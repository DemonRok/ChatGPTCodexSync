using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Restore;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class ArchiveRestoreService(
  IChatGptProcessDetector processDetector,
  ISevenZipToolProvider sevenZipToolProvider,
  IOptions<CodexSyncOptions> options,
  ILogger<ArchiveRestoreService> logger) : IRestoreService
{
  public async Task<RestoreResult> RestoreAsync(
    RestoreRequest request,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    if (processDetector.IsChatGptRunning())
    {
      throw new InvalidOperationException("ChatGPT Desktop or Codex appears to be running. Close it before starting a restore.");
    }

    if (!File.Exists(request.ArchivePath))
    {
      throw new FileNotFoundException("Backup archive not found.", request.ArchivePath);
    }

    var tempExtractDirectory = Path.Combine(
      Path.GetTempPath(),
      $"ChatGPTCodexSync-restore-{Guid.NewGuid():N}");

    progress.Report(new BackupProgress("Extracting backup archive to a temporary directory...", 10));
    Directory.CreateDirectory(tempExtractDirectory);

    string? safetyBackupDirectory = null;
    try
    {
      await ExtractArchiveAsync(request, tempExtractDirectory, progress, cancellationToken);

      progress.Report(new BackupProgress("Reading backup manifest...", 45));
      var manifest = await ReadManifestAsync(tempExtractDirectory, cancellationToken);
      ValidateManifest(manifest);

      var extractedCodexDirectory = Path.Combine(tempExtractDirectory, options.Value.CodexDirectoryName);
      if (!Directory.Exists(extractedCodexDirectory))
      {
        throw new DirectoryNotFoundException($"The archive does not contain the expected {options.Value.CodexDirectoryName} directory.");
      }

      Directory.CreateDirectory(request.SafetyBackupsDirectoryPath);

      if (Directory.Exists(request.TargetCodexDirectoryPath))
      {
        progress.Report(new BackupProgress("Creating safety backup of the current .codex directory...", 60));
        safetyBackupDirectory = CreateSafetyBackupPath(request.SafetyBackupsDirectoryPath);
        await MoveDirectoryAsync(
          request.TargetCodexDirectoryPath,
          safetyBackupDirectory,
          percent => progress.Report(new BackupProgress("Creating safety backup of the current .codex directory...", 60 + percent * 0.15d)),
          cancellationToken);
      }

      try
      {
        progress.Report(new BackupProgress("Restoring .codex directory...", 80));
        await MoveDirectoryAsync(
          extractedCodexDirectory,
          request.TargetCodexDirectoryPath,
          percent => progress.Report(new BackupProgress("Restoring .codex directory...", 80 + percent * 0.15d)),
          cancellationToken);
      }
      catch
      {
        await TryRollbackCurrentProfileAsync(request.TargetCodexDirectoryPath, safetyBackupDirectory, cancellationToken);
        throw;
      }

      progress.Report(new BackupProgress("Restore completed.", 100));
      return new RestoreResult(
        request.ArchivePath,
        request.TargetCodexDirectoryPath,
        safetyBackupDirectory,
        manifest);
    }
    catch (Exception exception)
    {
      logger.LogError(exception, "Restore failed for archive {ArchivePath}", request.ArchivePath);
      throw;
    }
    finally
    {
      TryDeleteDirectory(tempExtractDirectory);
    }
  }

  private async Task ExtractArchiveAsync(
    RestoreRequest request,
    string destinationDirectory,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var extension = Path.GetExtension(request.ArchivePath);

    if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
    {
      await Task.Run(
        () => ZipFile.ExtractToDirectory(request.ArchivePath, destinationDirectory, overwriteFiles: true),
        cancellationToken);
      return;
    }

    if (extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
    {
      var sevenZipTool = await sevenZipToolProvider.GetSevenZipAsync(request.OfflineMode, progress, cancellationToken);
      if (sevenZipTool is null)
      {
        throw new InvalidOperationException("7-Zip is required to restore .7z backups.");
      }

      await RunSevenZipAsync(
        sevenZipTool.ExecutablePath,
        ["x", "-bsp1", request.ArchivePath, $"-o{destinationDirectory}", "-y"],
        percent => progress.Report(new BackupProgress("Extracting backup archive with 7-Zip...", 10 + percent * 0.35d)),
        cancellationToken);
      return;
    }

    throw new NotSupportedException($"Unsupported backup archive format: {extension}");
  }

  private async Task<BackupManifest> ReadManifestAsync(
    string extractedDirectory,
    CancellationToken cancellationToken)
  {
    var manifestPath = Path.Combine(extractedDirectory, options.Value.ManifestFileName);
    if (!File.Exists(manifestPath))
    {
      throw new FileNotFoundException("The backup archive does not contain a ChatGPTCodexSync manifest.", manifestPath);
    }

    await using var stream = File.OpenRead(manifestPath);
    var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);

    return manifest ?? throw new InvalidOperationException("The backup manifest is empty or invalid.");
  }

  private static void ValidateManifest(BackupManifest manifest)
  {
    if (!string.Equals(manifest.ApplicationName, "ChatGPTCodexSync", StringComparison.Ordinal))
    {
      throw new InvalidOperationException("The selected archive was not created by ChatGPTCodexSync.");
    }
  }

  private static string CreateSafetyBackupPath(string safetyBackupsDirectoryPath)
  {
    var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
    return Path.Combine(safetyBackupsDirectoryPath, $".codex_before_restore_{timestamp}");
  }

  private static async Task TryRollbackCurrentProfileAsync(
    string targetCodexDirectoryPath,
    string? safetyBackupDirectoryPath,
    CancellationToken cancellationToken)
  {
    if (safetyBackupDirectoryPath is null
      || !Directory.Exists(safetyBackupDirectoryPath)
      || Directory.Exists(targetCodexDirectoryPath))
    {
      return;
    }

    await MoveDirectoryAsync(safetyBackupDirectoryPath, targetCodexDirectoryPath, null, cancellationToken);
  }

  private static Task MoveDirectoryAsync(
    string sourceDirectoryPath,
    string destinationDirectoryPath,
    Action<double>? progressChanged,
    CancellationToken cancellationToken)
  {
    return Task.Run(
      () => MoveDirectory(sourceDirectoryPath, destinationDirectoryPath, progressChanged, cancellationToken),
      cancellationToken);
  }

  private static void MoveDirectory(
    string sourceDirectoryPath,
    string destinationDirectoryPath,
    Action<double>? progressChanged,
    CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectoryPath)!);

    if (AreSameVolume(sourceDirectoryPath, destinationDirectoryPath))
    {
      Directory.Move(sourceDirectoryPath, destinationDirectoryPath);
      return;
    }

    var files = Directory
      .EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories)
      .ToList();

    CopyDirectory(sourceDirectoryPath, destinationDirectoryPath, files.Count, progressChanged, cancellationToken);
    DeleteDirectoryTree(sourceDirectoryPath, cancellationToken);
  }

  private static void CopyDirectory(
    string sourceDirectoryPath,
    string destinationDirectoryPath,
    int totalFileCount,
    Action<double>? progressChanged,
    CancellationToken cancellationToken)
  {
    var copiedFileCount = 0;
    CopyDirectoryCore(sourceDirectoryPath, destinationDirectoryPath, totalFileCount, progressChanged, ref copiedFileCount, cancellationToken);
  }

  private static void CopyDirectoryCore(
    string sourceDirectoryPath,
    string destinationDirectoryPath,
    int totalFileCount,
    Action<double>? progressChanged,
    ref int copiedFileCount,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Directory.CreateDirectory(destinationDirectoryPath);

    foreach (var filePath in Directory.EnumerateFiles(sourceDirectoryPath))
    {
      cancellationToken.ThrowIfCancellationRequested();

      var destinationFilePath = Path.Combine(
        destinationDirectoryPath,
        Path.GetFileName(filePath));

      File.Copy(filePath, destinationFilePath, overwrite: false);
      copiedFileCount++;
      if (totalFileCount > 0)
      {
        progressChanged?.Invoke(copiedFileCount * 100d / totalFileCount);
      }
    }

    foreach (var childDirectoryPath in Directory.EnumerateDirectories(sourceDirectoryPath))
    {
      cancellationToken.ThrowIfCancellationRequested();

      var destinationChildDirectoryPath = Path.Combine(
        destinationDirectoryPath,
        Path.GetFileName(childDirectoryPath));

      CopyDirectoryCore(childDirectoryPath, destinationChildDirectoryPath, totalFileCount, progressChanged, ref copiedFileCount, cancellationToken);
    }
  }

  private static bool AreSameVolume(string leftPath, string rightPath)
  {
    var leftRoot = Path.GetPathRoot(Path.GetFullPath(leftPath));
    var rightRoot = Path.GetPathRoot(Path.GetFullPath(rightPath));

    return string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
  }

  private static void DeleteDirectoryTree(
    string directoryPath,
    CancellationToken cancellationToken)
  {
    if (!Directory.Exists(directoryPath))
    {
      return;
    }

    foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
    {
      cancellationToken.ThrowIfCancellationRequested();
      File.SetAttributes(filePath, FileAttributes.Normal);
    }

    foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
    {
      cancellationToken.ThrowIfCancellationRequested();
      File.SetAttributes(childDirectoryPath, FileAttributes.Directory);
    }

    File.SetAttributes(directoryPath, FileAttributes.Directory);
    Directory.Delete(directoryPath, recursive: true);
  }

  private static async Task RunSevenZipAsync(
    string executablePath,
    IReadOnlyList<string> arguments,
    Action<double>? progressChanged,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = executablePath,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException($"Unable to start {executablePath}.");

    var outputBuilder = new System.Text.StringBuilder();
    var errorBuilder = new System.Text.StringBuilder();
    var outputTask = ReadProcessOutputAsync(process.StandardOutput, outputBuilder, progressChanged, cancellationToken);
    var errorTask = ReadProcessOutputAsync(process.StandardError, errorBuilder, progressChanged, cancellationToken);

    await process.WaitForExitAsync(cancellationToken);
    await Task.WhenAll(outputTask, errorTask);

    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"7-Zip extraction failed with exit code {process.ExitCode}.{Environment.NewLine}{outputBuilder}{Environment.NewLine}{errorBuilder}");
    }
  }

  private static async Task ReadProcessOutputAsync(
    TextReader reader,
    System.Text.StringBuilder outputBuilder,
    Action<double>? progressChanged,
    CancellationToken cancellationToken)
  {
    var buffer = new char[512];
    while (true)
    {
      var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
      if (read == 0)
      {
        return;
      }

      var chunk = new string(buffer, 0, read);
      outputBuilder.Append(chunk);
      ReportSevenZipProgress(chunk, progressChanged);
    }
  }

  private static void ReportSevenZipProgress(string outputChunk, Action<double>? progressChanged)
  {
    if (progressChanged is null)
    {
      return;
    }

    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(outputChunk, @"(?<!\d)(\d{1,3})%"))
    {
      if (int.TryParse(match.Groups[1].Value, out var percent))
      {
        progressChanged(Math.Clamp(percent, 0, 100));
      }
    }
  }

  private static void TryDeleteDirectory(string directoryPath)
  {
    try
    {
      if (Directory.Exists(directoryPath))
      {
        Directory.Delete(directoryPath, true);
      }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}

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
        Directory.Move(request.TargetCodexDirectoryPath, safetyBackupDirectory);
      }

      try
      {
        progress.Report(new BackupProgress("Restoring .codex directory...", 80));
        Directory.Move(extractedCodexDirectory, request.TargetCodexDirectoryPath);
      }
      catch
      {
        TryRollbackCurrentProfile(request.TargetCodexDirectoryPath, safetyBackupDirectory);
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
        ["x", request.ArchivePath, $"-o{destinationDirectory}", "-y"],
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

  private static void TryRollbackCurrentProfile(
    string targetCodexDirectoryPath,
    string? safetyBackupDirectoryPath)
  {
    if (safetyBackupDirectoryPath is null
      || !Directory.Exists(safetyBackupDirectoryPath)
      || Directory.Exists(targetCodexDirectoryPath))
    {
      return;
    }

    Directory.Move(safetyBackupDirectoryPath, targetCodexDirectoryPath);
  }

  private static async Task RunSevenZipAsync(
    string executablePath,
    IReadOnlyList<string> arguments,
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

    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    var output = await outputTask;
    var error = await errorTask;
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"7-Zip extraction failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
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

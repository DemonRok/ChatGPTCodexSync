using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class ZipBackupService(
  IChatGptProcessDetector processDetector,
  ISevenZipToolProvider sevenZipToolProvider,
  IOptions<CodexSyncOptions> options,
  ILogger<ZipBackupService> logger) : IBackupService
{
  public async Task<BackupResult> CreateBackupAsync(
    BackupRequest request,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    if (processDetector.IsChatGptRunning())
    {
      throw new InvalidOperationException("ChatGPT Desktop or Codex appears to be running. Close it before starting a backup.");
    }

    if (!Directory.Exists(request.CodexDirectoryPath))
    {
      throw new DirectoryNotFoundException($"Codex directory not found: {request.CodexDirectoryPath}");
    }

    Directory.CreateDirectory(request.BackupsDirectoryPath);

    await WaitForDatabaseFilesAsync(request.CodexDirectoryPath, progress, cancellationToken);

    var sevenZipTool = await sevenZipToolProvider.GetSevenZipAsync(request.OfflineMode, progress, cancellationToken);
    var archiveFormat = sevenZipTool is null ? "zip" : "7z";
    var manifest = CreateManifest(request, archiveFormat);
    var archivePath = CreateArchivePath(request.BackupsDirectoryPath, manifest);

    progress.Report(new BackupProgress("Creating backup archive...", 0));
    logger.LogInformation("Creating backup archive {ArchivePath}", archivePath);

    if (sevenZipTool is null)
    {
      await Task.Run(() => CreateZipArchive(request, manifest, archivePath, progress, cancellationToken), cancellationToken);
    }
    else
    {
      await CreateSevenZipArchiveAsync(sevenZipTool, request, manifest, archivePath, progress, cancellationToken);
    }

    progress.Report(new BackupProgress($"Backup completed: {archivePath}", 100));
    return new BackupResult(archivePath, manifest);
  }

  private static BackupManifest CreateManifest(BackupRequest request, string archiveFormat)
  {
    return new BackupManifest(
      "ChatGPTCodexSync",
      request.ApplicationVersion,
      Environment.MachineName,
      Environment.UserName,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      request.CodexDirectoryPath,
      DateTimeOffset.Now,
      archiveFormat);
  }

  private static string CreateArchivePath(string backupsDirectoryPath, BackupManifest manifest)
  {
    var timestamp = manifest.CreatedAt.ToString("yyyyMMdd-HHmmss");
    var safeMachineName = SanitizeFileName(manifest.MachineName);
    var safeUserName = SanitizeFileName(manifest.UserName);
    var fileName = $"chatgptcodexsync_{safeMachineName}_{safeUserName}_{timestamp}.{manifest.ArchiveFormat}";

    return Path.Combine(backupsDirectoryPath, fileName);
  }

  private async Task CreateSevenZipArchiveAsync(
    ArchiveTool sevenZipTool,
    BackupRequest request,
    BackupManifest manifest,
    string archivePath,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var compressionSwitches = GetSevenZipCompressionSwitches(request.SevenZipCompressionMode);
    var tempManifestDirectory = Path.Combine(
      Path.GetTempPath(),
      $"ChatGPTCodexSync-manifest-{Guid.NewGuid():N}");

    Directory.CreateDirectory(tempManifestDirectory);

    try
    {
      var manifestPath = Path.Combine(tempManifestDirectory, options.Value.ManifestFileName);
      await File.WriteAllTextAsync(
        manifestPath,
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
        cancellationToken);

      progress.Report(new BackupProgress("Adding backup manifest to 7z archive...", 0));
      await RunSevenZipAsync(
        sevenZipTool.ExecutablePath,
        tempManifestDirectory,
        new[]
        {
          "a",
          "-t7z",
          archivePath,
          options.Value.ManifestFileName
        }.Concat(compressionSwitches).ToArray(),
        null,
        cancellationToken);

      var codexParentDirectory = Directory.GetParent(request.CodexDirectoryPath)?.FullName
        ?? throw new DirectoryNotFoundException($"Unable to locate parent directory for {request.CodexDirectoryPath}");
      var codexDirectoryName = Path.GetFileName(request.CodexDirectoryPath);

      progress.Report(new BackupProgress($"Adding .codex directory to 7z archive using {request.SevenZipCompressionMode} mode...", 5));
      if (request.SevenZipCompressionMode == SevenZipCompressionMode.Maximum)
      {
        progress.Report(new BackupProgress("Maximum mode uses SaveCodex.cmd-compatible compression switches.", 5));
      }
      progress.Report(new BackupProgress($"7-Zip switches: {string.Join(' ', compressionSwitches.Where(static value => value != "-y"))}", 5));

      var lastReportedPercent = -1;
      var highestSevenZipPercent = -1;
      await RunSevenZipAsync(
        sevenZipTool.ExecutablePath,
        codexParentDirectory,
        new[]
        {
          "a",
          "-t7z",
          "-bsp1",
          archivePath,
          $"{codexDirectoryName}\\*",
          "-xr!*.lock"
        }.Concat(compressionSwitches).ToArray(),
        sevenZipPercent =>
        {
          if (sevenZipPercent < highestSevenZipPercent)
          {
            return;
          }

          highestSevenZipPercent = sevenZipPercent;
          var mappedPercent = 5 + (int)Math.Round(sevenZipPercent * 0.95d);
          if (mappedPercent != lastReportedPercent)
          {
            lastReportedPercent = mappedPercent;
            progress.Report(new BackupProgress("Compressing with 7-Zip...", mappedPercent));
          }
        },
        cancellationToken);

      progress.Report(new BackupProgress("7z archive created.", 100));
    }
    finally
    {
      TryDeleteDirectory(tempManifestDirectory);
    }
  }

  private static string[] GetSevenZipCompressionSwitches(SevenZipCompressionMode compressionMode)
  {
    return compressionMode switch
    {
      SevenZipCompressionMode.Fast =>
      [
        "-mx=1",
        GetArchitectureAwareThreadSwitch(),
        "-y"
      ],
      SevenZipCompressionMode.Balanced =>
      [
        "-mx=5",
        GetArchitectureAwareThreadSwitch(),
        "-y"
      ],
      SevenZipCompressionMode.Maximum =>
      [
        "-mx=9",
        GetArchitectureAwareThreadSwitch(),
        "-y"
      ],
      _ => throw new ArgumentOutOfRangeException(nameof(compressionMode), compressionMode, null)
    };
  }

  private static string GetArchitectureAwareThreadSwitch()
  {
    var threadCount = Environment.Is64BitProcess
      ? Environment.ProcessorCount
      : Math.Min(Environment.ProcessorCount, 2);

    return $"-mmt={threadCount}";
  }

  private void CreateZipArchive(
    BackupRequest request,
    BackupManifest manifest,
    string archivePath,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var files = Directory
      .EnumerateFiles(request.CodexDirectoryPath, "*", SearchOption.AllDirectories)
      .Where(file => !ShouldSkipFile(file))
      .ToList();

    progress.Report(new BackupProgress($"Found {files.Count} files to archive.", 0));

    using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
    AddManifest(archive, manifest);

    var lastReportedPercent = -1;
    for (var index = 0; index < files.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var file = files[index];
      var relativePath = Path.GetRelativePath(request.CodexDirectoryPath, file);
      var entryName = Path.Combine(options.Value.CodexDirectoryName, relativePath).Replace('\\', '/');

      archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);

      var percent = files.Count == 0 ? 100 : (int)((index + 1) * 100d / files.Count);
      if (percent != lastReportedPercent || index == files.Count - 1)
      {
        lastReportedPercent = percent;
        progress.Report(new BackupProgress($"Archived {index + 1} of {files.Count} files.", percent));
      }
    }
  }

  private static async Task WaitForDatabaseFilesAsync(
    string codexDirectoryPath,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var sensitiveFiles = Directory
      .EnumerateFiles(codexDirectoryPath, "*", SearchOption.AllDirectories)
      .Where(IsDatabaseRelatedFile)
      .ToList();

    if (sensitiveFiles.Count == 0)
    {
      progress.Report(new BackupProgress("No SQLite files found to check.", 0));
      return;
    }

    progress.Report(new BackupProgress($"Checking {sensitiveFiles.Count} SQLite-related files...", 0));

    var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(30);
    while (DateTimeOffset.UtcNow < timeoutAt)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var lockedFiles = sensitiveFiles
        .Where(file => !CanOpenForBackup(file))
        .ToList();

      if (lockedFiles.Count == 0)
      {
        progress.Report(new BackupProgress("SQLite files are ready for backup.", 0));
        return;
      }

      progress.Report(new BackupProgress($"Waiting for {lockedFiles.Count} SQLite-related files to be released...", 0));
      await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    throw new IOException("Some SQLite-related files are still locked. Close ChatGPT Desktop and try again.");
  }

  private static bool IsDatabaseRelatedFile(string filePath)
  {
    var fileName = Path.GetFileName(filePath);
    var extension = Path.GetExtension(filePath);

    return extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
      || extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
      || extension.Equals(".sqlite3", StringComparison.OrdinalIgnoreCase)
      || fileName.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
      || fileName.EndsWith("-shm", StringComparison.OrdinalIgnoreCase);
  }

  private static bool CanOpenForBackup(string filePath)
  {
    try
    {
      using var stream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);

      return stream.CanRead;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private void AddManifest(ZipArchive archive, BackupManifest manifest)
  {
    var entry = archive.CreateEntry(options.Value.ManifestFileName, CompressionLevel.Optimal);
    using var stream = entry.Open();

    JsonSerializer.Serialize(
      stream,
      manifest,
      new JsonSerializerOptions { WriteIndented = true });
  }

  private static bool ShouldSkipFile(string filePath)
  {
    var extension = Path.GetExtension(filePath);

    return extension.Equals(".lock", StringComparison.OrdinalIgnoreCase);
  }

  private static async Task RunSevenZipAsync(
    string executablePath,
    string workingDirectory,
    IReadOnlyList<string> arguments,
    Action<int>? progressChanged,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = executablePath,
      WorkingDirectory = workingDirectory,
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
      throw new InvalidOperationException($"7-Zip failed with exit code {process.ExitCode}.{Environment.NewLine}{outputBuilder}{Environment.NewLine}{errorBuilder}");
    }
  }

  private static async Task ReadProcessOutputAsync(
    TextReader reader,
    System.Text.StringBuilder outputBuilder,
    Action<int>? progressChanged,
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

  private static void ReportSevenZipProgress(string outputChunk, Action<int>? progressChanged)
  {
    if (progressChanged is null)
    {
      return;
    }

    foreach (Match match in Regex.Matches(outputChunk, @"(?<!\d)(\d{1,3})%"))
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

  private static string SanitizeFileName(string value)
  {
    foreach (var invalidChar in Path.GetInvalidFileNameChars())
    {
      value = value.Replace(invalidChar, '_');
    }

    return value;
  }
}

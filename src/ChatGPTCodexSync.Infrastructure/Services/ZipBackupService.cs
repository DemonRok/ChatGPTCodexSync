using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Text.Json;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class ZipBackupService(
  IChatGptProcessDetector processDetector,
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

    var manifest = CreateManifest(request);
    var archivePath = CreateArchivePath(request.BackupsDirectoryPath, manifest);

    progress.Report(new BackupProgress("Creating backup archive...", 0));
    logger.LogInformation("Creating backup archive {ArchivePath}", archivePath);

    await Task.Run(() => CreateZipArchive(request, manifest, archivePath, progress, cancellationToken), cancellationToken);

    progress.Report(new BackupProgress($"Backup completed: {archivePath}", 100));
    return new BackupResult(archivePath, manifest);
  }

  private BackupManifest CreateManifest(BackupRequest request)
  {
    return new BackupManifest(
      "ChatGPTCodexSync",
      request.ApplicationVersion,
      Environment.MachineName,
      Environment.UserName,
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      request.CodexDirectoryPath,
      DateTimeOffset.Now,
      "zip");
  }

  private static string CreateArchivePath(string backupsDirectoryPath, BackupManifest manifest)
  {
    var timestamp = manifest.CreatedAt.ToString("yyyyMMdd-HHmmss");
    var safeMachineName = SanitizeFileName(manifest.MachineName);
    var safeUserName = SanitizeFileName(manifest.UserName);
    var fileName = $"chatgptcodexsync_{safeMachineName}_{safeUserName}_{timestamp}.zip";

    return Path.Combine(backupsDirectoryPath, fileName);
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

  private static string SanitizeFileName(string value)
  {
    foreach (var invalidChar in Path.GetInvalidFileNameChars())
    {
      value = value.Replace(invalidChar, '_');
    }

    return value;
  }
}

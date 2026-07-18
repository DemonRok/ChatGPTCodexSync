using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using ChatGPTCodexSync.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Text.Json;

namespace ChatGPTCodexSync.Tests;

public sealed class ArchiveRestoreServiceTests
{
  [Fact]
  public async Task RestoreAsync_RestoresZipBackupAndCreatesSafetyBackup()
  {
    var root = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-tests-{Guid.NewGuid():N}");
    var archiveSource = Path.Combine(root, "archive-source");
    var sourceCodex = Path.Combine(archiveSource, ".codex");
    var targetCodex = Path.Combine(root, "target", ".codex");
    var safetyBackups = Path.Combine(root, "safety");
    var archivePath = Path.Combine(root, "backup.zip");

    try
    {
      Directory.CreateDirectory(sourceCodex);
      Directory.CreateDirectory(targetCodex);
      await File.WriteAllTextAsync(Path.Combine(sourceCodex, "restored.txt"), "restored");
      await File.WriteAllTextAsync(Path.Combine(targetCodex, "current.txt"), "current");

      var manifest = new BackupManifest(
        "ChatGPTCodexSync",
        "0.2.1",
        "TEST-PC",
        "mauro",
        @"C:\Users\mauro",
        @"C:\Users\mauro\.codex",
        DateTimeOffset.Now,
        "zip");

      await File.WriteAllTextAsync(
        Path.Combine(archiveSource, "chatgptcodexsync.manifest.json"),
        JsonSerializer.Serialize(manifest));

      ZipFile.CreateFromDirectory(archiveSource, archivePath);

      var service = new ArchiveRestoreService(
        new FakeProcessDetector(),
        new FakeSevenZipToolProvider(),
        Options.Create(new CodexSyncOptions()),
        NullLogger<ArchiveRestoreService>.Instance);

      var result = await service.RestoreAsync(
        new Core.Restore.RestoreRequest(archivePath, targetCodex, safetyBackups, false),
        new Progress<BackupProgress>(),
        CancellationToken.None);

      Assert.True(File.Exists(Path.Combine(targetCodex, "restored.txt")));
      Assert.False(File.Exists(Path.Combine(targetCodex, "current.txt")));
      Assert.NotNull(result.SafetyBackupDirectoryPath);
      Assert.True(File.Exists(Path.Combine(result.SafetyBackupDirectoryPath, "current.txt")));
      Assert.Equal("ChatGPTCodexSync", result.Manifest.ApplicationName);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, true);
      }
    }
  }

  private sealed class FakeProcessDetector : IChatGptProcessDetector
  {
    public bool IsChatGptRunning() => false;
  }

  private sealed class FakeSevenZipToolProvider : ISevenZipToolProvider
  {
    public Task<ArchiveTool?> GetSevenZipAsync(
      bool offlineMode,
      IProgress<BackupProgress> progress,
      CancellationToken cancellationToken)
    {
      return Task.FromResult<ArchiveTool?>(null);
    }
  }
}

using ChatGPTCodexSync.Core.Backup;

namespace ChatGPTCodexSync.Core.Restore;

public sealed record RestoreResult(
  string ArchivePath,
  string RestoredCodexDirectoryPath,
  string? SafetyBackupDirectoryPath,
  BackupManifest Manifest);

namespace ChatGPTCodexSync.Core.Restore;

public sealed record RestoreRequest(
  string ArchivePath,
  string TargetCodexDirectoryPath,
  string SafetyBackupsDirectoryPath,
  bool OfflineMode);

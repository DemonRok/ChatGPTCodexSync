namespace ChatGPTCodexSync.Core.Backup;

public sealed record BackupRequest(
  string CodexDirectoryPath,
  string BackupsDirectoryPath,
  string ApplicationVersion);

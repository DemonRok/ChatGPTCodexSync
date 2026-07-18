namespace ChatGPTCodexSync.Core.Backup;

public sealed record BackupRequest(
  string CodexDirectoryPath,
  string BackupsDirectoryPath,
  string ApplicationVersion,
  SevenZipCompressionMode SevenZipCompressionMode,
  bool OfflineMode);

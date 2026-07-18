namespace ChatGPTCodexSync.Core.Backup;

public sealed record BackupManifest(
  string ApplicationName,
  string ApplicationVersion,
  string MachineName,
  string UserName,
  string UserProfilePath,
  string CodexDirectoryPath,
  DateTimeOffset CreatedAt,
  string ArchiveFormat);

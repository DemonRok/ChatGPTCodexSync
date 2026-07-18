namespace ChatGPTCodexSync.Core.Backup;

public sealed record BackupResult(string ArchivePath, BackupManifest Manifest);

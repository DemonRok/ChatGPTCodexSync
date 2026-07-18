namespace ChatGPTCodexSync.Core.Backup;

public sealed record BackupProgress(string Message, double? Percent = null);

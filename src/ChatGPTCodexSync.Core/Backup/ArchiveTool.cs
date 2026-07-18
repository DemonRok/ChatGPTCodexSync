namespace ChatGPTCodexSync.Core.Backup;

public sealed record ArchiveTool(
  string ExecutablePath,
  string Version,
  bool WasDownloaded);

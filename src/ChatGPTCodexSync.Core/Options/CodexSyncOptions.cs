namespace ChatGPTCodexSync.Core.Options;

public sealed class CodexSyncOptions
{
  public string CodexDirectoryName { get; set; } = ".codex";

  public string BackupsDirectoryName { get; set; } = "Backups";

  public string ManifestFileName { get; set; } = "chatgptcodexsync.manifest.json";
}

namespace ChatGPTCodexSync.Core.Options;

public sealed class CodexSyncOptions
{
  public string CodexDirectoryName { get; set; } = ".codex";

  public string BackupsDirectoryName { get; set; } = "Backups";

  public string ManifestFileName { get; set; } = "chatgptcodexsync.manifest.json";

  public bool Allow7ZipAutoDownload { get; set; } = true;

  public bool OfflineMode { get; set; }

  public string ToolsDirectoryName { get; set; } = "tools";

  public string SevenZipToolDirectoryName { get; set; } = "7zip";

  public string Minimum7ZipVersion { get; set; } = "26.02";

  public string SevenZipFullDownloadUrl { get; set; } = "https://www.7-zip.org/a/7z2602-x64.exe";

  public string SevenZipExtraDownloadUrl { get; set; } = "https://www.7-zip.org/a/7z2602-extra.7z";

  public string SevenZipDownloadUrl { get; set; } = "https://www.7-zip.org/a/7zr.exe";
}

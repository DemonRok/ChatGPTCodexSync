using ChatGPTCodexSync.Core.Backup;

namespace ChatGPTCodexSync.Tests;

public sealed class BackupManifestTests
{
  [Fact]
  public void ManifestKeepsOriginalProfilePaths()
  {
    var manifest = new BackupManifest(
      "ChatGPTCodexSync",
      "0.1.3",
      "PC",
      "mauro",
      @"C:\Users\mauro",
      @"C:\Users\mauro\.codex",
      DateTimeOffset.UnixEpoch,
      "zip");

    Assert.Equal(@"C:\Users\mauro", manifest.UserProfilePath);
    Assert.Equal(@"C:\Users\mauro\.codex", manifest.CodexDirectoryPath);
  }
}

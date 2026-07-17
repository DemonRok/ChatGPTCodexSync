using ChatGPTCodexSync.Core.Options;

namespace ChatGPTCodexSync.Tests;

public sealed class CodexSyncOptionsTests
{
  [Fact]
  public void DefaultsUseExpectedPortableLayout()
  {
    var options = new CodexSyncOptions();

    Assert.Equal(".codex", options.CodexDirectoryName);
    Assert.Equal("Backups", options.BackupsDirectoryName);
    Assert.Equal("chatgptcodexsync.manifest.json", options.ManifestFileName);
  }
}

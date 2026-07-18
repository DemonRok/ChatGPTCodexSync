using ChatGPTCodexSync.Core.Options;

namespace ChatGPTCodexSync.Tests;

public sealed class SevenZipOptionsTests
{
  [Fact]
  public void DefaultsUsePortableToolsDirectory()
  {
    var options = new CodexSyncOptions();

    Assert.True(options.Allow7ZipAutoDownload);
    Assert.False(options.OfflineMode);
    Assert.Equal("tools", options.ToolsDirectoryName);
    Assert.Equal("7zip", options.SevenZipToolDirectoryName);
    Assert.Equal("26.02", options.Minimum7ZipVersion);
    Assert.Equal("https://www.7-zip.org/a/7z2602-x64.exe", options.SevenZipFullDownloadUrl);
    Assert.Equal("https://www.7-zip.org/a/7z2602-extra.7z", options.SevenZipExtraDownloadUrl);
    Assert.Equal("https://www.7-zip.org/a/7zr.exe", options.SevenZipDownloadUrl);
  }
}

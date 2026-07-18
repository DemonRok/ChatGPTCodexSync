using System.Reflection;
using ChatGPTCodexSync.Infrastructure.Services;

namespace ChatGPTCodexSync.Tests;

public sealed class SqliteFileDetectionTests
{
  [Theory]
  [InlineData("state.db", true)]
  [InlineData("state.sqlite", true)]
  [InlineData("state.sqlite3", true)]
  [InlineData("state.db-wal", true)]
  [InlineData("state.db-shm", true)]
  [InlineData("notes.json", false)]
  public void DatabaseRelatedFileDetectionMatchesExpectedFiles(string fileName, bool expected)
  {
    var method = typeof(ZipBackupService).GetMethod(
      "IsDatabaseRelatedFile",
      BindingFlags.NonPublic | BindingFlags.Static);

    Assert.NotNull(method);

    var actual = (bool)method.Invoke(null, [fileName])!;
    Assert.Equal(expected, actual);
  }
}

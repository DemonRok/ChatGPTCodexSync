using ChatGPTCodexSync.Core.Backup;

namespace ChatGPTCodexSync.Core.Services;

public interface ISevenZipToolProvider
{
  Task<ArchiveTool?> GetSevenZipAsync(
    bool offlineMode,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken);
}

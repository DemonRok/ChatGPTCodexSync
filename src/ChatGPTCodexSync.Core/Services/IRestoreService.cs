using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Restore;

namespace ChatGPTCodexSync.Core.Services;

public interface IRestoreService
{
  Task<RestoreResult> RestoreAsync(
    RestoreRequest request,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken);
}

using ChatGPTCodexSync.Core.Backup;

namespace ChatGPTCodexSync.Core.Services;

public interface IBackupService
{
  Task<BackupResult> CreateBackupAsync(
    BackupRequest request,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken);
}

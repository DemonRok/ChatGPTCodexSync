using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Input;

namespace ChatGPTCodexSync.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
  private readonly ILogger<MainWindowViewModel> _logger;
  private readonly string _windowTitle;
  private readonly IBackupService _backupService;
  private readonly ICodexProfileLocator _codexProfileLocator;
  private readonly StringBuilder _log = new();
  private bool _isBackupRunning;
  private double _progressValue;
  private string _progressText;
  private string _statusMessage;
  private string _codexDirectoryPath;
  private string _logText;

  public MainWindowViewModel(
    ICodexProfileLocator codexProfileLocator,
    IBackupService backupService,
    ILogger<MainWindowViewModel> logger)
  {
    _logger = logger;
    _backupService = backupService;
    _codexProfileLocator = codexProfileLocator;

    var currentProfile = codexProfileLocator.GetCurrentProfile();
    _codexDirectoryPath = currentProfile.CodexDirectoryPath;
    _windowTitle = $"ChatGPTCodexSync ver. {GetApplicationVersion()}";
    _statusMessage = "Ready to create a backup.";
    _progressText = "0%";
    _logText = "Backup and restore logs will appear here.";
    CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);

    _logger.LogInformation("Current Codex profile located: {CodexDirectoryPath}", _codexDirectoryPath);
  }

  public string CodexDirectoryPath
  {
    get => _codexDirectoryPath;
    private set => SetProperty(ref _codexDirectoryPath, value);
  }

  public string WindowTitle => _windowTitle;

  public ICommand CreateBackupCommand { get; }

  public double ProgressValue
  {
    get => _progressValue;
    private set
    {
      if (SetProperty(ref _progressValue, value))
      {
        ProgressText = $"{Math.Clamp((int)Math.Round(value), 0, 100)}%";
      }
    }
  }

  public bool IsBackupRunning
  {
    get => _isBackupRunning;
    private set => SetProperty(ref _isBackupRunning, value);
  }

  public string ProgressText
  {
    get => _progressText;
    private set => SetProperty(ref _progressText, value);
  }

  public string StatusMessage
  {
    get => _statusMessage;
    private set => SetProperty(ref _statusMessage, value);
  }

  public string LogText
  {
    get => _logText;
    private set => SetProperty(ref _logText, value);
  }

  private async Task CreateBackupAsync(CancellationToken cancellationToken)
  {
    try
    {
      IsBackupRunning = true;
      ProgressValue = 0;
      _log.Clear();
      AppendLog("Starting backup...");
      StatusMessage = "Backup in progress...";

      var profile = _codexProfileLocator.GetCurrentProfile();
      var appDirectory = AppContext.BaseDirectory;
      var backupsDirectory = Path.Combine(appDirectory, "Backups");
      var request = new BackupRequest(profile.CodexDirectoryPath, backupsDirectory, GetApplicationVersion());

      var progress = new Progress<BackupProgress>(backupProgress =>
      {
        AppendLog(backupProgress.Message);

        if (backupProgress.Percent.HasValue)
        {
          ProgressValue = backupProgress.Percent.Value;
        }
      });

      var result = await _backupService.CreateBackupAsync(request, progress, cancellationToken);
      ProgressValue = 100;
      StatusMessage = "Backup completed.";
      AppendLog($"Archive: {result.ArchivePath}");
    }
    catch (Exception exception)
    {
      StatusMessage = "Backup failed.";
      AppendLog(exception.Message);
      _logger.LogError(exception, "Backup failed");
    }
    finally
    {
      IsBackupRunning = false;
    }
  }

  private void AppendLog(string message)
  {
    _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    LogText = _log.ToString();
  }

  private static string GetApplicationVersion()
  {
    var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;
    var informationalVersion = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion;

    if (string.IsNullOrWhiteSpace(informationalVersion))
    {
      return "0.0.0";
    }

    return informationalVersion.Split('+')[0];
  }
}

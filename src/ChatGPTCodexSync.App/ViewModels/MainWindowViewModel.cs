using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Restore;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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
  private readonly IRestoreService _restoreService;
  private readonly ICodexProfileLocator _codexProfileLocator;
  private readonly StringBuilder _log = new();
  private bool _isOperationRunning;
  private double _progressValue;
  private string _progressText;
  private string? _lastLogMessage;
  private SevenZipCompressionMode _selectedCompressionMode = SevenZipCompressionMode.Fast;
  private bool _offlineMode;
  private string _statusMessage;
  private string _codexDirectoryPath;
  private string _logText;

  public MainWindowViewModel(
    ICodexProfileLocator codexProfileLocator,
    IBackupService backupService,
    IRestoreService restoreService,
    ILogger<MainWindowViewModel> logger)
  {
    _logger = logger;
    _backupService = backupService;
    _restoreService = restoreService;
    _codexProfileLocator = codexProfileLocator;

    var currentProfile = codexProfileLocator.GetCurrentProfile();
    _codexDirectoryPath = currentProfile.CodexDirectoryPath;
    _windowTitle = $"ChatGPTCodexSync ver. {GetApplicationVersion()}";
    _statusMessage = "Ready to create a backup.";
    _progressText = "0%";
    _logText = "Backup and restore logs will appear here.";
    CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
    RestoreCommand = new AsyncRelayCommand(RestoreAsync);

    _logger.LogInformation("Current Codex profile located: {CodexDirectoryPath}", _codexDirectoryPath);
  }

  public string CodexDirectoryPath
  {
    get => _codexDirectoryPath;
    private set => SetProperty(ref _codexDirectoryPath, value);
  }

  public string WindowTitle => _windowTitle;

  public ICommand CreateBackupCommand { get; }

  public ICommand RestoreCommand { get; }

  public IReadOnlyList<SevenZipCompressionMode> CompressionModes { get; } =
  [
    SevenZipCompressionMode.Fast,
    SevenZipCompressionMode.Balanced,
    SevenZipCompressionMode.Maximum
  ];

  public SevenZipCompressionMode SelectedCompressionMode
  {
    get => _selectedCompressionMode;
    set => SetProperty(ref _selectedCompressionMode, value);
  }

  public bool OfflineMode
  {
    get => _offlineMode;
    set => SetProperty(ref _offlineMode, value);
  }

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

  public bool IsOperationRunning
  {
    get => _isOperationRunning;
    private set
    {
      if (SetProperty(ref _isOperationRunning, value))
      {
        OnPropertyChanged(nameof(CanConfigureActions));
      }
    }
  }

  public bool CanConfigureActions => !IsOperationRunning;

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
      StartOperation();
      AppendLog("Starting backup...");
      StatusMessage = "Backup in progress...";

      var profile = _codexProfileLocator.GetCurrentProfile();
      var appDirectory = AppContext.BaseDirectory;
      var backupsDirectory = Path.Combine(appDirectory, "Backups");
      var request = new BackupRequest(
        profile.CodexDirectoryPath,
        backupsDirectory,
        GetApplicationVersion(),
        SelectedCompressionMode,
        OfflineMode);

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
      IsOperationRunning = false;
    }
  }

  private async Task RestoreAsync(CancellationToken cancellationToken)
  {
    var dialog = new OpenFileDialog
    {
      Title = "Select ChatGPTCodexSync backup",
      Filter = "ChatGPTCodexSync backups (*.7z;*.zip)|*.7z;*.zip|7-Zip archives (*.7z)|*.7z|ZIP archives (*.zip)|*.zip|All files (*.*)|*.*",
      CheckFileExists = true,
      Multiselect = false
    };

    if (dialog.ShowDialog() != true)
    {
      return;
    }

    try
    {
      StartOperation();
      AppendLog("Starting restore...");
      StatusMessage = "Restore in progress...";

      var profile = _codexProfileLocator.GetCurrentProfile();
      var safetyBackupsDirectory = Path.Combine(AppContext.BaseDirectory, "SafetyBackups");
      var request = new RestoreRequest(
        dialog.FileName,
        profile.CodexDirectoryPath,
        safetyBackupsDirectory,
        OfflineMode);

      var progress = new Progress<BackupProgress>(restoreProgress =>
      {
        AppendLog(restoreProgress.Message);

        if (restoreProgress.Percent.HasValue)
        {
          ProgressValue = restoreProgress.Percent.Value;
        }
      });

      var result = await _restoreService.RestoreAsync(request, progress, cancellationToken);
      ProgressValue = 100;
      StatusMessage = "Restore completed.";
      AppendLog($"Restored profile: {result.RestoredCodexDirectoryPath}");

      if (!string.IsNullOrWhiteSpace(result.SafetyBackupDirectoryPath))
      {
        AppendLog($"Safety backup: {result.SafetyBackupDirectoryPath}");
      }
    }
    catch (Exception exception)
    {
      StatusMessage = "Restore failed.";
      AppendLog(exception.Message);
      _logger.LogError(exception, "Restore failed");
    }
    finally
    {
      IsOperationRunning = false;
    }
  }

  private void StartOperation()
  {
    IsOperationRunning = true;
    ProgressValue = 0;
    _log.Clear();
    _lastLogMessage = null;
  }

  private void AppendLog(string message)
  {
    if (string.Equals(_lastLogMessage, message, StringComparison.Ordinal))
    {
      return;
    }

    _lastLogMessage = message;
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

using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace ChatGPTCodexSync.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
  private readonly ILogger<MainWindowViewModel> _logger;
  private readonly string _windowTitle;
  private string _statusMessage;
  private string _codexDirectoryPath;

  public MainWindowViewModel(
    ICodexProfileLocator codexProfileLocator,
    ILogger<MainWindowViewModel> logger)
  {
    _logger = logger;

    var currentProfile = codexProfileLocator.GetCurrentProfile();
    _codexDirectoryPath = currentProfile.CodexDirectoryPath;
    _windowTitle = $"ChatGPTCodexSync ver. {GetApplicationVersion()}";
    _statusMessage = "Ready for the next implementation phases.";

    _logger.LogInformation("Current Codex profile located: {CodexDirectoryPath}", _codexDirectoryPath);
  }

  public string CodexDirectoryPath
  {
    get => _codexDirectoryPath;
    private set => SetProperty(ref _codexDirectoryPath, value);
  }

  public string WindowTitle => _windowTitle;

  public string StatusMessage
  {
    get => _statusMessage;
    private set => SetProperty(ref _statusMessage, value);
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

using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;

namespace ChatGPTCodexSync.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
  private readonly ILogger<MainWindowViewModel> _logger;
  private string _statusMessage;
  private string _codexDirectoryPath;

  public MainWindowViewModel(
    ICodexProfileLocator codexProfileLocator,
    ILogger<MainWindowViewModel> logger)
  {
    _logger = logger;

    var currentProfile = codexProfileLocator.GetCurrentProfile();
    _codexDirectoryPath = currentProfile.CodexDirectoryPath;
    _statusMessage = "Pronto per la configurazione delle prossime fasi.";

    _logger.LogInformation("Profilo Codex corrente individuato: {CodexDirectoryPath}", _codexDirectoryPath);
  }

  public string CodexDirectoryPath
  {
    get => _codexDirectoryPath;
    private set => SetProperty(ref _codexDirectoryPath, value);
  }

  public string StatusMessage
  {
    get => _statusMessage;
    private set => SetProperty(ref _statusMessage, value);
  }
}

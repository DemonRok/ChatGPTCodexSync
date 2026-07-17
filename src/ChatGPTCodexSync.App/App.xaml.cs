using System.Windows;
using ChatGPTCodexSync.App.ViewModels;
using ChatGPTCodexSync.Core;
using ChatGPTCodexSync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatGPTCodexSync.App;

public partial class App : Application
{
  private readonly IHost _host;

  public App()
  {
    _host = Host.CreateDefaultBuilder()
      .ConfigureLogging(logging =>
      {
        logging.ClearProviders();
        logging.AddDebug();
      })
      .ConfigureServices((context, services) =>
      {
        services
          .AddCoreServices()
          .AddInfrastructureServices(context.Configuration);

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
      })
      .Build();
  }

  protected override async void OnStartup(StartupEventArgs e)
  {
    try
    {
      await _host.StartAsync();

      var mainWindow = _host.Services.GetRequiredService<MainWindow>();
      mainWindow.Show();
    }
    catch (Exception exception)
    {
      MessageBox.Show(
        exception.Message,
        "Errore avvio ChatGPTCodexSync",
        MessageBoxButton.OK,
        MessageBoxImage.Error);

      Shutdown(1);
    }

    base.OnStartup(e);
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    await _host.StopAsync();
    _host.Dispose();

    base.OnExit(e);
  }
}

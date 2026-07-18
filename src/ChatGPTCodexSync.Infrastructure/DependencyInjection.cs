using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using ChatGPTCodexSync.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChatGPTCodexSync.Infrastructure;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructureServices(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<CodexSyncOptions>(configuration.GetSection("CodexSync"));
    services.AddSingleton<ICodexProfileLocator, CodexProfileLocator>();
    services.AddSingleton<IChatGptProcessDetector, ChatGptProcessDetector>();
    services.AddSingleton<IBackupService, ZipBackupService>();

    return services;
  }
}

using ChatGPTCodexSync.Core.Models;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Options;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class CodexProfileLocator(IOptions<CodexSyncOptions> options) : ICodexProfileLocator
{
  public CodexProfile GetCurrentProfile()
  {
    var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var codexDirectoryPath = Path.Combine(userProfilePath, options.Value.CodexDirectoryName);

    return new CodexProfile(userProfilePath, codexDirectoryPath);
  }
}

using ChatGPTCodexSync.Core.Models;

namespace ChatGPTCodexSync.Core.Services;

public interface ICodexProfileLocator
{
  CodexProfile GetCurrentProfile();
}

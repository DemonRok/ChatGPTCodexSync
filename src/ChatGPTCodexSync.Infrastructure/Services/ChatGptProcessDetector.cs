using ChatGPTCodexSync.Core.Services;
using System.Diagnostics;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class ChatGptProcessDetector : IChatGptProcessDetector
{
  private static readonly string[] ProcessNameFragments =
  [
    "ChatGPT",
    "OpenAI",
    "Codex"
  ];

  public bool IsChatGptRunning()
  {
    var currentProcessId = Environment.ProcessId;

    return Process.GetProcesses()
      .Where(process => process.Id != currentProcessId)
      .Any(process => ProcessNameFragments.Any(fragment =>
        process.ProcessName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
  }
}

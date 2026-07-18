using ChatGPTCodexSync.Core.Backup;
using ChatGPTCodexSync.Core.Options;
using ChatGPTCodexSync.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ChatGPTCodexSync.Infrastructure.Services;

internal sealed class SevenZipToolProvider(
  IOptions<CodexSyncOptions> options,
  ILogger<SevenZipToolProvider> logger) : ISevenZipToolProvider
{
  private static readonly string[] Installed7ZipPaths =
  [
    @"C:\Program Files\7-Zip\7z.exe",
    @"C:\Program Files (x86)\7-Zip\7z.exe"
  ];

  public async Task<ArchiveTool?> GetSevenZipAsync(
    bool offlineMode,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    if (offlineMode || options.Value.OfflineMode)
    {
      progress.Report(new BackupProgress("Offline mode enabled. Checking installed 7-Zip only."));
      return await FindInstalled7ZipAsync(progress, cancellationToken);
    }

    var portableTool = await GetPortable7ZipAsync(progress, cancellationToken);
    if (portableTool is not null)
    {
      return portableTool;
    }

    return await FindInstalled7ZipAsync(progress, cancellationToken);
  }

  private async Task<ArchiveTool?> GetPortable7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var executablePath = GetPortableExecutablePath();
    if (File.Exists(executablePath))
    {
      var version = await GetVersionAsync(executablePath, cancellationToken);
      if (IsVersionSupported(version))
      {
        progress.Report(new BackupProgress($"Using portable 7-Zip {version}: {executablePath}"));
        return new ArchiveTool(executablePath, version, false);
      }

      progress.Report(new BackupProgress($"Portable 7-Zip {version} is older than required {options.Value.Minimum7ZipVersion}."));
    }

    if (!options.Value.Allow7ZipAutoDownload)
    {
      progress.Report(new BackupProgress("7-Zip auto-download is disabled."));
      return null;
    }

    try
    {
      progress.Report(new BackupProgress("Downloading portable 7-Zip console tool..."));
      Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);

      var tempDownloadPath = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-7zr-{Guid.NewGuid():N}.exe");
      using var httpClient = new HttpClient();
      await using (var downloadStream = await httpClient.GetStreamAsync(options.Value.SevenZipDownloadUrl, cancellationToken))
      await using (var fileStream = File.Create(tempDownloadPath))
      {
        await downloadStream.CopyToAsync(fileStream, cancellationToken);
      }

      File.Copy(tempDownloadPath, executablePath, true);
      File.Delete(tempDownloadPath);

      var downloadedVersion = await GetVersionAsync(executablePath, cancellationToken);
      if (!IsVersionSupported(downloadedVersion))
      {
        progress.Report(new BackupProgress($"Downloaded 7-Zip {downloadedVersion} is older than required {options.Value.Minimum7ZipVersion}."));
        return null;
      }

      progress.Report(new BackupProgress($"Using downloaded 7-Zip {downloadedVersion}: {executablePath}"));
      return new ArchiveTool(executablePath, downloadedVersion, true);
    }
    catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
    {
      logger.LogWarning(exception, "Unable to download portable 7-Zip");
      progress.Report(new BackupProgress("Unable to download portable 7-Zip. Checking installed 7-Zip."));
      return null;
    }
  }

  private async Task<ArchiveTool?> FindInstalled7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    foreach (var candidatePath in EnumerateInstalledCandidates())
    {
      if (!File.Exists(candidatePath))
      {
        continue;
      }

      var version = await GetVersionAsync(candidatePath, cancellationToken);
      if (!IsVersionSupported(version))
      {
        progress.Report(new BackupProgress($"Installed 7-Zip {version} is older than required {options.Value.Minimum7ZipVersion}: {candidatePath}"));
        continue;
      }

      progress.Report(new BackupProgress($"Using installed 7-Zip {version}: {candidatePath}"));
      return new ArchiveTool(candidatePath, version, false);
    }

    progress.Report(new BackupProgress("7-Zip is not available. Falling back to ZIP."));
    return null;
  }

  private string GetPortableExecutablePath()
  {
    return Path.Combine(
      AppContext.BaseDirectory,
      options.Value.ToolsDirectoryName,
      options.Value.SevenZipToolDirectoryName,
      "7zr.exe");
  }

  private static IEnumerable<string> EnumerateInstalledCandidates()
  {
    var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      yield return Path.Combine(directory.Trim(), "7z.exe");
    }

    foreach (var installedPath in Installed7ZipPaths)
    {
      yield return installedPath;
    }
  }

  private static async Task<string> GetVersionAsync(string executablePath, CancellationToken cancellationToken)
  {
    var result = await RunProcessAsync(executablePath, [], cancellationToken);
    var match = Regex.Match(result, @"\b(\d+\.\d+)\b");

    return match.Success ? match.Groups[1].Value : "0.0";
  }

  private bool IsVersionSupported(string version)
  {
    return CompareVersions(version, options.Value.Minimum7ZipVersion) >= 0;
  }

  private static int CompareVersions(string left, string right)
  {
    var leftParts = ParseVersion(left);
    var rightParts = ParseVersion(right);

    for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
    {
      var leftPart = index < leftParts.Length ? leftParts[index] : 0;
      var rightPart = index < rightParts.Length ? rightParts[index] : 0;
      var comparison = leftPart.CompareTo(rightPart);

      if (comparison != 0)
      {
        return comparison;
      }
    }

    return 0;
  }

  private static int[] ParseVersion(string version)
  {
    return version
      .Split('.', StringSplitOptions.RemoveEmptyEntries)
      .Select(part => int.TryParse(part, out var value) ? value : 0)
      .ToArray();
  }

  private static async Task<string> RunProcessAsync(
    string executablePath,
    IReadOnlyList<string> arguments,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = executablePath,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {executablePath}.");
    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    return await outputTask + Environment.NewLine + await errorTask;
  }
}

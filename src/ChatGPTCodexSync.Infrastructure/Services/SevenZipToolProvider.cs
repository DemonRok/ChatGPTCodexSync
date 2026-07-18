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

    var portableFullTool = await GetPortableFull7ZipAsync(progress, cancellationToken);
    if (portableFullTool is not null)
    {
      return portableFullTool;
    }

    var portableExtraTool = await GetPortableExtra7ZipAsync(progress, cancellationToken);
    if (portableExtraTool is not null)
    {
      return portableExtraTool;
    }

    if (options.Value.Allow7ZipAutoDownload)
    {
      var downloadedTool = await DownloadPortable7ZipAsync(progress, cancellationToken);
      if (downloadedTool is not null)
      {
        return downloadedTool;
      }
    }
    else
    {
      progress.Report(new BackupProgress("7-Zip auto-download is disabled."));
    }

    var installedTool = await FindInstalled7ZipAsync(progress, cancellationToken, reportMissing: false);
    if (installedTool is not null)
    {
      return installedTool;
    }

    var portableReducedTool = await GetPortableReduced7ZipAsync(progress, false, cancellationToken);
    if (portableReducedTool is not null)
    {
      return portableReducedTool;
    }

    progress.Report(new BackupProgress("7-Zip is not available. Falling back to ZIP."));
    return null;
  }

  private async Task<ArchiveTool?> GetPortableFull7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var executablePath = GetPortableFullExecutablePath();
    if (!File.Exists(executablePath))
    {
      return null;
    }

    var version = await GetVersionAsync(executablePath, cancellationToken);
    if (IsVersionSupported(version))
    {
      progress.Report(new BackupProgress($"Using portable full 7-Zip {version}: {executablePath}"));
      return new ArchiveTool(executablePath, version, false);
    }

    progress.Report(new BackupProgress($"Portable full 7-Zip {version} is older than required {options.Value.Minimum7ZipVersion}."));
    return null;
  }

  private async Task<ArchiveTool?> GetPortableExtra7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var executablePath = GetPortableExtraExecutablePath();
    if (!File.Exists(executablePath))
    {
      return null;
    }

    var version = await GetVersionAsync(executablePath, cancellationToken);
    if (IsVersionSupported(version))
    {
      progress.Report(new BackupProgress($"Using portable x64 7-Zip console {version}: {executablePath}"));
      return new ArchiveTool(executablePath, version, false);
    }

    progress.Report(new BackupProgress($"Portable x64 7-Zip console {version} is older than required {options.Value.Minimum7ZipVersion}."));
    return null;
  }

  private async Task<ArchiveTool?> GetPortableReduced7ZipAsync(
    IProgress<BackupProgress> progress,
    bool allowDownload,
    CancellationToken cancellationToken)
  {
    var executablePath = GetPortableReducedExecutablePath();
    if (File.Exists(executablePath))
    {
      var version = await GetVersionAsync(executablePath, cancellationToken);
      if (IsVersionSupported(version))
      {
        progress.Report(new BackupProgress($"Using portable reduced 7-Zip {version}: {executablePath}"));
        return new ArchiveTool(executablePath, version, false);
      }

      progress.Report(new BackupProgress($"Portable reduced 7-Zip {version} is older than required {options.Value.Minimum7ZipVersion}."));
    }

    if (!allowDownload)
    {
      return null;
    }

    try
    {
      progress.Report(new BackupProgress("Downloading portable reduced 7-Zip fallback..."));
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

      progress.Report(new BackupProgress($"Using downloaded reduced 7-Zip {downloadedVersion}: {executablePath}"));
      return new ArchiveTool(executablePath, downloadedVersion, true);
    }
    catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
    {
      logger.LogWarning(exception, "Unable to download portable 7-Zip");
      progress.Report(new BackupProgress("Unable to download portable 7-Zip. Checking installed 7-Zip."));
      return null;
    }
  }

  private async Task<ArchiveTool?> DownloadPortable7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var bootstrapTool = await GetPortableReduced7ZipAsync(progress, true, cancellationToken);
    if (bootstrapTool is null)
    {
      return null;
    }

    var fullTool = await DownloadPortableFull7ZipAsync(bootstrapTool, progress, cancellationToken);
    if (fullTool is not null)
    {
      return fullTool;
    }

    var extraTool = await DownloadPortableExtra7ZipAsync(bootstrapTool, progress, cancellationToken);
    return extraTool ?? bootstrapTool;
  }

  private async Task<ArchiveTool?> DownloadPortableFull7ZipAsync(
    ArchiveTool bootstrapTool,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    if (!Environment.Is64BitOperatingSystem)
    {
      return null;
    }

    var toolDirectory = GetPortableToolDirectory();
    var tempDownloadPath = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-7z-full-{Guid.NewGuid():N}.exe");
    var tempExtractDirectory = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-7z-full-{Guid.NewGuid():N}");

    try
    {
      progress.Report(new BackupProgress("Downloading portable full 7-Zip package..."));
      await DownloadFileAsync(options.Value.SevenZipFullDownloadUrl, tempDownloadPath, cancellationToken);

      progress.Report(new BackupProgress("Extracting portable full 7-Zip package..."));
      Directory.CreateDirectory(tempExtractDirectory);
      await RunProcessAsync(
        bootstrapTool.ExecutablePath,
        ["x", tempDownloadPath, $"-o{tempExtractDirectory}", "-y"],
        cancellationToken);

      var extractedExecutablePath = Directory
        .EnumerateFiles(tempExtractDirectory, "7z.exe", SearchOption.AllDirectories)
        .FirstOrDefault();

      var extractedDllPath = Directory
        .EnumerateFiles(tempExtractDirectory, "7z.dll", SearchOption.AllDirectories)
        .FirstOrDefault();

      if (extractedExecutablePath is null || extractedDllPath is null)
      {
        progress.Report(new BackupProgress("Portable full 7-Zip package did not contain the expected files."));
        return null;
      }

      Directory.CreateDirectory(toolDirectory);
      File.Copy(extractedExecutablePath, GetPortableFullExecutablePath(), true);
      File.Copy(extractedDllPath, Path.Combine(toolDirectory, "7z.dll"), true);

      return await GetPortableFull7ZipAsync(progress, cancellationToken);
    }
    catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      logger.LogWarning(exception, "Unable to prepare portable full 7-Zip");
      progress.Report(new BackupProgress("Unable to prepare portable full 7-Zip. Trying portable console package."));
      return null;
    }
    finally
    {
      TryDeleteFile(tempDownloadPath);
      TryDeleteDirectory(tempExtractDirectory);
    }
  }

  private async Task<ArchiveTool?> DownloadPortableExtra7ZipAsync(
    ArchiveTool bootstrapTool,
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken)
  {
    var toolDirectory = GetPortableToolDirectory();
    var tempDownloadPath = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-7z-extra-{Guid.NewGuid():N}.7z");
    var tempExtractDirectory = Path.Combine(Path.GetTempPath(), $"ChatGPTCodexSync-7z-extra-{Guid.NewGuid():N}");

    try
    {
      progress.Report(new BackupProgress("Downloading portable x64 7-Zip console package..."));
      await DownloadFileAsync(options.Value.SevenZipExtraDownloadUrl, tempDownloadPath, cancellationToken);

      progress.Report(new BackupProgress("Extracting portable x64 7-Zip console package..."));
      Directory.CreateDirectory(tempExtractDirectory);
      await RunProcessAsync(
        bootstrapTool.ExecutablePath,
        ["x", tempDownloadPath, $"-o{tempExtractDirectory}", "-y"],
        cancellationToken);

      var extractedExecutablePath = Path.Combine(tempExtractDirectory, "x64", "7za.exe");
      if (!File.Exists(extractedExecutablePath))
      {
        progress.Report(new BackupProgress("Portable x64 7-Zip console package did not contain the expected file."));
        return null;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(GetPortableExtraExecutablePath())!);
      File.Copy(extractedExecutablePath, GetPortableExtraExecutablePath(), true);

      var extractedDllPath = Path.Combine(tempExtractDirectory, "x64", "7za.dll");
      if (File.Exists(extractedDllPath))
      {
        File.Copy(extractedDllPath, Path.Combine(toolDirectory, "x64", "7za.dll"), true);
      }

      return await GetPortableExtra7ZipAsync(progress, cancellationToken);
    }
    catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
    {
      logger.LogWarning(exception, "Unable to prepare portable x64 7-Zip console");
      progress.Report(new BackupProgress("Unable to prepare portable x64 7-Zip console."));
      return null;
    }
    finally
    {
      TryDeleteFile(tempDownloadPath);
      TryDeleteDirectory(tempExtractDirectory);
    }
  }

  private async Task<ArchiveTool?> FindInstalled7ZipAsync(
    IProgress<BackupProgress> progress,
    CancellationToken cancellationToken,
    bool reportMissing = true)
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

    if (reportMissing)
    {
      progress.Report(new BackupProgress("7-Zip is not available. Falling back to ZIP."));
    }

    return null;
  }

  private string GetPortableFullExecutablePath()
  {
    return Path.Combine(GetPortableToolDirectory(), "7z.exe");
  }

  private string GetPortableExtraExecutablePath()
  {
    return Path.Combine(GetPortableToolDirectory(), "x64", "7za.exe");
  }

  private string GetPortableReducedExecutablePath()
  {
    return Path.Combine(GetPortableToolDirectory(), "7zr.exe");
  }

  private string GetPortableToolDirectory()
  {
    return Path.Combine(
      AppContext.BaseDirectory,
      options.Value.ToolsDirectoryName,
      options.Value.SevenZipToolDirectoryName);
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

    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"7-Zip failed with exit code {process.ExitCode}.");
    }

    return await outputTask + Environment.NewLine + await errorTask;
  }

  private static async Task DownloadFileAsync(
    string url,
    string destinationPath,
    CancellationToken cancellationToken)
  {
    using var httpClient = new HttpClient();
    await using var downloadStream = await httpClient.GetStreamAsync(url, cancellationToken);
    await using var fileStream = File.Create(destinationPath);

    await downloadStream.CopyToAsync(fileStream, cancellationToken);
  }

  private static void TryDeleteFile(string filePath)
  {
    try
    {
      if (File.Exists(filePath))
      {
        File.Delete(filePath);
      }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private static void TryDeleteDirectory(string directoryPath)
  {
    try
    {
      if (Directory.Exists(directoryPath))
      {
        Directory.Delete(directoryPath, true);
      }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }
}

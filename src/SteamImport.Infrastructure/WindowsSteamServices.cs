using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SteamImport.Infrastructure;

[SupportedOSPlatform("windows")]
public static class WindowsSteamInstallationLocator
{
    public static SteamInstallation? Find()
    {
        foreach (var candidate in GetCandidatePaths())
        {
            try
            {
                return SteamInstallation.Open(candidate);
            }
            catch (InvalidOperationException)
            {
                // Keep trying the remaining Windows discovery locations.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var currentUserPath = ReadRegistryValue(
            Registry.CurrentUser,
            @"Software\Valve\Steam",
            "SteamPath");
        if (!string.IsNullOrWhiteSpace(currentUserPath))
        {
            yield return currentUserPath;
        }

        var localMachinePath = ReadRegistryValue(
            Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Valve\Steam",
            "InstallPath");
        if (!string.IsNullOrWhiteSpace(localMachinePath))
        {
            yield return localMachinePath;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }
    }

    private static string? ReadRegistryValue(RegistryKey root, string keyName, string valueName)
    {
        using var key = root.OpenSubKey(keyName);
        return key?.GetValue(valueName) as string;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSteamProcessProbe : ISteamProcessProbe
{
    public bool IsRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSteamClientController : ISteamClientController
{
    private static readonly string[] ClientProcessNames = ["steam", "steamwebhelper", "gameoverlayui"];
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);

    public bool IsRunning() => HasProcess("steam");

    public Task RequestShutdownAsync(
        string steamExecutablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = StartSteam(steamExecutablePath, "-shutdown");
        return Task.CompletedTask;
    }

    public async Task<bool> WaitForExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (HasAnyClientProcess())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        return true;
    }

    public async Task StartBigPictureAsync(
        string steamExecutablePath,
        CancellationToken cancellationToken)
    {
        using var process = StartSteam(steamExecutablePath, "-bigpicture");
        var stopwatch = Stopwatch.StartNew();
        while (!IsRunning())
        {
            if (stopwatch.Elapsed >= StartupTimeout)
            {
                throw new TimeoutException(
                    "A importação terminou, mas a Steam não iniciou em modo Big Picture. Abra-a manualmente.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private static Process StartSteam(string steamExecutablePath, string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamExecutablePath);
        var executablePath = Path.GetFullPath(steamExecutablePath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("O executável da Steam não foi encontrado.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ??
               throw new InvalidOperationException("A Steam recusou o comando de inicialização.");
    }

    private static bool HasAnyClientProcess() => ClientProcessNames.Any(HasProcess);

    private static bool HasProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsGameActivityProbe : IGameActivityProbe
{
    private static readonly Regex LibraryPathPattern = new(
        "\\\"path\\\"\\s+\\\"(?<path>.+)\\\"",
        RegexOptions.CultureInvariant);

    public bool IsGameRunning(string gamesRootPath, string steamRootPath)
    {
        if (HasProcess("gameoverlayui"))
        {
            return true;
        }

        var gameRoots = ReadGameRoots(gamesRootPath, steamRootPath);
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var executablePath = process.MainModule?.FileName;
                    if (executablePath is not null && gameRoots.Any(root => IsDescendant(root, executablePath)))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or
                    InvalidOperationException or
                    NotSupportedException)
                {
                    // Protected and already-exited processes cannot be classified by their image path.
                }
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<string> ReadGameRoots(string gamesRootPath, string steamRootPath)
    {
        var roots = new List<string>();
        AddRoot(roots, gamesRootPath);
        AddRoot(roots, Path.Combine(steamRootPath, "steamapps", "common"));
        var librariesPath = Path.Combine(steamRootPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(librariesPath))
        {
            return roots;
        }

        try
        {
            foreach (var line in File.ReadLines(librariesPath))
            {
                var match = LibraryPathPattern.Match(line);
                if (match.Success)
                {
                    AddRoot(
                        roots,
                        Path.Combine(match.Groups["path"].Value.Replace("\\\\", "\\"), "steamapps", "common"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The primary library and configured games root are still usable.
        }

        return roots;
    }

    private static void AddRoot(List<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        }
    }

    private static bool IsDescendant(string rootPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(rootPath, Path.GetFullPath(candidatePath));
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, ".", StringComparison.Ordinal) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool HasProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

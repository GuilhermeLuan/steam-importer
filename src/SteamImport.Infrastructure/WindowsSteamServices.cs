using System.Diagnostics;
using System.Runtime.Versioning;
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

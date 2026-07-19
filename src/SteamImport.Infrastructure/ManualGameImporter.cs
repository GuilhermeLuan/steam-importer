using SteamImport.Core;

namespace SteamImport.Infrastructure;

public interface ISteamProcessProbe
{
    bool IsRunning();
}

public sealed record ManualGameImportRequest(
    string ShortcutsPath,
    string BackupDirectory,
    string DisplayName,
    string ExecutablePath);

public sealed class ManualGameImporter(
    ISteamProcessProbe steamProcessProbe,
    IAppLog? appLog = null)
{
    private readonly IAppLog log = appLog ?? NullAppLog.Instance;

    public SteamShortcut Import(ManualGameImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (steamProcessProbe.IsRunning())
        {
            log.LogWarning("import.blocked", "reason=steam-running");
            throw new SteamIsRunningException();
        }

        var displayName = request.DisplayName.Trim();
        var executablePath = request.ExecutablePath.Trim().Trim('"');
        var shortcut = new SteamShortcut(
            SteamShortcutAppId.Calculate(executablePath, displayName),
            displayName,
            executablePath,
            GetDirectoryName(executablePath));
        log.LogInformation(
            "import.started",
            $"appId={shortcut.AppId} existingFile={File.Exists(request.ShortcutsPath)}");

        string? backupPath = null;
        if (File.Exists(request.ShortcutsPath))
        {
            Directory.CreateDirectory(request.BackupDirectory);
            backupPath = Path.Combine(
                request.BackupDirectory,
                $"shortcuts-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfffffff}-{Guid.NewGuid():N}.vdf");
            File.Copy(request.ShortcutsPath, backupPath);
            log.LogInformation("import.backup-created", "result=success");
        }

        try
        {
            if (steamProcessProbe.IsRunning())
            {
                log.LogWarning("import.blocked", "reason=steam-started-before-write");
                throw new SteamIsRunningException("Steam started before the shortcut could be written. Close it and try again.");
            }

            SteamShortcutStore.Add(request.ShortcutsPath, shortcut);
            RotateBackups(request.BackupDirectory);
            log.LogInformation("import.completed", $"appId={shortcut.AppId} result=success");
            return shortcut;
        }
        catch (Exception exception)
        {
            log.LogError("import.failed", $"appId={shortcut.AppId}", exception);
            if (backupPath is not null)
            {
                File.Copy(backupPath, request.ShortcutsPath, overwrite: true);
                log.LogInformation("import.backup-restored", "result=success");
            }

            throw;
        }
    }

    private static string GetDirectoryName(string executablePath)
    {
        var separatorIndex = Math.Max(
            executablePath.LastIndexOf('\\'),
            executablePath.LastIndexOf('/'));
        return separatorIndex > 0 ? executablePath[..separatorIndex] : string.Empty;
    }

    private static void RotateBackups(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        foreach (var obsoleteBackup in Directory
                     .EnumerateFiles(backupDirectory, "shortcuts-*.vdf")
                     .OrderByDescending(path => path, StringComparer.Ordinal)
                     .Skip(5))
        {
            File.Delete(obsoleteBackup);
        }
    }
}

public sealed class SteamIsRunningException : InvalidOperationException
{
    public SteamIsRunningException()
        : base("Steam must be closed before importing a shortcut.")
    {
    }

    public SteamIsRunningException(string message)
        : base(message)
    {
    }

    public SteamIsRunningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace SteamImport.Infrastructure;

public sealed record SteamAccount(string Id, string ShortcutsPath);

public sealed record SteamInstallation(
    string RootPath,
    IReadOnlyList<SteamAccount> Accounts)
{
    public static SteamInstallation Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var executablePath = Path.Combine(fullPath, "steam.exe");
        var userDataPath = Path.Combine(fullPath, "userdata");
        if (!File.Exists(executablePath) || !Directory.Exists(userDataPath))
        {
            throw new InvalidOperationException("The selected folder is not a valid Steam installation.");
        }

        var accounts = Directory
            .EnumerateDirectories(userDataPath)
            .Select(path => new DirectoryInfo(path).Name)
            .Where(id => ulong.TryParse(id, out _))
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new SteamAccount(
                id,
                Path.Combine(userDataPath, id, "config", "shortcuts.vdf")))
            .ToArray();

        return new SteamInstallation(fullPath, accounts);
    }
}

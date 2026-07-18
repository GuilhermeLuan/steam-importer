namespace SteamImport.Core;

public sealed record SteamShortcut(
    uint AppId,
    string DisplayName,
    string ExecutablePath,
    string StartDirectory);

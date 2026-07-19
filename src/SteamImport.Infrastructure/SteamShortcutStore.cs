using SteamImport.Core;

namespace SteamImport.Infrastructure;

public static class SteamShortcutStore
{
    public static IReadOnlyList<SteamShortcut> ReadAll(string shortcutsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutsPath);

        if (!File.Exists(shortcutsPath))
        {
            return [];
        }

        var root = ReadDocument(shortcutsPath);
        return (root.Children ?? [])
            .Where(node => node.Type == BinaryVdf.Object)
            .Select(ReadShortcut)
            .ToArray();
    }

    public static void Add(string shortcutsPath, SteamShortcut shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutsPath);
        ArgumentNullException.ThrowIfNull(shortcut);

        var root = File.Exists(shortcutsPath)
            ? ReadDocument(shortcutsPath)
            : BinaryVdfNode.Object("shortcuts");
        var children = root.Children
            ?? throw new InvalidDataException("The shortcuts VDF root must be an object.");

        if (children
            .Where(node => node.Type == BinaryVdf.Object)
            .Select(ReadShortcut)
            .Any(existing => existing.AppId == shortcut.AppId))
        {
            throw new InvalidOperationException($"A Steam shortcut with AppID {shortcut.AppId} already exists.");
        }

        var nextIndex = children
            .Select(node => int.TryParse(node.Name, out var index) ? index : -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        children.Add(CreateNode(nextIndex, shortcut));

        var fullPath = Path.GetFullPath(shortcutsPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(shortcutsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                BinaryVdf.Write(stream, root);
                stream.Flush(flushToDisk: true);
            }

            _ = ReadDocument(temporaryPath);
            if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static BinaryVdfNode ReadDocument(string shortcutsPath)
    {
        using var stream = File.OpenRead(shortcutsPath);
        var root = BinaryVdf.Read(stream);
        if (root.Type != BinaryVdf.Object || !string.Equals(root.Name, "shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The file is not a Steam shortcuts VDF document.");
        }

        return root;
    }

    private static SteamShortcut ReadShortcut(BinaryVdfNode node)
    {
        var fields = (node.Children ?? [])
            .ToDictionary(child => child.Name, StringComparer.OrdinalIgnoreCase);
        return new SteamShortcut(
            unchecked((uint)GetRequired<int>(fields, "appid")),
            GetRequired<string>(fields, "AppName"),
            Unquote(GetRequired<string>(fields, "Exe")),
            Unquote(GetRequired<string>(fields, "StartDir")));
    }

    private static T GetRequired<T>(Dictionary<string, BinaryVdfNode> fields, string name) =>
        fields.TryGetValue(name, out var field)
            ? field.GetValue<T>()
            : throw new InvalidDataException($"Steam shortcut is missing required field '{name}'.");

    private static BinaryVdfNode CreateNode(int index, SteamShortcut shortcut) =>
        BinaryVdfNode.Object(
            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [
                BinaryVdfNode.Int32("appid", unchecked((int)shortcut.AppId)),
                BinaryVdfNode.String("AppName", shortcut.DisplayName),
                BinaryVdfNode.String("Exe", Quote(shortcut.ExecutablePath)),
                BinaryVdfNode.String("StartDir", Quote(shortcut.StartDirectory)),
                BinaryVdfNode.String("icon", string.Empty),
                BinaryVdfNode.String("ShortcutPath", string.Empty),
                BinaryVdfNode.String("LaunchOptions", string.Empty),
                BinaryVdfNode.Int32("IsHidden", 0),
                BinaryVdfNode.Int32("AllowDesktopConfig", 1),
                BinaryVdfNode.Int32("AllowOverlay", 1),
                BinaryVdfNode.Int32("OpenVR", 0),
                BinaryVdfNode.Int32("Devkit", 0),
                BinaryVdfNode.String("DevkitGameID", string.Empty),
                BinaryVdfNode.Int32("DevkitOverrideAppID", 0),
                BinaryVdfNode.Int32("LastPlayTime", 0),
                BinaryVdfNode.String("FlatpakAppID", string.Empty),
                BinaryVdfNode.Object("tags"),
            ]);

    private static string Quote(string path) => $"\"{path.Trim('"')}\"";

    private static string Unquote(string path) => path.Length >= 2 && path[0] == '"' && path[^1] == '"'
        ? path[1..^1]
        : path;
}

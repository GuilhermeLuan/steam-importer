using SteamImport.Infrastructure;
using SteamImport.Core;
using ValveKeyValue;

namespace SteamImport.Infrastructure.Tests;

public sealed class SteamShortcutStoreTests
{
    [Fact]
    public void AccountWithoutShortcutFileHasNoShortcuts()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}",
            "shortcuts.vdf");

        var shortcuts = SteamShortcutStore.ReadAll(path);

        Assert.Empty(shortcuts);
    }

    [Fact]
    public void FirstShortcutCanBeAddedAndReadBack()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "shortcuts.vdf");
        var shortcut = new SteamShortcut(
            0x85669BD1,
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe",
            @"C:\Games\Neon Horizon");

        try
        {
            SteamShortcutStore.Add(path, shortcut);

            Assert.Equal([shortcut], SteamShortcutStore.ReadAll(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExistingShortcutsRemainUnchangedWhenAnotherIsAdded()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "shortcuts.vdf");
        var existing = new SteamShortcut(
            0x80000001,
            "Existing Game",
            @"D:\Existing\game.exe",
            @"D:\Existing");
        var imported = new SteamShortcut(
            0x80000002,
            "Imported Game",
            @"D:\Imported\game.exe",
            @"D:\Imported");

        try
        {
            SteamShortcutStore.Add(path, existing);
            SteamShortcutStore.Add(path, imported);

            Assert.Equal([existing, imported], SteamShortcutStore.ReadAll(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WrittenShortcutIsACompleteValveKeyValueBinaryDocument()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "shortcuts.vdf");
        var shortcut = new SteamShortcut(
            0x85669BD1,
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe",
            @"C:\Games\Neon Horizon");

        try
        {
            SteamShortcutStore.Add(path, shortcut);

            using var stream = File.OpenRead(path);
            var document = KVSerializer
                .Create(KVSerializationFormat.KeyValues1Binary)
                .Deserialize(stream);

            Assert.Equal("shortcuts", document.Name);
            Assert.Equal("Neon Horizon", (string)document["0"]["AppName"]);
            Assert.Equal(stream.Length, stream.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ShortcutDocumentWithoutItsFinalMarkerIsRejected()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(directory, "shortcuts.vdf");
        var shortcut = new SteamShortcut(
            0x85669BD1,
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe",
            @"C:\Games\Neon Horizon");

        try
        {
            SteamShortcutStore.Add(path, shortcut);
            var completeDocument = File.ReadAllBytes(path);
            File.WriteAllBytes(path, completeDocument[..^1]);

            Assert.Throws<InvalidDataException>(() => SteamShortcutStore.ReadAll(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

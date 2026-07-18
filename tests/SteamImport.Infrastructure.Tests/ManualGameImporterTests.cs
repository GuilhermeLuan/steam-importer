using SteamImport.Infrastructure;
using SteamImport.Core;

namespace SteamImport.Infrastructure.Tests;

public sealed class ManualGameImporterTests
{
    [Fact]
    public void RunningSteamBlocksImportBeforeAnyFileIsChanged()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var request = new ManualGameImportRequest(
            shortcutsPath,
            System.IO.Path.Combine(root, "backups"),
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe");
        var importer = new ManualGameImporter(new FixedSteamProcessProbe(isRunning: true));

        Assert.Throws<SteamIsRunningException>(() => importer.Import(request));
        Assert.False(File.Exists(shortcutsPath));
        Assert.False(Directory.Exists(request.BackupDirectory));
    }

    [Fact]
    public void RunningSteamIsRecordedInTheApplicationLog()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var log = new FileAppLog(System.IO.Path.Combine(root, "logs"));
        var request = new ManualGameImportRequest(
            System.IO.Path.Combine(root, "shortcuts.vdf"),
            System.IO.Path.Combine(root, "backups"),
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe");
        var importer = new ManualGameImporter(
            new FixedSteamProcessProbe(isRunning: true),
            log);

        try
        {
            Assert.Throws<SteamIsRunningException>(() => importer.Import(request));

            var contents = File.ReadAllText(log.FilePath);
            Assert.Contains("level=WARN event=import.blocked reason=steam-running", contents, StringComparison.Ordinal);
            Assert.DoesNotContain(request.ExecutablePath, contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClosedSteamAllowsAReviewedGameToBeImportedWithARecoverableBackup()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var backupDirectory = System.IO.Path.Combine(root, "backups");
        var existing = new SteamShortcut(
            0x80000001,
            "Existing Game",
            @"D:\Existing\game.exe",
            @"D:\Existing");
        SteamShortcutStore.Add(shortcutsPath, existing);
        var request = new ManualGameImportRequest(
            shortcutsPath,
            backupDirectory,
            "Neon Horizon",
            @"C:\Games\Neon Horizon\NeonHorizon.exe");
        var importer = new ManualGameImporter(new FixedSteamProcessProbe(isRunning: false));

        try
        {
            var imported = importer.Import(request);

            Assert.Equal(0x85669BD1u, imported.AppId);
            Assert.Equal([existing, imported], SteamShortcutStore.ReadAll(shortcutsPath));
            var backup = Assert.Single(Directory.GetFiles(backupDirectory, "*.vdf"));
            Assert.Equal([existing], SteamShortcutStore.ReadAll(backup));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportKeepsOnlyFiveMostRecentBackups()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var backupDirectory = System.IO.Path.Combine(root, "backups");
        SteamShortcutStore.Add(
            shortcutsPath,
            new SteamShortcut(0x80000001, "Existing Game", @"D:\Existing\game.exe", @"D:\Existing"));
        var importer = new ManualGameImporter(new FixedSteamProcessProbe(isRunning: false));

        try
        {
            for (var index = 0; index < 6; index++)
            {
                importer.Import(new ManualGameImportRequest(
                    shortcutsPath,
                    backupDirectory,
                    $"Imported Game {index}",
                    $@"C:\Games\Imported{index}\game.exe"));
            }

            Assert.Equal(5, Directory.GetFiles(backupDirectory, "*.vdf").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SteamStartingBeforeTheCommitLeavesTheShortcutFileUnchanged()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var existing = new SteamShortcut(
            0x80000001,
            "Existing Game",
            @"D:\Existing\game.exe",
            @"D:\Existing");
        SteamShortcutStore.Add(shortcutsPath, existing);
        var importer = new ManualGameImporter(new SequenceSteamProcessProbe(false, true));

        try
        {
            Assert.Throws<SteamIsRunningException>(() => importer.Import(
                new ManualGameImportRequest(
                    shortcutsPath,
                    System.IO.Path.Combine(root, "backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe")));
            Assert.Equal([existing], SteamShortcutStore.ReadAll(shortcutsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedSteamProcessProbe(bool isRunning) : ISteamProcessProbe
    {
        public bool IsRunning() => isRunning;
    }

    private sealed class SequenceSteamProcessProbe(params bool[] states) : ISteamProcessProbe
    {
        private readonly Queue<bool> states = new(states);

        public bool IsRunning() => states.Dequeue();
    }
}

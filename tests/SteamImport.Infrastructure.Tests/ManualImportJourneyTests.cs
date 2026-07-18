using SteamImport.Core;
using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class ManualImportJourneyTests
{
    [Fact]
    public void FixtureAccountCompletesTheManualImportJourneyWithoutChangingExistingShortcuts()
    {
        var fixtureRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Journey-{Guid.NewGuid():N}");
        var steamRoot = System.IO.Path.Combine(fixtureRoot, "Steam");
        var gameRoot = System.IO.Path.Combine(fixtureRoot, "Games", "Neon Horizon");
        var configDirectory = System.IO.Path.Combine(
            steamRoot,
            "userdata",
            "76561198000000001",
            "config");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(gameRoot);
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var executablePath = System.IO.Path.Combine(gameRoot, "NeonHorizon.exe");
        File.WriteAllBytes(executablePath, []);
        File.WriteAllBytes(System.IO.Path.Combine(gameRoot, "setup.exe"), []);
        var existing = new SteamShortcut(
            0x80000001,
            "Existing Game",
            @"D:\Existing\game.exe",
            @"D:\Existing");
        var shortcutsPath = System.IO.Path.Combine(configDirectory, "shortcuts.vdf");
        SteamShortcutStore.Add(shortcutsPath, existing);

        try
        {
            var installation = SteamInstallation.Open(steamRoot);
            var account = Assert.Single(installation.Accounts);
            var review = ManualImportPlanner.CreateReview(gameRoot);
            var imported = new ManualGameImporter(new ClosedSteamProcessProbe()).Import(
                new ManualGameImportRequest(
                    account.ShortcutsPath,
                    System.IO.Path.Combine(fixtureRoot, "Backups"),
                    review.DisplayName,
                    review.RecommendedExecutable));

            Assert.Equal("Neon Horizon", imported.DisplayName);
            Assert.Equal(executablePath, imported.ExecutablePath);
            Assert.Equal([existing, imported], SteamShortcutStore.ReadAll(shortcutsPath));
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private sealed class ClosedSteamProcessProbe : ISteamProcessProbe
    {
        public bool IsRunning() => false;
    }
}

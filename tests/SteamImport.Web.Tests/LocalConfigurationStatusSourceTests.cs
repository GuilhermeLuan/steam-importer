using System.Text;
using SteamImport.Infrastructure;
using SteamImport.Web;
using Xunit;

namespace SteamImport.Web.Tests;

public sealed class LocalConfigurationStatusSourceTests
{
    [Fact]
    public void MissingConfigurationReportsEveryReadinessSignalAsFalse()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Status-{Guid.NewGuid():N}",
            "config.json");
        var store = new LocalConfigurationStore(path, new TestSecretProtector());
        var source = new LocalConfigurationStatusSource(store);

        var status = source.GetStatus();

        Assert.Equal(new SteamImportStatus(false, false, false), status);
        Assert.False(status.Ready);
    }

    [Fact]
    public void ValidPersistedConfigurationReportsTheComputerAsReady()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Status-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var store = new LocalConfigurationStore(
            System.IO.Path.Combine(root, "config.json"),
            new TestSecretProtector());

        try
        {
            store.Save(new LocalConfiguration(
                gamesRoot,
                "sgdb-secret-key",
                steamRoot,
                accountId));

            var status = new LocalConfigurationStatusSource(store).GetStatus();

            Assert.Equal(new SteamImportStatus(true, true, true), status);
            Assert.True(status.Ready);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidPersistedConfigurationReportsNotReadyInsteadOfFailingTheStatusRequest()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Status-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var store = new LocalConfigurationStore(
            System.IO.Path.Combine(root, "config.json"),
            new TestSecretProtector());

        try
        {
            store.Save(new LocalConfiguration(
                gamesRoot,
                "sgdb-secret-key",
                steamRoot,
                accountId));
            Directory.Delete(gamesRoot);

            var status = new LocalConfigurationStatusSource(store).GetStatus();

            Assert.Equal(new SteamImportStatus(false, false, false), status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string secret) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

        public string Unprotect(string protectedSecret) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protectedSecret));
    }
}

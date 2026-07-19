using SteamImport.Infrastructure;
using System.Text;

namespace SteamImport.Infrastructure.Tests;

public sealed class LocalConfigurationStoreTests
{
    [Fact]
    public void MissingConfigurationIsReportedThroughThePublicStoreInterface()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Config-{Guid.NewGuid():N}",
            "config.json");
        var store = new LocalConfigurationStore(path, new ReversibleTestSecretProtector());

        var configuration = store.Load();

        Assert.Null(configuration);
    }

    [Fact]
    public void ValidConfigurationCanBeSavedAndLoadedWithoutPersistingTheApiKeyInPlainText()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Config-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var path = System.IO.Path.Combine(root, "config.json");
        var expected = new LocalConfiguration(
            gamesRoot,
            "sgdb-secret-key",
            steamRoot,
            accountId);
        var store = new LocalConfigurationStore(path, new ReversibleTestSecretProtector());

        try
        {
            store.Save(expected);

            Assert.Equal(expected, store.Load());
            Assert.DoesNotContain("sgdb-secret-key", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PersistedConfigurationThatIsNoLongerValidIsRejectedWhenLoaded()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Config-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var store = new LocalConfigurationStore(
            System.IO.Path.Combine(root, "config.json"),
            new ReversibleTestSecretProtector());

        try
        {
            store.Save(new LocalConfiguration(
                gamesRoot,
                "sgdb-secret-key",
                steamRoot,
                accountId));
            Directory.Delete(gamesRoot);

            Assert.Throws<InvalidLocalConfigurationException>(() => store.Load());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorruptedConfigurationIsReportedAsInvalidData()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Config-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "{ not-valid-json");
        var store = new LocalConfigurationStore(path, new ReversibleTestSecretProtector());

        try
        {
            Assert.Throws<InvalidDataException>(() => store.Load());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ReversibleTestSecretProtector : ISecretProtector
    {
        public string Protect(string secret) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

        public string Unprotect(string protectedSecret) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protectedSecret));
    }
}

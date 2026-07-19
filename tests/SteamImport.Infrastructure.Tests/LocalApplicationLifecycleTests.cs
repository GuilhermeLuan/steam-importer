using System.Text;
using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class LocalApplicationLifecycleTests
{
    [Fact]
    public void SavingTheFirstConfigurationRegistersStartupForTheCurrentUser()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Lifecycle-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var store = new LocalConfigurationStore(
            System.IO.Path.Combine(root, "config.json"),
            new ReversibleTestSecretProtector());
        var startupRegistration = new RecordingUserStartupRegistration();
        var executablePath = System.IO.Path.Combine(root, "SteamImport.exe");
        var setup = new LocalSetup(store, startupRegistration, executablePath);
        var configuration = new LocalConfiguration(
            gamesRoot,
            "sgdb-secret-key",
            steamRoot,
            accountId);

        try
        {
            setup.Save(configuration);

            Assert.Equal(configuration, store.Load());
            Assert.Equal(executablePath, startupRegistration.RegisteredExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ASubsequentStartupResumesTheSavedConfigurationMinimized()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Lifecycle-{Guid.NewGuid():N}");
        var gamesRoot = System.IO.Path.Combine(root, "Games");
        var steamRoot = System.IO.Path.Combine(root, "Steam");
        var accountId = "76561198000000001";
        Directory.CreateDirectory(gamesRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(steamRoot, "userdata", accountId, "config"));
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        var store = new LocalConfigurationStore(
            System.IO.Path.Combine(root, "config.json"),
            new ReversibleTestSecretProtector());
        var configuration = new LocalConfiguration(
            gamesRoot,
            "sgdb-secret-key",
            steamRoot,
            accountId);

        try
        {
            store.Save(configuration);

            var result = new LocalStartup(store).Resume();

            Assert.Equal(configuration, result.Configuration);
            Assert.True(result.StartMinimized);
            Assert.False(result.RequiresSetup);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorruptedConfigurationReopensSetupWithoutDeletingLogsOrBackups()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Lifecycle-{Guid.NewGuid():N}");
        var configPath = System.IO.Path.Combine(root, "config.json");
        var logPath = System.IO.Path.Combine(root, "Logs", "existing.log");
        var backupPath = System.IO.Path.Combine(root, "Backups", "account", "shortcuts.vdf");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(configPath, "{ not-valid-json");
        File.WriteAllText(logPath, "existing log");
        File.WriteAllText(backupPath, "existing backup");
        var store = new LocalConfigurationStore(configPath, new ReversibleTestSecretProtector());

        try
        {
            var result = new LocalStartup(store).Resume();

            Assert.True(result.RequiresSetup);
            Assert.True(result.SavedConfigurationInvalid);
            Assert.True(File.Exists(configPath));
            Assert.Equal("existing log", File.ReadAllText(logPath));
            Assert.Equal("existing backup", File.ReadAllText(backupPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConfigurationWithAnUnreadableProtectedKeyReopensSetup()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Lifecycle-{Guid.NewGuid():N}");
        var configPath = System.IO.Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            configPath,
            """
            {
              "gamesRootPath": "unused",
              "protectedSteamGridDbApiKey": "unreadable",
              "steamRootPath": "unused",
              "steamAccountId": "unused"
            }
            """);
        var store = new LocalConfigurationStore(configPath, new UnreadableTestSecretProtector());

        try
        {
            var result = new LocalStartup(store).Resume();

            Assert.True(result.RequiresSetup);
            Assert.True(result.SavedConfigurationInvalid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ASecondApplicationInstanceCannotStartForTheSameUser()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Lifecycle-{Guid.NewGuid():N}");
        var lockPath = System.IO.Path.Combine(root, "steam-import.lock");

        try
        {
            using var firstInstance = SingleUserApplicationInstance.TryAcquire(lockPath);
            using var secondInstance = SingleUserApplicationInstance.TryAcquire(lockPath);

            Assert.NotNull(firstInstance);
            Assert.Null(secondInstance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingUserStartupRegistration : IUserStartupRegistration
    {
        public string? RegisteredExecutablePath { get; private set; }

        public void EnsureRegistered(string executablePath) =>
            RegisteredExecutablePath = executablePath;
    }

    private sealed class ReversibleTestSecretProtector : ISecretProtector
    {
        public string Protect(string secret) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

        public string Unprotect(string protectedSecret) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protectedSecret));
    }

    private sealed class UnreadableTestSecretProtector : ISecretProtector
    {
        public string Protect(string secret) => secret;

        public string Unprotect(string protectedSecret) =>
            throw new ArgumentException("The protected secret cannot be read.", nameof(protectedSecret));
    }
}

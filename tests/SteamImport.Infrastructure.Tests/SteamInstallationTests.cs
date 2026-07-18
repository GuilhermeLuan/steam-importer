using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class SteamInstallationTests
{
    [Fact]
    public void ManualInstallationExposesLocalSteamAccounts()
    {
        using var fixture = TemporarySteamInstallation.Create();
        fixture.AddAccount("76561198000000001");
        fixture.AddAccount("76561198000000002");
        fixture.AddAccount("not-an-account");

        var installation = SteamInstallation.Open(fixture.Path);

        Assert.Equal(fixture.Path, installation.RootPath);
        Assert.Collection(
            installation.Accounts,
            account =>
            {
                Assert.Equal("76561198000000001", account.Id);
                Assert.Equal(
                    System.IO.Path.Combine(fixture.Path, "userdata", account.Id, "config", "shortcuts.vdf"),
                    account.ShortcutsPath);
            },
            account => Assert.Equal("76561198000000002", account.Id));
    }

    private sealed class TemporarySteamInstallation : IDisposable
    {
        private TemporarySteamInstallation(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporarySteamInstallation Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SteamImport-Steam-{Guid.NewGuid():N}");
            Directory.CreateDirectory(System.IO.Path.Combine(path, "userdata"));
            File.WriteAllBytes(System.IO.Path.Combine(path, "steam.exe"), []);
            return new TemporarySteamInstallation(path);
        }

        public void AddAccount(string id)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "userdata", id, "config"));
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

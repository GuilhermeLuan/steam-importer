using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SteamImport.Infrastructure;
using SteamImport.Web;
using Xunit;

namespace SteamImport.Web.Tests;

public sealed class RemoteReviewBrowserTests
{
    [Fact]
    public async Task UserCanReviewAndCancelCandidateWithoutLeavingTheCandidateList()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Browser-{Guid.NewGuid():N}");
        var game = System.IO.Path.Combine(root, "Neon Horizon");
        Directory.CreateDirectory(System.IO.Path.Combine(game, "bin"));
        File.WriteAllBytes(System.IO.Path.Combine(game, "NeonHorizon.exe"), []);
        File.WriteAllBytes(System.IO.Path.Combine(game, "bin", "Alternative.exe"), []);

        try
        {
            await using var application = SteamImportServer.Build(
                new FixedStatusSource(new SteamImportStatus(true, true, true)),
                new FixedGamesRootSource(root));
            application.Urls.Add("http://127.0.0.1:0");
            await application.StartAsync(CancellationToken.None);
            var server = application.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(address);
            var secondGame = System.IO.Path.Combine(root, "Moonlight");
            Directory.CreateDirectory(secondGame);
            File.WriteAllBytes(System.IO.Path.Combine(secondGame, "Moonlight.exe"), []);
            await page.GetByRole(AriaRole.Button, new() { Name = "ATUALIZAR DESCOBERTA" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Moonlight" }))
                .ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon" }).ClickAsync();
            await page.GetByLabel("Nome do jogo").FillAsync("Neon Horizon Revisado");
            await page.GetByLabel("bin/Alternative.exe").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancelar" }).ClickAsync();

            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon" }))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByLabel("Nome do jogo")).ToBeHiddenAsync();
            await Assertions.Expect(page.GetByText("Neon Horizon Revisado")).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByText("REVISÃO CANCELADA // NENHUMA ALTERAÇÃO NA STEAM"))
                .ToBeVisibleAsync();
            await page.SetViewportSizeAsync(390, 844);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon" }))
                .ToBeVisibleAsync();
            var hasHorizontalOverflow = await page.EvaluateAsync<bool>(
                "document.documentElement.scrollWidth > document.documentElement.clientWidth");
            Assert.False(hasHorizontalOverflow);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UserChoosesTheSteamGridDbMatchBeforeOfficialNameAndArtworkAreAdopted()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Browser-{Guid.NewGuid():N}");
        var game = System.IO.Path.Combine(root, "Neon Horizon");
        Directory.CreateDirectory(game);
        File.WriteAllBytes(System.IO.Path.Combine(game, "NeonHorizon.exe"), []);
        var steamGridDb = new FakeSteamGridDbClient();

        try
        {
            await using var application = SteamImportServer.Build(
                new FixedStatusSource(new SteamImportStatus(true, true, true)),
                new FixedGamesRootSource(root),
                new SystemGameFolderScanner(),
                steamGridDb);
            application.Urls.Add("http://127.0.0.1:0");
            await application.StartAsync(CancellationToken.None);
            var server = application.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(address);
            await page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon Official" }))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon Remastered" }))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByLabel("Nome do jogo")).ToHaveValueAsync("Neon Horizon");
            Assert.Null(steamGridDb.SelectedGameId);

            await page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon Official" }).ClickAsync();

            await Assertions.Expect(page.GetByLabel("Nome do jogo"))
                .ToHaveValueAsync("Neon Horizon Official");
            await Assertions.Expect(page.GetByAltText("Grid vertical de Neon Horizon Official"))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByAltText("Hero de Neon Horizon Official"))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("GRID HORIZONTAL // AUSENTE"))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("LOGO // AUSENTE"))
                .ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("ÍCONE // AUSENTE"))
                .ToBeVisibleAsync();
            Assert.Equal(42, steamGridDb.SelectedGameId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UserCanConfirmTheReviewedGameAndSeeTheRemoteImportResult()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Browser-{Guid.NewGuid():N}");
        var game = System.IO.Path.Combine(root, "Neon Horizon");
        var steamRoot = root + "-Steam";
        var shortcutsPath = System.IO.Path.Combine(steamRoot, "userdata", "1", "config", "shortcuts.vdf");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shortcutsPath)!);
        File.WriteAllBytes(System.IO.Path.Combine(game, "NeonHorizon.exe"), []);
        File.WriteAllBytes(System.IO.Path.Combine(steamRoot, "steam.exe"), []);
        using var artworkHttpClient = new HttpClient(new RejectingArtworkHandler());
        using var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            artworkHttpClient);

        try
        {
            await using var application = SteamImportServer.Build(
                new FixedStatusSource(new SteamImportStatus(true, true, true)),
                new FixedGamesRootSource(root),
                new SystemGameFolderScanner(),
                new NoArtworkSteamGridDbClient(),
                new FixedRemoteImportContextSource(new RemoteImportContext(
                    System.IO.Path.Combine(steamRoot, "steam.exe"),
                    shortcutsPath,
                    System.IO.Path.Combine(steamRoot, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"))),
                importer);
            application.Urls.Add("http://127.0.0.1:0");
            await application.StartAsync(CancellationToken.None);
            var server = application.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
            var page = await browser.NewPageAsync();

            await page.GotoAsync(address);
            await page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Neon Horizon Official" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Importar" }).ClickAsync();

            await Assertions.Expect(page.GetByText("IMPORTADO // NEON HORIZON OFFICIAL"))
                .ToBeVisibleAsync();
            Assert.Single(SteamShortcutStore.ReadAll(shortcutsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(steamRoot, recursive: true);
        }
    }

    private sealed class FixedStatusSource(SteamImportStatus status) : IStatusSource
    {
        public SteamImportStatus GetStatus() => status;
    }

    private sealed class FixedGamesRootSource(string path) : IGamesRootSource
    {
        public string? GetGamesRootPath() => path;
    }

    private sealed class FakeSteamGridDbClient : ISteamGridDbClient
    {
        public long? SelectedGameId { get; private set; }

        public Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
            string provisionalName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SteamGridDbGameMatch>>(
            [
                new(42, "Neon Horizon Official", new Uri("https://cdn.example/cover.png")),
                new(84, "Neon Horizon Remastered", null),
            ]);

        public Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
            long gameId,
            CancellationToken cancellationToken)
        {
            SelectedGameId = gameId;
            return Task.FromResult(new SteamGridDbGameArtwork(
                gameId,
                "Neon Horizon Official",
                Asset(10, "vertical"),
                null,
                Asset(11, "hero"),
                null,
                null));
        }

        private static SteamGridDbArtworkAsset Asset(long id, string name) =>
            new(
                id,
                20,
                new Uri($"https://cdn.example/{name}.png"),
                new Uri($"https://cdn.example/{name}-thumb.png"));
    }

    private sealed class NoArtworkSteamGridDbClient : ISteamGridDbClient
    {
        public Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
            string provisionalName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SteamGridDbGameMatch>>(
                [new SteamGridDbGameMatch(42, "Neon Horizon Official", null)]);

        public Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
            long gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SteamGridDbGameArtwork(
                gameId,
                "Neon Horizon Official",
                null,
                null,
                null,
                null,
                null));
    }

    private sealed class FixedRemoteImportContextSource(RemoteImportContext context)
        : IRemoteImportContextSource
    {
        public RemoteImportContext? GetContext() => context;
    }

    private sealed class ClosedSteamClient : ISteamClientController
    {
        public bool IsRunning() => false;

        public Task RequestShutdownAsync(string steamExecutablePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task StartBigPictureAsync(string steamExecutablePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class NoRunningGameProbe : IGameActivityProbe
    {
        public bool IsGameRunning(string gamesRootPath, string steamRootPath) => false;
    }

    private sealed class RejectingArtworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}

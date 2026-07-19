using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
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

    private sealed class FixedStatusSource(SteamImportStatus status) : IStatusSource
    {
        public SteamImportStatus GetStatus() => status;
    }

    private sealed class FixedGamesRootSource(string path) : IGamesRootSource
    {
        public string? GetGamesRootPath() => path;
    }
}

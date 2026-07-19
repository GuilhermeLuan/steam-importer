using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SteamImport.Web;
using Xunit;

namespace SteamImport.Web.Tests;

public sealed class StatusEndpointTests
{
    [Fact]
    public async Task StatusEndpointReportsReadinessWithoutExposingTheSteamGridDbKey()
    {
        const string Secret = "sgdb-do-not-expose";
        var source = new FixedStatusSource(
            new SteamImportStatus(
                ConfigurationReady: true,
                SteamReady: true,
                AccountReady: true));
        await using var application = SteamImportServer.Build(source);
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var response = await client.GetAsync(
            "/api/status",
            CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"configurationReady\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"steamReady\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"accountReady\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"ready\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePageProvidesTheMinimalRemoteStatusInterface()
    {
        var source = new FixedStatusSource(new SteamImportStatus(false, false, false));
        await using var application = SteamImportServer.Build(source);
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var response = await client.GetAsync("/", CancellationToken.None);
        var html = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.Contains("STEAM_IMPORT", html, StringComparison.Ordinal);
        Assert.Contains("CONFIGURAÇÃO", html, StringComparison.Ordinal);
        Assert.Contains("STEAM", html, StringComparison.Ordinal);
        Assert.Contains("CONTA", html, StringComparison.Ordinal);
        Assert.Contains("/api/status", html, StringComparison.Ordinal);
    }

    private sealed class FixedStatusSource(SteamImportStatus status) : IStatusSource
    {
        public SteamImportStatus GetStatus() => status;
    }
}

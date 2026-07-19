using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SteamImport.Infrastructure;
using SteamImport.Web;
using Xunit;

namespace SteamImport.Web.Tests;

public sealed class GameEndpointsTests
{
    [Fact]
    public async Task GamesEndpointListsOnlyDirectFoldersWithoutExposingServerPaths()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddGame("Moonlight");
        games.AddNestedFolder("Neon Horizon", "Extras");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var response = await client.GetAsync("/api/games", CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(json);

        response.EnsureSuccessStatusCode();
        var candidates = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Moonlight", "Neon Horizon"], candidates.Select(ReadName));
        Assert.All(candidates, candidate => Assert.NotEqual(Guid.Empty, ReadId(candidate)));
        Assert.DoesNotContain(games.Path, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Extras", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshEndpointReplacesTheSnapshotAndItsServerGeneratedIds()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        var first = await ReadCandidates(client, HttpMethod.Get, "/api/games");
        games.AddGame("Moonlight");
        var cached = await ReadCandidates(client, HttpMethod.Get, "/api/games");
        var refreshed = await ReadCandidates(client, HttpMethod.Post, "/api/games/refresh");

        Assert.Single(first);
        Assert.Single(cached);
        Assert.Equal(first[0].Id, cached[0].Id);
        Assert.Equal(["Moonlight", "Neon Horizon"], refreshed.Select(candidate => candidate.Name));
        Assert.DoesNotContain(first[0].Id, refreshed.Select(candidate => candidate.Id));
    }

    [Fact]
    public async Task SelectingCandidateReturnsPlausibleExecutablesAndLocalRecommendationWithoutAbsolutePaths()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");
        games.AddFile("Neon Horizon", "setup.exe");
        games.AddFile("Neon Horizon", "GameConfig.exe");
        games.AddFile("Neon Horizon", System.IO.Path.Combine("tools", "CrashReporter.exe"));

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));

        using var response = await client.GetAsync($"/api/games/{candidate.Id}", CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(json);

        response.EnsureSuccessStatusCode();
        var review = document.RootElement;
        Assert.Equal("Neon Horizon", review.GetProperty("provisionalName").GetString());
        var executable = Assert.Single(review.GetProperty("executables").EnumerateArray());
        var executableId = executable.GetProperty("executableId").GetGuid();
        Assert.NotEqual(Guid.Empty, executableId);
        Assert.Equal("NeonHorizon.exe", executable.GetProperty("relativePath").GetString());
        Assert.Equal(executableId, review.GetProperty("recommendedExecutableId").GetGuid());
        Assert.DoesNotContain(games.Path, json, StringComparison.Ordinal);
        Assert.DoesNotContain("setup.exe", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("config", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateLinkThatEscapesTheConfiguredRootIsRejected()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddEscapingDirectoryLink("Escape", "OutsideGame.exe");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));

        using var response = await client.GetAsync($"/api/games/{candidate.Id}", CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Contains("link", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(games.OutsidePath, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewDoesNotFollowNestedLinksOutsideTheCandidateFolder()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");
        games.AddNestedEscapingDirectoryLink("Neon Horizon", "External", "OutsideGame.exe");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));

        using var response = await client.GetAsync($"/api/games/{candidate.Id}", CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(json);

        var executable = Assert.Single(document.RootElement.GetProperty("executables").EnumerateArray());
        Assert.Equal("NeonHorizon.exe", executable.GetProperty("relativePath").GetString());
        Assert.DoesNotContain("OutsideGame", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InaccessibleCandidateReturnsActionableErrorWithoutBreakingTheCandidateList()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Locked Game");
        games.AddGame("Readable Game");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path),
            new InaccessibleReviewScanner());
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidates = await ReadCandidates(client, HttpMethod.Get, "/api/games");

        using var response = await client.GetAsync($"/api/games/{candidates[0].Id}", CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);
        var candidatesAfterFailure = await ReadCandidates(client, HttpMethod.Get, "/api/games");

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Contains("acesso", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(games.Path, problem, StringComparison.Ordinal);
        Assert.Equal(["Locked Game", "Readable Game"], candidatesAfterFailure.Select(candidate => candidate.Name));
    }

    [Fact]
    public async Task GamesEndpointRequiresTheLocalConfigurationToBeReady()
    {
        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(false, false, false)));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var response = await client.GetAsync("/api/games", CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("configura", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateWithoutPlausibleExecutableReturnsActionableError()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Tools Only");
        games.AddFile("Tools Only", "setup.exe");
        games.AddFile("Tools Only", "GameConfig.exe");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));

        using var response = await client.GetAsync($"/api/games/{candidate.Id}", CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Contains("executável", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(games.Path, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiRejectsStaleIdsAndPathsSuppliedInPlaceOfServerGeneratedIds()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var staleCandidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));
        await ReadCandidates(client, HttpMethod.Post, "/api/games/refresh");

        using var staleResponse = await client.GetAsync(
            $"/api/games/{staleCandidate.Id}",
            CancellationToken.None);
        using var pathResponse = await client.GetAsync(
            "/api/games/C:%5CGames%5COutside",
            CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, staleResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, pathResponse.StatusCode);
    }

    [Fact]
    public async Task CandidateRemovedAfterDiscoveryReturnsActionableError()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Temporary Game");
        games.AddFile("Temporary Game", "TemporaryGame.exe");

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path));
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));
        Directory.Delete(System.IO.Path.Combine(games.Path, "Temporary Game"), recursive: true);

        using var response = await client.GetAsync($"/api/games/{candidate.Id}", CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Contains("atualiz", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(games.Path, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MatchesEndpointSearchesTheReviewedProvisionalNameAndReturnsEveryOption()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");
        var steamGridDb = new FakeSteamGridDbClient
        {
            Matches =
            [
                new SteamGridDbGameMatch(42, "Neon Horizon", new Uri("https://cdn.example/neon.png")),
                new SteamGridDbGameMatch(84, "Neon Horizon Remastered", null),
            ],
        };

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path),
            new SystemGameFolderScanner(),
            steamGridDb);
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));

        using var response = await client.GetAsync(
            $"/api/games/{candidate.Id}/matches",
            CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Neon Horizon", steamGridDb.SearchTerm);
        var matches = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal([42L, 84L], matches.Select(match => match.GetProperty("gameId").GetInt64()));
        Assert.Equal(
            ["Neon Horizon", "Neon Horizon Remastered"],
            matches.Select(match => match.GetProperty("officialName").GetString()));
        Assert.DoesNotContain("selected", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitlySelectedMatchReturnsOfficialNameAndAvailableArtworkIndependently()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");
        var steamGridDb = new FakeSteamGridDbClient
        {
            Matches = [new SteamGridDbGameMatch(42, "Neon Horizon", null)],
            Artwork = new SteamGridDbGameArtwork(
                42,
                "Neon Horizon Official",
                ArtworkAsset(10, 20, "vertical"),
                null,
                ArtworkAsset(11, 18, "hero"),
                null,
                null),
        };

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path),
            new SystemGameFolderScanner(),
            steamGridDb);
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));
        using var matchesResponse = await client.GetAsync(
            $"/api/games/{candidate.Id}/matches",
            CancellationToken.None);
        matchesResponse.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            $"/api/games/{candidate.Id}/matches/42/artwork",
            CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(json);
        var artwork = document.RootElement;

        Assert.Equal(42, steamGridDb.SelectedGameId);
        Assert.Equal("Neon Horizon Official", artwork.GetProperty("officialName").GetString());
        Assert.Equal(
            "https://cdn.example/vertical-thumb.png",
            artwork.GetProperty("verticalGrid").GetProperty("previewUrl").GetString());
        Assert.Equal(
            "https://cdn.example/hero-thumb.png",
            artwork.GetProperty("hero").GetProperty("previewUrl").GetString());
        Assert.Equal(JsonValueKind.Null, artwork.GetProperty("horizontalGrid").ValueKind);
        Assert.Equal(JsonValueKind.Null, artwork.GetProperty("logo").ValueKind);
        Assert.Equal(JsonValueKind.Null, artwork.GetProperty("icon").ValueKind);
    }

    [Fact]
    public async Task ArtworkEndpointRejectsAGameThatWasNotOfferedForTheCandidate()
    {
        using var games = TemporaryGamesRoot.Create();
        games.AddGame("Neon Horizon");
        games.AddFile("Neon Horizon", "NeonHorizon.exe");
        var steamGridDb = new FakeSteamGridDbClient
        {
            Matches = [new SteamGridDbGameMatch(42, "Neon Horizon", null)],
        };

        await using var application = SteamImportServer.Build(
            new FixedStatusSource(new SteamImportStatus(true, true, true)),
            new FixedGamesRootSource(games.Path),
            new SystemGameFolderScanner(),
            steamGridDb);
        application.Urls.Add("http://127.0.0.1:0");
        await application.StartAsync(CancellationToken.None);
        var server = application.Services.GetRequiredService<IServer>();
        var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var candidate = Assert.Single(await ReadCandidates(client, HttpMethod.Get, "/api/games"));
        using var matchesResponse = await client.GetAsync(
            $"/api/games/{candidate.Id}/matches",
            CancellationToken.None);
        matchesResponse.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            $"/api/games/{candidate.Id}/matches/84/artwork",
            CancellationToken.None);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("escolha", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Null(steamGridDb.SelectedGameId);
    }

    private static SteamGridDbArtworkAsset ArtworkAsset(long id, int score, string name) =>
        new(
            id,
            score,
            new Uri($"https://cdn.example/{name}.png"),
            new Uri($"https://cdn.example/{name}-thumb.png"));

    private static string ReadName(JsonElement candidate) =>
        candidate.GetProperty("provisionalName").GetString()!;

    private static Guid ReadId(JsonElement candidate) =>
        candidate.GetProperty("candidateId").GetGuid();

    private static async Task<(Guid Id, string Name)[]> ReadCandidates(
        HttpClient client,
        HttpMethod method,
        string path)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await client.SendAsync(request, CancellationToken.None);
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateArray()
            .Select(candidate => (ReadId(candidate), ReadName(candidate)))
            .ToArray();
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
        public IReadOnlyList<SteamGridDbGameMatch> Matches { get; init; } = [];

        public SteamGridDbGameArtwork? Artwork { get; init; }

        public string? SearchTerm { get; private set; }

        public long? SelectedGameId { get; private set; }

        public Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
            string provisionalName,
            CancellationToken cancellationToken)
        {
            SearchTerm = provisionalName;
            return Task.FromResult(Matches);
        }

        public Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
            long gameId,
            CancellationToken cancellationToken)
        {
            SelectedGameId = gameId;
            return Task.FromResult(Artwork ?? throw new NotSupportedException());
        }
    }

    private sealed class InaccessibleReviewScanner : IGameFolderScanner
    {
        private readonly SystemGameFolderScanner discovery = new();

        public IReadOnlyList<GameFolder> FindCandidates(string rootPath) =>
            discovery.FindCandidates(rootPath);

        public IReadOnlyList<string> FindExecutables(string gameFolderPath) =>
            throw new UnauthorizedAccessException("Sensitive filesystem path");
    }

    private sealed class TemporaryGamesRoot : IDisposable
    {
        private readonly List<string> escapingLinkPaths = [];

        private TemporaryGamesRoot(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public string OutsidePath => Path + "-Outside";

        public static TemporaryGamesRoot Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SteamImport-Games-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryGamesRoot(path);
        }

        public void AddGame(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name));

        public void AddNestedFolder(string game, string name) =>
            Directory.CreateDirectory(System.IO.Path.Combine(Path, game, name));

        public void AddFile(string game, string relativePath)
        {
            var path = System.IO.Path.Combine(Path, game, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        public void AddEscapingDirectoryLink(string name, string executableName)
        {
            Directory.CreateDirectory(OutsidePath);
            File.WriteAllBytes(System.IO.Path.Combine(OutsidePath, executableName), []);
            var linkPath = System.IO.Path.Combine(Path, name);
            Directory.CreateSymbolicLink(linkPath, OutsidePath);
            escapingLinkPaths.Add(linkPath);
        }

        public void AddNestedEscapingDirectoryLink(string game, string name, string executableName)
        {
            Directory.CreateDirectory(OutsidePath);
            File.WriteAllBytes(System.IO.Path.Combine(OutsidePath, executableName), []);
            var linkPath = System.IO.Path.Combine(Path, game, name);
            Directory.CreateSymbolicLink(linkPath, OutsidePath);
            escapingLinkPaths.Add(linkPath);
        }

        public void Dispose()
        {
            foreach (var linkPath in escapingLinkPaths)
            {
                if (Directory.Exists(linkPath))
                {
                    Directory.Delete(linkPath);
                }
            }

            Directory.Delete(Path, recursive: true);
            if (Directory.Exists(OutsidePath))
            {
                Directory.Delete(OutsidePath, recursive: true);
            }
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class SteamGridDbClientTests
{
    [Fact]
    public async Task SearchReturnsMatchesWithTheirBestStaticVerticalCover()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            return path.Contains("/search/autocomplete/", StringComparison.Ordinal)
                ? Json("""
                    {"success":true,"data":[{"id":42,"name":"Neon Horizon","types":["game"],"verified":true}]}
                    """)
                : Json("""
                    {"success":true,"data":[
                      {"id":8,"score":3,"url":"https://cdn.example/low.png","thumb":"https://cdn.example/low-thumb.png"},
                      {"id":9,"score":12,"url":"https://cdn.example/best.png","thumb":"https://cdn.example/best-thumb.png"}
                    ]}
                    """);
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var matches = await client.SearchGamesAsync("Neon Horizon: Deluxe", CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal(42, match.GameId);
        Assert.Equal("Neon Horizon", match.OfficialName);
        Assert.Equal(new Uri("https://cdn.example/best-thumb.png"), match.CoverUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/api/v2/search/autocomplete/Neon%20Horizon%3A%20Deluxe", handler.Requests[0].Path);
        Assert.Contains("types=static", handler.Requests[1].Path, StringComparison.Ordinal);
        Assert.Contains("dimensions=600x900%2C342x482%2C660x930", handler.Requests[1].Path, StringComparison.Ordinal);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Authorization?.Scheme);
            Assert.Equal("secret-key", request.Authorization?.Parameter);
            Assert.DoesNotContain("secret-key", request.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SelectedGameReturnsOfficialNameAndHighestScoredStaticArtworkPerCategory()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/games/id/42", StringComparison.Ordinal))
            {
                return Json("""{"success":true,"data":{"id":42,"name":"Neon Horizon"}}""");
            }

            if (path.Contains("dimensions=600x900", StringComparison.Ordinal))
            {
                return Assets((1, 4, "vertical-low"), (2, 20, "vertical-best"));
            }

            if (path.Contains("/grids/game/42", StringComparison.Ordinal))
            {
                return Assets((3, 11, "horizontal"));
            }

            if (path.Contains("/heroes/game/42", StringComparison.Ordinal))
            {
                return Assets((4, 12, "hero"));
            }

            if (path.Contains("/logos/game/42", StringComparison.Ordinal))
            {
                return Assets((5, 13, "logo"));
            }

            return Json("""{"success":true,"data":[]}""");
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var selected = await client.GetRecommendedArtworkAsync(42, CancellationToken.None);

        Assert.Equal("Neon Horizon", selected.OfficialName);
        Assert.Equal("https://cdn.example/vertical-best.png", selected.VerticalGrid!.Url.AbsoluteUri);
        Assert.Equal("https://cdn.example/horizontal.png", selected.HorizontalGrid!.Url.AbsoluteUri);
        Assert.Equal("https://cdn.example/hero.png", selected.Hero!.Url.AbsoluteUri);
        Assert.Equal("https://cdn.example/logo.png", selected.Logo!.Url.AbsoluteUri);
        Assert.Null(selected.Icon);
        Assert.Equal(6, handler.Requests.Count);
        Assert.All(
            handler.Requests.Where(request => !request.Path.Contains("/games/id/", StringComparison.Ordinal)),
            request => Assert.Contains("types=static", request.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticationFailureIsActionableAndDoesNotExposeTheApiKey()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("secret-key is invalid"),
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var error = await Assert.ThrowsAsync<SteamGridDbException>(() =>
            client.SearchGamesAsync("Neon Horizon", CancellationToken.None));

        Assert.Equal(SteamGridDbFailure.Authentication, error.Failure);
        Assert.Contains("chave", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configura", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimitFailureTellsTheUserToRetryLater()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var error = await Assert.ThrowsAsync<SteamGridDbException>(() =>
            client.SearchGamesAsync("Neon Horizon", CancellationToken.None));

        Assert.Equal(SteamGridDbFailure.RateLimited, error.Failure);
        Assert.Contains("aguarde", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("novamente", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServiceFailureIsReportedAsTemporaryUnavailability()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var error = await Assert.ThrowsAsync<SteamGridDbException>(() =>
            client.SearchGamesAsync("Neon Horizon", CancellationToken.None));

        Assert.Equal(SteamGridDbFailure.Unavailable, error.Failure);
        Assert.Contains("indisponível", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("novamente", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutLeakingRequestDetails()
    {
        var handler = new RecordingHandler(_ =>
            throw new HttpRequestException("request with secret-key failed"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        };
        var client = new SteamGridDbClient(httpClient, new FixedApiKeySource("secret-key"));

        var error = await Assert.ThrowsAsync<SteamGridDbException>(() =>
            client.SearchGamesAsync("Neon Horizon", CancellationToken.None));

        Assert.Equal(SteamGridDbFailure.Unavailable, error.Failure);
        Assert.Contains("conexão", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", error.ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Assets(params (long Id, int Score, string Name)[] assets) =>
        Json($$"""
            {"success":true,"data":[
              {{string.Join(',', assets.Select(asset =>
                  $$"""{"id":{{asset.Id}},"score":{{asset.Score}},"url":"https://cdn.example/{{asset.Name}}.png","thumb":"https://cdn.example/{{asset.Name}}-thumb.png"}"""))}}
            ]}
            """);

    private sealed class FixedApiKeySource(string apiKey) : ISteamGridDbApiKeySource
    {
        public string? GetApiKey() => apiKey;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization is null
                    ? null
                    : new AuthenticationHeaderValue(
                        request.Headers.Authorization.Scheme,
                        request.Headers.Authorization.Parameter)));
            return Task.FromResult(respond(request));
        }
    }

    private sealed record RecordedRequest(string Path, AuthenticationHeaderValue? Authorization);
}

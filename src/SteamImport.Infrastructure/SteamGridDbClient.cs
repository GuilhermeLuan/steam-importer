namespace SteamImport.Infrastructure;

using System.Net.Http.Headers;
using System.Text.Json;

public enum SteamGridDbFailure
{
    Authentication,
    RateLimited,
    Unavailable,
    InvalidResponse,
    MissingConfiguration,
}

public sealed class SteamGridDbException(
    SteamGridDbFailure failure,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SteamGridDbFailure Failure { get; } = failure;
}

public interface ISteamGridDbApiKeySource
{
    string? GetApiKey();
}

public sealed record SteamGridDbGameMatch(
    long GameId,
    string OfficialName,
    Uri? CoverUrl);

public sealed record SteamGridDbArtworkAsset(
    long AssetId,
    int Score,
    Uri Url,
    Uri PreviewUrl);

public sealed record SteamGridDbGameArtwork(
    long GameId,
    string OfficialName,
    SteamGridDbArtworkAsset? VerticalGrid,
    SteamGridDbArtworkAsset? HorizontalGrid,
    SteamGridDbArtworkAsset? Hero,
    SteamGridDbArtworkAsset? Logo,
    SteamGridDbArtworkAsset? Icon);

public interface ISteamGridDbClient
{
    Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
        string provisionalName,
        CancellationToken cancellationToken);

    Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
        long gameId,
        CancellationToken cancellationToken);
}

public sealed class SteamGridDbClient(
    HttpClient httpClient,
    ISteamGridDbApiKeySource apiKeySource) : ISteamGridDbClient
{
    private const string VerticalDimensions = "600x900,342x482,660x930";
    private const string HorizontalDimensions = "460x215,920x430";

    public async Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
        string provisionalName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalName);
        var games = await ReadArrayAsync(
            $"search/autocomplete/{Uri.EscapeDataString(provisionalName)}",
            cancellationToken);
        var matches = new List<SteamGridDbGameMatch>();
        foreach (var game in games.EnumerateArray())
        {
            var gameId = game.GetProperty("id").GetInt64();
            var covers = await ReadArrayAsync(
                $"grids/game/{gameId}?types=static&dimensions={Uri.EscapeDataString(VerticalDimensions)}&limit=50",
                cancellationToken);
            var cover = covers
                .EnumerateArray()
                .OrderByDescending(asset => asset.GetProperty("score").GetInt32())
                .ThenBy(asset => asset.GetProperty("id").GetInt64())
                .Select(asset => ReadHttpsUri(asset.GetProperty("thumb").GetString()))
                .FirstOrDefault(uri => uri is not null);
            matches.Add(new SteamGridDbGameMatch(
                gameId,
                game.GetProperty("name").GetString()!,
                cover));
        }

        return matches;
    }

    public async Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
        long gameId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);

        var game = await ReadDataAsync($"games/id/{gameId}", cancellationToken);
        var verticalTask = ReadBestArtworkAsync(
            $"grids/game/{gameId}?types=static&dimensions={Uri.EscapeDataString(VerticalDimensions)}&limit=50",
            cancellationToken);
        var horizontalTask = ReadBestArtworkAsync(
            $"grids/game/{gameId}?types=static&dimensions={Uri.EscapeDataString(HorizontalDimensions)}&limit=50",
            cancellationToken);
        var heroTask = ReadBestArtworkAsync($"heroes/game/{gameId}?types=static&limit=50", cancellationToken);
        var logoTask = ReadBestArtworkAsync($"logos/game/{gameId}?types=static&limit=50", cancellationToken);
        var iconTask = ReadBestArtworkAsync($"icons/game/{gameId}?types=static&limit=50", cancellationToken);

        await Task.WhenAll(verticalTask, horizontalTask, heroTask, logoTask, iconTask);
        return new SteamGridDbGameArtwork(
            gameId,
            game.GetProperty("name").GetString()!,
            await verticalTask,
            await horizontalTask,
            await heroTask,
            await logoTask,
            await iconTask);
    }

    private async Task<SteamGridDbArtworkAsset?> ReadBestArtworkAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var assets = await ReadArrayAsync(relativePath, cancellationToken);
        return assets
            .EnumerateArray()
            .Select(ReadArtwork)
            .Where(asset => asset is not null)
            .OrderByDescending(asset => asset!.Score)
            .ThenBy(asset => asset!.AssetId)
            .FirstOrDefault();
    }

    private static SteamGridDbArtworkAsset? ReadArtwork(JsonElement asset)
    {
        var url = ReadHttpsUri(asset.GetProperty("url").GetString());
        if (url is null)
        {
            return null;
        }

        var preview = ReadHttpsUri(asset.GetProperty("thumb").GetString()) ?? url;
        return new SteamGridDbArtworkAsset(
            asset.GetProperty("id").GetInt64(),
            asset.GetProperty("score").GetInt32(),
            url,
            preview);
    }

    private async Task<JsonElement> ReadArrayAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var data = await ReadDataAsync(relativePath, cancellationToken);
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A API do SteamGridDB devolveu dados inesperados.");
        }

        return data;
    }

    private async Task<JsonElement> ReadDataAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var apiKey = apiKeySource.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SteamGridDbException(
                SteamGridDbFailure.MissingConfiguration,
                "Configure a chave do SteamGridDB no PC-console e tente novamente.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SteamGridDbException(
                SteamGridDbFailure.Unavailable,
                "Não foi possível estabelecer conexão com o SteamGridDB. Verifique a rede e tente novamente.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamGridDbException(
                SteamGridDbFailure.Unavailable,
                "A consulta ao SteamGridDB demorou demais. Verifique a rede e tente novamente.");
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new SteamGridDbException(
                    SteamGridDbFailure.Authentication,
                    "A chave do SteamGridDB foi recusada. Atualize a chave na configuração do PC-console e tente novamente.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new SteamGridDbException(
                    SteamGridDbFailure.RateLimited,
                    "O limite de consultas do SteamGridDB foi atingido. Aguarde um pouco e tente novamente.");
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new SteamGridDbException(
                    SteamGridDbFailure.Unavailable,
                    "O SteamGridDB está temporariamente indisponível. Tente novamente em alguns minutos.");
            }

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(cancellationToken));
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    private static Uri? ReadHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;
}

public sealed class LocalConfigurationSteamGridDbApiKeySource(
    LocalConfigurationStore configurationStore) : ISteamGridDbApiKeySource
{
    public string? GetApiKey()
    {
        try
        {
            return configurationStore.Load()?.SteamGridDbApiKey;
        }
        catch (Exception exception) when (
            exception is InvalidLocalConfigurationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            FormatException)
        {
            return null;
        }
    }
}

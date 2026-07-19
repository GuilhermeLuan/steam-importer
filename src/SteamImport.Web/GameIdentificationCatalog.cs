namespace SteamImport.Web;

using SteamImport.Infrastructure;

public sealed class GameIdentificationCatalog(
    GameCandidateCatalog candidateCatalog,
    ISteamGridDbClient steamGridDbClient)
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, HashSet<long>> searchedMatches = [];

    public async Task<IReadOnlyList<SteamGridDbGameMatch>?> SearchAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        var review = candidateCatalog.GetReview(candidateId);
        if (review is null)
        {
            return null;
        }

        var matches = await steamGridDbClient.SearchGamesAsync(
            review.ProvisionalName,
            cancellationToken);
        lock (sync)
        {
            searchedMatches[candidateId] = matches.Select(match => match.GameId).ToHashSet();
        }

        return matches;
    }

    public async Task<SteamGridDbGameArtwork?> SelectAsync(
        Guid candidateId,
        long gameId,
        CancellationToken cancellationToken)
    {
        if (candidateCatalog.GetReview(candidateId) is null)
        {
            return null;
        }

        lock (sync)
        {
            if (!searchedMatches.TryGetValue(candidateId, out var matches) || !matches.Contains(gameId))
            {
                throw new SteamGridDbMatchNotSearchedException();
            }
        }

        return await steamGridDbClient.GetRecommendedArtworkAsync(gameId, cancellationToken);
    }
}

public sealed class SteamGridDbMatchNotSearchedException : InvalidOperationException;

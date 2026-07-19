namespace SteamImport.Web;

using SteamImport.Core;
using SteamImport.Infrastructure;

public sealed record GameCandidateSummary(
    Guid CandidateId,
    string ProvisionalName);

public sealed record GameExecutableOption(
    Guid ExecutableId,
    string RelativePath);

public sealed record GameCandidateReview(
    Guid CandidateId,
    string ProvisionalName,
    Guid RecommendedExecutableId,
    IReadOnlyList<GameExecutableOption> Executables);

public sealed record ResolvedGameExecutable(
    string GamesRootPath,
    string CandidatePath,
    string ExecutablePath);

public interface IGamesRootSource
{
    string? GetGamesRootPath();
}

public sealed class LocalConfigurationGamesRootSource(LocalConfigurationStore configurationStore)
    : IGamesRootSource
{
    public string? GetGamesRootPath()
    {
        try
        {
            return configurationStore.Load()?.GamesRootPath;
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

public sealed class GamesRootNotConfiguredException : InvalidOperationException
{
}

public sealed class GameCandidateCatalog(
    IGamesRootSource gamesRootSource,
    IGameFolderScanner gameFolderScanner)
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, ReviewedCandidateEntry> reviews = [];
    private CandidateEntry[]? snapshot;

    public IReadOnlyList<GameCandidateSummary> List()
    {
        lock (sync)
        {
            snapshot ??= Discover();
            return Summarize(snapshot);
        }
    }

    public IReadOnlyList<GameCandidateSummary> Refresh()
    {
        lock (sync)
        {
            snapshot = Discover();
            reviews.Clear();
            return Summarize(snapshot);
        }
    }

    public GameCandidateReview? GetReview(Guid candidateId)
    {
        string? path;
        lock (sync)
        {
            snapshot ??= Discover();
            if (reviews.TryGetValue(candidateId, out var existingReview))
            {
                return existingReview.Review;
            }

            path = snapshot
                .SingleOrDefault(candidate => candidate.CandidateId == candidateId)
                ?.Path;
        }

        if (path is null)
        {
            return null;
        }

        var planned = ManualImportPlanner.CreateReview(
            Path.GetFileName(path),
            gameFolderScanner.FindExecutables(path));
        var executables = planned.ExecutableCandidates
            .Select(executablePath => new GameExecutableEntry(
                Guid.NewGuid(),
                executablePath,
                Path.GetRelativePath(path, executablePath)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/')))
            .ToArray();
        var recommended = executables.Single(executable =>
            string.Equals(executable.Path, planned.RecommendedExecutable, StringComparison.OrdinalIgnoreCase));
        var review = new GameCandidateReview(
            candidateId,
            planned.DisplayName,
            recommended.ExecutableId,
            executables
                .Select(executable => new GameExecutableOption(executable.ExecutableId, executable.RelativePath))
                .ToArray());
        lock (sync)
        {
            if (snapshot?.Any(candidate =>
                    candidate.CandidateId == candidateId &&
                    string.Equals(candidate.Path, path, StringComparison.Ordinal)) != true)
            {
                return null;
            }

            reviews[candidateId] = new ReviewedCandidateEntry(path, review, executables);
        }

        return review;
    }

    public ResolvedGameExecutable? ResolveExecutable(Guid candidateId, Guid executableId)
    {
        lock (sync)
        {
            if (!reviews.TryGetValue(candidateId, out var reviewed))
            {
                return null;
            }

            var executable = reviewed.Executables.SingleOrDefault(item => item.ExecutableId == executableId);
            if (executable is null || !File.Exists(executable.Path))
            {
                return null;
            }

            return new ResolvedGameExecutable(
                Path.GetDirectoryName(reviewed.CandidatePath)!,
                reviewed.CandidatePath,
                executable.Path);
        }
    }

    private CandidateEntry[] Discover()
    {
        var rootPath = gamesRootSource.GetGamesRootPath();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new GamesRootNotConfiguredException();
        }

        return gameFolderScanner
            .FindCandidates(rootPath)
            .Select(folder => new CandidateEntry(
                Guid.NewGuid(),
                folder.Name,
                folder.Path))
            .OrderBy(candidate => candidate.ProvisionalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GameCandidateSummary[] Summarize(IEnumerable<CandidateEntry> entries) =>
        entries
            .Select(entry => new GameCandidateSummary(entry.CandidateId, entry.ProvisionalName))
            .ToArray();

    private sealed record CandidateEntry(
        Guid CandidateId,
        string ProvisionalName,
        string Path);

    private sealed record GameExecutableEntry(
        Guid ExecutableId,
        string Path,
        string RelativePath);

    private sealed record ReviewedCandidateEntry(
        string CandidatePath,
        GameCandidateReview Review,
        IReadOnlyList<GameExecutableEntry> Executables);
}

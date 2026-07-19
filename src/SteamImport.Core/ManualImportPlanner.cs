namespace SteamImport.Core;

public static class ManualImportPlanner
{
    private static readonly string[] ExcludedExecutableTerms =
    [
        "unins",
        "uninstall",
        "setup",
        "installer",
        "config",
        "crashreporter",
        "crashhandler",
        "vcredist",
        "dxsetup",
    ];

    public static ManualImportReview CreateReview(string gameFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);

        var fullPath = Path.GetFullPath(gameFolder);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Game folder was not found: {fullPath}");
        }

        var displayName = new DirectoryInfo(fullPath).Name;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return CreateReview(
            displayName,
            Directory.EnumerateFiles(fullPath, "*.exe", enumerationOptions));
    }

    public static ManualImportReview CreateReview(
        string displayName,
        IEnumerable<string> executablePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(executablePaths);
        var candidates = executablePaths
            .Where(IsGameExecutable)
            .OrderByDescending(path => SimilarityScore(displayName, path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new NoGameExecutableException();
        }

        return new ManualImportReview(displayName, candidates[0], candidates);
    }

    private static int SimilarityScore(string displayName, string executablePath)
    {
        var normalizedName = Normalize(displayName);
        var normalizedExecutable = Normalize(Path.GetFileNameWithoutExtension(executablePath));
        return string.Equals(normalizedName, normalizedExecutable, StringComparison.Ordinal) ? 1 : 0;
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static bool IsGameExecutable(string path)
    {
        var fileName = Normalize(Path.GetFileNameWithoutExtension(path));
        return !ExcludedExecutableTerms.Any(
            term => fileName.Contains(Normalize(term), StringComparison.Ordinal));
    }
}

public sealed record ManualImportReview(
    string DisplayName,
    string RecommendedExecutable,
    IReadOnlyList<string> ExecutableCandidates);

public sealed class NoGameExecutableException()
    : InvalidOperationException("The selected game folder does not contain an executable candidate.")
{
}

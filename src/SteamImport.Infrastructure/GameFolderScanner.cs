namespace SteamImport.Infrastructure;

public sealed record GameFolder(
    string Name,
    string Path);

public interface IGameFolderScanner
{
    IReadOnlyList<GameFolder> FindCandidates(string rootPath);

    IReadOnlyList<string> FindExecutables(string gameFolderPath);
}

public sealed class SystemGameFolderScanner : IGameFolderScanner
{
    private static readonly EnumerationOptions CandidateOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    private static readonly EnumerationOptions ExecutableOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public IReadOnlyList<GameFolder> FindCandidates(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return Directory
            .EnumerateDirectories(rootPath, "*", CandidateOptions)
            .Select(path => new GameFolder(Path.GetFileName(path), path))
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> FindExecutables(string gameFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolderPath);
        if ((File.GetAttributes(gameFolderPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafeGameFolderException(
                "O candidato é um link ou junção e não pode ser revisado. Mova o jogo para dentro da pasta raiz configurada.");
        }

        return Directory
            .EnumerateFiles(gameFolderPath, "*.exe", ExecutableOptions)
            .ToArray();
    }
}

public sealed class UnsafeGameFolderException(string message) : InvalidOperationException(message);

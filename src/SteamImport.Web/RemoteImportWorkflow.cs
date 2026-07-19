namespace SteamImport.Web;

using SteamImport.Infrastructure;

public sealed record RemoteImportContext(
    string SteamExecutablePath,
    string ShortcutsPath,
    string GridDirectory,
    string BackupDirectory);

public interface IRemoteImportContextSource
{
    RemoteImportContext? GetContext();
}

public sealed class LocalConfigurationRemoteImportContextSource(
    LocalConfigurationStore configurationStore) : IRemoteImportContextSource
{
    public RemoteImportContext? GetContext()
    {
        var configuration = configurationStore.Load();
        if (configuration is null)
        {
            return null;
        }

        var installation = SteamInstallation.Open(configuration.SteamRootPath);
        var account = installation.Accounts.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, configuration.SteamAccountId, StringComparison.Ordinal));
        if (account is null)
        {
            return null;
        }

        return new RemoteImportContext(
            Path.Combine(installation.RootPath, "steam.exe"),
            account.ShortcutsPath,
            Path.Combine(Path.GetDirectoryName(account.ShortcutsPath)!, "grid"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamImport",
                "Backups",
                account.Id));
    }
}

public sealed record RemoteImportCommand(
    Guid CandidateId,
    Guid ExecutableId,
    long GameId,
    string DisplayName);

public sealed class RemoteImportWorkflow(
    GameCandidateCatalog candidateCatalog,
    GameIdentificationCatalog identificationCatalog,
    IRemoteImportContextSource contextSource,
    IRemoteGameImporter importer)
{
    public Task<RemoteGameImportResult> ImportAsync(
        RemoteImportCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var displayName = command.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidRemoteImportRequestException("Informe o nome que será exibido na Steam.");
        }

        var executable = candidateCatalog.ResolveExecutable(
            command.CandidateId,
            command.ExecutableId);
        var artwork = identificationCatalog.GetSelection(
            command.CandidateId,
            command.GameId);
        RemoteImportContext? context;
        try
        {
            context = contextSource.GetContext();
        }
        catch (Exception exception) when (
            exception is InvalidLocalConfigurationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            FormatException or
            InvalidOperationException)
        {
            throw new RemoteImportReviewNotReadyException();
        }
        if (executable is null || artwork is null || context is null)
        {
            throw new RemoteImportReviewNotReadyException();
        }

        return importer.ImportAsync(
            new RemoteGameImportRequest(
                context.SteamExecutablePath,
                executable.GamesRootPath,
                context.ShortcutsPath,
                context.GridDirectory,
                context.BackupDirectory,
                displayName,
                executable.ExecutablePath,
                ReadArtwork(artwork)),
            cancellationToken);
    }

    private static RemoteArtworkSelection[] ReadArtwork(SteamGridDbGameArtwork artwork)
    {
        var selections = new List<RemoteArtworkSelection>(5);
        Add(selections, SteamArtworkKind.VerticalGrid, artwork.VerticalGrid);
        Add(selections, SteamArtworkKind.HorizontalGrid, artwork.HorizontalGrid);
        Add(selections, SteamArtworkKind.Hero, artwork.Hero);
        Add(selections, SteamArtworkKind.Logo, artwork.Logo);
        Add(selections, SteamArtworkKind.Icon, artwork.Icon);
        return [.. selections];
    }

    private static void Add(
        List<RemoteArtworkSelection> selections,
        SteamArtworkKind kind,
        SteamGridDbArtworkAsset? artwork)
    {
        if (artwork is not null)
        {
            selections.Add(new RemoteArtworkSelection(kind, artwork.Url));
        }
    }
}

public sealed class RemoteImportReviewNotReadyException : InvalidOperationException
{
    public RemoteImportReviewNotReadyException()
        : base("A revisão expirou ou não está completa. Revise o jogo e escolha o título novamente.")
    {
    }
}

public sealed class InvalidRemoteImportRequestException(string message) : ArgumentException(message);

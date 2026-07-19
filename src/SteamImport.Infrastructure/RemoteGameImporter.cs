using SteamImport.Core;

namespace SteamImport.Infrastructure;

public enum SteamArtworkKind
{
    HorizontalGrid,
    VerticalGrid,
    Hero,
    Logo,
    Icon,
}

public sealed record RemoteArtworkSelection(
    SteamArtworkKind Kind,
    Uri Url);

public sealed record RemoteGameImportRequest(
    string SteamExecutablePath,
    string GamesRootPath,
    string ShortcutsPath,
    string GridDirectory,
    string BackupDirectory,
    string DisplayName,
    string ExecutablePath,
    IReadOnlyList<RemoteArtworkSelection> Artworks);

public sealed record RemoteGameImportResult(
    SteamShortcut Shortcut,
    bool SteamWasRunning,
    bool SteamRestarted);

public interface ISteamClientController
{
    bool IsRunning();

    Task RequestShutdownAsync(string steamExecutablePath, CancellationToken cancellationToken);

    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    Task StartBigPictureAsync(string steamExecutablePath, CancellationToken cancellationToken);
}

public interface IGameActivityProbe
{
    bool IsGameRunning(string gamesRootPath, string steamRootPath);
}

public interface IRemoteGameImporter
{
    Task<RemoteGameImportResult> ImportAsync(
        RemoteGameImportRequest request,
        CancellationToken cancellationToken);
}

public sealed class RemoteGameImporter(
    ISteamClientController steamClient,
    IGameActivityProbe gameActivityProbe,
    HttpClient artworkHttpClient,
    IAppLog? appLog = null,
    TimeSpan? artworkDownloadTimeout = null) : IRemoteGameImporter, IDisposable
{
    private const int MaximumArtworkBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan SteamShutdownTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] ArtworkExtensions = [".png", ".jpg", ".jpeg", ".ico"];
    private readonly SemaphoreSlim importGate = new(1, 1);
    private readonly TimeSpan downloadTimeout = artworkDownloadTimeout ?? TimeSpan.FromSeconds(15);

    public async Task<RemoteGameImportResult> ImportAsync(
        RemoteGameImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await importGate.WaitAsync(0, cancellationToken))
        {
            throw new ImportAlreadyInProgressException();
        }

        try
        {
            return await ImportExclusiveAsync(request, cancellationToken);
        }
        finally
        {
            importGate.Release();
        }
    }

    private async Task<RemoteGameImportResult> ImportExclusiveAsync(
        RemoteGameImportRequest request,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"SteamImport-{Guid.NewGuid():N}");

        try
        {
            var stagedArtwork = await StageArtworkAsync(
                request.Artworks,
                temporaryDirectory,
                cancellationToken);
            var steamRootPath = Path.GetDirectoryName(request.SteamExecutablePath) ?? string.Empty;
            if (gameActivityProbe.IsGameRunning(request.GamesRootPath, steamRootPath))
            {
                throw new GameIsRunningException();
            }

            var steamWasRunning = steamClient.IsRunning();
            var steamStopped = false;
            if (steamWasRunning)
            {
                await steamClient.RequestShutdownAsync(request.SteamExecutablePath, cancellationToken);
                steamStopped = await steamClient.WaitForExitAsync(
                    SteamShutdownTimeout,
                    CancellationToken.None);
                if (!steamStopped)
                {
                    throw new SteamShutdownTimedOutException();
                }
            }

            try
            {
                var appId = SteamShortcutAppId.Calculate(
                    request.ExecutablePath.Trim().Trim('"'),
                    request.DisplayName.Trim());
                BackupArtwork(
                    stagedArtwork,
                    request.GridDirectory,
                    request.BackupDirectory,
                    appId);
                var shortcut = new ManualGameImporter(
                    new ControllerSteamProcessProbe(steamClient),
                    appLog).Import(
                    new ManualGameImportRequest(
                        request.ShortcutsPath,
                        request.BackupDirectory,
                        request.DisplayName,
                        request.ExecutablePath));
                InstallArtwork(stagedArtwork, request.GridDirectory, shortcut.AppId);
                return new RemoteGameImportResult(shortcut, steamWasRunning, SteamRestarted: steamWasRunning);
            }
            finally
            {
                if (steamWasRunning && steamStopped)
                {
                    await steamClient.StartBigPictureAsync(request.SteamExecutablePath, CancellationToken.None);
                }
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private async Task<IReadOnlyList<StagedArtwork>> StageArtworkAsync(
        IReadOnlyList<RemoteArtworkSelection> selections,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (selections.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(temporaryDirectory);
        var staged = new List<StagedArtwork>(selections.Count);
        foreach (var selection in selections)
        {
            if (selection.Url.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidArtworkException("A arte selecionada não usa uma conexão HTTPS segura.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(downloadTimeout);
            try
            {
                using var response = await DownloadArtworkAsync(selection.Url, timeoutSource.Token);
                if (response.Content.Headers.ContentLength is > MaximumArtworkBytes)
                {
                    throw new InvalidArtworkException("A arte selecionada excede o tamanho permitido.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                using var bytes = new MemoryStream();
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, timeoutSource.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    if (bytes.Length + read > MaximumArtworkBytes)
                    {
                        throw new InvalidArtworkException("A arte selecionada excede o tamanho permitido.");
                    }

                    bytes.Write(buffer, 0, read);
                }

                var contents = bytes.ToArray();
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var extension = IsPng(contents) && string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase)
                    ? ".png"
                    : IsJpeg(contents) && string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                        ? ".jpg"
                        : throw new InvalidArtworkException("A arte selecionada não é uma imagem PNG ou JPEG válida.");

                var path = Path.Combine(temporaryDirectory, $"{selection.Kind}-{Guid.NewGuid():N}{extension}");
                await File.WriteAllBytesAsync(path, contents, timeoutSource.Token);
                staged.Add(new StagedArtwork(selection.Kind, path, extension));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidArtworkException(
                    "O download de uma das artes demorou demais. Verifique a rede e tente novamente.");
            }
        }

        return staged;
    }

    private async Task<HttpResponseMessage> DownloadArtworkAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await artworkHttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new InvalidArtworkException(
                    "Não foi possível baixar uma das artes selecionadas. Tente novamente.");
            }

            return response;
        }
        catch (HttpRequestException)
        {
            throw new InvalidArtworkException(
                "Não foi possível baixar uma das artes selecionadas. Verifique a rede e tente novamente.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidArtworkException(
                "O download de uma das artes demorou demais. Verifique a rede e tente novamente.");
        }
    }

    private static bool IsPng(ReadOnlySpan<byte> contents) =>
        contents.Length >= 8 &&
        contents[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static bool IsJpeg(ReadOnlySpan<byte> contents) =>
        contents.Length >= 3 &&
        contents[0] == 0xFF &&
        contents[1] == 0xD8 &&
        contents[2] == 0xFF;

    private static void InstallArtwork(
        IReadOnlyList<StagedArtwork> stagedArtwork,
        string gridDirectory,
        uint appId)
    {
        if (stagedArtwork.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(gridDirectory);
        foreach (var artwork in stagedArtwork)
        {
            var destinationPath = Path.Combine(gridDirectory, GetArtworkFileName(artwork, appId));
            var temporaryPath = Path.Combine(
                gridDirectory,
                $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var source = File.OpenRead(artwork.Path))
                using (var destination = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 81920,
                           FileOptions.WriteThrough))
                {
                    source.CopyTo(destination);
                    destination.Flush(flushToDisk: true);
                }

                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }

                foreach (var obsoletePath in EnumerateArtworkVariants(gridDirectory, artwork.Kind, appId)
                             .Where(path => !string.Equals(path, destinationPath, StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(obsoletePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private static void BackupArtwork(
        IReadOnlyList<StagedArtwork> stagedArtwork,
        string gridDirectory,
        string backupDirectory,
        uint appId)
    {
        string? artworkBackupDirectory = null;
        foreach (var artwork in stagedArtwork)
        {
            foreach (var existingPath in EnumerateArtworkVariants(gridDirectory, artwork.Kind, appId))
            {
                artworkBackupDirectory ??= Path.Combine(
                    backupDirectory,
                    $"artwork-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfffffff}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(artworkBackupDirectory);
                File.Copy(existingPath, Path.Combine(artworkBackupDirectory, Path.GetFileName(existingPath)));
            }
        }
    }

    private static IEnumerable<string> EnumerateArtworkVariants(
        string gridDirectory,
        SteamArtworkKind kind,
        uint appId)
    {
        if (!Directory.Exists(gridDirectory))
        {
            yield break;
        }

        var stem = GetArtworkStem(kind, appId);
        foreach (var extension in ArtworkExtensions)
        {
            var path = Path.Combine(gridDirectory, $"{stem}{extension}");
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static string GetArtworkFileName(StagedArtwork artwork, uint appId) =>
        $"{GetArtworkStem(artwork.Kind, appId)}{artwork.Extension}";

    private static string GetArtworkStem(SteamArtworkKind kind, uint appId)
    {
        var suffix = kind switch
        {
            SteamArtworkKind.HorizontalGrid => string.Empty,
            SteamArtworkKind.VerticalGrid => "p",
            SteamArtworkKind.Hero => "_hero",
            SteamArtworkKind.Logo => "_logo",
            SteamArtworkKind.Icon => "_icon",
            _ => throw new InvalidOperationException("Unknown Steam artwork kind."),
        };
        return $"{appId}{suffix}";
    }

    private sealed record StagedArtwork(
        SteamArtworkKind Kind,
        string Path,
        string Extension);

    private sealed class ControllerSteamProcessProbe(ISteamClientController controller)
        : ISteamProcessProbe
    {
        public bool IsRunning() => controller.IsRunning();
    }

    public void Dispose() => importGate.Dispose();
}

public sealed class InvalidArtworkException(string message) : IOException(message);

public sealed class SteamShutdownTimedOutException : TimeoutException
{
    public SteamShutdownTimedOutException()
        : base("A Steam não encerrou dentro do tempo esperado. Feche jogos e diálogos abertos e tente novamente.")
    {
    }
}

public sealed class ImportAlreadyInProgressException : InvalidOperationException
{
    public ImportAlreadyInProgressException()
        : base("Outra importação já está em andamento. Aguarde a conclusão e tente novamente.")
    {
    }
}

public sealed class GameIsRunningException : InvalidOperationException
{
    public GameIsRunningException()
        : base("Há um jogo em execução no PC-console. Feche-o e tente novamente.")
    {
    }
}

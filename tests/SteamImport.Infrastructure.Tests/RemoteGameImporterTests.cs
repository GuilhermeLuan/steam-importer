using SteamImport.Core;
using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class RemoteGameImporterTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task ClosedSteamCompletesRemoteImportWithoutOpeningTheClient()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var existing = new SteamShortcut(
            0x80000001,
            "Existing Game",
            @"D:\Existing\game.exe",
            @"D:\Existing");
        SteamShortcutStore.Add(shortcutsPath, existing);
        var steamClient = new ClosedSteamClient();
        using var httpClient = new HttpClient(new RejectingHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            var result = await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    []),
                CancellationToken.None);

            Assert.False(result.SteamWasRunning);
            Assert.False(result.SteamRestarted);
            Assert.Equal([existing, result.Shortcut], SteamShortcutStore.ReadAll(shortcutsPath));
            Assert.False(steamClient.BigPictureStarted);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SelectedArtworkIsValidatedAndInstalledWithTheShortcutAppId()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var gridDirectory = System.IO.Path.Combine(root, "userdata", "1", "config", "grid");
        using var httpClient = new HttpClient(new ArtworkHandler(OnePixelPng, "image/png"));
        var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            var result = await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf"),
                    gridDirectory,
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/vertical"))]),
                CancellationToken.None);

            var installed = System.IO.Path.Combine(gridDirectory, $"{result.Shortcut.AppId}p.png");
            Assert.Equal(OnePixelPng, await File.ReadAllBytesAsync(installed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingArtworkIsBackedUpBeforeItIsReplaced()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var gridDirectory = System.IO.Path.Combine(root, "userdata", "1", "config", "grid");
        var backupDirectory = System.IO.Path.Combine(root, "Backups");
        var displayName = "Neon Horizon";
        var executablePath = @"C:\Games\Neon Horizon\NeonHorizon.exe";
        var appId = SteamShortcutAppId.Calculate(executablePath, displayName);
        Directory.CreateDirectory(gridDirectory);
        var artworkPath = System.IO.Path.Combine(gridDirectory, $"{appId}p.png");
        var previousArtwork = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(artworkPath, previousArtwork);
        using var httpClient = new HttpClient(new ArtworkHandler(OnePixelPng, "image/png"));
        var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    gridDirectory,
                    backupDirectory,
                    displayName,
                    executablePath,
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/vertical"))]),
                CancellationToken.None);

            var backup = Assert.Single(Directory.EnumerateFiles(
                backupDirectory,
                $"{appId}p.png",
                SearchOption.AllDirectories));
            Assert.Equal(previousArtwork, await File.ReadAllBytesAsync(backup));
            Assert.Equal(OnePixelPng, await File.ReadAllBytesAsync(artworkPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunningSteamIsClosedNormallyAndReopenedInBigPictureAfterImport()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var steamClient = new RunningSteamClient();
        using var httpClient = new HttpClient(new RejectingHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            var result = await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    []),
                CancellationToken.None);

            Assert.True(result.SteamWasRunning);
            Assert.True(result.SteamRestarted);
            Assert.True(steamClient.IsRunning());
            Assert.True(steamClient.ShutdownRequested);
            Assert.True(steamClient.BigPictureStarted);
            Assert.Single(SteamShortcutStore.ReadAll(shortcutsPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentImportIsRejectedBeforeStartingMoreWork()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var handler = new BlockingFirstArtworkHandler(OnePixelPng);
        using var httpClient = new HttpClient(handler);
        var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            httpClient);
        RemoteGameImportRequest Request(string name) => new(
            System.IO.Path.Combine(root, "steam.exe"),
            System.IO.Path.Combine(root, "Games"),
            System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf"),
            System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
            System.IO.Path.Combine(root, "Backups"),
            name,
            $@"C:\Games\{name}\game.exe",
            [new RemoteArtworkSelection(
                SteamArtworkKind.VerticalGrid,
                new Uri("https://cdn.example/vertical"))]);

        try
        {
            var firstImport = importer.ImportAsync(Request("First Game"), CancellationToken.None);
            await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Exception? secondError = null;
            try
            {
                await importer.ImportAsync(Request("Second Game"), CancellationToken.None);
            }
            catch (Exception exception)
            {
                secondError = exception;
            }
            finally
            {
                handler.ReleaseFirstRequest.SetResult();
            }

            await firstImport;
            Assert.IsType<ImportAlreadyInProgressException>(secondError);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SteamShutdownTimeoutLeavesTheLibraryUntouchedAndNeverForcesOrRestartsTheClient()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var steamClient = new TimedOutSteamClient();
        using var httpClient = new HttpClient(new RejectingHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient);

        var error = await Assert.ThrowsAsync<SteamShutdownTimedOutException>(() =>
            importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    []),
                CancellationToken.None));

        Assert.Contains("não encerrou", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(steamClient.IsRunning());
        Assert.True(steamClient.ShutdownRequested);
        Assert.False(steamClient.BigPictureStarted);
        Assert.False(File.Exists(shortcutsPath));
    }

    [Fact]
    public async Task RunningGameBlocksTheImportWithAnActionableMessage()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var steamClient = new ClosedSteamClient();
        using var httpClient = new HttpClient(new RejectingHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new RunningGameProbe(),
            httpClient);

        var error = await Assert.ThrowsAsync<GameIsRunningException>(() =>
            importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    []),
                CancellationToken.None));

        Assert.Contains("jogo em execução", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(steamClient.BigPictureStarted);
        Assert.False(File.Exists(shortcutsPath));
    }

    [Fact]
    public async Task InvalidSelectedArtworkFailsBeforeSteamIsClosedOrTheLibraryIsChanged()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var steamClient = new RunningSteamClient();
        using var httpClient = new HttpClient(new ArtworkHandler([1, 2, 3], "image/png"));
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient);

        var error = await Assert.ThrowsAsync<InvalidArtworkException>(() =>
            importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/not-an-image"))]),
                CancellationToken.None));

        Assert.Contains("imagem", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(steamClient.ShutdownRequested);
        Assert.True(steamClient.IsRunning());
        Assert.False(File.Exists(shortcutsPath));
    }

    [Fact]
    public async Task JpegArtworkIsInstalledUsingItsValidatedFormat()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var gridDirectory = System.IO.Path.Combine(root, "userdata", "1", "config", "grid");
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        using var httpClient = new HttpClient(new ArtworkHandler(jpeg, "image/jpeg"));
        var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            var result = await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf"),
                    gridDirectory,
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.Hero,
                        new Uri("https://cdn.example/hero"))]),
                CancellationToken.None);

            var installed = System.IO.Path.Combine(gridDirectory, $"{result.Shortcut.AppId}_hero.jpg");
            Assert.Equal(jpeg, await File.ReadAllBytesAsync(installed));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReplacingArtworkBacksUpAndRemovesThePreviousFormatVariant()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var shortcutsPath = System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf");
        var gridDirectory = System.IO.Path.Combine(root, "userdata", "1", "config", "grid");
        var backupDirectory = System.IO.Path.Combine(root, "Backups");
        var displayName = "Neon Horizon";
        var executablePath = @"C:\Games\Neon Horizon\NeonHorizon.exe";
        var appId = SteamShortcutAppId.Calculate(executablePath, displayName);
        Directory.CreateDirectory(gridDirectory);
        var previousPath = System.IO.Path.Combine(gridDirectory, $"{appId}p.jpg");
        var previousArtwork = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        await File.WriteAllBytesAsync(previousPath, previousArtwork);
        using var httpClient = new HttpClient(new ArtworkHandler(OnePixelPng, "image/png"));
        var importer = new RemoteGameImporter(
            new ClosedSteamClient(),
            new NoRunningGameProbe(),
            httpClient);

        try
        {
            await importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    shortcutsPath,
                    gridDirectory,
                    backupDirectory,
                    displayName,
                    executablePath,
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/vertical"))]),
                CancellationToken.None);

            var backup = Assert.Single(Directory.EnumerateFiles(
                backupDirectory,
                $"{appId}p.jpg",
                SearchOption.AllDirectories));
            Assert.Equal(previousArtwork, await File.ReadAllBytesAsync(backup));
            Assert.False(File.Exists(previousPath));
            Assert.True(File.Exists(System.IO.Path.Combine(gridDirectory, $"{appId}p.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkDownloadFailureIsActionableAndHappensBeforeSteamShutdown()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var steamClient = new RunningSteamClient();
        using var httpClient = new HttpClient(new FailingArtworkHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient);

        var error = await Assert.ThrowsAsync<InvalidArtworkException>(() =>
            importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/unavailable"))]),
                CancellationToken.None));

        Assert.Contains("baixar", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(steamClient.ShutdownRequested);
        Assert.DoesNotContain("cdn.example", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArtworkBodyTimeoutFailsBeforeSteamShutdown()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Remote-{Guid.NewGuid():N}");
        var steamClient = new RunningSteamClient();
        using var httpClient = new HttpClient(new HangingArtworkHandler());
        var importer = new RemoteGameImporter(
            steamClient,
            new NoRunningGameProbe(),
            httpClient,
            artworkDownloadTimeout: TimeSpan.FromMilliseconds(20));

        var error = await Assert.ThrowsAsync<InvalidArtworkException>(() =>
            importer.ImportAsync(
                new RemoteGameImportRequest(
                    System.IO.Path.Combine(root, "steam.exe"),
                    System.IO.Path.Combine(root, "Games"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "shortcuts.vdf"),
                    System.IO.Path.Combine(root, "userdata", "1", "config", "grid"),
                    System.IO.Path.Combine(root, "Backups"),
                    "Neon Horizon",
                    @"C:\Games\Neon Horizon\NeonHorizon.exe",
                    [new RemoteArtworkSelection(
                        SteamArtworkKind.VerticalGrid,
                        new Uri("https://cdn.example/hanging"))]),
                CancellationToken.None));

        Assert.Contains("demorou", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(steamClient.ShutdownRequested);
    }

    private sealed class ClosedSteamClient : ISteamClientController
    {
        public bool BigPictureStarted { get; private set; }

        public bool IsRunning() => false;

        public Task RequestShutdownAsync(string steamExecutablePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A closed Steam client must not be shut down.");

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A closed Steam client must not be awaited.");

        public Task StartBigPictureAsync(string steamExecutablePath, CancellationToken cancellationToken)
        {
            BigPictureStarted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class NoRunningGameProbe : IGameActivityProbe
    {
        public bool IsGameRunning(string gamesRootPath, string steamRootPath) => false;
    }

    private sealed class RunningGameProbe : IGameActivityProbe
    {
        public bool IsGameRunning(string gamesRootPath, string steamRootPath) => true;
    }

    private sealed class RunningSteamClient : ISteamClientController
    {
        private bool isRunning = true;

        public bool ShutdownRequested { get; private set; }

        public bool BigPictureStarted { get; private set; }

        public bool IsRunning() => isRunning;

        public Task RequestShutdownAsync(string steamExecutablePath, CancellationToken cancellationToken)
        {
            ShutdownRequested = true;
            return Task.CompletedTask;
        }

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            isRunning = false;
            return Task.FromResult(true);
        }

        public Task StartBigPictureAsync(string steamExecutablePath, CancellationToken cancellationToken)
        {
            BigPictureStarted = true;
            isRunning = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TimedOutSteamClient : ISteamClientController
    {
        public bool ShutdownRequested { get; private set; }

        public bool BigPictureStarted { get; private set; }

        public bool IsRunning() => true;

        public Task RequestShutdownAsync(string steamExecutablePath, CancellationToken cancellationToken)
        {
            ShutdownRequested = true;
            return Task.CompletedTask;
        }

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task StartBigPictureAsync(string steamExecutablePath, CancellationToken cancellationToken)
        {
            BigPictureStarted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No artwork should be downloaded.");
    }

    private sealed class ArtworkHandler(byte[] content, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingFirstArtworkHandler(byte[] content) : HttpMessageHandler
    {
        private int requestCount;

        public TaskCompletionSource FirstRequestStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRequest { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 1)
            {
                FirstRequestStarted.SetResult();
                await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        }
    }

    private sealed class FailingArtworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    private sealed class HangingArtworkHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}

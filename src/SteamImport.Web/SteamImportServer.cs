using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SteamImport.Core;
using SteamImport.Infrastructure;

namespace SteamImport.Web;

public sealed record SteamImportStatus(
    bool ConfigurationReady,
    bool SteamReady,
    bool AccountReady)
{
    public bool Ready => ConfigurationReady && SteamReady && AccountReady;
}

public interface IStatusSource
{
    SteamImportStatus GetStatus();
}

public sealed class LocalConfigurationStatusSource(LocalConfigurationStore configurationStore) : IStatusSource
{
    public SteamImportStatus GetStatus()
    {
        LocalConfiguration? configuration;
        try
        {
            configuration = configurationStore.Load();
        }
        catch (Exception exception) when (
            exception is InvalidLocalConfigurationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            FormatException)
        {
            return new SteamImportStatus(false, false, false);
        }

        if (configuration is null)
        {
            return new SteamImportStatus(false, false, false);
        }

        var configurationReady =
            Directory.Exists(configuration.GamesRootPath) &&
            !string.IsNullOrWhiteSpace(configuration.SteamGridDbApiKey);
        try
        {
            var installation = SteamInstallation.Open(configuration.SteamRootPath);
            var accountReady = installation.Accounts.Any(account =>
                string.Equals(account.Id, configuration.SteamAccountId, StringComparison.Ordinal));
            return new SteamImportStatus(configurationReady, true, accountReady);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new SteamImportStatus(configurationReady, false, false);
        }
    }
}

public static class SteamImportServer
{
    public const int Port = 5050;

    public static WebApplication Build(IStatusSource statusSource) =>
        Build(
            statusSource,
            new MissingGamesRootSource(),
            new SystemGameFolderScanner(),
            new MissingSteamGridDbClient());

    public static WebApplication Build(
        IStatusSource statusSource,
        IGamesRootSource gamesRootSource) =>
        Build(
            statusSource,
            gamesRootSource,
            new SystemGameFolderScanner(),
            new MissingSteamGridDbClient());

    public static WebApplication Build(
        IStatusSource statusSource,
        IGamesRootSource gamesRootSource,
        IGameFolderScanner gameFolderScanner) =>
        Build(
            statusSource,
            gamesRootSource,
            gameFolderScanner,
            new MissingSteamGridDbClient());

    public static WebApplication Build(
        IStatusSource statusSource,
        IGamesRootSource gamesRootSource,
        IGameFolderScanner gameFolderScanner,
        ISteamGridDbClient steamGridDbClient)
    {
        ArgumentNullException.ThrowIfNull(statusSource);
        ArgumentNullException.ThrowIfNull(gamesRootSource);
        ArgumentNullException.ThrowIfNull(gameFolderScanner);
        ArgumentNullException.ThrowIfNull(steamGridDbClient);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(statusSource);
        builder.Services.AddSingleton(gamesRootSource);
        builder.Services.AddSingleton(gameFolderScanner);
        builder.Services.AddSingleton(steamGridDbClient);
        builder.Services.AddSingleton<GameCandidateCatalog>();
        builder.Services.AddSingleton<GameIdentificationCatalog>();

        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' https:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            await next(context);
        });
        application.MapGet("/", () => Results.Content(StatusPage.Html, "text/html; charset=utf-8"));
        application.MapGet("/app.css", () => Results.Content(StatusPage.Css, "text/css; charset=utf-8"));
        application.MapGet("/app.js", () => Results.Content(StatusPage.JavaScript, "text/javascript; charset=utf-8"));
        application.MapGet("/api/status", (HttpContext context, IStatusSource source) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return source.GetStatus();
        });
        application.MapGet("/api/games", (HttpContext context, GameCandidateCatalog catalog) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return ReadCandidateList(catalog.List);
        });
        application.MapPost("/api/games/refresh", (HttpContext context, GameCandidateCatalog catalog) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return ReadCandidateList(catalog.Refresh);
        });
        application.MapGet(
            "/api/games/{candidateId:guid}/matches",
            async (Guid candidateId, HttpContext context, GameIdentificationCatalog catalog, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var matches = await catalog.SearchAsync(candidateId, cancellationToken);
                    return matches is null
                        ? Results.NotFound()
                        : Results.Ok(matches);
                }
                catch (SteamGridDbException exception)
                {
                    return SteamGridDbProblem(exception);
                }
            });
        application.MapGet(
            "/api/games/{candidateId:guid}/matches/{gameId:long}/artwork",
            async (Guid candidateId, long gameId, HttpContext context, GameIdentificationCatalog catalog, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                try
                {
                    var artwork = await catalog.SelectAsync(candidateId, gameId, cancellationToken);
                    return artwork is null
                        ? Results.NotFound()
                        : Results.Ok(artwork);
                }
                catch (SteamGridDbMatchNotSearchedException)
                {
                    return Results.Problem(
                        title: "Escolha inválida.",
                        detail: "Pesquise os títulos e escolha uma das opções exibidas antes de carregar as artes.",
                        statusCode: StatusCodes.Status409Conflict);
                }
                catch (SteamGridDbException exception)
                {
                    return SteamGridDbProblem(exception);
                }
            });
        application.MapGet("/api/games/{candidateId:guid}", (Guid candidateId, HttpContext context, GameCandidateCatalog catalog) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var review = catalog.GetReview(candidateId);
                return review is null
                    ? Results.NotFound()
                    : Results.Ok(review);
            }
            catch (UnsafeGameFolderException exception)
            {
                return Results.Problem(
                    title: "Não foi possível revisar este candidato.",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(
                    title: "Acesso negado ao candidato.",
                    detail: "Verifique as permissões da pasta no PC-console e tente atualizar a descoberta.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (IOException)
            {
                return Results.Problem(
                    title: "A pasta do candidato não está mais disponível.",
                    detail: "Atualize a descoberta e tente novamente.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (NoGameExecutableException)
            {
                return Results.Problem(
                    title: "Nenhum executável plausível foi encontrado.",
                    detail: "Verifique se a instalação terminou ou escolha outra pasta de jogo.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });
        return application;
    }

    private static IResult ReadCandidateList(Func<IReadOnlyList<GameCandidateSummary>> read)
    {
        try
        {
            return Results.Ok(read());
        }
        catch (GamesRootNotConfiguredException)
        {
            return Results.Problem(
                title: "Configuração local pendente.",
                detail: "Conclua a configuração da pasta raiz na janela do Steam Import no Windows.",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static IResult SteamGridDbProblem(SteamGridDbException exception) =>
        Results.Problem(
            title: "Não foi possível consultar o SteamGridDB.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed class MissingGamesRootSource : IGamesRootSource
    {
        public string? GetGamesRootPath() => null;
    }

    private sealed class MissingSteamGridDbClient : ISteamGridDbClient
    {
        public Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
            string provisionalName,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SteamGridDbGameMatch>>(MissingConfiguration());

        public Task<SteamGridDbGameArtwork> GetRecommendedArtworkAsync(
            long gameId,
            CancellationToken cancellationToken) =>
            Task.FromException<SteamGridDbGameArtwork>(MissingConfiguration());

        private static SteamGridDbException MissingConfiguration() => new(
            SteamGridDbFailure.MissingConfiguration,
            "Configure a chave do SteamGridDB no PC-console e tente novamente.");
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    public static WebApplication Build(IStatusSource statusSource)
    {
        ArgumentNullException.ThrowIfNull(statusSource);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(statusSource);

        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
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
        return application;
    }
}

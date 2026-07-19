using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using SteamImport.Infrastructure;
using SteamImport.Web;

namespace SteamImport.App;

public partial class App : Application
{
    private static readonly IAppLog ApplicationLog = CreateLog();
    private static readonly LocalConfigurationStore LocalConfigurationStore = CreateConfigurationStore();
    private static readonly HttpClient SteamGridDbHttpClient = CreateSteamGridDbHttpClient();
    private WebApplication? webApplication;

    internal static IAppLog Log => ApplicationLog;

    internal static string? LogFilePath => (ApplicationLog as FileAppLog)?.FilePath;

    internal static LocalConfigurationStore ConfigurationStore => LocalConfigurationStore;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        Log.LogInformation(
            "app.started",
            $"version={typeof(App).Assembly.GetName().Version} os={Environment.OSVersion.VersionString.Replace(' ', '-')}");
        base.OnStartup(e);
        StartWebServer();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (webApplication is not null)
        {
            webApplication.StopAsync().GetAwaiter().GetResult();
            webApplication.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Log.LogInformation("app.exited", $"exitCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void StartWebServer()
    {
        try
        {
            webApplication = SteamImportServer.Build(
                new LocalConfigurationStatusSource(ConfigurationStore),
                new LocalConfigurationGamesRootSource(ConfigurationStore),
                new SystemGameFolderScanner(),
                new SteamGridDbClient(
                    SteamGridDbHttpClient,
                    new LocalConfigurationSteamGridDbApiKeySource(ConfigurationStore)),
                new LocalConfigurationRemoteImportContextSource(ConfigurationStore),
                new RemoteGameImporter(
                    new WindowsSteamClientController(),
                    new WindowsGameActivityProbe(),
                    SteamGridDbHttpClient,
                    Log));
            webApplication.Urls.Add($"http://0.0.0.0:{SteamImportServer.Port}");
            webApplication.StartAsync().GetAwaiter().GetResult();
            Log.LogInformation("web.started", $"port={SteamImportServer.Port}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Log.LogError("web.start-failed", $"port={SteamImportServer.Port}", exception);
            webApplication = null;
        }
    }

    private static IAppLog CreateLog()
    {
        try
        {
            return new FileAppLog(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamImport",
                "Logs"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return NullAppLog.Instance;
        }
    }

    private static LocalConfigurationStore CreateConfigurationStore() =>
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamImport",
                "config.json"),
            new WindowsUserSecretProtector());

    private static HttpClient CreateSteamGridDbHttpClient() => new()
    {
        BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/"),
        Timeout = TimeSpan.FromSeconds(15),
    };

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.LogError("app.unhandled-ui-error", "result=shutdown", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            BuildUnexpectedErrorMessage(),
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
                        new InvalidOperationException($"Unhandled object: {e.ExceptionObject}");
        Log.LogError("app.unhandled-error", $"terminating={e.IsTerminating}", exception);
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.LogError("app.unobserved-task-error", "result=observed", e.Exception);
        e.SetObserved();
    }

    internal static string BuildUnexpectedErrorMessage() =>
        LogFilePath is null
            ? "Ocorreu um erro inesperado. Tente novamente."
            : $"Ocorreu um erro inesperado. Consulte o log para obter detalhes:\n\n{LogFilePath}";
}

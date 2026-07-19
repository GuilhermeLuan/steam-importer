using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using SteamImport.Infrastructure;
using SteamImport.Web;
using MessageBox = System.Windows.MessageBox;

namespace SteamImport.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF application disposes tray resources during OnExit.")]
public partial class App : System.Windows.Application
{
    private static readonly string ApplicationDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamImport");
    private static readonly IAppLog ApplicationLog = CreateLog();
    private static readonly LocalConfigurationStore LocalConfigurationStore = CreateConfigurationStore();
    private static readonly HttpClient SteamGridDbHttpClient = CreateSteamGridDbHttpClient();
    private SingleUserApplicationInstance? applicationInstance;
    private WebApplication? webApplication;
    private System.Windows.Forms.NotifyIcon? trayIcon;
    private System.Windows.Forms.ContextMenuStrip? trayMenu;
    private TrayApplicationLifecycle? trayLifecycle;
    private bool shutdownRequested;

    internal static IAppLog Log => ApplicationLog;

    internal static string? LogFilePath => (ApplicationLog as FileAppLog)?.FilePath;

    internal static LocalConfigurationStore ConfigurationStore => LocalConfigurationStore;

    internal static LocalStartup LocalStartup => new(LocalConfigurationStore);

    internal static void SaveConfiguration(LocalConfiguration configuration)
    {
        var executablePath = Environment.ProcessPath ??
                             throw new InvalidOperationException("Não foi possível identificar o executável do Steam Import.");
        new LocalSetup(
            LocalConfigurationStore,
            new WindowsStartupRegistration(),
            executablePath).Save(configuration);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        base.OnStartup(e);
        applicationInstance = SingleUserApplicationInstance.TryAcquire(
            Path.Combine(ApplicationDataDirectory, "steam-import.lock"));
        if (applicationInstance is null)
        {
            Log.LogWarning("app.second-instance", "result=controlled-exit");
            MessageBox.Show(
                "O Steam Import já está em execução para este usuário.",
                "Steam Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Log.LogInformation(
            "app.started",
            $"version={typeof(App).Assembly.GetName().Version} os={Environment.OSVersion.VersionString.Replace(' ', '-')}");
        trayLifecycle = new TrayApplicationLifecycle(LocalStartup.Resume().StartMinimized);
        if (!TryCreateTrayIcon() && trayLifecycle.WindowState == TrayWindowState.Hidden)
        {
            trayLifecycle = new TrayApplicationLifecycle(startHidden: false);
        }

        MainWindow = new MainWindow();
        MainWindow.ShowInTaskbar = trayLifecycle.WindowState == TrayWindowState.Visible;
        MainWindow.Show();
        if (trayLifecycle.WindowState == TrayWindowState.Hidden)
        {
            MainWindow.Hide();
        }

        StartWebServer();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeTrayIcon();
        if (webApplication is not null)
        {
            webApplication.StopAsync().GetAwaiter().GetResult();
            webApplication.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        Log.LogInformation("app.exited", $"exitCode={e.ApplicationExitCode}");
        applicationInstance?.Dispose();
        base.OnExit(e);
    }

    internal bool HandleMainWindowClosing()
    {
        if (shutdownRequested || trayIcon is null)
        {
            return false;
        }

        var action = trayLifecycle?.RequestWindowClose() ?? TrayWindowCloseAction.Exit;
        if (action == TrayWindowCloseAction.Exit)
        {
            return false;
        }

        MainWindow.Hide();
        MainWindow.ShowInTaskbar = false;
        return true;
    }

    private bool TryCreateTrayIcon()
    {
        try
        {
            trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add("Abrir", null, (_, _) => OpenMainWindow());
            trayMenu.Items.Add("Sair", null, (_, _) => ExitFromTray());
            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Steam Import",
                ContextMenuStrip = trayMenu,
                Visible = true,
            };
            trayIcon.DoubleClick += (_, _) => OpenMainWindow();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.LogWarning("tray.create-failed", "result=window-visible");
            DisposeTrayIcon();
            return false;
        }
    }

    private void OpenMainWindow()
    {
        if (MainWindow is null || trayLifecycle?.WindowState == TrayWindowState.Exited)
        {
            return;
        }

        trayLifecycle!.OpenFromTray();
        MainWindow.ShowInTaskbar = true;
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Show();
        MainWindow.Activate();
    }

    private void ExitFromTray()
    {
        RequestApplicationExit();
    }

    private void RequestApplicationExit(int exitCode = 0)
    {
        shutdownRequested = true;
        trayLifecycle?.ExitFromTray();
        Shutdown(exitCode);
    }

    private void DisposeTrayIcon()
    {
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        trayMenu?.Dispose();
        trayMenu = null;
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
            return new FileAppLog(Path.Combine(ApplicationDataDirectory, "Logs"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return NullAppLog.Instance;
        }
    }

    private static LocalConfigurationStore CreateConfigurationStore() =>
        new(
            Path.Combine(ApplicationDataDirectory, "config.json"),
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
        RequestApplicationExit(1);
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

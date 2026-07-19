using System.IO;
using System.Windows;
using System.Windows.Threading;
using SteamImport.Infrastructure;

namespace SteamImport.App;

public partial class App : Application
{
    private static readonly IAppLog ApplicationLog = CreateLog();

    internal static IAppLog Log => ApplicationLog;

    internal static string? LogFilePath => (ApplicationLog as FileAppLog)?.FilePath;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        Log.LogInformation(
            "app.started",
            $"version={typeof(App).Assembly.GetName().Version} os={Environment.OSVersion.VersionString.Replace(' ', '-')}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.LogInformation("app.exited", $"exitCode={e.ApplicationExitCode}");
        base.OnExit(e);
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

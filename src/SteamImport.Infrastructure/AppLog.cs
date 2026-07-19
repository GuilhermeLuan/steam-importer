using System.Text;

namespace SteamImport.Infrastructure;

public interface IAppLog
{
    void LogInformation(string eventName, string message);

    void LogWarning(string eventName, string message);

    void LogError(string eventName, string message, Exception exception);
}

public sealed class FileAppLog : IAppLog
{
    private readonly object writeLock = new();

    public FileAppLog(string directory, int retainedFileCount = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedFileCount);

        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(
            directory,
            $"steam-import-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfffffff}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
        using (File.Create(FilePath))
        {
        }

        Rotate(directory, retainedFileCount);
    }

    public string FilePath { get; }

    public void LogInformation(string eventName, string message) =>
        Write("INFO", eventName, message, exception: null);

    public void LogWarning(string eventName, string message) =>
        Write("WARN", eventName, message, exception: null);

    public void LogError(string eventName, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write("ERROR", eventName, message, exception);
    }

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        try
        {
            var line = $"{DateTimeOffset.UtcNow:O} level={level} event={NormalizeEventName(eventName)} {SingleLine(message)}";
            var entry = exception is null
                ? line + Environment.NewLine
                : line + Environment.NewLine + exception + Environment.NewLine;
            lock (writeLock)
            {
                File.AppendAllText(FilePath, entry, Encoding.UTF8);
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Logging must never interrupt the import workflow.
        }
    }

    private static string NormalizeEventName(string eventName) =>
        string.Join('.', eventName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SingleLine(string message) =>
        message.ReplaceLineEndings(" ").Trim();

    private static void Rotate(string directory, int retainedFileCount)
    {
        foreach (var obsoleteLog in Directory
                     .EnumerateFiles(directory, "steam-import-*.log")
                     .OrderByDescending(path => path, StringComparer.Ordinal)
                     .Skip(retainedFileCount))
        {
            try
            {
                File.Delete(obsoleteLog);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked historical log can be retried on the next application start.
            }
        }
    }
}

public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    private NullAppLog()
    {
    }

    public void LogInformation(string eventName, string message)
    {
    }

    public void LogWarning(string eventName, string message)
    {
    }

    public void LogError(string eventName, string message, Exception exception)
    {
    }
}

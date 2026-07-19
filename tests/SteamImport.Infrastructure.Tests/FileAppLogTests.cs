using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class FileAppLogTests
{
    [Fact]
    public void ErrorEventIsWrittenWithContextAndExceptionDetails()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Logs-{Guid.NewGuid():N}");

        try
        {
            var log = new FileAppLog(directory);

            log.LogError(
                "import.failed",
                "account=123 existingShortcuts=2",
                new InvalidOperationException("Test failure"));

            var contents = File.ReadAllText(log.FilePath);
            Assert.Contains("level=ERROR event=import.failed account=123 existingShortcuts=2", contents, StringComparison.Ordinal);
            Assert.Contains("System.InvalidOperationException: Test failure", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void OnlyFiveMostRecentLogFilesAreRetained()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"SteamImport-Logs-{Guid.NewGuid():N}");

        try
        {
            for (var index = 0; index < 6; index++)
            {
                _ = new FileAppLog(directory);
            }

            Assert.Equal(5, Directory.GetFiles(directory, "steam-import-*.log").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

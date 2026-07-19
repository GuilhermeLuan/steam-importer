namespace SteamImport.Infrastructure;

public sealed class SingleUserApplicationInstance : IDisposable
{
    private readonly FileStream lockFile;

    private SingleUserApplicationInstance(FileStream lockFile)
    {
        this.lockFile = lockFile;
    }

    public static SingleUserApplicationInstance? TryAcquire(string lockFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        var fullPath = Path.GetFullPath(lockFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            var stream = new FileStream(
                fullPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return new SingleUserApplicationInstance(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => lockFile.Dispose();
}

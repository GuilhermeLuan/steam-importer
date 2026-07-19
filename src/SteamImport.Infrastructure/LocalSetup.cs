namespace SteamImport.Infrastructure;

public interface IUserStartupRegistration
{
    void EnsureRegistered(string executablePath);
}

public sealed class LocalSetup
{
    private readonly LocalConfigurationStore configurationStore;
    private readonly IUserStartupRegistration startupRegistration;
    private readonly string executablePath;

    public LocalSetup(
        LocalConfigurationStore configurationStore,
        IUserStartupRegistration startupRegistration,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(configurationStore);
        ArgumentNullException.ThrowIfNull(startupRegistration);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.configurationStore = configurationStore;
        this.startupRegistration = startupRegistration;
        this.executablePath = executablePath;
    }

    public void Save(LocalConfiguration configuration)
    {
        configurationStore.Save(configuration);
        startupRegistration.EnsureRegistered(executablePath);
    }
}

public sealed class LocalStartup
{
    private readonly LocalConfigurationStore configurationStore;

    public LocalStartup(LocalConfigurationStore configurationStore)
    {
        ArgumentNullException.ThrowIfNull(configurationStore);
        this.configurationStore = configurationStore;
    }

    public LocalStartupResult Resume()
    {
        try
        {
            return new LocalStartupResult(configurationStore.Load(), SavedConfigurationInvalid: false);
        }
        catch (Exception exception) when (
            exception is InvalidLocalConfigurationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            FormatException or
            ArgumentException)
        {
            return new LocalStartupResult(Configuration: null, SavedConfigurationInvalid: true);
        }
    }
}

public sealed record LocalStartupResult(
    LocalConfiguration? Configuration,
    bool SavedConfigurationInvalid)
{
    public bool StartMinimized => Configuration is not null;

    public bool RequiresSetup => Configuration is null;
}

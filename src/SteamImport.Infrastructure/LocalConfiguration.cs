namespace SteamImport.Infrastructure;

using System.ComponentModel;
using System.Text.Json;

public sealed record LocalConfiguration(
    string GamesRootPath,
    string SteamGridDbApiKey,
    string SteamRootPath,
    string SteamAccountId);

public interface ISecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public sealed class LocalConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string path;
    private readonly ISecretProtector secretProtector;

    public LocalConfigurationStore(string path, ISecretProtector secretProtector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(secretProtector);
        this.path = Path.GetFullPath(path);
        this.secretProtector = secretProtector;
    }

    public LocalConfiguration? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        LocalConfiguration configuration;
        try
        {
            var persisted = JsonSerializer.Deserialize<PersistedConfiguration>(
                File.ReadAllText(path),
                SerializerOptions) ?? throw new InvalidDataException("O arquivo de configuração está vazio.");
            configuration = new LocalConfiguration(
                persisted.GamesRootPath,
                secretProtector.Unprotect(persisted.ProtectedSteamGridDbApiKey),
                persisted.SteamRootPath,
                persisted.SteamAccountId);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or Win32Exception)
        {
            throw new InvalidDataException("O arquivo de configuração está corrompido ou pertence a outro usuário.", exception);
        }

        LocalConfigurationValidator.Validate(configuration);
        return configuration;
    }

    public void Save(LocalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        LocalConfigurationValidator.Validate(configuration);

        var persisted = new PersistedConfiguration(
            configuration.GamesRootPath,
            secretProtector.Protect(configuration.SteamGridDbApiKey),
            configuration.SteamRootPath,
            configuration.SteamAccountId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(persisted, SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PersistedConfiguration(
        string GamesRootPath,
        string ProtectedSteamGridDbApiKey,
        string SteamRootPath,
        string SteamAccountId);
}

public static class LocalConfigurationValidator
{
    public static void Validate(LocalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Directory.Exists(configuration.GamesRootPath))
        {
            throw new InvalidLocalConfigurationException("A pasta raiz dos jogos não existe.");
        }

        if (string.IsNullOrWhiteSpace(configuration.SteamGridDbApiKey))
        {
            throw new InvalidLocalConfigurationException("Informe a chave pessoal do SteamGridDB.");
        }

        SteamInstallation installation;
        try
        {
            installation = SteamInstallation.Open(configuration.SteamRootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidLocalConfigurationException("A instalação da Steam não é válida.", exception);
        }

        if (!installation.Accounts.Any(account =>
                string.Equals(account.Id, configuration.SteamAccountId, StringComparison.Ordinal)))
        {
            throw new InvalidLocalConfigurationException("A conta Steam selecionada não existe nesta instalação.");
        }
    }
}

public sealed class InvalidLocalConfigurationException : Exception
{
    public InvalidLocalConfigurationException(string message)
        : base(message)
    {
    }

    public InvalidLocalConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

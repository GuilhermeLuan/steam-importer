using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SteamImport.Infrastructure;

[SupportedOSPlatform("windows")]
public static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void EnsureRegistered(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue("SteamImport", $"\"{executablePath}\"", RegistryValueKind.String);
    }
}

public static class LocalNetworkAddresses
{
    public static IReadOnlyList<string> GetHttpAddresses(int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                .Select(address => $"http://{address}:{port}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();
        }
        catch (SocketException)
        {
            return [];
        }
    }
}

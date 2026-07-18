using System.Text;

namespace SteamImport.Core;

public static class SteamShortcutAppId
{
    private const uint NonSteamShortcutBit = 0x80000000;
    private const uint Crc32Polynomial = 0xEDB88320;

    public static uint Calculate(string executablePath, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var shortcutIdentity = $"\"{executablePath}\"{displayName}";
        var crc = ComputeCrc32(Encoding.UTF8.GetBytes(shortcutIdentity));
        return crc | NonSteamShortcutBit;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1
                    ? (crc >> 1) ^ Crc32Polynomial
                    : crc >> 1;
            }
        }

        return ~crc;
    }
}

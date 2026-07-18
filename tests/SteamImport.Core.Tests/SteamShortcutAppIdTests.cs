using SteamImport.Core;

namespace SteamImport.Core.Tests;

public sealed class SteamShortcutAppIdTests
{
    [Fact]
    public void SameExecutableAndNameProduceTheSteamCompatibleAppId()
    {
        var appId = SteamShortcutAppId.Calculate(
            @"C:\Games\Neon Horizon\NeonHorizon.exe",
            "Neon Horizon");

        Assert.Equal(0x85669BD1u, appId);
    }
}

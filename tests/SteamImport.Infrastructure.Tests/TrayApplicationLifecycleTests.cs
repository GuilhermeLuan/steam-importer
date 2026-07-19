using SteamImport.Infrastructure;

namespace SteamImport.Infrastructure.Tests;

public sealed class TrayApplicationLifecycleTests
{
    [Fact]
    public void AnUnconfiguredApplicationStartsVisibleForTheGuidedSetup()
    {
        var lifecycle = new TrayApplicationLifecycle(startHidden: false);

        Assert.Equal(TrayWindowState.Visible, lifecycle.WindowState);
    }

    [Fact]
    public void AConfiguredApplicationStartsHiddenUntilTheUserOpensItFromTheTray()
    {
        var lifecycle = new TrayApplicationLifecycle(startHidden: true);

        Assert.Equal(TrayWindowState.Hidden, lifecycle.WindowState);
    }

    [Fact]
    public void OpeningFromTheTrayMakesTheWindowVisible()
    {
        var lifecycle = new TrayApplicationLifecycle(startHidden: true);

        lifecycle.OpenFromTray();

        Assert.Equal(TrayWindowState.Visible, lifecycle.WindowState);
    }

    [Fact]
    public void ClosingTheWindowHidesItInsteadOfExiting()
    {
        var lifecycle = new TrayApplicationLifecycle(startHidden: false);

        var action = lifecycle.RequestWindowClose();

        Assert.Equal(TrayWindowCloseAction.Hide, action);
        Assert.Equal(TrayWindowState.Hidden, lifecycle.WindowState);
    }

    [Fact]
    public void ExitingFromTheTrayAllowsTheWindowAndApplicationToClose()
    {
        var lifecycle = new TrayApplicationLifecycle(startHidden: true);

        lifecycle.ExitFromTray();

        Assert.Equal(TrayWindowState.Exited, lifecycle.WindowState);
        Assert.Equal(TrayWindowCloseAction.Exit, lifecycle.RequestWindowClose());
    }
}

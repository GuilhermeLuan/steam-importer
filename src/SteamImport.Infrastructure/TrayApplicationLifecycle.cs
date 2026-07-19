namespace SteamImport.Infrastructure;

public enum TrayWindowState
{
    Visible,
    Hidden,
    Exited,
}

public enum TrayWindowCloseAction
{
    Hide,
    Exit,
}

public sealed class TrayApplicationLifecycle
{
    public TrayApplicationLifecycle(bool startHidden)
    {
        WindowState = startHidden ? TrayWindowState.Hidden : TrayWindowState.Visible;
    }

    public TrayWindowState WindowState { get; private set; }

    public void OpenFromTray() => WindowState = TrayWindowState.Visible;

    public TrayWindowCloseAction RequestWindowClose()
    {
        if (WindowState == TrayWindowState.Exited)
        {
            return TrayWindowCloseAction.Exit;
        }

        WindowState = TrayWindowState.Hidden;
        return TrayWindowCloseAction.Hide;
    }

    public void ExitFromTray() => WindowState = TrayWindowState.Exited;
}

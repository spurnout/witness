namespace GoatShot.App.Services;

/// <summary>
/// Decides whether a quiet capture needs tray feedback. Split out from the window because the
/// interesting case is a WPF quirk worth pinning with a test: a minimized window still reports
/// IsVisible == true, so checking visibility alone hides the balloon exactly when the user has
/// nothing on screen to tell them the capture worked.
/// </summary>
public static class CaptureFeedbackPolicy
{
    public static bool ShouldShowTrayNotification(bool workspaceVisible, bool workspaceMinimized)
    {
        return !workspaceVisible || workspaceMinimized;
    }
}

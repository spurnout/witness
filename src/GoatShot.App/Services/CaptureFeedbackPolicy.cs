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

    /// <summary>
    /// The balloon text for a quiet capture. Keyed off whether the clipboard copy actually landed —
    /// not off the setting that requested it — so a copy lost to a busy clipboard is reported as a
    /// plain save instead of a claim the user will disprove the moment they paste.
    /// </summary>
    public static string DescribeQuietCapture(bool copiedToClipboard, string fileName)
    {
        return copiedToClipboard ? $"Copied to clipboard: {fileName}" : $"Saved: {fileName}";
    }
}

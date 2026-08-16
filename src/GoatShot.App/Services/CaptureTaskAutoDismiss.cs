namespace GoatShot.App.Services;

/// <summary>
/// Timing rules for the capture actions window's self-dismiss. The window is a convenience, not a
/// dialog, so it fades out on its own rather than waiting to be closed. Kept separate from the
/// window so the clamping is testable without a WPF dispatcher.
/// </summary>
public static class CaptureTaskAutoDismiss
{
    /// <summary>Shipped delay before the fade starts.</summary>
    public const int DefaultSeconds = 8;

    /// <summary>Ten minutes. Past this the setting is indistinguishable from "stay open".</summary>
    public const int MaxSeconds = 600;

    /// <summary>How long the fade itself takes once the delay elapses.</summary>
    public static TimeSpan FadeDuration { get; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Clamps to a usable delay. Negative values mean the setting was hand-edited or corrupted, and
    /// collapse to "stay open" rather than to zero — dismissing the window instantly would look like
    /// the capture failed.
    /// </summary>
    public static int NormalizeSeconds(int seconds)
    {
        return seconds <= 0 ? 0 : Math.Min(seconds, MaxSeconds);
    }

    /// <summary>False when the window should stay open until the user closes it.</summary>
    public static bool IsEnabled(int seconds) => NormalizeSeconds(seconds) > 0;
}

namespace GoatShot.App.Models;

public sealed class CaptureSource
{
    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
    public string? MonitorName { get; set; }

    /// <summary>Page URL for browser-extension captures; null for native desktop sources.</summary>
    public string? SourceUrl { get; set; }

    public string AppLabel => string.IsNullOrWhiteSpace(ProcessName) ? "unknown" : ProcessName;
    public string WindowLabel => string.IsNullOrWhiteSpace(WindowTitle) ? "untitled" : WindowTitle;
}

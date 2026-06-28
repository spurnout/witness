namespace GoatShot.App.Models;

public sealed class VideoToolResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public CaptureItem? Item { get; set; }
}

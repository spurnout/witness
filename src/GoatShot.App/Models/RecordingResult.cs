namespace GoatShot.App.Models;

public sealed class RecordingResult
{
    public bool IsRecording { get; set; }
    public bool IsPaused { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public CaptureItem? Item { get; set; }
}

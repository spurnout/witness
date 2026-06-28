namespace GoatShot.App.Models;

public sealed class VisualRedactionResult
{
    public bool Succeeded { get; set; }
    public string? OutputPath { get; set; }
    public CaptureItem? Item { get; set; }
    public int RedactionCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public SensitiveScanResult? SensitiveScan { get; set; }
}

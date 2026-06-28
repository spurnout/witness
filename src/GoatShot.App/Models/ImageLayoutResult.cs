namespace GoatShot.App.Models;

public sealed class ImageLayoutResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> OutputPaths { get; set; } = new();
    public List<CaptureItem> Items { get; set; } = new();
}

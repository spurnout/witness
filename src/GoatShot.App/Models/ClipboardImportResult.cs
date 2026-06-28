namespace GoatShot.App.Models;

public sealed class ClipboardImportResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<CaptureItem> Items { get; set; } = new();
}

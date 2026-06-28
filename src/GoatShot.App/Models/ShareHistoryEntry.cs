namespace GoatShot.App.Models;

public sealed class ShareHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string CaptureItemId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public ShareDestination Destination { get; set; }
    public bool ExternalDestination { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Url { get; set; }
}

namespace GoatShot.App.Models;

public sealed class AiActionHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public AiActionKind Action { get; set; }
    public string CaptureItemId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public AiActionReviewStatus ReviewStatus { get; set; } = AiActionReviewStatus.Pending;
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? TextPreview { get; set; }
    public string? ParentEntryId { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

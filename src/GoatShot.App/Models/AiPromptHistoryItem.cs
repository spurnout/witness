namespace GoatShot.App.Models;

public sealed class AiPromptHistoryItem
{
    public AiActionKind Action { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

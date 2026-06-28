namespace GoatShot.App.Models;

public sealed class VideoIntelligenceResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Chapters { get; set; } = new();
    public bool UsedAiProvider { get; set; }
    public string ProviderName { get; set; } = "Local";
    public string ModelId { get; set; } = "local-transcript-draft";
    public string ProviderMessage { get; set; } = string.Empty;
}

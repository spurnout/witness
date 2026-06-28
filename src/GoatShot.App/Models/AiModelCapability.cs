namespace GoatShot.App.Models;

public sealed class AiModelCapability
{
    public string ProviderName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsImageEdit { get; set; }
    public bool SupportsImageAnalysis { get; set; }
    public bool SupportsVideoInput { get; set; }
    public bool SupportsTextOutput { get; set; } = true;
    public bool SupportsImageOutput { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

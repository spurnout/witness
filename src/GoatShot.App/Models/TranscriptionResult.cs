namespace GoatShot.App.Models;

public sealed class TranscriptionResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TranscriptPath { get; set; }
    public string? SrtPath { get; set; }
    public List<TranscriptSegment> Segments { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public bool UsedAiProvider { get; set; }
    public string ProviderName { get; set; } = "Local";
    public string ModelId { get; set; } = "local-transcription";
}

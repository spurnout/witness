namespace GoatShot.App.Models;

public sealed class BugReportEnrichment
{
    public string? TranscriptPath { get; set; }
    public string? SrtPath { get; set; }
    public string? VideoSummaryPath { get; set; }
    public List<string> KeyframePaths { get; set; } = new();
    public string? ContextNotes { get; set; }
    public List<AiActionHistoryEntry> AiHistory { get; set; } = new();
}

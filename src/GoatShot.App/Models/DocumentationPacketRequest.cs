namespace GoatShot.App.Models;

public sealed class DocumentationPacketRequest
{
    public CaptureItem Item { get; set; } = new();
    public string? OutputDirectory { get; set; }
    public string? TranscriptPath { get; set; }
    public string? SrtPath { get; set; }
    public string? VideoSummaryPath { get; set; }
    public string? BugReportPath { get; set; }
    public string BugReportFormat { get; set; } = "markdown";
    public bool GenerateBugReport { get; set; }
    public List<string> KeyframePaths { get; set; } = new();
    public string? ContextNotes { get; set; }
}

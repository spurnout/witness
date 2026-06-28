namespace GoatShot.App.Models;

public sealed class StepRecordingExportResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MarkdownPath { get; set; }
    public string? HtmlPath { get; set; }
    public int StepCount { get; set; }
}

namespace GoatShot.App.Models;

public sealed class BugReportExportResult
{
    public bool Succeeded { get; set; }
    public string? Path { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Format { get; set; } = "markdown";
}

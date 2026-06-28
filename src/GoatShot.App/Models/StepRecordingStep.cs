namespace GoatShot.App.Models;

public sealed class StepRecordingStep
{
    public int Number { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string CaptureItemId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string? SourceApp { get; set; }
    public string? WindowTitle { get; set; }
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public string? OcrText { get; set; }

    public string SourceLabel
    {
        get
        {
            var app = string.IsNullOrWhiteSpace(SourceApp) ? "unknown app" : SourceApp;
            var title = string.IsNullOrWhiteSpace(WindowTitle) ? "untitled window" : WindowTitle;
            return $"{app} / {title}";
        }
    }
}

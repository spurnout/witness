namespace GoatShot.App.Models;

public sealed class DiagnosticSnapshot
{
    public string OsDescription { get; set; } = string.Empty;
    public string RuntimeDescription { get; set; } = string.Empty;
    public string CaptureEngine { get; set; } = string.Empty;
    public string RecordingEngine { get; set; } = string.Empty;
    public string RecordingReadiness { get; set; } = string.Empty;
    public string EncoderStatus { get; set; } = string.Empty;
    public string OcrStatus { get; set; } = string.Empty;
    public string AiStatus { get; set; } = string.Empty;
    public string PolicyStatus { get; set; } = string.Empty;
    public string StartupStatus { get; set; } = string.Empty;
    public string MetadataIndexStatus { get; set; } = string.Empty;
    public string UploadQueueStatus { get; set; } = string.Empty;
    public string PrintImportStatus { get; set; } = string.Empty;
    public string PluginStatus { get; set; } = string.Empty;
    public string BrowserBridgeStatus { get; set; } = string.Empty;
    public string SharingStatus { get; set; } = string.Empty;
}

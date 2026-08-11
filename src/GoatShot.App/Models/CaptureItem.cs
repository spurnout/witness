using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

public sealed class CaptureItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CaptureKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public CaptureBounds? Bounds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public bool IsPrivate { get; set; }
    public string? SourceApp { get; set; }
    public string? SourceWindowTitle { get; set; }
    public string? SourceMonitorName { get; set; }
    public string? HotkeyProfile { get; set; }
    public string? OcrText { get; set; }
    public string? OcrLanguageTag { get; set; }
    public DateTimeOffset? OcrRecognizedAt { get; set; }
    public List<OcrRecognizedWord> OcrWords { get; set; } = new();
    public string? Notes { get; set; }
    public string? ReceiptId { get; set; }
    public string? SourceReceiptId { get; set; }
    public string? ArtifactRole { get; set; }
    public bool IsOriginal { get; set; }
    public bool SourceAvailable { get; set; } = true;
    public string? IntegrityStatus { get; set; }

    [JsonIgnore]
    public string Title => $"{Kind} - {CreatedAt:MMM d, h:mm tt}";

    [JsonIgnore]
    public string SizeLabel => $"{Width} x {Height}";

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public string BytesLabel => Bytes < 1024 * 1024
        ? $"{Bytes / 1024d:0.#} KB"
        : $"{Bytes / 1024d / 1024d:0.##} MB";

    [JsonIgnore]
    public string AccessibilityName =>
        $"{Kind} capture, {Title}, {Width} by {Height} pixels, {CreatedAt:f}";
}

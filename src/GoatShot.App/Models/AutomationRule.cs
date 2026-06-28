using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New rule";
    public bool IsEnabled { get; set; } = true;
    public AutomationTrigger Trigger { get; set; }
    public string SourceAppContains { get; set; } = string.Empty;
    public string WindowTitleContains { get; set; } = string.Empty;
    public string CaptureKind { get; set; } = string.Empty;
    public string MonitorContains { get; set; } = string.Empty;
    public string HotkeyProfile { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long? MinFileSizeBytes { get; set; }
    public long? MaxFileSizeBytes { get; set; }
    public string OcrContains { get; set; } = string.Empty;
    public bool? RequiresSensitiveData { get; set; }
    public VisualRedactionMode? ImageEffectMode { get; set; }
    public string ImageEffectRegion { get; set; } = string.Empty;
    public List<AutomationActionKind> Actions { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

public sealed class WorkflowProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Receipts workflow profile";
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public bool IncludesSensitiveValues { get; set; }
    public WorkflowProfileSharing Sharing { get; set; } = new();
    public WorkflowProfileRecording? Recording { get; set; }
    public WorkflowProfileWatchFolders WatchFolders { get; set; } = new();
    public List<AutomationRule> AutomationRules { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

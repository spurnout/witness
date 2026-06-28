namespace GoatShot.App.Models;

public sealed class WorkflowActionDryRunPlan
{
    public AutomationActionKind Action { get; set; }
    public bool Configured { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string ResolvedCommand { get; set; } = string.Empty;
    public string PayloadSummary { get; set; } = string.Empty;
    public Dictionary<string, string> MetadataPreview { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

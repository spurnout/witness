namespace GoatShot.App.Models;

public sealed class PluginUpdateSurfaceSummary
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RegistryLocation { get; set; } = string.Empty;
    public string CountsText { get; set; } = string.Empty;
    public string MutationBoundary { get; set; } = string.Empty;
    public string CliCommand { get; set; } = string.Empty;
    public string PluginsRoot { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
    public List<PluginUpdateSurfaceRow> Rows { get; set; } = new();
}

public sealed class PluginUpdateSurfaceRow
{
    public string Status { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VersionText { get; set; } = string.Empty;
    public string GateText { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;

    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(Name) ? PluginId : $"{Name} ({PluginId})";
        var gate = string.IsNullOrWhiteSpace(GateText) ? string.Empty : $" | {GateText}";
        return $"[{Status}] {name} - {VersionText}{gate}{Environment.NewLine}    {Message}{Environment.NewLine}    Next: {NextAction}";
    }
}

namespace GoatShot.App.Models;

public sealed class WorkflowProfileImportOptions
{
    public bool ReplaceAutomationRules { get; set; } = true;
    public bool IncludeSensitiveValues { get; set; }
}

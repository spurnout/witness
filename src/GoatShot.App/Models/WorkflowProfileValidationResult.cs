namespace GoatShot.App.Models;

public sealed class WorkflowProfileValidationResult
{
    public bool Succeeded => Errors.Count == 0;
    public string? Path { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public int RuleCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Message => Succeeded
        ? $"Workflow profile '{DisplayName}' is valid with {RuleCount} automation rule(s) and {Warnings.Count} warning(s)."
        : $"Workflow profile '{DisplayName}' is invalid with {Errors.Count} error(s) and {Warnings.Count} warning(s).";

    private string DisplayName => string.IsNullOrWhiteSpace(ProfileName) ? "unnamed" : ProfileName;
}

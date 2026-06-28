namespace GoatShot.App.Models;

public sealed class AutomationActionResult
{
    public AutomationActionKind Action { get; set; }
    public bool Succeeded { get; set; }
    public bool Executed { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
}

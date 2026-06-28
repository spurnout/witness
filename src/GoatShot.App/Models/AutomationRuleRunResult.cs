namespace GoatShot.App.Models;

public sealed class AutomationRuleRunResult
{
    public AutomationRuleEvaluation Evaluation { get; set; } = new();
    public List<AutomationActionResult> Actions { get; set; } = new();
}

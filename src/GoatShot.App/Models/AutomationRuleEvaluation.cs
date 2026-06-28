namespace GoatShot.App.Models;

public sealed class AutomationRuleEvaluation
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public AutomationTrigger RuleTrigger { get; set; }
    public AutomationTrigger RequestedTrigger { get; set; }
    public bool IsEnabled { get; set; }
    public bool TriggerMatches { get; set; }
    public bool Matches { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<AutomationActionKind> Actions { get; set; } = new();
}

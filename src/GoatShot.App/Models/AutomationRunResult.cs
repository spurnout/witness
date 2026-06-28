namespace GoatShot.App.Models;

public sealed class AutomationRunResult
{
    public AutomationTrigger Trigger { get; set; }
    public bool DryRun { get; set; }
    public int TotalRules { get; set; }
    public int MatchingRules { get; set; }
    public string? LogPath { get; set; }
    public string? MarkdownLogPath { get; set; }
    public string? LogMessage { get; set; }
    public List<AutomationRuleEvaluation> Evaluations { get; set; } = new();
    public List<AutomationRuleRunResult> Rules { get; set; } = new();
    public bool Succeeded => Rules.SelectMany(rule => rule.Actions).All(action => action.Succeeded);
    public string Summary => DryRun
        ? $"Dry run found {MatchingRules} matching automation rule(s) out of {TotalRules}."
        : $"Ran {Rules.Count} automation rule(s) for {Trigger}.";
}

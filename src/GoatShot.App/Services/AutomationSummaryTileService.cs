using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AutomationSummaryTileService
{
    private const string ReadyTone = "Ready";
    private const string NeutralTone = "Neutral";

    public IReadOnlyList<SettingsSummaryTile> Create(
        IReadOnlyList<AutomationRule> rules,
        bool watchFoldersEnabled,
        IReadOnlyList<string> watchFolders)
    {
        return new[]
        {
            CreateWorkflowRulesTile(rules),
            CreateWatchFoldersTile(watchFoldersEnabled, watchFolders),
            CreateRuleTriggersTile(rules)
        };
    }

    private static SettingsSummaryTile CreateWorkflowRulesTile(IReadOnlyList<AutomationRule> rules)
    {
        var enabledCount = rules.Count(rule => rule.IsEnabled);
        var detail = rules.Count == 0
            ? "No saved rules"
            : string.Join(", ", rules.Select(rule => rule.Name).Take(2)) +
                (rules.Count > 2 ? $", +{rules.Count - 2} more" : string.Empty);
        return new SettingsSummaryTile(
            "Workflow rules",
            $"{enabledCount}/{rules.Count}",
            detail,
            enabledCount > 0 ? ReadyTone : NeutralTone);
    }

    private static SettingsSummaryTile CreateWatchFoldersTile(
        bool watchFoldersEnabled,
        IReadOnlyList<string> watchFolders)
    {
        return new SettingsSummaryTile(
            "Watch folders",
            watchFolders.Count.ToString(CultureInfo.InvariantCulture),
            watchFoldersEnabled ? "Watching while GoatShot runs" : "Watching disabled",
            watchFoldersEnabled && watchFolders.Count > 0 ? ReadyTone : NeutralTone);
    }

    private static SettingsSummaryTile CreateRuleTriggersTile(IReadOnlyList<AutomationRule> rules)
    {
        var triggers = rules
            .Where(rule => rule.IsEnabled)
            .Select(rule => rule.Trigger)
            .Distinct()
            .ToList();
        return new SettingsSummaryTile(
            "Rule triggers",
            triggers.Count.ToString(CultureInfo.InvariantCulture),
            triggers.Count == 0 ? "No active triggers" : string.Join(", ", triggers),
            triggers.Count > 0 ? ReadyTone : NeutralTone);
    }
}

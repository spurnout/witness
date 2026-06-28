using GoatShot.App.Models;

namespace GoatShot.App.Services;

internal sealed class QuickAutomationRuleDraft
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Settings quick workflow rule";
    public bool IsEnabled { get; set; } = true;
    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.CaptureCreated;
    public string SourceAppContains { get; set; } = string.Empty;
    public string WindowTitleContains { get; set; } = string.Empty;
    public string CaptureKind { get; set; } = string.Empty;
    public string MonitorContains { get; set; } = string.Empty;
    public string HotkeyProfile { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long? MinFileSizeBytes { get; set; }
    public long? MaxFileSizeBytes { get; set; }
    public string OcrContains { get; set; } = string.Empty;
    public bool? RequiresSensitiveData { get; set; }
    public VisualRedactionMode? ImageEffectMode { get; set; }
    public string ImageEffectRegion { get; set; } = string.Empty;
    public List<AutomationActionKind> Actions { get; set; } = new();
}

internal static class SettingsQuickAutomationRule
{
    public const string RuleId = "settings.quick.rule";
    public const string LegacyRuleId = "settings.capture.simple";

    public static QuickAutomationRuleDraft Load(AppSettings settings)
    {
        var rule = settings.AutomationRules.FirstOrDefault(rule =>
            rule.Id.Equals(RuleId, StringComparison.OrdinalIgnoreCase)) ??
            settings.AutomationRules.FirstOrDefault(rule =>
                rule.Id.Equals(LegacyRuleId, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            return new QuickAutomationRuleDraft();
        }

        return new QuickAutomationRuleDraft
        {
            Id = rule.Id,
            Name = string.IsNullOrWhiteSpace(rule.Name) ? "Settings quick workflow rule" : rule.Name,
            IsEnabled = rule.IsEnabled,
            Trigger = rule.Trigger,
            SourceAppContains = rule.SourceAppContains,
            WindowTitleContains = rule.WindowTitleContains,
            CaptureKind = rule.CaptureKind,
            MonitorContains = rule.MonitorContains,
            HotkeyProfile = rule.HotkeyProfile,
            FileExtension = rule.FileExtension,
            MinFileSizeBytes = rule.MinFileSizeBytes,
            MaxFileSizeBytes = rule.MaxFileSizeBytes,
            OcrContains = rule.OcrContains,
            RequiresSensitiveData = rule.RequiresSensitiveData,
            ImageEffectMode = rule.ImageEffectMode,
            ImageEffectRegion = rule.ImageEffectRegion,
            Actions = rule.Actions.Distinct().ToList()
        };
    }

    public static void Save(AppSettings settings, QuickAutomationRuleDraft draft)
    {
        settings.AutomationRules.RemoveAll(rule =>
            rule.Id.Equals(RuleId, StringComparison.OrdinalIgnoreCase) ||
            rule.Id.Equals(LegacyRuleId, StringComparison.OrdinalIgnoreCase));

        var actions = draft.Actions.Distinct().ToList();
        if (actions.Count == 0)
        {
            return;
        }

        settings.AutomationRules.Add(new AutomationRule
        {
            Id = RuleId,
            Name = string.IsNullOrWhiteSpace(draft.Name) ? "Settings quick workflow rule" : draft.Name.Trim(),
            IsEnabled = draft.IsEnabled,
            Trigger = draft.Trigger,
            SourceAppContains = draft.SourceAppContains.Trim(),
            WindowTitleContains = draft.WindowTitleContains.Trim(),
            CaptureKind = draft.CaptureKind.Trim(),
            MonitorContains = draft.MonitorContains.Trim(),
            HotkeyProfile = draft.HotkeyProfile.Trim(),
            FileExtension = draft.FileExtension.Trim(),
            MinFileSizeBytes = draft.MinFileSizeBytes,
            MaxFileSizeBytes = draft.MaxFileSizeBytes,
            OcrContains = draft.OcrContains.Trim(),
            RequiresSensitiveData = draft.RequiresSensitiveData,
            ImageEffectMode = draft.ImageEffectMode,
            ImageEffectRegion = draft.ImageEffectRegion.Trim(),
            Actions = actions
        });
    }
}

internal sealed record AutomationRuleListItem(string Id, string DisplayText, string Summary);

internal static class SettingsAutomationRuleManager
{
    private static readonly AutomationActionKind[] EditorActionKinds =
    [
        AutomationActionKind.CopyImageToClipboard,
        AutomationActionKind.CopyFileToClipboard,
        AutomationActionKind.CopyPathToClipboard,
        AutomationActionKind.SaveToFolder,
        AutomationActionKind.RunOcr,
        AutomationActionKind.RedactDetectedSensitiveData,
        AutomationActionKind.ApplyImageEffect,
        AutomationActionKind.StripMetadataCopy,
        AutomationActionKind.ShareDefaultDestination,
        AutomationActionKind.GenerateDocument,
        AutomationActionKind.RunCustomScript,
        AutomationActionKind.CallCustomWebhook,
        AutomationActionKind.ShowNotification
    ];

    private static readonly HashSet<AutomationActionKind> EditorActionSet = new(EditorActionKinds);

    public static List<AutomationRule> CloneRulesForManager(IEnumerable<AutomationRule> rules)
    {
        var result = new List<AutomationRule>();
        var hasCurrentQuickRule = rules.Any(rule =>
            rule.Id.Equals(SettingsQuickAutomationRule.RuleId, StringComparison.OrdinalIgnoreCase));

        foreach (var source in rules)
        {
            if (source.Id.Equals(SettingsQuickAutomationRule.LegacyRuleId, StringComparison.OrdinalIgnoreCase) &&
                hasCurrentQuickRule)
            {
                continue;
            }

            var clone = CloneRule(source);
            if (clone.Id.Equals(SettingsQuickAutomationRule.LegacyRuleId, StringComparison.OrdinalIgnoreCase))
            {
                clone.Id = SettingsQuickAutomationRule.RuleId;
            }

            if (string.IsNullOrWhiteSpace(clone.Id) ||
                result.Any(existing => existing.Id.Equals(clone.Id, StringComparison.OrdinalIgnoreCase)))
            {
                clone.Id = CreateRuleId();
            }

            result.Add(clone);
        }

        return result;
    }

    public static AutomationRule CreateNewRule(IEnumerable<AutomationRule> existingRules)
    {
        return new AutomationRule
        {
            Id = CreateRuleId(),
            Name = UniqueName(existingRules, "New workflow rule"),
            IsEnabled = false,
            Trigger = AutomationTrigger.CaptureCreated,
            Actions = [AutomationActionKind.ShowNotification]
        };
    }

    public static AutomationRule DuplicateRule(AutomationRule source, IEnumerable<AutomationRule> existingRules)
    {
        var clone = CloneRule(source);
        clone.Id = CreateRuleId();
        clone.Name = UniqueName(existingRules, $"{DisplayName(source)} copy");
        return clone;
    }

    public static QuickAutomationRuleDraft ToDraft(AutomationRule rule)
    {
        return new QuickAutomationRuleDraft
        {
            Id = rule.Id,
            Name = DisplayName(rule),
            IsEnabled = rule.IsEnabled,
            Trigger = rule.Trigger,
            SourceAppContains = rule.SourceAppContains,
            WindowTitleContains = rule.WindowTitleContains,
            CaptureKind = rule.CaptureKind,
            MonitorContains = rule.MonitorContains,
            HotkeyProfile = rule.HotkeyProfile,
            FileExtension = rule.FileExtension,
            MinFileSizeBytes = rule.MinFileSizeBytes,
            MaxFileSizeBytes = rule.MaxFileSizeBytes,
            OcrContains = rule.OcrContains,
            RequiresSensitiveData = rule.RequiresSensitiveData,
            ImageEffectMode = rule.ImageEffectMode,
            ImageEffectRegion = rule.ImageEffectRegion,
            Actions = rule.Actions.Where(EditorActionSet.Contains).Distinct().ToList()
        };
    }

    public static AutomationRule ApplyDraft(AutomationRule rule, QuickAutomationRuleDraft draft)
    {
        rule.Name = string.IsNullOrWhiteSpace(draft.Name) ? "Workflow rule" : draft.Name.Trim();
        rule.IsEnabled = draft.IsEnabled;
        rule.Trigger = draft.Trigger;
        rule.SourceAppContains = draft.SourceAppContains.Trim();
        rule.WindowTitleContains = draft.WindowTitleContains.Trim();
        rule.CaptureKind = draft.CaptureKind.Trim();
        rule.MonitorContains = draft.MonitorContains.Trim();
        rule.HotkeyProfile = draft.HotkeyProfile.Trim();
        rule.FileExtension = draft.FileExtension.Trim();
        rule.MinFileSizeBytes = draft.MinFileSizeBytes;
        rule.MaxFileSizeBytes = draft.MaxFileSizeBytes;
        rule.OcrContains = draft.OcrContains.Trim();
        rule.RequiresSensitiveData = draft.RequiresSensitiveData;
        rule.ImageEffectMode = draft.ImageEffectMode;
        rule.ImageEffectRegion = draft.ImageEffectRegion.Trim();

        var hiddenActions = rule.Actions
            .Where(action => !EditorActionSet.Contains(action))
            .ToList();
        rule.Actions = hiddenActions
            .Concat(draft.Actions.Where(EditorActionSet.Contains))
            .Distinct()
            .ToList();

        return rule;
    }

    public static AutomationRule CreateRuleFromDraft(QuickAutomationRuleDraft draft)
    {
        var rule = new AutomationRule
        {
            Id = string.IsNullOrWhiteSpace(draft.Id) ? CreateRuleId() : draft.Id.Trim()
        };
        return ApplyDraft(rule, draft);
    }

    public static List<AutomationRuleListItem> CreateListItems(IEnumerable<AutomationRule> rules)
    {
        return rules.Select(rule =>
            {
                var state = rule.IsEnabled ? "On" : "Off";
                var name = DisplayName(rule);
                var actions = rule.Actions.Count == 0
                    ? "no actions"
                    : string.Join(", ", rule.Actions.Take(3));
                if (rule.Actions.Count > 3)
                {
                    actions += $", +{rule.Actions.Count - 3} more";
                }

                var conditions = CountConditions(rule);
                var summary = $"{rule.Trigger}; {conditions} condition(s); {actions}";
                return new AutomationRuleListItem(rule.Id, $"{state}: {name} - {summary}", summary);
            })
            .ToList();
    }

    public static List<AutomationRule> PrepareRulesForSave(IEnumerable<AutomationRule> rules)
    {
        return CloneRulesForManager(rules)
            .Where(rule => rule.Actions.Count > 0)
            .ToList();
    }

    public static bool HasEditorActions(QuickAutomationRuleDraft draft)
    {
        return draft.Actions.Any(EditorActionSet.Contains);
    }

    private static AutomationRule CloneRule(AutomationRule rule)
    {
        return new AutomationRule
        {
            Id = rule.Id,
            Name = rule.Name,
            IsEnabled = rule.IsEnabled,
            Trigger = rule.Trigger,
            SourceAppContains = rule.SourceAppContains,
            WindowTitleContains = rule.WindowTitleContains,
            CaptureKind = rule.CaptureKind,
            MonitorContains = rule.MonitorContains,
            HotkeyProfile = rule.HotkeyProfile,
            FileExtension = rule.FileExtension,
            MinFileSizeBytes = rule.MinFileSizeBytes,
            MaxFileSizeBytes = rule.MaxFileSizeBytes,
            OcrContains = rule.OcrContains,
            RequiresSensitiveData = rule.RequiresSensitiveData,
            ImageEffectMode = rule.ImageEffectMode,
            ImageEffectRegion = rule.ImageEffectRegion,
            Actions = rule.Actions.Distinct().ToList(),
            ExtensionData = rule.ExtensionData is null || rule.ExtensionData.Count == 0
                ? null
                : rule.ExtensionData.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string UniqueName(IEnumerable<AutomationRule> existingRules, string baseName)
    {
        var used = existingRules
            .Select(DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {DateTimeOffset.Now:yyyyMMddHHmmss}";
    }

    private static string DisplayName(AutomationRule rule)
    {
        return string.IsNullOrWhiteSpace(rule.Name) ? "Workflow rule" : rule.Name.Trim();
    }

    private static string CreateRuleId()
    {
        return $"settings.rule.{Guid.NewGuid():N}";
    }

    private static int CountConditions(AutomationRule rule)
    {
        var count = 0;
        AddIfPresent(rule.SourceAppContains, ref count);
        AddIfPresent(rule.WindowTitleContains, ref count);
        AddIfPresent(rule.CaptureKind, ref count);
        AddIfPresent(rule.MonitorContains, ref count);
        AddIfPresent(rule.HotkeyProfile, ref count);
        AddIfPresent(rule.FileExtension, ref count);
        AddIfPresent(rule.OcrContains, ref count);
        if (rule.MinFileSizeBytes is not null)
        {
            count++;
        }

        if (rule.MaxFileSizeBytes is not null)
        {
            count++;
        }

        if (rule.RequiresSensitiveData is not null)
        {
            count++;
        }

        return count;
    }

    private static void AddIfPresent(string? value, ref int count)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            count++;
        }
    }
}

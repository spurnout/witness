using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record WorkflowRuleTemplate(
    string Id,
    string Name,
    string Category,
    string Description,
    AutomationTrigger Trigger,
    IReadOnlyList<AutomationActionKind> Actions,
    IReadOnlyList<string> Notes)
{
    public string ActionSummary => Actions.Count == 0 ? "none" : string.Join(", ", Actions);
}

public static class WorkflowRuleTemplateCatalog
{
    private static readonly IReadOnlyList<WorkflowRuleTemplateDefinition> Definitions =
    [
        new(
            "capture-doc",
            "Capture to documentation",
            "Documentation",
            "Run OCR on new image captures, export a local bug-report/document shell, and notify when it is ready.",
            AutomationTrigger.CaptureCreated,
            [AutomationActionKind.RunOcr, AutomationActionKind.GenerateDocument, AutomationActionKind.ShowNotification],
            ["Starts disabled so the output folder and document format can be reviewed before automation runs."],
            rule =>
            {
                rule.FileExtension = ".png";
            }),
        new(
            "ocr-redact",
            "OCR sensitive-data redaction",
            "Privacy",
            "When OCR finds sensitive text, create a flattened redacted copy, export a redacted report, and notify.",
            AutomationTrigger.OcrCompleted,
            [AutomationActionKind.RedactDetectedSensitiveData, AutomationActionKind.GenerateDocument, AutomationActionKind.ShowNotification],
            ["Requires OCR text with a sensitive-data finding.", "Solid redaction remains the safe default."],
            rule =>
            {
                rule.RequiresSensitiveData = true;
                rule.ImageEffectMode = VisualRedactionMode.Solid;
                rule.ImageEffectRegion = "full";
            }),
        new(
            "upload-notify",
            "Upload completion notification",
            "Sharing",
            "Show a local status notification when queued or manual uploads complete.",
            AutomationTrigger.UploadCompleted,
            [AutomationActionKind.ShowNotification],
            ["Does not call external providers itself; it reacts after the upload-completed event."],
            _ => { }),
        new(
            "recording-bug-report",
            "Recording to bug report",
            "Documentation",
            "When an MP4 recording completes, generate a local bug-report/document shell and notify.",
            AutomationTrigger.RecordingCompleted,
            [AutomationActionKind.GenerateDocument, AutomationActionKind.ShowNotification],
            ["Targets MP4 recordings; GIF recordings can use a duplicate rule with capture kind changed."],
            rule =>
            {
                rule.CaptureKind = "RecordingMp4";
                rule.FileExtension = ".mp4";
            }),
        new(
            "ai-summary-followup",
            "AI summary follow-up",
            "AI",
            "After an AI action completes, generate a local report shell and notify so the result can be reviewed.",
            AutomationTrigger.AiActionCompleted,
            [AutomationActionKind.GenerateDocument, AutomationActionKind.ShowNotification],
            ["Does not invoke AI by itself; it reacts to an explicit AI action completion event."],
            _ => { })
    ];

    public static IReadOnlyList<WorkflowRuleTemplate> ListTemplates()
    {
        return Definitions.Select(definition => definition.ToTemplate()).ToList();
    }

    public static WorkflowRuleTemplate? Find(string idOrName)
    {
        idOrName = (idOrName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        return Definitions
            .FirstOrDefault(definition =>
                definition.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                definition.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
            ?.ToTemplate();
    }

    public static AutomationRule CreateRule(
        string templateId,
        IEnumerable<AutomationRule> existingRules,
        bool enabled = false,
        string? nameOverride = null)
    {
        var definition = Definitions.FirstOrDefault(definition =>
            definition.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase) ||
            definition.Name.Equals(templateId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            throw new ArgumentException($"Unknown workflow rule template: {templateId}.", nameof(templateId));
        }

        var name = UniqueName(existingRules, string.IsNullOrWhiteSpace(nameOverride) ? definition.Name : nameOverride.Trim());
        var rule = new AutomationRule
        {
            Id = CreateTemplateRuleId(definition.Id),
            Name = name,
            IsEnabled = enabled,
            Trigger = definition.Trigger,
            Actions = definition.Actions.Distinct().ToList()
        };
        definition.Configure(rule);
        return rule;
    }

    public static bool TryAddTemplateRule(
        AppSettings settings,
        string templateId,
        bool enabled,
        bool replaceExisting,
        out AutomationRule? rule,
        out string message)
    {
        try
        {
            var template = Find(templateId);
            if (template is null)
            {
                throw new ArgumentException($"Unknown workflow rule template: {templateId}.", nameof(templateId));
            }

            if (replaceExisting)
            {
                var prefix = CreateTemplateRulePrefix(template.Id);
                settings.AutomationRules.RemoveAll(candidate =>
                    candidate.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            rule = CreateRule(template.Id, settings.AutomationRules, enabled);
            settings.AutomationRules.Add(rule);
            message = $"Added workflow template '{rule.Name}' ({rule.Id}).";
            return true;
        }
        catch (ArgumentException ex)
        {
            rule = null;
            message = ex.Message;
            return false;
        }
    }

    private static string UniqueName(IEnumerable<AutomationRule> existingRules, string baseName)
    {
        var used = existingRules
            .Select(rule => string.IsNullOrWhiteSpace(rule.Name) ? "Workflow rule" : rule.Name.Trim())
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

    private static string CreateTemplateRuleId(string templateId)
    {
        return $"{CreateTemplateRulePrefix(templateId)}{Guid.NewGuid():N}";
    }

    private static string CreateTemplateRulePrefix(string templateId)
    {
        var normalized = new string(templateId
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        return $"settings.template.{normalized}.";
    }

    private sealed record WorkflowRuleTemplateDefinition(
        string Id,
        string Name,
        string Category,
        string Description,
        AutomationTrigger Trigger,
        IReadOnlyList<AutomationActionKind> Actions,
        IReadOnlyList<string> Notes,
        Action<AutomationRule> Configure)
    {
        public WorkflowRuleTemplate ToTemplate()
        {
            return new WorkflowRuleTemplate(Id, Name, Category, Description, Trigger, Actions, Notes);
        }
    }
}

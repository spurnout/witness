using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class WorkflowRuleTemplateCatalogTests
{
    [TestMethod]
    public void ListTemplates_IncludesCommonWorkflowStarters()
    {
        var templates = WorkflowRuleTemplateCatalog.ListTemplates();
        var ids = templates.Select(template => template.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "capture-doc",
                "ocr-redact",
                "upload-notify",
                "recording-bug-report",
                "ai-summary-followup"
            },
            ids.ToList());

        Assert.IsTrue(templates.All(template => template.Actions.Count > 0));
        Assert.IsTrue(templates.All(template => !string.IsNullOrWhiteSpace(template.Description)));
    }

    [TestMethod]
    public void CreateRule_BuildsDisabledReviewableCaptureDocumentationRule()
    {
        var rule = WorkflowRuleTemplateCatalog.CreateRule(
            "capture-doc",
            Array.Empty<AutomationRule>());

        Assert.IsFalse(rule.IsEnabled);
        Assert.IsTrue(rule.Id.StartsWith("settings.template.capture-doc.", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("Capture to documentation", rule.Name);
        Assert.AreEqual(AutomationTrigger.CaptureCreated, rule.Trigger);
        Assert.AreEqual(".png", rule.FileExtension);
        CollectionAssert.AreEqual(
            new[]
            {
                AutomationActionKind.RunOcr,
                AutomationActionKind.GenerateDocument,
                AutomationActionKind.ShowNotification
            },
            rule.Actions);
    }

    [TestMethod]
    public void CreateRule_ConfiguresSensitiveOcrRedactionConditions()
    {
        var rule = WorkflowRuleTemplateCatalog.CreateRule(
            "ocr-redact",
            Array.Empty<AutomationRule>(),
            enabled: true);

        Assert.IsTrue(rule.IsEnabled);
        Assert.AreEqual(AutomationTrigger.OcrCompleted, rule.Trigger);
        Assert.AreEqual(true, rule.RequiresSensitiveData);
        Assert.AreEqual(VisualRedactionMode.Solid, rule.ImageEffectMode);
        Assert.AreEqual("full", rule.ImageEffectRegion);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.RedactDetectedSensitiveData);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.GenerateDocument);
    }

    [TestMethod]
    public void CreateRule_UsesUniqueNamesAgainstExistingRules()
    {
        var existing = new[]
        {
            new AutomationRule { Name = "Capture to documentation" }
        };

        var rule = WorkflowRuleTemplateCatalog.CreateRule("capture-doc", existing);

        Assert.AreEqual("Capture to documentation 2", rule.Name);
    }

    [TestMethod]
    public void TryAddTemplateRule_ReplacesExistingTemplateByDisplayName()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                WorkflowRuleTemplateCatalog.CreateRule("capture-doc", Array.Empty<AutomationRule>()),
                new AutomationRule
                {
                    Id = "custom",
                    Name = "Custom",
                    Trigger = AutomationTrigger.CaptureCreated,
                    Actions = [AutomationActionKind.ShowNotification]
                }
            ]
        };

        var succeeded = WorkflowRuleTemplateCatalog.TryAddTemplateRule(
            settings,
            "Capture to documentation",
            enabled: true,
            replaceExisting: true,
            out var rule,
            out var message);

        Assert.IsTrue(succeeded, message);
        Assert.IsNotNull(rule);
        Assert.IsTrue(rule.IsEnabled);
        Assert.AreEqual(2, settings.AutomationRules.Count);
        Assert.AreEqual(1, settings.AutomationRules.Count(candidate =>
            candidate.Id.StartsWith("settings.template.capture-doc.", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(settings.AutomationRules.Any(candidate => candidate.Id == "custom"));
    }

    [TestMethod]
    public void TemplateRules_EvaluateAgainstMatchingEvents()
    {
        var rule = WorkflowRuleTemplateCatalog.CreateRule(
            "recording-bug-report",
            Array.Empty<AutomationRule>(),
            enabled: true);
        var item = new CaptureItem
        {
            Kind = CaptureKind.RecordingMp4,
            FilePath = Path.Combine(Path.GetTempPath(), "demo.mp4"),
            Bytes = 1024
        };

        var evaluation = AutomationService.EvaluateRule(rule, AutomationTrigger.RecordingCompleted, item);

        Assert.IsTrue(evaluation.Matches, string.Join("; ", evaluation.Reasons));
        CollectionAssert.Contains(evaluation.Actions, AutomationActionKind.GenerateDocument);
        CollectionAssert.Contains(evaluation.Actions, AutomationActionKind.ShowNotification);
    }
}

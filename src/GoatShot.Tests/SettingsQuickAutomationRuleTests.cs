using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SettingsQuickAutomationRuleTests
{
    [TestMethod]
    public void Load_MigratesLegacySimpleRuleIntoQuickRuleDraft()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                new AutomationRule
                {
                    Id = SettingsQuickAutomationRule.LegacyRuleId,
                    Name = "Legacy capture rule",
                    Trigger = AutomationTrigger.CaptureCreated,
                    SourceAppContains = "chrome",
                    Actions =
                    [
                        AutomationActionKind.CopyImageToClipboard,
                        AutomationActionKind.StripMetadataCopy
                    ]
                }
            ]
        };

        var draft = SettingsQuickAutomationRule.Load(settings);

        Assert.AreEqual("Legacy capture rule", draft.Name);
        Assert.AreEqual(AutomationTrigger.CaptureCreated, draft.Trigger);
        Assert.AreEqual("chrome", draft.SourceAppContains);
        CollectionAssert.Contains(draft.Actions, AutomationActionKind.CopyImageToClipboard);
        CollectionAssert.Contains(draft.Actions, AutomationActionKind.StripMetadataCopy);
    }

    [TestMethod]
    public void Save_ReplacesLegacyRuleWithRichQuickRule()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                new AutomationRule
                {
                    Id = SettingsQuickAutomationRule.LegacyRuleId,
                    Actions = [AutomationActionKind.CopyImageToClipboard]
                }
            ]
        };

        SettingsQuickAutomationRule.Save(settings, new QuickAutomationRuleDraft
        {
            Name = "OCR secrets",
            IsEnabled = true,
            Trigger = AutomationTrigger.OcrCompleted,
            SourceAppContains = "browser",
            WindowTitleContains = "checkout",
            CaptureKind = "ActiveWindow",
            MonitorContains = "DISPLAY1",
            HotkeyProfile = "Support",
            FileExtension = "png",
            MinFileSizeBytes = 100,
            MaxFileSizeBytes = 500,
            OcrContains = "token",
            RequiresSensitiveData = true,
            ImageEffectMode = VisualRedactionMode.Pixelate,
            ImageEffectRegion = "10,10,80,80",
            Actions =
            [
                AutomationActionKind.ApplyImageEffect,
                AutomationActionKind.RedactDetectedSensitiveData,
                AutomationActionKind.GenerateDocument,
                AutomationActionKind.ShowNotification
            ]
        });

        var rule = settings.AutomationRules.Single();
        Assert.AreEqual(SettingsQuickAutomationRule.RuleId, rule.Id);
        Assert.AreEqual("OCR secrets", rule.Name);
        Assert.AreEqual(AutomationTrigger.OcrCompleted, rule.Trigger);
        Assert.AreEqual("browser", rule.SourceAppContains);
        Assert.AreEqual("checkout", rule.WindowTitleContains);
        Assert.AreEqual("ActiveWindow", rule.CaptureKind);
        Assert.AreEqual("DISPLAY1", rule.MonitorContains);
        Assert.AreEqual("Support", rule.HotkeyProfile);
        Assert.AreEqual("png", rule.FileExtension);
        Assert.AreEqual(100L, rule.MinFileSizeBytes);
        Assert.AreEqual(500L, rule.MaxFileSizeBytes);
        Assert.AreEqual("token", rule.OcrContains);
        Assert.AreEqual(true, rule.RequiresSensitiveData);
        Assert.AreEqual(VisualRedactionMode.Pixelate, rule.ImageEffectMode);
        Assert.AreEqual("10,10,80,80", rule.ImageEffectRegion);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.ApplyImageEffect);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.RedactDetectedSensitiveData);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.GenerateDocument);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.ShowNotification);
        Assert.IsFalse(settings.AutomationRules.Any(rule => rule.Id == SettingsQuickAutomationRule.LegacyRuleId));
    }

    [TestMethod]
    public void Save_RemovesQuickRuleWhenNoActionsAreSelected()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                new AutomationRule
                {
                    Id = SettingsQuickAutomationRule.RuleId,
                    Actions = [AutomationActionKind.ShowNotification]
                },
                new AutomationRule
                {
                    Id = "other-rule",
                    Actions = [AutomationActionKind.CopyPathToClipboard]
                }
            ]
        };

        SettingsQuickAutomationRule.Save(settings, new QuickAutomationRuleDraft());

        Assert.AreEqual(1, settings.AutomationRules.Count);
        Assert.AreEqual("other-rule", settings.AutomationRules[0].Id);
    }

    [TestMethod]
    public void CloneRulesForManager_MigratesLegacyIdAndSkipsItWhenCurrentQuickRuleExists()
    {
        var legacyOnly = SettingsAutomationRuleManager.CloneRulesForManager(
        [
            new AutomationRule
            {
                Id = SettingsQuickAutomationRule.LegacyRuleId,
                Name = "Legacy",
                Actions = [AutomationActionKind.CopyImageToClipboard]
            }
        ]);

        Assert.AreEqual(SettingsQuickAutomationRule.RuleId, legacyOnly.Single().Id);

        var withCurrent = SettingsAutomationRuleManager.CloneRulesForManager(
        [
            new AutomationRule
            {
                Id = SettingsQuickAutomationRule.RuleId,
                Name = "Current",
                Actions = [AutomationActionKind.ShowNotification]
            },
            new AutomationRule
            {
                Id = SettingsQuickAutomationRule.LegacyRuleId,
                Name = "Legacy",
                Actions = [AutomationActionKind.CopyImageToClipboard]
            }
        ]);

        Assert.AreEqual(1, withCurrent.Count);
        Assert.AreEqual("Current", withCurrent[0].Name);
    }

    [TestMethod]
    public void CreateNewRule_UsesDisabledNotificationRuleAndUniqueName()
    {
        var rule = SettingsAutomationRuleManager.CreateNewRule(
        [
            new AutomationRule
            {
                Name = "New workflow rule",
                Actions = [AutomationActionKind.ShowNotification]
            }
        ]);

        Assert.IsFalse(rule.IsEnabled);
        Assert.AreEqual("New workflow rule 2", rule.Name);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.ShowNotification);
    }

    [TestMethod]
    public void ApplyDraft_RoundTripsAdvancedFieldsAndPreservesHiddenActions()
    {
        var rule = new AutomationRule
        {
            Id = "advanced",
            Name = "Advanced",
            Trigger = AutomationTrigger.UploadCompleted,
            CaptureKind = "Region",
            MonitorContains = "DISPLAY1",
            MinFileSizeBytes = 100,
            MaxFileSizeBytes = 500,
            ImageEffectMode = VisualRedactionMode.Blur,
            ImageEffectRegion = "10,10,80,80",
            Actions =
            [
                AutomationActionKind.ApplyImageEffect,
                AutomationActionKind.CopyPathToClipboard,
                AutomationActionKind.DeleteLocalFile
            ]
        };

        var draft = SettingsAutomationRuleManager.ToDraft(rule);
        Assert.AreEqual("Region", draft.CaptureKind);
        Assert.AreEqual("DISPLAY1", draft.MonitorContains);
        Assert.AreEqual(100L, draft.MinFileSizeBytes);
        Assert.AreEqual(500L, draft.MaxFileSizeBytes);
        Assert.AreEqual(VisualRedactionMode.Blur, draft.ImageEffectMode);
        Assert.AreEqual("10,10,80,80", draft.ImageEffectRegion);
        CollectionAssert.Contains(draft.Actions, AutomationActionKind.ApplyImageEffect);
        CollectionAssert.Contains(draft.Actions, AutomationActionKind.CopyPathToClipboard);
        Assert.IsFalse(draft.Actions.Contains(AutomationActionKind.DeleteLocalFile));

        draft.Name = "Updated";
        draft.Trigger = AutomationTrigger.OcrCompleted;
        draft.CaptureKind = "ActiveWindow";
        draft.MonitorContains = "DISPLAY2";
        draft.MinFileSizeBytes = 256;
        draft.MaxFileSizeBytes = 4096;
        draft.ImageEffectMode = VisualRedactionMode.Pixelate;
        draft.ImageEffectRegion = "0,0,50,100";
        draft.Actions = [AutomationActionKind.ShowNotification];

        SettingsAutomationRuleManager.ApplyDraft(rule, draft);

        Assert.AreEqual("Updated", rule.Name);
        Assert.AreEqual(AutomationTrigger.OcrCompleted, rule.Trigger);
        Assert.AreEqual("ActiveWindow", rule.CaptureKind);
        Assert.AreEqual("DISPLAY2", rule.MonitorContains);
        Assert.AreEqual(256L, rule.MinFileSizeBytes);
        Assert.AreEqual(4096L, rule.MaxFileSizeBytes);
        Assert.AreEqual(VisualRedactionMode.Pixelate, rule.ImageEffectMode);
        Assert.AreEqual("0,0,50,100", rule.ImageEffectRegion);
        Assert.IsFalse(rule.Actions.Contains(AutomationActionKind.ApplyImageEffect));
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.ShowNotification);
        CollectionAssert.Contains(rule.Actions, AutomationActionKind.DeleteLocalFile);
        Assert.IsFalse(rule.Actions.Contains(AutomationActionKind.CopyPathToClipboard));
    }

    [TestMethod]
    public void PrepareRulesForSave_DropsEmptyActionDrafts()
    {
        var rules = SettingsAutomationRuleManager.PrepareRulesForSave(
        [
            new AutomationRule
            {
                Id = "empty",
                Name = "Empty"
            },
            new AutomationRule
            {
                Id = "ready",
                Name = "Ready",
                Actions = [AutomationActionKind.ShowNotification]
            }
        ]);

        Assert.AreEqual(1, rules.Count);
        Assert.AreEqual("ready", rules[0].Id);
    }

    [TestMethod]
    public void CreateListItems_SummarizesEnabledStateTriggerConditionsAndActions()
    {
        var item = SettingsAutomationRuleManager.CreateListItems(
        [
            new AutomationRule
            {
                Id = "rule",
                Name = "OCR alert",
                Trigger = AutomationTrigger.OcrCompleted,
                OcrContains = "token",
                RequiresSensitiveData = true,
                Actions =
                [
                    AutomationActionKind.RedactDetectedSensitiveData,
                    AutomationActionKind.GenerateDocument,
                    AutomationActionKind.ShowNotification,
                    AutomationActionKind.CopyPathToClipboard
                ]
            }
        ]).Single();

        StringAssert.StartsWith(item.DisplayText, "On: OCR alert");
        StringAssert.Contains(item.DisplayText, "OcrCompleted");
        StringAssert.Contains(item.DisplayText, "2 condition(s)");
        StringAssert.Contains(item.DisplayText, "+1 more");
    }
}

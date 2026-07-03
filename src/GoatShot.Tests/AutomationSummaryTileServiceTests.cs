using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AutomationSummaryTileServiceTests
{
    [TestMethod]
    public void Create_ReportsEmptyStateWithNeutralTones()
    {
        var tiles = new AutomationSummaryTileService().Create(
            Array.Empty<AutomationRule>(),
            watchFoldersEnabled: false,
            Array.Empty<string>());

        Assert.AreEqual(3, tiles.Count);
        Assert.AreEqual("Workflow rules", tiles[0].Title);
        Assert.AreEqual("0/0", tiles[0].Value);
        Assert.AreEqual("No saved rules", tiles[0].Detail);
        Assert.AreEqual("Neutral", tiles[0].Tone);
        Assert.AreEqual("Watch folders", tiles[1].Title);
        Assert.AreEqual("0", tiles[1].Value);
        Assert.AreEqual("Watching disabled", tiles[1].Detail);
        Assert.AreEqual("Neutral", tiles[1].Tone);
        Assert.AreEqual("Rule triggers", tiles[2].Title);
        Assert.AreEqual("0", tiles[2].Value);
        Assert.AreEqual("No active triggers", tiles[2].Detail);
        Assert.AreEqual("Neutral", tiles[2].Tone);
    }

    [TestMethod]
    public void Create_CountsEnabledRulesAndListsUpToTwoNames()
    {
        var tiles = new AutomationSummaryTileService().Create(
            new[]
            {
                Rule("Capture to document", enabled: true, AutomationTrigger.CaptureCreated),
                Rule("Upload notify", enabled: true, AutomationTrigger.UploadCompleted),
                Rule("Recording report", enabled: false, AutomationTrigger.RecordingCompleted)
            },
            watchFoldersEnabled: true,
            new[] { @"C:\Drop", @"C:\Scans" });

        Assert.AreEqual("2/3", tiles[0].Value);
        Assert.AreEqual("Capture to document, Upload notify, +1 more", tiles[0].Detail);
        Assert.AreEqual("Ready", tiles[0].Tone);
        Assert.AreEqual("2", tiles[1].Value);
        Assert.AreEqual("Watching while GoatShot runs", tiles[1].Detail);
        Assert.AreEqual("Ready", tiles[1].Tone);
    }

    [TestMethod]
    public void Create_CountsDistinctTriggersAmongEnabledRulesOnly()
    {
        var tiles = new AutomationSummaryTileService().Create(
            new[]
            {
                Rule("Copy region captures", enabled: true, AutomationTrigger.CaptureCreated),
                Rule("Document region captures", enabled: true, AutomationTrigger.CaptureCreated),
                Rule("OCR follow-up", enabled: true, AutomationTrigger.OcrCompleted),
                Rule("Recording report", enabled: false, AutomationTrigger.RecordingCompleted)
            },
            watchFoldersEnabled: false,
            Array.Empty<string>());

        Assert.AreEqual("2", tiles[2].Value);
        Assert.AreEqual("CaptureCreated, OcrCompleted", tiles[2].Detail);
        Assert.AreEqual("Ready", tiles[2].Tone);
    }

    [TestMethod]
    public void Create_DisabledRulesAndEmptyFolderListStayNeutral()
    {
        var tiles = new AutomationSummaryTileService().Create(
            new[] { Rule("Only draft", enabled: false, AutomationTrigger.CaptureCreated) },
            watchFoldersEnabled: true,
            Array.Empty<string>());

        Assert.AreEqual("0/1", tiles[0].Value);
        Assert.AreEqual("Only draft", tiles[0].Detail);
        Assert.AreEqual("Neutral", tiles[0].Tone);
        Assert.AreEqual("Watching while GoatShot runs", tiles[1].Detail);
        Assert.AreEqual("Neutral", tiles[1].Tone);
        Assert.AreEqual("No active triggers", tiles[2].Detail);
        Assert.AreEqual("Neutral", tiles[2].Tone);
    }

    private static AutomationRule Rule(string name, bool enabled, AutomationTrigger trigger)
    {
        return new AutomationRule
        {
            Name = name,
            IsEnabled = enabled,
            Trigger = trigger
        };
    }
}

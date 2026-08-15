using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SettingsSearchIndexTests
{
    private static readonly SettingsSearchEntry[] Entries =
    [
        new("Library root", "General", "General"),
        new("Filename template", "General", "General"),
        new("Queue poll interval seconds", "Sharing", "Sharing"),
        new("Default share destination", "Sharing", "Sharing"),
        new("Include subdirectories", "Automation", "Automation"),
        new("Start or stop recording", "Keybinds", "Keybinds"),
        new("Frames per second", "Recording", "Recording")
    ];

    private static string[] Match(string query, int limit = 10) =>
        SettingsSearchIndex.Match(Entries, query, limit).Select(entry => entry.Label).ToArray();

    [TestMethod]
    public void Match_ReturnsNothingForAnEmptyQuery()
    {
        Assert.AreEqual(0, SettingsSearchIndex.Match(Entries, "   ", 10).Count);
        Assert.AreEqual(0, SettingsSearchIndex.Match(Entries, null, 10).Count);
    }

    [TestMethod]
    public void Match_FindsSettingsByAnyWordRegardlessOfCase()
    {
        CollectionAssert.AreEqual(new[] { "Queue poll interval seconds" }, Match("QUEUE"));
        CollectionAssert.AreEqual(new[] { "Queue poll interval seconds" }, Match("poll"));
    }

    [TestMethod]
    public void Match_RanksAPrefixHitAboveAMidWordHit()
    {
        var results = Match("s");

        Assert.IsTrue(
            Array.IndexOf(results, "Start or stop recording") < Array.IndexOf(results, "Queue poll interval seconds"),
            "A label starting with the query should outrank one that merely contains it.");
    }

    [TestMethod]
    public void Match_AllowsFindingASettingByItsSectionName()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Include subdirectories" },
            Match("automation"));
    }

    [TestMethod]
    public void Match_RequiresEveryTermSoRefiningNarrowsResults()
    {
        CollectionAssert.AreEqual(new[] { "Queue poll interval seconds" }, Match("queue poll"));
        Assert.AreEqual(0, Match("queue recording").Length);
    }

    [TestMethod]
    public void Match_HonoursTheResultLimit()
    {
        Assert.AreEqual(2, SettingsSearchIndex.Match(Entries, "e", 2).Count);
    }

    [TestMethod]
    public void Match_IgnoresEntriesWithoutAUsableLabel()
    {
        SettingsSearchEntry[] entries =
        [
            new("   ", "General", "General"),
            new("Library root", "General", "General")
        ];

        CollectionAssert.AreEqual(
            new[] { "Library root" },
            SettingsSearchIndex.Match(entries, "r", 10).Select(entry => entry.Label).ToArray());
    }

    [TestMethod]
    public void Match_DeduplicatesRepeatedLabelsWithinTheSameSection()
    {
        SettingsSearchEntry[] entries =
        [
            new("Include subdirectories", "Automation", "Automation"),
            new("Include subdirectories", "Automation", "Automation")
        ];

        Assert.AreEqual(1, SettingsSearchIndex.Match(entries, "include", 10).Count);
    }
}

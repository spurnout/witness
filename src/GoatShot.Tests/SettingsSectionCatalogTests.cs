using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SettingsSectionCatalogTests
{
    [TestMethod]
    public void All_ProvidesPrimarySettingsSectionsInNavigationOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "General", "Keybinds", "Recording", "Sharing", "Automation", "Plugins", "Ai" },
            SettingsSectionCatalog.All.Select(section => section.Key).ToArray());

        Assert.IsTrue(SettingsSectionCatalog.All.All(section => !string.IsNullOrWhiteSpace(section.Label)));
        Assert.IsTrue(SettingsSectionCatalog.All.All(section => !string.IsNullOrWhiteSpace(section.Description)));
    }

    [TestMethod]
    public void Find_IsCaseInsensitiveAndRejectsUnknownKeys()
    {
        Assert.AreEqual("Sharing", SettingsSectionCatalog.Find("sharing")?.Label);
        Assert.AreEqual("Plugins", SettingsSectionCatalog.Find("plugins")?.Label);
        Assert.AreEqual("AI", SettingsSectionCatalog.Find("AI")?.Label);
        Assert.IsNull(SettingsSectionCatalog.Find("missing"));
        Assert.IsNull(SettingsSectionCatalog.Find(""));
    }
}

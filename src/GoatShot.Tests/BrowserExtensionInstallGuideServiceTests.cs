using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionInstallGuideServiceTests
{
    [TestMethod]
    public void CreateGuide_IncludesBrowserSpecificNativeHostState()
    {
        var status = new BrowserNativeHostStatus
        {
            HostName = BrowserNativeHostRegistrationService.HostName,
            ManifestRoot = @"C:\GoatShot\native-host",
            Registrations =
            {
                new BrowserNativeHostRegistrationState
                {
                    Browser = BrowserNativeHostBrowser.Chrome,
                    Installed = true,
                    ManifestPath = @"C:\GoatShot\native-host\chrome.json"
                }
            }
        };

        var result = new BrowserExtensionInstallGuideService().CreateGuide(new BrowserExtensionInstallGuideRequest
        {
            Browser = "chrome",
            ExtensionSourceDirectory = ".",
            ExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            NativeHostStatus = status
        });

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual("Chrome", result.Entries[0].Browser);
        Assert.IsTrue(result.Entries[0].NativeHostInstalled);
        Assert.AreEqual("not-detectable-from-desktop", result.Entries[0].ExtensionInstalledState);
        StringAssert.Contains(result.Markdown, "chrome://extensions");
        StringAssert.Contains(result.Markdown, "Host Status");
    }

    [TestMethod]
    public void CreateGuide_AllBrowsersWhenBrowserOmitted()
    {
        var result = new BrowserExtensionInstallGuideService().CreateGuide(new BrowserExtensionInstallGuideRequest
        {
            ExtensionSourceDirectory = ".",
            NativeHostStatus = new BrowserNativeHostStatus()
        });

        Assert.AreEqual(3, result.Entries.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Chrome", "Edge", "Firefox" },
            result.Entries.Select(entry => entry.Browser).ToArray());
        StringAssert.Contains(result.Markdown, "about:debugging");
    }
}

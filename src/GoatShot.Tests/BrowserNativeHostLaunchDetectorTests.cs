using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserNativeHostLaunchDetectorTests
{
    [TestMethod]
    public void Resolve_BareRedirectedWinExeLaunch_RemainsInteractive()
    {
        var resolved = BrowserNativeHostLaunchDetector.Resolve([], standardInputIsRedirected: true);

        Assert.AreEqual(0, resolved.Length);
    }

    [TestMethod]
    public void Resolve_ChromeNativeMessagingLaunch_UsesGovernedRuntimeVerb()
    {
        var resolved = BrowserNativeHostLaunchDetector.Resolve(
            ["chrome-extension://abcdefghijklmnopabcdefghijklmnop/", "--parent-window=42"],
            standardInputIsRedirected: true);

        CollectionAssert.AreEqual(new[] { "--browser-native-host" }, resolved);
    }

    [TestMethod]
    public void Resolve_FirefoxNativeMessagingLaunch_UsesGovernedRuntimeVerb()
    {
        var resolved = BrowserNativeHostLaunchDetector.Resolve(
            ["moz-extension://goatshot-page-capture/"],
            standardInputIsRedirected: true);

        CollectionAssert.AreEqual(new[] { "--browser-native-host" }, resolved);
    }

    [TestMethod]
    public void Resolve_NonBrowserArguments_ArePreserved()
    {
        var resolved = BrowserNativeHostLaunchDetector.Resolve(
            ["--render-main-output", "main.png"],
            standardInputIsRedirected: true);

        CollectionAssert.AreEqual(new[] { "--render-main-output", "main.png" }, resolved);
    }

    [TestMethod]
    public void Resolve_UnredirectedBrowserLikeArgument_IsPreserved()
    {
        var resolved = BrowserNativeHostLaunchDetector.Resolve(
            ["chrome-extension://abcdefghijklmnopabcdefghijklmnop/"],
            standardInputIsRedirected: false);

        CollectionAssert.AreEqual(
            new[] { "chrome-extension://abcdefghijklmnopabcdefghijklmnop/" },
            resolved);
    }
}

using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionInstallPlanServiceTests
{
    [TestMethod]
    public async Task CreateAsync_AllBrowsersWritesReadOnlyPlan()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "install-plan");

        var result = await new BrowserExtensionInstallPlanService().CreateAsync(new BrowserExtensionInstallPlanRequest
        {
            Browser = "all",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            ChromeExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            EdgeExtensionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            FirefoxExtensionId = "goatshot@example.invalid",
            NativeHostStatus = NativeStatus(installedBrowser: BrowserNativeHostBrowser.Chrome),
            PolicyAllowed = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Entries.SelectMany(entry => entry.Issues)));
        CollectionAssert.AreEquivalent(
            new[] { "chrome", "edge", "firefox" },
            result.Entries.Select(entry => entry.Browser).ToArray());

        var chrome = result.Entries.Single(entry => entry.Browser == "chrome");
        Assert.AreEqual("manual-install-plan-ready", chrome.Status);
        Assert.AreEqual("not-supported-read-only-plan", chrome.AutomaticInstallStatus);
        Assert.IsFalse(chrome.DesktopCanDetectInstalledExtension);
        Assert.IsTrue(chrome.NativeHostInstalled);
        Assert.IsTrue(chrome.Commands.Any(command => command.Contains("native-host install", StringComparison.OrdinalIgnoreCase)));

        foreach (var entry in result.Entries)
        {
            Assert.AreEqual("manual-install-plan-ready", entry.Status);
            Assert.IsTrue(entry.ExtensionSourceExists);
            Assert.IsTrue(entry.RequiredEvidence.Any(item => item.Contains("Host Status", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(entry.Warnings.Any(item => item.Contains("cannot prove the extension is installed", StringComparison.OrdinalIgnoreCase)));
        }

        Assert.IsTrue(result.NonGoals.Any(item => item.Contains("does not install browser extensions", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.AuthorityBoundaries.Any(item => item.Contains("cannot honestly claim extension installation", StringComparison.OrdinalIgnoreCase)));
        AssertGeneratedFile(output, "install-plan.md");
        AssertGeneratedFile(output, "install-plan.json");

        var markdown = await File.ReadAllTextAsync(Path.Combine(output, "install-plan.md"));
        StringAssert.Contains(markdown, "GoatShot Browser Extension Install Plan");
        StringAssert.Contains(markdown, "does not install browser extensions");
        StringAssert.Contains(markdown, "Host Status");
    }

    [TestMethod]
    public async Task CreateAsync_DetectsGeneratedStorePackageArtifacts()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var packageRoot = Path.Combine(root, "store-package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "chrome"));
        var extensionZip = Path.Combine(packageRoot, "chrome", "goatshot-browser-extension-chrome-v0.1.0.zip");
        var submissionZip = Path.Combine(packageRoot, "goatshot-browser-extension-chrome-store-submission.zip");
        await File.WriteAllTextAsync(extensionZip, "zip-placeholder");
        await File.WriteAllTextAsync(submissionZip, "submission-placeholder");

        var result = await new BrowserExtensionInstallPlanService().CreateAsync(new BrowserExtensionInstallPlanRequest
        {
            Browser = "chrome",
            ExtensionSourceDirectory = source,
            StorePackageRoot = packageRoot,
            OutputPath = Path.Combine(root, "install-plan"),
            NativeHostStatus = NativeStatus(),
            PolicyAllowed = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Entries.SelectMany(entry => entry.Issues)));
        var entry = result.Entries.Single();
        Assert.IsTrue(entry.StorePackageAvailable);
        Assert.AreEqual(extensionZip, entry.StorePackagePath);
        Assert.AreEqual(submissionZip, entry.StoreSubmissionBundlePath);
        Assert.IsTrue(entry.ManualSteps.Any(step => step.Contains(extensionZip, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CreateAsync_ManagedPolicyBlockDoesNotClaimPlanReady()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "install-plan");

        var result = await new BrowserExtensionInstallPlanService().CreateAsync(new BrowserExtensionInstallPlanRequest
        {
            Browser = "edge",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            NativeHostStatus = NativeStatus(),
            PolicyAllowed = false,
            PolicyReason = "Browser extension handoff is disabled by managed policy."
        });

        Assert.IsFalse(result.Succeeded);
        var entry = result.Entries.Single();
        Assert.AreEqual("blocked-by-managed-policy", entry.Status);
        Assert.IsTrue(entry.Issues.Any(issue => issue.Contains("managed policy", StringComparison.OrdinalIgnoreCase)));

        var markdown = await File.ReadAllTextAsync(Path.Combine(output, "install-plan.md"));
        StringAssert.Contains(markdown, "Policy allowed: `False`");
        StringAssert.Contains(markdown, "blocked-by-managed-policy");
    }

    [TestMethod]
    public async Task CreateAsync_MissingSourceAndPackageBlocksPlan()
    {
        var root = TestRoot();
        var missingSource = Path.Combine(root, "missing-extension");

        var result = await new BrowserExtensionInstallPlanService().CreateAsync(new BrowserExtensionInstallPlanRequest
        {
            Browser = "firefox",
            ExtensionSourceDirectory = missingSource,
            OutputPath = Path.Combine(root, "install-plan"),
            NativeHostStatus = NativeStatus(),
            PolicyAllowed = true
        });

        Assert.IsFalse(result.Succeeded);
        var entry = result.Entries.Single();
        Assert.AreEqual("blocked-missing-extension-source", entry.Status);
        Assert.IsFalse(entry.ExtensionSourceExists);
        Assert.IsFalse(entry.StorePackageAvailable);
        Assert.IsTrue(entry.Issues.Any(issue => issue.Contains("Extension source", StringComparison.OrdinalIgnoreCase)));
    }

    private static string TestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateExtensionSource(string root)
    {
        var source = Path.Combine(root, "browser-extension");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "manifest.json"), "{}");
        return source;
    }

    private static BrowserNativeHostStatus NativeStatus(BrowserNativeHostBrowser? installedBrowser = null)
    {
        var status = new BrowserNativeHostStatus
        {
            HostName = BrowserNativeHostRegistrationService.HostName,
            ManifestRoot = @"C:\GoatShot\native-host"
        };

        foreach (var browser in new[] { BrowserNativeHostBrowser.Chrome, BrowserNativeHostBrowser.Edge, BrowserNativeHostBrowser.Firefox })
        {
            status.Registrations.Add(new BrowserNativeHostRegistrationState
            {
                Browser = browser,
                Installed = browser == installedBrowser,
                ManifestPath = browser == installedBrowser
                    ? $@"C:\GoatShot\native-host\{browser.ToString().ToLowerInvariant()}.json"
                    : string.Empty,
                Message = browser == installedBrowser ? "Installed." : "Not installed."
            });
        }

        return status;
    }

    private static void AssertGeneratedFile(string output, string fileName)
    {
        Assert.IsTrue(File.Exists(Path.Combine(output, fileName)), $"{fileName} was not generated.");
    }
}

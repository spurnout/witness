using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionEnterprisePolicyPlanServiceTests
{
    [TestMethod]
    public async Task CreateAsync_AllTargetsWritesTemplatesWithoutApplyingPolicy()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "enterprise-policy-plan");

        var result = await new BrowserExtensionEnterprisePolicyPlanService().CreateAsync(new BrowserExtensionEnterprisePolicyPlanRequest
        {
            Browser = "all",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            ChromeExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            EdgeExtensionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            FirefoxExtensionId = "goatshot@example.invalid",
            FirefoxInstallUrl = "https://example.invalid/goatshot.xpi",
            PolicyAllowed = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Entries.SelectMany(entry => entry.Issues)));
        CollectionAssert.AreEquivalent(
            new[] { "chrome", "edge", "firefox" },
            result.Entries.Select(entry => entry.Browser).ToArray());
        Assert.IsFalse(result.WouldApplyPolicy);
        Assert.IsFalse(result.WouldWriteRegistry);
        Assert.IsFalse(result.WouldInstallExtension);
        Assert.IsTrue(result.NonGoals.Any(item => item.Contains("does not apply", StringComparison.OrdinalIgnoreCase)));

        foreach (var entry in result.Entries)
        {
            Assert.AreEqual("enterprise-policy-template-ready", entry.Status);
            Assert.IsFalse(entry.WouldApplyPolicy);
            Assert.IsFalse(entry.WouldInstallExtension);
            Assert.IsTrue(File.Exists(entry.PolicyTemplatePath));
            Assert.IsTrue(entry.RequiredEvidence.Any(item => item.Contains("Host Status", StringComparison.OrdinalIgnoreCase)));
        }

        var chromeReg = await File.ReadAllTextAsync(Path.Combine(output, "chrome-extension-install-forcelist.reg"));
        StringAssert.Contains(chromeReg, @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist");
        StringAssert.Contains(chromeReg, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;https://clients2.google.com/service/update2/crx");

        var edgeReg = await File.ReadAllTextAsync(Path.Combine(output, "edge-extension-install-forcelist.reg"));
        StringAssert.Contains(edgeReg, @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist");
        StringAssert.Contains(edgeReg, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb;https://edge.microsoft.com/extensionwebstorebase/v1/crx");

        var firefoxPolicy = await File.ReadAllTextAsync(Path.Combine(output, "firefox-policies.json"));
        StringAssert.Contains(firefoxPolicy, "force_installed");
        StringAssert.Contains(firefoxPolicy, "https://example.invalid/goatshot.xpi");

        AssertGeneratedFile(output, "enterprise-policy-plan.md");
        AssertGeneratedFile(output, "enterprise-policy-plan.json");
        var markdown = await File.ReadAllTextAsync(Path.Combine(output, "enterprise-policy-plan.md"));
        StringAssert.Contains(markdown, "Would apply policy: `False`");
        StringAssert.Contains(markdown, "Native-host deployment remains separate");
    }

    [TestMethod]
    public async Task CreateAsync_MissingChromeExtensionIdBlocksTargetButWritesTemplate()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "enterprise-policy-plan");

        var result = await new BrowserExtensionEnterprisePolicyPlanService().CreateAsync(new BrowserExtensionEnterprisePolicyPlanRequest
        {
            Browser = "chrome",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            PolicyAllowed = true
        });

        Assert.IsFalse(result.Succeeded);
        var entry = result.Entries.Single();
        Assert.AreEqual("blocked-missing-extension-id", entry.Status);
        Assert.IsTrue(entry.Issues.Any(issue => issue.Contains("extension id", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(File.Exists(entry.PolicyTemplatePath));

        var registryTemplate = await File.ReadAllTextAsync(entry.PolicyTemplatePath);
        StringAssert.Contains(registryTemplate, "<reviewed-extension-id>;<reviewed-update-url>");
    }

    [TestMethod]
    public async Task CreateAsync_ManagedPolicyBlockDoesNotClaimReady()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "enterprise-policy-plan");

        var result = await new BrowserExtensionEnterprisePolicyPlanService().CreateAsync(new BrowserExtensionEnterprisePolicyPlanRequest
        {
            Browser = "edge",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            EdgeExtensionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            PolicyAllowed = false,
            PolicyReason = "Browser extension handoff is disabled by managed policy."
        });

        Assert.IsFalse(result.Succeeded);
        var entry = result.Entries.Single();
        Assert.AreEqual("blocked-by-managed-policy", entry.Status);
        Assert.IsFalse(entry.WouldApplyPolicy);
        Assert.IsFalse(entry.WouldInstallExtension);
        Assert.IsTrue(entry.Issues.Any(issue => issue.Contains("managed policy", StringComparison.OrdinalIgnoreCase)));

        var markdown = await File.ReadAllTextAsync(Path.Combine(output, "enterprise-policy-plan.md"));
        StringAssert.Contains(markdown, "Policy allowed: `False`");
        StringAssert.Contains(markdown, "blocked-by-managed-policy");
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

    private static void AssertGeneratedFile(string output, string fileName)
    {
        Assert.IsTrue(File.Exists(Path.Combine(output, fileName)), $"{fileName} was not generated.");
    }
}

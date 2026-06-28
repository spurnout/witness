using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionInstallAssistServiceTests
{
    [TestMethod]
    public async Task CreateAsync_ChromeWritesTemporaryIsolatedLaunchArtifacts()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);
        var output = Path.Combine(root, "install-assist");

        var result = await new BrowserExtensionInstallAssistService().CreateAsync(new BrowserExtensionInstallAssistRequest
        {
            Browser = "chrome",
            ExtensionSourceDirectory = source,
            OutputPath = output,
            StartUrl = "about:blank",
            RemoteDebuggingPort = 58618,
            PolicyAllowed = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
        Assert.AreEqual("chrome", result.Browser);
        Assert.AreEqual("isolated-profile-load-extension", result.Mode);
        Assert.IsTrue(result.SourceExists);
        Assert.IsTrue(result.SupportsAutomatedLaunch);
        Assert.IsFalse(result.Started);
        Assert.IsFalse(result.MutationFlags.MutatesExistingBrowserProfile);
        Assert.IsFalse(result.MutationFlags.InstallsExtensionPermanently);
        Assert.IsFalse(result.MutationFlags.ContactsBrowserStoreAccount);
        Assert.IsFalse(result.MutationFlags.WritesRegistryOrEnterprisePolicy);
        Assert.IsTrue(result.BrowserArguments.Any(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal)));
        Assert.IsTrue(result.BrowserArguments.Any(argument => argument.StartsWith("--load-extension=", StringComparison.Ordinal)));
        Assert.IsTrue(result.BrowserArguments.Contains("--remote-debugging-port=58618"));
        AssertGeneratedFile(result.LaunchScriptPath);
        AssertGeneratedFile(result.LaunchPlanPath);
        AssertGeneratedFile(result.AssistGuidePath);
        AssertGeneratedFile(result.AssistJsonPath);

        var guide = await File.ReadAllTextAsync(result.AssistGuidePath);
        StringAssert.Contains(guide, "temporary");
        StringAssert.Contains(guide, "Does not publish");
        StringAssert.Contains(guide, "Installs extension permanently: `False`");

        var script = await File.ReadAllTextAsync(result.LaunchScriptPath);
        StringAssert.Contains(script, "--disable-extensions-except");
        StringAssert.Contains(script, "--load-extension");
        StringAssert.Contains(script, "Temporary isolated-profile extension loading only");
    }

    [TestMethod]
    public async Task CreateAsync_FirefoxWritesManualTemporaryLoadBoundary()
    {
        var root = TestRoot();
        var source = CreateExtensionSource(root);

        var result = await new BrowserExtensionInstallAssistService().CreateAsync(new BrowserExtensionInstallAssistRequest
        {
            Browser = "firefox",
            ExtensionSourceDirectory = source,
            OutputPath = Path.Combine(root, "install-assist"),
            PolicyAllowed = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
        Assert.AreEqual("firefox", result.Browser);
        Assert.AreEqual("manual-temporary-load", result.Mode);
        Assert.IsFalse(result.SupportsAutomatedLaunch);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.ProfileDirectory));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.LaunchScriptPath));
        Assert.IsTrue(result.Commands.Any(command => command.Contains("about:debugging", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("manual browser action", StringComparison.OrdinalIgnoreCase)));
        AssertGeneratedFile(result.LaunchPlanPath);
        AssertGeneratedFile(result.AssistGuidePath);
        AssertGeneratedFile(result.AssistJsonPath);

        var guide = await File.ReadAllTextAsync(result.AssistGuidePath);
        StringAssert.Contains(guide, "Supports automated launch: `False`");
        StringAssert.Contains(guide, "browser store, or enterprise policy");
    }

    [TestMethod]
    public async Task CreateAsync_MissingSourceBlocksAssistButStillWritesArtifacts()
    {
        var root = TestRoot();
        var missingSource = Path.Combine(root, "missing-extension");

        var result = await new BrowserExtensionInstallAssistService().CreateAsync(new BrowserExtensionInstallAssistRequest
        {
            Browser = "edge",
            ExtensionSourceDirectory = missingSource,
            OutputPath = Path.Combine(root, "install-assist"),
            PolicyAllowed = true
        });

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.SourceExists);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("manifest.json", StringComparison.OrdinalIgnoreCase)));
        AssertGeneratedFile(result.LaunchScriptPath);
        AssertGeneratedFile(result.LaunchPlanPath);
        AssertGeneratedFile(result.AssistGuidePath);
        AssertGeneratedFile(result.AssistJsonPath);
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

    private static void AssertGeneratedFile(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected generated file was missing: {path}");
    }
}

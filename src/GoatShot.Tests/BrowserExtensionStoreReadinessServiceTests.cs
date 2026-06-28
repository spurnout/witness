using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionStoreReadinessServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WritesStoreReadinessArtifactsAndPermissionRationale()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-readiness");
        try
        {
            var result = await new BrowserExtensionStoreReadinessService().CreateAsync(new BrowserExtensionStoreReadinessRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "all",
                SupportUrl = "https://example.invalid/goatshot-support",
                PrivacyUrl = "https://example.invalid/goatshot-privacy"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(4, result.Targets.Count);
            CollectionAssert.AreEquivalent(
                new[] { "local", "chrome", "edge", "firefox" },
                result.Targets.Select(target => target.Target).ToArray());
            Assert.IsTrue(result.GeneratedFiles.Count >= 5);
            AssertGeneratedFile(output, "store-readiness.md");
            AssertGeneratedFile(output, "permission-rationale.md");
            AssertGeneratedFile(output, "privacy-data-use.md");
            AssertGeneratedFile(output, "screenshots-checklist.md");
            AssertGeneratedFile(output, "store-readiness.json");

            var rationale = await File.ReadAllTextAsync(Path.Combine(output, "permission-rationale.md"));
            StringAssert.Contains(rationale, "nativeMessaging");
            StringAssert.Contains(rationale, "downloads");
            StringAssert.Contains(rationale, "host:<all_urls>");

            var privacy = await File.ReadAllTextAsync(Path.Combine(output, "privacy-data-use.md"));
            StringAssert.Contains(privacy, "Not Collected In This Prototype");
            StringAssert.Contains(privacy, "Cookies.");
            StringAssert.Contains(privacy, "Automatic uploads.");

            var readiness = await File.ReadAllTextAsync(Path.Combine(output, "store-readiness.md"));
            StringAssert.Contains(readiness, "It is not browser-store publication");
            StringAssert.Contains(readiness, "Host Status screenshots remain separate manual evidence lanes");
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_MarksFirefoxAsManualBrowserSpecificReviewBoundary()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-readiness");
        try
        {
            var result = await new BrowserExtensionStoreReadinessService().CreateAsync(new BrowserExtensionStoreReadinessRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "firefox",
                SupportUrl = "https://example.invalid/goatshot-support",
                PrivacyUrl = "https://example.invalid/goatshot-privacy"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(1, result.Targets.Count);
            Assert.AreEqual("firefox", result.Targets[0].Target);
            Assert.AreEqual("manual-browser-specific-review-required", result.Targets[0].Status);
            Assert.IsTrue(result.Targets[0].Warnings.Any(warning =>
                warning.Contains("browser-specific MV3/native-host behavior", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_ReportsPackageValidationIssuesBeforeStoreChecklist()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "name": "Bad Store Fixture",
                  "version": "0.1.0",
                  "permissions": ["activeTab", "downloads", "storage"],
                  "host_permissions": ["https://*/*"],
                  "content_scripts": [{ "matches": ["https://*/*"], "js": ["content-script.js"] }],
                  "background": { "service_worker": "service-worker.js" },
                  "action": { "default_popup": "popup.html" },
                  "options_ui": { "page": "options.html" }
                }
                """);
            await WriteRequiredFilesExceptAsync(root, "manifest.json");
            var output = Path.Combine(root, "readiness");

            var result = await new BrowserExtensionStoreReadinessService().CreateAsync(new BrowserExtensionStoreReadinessRequest
            {
                ExtensionSourceDirectory = root,
                OutputPath = output,
                Target = "chrome",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("nativeMessaging", StringComparison.Ordinal)));
            Assert.AreEqual("blocked", result.Targets.Single().Status);
            Assert.IsTrue(result.Targets.Single().Issues.Any(issue => issue.Contains("nativeMessaging", StringComparison.Ordinal)));
            AssertGeneratedFile(output, "store-readiness.md");
            AssertGeneratedFile(output, "store-readiness.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "browser-extension")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GoatShot repo root.");
    }

    private static async Task WriteRequiredFilesExceptAsync(string root, params string[] excluded)
    {
        var excludedSet = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[]
        {
            "content-script.js",
            "service-worker.js",
            "popup.html",
            "popup.js",
            "options.html",
            "options.js",
            "extension-ui.css"
        })
        {
            if (!excludedSet.Contains(file))
            {
                await File.WriteAllTextAsync(Path.Combine(root, file), "");
            }
        }
    }

    private static void AssertGeneratedFile(string output, string fileName)
    {
        Assert.IsTrue(File.Exists(Path.Combine(output, fileName)), $"{fileName} was not generated.");
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

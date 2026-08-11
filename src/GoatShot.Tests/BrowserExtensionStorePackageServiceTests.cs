using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionStorePackageServiceTests
{
    [TestMethod]
    public async Task CreateAsync_CreatesTargetPackageManifestAndSubmissionBundle()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-package");
        try
        {
            var result = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "chrome",
                SupportUrl = "https://example.invalid/goatshot-support",
                PrivacyUrl = "https://example.invalid/goatshot-privacy",
                ReleaseNotes = "Initial reviewed store submission package."
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Targets.SelectMany(target => target.Issues)));
            Assert.AreEqual(1, result.Targets.Count);

            var target = result.Targets.Single();
            Assert.AreEqual("chrome", target.Target);
            Assert.AreEqual("store-package-created-manual-submission-required", target.Status);
            Assert.IsTrue(File.Exists(target.ExtensionPackagePath), "Extension package was not created.");
            Assert.IsTrue(File.Exists(target.SubmissionBundlePath), "Submission bundle was not created.");
            StringAssert.Contains(Path.GetFileName(target.ExtensionPackagePath), "receipts-browser-extension-");
            StringAssert.Contains(Path.GetFileName(target.SubmissionBundlePath), "receipts-browser-extension-");
            Assert.IsTrue(File.Exists(target.SubmissionManifestPath), "Submission manifest was not created.");
            Assert.AreEqual(64, target.ExtensionPackageSha256.Length);
            Assert.AreEqual(64, target.SubmissionBundleSha256.Length);
            Assert.IsTrue(target.SourceFileHashes.Any(file => file.Path == "manifest.json"));
            Assert.IsTrue(target.ManualEvidenceRequired.Any(item =>
                item.Contains("Host Status", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(target.PublicationNonGoals.Any(item =>
                item.Contains("not browser-store publication proof", StringComparison.OrdinalIgnoreCase)));

            var manifest = await File.ReadAllTextAsync(target.SubmissionManifestPath);
            StringAssert.Contains(manifest, "ExtensionPackageSha256");
            StringAssert.Contains(manifest, "SubmissionBundleSha256");
            StringAssert.Contains(manifest, "ManualEvidenceRequired");

            var checklist = await File.ReadAllTextAsync(Path.Combine(target.TargetFolder, "store-submission-checklist.md"));
            StringAssert.Contains(checklist, "manual submission");
            StringAssert.Contains(checklist, "not browser-store publication proof");

            AssertGeneratedFile(output, "store-package-summary.md");
            AssertGeneratedFile(output, "store-package-summary.json");
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_ReportsMissingMetadataWithoutClaimingSubmissionReady()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-package");
        try
        {
            var result = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "edge"
            });

            Assert.IsFalse(result.Succeeded);
            var issues = result.Targets.Single().Issues;
            Assert.IsTrue(issues.Any(issue => issue.Contains("Support URL", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(issues.Any(issue => issue.Contains("Privacy URL", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(issues.Any(issue => issue.Contains("Release notes", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("blocked", result.Targets.Single().Status);
            Assert.IsTrue(File.Exists(result.Targets.Single().SubmissionManifestPath));
            Assert.IsTrue(File.Exists(result.Targets.Single().SubmissionBundlePath));
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_AllTargetsCreatesChromeEdgeAndFirefoxBundles()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-package");
        try
        {
            var result = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "all",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy",
                ReleaseNotes = "Reviewed package generation smoke."
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Targets.SelectMany(target => target.Issues)));
            CollectionAssert.AreEquivalent(
                new[] { "chrome", "edge", "firefox" },
                result.Targets.Select(target => target.Target).ToArray());
            foreach (var target in result.Targets)
            {
                Assert.IsTrue(File.Exists(target.ExtensionPackagePath), $"{target.Target} extension package missing.");
                Assert.IsTrue(File.Exists(target.SubmissionBundlePath), $"{target.Target} submission bundle missing.");
                Assert.AreEqual(64, target.SubmissionBundleSha256.Length);
            }
        }
        finally
        {
            DeleteIfExists(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_RedactsSensitiveMetadataInGeneratedFiles()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "store-package");
        try
        {
            var result = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = output,
                Target = "chrome",
                SupportUrl = "https://example.invalid/support?token=secret-support-token",
                PrivacyUrl = "https://example.invalid/privacy?api_key=secret-privacy-key",
                ReleaseNotes = "Authorization: Bearer secret-release-token"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Targets.SelectMany(target => target.Issues)));
            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(output, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));

            Assert.IsFalse(generatedText.Contains("secret-support-token", StringComparison.Ordinal));
            Assert.IsFalse(generatedText.Contains("secret-privacy-key", StringComparison.Ordinal));
            Assert.IsFalse(generatedText.Contains("secret-release-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
        }
        finally
        {
            DeleteIfExists(output);
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

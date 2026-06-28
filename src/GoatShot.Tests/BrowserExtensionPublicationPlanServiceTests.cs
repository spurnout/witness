using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionPublicationPlanServiceTests
{
    [TestMethod]
    public async Task CreateAsync_PlansManualPublicationFromExistingStorePackage()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var outputRoot = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        var storePackageRoot = Path.Combine(outputRoot, "store-package");
        var planOutput = Path.Combine(outputRoot, "publication-plan");
        try
        {
            var package = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = storePackageRoot,
                Target = "chrome",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy",
                ReleaseNotes = "Reviewed publication package."
            });
            Assert.IsTrue(package.Succeeded, string.Join(Environment.NewLine, package.Targets.SelectMany(target => target.Issues)));

            var result = await new BrowserExtensionPublicationPlanService().CreateAsync(new BrowserExtensionPublicationPlanRequest
            {
                ExtensionSourceDirectory = source,
                StorePackageRoot = storePackageRoot,
                OutputPath = planOutput,
                Target = "chrome",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Targets.SelectMany(target => target.Issues)));
            Assert.IsFalse(result.WouldPublish);
            Assert.IsFalse(result.WouldContactStoreAccount);
            Assert.IsFalse(result.WouldUploadPackage);
            Assert.IsFalse(result.WouldInstallExtension);
            Assert.IsFalse(result.WouldMutateBrowserProfile);
            Assert.AreEqual(1, result.Targets.Count);
            var target = result.Targets.Single();
            Assert.AreEqual("chrome", target.Target);
            Assert.AreEqual("manual-publication-plan-ready", target.Status);
            Assert.IsTrue(target.StorePackageAvailable);
            Assert.IsTrue(target.StoreSubmissionBundleAvailable);
            Assert.IsFalse(target.WouldPublish);
            Assert.IsFalse(target.WouldUploadPackage);
            Assert.IsTrue(target.RequiresDeveloperAccount);
            Assert.IsTrue(target.RequiredEvidence.Any(item => item.Contains("review/signing/availability", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.SourceReferences.Any(sourceRef => sourceRef.Contains("developer.chrome.com", StringComparison.OrdinalIgnoreCase)));

            AssertGeneratedFile(planOutput, "publication-plan.md");
            AssertGeneratedFile(planOutput, "publication-plan.json");
            var markdown = await File.ReadAllTextAsync(Path.Combine(planOutput, "publication-plan.md"));
            StringAssert.Contains(markdown, "Would publish: `False`");
            StringAssert.Contains(markdown, "This plan does not publish");
            StringAssert.Contains(markdown, "Chrome Web Store");
        }
        finally
        {
            DeleteIfExists(outputRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_PlansAllTargetsFromExistingStorePackages()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var outputRoot = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        var storePackageRoot = Path.Combine(outputRoot, "store-package");
        var planOutput = Path.Combine(outputRoot, "publication-plan");
        try
        {
            var package = await new BrowserExtensionStorePackageService().CreateAsync(new BrowserExtensionStorePackageRequest
            {
                ExtensionSourceDirectory = source,
                OutputPath = storePackageRoot,
                Target = "all",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy",
                ReleaseNotes = "Reviewed publication package."
            });
            Assert.IsTrue(package.Succeeded, string.Join(Environment.NewLine, package.Targets.SelectMany(target => target.Issues)));

            var result = await new BrowserExtensionPublicationPlanService().CreateAsync(new BrowserExtensionPublicationPlanRequest
            {
                ExtensionSourceDirectory = source,
                StorePackageRoot = storePackageRoot,
                OutputPath = planOutput,
                Target = "all",
                SupportUrl = "https://example.invalid/support",
                PrivacyUrl = "https://example.invalid/privacy"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Targets.SelectMany(target => target.Issues)));
            CollectionAssert.AreEquivalent(
                new[] { "chrome", "edge", "firefox" },
                result.Targets.Select(target => target.Target).ToArray());
            Assert.IsFalse(result.WouldPublish);
            Assert.IsFalse(result.WouldContactStoreAccount);
            Assert.IsFalse(result.WouldUploadPackage);
            Assert.IsFalse(result.WouldInstallExtension);
            Assert.IsFalse(result.WouldMutateBrowserProfile);

            foreach (var target in result.Targets)
            {
                Assert.AreEqual("manual-publication-plan-ready", target.Status);
                Assert.IsTrue(target.StorePackageAvailable, $"{target.Target} package was missing.");
                Assert.IsTrue(target.StoreSubmissionBundleAvailable, $"{target.Target} submission bundle was missing.");
                Assert.IsFalse(target.WouldPublish);
                Assert.IsFalse(target.WouldContactStoreAccount);
                Assert.IsFalse(target.WouldUploadPackage);
                Assert.IsFalse(target.WouldInstallExtension);
                Assert.IsTrue(target.RequiresDeveloperAccount);
                Assert.IsTrue(target.RequiresManualReview);
                Assert.IsTrue(target.RequiresLiveBrowserEvidence);
            }

            AssertGeneratedFile(planOutput, "publication-plan.md");
            AssertGeneratedFile(planOutput, "publication-plan.json");
        }
        finally
        {
            DeleteIfExists(outputRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_BlocksWithoutStorePackageButStillWritesBoundaryPlan()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var outputRoot = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new BrowserExtensionPublicationPlanService().CreateAsync(new BrowserExtensionPublicationPlanRequest
            {
                ExtensionSourceDirectory = source,
                StorePackageRoot = Path.Combine(outputRoot, "missing-store-package"),
                OutputPath = Path.Combine(outputRoot, "publication-plan"),
                Target = "edge",
                SupportUrl = "https://example.invalid/support?token=super-secret",
                PrivacyUrl = "https://example.invalid/privacy?api_key=super-secret"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.WouldPublish);
            Assert.IsFalse(result.WouldContactStoreAccount);
            Assert.IsFalse(result.WouldUploadPackage);
            var target = result.Targets.Single();
            Assert.AreEqual("blocked-before-manual-publication", target.Status);
            Assert.IsTrue(target.Issues.Any(issue => issue.Contains("Store-package root was not found", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(target.Issues.Any(issue => issue.Contains("No edge extension package", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "publication-plan.md");
            AssertGeneratedFile(result.OutputPath, "publication-plan.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(result.OutputPath, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
        }
        finally
        {
            DeleteIfExists(outputRoot);
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

using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionPublicationEvidenceRecordServiceTests
{
    [TestMethod]
    public async Task RecordAsync_PassedRequiresRequiredEvidenceAndWritesRedactedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(root, "package.md", "account.md", "submission.md", "review.md", "signing.md", "listing.md", "install.md", "browser-proof.md");

            var result = await new BrowserExtensionPublicationEvidenceRecordService().RecordAsync(new BrowserExtensionPublicationEvidenceRecordRequest
            {
                Target = "Chrome",
                Status = "passed",
                OutputPath = root,
                OperatorName = "QA Operator",
                Note = "reviewed token=super-secret-token-1234567890",
                Evidence =
                {
                    Evidence("package", "package.md"),
                    Evidence("account", "account.md"),
                    Evidence("submission", "submission.md"),
                    Evidence("review", "review.md"),
                    Evidence("signing", "signing.md"),
                    Evidence("listing", "listing.md"),
                    Evidence("install", "install.md"),
                    Evidence("live-browser", "browser-proof.md")
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.ProofComplete);
            Assert.AreEqual("chrome", result.Target);
            Assert.AreEqual(8, result.Evidence.Count);
            Assert.AreEqual(0, result.MissingRequiredCategories.Count);
            Assert.AreEqual(0, result.MissingRecommendedCategories.Count);
            Assert.IsFalse(result.WouldContactStoreAccount);
            Assert.IsFalse(result.WouldUploadPackage);
            Assert.IsFalse(result.WouldSubmitReview);
            Assert.IsFalse(result.WouldSignOrPublish);
            Assert.IsFalse(result.WouldInstallExtension);
            Assert.IsFalse(result.WouldMutateBrowserProfile);
            Assert.IsFalse(result.WouldRegisterNativeHost);
            Assert.IsFalse(result.WouldApplyEnterprisePolicy);
            AssertGeneratedFile(root, "chrome-publication-evidence.md");
            AssertGeneratedFile(root, "chrome-publication-evidence.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
            StringAssert.Contains(generatedText, "Proof complete: `True`");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_PassedWithMissingEvidenceFailsButWritesRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new BrowserExtensionPublicationEvidenceRecordService().RecordAsync(new BrowserExtensionPublicationEvidenceRecordRequest
            {
                Target = "Firefox",
                Status = "passed",
                OutputPath = root,
                Evidence =
                {
                    Evidence("package", "package.md")
                }
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ProofComplete);
            CollectionAssert.Contains(result.MissingRequiredCategories, "account");
            CollectionAssert.Contains(result.MissingRequiredCategories, "submission");
            CollectionAssert.Contains(result.MissingRequiredCategories, "review");
            CollectionAssert.Contains(result.MissingRequiredCategories, "signing");
            CollectionAssert.Contains(result.MissingRequiredCategories, "listing");
            CollectionAssert.Contains(result.MissingRequiredCategories, "install");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Passed browser extension publication evidence requires", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "firefox-publication-evidence.md");
            AssertGeneratedFile(root, "firefox-publication-evidence.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_BlockedRequiresNote()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new BrowserExtensionPublicationEvidenceRecordService().RecordAsync(new BrowserExtensionPublicationEvidenceRecordRequest
            {
                Target = "Edge",
                Status = "blocked",
                OutputPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("require --note", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "edge-publication-evidence.md");
            AssertGeneratedFile(root, "edge-publication-evidence.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_ExternalEvidencePathIsReducedToFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        var externalRoot = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(externalRoot);
            var externalEvidence = Path.Combine(externalRoot, "external-account.md");
            File.WriteAllText(externalEvidence, "external account proof");

            var result = await new BrowserExtensionPublicationEvidenceRecordService().RecordAsync(new BrowserExtensionPublicationEvidenceRecordRequest
            {
                Target = "edge",
                Status = "pending",
                OutputPath = root,
                Evidence =
                {
                    Evidence("account", externalEvidence)
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(1, result.Evidence.Count);
            Assert.IsFalse(result.Evidence[0].InsideOutputRoot);
            Assert.IsTrue(result.Evidence[0].Exists);
            StringAssert.Contains(result.Evidence[0].Value, "external-account.md");
            Assert.IsFalse(result.Evidence[0].Value.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(result.Evidence[0].Warning, "External evidence path was reduced");

            var generatedText = File.ReadAllText(Path.Combine(root, "edge-publication-evidence.md"));
            Assert.IsFalse(generatedText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(generatedText, "[external evidence: external-account.md]");
        }
        finally
        {
            DeleteIfExists(root);
            DeleteIfExists(externalRoot);
        }
    }

    private static BrowserExtensionPublicationEvidenceInput Evidence(string category, string value) => new()
    {
        Category = category,
        Value = value
    };

    private static void CreateEvidenceFiles(string root, params string[] fileNames)
    {
        Directory.CreateDirectory(root);
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(root, fileName), $"{fileName} evidence");
        }
    }

    private static void AssertGeneratedFile(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        Assert.IsTrue(File.Exists(path), $"{fileName} was not generated.");
        Assert.IsTrue(new FileInfo(path).Length > 0, $"{fileName} was empty.");
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

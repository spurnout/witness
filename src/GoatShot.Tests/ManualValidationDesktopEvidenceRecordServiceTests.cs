using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationDesktopEvidenceRecordServiceTests
{
    [TestMethod]
    public async Task RecordAsync_PassedKeyboardRequiresAllEvidenceCategoriesAndWritesRedactedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(
                root,
                "reviewed/keyboard-notes.md",
                "reviewed/surface-coverage.md",
                "reviewed/focus-order.md",
                "reviewed/result.md",
                "reviewed/privacy-review.md",
                "reviewed/focus-visual.png");

            var result = await new ManualValidationDesktopEvidenceRecordService().RecordAsync(new ManualValidationDesktopEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "keyboard",
                Status = "passed",
                OperatorName = "QA Operator",
                Note = "reviewed token=super-secret-token-1234567890",
                Evidence =
                {
                    Evidence("notes", "reviewed/keyboard-notes.md"),
                    Evidence("surface-coverage", "reviewed/surface-coverage.md"),
                    Evidence("focus-order", "reviewed/focus-order.md"),
                    Evidence("result", "reviewed/result.md"),
                    Evidence("privacy", "reviewed/privacy-review.md"),
                    Evidence("focus-visual", "reviewed/focus-visual.png")
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.ProofComplete);
            Assert.AreEqual("keyboard-traversal", result.LaneId);
            Assert.AreEqual("Keyboard Traversal", result.LaneTitle);
            Assert.AreEqual(Path.Combine(root, ManualValidationDesktopEvidenceRecordService.DefaultDirectoryName), result.OutputPath);
            Assert.AreEqual(6, result.Evidence.Count);
            Assert.AreEqual(0, result.MissingRequiredCategories.Count);
            CollectionAssert.Contains(result.MissingRecommendedCategories, "failure-media");
            Assert.IsTrue(result.Evidence.All(item => item.InsideManualValidationRoot));
            Assert.IsFalse(result.WouldLaunchApp);
            Assert.IsFalse(result.WouldChangeWindowsSettings);
            Assert.IsFalse(result.WouldCaptureScreen);
            Assert.IsFalse(result.WouldRecordScreen);
            Assert.IsFalse(result.WouldUpdateManualLane);
            Assert.IsFalse(result.WouldCertifyAccessibility);
            Assert.IsFalse(result.WouldMutateUserProfile);
            AssertGeneratedFile(result.OutputPath, "keyboard-traversal-desktop-evidence.md");
            AssertGeneratedFile(result.OutputPath, "keyboard-traversal-desktop-evidence.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(result.OutputPath, "*.*", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
            StringAssert.Contains(generatedText, "Proof complete: `True`");
            StringAssert.Contains(generatedText, "Would certify accessibility: `False`");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_PassedTextScalingWithMissingEvidenceFailsButWritesRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(
                root,
                "reviewed/text-scaling-notes.md",
                "reviewed/scale-125.md");

            var result = await new ManualValidationDesktopEvidenceRecordService().RecordAsync(new ManualValidationDesktopEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "text-scaling",
                Status = "passed",
                Evidence =
                {
                    Evidence("notes", "reviewed/text-scaling-notes.md"),
                    Evidence("scale-125", "reviewed/scale-125.md")
                }
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ProofComplete);
            CollectionAssert.Contains(result.MissingRequiredCategories, "scale-150");
            CollectionAssert.Contains(result.MissingRequiredCategories, "layout-review");
            CollectionAssert.Contains(result.MissingRequiredCategories, "restore");
            CollectionAssert.Contains(result.MissingRequiredCategories, "privacy");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Passed text-scaling evidence requires", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "text-scaling-desktop-evidence.md");
            AssertGeneratedFile(result.OutputPath, "text-scaling-desktop-evidence.json");
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
            Directory.CreateDirectory(root);

            var result = await new ManualValidationDesktopEvidenceRecordService().RecordAsync(new ManualValidationDesktopEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "high-contrast",
                Status = "blocked"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("require --note", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "high-contrast-desktop-evidence.md");
            AssertGeneratedFile(result.OutputPath, "high-contrast-desktop-evidence.json");
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
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(externalRoot);
            var externalEvidence = Path.Combine(externalRoot, "external-desktop-proof.md");
            File.WriteAllText(externalEvidence, "external desktop proof");

            var result = await new ManualValidationDesktopEvidenceRecordService().RecordAsync(new ManualValidationDesktopEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "live-region-drag",
                Status = "pending",
                Evidence =
                {
                    Evidence("notes", externalEvidence)
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(1, result.Evidence.Count);
            Assert.IsFalse(result.Evidence[0].InsideManualValidationRoot);
            Assert.IsTrue(result.Evidence[0].Exists);
            StringAssert.Contains(result.Evidence[0].Value, "external-desktop-proof.md");
            Assert.IsFalse(result.Evidence[0].Value.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(result.Evidence[0].Warning, "External evidence path was reduced");

            var generatedText = File.ReadAllText(Path.Combine(result.OutputPath, "live-region-drag-desktop-evidence.md"));
            Assert.IsFalse(generatedText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(generatedText, "[external evidence: external-desktop-proof.md]");
        }
        finally
        {
            DeleteIfExists(root);
            DeleteIfExists(externalRoot);
        }
    }

    private static ManualValidationDesktopEvidenceInput Evidence(string category, string value) => new()
    {
        Category = category,
        Value = value
    };

    private static void CreateEvidenceFiles(string root, params string[] fileNames)
    {
        Directory.CreateDirectory(root);
        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(root, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{fileName} evidence");
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

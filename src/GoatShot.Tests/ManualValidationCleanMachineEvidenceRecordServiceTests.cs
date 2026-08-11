using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationCleanMachineEvidenceRecordServiceTests
{
    [TestMethod]
    public async Task RecordAsync_PassedRequiresAllEvidenceCategoriesAndWritesRedactedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(
                root,
                "reviewed/machine-info.txt",
                "reviewed/package-manifest.json",
                "reviewed/portable-hash.txt",
                "reviewed/diagnostics-print.txt",
                "reviewed/paths.txt",
                "reviewed/main-window-render.png",
                "reviewed/settings-save-proof.png",
                "reviewed/capture-import-edit-export-proof.png",
                "reviewed/installer-build-or-skipped.md",
                "reviewed/privacy-review.md",
                "reviewed/clean-machine-script-result.json");

            var result = await new ManualValidationCleanMachineEvidenceRecordService().RecordAsync(new ManualValidationCleanMachineEvidenceRecordRequest
            {
                RootPath = root,
                Status = "passed",
                OperatorName = "QA Operator",
                Note = "reviewed token=super-secret-token-1234567890",
                Evidence =
                {
                    Evidence("machine", "reviewed/machine-info.txt"),
                    Evidence("package", "reviewed/package-manifest.json"),
                    Evidence("hash", "reviewed/portable-hash.txt"),
                    Evidence("diagnostics", "reviewed/diagnostics-print.txt"),
                    Evidence("paths", "reviewed/paths.txt"),
                    Evidence("first-launch", "reviewed/main-window-render.png"),
                    Evidence("settings", "reviewed/settings-save-proof.png"),
                    Evidence("capture-export", "reviewed/capture-import-edit-export-proof.png"),
                    Evidence("installer", "reviewed/installer-build-or-skipped.md"),
                    Evidence("privacy", "reviewed/privacy-review.md"),
                    Evidence("script-result", "reviewed/clean-machine-script-result.json")
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.ProofComplete);
            Assert.AreEqual(Path.Combine(root, ManualValidationCleanMachineEvidenceRecordService.DefaultDirectoryName), result.OutputPath);
            Assert.AreEqual(11, result.Evidence.Count);
            Assert.AreEqual(0, result.MissingRequiredCategories.Count);
            Assert.AreEqual(0, result.MissingRecommendedCategories.Count);
            Assert.IsTrue(result.Evidence.All(item => item.InsideManualValidationRoot));
            Assert.IsFalse(result.WouldLaunchApp);
            Assert.IsFalse(result.WouldRunInstaller);
            Assert.IsFalse(result.WouldInstallOrUninstall);
            Assert.IsFalse(result.WouldMutateUserProfile);
            Assert.IsFalse(result.WouldCaptureScreen);
            Assert.IsFalse(result.WouldUpdateManualLane);
            Assert.IsFalse(result.WouldCertifyCleanMachine);
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.md");
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(result.OutputPath, "*.*", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
            StringAssert.Contains(generatedText, "Proof complete: `True`");
            StringAssert.Contains(generatedText, "Would launch Receipts: `False`");
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
            Directory.CreateDirectory(root);

            var result = await new ManualValidationCleanMachineEvidenceRecordService().RecordAsync(new ManualValidationCleanMachineEvidenceRecordRequest
            {
                RootPath = root,
                Status = "passed",
                Evidence =
                {
                    Evidence("machine", "machine-info.txt"),
                    Evidence("package", "package-manifest.json")
                }
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ProofComplete);
            CollectionAssert.Contains(result.MissingRequiredCategories, "hash");
            CollectionAssert.Contains(result.MissingRequiredCategories, "diagnostics");
            CollectionAssert.Contains(result.MissingRequiredCategories, "paths");
            CollectionAssert.Contains(result.MissingRequiredCategories, "first-launch");
            CollectionAssert.Contains(result.MissingRequiredCategories, "settings");
            CollectionAssert.Contains(result.MissingRequiredCategories, "capture-export");
            CollectionAssert.Contains(result.MissingRequiredCategories, "installer");
            CollectionAssert.Contains(result.MissingRequiredCategories, "privacy");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Passed clean-machine evidence requires", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.md");
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.json");
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

            var result = await new ManualValidationCleanMachineEvidenceRecordService().RecordAsync(new ManualValidationCleanMachineEvidenceRecordRequest
            {
                RootPath = root,
                Status = "blocked"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("require --note", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.md");
            AssertGeneratedFile(result.OutputPath, "clean-machine-evidence.json");
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
            var externalEvidence = Path.Combine(externalRoot, "external-clean-machine-proof.md");
            File.WriteAllText(externalEvidence, "external clean-machine proof");

            var result = await new ManualValidationCleanMachineEvidenceRecordService().RecordAsync(new ManualValidationCleanMachineEvidenceRecordRequest
            {
                RootPath = root,
                Status = "pending",
                Evidence =
                {
                    Evidence("machine", externalEvidence)
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(1, result.Evidence.Count);
            Assert.IsFalse(result.Evidence[0].InsideManualValidationRoot);
            Assert.IsTrue(result.Evidence[0].Exists);
            StringAssert.Contains(result.Evidence[0].Value, "external-clean-machine-proof.md");
            Assert.IsFalse(result.Evidence[0].Value.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(result.Evidence[0].Warning, "External evidence path was reduced");

            var generatedText = File.ReadAllText(Path.Combine(result.OutputPath, "clean-machine-evidence.md"));
            Assert.IsFalse(generatedText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(generatedText, "[external evidence: external-clean-machine-proof.md]");
        }
        finally
        {
            DeleteIfExists(root);
            DeleteIfExists(externalRoot);
        }
    }

    private static ManualValidationCleanMachineEvidenceInput Evidence(string category, string value) => new()
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

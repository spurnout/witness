using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationHardwareEvidenceRecordServiceTests
{
    [TestMethod]
    public async Task RecordAsync_PassedMultiMonitorCaptureRequiresAllEvidenceCategoriesAndWritesRedactedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(
                root,
                "reviewed/notes.md",
                "reviewed/safe-content.md",
                "reviewed/topology.md",
                "reviewed/capture-output.md",
                "reviewed/dimensions.md",
                "reviewed/privacy-review.md",
                "reviewed/capture-engine.json");

            var result = await new ManualValidationHardwareEvidenceRecordService().RecordAsync(new ManualValidationHardwareEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "multi-monitor-capture",
                Status = "passed",
                OperatorName = "QA Operator",
                Note = "reviewed token=super-secret-token-1234567890",
                Evidence =
                {
                    Evidence("notes", "reviewed/notes.md"),
                    Evidence("safe-content", "reviewed/safe-content.md"),
                    Evidence("topology", "reviewed/topology.md"),
                    Evidence("capture-output", "reviewed/capture-output.md"),
                    Evidence("dimensions", "reviewed/dimensions.md"),
                    Evidence("privacy", "reviewed/privacy-review.md"),
                    Evidence("wgc-diagnostics", "reviewed/capture-engine.json")
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.ProofComplete);
            Assert.AreEqual("multi-monitor-capture", result.LaneId);
            Assert.AreEqual("Multi Monitor Capture", result.LaneTitle);
            Assert.AreEqual(Path.Combine(root, ManualValidationHardwareEvidenceRecordService.DefaultDirectoryName), result.OutputPath);
            Assert.AreEqual(7, result.Evidence.Count);
            Assert.AreEqual(0, result.MissingRequiredCategories.Count);
            CollectionAssert.Contains(result.MissingRecommendedCategories, "failure-media");
            Assert.IsTrue(result.Evidence.All(item => item.InsideManualValidationRoot));
            Assert.IsFalse(result.WouldCaptureDesktop);
            Assert.IsFalse(result.WouldRecordDesktop);
            Assert.IsFalse(result.WouldContactAndroidDevice);
            Assert.IsFalse(result.WouldImportPhoneMedia);
            Assert.IsFalse(result.WouldChangeDeviceSettings);
            Assert.IsFalse(result.WouldUpdateManualLane);
            Assert.IsFalse(result.WouldCertifyHardware);
            Assert.IsFalse(result.WouldMutateUserProfile);
            AssertGeneratedFile(result.OutputPath, "multi-monitor-capture-hardware-evidence.md");
            AssertGeneratedFile(result.OutputPath, "multi-monitor-capture-hardware-evidence.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(result.OutputPath, "*.*", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
            StringAssert.Contains(generatedText, "Proof complete: `True`");
            StringAssert.Contains(generatedText, "Would certify hardware: `False`");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_PassedLongRecordingWithMissingEvidenceFailsButWritesRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            CreateEvidenceFiles(
                root,
                "reviewed/notes.md",
                "reviewed/duration.md");

            var result = await new ManualValidationHardwareEvidenceRecordService().RecordAsync(new ManualValidationHardwareEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "long-recording",
                Status = "passed",
                Evidence =
                {
                    Evidence("notes", "reviewed/notes.md"),
                    Evidence("duration", "reviewed/duration.md")
                }
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ProofComplete);
            CollectionAssert.Contains(result.MissingRequiredCategories, "safe-content");
            CollectionAssert.Contains(result.MissingRequiredCategories, "playback");
            CollectionAssert.Contains(result.MissingRequiredCategories, "sync");
            CollectionAssert.Contains(result.MissingRequiredCategories, "recovery");
            CollectionAssert.Contains(result.MissingRequiredCategories, "privacy");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Passed long-recording evidence requires", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "long-recording-hardware-evidence.md");
            AssertGeneratedFile(result.OutputPath, "long-recording-hardware-evidence.json");
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

            var result = await new ManualValidationHardwareEvidenceRecordService().RecordAsync(new ManualValidationHardwareEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "android",
                Status = "blocked"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("require --note", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(result.OutputPath, "android-safe-device-proof-hardware-evidence.md");
            AssertGeneratedFile(result.OutputPath, "android-safe-device-proof-hardware-evidence.json");
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
            var externalEvidence = Path.Combine(externalRoot, "external-hardware-proof.md");
            File.WriteAllText(externalEvidence, "external hardware proof");

            var result = await new ManualValidationHardwareEvidenceRecordService().RecordAsync(new ManualValidationHardwareEvidenceRecordRequest
            {
                RootPath = root,
                Lane = "multi-monitor-recording",
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
            StringAssert.Contains(result.Evidence[0].Value, "external-hardware-proof.md");
            Assert.IsFalse(result.Evidence[0].Value.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(result.Evidence[0].Warning, "External evidence path was reduced");

            var generatedText = File.ReadAllText(Path.Combine(result.OutputPath, "multi-monitor-recording-hardware-evidence.md"));
            Assert.IsFalse(generatedText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(generatedText, "[external evidence: external-hardware-proof.md]");
        }
        finally
        {
            DeleteIfExists(root);
            DeleteIfExists(externalRoot);
        }
    }

    private static ManualValidationHardwareEvidenceInput Evidence(string category, string value) => new()
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

using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationFindingsServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WritesSortedFindingsForOpenManualLanes()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationFindingsService().CreateAsync(new ManualValidationFindingsRequest
            {
                RootPath = root
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsFalse(result.SummarySucceeded, "Fresh manual evidence should still be incomplete.");
            Assert.IsTrue(File.Exists(result.FindingsMarkdownPath));
            Assert.IsTrue(File.Exists(result.FindingsJsonPath));
            Assert.IsTrue(result.RequiredBlockingCount > 0);
            Assert.IsTrue(result.HardwareGatedOpenCount > 0);
            Assert.AreEqual(1, result.OptionalCompatibilityOpenCount);
            Assert.AreEqual(1, result.ParkedCount);
            Assert.AreEqual(0, result.RedactionFindingCount);

            var keyboard = result.Findings.Single(finding => finding.Id == "keyboard-traversal");
            Assert.AreEqual(ManualValidationFindingSeverity.P1, keyboard.Severity);
            Assert.AreEqual("RequiredProofMissing", keyboard.Category);
            Assert.IsTrue(keyboard.BlocksLocalV1Handoff);
            StringAssert.Contains(keyboard.RecommendedAction, "manual-validation record-lane");

            var android = result.Findings.Single(finding => finding.Id == "android-safe-device-proof");
            Assert.AreEqual(ManualValidationLaneRequirement.HardwareGated, android.Requirement);
            Assert.IsFalse(android.BlocksLocalV1Handoff);
            StringAssert.Contains(android.ClaimBoundary, "matching claim remains unproven");

            var provider = result.Findings.Single(finding => finding.Id == "live-provider-proof");
            Assert.AreEqual(ManualValidationFindingSeverity.Parked, provider.Severity);
            Assert.AreEqual("ParkedScope", provider.Category);

            var markdown = await File.ReadAllTextAsync(result.FindingsMarkdownPath);
            StringAssert.Contains(markdown, "Release-Blocking Required Findings");
            StringAssert.Contains(markdown, "Hardware-Gated Claim Boundaries");
            StringAssert.Contains(markdown, "Optional And Parked Boundaries");
            StringAssert.Contains(markdown, "Clean Machine Portable Installer");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_FlagsRedactionFindings()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var diagnostics = Path.Combine(root, "diagnostics");
            Directory.CreateDirectory(diagnostics);
            await File.WriteAllTextAsync(
                Path.Combine(diagnostics, "unsafe-provider-output.txt"),
                "callback=https://example.test/oauth?code=fake-code-1234567890 token=secret-token-1234567890");

            var result = await new ManualValidationFindingsService().CreateAsync(new ManualValidationFindingsRequest
            {
                RootPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.RedactionFindingCount > 0);
            Assert.IsTrue(result.Findings.Any(finding =>
                finding.Category == "RedactionRisk" &&
                finding.Severity == ManualValidationFindingSeverity.P0 &&
                finding.BlocksLocalV1Handoff));

            var markdown = await File.ReadAllTextAsync(result.FindingsMarkdownPath);
            StringAssert.Contains(markdown, "Potential Sensitive Data");
            StringAssert.Contains(markdown, "Do not publish or bundle manual proof");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_FailsForMissingFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-missing-findings-" + Guid.NewGuid().ToString("N"));

        var result = await new ManualValidationFindingsService().CreateAsync(new ManualValidationFindingsRequest
        {
            RootPath = path
        });

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "was not found");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-findings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

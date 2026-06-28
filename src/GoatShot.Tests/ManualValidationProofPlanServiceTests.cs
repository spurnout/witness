using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationProofPlanServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WritesRunbookForOpenRequiredLanes()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationProofPlanService().CreateAsync(new ManualValidationProofPlanRequest
            {
                RootPath = root
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsFalse(result.SummarySucceeded, "Fresh manual evidence should still be incomplete.");
            Assert.IsTrue(File.Exists(result.PlanMarkdownPath));
            Assert.IsTrue(File.Exists(result.PlanJsonPath));
            Assert.IsTrue(result.RequiredOpenCount > 0);
            Assert.IsTrue(result.HardwareGatedOpenCount > 0);
            Assert.AreEqual(1, result.OptionalCompatibilityOpenCount);
            Assert.AreEqual(1, result.ParkedCount);

            var keyboard = result.Lanes.Single(lane => lane.Id == "keyboard-traversal");
            Assert.IsTrue(keyboard.BlocksLocalV1Handoff);
            Assert.AreEqual(ManualValidationLaneRequirement.Required, keyboard.Requirement);
            Assert.IsTrue(keyboard.OperatorSteps.Any(step => step.Contains("Tab through Main Window", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(keyboard.RecommendedEvidence.Any(item => item.Contains("keyboard", StringComparison.OrdinalIgnoreCase)));

            var android = result.Lanes.Single(lane => lane.Id == "android-safe-device-proof");
            Assert.AreEqual(ManualValidationLaneRequirement.HardwareGated, android.Requirement);
            Assert.IsFalse(android.BlocksLocalV1Handoff);

            var provider = result.Lanes.Single(lane => lane.Id == "live-provider-proof");
            Assert.AreEqual(ManualValidationLaneRequirement.Parked, provider.Requirement);
            Assert.IsTrue(provider.ClaimBoundary.Contains("Out of current scope", StringComparison.OrdinalIgnoreCase));

            var markdown = await File.ReadAllTextAsync(result.PlanMarkdownPath);
            StringAssert.Contains(markdown, "Required Local V1 Handoff Lanes");
            StringAssert.Contains(markdown, "Required Lane Runbook");
            StringAssert.Contains(markdown, "Hardware-Gated Claim Boundaries");
            StringAssert.Contains(markdown, "Parked Lanes");
            StringAssert.Contains(markdown, "Clean Machine Portable Installer");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_WritesToSeparateOutputFolder()
    {
        var root = CreateTempRoot();
        var output = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationProofPlanService().CreateAsync(new ManualValidationProofPlanRequest
            {
                RootPath = root,
                OutputPath = output
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(Path.GetFullPath(output), result.OutputRoot);
            Assert.AreEqual(Path.Combine(Path.GetFullPath(output), "manual-validation-proof-plan.md"), result.PlanMarkdownPath);
            Assert.AreEqual(Path.Combine(Path.GetFullPath(output), "manual-validation-proof-plan.json"), result.PlanJsonPath);
            Assert.IsTrue(File.Exists(result.PlanMarkdownPath));
            Assert.IsTrue(File.Exists(result.PlanJsonPath));
            Assert.IsTrue(File.Exists(Path.Combine(root, ManualValidationSummaryService.SummaryMarkdownFileName)));
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(output);
        }
    }

    [TestMethod]
    public async Task CreateAsync_FailsForMissingFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-missing-proof-plan-" + Guid.NewGuid().ToString("N"));

        var result = await new ManualValidationProofPlanService().CreateAsync(new ManualValidationProofPlanRequest
        {
            RootPath = path
        });

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "was not found");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-proof-plan-test-" + Guid.NewGuid().ToString("N"));
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

using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationSummaryServiceTests
{
    [TestMethod]
    public async Task SummarizeAsync_ReportsFreshHarnessAsIncomplete()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(File.Exists(result.SummaryJsonPath));
            Assert.IsTrue(File.Exists(result.SummaryMarkdownPath));
            Assert.IsTrue(result.Lanes.Count >= 13);
            Assert.IsTrue(result.Lanes.Any(lane => lane.Id == "keyboard-traversal" && lane.Status == ManualValidationLaneStatus.NotRun));
            Assert.IsTrue(result.Lanes.Single(lane => lane.Id == "keyboard-traversal").BlocksLocalV1Handoff);
            Assert.AreEqual(ManualValidationLaneRequirement.Required, result.Lanes.Single(lane => lane.Id == "keyboard-traversal").Requirement);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Keyboard Traversal: required result is not run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Long Recording Stability: not run because it is hardware-gated", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Browser Extension Live Fixture: optional compatibility proof is not run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Live Provider Account Proof", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Diagnostics bundle", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SummarizeAsync_AllowsParkedProviderLaneToRemainNotRun()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            foreach (var lane in ManualValidationHarnessService.ExpectedLanes.Where(lane => !lane.OAuthParked))
            {
                MarkStatus(Path.Combine(root, lane.RelativePath), "Passed");
            }

            Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "goatshot-diagnostics.zip"), "fake zip");

            var result = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual("clean", result.Redaction.Status);
            Assert.IsTrue(result.Diagnostics.DiagnosticsBundleExists);
            Assert.IsTrue(result.Lanes.Single(lane => lane.Id == "live-provider-proof").OAuthParked);
            Assert.AreEqual(ManualValidationLaneRequirement.Parked, result.Lanes.Single(lane => lane.Id == "live-provider-proof").Requirement);
            Assert.AreEqual(ManualValidationLaneStatus.NotRun, result.Lanes.Single(lane => lane.Id == "live-provider-proof").Status);

            var markdown = await File.ReadAllTextAsync(result.SummaryMarkdownPath);
            StringAssert.Contains(markdown, "Status: `complete`");
            StringAssert.Contains(markdown, "Blocks Local V1 Handoff");
            StringAssert.Contains(markdown, "Live Provider Account Proof");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SummarizeAsync_AllowsHardwareAndOptionalLanesToRemainVisibleWarnings()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            foreach (var lane in ManualValidationHarnessService.ExpectedLanes.Where(lane =>
                         lane.Requirement is ManualValidationLaneRequirement.Required))
            {
                MarkStatus(Path.Combine(root, lane.RelativePath), "Passed");
            }

            Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "goatshot-diagnostics.zip"), "fake zip");

            var result = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(ManualValidationLaneStatus.NotRun, result.Lanes.Single(lane => lane.Id == "long-recording").Status);
            Assert.AreEqual(ManualValidationLaneRequirement.HardwareGated, result.Lanes.Single(lane => lane.Id == "long-recording").Requirement);
            Assert.IsFalse(result.Lanes.Single(lane => lane.Id == "long-recording").BlocksLocalV1Handoff);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Long Recording Stability: not run because it is hardware-gated", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Browser Extension Live Fixture: optional compatibility proof is not run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.Issues.Any(issue => issue.Contains("Long Recording Stability", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.Issues.Any(issue => issue.Contains("Browser Extension Live Fixture", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SummarizeAsync_RequiresNoteForBlockedOrFailedLane()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            foreach (var lane in ManualValidationHarnessService.ExpectedLanes.Where(lane => !lane.OAuthParked))
            {
                MarkStatus(Path.Combine(root, lane.RelativePath), "Passed");
            }

            var keyboard = Path.Combine(root, "02-keyboard-traversal.md");
            MarkStatus(keyboard, "Blocked");
            Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "goatshot-diagnostics.zip"), "fake zip");

            var missingNote = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });
            Assert.IsFalse(missingNote.Succeeded);
            Assert.IsTrue(missingNote.Issues.Any(issue => issue.Contains("requires a short operator note", StringComparison.OrdinalIgnoreCase)));

            await File.AppendAllTextAsync(keyboard, Environment.NewLine + "- Blocked by unavailable keyboard-only overlay path; retest scheduled with safe desktop content." + Environment.NewLine);
            var withNote = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsTrue(withNote.Succeeded, string.Join(Environment.NewLine, withNote.Issues));
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, withNote.Lanes.Single(lane => lane.Id == "keyboard-traversal").Status);
            Assert.AreEqual(true, withNote.Lanes.Single(lane => lane.Id == "keyboard-traversal").HasRequiredNote);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SummarizeAsync_FailsWhenTextEvidenceContainsSensitiveValues()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            foreach (var lane in ManualValidationHarnessService.ExpectedLanes.Where(lane => !lane.OAuthParked))
            {
                MarkStatus(Path.Combine(root, lane.RelativePath), "Passed");
            }

            Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "goatshot-diagnostics.zip"), "fake zip");
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "unsafe-provider-output.txt"), "callback=https://example.test/oauth?code=fake-code-1234567890 user=alex@example.test");

            var result = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("warning", result.Redaction.Status);
            Assert.IsTrue(result.Redaction.Findings.Any(finding => finding.RelativePath.Contains("unsafe-provider-output", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Potential sensitive data", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void MarkStatus(string path, string status)
    {
        var text = File.ReadAllText(path);
        foreach (var candidate in new[] { "Pending", "Passed", "Failed", "Blocked" })
        {
            text = text.Replace($"- [x] {candidate}", $"- [ ] {candidate}", StringComparison.OrdinalIgnoreCase);
            text = text.Replace($"- [ ] {candidate}", candidate.Equals(status, StringComparison.OrdinalIgnoreCase)
                ? $"- [x] {candidate}"
                : $"- [ ] {candidate}", StringComparison.OrdinalIgnoreCase);
        }

        File.WriteAllText(path, text);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-summary-test-" + Guid.NewGuid().ToString("N"));
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

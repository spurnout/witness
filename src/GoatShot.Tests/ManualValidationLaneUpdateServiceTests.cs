using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationLaneUpdateServiceTests
{
    [TestMethod]
    public async Task UpdateAsync_PassedLaneUpdatesResultEvidenceAndSummary()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var evidencePath = Path.Combine(root, "desktop-proof", "screenshots", "main-window.png");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, "safe screenshot placeholder");

            var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "keyboard",
                Status = "passed",
                Note = "Keyboard traversal completed on safe demo content.",
                OperatorName = "QA",
                ObservedAt = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero),
                EvidencePaths = ["desktop-proof/screenshots/main-window.png"]
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("keyboard-traversal", result.LaneId);
            Assert.AreEqual(ManualValidationLaneStatus.Passed, result.Status);
            Assert.AreEqual(1, result.Evidence.Count);
            Assert.AreEqual("desktop-proof/screenshots/main-window.png", result.Evidence[0].Value);
            Assert.IsTrue(result.Evidence[0].Exists);
            Assert.IsTrue(result.Evidence[0].InsideManualFolder);

            var laneText = await File.ReadAllTextAsync(Path.Combine(root, "02-keyboard-traversal.md"));
            StringAssert.Contains(laneText, "- [x] Passed");
            StringAssert.Contains(laneText, "- [ ] Blocked");
            StringAssert.Contains(laneText, "Keyboard traversal completed on safe demo content.");
            StringAssert.Contains(laneText, "desktop-proof/screenshots/main-window.png");
            StringAssert.Contains(laneText, "## Operator Update");

            var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });
            var keyboard = summary.Lanes.Single(lane => lane.Id == "keyboard-traversal");
            Assert.AreEqual(ManualValidationLaneStatus.Passed, keyboard.Status);
            Assert.IsTrue(keyboard.EvidenceCount > 0);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_BlockedLaneRequiresNote()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "screen-reader",
                Status = "blocked"
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "requires --note");

            var laneText = await File.ReadAllTextAsync(Path.Combine(root, "03-screen-reader-narrator-nvda.md"));
            StringAssert.Contains(laneText, "- [ ] Blocked");
            StringAssert.Contains(laneText, "- [ ] Passed");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_BlockedLaneWithNoteSatisfiesSummaryNoteRequirement()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "screen-reader",
                Status = "blocked",
                Note = "Narrator pass is waiting on a human observation session."
            });

            Assert.IsTrue(result.Succeeded, result.Message);

            var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });
            var lane = summary.Lanes.Single(item => item.Id == "screen-reader");
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, lane.Status);
            Assert.AreEqual(true, lane.HasRequiredNote);
            Assert.IsFalse(lane.Issues.Any(issue => issue.Contains("requires a short operator note", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_RedactsSensitiveNoteAndExternalEvidencePath()
    {
        var root = CreateTempRoot();
        var externalRoot = CreateTempRoot("goatshot-manual-validation-external-test-");
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var externalEvidence = Path.Combine(externalRoot, "external-proof.png");
            await File.WriteAllTextAsync(externalEvidence, "safe external placeholder");

            var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "text scaling",
                Status = "blocked",
                Note = "Waiting on Windows scale checks; api_key=abcdefghijklmnop",
                EvidencePaths = [externalEvidence]
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, result.Evidence.Count);
            Assert.AreEqual("[external evidence: external-proof.png]", result.Evidence[0].Value);
            Assert.IsFalse(result.Evidence[0].InsideManualFolder);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("External evidence path", StringComparison.OrdinalIgnoreCase)));

            var laneText = await File.ReadAllTextAsync(Path.Combine(root, "04-text-scaling.md"));
            StringAssert.Contains(laneText, "[REDACTED:api-key-or-password-field]");
            Assert.IsFalse(laneText.Contains("abcdefghijklmnop", StringComparison.Ordinal));
            Assert.IsFalse(laneText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase), laneText);
            StringAssert.Contains(laneText, "[external evidence: external-proof.png]");
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(externalRoot);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_RejectsNotApplicableForRequiredLane()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
            {
                RootPath = root,
                Lane = "keyboard-traversal",
                Status = "not-applicable",
                Note = "Not relevant."
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Required manual validation lanes cannot be marked not applicable");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempRoot(string prefix = "goatshot-manual-validation-lane-update-test-")
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
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

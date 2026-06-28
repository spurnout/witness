using GoatShot.App.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationDesktopProofServiceTests
{
    [TestMethod]
    public async Task CollectAsync_RunCommandsWritesBlockedLanesWithCommandEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            MarkStatus(Path.Combine(root, ManualValidationBaselineService.BaselineFileName), "Passed");
            Directory.CreateDirectory(Path.Combine(root, "diagnostics"));
            await File.WriteAllTextAsync(Path.Combine(root, "diagnostics", "goatshot-diagnostics.zip"), "fake diagnostic bundle");

            var result = await new ManualValidationDesktopProofService().CollectAsync(
                new ManualValidationDesktopProofRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    AppPath = "goatshot-test-app.exe",
                    TimeoutSeconds = 5
                },
                new FakeDesktopProofRunner());

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, result.Status);
            Assert.IsTrue(result.Commands.Count > 5);
            Assert.AreEqual(0, result.FailedCommands.Count);
            Assert.IsTrue(File.Exists(result.CommandResultsPath));
            Assert.IsTrue(File.Exists(result.EnvironmentReportPath));
            Assert.IsTrue(File.Exists(result.SummaryMarkdownPath));
            Assert.IsTrue(File.Exists(result.SummaryJsonPath));
            Assert.IsTrue(File.Exists(Path.Combine(root, "desktop-proof", "screenshots", "main-window.png")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "desktop-proof", "screenshots", "proof-scene.png")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "desktop-proof", "audits", "main-accessibility.md")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "desktop-proof", "audits", "proof-scene-accessibility.md")));
            CollectionAssert.Contains(result.EvidenceFiles, "desktop-proof/desktop-proof-summary.md");
            CollectionAssert.Contains(result.EvidenceFiles, "desktop-proof/desktop-proof-summary.json");

            Assert.IsTrue(result.Summary.CommandBackedEvidencePresent);
            Assert.IsTrue(result.Summary.HumanObservationRequired);
            Assert.IsTrue(result.Summary.BlocksLocalV1Handoff);
            Assert.AreEqual(0, result.Summary.FailedCommandCount);
            CollectionAssert.Contains(result.Summary.RemainingHumanLanes, "Keyboard Traversal");

            var summaryMarkdown = await File.ReadAllTextAsync(result.SummaryMarkdownPath);
            StringAssert.Contains(summaryMarkdown, "Human/operator observation required: yes");
            StringAssert.Contains(summaryMarkdown, "Narrator/NVDA");
            StringAssert.Contains(summaryMarkdown, "Do not mark the required desktop lanes Passed from this summary alone.");

            var summaryJson = JsonSerializer.Deserialize<ManualValidationDesktopProofSummary>(
                await File.ReadAllTextAsync(result.SummaryJsonPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new JsonStringEnumConverter() }
                });
            Assert.IsNotNull(summaryJson);
            Assert.IsTrue(summaryJson.HumanObservationRequired);
            Assert.IsTrue(summaryJson.BlocksLocalV1Handoff);

            var keyboard = await File.ReadAllTextAsync(Path.Combine(root, "02-keyboard-traversal.md"));
            StringAssert.Contains(keyboard, "- [x] Blocked");
            StringAssert.Contains(keyboard, "desktop-proof/audits/main-accessibility.md");
            StringAssert.Contains(keyboard, "desktop-proof/screenshots/proof-scene.png");
            StringAssert.Contains(keyboard, "Human keyboard traversal was not performed");
            StringAssert.Contains(keyboard, "Do not claim this lane passed");

            var screenReader = await File.ReadAllTextAsync(Path.Combine(root, "03-screen-reader-narrator-nvda.md"));
            StringAssert.Contains(screenReader, "Narrator/NVDA was not driven or observed");

            var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.IsTrue(summary.Succeeded, string.Join(Environment.NewLine, summary.Issues));
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, summary.Lanes.Single(lane => lane.Id == "keyboard-traversal").Status);
            Assert.IsTrue(summary.Lanes.Single(lane => lane.Id == "keyboard-traversal").HasRequiredNote);
            Assert.IsFalse(summary.Issues.Any(issue => issue.Contains("Keyboard Traversal: required result is not run", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CollectAsync_RunCommandsReportsFailedCommand()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationDesktopProofService().CollectAsync(
                new ManualValidationDesktopProofRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    AppPath = "goatshot-test-app.exe"
                },
                new FakeDesktopProofRunner(failedCommandName: "Audit WPF Main"));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Failed, result.Status);
            CollectionAssert.Contains(result.FailedCommands, "Audit WPF Main");
            Assert.AreEqual(1, result.Summary.FailedCommandCount);
            CollectionAssert.Contains(result.Summary.FailedCommands, "Audit WPF Main");

            var keyboard = await File.ReadAllTextAsync(Path.Combine(root, "02-keyboard-traversal.md"));
            StringAssert.Contains(keyboard, "- [x] Failed");
            StringAssert.Contains(keyboard, "One or more desktop-proof commands failed");
            StringAssert.Contains(keyboard, "Audit WPF Main");

            var summaryMarkdown = await File.ReadAllTextAsync(result.SummaryMarkdownPath);
            StringAssert.Contains(summaryMarkdown, "## Failed Commands");
            StringAssert.Contains(summaryMarkdown, "Audit WPF Main");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CollectAsync_WithoutCommandEvidenceReturnsBlocked()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationDesktopProofService().CollectAsync(new ManualValidationDesktopProofRequest
            {
                RootPath = root,
                RunCommands = false
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, result.Status);
            StringAssert.Contains(result.Message, "no command results");
            Assert.IsFalse(result.Summary.CommandBackedEvidencePresent);
            Assert.IsTrue(result.Summary.HumanObservationRequired);

            var keyboard = await File.ReadAllTextAsync(Path.Combine(root, "02-keyboard-traversal.md"));
            StringAssert.Contains(keyboard, "- [x] Blocked");
            StringAssert.Contains(keyboard, "No desktop-proof commands were recorded");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-desktop-proof-test-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeDesktopProofRunner(string? failedCommandName = null) : IManualValidationDesktopProofCommandRunner
    {
        public Task<ManualValidationDesktopProofCommandResult> RunAsync(
            ManualValidationDesktopProofCommandSpec command,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
            if (Path.GetExtension(command.OutputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllBytes(command.OutputPath, [0x89, 0x50, 0x4E, 0x47]);
            }
            else
            {
                File.WriteAllText(command.OutputPath, $"# {command.Name}{Environment.NewLine}{Environment.NewLine}Fake audit evidence.");
            }

            var exitCode = command.Name.Equals(failedCommandName, StringComparison.OrdinalIgnoreCase) ? 5 : 0;
            return Task.FromResult(ManualValidationDesktopProofCommandResult.FromSpec(
                command,
                exitCode,
                $"{command.Name} complete",
                exitCode == 0 ? string.Empty : $"{command.Name} failed",
                timedOut: false));
        }
    }
}

using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationBaselineServiceTests
{
    [TestMethod]
    public async Task CompleteAsync_RunCommandsWritesPassedBaselineAndRawJsonEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var result = await new ManualValidationBaselineService().CompleteAsync(
                new ManualValidationBaselineRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    CliPath = "goatshot-test-cli.exe",
                    TimeoutSeconds = 5
                },
                new FakeBaselineRunner());

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(ManualValidationLaneStatus.Passed, result.Status);
            Assert.IsTrue(File.Exists(result.CommandResultsPath));
            Assert.IsTrue(result.Commands.All(command => command.ExitCode == 0));
            Assert.AreEqual(0, result.MissingRequiredEvidence.Count, string.Join(Environment.NewLine, result.MissingRequiredEvidence));

            var baseline = await File.ReadAllTextAsync(Path.Combine(root, ManualValidationBaselineService.BaselineFileName));
            StringAssert.Contains(baseline, "- [x] Passed");
            StringAssert.Contains(baseline, "diagnostics/recording-readiness.json");
            StringAssert.Contains(baseline, "This baseline proves current local build/test/diagnostic evidence only");

            var recordingReadiness = await File.ReadAllTextAsync(Path.Combine(root, "diagnostics", "recording-readiness.json"));
            Assert.IsTrue(recordingReadiness.TrimStart().StartsWith("{", StringComparison.Ordinal), recordingReadiness);
            Assert.IsFalse(recordingReadiness.Contains("Command:", StringComparison.OrdinalIgnoreCase), recordingReadiness);

            var buildOutput = await File.ReadAllTextAsync(Path.Combine(root, "diagnostics", "build-release.txt"));
            StringAssert.Contains(buildOutput, "Command: dotnet build");

            var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });
            Assert.IsFalse(summary.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Passed, summary.Lanes.Single(lane => lane.Id == "baseline").Status);
            Assert.IsFalse(summary.Issues.Any(issue => issue.Contains("Baseline Setup: required result is not run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(summary.Issues.Any(issue => issue.Contains("Keyboard Traversal: required result is not run", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CompleteAsync_RunCommandsReportsFailedCommand()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var result = await new ManualValidationBaselineService().CompleteAsync(
                new ManualValidationBaselineRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    CliPath = "goatshot-test-cli.exe"
                },
                new FakeBaselineRunner(failedCommandName: "Release tests"));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Failed, result.Status);
            CollectionAssert.Contains(result.FailedCommands, "Release tests");

            var baseline = await File.ReadAllTextAsync(Path.Combine(root, ManualValidationBaselineService.BaselineFileName));
            StringAssert.Contains(baseline, "- [x] Failed");
            StringAssert.Contains(baseline, "One or more baseline commands failed");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CompleteAsync_WithoutCommandEvidenceBlocksBaseline()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var result = await new ManualValidationBaselineService().CompleteAsync(new ManualValidationBaselineRequest
            {
                RootPath = root,
                RunCommands = false
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, result.Status);
            Assert.IsTrue(result.MissingRequiredEvidence.Contains("diagnostics/build-release.txt"));
            Assert.IsTrue(result.MissingRequiredEvidence.Contains("diagnostics/baseline-command-results.json"));

            var baseline = await File.ReadAllTextAsync(Path.Combine(root, ManualValidationBaselineService.BaselineFileName));
            StringAssert.Contains(baseline, "- [x] Blocked");
            StringAssert.Contains(baseline, "missing baseline evidence");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-baseline-test-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeBaselineRunner(string? failedCommandName = null) : IManualValidationBaselineCommandRunner
    {
        public Task<ManualValidationBaselineCommandResult> RunAsync(
            ManualValidationBaselineCommandSpec command,
            CancellationToken cancellationToken = default)
        {
            if (command.Name.Equals("CLI diagnostics bundle", StringComparison.OrdinalIgnoreCase))
            {
                var outputIndex = command.Arguments
                    .Select((value, index) => new { value, index })
                    .FirstOrDefault(item => item.value.Equals("--output", StringComparison.OrdinalIgnoreCase))
                    ?.index;
                if (outputIndex is not null && outputIndex.Value + 1 < command.Arguments.Count)
                {
                    var bundlePath = command.Arguments[outputIndex.Value + 1];
                    Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
                    File.WriteAllText(bundlePath, "fake diagnostics bundle");
                }
            }

            var exitCode = command.Name.Equals(failedCommandName, StringComparison.OrdinalIgnoreCase) ? 7 : 0;
            var stdout = Path.GetExtension(command.OutputPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? $"{{ \"command\": \"{command.Name}\", \"ok\": {(exitCode == 0 ? "true" : "false")} }}"
                : $"{command.Name} complete";
            var stderr = exitCode == 0 ? string.Empty : $"{command.Name} failed";
            return Task.FromResult(ManualValidationBaselineCommandResult.FromSpec(
                command,
                exitCode,
                stdout,
                stderr,
                timedOut: false));
        }
    }
}

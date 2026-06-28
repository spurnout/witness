using GoatShot.App.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationHardwareProofServiceTests
{
    [TestMethod]
    public async Task CollectAsync_RunCommandsWritesBlockedHardwareLanesWithEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationHardwareProofService().CollectAsync(
                new ManualValidationHardwareProofRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    CliPath = "goatshot-test-cli.exe",
                    TimeoutSeconds = 5
                },
                new FakeHardwareProofRunner());

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, result.Status);
            Assert.AreEqual(6, result.Commands.Count);
            Assert.AreEqual(0, result.FailedCommands.Count);
            Assert.IsTrue(File.Exists(Path.Combine(root, "hardware-proof", "environment.md")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "hardware-proof", "recording-preflight.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "hardware-proof", "android-diagnostics.json")));
            Assert.IsTrue(File.Exists(result.SummaryMarkdownPath));
            Assert.IsTrue(File.Exists(result.SummaryJsonPath));
            CollectionAssert.Contains(result.EvidenceFiles, "hardware-proof/hardware-proof-summary.md");
            CollectionAssert.Contains(result.EvidenceFiles, "hardware-proof/hardware-proof-summary.json");

            Assert.IsTrue(result.Summary.ReadinessEvidencePresent);
            Assert.IsTrue(result.Summary.LiveHardwareProofRequired);
            Assert.IsFalse(result.Summary.BlocksLocalV1Handoff);
            Assert.IsTrue(result.Summary.BlocksHardwareClaims);
            Assert.AreEqual(0, result.Summary.NonzeroCommandCount);
            CollectionAssert.Contains(result.Summary.RemainingHardwareLanes, "Long Recording Stability");

            var summaryMarkdown = await File.ReadAllTextAsync(result.SummaryMarkdownPath);
            StringAssert.Contains(summaryMarkdown, "Live hardware proof required: yes");
            StringAssert.Contains(summaryMarkdown, "Blocks hardware/device claims until operator lanes pass: yes");
            StringAssert.Contains(summaryMarkdown, "Do not mark hardware-gated lanes Passed from this summary alone.");

            var summaryJson = JsonSerializer.Deserialize<ManualValidationHardwareProofSummary>(
                await File.ReadAllTextAsync(result.SummaryJsonPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new JsonStringEnumConverter() }
                });
            Assert.IsNotNull(summaryJson);
            Assert.IsTrue(summaryJson.LiveHardwareProofRequired);
            Assert.IsTrue(summaryJson.BlocksHardwareClaims);

            var multiMonitor = await File.ReadAllTextAsync(Path.Combine(root, "07-multi-monitor-capture.md"));
            StringAssert.Contains(multiMonitor, "- [x] Blocked");
            StringAssert.Contains(multiMonitor, "hardware-proof/environment.md");
            StringAssert.Contains(multiMonitor, "Live multi-monitor capture was not performed");
            StringAssert.Contains(multiMonitor, "Do not claim this lane passed");

            var longRecording = await File.ReadAllTextAsync(Path.Combine(root, "09-long-recording.md"));
            StringAssert.Contains(longRecording, "Long-run recording stability was not performed");
            StringAssert.Contains(longRecording, "hardware-proof/diagnostics-recording.json");

            var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
            {
                RootPath = root
            });

            Assert.AreEqual(ManualValidationLaneStatus.Blocked, summary.Lanes.Single(lane => lane.Id == "multi-monitor-capture").Status);
            Assert.AreEqual(ManualValidationLaneRequirement.HardwareGated, summary.Lanes.Single(lane => lane.Id == "multi-monitor-capture").Requirement);
            Assert.IsFalse(summary.Lanes.Single(lane => lane.Id == "multi-monitor-capture").BlocksLocalV1Handoff);
            Assert.AreEqual(0, summary.Lanes.Single(lane => lane.Id == "long-recording").Issues.Count);
            Assert.AreEqual(0, summary.Lanes.Single(lane => lane.Id == "android-safe-device-proof").Issues.Count);
            Assert.IsFalse(summary.Issues.Any(issue => issue.Contains("multiple checked result statuses", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CollectAsync_FailedDiagnosticsRemainBlockedEvidence()
    {
        var root = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));

            var result = await new ManualValidationHardwareProofService().CollectAsync(
                new ManualValidationHardwareProofRequest
                {
                    RootPath = root,
                    RunCommands = true,
                    RepoRoot = root,
                    CliPath = "goatshot-test-cli.exe"
                },
                new FakeHardwareProofRunner(failedCommandName: "Android diagnostics"));

            Assert.IsTrue(result.Succeeded, result.Message);
            CollectionAssert.Contains(result.FailedCommands, "Android diagnostics");
            Assert.AreEqual(1, result.Summary.NonzeroCommandCount);
            CollectionAssert.Contains(result.Summary.NonzeroCommands, "Android diagnostics");

            var android = await File.ReadAllTextAsync(Path.Combine(root, "13-android-safe-device-proof.md"));
            StringAssert.Contains(android, "- [x] Blocked");
            StringAssert.Contains(android, "Android diagnostics exited 7");
            StringAssert.Contains(android, "Live Android safe-device proof was not performed");

            var summaryMarkdown = await File.ReadAllTextAsync(result.SummaryMarkdownPath);
            StringAssert.Contains(summaryMarkdown, "## Nonzero Diagnostics");
            StringAssert.Contains(summaryMarkdown, "Android diagnostics");
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

            var result = await new ManualValidationHardwareProofService().CollectAsync(new ManualValidationHardwareProofRequest
            {
                RootPath = root,
                RunCommands = false
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(ManualValidationLaneStatus.Blocked, result.Status);
            StringAssert.Contains(result.Message, "no command results");
            Assert.IsFalse(result.Summary.ReadinessEvidencePresent);
            Assert.IsTrue(result.Summary.LiveHardwareProofRequired);

            var lane = await File.ReadAllTextAsync(Path.Combine(root, "08-multi-monitor-recording.md"));
            StringAssert.Contains(lane, "- [x] Blocked");
            StringAssert.Contains(lane, "No hardware-proof commands were recorded");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ProcessRunner_TimedOutCommandReturnsPromptly()
    {
        var root = CreateTempRoot();
        try
        {
            var command = new ManualValidationHardwareProofCommandSpec(
                "Hung probe",
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                ["/c", "ping -n 10 127.0.0.1 >nul"],
                root,
                Path.Combine(root, "probe.json"),
                Path.Combine(root, "probe.log"),
                TimeoutSeconds: 1);
            var stopwatch = Stopwatch.StartNew();

            var result = await new ProcessManualValidationHardwareProofCommandRunner().RunAsync(command);

            stopwatch.Stop();
            Assert.IsTrue(result.TimedOut);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Timed out command returned too slowly: {stopwatch.Elapsed}");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-hardware-proof-test-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeHardwareProofRunner(string? failedCommandName = null) : IManualValidationHardwareProofCommandRunner
    {
        public Task<ManualValidationHardwareProofCommandResult> RunAsync(
            ManualValidationHardwareProofCommandSpec command,
            CancellationToken cancellationToken = default)
        {
            var exitCode = command.Name.Equals(failedCommandName, StringComparison.OrdinalIgnoreCase) ? 7 : 0;
            var stdout = exitCode == 0
                ? $$"""{"command":"{{command.Name}}","ready":true}"""
                : string.Empty;
            var stderr = exitCode == 0 ? string.Empty : $"{command.Name} unavailable";

            return Task.FromResult(ManualValidationHardwareProofCommandResult.FromSpec(
                command,
                exitCode,
                stdout,
                stderr,
                timedOut: false));
        }
    }
}

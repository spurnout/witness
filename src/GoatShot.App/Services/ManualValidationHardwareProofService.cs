using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class ManualValidationHardwareProofService
{
    public const string ProofDirectoryName = "hardware-proof";
    public const string CommandResultsFileName = "hardware-proof-command-results.json";
    public const string EnvironmentReportFileName = "environment.md";
    public const string EnvironmentJsonFileName = "environment.json";
    public const string SummaryMarkdownFileName = "hardware-proof-summary.md";
    public const string SummaryJsonFileName = "hardware-proof-summary.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyDictionary<string, HardwareProofLanePlan> LanePlans =
        new Dictionary<string, HardwareProofLanePlan>(StringComparer.OrdinalIgnoreCase)
        {
            ["multi-monitor-capture"] = new(
                "07-multi-monitor-capture.md",
                "Multi Monitor Capture",
                [
                    "hardware-proof/environment.md",
                    "hardware-proof/capture-engine-wgc.json",
                    "hardware-proof/hardware-proof-command-results.json"
                ],
                [
                    "Current display topology and capture-engine readiness were recorded.",
                    "This helper did not capture private desktop pixels.",
                    "A human all-monitor/cross-monitor capture pass is still required before this lane can be completed."
                ],
                "Live multi-monitor capture was not performed by this helper; keep this lane blocked until safe content is staged and output dimensions/content are reviewed."),
            ["multi-monitor-recording"] = new(
                "08-multi-monitor-recording.md",
                "Multi Monitor Recording",
                [
                    "hardware-proof/environment.md",
                    "hardware-proof/recording-preflight.json",
                    "hardware-proof/diagnostics-recording.json",
                    "hardware-proof/hardware-proof-command-results.json"
                ],
                [
                    "Current display topology and recording-engine readiness were recorded.",
                    "This helper did not record the desktop.",
                    "A human multi-monitor recording pass with safe content is still required before this lane can be completed."
                ],
                "Live multi-monitor recording was not performed by this helper; keep this lane blocked until a safe active/all-monitor/cross-monitor recording is reviewed."),
            ["long-recording"] = new(
                "09-long-recording.md",
                "Long Recording Stability",
                [
                    "hardware-proof/environment.md",
                    "hardware-proof/recording-preflight.json",
                    "hardware-proof/diagnostics-recording.json",
                    "hardware-proof/recording-devices.json",
                    "hardware-proof/diagnostics-devices.json",
                    "hardware-proof/hardware-proof-command-results.json"
                ],
                [
                    "Current recording engine, encoder, audio, and device readiness were recorded.",
                    "This helper skipped live 30-minute recording capture and retained no audio/video media.",
                    "A human long-recording pass with reviewed safe media is still required before this lane can be completed."
                ],
                "Long-run recording stability was not performed by this helper; keep this lane blocked until a reviewed safe recording proves playback, duration, sync, and recovery behavior."),
            ["android-safe-device-proof"] = new(
                "13-android-safe-device-proof.md",
                "Android Safe Device Proof",
                [
                    "hardware-proof/android-diagnostics.json",
                    "hardware-proof/hardware-proof-command-results.json"
                ],
                [
                    "Android diagnostics readiness was recorded without importing phone media.",
                    "This helper skipped screenshot, screenrecord, and preview execution against a phone.",
                    "A safe-device operator pass is still required before this lane can be completed."
                ],
                "Live Android safe-device proof was not performed by this helper; keep this lane blocked until safe phone content is staged and reviewed.")
        };

    public async Task<ManualValidationHardwareProofResult> CollectAsync(
        ManualValidationHardwareProofRequest request,
        IManualValidationHardwareProofCommandRunner? runner = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationHardwareProofResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationHardwareProofResult.Failed($"Manual validation folder was not found: {root}");
        }

        foreach (var plan in LanePlans.Values)
        {
            var path = Path.Combine(root, plan.FileName);
            if (!File.Exists(path))
            {
                return ManualValidationHardwareProofResult.Failed($"Expected manual validation template was not found: {path}");
            }
        }

        var proofRoot = Path.Combine(root, ProofDirectoryName);
        var logsRoot = Path.Combine(proofRoot, "logs");
        Directory.CreateDirectory(proofRoot);
        Directory.CreateDirectory(logsRoot);

        runner ??= new ProcessManualValidationHardwareProofCommandRunner();
        var commandResults = request.RunCommands
            ? await RunCommandsAsync(root, proofRoot, logsRoot, request, runner, cancellationToken)
            : await ReadExistingCommandResultsAsync(proofRoot, cancellationToken);

        var environment = BuildEnvironment(root, proofRoot, request);
        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, EnvironmentJsonFileName),
            JsonSerializer.Serialize(environment, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, EnvironmentReportFileName),
            BuildEnvironmentMarkdown(environment),
            cancellationToken);

        var status = ManualValidationLaneStatus.Blocked;
        foreach (var plan in LanePlans.Values)
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, plan.FileName),
                BuildLaneMarkdown(root, proofRoot, plan, status, commandResults, environment),
                cancellationToken);
        }

        var evidenceBeforeSummary = BuildEvidenceList(root, proofRoot);
        var failedCommands = commandResults
            .Where(command => command.ExitCode != 0)
            .ToList();
        var summary = BuildSummary(root, proofRoot, status, commandResults, failedCommands, environment, evidenceBeforeSummary);
        var summaryJsonPath = Path.Combine(proofRoot, SummaryJsonFileName);
        var summaryMarkdownPath = Path.Combine(proofRoot, SummaryMarkdownFileName);
        await File.WriteAllTextAsync(
            summaryJsonPath,
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            summaryMarkdownPath,
            BuildSummaryMarkdown(summary),
            cancellationToken);

        var result = new ManualValidationHardwareProofResult
        {
            Succeeded = commandResults.Count > 0,
            Message = commandResults.Count > 0
                ? "Hardware/device readiness proof packet was collected; live hardware lanes remain blocked."
                : "Hardware/device readiness proof packet is blocked: no command results were recorded.",
            RootPath = root,
            ProofDirectory = proofRoot,
            CommandResultsPath = Path.Combine(proofRoot, CommandResultsFileName),
            EnvironmentReportPath = Path.Combine(proofRoot, EnvironmentReportFileName),
            EnvironmentJsonPath = Path.Combine(proofRoot, EnvironmentJsonFileName),
            SummaryMarkdownPath = summaryMarkdownPath,
            SummaryJsonPath = summaryJsonPath,
            Summary = summary,
            Status = status,
            Commands = commandResults,
            FailedCommands = failedCommands.Select(command => command.Name).ToList(),
            UpdatedLaneFiles = LanePlans.Values.Select(plan => plan.FileName).ToList(),
            EvidenceFiles = BuildEvidenceList(root, proofRoot)
        };

        return result;
    }

    private static async Task<List<ManualValidationHardwareProofCommandResult>> RunCommandsAsync(
        string root,
        string proofRoot,
        string logsRoot,
        ManualValidationHardwareProofRequest request,
        IManualValidationHardwareProofCommandRunner runner,
        CancellationToken cancellationToken)
    {
        var commands = BuildCommands(root, proofRoot, logsRoot, request).ToList();
        var results = new List<ManualValidationHardwareProofCommandResult>();
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(command.LogPath)!);
            var result = await runner.RunAsync(command, cancellationToken);
            results.Add(result);
            await File.WriteAllTextAsync(command.OutputPath, FormatOutput(command, result), cancellationToken);
            await File.WriteAllTextAsync(command.LogPath, FormatCommandLog(result), cancellationToken);
        }

        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, CommandResultsFileName),
            JsonSerializer.Serialize(results, JsonOptions) + Environment.NewLine,
            cancellationToken);
        return results;
    }

    private static IEnumerable<ManualValidationHardwareProofCommandSpec> BuildCommands(
        string root,
        string proofRoot,
        string logsRoot,
        ManualValidationHardwareProofRequest request)
    {
        var repoRoot = string.IsNullOrWhiteSpace(request.RepoRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RepoRoot));
        var cliPath = ResolveCliPath(request.CliPath);
        var timeoutSeconds = request.TimeoutSeconds <= 0 ? 120 : request.TimeoutSeconds;

        ManualValidationHardwareProofCommandSpec Cmd(string name, string[] args, string outputName) =>
            new(name, cliPath, args, repoRoot, Path.Combine(proofRoot, outputName), Path.Combine(logsRoot, SafeFileName(name) + ".txt"), timeoutSeconds);

        yield return Cmd("Recording preflight", ["record", "preflight", "--json"], "recording-preflight.json");
        yield return Cmd("Recording devices", ["record", "devices", "--json"], "recording-devices.json");
        yield return Cmd("Recording diagnostics", ["diagnostics", "recording", "--json"], "diagnostics-recording.json");
        yield return Cmd("Device diagnostics", ["diagnostics", "devices", "--json"], "diagnostics-devices.json");
        yield return Cmd("WGC capture engine diagnostics", ["diagnostics", "capture-engine", "--engine", "wgc", "--json"], "capture-engine-wgc.json");
        yield return Cmd("Android diagnostics", ["diagnostics", "android", "--json"], "android-diagnostics.json");
    }

    private static async Task<List<ManualValidationHardwareProofCommandResult>> ReadExistingCommandResultsAsync(
        string proofRoot,
        CancellationToken cancellationToken)
    {
        var resultsPath = Path.Combine(proofRoot, CommandResultsFileName);
        if (!File.Exists(resultsPath))
        {
            return new List<ManualValidationHardwareProofCommandResult>();
        }

        try
        {
            var text = await File.ReadAllTextAsync(resultsPath, cancellationToken);
            return JsonSerializer.Deserialize<List<ManualValidationHardwareProofCommandResult>>(text, JsonOptions) ?? new List<ManualValidationHardwareProofCommandResult>();
        }
        catch (JsonException)
        {
            return new List<ManualValidationHardwareProofCommandResult>();
        }
    }

    private static ManualValidationHardwareEnvironment BuildEnvironment(
        string root,
        string proofRoot,
        ManualValidationHardwareProofRequest request)
    {
        var screens = Forms.Screen.AllScreens
            .Select(screen => new ManualValidationHardwareScreen
            {
                DeviceName = screen.DeviceName,
                Primary = screen.Primary,
                Bounds = $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}",
                WorkingArea = $"{screen.WorkingArea.X},{screen.WorkingArea.Y},{screen.WorkingArea.Width},{screen.WorkingArea.Height}"
            })
            .ToList();

        return new ManualValidationHardwareEnvironment
        {
            GeneratedAt = DateTimeOffset.Now,
            RootPath = root,
            ProofDirectory = proofRoot,
            WindowsVersion = Environment.OSVersion.VersionString,
            CliPath = ResolveCliPath(request.CliPath),
            ScreenCount = screens.Count,
            Screens = screens,
            Ffmpeg = FindOnPath("ffmpeg"),
            Ffprobe = FindOnPath("ffprobe")
        };
    }

    private static string BuildEnvironmentMarkdown(ManualValidationHardwareEnvironment environment)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Hardware And Device Proof Environment");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {environment.GeneratedAt:O}");
        builder.AppendLine();
        builder.AppendLine($"- Windows version: {environment.WindowsVersion}");
        builder.AppendLine($"- Screen count: {environment.ScreenCount.ToString(CultureInfo.InvariantCulture)}");
        foreach (var screen in environment.Screens)
        {
            builder.AppendLine($"  - {screen.DeviceName}: primary={Bool(screen.Primary)}, bounds={screen.Bounds}, workArea={screen.WorkingArea}");
        }

        builder.AppendLine($"- ffmpeg: {environment.Ffmpeg}");
        builder.AppendLine($"- ffprobe: {environment.Ffprobe}");
        builder.AppendLine();
        builder.AppendLine("Evidence limit: this file records current machine readiness only. It does not prove live multi-monitor capture, multi-monitor recording, long-run recording stability, Android safe-device media, or safe-content review.");
        return builder.ToString();
    }

    private static string BuildLaneMarkdown(
        string root,
        string proofRoot,
        HardwareProofLanePlan plan,
        ManualValidationLaneStatus status,
        IReadOnlyList<ManualValidationHardwareProofCommandResult> commands,
        ManualValidationHardwareEnvironment environment)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {plan.Title}");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime):yyyy-MM-dd}");
        builder.AppendLine($"Generated at: {DateTimeOffset.Now:O}");
        builder.AppendLine();
        builder.AppendLine("Purpose: Attach local hardware/device readiness evidence while keeping live hardware proof boundaries explicit.");
        builder.AppendLine();
        builder.AppendLine("## Requirement");
        builder.AppendLine();
        builder.AppendLine("- Classification: Hardware-gated");
        builder.AppendLine("- Rationale: This lane is required only when matching hardware/device content is available and safe to inspect.");
        builder.AppendLine("- Hardware-gated lanes stay visible but do not prove their corresponding claims until passed.");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.NotRun)}] Pending");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Passed)}] Passed");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Failed)}] Failed");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Blocked)}] Blocked");
        builder.AppendLine();
        builder.AppendLine("## Safety And Redaction");
        builder.AppendLine();
        builder.AppendLine("- [x] Safe demo content only; this helper collected text diagnostics and environment metadata, not private screenshots, recordings, or phone media.");
        builder.AppendLine("- [x] Provider account names, email addresses, OAuth codes, tokens, signed upload URLs, and private filesystem paths are redacted before sharing externally.");
        builder.AppendLine("- [x] Evidence files added for this lane are command/readiness artifacts intended for local handoff review.");
        builder.AppendLine("- [x] This lane is described as readiness evidence only, not hardware stability proof or accessibility certification.");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- Receipts build/package: see baseline lane and release proof bundle.");
        builder.AppendLine($"- Windows version: {environment.WindowsVersion}");
        builder.AppendLine($"- Display/monitor setup: {environment.ScreenCount.ToString(CultureInfo.InvariantCulture)} display(s) detected.");
        builder.AppendLine("- Input/audio/camera devices: see `hardware-proof/recording-devices.json` and `hardware-proof/diagnostics-devices.json`.");
        builder.AppendLine($"- FFmpeg/ffprobe availability: ffmpeg={environment.Ffmpeg}; ffprobe={environment.Ffprobe}.");
        builder.AppendLine($"- Diagnostics bundle or diagnostics output path: `{Path.GetRelativePath(root, proofRoot).Replace('\\', '/')}/`.");
        builder.AppendLine();
        builder.AppendLine("## Checklist");
        builder.AppendLine();
        foreach (var note in plan.ChecklistNotes)
        {
            builder.AppendLine($"- [x] {note}");
        }

        builder.AppendLine("- [ ] Human/operator live proof required before changing this lane to Passed.");
        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        foreach (var evidence in plan.Evidence)
        {
            var path = Path.Combine(root, evidence.Replace('/', Path.DirectorySeparatorChar));
            builder.AppendLine($"- [{Check(File.Exists(path) || Directory.Exists(path))}] `{evidence}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Command Results");
        builder.AppendLine();
        if (commands.Count == 0)
        {
            builder.AppendLine("- No hardware-proof commands were recorded; rerun with `manual-validation hardware-proof --run-commands`.");
        }
        else
        {
            foreach (var command in commands)
            {
                builder.AppendLine($"- {command.Name}: exit `{command.ExitCode}`; output `{Path.GetRelativePath(root, command.OutputPath).Replace('\\', '/')}`; log `{Path.GetRelativePath(root, command.LogPath).Replace('\\', '/')}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine("- Observed behavior: Hardware/device readiness diagnostics were collected for the current machine.");
        builder.AppendLine("- Defects or follow-ups: " + BuildBlockedReason(plan, commands, environment));
        builder.AppendLine("- Public claim boundary: Do not claim this lane passed until safe live hardware/device proof has been performed and the result is changed from Blocked to Passed by an operator.");
        return builder.ToString();
    }

    private static ManualValidationHardwareProofSummary BuildSummary(
        string root,
        string proofRoot,
        ManualValidationLaneStatus status,
        IReadOnlyList<ManualValidationHardwareProofCommandResult> commands,
        IReadOnlyList<ManualValidationHardwareProofCommandResult> failedCommands,
        ManualValidationHardwareEnvironment environment,
        IReadOnlyList<string> evidenceFiles)
    {
        var expectedEvidence = LanePlans.Values
            .SelectMany(plan => plan.Evidence)
            .Concat([
                $"hardware-proof/{CommandResultsFileName}",
                $"hardware-proof/{EnvironmentReportFileName}",
                $"hardware-proof/{EnvironmentJsonFileName}"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingEvidence = expectedEvidence
            .Where(path => !EvidenceExists(root, path))
            .ToList();

        return new ManualValidationHardwareProofSummary
        {
            GeneratedAt = DateTimeOffset.Now,
            Status = status,
            ProofDirectory = Path.GetRelativePath(root, proofRoot).Replace('\\', '/') + "/",
            CommandCount = commands.Count,
            NonzeroCommandCount = failedCommands.Count,
            NonzeroCommands = failedCommands.Select(command => command.Name).ToList(),
            EvidenceFileCount = evidenceFiles.Count,
            ExpectedEvidence = expectedEvidence,
            MissingEvidence = missingEvidence,
            ReadinessEvidencePresent = commands.Count > 0,
            LiveHardwareProofRequired = true,
            BlocksLocalV1Handoff = false,
            BlocksHardwareClaims = true,
            RemainingHardwareLanes = LanePlans.Values
                .Select(plan => plan.Title)
                .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WindowsVersion = environment.WindowsVersion,
            DisplayCount = environment.ScreenCount,
            Ffmpeg = environment.Ffmpeg,
            Ffprobe = environment.Ffprobe,
            ClaimBoundary = "This packet is current-machine readiness evidence only. It does not prove live multi-monitor capture, multi-monitor recording, long-run recording stability, Android safe-device media, or safe-content review.",
            NextOperatorSteps =
            [
                "Stage safe multi-monitor desktop content before capture or recording checks.",
                "Run and review live multi-monitor capture and recording output before passing those lanes.",
                "Run and review a long recording with safe microphone/system-audio/webcam content before passing the stability lane.",
                "Run Android screenshot/video/preview proof only against staged safe-device content before passing the Android lane."
            ]
        };
    }

    private static string BuildSummaryMarkdown(ManualValidationHardwareProofSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Hardware Proof Packet Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {summary.GeneratedAt:O}");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine($"- Status written to hardware-gated lanes: {summary.Status}");
        builder.AppendLine($"- Readiness evidence present: {Bool(summary.ReadinessEvidencePresent)}");
        builder.AppendLine($"- Commands recorded: {summary.CommandCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Nonzero diagnostics: {summary.NonzeroCommandCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Evidence files currently in packet: {summary.EvidenceFileCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Live hardware proof required: {Bool(summary.LiveHardwareProofRequired)}");
        builder.AppendLine($"- Blocks local V1 handoff: {Bool(summary.BlocksLocalV1Handoff)}");
        builder.AppendLine($"- Blocks hardware/device claims until operator lanes pass: {Bool(summary.BlocksHardwareClaims)}");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- Windows version: {summary.WindowsVersion}");
        builder.AppendLine($"- Display count: {summary.DisplayCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- ffmpeg: {summary.Ffmpeg}");
        builder.AppendLine($"- ffprobe: {summary.Ffprobe}");
        builder.AppendLine();
        builder.AppendLine("## Remaining Hardware Lanes");
        builder.AppendLine();
        foreach (var lane in summary.RemainingHardwareLanes)
        {
            builder.AppendLine($"- {lane}");
        }

        builder.AppendLine();
        builder.AppendLine("## Missing Evidence");
        builder.AppendLine();
        if (summary.MissingEvidence.Count == 0)
        {
            builder.AppendLine("- None from the expected readiness evidence set.");
        }
        else
        {
            foreach (var evidence in summary.MissingEvidence)
            {
                builder.AppendLine($"- `{evidence}`");
            }
        }

        if (summary.NonzeroCommands.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Nonzero Diagnostics");
            builder.AppendLine();
            foreach (var command in summary.NonzeroCommands)
            {
                builder.AppendLine($"- {command}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Next Operator Steps");
        builder.AppendLine();
        foreach (var step in summary.NextOperatorSteps)
        {
            builder.AppendLine($"- {step}");
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine();
        builder.AppendLine(summary.ClaimBoundary);
        builder.AppendLine();
        builder.AppendLine("Do not mark hardware-gated lanes Passed from this summary alone.");
        return builder.ToString();
    }

    private static bool EvidenceExists(string root, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(root, normalized);
        if (!relativePath.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        var directory = Path.GetDirectoryName(path);
        var pattern = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(directory) &&
            Directory.Exists(directory) &&
            Directory.EnumerateFileSystemEntries(directory, pattern).Any();
    }

    private static string BuildBlockedReason(
        HardwareProofLanePlan plan,
        IReadOnlyList<ManualValidationHardwareProofCommandResult> commands,
        ManualValidationHardwareEnvironment environment)
    {
        if (plan.FileName.StartsWith("07-", StringComparison.Ordinal) ||
            plan.FileName.StartsWith("08-", StringComparison.Ordinal))
        {
            return environment.ScreenCount < 2
                ? $"Only {environment.ScreenCount.ToString(CultureInfo.InvariantCulture)} display(s) were detected; {plan.BlockedReason}"
                : plan.BlockedReason;
        }

        if (plan.FileName.StartsWith("13-", StringComparison.Ordinal))
        {
            var android = commands.FirstOrDefault(command => command.Name.Equals("Android diagnostics", StringComparison.OrdinalIgnoreCase));
            return android is not null && android.ExitCode != 0
                ? $"Android diagnostics exited {android.ExitCode.ToString(CultureInfo.InvariantCulture)}; {plan.BlockedReason}"
                : plan.BlockedReason;
        }

        return plan.BlockedReason;
    }

    private static List<string> BuildEvidenceList(string root, string proofRoot)
    {
        if (!Directory.Exists(proofRoot))
        {
            return new List<string>();
        }

        return Directory.EnumerateFiles(proofRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatOutput(
        ManualValidationHardwareProofCommandSpec command,
        ManualValidationHardwareProofCommandResult result)
    {
        if (Path.GetExtension(command.OutputPath).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.StandardOutput.TrimEnd() + Environment.NewLine;
        }

        return FormatCommandLog(result);
    }

    private static string FormatCommandLog(ManualValidationHardwareProofCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {result.FileName} {string.Join(' ', result.Arguments)}");
        builder.AppendLine($"WorkingDirectory: {result.WorkingDirectory}");
        builder.AppendLine($"OutputPath: {result.OutputPath}");
        builder.AppendLine($"ExitCode: {result.ExitCode}");
        builder.AppendLine($"TimedOut: {result.TimedOut}");
        builder.AppendLine();
        builder.AppendLine("STDOUT:");
        builder.AppendLine(result.StandardOutput ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("STDERR:");
        builder.AppendLine(result.StandardError ?? string.Empty);
        return builder.ToString();
    }

    private static string ResolveCliPath(string? requestedCliPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedCliPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedCliPath));
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) &&
            (Path.GetFileName(Environment.ProcessPath).Equals(BrandIdentity.CommandLineExecutableName, StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(Environment.ProcessPath).Equals($"{BrandIdentity.LegacyProductName}.Cli.exe", StringComparison.OrdinalIgnoreCase)))
        {
            return Environment.ProcessPath!;
        }

        var buildDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "GoatShot.Cli",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0");
        var currentPath = Path.Combine(buildDirectory, BrandIdentity.CommandLineExecutableName);
        var legacyPath = Path.Combine(buildDirectory, $"{BrandIdentity.LegacyProductName}.Cli.exe");
        return File.Exists(currentPath) || !File.Exists(legacyPath) ? currentPath : legacyPath;
    }

    private static string FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return "not-detected";
        }

        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(part.Trim(), executable + ".exe");
            if (File.Exists(candidate))
            {
                return "detected";
            }
        }

        return "not-detected";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = name.Select(ch => invalid.Contains(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars).Replace(' ', '-');
    }

    private static string Check(bool value) => value ? "x" : " ";
    private static string Bool(bool value) => value ? "yes" : "no";

    private sealed record HardwareProofLanePlan(
        string FileName,
        string Title,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> ChecklistNotes,
        string BlockedReason);
}

public sealed class ProcessManualValidationHardwareProofCommandRunner : IManualValidationHardwareProofCommandRunner
{
    public async Task<ManualValidationHardwareProofCommandResult> RunAsync(
        ManualValidationHardwareProofCommandSpec command,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return ManualValidationHardwareProofCommandResult.FromSpec(command, -1, string.Empty, "Process did not start.", timedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var timeout = TimeSpan.FromSeconds(command.TimeoutSeconds <= 0 ? 120 : command.TimeoutSeconds);
        var exited = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                // The process either exited during cleanup or left an inherited pipe open.
            }

            return ManualValidationHardwareProofCommandResult.FromSpec(
                command,
                -1,
                await SafeReadAsync(stdoutTask, TimeSpan.FromSeconds(2)),
                await SafeReadAsync(stderrTask, TimeSpan.FromSeconds(2)),
                timedOut: true);
        }

        return ManualValidationHardwareProofCommandResult.FromSpec(
            command,
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut: false);
    }

    private static async Task<string> SafeReadAsync(Task<string> task, TimeSpan timeout)
    {
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException)
        {
            return string.Empty;
        }
    }
}

public interface IManualValidationHardwareProofCommandRunner
{
    Task<ManualValidationHardwareProofCommandResult> RunAsync(
        ManualValidationHardwareProofCommandSpec command,
        CancellationToken cancellationToken = default);
}

public sealed record ManualValidationHardwareProofRequest
{
    public string RootPath { get; init; } = string.Empty;
    public bool RunCommands { get; init; }
    public string? RepoRoot { get; init; }
    public string? CliPath { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record ManualValidationHardwareProofCommandSpec(
    string Name,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string OutputPath,
    string LogPath,
    int TimeoutSeconds);

public sealed record ManualValidationHardwareProofCommandResult
{
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public static ManualValidationHardwareProofCommandResult FromSpec(
        ManualValidationHardwareProofCommandSpec spec,
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut) =>
        new()
        {
            Name = spec.Name,
            FileName = spec.FileName,
            Arguments = spec.Arguments.ToArray(),
            WorkingDirectory = spec.WorkingDirectory,
            OutputPath = spec.OutputPath,
            LogPath = spec.LogPath,
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = SensitiveTextDetector.Redact(standardOutput),
            StandardError = SensitiveTextDetector.Redact(standardError)
        };
}

public sealed class ManualValidationHardwareEnvironment
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string ProofDirectory { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public string CliPath { get; set; } = string.Empty;
    public int ScreenCount { get; set; }
    public List<ManualValidationHardwareScreen> Screens { get; set; } = new();
    public string Ffmpeg { get; set; } = string.Empty;
    public string Ffprobe { get; set; } = string.Empty;
}

public sealed class ManualValidationHardwareScreen
{
    public string DeviceName { get; set; } = string.Empty;
    public bool Primary { get; set; }
    public string Bounds { get; set; } = string.Empty;
    public string WorkingArea { get; set; } = string.Empty;
}

public sealed class ManualValidationHardwareProofResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string ProofDirectory { get; set; } = string.Empty;
    public string CommandResultsPath { get; set; } = string.Empty;
    public string EnvironmentReportPath { get; set; } = string.Empty;
    public string EnvironmentJsonPath { get; set; } = string.Empty;
    public string SummaryMarkdownPath { get; set; } = string.Empty;
    public string SummaryJsonPath { get; set; } = string.Empty;
    public ManualValidationHardwareProofSummary Summary { get; set; } = new();
    public ManualValidationLaneStatus Status { get; set; }
    public List<ManualValidationHardwareProofCommandResult> Commands { get; set; } = new();
    public List<string> FailedCommands { get; set; } = new();
    public List<string> UpdatedLaneFiles { get; set; } = new();
    public List<string> EvidenceFiles { get; set; } = new();

    public static ManualValidationHardwareProofResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Status = ManualValidationLaneStatus.Failed
        };
}

public sealed class ManualValidationHardwareProofSummary
{
    public DateTimeOffset GeneratedAt { get; set; }
    public ManualValidationLaneStatus Status { get; set; }
    public string ProofDirectory { get; set; } = string.Empty;
    public int CommandCount { get; set; }
    public int NonzeroCommandCount { get; set; }
    public List<string> NonzeroCommands { get; set; } = new();
    public int EvidenceFileCount { get; set; }
    public List<string> ExpectedEvidence { get; set; } = new();
    public List<string> MissingEvidence { get; set; } = new();
    public bool ReadinessEvidencePresent { get; set; }
    public bool LiveHardwareProofRequired { get; set; }
    public bool BlocksLocalV1Handoff { get; set; }
    public bool BlocksHardwareClaims { get; set; }
    public List<string> RemainingHardwareLanes { get; set; } = new();
    public string WindowsVersion { get; set; } = string.Empty;
    public int DisplayCount { get; set; }
    public string Ffmpeg { get; set; } = string.Empty;
    public string Ffprobe { get; set; } = string.Empty;
    public string ClaimBoundary { get; set; } = string.Empty;
    public List<string> NextOperatorSteps { get; set; } = new();
}

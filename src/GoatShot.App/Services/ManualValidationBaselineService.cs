using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class ManualValidationBaselineService
{
    public const string BaselineFileName = "01-baseline-setup.md";
    public const string CommandResultsFileName = "baseline-command-results.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ManualValidationBaselineResult> CompleteAsync(
        ManualValidationBaselineRequest request,
        IManualValidationBaselineCommandRunner? runner = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationBaselineResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationBaselineResult.Failed($"Manual validation folder was not found: {root}");
        }

        var baselinePath = Path.Combine(root, BaselineFileName);
        if (!File.Exists(baselinePath))
        {
            return ManualValidationBaselineResult.Failed($"Baseline template was not found: {baselinePath}");
        }

        var diagnosticsRoot = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);

        runner ??= new ProcessManualValidationBaselineCommandRunner();
        var commandResults = request.RunCommands
            ? await RunCommandsAsync(root, diagnosticsRoot, request, runner, cancellationToken)
            : await ReadExistingCommandResultsAsync(diagnosticsRoot, cancellationToken);

        var evidence = BuildEvidence(root, diagnosticsRoot);
        var failedCommands = commandResults
            .Where(command => command.ExitCode != 0)
            .ToList();
        var missingRequiredEvidence = evidence
            .Where(item => item.Required && !item.Exists)
            .ToList();

        var status = ManualValidationLaneStatus.Passed;
        var message = "Baseline setup evidence is complete.";
        if (failedCommands.Count > 0)
        {
            status = ManualValidationLaneStatus.Failed;
            message = $"Baseline setup has {failedCommands.Count} failed command(s).";
        }
        else if (missingRequiredEvidence.Count > 0)
        {
            status = ManualValidationLaneStatus.Blocked;
            message = $"Baseline setup is missing {missingRequiredEvidence.Count} required evidence file(s).";
        }

        await File.WriteAllTextAsync(
            baselinePath,
            BuildBaselineMarkdown(root, diagnosticsRoot, status, commandResults, evidence, request.RunCommands),
            cancellationToken);

        var result = new ManualValidationBaselineResult
        {
            Succeeded = status is ManualValidationLaneStatus.Passed,
            Message = message,
            RootPath = root,
            BaselinePath = baselinePath,
            DiagnosticsDirectory = diagnosticsRoot,
            CommandResultsPath = Path.Combine(diagnosticsRoot, CommandResultsFileName),
            Status = status,
            Commands = commandResults,
            Evidence = evidence,
            MissingRequiredEvidence = missingRequiredEvidence.Select(item => item.RelativePath).ToList(),
            FailedCommands = failedCommands.Select(command => command.Name).ToList()
        };

        return result;
    }

    private static async Task<List<ManualValidationBaselineCommandResult>> RunCommandsAsync(
        string root,
        string diagnosticsRoot,
        ManualValidationBaselineRequest request,
        IManualValidationBaselineCommandRunner runner,
        CancellationToken cancellationToken)
    {
        var commands = BuildCommands(root, diagnosticsRoot, request).ToList();
        var results = new List<ManualValidationBaselineCommandResult>();
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await runner.RunAsync(command, cancellationToken);
            results.Add(result);
            Directory.CreateDirectory(Path.GetDirectoryName(command.OutputPath)!);
            await File.WriteAllTextAsync(
                command.OutputPath,
                FormatCommandOutput(command, result),
                cancellationToken);
        }

        await File.WriteAllTextAsync(
            Path.Combine(diagnosticsRoot, CommandResultsFileName),
            JsonSerializer.Serialize(results, JsonOptions) + Environment.NewLine,
            cancellationToken);
        return results;
    }

    private static async Task<List<ManualValidationBaselineCommandResult>> ReadExistingCommandResultsAsync(
        string diagnosticsRoot,
        CancellationToken cancellationToken)
    {
        var resultsPath = Path.Combine(diagnosticsRoot, CommandResultsFileName);
        if (!File.Exists(resultsPath))
        {
            return new List<ManualValidationBaselineCommandResult>();
        }

        try
        {
            var text = await File.ReadAllTextAsync(resultsPath, cancellationToken);
            return JsonSerializer.Deserialize<List<ManualValidationBaselineCommandResult>>(text, JsonOptions) ?? new List<ManualValidationBaselineCommandResult>();
        }
        catch (JsonException)
        {
            return new List<ManualValidationBaselineCommandResult>();
        }
    }

    private static IEnumerable<ManualValidationBaselineCommandSpec> BuildCommands(
        string root,
        string diagnosticsRoot,
        ManualValidationBaselineRequest request)
    {
        var repoRoot = string.IsNullOrWhiteSpace(request.RepoRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RepoRoot));
        var cliPath = ResolveCliPath(request.CliPath);
        var timeoutSeconds = request.TimeoutSeconds <= 0 ? 300 : request.TimeoutSeconds;

        yield return new ManualValidationBaselineCommandSpec(
            "Release build",
            "dotnet",
            new[] { "build", ".\\GoatShot.slnx", "-c", "Release" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "build-release.txt"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "Release tests",
            "dotnet",
            new[] { "test", ".\\GoatShot.slnx", "-c", "Release" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "test-release.txt"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "CLI diagnostics print",
            cliPath,
            new[] { "diagnostics", "print" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "diagnostics-print.txt"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "CLI diagnostics bundle",
            cliPath,
            new[] { "diagnostics", "bundle", "--output", Path.Combine(diagnosticsRoot, "goatshot-diagnostics.zip") },
            repoRoot,
            Path.Combine(diagnosticsRoot, "diagnostics-bundle.txt"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "Recording readiness",
            cliPath,
            new[] { "diagnostics", "recording", "--json" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "recording-readiness.json"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "Recording devices",
            cliPath,
            new[] { "record", "devices", "--json" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "recording-devices.json"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "WGC capture engine diagnostics",
            cliPath,
            new[] { "diagnostics", "capture-engine", "--engine", "wgc", "--json" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "capture-engine-wgc.json"),
            timeoutSeconds);
        yield return new ManualValidationBaselineCommandSpec(
            "Provider diagnostics",
            cliPath,
            new[] { "diagnostics", "providers", "--json" },
            repoRoot,
            Path.Combine(diagnosticsRoot, "providers.json"),
            timeoutSeconds);
    }

    private static string ResolveCliPath(string? requestedCliPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedCliPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedCliPath));
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath!;
        }

        return Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "GoatShot.Cli",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0",
            "GoatShot.Cli.exe");
    }

    private static List<ManualValidationBaselineEvidenceItem> BuildEvidence(string root, string diagnosticsRoot)
    {
        var distRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "dist"));
        var distZip = Directory.Exists(distRoot)
            ? Directory.EnumerateFiles(distRoot, "GoatShot-*-portable.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        return new List<ManualValidationBaselineEvidenceItem>
        {
            Item(diagnosticsRoot, "build-release.txt", true),
            Item(diagnosticsRoot, "test-release.txt", true),
            Item(diagnosticsRoot, "diagnostics-print.txt", true),
            Item(diagnosticsRoot, "goatshot-diagnostics.zip", true),
            Item(diagnosticsRoot, "recording-readiness.json", true),
            Item(diagnosticsRoot, "recording-devices.json", true),
            Item(diagnosticsRoot, "capture-engine-wgc.json", true),
            Item(diagnosticsRoot, "providers.json", true),
            Item(diagnosticsRoot, CommandResultsFileName, true),
            new()
            {
                RelativePath = string.IsNullOrWhiteSpace(distZip)
                    ? "artifacts/dist/GoatShot-*-portable.zip"
                    : Path.GetRelativePath(root, distZip).Replace('\\', '/'),
                Path = distZip ?? string.Empty,
                Exists = !string.IsNullOrWhiteSpace(distZip) && File.Exists(distZip),
                Required = false
            }
        };
    }

    private static ManualValidationBaselineEvidenceItem Item(string diagnosticsRoot, string fileName, bool required)
    {
        var path = Path.Combine(diagnosticsRoot, fileName);
        return new ManualValidationBaselineEvidenceItem
        {
            RelativePath = Path.Combine("diagnostics", fileName).Replace('\\', '/'),
            Path = path,
            Exists = File.Exists(path),
            Required = required
        };
    }

    private static string BuildBaselineMarkdown(
        string root,
        string diagnosticsRoot,
        ManualValidationLaneStatus status,
        IReadOnlyList<ManualValidationBaselineCommandResult> commands,
        IReadOnlyList<ManualValidationBaselineEvidenceItem> evidence,
        bool commandsWereRun)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Baseline Setup");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime):yyyy-MM-dd}");
        builder.AppendLine($"Generated at: {DateTimeOffset.Now:O}");
        builder.AppendLine();
        builder.AppendLine("Purpose: Record the build, package, machine, diagnostics, and capability probes before manual proof starts.");
        builder.AppendLine();
        builder.AppendLine("## Requirement");
        builder.AppendLine();
        builder.AppendLine("- Classification: Required");
        builder.AppendLine("- Rationale: Required before handoff so the manual run has current build, diagnostics, and environment context.");
        builder.AppendLine("- Required lanes block the local V1 manual handoff when left pending or not run.");
        builder.AppendLine("- Hardware-gated and optional compatibility lanes must stay visible but do not prove their corresponding claims until passed.");
        builder.AppendLine("- Parked lanes stay out of the current proof gate until explicitly scheduled.");
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
        builder.AppendLine("- [x] Safe demo content only; this baseline helper collected command evidence and diagnostics, not private desktop media.");
        builder.AppendLine("- [x] Provider account names, email addresses, OAuth codes, tokens, signed upload URLs, and private filesystem paths are redacted before sharing externally.");
        builder.AppendLine("- [x] Evidence files added for this lane are local diagnostic/text artifacts and were intended for local handoff review.");
        builder.AppendLine("- [x] This lane is described as observed behavior only, not full compliance certification.");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- GoatShot build/package: {(commands.Any(command => command.Name.Equals("Release build", StringComparison.OrdinalIgnoreCase) && command.ExitCode == 0) ? "Release build passed" : "see diagnostics/build-release.txt")}.");
        builder.AppendLine($"- Windows version: {Environment.OSVersion.VersionString}");
        builder.AppendLine($"- Display/monitor setup: {Forms.Screen.AllScreens.Length.ToString(CultureInfo.InvariantCulture)} display(s) detected by Windows Forms.");
        builder.AppendLine("- Input/audio/camera devices: see `diagnostics/recording-devices.json`.");
        builder.AppendLine($"- FFmpeg/ffprobe availability: ffmpeg={FindOnPath("ffmpeg")}; ffprobe={FindOnPath("ffprobe")}.");
        builder.AppendLine($"- Diagnostics bundle or diagnostics output path: `{Path.GetRelativePath(root, diagnosticsRoot).Replace('\\', '/')}/`.");
        builder.AppendLine();
        builder.AppendLine("## Checklist");
        builder.AppendLine();
        var buildPassed = commands.Any(command => command.Name.Equals("Release build", StringComparison.OrdinalIgnoreCase) && command.ExitCode == 0);
        var testsPassed = commands.Any(command => command.Name.Equals("Release tests", StringComparison.OrdinalIgnoreCase) && command.ExitCode == 0);
        var packageExists = evidence.Any(item => item.RelativePath.Contains("portable.zip", StringComparison.OrdinalIgnoreCase) && item.Exists);
        var diagnosticsPrintExists = EvidenceExists(evidence, "diagnostics-print.txt");
        var recordingReadinessExists = EvidenceExists(evidence, "recording-readiness.json");
        var recordingDevicesExists = EvidenceExists(evidence, "recording-devices.json");
        var providersExists = EvidenceExists(evidence, "providers.json");
        var captureEngineExists = EvidenceExists(evidence, "capture-engine-wgc.json");
        builder.AppendLine($"- [{Check(packageExists || buildPassed)}] Start from the latest portable package or Release build.");
        builder.AppendLine($"- [{Check(buildPassed && testsPassed)}] Confirm the tested source state passes Release build and tests.");
        builder.AppendLine($"- [{Check(diagnosticsPrintExists)}] Run CLI diagnostics print and save output under diagnostics/.");
        builder.AppendLine($"- [{Check(recordingReadinessExists && recordingDevicesExists && providersExists && captureEngineExists)}] Run diagnostics recording, devices, providers, and capture-engine probes.");
        builder.AppendLine($"- [{Check(recordingReadinessExists && recordingDevicesExists)}] Record whether WGC, D3D11, Media Foundation H.264/HEVC, WASAPI, camera, FFmpeg, and ffprobe are detected.");
        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        builder.AppendLine("- Screenshots: none; baseline uses command evidence only.");
        builder.AppendLine("- Recordings: none; baseline uses command evidence only.");
        builder.AppendLine("- Command output:");
        foreach (var item in evidence.Where(item => item.Exists && item.RelativePath.StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine($"  - `{item.RelativePath}`");
        }

        builder.AppendLine("- Related files:");
        foreach (var item in evidence.Where(item => item.Exists && !item.RelativePath.StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine($"  - `{item.RelativePath}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Command Results");
        builder.AppendLine();
        if (commands.Count == 0)
        {
            builder.AppendLine(commandsWereRun
                ? "- No command results were recorded."
                : "- Existing command results were not found; rerun with `manual-validation baseline --run-commands`.");
        }
        else
        {
            foreach (var command in commands)
            {
                builder.AppendLine($"- {command.Name}: exit `{command.ExitCode}`; log `{Path.GetRelativePath(root, command.OutputPath).Replace('\\', '/')}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine(status switch
        {
            ManualValidationLaneStatus.Passed => "- Observed behavior: Local baseline command evidence is present and all collected commands exited 0.",
            ManualValidationLaneStatus.Failed => "- Observed behavior: One or more baseline commands failed; review command output before manual proof continues.",
            ManualValidationLaneStatus.Blocked => "- Observed behavior: Baseline command evidence is incomplete; rerun with `manual-validation baseline --run-commands`.",
            _ => "- Observed behavior: Baseline has not been run."
        });
        builder.AppendLine(status switch
        {
            ManualValidationLaneStatus.Passed => "- Defects or follow-ups: none from the command-only baseline helper.",
            ManualValidationLaneStatus.Failed => "- Defects or follow-ups: fix failed baseline command(s) and regenerate the summary.",
            ManualValidationLaneStatus.Blocked => "- Defects or follow-ups: missing baseline evidence must be collected before handoff.",
            _ => "- Defects or follow-ups: baseline pending."
        });
        builder.AppendLine("- Public claim boundary: This baseline proves current local build/test/diagnostic evidence only; it does not prove keyboard traversal, screen-reader behavior, text scaling, high contrast, live region drag, hardware stability, clean Windows VM behavior, installer install/uninstall, Android live-device behavior, or OAuth/live-provider accounts.");
        return builder.ToString();
    }

    private static string Check(bool value) => value ? "x" : " ";

    private static bool EvidenceExists(
        IEnumerable<ManualValidationBaselineEvidenceItem> evidence,
        string fileName) =>
        evidence.Any(item => item.RelativePath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) && item.Exists);

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

    private static string FormatCommandOutput(
        ManualValidationBaselineCommandSpec command,
        ManualValidationBaselineCommandResult result)
    {
        if (Path.GetExtension(command.OutputPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(result.StandardOutput)
                ? string.Empty
                : result.StandardOutput.TrimEnd() + Environment.NewLine;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Command: {result.FileName} {string.Join(' ', result.Arguments)}");
        builder.AppendLine($"WorkingDirectory: {result.WorkingDirectory}");
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
}

public sealed class ProcessManualValidationBaselineCommandRunner : IManualValidationBaselineCommandRunner
{
    public async Task<ManualValidationBaselineCommandResult> RunAsync(
        ManualValidationBaselineCommandSpec command,
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
            return ManualValidationBaselineCommandResult.FromSpec(command, -1, string.Empty, "Process did not start.", timedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var timeout = TimeSpan.FromSeconds(command.TimeoutSeconds <= 0 ? 300 : command.TimeoutSeconds);
        var exited = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between timeout check and kill.
            }

            return ManualValidationBaselineCommandResult.FromSpec(command, -1, await SafeReadAsync(stdoutTask), await SafeReadAsync(stderrTask), timedOut: true);
        }

        return ManualValidationBaselineCommandResult.FromSpec(
            command,
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut: false);
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public interface IManualValidationBaselineCommandRunner
{
    Task<ManualValidationBaselineCommandResult> RunAsync(
        ManualValidationBaselineCommandSpec command,
        CancellationToken cancellationToken = default);
}

public sealed record ManualValidationBaselineRequest
{
    public string RootPath { get; init; } = string.Empty;
    public bool RunCommands { get; init; }
    public string? RepoRoot { get; init; }
    public string? CliPath { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
}

public sealed record ManualValidationBaselineCommandSpec(
    string Name,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string OutputPath,
    int TimeoutSeconds);

public sealed record ManualValidationBaselineCommandResult
{
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public static ManualValidationBaselineCommandResult FromSpec(
        ManualValidationBaselineCommandSpec spec,
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
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = SensitiveTextDetector.Redact(standardOutput),
            StandardError = SensitiveTextDetector.Redact(standardError)
        };
}

public sealed record ManualValidationBaselineEvidenceItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool Required { get; init; }
}

public sealed class ManualValidationBaselineResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string BaselinePath { get; set; } = string.Empty;
    public string DiagnosticsDirectory { get; set; } = string.Empty;
    public string CommandResultsPath { get; set; } = string.Empty;
    public ManualValidationLaneStatus Status { get; set; }
    public List<ManualValidationBaselineCommandResult> Commands { get; set; } = new();
    public List<ManualValidationBaselineEvidenceItem> Evidence { get; set; } = new();
    public List<string> MissingRequiredEvidence { get; set; } = new();
    public List<string> FailedCommands { get; set; } = new();

    public static ManualValidationBaselineResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Status = ManualValidationLaneStatus.Failed
        };
}

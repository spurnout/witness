using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class ManualValidationOperatorPackService
{
    public const string DefaultDirectoryName = "required-desktop-operator-pack";
    public const string ChecklistFileName = "required-desktop-operator-checklist.md";
    public const string CommandReferenceFileName = "record-lane-command-reference.ps1";
    public const string ManifestFileName = "operator-pack-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ManualValidationOperatorPackResult> CreateAsync(
        ManualValidationOperatorPackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationOperatorPackResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationOperatorPackResult.Failed($"Manual validation folder was not found: {root}");
        }

        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.Combine(root, DefaultDirectoryName)
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        Directory.CreateDirectory(outputRoot);

        var proofPlan = await new ManualValidationProofPlanService().CreateAsync(
            new ManualValidationProofPlanRequest
            {
                RootPath = root,
                OutputPath = root
            },
            cancellationToken);

        var result = new ManualValidationOperatorPackResult
        {
            Succeeded = proofPlan.Succeeded,
            Message = proofPlan.Succeeded
                ? $"Manual validation operator pack written: {outputRoot}"
                : $"Manual validation operator pack written with issue(s): {outputRoot}",
            RootPath = root,
            OutputRoot = outputRoot,
            GeneratedAt = DateTimeOffset.Now,
            ChecklistPath = Path.Combine(outputRoot, ChecklistFileName),
            CommandReferencePath = Path.Combine(outputRoot, CommandReferenceFileName),
            ManifestPath = Path.Combine(outputRoot, ManifestFileName),
            ProofPlanPath = proofPlan.PlanJsonPath,
            SummaryJsonPath = proofPlan.SummaryJsonPath,
            RequiredOpenCount = proofPlan.RequiredOpenCount,
            HardwareGatedOpenCount = proofPlan.HardwareGatedOpenCount,
            OptionalCompatibilityOpenCount = proofPlan.OptionalCompatibilityOpenCount,
            ParkedCount = proofPlan.ParkedCount
        };
        result.Issues.AddRange(proofPlan.Issues);
        result.Warnings.AddRange(proofPlan.Warnings);

        var requiredDesktopLanes = proofPlan.Lanes
            .Where(lane => lane.Requirement is ManualValidationLaneRequirement.Required)
            .Where(lane => !lane.Id.Equals("baseline", StringComparison.OrdinalIgnoreCase))
            .Where(lane => lane.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable)
            .OrderBy(lane => lane.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var lane in requiredDesktopLanes)
        {
            var laneDirectory = Path.Combine(outputRoot, lane.Id);
            Directory.CreateDirectory(laneDirectory);
            var notesPath = Path.Combine(laneDirectory, "notes.md");
            var relativeNotesPath = ToRootRelative(root, notesPath);
            await File.WriteAllTextAsync(
                notesPath,
                BuildLaneNotes(root, lane, relativeNotesPath),
                cancellationToken);

            result.Lanes.Add(new ManualValidationOperatorPackLane
            {
                Id = lane.Id,
                Title = lane.Title,
                Status = lane.Status,
                RelativeTemplatePath = lane.RelativePath,
                NotesPath = notesPath,
                NotesRelativePath = relativeNotesPath,
                RecordPassedCommand = BuildRecordCommand(request, root, lane.Id, "passed", "<replace with observed pass note>", relativeNotesPath),
                RecordFailedCommand = BuildRecordCommand(request, root, lane.Id, "failed", "<replace with observed failure and follow-up>", relativeNotesPath),
                RecordBlockedCommand = BuildRecordCommand(request, root, lane.Id, "blocked", "<replace with blocker and next action>", relativeNotesPath)
            });
        }

        await File.WriteAllTextAsync(
            result.ChecklistPath,
            BuildChecklist(request, root, outputRoot, proofPlan, result.Lanes),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.CommandReferencePath,
            BuildCommandReference(request, root, result.Lanes),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.ManifestPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken);

        return result;
    }

    private static string BuildLaneNotes(
        string root,
        ManualValidationProofPlanLane lane,
        string relativeNotesPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {lane.Title} Operator Notes");
        builder.AppendLine();
        builder.AppendLine($"Template: `{lane.RelativePath}`");
        builder.AppendLine($"Status before operator pass: `{lane.Status}`");
        builder.AppendLine($"Evidence file: `{relativeNotesPath}`");
        builder.AppendLine();
        builder.AppendLine("## Safety");
        builder.AppendLine();
        builder.AppendLine("- Use safe demo content only.");
        builder.AppendLine("- Do not include private desktop pixels, account identifiers, OAuth screens, chats, email, credentials, customer data, or private filesystem paths.");
        builder.AppendLine("- This is observed behavior only, not accessibility compliance certification.");
        builder.AppendLine("- Keep this file inside the manual-validation folder so redaction scanning can review it.");
        builder.AppendLine();
        AppendList(builder, "Operator Steps", lane.OperatorSteps);
        AppendList(builder, "Recommended Evidence", lane.RecommendedEvidence);
        builder.AppendLine("## Observed Result");
        builder.AppendLine();
        builder.AppendLine("- Result: pass / fail / blocked");
        builder.AppendLine("- Operator:");
        builder.AppendLine("- Date/time:");
        builder.AppendLine("- Environment changes made and restored:");
        builder.AppendLine("- Evidence files reviewed:");
        builder.AppendLine();
        builder.AppendLine("## Findings");
        builder.AppendLine();
        builder.AppendLine("- Focus/order/naming/contrast/layout issue:");
        builder.AppendLine("- Reproduction steps:");
        builder.AppendLine("- Severity:");
        builder.AppendLine("- Suggested follow-up:");
        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine();
        builder.AppendLine(lane.ClaimBoundary);
        builder.AppendLine();
        builder.AppendLine("After the human pass is complete, use the matching command from `record-lane-command-reference.ps1` and include this notes file as evidence.");
        return builder.ToString();
    }

    private static string BuildChecklist(
        ManualValidationOperatorPackRequest request,
        string root,
        string outputRoot,
        ManualValidationProofPlanResult proofPlan,
        IReadOnlyList<ManualValidationOperatorPackLane> lanes)
    {
        var appPath = ResolveAppPath(request.AppPath);
        var builder = new StringBuilder();
        builder.AppendLine("# Required Desktop Operator Pack");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Manual validation folder: `{root}`");
        builder.AppendLine($"Output folder: `{outputRoot}`");
        builder.AppendLine($"Proof plan: `{proofPlan.PlanMarkdownPath}`");
        builder.AppendLine($"Summary JSON: `{proofPlan.SummaryJsonPath}`");
        builder.AppendLine();
        builder.AppendLine("## Counts");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Required open lanes: `{proofPlan.RequiredOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hardware-gated open lanes: `{proofPlan.HardwareGatedOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Optional compatibility open lanes: `{proofPlan.OptionalCompatibilityOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Parked lanes: `{proofPlan.ParkedCount}`");
        builder.AppendLine();
        builder.AppendLine("## Safe Staging Commands");
        builder.AppendLine();
        builder.AppendLine("These commands are optional staging aids; they do not complete the lanes by themselves.");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine($"{PsQuote(appPath)} --proof-scene");
        builder.AppendLine($"{PsQuote(appPath)} --render-proof-scene-output {PsQuote(Path.Combine(outputRoot, "proof-scene.png"))}");
        builder.AppendLine($"{PsQuote(appPath)} --audit-wpf-surface proof-scene --audit-wpf-output {PsQuote(Path.Combine(outputRoot, "proof-scene-accessibility.md"))}");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Required Lane Files");
        builder.AppendLine();
        if (lanes.Count == 0)
        {
            builder.AppendLine("- No required desktop lanes are currently open.");
        }
        else
        {
            foreach (var lane in lanes)
            {
                builder.AppendLine($"- `{lane.NotesRelativePath}` - {lane.Title} (`{lane.Status}`)");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Recording Results");
        builder.AppendLine();
        builder.AppendLine("Use `record-lane-command-reference.ps1` as a command template after a human/operator pass. Edit the note text first. The generated script prints commands and does not record results by itself.");
        builder.AppendLine();
        builder.AppendLine("## Boundaries");
        builder.AppendLine();
        builder.AppendLine("- Do not mark a lane `Passed` unless the named human interaction was actually performed.");
        builder.AppendLine("- Do not claim full accessibility compliance from this pack.");
        builder.AppendLine("- Do not include private desktop screenshots, account data, tokens, OAuth screens, or live provider proof.");
        builder.AppendLine("- Hardware-gated and OAuth/live-provider lanes remain outside this required desktop operator pack.");
        return builder.ToString();
    }

    private static string BuildCommandReference(
        ManualValidationOperatorPackRequest request,
        string root,
        IReadOnlyList<ManualValidationOperatorPackLane> lanes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts manual-validation command reference");
        builder.AppendLine("# This script prints command templates. It does not execute record-lane commands.");
        builder.AppendLine("# Edit the --note value and run one printed command only after a human/operator pass.");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Write-Output {PsQuote("# Manual folder: " + root)}");
        builder.AppendLine();
        foreach (var lane in lanes)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Write-Output {PsQuote("# " + lane.Title)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Write-Output {PsQuote(lane.RecordPassedCommand)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Write-Output {PsQuote(lane.RecordFailedCommand)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"Write-Output {PsQuote(lane.RecordBlockedCommand)}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildRecordCommand(
        ManualValidationOperatorPackRequest request,
        string root,
        string laneId,
        string status,
        string note,
        string evidencePath)
    {
        var cliPath = ResolveCliPath(request.CliPath);
        return string.Join(
            " ",
            [
                "&",
                PsQuote(cliPath),
                "manual-validation",
                "record-lane",
                "--folder",
                PsQuote(root),
                "--lane",
                PsQuote(laneId),
                "--status",
                PsQuote(status),
                "--note",
                PsQuote(note),
                "--evidence",
                PsQuote(evidencePath),
                "--json"
            ]);
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var value in values)
            {
                builder.AppendLine($"- {value}");
            }
        }

        builder.AppendLine();
    }

    private static string ToRootRelative(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
            {
                return relative.Replace('\\', '/');
            }
        }
        catch (ArgumentException)
        {
            // Fall through to file-name-only display.
        }

        return Path.GetFileName(path);
    }

    private static string ResolveCliPath(string? requestedCliPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedCliPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedCliPath));
        }

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            var packagedCli = Path.Combine(processDirectory, BrandIdentity.CommandLineExecutableName);
            if (File.Exists(packagedCli))
            {
                return packagedCli;
            }

            var legacyPackagedCli = Path.Combine(processDirectory, $"{BrandIdentity.LegacyProductName}.Cli.exe");
            if (File.Exists(legacyPackagedCli))
            {
                return legacyPackagedCli;
            }
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

    private static string ResolveAppPath(string? requestedAppPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedAppPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedAppPath));
        }

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            var packagedApp = Path.Combine(processDirectory, BrandIdentity.DesktopExecutableName);
            if (File.Exists(packagedApp))
            {
                return packagedApp;
            }

            var legacyPackagedApp = Path.Combine(processDirectory, $"{BrandIdentity.LegacyProductName}.exe");
            if (File.Exists(legacyPackagedApp))
            {
                return legacyPackagedApp;
            }
        }

        var buildDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "GoatShot.App",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0");
        var currentPath = Path.Combine(buildDirectory, BrandIdentity.DesktopExecutableName);
        var legacyPath = Path.Combine(buildDirectory, $"{BrandIdentity.LegacyProductName}.exe");
        return File.Exists(currentPath) || !File.Exists(legacyPath) ? currentPath : legacyPath;
    }

    private static string PsQuote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

public sealed class ManualValidationOperatorPackRequest
{
    public string RootPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? CliPath { get; set; }
    public string? AppPath { get; set; }
}

public sealed class ManualValidationOperatorPackResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string ChecklistPath { get; set; } = string.Empty;
    public string CommandReferencePath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ProofPlanPath { get; set; } = string.Empty;
    public string SummaryJsonPath { get; set; } = string.Empty;
    public int RequiredOpenCount { get; set; }
    public int HardwareGatedOpenCount { get; set; }
    public int OptionalCompatibilityOpenCount { get; set; }
    public int ParkedCount { get; set; }
    public List<ManualValidationOperatorPackLane> Lanes { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ManualValidationOperatorPackResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = [SensitiveTextDetector.Redact(message)]
        };
}

public sealed class ManualValidationOperatorPackLane
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ManualValidationLaneStatus Status { get; set; }
    public string RelativeTemplatePath { get; set; } = string.Empty;
    public string NotesPath { get; set; } = string.Empty;
    public string NotesRelativePath { get; set; } = string.Empty;
    public string RecordPassedCommand { get; set; } = string.Empty;
    public string RecordFailedCommand { get; set; } = string.Empty;
    public string RecordBlockedCommand { get; set; } = string.Empty;
}

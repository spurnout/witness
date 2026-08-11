using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class ManualValidationProofPlanService
{
    private const string PlanMarkdownFileName = "manual-validation-proof-plan.md";
    private const string PlanJsonFileName = "manual-validation-proof-plan.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ManualValidationProofPlanResult> CreateAsync(
        ManualValidationProofPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationProofPlanResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationProofPlanResult.Failed($"Manual validation folder was not found: {root}");
        }

        var summary = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
        {
            RootPath = root
        }, cancellationToken);

        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? root
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        Directory.CreateDirectory(outputRoot);

        var result = new ManualValidationProofPlanResult
        {
            Succeeded = summary.Redaction.Findings.Count == 0,
            Message = summary.Redaction.Findings.Count == 0
                ? $"Manual validation proof plan written: {outputRoot}"
                : $"Manual validation proof plan written with redaction warning(s): {outputRoot}",
            RootPath = root,
            OutputRoot = outputRoot,
            GeneratedAt = DateTimeOffset.Now,
            SummarySucceeded = summary.Succeeded,
            SummaryJsonPath = summary.SummaryJsonPath,
            SummaryMarkdownPath = summary.SummaryMarkdownPath,
            PlanMarkdownPath = Path.Combine(outputRoot, PlanMarkdownFileName),
            PlanJsonPath = Path.Combine(outputRoot, PlanJsonFileName),
            RedactionStatus = summary.Redaction.Status
        };

        result.Lanes.AddRange(summary.Lanes.Select(BuildLane));
        result.RequiredOpenCount = result.Lanes.Count(lane =>
            lane.BlocksLocalV1Handoff && lane.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable);
        result.HardwareGatedOpenCount = result.Lanes.Count(lane =>
            lane.Requirement is ManualValidationLaneRequirement.HardwareGated &&
            lane.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable);
        result.OptionalCompatibilityOpenCount = result.Lanes.Count(lane =>
            lane.Requirement is ManualValidationLaneRequirement.OptionalCompatibility &&
            lane.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable);
        result.ParkedCount = result.Lanes.Count(lane => lane.Requirement is ManualValidationLaneRequirement.Parked);
        result.Issues.AddRange(summary.Issues);
        result.Warnings.AddRange(summary.Warnings);

        await File.WriteAllTextAsync(
            result.PlanJsonPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            result.PlanMarkdownPath,
            BuildMarkdown(result),
            cancellationToken);

        return result;
    }

    private static ManualValidationProofPlanLane BuildLane(ManualValidationLaneSummary summary)
    {
        var open = summary.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable;
        var lane = new ManualValidationProofPlanLane
        {
            Id = summary.Id,
            Title = summary.Title,
            Requirement = summary.Requirement,
            RequirementLabel = FormatRequirement(summary.Requirement),
            RequirementRationale = summary.RequirementRationale,
            BlocksLocalV1Handoff = summary.BlocksLocalV1Handoff,
            Status = summary.Status,
            RelativePath = summary.RelativePath,
            EvidenceFolder = GetEvidenceFolder(summary.Id),
            IsOpen = open,
            ClaimBoundary = BuildClaimBoundary(summary)
        };

        lane.OperatorSteps.AddRange(BuildOperatorSteps(summary));
        lane.RecommendedEvidence.AddRange(BuildRecommendedEvidence(summary));
        return lane;
    }

    private static IReadOnlyList<string> BuildOperatorSteps(ManualValidationLaneSummary summary) =>
        summary.Id switch
        {
            "baseline" => new[]
            {
                "Open the evidence folder and complete environment fields in 01-baseline-setup.md.",
                "Run the commands from commands.md for build, tests, diagnostics print, diagnostics recording, devices, capture engine, and providers.",
                "Save command outputs under diagnostics/ and keep receipts-diagnostics.zip attached (legacy goatshot-diagnostics.zip remains accepted).",
                "Mark Passed only after the build/test/diagnostics outputs are present and reviewed for redaction."
            },
            "keyboard-traversal" => new[]
            {
                "Use only keyboard input from app startup.",
                "Tab through Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.",
                "Record focus-order problems, missing focus visuals, disabled-control behavior, and controls that require the mouse.",
                "Mark Failed or Blocked with notes if any core task cannot be completed."
            },
            "screen-reader" => new[]
            {
                "Start Narrator and NVDA if available.",
                "Traverse Main Window, Settings, Recording controls, Editor, AI review, upload confirmation/result, share history, and queue states.",
                "Record controls with missing names, ambiguous state, noisy repetition, or unreadable status changes.",
                "Keep this as observed AT behavior only, not compliance certification."
            },
            "text-scaling" => new[]
            {
                "Set Windows text scaling to 125%, then 150%, and 200% if usable on the display.",
                "Check Main Window, Settings, Editor, Recording controls, AI review, Share History, and Upload Result.",
                "Record clipped labels, overlapping controls, unreachable actions, missing scroll regions, or unreadable dense panels.",
                "Restore the operator's preferred Windows scaling after the pass."
            },
            "high-contrast" => new[]
            {
                "Enable a Windows high-contrast theme.",
                "Check text, focus outlines, selected states, disabled states, scroll regions, dialogs, and primary actions.",
                "Record unreadable text, invisible selection/focus, or controls that lose affordance.",
                "Restore the operator's preferred Windows theme after the pass."
            },
            "live-region-drag" => new[]
            {
                "Stage safe desktop content with no private files, chats, email, credentials, or customer data visible.",
                "Start interactive region capture from Receipts.",
                "Verify drag, resize, cancel, complete, edge snapping, Shift snap bypass, pixel lens, size badge, padding, and chooser behavior.",
                "Keep screenshots or short clips only if they contain safe demo content."
            },
            "clean-machine-install" => new[]
            {
                "Use the latest portable ZIP on a clean Windows profile or VM.",
                "Set isolated RECEIPTS_LOCAL_ROOT and RECEIPTS_LIBRARY_ROOT; legacy GOATSHOT_* aliases remain valid for older packages.",
                "Launch Receipts.exe (or GoatShot.exe for a legacy input package) and confirm app folders are created, Settings can be saved, and a simple capture/import/edit/export loop works.",
                "If installer tooling is available, separately record installer compile/install/uninstall behavior; otherwise keep installer compilation skipped."
            },
            _ => BuildGenericSteps(summary)
        };

    private static IReadOnlyList<string> BuildGenericSteps(ManualValidationLaneSummary summary)
    {
        if (summary.Requirement is ManualValidationLaneRequirement.HardwareGated)
        {
            return new[]
            {
                "Stage only safe content and confirm the required hardware/device is available.",
                "Run the lane checklist in the matching template.",
                "Attach bounded diagnostics and reviewed safe media only.",
                "Do not claim this hardware/device capability until the lane is passed."
            };
        }

        if (summary.Requirement is ManualValidationLaneRequirement.OptionalCompatibility)
        {
            return new[]
            {
                "Run only if this compatibility claim is needed.",
                "Use safe fixtures and generated helper commands.",
                "Attach screenshots, redacted payloads, and validation output.",
                "Do not treat this optional proof as required for the current local V1 candidate."
            };
        }

        if (summary.Requirement is ManualValidationLaneRequirement.Parked)
        {
            return new[]
            {
                "Leave this lane NotRun while OAuth/live-provider proof is parked.",
                "Schedule a dedicated account-proof tranche before using real provider accounts.",
                "Use disposable/test accounts and redact account identifiers, OAuth codes, tokens, and signed URLs when reopened."
            };
        }

        return new[]
        {
            "Complete the checklist in the matching lane template.",
            "Attach safe, redacted evidence.",
            "Mark Passed, Failed, or Blocked with operator notes."
        };
    }

    private static IReadOnlyList<string> BuildRecommendedEvidence(ManualValidationLaneSummary summary) =>
        summary.Id switch
        {
            "baseline" => new[]
            {
                "diagnostics/build-release.txt",
                "diagnostics/test-release.txt",
                "diagnostics/diagnostics-print.txt",
                "diagnostics/receipts-diagnostics.zip (legacy goatshot-diagnostics.zip accepted)"
            },
            "keyboard-traversal" => new[]
            {
                "keyboard-traversal-notes.md or completed 02-keyboard-traversal.md",
                "safe screenshots for focus failures only",
                "short safe clip when a focus trap or keyboard-only blocker occurs"
            },
            "screen-reader" => new[]
            {
                "screen-reader-observations.md or completed 03-screen-reader-narrator-nvda.md",
                "safe screenshots only for controls that are hard to identify from notes"
            },
            "text-scaling" => new[]
            {
                "125-percent-main-settings.png",
                "150-percent-editor-recording.png",
                "200-percent-overlap-proof.png if usable"
            },
            "high-contrast" => new[]
            {
                "high-contrast-main.png",
                "high-contrast-settings.png",
                "high-contrast-focus-or-selected-state.png"
            },
            "live-region-drag" => new[]
            {
                "region-drag-start.png",
                "region-drag-snapping.png",
                "region-drag-complete.png or short safe clip"
            },
            "clean-machine-install" => new[]
            {
                "portable-first-launch.png",
                "isolated-paths.txt",
                "settings-save-proof.png",
                "capture-export-loop-proof.png",
                "installer-skipped-or-compiled-note.md"
            },
            _ => new[]
            {
                "completed lane template",
                "safe screenshots or short clips when useful",
                "diagnostics output relevant to this lane"
            }
        };

    private static string BuildClaimBoundary(ManualValidationLaneSummary summary) =>
        summary.Requirement switch
        {
            ManualValidationLaneRequirement.Required =>
                "Blocks local V1 manual handoff until passed, failed with accepted follow-up, or explicitly waived.",
            ManualValidationLaneRequirement.HardwareGated =>
                "Does not block the local V1 candidate when hardware/content is unavailable, but the matching hardware claim remains unproven.",
            ManualValidationLaneRequirement.OptionalCompatibility =>
                "Optional compatibility evidence; absence does not block the current V1 claim unless this compatibility is advertised.",
            ManualValidationLaneRequirement.Parked =>
                "Out of current scope. Do not run or claim this proof until a dedicated account/OAuth tranche is scheduled.",
            _ => "Claim boundary must be reviewed before public handoff."
        };

    private static string GetEvidenceFolder(string laneId) =>
        laneId.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "-", StringComparison.OrdinalIgnoreCase);

    private static string BuildMarkdown(ManualValidationProofPlanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Manual Validation Proof Plan");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {result.GeneratedAt:O}");
        builder.AppendLine($"Root: `{result.RootPath}`");
        builder.AppendLine($"Summary JSON: `{result.SummaryJsonPath}`");
        builder.AppendLine($"Summary Markdown: `{result.SummaryMarkdownPath}`");
        builder.AppendLine($"Summary complete: `{result.SummarySucceeded}`");
        builder.AppendLine($"Redaction status: `{result.RedactionStatus}`");
        builder.AppendLine();
        builder.AppendLine("## Counts");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Required open lanes: `{result.RequiredOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hardware-gated open lanes: `{result.HardwareGatedOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Optional compatibility open lanes: `{result.OptionalCompatibilityOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Parked lanes: `{result.ParkedCount}`");
        builder.AppendLine();
        AppendLaneTable(builder, "Required Local V1 Handoff Lanes", result.Lanes.Where(lane => lane.Requirement is ManualValidationLaneRequirement.Required));
        AppendLaneDetails(builder, result.Lanes.Where(lane =>
            lane.Requirement is ManualValidationLaneRequirement.Required &&
            lane.Status is not ManualValidationLaneStatus.Passed and not ManualValidationLaneStatus.NotApplicable));
        AppendLaneTable(builder, "Hardware-Gated Claim Boundaries", result.Lanes.Where(lane => lane.Requirement is ManualValidationLaneRequirement.HardwareGated));
        AppendLaneTable(builder, "Optional Compatibility Claim Boundaries", result.Lanes.Where(lane => lane.Requirement is ManualValidationLaneRequirement.OptionalCompatibility));
        AppendLaneTable(builder, "Parked Lanes", result.Lanes.Where(lane => lane.Requirement is ManualValidationLaneRequirement.Parked));
        AppendList(builder, "Summary Issues", result.Issues);
        AppendList(builder, "Summary Warnings", result.Warnings);
        return builder.ToString();
    }

    private static void AppendLaneTable(StringBuilder builder, string title, IEnumerable<ManualValidationProofPlanLane> lanes)
    {
        var list = lanes.ToList();
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (list.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        builder.AppendLine("| Lane | Requirement | Status | Blocks Local V1 | Template | Boundary |");
        builder.AppendLine("|---|---|---:|---:|---|---|");
        foreach (var lane in list)
        {
            builder.AppendLine($"| {lane.Title} | {lane.RequirementLabel} | `{lane.Status}` | {(lane.BlocksLocalV1Handoff ? "yes" : "no")} | `{lane.RelativePath}` | {lane.ClaimBoundary} |");
        }
    }

    private static void AppendLaneDetails(StringBuilder builder, IEnumerable<ManualValidationProofPlanLane> lanes)
    {
        var list = lanes.ToList();
        builder.AppendLine();
        builder.AppendLine("## Required Lane Runbook");
        builder.AppendLine();
        if (list.Count == 0)
        {
            builder.AppendLine("- No required lanes are open.");
            return;
        }

        foreach (var lane in list)
        {
            builder.AppendLine($"### {lane.Title}");
            builder.AppendLine();
            builder.AppendLine($"Template: `{lane.RelativePath}`");
            builder.AppendLine($"Evidence folder hint: `{lane.EvidenceFolder}/`");
            builder.AppendLine($"Boundary: {lane.ClaimBoundary}");
            AppendInlineList(builder, "Operator steps", lane.OperatorSteps);
            AppendInlineList(builder, "Recommended evidence", lane.RecommendedEvidence);
            builder.AppendLine();
        }
    }

    private static void AppendInlineList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine();
        builder.AppendLine($"{title}:");
        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }

    private static string FormatRequirement(ManualValidationLaneRequirement requirement) =>
        requirement switch
        {
            ManualValidationLaneRequirement.Required => "Required",
            ManualValidationLaneRequirement.HardwareGated => "Hardware-gated",
            ManualValidationLaneRequirement.OptionalCompatibility => "Optional compatibility",
            ManualValidationLaneRequirement.Parked => "Parked",
            _ => requirement.ToString()
        };
}

public sealed class ManualValidationProofPlanRequest
{
    public string RootPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
}

public sealed class ManualValidationProofPlanResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public bool SummarySucceeded { get; set; }
    public string SummaryJsonPath { get; set; } = string.Empty;
    public string SummaryMarkdownPath { get; set; } = string.Empty;
    public string PlanMarkdownPath { get; set; } = string.Empty;
    public string PlanJsonPath { get; set; } = string.Empty;
    public string RedactionStatus { get; set; } = "clean";
    public int RequiredOpenCount { get; set; }
    public int HardwareGatedOpenCount { get; set; }
    public int OptionalCompatibilityOpenCount { get; set; }
    public int ParkedCount { get; set; }
    public List<ManualValidationProofPlanLane> Lanes { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ManualValidationProofPlanResult Failed(string message)
    {
        return new ManualValidationProofPlanResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = [SensitiveTextDetector.Redact(message)]
        };
    }
}

public sealed class ManualValidationProofPlanLane
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ManualValidationLaneRequirement Requirement { get; set; }
    public string RequirementLabel { get; set; } = string.Empty;
    public string RequirementRationale { get; set; } = string.Empty;
    public bool BlocksLocalV1Handoff { get; set; }
    public ManualValidationLaneStatus Status { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string EvidenceFolder { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string ClaimBoundary { get; set; } = string.Empty;
    public List<string> OperatorSteps { get; set; } = new();
    public List<string> RecommendedEvidence { get; set; } = new();
}

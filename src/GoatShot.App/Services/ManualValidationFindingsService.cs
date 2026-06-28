using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class ManualValidationFindingsService
{
    public const string FindingsMarkdownFileName = "manual-validation-findings.md";
    public const string FindingsJsonFileName = "manual-validation-findings.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ManualValidationFindingsResult> CreateAsync(
        ManualValidationFindingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationFindingsResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationFindingsResult.Failed($"Manual validation folder was not found: {root}");
        }

        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? root
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        Directory.CreateDirectory(outputRoot);

        var summary = await new ManualValidationSummaryService().SummarizeAsync(
            new ManualValidationSummaryRequest { RootPath = root },
            cancellationToken);

        var result = new ManualValidationFindingsResult
        {
            RootPath = root,
            OutputRoot = outputRoot,
            GeneratedAt = DateTimeOffset.Now,
            SummarySucceeded = summary.Succeeded,
            SummaryJsonPath = summary.SummaryJsonPath,
            SummaryMarkdownPath = summary.SummaryMarkdownPath,
            FindingsMarkdownPath = Path.Combine(outputRoot, FindingsMarkdownFileName),
            FindingsJsonPath = Path.Combine(outputRoot, FindingsJsonFileName),
            RedactionStatus = summary.Redaction.Status
        };

        foreach (var finding in summary.Redaction.Findings)
        {
            result.Findings.Add(BuildRedactionFinding(finding));
        }

        foreach (var lane in summary.Lanes)
        {
            var finding = BuildLaneFinding(lane);
            if (finding is not null)
            {
                result.Findings.Add(finding);
            }
        }

        result.Findings = result.Findings
            .OrderBy(finding => SeverityOrder(finding.Severity))
            .ThenBy(finding => RequirementOrder(finding.Requirement))
            .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.RequiredBlockingCount = result.Findings.Count(finding =>
            finding.Requirement is ManualValidationLaneRequirement.Required &&
            finding.BlocksLocalV1Handoff);
        result.HardwareGatedOpenCount = result.Findings.Count(finding =>
            finding.Requirement is ManualValidationLaneRequirement.HardwareGated);
        result.OptionalCompatibilityOpenCount = result.Findings.Count(finding =>
            finding.Requirement is ManualValidationLaneRequirement.OptionalCompatibility);
        result.ParkedCount = result.Findings.Count(finding =>
            finding.Requirement is ManualValidationLaneRequirement.Parked);
        result.RedactionFindingCount = result.Findings.Count(finding =>
            finding.Category.Equals("RedactionRisk", StringComparison.OrdinalIgnoreCase));
        result.Succeeded = result.RedactionFindingCount == 0;
        result.Message = result.Succeeded
            ? $"Manual validation findings written: {outputRoot}"
            : $"Manual validation findings written with redaction warning(s): {outputRoot}";

        await File.WriteAllTextAsync(
            result.FindingsJsonPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            result.FindingsMarkdownPath,
            BuildMarkdown(result),
            cancellationToken);

        return result;
    }

    private static ManualValidationFinding BuildRedactionFinding(ManualValidationRedactionFinding finding) =>
        new()
        {
            Id = $"redaction:{finding.RelativePath}",
            Title = "Potential Sensitive Data",
            Category = "RedactionRisk",
            Severity = ManualValidationFindingSeverity.P0,
            Requirement = ManualValidationLaneRequirement.Required,
            RequirementLabel = "Required",
            Status = ManualValidationLaneStatus.Failed,
            Source = finding.RelativePath,
            BlocksLocalV1Handoff = true,
            EvidenceCount = finding.Count,
            Summary = $"Potential sensitive data in `{finding.RelativePath}`: {finding.Summary}",
            RecommendedAction = "Remove or redact the flagged evidence before handoff, then rerun manual-validation summarize/findings.",
            ClaimBoundary = "Do not publish or bundle manual proof while redaction findings remain."
        };

    private static ManualValidationFinding? BuildLaneFinding(ManualValidationLaneSummary lane)
    {
        if (lane.Requirement is ManualValidationLaneRequirement.Parked)
        {
            return new ManualValidationFinding
            {
                Id = lane.Id,
                Title = lane.Title,
                Category = "ParkedScope",
                Severity = ManualValidationFindingSeverity.Parked,
                Requirement = lane.Requirement,
                RequirementLabel = FormatRequirement(lane.Requirement),
                Status = lane.Status,
                Source = lane.RelativePath,
                BlocksLocalV1Handoff = false,
                EvidenceCount = lane.EvidenceCount,
                Summary = $"{lane.Title} remains parked. {lane.RequirementRationale}",
                RecommendedAction = "Keep this lane parked until a dedicated OAuth/live-account proof tranche is scheduled.",
                ClaimBoundary = "Do not claim live provider account, consent-screen, refresh-token, upload, or remote-delete proof."
            };
        }

        if (lane.Status is ManualValidationLaneStatus.Passed or ManualValidationLaneStatus.NotApplicable)
        {
            return null;
        }

        var category = lane.Requirement switch
        {
            ManualValidationLaneRequirement.Required => lane.Status switch
            {
                ManualValidationLaneStatus.Failed => "ObservedRequiredFailure",
                ManualValidationLaneStatus.Blocked => "RequiredBlocked",
                ManualValidationLaneStatus.Missing => "RequiredTemplateMissing",
                _ => "RequiredProofMissing"
            },
            ManualValidationLaneRequirement.HardwareGated => "HardwareGatedClaimBoundary",
            ManualValidationLaneRequirement.OptionalCompatibility => "OptionalCompatibilityOpen",
            _ => "ManualValidationFinding"
        };

        var severity = lane.Requirement switch
        {
            ManualValidationLaneRequirement.Required => lane.Status is ManualValidationLaneStatus.Failed or ManualValidationLaneStatus.Missing
                ? ManualValidationFindingSeverity.P0
                : ManualValidationFindingSeverity.P1,
            ManualValidationLaneRequirement.HardwareGated => lane.Status is ManualValidationLaneStatus.Failed
                ? ManualValidationFindingSeverity.P1
                : ManualValidationFindingSeverity.P2,
            ManualValidationLaneRequirement.OptionalCompatibility => ManualValidationFindingSeverity.P3,
            _ => ManualValidationFindingSeverity.P3
        };

        return new ManualValidationFinding
        {
            Id = lane.Id,
            Title = lane.Title,
            Category = category,
            Severity = severity,
            Requirement = lane.Requirement,
            RequirementLabel = FormatRequirement(lane.Requirement),
            Status = lane.Status,
            Source = lane.RelativePath,
            BlocksLocalV1Handoff = lane.BlocksLocalV1Handoff,
            EvidenceCount = lane.EvidenceCount,
            Summary = BuildLaneSummary(lane),
            RecommendedAction = BuildRecommendedAction(lane),
            ClaimBoundary = BuildClaimBoundary(lane)
        };
    }

    private static string BuildLaneSummary(ManualValidationLaneSummary lane) =>
        lane.Status switch
        {
            ManualValidationLaneStatus.Missing =>
                $"{lane.Title} template is missing from the manual-validation folder.",
            ManualValidationLaneStatus.Failed =>
                $"{lane.Title} has an observed failure that must be patched or explicitly accepted before the matching claim is made.",
            ManualValidationLaneStatus.Blocked =>
                $"{lane.Title} is blocked and still needs operator evidence or an accepted release boundary.",
            ManualValidationLaneStatus.NotRun =>
                $"{lane.Title} has not been run. {lane.RequirementRationale}",
            _ => $"{lane.Title} requires review. {lane.RequirementRationale}"
        };

    private static string BuildRecommendedAction(ManualValidationLaneSummary lane) =>
        lane.Requirement switch
        {
            ManualValidationLaneRequirement.Required => lane.Status switch
            {
                ManualValidationLaneStatus.Missing =>
                    "Regenerate the manual-validation harness, then rerun summary, proof-plan, and findings.",
                ManualValidationLaneStatus.Failed =>
                    "Patch the confirmed defect, add focused proof, rerun the exact lane, then regenerate summary/proof-plan/findings.",
                ManualValidationLaneStatus.Blocked =>
                    "Run the operator lane with safe evidence or document an explicit release waiver; patch concrete blockers before retry.",
                _ =>
                    "Complete the required operator lane with safe evidence using manual-validation record-lane."
            },
            ManualValidationLaneRequirement.HardwareGated =>
                "Stage safe hardware/device content and run the matching proof lane before advertising this hardware claim.",
            ManualValidationLaneRequirement.OptionalCompatibility =>
                "Reopen and run only if this compatibility claim will be advertised; otherwise keep it out of the V1 claim set.",
            _ => "Review this lane before public handoff."
        };

    private static string BuildClaimBoundary(ManualValidationLaneSummary lane) =>
        lane.Requirement switch
        {
            ManualValidationLaneRequirement.Required =>
                "Blocks local V1 manual handoff until passed, patched, or explicitly waived.",
            ManualValidationLaneRequirement.HardwareGated =>
                "Does not block the local V1 candidate when hardware/content is unavailable, but the matching claim remains unproven.",
            ManualValidationLaneRequirement.OptionalCompatibility =>
                "Optional compatibility evidence; absence does not block V1 unless this compatibility is advertised.",
            ManualValidationLaneRequirement.Parked =>
                "Out of current scope until a dedicated live-account proof tranche is scheduled.",
            _ => "Review claim boundary before handoff."
        };

    private static string BuildMarkdown(ManualValidationFindingsResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Manual Validation Findings");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {result.GeneratedAt:O}");
        builder.AppendLine($"Root: `{result.RootPath}`");
        builder.AppendLine($"Summary JSON: `{result.SummaryJsonPath}`");
        builder.AppendLine($"Summary complete: `{result.SummarySucceeded}`");
        builder.AppendLine($"Redaction status: `{result.RedactionStatus}`");
        builder.AppendLine();
        builder.AppendLine("## Counts");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Total findings: `{result.Findings.Count}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Release-blocking required findings: `{result.RequiredBlockingCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Hardware-gated claim boundaries: `{result.HardwareGatedOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Optional compatibility findings: `{result.OptionalCompatibilityOpenCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Parked lanes: `{result.ParkedCount}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Redaction findings: `{result.RedactionFindingCount}`");
        AppendFindingTable(builder, "Release-Blocking Required Findings", result.Findings.Where(finding => finding.BlocksLocalV1Handoff));
        AppendFindingTable(builder, "Hardware-Gated Claim Boundaries", result.Findings.Where(finding => finding.Requirement is ManualValidationLaneRequirement.HardwareGated));
        AppendFindingTable(builder, "Optional And Parked Boundaries", result.Findings.Where(finding =>
            finding.Requirement is ManualValidationLaneRequirement.OptionalCompatibility or ManualValidationLaneRequirement.Parked));
        AppendFindingDetails(builder, result.Findings);
        return builder.ToString();
    }

    private static void AppendFindingTable(
        StringBuilder builder,
        string title,
        IEnumerable<ManualValidationFinding> findings)
    {
        var list = findings.ToList();
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (list.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        builder.AppendLine("| Priority | Category | Finding | Status | Source | Action |");
        builder.AppendLine("|---:|---|---|---:|---|---|");
        foreach (var finding in list)
        {
            builder.AppendLine($"| `{finding.Severity}` | {finding.Category} | {finding.Title} | `{finding.Status}` | `{finding.Source}` | {finding.RecommendedAction} |");
        }
    }

    private static void AppendFindingDetails(StringBuilder builder, IReadOnlyList<ManualValidationFinding> findings)
    {
        builder.AppendLine();
        builder.AppendLine("## Details");
        builder.AppendLine();
        if (findings.Count == 0)
        {
            builder.AppendLine("- No manual validation findings.");
            return;
        }

        foreach (var finding in findings)
        {
            builder.AppendLine($"### {finding.Severity} - {finding.Title}");
            builder.AppendLine();
            builder.AppendLine($"- Category: `{finding.Category}`");
            builder.AppendLine($"- Requirement: `{finding.RequirementLabel}`");
            builder.AppendLine($"- Status: `{finding.Status}`");
            builder.AppendLine($"- Source: `{finding.Source}`");
            builder.AppendLine($"- Blocks local V1 handoff: `{finding.BlocksLocalV1Handoff}`");
            builder.AppendLine($"- Summary: {finding.Summary}");
            builder.AppendLine($"- Recommended action: {finding.RecommendedAction}");
            builder.AppendLine($"- Claim boundary: {finding.ClaimBoundary}");
            builder.AppendLine();
        }
    }

    private static int SeverityOrder(ManualValidationFindingSeverity severity) =>
        severity switch
        {
            ManualValidationFindingSeverity.P0 => 0,
            ManualValidationFindingSeverity.P1 => 1,
            ManualValidationFindingSeverity.P2 => 2,
            ManualValidationFindingSeverity.P3 => 3,
            ManualValidationFindingSeverity.Parked => 4,
            _ => 5
        };

    private static int RequirementOrder(ManualValidationLaneRequirement requirement) =>
        requirement switch
        {
            ManualValidationLaneRequirement.Required => 0,
            ManualValidationLaneRequirement.HardwareGated => 1,
            ManualValidationLaneRequirement.OptionalCompatibility => 2,
            ManualValidationLaneRequirement.Parked => 3,
            _ => 4
        };

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

public sealed class ManualValidationFindingsRequest
{
    public string RootPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
}

public sealed class ManualValidationFindingsResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public bool SummarySucceeded { get; set; }
    public string SummaryJsonPath { get; set; } = string.Empty;
    public string SummaryMarkdownPath { get; set; } = string.Empty;
    public string FindingsMarkdownPath { get; set; } = string.Empty;
    public string FindingsJsonPath { get; set; } = string.Empty;
    public string RedactionStatus { get; set; } = "clean";
    public int RequiredBlockingCount { get; set; }
    public int HardwareGatedOpenCount { get; set; }
    public int OptionalCompatibilityOpenCount { get; set; }
    public int ParkedCount { get; set; }
    public int RedactionFindingCount { get; set; }
    public List<ManualValidationFinding> Findings { get; set; } = new();
    public List<string> Issues { get; set; } = new();

    public static ManualValidationFindingsResult Failed(string message)
    {
        return new ManualValidationFindingsResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = [SensitiveTextDetector.Redact(message)]
        };
    }
}

public sealed class ManualValidationFinding
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public ManualValidationFindingSeverity Severity { get; set; }
    public ManualValidationLaneRequirement Requirement { get; set; }
    public string RequirementLabel { get; set; } = string.Empty;
    public ManualValidationLaneStatus Status { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool BlocksLocalV1Handoff { get; set; }
    public int EvidenceCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string ClaimBoundary { get; set; } = string.Empty;
}

public enum ManualValidationFindingSeverity
{
    P0,
    P1,
    P2,
    P3,
    Parked
}

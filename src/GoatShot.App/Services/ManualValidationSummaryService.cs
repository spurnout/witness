using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ManualValidationSummaryService
{
    public const string SummaryJsonFileName = "manual-validation-summary.json";
    public const string SummaryMarkdownFileName = "manual-validation-summary.md";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".txt",
        ".json",
        ".jsonl",
        ".log",
        ".csv",
        ".ps1",
        ".xml",
        ".html",
        ".htm",
        ".yaml",
        ".yml"
    };

    public async Task<ManualValidationSummaryResult> SummarizeAsync(
        ManualValidationSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationSummaryResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationSummaryResult.Failed($"Manual validation folder was not found: {root}");
        }

        var result = new ManualValidationSummaryResult
        {
            RootPath = root,
            GeneratedAt = DateTimeOffset.Now,
            SummaryJsonPath = Path.Combine(root, SummaryJsonFileName),
            SummaryMarkdownPath = Path.Combine(root, SummaryMarkdownFileName),
            Diagnostics = BuildDiagnosticsSummary(root)
        };

        foreach (var lane in ManualValidationHarnessService.ExpectedLanes)
        {
            result.Lanes.Add(await SummarizeLaneAsync(root, lane, cancellationToken));
        }

        result.Redaction = await ScanRedactionAsync(root, cancellationToken);
        AddValidationIssues(result);
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.Succeeded
            ? $"Manual validation folder is complete: {root}"
            : $"Manual validation folder is incomplete: {root}";

        await File.WriteAllTextAsync(
            result.SummaryJsonPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            result.SummaryMarkdownPath,
            BuildMarkdown(result),
            cancellationToken);

        return result;
    }

    private static async Task<ManualValidationLaneSummary> SummarizeLaneAsync(
        string root,
        ManualValidationLaneDefinition lane,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, lane.RelativePath);
        var summary = new ManualValidationLaneSummary
        {
            Id = lane.Id,
            Title = lane.Title,
            RelativePath = lane.RelativePath,
            Path = path,
            OAuthParked = lane.OAuthParked,
            Requirement = lane.Requirement,
            RequirementRationale = lane.RequirementRationale,
            BlocksLocalV1Handoff = lane.Requirement is ManualValidationLaneRequirement.Required,
            Status = ManualValidationLaneStatus.Missing,
            Exists = File.Exists(path)
        };

        if (!summary.Exists)
        {
            summary.Issues.Add("Expected lane template is missing.");
            return summary;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        summary.Status = ParseStatus(text, summary.Issues);
        summary.HasRequiredNote = summary.Status is ManualValidationLaneStatus.Failed or ManualValidationLaneStatus.Blocked
            ? HasMeaningfulNotes(text)
            : null;
        if (summary.HasRequiredNote == false)
        {
            summary.Issues.Add("Failed or blocked lane requires a short operator note.");
        }

        summary.EvidenceCount = CountEvidenceHints(text);
        return summary;
    }

    private static ManualValidationLaneStatus ParseStatus(string text, List<string> issues)
    {
        var checkedStatuses = new List<ManualValidationLaneStatus>();
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var label = line[5..].Trim();
            if (label.Contains("Passed", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("Pass", StringComparison.OrdinalIgnoreCase))
            {
                checkedStatuses.Add(ManualValidationLaneStatus.Passed);
            }
            else if (label.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                     label.Equals("Fail", StringComparison.OrdinalIgnoreCase))
            {
                checkedStatuses.Add(ManualValidationLaneStatus.Failed);
            }
            else if (label.Contains("Blocked", StringComparison.OrdinalIgnoreCase))
            {
                checkedStatuses.Add(ManualValidationLaneStatus.Blocked);
            }
            else if (label.Contains("Not applicable", StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("N/A", StringComparison.OrdinalIgnoreCase))
            {
                checkedStatuses.Add(ManualValidationLaneStatus.NotApplicable);
            }
            else if (label.Contains("Pending", StringComparison.OrdinalIgnoreCase) ||
                     label.Contains("Not run", StringComparison.OrdinalIgnoreCase))
            {
                checkedStatuses.Add(ManualValidationLaneStatus.NotRun);
            }
        }

        checkedStatuses = checkedStatuses.Distinct().ToList();
        if (checkedStatuses.Count > 1)
        {
            issues.Add($"Lane has multiple checked result statuses: {string.Join(", ", checkedStatuses)}.");
            return checkedStatuses.Contains(ManualValidationLaneStatus.Failed)
                ? ManualValidationLaneStatus.Failed
                : checkedStatuses.Contains(ManualValidationLaneStatus.Blocked)
                    ? ManualValidationLaneStatus.Blocked
                    : ManualValidationLaneStatus.NotRun;
        }

        return checkedStatuses.SingleOrDefault(ManualValidationLaneStatus.NotRun);
    }

    private static bool HasMeaningfulNotes(string text)
    {
        var inNotes = false;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inNotes = line.Equals("## Notes", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inNotes ||
                string.IsNullOrWhiteSpace(line) ||
                line.Equals("- Observed behavior:", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("- Defects or follow-ups:", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("- Public claim boundary:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static int CountEvidenceHints(string text)
    {
        var count = 0;
        var inEvidence = false;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inEvidence = line.Equals("## Evidence", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvidence ||
                string.IsNullOrWhiteSpace(line) ||
                line is "- Screenshots:" or "- Recordings:" or "- Command output:" or "- Related files:")
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static ManualValidationDiagnosticsSummary BuildDiagnosticsSummary(string root)
    {
        var diagnosticsRoot = Path.Combine(root, "diagnostics");
        string? bundle = null;
        if (Directory.Exists(diagnosticsRoot))
        {
            var currentBundle = Path.Combine(diagnosticsRoot, ManualValidationBaselineService.DiagnosticsBundleFileName);
            var legacyBundle = Path.Combine(diagnosticsRoot, ManualValidationBaselineService.LegacyDiagnosticsBundleFileName);
            bundle = File.Exists(currentBundle)
                ? currentBundle
                : File.Exists(legacyBundle)
                    ? legacyBundle
                    : Directory.EnumerateFiles(diagnosticsRoot, "*.zip", SearchOption.TopDirectoryOnly)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
        }
        var files = Directory.Exists(diagnosticsRoot)
            ? Directory.EnumerateFiles(diagnosticsRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList()
            : new List<string>();

        return new ManualValidationDiagnosticsSummary
        {
            DiagnosticsDirectory = diagnosticsRoot,
            DiagnosticsDirectoryExists = Directory.Exists(diagnosticsRoot),
            DiagnosticsBundlePath = bundle ?? string.Empty,
            DiagnosticsBundleExists = !string.IsNullOrWhiteSpace(bundle) && File.Exists(bundle),
            FileCount = files.Count,
            Files = files
        };
    }

    private static async Task<ManualValidationRedactionSummary> ScanRedactionAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var summary = new ManualValidationRedactionSummary();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(IsTextEvidenceFile)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 1_000_000)
            {
                summary.SkippedLargeFiles.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                continue;
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken);
            var findings = SensitiveTextDetector.Find(text)
                .Where(finding => !IsLocalNoise(finding))
                .ToList();
            if (findings.Count == 0)
            {
                continue;
            }

            summary.Findings.Add(new ManualValidationRedactionFinding
            {
                RelativePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Count = findings.Count,
                Summary = SensitiveTextDetector.Summarize(findings),
                Kinds = findings.Select(finding => finding.Kind)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        summary.Status = summary.Findings.Count == 0
            ? "clean"
            : "warning";
        return summary;
    }

    private static bool IsTextEvidenceFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals(SummaryJsonFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(SummaryMarkdownFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TextExtensions.Contains(Path.GetExtension(path));
    }

    private static bool IsLocalNoise(SensitiveFinding finding) =>
        finding.Kind.Equals("IP address", StringComparison.OrdinalIgnoreCase) &&
        (finding.Value.StartsWith("127.", StringComparison.Ordinal) ||
         finding.Value.Equals("0.0.0.0", StringComparison.Ordinal));

    private static void AddValidationIssues(ManualValidationSummaryResult result)
    {
        if (!result.Diagnostics.DiagnosticsDirectoryExists)
        {
            result.Warnings.Add("Diagnostics directory is missing.");
        }

        if (!result.Diagnostics.DiagnosticsBundleExists)
        {
            result.Warnings.Add("Diagnostics bundle is missing; create one when manual evidence is ready to hand off.");
        }

        foreach (var lane in result.Lanes)
        {
            foreach (var issue in lane.Issues)
            {
                result.Issues.Add($"{lane.Title}: {issue}");
            }

            if (lane.Status is ManualValidationLaneStatus.Missing)
            {
                continue;
            }

            if (lane.Status is not ManualValidationLaneStatus.NotRun)
            {
                continue;
            }

            if (lane.Requirement is ManualValidationLaneRequirement.Parked)
            {
                result.Warnings.Add($"{lane.Title}: not required while this lane is parked. {lane.RequirementRationale}");
                continue;
            }

            if (lane.Requirement is ManualValidationLaneRequirement.HardwareGated)
            {
                result.Warnings.Add($"{lane.Title}: not run because it is hardware-gated. {lane.RequirementRationale}");
                continue;
            }

            if (lane.Requirement is ManualValidationLaneRequirement.OptionalCompatibility)
            {
                result.Warnings.Add($"{lane.Title}: optional compatibility proof is not run. {lane.RequirementRationale}");
                continue;
            }

            result.Issues.Add($"{lane.Title}: required result is not run.");
        }

        foreach (var finding in result.Redaction.Findings)
        {
            result.Issues.Add($"Potential sensitive data in {finding.RelativePath}: {finding.Summary}");
        }
    }

    private static string BuildMarkdown(ManualValidationSummaryResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Manual Validation Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {result.GeneratedAt:O}");
        builder.AppendLine($"Root: `{result.RootPath}`");
        builder.AppendLine($"Status: `{(result.Succeeded ? "complete" : "incomplete")}`");
        builder.AppendLine();
        builder.AppendLine("## Lanes");
        builder.AppendLine();
        builder.AppendLine("| Lane | Requirement | Blocks Local V1 Handoff | Status | Notes Required | Evidence Count | File |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var lane in result.Lanes)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {lane.Title} | {FormatRequirement(lane.Requirement)} | {(lane.BlocksLocalV1Handoff ? "yes" : "no")} | `{lane.Status}` | {FormatNullable(lane.HasRequiredNote)} | {lane.EvidenceCount} | `{lane.RelativePath}` |");
        }

        builder.AppendLine();
        builder.AppendLine("## Diagnostics");
        builder.AppendLine();
        builder.AppendLine($"- Folder exists: `{result.Diagnostics.DiagnosticsDirectoryExists}`");
        builder.AppendLine($"- Bundle exists: `{result.Diagnostics.DiagnosticsBundleExists}`");
        builder.AppendLine($"- Bundle path: `{Empty(result.Diagnostics.DiagnosticsBundlePath)}`");
        builder.AppendLine($"- File count: `{result.Diagnostics.FileCount}`");
        builder.AppendLine();
        builder.AppendLine("## Redaction");
        builder.AppendLine();
        builder.AppendLine($"- Status: `{result.Redaction.Status}`");
        if (result.Redaction.Findings.Count == 0)
        {
            builder.AppendLine("- Findings: none.");
        }
        else
        {
            foreach (var finding in result.Redaction.Findings)
            {
                builder.AppendLine($"- `{finding.RelativePath}`: {finding.Summary}");
            }
        }

        AppendList(builder, "Issues", result.Issues);
        AppendList(builder, "Warnings", result.Warnings);
        return builder.ToString();
    }

    private static string FormatNullable(bool? value) =>
        value is null ? "n/a" : value.Value ? "yes" : "no";

    private static string FormatRequirement(ManualValidationLaneRequirement requirement) =>
        requirement switch
        {
            ManualValidationLaneRequirement.Required => "Required",
            ManualValidationLaneRequirement.HardwareGated => "Hardware-gated",
            ManualValidationLaneRequirement.OptionalCompatibility => "Optional compatibility",
            ManualValidationLaneRequirement.Parked => "Parked",
            _ => requirement.ToString()
        };

    private static string Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

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
}

public sealed class ManualValidationSummaryRequest
{
    public string RootPath { get; set; } = string.Empty;
}

public sealed class ManualValidationSummaryResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string SummaryJsonPath { get; set; } = string.Empty;
    public string SummaryMarkdownPath { get; set; } = string.Empty;
    public List<ManualValidationLaneSummary> Lanes { get; set; } = new();
    public ManualValidationDiagnosticsSummary Diagnostics { get; set; } = new();
    public ManualValidationRedactionSummary Redaction { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ManualValidationSummaryResult Failed(string message)
    {
        return new ManualValidationSummaryResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = new List<string> { SensitiveTextDetector.Redact(message) }
        };
    }
}

public sealed class ManualValidationLaneSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool OAuthParked { get; set; }
    public ManualValidationLaneRequirement Requirement { get; set; }
    public string RequirementRationale { get; set; } = string.Empty;
    public bool BlocksLocalV1Handoff { get; set; }
    public bool Exists { get; set; }
    public ManualValidationLaneStatus Status { get; set; }
    public bool? HasRequiredNote { get; set; }
    public int EvidenceCount { get; set; }
    public List<string> Issues { get; set; } = new();
}

public enum ManualValidationLaneStatus
{
    Missing,
    NotRun,
    Passed,
    Failed,
    Blocked,
    NotApplicable
}

public sealed class ManualValidationDiagnosticsSummary
{
    public string DiagnosticsDirectory { get; set; } = string.Empty;
    public bool DiagnosticsDirectoryExists { get; set; }
    public string DiagnosticsBundlePath { get; set; } = string.Empty;
    public bool DiagnosticsBundleExists { get; set; }
    public int FileCount { get; set; }
    public List<string> Files { get; set; } = new();
}

public sealed class ManualValidationRedactionSummary
{
    public string Status { get; set; } = "clean";
    public List<ManualValidationRedactionFinding> Findings { get; set; } = new();
    public List<string> SkippedLargeFiles { get; set; } = new();
}

public sealed class ManualValidationRedactionFinding
{
    public string RelativePath { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Kinds { get; set; } = new();
}

using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class ManualValidationDesktopEvidenceRecordService
{
    public const string DefaultDirectoryName = "required-desktop-evidence";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<ManualValidationDesktopEvidenceLaneDefinition> LaneDefinitions =
    [
        new()
        {
            Id = "keyboard-traversal",
            Title = "Keyboard Traversal",
            RequiredCategories = ["notes", "surface-coverage", "focus-order", "result", "privacy"],
            RecommendedCategories = ["focus-visual", "failure-media"],
            PassedRequirement = "Passed keyboard traversal evidence requires notes, surface coverage, focus-order, result, and privacy-review evidence."
        },
        new()
        {
            Id = "screen-reader",
            Title = "Screen Reader Narrator NVDA",
            RequiredCategories = ["notes", "surface-coverage", "screen-reader", "status-output", "privacy"],
            RecommendedCategories = ["narrator", "nvda", "control-names"],
            PassedRequirement = "Passed screen-reader evidence requires notes, surface coverage, screen-reader observation, status/output, and privacy-review evidence."
        },
        new()
        {
            Id = "text-scaling",
            Title = "Text Scaling",
            RequiredCategories = ["notes", "scale-125", "scale-150", "layout-review", "restore", "privacy"],
            RecommendedCategories = ["scale-200"],
            PassedRequirement = "Passed text-scaling evidence requires notes, 125 percent, 150 percent, layout-review, restore, and privacy-review evidence."
        },
        new()
        {
            Id = "high-contrast",
            Title = "High Contrast",
            RequiredCategories = ["notes", "theme", "main", "settings", "focus-selected", "restore", "privacy"],
            RecommendedCategories = ["dialog"],
            PassedRequirement = "Passed high-contrast evidence requires notes, theme, main surface, settings surface, focus/selected state, restore, and privacy-review evidence."
        },
        new()
        {
            Id = "live-region-drag",
            Title = "Live Region Drag Path",
            RequiredCategories = ["notes", "safe-content", "start", "snap", "complete", "privacy"],
            RecommendedCategories = ["cancel", "shift-bypass", "pixel-lens"],
            PassedRequirement = "Passed live-region-drag evidence requires notes, safe-content, start, snap, complete, and privacy-review evidence."
        }
    ];

    public async Task<ManualValidationDesktopEvidenceRecordResult> RecordAsync(
        ManualValidationDesktopEvidenceRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(request.RootPath)
            ? string.Empty
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine(
                string.IsNullOrWhiteSpace(root) ? Path.Combine("artifacts", "manual-validation") : root,
                DefaultDirectoryName))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var lane = ResolveLane(request.Lane);
        var result = new ManualValidationDesktopEvidenceRecordResult
        {
            RootPath = root,
            OutputPath = outputRoot,
            LaneId = lane?.Id ?? NormalizeLaneId(request.Lane),
            LaneTitle = lane?.Title ?? SensitiveTextDetector.Redact(request.Lane ?? string.Empty),
            Status = NormalizeStatus(request.Status),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.Now,
            WouldLaunchApp = false,
            WouldChangeWindowsSettings = false,
            WouldCaptureScreen = false,
            WouldRecordScreen = false,
            WouldUpdateManualLane = false,
            WouldCertifyAccessibility = false,
            WouldMutateUserProfile = false
        };

        if (string.IsNullOrWhiteSpace(root))
        {
            result.Issues.Add("Manual-validation folder is required.");
        }
        else if (!Directory.Exists(root))
        {
            result.Issues.Add($"Manual-validation folder was not found: {SensitiveTextDetector.Redact(root)}");
        }

        if (lane is null)
        {
            result.Issues.Add("Lane must be keyboard-traversal, screen-reader, text-scaling, high-contrast, or live-region-drag.");
        }
        else
        {
            result.RequiredCategories.AddRange(lane.RequiredCategories);
            result.RecommendedCategories.AddRange(lane.RecommendedCategories);
        }

        if (string.IsNullOrWhiteSpace(result.Status))
        {
            result.Issues.Add("Status must be passed, failed, blocked, or pending.");
        }

        if (result.Status is "failed" or "blocked" &&
            string.IsNullOrWhiteSpace(result.Note))
        {
            result.Issues.Add("Failed or blocked desktop evidence records require --note.");
        }

        result.Evidence.AddRange(NormalizeEvidence(root, outputRoot, request.Evidence));

        var presentCategories = result.Evidence
            .Where(evidence => result.RequiredCategories.Contains(evidence.Category, StringComparer.OrdinalIgnoreCase))
            .Select(evidence => evidence.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.MissingRequiredCategories.AddRange(result.RequiredCategories
            .Where(category => !presentCategories.Contains(category)));

        var presentRecommendedCategories = result.Evidence
            .Where(evidence => result.RecommendedCategories.Contains(evidence.Category, StringComparer.OrdinalIgnoreCase))
            .Select(evidence => evidence.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.MissingRecommendedCategories.AddRange(result.RecommendedCategories
            .Where(category => !presentRecommendedCategories.Contains(category)));

        if (result.Status == "passed" && result.MissingRequiredCategories.Count > 0)
        {
            result.Issues.Add(lane?.PassedRequirement ?? "Passed desktop evidence is missing required evidence categories.");
        }

        if (result.MissingRecommendedCategories.Count > 0)
        {
            result.Warnings.Add("Recommended desktop evidence is not attached; keep the record scoped to reviewed evidence only.");
        }

        result.ProofComplete = result.Status == "passed" &&
            result.Issues.Count == 0 &&
            result.MissingRequiredCategories.Count == 0;
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.ProofComplete
            ? $"{result.LaneTitle} evidence recorded as passed. The recorder did not launch Receipts, change Windows settings, capture or record the screen, update the manual lane, or certify accessibility."
            : result.Succeeded
                ? $"{result.LaneTitle} evidence recorded as {result.Status}. The recorder did not launch Receipts, change Windows settings, capture or record the screen, update the manual lane, or certify accessibility."
                : $"{EmptyIfMissing(result.LaneTitle)} evidence record has blockers. The recorder did not launch Receipts, change Windows settings, capture or record the screen, update the manual lane, or certify accessibility.";

        Directory.CreateDirectory(outputRoot);
        var slug = string.IsNullOrWhiteSpace(result.LaneId) ? "desktop" : result.LaneId;
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-desktop-evidence.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-desktop-evidence.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static List<ManualValidationDesktopEvidenceItem> NormalizeEvidence(
        string manualValidationRoot,
        string outputRoot,
        IReadOnlyList<ManualValidationDesktopEvidenceInput> evidence)
    {
        var normalized = new List<ManualValidationDesktopEvidenceItem>();
        foreach (var item in evidence)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                continue;
            }

            var category = NormalizeCategory(item.Category);
            var value = Environment.ExpandEnvironmentVariables(item.Value.Trim());
            var display = SensitiveTextDetector.Redact(value);
            var warning = string.Empty;
            var exists = false;
            var insideManualValidationRoot = false;
            var insideOutputRoot = false;

            try
            {
                var candidatePath = ResolveEvidencePath(manualValidationRoot, outputRoot, value);
                exists = File.Exists(candidatePath) || Directory.Exists(candidatePath);
                insideManualValidationRoot = !string.IsNullOrWhiteSpace(manualValidationRoot) &&
                    IsInsideRoot(manualValidationRoot, candidatePath);
                insideOutputRoot = IsInsideRoot(outputRoot, candidatePath);

                if (insideManualValidationRoot)
                {
                    display = Path.GetRelativePath(manualValidationRoot, candidatePath).Replace('\\', '/');
                }
                else if (insideOutputRoot)
                {
                    display = Path.GetRelativePath(outputRoot, candidatePath).Replace('\\', '/');
                }
                else if (Path.IsPathRooted(value))
                {
                    var fileName = Path.GetFileName(value);
                    display = string.IsNullOrWhiteSpace(fileName)
                        ? "[external evidence path omitted]"
                        : $"[external evidence: {SensitiveTextDetector.Redact(fileName)}]";
                    warning = "External evidence path was reduced to a file name only.";
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                display = SensitiveTextDetector.Redact(value);
                warning = "Evidence value could not be normalized as a path.";
            }

            var findings = SensitiveTextDetector.Find(value);
            if (findings.Count > 0)
            {
                var redactionWarning = $"Evidence value was redacted: {SensitiveTextDetector.Summarize(findings)}";
                warning = string.IsNullOrWhiteSpace(warning)
                    ? redactionWarning
                    : $"{warning} {redactionWarning}";
            }

            normalized.Add(new ManualValidationDesktopEvidenceItem
            {
                Category = category,
                Value = display,
                Exists = exists,
                InsideManualValidationRoot = insideManualValidationRoot,
                InsideOutputRoot = insideOutputRoot,
                Warning = warning
            });
        }

        return normalized;
    }

    private static string ResolveEvidencePath(string manualValidationRoot, string outputRoot, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        if (!string.IsNullOrWhiteSpace(manualValidationRoot))
        {
            var manualCandidate = Path.GetFullPath(Path.Combine(manualValidationRoot, value));
            if (File.Exists(manualCandidate) || Directory.Exists(manualCandidate))
            {
                return manualCandidate;
            }
        }

        return Path.GetFullPath(Path.Combine(outputRoot, value));
    }

    private static string BuildMarkdown(ManualValidationDesktopEvidenceRecordResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Required Desktop Evidence Record");
        builder.AppendLine();
        builder.AppendLine($"Lane: `{EmptyIfMissing(result.LaneTitle)}`");
        builder.AppendLine($"Lane id: `{EmptyIfMissing(result.LaneId)}`");
        builder.AppendLine($"Status: `{result.Status}`");
        builder.AppendLine($"Proof complete: `{result.ProofComplete}`");
        builder.AppendLine($"Observed at: `{result.ObservedAt:O}`");
        builder.AppendLine($"Manual-validation folder: `{EmptyIfMissing(result.RootPath)}`");
        builder.AppendLine($"Record folder: `{result.OutputPath}`");
        if (!string.IsNullOrWhiteSpace(result.OperatorName))
        {
            builder.AppendLine($"Operator: `{result.OperatorName}`");
        }

        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            builder.AppendLine($"Note: {result.Note}");
        }

        builder.AppendLine();
        builder.AppendLine("## Mutation Boundary");
        builder.AppendLine();
        builder.AppendLine($"- Would launch Receipts: `{result.WouldLaunchApp}`");
        builder.AppendLine($"- Would change Windows settings: `{result.WouldChangeWindowsSettings}`");
        builder.AppendLine($"- Would capture screen: `{result.WouldCaptureScreen}`");
        builder.AppendLine($"- Would record screen: `{result.WouldRecordScreen}`");
        builder.AppendLine($"- Would update manual lane: `{result.WouldUpdateManualLane}`");
        builder.AppendLine($"- Would certify accessibility: `{result.WouldCertifyAccessibility}`");
        builder.AppendLine($"- Would mutate user profile: `{result.WouldMutateUserProfile}`");

        AppendList(builder, "Issues", result.Issues);
        AppendList(builder, "Warnings", result.Warnings);
        AppendList(builder, "Missing Required Categories", result.MissingRequiredCategories);
        AppendList(builder, "Missing Recommended Categories", result.MissingRecommendedCategories);

        if (result.Evidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Evidence");
            foreach (var item in result.Evidence)
            {
                builder.AppendLine($"- `{item.Category}`: {item.Value} (exists: `{item.Exists}`, inside manual folder: `{item.InsideManualValidationRoot}`, inside record folder: `{item.InsideOutputRoot}`)");
                if (!string.IsNullOrWhiteSpace(item.Warning))
                {
                    builder.AppendLine($"  - Warning: {item.Warning}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine("- This record captures operator-reviewed evidence references only.");
        builder.AppendLine("- The recorder does not launch Receipts, change Windows settings, capture screenshots, record videos, certify accessibility, mutate user profiles, or update the manual-validation lane.");
        builder.AppendLine("- A `passed` status is only accepted when the lane-specific required evidence categories are present.");
        builder.AppendLine("- After reviewing this record, use `manual-validation record-lane` only if the operator-owned desktop pass was actually performed and accepted.");
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        foreach (var item in items)
        {
            builder.AppendLine($"- {item}");
        }
    }

    private static ManualValidationDesktopEvidenceLaneDefinition? ResolveLane(string? value)
    {
        var normalized = NormalizeLaneId(value);
        return LaneDefinitions.FirstOrDefault(lane =>
            lane.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLaneId(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        return normalized switch
        {
            "keyboard" or "keyboardtraversal" or "tabtraversal" or "focus" or "focusorder" => "keyboard-traversal",
            "screenreader" or "screen-reader-narrator-nvda" or "narrator" or "nvda" or "at" => "screen-reader",
            "textscaling" or "text-scale" or "scaling" or "dpi" => "text-scaling",
            "highcontrast" or "contrast" => "high-contrast",
            "liveregiondrag" or "regiondrag" or "drag" or "capturedrag" => "live-region-drag",
            _ => normalized
        };
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized switch
        {
            "pass" or "passed" or "success" => "passed",
            "fail" or "failed" or "failure" => "failed",
            "block" or "blocked" => "blocked",
            "pending" or "notrun" or "unrun" => "pending",
            _ => string.Empty
        };
    }

    private static string NormalizeCategory(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized switch
        {
            "notes" or "note" or "observations" or "observation" or "operatornotes" or "template" => "notes",
            "surfacecoverage" or "surfaces" or "coverage" or "flowcoverage" => "surface-coverage",
            "focusorder" or "taborder" or "keyboardorder" => "focus-order",
            "result" or "outcome" or "summary" => "result",
            "privacy" or "redaction" or "review" or "safeevidence" or "safecontentreview" => "privacy",
            "focusvisual" or "focusring" or "focusoutline" => "focus-visual",
            "failuremedia" or "failureclip" or "failurescreenshot" => "failure-media",
            "screenreader" or "assistivetech" or "atobservation" => "screen-reader",
            "statusoutput" or "liveoutput" or "announcements" or "status" => "status-output",
            "narrator" => "narrator",
            "nvda" => "nvda",
            "controlnames" or "names" or "automationnames" => "control-names",
            "scale125" or "textscale125" or "125" => "scale-125",
            "scale150" or "textscale150" or "150" => "scale-150",
            "scale200" or "textscale200" or "200" => "scale-200",
            "layoutreview" or "layout" or "overlap" or "clipping" => "layout-review",
            "restore" or "restored" or "reset" => "restore",
            "theme" or "contrasttheme" => "theme",
            "main" or "mainwindow" => "main",
            "settings" or "settingswindow" => "settings",
            "focusselected" or "selectedstate" or "selection" => "focus-selected",
            "dialog" or "modal" => "dialog",
            "safecontent" or "stagedcontent" => "safe-content",
            "start" or "dragstart" => "start",
            "snap" or "snapping" or "edgesnap" => "snap",
            "complete" or "completed" or "finish" or "regioncomplete" => "complete",
            "cancel" or "cancelled" => "cancel",
            "shiftbypass" or "bypass" => "shift-bypass",
            "pixellens" or "lens" => "pixel-lens",
            _ => "other"
        };
    }

    private static bool IsInsideRoot(string root, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> WriteFileAsync(
        string root,
        string fileName,
        string contents,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, fileName);
        await File.WriteAllTextAsync(path, contents, cancellationToken);
        return path;
    }

    private static string EmptyIfMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;
}

public sealed class ManualValidationDesktopEvidenceRecordRequest
{
    public string? RootPath { get; set; }
    public string? Lane { get; set; }
    public string? Status { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<ManualValidationDesktopEvidenceInput> Evidence { get; set; } = new();
}

public sealed class ManualValidationDesktopEvidenceInput
{
    public string Category { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
}

public sealed class ManualValidationDesktopEvidenceRecordResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string LaneId { get; set; } = string.Empty;
    public string LaneTitle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ProofComplete { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool WouldLaunchApp { get; set; }
    public bool WouldChangeWindowsSettings { get; set; }
    public bool WouldCaptureScreen { get; set; }
    public bool WouldRecordScreen { get; set; }
    public bool WouldUpdateManualLane { get; set; }
    public bool WouldCertifyAccessibility { get; set; }
    public bool WouldMutateUserProfile { get; set; }
    public List<string> RequiredCategories { get; set; } = new();
    public List<string> RecommendedCategories { get; set; } = new();
    public List<string> MissingRequiredCategories { get; set; } = new();
    public List<string> MissingRecommendedCategories { get; set; } = new();
    public List<ManualValidationDesktopEvidenceItem> Evidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class ManualValidationDesktopEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideManualValidationRoot { get; set; }
    public bool InsideOutputRoot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public sealed class ManualValidationDesktopEvidenceLaneDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<string> RequiredCategories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecommendedCategories { get; set; } = Array.Empty<string>();
    public string PassedRequirement { get; set; } = string.Empty;
}

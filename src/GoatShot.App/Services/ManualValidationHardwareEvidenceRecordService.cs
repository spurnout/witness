using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class ManualValidationHardwareEvidenceRecordService
{
    public const string DefaultDirectoryName = "hardware-evidence";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<ManualValidationHardwareEvidenceLaneDefinition> LaneDefinitions =
    [
        new()
        {
            Id = "multi-monitor-capture",
            Title = "Multi Monitor Capture",
            RequiredCategories = ["notes", "safe-content", "topology", "capture-output", "dimensions", "privacy"],
            RecommendedCategories = ["wgc-diagnostics", "failure-media"],
            PassedRequirement = "Passed multi-monitor capture evidence requires notes, safe-content, display topology, capture output, dimensions/content review, and privacy-review evidence."
        },
        new()
        {
            Id = "multi-monitor-recording",
            Title = "Multi Monitor Recording",
            RequiredCategories = ["notes", "safe-content", "topology", "recording-output", "playback", "privacy"],
            RecommendedCategories = ["audio-sync", "encoder-diagnostics"],
            PassedRequirement = "Passed multi-monitor recording evidence requires notes, safe-content, display topology, recording output, playback review, and privacy-review evidence."
        },
        new()
        {
            Id = "long-recording",
            Title = "Long Recording Stability",
            RequiredCategories = ["notes", "safe-content", "duration", "playback", "sync", "recovery", "privacy"],
            RecommendedCategories = ["devices", "ffprobe"],
            PassedRequirement = "Passed long-recording evidence requires notes, safe-content, duration, playback, sync, recovery, and privacy-review evidence."
        },
        new()
        {
            Id = "android-safe-device-proof",
            Title = "Android Safe Device Proof",
            RequiredCategories = ["notes", "safe-content", "device", "screenshot-or-video", "import-result", "cleanup", "privacy"],
            RecommendedCategories = ["preview", "adb-diagnostics"],
            PassedRequirement = "Passed Android safe-device evidence requires notes, safe-content, device, screenshot-or-video, import result, cleanup, and privacy-review evidence."
        }
    ];

    public async Task<ManualValidationHardwareEvidenceRecordResult> RecordAsync(
        ManualValidationHardwareEvidenceRecordRequest request,
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
        var result = new ManualValidationHardwareEvidenceRecordResult
        {
            RootPath = root,
            OutputPath = outputRoot,
            LaneId = lane?.Id ?? NormalizeLaneId(request.Lane),
            LaneTitle = lane?.Title ?? SensitiveTextDetector.Redact(request.Lane ?? string.Empty),
            Status = NormalizeStatus(request.Status),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.Now,
            WouldCaptureDesktop = false,
            WouldRecordDesktop = false,
            WouldContactAndroidDevice = false,
            WouldImportPhoneMedia = false,
            WouldChangeDeviceSettings = false,
            WouldUpdateManualLane = false,
            WouldCertifyHardware = false,
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
            result.Issues.Add("Lane must be multi-monitor-capture, multi-monitor-recording, long-recording, or android-safe-device-proof.");
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
            result.Issues.Add("Failed or blocked hardware evidence records require --note.");
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
            result.Issues.Add(lane?.PassedRequirement ?? "Passed hardware evidence is missing required evidence categories.");
        }

        if (result.MissingRecommendedCategories.Count > 0)
        {
            result.Warnings.Add("Recommended hardware/device evidence is not attached; keep the record scoped to reviewed safe evidence only.");
        }

        result.ProofComplete = result.Status == "passed" &&
            result.Issues.Count == 0 &&
            result.MissingRequiredCategories.Count == 0;
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.ProofComplete
            ? $"{result.LaneTitle} evidence recorded as passed. The recorder did not capture or record the desktop, contact Android devices, import phone media, change device settings, update the manual lane, or certify hardware."
            : result.Succeeded
                ? $"{result.LaneTitle} evidence recorded as {result.Status}. The recorder did not capture or record the desktop, contact Android devices, import phone media, change device settings, update the manual lane, or certify hardware."
                : $"{EmptyIfMissing(result.LaneTitle)} evidence record has blockers. The recorder did not capture or record the desktop, contact Android devices, import phone media, change device settings, update the manual lane, or certify hardware.";

        Directory.CreateDirectory(outputRoot);
        var slug = string.IsNullOrWhiteSpace(result.LaneId) ? "hardware" : result.LaneId;
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-hardware-evidence.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-hardware-evidence.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static List<ManualValidationHardwareEvidenceItem> NormalizeEvidence(
        string manualValidationRoot,
        string outputRoot,
        IReadOnlyList<ManualValidationHardwareEvidenceInput> evidence)
    {
        var normalized = new List<ManualValidationHardwareEvidenceItem>();
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

            normalized.Add(new ManualValidationHardwareEvidenceItem
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

    private static string BuildMarkdown(ManualValidationHardwareEvidenceRecordResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Hardware Evidence Record");
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
        builder.AppendLine($"- Would capture desktop: `{result.WouldCaptureDesktop}`");
        builder.AppendLine($"- Would record desktop: `{result.WouldRecordDesktop}`");
        builder.AppendLine($"- Would contact Android device: `{result.WouldContactAndroidDevice}`");
        builder.AppendLine($"- Would import phone media: `{result.WouldImportPhoneMedia}`");
        builder.AppendLine($"- Would change device settings: `{result.WouldChangeDeviceSettings}`");
        builder.AppendLine($"- Would update manual lane: `{result.WouldUpdateManualLane}`");
        builder.AppendLine($"- Would certify hardware: `{result.WouldCertifyHardware}`");
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
        builder.AppendLine("- This record captures operator-reviewed hardware/device evidence references only.");
        builder.AppendLine("- The recorder does not capture or record the desktop, contact Android devices, import phone media, change device settings, certify hardware, mutate user profiles, or update the manual-validation lane.");
        builder.AppendLine("- A `passed` status is only accepted when the lane-specific required evidence categories are present.");
        builder.AppendLine("- After reviewing this record, use `manual-validation record-lane` only if the operator-owned hardware/device pass was actually performed and accepted.");
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

    private static ManualValidationHardwareEvidenceLaneDefinition? ResolveLane(string? value)
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
            "multimonitorcapture" or "multi-monitor" or "capture" or "all-monitor-capture" or "cross-monitor-capture" => "multi-monitor-capture",
            "multimonitorrecording" or "recording" or "all-monitor-recording" or "cross-monitor-recording" => "multi-monitor-recording",
            "longrecording" or "long-run" or "longrun" or "stability" => "long-recording",
            "android" or "androidsafe" or "android-device" or "android-safe-device" or "androidsafedeviceproof" => "android-safe-device-proof",
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
            "safecontent" or "stagedcontent" or "contentreview" or "privacycontent" => "safe-content",
            "topology" or "displaytopology" or "displays" or "monitors" or "environment" => "topology",
            "captureoutput" or "capture" or "screenshot" or "image" => "capture-output",
            "dimensions" or "dimensionreview" or "contentdimensions" or "bounds" => "dimensions",
            "privacy" or "redaction" or "review" or "safereview" => "privacy",
            "wgcdiagnostics" or "captureengine" or "capturediagnostics" => "wgc-diagnostics",
            "failuremedia" or "failureclip" or "failurescreenshot" => "failure-media",
            "recordingoutput" or "recording" or "video" or "mp4" => "recording-output",
            "playback" or "playbackreview" or "mediareview" => "playback",
            "audiosync" or "avsync" => "audio-sync",
            "encoderdiagnostics" or "recordingdiagnostics" or "preflight" => "encoder-diagnostics",
            "duration" or "elapsed" or "runtime" => "duration",
            "sync" or "audiosynchronization" or "av" => "sync",
            "recovery" or "stoprecovery" or "resume" or "cancelrecovery" => "recovery",
            "devices" or "deviceinventory" or "recordingdevices" => "devices",
            "ffprobe" or "mediainfo" or "metadata" => "ffprobe",
            "device" or "androiddevice" or "phone" or "serial" => "device",
            "screenshotorvideo" or "screenrecord" or "screencap" or "androidmedia" => "screenshot-or-video",
            "importresult" or "workspaceimport" or "libraryimport" => "import-result",
            "cleanup" or "remotecleanup" or "temporaryfilecleanup" => "cleanup",
            "preview" or "androidpreview" or "contactsheet" => "preview",
            "adbdiagnostics" or "androiddiagnostics" or "adb" => "adb-diagnostics",
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

public sealed class ManualValidationHardwareEvidenceRecordRequest
{
    public string? RootPath { get; set; }
    public string? Lane { get; set; }
    public string? Status { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<ManualValidationHardwareEvidenceInput> Evidence { get; set; } = new();
}

public sealed class ManualValidationHardwareEvidenceInput
{
    public string Category { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
}

public sealed class ManualValidationHardwareEvidenceRecordResult
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
    public bool WouldCaptureDesktop { get; set; }
    public bool WouldRecordDesktop { get; set; }
    public bool WouldContactAndroidDevice { get; set; }
    public bool WouldImportPhoneMedia { get; set; }
    public bool WouldChangeDeviceSettings { get; set; }
    public bool WouldUpdateManualLane { get; set; }
    public bool WouldCertifyHardware { get; set; }
    public bool WouldMutateUserProfile { get; set; }
    public List<string> RequiredCategories { get; set; } = new();
    public List<string> RecommendedCategories { get; set; } = new();
    public List<string> MissingRequiredCategories { get; set; } = new();
    public List<string> MissingRecommendedCategories { get; set; } = new();
    public List<ManualValidationHardwareEvidenceItem> Evidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class ManualValidationHardwareEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideManualValidationRoot { get; set; }
    public bool InsideOutputRoot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public sealed class ManualValidationHardwareEvidenceLaneDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<string> RequiredCategories { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecommendedCategories { get; set; } = Array.Empty<string>();
    public string PassedRequirement { get; set; } = string.Empty;
}

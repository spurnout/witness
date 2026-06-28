using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class ManualValidationCleanMachineEvidenceRecordService
{
    public const string DefaultDirectoryName = "clean-machine-evidence";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<string> RequiredCategories =
    [
        "machine",
        "package",
        "hash",
        "diagnostics",
        "paths",
        "first-launch",
        "settings",
        "capture-export",
        "installer",
        "privacy"
    ];

    private static readonly IReadOnlyList<string> RecommendedCategories =
    [
        "script-result"
    ];

    public async Task<ManualValidationCleanMachineEvidenceRecordResult> RecordAsync(
        ManualValidationCleanMachineEvidenceRecordRequest request,
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

        var result = new ManualValidationCleanMachineEvidenceRecordResult
        {
            RootPath = root,
            OutputPath = outputRoot,
            Status = NormalizeStatus(request.Status),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.Now,
            WouldLaunchApp = false,
            WouldRunInstaller = false,
            WouldInstallOrUninstall = false,
            WouldMutateUserProfile = false,
            WouldCaptureScreen = false,
            WouldUpdateManualLane = false,
            WouldCertifyCleanMachine = false
        };

        if (string.IsNullOrWhiteSpace(root))
        {
            result.Issues.Add("Manual-validation folder is required.");
        }
        else if (!Directory.Exists(root))
        {
            result.Issues.Add($"Manual-validation folder was not found: {SensitiveTextDetector.Redact(root)}");
        }

        if (string.IsNullOrWhiteSpace(result.Status))
        {
            result.Issues.Add("Status must be passed, failed, blocked, or pending.");
        }

        if (result.Status is "failed" or "blocked" &&
            string.IsNullOrWhiteSpace(result.Note))
        {
            result.Issues.Add("Failed or blocked clean-machine evidence records require --note.");
        }

        result.Evidence.AddRange(NormalizeEvidence(root, outputRoot, request.Evidence));
        result.RequiredCategories.AddRange(RequiredCategories);
        result.RecommendedCategories.AddRange(RecommendedCategories);

        var presentCategories = result.Evidence
            .Where(evidence => RequiredCategories.Contains(evidence.Category, StringComparer.OrdinalIgnoreCase))
            .Select(evidence => evidence.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.MissingRequiredCategories.AddRange(RequiredCategories
            .Where(category => !presentCategories.Contains(category)));

        var presentRecommendedCategories = result.Evidence
            .Where(evidence => RecommendedCategories.Contains(evidence.Category, StringComparer.OrdinalIgnoreCase))
            .Select(evidence => evidence.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.MissingRecommendedCategories.AddRange(RecommendedCategories
            .Where(category => !presentRecommendedCategories.Contains(category)));

        if (result.Status == "passed" && result.MissingRequiredCategories.Count > 0)
        {
            result.Issues.Add("Passed clean-machine evidence requires machine, package, hash, diagnostics, paths, first-launch, settings, capture-export, installer, and privacy evidence.");
        }

        if (result.MissingRecommendedCategories.Count > 0)
        {
            result.Warnings.Add("Clean-machine helper script result evidence is not attached; keep command-backed proof separate from the reviewed human GUI evidence.");
        }

        result.ProofComplete = result.Status == "passed" &&
            result.Issues.Count == 0 &&
            result.MissingRequiredCategories.Count == 0;
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.ProofComplete
            ? "Clean-machine evidence recorded as passed. The recorder did not launch GoatShot, run installers, mutate profiles, capture the screen, or update the manual lane."
            : result.Succeeded
                ? $"Clean-machine evidence recorded as {result.Status}. The recorder did not launch GoatShot, run installers, mutate profiles, capture the screen, or update the manual lane."
                : "Clean-machine evidence record has blockers. The recorder did not launch GoatShot, run installers, mutate profiles, capture the screen, or update the manual lane.";

        Directory.CreateDirectory(outputRoot);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "clean-machine-evidence.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "clean-machine-evidence.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static List<ManualValidationCleanMachineEvidenceItem> NormalizeEvidence(
        string manualValidationRoot,
        string outputRoot,
        IReadOnlyList<ManualValidationCleanMachineEvidenceInput> evidence)
    {
        var normalized = new List<ManualValidationCleanMachineEvidenceItem>();
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

            normalized.Add(new ManualValidationCleanMachineEvidenceItem
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

    private static string BuildMarkdown(ManualValidationCleanMachineEvidenceRecordResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Clean-Machine Evidence Record");
        builder.AppendLine();
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
        builder.AppendLine($"- Would launch GoatShot: `{result.WouldLaunchApp}`");
        builder.AppendLine($"- Would run installer: `{result.WouldRunInstaller}`");
        builder.AppendLine($"- Would install or uninstall: `{result.WouldInstallOrUninstall}`");
        builder.AppendLine($"- Would mutate user profile: `{result.WouldMutateUserProfile}`");
        builder.AppendLine($"- Would capture screen: `{result.WouldCaptureScreen}`");
        builder.AppendLine($"- Would update manual lane: `{result.WouldUpdateManualLane}`");
        builder.AppendLine($"- Would certify clean machine: `{result.WouldCertifyCleanMachine}`");

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
        builder.AppendLine("- The recorder does not launch GoatShot, run the clean-machine helper, install or uninstall software, mutate user profiles, capture screenshots, certify a clean Windows environment, or update the manual-validation lane.");
        builder.AppendLine("- A `passed` status is only accepted when machine/profile, package, hash, diagnostics, paths, first-launch, settings, capture/export, installer, and privacy-review evidence are all present.");
        builder.AppendLine("- After reviewing this record, use `manual-validation record-lane --lane clean-machine-install` only if the operator-owned clean-machine pass was actually performed and accepted.");
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
            "machine" or "machineinfo" or "environment" or "windows" or "profile" or "vm" or "cleanprofile" => "machine",
            "package" or "portable" or "portablezip" or "zip" or "bundle" or "manifest" => "package",
            "hash" or "portablehash" or "sha256" or "checksum" => "hash",
            "diagnostics" or "diagnostic" or "diagnosticsprint" or "cli" => "diagnostics",
            "paths" or "isolatedpaths" or "approots" or "roots" or "path" => "paths",
            "firstlaunch" or "launch" or "mainwindow" or "mainwindowrender" or "render" or "wpf" => "first-launch",
            "settings" or "settingssave" or "settingreload" or "preferences" => "settings",
            "captureexport" or "captureloop" or "importexport" or "captureimporteditexport" or "editor" or "export" => "capture-export",
            "installer" or "installerskipped" or "installerbuild" or "installerlog" or "installuninstall" or "uninstall" => "installer",
            "privacy" or "redaction" or "review" or "safeevidence" or "safecontent" => "privacy",
            "scriptresult" or "helperresult" or "cleanmachinescriptresult" or "commandresult" => "script-result",
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

public sealed class ManualValidationCleanMachineEvidenceRecordRequest
{
    public string? RootPath { get; set; }
    public string? Status { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<ManualValidationCleanMachineEvidenceInput> Evidence { get; set; } = new();
}

public sealed class ManualValidationCleanMachineEvidenceInput
{
    public string Category { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
}

public sealed class ManualValidationCleanMachineEvidenceRecordResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ProofComplete { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool WouldLaunchApp { get; set; }
    public bool WouldRunInstaller { get; set; }
    public bool WouldInstallOrUninstall { get; set; }
    public bool WouldMutateUserProfile { get; set; }
    public bool WouldCaptureScreen { get; set; }
    public bool WouldUpdateManualLane { get; set; }
    public bool WouldCertifyCleanMachine { get; set; }
    public List<string> RequiredCategories { get; set; } = new();
    public List<string> RecommendedCategories { get; set; } = new();
    public List<string> MissingRequiredCategories { get; set; } = new();
    public List<string> MissingRecommendedCategories { get; set; } = new();
    public List<ManualValidationCleanMachineEvidenceItem> Evidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class ManualValidationCleanMachineEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideManualValidationRoot { get; set; }
    public bool InsideOutputRoot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

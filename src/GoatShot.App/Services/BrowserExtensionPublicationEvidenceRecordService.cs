using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionPublicationEvidenceRecordService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<string> RequiredCategories =
    [
        "package",
        "account",
        "submission",
        "review",
        "signing",
        "listing",
        "install"
    ];

    private static readonly IReadOnlyList<string> RecommendedCategories =
    [
        "live-browser"
    ];

    public async Task<BrowserExtensionPublicationEvidenceRecordResult> RecordAsync(
        BrowserExtensionPublicationEvidenceRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-publication-evidence"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var result = new BrowserExtensionPublicationEvidenceRecordResult
        {
            OutputPath = outputRoot,
            Target = NormalizeTarget(request.Target),
            Status = NormalizeStatus(request.Status),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.Now,
            WouldContactStoreAccount = false,
            WouldUploadPackage = false,
            WouldSubmitReview = false,
            WouldSignOrPublish = false,
            WouldInstallExtension = false,
            WouldMutateBrowserProfile = false,
            WouldRegisterNativeHost = false,
            WouldApplyEnterprisePolicy = false
        };

        if (string.IsNullOrWhiteSpace(result.Target))
        {
            result.Issues.Add("Target must be chrome, edge, or firefox.");
        }

        if (string.IsNullOrWhiteSpace(result.Status))
        {
            result.Issues.Add("Status must be passed, failed, blocked, or pending.");
        }

        if (result.Status is "failed" or "blocked" &&
            string.IsNullOrWhiteSpace(result.Note))
        {
            result.Issues.Add("Failed or blocked browser publication evidence records require --note.");
        }

        result.Evidence.AddRange(NormalizeEvidence(outputRoot, request.Evidence));
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
            result.Issues.Add("Passed browser extension publication evidence requires package, account, submission, review, signing, listing, and install evidence.");
        }

        if (result.MissingRecommendedCategories.Count > 0)
        {
            result.Warnings.Add("Live browser proof evidence is not attached; keep functional compatibility claims separate until that lane is recorded.");
        }

        result.ProofComplete = result.Status == "passed" &&
            result.Issues.Count == 0 &&
            result.MissingRequiredCategories.Count == 0;
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.ProofComplete
            ? $"Browser extension publication evidence recorded as passed for {result.Target}. No store account, package upload, signing, installation, or profile mutation was performed by the recorder."
            : result.Succeeded
                ? $"Browser extension publication evidence recorded as {result.Status} for {result.Target}. No browser store or profile was mutated by the recorder."
                : $"Browser extension publication evidence record has blockers for {EmptyIfMissing(result.Target)}. No browser store or profile was mutated by the recorder.";

        Directory.CreateDirectory(outputRoot);
        var slug = Slug(string.IsNullOrWhiteSpace(result.Target) ? "browser-extension" : result.Target);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-publication-evidence.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-publication-evidence.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static List<BrowserExtensionPublicationEvidenceItem> NormalizeEvidence(
        string outputRoot,
        IReadOnlyList<BrowserExtensionPublicationEvidenceInput> evidence)
    {
        var normalized = new List<BrowserExtensionPublicationEvidenceItem>();
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
            var insideOutputRoot = false;

            try
            {
                var candidatePath = Path.IsPathRooted(value)
                    ? Path.GetFullPath(value)
                    : Path.GetFullPath(Path.Combine(outputRoot, value));
                exists = File.Exists(candidatePath) || Directory.Exists(candidatePath);
                insideOutputRoot = IsInsideRoot(outputRoot, candidatePath);

                if (insideOutputRoot)
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

            normalized.Add(new BrowserExtensionPublicationEvidenceItem
            {
                Category = category,
                Value = display,
                Exists = exists,
                InsideOutputRoot = insideOutputRoot,
                Warning = warning
            });
        }

        return normalized;
    }

    private static string BuildMarkdown(BrowserExtensionPublicationEvidenceRecordResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Browser Extension Publication Evidence Record");
        builder.AppendLine();
        builder.AppendLine($"Target: `{EmptyIfMissing(result.Target)}`");
        builder.AppendLine($"Status: `{result.Status}`");
        builder.AppendLine($"Proof complete: `{result.ProofComplete}`");
        builder.AppendLine($"Observed at: `{result.ObservedAt:O}`");
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
        builder.AppendLine($"- Would contact store account: `{result.WouldContactStoreAccount}`");
        builder.AppendLine($"- Would upload package: `{result.WouldUploadPackage}`");
        builder.AppendLine($"- Would submit review: `{result.WouldSubmitReview}`");
        builder.AppendLine($"- Would sign or publish: `{result.WouldSignOrPublish}`");
        builder.AppendLine($"- Would install extension: `{result.WouldInstallExtension}`");
        builder.AppendLine($"- Would mutate browser profile: `{result.WouldMutateBrowserProfile}`");
        builder.AppendLine($"- Would register native host: `{result.WouldRegisterNativeHost}`");
        builder.AppendLine($"- Would apply enterprise policy: `{result.WouldApplyEnterprisePolicy}`");

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
                builder.AppendLine($"- `{item.Category}`: {item.Value} (exists: `{item.Exists}`, inside output: `{item.InsideOutputRoot}`)");
                if (!string.IsNullOrWhiteSpace(item.Warning))
                {
                    builder.AppendLine($"  - Warning: {item.Warning}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine("- This record captures operator-reviewed evidence references only.");
        builder.AppendLine("- The recorder does not contact browser-store accounts, upload packages, submit reviews, sign or publish listings, install extensions, register native hosts, apply enterprise policy, or mutate browser profiles.");
        builder.AppendLine("- A `passed` status is only accepted when package, account, submission, review, signing, listing, and install evidence are all present.");
        builder.AppendLine("- Live browser proof is recommended evidence, but it remains a separate functional compatibility lane.");
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

    private static string NormalizeTarget(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized switch
        {
            "chrome" or "googlechrome" or "chromium" => "chrome",
            "edge" or "microsoftedge" or "msedge" => "edge",
            "firefox" or "mozilla" or "amo" => "firefox",
            _ => string.Empty
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
            "package" or "zip" or "storepackage" or "submissionpackage" or "bundle" => "package",
            "account" or "developeraccount" or "storeaccount" or "accountdiagnostic" or "accountproof" => "account",
            "submission" or "submit" or "upload" or "uploadresult" or "validator" or "validation" => "submission",
            "review" or "reviewstatus" or "reviewresult" or "approval" or "rejection" => "review",
            "signing" or "signed" or "signature" or "availability" or "publish" or "publication" => "signing",
            "listing" or "metadata" or "listingmetadata" or "privacy" or "support" or "datause" => "listing",
            "install" or "installation" or "storeinstall" or "permanentinstall" => "install",
            "livebrowser" or "browserproof" or "functionalproof" or "hoststatus" or "safefixture" => "live-browser",
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

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "browser-extension" : slug;
    }
}

public sealed class BrowserExtensionPublicationEvidenceRecordRequest
{
    public string? Target { get; set; }
    public string? Status { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<BrowserExtensionPublicationEvidenceInput> Evidence { get; set; } = new();
}

public sealed class BrowserExtensionPublicationEvidenceInput
{
    public string Category { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
}

public sealed class BrowserExtensionPublicationEvidenceRecordResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ProofComplete { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool WouldContactStoreAccount { get; set; }
    public bool WouldUploadPackage { get; set; }
    public bool WouldSubmitReview { get; set; }
    public bool WouldSignOrPublish { get; set; }
    public bool WouldInstallExtension { get; set; }
    public bool WouldMutateBrowserProfile { get; set; }
    public bool WouldRegisterNativeHost { get; set; }
    public bool WouldApplyEnterprisePolicy { get; set; }
    public List<string> RequiredCategories { get; set; } = new();
    public List<string> RecommendedCategories { get; set; } = new();
    public List<string> MissingRequiredCategories { get; set; } = new();
    public List<string> MissingRecommendedCategories { get; set; } = new();
    public List<BrowserExtensionPublicationEvidenceItem> Evidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionPublicationEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideOutputRoot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

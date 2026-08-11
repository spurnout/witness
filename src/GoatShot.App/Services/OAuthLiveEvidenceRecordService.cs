using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class OAuthLiveEvidenceRecordService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyList<string> RequiredCategories =
    [
        "consent",
        "exchange",
        "refresh",
        "upload",
        "cleanup",
        "account"
    ];

    public async Task<OAuthLiveEvidenceRecordResult> RecordAsync(
        OAuthLiveEvidenceRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "oauth-live-evidence"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var result = new OAuthLiveEvidenceRecordResult
        {
            OutputPath = outputRoot,
            ProviderName = SensitiveTextDetector.Redact(request.ProviderName ?? string.Empty),
            Status = NormalizeStatus(request.Status),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            ObservedAt = request.ObservedAt ?? DateTimeOffset.Now,
            WouldOpenBrowser = false,
            WouldContactProvider = false,
            WouldExchangeCode = false,
            WouldStoreToken = false,
            WouldRefreshToken = false,
            WouldUploadFile = false,
            WouldDeleteRemoteFile = false
        };

        if (string.IsNullOrWhiteSpace(request.ProviderName))
        {
            result.Issues.Add("OAuth provider is required.");
        }

        if (string.IsNullOrWhiteSpace(result.Status))
        {
            result.Issues.Add("Status must be passed, failed, blocked, or pending.");
        }

        var provider = request.Providers.FirstOrDefault(candidate =>
            candidate.ProviderName.Equals(request.ProviderName, StringComparison.OrdinalIgnoreCase));
        if (provider is null && !string.IsNullOrWhiteSpace(request.ProviderName))
        {
            result.Issues.Add($"OAuth provider was not found: {SensitiveTextDetector.Redact(request.ProviderName)}");
        }
        else if (provider is not null)
        {
            result.ProviderName = SensitiveTextDetector.Redact(provider.ProviderName);
            result.ProviderKind = BuildProviderKind(provider.ProviderName);
            result.ClientIdConfigured = !string.IsNullOrWhiteSpace(provider.ClientId);
            result.AuthorizationEndpointConfigured = !string.IsNullOrWhiteSpace(provider.AuthorizationEndpoint);
            result.TokenEndpointConfigured = !string.IsNullOrWhiteSpace(provider.TokenEndpoint);
            result.Scopes = SensitiveTextDetector.Redact(provider.Scopes ?? string.Empty);
            result.UsesPkce = provider.UsePkce;
            result.RefreshTokenMarkerAlreadyStored = provider.RefreshTokenStored;
        }

        if (result.Status is "failed" or "blocked" &&
            string.IsNullOrWhiteSpace(result.Note))
        {
            result.Issues.Add("Failed or blocked OAuth live evidence records require --note.");
        }

        result.Evidence.AddRange(NormalizeEvidence(outputRoot, request.Evidence));
        result.RequiredCategories.AddRange(RequiredCategories);
        var presentCategories = result.Evidence
            .Where(evidence => RequiredCategories.Contains(evidence.Category, StringComparer.OrdinalIgnoreCase))
            .Select(evidence => evidence.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.MissingRequiredCategories.AddRange(RequiredCategories
            .Where(category => !presentCategories.Contains(category)));

        if (result.Status == "passed" && result.MissingRequiredCategories.Count > 0)
        {
            result.Issues.Add("Passed OAuth live evidence requires consent, exchange, refresh, upload, cleanup, and account evidence.");
        }

        result.ProofComplete = result.Status == "passed" &&
            result.Issues.Count == 0 &&
            result.MissingRequiredCategories.Count == 0;
        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.ProofComplete
            ? $"OAuth live evidence recorded as passed for {result.ProviderName}. No provider was contacted by the recorder."
            : result.Succeeded
                ? $"OAuth live evidence recorded as {result.Status} for {result.ProviderName}. No provider was contacted by the recorder."
                : $"OAuth live evidence record has blockers for {result.ProviderName}. No provider was contacted by the recorder.";

        Directory.CreateDirectory(outputRoot);
        var slug = Slug(string.IsNullOrWhiteSpace(result.ProviderName) ? "oauth-provider" : result.ProviderName);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-live-evidence.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            $"{slug}-live-evidence.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static List<OAuthLiveEvidenceItem> NormalizeEvidence(
        string outputRoot,
        IReadOnlyList<OAuthLiveEvidenceInput> evidence)
    {
        var normalized = new List<OAuthLiveEvidenceItem>();
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

            normalized.Add(new OAuthLiveEvidenceItem
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

    private static string BuildMarkdown(OAuthLiveEvidenceRecordResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts OAuth Live Evidence Record");
        builder.AppendLine();
        builder.AppendLine($"Provider: `{result.ProviderName}`");
        builder.AppendLine($"Provider kind: `{result.ProviderKind}`");
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
        builder.AppendLine($"- Would open browser: `{result.WouldOpenBrowser}`");
        builder.AppendLine($"- Would contact provider: `{result.WouldContactProvider}`");
        builder.AppendLine($"- Would exchange code: `{result.WouldExchangeCode}`");
        builder.AppendLine($"- Would store token: `{result.WouldStoreToken}`");
        builder.AppendLine($"- Would refresh token: `{result.WouldRefreshToken}`");
        builder.AppendLine($"- Would upload file: `{result.WouldUploadFile}`");
        builder.AppendLine($"- Would delete remote file: `{result.WouldDeleteRemoteFile}`");

        AppendList(builder, "Issues", result.Issues);
        AppendList(builder, "Missing Required Categories", result.MissingRequiredCategories);

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
        builder.AppendLine("- The recorder does not open a browser, contact a provider, exchange codes, store tokens, refresh tokens, upload files, or delete remote files.");
        builder.AppendLine("- A `passed` status is only accepted when consent, exchange, refresh, upload, cleanup, and account evidence are all present.");
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
            "consent" or "consentscreen" => "consent",
            "exchange" or "codeexchange" or "callback" => "exchange",
            "refresh" or "refreshtoken" => "refresh",
            "upload" or "uploadproof" => "upload",
            "cleanup" or "delete" or "remotedelete" => "cleanup",
            "account" or "accountdiagnostic" or "diagnostic" or "diagnostics" => "account",
            _ => "other"
        };
    }

    private static string BuildProviderKind(string providerName)
    {
        if (providerName.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return "youtube";
        }

        if (providerName.Contains("google photos", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("googlephotos", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("photos", StringComparison.OrdinalIgnoreCase))
        {
            return "google-photos";
        }

        if (providerName.Contains("onenote", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("one note", StringComparison.OrdinalIgnoreCase))
        {
            return "onenote";
        }

        if (providerName.Contains("onedrive", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return "onedrive";
        }

        if (providerName.Contains("google", StringComparison.OrdinalIgnoreCase))
        {
            return "google-drive";
        }

        if (providerName.Contains("dropbox", StringComparison.OrdinalIgnoreCase))
        {
            return "dropbox";
        }

        return "generic-oauth";
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

        return string.IsNullOrWhiteSpace(slug) ? "oauth-provider" : slug;
    }
}

public sealed class OAuthLiveEvidenceRecordRequest
{
    public IReadOnlyList<OAuthProviderSettings> Providers { get; set; } = Array.Empty<OAuthProviderSettings>();
    public string? ProviderName { get; set; }
    public string? Status { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<OAuthLiveEvidenceInput> Evidence { get; set; } = new();
}

public sealed class OAuthLiveEvidenceInput
{
    public string Category { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
}

public sealed class OAuthLiveEvidenceRecordResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ProofComplete { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool ClientIdConfigured { get; set; }
    public bool AuthorizationEndpointConfigured { get; set; }
    public bool TokenEndpointConfigured { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public bool UsesPkce { get; set; }
    public bool RefreshTokenMarkerAlreadyStored { get; set; }
    public bool WouldOpenBrowser { get; set; }
    public bool WouldContactProvider { get; set; }
    public bool WouldExchangeCode { get; set; }
    public bool WouldStoreToken { get; set; }
    public bool WouldRefreshToken { get; set; }
    public bool WouldUploadFile { get; set; }
    public bool WouldDeleteRemoteFile { get; set; }
    public List<string> RequiredCategories { get; set; } = new();
    public List<string> MissingRequiredCategories { get; set; } = new();
    public List<OAuthLiveEvidenceItem> Evidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class OAuthLiveEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideOutputRoot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

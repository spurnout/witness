using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class OAuthLiveProofPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<OAuthLiveProofPlanResult> CreateAsync(
        OAuthLiveProofPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "oauth-live-proof-plan"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        var callbackUri = ResolveCallbackUri(request);
        var providers = NormalizeProviders(request.Providers, request.ProviderName);

        var result = new OAuthLiveProofPlanResult
        {
            OutputPath = outputRoot,
            CallbackUri = SensitiveTextDetector.Redact(callbackUri),
            PolicyAllowed = request.PolicyAllowed,
            PolicyReason = SensitiveTextDetector.Redact(request.PolicyReason ?? string.Empty),
            WouldOpenBrowser = false,
            WouldContactProvider = false,
            WouldExchangeCode = false,
            WouldStoreToken = false,
            WouldRefreshToken = false,
            WouldUploadFile = false,
            WouldDeleteRemoteFile = false
        };

        if (providers.Count == 0)
        {
            result.Issues.Add(string.IsNullOrWhiteSpace(request.ProviderName)
                ? "No OAuth providers are configured."
                : $"OAuth provider was not found: {request.ProviderName}");
        }

        Directory.CreateDirectory(outputRoot);
        foreach (var provider in providers)
        {
            result.Providers.Add(BuildProviderPlan(provider, callbackUri, request));
        }

        result.ManualGates.AddRange(BuildManualGates());
        result.AuthorityBoundaries.AddRange(BuildAuthorityBoundaries());
        result.NonGoals.AddRange(BuildNonGoals());
        result.Succeeded = result.Issues.Count == 0 &&
            result.Providers.Count > 0 &&
            result.Providers.All(provider => provider.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"OAuth live proof plan created: {outputRoot}. No provider was contacted."
            : $"OAuth live proof plan created with blockers: {outputRoot}. No provider was contacted.";

        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "oauth-live-proof-plan.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "oauth-live-proof-plan.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static OAuthLiveProofProviderPlan BuildProviderPlan(
        OAuthProviderSettings provider,
        string callbackUri,
        OAuthLiveProofPlanRequest request)
    {
        var name = provider.ProviderName.Trim();
        var profile = BuildProviderProfile(name);
        var plan = new OAuthLiveProofProviderPlan
        {
            ProviderName = SensitiveTextDetector.Redact(name),
            ProviderKind = profile.Kind,
            AuthorizationEndpointConfigured = !string.IsNullOrWhiteSpace(provider.AuthorizationEndpoint),
            TokenEndpointConfigured = !string.IsNullOrWhiteSpace(provider.TokenEndpoint),
            ClientIdConfigured = !string.IsNullOrWhiteSpace(provider.ClientId),
            Scopes = SensitiveTextDetector.Redact(provider.Scopes ?? string.Empty),
            UsesPkce = provider.UsePkce,
            RefreshTokenMarkerAlreadyStored = provider.RefreshTokenStored,
            CallbackUri = SensitiveTextDetector.Redact(callbackUri),
            CleanupBoundary = profile.CleanupBoundary,
            WouldOpenBrowser = false,
            WouldContactProvider = false,
            WouldExchangeCode = false,
            WouldStoreToken = false,
            WouldRefreshToken = false,
            WouldUploadFile = false,
            WouldDeleteRemoteFile = false
        };

        if (!request.PolicyAllowed)
        {
            plan.Issues.Add(string.IsNullOrWhiteSpace(request.PolicyReason)
                ? "OAuth/live provider proof is disabled by managed policy."
                : SensitiveTextDetector.Redact(request.PolicyReason));
            plan.Status = "blocked-by-managed-policy";
        }

        if (!plan.ClientIdConfigured)
        {
            plan.Issues.Add("OAuth client ID is not configured.");
        }

        if (!plan.AuthorizationEndpointConfigured)
        {
            plan.Issues.Add("OAuth authorization endpoint is not configured.");
        }

        if (!plan.TokenEndpointConfigured)
        {
            plan.Issues.Add("OAuth token endpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(provider.Scopes))
        {
            plan.Warnings.Add("OAuth scopes are empty; confirm that the provider configuration is intentional before live proof.");
        }

        plan.RequiredEvidence.AddRange(BuildRequiredEvidence(name));
        plan.RequiredEvidence.AddRange(RedactAll(profile.ProviderSpecificEvidence));
        plan.ManualSteps.AddRange(BuildManualSteps(name));
        plan.ScopeReview.AddRange(RedactAll(profile.ScopeReview));
        plan.ConsentScreenChecklist.AddRange(RedactAll(profile.ConsentScreenChecklist));
        plan.AccountDiagnostics.AddRange(RedactAll(profile.AccountDiagnostics));
        plan.Commands.Add($"receipts oauth status --provider {Quote(name)} --json");
        plan.Commands.Add($"receipts oauth auth-url {Quote(name)} --state <operator-random-state> --callback {Quote(plan.CallbackUri)}");
        plan.Commands.Add($"receipts oauth exchange {Quote(name)} --code <authorization-code> --callback {Quote(plan.CallbackUri)}{(provider.UsePkce ? " --code-verifier <code-verifier-from-auth-url>" : string.Empty)}");
        plan.Commands.Add($"receipts oauth refresh {Quote(name)}");
        plan.Commands.Add($"receipts diagnostics providers --provider {Quote(name)} --json");
        plan.Commands.Add(profile.UploadProofCommand);

        if (plan.Issues.Count == 0)
        {
            plan.Status = "manual-live-proof-plan-ready";
            plan.Message = $"{name} has enough local OAuth configuration to start a human live proof run. This plan did not contact {name}.";
        }
        else if (string.IsNullOrWhiteSpace(plan.Status))
        {
            plan.Status = "blocked-before-live-proof";
            plan.Message = $"{name} needs local OAuth setup before a human live proof run.";
        }
        else
        {
            plan.Message = $"{name} live proof is blocked by policy.";
        }

        return plan;
    }

    private static OAuthProviderProofProfile BuildProviderProfile(string providerName)
    {
        if (providerName.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "youtube",
                [
                    "Confirm the configured scope stays limited to `https://www.googleapis.com/auth/youtube.upload`; broader Google scopes require a separate risk note.",
                    "Confirm the proof account owns or can publish to the intended YouTube channel before starting live proof.",
                    "Reject evidence that exposes Google authorization codes, bearer tokens, refresh tokens, account emails, private channel metadata, quota project IDs, or private video management URLs."
                ],
                [
                    "Capture the Google/YouTube consent screen with the account and channel identifiers redacted.",
                    "Confirm the app name and YouTube upload scope are visible before approval.",
                    "Confirm the redirect URI configured in Google Cloud matches the local callback URI."
                ],
                [
                    "Verify the Google Cloud OAuth client is in the expected publishing or test-user state.",
                    "Verify the proof account has a safe YouTube channel and enough upload quota for the test media.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "YouTube upload evidence should use a short safe test video, include the returned video ID or youtu.be URL, and record the privacy status used for proof.",
                    "YouTube cleanup evidence should be a reviewed delete/unlist note or UI/API proof; this planner does not perform remote delete."
                ],
                "YouTube cleanup is manual/reviewed until a safe delete flow is implemented and proven; do not leave private proof media published.",
                "receipts upload <safe-test-video.mp4> --provider \"YouTube\" --json");
        }

        if (providerName.Contains("onenote", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("one note", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "onenote",
                [
                    "Confirm the configured scopes stay limited to OneNote page creation needs such as `Notes.Create Files.Read offline_access` or a narrower approved Microsoft Graph set.",
                    "Confirm tenant policy allows the app and account to consent to the requested Notes permissions.",
                    "Reject evidence that exposes authorization codes, bearer tokens, refresh tokens, tenant IDs tied to private orgs, notebook names, section IDs, or private `oneNoteWebUrl` values."
                ],
                [
                    "Capture the Microsoft account or tenant consent screen with the user/tenant identifier redacted.",
                    "Confirm the app name, Notes permission prompt, and offline access behavior are visible before approval.",
                    "Confirm the redirect URI in the Microsoft app registration matches the local callback URI."
                ],
                [
                    "Verify the app registration, supported account type, and redirect URI in the expected Microsoft tenant.",
                    "Verify the test account has a safe proof notebook/section available or can create pages in the default notebook.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "OneNote export evidence should include a safe proof notebook/section boundary, returned page URL or ID, and confirmation that no private capture content was uploaded.",
                    "OneNote cleanup evidence should be a manual page delete note or reviewed API/UI proof; this planner does not perform remote delete."
                ],
                "OneNote cleanup is manual/reviewed until a safe delete flow is implemented and proven; remove safe proof pages after evidence capture.",
                "receipts upload <safe-test-file> --provider \"OneNote\" --json");
        }

        if (providerName.Contains("onedrive", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "onedrive",
                [
                    "Confirm the configured scopes stay at `Files.ReadWrite offline_access` or a narrower approved Microsoft Graph set.",
                    "Confirm tenant policy allows the app and account to consent to the requested file permissions.",
                    "Reject evidence that exposes authorization codes, bearer tokens, refresh tokens, tenant IDs tied to private orgs, or private `webUrl` values."
                ],
                [
                    "Capture the Microsoft account or tenant consent screen with the user/tenant identifier redacted.",
                    "Confirm the app name, file permission prompt, and offline access behavior are visible before approval.",
                    "Confirm the redirect URI in the Microsoft app registration matches the local callback URI."
                ],
                [
                    "Verify the app registration, supported account type, and redirect URI in the expected Microsoft tenant.",
                    "Verify conditional access or tenant consent policy will not block the proof account.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "OneDrive upload evidence should include a safe proof folder path, returned item ID or web URL, and confirmation that no private file content was uploaded.",
                    "OneDrive cleanup evidence should be a manual recycle-bin/delete note or reviewed API/UI proof; this planner does not perform remote delete."
                ],
                "OneDrive cleanup is manual/reviewed until a safe delete flow is implemented and proven; do not keep private proof files in the proof folder.",
                "receipts upload <safe-test-file> --provider \"OneDrive\" --onedrive-folder /ReceiptsProof --json");
        }

        if (providerName.Contains("google photos", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("googlephotos", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("photos", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "google-photos",
                [
                    "Confirm the configured scope stays limited to `https://www.googleapis.com/auth/photoslibrary.appendonly`; broader Google Photos scopes require a separate risk note.",
                    "Confirm the proof account owns the target Photos library or target album before starting live proof.",
                    "Reject evidence that exposes Google authorization codes, bearer tokens, refresh tokens, account emails, album IDs tied to private data, upload tokens, or private media URLs."
                ],
                [
                    "Capture the Google Photos consent screen with the account identifier redacted.",
                    "Confirm the app name and append-only Photos Library scope are visible before approval.",
                    "Confirm the redirect URI configured in Google Cloud matches the local callback URI."
                ],
                [
                    "Verify the Google Cloud OAuth client is in the expected publishing or test-user state.",
                    "Verify the safe proof account can upload to Google Photos and, if configured, to the target album.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "Google Photos upload evidence should include a safe proof image or video, returned media item ID or product URL, and the album boundary when an album was targeted.",
                    "Google Photos cleanup evidence should be a reviewed delete/archive note or UI/API proof; this planner does not perform remote delete."
                ],
                "Google Photos cleanup is manual/reviewed until a safe delete flow is implemented and proven; do not leave private proof media in the library.",
                "receipts upload <safe-test-image-or-video> --provider \"Google Photos\" --google-photos-album-id <safe-proof-album-id> --json");
        }

        if (providerName.Contains("google", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "google-drive",
                [
                    "Confirm the configured scopes remain limited to Drive file access such as `https://www.googleapis.com/auth/drive.file`; broader Drive scopes require a separate risk note.",
                    "Confirm the generated authorization URL includes offline access and consent prompting before expecting a refresh token.",
                    "Reject evidence that exposes Google authorization codes, bearer tokens, refresh tokens, account email addresses, or Drive file IDs tied to private data."
                ],
                [
                    "Capture the Google account chooser or consent screen with the account identifier redacted.",
                    "Confirm the app name and requested Drive scope are visible before approval.",
                    "Confirm the redirect URI shown or configured in Google Cloud matches the local callback URI."
                ],
                [
                    "Verify the Google Cloud OAuth client is in the expected publishing or test-user state.",
                    "Verify the approved test Google account is listed as a test user when the app is not published.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "Google Drive upload evidence should include a safe proof folder ID, returned Drive web link or file ID, and confirmation that no private file content was uploaded.",
                    "Google Drive cleanup evidence should be a manual trash/delete note or reviewed API/UI proof; this planner does not perform remote delete."
                ],
                "Google Drive cleanup is manual/reviewed until a safe delete flow is implemented and proven; do not keep private proof files in the test folder.",
                "receipts upload <safe-test-file> --provider \"Google Drive\" --google-drive-folder-id <safe-proof-folder-id> --json");
        }

        if (providerName.Contains("dropbox", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthProviderProofProfile(
                "dropbox",
                [
                    "Confirm the configured scopes stay limited to file upload/link needs such as `files.content.write` and `sharing.write`.",
                    "Confirm whether the Dropbox app is app-folder scoped or full-Dropbox scoped, and record that boundary in proof notes.",
                    "Reject evidence that exposes authorization codes, bearer tokens, refresh tokens, account emails, private temporary links, or upload-session data."
                ],
                [
                    "Capture the Dropbox consent screen with the account identifier redacted.",
                    "Confirm the app name, app-folder or full-Dropbox access boundary, and requested file permissions are visible before approval.",
                    "Confirm the Dropbox redirect URI configured for the app matches the local callback URI."
                ],
                [
                    "Verify the Dropbox app key/client ID and redirect URI belong to the approved product owner account.",
                    "Verify the test account can grant the requested app-folder or full-Dropbox access boundary.",
                    "Run provider diagnostics after token exchange and keep only redacted output."
                ],
                [
                    "Dropbox upload evidence should include a safe proof folder path, returned temporary link or metadata ID, and confirmation that no private file content was uploaded.",
                    "Dropbox cleanup evidence should be a manual delete note or reviewed API/UI proof; this planner does not perform remote delete."
                ],
                "Dropbox cleanup is manual/reviewed until a safe delete flow is implemented and proven; remove any safe test uploads after evidence capture.",
                "receipts upload <safe-test-file> --provider \"Dropbox\" --dropbox-folder /ReceiptsProof --json");
        }

        return new OAuthProviderProofProfile(
            "generic-oauth",
            [
                "Review every configured scope against the minimum permission needed for upload proof.",
                "Confirm refresh-token behavior is expected for this provider before treating refresh proof as required.",
                "Reject evidence that exposes authorization codes, bearer tokens, refresh tokens, account identifiers, private URLs, or upload-session data."
            ],
            [
                $"Capture the {providerName} consent screen with account identifiers redacted.",
                "Confirm the app name, requested scopes, and redirect/callback context are visible before approval.",
                "Confirm the configured redirect URI matches the local callback URI."
            ],
            [
                $"Verify the {providerName} OAuth app/client belongs to the approved owner account.",
                "Verify the proof account can grant the requested scopes.",
                "Run provider diagnostics after token exchange and keep only redacted output."
            ],
            [
                $"{providerName} upload evidence should include only safe non-private media and redacted provider return values.",
                $"{providerName} cleanup evidence should be manual or reviewed API/UI proof unless a safe delete path is implemented."
            ],
            $"{providerName} cleanup is manual/reviewed until a safe delete flow is implemented and proven.",
            $"receipts upload <safe-test-file> --provider {Quote(providerName)} --json");
    }

    private static IReadOnlyList<string> BuildRequiredEvidence(string providerName) => new[]
    {
        $"{providerName} consent screen screenshot showing app name, account context, requested scopes, and redirect/callback context without exposing authorization codes.",
        $"{providerName} callback or exchange command log with authorization codes, access tokens, refresh tokens, account names, and URLs redacted.",
        $"{providerName} token storage status showing access-token and refresh-token markers, not raw token values.",
        $"{providerName} refresh command result proving refresh-token recovery against the live account.",
        $"{providerName} upload proof using safe non-private test media and a returned URL/file id with secrets redacted.",
        $"{providerName} remote-delete or cleanup proof when the provider has a safe implemented cleanup path; otherwise a note that cleanup was manual or not supported.",
        "Updated manual-validation lane or findings output after proof is reviewed."
    };

    private static IReadOnlyList<string> BuildManualSteps(string providerName) => new[]
    {
        "Create a safe throwaway capture or media file with no private desktop, customer, account, or credential content.",
        $"Confirm the configured {providerName} OAuth client belongs to the product owner or approved test account.",
        "Run `oauth auth-url` and open the generated URL manually in a browser.",
        "Capture the consent screen before approval, with account identifiers redacted if needed.",
        "Exchange the returned code through `oauth exchange`; save only redacted command output.",
        "Run `oauth refresh` to prove refresh-token recovery.",
        "Run provider diagnostics and one safe upload proof.",
        "Review every saved artifact for OAuth codes, bearer tokens, refresh tokens, account names, private URLs, and upload session URLs before keeping it as release evidence."
    };

    private static IReadOnlyList<string> BuildManualGates() => new[]
    {
        "Human approval to use a real provider account.",
        "Provider-owned OAuth app/client configuration.",
        "Safe media staged for upload proof.",
        "Redaction review before artifacts are attached to release evidence.",
        "Provider cleanup or retention decision for uploaded test media."
    };

    private static IReadOnlyList<string> BuildAuthorityBoundaries() => new[]
    {
        "This command writes a local plan only.",
        "Receipts does not open a browser, exchange an authorization code, store credentials, refresh tokens, upload files, delete remote files, or contact providers while generating this plan.",
        "Live OAuth evidence requires a human operator, a real provider account, and reviewed redacted artifacts.",
        "Stored-token markers are not raw-token proof and must not be exported as secrets."
    };

    private static IReadOnlyList<string> BuildNonGoals() => new[]
    {
        "No live consent screen is opened.",
        "No authorization code is exchanged.",
        "No OAuth token or refresh token is stored.",
        "No provider API is contacted.",
        "No upload, share-link creation, refresh, cleanup, or remote delete is performed.",
        "No live-account readiness claim is made by this plan."
    };

    private static string BuildMarkdown(OAuthLiveProofPlanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts OAuth Live Proof Plan");
        builder.AppendLine();
        builder.AppendLine($"Callback URI: `{result.CallbackUri}`");
        builder.AppendLine($"Policy allowed: `{result.PolicyAllowed}`");
        if (!string.IsNullOrWhiteSpace(result.PolicyReason))
        {
            builder.AppendLine($"Policy reason: `{result.PolicyReason}`");
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

        if (result.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Plan Issues");
            foreach (var issue in result.Issues)
            {
                builder.AppendLine($"- {issue}");
            }
        }

        foreach (var provider in result.Providers)
        {
            builder.AppendLine();
            builder.AppendLine($"## {provider.ProviderName}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{provider.Status}`");
            builder.AppendLine($"Message: {provider.Message}");
            builder.AppendLine($"Client ID configured: `{provider.ClientIdConfigured}`");
            builder.AppendLine($"Authorization endpoint configured: `{provider.AuthorizationEndpointConfigured}`");
            builder.AppendLine($"Token endpoint configured: `{provider.TokenEndpointConfigured}`");
            builder.AppendLine($"PKCE: `{provider.UsesPkce}`");
            builder.AppendLine($"Scopes: `{(string.IsNullOrWhiteSpace(provider.Scopes) ? "(none)" : provider.Scopes)}`");
            builder.AppendLine($"Refresh-token marker already stored: `{provider.RefreshTokenMarkerAlreadyStored}`");
            builder.AppendLine($"Provider kind: `{provider.ProviderKind}`");
            builder.AppendLine($"Cleanup boundary: {provider.CleanupBoundary}");
            AppendList(builder, "Issues", provider.Issues);
            AppendList(builder, "Warnings", provider.Warnings);
            AppendList(builder, "Scope Review", provider.ScopeReview);
            AppendList(builder, "Consent Screen Checklist", provider.ConsentScreenChecklist);
            AppendList(builder, "Account Diagnostics", provider.AccountDiagnostics);
            AppendList(builder, "Manual Steps", provider.ManualSteps);
            AppendList(builder, "Required Evidence", provider.RequiredEvidence);
            AppendList(builder, "Suggested Commands", provider.Commands);
        }

        AppendList(builder, "Manual Gates", result.ManualGates);
        AppendList(builder, "Authority Boundaries", result.AuthorityBoundaries);
        AppendList(builder, "Non-Goals", result.NonGoals);
        return builder.ToString();
    }

    private static IReadOnlyList<OAuthProviderSettings> NormalizeProviders(
        IEnumerable<OAuthProviderSettings> providers,
        string? providerName)
    {
        var list = providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderName))
            .OrderBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(providerName) ||
            providerName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return list;
        }

        return list
            .Where(provider => provider.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string ResolveCallbackUri(OAuthLiveProofPlanRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CallbackUri))
        {
            return request.CallbackUri.Trim();
        }

        var port = Math.Clamp(request.CallbackPort <= 0 ? 53628 : request.CallbackPort, 1, 65_535);
        return $"http://127.0.0.1:{port}/oauth/callback";
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"### {title}");
        foreach (var item in items)
        {
            builder.AppendLine($"- {item}");
        }
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

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static IEnumerable<string> RedactAll(IEnumerable<string> values) =>
        values.Select(value => SensitiveTextDetector.Redact(value));

    private sealed record OAuthProviderProofProfile(
        string Kind,
        IReadOnlyList<string> ScopeReview,
        IReadOnlyList<string> ConsentScreenChecklist,
        IReadOnlyList<string> AccountDiagnostics,
        IReadOnlyList<string> ProviderSpecificEvidence,
        string CleanupBoundary,
        string UploadProofCommand);
}

public sealed class OAuthLiveProofPlanRequest
{
    public IReadOnlyList<OAuthProviderSettings> Providers { get; set; } = Array.Empty<OAuthProviderSettings>();
    public string? ProviderName { get; set; }
    public string? OutputPath { get; set; }
    public string? CallbackUri { get; set; }
    public int CallbackPort { get; set; } = 53628;
    public bool PolicyAllowed { get; set; } = true;
    public string? PolicyReason { get; set; }
}

public sealed class OAuthLiveProofPlanResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string CallbackUri { get; set; } = string.Empty;
    public bool PolicyAllowed { get; set; } = true;
    public string PolicyReason { get; set; } = string.Empty;
    public bool WouldOpenBrowser { get; set; }
    public bool WouldContactProvider { get; set; }
    public bool WouldExchangeCode { get; set; }
    public bool WouldStoreToken { get; set; }
    public bool WouldRefreshToken { get; set; }
    public bool WouldUploadFile { get; set; }
    public bool WouldDeleteRemoteFile { get; set; }
    public List<OAuthLiveProofProviderPlan> Providers { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> ManualGates { get; set; } = new();
    public List<string> AuthorityBoundaries { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class OAuthLiveProofProviderPlan
{
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool ClientIdConfigured { get; set; }
    public bool AuthorizationEndpointConfigured { get; set; }
    public bool TokenEndpointConfigured { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public bool UsesPkce { get; set; }
    public bool RefreshTokenMarkerAlreadyStored { get; set; }
    public string CallbackUri { get; set; } = string.Empty;
    public string CleanupBoundary { get; set; } = string.Empty;
    public bool WouldOpenBrowser { get; set; }
    public bool WouldContactProvider { get; set; }
    public bool WouldExchangeCode { get; set; }
    public bool WouldStoreToken { get; set; }
    public bool WouldRefreshToken { get; set; }
    public bool WouldUploadFile { get; set; }
    public bool WouldDeleteRemoteFile { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ScopeReview { get; set; } = new();
    public List<string> ConsentScreenChecklist { get; set; } = new();
    public List<string> AccountDiagnostics { get; set; } = new();
    public List<string> ManualSteps { get; set; } = new();
    public List<string> RequiredEvidence { get; set; } = new();
    public List<string> Commands { get; set; } = new();
}

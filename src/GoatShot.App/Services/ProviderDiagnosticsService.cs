using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ProviderDiagnosticsService
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;

    public ProviderDiagnosticsService(AppSettings settings, SecretStore secretStore)
    {
        _settings = settings;
        _secretStore = secretStore;
    }

    public IReadOnlyList<ProviderDiagnosticRecord> GetDiagnostics()
    {
        return ShareProviderCatalog.CreateDefault()
            .Select(BuildRecord)
            .OrderBy(record => record.CatalogImplemented ? 0 : 1)
            .ThenBy(record => record.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ProviderDiagnosticRecord BuildRecord(IShareProvider provider)
    {
        var record = new ProviderDiagnosticRecord
        {
            ProviderName = provider.ProviderName,
            DestinationName = ResolveDestinationName(provider.ProviderName),
            AuthType = provider.AuthType,
            CatalogImplemented = provider.IsImplemented,
            SupportsPublicLinks = provider.SupportsPublicLinks,
            SupportsPrivateLinks = provider.SupportsPrivateLinks,
            SupportsExpiration = provider.SupportsExpiration,
            SupportsPassword = provider.SupportsPassword
        };

        AddProviderRequirements(record);
        if (!record.CatalogImplemented)
        {
            record.Notes.Add("Roadmap provider; upload adapter is not implemented yet.");
        }

        if (record.AuthType.Contains("OAuth", StringComparison.OrdinalIgnoreCase))
        {
            record.Notes.Add("Local diagnostics check saved token/configuration presence only; live consent and provider-specific OAuth polish remain separate proof lanes.");
        }

        var policyReason = string.Empty;
        var blockedByPolicy = TryResolveDestination(record, out var destination) &&
            !ManagedPolicyService.IsShareDestinationAllowed(_settings, destination, out policyReason);
        if (blockedByPolicy)
        {
            record.Notes.Add(policyReason);
        }

        record.ReadyForLocalAttempt =
            !blockedByPolicy &&
            record.CatalogImplemented &&
            record.MissingSettings.Count == 0 &&
            record.MissingSecrets.Count == 0;
        record.Status = blockedByPolicy
            ? "Blocked by policy"
            : !record.CatalogImplemented
            ? "Roadmap"
            : record.ReadyForLocalAttempt
                ? "Ready"
                : "Needs configuration";
        record.ReadinessSummary = BuildSummary(record);
        return record;
    }

    private void AddProviderRequirements(ProviderDiagnosticRecord record)
    {
        switch (record.ProviderName)
        {
            case "Local folder":
                NoteSetting(record, "Local export folder", _settings.LocalExportFolder, "default Desktop\\GoatShot Exports folder");
                break;
            case "Clipboard image":
            case "Clipboard file":
            case "Clipboard path":
            case "Markdown image link":
                record.Notes.Add("Local clipboard/path destination; no provider credentials required.");
                break;
            case "Email attachment":
                record.Notes.Add("Uses the default Windows mail client handoff; mail client availability is not verified.");
                break;
            case "S3-compatible":
                RequireSetting(record, "S3 endpoint", _settings.S3Endpoint);
                RequireSetting(record, "S3 bucket", _settings.S3Bucket);
                NoteSetting(record, "S3 region", _settings.S3Region, "us-east-1 default");
                RequireSecret(record, "S3 access key ID and secret access key", _secretStore.HasS3Credentials);
                break;
            case "SFTP":
                RequireSetting(record, "SFTP host", _settings.SftpHost);
                RequireSetting(record, "SFTP username", _settings.SftpUsername);
                NoteSetting(record, "SFTP remote directory", _settings.SftpRemoteDirectory, "/ default");
                if (!string.IsNullOrWhiteSpace(_settings.SftpPrivateKeyPath))
                {
                    if (File.Exists(Environment.ExpandEnvironmentVariables(_settings.SftpPrivateKeyPath)))
                    {
                        record.ConfiguredSettings.Add("SFTP private key path exists");
                    }
                    else
                    {
                        record.MissingSettings.Add("SFTP private key file");
                    }
                }

                if (ShareService.FindSftpExecutable(_settings.SftpExecutablePath) is null)
                {
                    record.MissingSettings.Add("OpenSSH sftp.exe");
                }
                else
                {
                    record.ConfiguredSettings.Add("OpenSSH sftp.exe available");
                }
                break;
            case "Imgur":
                NoteSetting(record, "Imgur API endpoint", _settings.ImgurApiEndpoint, "default Imgur API endpoint");
                RequireSecret(record, "Imgur Client ID", _secretStore.HasImgurClientId);
                break;
            case "Cloudinary":
                RequireSetting(record, "Cloudinary cloud name", _settings.CloudinaryCloudName);
                NoteSetting(record, "Cloudinary resource type", _settings.CloudinaryResourceType, "auto default");
                NoteSetting(record, "Cloudinary API base URL", _settings.CloudinaryApiBaseUrl, "default Cloudinary API base URL");
                var hasPreset = !string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset);
                var hasCredentials = _secretStore.HasCloudinaryCredentials;
                if (hasPreset)
                {
                    record.ConfiguredSettings.Add("Cloudinary unsigned upload preset");
                }

                if (hasCredentials)
                {
                    record.SavedSecrets.Add("Cloudinary API key and API secret");
                    if (!hasPreset)
                    {
                        record.Notes.Add("Cloudinary unsigned upload preset is not configured; authenticated upload can be used instead.");
                    }
                }
                else
                {
                    record.Notes.Add("Cloudinary can use either an unsigned upload preset or DPAPI-saved API credentials.");
                }

                if (!hasPreset && !hasCredentials)
                {
                    record.MissingSettings.Add("Cloudinary unsigned upload preset");
                    record.MissingSecrets.Add("Cloudinary API key and API secret");
                }
                break;
            case "Dropbox":
                NoteSetting(record, "Dropbox content API base URL", _settings.DropboxContentApiBaseUrl, "default Dropbox content API endpoint");
                NoteSetting(record, "Dropbox API base URL", _settings.DropboxApiBaseUrl, "default Dropbox API endpoint");
                NoteSetting(record, "Dropbox remote folder", _settings.DropboxRemoteFolder, "/GoatShot default");
                RequireSecret(record, "Dropbox access token", _secretStore.HasDropboxAccessToken);
                NoteOAuthToken(record, "Dropbox");
                break;
            case "Google Drive":
                NoteSetting(record, "Google Drive upload API base URL", _settings.GoogleDriveUploadApiBaseUrl, "default Drive upload API endpoint");
                NoteSetting(record, "Google Drive API base URL", _settings.GoogleDriveApiBaseUrl, "default Drive API endpoint");
                OptionalSetting(record, "Google Drive folder ID", _settings.GoogleDriveFolderId);
                RequireSecret(record, "Google Drive access token", _secretStore.HasGoogleDriveAccessToken);
                NoteOAuthToken(record, "Google Drive");
                break;
            case "Google Photos":
                NoteSetting(record, "Google Photos upload API base URL", _settings.GooglePhotosUploadApiBaseUrl, "default Google Photos upload endpoint");
                NoteSetting(record, "Google Photos API base URL", _settings.GooglePhotosApiBaseUrl, "default Google Photos Library API endpoint");
                OptionalSetting(record, "Google Photos album ID", _settings.GooglePhotosAlbumId);
                RequireSecret(record, "Google Photos OAuth access token", _secretStore.HasGooglePhotosAccessToken);
                NoteOAuthToken(record, "Google Photos");
                record.Notes.Add("Uploads image/video bytes and creates a Google Photos media item; live consent, quota, album ownership, cleanup, and account proof remain separate proof lanes.");
                break;
            case "OneDrive":
                NoteSetting(record, "OneDrive Graph API base URL", _settings.OneDriveGraphApiBaseUrl, "default Microsoft Graph endpoint");
                NoteSetting(record, "OneDrive remote folder", _settings.OneDriveRemoteFolder, "/GoatShot default");
                RequireSecret(record, "OneDrive access token", _secretStore.HasOneDriveAccessToken);
                NoteOAuthToken(record, "OneDrive");
                break;
            case "YouTube":
                NoteSetting(record, "YouTube upload API base URL", _settings.YouTubeUploadApiBaseUrl, "default YouTube upload API endpoint");
                NoteSetting(record, "YouTube privacy status", _settings.YouTubePrivacyStatus, "unlisted default");
                NoteSetting(record, "YouTube category ID", _settings.YouTubeCategoryId, "22 People & Blogs default");
                RequireSecret(record, "YouTube OAuth access token", _secretStore.HasYouTubeAccessToken);
                NoteOAuthToken(record, "YouTube");
                record.Notes.Add("Uploads video files through the YouTube Data API; live consent, quota, processing, and account proof remain separate proof lanes.");
                break;
            case "OneNote":
                NoteSetting(record, "OneNote Graph API base URL", _settings.OneNoteGraphApiBaseUrl, "default Microsoft Graph endpoint");
                OptionalSetting(record, "OneNote section ID", _settings.OneNoteSectionId);
                RequireSecret(record, "OneNote OAuth access token", _secretStore.HasOneNoteAccessToken);
                NoteOAuthToken(record, "OneNote");
                record.Notes.Add("Creates a OneNote page with the selected capture attached or embedded; live account proof remains a separate proof lane.");
                break;
            case "Linear":
                NoteSetting(record, "Linear GraphQL endpoint", _settings.LinearGraphqlEndpoint, "default Linear API endpoint");
                if (string.IsNullOrWhiteSpace(_settings.LinearTeamId) &&
                    string.IsNullOrWhiteSpace(_settings.LinearIssueId))
                {
                    record.MissingSettings.Add("Linear team ID or existing issue ID");
                }
                else
                {
                    OptionalSetting(record, "Linear team ID", _settings.LinearTeamId);
                    OptionalSetting(record, "Linear existing issue ID", _settings.LinearIssueId);
                }

                RequireSecret(record, "Linear API key or OAuth access token", _secretStore.HasLinearCredential);
                break;
            case "GitHub Issues":
                RequireSetting(record, "GitHub repository", _settings.GitHubRepository);
                NoteSetting(record, "GitHub API base URL", _settings.GitHubApiBaseUrl, "default GitHub API endpoint");
                RequireSecret(record, "GitHub personal access token", _secretStore.HasGitHubToken);
                break;
            case "Jira":
                RequireSetting(record, "Jira base URL", _settings.JiraBaseUrl);
                RequireSetting(record, "Jira project key", _settings.JiraProjectKey);
                RequireSetting(record, "Jira account email", _settings.JiraAccountEmail);
                NoteSetting(record, "Jira issue type", _settings.JiraIssueType, "Bug default");
                RequireSecret(record, "Jira API token", _secretStore.HasJiraApiToken);
                break;
            case "Slack":
                RequireSetting(record, "Slack webhook URL", _settings.SlackWebhookUrl);
                record.Notes.Add("Incoming webhooks send notifications only; local media is not uploaded to Slack.");
                break;
            case "Discord":
                RequireSetting(record, "Discord webhook URL", _settings.DiscordWebhookUrl);
                break;
            case "Microsoft Teams":
                RequireSetting(record, "Microsoft Teams webhook URL", _settings.TeamsWebhookUrl);
                record.Notes.Add("Incoming webhooks send notifications only; local media is not uploaded to Teams.");
                break;
            case "WebDAV":
                RequireSetting(record, "WebDAV base URL", _settings.WebDavBaseUrl);
                NoteSetting(record, "WebDAV remote directory", _settings.WebDavRemoteDirectory, "/ default");
                OptionalSetting(record, "WebDAV username", _settings.WebDavUsername);
                if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername))
                {
                    RequireSecret(record, "WebDAV password", _secretStore.HasWebDavPassword);
                }
                break;
            case "Azure DevOps":
                RequireSetting(record, "Azure DevOps organization", _settings.AzureDevOpsOrganization);
                RequireSetting(record, "Azure DevOps project", _settings.AzureDevOpsProject);
                NoteSetting(record, "Azure DevOps base URL", _settings.AzureDevOpsBaseUrl, "default Azure DevOps base URL");
                NoteSetting(record, "Azure DevOps work item type", _settings.AzureDevOpsWorkItemType, "Bug default");
                RequireSecret(record, "Azure DevOps personal access token", _secretStore.HasAzureDevOpsPat);
                break;
            case "FTP/FTPS":
                RequireSetting(record, "FTP/FTPS host", _settings.FtpHost);
                RequireSetting(record, "FTP/FTPS username", _settings.FtpUsername);
                NoteSetting(record, "FTP/FTPS remote directory", _settings.FtpRemoteDirectory, "/ default");
                record.ConfiguredSettings.Add(_settings.FtpUseFtps ? "FTPS explicit TLS enabled" : "Plain FTP selected");
                RequireSecret(record, "FTP/FTPS password", _secretStore.HasFtpPassword);
                break;
            case "Custom script":
                RequireSetting(record, "Custom script command", _settings.CustomScriptCommand);
                record.Notes.Add("Runs a local PowerShell command after placeholder substitution; command output may return a URL.");
                break;
            case "Custom webhook":
                RequireSetting(record, "Custom webhook URL", _settings.CustomWebhookUrl);
                record.Notes.Add("Posts the capture file and local metadata to the configured endpoint.");
                break;
        }
    }

    private static void RequireSetting(ProviderDiagnosticRecord record, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            record.MissingSettings.Add(label);
        }
        else
        {
            record.ConfiguredSettings.Add(label);
        }
    }

    private static void OptionalSetting(ProviderDiagnosticRecord record, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            record.ConfiguredSettings.Add(label);
        }
    }

    private static void NoteSetting(ProviderDiagnosticRecord record, string label, string? value, string defaultDescription)
    {
        record.ConfiguredSettings.Add(string.IsNullOrWhiteSpace(value)
            ? $"{label} ({defaultDescription})"
            : label);
    }

    private static void RequireSecret(ProviderDiagnosticRecord record, string label, bool saved)
    {
        if (saved)
        {
            record.SavedSecrets.Add(label);
        }
        else
        {
            record.MissingSecrets.Add(label);
        }
    }

    private void NoteOAuthToken(ProviderDiagnosticRecord record, string providerName)
    {
        if (_secretStore.HasOAuthToken(providerName))
        {
            record.SavedSecrets.Add($"{providerName} generic OAuth token bundle");
        }
    }

    private static string ResolveDestinationName(string providerName)
    {
        var destination = ShareService.ParseDestination(providerName);
        if (destination == ShareDestination.ClipboardImage &&
            !providerName.Equals("Clipboard image", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return destination.ToString();
    }

    private static string BuildSummary(ProviderDiagnosticRecord record)
    {
        return record.Status switch
        {
            "Ready" => $"{record.ProviderName} has the local settings and saved secret markers needed for a share attempt.",
            "Blocked by policy" => $"{record.ProviderName} is disabled or restricted by the effective managed policy.",
            "Roadmap" => $"{record.ProviderName} is still a roadmap destination.",
            _ => $"{record.ProviderName} is missing {record.MissingSettings.Count + record.MissingSecrets.Count} required item(s)."
        };
    }

    private static bool TryResolveDestination(ProviderDiagnosticRecord record, out ShareDestination destination)
    {
        if (Enum.TryParse(record.DestinationName, ignoreCase: true, out destination))
        {
            return true;
        }

        destination = default;
        return false;
    }
}

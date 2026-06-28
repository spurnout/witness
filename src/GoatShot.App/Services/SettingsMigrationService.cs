using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class SettingsMigrationService
{
    public const int CurrentSchemaVersion = 14;

    public static SettingsMigrationResult Migrate(AppSettings settings)
    {
        var changed = false;

        if (settings.SettingsSchemaVersion < CurrentSchemaVersion)
        {
            settings.SettingsSchemaVersion = CurrentSchemaVersion;
            changed = true;
        }

        if (settings.Recording is null)
        {
            settings.Recording = new RecordingSettings();
            changed = true;
        }

        if (settings.OAuth is null)
        {
            settings.OAuth = new OAuthSettings();
            changed = true;
        }

        if (settings.UploadQueue is null)
        {
            settings.UploadQueue = new UploadQueueSettings();
            changed = true;
        }

        if (settings.ManagedPolicy is null)
        {
            settings.ManagedPolicy = new ManagedPolicySettings();
            changed = true;
        }

        if (settings.ManagedPolicy.AllowedShareDestinations is null)
        {
            settings.ManagedPolicy.AllowedShareDestinations = new List<string>();
            changed = true;
        }

        if (settings.ManagedPolicy.AllowedPluginIds is null)
        {
            settings.ManagedPolicy.AllowedPluginIds = new List<string>();
            changed = true;
        }

        if (settings.ManagedPolicy.AllowedPluginActionIds is null)
        {
            settings.ManagedPolicy.AllowedPluginActionIds = new List<string>();
            changed = true;
        }

        if (settings.UploadDenylistProcesses is null)
        {
            settings.UploadDenylistProcesses = new List<string>();
            changed = true;
        }

        if (settings.AiDenylistProcesses is null)
        {
            settings.AiDenylistProcesses = new List<string>();
            changed = true;
        }

        if (settings.WatchFolders is null)
        {
            settings.WatchFolders = new List<string>();
            changed = true;
        }

        if (settings.TrustedPluginIds is null)
        {
            settings.TrustedPluginIds = new List<string>();
            changed = true;
        }

        if (settings.EnabledPluginIds is null)
        {
            settings.EnabledPluginIds = new List<string>();
            changed = true;
        }

        if (settings.AllowedPluginActionIds is null)
        {
            settings.AllowedPluginActionIds = new List<string>();
            changed = true;
        }

        if (settings.AutomationRules is null)
        {
            settings.AutomationRules = new List<AutomationRule>();
            changed = true;
        }

        if (settings.HotkeyProfiles is null)
        {
            settings.HotkeyProfiles = new List<HotkeyWorkflowProfile>();
            changed = true;
        }

        if (settings.ProviderProfiles is null)
        {
            settings.ProviderProfiles = new List<ProviderWorkflowProfile>();
            changed = true;
        }

        if (settings.RecordingProfiles is null)
        {
            settings.RecordingProfiles = new List<RecordingWorkflowProfile>();
            changed = true;
        }

        if (settings.CaptureContextPadding < 0 || settings.CaptureContextPadding > CaptureOverlayGeometry.MaxContextPadding)
        {
            settings.CaptureContextPadding = Math.Clamp(settings.CaptureContextPadding, 0, CaptureOverlayGeometry.MaxContextPadding);
            changed = true;
        }

        foreach (var profile in RecordingProfileCatalog.CreateDefaultProfiles())
        {
            changed |= EnsureRecordingProfile(
                settings,
                profile.Name,
                profile.Description,
                profile.Settings);
        }

        changed |= EnsureRecordingOverlayDefaults(settings.Recording);
        foreach (var profile in settings.RecordingProfiles)
        {
            changed |= EnsureRecordingOverlayDefaults(profile.Settings);
        }

        if (string.IsNullOrWhiteSpace(settings.SlackMessageTemplate))
        {
            settings.SlackMessageTemplate = "GoatShot capture ready: {file} ({bytes} bytes)";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.DiscordMessageTemplate))
        {
            settings.DiscordMessageTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.TeamsMessageTemplate))
        {
            settings.TeamsMessageTemplate = "GoatShot capture ready: {file} ({bytes} bytes)";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GeminiSpeechToTextModelId))
        {
            settings.GeminiSpeechToTextModelId = "gemini-3.5-flash";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.WebDavRemoteDirectory))
        {
            settings.WebDavRemoteDirectory = "/";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.FtpRemoteDirectory))
        {
            settings.FtpRemoteDirectory = "/";
            changed = true;
        }
        if (settings.FtpPort <= 0)
        {
            settings.FtpPort = 21;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GitHubApiBaseUrl))
        {
            settings.GitHubApiBaseUrl = "https://api.github.com";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GitHubIssueTitleTemplate))
        {
            settings.GitHubIssueTitleTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.JiraIssueType))
        {
            settings.JiraIssueType = "Bug";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.JiraSummaryTemplate))
        {
            settings.JiraSummaryTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.AzureDevOpsBaseUrl))
        {
            settings.AzureDevOpsBaseUrl = "https://dev.azure.com";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.AzureDevOpsWorkItemType))
        {
            settings.AzureDevOpsWorkItemType = "Bug";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.AzureDevOpsTitleTemplate))
        {
            settings.AzureDevOpsTitleTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubeUploadApiBaseUrl))
        {
            settings.YouTubeUploadApiBaseUrl = "https://www.googleapis.com/upload/youtube/v3";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubeTitleTemplate))
        {
            settings.YouTubeTitleTemplate = "GoatShot recording: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubeDescriptionTemplate))
        {
            settings.YouTubeDescriptionTemplate = "Uploaded from GoatShot capture {id}.";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubePrivacyStatus))
        {
            settings.YouTubePrivacyStatus = "unlisted";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubeCategoryId))
        {
            settings.YouTubeCategoryId = "22";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.OneNoteGraphApiBaseUrl))
        {
            settings.OneNoteGraphApiBaseUrl = "https://graph.microsoft.com/v1.0";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.OneNotePageTitleTemplate))
        {
            settings.OneNotePageTitleTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GooglePhotosUploadApiBaseUrl))
        {
            settings.GooglePhotosUploadApiBaseUrl = "https://photoslibrary.googleapis.com/v1/uploads";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GooglePhotosApiBaseUrl))
        {
            settings.GooglePhotosApiBaseUrl = "https://photoslibrary.googleapis.com/v1";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.GooglePhotosDescriptionTemplate))
        {
            settings.GooglePhotosDescriptionTemplate = "GoatShot capture: {file}";
            changed = true;
        }

        changed |= EnsureOAuthProvider(settings.OAuth, "Google Drive", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/auth/drive.file");
        changed |= EnsureOAuthProvider(settings.OAuth, "Google Photos", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/auth/photoslibrary.appendonly");
        changed |= EnsureOAuthProvider(settings.OAuth, "OneDrive", "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", "Files.ReadWrite offline_access");
        changed |= EnsureOAuthProvider(settings.OAuth, "Dropbox", "https://www.dropbox.com/oauth2/authorize", "https://api.dropboxapi.com/oauth2/token", "files.content.write sharing.write");
        changed |= EnsureOAuthProvider(settings.OAuth, "YouTube", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/auth/youtube.upload");
        changed |= EnsureOAuthProvider(settings.OAuth, "OneNote", "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", "Notes.Create Files.Read offline_access");

        return new SettingsMigrationResult(changed);
    }

    private static bool EnsureOAuthProvider(
        OAuthSettings settings,
        string providerName,
        string authorizationEndpoint,
        string tokenEndpoint,
        string scopes)
    {
        if (settings.Providers.Any(provider => provider.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        settings.Providers.Add(new OAuthProviderSettings
        {
            ProviderName = providerName,
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = tokenEndpoint,
            Scopes = scopes
        });
        return true;
    }

    private static bool EnsureRecordingProfile(
        AppSettings settings,
        string name,
        string description,
        RecordingSettings recordingSettings)
    {
        if (settings.RecordingProfiles.Any(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        settings.RecordingProfiles.Add(new RecordingWorkflowProfile
        {
            Name = name,
            Description = description,
            Settings = recordingSettings
        });
        return true;
    }

    private static bool EnsureRecordingOverlayDefaults(RecordingSettings settings)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(settings.RecordingTimerPosition))
        {
            settings.RecordingTimerPosition = "TopLeft";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.KeystrokeOverlayPosition))
        {
            settings.KeystrokeOverlayPosition = "BottomLeft";
            changed = true;
        }

        if (settings.RecordingOverlayBadgeFontSize <= 0)
        {
            settings.RecordingOverlayBadgeFontSize = 16;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.RecordingOverlayStyle))
        {
            settings.RecordingOverlayStyle = "Neon";
            changed = true;
        }

        return changed;
    }
}

public sealed record SettingsMigrationResult(bool Changed);

using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WorkflowProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public WorkflowProfileService(AppSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
    }

    public async Task<WorkflowProfileOperationResult> ExportAsync(
        string path,
        string? name = null,
        bool includeSensitiveValues = false,
        CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var profile = CreateProfile(name, includeSensitiveValues);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);

        return new WorkflowProfileOperationResult
        {
            Succeeded = true,
            Path = path,
            Message = includeSensitiveValues
                ? "Workflow profile exported with custom script/webhook values included. DPAPI credentials were omitted."
                : "Workflow profile exported. Custom script/webhook values and DPAPI credentials were omitted."
        };
    }

    public async Task<WorkflowProfileOperationResult> ImportAsync(
        string path,
        WorkflowProfileImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new WorkflowProfileImportOptions();
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(path))
        {
            return new WorkflowProfileOperationResult
            {
                Succeeded = false,
                Path = path,
                Message = $"Workflow profile not found: {path}"
            };
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        WorkflowProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<WorkflowProfile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new WorkflowProfileOperationResult
            {
                Succeeded = false,
                Path = path,
                Message = $"Workflow profile JSON is invalid: {ex.Message}"
            };
        }

        if (profile is null)
        {
            return new WorkflowProfileOperationResult
            {
                Succeeded = false,
                Path = path,
                Message = "Workflow profile JSON did not contain a profile object."
            };
        }

        var validation = ValidateProfile(profile, path);
        if (!validation.Succeeded)
        {
            return new WorkflowProfileOperationResult
            {
                Succeeded = false,
                Path = path,
                Message = validation.Message + Environment.NewLine + string.Join(Environment.NewLine, validation.Errors)
            };
        }

        Apply(profile, options);
        _settingsStore.Save(_settings);

        return new WorkflowProfileOperationResult
        {
            Succeeded = true,
            Path = path,
            Message = options.IncludeSensitiveValues && profile.IncludesSensitiveValues
                ? $"Imported workflow profile '{profile.Name}' including custom script/webhook values. DPAPI credentials were not imported."
                : $"Imported workflow profile '{profile.Name}'. Custom script/webhook values and DPAPI credentials were not imported."
        };
    }

    public async Task<WorkflowProfileValidationResult> ValidateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(path))
        {
            return new WorkflowProfileValidationResult
            {
                Path = path,
                Errors = { $"Workflow profile not found: {path}" }
            };
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        try
        {
            var profile = JsonSerializer.Deserialize<WorkflowProfile>(json, JsonOptions);
            if (profile is null)
            {
                return new WorkflowProfileValidationResult
                {
                    Path = path,
                    Errors = { "Workflow profile JSON did not contain a profile object." }
                };
            }

            return ValidateProfile(profile, path);
        }
        catch (JsonException ex)
        {
            return new WorkflowProfileValidationResult
            {
                Path = path,
                Errors = { $"Workflow profile JSON is invalid: {ex.Message}" }
            };
        }
    }

    public WorkflowProfile CreateProfile(string? name = null, bool includeSensitiveValues = false)
    {
        return new WorkflowProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "GoatShot workflow profile" : name.Trim(),
            ExportedAt = DateTimeOffset.Now,
            IncludesSensitiveValues = includeSensitiveValues,
            Sharing = new WorkflowProfileSharing
            {
                DefaultShareDestination = _settings.DefaultShareDestination,
                ConfirmBeforeUpload = _settings.ConfirmBeforeUpload,
                LocalExportFolder = _settings.LocalExportFolder,
                S3Endpoint = _settings.S3Endpoint,
                S3Region = _settings.S3Region,
                S3Bucket = _settings.S3Bucket,
                S3KeyPrefix = _settings.S3KeyPrefix,
                S3PublicBaseUrl = _settings.S3PublicBaseUrl,
                ImgurApiEndpoint = _settings.ImgurApiEndpoint,
                SftpExecutablePath = _settings.SftpExecutablePath,
                SftpHost = _settings.SftpHost,
                SftpPort = _settings.SftpPort,
                SftpUsername = _settings.SftpUsername,
                SftpRemoteDirectory = _settings.SftpRemoteDirectory,
                SftpPrivateKeyPath = _settings.SftpPrivateKeyPath,
                SftpPublicBaseUrl = _settings.SftpPublicBaseUrl,
                WebDavBaseUrl = _settings.WebDavBaseUrl,
                WebDavRemoteDirectory = _settings.WebDavRemoteDirectory,
                WebDavUsername = _settings.WebDavUsername,
                WebDavPublicBaseUrl = _settings.WebDavPublicBaseUrl,
                FtpHost = _settings.FtpHost,
                FtpPort = _settings.FtpPort,
                FtpUseFtps = _settings.FtpUseFtps,
                FtpUsername = _settings.FtpUsername,
                FtpRemoteDirectory = _settings.FtpRemoteDirectory,
                FtpPublicBaseUrl = _settings.FtpPublicBaseUrl,
                CloudinaryApiBaseUrl = _settings.CloudinaryApiBaseUrl,
                CloudinaryCloudName = _settings.CloudinaryCloudName,
                CloudinaryResourceType = _settings.CloudinaryResourceType,
                CloudinaryUploadPreset = _settings.CloudinaryUploadPreset,
                CloudinaryFolder = _settings.CloudinaryFolder,
                DropboxContentApiBaseUrl = _settings.DropboxContentApiBaseUrl,
                DropboxApiBaseUrl = _settings.DropboxApiBaseUrl,
                DropboxRemoteFolder = _settings.DropboxRemoteFolder,
                GoogleDriveUploadApiBaseUrl = _settings.GoogleDriveUploadApiBaseUrl,
                GoogleDriveApiBaseUrl = _settings.GoogleDriveApiBaseUrl,
                GoogleDriveFolderId = _settings.GoogleDriveFolderId,
                GoogleDriveCreateAnyoneReaderLink = _settings.GoogleDriveCreateAnyoneReaderLink,
                GooglePhotosUploadApiBaseUrl = _settings.GooglePhotosUploadApiBaseUrl,
                GooglePhotosApiBaseUrl = _settings.GooglePhotosApiBaseUrl,
                GooglePhotosAlbumId = _settings.GooglePhotosAlbumId,
                GooglePhotosDescriptionTemplate = _settings.GooglePhotosDescriptionTemplate,
                OneDriveGraphApiBaseUrl = _settings.OneDriveGraphApiBaseUrl,
                OneDriveRemoteFolder = _settings.OneDriveRemoteFolder,
                OneDriveCreateAnonymousViewLink = _settings.OneDriveCreateAnonymousViewLink,
                YouTubeUploadApiBaseUrl = _settings.YouTubeUploadApiBaseUrl,
                YouTubeTitleTemplate = _settings.YouTubeTitleTemplate,
                YouTubeDescriptionTemplate = _settings.YouTubeDescriptionTemplate,
                YouTubePrivacyStatus = _settings.YouTubePrivacyStatus,
                YouTubeCategoryId = _settings.YouTubeCategoryId,
                OneNoteGraphApiBaseUrl = _settings.OneNoteGraphApiBaseUrl,
                OneNoteSectionId = _settings.OneNoteSectionId,
                OneNotePageTitleTemplate = _settings.OneNotePageTitleTemplate,
                LinearGraphqlEndpoint = _settings.LinearGraphqlEndpoint,
                LinearTeamId = _settings.LinearTeamId,
                LinearIssueId = _settings.LinearIssueId,
                LinearIssueTitleTemplate = _settings.LinearIssueTitleTemplate,
                LinearCreateAttachment = _settings.LinearCreateAttachment,
                LinearUseOAuthBearerToken = _settings.LinearUseOAuthBearerToken,
                GitHubApiBaseUrl = _settings.GitHubApiBaseUrl,
                GitHubRepository = _settings.GitHubRepository,
                GitHubIssueTitleTemplate = _settings.GitHubIssueTitleTemplate,
                GitHubLabels = _settings.GitHubLabels,
                GitHubAssignees = _settings.GitHubAssignees,
                JiraBaseUrl = _settings.JiraBaseUrl,
                JiraProjectKey = _settings.JiraProjectKey,
                JiraIssueType = _settings.JiraIssueType,
                JiraSummaryTemplate = _settings.JiraSummaryTemplate,
                JiraLabels = _settings.JiraLabels,
                JiraAccountEmail = _settings.JiraAccountEmail,
                AzureDevOpsBaseUrl = _settings.AzureDevOpsBaseUrl,
                AzureDevOpsOrganization = _settings.AzureDevOpsOrganization,
                AzureDevOpsProject = _settings.AzureDevOpsProject,
                AzureDevOpsWorkItemType = _settings.AzureDevOpsWorkItemType,
                AzureDevOpsTitleTemplate = _settings.AzureDevOpsTitleTemplate,
                AzureDevOpsTags = _settings.AzureDevOpsTags,
                AzureDevOpsAssignedTo = _settings.AzureDevOpsAssignedTo,
                UploadDenylistProcesses = _settings.UploadDenylistProcesses.ToList(),
                CustomScriptCommand = includeSensitiveValues ? EmptyToNull(_settings.CustomScriptCommand) : null,
                CustomWebhookUrl = includeSensitiveValues ? EmptyToNull(_settings.CustomWebhookUrl) : null,
                SlackWebhookUrl = includeSensitiveValues ? EmptyToNull(_settings.SlackWebhookUrl) : null,
                SlackMessageTemplate = _settings.SlackMessageTemplate,
                DiscordWebhookUrl = includeSensitiveValues ? EmptyToNull(_settings.DiscordWebhookUrl) : null,
                DiscordMessageTemplate = _settings.DiscordMessageTemplate,
                TeamsWebhookUrl = includeSensitiveValues ? EmptyToNull(_settings.TeamsWebhookUrl) : null,
                TeamsMessageTemplate = _settings.TeamsMessageTemplate
            },
            Recording = new WorkflowProfileRecording
            {
                DefaultSettings = CloneRecordingSettings(_settings.Recording),
                Profiles = _settings.RecordingProfiles
                    .Select(CloneRecordingProfile)
                    .ToList()
            },
            WatchFolders = new WorkflowProfileWatchFolders
            {
                EnableWatchFolders = _settings.EnableWatchFolders,
                IncludeSubdirectories = _settings.WatchFolderIncludeSubdirectories,
                AutoImport = _settings.WatchFolderAutoImport,
                CopyImageAfterImport = _settings.WatchFolderCopyImageAfterImport,
                StripMetadataAfterImport = _settings.WatchFolderStripMetadataAfterImport,
                ShareAfterImport = _settings.WatchFolderShareAfterImport,
                EnableVirtualPrinterImport = _settings.EnableVirtualPrinterImport,
                VirtualPrinterImportFolder = _settings.VirtualPrinterImportFolder,
                VirtualPrinterImportIncludeSubdirectories = _settings.VirtualPrinterImportIncludeSubdirectories,
                WatchFolders = _settings.WatchFolders.ToList()
            },
            AutomationRules = _settings.AutomationRules
                .Select(CloneRule)
                .ToList()
        };
    }

    private void Apply(WorkflowProfile profile, WorkflowProfileImportOptions options)
    {
        _settings.DefaultShareDestination = string.IsNullOrWhiteSpace(profile.Sharing.DefaultShareDestination)
            ? "Clipboard image"
            : profile.Sharing.DefaultShareDestination;
        _settings.ConfirmBeforeUpload = profile.Sharing.ConfirmBeforeUpload;
        _settings.LocalExportFolder = profile.Sharing.LocalExportFolder;
        _settings.S3Endpoint = profile.Sharing.S3Endpoint;
        _settings.S3Region = string.IsNullOrWhiteSpace(profile.Sharing.S3Region)
            ? "us-east-1"
            : profile.Sharing.S3Region;
        _settings.S3Bucket = profile.Sharing.S3Bucket;
        _settings.S3KeyPrefix = string.IsNullOrWhiteSpace(profile.Sharing.S3KeyPrefix)
            ? "goatshot/"
            : profile.Sharing.S3KeyPrefix;
        _settings.S3PublicBaseUrl = profile.Sharing.S3PublicBaseUrl;
        _settings.ImgurApiEndpoint = string.IsNullOrWhiteSpace(profile.Sharing.ImgurApiEndpoint)
            ? "https://api.imgur.com/3/image"
            : profile.Sharing.ImgurApiEndpoint;
        _settings.SftpExecutablePath = profile.Sharing.SftpExecutablePath;
        _settings.SftpHost = profile.Sharing.SftpHost;
        _settings.SftpPort = profile.Sharing.SftpPort <= 0 ? 22 : profile.Sharing.SftpPort;
        _settings.SftpUsername = profile.Sharing.SftpUsername;
        _settings.SftpRemoteDirectory = string.IsNullOrWhiteSpace(profile.Sharing.SftpRemoteDirectory)
            ? "/"
            : profile.Sharing.SftpRemoteDirectory;
        _settings.SftpPrivateKeyPath = profile.Sharing.SftpPrivateKeyPath;
        _settings.SftpPublicBaseUrl = profile.Sharing.SftpPublicBaseUrl;
        _settings.WebDavBaseUrl = profile.Sharing.WebDavBaseUrl;
        _settings.WebDavRemoteDirectory = string.IsNullOrWhiteSpace(profile.Sharing.WebDavRemoteDirectory)
            ? "/"
            : profile.Sharing.WebDavRemoteDirectory;
        _settings.WebDavUsername = profile.Sharing.WebDavUsername;
        _settings.WebDavPublicBaseUrl = profile.Sharing.WebDavPublicBaseUrl;
        _settings.FtpHost = profile.Sharing.FtpHost;
        _settings.FtpPort = profile.Sharing.FtpPort <= 0 ? 21 : profile.Sharing.FtpPort;
        _settings.FtpUseFtps = profile.Sharing.FtpUseFtps;
        _settings.FtpUsername = profile.Sharing.FtpUsername;
        _settings.FtpRemoteDirectory = string.IsNullOrWhiteSpace(profile.Sharing.FtpRemoteDirectory)
            ? "/"
            : profile.Sharing.FtpRemoteDirectory;
        _settings.FtpPublicBaseUrl = profile.Sharing.FtpPublicBaseUrl;
        _settings.CloudinaryApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.CloudinaryApiBaseUrl)
            ? "https://api.cloudinary.com"
            : profile.Sharing.CloudinaryApiBaseUrl;
        _settings.CloudinaryCloudName = profile.Sharing.CloudinaryCloudName;
        _settings.CloudinaryResourceType = string.IsNullOrWhiteSpace(profile.Sharing.CloudinaryResourceType)
            ? "auto"
            : profile.Sharing.CloudinaryResourceType;
        _settings.CloudinaryUploadPreset = profile.Sharing.CloudinaryUploadPreset;
        _settings.CloudinaryFolder = profile.Sharing.CloudinaryFolder;
        _settings.DropboxContentApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.DropboxContentApiBaseUrl)
            ? "https://content.dropboxapi.com"
            : profile.Sharing.DropboxContentApiBaseUrl;
        _settings.DropboxApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.DropboxApiBaseUrl)
            ? "https://api.dropboxapi.com"
            : profile.Sharing.DropboxApiBaseUrl;
        _settings.DropboxRemoteFolder = string.IsNullOrWhiteSpace(profile.Sharing.DropboxRemoteFolder)
            ? "/GoatShot"
            : profile.Sharing.DropboxRemoteFolder;
        _settings.GoogleDriveUploadApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.GoogleDriveUploadApiBaseUrl)
            ? "https://www.googleapis.com/upload/drive/v3"
            : profile.Sharing.GoogleDriveUploadApiBaseUrl;
        _settings.GoogleDriveApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.GoogleDriveApiBaseUrl)
            ? "https://www.googleapis.com/drive/v3"
            : profile.Sharing.GoogleDriveApiBaseUrl;
        _settings.GoogleDriveFolderId = profile.Sharing.GoogleDriveFolderId;
        _settings.GoogleDriveCreateAnyoneReaderLink = profile.Sharing.GoogleDriveCreateAnyoneReaderLink;
        _settings.GooglePhotosUploadApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.GooglePhotosUploadApiBaseUrl)
            ? "https://photoslibrary.googleapis.com/v1/uploads"
            : profile.Sharing.GooglePhotosUploadApiBaseUrl;
        _settings.GooglePhotosApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.GooglePhotosApiBaseUrl)
            ? "https://photoslibrary.googleapis.com/v1"
            : profile.Sharing.GooglePhotosApiBaseUrl;
        _settings.GooglePhotosAlbumId = profile.Sharing.GooglePhotosAlbumId;
        _settings.GooglePhotosDescriptionTemplate = string.IsNullOrWhiteSpace(profile.Sharing.GooglePhotosDescriptionTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.GooglePhotosDescriptionTemplate;
        _settings.OneDriveGraphApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.OneDriveGraphApiBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : profile.Sharing.OneDriveGraphApiBaseUrl;
        _settings.OneDriveRemoteFolder = string.IsNullOrWhiteSpace(profile.Sharing.OneDriveRemoteFolder)
            ? "/GoatShot"
            : profile.Sharing.OneDriveRemoteFolder;
        _settings.OneDriveCreateAnonymousViewLink = profile.Sharing.OneDriveCreateAnonymousViewLink;
        _settings.YouTubeUploadApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.YouTubeUploadApiBaseUrl)
            ? "https://www.googleapis.com/upload/youtube/v3"
            : profile.Sharing.YouTubeUploadApiBaseUrl;
        _settings.YouTubeTitleTemplate = string.IsNullOrWhiteSpace(profile.Sharing.YouTubeTitleTemplate)
            ? "GoatShot recording: {file}"
            : profile.Sharing.YouTubeTitleTemplate;
        _settings.YouTubeDescriptionTemplate = string.IsNullOrWhiteSpace(profile.Sharing.YouTubeDescriptionTemplate)
            ? "Uploaded from GoatShot capture {id}."
            : profile.Sharing.YouTubeDescriptionTemplate;
        _settings.YouTubePrivacyStatus = string.IsNullOrWhiteSpace(profile.Sharing.YouTubePrivacyStatus)
            ? "unlisted"
            : profile.Sharing.YouTubePrivacyStatus;
        _settings.YouTubeCategoryId = string.IsNullOrWhiteSpace(profile.Sharing.YouTubeCategoryId)
            ? "22"
            : profile.Sharing.YouTubeCategoryId;
        _settings.OneNoteGraphApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.OneNoteGraphApiBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : profile.Sharing.OneNoteGraphApiBaseUrl;
        _settings.OneNoteSectionId = profile.Sharing.OneNoteSectionId;
        _settings.OneNotePageTitleTemplate = string.IsNullOrWhiteSpace(profile.Sharing.OneNotePageTitleTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.OneNotePageTitleTemplate;
        _settings.LinearGraphqlEndpoint = string.IsNullOrWhiteSpace(profile.Sharing.LinearGraphqlEndpoint)
            ? "https://api.linear.app/graphql"
            : profile.Sharing.LinearGraphqlEndpoint;
        _settings.LinearTeamId = profile.Sharing.LinearTeamId;
        _settings.LinearIssueId = profile.Sharing.LinearIssueId;
        _settings.LinearIssueTitleTemplate = string.IsNullOrWhiteSpace(profile.Sharing.LinearIssueTitleTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.LinearIssueTitleTemplate;
        _settings.LinearCreateAttachment = profile.Sharing.LinearCreateAttachment;
        _settings.LinearUseOAuthBearerToken = profile.Sharing.LinearUseOAuthBearerToken;
        _settings.GitHubApiBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.GitHubApiBaseUrl)
            ? "https://api.github.com"
            : profile.Sharing.GitHubApiBaseUrl;
        _settings.GitHubRepository = profile.Sharing.GitHubRepository;
        _settings.GitHubIssueTitleTemplate = string.IsNullOrWhiteSpace(profile.Sharing.GitHubIssueTitleTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.GitHubIssueTitleTemplate;
        _settings.GitHubLabels = profile.Sharing.GitHubLabels;
        _settings.GitHubAssignees = profile.Sharing.GitHubAssignees;
        _settings.JiraBaseUrl = profile.Sharing.JiraBaseUrl;
        _settings.JiraProjectKey = profile.Sharing.JiraProjectKey;
        _settings.JiraIssueType = string.IsNullOrWhiteSpace(profile.Sharing.JiraIssueType)
            ? "Bug"
            : profile.Sharing.JiraIssueType;
        _settings.JiraSummaryTemplate = string.IsNullOrWhiteSpace(profile.Sharing.JiraSummaryTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.JiraSummaryTemplate;
        _settings.JiraLabels = profile.Sharing.JiraLabels;
        _settings.JiraAccountEmail = profile.Sharing.JiraAccountEmail;
        _settings.AzureDevOpsBaseUrl = string.IsNullOrWhiteSpace(profile.Sharing.AzureDevOpsBaseUrl)
            ? "https://dev.azure.com"
            : profile.Sharing.AzureDevOpsBaseUrl;
        _settings.AzureDevOpsOrganization = profile.Sharing.AzureDevOpsOrganization;
        _settings.AzureDevOpsProject = profile.Sharing.AzureDevOpsProject;
        _settings.AzureDevOpsWorkItemType = string.IsNullOrWhiteSpace(profile.Sharing.AzureDevOpsWorkItemType)
            ? "Bug"
            : profile.Sharing.AzureDevOpsWorkItemType;
        _settings.AzureDevOpsTitleTemplate = string.IsNullOrWhiteSpace(profile.Sharing.AzureDevOpsTitleTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.AzureDevOpsTitleTemplate;
        _settings.AzureDevOpsTags = profile.Sharing.AzureDevOpsTags;
        _settings.AzureDevOpsAssignedTo = profile.Sharing.AzureDevOpsAssignedTo;
        _settings.UploadDenylistProcesses = profile.Sharing.UploadDenylistProcesses
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.IncludeSensitiveValues && profile.IncludesSensitiveValues)
        {
            _settings.CustomScriptCommand = profile.Sharing.CustomScriptCommand ?? string.Empty;
            _settings.CustomWebhookUrl = profile.Sharing.CustomWebhookUrl ?? string.Empty;
            _settings.SlackWebhookUrl = profile.Sharing.SlackWebhookUrl ?? string.Empty;
            _settings.DiscordWebhookUrl = profile.Sharing.DiscordWebhookUrl ?? string.Empty;
            _settings.TeamsWebhookUrl = profile.Sharing.TeamsWebhookUrl ?? string.Empty;
        }

        _settings.SlackMessageTemplate = string.IsNullOrWhiteSpace(profile.Sharing.SlackMessageTemplate)
            ? "GoatShot capture ready: {file} ({bytes} bytes)"
            : profile.Sharing.SlackMessageTemplate;
        _settings.DiscordMessageTemplate = string.IsNullOrWhiteSpace(profile.Sharing.DiscordMessageTemplate)
            ? "GoatShot capture: {file}"
            : profile.Sharing.DiscordMessageTemplate;
        _settings.TeamsMessageTemplate = string.IsNullOrWhiteSpace(profile.Sharing.TeamsMessageTemplate)
            ? "GoatShot capture ready: {file} ({bytes} bytes)"
            : profile.Sharing.TeamsMessageTemplate;

        if (profile.Recording is not null)
        {
            _settings.Recording = CloneRecordingSettings(profile.Recording.DefaultSettings);
            _settings.RecordingProfiles = profile.Recording.Profiles
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(CloneRecordingProfile)
                .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
        }

        _settings.EnableWatchFolders = profile.WatchFolders.EnableWatchFolders;
        _settings.WatchFolderIncludeSubdirectories = profile.WatchFolders.IncludeSubdirectories;
        _settings.WatchFolderAutoImport = profile.WatchFolders.AutoImport;
        _settings.WatchFolderCopyImageAfterImport = profile.WatchFolders.CopyImageAfterImport;
        _settings.WatchFolderStripMetadataAfterImport = profile.WatchFolders.StripMetadataAfterImport;
        _settings.WatchFolderShareAfterImport = profile.WatchFolders.ShareAfterImport;
        _settings.EnableVirtualPrinterImport = profile.WatchFolders.EnableVirtualPrinterImport;
        _settings.VirtualPrinterImportFolder = profile.WatchFolders.VirtualPrinterImportFolder ?? string.Empty;
        _settings.VirtualPrinterImportIncludeSubdirectories = profile.WatchFolders.VirtualPrinterImportIncludeSubdirectories;
        _settings.WatchFolders = profile.WatchFolders.WatchFolders
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var importedRules = profile.AutomationRules.Select(CloneRule).ToList();
        if (options.ReplaceAutomationRules)
        {
            _settings.AutomationRules = importedRules;
        }
        else
        {
            foreach (var rule in importedRules)
            {
                _settings.AutomationRules.RemoveAll(existing => existing.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase));
                _settings.AutomationRules.Add(rule);
            }
        }
    }

    private static AutomationRule CloneRule(AutomationRule rule)
    {
        return new AutomationRule
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
            Name = rule.Name,
            IsEnabled = rule.IsEnabled,
            Trigger = rule.Trigger,
            SourceAppContains = rule.SourceAppContains,
            WindowTitleContains = rule.WindowTitleContains,
            CaptureKind = rule.CaptureKind,
            MonitorContains = rule.MonitorContains,
            HotkeyProfile = rule.HotkeyProfile,
            FileExtension = rule.FileExtension,
            MinFileSizeBytes = rule.MinFileSizeBytes,
            MaxFileSizeBytes = rule.MaxFileSizeBytes,
            OcrContains = rule.OcrContains,
            RequiresSensitiveData = rule.RequiresSensitiveData,
            ImageEffectMode = rule.ImageEffectMode,
            ImageEffectRegion = rule.ImageEffectRegion,
            Actions = rule.Actions.ToList(),
            ExtensionData = CloneExtensionData(rule.ExtensionData)
        };
    }

    private static WorkflowProfileValidationResult ValidateProfile(WorkflowProfile profile, string? path)
    {
        var result = new WorkflowProfileValidationResult
        {
            Path = path,
            ProfileName = profile.Name,
            SchemaVersion = profile.SchemaVersion,
            RuleCount = profile.AutomationRules.Count
        };

        if (profile.SchemaVersion != 1)
        {
            result.Errors.Add($"Unsupported schema version: {profile.SchemaVersion}. Expected 1.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            result.Warnings.Add("Profile name is empty.");
        }

        AddDuplicateWarnings(
            profile.AutomationRules.Select(rule => rule.Id),
            "rule id",
            result);
        AddDuplicateWarnings(
            profile.AutomationRules.Select(rule => rule.Name).Where(name => !string.IsNullOrWhiteSpace(name)),
            "rule name",
            result);

        for (var index = 0; index < profile.AutomationRules.Count; index++)
        {
            var rule = profile.AutomationRules[index];
            var label = string.IsNullOrWhiteSpace(rule.Name) ? $"rule #{index + 1}" : $"rule '{rule.Name}'";
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                result.Errors.Add($"{label} has an empty id.");
            }

            if (!Enum.IsDefined(rule.Trigger))
            {
                result.Errors.Add($"{label} has invalid trigger value: {rule.Trigger}.");
            }

            if (rule.Actions.Count == 0)
            {
                result.Errors.Add($"{label} has no actions.");
            }

            foreach (var action in rule.Actions)
            {
                if (!Enum.IsDefined(action))
                {
                    result.Errors.Add($"{label} has invalid action value: {action}.");
                }
            }

            if (rule.MinFileSizeBytes is { } minBytes && minBytes < 0)
            {
                result.Errors.Add($"{label} has a negative minimum file size.");
            }

            if (rule.MaxFileSizeBytes is { } maxBytes && maxBytes < 0)
            {
                result.Errors.Add($"{label} has a negative maximum file size.");
            }

            if (rule.MinFileSizeBytes is { } min && rule.MaxFileSizeBytes is { } max && min > max)
            {
                result.Errors.Add($"{label} has minimum file size greater than maximum file size.");
            }

            if (rule.Actions.Contains(AutomationActionKind.DeleteLocalFile))
            {
                result.Warnings.Add($"{label} includes DeleteLocalFile. Unattended automation records this as blocked; desktop task actions still require explicit confirmation.");
            }

            if (rule.Actions.Contains(AutomationActionKind.ShareDefaultDestination) &&
                rule.Actions.Contains(AutomationActionKind.DeleteLocalFile))
            {
                result.Warnings.Add($"{label} combines sharing and local delete. Verify the delete confirmation path before enabling.");
            }
        }

        if (!profile.IncludesSensitiveValues &&
            (!string.IsNullOrWhiteSpace(profile.Sharing.CustomScriptCommand) ||
             !string.IsNullOrWhiteSpace(profile.Sharing.CustomWebhookUrl) ||
             !string.IsNullOrWhiteSpace(profile.Sharing.SlackWebhookUrl) ||
             !string.IsNullOrWhiteSpace(profile.Sharing.DiscordWebhookUrl) ||
             !string.IsNullOrWhiteSpace(profile.Sharing.TeamsWebhookUrl)))
        {
            result.Warnings.Add("Profile says sensitive values were omitted but contains script/webhook URL fields.");
        }

        if (profile.IncludesSensitiveValues)
        {
            result.Warnings.Add("Profile includes custom script/webhook values; import only applies them with --include-sensitive. DPAPI credentials are still not imported.");
        }

        return result;
    }

    private static void AddDuplicateWarnings(
        IEnumerable<string> values,
        string label,
        WorkflowProfileValidationResult result)
    {
        foreach (var duplicate in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            result.Warnings.Add($"Duplicate {label}: {duplicate}");
        }
    }

    private static Dictionary<string, JsonElement>? CloneExtensionData(Dictionary<string, JsonElement>? source)
    {
        return source is null || source.Count == 0
            ? null
            : source.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static RecordingWorkflowProfile CloneRecordingProfile(RecordingWorkflowProfile profile)
    {
        return new RecordingWorkflowProfile
        {
            Name = profile.Name.Trim(),
            Description = profile.Description.Trim(),
            Settings = CloneRecordingSettings(profile.Settings)
        };
    }

    private static RecordingSettings CloneRecordingSettings(RecordingSettings? settings)
    {
        settings ??= new RecordingSettings();
        return new RecordingSettings
        {
            PreferProductionCaptureEngine = settings.PreferProductionCaptureEngine,
            PreferHevcEncoding = settings.PreferHevcEncoding,
            QualityProfile = settings.QualityProfile,
            FramesPerSecond = settings.FramesPerSecond,
            TargetWidth = settings.TargetWidth,
            TargetHeight = settings.TargetHeight,
            BitrateKbps = settings.BitrateKbps,
            UseVariableBitrate = settings.UseVariableBitrate,
            IncludeMicrophone = settings.IncludeMicrophone,
            IncludeSystemAudio = settings.IncludeSystemAudio,
            MicrophoneDeviceId = settings.MicrophoneDeviceId,
            SystemAudioDeviceId = settings.SystemAudioDeviceId,
            MicrophoneGain = settings.MicrophoneGain,
            SystemAudioGain = settings.SystemAudioGain,
            NoiseGateThresholdDb = settings.NoiseGateThresholdDb,
            MicrophoneMuted = settings.MicrophoneMuted,
            SystemAudioMuted = settings.SystemAudioMuted,
            EnableWebcamOverlay = settings.EnableWebcamOverlay,
            WebcamDeviceId = settings.WebcamDeviceId,
            WebcamOverlayPosition = settings.WebcamOverlayPosition,
            WebcamOverlayShape = settings.WebcamOverlayShape,
            MirrorWebcam = settings.MirrorWebcam,
            ShowCountdown = settings.ShowCountdown,
            ShowRecordingBorder = settings.ShowRecordingBorder,
            ShowRecordingTimer = settings.ShowRecordingTimer,
            ShowKeystrokeOverlay = settings.ShowKeystrokeOverlay,
            RecordingTimerPosition = settings.RecordingTimerPosition,
            KeystrokeOverlayPosition = settings.KeystrokeOverlayPosition,
            RecordingOverlayBadgeFontSize = settings.RecordingOverlayBadgeFontSize,
            RecordingOverlayStyle = settings.RecordingOverlayStyle
        };
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

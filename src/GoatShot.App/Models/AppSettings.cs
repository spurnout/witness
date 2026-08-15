namespace GoatShot.App.Models;

public sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; } = 17;
    public string LibraryRoot { get; set; } = string.Empty;
    public bool AutoCopyImageAfterCapture { get; set; } = true;
    public bool IncludeCursor { get; set; } = true;
    public int CaptureContextPadding { get; set; }
    public bool PrivateCaptureMode { get; set; }
    public bool RunAtStartup { get; set; }
    public bool ConfirmBeforeUpload { get; set; } = true;
    public bool AiEnabled { get; set; }
    public bool ConfirmBeforeAiRequest { get; set; } = true;
    public string GeminiModelManifestPath { get; set; } = string.Empty;
    public string GeminiApiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string GeminiDefaultModelId { get; set; } = "gemini-3.1-flash-image";
    public string GeminiSpeechToTextModelId { get; set; } = "gemini-3.5-flash";
    public string TranscriptionProvider { get; set; } = "external-whisper";
    public string ExternalWhisperExecutablePath { get; set; } = string.Empty;
    public string ExternalWhisperModelPath { get; set; } = string.Empty;
    public string ExternalWhisperLanguage { get; set; } = "en";
    public string DefaultShareDestination { get; set; } = "Clipboard image";
    public string FileNameTemplate { get; set; } = "{date}-{time}-{app}-{capture_type}-{counter}";
    public string LocalExportFolder { get; set; } = string.Empty;
    public string CustomScriptCommand { get; set; } = string.Empty;
    public string CustomWebhookUrl { get; set; } = string.Empty;
    public string SlackWebhookUrl { get; set; } = string.Empty;
    public string SlackMessageTemplate { get; set; } = "Receipts capture ready: {file} ({bytes} bytes)";
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public string DiscordMessageTemplate { get; set; } = "Receipts capture: {file}";
    public string TeamsWebhookUrl { get; set; } = string.Empty;
    public string TeamsMessageTemplate { get; set; } = "Receipts capture ready: {file} ({bytes} bytes)";
    public string S3Endpoint { get; set; } = string.Empty;
    public string S3Region { get; set; } = "us-east-1";
    public string S3Bucket { get; set; } = string.Empty;
    public string S3KeyPrefix { get; set; } = "receipts/";
    public string S3PublicBaseUrl { get; set; } = string.Empty;
    public string ImgurApiEndpoint { get; set; } = "https://api.imgur.com/3/image";
    public string SftpExecutablePath { get; set; } = string.Empty;
    public string SftpHost { get; set; } = string.Empty;
    public int SftpPort { get; set; } = 22;
    public string SftpUsername { get; set; } = string.Empty;
    public string SftpRemoteDirectory { get; set; } = "/";
    public string SftpPrivateKeyPath { get; set; } = string.Empty;
    public string SftpHostKeyFingerprint { get; set; } = string.Empty;
    public string SftpPublicBaseUrl { get; set; } = string.Empty;
    public string WebDavBaseUrl { get; set; } = string.Empty;
    public string WebDavRemoteDirectory { get; set; } = "/";
    public string WebDavUsername { get; set; } = string.Empty;
    public string WebDavPublicBaseUrl { get; set; } = string.Empty;
    public string FtpHost { get; set; } = string.Empty;
    public int FtpPort { get; set; } = 21;
    public bool FtpUseFtps { get; set; }
    public string FtpUsername { get; set; } = string.Empty;
    public string FtpRemoteDirectory { get; set; } = "/";
    public string FtpPublicBaseUrl { get; set; } = string.Empty;
    public string CloudinaryApiBaseUrl { get; set; } = "https://api.cloudinary.com";
    public string CloudinaryCloudName { get; set; } = string.Empty;
    public string CloudinaryResourceType { get; set; } = "auto";
    public string CloudinaryUploadPreset { get; set; } = string.Empty;
    public string CloudinaryFolder { get; set; } = string.Empty;
    public string DropboxContentApiBaseUrl { get; set; } = "https://content.dropboxapi.com";
    public string DropboxApiBaseUrl { get; set; } = "https://api.dropboxapi.com";
    public string DropboxRemoteFolder { get; set; } = "/Receipts";
    public string GoogleDriveUploadApiBaseUrl { get; set; } = "https://www.googleapis.com/upload/drive/v3";
    public string GoogleDriveApiBaseUrl { get; set; } = "https://www.googleapis.com/drive/v3";
    public string GoogleDriveFolderId { get; set; } = string.Empty;
    public bool GoogleDriveCreateAnyoneReaderLink { get; set; }
    public string GooglePhotosUploadApiBaseUrl { get; set; } = "https://photoslibrary.googleapis.com/v1/uploads";
    public string GooglePhotosApiBaseUrl { get; set; } = "https://photoslibrary.googleapis.com/v1";
    public string GooglePhotosAlbumId { get; set; } = string.Empty;
    public string GooglePhotosDescriptionTemplate { get; set; } = "Receipts capture: {file}";
    public string OneDriveGraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string OneDriveRemoteFolder { get; set; } = "/Receipts";
    public bool OneDriveCreateAnonymousViewLink { get; set; }
    public string YouTubeUploadApiBaseUrl { get; set; } = "https://www.googleapis.com/upload/youtube/v3";
    public string YouTubeTitleTemplate { get; set; } = "Receipts recording: {file}";
    public string YouTubeDescriptionTemplate { get; set; } = "Uploaded from Receipts capture {id}.";
    public string YouTubePrivacyStatus { get; set; } = "unlisted";
    public string YouTubeCategoryId { get; set; } = "22";
    public string OneNoteGraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string OneNoteSectionId { get; set; } = string.Empty;
    public string OneNotePageTitleTemplate { get; set; } = "Receipts capture: {file}";
    public string LinearGraphqlEndpoint { get; set; } = "https://api.linear.app/graphql";
    public string LinearTeamId { get; set; } = string.Empty;
    public string LinearIssueId { get; set; } = string.Empty;
    public string LinearIssueTitleTemplate { get; set; } = "Receipts capture: {file}";
    public bool LinearCreateAttachment { get; set; } = true;
    public bool LinearUseOAuthBearerToken { get; set; }
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
    public string GitHubRepository { get; set; } = string.Empty;
    public string GitHubIssueTitleTemplate { get; set; } = "Receipts capture: {file}";
    public string GitHubLabels { get; set; } = string.Empty;
    public string GitHubAssignees { get; set; } = string.Empty;
    public string JiraBaseUrl { get; set; } = string.Empty;
    public string JiraProjectKey { get; set; } = string.Empty;
    public string JiraIssueType { get; set; } = "Bug";
    public string JiraSummaryTemplate { get; set; } = "Receipts capture: {file}";
    public string JiraLabels { get; set; } = string.Empty;
    public string JiraAccountEmail { get; set; } = string.Empty;
    public string AzureDevOpsBaseUrl { get; set; } = "https://dev.azure.com";
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string AzureDevOpsProject { get; set; } = string.Empty;
    public string AzureDevOpsWorkItemType { get; set; } = "Bug";
    public string AzureDevOpsTitleTemplate { get; set; } = "Receipts capture: {file}";
    public string AzureDevOpsTags { get; set; } = string.Empty;
    public string AzureDevOpsAssignedTo { get; set; } = string.Empty;
    public string OcrLanguageTag { get; set; } = string.Empty;
    public RecordingSettings Recording { get; set; } = new();
    public ReplayBufferSettings Replay { get; set; } = new();
    public OAuthSettings OAuth { get; set; } = new();
    public UploadQueueSettings UploadQueue { get; set; } = new();
    public ManagedPolicySettings ManagedPolicy { get; set; } = new();
    public bool EnableWatchFolders { get; set; }
    public bool WatchFolderIncludeSubdirectories { get; set; }
    public bool WatchFolderAutoImport { get; set; } = true;
    public bool WatchFolderCopyImageAfterImport { get; set; }
    public bool WatchFolderStripMetadataAfterImport { get; set; }
    public bool WatchFolderShareAfterImport { get; set; }
    public bool EnableVirtualPrinterImport { get; set; }
    public string VirtualPrinterImportFolder { get; set; } = string.Empty;
    public bool VirtualPrinterImportIncludeSubdirectories { get; set; }
    public bool EnableLocalPlugins { get; set; }
    public List<string> UploadDenylistProcesses { get; set; } = new();
    public List<string> AiDenylistProcesses { get; set; } = new();
    public List<string> WatchFolders { get; set; } = new();
    public List<string> TrustedPluginIds { get; set; } = new();
    public List<string> EnabledPluginIds { get; set; } = new();
    public List<string> AllowedPluginActionIds { get; set; } = new();
    public List<AutomationRule> AutomationRules { get; set; } = new();
    public List<KeybindAssignment> Keybinds { get; set; } = new();
    public List<HotkeyWorkflowProfile> HotkeyProfiles { get; set; } = new();
    public List<RecordingWorkflowProfile> RecordingProfiles { get; set; } = new();
    public List<ProviderWorkflowProfile> ProviderProfiles { get; set; } = new();
}

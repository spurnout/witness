namespace GoatShot.App.Models;

public sealed class WorkflowProfileSharing
{
    public string DefaultShareDestination { get; set; } = "Clipboard image";
    public bool ConfirmBeforeUpload { get; set; } = true;
    public string LocalExportFolder { get; set; } = string.Empty;
    public string S3Endpoint { get; set; } = string.Empty;
    public string S3Region { get; set; } = "us-east-1";
    public string S3Bucket { get; set; } = string.Empty;
    public string S3KeyPrefix { get; set; } = string.Empty;
    public string S3PublicBaseUrl { get; set; } = string.Empty;
    public string ImgurApiEndpoint { get; set; } = "https://api.imgur.com/3/image";
    public string SftpExecutablePath { get; set; } = string.Empty;
    public string SftpHost { get; set; } = string.Empty;
    public int SftpPort { get; set; } = 22;
    public string SftpUsername { get; set; } = string.Empty;
    public string SftpRemoteDirectory { get; set; } = "/";
    public string SftpPrivateKeyPath { get; set; } = string.Empty;
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
    public string DropboxRemoteFolder { get; set; } = "/GoatShot";
    public string GoogleDriveUploadApiBaseUrl { get; set; } = "https://www.googleapis.com/upload/drive/v3";
    public string GoogleDriveApiBaseUrl { get; set; } = "https://www.googleapis.com/drive/v3";
    public string GoogleDriveFolderId { get; set; } = string.Empty;
    public bool GoogleDriveCreateAnyoneReaderLink { get; set; }
    public string GooglePhotosUploadApiBaseUrl { get; set; } = "https://photoslibrary.googleapis.com/v1/uploads";
    public string GooglePhotosApiBaseUrl { get; set; } = "https://photoslibrary.googleapis.com/v1";
    public string GooglePhotosAlbumId { get; set; } = string.Empty;
    public string GooglePhotosDescriptionTemplate { get; set; } = "GoatShot capture: {file}";
    public string OneDriveGraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string OneDriveRemoteFolder { get; set; } = "/GoatShot";
    public bool OneDriveCreateAnonymousViewLink { get; set; }
    public string YouTubeUploadApiBaseUrl { get; set; } = "https://www.googleapis.com/upload/youtube/v3";
    public string YouTubeTitleTemplate { get; set; } = "GoatShot recording: {file}";
    public string YouTubeDescriptionTemplate { get; set; } = "Uploaded from GoatShot capture {id}.";
    public string YouTubePrivacyStatus { get; set; } = "unlisted";
    public string YouTubeCategoryId { get; set; } = "22";
    public string OneNoteGraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";
    public string OneNoteSectionId { get; set; } = string.Empty;
    public string OneNotePageTitleTemplate { get; set; } = "GoatShot capture: {file}";
    public string LinearGraphqlEndpoint { get; set; } = "https://api.linear.app/graphql";
    public string LinearTeamId { get; set; } = string.Empty;
    public string LinearIssueId { get; set; } = string.Empty;
    public string LinearIssueTitleTemplate { get; set; } = "GoatShot capture: {file}";
    public bool LinearCreateAttachment { get; set; } = true;
    public bool LinearUseOAuthBearerToken { get; set; }
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
    public string GitHubRepository { get; set; } = string.Empty;
    public string GitHubIssueTitleTemplate { get; set; } = "GoatShot capture: {file}";
    public string GitHubLabels { get; set; } = string.Empty;
    public string GitHubAssignees { get; set; } = string.Empty;
    public string JiraBaseUrl { get; set; } = string.Empty;
    public string JiraProjectKey { get; set; } = string.Empty;
    public string JiraIssueType { get; set; } = "Bug";
    public string JiraSummaryTemplate { get; set; } = "GoatShot capture: {file}";
    public string JiraLabels { get; set; } = string.Empty;
    public string JiraAccountEmail { get; set; } = string.Empty;
    public string AzureDevOpsBaseUrl { get; set; } = "https://dev.azure.com";
    public string AzureDevOpsOrganization { get; set; } = string.Empty;
    public string AzureDevOpsProject { get; set; } = string.Empty;
    public string AzureDevOpsWorkItemType { get; set; } = "Bug";
    public string AzureDevOpsTitleTemplate { get; set; } = "GoatShot capture: {file}";
    public string AzureDevOpsTags { get; set; } = string.Empty;
    public string AzureDevOpsAssignedTo { get; set; } = string.Empty;
    public List<string> UploadDenylistProcesses { get; set; } = new();
    public string? CustomScriptCommand { get; set; }
    public string? CustomWebhookUrl { get; set; }
    public string? SlackWebhookUrl { get; set; }
    public string SlackMessageTemplate { get; set; } = "GoatShot capture ready: {file} ({bytes} bytes)";
    public string? DiscordWebhookUrl { get; set; }
    public string DiscordMessageTemplate { get; set; } = "GoatShot capture: {file}";
    public string? TeamsWebhookUrl { get; set; }
    public string TeamsMessageTemplate { get; set; } = "GoatShot capture ready: {file} ({bytes} bytes)";
}

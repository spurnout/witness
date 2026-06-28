using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ShareProviderCatalog
{
    public static IReadOnlyList<IShareProvider> CreateDefault(AppSettings? settings = null, AppPaths? paths = null)
    {
        var effectiveSettings = settings ?? new AppSettings();
        return new IShareProvider[]
        {
            new LocalFolderShareProvider(effectiveSettings),
            new ClipboardShareProvider(ShareDestination.ClipboardImage),
            new ClipboardShareProvider(ShareDestination.ClipboardFile),
            new ClipboardShareProvider(ShareDestination.ClipboardPath),
            new ClipboardShareProvider(ShareDestination.MarkdownImageLink),
            new EmailAttachmentShareProvider(),
            Existing(ShareDestination.S3Compatible, "S3-compatible", "Access key", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.Sftp, "SFTP", "SSH key", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.Imgur, "Imgur", "Client ID", publicLinks: true, privateLinks: false),
            Existing(ShareDestination.Cloudinary, "Cloudinary", "Upload preset or API key", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.Dropbox, "Dropbox", "OAuth", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.GoogleDrive, "Google Drive", "OAuth", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.GooglePhotos, "Google Photos", "OAuth", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.OneDrive, "OneDrive", "OAuth", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.YouTube, "YouTube", "OAuth", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.OneNote, "OneNote", "OAuth", publicLinks: false, privateLinks: true),
            Existing(ShareDestination.Linear, "Linear", "API key or OAuth", publicLinks: false, privateLinks: true),
            Existing(ShareDestination.GitHubIssues, "GitHub Issues", "Personal access token", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.Jira, "Jira", "API token", publicLinks: false, privateLinks: true),
            new SlackWebhookShareProvider(effectiveSettings),
            new DiscordWebhookShareProvider(effectiveSettings),
            new MicrosoftTeamsWebhookShareProvider(effectiveSettings),
            Existing(ShareDestination.WebDav, "WebDAV", "Basic", publicLinks: true, privateLinks: true),
            Existing(ShareDestination.AzureDevOps, "Azure DevOps", "Personal access token", publicLinks: false, privateLinks: true),
            Existing(ShareDestination.FtpFtps, "FTP/FTPS", "Password", publicLinks: true, privateLinks: true),
            paths is null
                ? Existing(ShareDestination.CustomScript, "Custom script", "Local command", publicLinks: true, privateLinks: true)
                : new CustomScriptShareProvider(paths, effectiveSettings),
            new CustomWebhookShareProvider(effectiveSettings)
        };
    }

    public static IReadOnlyList<IShareProvider> CreateExecutable(AppPaths paths, AppSettings settings, SecretStore secretStore)
    {
        return new IShareProvider[]
        {
            new LocalFolderShareProvider(settings),
            new ClipboardShareProvider(ShareDestination.ClipboardImage),
            new ClipboardShareProvider(ShareDestination.ClipboardFile),
            new ClipboardShareProvider(ShareDestination.ClipboardPath),
            new ClipboardShareProvider(ShareDestination.MarkdownImageLink),
            new EmailAttachmentShareProvider(),
            new CustomScriptShareProvider(paths, settings),
            new CustomWebhookShareProvider(settings),
            new S3CompatibleShareProvider(settings, secretStore),
            new ImgurShareProvider(settings, secretStore),
            new CloudinaryShareProvider(settings, secretStore),
            new SftpShareProvider(paths, settings),
            new LinearShareProvider(settings, secretStore),
            new GitHubIssuesShareProvider(settings, secretStore),
            new JiraShareProvider(settings, secretStore),
            new AzureDevOpsShareProvider(settings, secretStore),
            new GooglePhotosShareProvider(settings, secretStore),
            new YouTubeShareProvider(settings, secretStore),
            new OneNoteShareProvider(settings, secretStore),
            new WebDavShareProvider(settings, secretStore),
            new SlackWebhookShareProvider(settings),
            new DiscordWebhookShareProvider(settings),
            new MicrosoftTeamsWebhookShareProvider(settings),
            new FtpFtpsShareProvider(settings, secretStore)
        };
    }

    private static CatalogShareProvider Existing(
        ShareDestination destination,
        string name,
        string authType,
        bool publicLinks,
        bool privateLinks)
    {
        return new CatalogShareProvider(destination, name, authType, publicLinks, privateLinks, isImplemented: true);
    }

    private static CatalogShareProvider Roadmap(string name, string authType, bool publicLinks, bool privateLinks)
    {
        return new CatalogShareProvider(null, name, authType, publicLinks, privateLinks, isImplemented: false);
    }

    private sealed class CatalogShareProvider : IShareProvider
    {
        private readonly bool _isImplemented;

        public CatalogShareProvider(
            ShareDestination? destination,
            string providerName,
            string authType,
            bool publicLinks,
            bool privateLinks,
            bool isImplemented)
        {
            Destination = destination;
            ProviderName = providerName;
            AuthType = authType;
            SupportsPublicLinks = publicLinks;
            SupportsPrivateLinks = privateLinks;
            _isImplemented = isImplemented;
        }

        public ShareDestination? Destination { get; }
        public string ProviderName { get; }
        public string AuthType { get; }
        public bool IsImplemented => _isImplemented;
        public bool SupportsPublicLinks { get; }
        public bool SupportsPrivateLinks { get; }
        public bool SupportsExpiration => !_isImplemented;
        public bool SupportsPassword => !_isImplemented;

        public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
        {
            var message = _isImplemented
                ? $"{ProviderName} is implemented in GoatShot and tracked in the provider catalog."
                : $"{ProviderName} is a roadmap provider; adapter implementation is pending.";

            return Task.FromResult(new ProviderHealth(_isImplemented, message));
        }

        public Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ShareUploadResult(
                false,
                null,
                $"{ProviderName} catalog adapter does not execute uploads yet; use ShareService until provider extraction is complete."));
        }
    }
}

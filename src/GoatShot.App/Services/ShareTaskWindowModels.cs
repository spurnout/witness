using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record UploadConfirmationViewModel(
    string Title,
    string Destination,
    string FileName,
    string FilePath,
    string CaptureType,
    string Size,
    string RiskSummary,
    string PrivacySummary);

public sealed record UploadResultViewModel(
    string Title,
    string Destination,
    string FileName,
    bool Succeeded,
    string Message,
    string? Url,
    string? MarkdownLink,
    string? QrCodePath,
    bool CanOpenLink,
    bool CanRetry,
    bool CanDeleteRemote);

public static class ShareTaskWindowModels
{
    public static bool ShouldConfirm(AppSettings settings, ShareDestination destination)
    {
        return settings.ConfirmBeforeUpload && ShareService.IsExternalDestination(destination);
    }

    public static UploadConfirmationViewModel BuildConfirmation(CaptureItem item, ShareDestination destination)
    {
        return new UploadConfirmationViewModel(
            "Confirm share",
            DestinationLabel(destination),
            item.FileName,
            item.FilePath,
            item.Kind.ToString(),
            FormatBytes(item.Bytes),
            RiskSummary(destination),
            PrivacySummary(item));
    }

    public static UploadResultViewModel BuildResult(
        CaptureItem item,
        ShareDestination destination,
        ShareResult result,
        string? qrCodePath = null)
    {
        var url = string.IsNullOrWhiteSpace(result.Url) ? null : result.Url.Trim();
        return new UploadResultViewModel(
            result.Succeeded ? "Share complete" : "Share failed",
            DestinationLabel(destination),
            item.FileName,
            result.Succeeded,
            result.Message,
            url,
            url is null ? null : $"[{EscapeMarkdownLinkText(item.FileName)}]({url})",
            qrCodePath,
            IsOpenableWebUrl(url),
            !result.Succeeded,
            false);
    }

    private static string DestinationLabel(ShareDestination destination)
    {
        return destination switch
        {
            ShareDestination.S3Compatible => "S3-compatible",
            ShareDestination.GooglePhotos => "Google Photos",
            ShareDestination.GitHubIssues => "GitHub Issues",
            ShareDestination.AzureDevOps => "Azure DevOps",
            ShareDestination.SlackWebhook => "Slack",
            ShareDestination.DiscordWebhook => "Discord",
            ShareDestination.MicrosoftTeamsWebhook => "Microsoft Teams",
            ShareDestination.WebDav => "WebDAV",
            ShareDestination.FtpFtps => "FTP/FTPS",
            ShareDestination.CustomScript => "Custom script",
            ShareDestination.CustomWebhook => "Custom webhook",
            ShareDestination.LocalFolder => "Local folder",
            ShareDestination.ClipboardImage => "Clipboard image",
            ShareDestination.ClipboardFile => "Clipboard file",
            ShareDestination.ClipboardPath => "Clipboard path",
            ShareDestination.MarkdownImageLink => "Markdown image link",
            ShareDestination.EmailAttachment => "Email attachment",
            _ => destination.ToString()
        };
    }

    private static string RiskSummary(ShareDestination destination)
    {
        return destination switch
        {
            ShareDestination.CustomScript => "Runs a configured local PowerShell command. Review the command and output location before continuing.",
            ShareDestination.EmailAttachment => "Opens the default mail client and places the capture path into the message body.",
            ShareDestination.SlackWebhook or ShareDestination.MicrosoftTeamsWebhook => "Sends capture metadata to the configured incoming webhook; local media is not uploaded by this destination.",
            ShareDestination.CustomWebhook => "Posts the capture file and metadata to the configured webhook endpoint.",
            ShareDestination.DiscordWebhook => "Uploads the capture file to the configured Discord webhook.",
            ShareDestination.WebDav or ShareDestination.FtpFtps or ShareDestination.Sftp => "Uploads the capture file to the configured remote server.",
            ShareDestination.GitHubIssues or ShareDestination.Jira or ShareDestination.AzureDevOps or ShareDestination.Linear => "Creates or updates a work-tracking item using configured provider credentials.",
            ShareDestination.S3Compatible or ShareDestination.Imgur or ShareDestination.Cloudinary or ShareDestination.Dropbox or ShareDestination.GoogleDrive or ShareDestination.GooglePhotos or ShareDestination.OneDrive or ShareDestination.YouTube or ShareDestination.OneNote => "Uploads the capture file to the configured provider account.",
            _ => "Shares this capture using the selected destination."
        };
    }

    private static string PrivacySummary(CaptureItem item)
    {
        var notes = new List<string>();
        if (item.IsPrivate)
        {
            notes.Add("Marked private");
        }

        if (!string.IsNullOrWhiteSpace(item.OcrText))
        {
            notes.Add("Contains OCR text");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceApp))
        {
            notes.Add($"Source app: {item.SourceApp}");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceWindowTitle))
        {
            notes.Add("Window title captured");
        }

        return notes.Count == 0 ? "No privacy markers stored for this capture." : string.Join("; ", notes);
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024 * 1024
            ? $"{bytes / 1024d:0.#} KB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }

    private static string EscapeMarkdownLinkText(string value)
    {
        return value.Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static bool IsOpenableWebUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}

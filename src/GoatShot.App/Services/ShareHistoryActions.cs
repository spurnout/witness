using System.Diagnostics;
using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ShareHistoryActionModel
{
    public ShareHistoryActionModel(ShareHistoryEntry entry)
    {
        Entry = entry;
    }

    public ShareHistoryEntry Entry { get; }
    public string Id => Entry.Id;
    public string ShortId => Entry.Id[..Math.Min(12, Entry.Id.Length)];
    public string CreatedText => Entry.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    public string StatusText => Entry.Succeeded ? "Succeeded" : "Failed";
    public string DestinationText => Entry.Destination.ToString();
    public string ScopeText => Entry.ExternalDestination ? "External" : "Local";
    public string FileName => Entry.FileName;
    public string Message => Entry.Message;
    public string UrlText => Entry.Url ?? string.Empty;
    public string MarkdownLink => ShareHistoryActions.BuildMarkdownLink(Entry) ?? string.Empty;
    public bool CanCopyUrl => !string.IsNullOrWhiteSpace(Entry.Url);
    public bool CanOpenUrl => ShareHistoryActions.IsOpenableWebUrl(Entry.Url);
    public bool CanCopyMarkdown => !string.IsNullOrWhiteSpace(MarkdownLink);
    public bool CanRetry => !Entry.Succeeded && File.Exists(Entry.FilePath);
    public string RetryText => CanRetry
        ? "Queue retry"
        : Entry.Succeeded
            ? "Already succeeded"
            : "Source file missing";
}

public static class ShareHistoryActions
{
    public static IReadOnlyList<ShareHistoryActionModel> BuildModels(IEnumerable<ShareHistoryEntry> entries)
    {
        return entries.Select(entry => new ShareHistoryActionModel(entry)).ToList();
    }

    public static ShareHistoryEntry? FindEntry(IEnumerable<ShareHistoryEntry> entries, string id)
    {
        return entries.FirstOrDefault(entry =>
            entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            entry.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
    }

    public static string? BuildMarkdownLink(ShareHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(entry.FileName)
            ? "Receipts upload"
            : Path.GetFileNameWithoutExtension(entry.FileName);
        var escapedLabel = label.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        return $"![{escapedLabel}]({entry.Url})";
    }

    public static bool IsOpenableWebUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    public static CaptureItem ToRetryCaptureItem(ShareHistoryEntry entry)
    {
        var bytes = entry.Bytes;
        if (bytes <= 0 && File.Exists(entry.FilePath))
        {
            bytes = new FileInfo(entry.FilePath).Length;
        }

        return new CaptureItem
        {
            Id = string.IsNullOrWhiteSpace(entry.CaptureItemId) ? Guid.NewGuid().ToString("N") : entry.CaptureItemId,
            Kind = CaptureKind.Imported,
            CreatedAt = entry.CreatedAt,
            FilePath = entry.FilePath,
            ThumbnailPath = entry.FilePath,
            Bytes = bytes,
            Notes = $"Retry from share history {entry.Id}"
        };
    }

    public static void CopyUrl(ShareHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
        {
            return;
        }

        ClipboardInterop.SetText(entry.Url);
    }

    public static void CopyMarkdown(ShareHistoryEntry entry)
    {
        var markdown = BuildMarkdownLink(entry);
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            ClipboardInterop.SetText(markdown);
        }
    }

    public static void OpenUrl(ShareHistoryEntry entry)
    {
        if (IsOpenableWebUrl(entry.Url))
        {
            Process.Start(new ProcessStartInfo(entry.Url!) { UseShellExecute = true });
        }
    }
}

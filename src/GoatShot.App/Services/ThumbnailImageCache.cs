using System.Windows.Media.Imaging;

namespace GoatShot.App.Services;

/// <summary>
/// Decodes small, frozen thumbnails once and shares them between the library filmstrip and the
/// capture gallery. Entries are keyed by path, size, and last write time so a regenerated
/// thumbnail at the same path is picked up, and the cache is bounded so it never grows with
/// the library.
/// </summary>
public static class ThumbnailImageCache
{
    private const int Capacity = 400;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, BitmapSource Image)> Recency = new();

    /// <summary>Returns the image, or null when the file is missing or cannot be decoded.</summary>
    public static BitmapSource? TryLoad(string? path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        var key = $"{info.FullName}|{decodePixelWidth}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var hit))
            {
                Recency.Remove(hit);
                Recency.AddFirst(hit);
                return hit.Value.Image;
            }
        }

        BitmapSource image;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(info.FullName, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception ex) when (CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(ex))
        {
            // A stale or damaged thumbnail must not prevent capture feedback or history access.
            return null;
        }

        lock (Gate)
        {
            if (!Entries.ContainsKey(key))
            {
                Entries[key] = Recency.AddFirst((key, image));
                while (Entries.Count > Capacity && Recency.Last is { } oldest)
                {
                    Recency.RemoveLast();
                    Entries.Remove(oldest.Value.Key);
                }
            }
        }

        return image;
    }
}

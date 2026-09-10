using System.ComponentModel;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class CaptureGalleryEntry(CaptureItem item, bool isLatest) : INotifyPropertyChanged
{
    private bool _isSelected = isLatest;
    public CaptureItem Item { get; } = item;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class CaptureGalleryModels
{
    public static IReadOnlyList<CaptureGalleryEntry> BuildItems(
        IEnumerable<CaptureItem> captures,
        CaptureItem latest)
    {
        // Private captures are temporary and must never become history in this surface either.
        var history = latest.IsPrivate
            ? Enumerable.Empty<CaptureItem>()
            : captures.Where(item => !item.IsPrivate).OrderByDescending(item => item.CreatedAt);

        return history.Prepend(latest)
            .Where(item => Path.GetExtension(item.FilePath).ToLowerInvariant()
                is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
            .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CaptureGalleryEntry(item, item.Id.Equals(latest.Id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}

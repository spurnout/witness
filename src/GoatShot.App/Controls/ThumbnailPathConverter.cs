using System.Globalization;
using System.Windows.Data;
using GoatShot.App.Services;

namespace GoatShot.App.Controls;

/// <summary>
/// Binds a thumbnail path to a small decoded image. Unlike WPF's default string-to-image
/// conversion this decodes at display size, does not hold the file open, and reuses
/// <see cref="ThumbnailImageCache"/> across list refreshes.
/// </summary>
public sealed class ThumbnailPathConverter : IValueConverter
{
    public int DecodePixelWidth { get; set; } = 292;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThumbnailImageCache.TryLoad(value as string, DecodePixelWidth);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GoatShot.App.Services;

/// <summary>
/// Reads single pixels out of the frozen overlay screenshot. Colors come from the frozen frame
/// rather than the live screen so the readout always matches what the lens is showing.
/// </summary>
public static class CaptureOverlayColorReader
{
    public static string? TryReadHex(BitmapSource source, int pixelX, int pixelY)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (pixelX < 0 || pixelY < 0 || pixelX >= source.PixelWidth || pixelY >= source.PixelHeight)
        {
            return null;
        }

        BitmapSource probe = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        probe.CopyPixels(new Int32Rect(pixelX, pixelY, 1, 1), pixel, 4, 0);
        return $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}";
    }
}

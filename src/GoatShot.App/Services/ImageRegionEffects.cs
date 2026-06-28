using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ImageRegionEffects
{
    private const int PixelBlockSize = 14;
    private const int BlurScaleDivisor = 18;

    public static void Apply(Bitmap bitmap, IEnumerable<RectangleF> regions, VisualRedactionMode mode)
    {
        foreach (var region in regions)
        {
            Apply(bitmap, region, mode);
        }
    }

    public static void Apply(Bitmap bitmap, RectangleF region, VisualRedactionMode mode)
    {
        var rect = Clamp(region, bitmap.Width, bitmap.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        switch (mode)
        {
            case VisualRedactionMode.Pixelate:
                Pixelate(bitmap, rect);
                break;
            case VisualRedactionMode.Blur:
                Blur(bitmap, rect);
                break;
            default:
                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(Color.Black))
                {
                    graphics.FillRectangle(brush, rect);
                }

                break;
        }
    }

    public static Bitmap CreateRegionEffectBitmap(string sourcePath, RectangleF region, VisualRedactionMode mode)
    {
        using var source = Image.FromFile(sourcePath);
        using var full = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(full))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var rect = Clamp(region, full.Width, full.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return new Bitmap(1, 1);
        }

        Apply(full, rect, mode);
        return full.Clone(rect, PixelFormat.Format32bppArgb);
    }

    private static Rectangle Clamp(RectangleF region, int width, int height)
    {
        var left = (int)Math.Floor(Math.Max(0, region.Left));
        var top = (int)Math.Floor(Math.Max(0, region.Top));
        var right = (int)Math.Ceiling(Math.Min(width, region.Right));
        var bottom = (int)Math.Ceiling(Math.Min(height, region.Bottom));
        return Rectangle.FromLTRB(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }

    private static void Pixelate(Bitmap bitmap, Rectangle rect)
    {
        var smallWidth = Math.Max(1, rect.Width / PixelBlockSize);
        var smallHeight = Math.Max(1, rect.Height / PixelBlockSize);
        using var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb);
        using (var smallGraphics = Graphics.FromImage(small))
        {
            smallGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            smallGraphics.PixelOffsetMode = PixelOffsetMode.Half;
            smallGraphics.DrawImage(bitmap, new Rectangle(0, 0, smallWidth, smallHeight), rect, GraphicsUnit.Pixel);
        }

        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(small, rect);
    }

    private static void Blur(Bitmap bitmap, Rectangle rect)
    {
        var smallWidth = Math.Max(1, rect.Width / BlurScaleDivisor);
        var smallHeight = Math.Max(1, rect.Height / BlurScaleDivisor);
        using var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb);
        using (var smallGraphics = Graphics.FromImage(small))
        {
            smallGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            smallGraphics.CompositingQuality = CompositingQuality.HighQuality;
            smallGraphics.SmoothingMode = SmoothingMode.HighQuality;
            smallGraphics.DrawImage(bitmap, new Rectangle(0, 0, smallWidth, smallHeight), rect, GraphicsUnit.Pixel);
        }

        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(small, rect);
    }
}

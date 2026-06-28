using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ScrollingStitcher
{
    public static Bitmap Stitch(IReadOnlyList<Bitmap> frames, ScrollingCaptureOptions? options = null)
    {
        options = (options ?? new ScrollingCaptureOptions()).Normalize();
        if (frames.Count == 0)
        {
            return new Bitmap(1, 1);
        }

        if (frames.Count == 1)
        {
            return new Bitmap(frames[0]);
        }

        return options.Axis == ScrollingCaptureAxis.Horizontal
            ? StitchHorizontal(frames, options)
            : StitchVertical(frames, options);
    }

    public static bool LooksLikeSameFrame(Bitmap previous, Bitmap current, ScrollingCaptureOptions? options = null)
    {
        options = (options ?? new ScrollingCaptureOptions()).Normalize();
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            return false;
        }

        var sticky = EffectiveStickyPixels(previous, current, options);
        var startX = options.Axis == ScrollingCaptureAxis.Horizontal ? sticky : 0;
        var startY = options.Axis == ScrollingCaptureAxis.Vertical ? sticky : 0;
        var width = Math.Max(1, current.Width - startX);
        var height = Math.Max(1, current.Height - startY);
        var sampleX = Math.Max(1, width / 20);
        var sampleY = Math.Max(1, height / 20);
        long score = 0;
        var samples = 0;

        for (var y = startY; y < current.Height; y += sampleY)
        {
            for (var x = startX; x < current.Width; x += sampleX)
            {
                var a = previous.GetPixel(x, y);
                var b = current.GetPixel(x, y);
                score += PixelDistance(a, b);
                samples++;
            }
        }

        return samples > 0 && score / samples < 3;
    }

    public static int FindBestOverlap(Bitmap previous, Bitmap current, ScrollingCaptureOptions? options = null)
    {
        options = (options ?? new ScrollingCaptureOptions()).Normalize();
        return options.Axis == ScrollingCaptureAxis.Horizontal
            ? FindBestHorizontalOverlap(previous, current, options, EffectiveStickyPixels(previous, current, options))
            : FindBestVerticalOverlap(previous, current, options, EffectiveStickyPixels(previous, current, options));
    }

    private static Bitmap StitchVertical(IReadOnlyList<Bitmap> frames, ScrollingCaptureOptions options)
    {
        var overlaps = new List<int> { 0 };
        var stickies = new List<int> { 0 };
        for (var index = 1; index < frames.Count; index++)
        {
            var sticky = EffectiveStickyPixels(frames[index - 1], frames[index], options);
            stickies.Add(sticky);
            overlaps.Add(FindBestVerticalOverlap(frames[index - 1], frames[index], options, sticky));
        }

        var width = frames.Min(frame => frame.Width);
        var height = frames[0].Height + frames.Skip(1)
            .Select((frame, index) => Math.Max(0, frame.Height - stickies[index + 1] - overlaps[index + 1]))
            .Sum();
        var stitched = new Bitmap(width, Math.Max(1, height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(stitched);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var y = 0;
        graphics.DrawImage(frames[0], new Rectangle(0, y, width, frames[0].Height), new Rectangle(0, 0, width, frames[0].Height), GraphicsUnit.Pixel);
        y += frames[0].Height;

        for (var index = 1; index < frames.Count; index++)
        {
            var sourceY = stickies[index] + overlaps[index];
            var frame = frames[index];
            var sourceHeight = frame.Height - sourceY;
            if (sourceHeight <= 0)
            {
                continue;
            }

            graphics.DrawImage(
                frame,
                new Rectangle(0, y, width, sourceHeight),
                new Rectangle(0, sourceY, width, sourceHeight),
                GraphicsUnit.Pixel);
            y += sourceHeight;
        }

        return stitched;
    }

    private static Bitmap StitchHorizontal(IReadOnlyList<Bitmap> frames, ScrollingCaptureOptions options)
    {
        var overlaps = new List<int> { 0 };
        var stickies = new List<int> { 0 };
        for (var index = 1; index < frames.Count; index++)
        {
            var sticky = EffectiveStickyPixels(frames[index - 1], frames[index], options);
            stickies.Add(sticky);
            overlaps.Add(FindBestHorizontalOverlap(frames[index - 1], frames[index], options, sticky));
        }

        var width = frames[0].Width + frames.Skip(1)
            .Select((frame, index) => Math.Max(0, frame.Width - stickies[index + 1] - overlaps[index + 1]))
            .Sum();
        var height = frames.Min(frame => frame.Height);
        var stitched = new Bitmap(Math.Max(1, width), height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(stitched);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var x = 0;
        graphics.DrawImage(frames[0], new Rectangle(x, 0, frames[0].Width, height), new Rectangle(0, 0, frames[0].Width, height), GraphicsUnit.Pixel);
        x += frames[0].Width;

        for (var index = 1; index < frames.Count; index++)
        {
            var sourceX = stickies[index] + overlaps[index];
            var frame = frames[index];
            var sourceWidth = frame.Width - sourceX;
            if (sourceWidth <= 0)
            {
                continue;
            }

            graphics.DrawImage(
                frame,
                new Rectangle(x, 0, sourceWidth, height),
                new Rectangle(sourceX, 0, sourceWidth, height),
                GraphicsUnit.Pixel);
            x += sourceWidth;
        }

        return stitched;
    }

    private static int FindBestVerticalOverlap(Bitmap previous, Bitmap current, ScrollingCaptureOptions options, int sticky)
    {
        var maxOverlap = Math.Min(Math.Min(previous.Height, current.Height - sticky) / 2, options.MaximumOverlapPixels);
        if (maxOverlap < options.MinimumOverlapPixels)
        {
            return Math.Min(24, Math.Max(0, current.Height - sticky) / 4);
        }

        var bestOverlap = Math.Min(96, maxOverlap);
        var bestScore = long.MaxValue;
        for (var overlap = options.MinimumOverlapPixels; overlap <= maxOverlap; overlap += 16)
        {
            var score = VerticalOverlapScore(previous, current, overlap, sticky);
            if (score < bestScore)
            {
                bestScore = score;
                bestOverlap = overlap;
            }
        }

        return bestOverlap;
    }

    private static int FindBestHorizontalOverlap(Bitmap previous, Bitmap current, ScrollingCaptureOptions options, int sticky)
    {
        var maxOverlap = Math.Min(Math.Min(previous.Width, current.Width - sticky) / 2, options.MaximumOverlapPixels);
        if (maxOverlap < options.MinimumOverlapPixels)
        {
            return Math.Min(24, Math.Max(0, current.Width - sticky) / 4);
        }

        var bestOverlap = Math.Min(96, maxOverlap);
        var bestScore = long.MaxValue;
        for (var overlap = options.MinimumOverlapPixels; overlap <= maxOverlap; overlap += 16)
        {
            var score = HorizontalOverlapScore(previous, current, overlap, sticky);
            if (score < bestScore)
            {
                bestScore = score;
                bestOverlap = overlap;
            }
        }

        return bestOverlap;
    }

    private static long VerticalOverlapScore(Bitmap previous, Bitmap current, int overlap, int sticky)
    {
        var width = Math.Min(previous.Width, current.Width);
        var sampleX = Math.Max(1, width / 24);
        var sampleY = Math.Max(1, overlap / 12);
        long score = 0;
        for (var y = 0; y < overlap; y += sampleY)
        {
            var previousY = previous.Height - overlap + y;
            var currentY = sticky + y;
            for (var x = 0; x < width; x += sampleX)
            {
                score += PixelDistance(previous.GetPixel(x, previousY), current.GetPixel(x, currentY));
            }
        }

        return score;
    }

    private static long HorizontalOverlapScore(Bitmap previous, Bitmap current, int overlap, int sticky)
    {
        var height = Math.Min(previous.Height, current.Height);
        var sampleX = Math.Max(1, overlap / 12);
        var sampleY = Math.Max(1, height / 24);
        long score = 0;
        for (var x = 0; x < overlap; x += sampleX)
        {
            var previousX = previous.Width - overlap + x;
            var currentX = sticky + x;
            for (var y = 0; y < height; y += sampleY)
            {
                score += PixelDistance(previous.GetPixel(previousX, y), current.GetPixel(currentX, y));
            }
        }

        return score;
    }

    private static int PixelDistance(Color a, Color b)
    {
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }

    internal static int EffectiveStickyPixels(Bitmap previous, Bitmap current, ScrollingCaptureOptions options)
    {
        var limit = options.Axis == ScrollingCaptureAxis.Horizontal
            ? Math.Max(0, current.Width - 1)
            : Math.Max(0, current.Height - 1);
        var configured = Math.Min(options.StickyPixels, limit);
        if (!options.AutoDetectStickyRegion)
        {
            return configured;
        }

        var detected = options.Axis == ScrollingCaptureAxis.Horizontal
            ? DetectStickyLeftPixels(previous, current, options)
            : DetectStickyTopPixels(previous, current, options);
        return Math.Min(limit, Math.Max(configured, detected));
    }

    private static int DetectStickyTopPixels(Bitmap previous, Bitmap current, ScrollingCaptureOptions options)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            return 0;
        }

        var max = Math.Min(
            options.MaximumAutoStickyPixels,
            Math.Max(0, Math.Min(previous.Height, current.Height) / 3));
        var width = Math.Min(previous.Width, current.Width);
        var sampleX = Math.Max(1, width / 32);
        var sticky = 0;
        for (var y = 0; y < max; y++)
        {
            long rowDistance = 0;
            var samples = 0;
            for (var x = 0; x < width; x += sampleX)
            {
                rowDistance += PixelDistance(previous.GetPixel(x, y), current.GetPixel(x, y));
                samples++;
            }

            if (samples == 0 || rowDistance / samples > 8)
            {
                break;
            }

            sticky = y + 1;
        }

        return sticky >= 4 ? sticky : 0;
    }

    private static int DetectStickyLeftPixels(Bitmap previous, Bitmap current, ScrollingCaptureOptions options)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            return 0;
        }

        var max = Math.Min(
            options.MaximumAutoStickyPixels,
            Math.Max(0, Math.Min(previous.Width, current.Width) / 3));
        var height = Math.Min(previous.Height, current.Height);
        var sampleY = Math.Max(1, height / 32);
        var sticky = 0;
        for (var x = 0; x < max; x++)
        {
            long columnDistance = 0;
            var samples = 0;
            for (var y = 0; y < height; y += sampleY)
            {
                columnDistance += PixelDistance(previous.GetPixel(x, y), current.GetPixel(x, y));
                samples++;
            }

            if (samples == 0 || columnDistance / samples > 8)
            {
                break;
            }

            sticky = x + 1;
        }

        return sticky >= 4 ? sticky : 0;
    }
}

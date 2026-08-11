using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ScrollingCaptureStressService
{
    public static Task<ScrollingStressReport> GenerateAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
            Directory.CreateDirectory(fullDirectory);

            var results = new List<ScrollingStressScenarioResult>();
            foreach (var scenario in BuildScenarios())
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(GenerateScenario(fullDirectory, scenario));
            }

            var report = new ScrollingStressReport(
                DateTimeOffset.Now,
                fullDirectory,
                "Synthetic scrolling targets are generated locally and contain no live desktop pixels.",
                "If simulated wheel scrolling cannot control a real target, capture overlapping frames manually and use `receipts image stitch` with the matching scroll profile.",
                results);
            return report;
        }, cancellationToken);
    }

    private static ScrollingStressScenarioResult GenerateScenario(string outputDirectory, SyntheticScrollScenario scenario)
    {
        var scenarioDirectory = Path.Combine(outputDirectory, scenario.Name);
        Directory.CreateDirectory(scenarioDirectory);

        using var content = scenario.Axis == ScrollingCaptureAxis.Horizontal
            ? CreateHorizontalContent(scenario)
            : CreateVerticalContent(scenario);

        using var frames = new DisposableBitmapList();
        var framePaths = new List<string>();
        for (var index = 0; index < scenario.FrameCount; index++)
        {
            var frame = scenario.Axis == ScrollingCaptureAxis.Horizontal
                ? CreateHorizontalFrame(content, scenario, index)
                : CreateVerticalFrame(content, scenario, index);
            frames.Add(frame);

            var framePath = Path.Combine(scenarioDirectory, $"frame-{index + 1}.png");
            frame.Save(framePath, ImageFormat.Png);
            framePaths.Add(framePath);
        }

        var options = scenario.Options.Normalize();
        var detectedSticky = frames.Count > 1
            ? ScrollingStitcher.EffectiveStickyPixels(frames[0], frames[1], options)
            : 0;
        var bestOverlap = frames.Count > 1
            ? ScrollingStitcher.FindBestOverlap(frames[0], frames[1], options)
            : 0;

        using var stitched = ScrollingStitcher.Stitch(frames, options);
        var stitchedPath = Path.Combine(scenarioDirectory, "stitched.png");
        stitched.Save(stitchedPath, ImageFormat.Png);

        return new ScrollingStressScenarioResult(
            scenario.Name,
            scenario.Description,
            scenario.Profile,
            scenario.Axis,
            scenario.FrameCount,
            scenario.ExpectedStickyPixels,
            scenario.ExpectedOverlapPixels,
            detectedSticky,
            bestOverlap,
            scenario.ExpectedStitchedWidth,
            scenario.ExpectedStitchedHeight,
            stitched.Width,
            stitched.Height,
            framePaths,
            stitchedPath);
    }

    private static IReadOnlyList<SyntheticScrollScenario> BuildScenarios()
    {
        return
        [
            new SyntheticScrollScenario(
                "plain-page",
                "Plain vertically scrolling page with overlapping frames and no sticky region.",
                "browser",
                ScrollingCaptureAxis.Vertical,
                FrameWidth: 420,
                FrameHeight: 256,
                FrameCount: 4,
                StepPixels: 192,
                StickyPixels: 0),
            new SyntheticScrollScenario(
                "document-page",
                "Document-like vertical target with page margins and repeated content landmarks.",
                "document",
                ScrollingCaptureAxis.Vertical,
                FrameWidth: 420,
                FrameHeight: 288,
                FrameCount: 4,
                StepPixels: 224,
                StickyPixels: 0),
            new SyntheticScrollScenario(
                "browser-sticky-header",
                "Browser-like vertical page with a repeated sticky top header.",
                "browser",
                ScrollingCaptureAxis.Vertical,
                FrameWidth: 420,
                FrameHeight: 272,
                FrameCount: 4,
                StepPixels: 160,
                StickyPixels: 48),
            new SyntheticScrollScenario(
                "large-table-sticky-column",
                "Large horizontal table with a repeated sticky left column.",
                "table",
                ScrollingCaptureAxis.Horizontal,
                FrameWidth: 344,
                FrameHeight: 220,
                FrameCount: 4,
                StepPixels: 192,
                StickyPixels: 56),
            new SyntheticScrollScenario(
                "horizontal-strip",
                "Wide horizontally scrolling strip with no sticky left edge.",
                "table",
                ScrollingCaptureAxis.Horizontal,
                FrameWidth: 352,
                FrameHeight: 190,
                FrameCount: 4,
                StepPixels: 256,
                StickyPixels: 0)
        ];
    }

    private static Bitmap CreateVerticalContent(SyntheticScrollScenario scenario)
    {
        var contentHeight = scenario.BodySize + (scenario.StepPixels * (scenario.FrameCount - 1));
        var bitmap = new Bitmap(scenario.FrameWidth, contentHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, ContentColor(x, y, scenario.Name));
            }
        }

        using var graphics = Graphics.FromImage(bitmap);
        PrepareGraphics(graphics);
        DrawVerticalLandmarks(graphics, scenario, bitmap.Width, bitmap.Height);
        return bitmap;
    }

    private static Bitmap CreateHorizontalContent(SyntheticScrollScenario scenario)
    {
        var contentWidth = scenario.BodySize + (scenario.StepPixels * (scenario.FrameCount - 1));
        var bitmap = new Bitmap(contentWidth, scenario.FrameHeight, PixelFormat.Format32bppArgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, ContentColor(x, y, scenario.Name));
            }
        }

        using var graphics = Graphics.FromImage(bitmap);
        PrepareGraphics(graphics);
        DrawHorizontalLandmarks(graphics, scenario, bitmap.Width, bitmap.Height);
        return bitmap;
    }

    private static Bitmap CreateVerticalFrame(Bitmap content, SyntheticScrollScenario scenario, int index)
    {
        var frame = new Bitmap(scenario.FrameWidth, scenario.FrameHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(frame);
        PrepareGraphics(graphics);
        graphics.Clear(Color.FromArgb(255, 14, 22, 30));

        if (scenario.StickyPixels > 0)
        {
            DrawStickyHeader(graphics, scenario.FrameWidth, scenario.StickyPixels, scenario.Name);
        }

        var bodyHeight = scenario.BodySize;
        var sourceY = index * scenario.StepPixels;
        graphics.DrawImage(
            content,
            new Rectangle(0, scenario.StickyPixels, scenario.FrameWidth, bodyHeight),
            new Rectangle(0, sourceY, scenario.FrameWidth, bodyHeight),
            GraphicsUnit.Pixel);
        return frame;
    }

    private static Bitmap CreateHorizontalFrame(Bitmap content, SyntheticScrollScenario scenario, int index)
    {
        var frame = new Bitmap(scenario.FrameWidth, scenario.FrameHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(frame);
        PrepareGraphics(graphics);
        graphics.Clear(Color.FromArgb(255, 14, 22, 30));

        if (scenario.StickyPixels > 0)
        {
            DrawStickyColumn(graphics, scenario.StickyPixels, scenario.FrameHeight, scenario.Name);
        }

        var bodyWidth = scenario.BodySize;
        var sourceX = index * scenario.StepPixels;
        graphics.DrawImage(
            content,
            new Rectangle(scenario.StickyPixels, 0, bodyWidth, scenario.FrameHeight),
            new Rectangle(sourceX, 0, bodyWidth, scenario.FrameHeight),
            GraphicsUnit.Pixel);
        return frame;
    }

    private static void DrawVerticalLandmarks(Graphics graphics, SyntheticScrollScenario scenario, int width, int height)
    {
        using var linePen = new Pen(Color.FromArgb(120, 255, 255, 255), 1);
        using var accentBrush = new SolidBrush(Color.FromArgb(210, 41, 214, 184));
        using var textBrush = new SolidBrush(Color.FromArgb(230, 248, 252, 255));
        using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);

        for (var y = 32; y < height; y += 64)
        {
            graphics.DrawLine(linePen, 18, y, width - 18, y);
            graphics.FillRectangle(accentBrush, 26, y + 10, 110 + (y % 90), 8);
            graphics.DrawString($"{scenario.Name} y={y}", smallFont, textBrush, 26, y + 22);
        }

        if (scenario.Name.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            using var pageBrush = new SolidBrush(Color.FromArgb(235, 246, 247, 242));
            using var inkBrush = new SolidBrush(Color.FromArgb(210, 32, 42, 54));
            for (var y = 18; y < height; y += 220)
            {
                graphics.FillRectangle(pageBrush, 46, y, width - 92, 176);
                graphics.DrawRectangle(Pens.LightSlateGray, 46, y, width - 92, 176);
                graphics.DrawString($"Document page {1 + y / 220}", font, inkBrush, 68, y + 22);
            }
        }
    }

    private static void DrawHorizontalLandmarks(Graphics graphics, SyntheticScrollScenario scenario, int width, int height)
    {
        using var linePen = new Pen(Color.FromArgb(130, 255, 255, 255), 1);
        using var accentBrush = new SolidBrush(Color.FromArgb(220, 41, 214, 184));
        using var textBrush = new SolidBrush(Color.FromArgb(230, 248, 252, 255));
        using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);

        for (var x = 36; x < width; x += 96)
        {
            graphics.DrawLine(linePen, x, 16, x, height - 16);
            graphics.FillRectangle(accentBrush, x + 10, 28, 9, height - 56);
            graphics.DrawString($"x={x}", font, textBrush, x + 24, 38);
        }

        if (scenario.Name.Contains("table", StringComparison.OrdinalIgnoreCase))
        {
            using var headerBrush = new SolidBrush(Color.FromArgb(210, 18, 42, 54));
            graphics.FillRectangle(headerBrush, 0, 0, width, 34);
            for (var row = 42; row < height; row += 34)
            {
                graphics.DrawLine(linePen, 0, row, width, row);
            }
        }
    }

    private static void DrawStickyHeader(Graphics graphics, int width, int height, string label)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 9, 32, 42));
        using var accent = new SolidBrush(Color.FromArgb(255, 48, 230, 195));
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.FillRectangle(brush, 0, 0, width, height);
        graphics.FillRectangle(accent, 18, 16, 108, 8);
        graphics.DrawString($"Sticky header - {label}", font, textBrush, 146, 11);
    }

    private static void DrawStickyColumn(Graphics graphics, int width, int height, string label)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 9, 32, 42));
        using var accent = new SolidBrush(Color.FromArgb(255, 48, 230, 195));
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.FillRectangle(brush, 0, 0, width, height);
        graphics.FillRectangle(accent, 14, 20, 8, height - 40);
        graphics.TranslateTransform(42, height - 18);
        graphics.RotateTransform(-90);
        graphics.DrawString($"Sticky column - {label}", font, textBrush, 0, 0);
        graphics.ResetTransform();
    }

    private static void PrepareGraphics(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
    }

    private static Color ContentColor(int x, int y, string scenarioName)
    {
        var seed = HashCode.Combine(x / 3, y / 3, scenarioName);
        var r = 42 + Math.Abs(seed % 160);
        var g = 48 + Math.Abs((seed >> 7) % 150);
        var b = 58 + Math.Abs((seed >> 13) % 140);
        return Color.FromArgb(255, r, g, b);
    }

    private sealed record SyntheticScrollScenario(
        string Name,
        string Description,
        string Profile,
        ScrollingCaptureAxis Axis,
        int FrameWidth,
        int FrameHeight,
        int FrameCount,
        int StepPixels,
        int StickyPixels)
    {
        public int BodySize => Axis == ScrollingCaptureAxis.Horizontal
            ? FrameWidth - StickyPixels
            : FrameHeight - StickyPixels;

        public int ExpectedOverlapPixels => BodySize - StepPixels;

        public int ExpectedStickyPixels => StickyPixels;

        public int ExpectedStitchedWidth => Axis == ScrollingCaptureAxis.Horizontal
            ? FrameWidth + ((FrameCount - 1) * StepPixels)
            : FrameWidth;

        public int ExpectedStitchedHeight => Axis == ScrollingCaptureAxis.Vertical
            ? FrameHeight + ((FrameCount - 1) * StepPixels)
            : FrameHeight;

        public ScrollingCaptureOptions Options
        {
            get
            {
                var options = ScrollingCaptureProfiles.For(Profile);
                options.Axis = Axis;
                options.AutoDetectStickyRegion = true;
                options.StickyPixels = 0;
                options.MaximumAutoStickyPixels = Math.Max(120, StickyPixels + 32);
                options.MinimumOverlapPixels = 32;
                options.MaximumOverlapPixels = Math.Max(160, ExpectedOverlapPixels + 64);
                return options;
            }
        }
    }

    private sealed class DisposableBitmapList : List<Bitmap>, IDisposable
    {
        public void Dispose()
        {
            foreach (var bitmap in this)
            {
                bitmap.Dispose();
            }
        }
    }
}

public sealed record ScrollingStressReport(
    DateTimeOffset GeneratedAt,
    string OutputDirectory,
    string PrivacyNote,
    string RetryGuidance,
    IReadOnlyList<ScrollingStressScenarioResult> Scenarios);

public sealed record ScrollingStressScenarioResult(
    string Name,
    string Description,
    string Profile,
    ScrollingCaptureAxis Axis,
    int FrameCount,
    int ExpectedStickyPixels,
    int ExpectedOverlapPixels,
    int DetectedStickyPixels,
    int BestOverlapPixels,
    int ExpectedStitchedWidth,
    int ExpectedStitchedHeight,
    int StitchedWidth,
    int StitchedHeight,
    IReadOnlyList<string> FramePaths,
    string StitchedPath);

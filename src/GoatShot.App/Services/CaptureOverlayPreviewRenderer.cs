using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Windows;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingPen = System.Drawing.Pen;
using DrawingSolidBrush = System.Drawing.SolidBrush;

namespace GoatShot.App.Services;

public static class CaptureOverlayPreviewRenderer
{
    private const int Width = 1100;
    private const int Height = 700;

    public static async Task RenderAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A capture overlay preview output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var frozenScreen = CreateSyntheticFrozenScreen();
        var virtualBounds = new GoatShot.App.Models.CaptureBounds
        {
            X = 0,
            Y = 0,
            Width = Width,
            Height = Height
        };
        var targets = new[]
        {
            new CaptureOverlayTarget(
                "synthetic-monitor:1",
                "Primary monitor DISPLAY1 (1100 x 700)",
                CaptureOverlayTargetKind.Monitor,
                virtualBounds),
            new CaptureOverlayTarget(
                "synthetic-window:workspace",
                "Window: Synthetic workspace (420 x 420)",
                CaptureOverlayTargetKind.Window,
                new GoatShot.App.Models.CaptureBounds { X = 50, Y = 130, Width = 420, Height = 420 }),
            new CaptureOverlayTarget(
                "synthetic-window:preview",
                "Window: Preview target (550 x 170)",
                CaptureOverlayTargetKind.Window,
                new GoatShot.App.Models.CaptureBounds { X = 500, Y = 130, Width = 550, Height = 170 }),
            new CaptureOverlayTarget(
                "synthetic-window:preview:content",
                "Content area: Preview target",
                CaptureOverlayTargetKind.ContentArea,
                new GoatShot.App.Models.CaptureBounds { X = 520, Y = 175, Width = 510, Height = 105 },
                ShowInChooser: false)
        };

        var window = new RegionCaptureWindow(
            frozenScreen,
            contextPadding: 16,
            targets: targets,
            virtualBounds: virtualBounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Width = Width,
            Height = Height,
            ShowInTaskbar = false,
            Topmost = false
        };

        try
        {
            window.Show();
            window.SetPreviewSelection(260, 190, 560, 290);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var pixelWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = File.Create(fullPath);
            encoder.Save(stream);
        }
        finally
        {
            window.Close();
        }
    }

    private static BitmapSource CreateSyntheticFrozenScreen()
    {
        using var bitmap = new DrawingBitmap(Width, Height);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(DrawingColor.FromArgb(16, 24, 32));

        using var titleFont = new DrawingFont("Segoe UI", 26, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);
        using var bodyFont = new DrawingFont("Segoe UI", 15, DrawingFontStyle.Regular, DrawingGraphicsUnit.Pixel);
        using var smallFont = new DrawingFont("Segoe UI", 12, DrawingFontStyle.Regular, DrawingGraphicsUnit.Pixel);
        using var panelBrush = new DrawingSolidBrush(DrawingColor.FromArgb(28, 42, 54));
        using var panelAltBrush = new DrawingSolidBrush(DrawingColor.FromArgb(33, 52, 67));
        using var accentBrush = new DrawingSolidBrush(DrawingColor.FromArgb(48, 230, 195));
        using var mutedBrush = new DrawingSolidBrush(DrawingColor.FromArgb(168, 188, 202));
        using var linePen = new DrawingPen(DrawingColor.FromArgb(60, 88, 108), 1);

        graphics.DrawString("Synthetic audit desktop", titleFont, DrawingBrushes.White, 48, 42);
        graphics.DrawString("No live desktop pixels are captured in this overlay proof.", bodyFont, mutedBrush, 50, 82);

        graphics.FillRoundedRectangle(panelBrush, 50, 130, 420, 420, 16);
        graphics.FillRoundedRectangle(panelAltBrush, 500, 130, 550, 170, 16);
        graphics.FillRoundedRectangle(panelBrush, 500, 330, 260, 220, 16);
        graphics.FillRoundedRectangle(panelBrush, 790, 330, 260, 220, 16);

        graphics.FillRoundedRectangle(accentBrush, 78, 160, 92, 10, 5);
        for (var row = 0; row < 8; row++)
        {
            var y = 200 + (row * 36);
            graphics.DrawLine(linePen, 78, y, 438, y);
            graphics.FillRoundedRectangle(
                row % 2 == 0 ? panelAltBrush : panelBrush,
                78,
                y + 10,
                260 + (row % 3 * 36),
                12,
                6);
        }

        graphics.DrawString("Preview target", bodyFont, DrawingBrushes.White, 530, 158);
        graphics.DrawString("Selection rectangle and size badge are rendered by the live overlay window.", smallFont, mutedBrush, 530, 188);
        graphics.FillRoundedRectangle(accentBrush, 530, 236, 160, 12, 6);
        graphics.FillRoundedRectangle(panelAltBrush, 530, 260, 430, 12, 6);

        graphics.DrawString("Workspace", bodyFont, DrawingBrushes.White, 530, 360);
        graphics.DrawString("Tools", bodyFont, DrawingBrushes.White, 820, 360);
        for (var row = 0; row < 4; row++)
        {
            graphics.FillRoundedRectangle(panelAltBrush, 530, 400 + row * 34, 190, 13, 6);
            graphics.FillRoundedRectangle(panelAltBrush, 820, 400 + row * 34, 190, 13, 6);
        }

        return ImageInterop.ToBitmapSource(bitmap);
    }
}

internal static class DrawingGraphicsExtensions
{
    public static void FillRoundedRectangle(
        this DrawingGraphics graphics,
        DrawingSolidBrush brush,
        float x,
        float y,
        float width,
        float height,
        float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}

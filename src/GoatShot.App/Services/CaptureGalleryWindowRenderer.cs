using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace GoatShot.App.Services;

/// <summary>Renders the real gallery with synthetic local images, without reading capture history.</summary>
public static class CaptureGalleryWindowRenderer
{
    public static async Task RenderAsync(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.Combine(Path.GetDirectoryName(fullPath)!, "gallery-preview-images");
        Directory.CreateDirectory(directory);
        var captures = new List<CaptureItem>();
        for (var index = 0; index < 42; index++)
        {
            var filePath = Path.Combine(directory, $"2026-09-10_10-{index:00}-00.png");
            var width = index % 5 == 0 ? 150 : 320;
            var height = index % 5 == 0 ? 240 : 190;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var paper = index % 4 == 0;
                drawing.DrawRectangle(paper ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(27, 34, 42)), null,
                    new Rect(0, 0, width, height));
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(47, 66, 79)), null, new Rect(0, 0, width, 22));
                for (var line = 0; line < 9; line++)
                {
                    var brush = paper ? Brushes.SlateGray : line % 3 == 0 ? Brushes.MediumAquamarine : Brushes.SlateGray;
                    drawing.DrawRectangle(brush, null, new Rect(14, 37 + line * 15, (width - 28) * (0.5 + (line + index) % 4 * 0.12), 4));
                }
            }

            var preview = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            preview.Render(visual);
            Save(preview, filePath);
            captures.Add(new CaptureItem
            {
                Id = $"gallery-preview-{index}", FilePath = filePath, ThumbnailPath = filePath,
                Kind = CaptureKind.Region, Width = width, Height = height,
                CreatedAt = new DateTimeOffset(2026, 9, 10, 10, index, 0, TimeSpan.Zero)
            });
        }

        var window = new CaptureGalleryWindow(autoDismissSeconds: 0)
        {
            Left = -10000, Top = -10000, Topmost = false
        };
        try
        {
            window.UpdateCaptures(captures, captures[^1]);
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            Save(bitmap, fullPath);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

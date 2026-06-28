using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class EditorWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string imagePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("An editor source image path is required.", nameof(imagePath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An editor screenshot output path is required.", nameof(outputPath));
        }

        var fullImagePath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullImagePath))
        {
            throw new FileNotFoundException("Editor source image was not found.", fullImagePath);
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var source = ImageInterop.LoadBitmapImage(fullImagePath);
        var item = new CaptureItem
        {
            Kind = CaptureKind.Imported,
            FilePath = fullImagePath,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Bytes = new FileInfo(fullImagePath).Length,
            ThumbnailPath = string.Empty
        };

        var window = new EditorWindow(item, services)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Width = 1200,
            Height = 820,
            ShowInTaskbar = false
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.Show();
            window.SelectTool(AnnotationMode.Redact);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = File.Create(fullOutputPath);
            encoder.Save(stream);
        }
        finally
        {
            window.Close();
        }
    }
}

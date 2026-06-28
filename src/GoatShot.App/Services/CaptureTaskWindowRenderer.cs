using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class CaptureTaskWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A capture task window screenshot output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var capturePath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? services.Paths.TempRoot : directory,
            "capture-task-preview.png");
        if (!File.Exists(capturePath))
        {
            await File.WriteAllBytesAsync(capturePath, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, cancellationToken);
        }

        services.Settings.AiEnabled = true;
        services.Settings.CustomScriptCommand = "Write-Output 'https://example.test/captures/{id}'";
        services.Settings.CustomWebhookUrl = "https://example.test/hooks/goatshot?token=preview-token";
        services.Settings.DefaultShareDestination = "Custom webhook";

        var item = new CaptureItem
        {
            Id = "capture-task-preview",
            Kind = CaptureKind.ActiveWindow,
            CreatedAt = DateTimeOffset.Parse("2026-06-14T12:00:00-07:00", CultureInfo.InvariantCulture),
            FilePath = capturePath,
            Bytes = 384_512,
            Width = 1440,
            Height = 900,
            IsPrivate = false,
            SourceApp = "Browser",
            SourceWindowTitle = "Issue tracker checkout error",
            OcrText = "Synthetic OCR context"
        };

        var window = new CaptureTaskWindow(CaptureTaskWindowModels.Build(item, services.Settings))
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
}

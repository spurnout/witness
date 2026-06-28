using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class UploadTaskWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string? surface,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An upload task window screenshot output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var item = new CaptureItem
        {
            Id = "upload-task-preview",
            Kind = CaptureKind.ActiveWindow,
            CreatedAt = DateTimeOffset.Parse("2026-06-14T12:00:00-07:00", CultureInfo.InvariantCulture),
            FilePath = @"C:\Users\you\Pictures\GoatShot\issue-checkout.png",
            Bytes = 384_512,
            Width = 1440,
            Height = 900,
            IsPrivate = true,
            SourceApp = "Browser",
            SourceWindowTitle = "Issue tracker",
            OcrText = "Synthetic OCR context"
        };

        Window window;
        var normalized = string.IsNullOrWhiteSpace(surface) ? "confirm" : surface.Trim();
        if (normalized.Equals("result", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("after", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("after-upload", StringComparison.OrdinalIgnoreCase))
        {
            const string url = "https://example.test/share/issue-checkout";
            var qrPath = Path.Combine(
                string.IsNullOrWhiteSpace(directory) ? services.Paths.TempRoot : directory,
                "upload-result-preview-qr.png");
            await services.Barcodes.GenerateQrCodeAsync(url, qrPath, cancellationToken);
            window = new UploadResultWindow(
                ShareTaskWindowModels.BuildResult(
                    item,
                    ShareDestination.CustomWebhook,
                    new ShareResult
                    {
                        Succeeded = true,
                        Url = url,
                        Message = "Webhook upload completed and copied URL: https://example.test/share/issue-checkout"
                    },
                    qrPath));
        }
        else
        {
            window = new UploadConfirmationWindow(
                ShareTaskWindowModels.BuildConfirmation(item, ShareDestination.CustomWebhook));
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;

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

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class ShareHistoryWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A share history screenshot output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await SeedPreviewDataAsync(services, cancellationToken);
        if (System.Windows.Application.Current is not null)
        {
            System.Windows.Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        var window = new ShareHistoryWindow(services)
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
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            {
                throw new IOException($"Share history window render did not produce a PNG at {fullPath}.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task SeedPreviewDataAsync(AppServices services, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Parse("2026-06-14T12:30:00-07:00", CultureInfo.InvariantCulture);
        services.Settings.LocalExportFolder = Path.Combine(services.Paths.LibraryRoot, "ShareHistoryRenderExports");

        var success = CreatePreviewItem(services, "history-success.png", 384, now);
        var failed = CreatePreviewItem(services, "history-failed.png", 512, now.AddMinutes(-3));
        await services.Sharing.ShareAsync(success, ShareDestination.LocalFolder, cancellationToken);
        await services.Sharing.ShareAsync(failed, ShareDestination.CustomWebhook, cancellationToken);
        await services.UploadQueue.EnqueueAsync(failed, ShareDestination.CustomWebhook, cancellationToken);
    }

    private static CaptureItem CreatePreviewItem(AppServices services, string fileName, int bytes, DateTimeOffset createdAt)
    {
        var path = Path.Combine(services.Paths.ImagesRoot, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(index => (byte)(index % 255)).ToArray());
        }

        return new CaptureItem
        {
            Id = $"share-history-render-{Path.GetFileNameWithoutExtension(fileName)}",
            Kind = CaptureKind.Imported,
            CreatedAt = createdAt,
            FilePath = path,
            ThumbnailPath = path,
            Bytes = new FileInfo(path).Length,
            Width = 960,
            Height = 540,
            SourceApp = "GoatShot preview",
            SourceWindowTitle = "Share history render proof",
            Notes = "Synthetic share history render item."
        };
    }
}

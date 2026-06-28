using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class AiHistoryWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An AI history screenshot output path is required.", nameof(outputPath));
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

        var window = new AiHistoryWindow(services)
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
                throw new IOException($"AI history render did not produce a PNG at {fullPath}.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task SeedPreviewDataAsync(AppServices services, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Parse("2026-06-15T10:30:00-07:00", CultureInfo.InvariantCulture);
        var capturePath = Path.Combine(services.Paths.ImagesRoot, "ai-review-preview.png");
        Directory.CreateDirectory(services.Paths.ImagesRoot);
        if (!File.Exists(capturePath))
        {
            await File.WriteAllBytesAsync(capturePath, Enumerable.Range(0, 512).Select(index => (byte)(index % 255)).ToArray(), cancellationToken);
        }

        var summaryPath = Path.Combine(services.Paths.DocumentsRoot, "ai-review-video-summary.md");
        Directory.CreateDirectory(services.Paths.DocumentsRoot);
        await File.WriteAllTextAsync(summaryPath, "# Checkout Failure\n\nPayment fails after submit.", cancellationToken);

        var item = new CaptureItem
        {
            Id = "ai-history-render-preview",
            Kind = CaptureKind.Imported,
            CreatedAt = now,
            FilePath = capturePath,
            ThumbnailPath = capturePath,
            Bytes = new FileInfo(capturePath).Length,
            Width = 1440,
            Height = 900,
            SourceApp = "GoatShot preview",
            SourceWindowTitle = "AI review render proof"
        };

        await services.AiHistory.RecordAsync(
            AiActionKind.ImageAnalysis,
            item,
            "gemini-preview",
            "Explain the screenshot and redact alice@example.com before saving.",
            succeeded: true,
            "Gemini analysis completed.",
            textOutput: "The checkout page shows a failed payment. Customer [REDACTED:email-address] is redacted.",
            cancellationToken: cancellationToken);

        await services.AiHistory.RecordAsync(
            AiActionKind.VideoSummary,
            item,
            "local-transcript-draft",
            "Generate title, summary, and chapters from provided local transcript/SRT.",
            succeeded: true,
            "Video intelligence draft exported as markdown.",
            outputPath: summaryPath,
            textOutput: "Checkout Failure\nThe recording shows payment failure after submit.",
            reviewStatus: AiActionReviewStatus.Accepted,
            cancellationToken: cancellationToken);

        await services.AiHistory.RecordAsync(
            AiActionKind.BugReportDraft,
            item,
            "gemini-preview",
            "Draft a bug report focused on the failed checkout action.",
            succeeded: false,
            "Provider AI was requested, but credentials are not configured.",
            reviewStatus: AiActionReviewStatus.Iterated,
            cancellationToken: cancellationToken);
    }
}

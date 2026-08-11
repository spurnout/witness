using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class FrameExplorerWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A Frame Explorer screenshot output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        FrameExplorerRenderFixture? fixture = null;
        FrameExplorerWindow? window = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            fixture = await CreateFixtureAsync(cancellationToken);
            window = new FrameExplorerWindow(services, fixture.Item, autoLoad: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                Width = 1320,
                Height = 850,
                ShowInTaskbar = false
            };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.PrepareRenderProofAsync(fixture.PreviewFramePath);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            window.ShowRenderTimelineHoverPreview();
            window.UpdateLayout();

            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = File.Create(fullPath);
            encoder.Save(stream);
        }
        finally
        {
            window?.Close();
            fixture?.Dispose();
        }
    }

    internal static async Task<FrameExplorerRenderFixture> CreateFixtureAsync(
        CancellationToken cancellationToken)
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "Receipts.FrameExplorerProof",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            var previewFramePath = await CreateFixtureFilesAsync(fixtureRoot, cancellationToken)
                .ConfigureAwait(false);
            return new FrameExplorerRenderFixture(
                fixtureRoot,
                previewFramePath,
                new CaptureItem
                {
                    Kind = CaptureKind.ReplayReceipt,
                    CreatedAt = new DateTimeOffset(2026, 8, 11, 12, 0, 4, TimeSpan.Zero),
                    FilePath = fixtureRoot,
                    ReceiptId = "receipt-frame-explorer-proof",
                    ArtifactRole = "original-replay",
                    IsOriginal = true,
                    IntegrityStatus = ReceiptVerificationPresentation.FormatStatus(
                        ReceiptVerificationStatus.IntactKnownDevice)
                });
        }
        catch
        {
            TryDeleteFixture(fixtureRoot);
            throw;
        }
    }

    private static async Task<string> CreateFixtureFilesAsync(
        string fixtureRoot,
        CancellationToken cancellationToken)
    {
        var framesRoot = Path.Combine(fixtureRoot, "local-analysis", "frames");
        var primarySegments = Path.Combine(fixtureRoot, "segments", "primary");
        var secondarySegments = Path.Combine(fixtureRoot, "segments", "secondary");
        Directory.CreateDirectory(framesRoot);
        Directory.CreateDirectory(primarySegments);
        Directory.CreateDirectory(secondarySegments);
        var beforePath = Path.Combine(framesRoot, "before.png");
        var afterPath = Path.Combine(framesRoot, "after.png");
        DrawConversationFrame(beforePath, edited: false);
        DrawConversationFrame(afterPath, edited: true);
        await File.WriteAllBytesAsync(
            Path.Combine(primarySegments, "segment-001.mp4"),
            [0, 0, 0, 24, 102, 116, 121, 112],
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(
            Path.Combine(secondarySegments, "segment-001.mp4"),
            [0, 0, 0, 24, 102, 116, 121, 112],
            cancellationToken).ConfigureAwait(false);

        var started = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var duration = TimeSpan.FromSeconds(4);
        var manifest = new ReceiptManifest
        {
            ReceiptId = "receipt-frame-explorer-proof",
            CreatedAtUtc = started,
            FinalizedAtUtc = started + duration,
            Application = new ReceiptApplicationManifest
            {
                ProductName = "Receipts",
                Version = "0.3.0",
                Build = "render-proof"
            },
            CaptureSettings = new ReceiptCaptureSettingsManifest
            {
                RecordingMode = "replay",
                TargetStrategy = "SeparateMonitorTracks",
                FramesPerSecond = 30,
                IncludeCursor = true,
                AdditionalSettings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["analysisSensitivity"] = "0.65"
                }
            },
            Tracks =
            [
                new ReceiptTrackManifest
                {
                    TrackId = "primary-display",
                    SourceKind = "monitor",
                    SourceId = "display-primary",
                    DisplayName = "Primary display · Messages",
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1280, Height = 720 }
                },
                new ReceiptTrackManifest
                {
                    TrackId = "secondary-display",
                    SourceKind = "monitor",
                    SourceId = "display-secondary",
                    DisplayName = "Secondary display · Reference",
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1280, Height = 720 }
                }
            ],
            Segments =
            [
                new ReceiptSegmentManifest
                {
                    SegmentId = "primary-001",
                    TrackId = "primary-display",
                    RelativePath = "segments/primary/segment-001.mp4",
                    CapturedAtUtc = started,
                    StartMonotonicTicks = 0,
                    DurationTicks = duration.Ticks
                },
                new ReceiptSegmentManifest
                {
                    SegmentId = "secondary-001",
                    TrackId = "secondary-display",
                    RelativePath = "segments/secondary/segment-001.mp4",
                    CapturedAtUtc = started,
                    StartMonotonicTicks = 0,
                    DurationTicks = duration.Ticks
                }
            ]
        };
        await File.WriteAllBytesAsync(
            Path.Combine(fixtureRoot, ReceiptIntegrityService.ManifestFileName),
            ReceiptCanonicalJson.Serialize(manifest),
            cancellationToken).ConfigureAwait(false);

        var beforeFrameId = "frame-before";
        var afterFrameId = "frame-after";
        var analysis = new ReceiptLocalAnalysis
        {
            ReceiptId = manifest.ReceiptId,
            AnalyzedAtUtc = started.AddSeconds(5),
            Sensitivity = 0.65d,
            SceneIndexingEnabled = true,
            OcrComparisonEnabled = true,
            Scenes =
            [
                new ReceiptSceneMarker
                {
                    TrackId = "primary-display",
                    SegmentId = "primary-001",
                    MonotonicTicks = 0,
                    RelativeFramePath = "local-analysis/frames/before.png",
                    IsSourceTransition = true,
                    IsVisuallyDistinct = true
                },
                new ReceiptSceneMarker
                {
                    TrackId = "primary-display",
                    SegmentId = "primary-001",
                    MonotonicTicks = TimeSpan.FromSeconds(2).Ticks,
                    RelativeFramePath = "local-analysis/frames/after.png",
                    IsVisuallyDistinct = true
                }
            ],
            Frames =
            [
                new ReceiptOcrFrame
                {
                    FrameId = beforeFrameId,
                    TrackId = "primary-display",
                    SegmentId = "primary-001",
                    SourceId = "display-primary",
                    MonotonicTicks = 0,
                    RelativeFramePath = "local-analysis/frames/before.png",
                    Text = "Alex: The refund was approved yesterday.",
                    LanguageTag = "en-US",
                    OcrSucceeded = true
                },
                new ReceiptOcrFrame
                {
                    FrameId = afterFrameId,
                    TrackId = "primary-display",
                    SegmentId = "primary-001",
                    SourceId = "display-primary",
                    MonotonicTicks = TimeSpan.FromSeconds(2).Ticks,
                    RelativeFramePath = "local-analysis/frames/after.png",
                    Text = "Alex: The refund is still under review.",
                    LanguageTag = "en-US",
                    OcrSucceeded = true
                }
            ],
            Changes =
            [
                new ReceiptOcrChange
                {
                    Kind = ReceiptOcrChangeKind.PossibleEdit,
                    TrackId = "primary-display",
                    SourceId = "display-primary",
                    BeforeFrameId = beforeFrameId,
                    AfterFrameId = afterFrameId,
                    BeforeText = "Alex: The refund was approved yesterday.",
                    AfterText = "Alex: The refund is still under review.",
                    Similarity = 0.48d,
                    Explanation = "Local OCR found both added and removed text. Review the before and after frames.",
                    ReviewState = ReceiptChangeReviewState.Pending
                }
            ]
        };
        await File.WriteAllTextAsync(
            Path.Combine(fixtureRoot, "local-analysis", ReceiptSceneAnalysisService.AnalysisFileName),
            JsonSerializer.Serialize(
                analysis,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
        return afterPath;
    }

    private static void DrawConversationFrame(string path, bool edited)
    {
        using var bitmap = new Bitmap(1280, 720);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(7, 15, 22));
        using var sidebar = new SolidBrush(Color.FromArgb(12, 28, 37));
        using var panel = new SolidBrush(Color.FromArgb(18, 39, 49));
        using var bubble = new SolidBrush(Color.FromArgb(25, 72, 83));
        using var accent = new SolidBrush(Color.FromArgb(45, 220, 205));
        using var ink = new SolidBrush(Color.FromArgb(235, 244, 246));
        using var muted = new SolidBrush(Color.FromArgb(148, 177, 184));
        using var titleFont = new Font("Segoe UI", 22, System.Drawing.FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 19, System.Drawing.FontStyle.Regular);
        using var smallFont = new Font("Segoe UI", 13, System.Drawing.FontStyle.Regular);
        graphics.FillRectangle(sidebar, 0, 0, 260, 720);
        graphics.FillRectangle(panel, 260, 0, 1020, 92);
        graphics.FillEllipse(accent, 28, 24, 42, 42);
        graphics.DrawString("Dispute review", titleFont, ink, 292, 25);
        graphics.DrawString("# support-case-1842", bodyFont, muted, 28, 105);
        graphics.DrawString("Alex", bodyFont, ink, 320, 170);
        graphics.FillRectangle(bubble, 320, 210, 760, 126);
        graphics.DrawString(
            edited
                ? "The refund is still under review."
                : "The refund was approved yesterday.",
            bodyFont,
            ink,
            new RectangleF(350, 242, 700, 58));
        graphics.DrawString(edited ? "Edited · 12:00:03 PM" : "12:00:01 PM", smallFont, muted, 350, 306);
        if (edited)
        {
            graphics.FillRectangle(accent, 320, 348, 760, 4);
            graphics.DrawString("Possible text edit detected locally", smallFont, accent, 320, 370);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    internal static void TryDeleteFixture(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A render-only fixture can be removed by normal temporary-file cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // A render-only fixture can be removed by normal temporary-file cleanup.
        }
    }
}

internal sealed record FrameExplorerRenderFixture(
    string RootPath,
    string PreviewFramePath,
    CaptureItem Item) : IDisposable
{
    public void Dispose() => FrameExplorerWindowRenderer.TryDeleteFixture(RootPath);
}

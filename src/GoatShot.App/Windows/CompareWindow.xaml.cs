using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace GoatShot.App.Windows;

public partial class CompareWindow : Window
{
    private static readonly Color DeletedTone = Color.FromRgb(0xFF, 0x6B, 0x81);
    private static readonly Color AddedTone = Color.FromRgb(0x30, 0xE6, 0xC3);

    public CompareWindow(CaptureItem before, CaptureItem after, CaptureComparisonResult comparison)
    {
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);

        VerdictText.Text = DescribeVerdict(comparison.Verdict);
        ExplanationText.Text = comparison.Explanation;
        MetricsText.Text = DescribeMetrics(comparison);
        BeforeCaptionText.Text = $"Before — {before.FileName} ({Created(before)})";
        AfterCaptionText.Text = $"After — {after.FileName} ({Created(after)})";

        LoadSide(before, BeforeImage, BeforeSurface, BeforeCanvas, comparison.BeforeHighlights, DeletedTone, "Removed text");
        LoadSide(after, AfterImage, AfterSurface, AfterCanvas, comparison.AfterHighlights, AddedTone, "Added text");
    }

    internal static string DescribeVerdict(CaptureComparisonVerdict verdict)
    {
        return verdict switch
        {
            CaptureComparisonVerdict.Addition => "Possible addition",
            CaptureComparisonVerdict.Deletion => "Possible deletion",
            CaptureComparisonVerdict.Edit => "Possible edit",
            CaptureComparisonVerdict.Identical => "No text changes",
            CaptureComparisonVerdict.BelowThreshold => "Differences below the noise threshold",
            _ => "Text not compared"
        };
    }

    internal static string DescribeMetrics(CaptureComparisonResult comparison)
    {
        var parts = new List<string>();
        if (comparison.Similarity is { } similarity)
        {
            parts.Add($"Text similarity: {similarity * 100:0}%");
        }

        parts.Add(comparison.PixelDiff switch
        {
            null => "Pixel comparison unavailable.",
            { DimensionsMatch: false } => "Pixel comparison skipped (different dimensions).",
            { } diff => $"Pixel difference: {diff.DifferencePercent:0.#}% of {diff.CellsTotal} cells."
        });

        return string.Join("  ·  ", parts);
    }

    private static string Created(CaptureItem item)
    {
        return item.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Sizes the pixel-space surface and draws the highlight boxes. The Viewbox owns all the
    /// scaling, so word rectangles land at raw OCR coordinates — same trick as the editor canvas.
    /// </summary>
    private static void LoadSide(
        CaptureItem item,
        Image image,
        Grid surface,
        Canvas canvas,
        IReadOnlyList<OcrRecognizedWord> highlights,
        Color tone,
        string highlightLabel)
    {
        BitmapSource? source = null;
        try
        {
            source = ImageInterop.LoadBitmapImage(item.FilePath);
        }
        catch (Exception ex) when (
            ex is IOException or NotSupportedException or UnauthorizedAccessException or FormatException)
        {
            // The comparison text panel still stands on its own if a file went missing or is
            // corrupt (WPF reports truncated images as FileFormatException, a FormatException).
        }

        if (source is null)
        {
            return;
        }

        image.Source = source;
        surface.Width = source.PixelWidth;
        surface.Height = source.PixelHeight;

        var strokeThickness = Math.Max(2d, source.PixelWidth / 800d);
        var stroke = new SolidColorBrush(tone);
        var fill = new SolidColorBrush(tone) { Opacity = 0.18 };
        stroke.Freeze();
        fill.Freeze();
        foreach (var word in highlights)
        {
            var overlay = new WpfRectangle
            {
                Width = Math.Max(1, word.Width + 6),
                Height = Math.Max(1, word.Height + 6),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                StrokeDashArray = [4d, 3d],
                Fill = fill
            };
            AutomationProperties.SetName(overlay, $"{highlightLabel}: {word.Text}");
            Canvas.SetLeft(overlay, word.X - 3);
            Canvas.SetTop(overlay, word.Y - 3);
            canvas.Children.Add(overlay);
        }
    }
}

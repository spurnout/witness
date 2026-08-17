using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public enum CaptureComparisonVerdict
{
    Identical,
    BelowThreshold,
    Addition,
    Deletion,
    Edit,
    MissingOcr
}

public sealed record PixelGridDiffResult(
    bool DimensionsMatch,
    int CellsChanged,
    int CellsTotal,
    double? DifferencePercent);

/// <summary>
/// Coarse cell-grid pixel comparison over BGRA buffers. Cells, not pixels, so a moved cursor or
/// re-rendered antialiasing reads as a small percentage instead of lighting up the whole frame.
/// </summary>
public static class PixelGridDiff
{
    public static PixelGridDiffResult Compute(
        byte[] bgraBefore,
        int beforeWidth,
        int beforeHeight,
        byte[] bgraAfter,
        int afterWidth,
        int afterHeight,
        int gridCells = 16,
        int channelThreshold = 12)
    {
        if (beforeWidth != afterWidth ||
            beforeHeight != afterHeight ||
            beforeWidth <= 0 ||
            beforeHeight <= 0)
        {
            return new PixelGridDiffResult(false, 0, 0, null);
        }

        gridCells = Math.Clamp(gridCells, 1, 64);
        var cellsChanged = 0;
        var cellsTotal = gridCells * gridCells;
        for (var cellY = 0; cellY < gridCells; cellY++)
        {
            var startY = cellY * beforeHeight / gridCells;
            var endY = Math.Max(startY + 1, (cellY + 1) * beforeHeight / gridCells);
            for (var cellX = 0; cellX < gridCells; cellX++)
            {
                var startX = cellX * beforeWidth / gridCells;
                var endX = Math.Max(startX + 1, (cellX + 1) * beforeWidth / gridCells);
                if (IsCellChanged(bgraBefore, bgraAfter, beforeWidth, startX, endX, startY, endY, channelThreshold))
                {
                    cellsChanged++;
                }
            }
        }

        return new PixelGridDiffResult(
            true,
            cellsChanged,
            cellsTotal,
            cellsChanged * 100d / cellsTotal);
    }

    /// <summary>
    /// File-based convenience over <see cref="Compute"/>. Null when either image cannot be
    /// loaded — the OCR comparison still stands on its own without a pixel layer.
    /// </summary>
    public static PixelGridDiffResult? TryComputeFromFiles(string beforePath, string afterPath)
    {
        try
        {
            var (beforePixels, beforeWidth, beforeHeight) = LoadBgra(beforePath);
            var (afterPixels, afterWidth, afterHeight) = LoadBgra(afterPath);
            return Compute(beforePixels, beforeWidth, beforeHeight, afterPixels, afterWidth, afterHeight);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or ExternalException)
        {
            // GDI+ reports unreadable/corrupt images as ArgumentException or OutOfMemoryException.
            return null;
        }
    }

    private static (byte[] Pixels, int Width, int Height) LoadBgra(string path)
    {
        using var bitmap = new Bitmap(path);
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            if (data.Stride == bitmap.Width * 4)
            {
                return (pixels, bitmap.Width, bitmap.Height);
            }

            // Compact away stride padding so Compute can treat the buffer as densely packed.
            var packed = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Buffer.BlockCopy(pixels, y * data.Stride, packed, y * bitmap.Width * 4, bitmap.Width * 4);
            }

            return (packed, bitmap.Width, bitmap.Height);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static bool IsCellChanged(
        byte[] before,
        byte[] after,
        int width,
        int startX,
        int endX,
        int startY,
        int endY,
        int channelThreshold)
    {
        // A sparse sample lattice keeps a full-screen compare cheap; four samples per axis is
        // enough to catch any change that covers a meaningful part of the cell.
        var stepX = Math.Max(1, (endX - startX) / 4);
        var stepY = Math.Max(1, (endY - startY) / 4);
        long delta = 0;
        var samples = 0;
        for (var y = startY; y < endY; y += stepY)
        {
            for (var x = startX; x < endX; x += stepX)
            {
                var offset = ((y * width) + x) * 4;
                delta += Math.Abs(before[offset] - after[offset]) +
                    Math.Abs(before[offset + 1] - after[offset + 1]) +
                    Math.Abs(before[offset + 2] - after[offset + 2]);
                samples++;
            }
        }

        return samples > 0 && delta / (double)(samples * 3) > channelThreshold;
    }
}

public sealed record CaptureComparisonResult(
    CaptureComparisonVerdict Verdict,
    string Explanation,
    double? Similarity,
    IReadOnlySet<string> AddedTokens,
    IReadOnlySet<string> DeletedTokens,
    IReadOnlyList<OcrRecognizedWord> BeforeHighlights,
    IReadOnlyList<OcrRecognizedWord> AfterHighlights,
    PixelGridDiffResult? PixelDiff);

/// <summary>
/// Pure comparison of two OCR'd captures. The verdict comes from the replay analysis comparer;
/// the token sets are recomputed with the same tokenizer so the highlight boxes can never drift
/// from the verdict's own arithmetic.
/// </summary>
public static class CaptureComparisonService
{
    public static CaptureComparisonResult Compare(
        string? beforeText,
        IReadOnlyList<OcrRecognizedWord> beforeWords,
        string? afterText,
        IReadOnlyList<OcrRecognizedWord> afterWords,
        PixelGridDiffResult? pixelDiff)
    {
        if (string.IsNullOrWhiteSpace(beforeText) || string.IsNullOrWhiteSpace(afterText))
        {
            return new CaptureComparisonResult(
                CaptureComparisonVerdict.MissingOcr,
                "One or both captures have no recognized text to compare.",
                null,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                [],
                [],
                pixelDiff);
        }

        var beforeTokens = ReceiptSceneAnalysisService.TokenizeForComparison(beforeText);
        var afterTokens = ReceiptSceneAnalysisService.TokenizeForComparison(afterText);
        var added = afterTokens.Except(beforeTokens, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var deleted = beforeTokens.Except(afterTokens, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        var change = ReceiptSceneAnalysisService.CompareTexts(beforeText, afterText);
        var (verdict, explanation, similarity) = change is not null
            ? (MapKind(change.Kind), change.Explanation, change.Similarity)
            : added.Count == 0 && deleted.Count == 0
                ? (CaptureComparisonVerdict.Identical, "The recognized text is the same in both captures.", (double?)null)
                : (CaptureComparisonVerdict.BelowThreshold,
                    "Text differences are below the OCR noise threshold; they may be recognition wobble.",
                    (double?)null);

        return new CaptureComparisonResult(
            verdict,
            explanation,
            similarity,
            added,
            deleted,
            MatchWords(beforeWords, deleted),
            MatchWords(afterWords, added),
            pixelDiff);
    }

    private static CaptureComparisonVerdict MapKind(ReceiptOcrChangeKind kind)
    {
        return kind switch
        {
            ReceiptOcrChangeKind.PossibleAddition => CaptureComparisonVerdict.Addition,
            ReceiptOcrChangeKind.PossibleDeletion => CaptureComparisonVerdict.Deletion,
            _ => CaptureComparisonVerdict.Edit
        };
    }

    /// <summary>
    /// Token→box projection by normalized word text. Repeated tokens highlight every instance on
    /// their side — token sets carry no position, so v1 accepts the over-highlight.
    /// </summary>
    private static IReadOnlyList<OcrRecognizedWord> MatchWords(
        IReadOnlyList<OcrRecognizedWord> words,
        IReadOnlySet<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return [];
        }

        return words
            .Where(word => tokens.Contains(NormalizeWordToken(word.Text)))
            .ToList();
    }

    private static string NormalizeWordToken(string text)
    {
        return new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}

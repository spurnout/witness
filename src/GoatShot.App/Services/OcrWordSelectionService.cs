using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// Live-text hit testing: which OCR words a drag rectangle selects and the text they spell.
/// Coordinates are source-image pixels on both sides, so no DPI or zoom math belongs here.
/// </summary>
public static class OcrWordSelectionService
{
    public sealed record OcrWordSelection(IReadOnlyList<OcrRecognizedWord> Words, string Text);

    public static OcrWordSelection Resolve(
        IReadOnlyList<OcrRecognizedWord> words,
        double x,
        double y,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0 || words.Count == 0)
        {
            return new OcrWordSelection([], string.Empty);
        }

        var right = x + width;
        var bottom = y + height;
        var selected = words
            .Where(word =>
                word.Width > 0 &&
                word.Height > 0 &&
                word.X < right &&
                word.X + word.Width > x &&
                word.Y < bottom &&
                word.Y + word.Height > y)
            .OrderBy(word => word.LineIndex)
            .ThenBy(word => word.StartIndex)
            .ToList();

        if (selected.Count == 0)
        {
            return new OcrWordSelection([], string.Empty);
        }

        var text = string.Join(
            Environment.NewLine,
            selected
                .GroupBy(word => word.LineIndex)
                .Select(line => string.Join(' ', line.Select(word => word.Text))));

        return new OcrWordSelection(selected, text);
    }
}

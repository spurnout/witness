using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class SensitiveRegionReviewPlanner
{
    private const int Padding = 4;

    public static SensitiveRegionReviewResult Build(CaptureItem item)
    {
        if (string.IsNullOrWhiteSpace(item.OcrText) || item.OcrWords.Count == 0)
        {
            return new SensitiveRegionReviewResult
            {
                Succeeded = false,
                Message = "Run OCR on this capture before reviewing sensitive regions."
            };
        }

        var scan = SensitiveTextDetector.Scan(item.OcrText);
        if (scan.Findings.Count == 0)
        {
            return new SensitiveRegionReviewResult
            {
                Succeeded = false,
                SensitiveScan = scan,
                Message = "No sensitive OCR text was detected."
            };
        }

        var boxes = BuildBoxes(item, scan.Findings).ToList();
        return new SensitiveRegionReviewResult
        {
            Succeeded = boxes.Count > 0,
            SensitiveScan = scan,
            Boxes = boxes,
            Message = boxes.Count == 0
                ? "Sensitive text was detected, but no OCR word boxes overlapped the findings."
                : $"{scan.Summary} Review {boxes.Count} region(s) before flattening redactions."
        };
    }

    public static IReadOnlyList<SensitiveRegionReviewBox> BuildBoxes(
        CaptureItem item,
        IReadOnlyList<SensitiveFinding> findings)
    {
        var boxes = new List<SensitiveRegionReviewBox>();
        foreach (var finding in findings)
        {
            var findingStart = finding.StartIndex;
            var findingEnd = finding.StartIndex + finding.Length;
            var overlappingWords = item.OcrWords
                .Where(word => Overlaps(word.StartIndex, word.Length, findingStart, findingEnd))
                .OrderBy(word => word.LineIndex)
                .ThenBy(word => word.X)
                .ToList();

            foreach (var lineGroup in overlappingWords.GroupBy(word => word.LineIndex))
            {
                var left = lineGroup.Min(word => word.X);
                var top = lineGroup.Min(word => word.Y);
                var right = lineGroup.Max(word => word.X + word.Width);
                var bottom = lineGroup.Max(word => word.Y + word.Height);
                boxes.Add(new SensitiveRegionReviewBox
                {
                    Kind = finding.Kind,
                    Preview = finding.Preview,
                    LineIndex = lineGroup.Key,
                    X = Math.Max(0, left - Padding),
                    Y = Math.Max(0, top - Padding),
                    Width = Math.Max(1, right - left + Padding * 2),
                    Height = Math.Max(1, bottom - top + Padding * 2)
                });
            }
        }

        return boxes;
    }

    private static bool Overlaps(int wordStart, int wordLength, int findingStart, int findingEnd)
    {
        var wordEnd = wordStart + wordLength;
        return wordStart < findingEnd && wordEnd > findingStart;
    }
}

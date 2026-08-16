using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureComparisonServiceTests
{
    [TestMethod]
    public void Compare_ClassifiesEditWithTokenSetsAndBoxes()
    {
        var beforeWords = Words(("invoice", 0), ("total", 100), ("100", 200));
        var afterWords = Words(("invoice", 0), ("total", 100), ("250", 200));

        var result = CaptureComparisonService.Compare(
            "invoice total 100", beforeWords,
            "invoice total 250", afterWords,
            pixelDiff: null);

        Assert.AreEqual(CaptureComparisonVerdict.Edit, result.Verdict);
        CollectionAssert.AreEquivalent(new[] { "250" }, result.AddedTokens.ToArray());
        CollectionAssert.AreEquivalent(new[] { "100" }, result.DeletedTokens.ToArray());
        Assert.AreEqual(1, result.BeforeHighlights.Count);
        Assert.AreEqual("100", result.BeforeHighlights[0].Text);
        Assert.AreEqual(1, result.AfterHighlights.Count);
        Assert.AreEqual("250", result.AfterHighlights[0].Text);
        Assert.IsNotNull(result.Similarity);
    }

    [TestMethod]
    public void Compare_MapsRepeatedTokensToEveryInstance()
    {
        var afterWords = Words(("250", 0), ("apples", 60), ("250", 120));

        var result = CaptureComparisonService.Compare(
            "apples", Words(("apples", 60)),
            "250 apples 250", afterWords,
            pixelDiff: null);

        Assert.AreEqual(2, result.AfterHighlights.Count);
        Assert.IsTrue(result.AfterHighlights.All(word => word.Text == "250"));
    }

    [TestMethod]
    public void Compare_ReturnsIdenticalForWhitespaceAndCaseDifferences()
    {
        var result = CaptureComparisonService.Compare(
            "Invoice   Total", Words(("Invoice", 0), ("Total", 80)),
            "invoice total", Words(("invoice", 0), ("total", 80)),
            pixelDiff: null);

        Assert.AreEqual(CaptureComparisonVerdict.Identical, result.Verdict);
        Assert.AreEqual(0, result.AddedTokens.Count);
        Assert.AreEqual(0, result.BeforeHighlights.Count);
    }

    [TestMethod]
    public void Compare_ReturnsBelowThresholdForTinyNoise()
    {
        const string common = "alpha bravo charlie delta echo foxtrot golf hotel india juliet " +
            "kilo lima mike november oscar papa quebec romeo sierra tango";

        var result = CaptureComparisonService.Compare(
            $"{common} one", Words(("one", 0)),
            $"{common} two", Words(("two", 0)),
            pixelDiff: null);

        // CompareTexts suppresses this as OCR wobble, but the tokens still differ — the compare
        // window reports it as below-threshold rather than pretending the texts match.
        Assert.AreEqual(CaptureComparisonVerdict.BelowThreshold, result.Verdict);
        CollectionAssert.AreEquivalent(new[] { "two" }, result.AddedTokens.ToArray());
        CollectionAssert.AreEquivalent(new[] { "one" }, result.DeletedTokens.ToArray());
    }

    [TestMethod]
    public void Compare_ReturnsMissingOcrWhenEitherSideHasNoText()
    {
        var result = CaptureComparisonService.Compare(
            "   ", [],
            "invoice", Words(("invoice", 0)),
            pixelDiff: null);

        Assert.AreEqual(CaptureComparisonVerdict.MissingOcr, result.Verdict);
        Assert.AreEqual(0, result.AfterHighlights.Count);
    }

    [TestMethod]
    public void PixelGridDiff_ReportsZeroForIdenticalBuffers()
    {
        var buffer = FilledBuffer(64, 64, 0x40);

        var result = PixelGridDiff.Compute(buffer, 64, 64, (byte[])buffer.Clone(), 64, 64);

        Assert.IsTrue(result.DimensionsMatch);
        Assert.AreEqual(0, result.CellsChanged);
        Assert.AreEqual(0d, result.DifferencePercent);
    }

    [TestMethod]
    public void PixelGridDiff_CountsChangedCells()
    {
        var before = FilledBuffer(64, 64, 0x40);
        var after = FilledBuffer(64, 64, 0x40);
        // Brighten the top-left 32x32 quadrant: exactly one quarter of the 16x16 grid.
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var offset = ((y * 64) + x) * 4;
                after[offset] = 0xF0;
                after[offset + 1] = 0xF0;
                after[offset + 2] = 0xF0;
            }
        }

        var result = PixelGridDiff.Compute(before, 64, 64, after, 64, 64);

        Assert.AreEqual(256, result.CellsTotal);
        Assert.AreEqual(64, result.CellsChanged);
        Assert.AreEqual(25d, result.DifferencePercent);
    }

    [TestMethod]
    public void PixelGridDiff_SkipsWhenDimensionsDiffer()
    {
        var result = PixelGridDiff.Compute(
            FilledBuffer(64, 64, 0x40), 64, 64,
            FilledBuffer(32, 32, 0x40), 32, 32);

        Assert.IsFalse(result.DimensionsMatch);
        Assert.IsNull(result.DifferencePercent);
    }

    private static List<OcrRecognizedWord> Words(params (string Text, double X)[] entries)
    {
        return entries
            .Select(entry => new OcrRecognizedWord
            {
                Text = entry.Text,
                Length = entry.Text.Length,
                X = entry.X,
                Y = 10,
                Width = 50,
                Height = 18
            })
            .ToList();
    }

    private static byte[] FilledBuffer(int width, int height, byte value)
    {
        var buffer = new byte[width * height * 4];
        for (var i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = value;
            buffer[i + 1] = value;
            buffer[i + 2] = value;
            buffer[i + 3] = 0xFF;
        }

        return buffer;
    }
}

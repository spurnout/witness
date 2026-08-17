using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class OcrWordSelectionServiceTests
{
    [TestMethod]
    public void Resolve_ReturnsIntersectingWordsInReadingOrder()
    {
        // Deliberately shuffled input: reading order must come from LineIndex + StartIndex,
        // not from list order.
        var words = new List<OcrRecognizedWord>
        {
            Word("delta", line: 1, start: 20, x: 80, y: 30),
            Word("alpha", line: 0, start: 0, x: 0, y: 0),
            Word("charlie", line: 1, start: 12, x: 0, y: 30),
            Word("bravo", line: 0, start: 6, x: 60, y: 0)
        };

        var selection = OcrWordSelectionService.Resolve(words, 0, 0, 140, 55);

        Assert.AreEqual($"alpha bravo{Environment.NewLine}charlie delta", selection.Text);
        Assert.AreEqual(4, selection.Words.Count);
    }

    [TestMethod]
    public void Resolve_JoinsSameLineWordsWithSpaces()
    {
        var words = new List<OcrRecognizedWord>
        {
            Word("hello", line: 0, start: 0, x: 0, y: 0),
            Word("world", line: 0, start: 6, x: 60, y: 0)
        };

        var selection = OcrWordSelectionService.Resolve(words, 0, 0, 200, 25);

        Assert.AreEqual("hello world", selection.Text);
    }

    [TestMethod]
    public void Resolve_ReturnsEmptyForNoIntersection()
    {
        var words = new List<OcrRecognizedWord> { Word("lonely", line: 0, start: 0, x: 0, y: 0) };

        var selection = OcrWordSelectionService.Resolve(words, 500, 500, 100, 100);

        Assert.AreEqual(0, selection.Words.Count);
        Assert.AreEqual(string.Empty, selection.Text);
    }

    [TestMethod]
    public void Resolve_IncludesPartiallyOverlappedWords()
    {
        // The rect clips only the right half of the word; a live-text drag should still take it.
        var words = new List<OcrRecognizedWord> { Word("partial", line: 0, start: 0, x: 100, y: 0) };

        var selection = OcrWordSelectionService.Resolve(words, 125, 5, 300, 40);

        Assert.AreEqual("partial", selection.Text);
    }

    [TestMethod]
    public void Resolve_IgnoresZeroAreaSelections()
    {
        var words = new List<OcrRecognizedWord> { Word("word", line: 0, start: 0, x: 0, y: 0) };

        Assert.AreEqual(0, OcrWordSelectionService.Resolve(words, 10, 10, 0, 0).Words.Count);
    }

    private static OcrRecognizedWord Word(string text, int line, int start, double x, double y)
    {
        return new OcrRecognizedWord
        {
            Text = text,
            LineIndex = line,
            StartIndex = start,
            Length = text.Length,
            X = x,
            Y = y,
            Width = 50,
            Height = 20
        };
    }
}

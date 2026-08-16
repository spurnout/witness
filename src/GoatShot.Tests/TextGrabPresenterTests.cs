using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class TextGrabPresenterTests
{
    [TestMethod]
    public void Compose_CopiesRawTextWhenNothingSensitiveFound()
    {
        var payload = TextGrabPresenter.Compose(Result("hello capture world", words: 3));

        Assert.IsTrue(payload.HasText);
        Assert.IsFalse(payload.Redacted);
        Assert.AreEqual("hello capture world", payload.ClipboardText);
        StringAssert.Contains(payload.StatusMessage, "3 word(s)");
    }

    [TestMethod]
    public void Compose_SubstitutesRedactedTextWhenSensitiveValuesDetected()
    {
        var payload = TextGrabPresenter.Compose(Result("contact test@example.com today", words: 3));

        Assert.IsTrue(payload.HasText);
        Assert.IsTrue(payload.Redacted);
        StringAssert.Contains(payload.ClipboardText, "[REDACTED:");
        Assert.IsFalse(payload.ClipboardText.Contains("test@example.com", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(payload.StatusMessage, "Redacted text copied to clipboard.");
    }

    [TestMethod]
    public void Compose_ReportsFailureWithoutText()
    {
        var payload = TextGrabPresenter.Compose(new OcrRecognitionResult
        {
            Succeeded = false,
            Message = "Windows OCR is unavailable for the requested language on this device."
        });

        Assert.IsFalse(payload.HasText);
        Assert.AreEqual(string.Empty, payload.ClipboardText);
        StringAssert.Contains(payload.StatusMessage, "unavailable");
    }

    [TestMethod]
    public void Compose_ReportsEmptyRecognitionAsNoText()
    {
        var payload = TextGrabPresenter.Compose(Result("   ", words: 0));

        Assert.IsFalse(payload.HasText);
        Assert.AreEqual("No text found in the selected region.", payload.StatusMessage);
    }

    private static OcrRecognitionResult Result(string text, int words)
    {
        return new OcrRecognitionResult
        {
            Succeeded = true,
            Text = text,
            Message = "OCR completed.",
            Words = Enumerable.Range(0, words)
                .Select(i => new OcrRecognizedWord { Text = $"w{i}" })
                .ToList()
        };
    }
}

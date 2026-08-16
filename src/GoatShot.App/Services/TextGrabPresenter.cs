using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// Turns an OCR result into what the text-grab hotkey should copy and say. Redaction mirrors the
/// stored-OCR path: recognized text that trips the sensitive scan never reaches the clipboard raw.
/// </summary>
public static class TextGrabPresenter
{
    public sealed record TextGrabPayload(bool HasText, string ClipboardText, string StatusMessage, bool Redacted);

    public static TextGrabPayload Compose(OcrRecognitionResult result)
    {
        if (!result.Succeeded)
        {
            return new TextGrabPayload(false, string.Empty, result.Message, false);
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return new TextGrabPayload(false, string.Empty, "No text found in the selected region.", false);
        }

        var scan = SensitiveTextDetector.Scan(result.Text);
        if (scan.Findings.Count > 0)
        {
            return new TextGrabPayload(
                true,
                scan.RedactedText,
                $"{scan.Summary} Redacted text copied to clipboard.",
                true);
        }

        return new TextGrabPayload(
            true,
            result.Text,
            $"Copied {result.Words.Count} word(s) of recognized text.",
            false);
    }
}

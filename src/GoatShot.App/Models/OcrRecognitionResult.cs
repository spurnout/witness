namespace GoatShot.App.Models;

public sealed class OcrRecognitionResult
{
    public bool Succeeded { get; set; }
    public string Text { get; set; } = string.Empty;
    public string LanguageTag { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public List<OcrRecognizedWord> Words { get; set; } = new();
}

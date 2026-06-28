namespace GoatShot.App.Models;

public sealed class OcrRecognizedWord
{
    public string Text { get; set; } = string.Empty;
    public int LineIndex { get; set; }
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double? Confidence { get; set; }
}

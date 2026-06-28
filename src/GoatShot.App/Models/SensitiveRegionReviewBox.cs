namespace GoatShot.App.Models;

public sealed class SensitiveRegionReviewBox
{
    public string Kind { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public int LineIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

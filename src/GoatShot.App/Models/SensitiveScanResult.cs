namespace GoatShot.App.Models;

public sealed class SensitiveScanResult
{
    public List<SensitiveFinding> Findings { get; set; } = new();
    public string RedactedText { get; set; } = string.Empty;
    public string Summary { get; set; } = "No sensitive data detected.";
}

namespace GoatShot.App.Models;

public sealed class SensitiveRegionReviewResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public SensitiveScanResult? SensitiveScan { get; set; }
    public List<SensitiveRegionReviewBox> Boxes { get; set; } = new();
}

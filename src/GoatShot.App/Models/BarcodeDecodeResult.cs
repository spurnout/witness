namespace GoatShot.App.Models;

public sealed class BarcodeDecodeResult
{
    public string SourcePath { get; set; } = string.Empty;
    public List<BarcodeDecodeItem> Items { get; set; } = new();
    public string Message { get; set; } = "No QR or barcode value found.";

    public bool Succeeded => Items.Count > 0;
}

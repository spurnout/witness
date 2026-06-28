using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

public sealed class BarcodeDecodeItem
{
    public string Format { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<BarcodePoint> Points { get; set; } = new();

    [JsonIgnore]
    public bool IsHttpUrl =>
        Uri.TryCreate(Text, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}

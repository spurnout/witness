using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

public sealed class SensitiveFinding
{
    public string Kind { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
    public string Preview { get; set; } = string.Empty;

    [JsonIgnore]
    public string Value { get; set; } = string.Empty;
}

namespace GoatShot.App.Models;

public sealed class ShareResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Url { get; set; }
}

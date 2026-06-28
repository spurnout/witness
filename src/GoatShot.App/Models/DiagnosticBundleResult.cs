namespace GoatShot.App.Models;

public sealed class DiagnosticBundleResult
{
    public bool Succeeded { get; set; }
    public string? Path { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Entries { get; set; } = new();
}

namespace GoatShot.App.Models;

public sealed class DocumentationPacketResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string PacketDirectory { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string IndexPath { get; set; } = string.Empty;
    public string? BugReportPath { get; set; }
    public List<string> LinkedFiles { get; set; } = new();
}

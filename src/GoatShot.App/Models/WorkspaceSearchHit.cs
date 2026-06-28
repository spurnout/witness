namespace GoatShot.App.Models;

public sealed class WorkspaceSearchHit
{
    public string Id { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}

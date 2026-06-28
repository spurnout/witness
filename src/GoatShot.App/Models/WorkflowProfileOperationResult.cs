namespace GoatShot.App.Models;

public sealed class WorkflowProfileOperationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }
}

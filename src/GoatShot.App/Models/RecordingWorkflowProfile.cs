namespace GoatShot.App.Models;

public sealed class RecordingWorkflowProfile
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RecordingSettings Settings { get; set; } = new();
}

public sealed class WorkflowProfileRecording
{
    public RecordingSettings DefaultSettings { get; set; } = new();
    public List<RecordingWorkflowProfile> Profiles { get; set; } = new();
}

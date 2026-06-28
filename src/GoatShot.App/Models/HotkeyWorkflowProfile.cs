namespace GoatShot.App.Models;

public sealed class HotkeyWorkflowProfile
{
    public string Name { get; set; } = string.Empty;
    public HotkeyAction Action { get; set; }
    public string ShareDestination { get; set; } = string.Empty;
    public bool RunOcr { get; set; }
    public bool DetectSensitiveData { get; set; }
    public bool AskBeforeUpload { get; set; } = true;
}

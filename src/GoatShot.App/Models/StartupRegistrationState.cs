namespace GoatShot.App.Models;

public sealed class StartupRegistrationState
{
    public bool IsRegistered { get; set; }
    public bool IsCurrentCommand { get; set; }
    public string? Command { get; set; }
    public string Message { get; set; } = string.Empty;
}

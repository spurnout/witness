namespace GoatShot.App.Models;

public sealed class StartupRegistrationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
}

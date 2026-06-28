namespace GoatShot.App.Models;

public sealed class ProviderWorkflowProfile
{
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public bool PreferPublicLink { get; set; }
    public bool RequireExpiration { get; set; }
    public TimeSpan? Expiration { get; set; }
    public bool RequirePassword { get; set; }
}

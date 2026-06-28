namespace GoatShot.App.Models;

public sealed class OAuthSettings
{
    public bool UseAuthorizationCodeFlow { get; set; } = true;
    public int LocalCallbackPort { get; set; } = 53628;
    public List<OAuthProviderSettings> Providers { get; set; } = new();
}

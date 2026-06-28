namespace GoatShot.App.Models;

public sealed class OAuthProviderSettings
{
    public string ProviderName { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public bool UsePkce { get; set; } = true;
    public bool RefreshTokenStored { get; set; }
}

namespace GoatShot.App.Models;

public sealed class OAuthStoredToken
{
    public string ProviderName { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public bool HasUsableAccessToken(TimeSpan refreshSkew)
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            return false;
        }

        return ExpiresAt is null || ExpiresAt.Value > DateTimeOffset.Now.Add(refreshSkew);
    }
}

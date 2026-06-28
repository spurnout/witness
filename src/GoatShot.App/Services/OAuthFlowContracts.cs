using GoatShot.App.Models;
using System.Security.Cryptography;

namespace GoatShot.App.Services;

public interface IOAuthFlow
{
    string ProviderName { get; }
    Uri BuildAuthorizationUri(OAuthProviderSettings settings, string state, Uri callbackUri);
    Task<OAuthTokenResult> ExchangeCodeAsync(OAuthProviderSettings settings, string code, Uri callbackUri, CancellationToken cancellationToken);
    Task<OAuthTokenResult> RefreshAsync(OAuthProviderSettings settings, string refreshToken, CancellationToken cancellationToken);
}

public sealed record OAuthTokenResult(
    bool Succeeded,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string Message);

public sealed record OAuthPkcePair(string CodeVerifier, string CodeChallenge, string CodeChallengeMethod)
{
    public static OAuthPkcePair Create()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);
        var challenge = Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        return new OAuthPkcePair(verifier, challenge, "S256");
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed record OAuthCallbackResult(
    bool Succeeded,
    string? Code,
    string? State,
    string Message);

public sealed record OAuthInteractiveResult(
    bool Succeeded,
    OAuthTokenResult? Token,
    Uri? AuthorizationUri,
    Uri? CallbackUri,
    string Message);

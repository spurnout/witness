using System.Net.Http;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class OAuthFlowService : IOAuthFlow
{
    private readonly HttpClient _httpClient;

    public OAuthFlowService()
        : this(new HttpClient())
    {
    }

    public OAuthFlowService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string ProviderName => "Generic OAuth";

    public Uri BuildAuthorizationUri(OAuthProviderSettings settings, string state, Uri callbackUri)
    {
        return BuildAuthorizationUri(settings, state, callbackUri, pkce: null);
    }

    public Uri BuildAuthorizationUri(OAuthProviderSettings settings, string state, Uri callbackUri, OAuthPkcePair? pkce)
    {
        if (string.IsNullOrWhiteSpace(settings.AuthorizationEndpoint))
        {
            throw new ArgumentException("OAuth authorization endpoint is not configured.", nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new ArgumentException("OAuth client ID is not configured.", nameof(settings));
        }

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId.Trim(),
            ["redirect_uri"] = callbackUri.ToString(),
            ["response_type"] = "code",
            ["state"] = state
        };

        if (!string.IsNullOrWhiteSpace(settings.Scopes))
        {
            parameters["scope"] = settings.Scopes.Trim();
        }

        if (pkce is not null)
        {
            parameters["code_challenge"] = pkce.CodeChallenge;
            parameters["code_challenge_method"] = pkce.CodeChallengeMethod;
        }

        if (settings.ProviderName.Contains("Google", StringComparison.OrdinalIgnoreCase))
        {
            parameters["access_type"] = "offline";
            parameters["prompt"] = "consent";
        }

        return AppendQuery(new Uri(settings.AuthorizationEndpoint, UriKind.Absolute), parameters);
    }

    public Task<OAuthTokenResult> ExchangeCodeAsync(
        OAuthProviderSettings settings,
        string code,
        Uri callbackUri,
        CancellationToken cancellationToken)
    {
        return ExchangeCodeAsync(settings, code, callbackUri, codeVerifier: null, cancellationToken);
    }

    public Task<OAuthTokenResult> ExchangeCodeAsync(
        OAuthProviderSettings settings,
        string code,
        Uri callbackUri,
        string? codeVerifier,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUri.ToString(),
            ["client_id"] = settings.ClientId.Trim()
        };

        if (!string.IsNullOrWhiteSpace(codeVerifier))
        {
            form["code_verifier"] = codeVerifier;
        }

        return SendTokenRequestAsync(settings, form, cancellationToken);
    }

    public Task<OAuthTokenResult> RefreshAsync(
        OAuthProviderSettings settings,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = settings.ClientId.Trim()
        };

        return SendTokenRequestAsync(settings, form, cancellationToken);
    }

    private async Task<OAuthTokenResult> SendTokenRequestAsync(
        OAuthProviderSettings settings,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TokenEndpoint))
        {
            return new OAuthTokenResult(false, null, null, null, "OAuth token endpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return new OAuthTokenResult(false, null, null, null, "OAuth client ID is not configured.");
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(settings.TokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new OAuthTokenResult(
                false,
                null,
                null,
                null,
                $"OAuth token request failed: {(int)response.StatusCode} {response.ReasonPhrase} {ExtractError(body)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = ReadString(root, "access_token");
            var refreshToken = ReadString(root, "refresh_token");
            var expiresAt = ReadExpiresAt(root);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new OAuthTokenResult(false, null, refreshToken, expiresAt, "OAuth token response did not include an access token.");
            }

            return new OAuthTokenResult(true, accessToken, refreshToken, expiresAt, "OAuth token request succeeded.");
        }
        catch (JsonException ex)
        {
            return new OAuthTokenResult(false, null, null, null, $"OAuth token response was not valid JSON: {ex.Message}");
        }
    }

    private static Uri AppendQuery(Uri baseUri, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new UriBuilder(baseUri);
        var existing = string.IsNullOrWhiteSpace(builder.Query)
            ? string.Empty
            : builder.Query.TrimStart('?') + "&";
        builder.Query = existing + string.Join("&", parameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? ReadExpiresAt(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var value) ||
            !value.TryGetInt32(out var seconds) ||
            seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.Now.AddSeconds(seconds);
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return ReadString(root, "error_description") ??
                   ReadString(root, "error") ??
                   body;
        }
        catch
        {
            return body;
        }
    }
}

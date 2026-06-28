using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class OAuthBrowserFlowService
{
    private readonly OAuthFlowService _oauthFlow;
    private readonly OAuthCallbackServerService _callbackServer;

    public OAuthBrowserFlowService(OAuthFlowService oauthFlow, OAuthCallbackServerService callbackServer)
    {
        _oauthFlow = oauthFlow;
        _callbackServer = callbackServer;
    }

    public async Task<OAuthInteractiveResult> AuthorizeAsync(
        OAuthProviderSettings provider,
        int preferredPort,
        Action<Uri> openBrowser,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider.ClientId))
        {
            return new OAuthInteractiveResult(false, null, null, null, $"{provider.ProviderName} OAuth client ID is not configured.");
        }

        var state = Guid.NewGuid().ToString("N");
        var pkce = provider.UsePkce ? OAuthPkcePair.Create() : null;

        await using var session = _callbackServer.Start(state, preferredPort, timeout, cancellationToken);
        var authorizationUri = _oauthFlow.BuildAuthorizationUri(provider, state, session.CallbackUri, pkce);
        var callbackTask = session.WaitForCallbackAsync();

        try
        {
            openBrowser(authorizationUri);
        }
        catch (Exception ex)
        {
            return new OAuthInteractiveResult(false, null, authorizationUri, session.CallbackUri, $"Could not open the browser for OAuth authorization: {ex.Message}");
        }

        var callback = await callbackTask;
        if (!callback.Succeeded || string.IsNullOrWhiteSpace(callback.Code))
        {
            return new OAuthInteractiveResult(false, null, authorizationUri, session.CallbackUri, callback.Message);
        }

        var token = await _oauthFlow.ExchangeCodeAsync(
            provider,
            callback.Code,
            session.CallbackUri,
            pkce?.CodeVerifier,
            cancellationToken);

        return new OAuthInteractiveResult(
            token.Succeeded,
            token,
            authorizationUri,
            session.CallbackUri,
            token.Message);
    }
}

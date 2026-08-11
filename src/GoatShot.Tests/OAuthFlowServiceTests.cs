using System.Net;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class OAuthFlowServiceTests
{
    [TestMethod]
    public void BuildAuthorizationUri_IncludesProviderParameters()
    {
        var service = new OAuthFlowService(new HttpClient(new StubHandler("{}")));
        var settings = new OAuthProviderSettings
        {
            ProviderName = "Google Drive",
            ClientId = "client-123",
            AuthorizationEndpoint = "https://accounts.example.test/oauth2/auth",
            TokenEndpoint = "https://accounts.example.test/oauth2/token",
            Scopes = "files.write offline_access"
        };

        var uri = service.BuildAuthorizationUri(
            settings,
            "state-value",
            new Uri("http://127.0.0.1:53628/oauth/callback"));

        var query = Uri.UnescapeDataString(uri.Query);
        Assert.AreEqual("https://accounts.example.test/oauth2/auth", uri.GetLeftPart(UriPartial.Path));
        StringAssert.Contains(query, "client_id=client-123");
        StringAssert.Contains(query, "redirect_uri=http://127.0.0.1:53628/oauth/callback");
        StringAssert.Contains(query, "response_type=code");
        StringAssert.Contains(query, "state=state-value");
        StringAssert.Contains(query, "scope=files.write offline_access");
        StringAssert.Contains(query, "access_type=offline");
        StringAssert.Contains(query, "prompt=consent");
    }

    [TestMethod]
    public async Task ExchangeCodeAsync_PostsAuthorizationCodeAndParsesTokens()
    {
        var handler = new StubHandler(
            """
            {"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600}
            """);
        var service = new OAuthFlowService(new HttpClient(handler));

        var result = await service.ExchangeCodeAsync(
            CreateSettings(),
            "authorization-code",
            new Uri("http://127.0.0.1:53628/oauth/callback"),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("access-token", result.AccessToken);
        Assert.AreEqual("refresh-token", result.RefreshToken);
        Assert.IsNotNull(result.ExpiresAt);
        StringAssert.Contains(handler.Body, "grant_type=authorization_code");
        StringAssert.Contains(handler.Body, "code=authorization-code");
        StringAssert.Contains(handler.Body, "redirect_uri=http%3A%2F%2F127.0.0.1%3A53628%2Foauth%2Fcallback");
        StringAssert.Contains(handler.Body, "client_id=client-123");
    }

    [TestMethod]
    public async Task RefreshAsync_PostsRefreshTokenAndPreservesNoNewRefreshToken()
    {
        var handler = new StubHandler(
            """
            {"access_token":"new-access-token","expires_in":1200}
            """);
        var service = new OAuthFlowService(new HttpClient(handler));

        var result = await service.RefreshAsync(
            CreateSettings(),
            "refresh-token",
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("new-access-token", result.AccessToken);
        Assert.IsNull(result.RefreshToken);
        Assert.IsNotNull(result.ExpiresAt);
        StringAssert.Contains(handler.Body, "grant_type=refresh_token");
        StringAssert.Contains(handler.Body, "refresh_token=refresh-token");
        StringAssert.Contains(handler.Body, "client_id=client-123");
    }

    [TestMethod]
    public async Task CallbackServer_WaitsForLoopbackCodeAndVerifiesState()
    {
        var server = new OAuthCallbackServerService();
        await using var session = server.Start("expected-state", 0, TimeSpan.FromSeconds(5), CancellationToken.None);
        var wait = session.WaitForCallbackAsync();

        using var http = new HttpClient();
        await http.GetStringAsync(new Uri($"{session.CallbackUri}?code=callback-code&state=expected-state"));
        var result = await wait;

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("callback-code", result.Code);
        Assert.AreEqual("expected-state", result.State);
    }

    [TestMethod]
    public async Task CallbackServer_IgnoresWrongStateBeforeValidCallback()
    {
        var server = new OAuthCallbackServerService();
        await using var session = server.Start("expected-state", 0, TimeSpan.FromSeconds(5), CancellationToken.None);
        var wait = session.WaitForCallbackAsync();

        using var http = new HttpClient();
        var rejected = await http.GetAsync(new Uri($"{session.CallbackUri}?code=attacker-code&state=wrong-state"));
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, rejected.StatusCode);
        await http.GetAsync(new Uri($"{session.CallbackUri}?code=valid-code&state=expected-state"));

        var result = await wait;
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("valid-code", result.Code);
    }

    [TestMethod]
    public async Task BrowserFlow_AuthorizeAsync_UsesPkceCallbackAndTokenExchange()
    {
        var handler = new StubHandler(
            """
            {"access_token":"access-token","refresh_token":"refresh-token","expires_in":3600}
            """);
        var flow = new OAuthFlowService(new HttpClient(handler));
        var browserFlow = new OAuthBrowserFlowService(flow, new OAuthCallbackServerService());
        Uri? openedUri = null;

        var result = await browserFlow.AuthorizeAsync(
            CreateSettings(),
            0,
            authorizationUri =>
            {
                openedUri = authorizationUri;
                var callback = QueryValue(authorizationUri, "redirect_uri");
                var state = QueryValue(authorizationUri, "state");
                _ = Task.Run(async () =>
                {
                    using var http = new HttpClient();
                    await http.GetStringAsync(new Uri($"{callback}?code=browser-code&state={state}"));
                });
            },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsNotNull(result.Token);
        Assert.AreEqual("access-token", result.Token.AccessToken);
        Assert.IsNotNull(openedUri);
        Assert.IsFalse(string.IsNullOrWhiteSpace(QueryValue(openedUri, "code_challenge")));
        Assert.AreEqual("S256", QueryValue(openedUri, "code_challenge_method"));
        StringAssert.Contains(handler.Body, "grant_type=authorization_code");
        StringAssert.Contains(handler.Body, "code=browser-code");
        StringAssert.Contains(handler.Body, "code_verifier=");
    }

    [TestMethod]
    public async Task SecretStore_SaveOAuthToken_PersistsGenericAndProviderAccessToken()
    {
        await WithTempPathsAsync(paths =>
        {
            var store = new SecretStore(paths);
            store.SaveOAuthToken(
                "Google Drive",
                new OAuthTokenResult(
                    true,
                    "access-token",
                    "refresh-token",
                    DateTimeOffset.Now.AddHours(1),
                    "ok"));

            var token = store.ReadOAuthToken("Google Drive");

            Assert.IsTrue(store.HasOAuthToken("Google Drive"));
            Assert.IsNotNull(token);
            Assert.AreEqual("Google Drive", token.ProviderName);
            Assert.AreEqual("access-token", token.AccessToken);
            Assert.AreEqual("refresh-token", token.RefreshToken);
            Assert.AreEqual("access-token", store.ReadGoogleDriveAccessToken());

            store.ClearOAuthToken("Google Drive");

            Assert.IsFalse(store.HasOAuthToken("Google Drive"));
            Assert.IsFalse(store.HasGoogleDriveAccessToken);

            store.SaveOAuthToken(
                "Google Photos",
                new OAuthTokenResult(
                    true,
                    "google-photos-access-token",
                    "google-photos-refresh-token",
                    DateTimeOffset.Now.AddHours(1),
                    "ok"));
            Assert.IsTrue(store.HasOAuthToken("Google Photos"));
            Assert.AreEqual("google-photos-access-token", store.ReadGooglePhotosAccessToken());

            store.ClearOAuthToken("Google Photos");
            Assert.IsFalse(store.HasOAuthToken("Google Photos"));
            Assert.IsFalse(store.HasGooglePhotosAccessToken);

            store.SaveOAuthToken(
                "YouTube",
                new OAuthTokenResult(
                    true,
                    "youtube-access-token",
                    "youtube-refresh-token",
                    DateTimeOffset.Now.AddHours(1),
                    "ok"));
            Assert.IsTrue(store.HasOAuthToken("YouTube"));
            Assert.AreEqual("youtube-access-token", store.ReadYouTubeAccessToken());

            store.ClearOAuthToken("YouTube");
            Assert.IsFalse(store.HasOAuthToken("YouTube"));
            Assert.IsFalse(store.HasYouTubeAccessToken);

            store.SaveOAuthToken(
                "OneNote",
                new OAuthTokenResult(
                    true,
                    "onenote-access-token",
                    "onenote-refresh-token",
                    DateTimeOffset.Now.AddHours(1),
                    "ok"));
            Assert.IsTrue(store.HasOAuthToken("OneNote"));
            Assert.AreEqual("onenote-access-token", store.ReadOneNoteAccessToken());

            store.ClearOAuthToken("OneNote");
            Assert.IsFalse(store.HasOAuthToken("OneNote"));
            Assert.IsFalse(store.HasOneNoteAccessToken);
            return Task.CompletedTask;
        });
    }

    private static OAuthProviderSettings CreateSettings()
    {
        return new OAuthProviderSettings
        {
            ProviderName = "Google Drive",
            ClientId = "client-123",
            AuthorizationEndpoint = "https://accounts.example.test/oauth2/auth",
            TokenEndpoint = "https://accounts.example.test/oauth2/token",
            Scopes = "files.write"
        };
    }

    private static string QueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0].Replace('+', ' '));
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pieces.Length == 2
                ? Uri.UnescapeDataString(pieces[1].Replace('+', ' '))
                : string.Empty;
        }

        return string.Empty;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            var paths = AppPaths.Create(new AppSettings());
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody)
            };
        }
    }
}

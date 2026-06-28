using System.Net;
using System.Net.Sockets;
using System.Text;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ShareServiceProviderExpansionTests
{
    [TestMethod]
    public async Task GitHubIssues_CreatesIssueWithTokenAndRedactedBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync(
                """
                {"html_url":"https://github.example.test/owner/repo/issues/42"}
                """,
                HttpStatusCode.Created);
            var settings = new AppSettings
            {
                GitHubApiBaseUrl = server.BaseUri.ToString(),
                GitHubRepository = "owner/repo",
                GitHubIssueTitleTemplate = "Bug {file}",
                GitHubLabels = "bug, goatshot",
                GitHubAssignees = "octocat"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveGitHubToken("github-token");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "github-shot.png", 24);
            item.Notes = "password=super-secret";

            var result = await sharing.ShareAsync(item, ShareDestination.GitHubIssues, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://github.example.test/owner/repo/issues/42", result.Url);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            Assert.AreEqual("/repos/owner/repo/issues", server.Requests[0].Path);
            Assert.AreEqual("Bearer github-token", server.Requests[0].Authorization);
            StringAssert.Contains(server.Requests[0].BodyText, "Bug github-shot.png");
            StringAssert.Contains(server.Requests[0].BodyText, "goatshot");
            Assert.IsFalse(server.Requests[0].BodyText.Contains("super-secret", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(server.Requests[0].BodyText.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task Jira_CreatesIssueWithBasicAuthAndAdfDescription()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync(
                """
                {"key":"GOAT-42","self":"https://jira.example.test/rest/api/3/issue/10042"}
                """,
                HttpStatusCode.Created);
            var settings = new AppSettings
            {
                JiraBaseUrl = server.BaseUri.ToString(),
                JiraProjectKey = "GOAT",
                JiraIssueType = "Bug",
                JiraSummaryTemplate = "Jira {file}",
                JiraLabels = "goatshot,bug",
                JiraAccountEmail = "user@example.test"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveJiraApiToken("jira-token");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "jira-shot.png", 32);
            item.OcrText = "api_key=secret-value";

            var result = await sharing.ShareAsync(item, ShareDestination.Jira, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(new Uri(server.BaseUri, "browse/GOAT-42").ToString(), result.Url);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            Assert.AreEqual("/rest/api/3/issue", server.Requests[0].Path);
            StringAssert.StartsWith(server.Requests[0].Authorization, "Basic ");
            StringAssert.Contains(server.Requests[0].BodyText, "\"summary\":\"Jira jira-shot.png\"");
            StringAssert.Contains(server.Requests[0].BodyText, "\"type\":\"doc\"");
            StringAssert.Contains(server.Requests[0].BodyText, "\"key\":\"GOAT\"");
            Assert.IsFalse(server.Requests[0].BodyText.Contains("secret-value", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(server.Requests[0].BodyText.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task AzureDevOps_CreatesWorkItemWithJsonPatchAndPat()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync(
                """
                {"id":42,"_links":{"html":{"href":"https://dev.azure.com/org/project/_workitems/edit/42"}}}
                """,
                HttpStatusCode.OK);
            var settings = new AppSettings
            {
                AzureDevOpsBaseUrl = server.BaseUri.ToString(),
                AzureDevOpsOrganization = "org",
                AzureDevOpsProject = "project",
                AzureDevOpsWorkItemType = "Bug",
                AzureDevOpsTitleTemplate = "ADO {file}",
                AzureDevOpsTags = "goatshot,bug",
                AzureDevOpsAssignedTo = "user@example.test"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveAzureDevOpsPat("ado-token");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "ado-shot.png", 40);
            item.Notes = "token=secret-value";

            var result = await sharing.ShareAsync(item, ShareDestination.AzureDevOps, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://dev.azure.com/org/project/_workitems/edit/42", result.Url);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            Assert.AreEqual("/org/project/_apis/wit/workitems/$Bug", server.Requests[0].Path);
            StringAssert.Contains(server.Requests[0].ContentType, "application/json-patch+json");
            StringAssert.StartsWith(server.Requests[0].Authorization, "Basic ");
            StringAssert.Contains(server.Requests[0].BodyText, "\"path\":\"/fields/System.Title\"");
            StringAssert.Contains(server.Requests[0].BodyText, "ADO ado-shot.png");
            StringAssert.Contains(server.Requests[0].BodyText, "\"path\":\"/fields/System.Tags\"");
            StringAssert.Contains(server.Requests[0].BodyText, "goatshot; bug");
            StringAssert.Contains(server.Requests[0].BodyText, "\"path\":\"/fields/System.AssignedTo\"");
            Assert.IsFalse(server.Requests[0].BodyText.Contains("secret-value", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(server.Requests[0].BodyText.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task SlackWebhook_PostsRedactedNotificationPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("ok");
            var settings = new AppSettings
            {
                SlackWebhookUrl = server.BaseUri.ToString(),
                SlackMessageTemplate = "Ready {file} {bytes}"
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "slack-shot.png", 24);

            var result = await sharing.ShareAsync(item, ShareDestination.SlackWebhook, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            StringAssert.Contains(server.Requests[0].BodyText, "Ready slack-shot.png 24");
            StringAssert.Contains(result.Message, "notification sent");
        });
    }

    [TestMethod]
    public async Task TeamsWebhook_PostsAdaptiveCardNotificationPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("{}");
            var settings = new AppSettings
            {
                TeamsWebhookUrl = server.BaseUri.ToString(),
                TeamsMessageTemplate = "Teams {file}"
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "teams-shot.png", 32);

            var result = await sharing.ShareAsync(item, ShareDestination.MicrosoftTeamsWebhook, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            StringAssert.Contains(server.Requests[0].BodyText, "AdaptiveCard");
            StringAssert.Contains(server.Requests[0].BodyText, "Teams teams-shot.png");
        });
    }

    [TestMethod]
    public async Task DiscordWebhook_UploadsMultipartFile()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("{}");
            var settings = new AppSettings
            {
                DiscordWebhookUrl = server.BaseUri.ToString(),
                DiscordMessageTemplate = "Discord {file}"
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "discord-shot.png", 48);

            var result = await sharing.ShareAsync(item, ShareDestination.DiscordWebhook, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("POST", server.Requests[0].Method);
            StringAssert.Contains(server.Requests[0].ContentType, "multipart/form-data");
            StringAssert.Contains(server.Requests[0].BodyText, "Discord discord-shot.png");
            StringAssert.Contains(server.Requests[0].BodyText, "discord-shot.png");
        });
    }

    [TestMethod]
    public async Task OneDriveSmallFile_UsesSimpleContentUpload()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync(
                """
                {"id":"one-small","webUrl":"https://onedrive.example.test/small"}
                """,
                HttpStatusCode.Created);
            var settings = new AppSettings
            {
                OneDriveGraphApiBaseUrl = server.BaseUri.ToString(),
                OneDriveRemoteFolder = "/GoatShot"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveOneDriveAccessToken("one-token");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "onedrive-small.png", 64);

            var result = await sharing.ShareAsync(item, ShareDestination.OneDrive, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://onedrive.example.test/small", result.Url);
            StringAssert.Contains(result.Message, "small-file");
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("PUT", server.Requests[0].Method);
            StringAssert.Contains(server.Requests[0].Path, "/me/drive/root:");
            StringAssert.Contains(server.Requests[0].Path, "/GoatShot/");
            StringAssert.Contains(server.Requests[0].Path, "onedrive-small.png");
            StringAssert.Contains(server.Requests[0].Path, ":/content");
            Assert.AreEqual("Bearer one-token", server.Requests[0].Authorization);
            Assert.AreEqual("image/png", server.Requests[0].ContentType);
            Assert.AreEqual(64, server.Requests[0].Body.Length);
        });
    }

    [TestMethod]
    public async Task OneDriveLargeFile_UsesUploadSessionChunks()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync((baseUri, request, _) =>
            {
                if (request.Path.Contains("createUploadSession", StringComparison.OrdinalIgnoreCase))
                {
                    return new CapturedResponse(
                        HttpStatusCode.OK,
                        $$"""
                        {"uploadUrl":"{{new Uri(baseUri, "upload-session")}}"}
                        """);
                }

                if (request.Path.Equals("/upload-session", StringComparison.OrdinalIgnoreCase) &&
                    request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
                {
                    return request.ContentRange.StartsWith("bytes 0-", StringComparison.OrdinalIgnoreCase)
                        ? new CapturedResponse(
                            HttpStatusCode.Accepted,
                            """
                            {"nextExpectedRanges":["3276800-"]}
                            """)
                        : new CapturedResponse(
                            HttpStatusCode.Created,
                            """
                            {"id":"one-large","webUrl":"https://onedrive.example.test/large"}
                            """);
                }

                return new CapturedResponse(HttpStatusCode.NotFound, "{}");
            });
            var settings = new AppSettings
            {
                OneDriveGraphApiBaseUrl = server.BaseUri.ToString(),
                OneDriveRemoteFolder = "/GoatShot"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveOneDriveAccessToken("one-token");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "onedrive-large.png", 5 * 1024 * 1024);

            var result = await sharing.ShareAsync(item, ShareDestination.OneDrive, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://onedrive.example.test/large", result.Url);
            StringAssert.Contains(result.Message, "upload-session");

            var requests = server.Requests;
            Assert.AreEqual(3, requests.Count);

            var create = requests.Single(request => request.Path.Contains("createUploadSession", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("POST", create.Method);
            Assert.AreEqual("Bearer one-token", create.Authorization);
            StringAssert.Contains(create.BodyText, "\"@microsoft.graph.conflictBehavior\":\"rename\"");
            StringAssert.Contains(create.BodyText, "onedrive-large.png");

            var chunks = requests
                .Where(request => request.Path.Equals("/upload-session", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.AreEqual(2, chunks.Count);
            Assert.AreEqual("PUT", chunks[0].Method);
            Assert.AreEqual("bytes 0-3276799/5242880", chunks[0].ContentRange);
            Assert.AreEqual("application/octet-stream", chunks[0].ContentType);
            Assert.AreEqual(3_276_800, chunks[0].Body.Length);
            Assert.AreEqual("bytes 3276800-5242879/5242880", chunks[1].ContentRange);
            Assert.AreEqual(1_966_080, chunks[1].Body.Length);
        });
    }

    [TestMethod]
    public async Task WebDavUpload_PutsFileWithBasicAuth()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("created", HttpStatusCode.Created);
            var settings = new AppSettings
            {
                WebDavBaseUrl = server.BaseUri.ToString(),
                WebDavRemoteDirectory = "/captures",
                WebDavUsername = "user"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveWebDavPassword("pass");
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "webdav-shot.png", 64);

            var result = await sharing.ShareAsync(item, ShareDestination.WebDav, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, server.Requests.Count);
            Assert.AreEqual("PUT", server.Requests[0].Method);
            StringAssert.Contains(server.Requests[0].Path, "/captures/");
            StringAssert.Contains(server.Requests[0].Path, "webdav-shot.png");
            StringAssert.StartsWith(server.Requests[0].Authorization, "Basic ");
            Assert.AreEqual(64, server.Requests[0].Body.Length);
        });
    }

    [TestMethod]
    public async Task FtpFtpsUpload_FailsBeforeNetworkWhenPasswordMissing()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                FtpHost = "files.example.test",
                FtpUsername = "deploy"
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "ftp-shot.png", 16);

            var result = await sharing.ShareAsync(item, ShareDestination.FtpFtps, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved password");
        });
    }

    [TestMethod]
    public async Task SecretStore_PersistsNewProviderPasswords()
    {
        await WithTempPathsAsync(paths =>
        {
            var store = new SecretStore(paths);

            store.SaveGitHubToken("github-token");
            store.SaveJiraApiToken("jira-token");
            store.SaveAzureDevOpsPat("ado-token");
            store.SaveWebDavPassword("webdav-pass");
            store.SaveFtpPassword("ftp-pass");

            Assert.IsTrue(store.HasGitHubToken);
            Assert.IsTrue(store.HasJiraApiToken);
            Assert.IsTrue(store.HasAzureDevOpsPat);
            Assert.IsTrue(store.HasWebDavPassword);
            Assert.IsTrue(store.HasFtpPassword);
            Assert.AreEqual("github-token", store.ReadGitHubToken());
            Assert.AreEqual("jira-token", store.ReadJiraApiToken());
            Assert.AreEqual("ado-token", store.ReadAzureDevOpsPat());
            Assert.AreEqual("webdav-pass", store.ReadWebDavPassword());
            Assert.AreEqual("ftp-pass", store.ReadFtpPassword());

            store.ClearGitHubToken();
            store.ClearJiraApiToken();
            store.ClearAzureDevOpsPat();
            store.ClearWebDavPassword();
            store.ClearFtpPassword();

            Assert.IsFalse(store.HasGitHubToken);
            Assert.IsFalse(store.HasJiraApiToken);
            Assert.IsFalse(store.HasAzureDevOpsPat);
            Assert.IsFalse(store.HasWebDavPassword);
            Assert.IsFalse(store.HasFtpPassword);
            return Task.CompletedTask;
        });
    }

    private static CaptureItem CreateCaptureItem(AppPaths paths, string fileName, int bytes)
    {
        var filePath = Path.Combine(paths.ImagesRoot, fileName);
        File.WriteAllBytes(filePath, Enumerable.Range(0, bytes).Select(index => (byte)(index % 255)).ToArray());

        return new CaptureItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CaptureKind.Imported,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = bytes,
            Width = 10,
            Height = 10
        };
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

    private sealed class LocalHttpCaptureServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _loop;
        private readonly Func<Uri, CapturedRequest, int, CapturedResponse> _responder;
        private readonly List<CapturedRequest> _requests = new();
        private readonly object _gate = new();

        private LocalHttpCaptureServer(
            HttpListener listener,
            Uri baseUri,
            Func<Uri, CapturedRequest, int, CapturedResponse> responder)
        {
            _listener = listener;
            BaseUri = baseUri;
            _responder = responder;
            _loop = Task.Run(ListenAsync);
        }

        public Uri BaseUri { get; }

        public IReadOnlyList<CapturedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        public static Task<LocalHttpCaptureServer> StartAsync(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return StartAsync((_, _, _) => new CapturedResponse(statusCode, responseBody));
        }

        public static Task<LocalHttpCaptureServer> StartAsync(
            Func<Uri, CapturedRequest, int, CapturedResponse> responder)
        {
            var port = FreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            return Task.FromResult(new LocalHttpCaptureServer(listener, new Uri(prefix), responder));
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Close();
            try
            {
                await _loop;
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ListenAsync()
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync();
                using var input = context.Request.InputStream;
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory);
                var request = new CapturedRequest(
                    context.Request.HttpMethod,
                    context.Request.Url?.AbsolutePath ?? string.Empty,
                    context.Request.ContentType ?? string.Empty,
                    context.Request.Headers["Authorization"] ?? string.Empty,
                    context.Request.Headers["Content-Range"] ?? string.Empty,
                    memory.ToArray());
                int requestIndex;
                lock (_gate)
                {
                    requestIndex = _requests.Count;
                    _requests.Add(request);
                }

                var response = _responder(BaseUri, request, requestIndex);
                var responseBytes = Encoding.UTF8.GetBytes(response.Body);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.ContentType;
                context.Response.ContentLength64 = responseBytes.Length;
                await context.Response.OutputStream.WriteAsync(responseBytes);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record CapturedResponse(
        HttpStatusCode StatusCode,
        string Body,
        string ContentType = "application/json");

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string ContentType,
        string Authorization,
        string ContentRange,
        byte[] Body)
    {
        public string BodyText => Encoding.UTF8.GetString(Body);
    }
}

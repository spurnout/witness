using System.Net;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ShareProviderAdapterTests
{
    [TestMethod]
    public async Task LocalFolderProvider_CopiesFileAndPreservesExistingExport()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.DocumentsRoot, "exports")
            };
            var provider = new LocalFolderShareProvider(settings);
            var item = CreateCaptureItem(paths, "adapter-local.png", 32);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var first = await provider.UploadAsync(ToRequest(item), CancellationToken.None);
            var second = await provider.UploadAsync(ToRequest(item), CancellationToken.None);
            var exports = Directory.GetFiles(settings.LocalExportFolder, "adapter-local*.png")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.AreEqual(ShareDestination.LocalFolder, provider.Destination);
            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(first.Succeeded, first.Message);
            Assert.IsTrue(second.Succeeded, second.Message);
            CollectionAssert.AreEqual(new[] { "adapter-local-2.png", "adapter-local.png" }, exports);
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_LocalFolderEntryExecutesThroughAdapter()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.DocumentsRoot, "catalog-exports")
            };
            var item = CreateCaptureItem(paths, "catalog-local.png", 16);
            var localProvider = ShareProviderCatalog.CreateDefault(settings)
                .Single(provider => provider.Destination == ShareDestination.LocalFolder);

            var result = await localProvider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsInstanceOfType<LocalFolderShareProvider>(localProvider);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(File.Exists(Path.Combine(settings.LocalExportFolder, "catalog-local.png")));
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_LocalClipboardAndEmailEntriesUseConcreteAdapters()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));

            Assert.IsInstanceOfType<ClipboardShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.ClipboardImage));
            Assert.IsInstanceOfType<ClipboardShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.ClipboardFile));
            Assert.IsInstanceOfType<ClipboardShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.ClipboardPath));
            Assert.IsInstanceOfType<ClipboardShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.MarkdownImageLink));
            Assert.IsInstanceOfType<EmailAttachmentShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.EmailAttachment));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_GooglePhotosYouTubeAndOneNoteEntriesUseConcreteAdapters()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));

            Assert.IsInstanceOfType<GooglePhotosShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.GooglePhotos));
            Assert.IsInstanceOfType<YouTubeShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.YouTube));
            Assert.IsInstanceOfType<OneNoteShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.OneNote));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ClipboardProvider_UsesLocalSurfaceForAllClipboardDestinations()
    {
        await WithTempPathsAsync(async paths =>
        {
            var item = CreateCaptureItem(paths, "clipboard-adapter.png", 16);
            var surface = new RecordingClipboardShareSurface();

            var image = await new ClipboardShareProvider(ShareDestination.ClipboardImage, surface)
                .UploadAsync(ToRequest(item), CancellationToken.None);
            var file = await new ClipboardShareProvider(ShareDestination.ClipboardFile, surface)
                .UploadAsync(ToRequest(item), CancellationToken.None);
            var path = await new ClipboardShareProvider(ShareDestination.ClipboardPath, surface)
                .UploadAsync(ToRequest(item), CancellationToken.None);
            var markdown = await new ClipboardShareProvider(ShareDestination.MarkdownImageLink, surface)
                .UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(image.Succeeded, image.Message);
            Assert.IsTrue(file.Succeeded, file.Message);
            Assert.IsTrue(path.Succeeded, path.Message);
            Assert.IsTrue(markdown.Succeeded, markdown.Message);
            Assert.AreEqual(item.FilePath, surface.ImagePath);
            CollectionAssert.AreEqual(new[] { item.FilePath }, surface.FileDropList.ToArray());
            Assert.AreEqual($"![clipboard-adapter]({item.FilePath.Replace("\\", "/", StringComparison.Ordinal)})", surface.Text);
        });
    }

    [TestMethod]
    public async Task EmailAttachmentProvider_UsesMailClientSurfaceAndClipboardPath()
    {
        await WithTempPathsAsync(async paths =>
        {
            var item = CreateCaptureItem(paths, "email-adapter.png", 18);
            var surface = new RecordingEmailHandoffSurface();
            var provider = new EmailAttachmentShareProvider(surface);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.AreEqual(ShareDestination.EmailAttachment, provider.Destination);
            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(item.FilePath, surface.Text);
            StringAssert.StartsWith(surface.MailtoUri, "mailto:?subject=Receipts%20capture%3A%20email-adapter.png");
            StringAssert.Contains(surface.MailtoUri, Uri.EscapeDataString(item.FilePath));
        });
    }

    [TestMethod]
    public async Task ShareService_UsesRegisteredProviderAdapterAndWritesHistory()
    {
        await WithTempPathsAsync(async paths =>
        {
            var adapter = new RecordingShareProvider();
            var sharing = new ShareService(
                paths,
                new AppSettings(),
                new SecretStore(paths),
                new IShareProvider[] { adapter });
            var item = CreateCaptureItem(paths, "facade-local.png", 24);

            var result = await sharing.ShareAsync(item, ShareDestination.LocalFolder, CancellationToken.None);
            var history = sharing.SearchHistory(destination: ShareDestination.LocalFolder, limit: 10);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("adapter executed", result.Message);
            Assert.AreEqual("https://example.test/facade-local.png", result.Url);
            Assert.IsNotNull(adapter.LastRequest);
            Assert.AreEqual(item.FilePath, adapter.LastRequest.FilePath);
            Assert.AreEqual(nameof(CaptureKind.Imported), adapter.LastRequest.CaptureType);
            Assert.AreEqual(item.Id, adapter.LastRequest.Metadata["id"]);
            Assert.AreEqual("24", adapter.LastRequest.Metadata["bytes"]);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("adapter executed", history[0].Message);
            Assert.AreEqual(result.Url, history[0].Url);
        });
    }

    [TestMethod]
    public async Task CustomWebhookProvider_PostsMultipartFileAndMetadata()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("accepted")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                CustomWebhookUrl = "https://webhook.example.test/upload"
            };
            var provider = new CustomWebhookShareProvider(settings, httpClient);
            var item = CreateCaptureItem(paths, "webhook-adapter.png", 40);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            StringAssert.Contains(result.Message, "Webhook upload completed: 202 Accepted");
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual(settings.CustomWebhookUrl, handler.LastRequest.RequestUri?.ToString());
            StringAssert.Contains(handler.LastContentType, "multipart/form-data");
            StringAssert.Contains(handler.LastBody, "webhook-adapter.png");
            StringAssert.Contains(handler.LastBody, "\"captureType\": \"Imported\"");
            StringAssert.Contains(handler.LastBody, "\"file\":");
        });
    }

    [TestMethod]
    public async Task CustomWebhookProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent("invalid payload")
            });
            using var httpClient = new HttpClient(handler);
            var provider = new CustomWebhookShareProvider(
                new AppSettings { CustomWebhookUrl = "https://webhook.example.test/upload" },
                httpClient);
            var item = CreateCaptureItem(paths, "webhook-failure.png", 18);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Webhook upload failed: 400 Bad Request invalid payload");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_ExecutableWebDavEntryUsesConcreteAdapter()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));
            var provider = providers.Single(candidate => candidate.Destination == ShareDestination.WebDav);

            Assert.IsInstanceOfType<WebDavShareProvider>(provider);
            Assert.AreEqual("WebDAV", provider.ProviderName);
            Assert.AreEqual("Basic", provider.AuthType);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task WebDavProvider_PutsFileWithBasicAuth()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("created")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                WebDavBaseUrl = "https://webdav.example.test/root",
                WebDavRemoteDirectory = "/captures",
                WebDavUsername = "user"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveWebDavPassword("pass");
            var provider = new WebDavShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "webdav adapter.png", 52);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            StringAssert.Contains(result.Message, "WebDAV upload completed");
            Assert.IsNull(result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Put, handler.LastRequest.Method);
            Assert.AreEqual("webdav.example.test", handler.LastRequest.RequestUri?.Host);
            StringAssert.Contains(handler.LastRequest.RequestUri?.AbsolutePath ?? string.Empty, "/root/captures/");
            StringAssert.Contains(handler.LastRequest.RequestUri?.AbsolutePath ?? string.Empty, "webdav%20adapter.png");
            Assert.AreEqual("Basic", handler.LastRequest.Headers.Authorization?.Scheme);
            Assert.AreEqual(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass")), handler.LastRequest.Headers.Authorization?.Parameter);
            Assert.AreEqual("image/png", handler.LastContentType);
            Assert.AreEqual(52, handler.LastBodyBytes.Length);
        });
    }

    [TestMethod]
    public async Task WebDavProvider_FailureDoesNotReturnPublicUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden",
                Content = new StringContent("access denied")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                WebDavBaseUrl = "https://webdav.example.test/root",
                WebDavPublicBaseUrl = "https://cdn.example.test/captures"
            };
            var provider = new WebDavShareProvider(settings, new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "webdav-failure.png", 19);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            StringAssert.Contains(result.Message, "WebDAV upload failed: 403 Forbidden access denied");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task WebDavProvider_MissingPasswordFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                WebDavBaseUrl = "https://webdav.example.test/root",
                WebDavUsername = "user"
            };
            var provider = new WebDavShareProvider(settings, new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "webdav-missing-password.png", 12);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved password");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_WebhookDestinationsUseConcreteAdapters()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));

            Assert.IsInstanceOfType<SlackWebhookShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.SlackWebhook));
            Assert.IsInstanceOfType<DiscordWebhookShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.DiscordWebhook));
            Assert.IsInstanceOfType<MicrosoftTeamsWebhookShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.MicrosoftTeamsWebhook));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task SlackWebhookProvider_PostsNotificationJson()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                SlackWebhookUrl = "https://hooks.slack.example.test/services/test",
                SlackMessageTemplate = "Ready {provider} {file} {bytes}"
            };
            var provider = new SlackWebhookShareProvider(settings, httpClient);
            var item = CreateCaptureItem(paths, "slack-adapter.png", 61);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNull(result.ShareUrl);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
            Assert.AreEqual(settings.SlackWebhookUrl, handler.LastRequest?.RequestUri?.ToString());
            Assert.AreEqual("application/json; charset=utf-8", handler.LastContentType);
            StringAssert.Contains(handler.LastBody, "Ready Slack slack-adapter.png 61");
            StringAssert.Contains(handler.LastBody, "unfurl_links");
        });
    }

    [TestMethod]
    public async Task SlackWebhookProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "Too Many Requests",
                Content = new StringContent("rate limited")
            });
            using var httpClient = new HttpClient(handler);
            var provider = new SlackWebhookShareProvider(
                new AppSettings { SlackWebhookUrl = "https://hooks.slack.example.test/services/test" },
                httpClient);
            var item = CreateCaptureItem(paths, "slack-failure.png", 20);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Slack webhook notification failed: 429 Too Many Requests rate limited");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task DiscordWebhookProvider_PostsMultipartFileAndExtractsUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"url":"https://discord.example.test/messages/1"}""")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                DiscordWebhookUrl = "https://discord.example.test/api/webhooks/test",
                DiscordMessageTemplate = "Discord {file} {capture_type}"
            };
            var provider = new DiscordWebhookShareProvider(settings, httpClient);
            var item = CreateCaptureItem(paths, "discord-adapter.png", 72);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://discord.example.test/messages/1", result.ShareUrl);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
            Assert.AreEqual(settings.DiscordWebhookUrl, handler.LastRequest?.RequestUri?.ToString());
            StringAssert.Contains(handler.LastContentType, "multipart/form-data");
            StringAssert.Contains(handler.LastBody, "payload_json");
            StringAssert.Contains(handler.LastBody, "Discord discord-adapter.png Imported");
            StringAssert.Contains(handler.LastBody, "discord-adapter.png");
            StringAssert.Contains(handler.LastBody, "image/png");
        });
    }

    [TestMethod]
    public async Task DiscordWebhookProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)
            {
                ReasonPhrase = "Payload Too Large",
                Content = new StringContent("file too large")
            });
            using var httpClient = new HttpClient(handler);
            var provider = new DiscordWebhookShareProvider(
                new AppSettings { DiscordWebhookUrl = "https://discord.example.test/api/webhooks/test" },
                httpClient);
            var item = CreateCaptureItem(paths, "discord-failure.png", 21);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            StringAssert.Contains(result.Message, "Discord webhook upload failed: 413 Payload Too Large file too large");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task TeamsWebhookProvider_PostsAdaptiveCardJson()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{}")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                TeamsWebhookUrl = "https://teams.example.test/webhook",
                TeamsMessageTemplate = "Teams {provider} {file}"
            };
            var provider = new MicrosoftTeamsWebhookShareProvider(settings, httpClient);
            var item = CreateCaptureItem(paths, "teams-adapter.png", 80);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNull(result.ShareUrl);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest?.Method);
            Assert.AreEqual(settings.TeamsWebhookUrl, handler.LastRequest?.RequestUri?.ToString());
            Assert.AreEqual("application/json; charset=utf-8", handler.LastContentType);
            StringAssert.Contains(handler.LastBody, "AdaptiveCard");
            StringAssert.Contains(handler.LastBody, "Teams Microsoft Teams teams-adapter.png");
        });
    }

    [TestMethod]
    public async Task TeamsWebhookProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Bad Gateway",
                Content = new StringContent("upstream unavailable")
            });
            using var httpClient = new HttpClient(handler);
            var provider = new MicrosoftTeamsWebhookShareProvider(
                new AppSettings { TeamsWebhookUrl = "https://teams.example.test/webhook" },
                httpClient);
            var item = CreateCaptureItem(paths, "teams-failure.png", 22);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Microsoft Teams webhook notification failed: 502 Bad Gateway upstream unavailable");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task HttpAdapters_PreCanceledTokenDoesNotSendRequests()
    {
        await WithTempPathsAsync(async paths =>
        {
            var item = CreateCaptureItem(paths, "canceled-http.png", 23);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var customHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var webDavHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
            var slackHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var discordHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var teamsHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var s3Handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var imgurHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var cloudinaryHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var githubHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var jiraHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var azureHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var linearHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var googlePhotosHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var youTubeHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            var oneNoteHandler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));

            await AssertPreCanceledAsync(
                new CustomWebhookShareProvider(
                    new AppSettings { CustomWebhookUrl = "https://webhook.example.test/upload" },
                    new HttpClient(customHandler)),
                customHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new WebDavShareProvider(
                    new AppSettings { WebDavBaseUrl = "https://webdav.example.test/root" },
                    new SecretStore(paths),
                    new HttpClient(webDavHandler)),
                webDavHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new SlackWebhookShareProvider(
                    new AppSettings { SlackWebhookUrl = "https://hooks.slack.example.test/services/test" },
                    new HttpClient(slackHandler)),
                slackHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new DiscordWebhookShareProvider(
                    new AppSettings { DiscordWebhookUrl = "https://discord.example.test/api/webhooks/test" },
                    new HttpClient(discordHandler)),
                discordHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new MicrosoftTeamsWebhookShareProvider(
                    new AppSettings { TeamsWebhookUrl = "https://teams.example.test/webhook" },
                    new HttpClient(teamsHandler)),
                teamsHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new S3CompatibleShareProvider(
                    new AppSettings { S3Endpoint = "https://s3.example.test", S3Bucket = "bucket" },
                    new SecretStore(paths),
                    new HttpClient(s3Handler)),
                s3Handler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new ImgurShareProvider(
                    new AppSettings { ImgurApiEndpoint = "https://imgur.example.test/image" },
                    new SecretStore(paths),
                    new HttpClient(imgurHandler)),
                imgurHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new CloudinaryShareProvider(
                    new AppSettings { CloudinaryCloudName = "demo", CloudinaryUploadPreset = "preset" },
                    new SecretStore(paths),
                    new HttpClient(cloudinaryHandler)),
                cloudinaryHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new GitHubIssuesShareProvider(
                    new AppSettings { GitHubRepository = "owner/repo" },
                    new SecretStore(paths),
                    new HttpClient(githubHandler)),
                githubHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new JiraShareProvider(
                    new AppSettings { JiraBaseUrl = "https://jira.example.test", JiraProjectKey = "GOAT", JiraAccountEmail = "user@example.test" },
                    new SecretStore(paths),
                    new HttpClient(jiraHandler)),
                jiraHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new AzureDevOpsShareProvider(
                    new AppSettings { AzureDevOpsOrganization = "org", AzureDevOpsProject = "project" },
                    new SecretStore(paths),
                    new HttpClient(azureHandler)),
                azureHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new LinearShareProvider(
                    new AppSettings { LinearTeamId = "team-id" },
                    new SecretStore(paths),
                    new HttpClient(linearHandler)),
                linearHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new GooglePhotosShareProvider(
                    new AppSettings(),
                    new SecretStore(paths),
                    new HttpClient(googlePhotosHandler)),
                googlePhotosHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new YouTubeShareProvider(
                    new AppSettings(),
                    new SecretStore(paths),
                    new HttpClient(youTubeHandler)),
                youTubeHandler,
                item,
                cancellation.Token);
            await AssertPreCanceledAsync(
                new OneNoteShareProvider(
                    new AppSettings(),
                    new SecretStore(paths),
                    new HttpClient(oneNoteHandler)),
                oneNoteHandler,
                item,
                cancellation.Token);
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_ExecutableFtpEntryUsesConcreteAdapter()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));
            var provider = providers.Single(candidate => candidate.Destination == ShareDestination.FtpFtps);

            Assert.IsInstanceOfType<FtpFtpsShareProvider>(provider);
            Assert.AreEqual("FTP/FTPS", provider.ProviderName);
            Assert.AreEqual("Password", provider.AuthType);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_ExecutableSftpEntryUsesConcreteAdapter()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));
            var provider = providers.Single(candidate => candidate.Destination == ShareDestination.Sftp);

            Assert.IsInstanceOfType<SftpShareProvider>(provider);
            Assert.AreEqual("SFTP", provider.ProviderName);
            Assert.AreEqual("SSH key", provider.AuthType);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task FtpFtpsProvider_UploadsThroughClientWithNormalizedUriAndPublicUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            var client = new RecordingFtpUploadClient("226 Transfer complete");
            var settings = new AppSettings
            {
                FtpHost = "ftps://files.example.test/base",
                FtpPort = 2121,
                FtpUsername = "deploy",
                FtpRemoteDirectory = "/captures/2026",
                FtpPublicBaseUrl = "https://cdn.example.test/public"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveFtpPassword("secret-pass");
            var provider = new FtpFtpsShareProvider(settings, secrets, client);
            var item = CreateCaptureItem(paths, "ftp adapter.png", 68);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(client.LastRequest);
            Assert.AreEqual("ftp", client.LastRequest.RequestUri.Scheme);
            Assert.AreEqual("files.example.test", client.LastRequest.RequestUri.Host);
            Assert.AreEqual(2121, client.LastRequest.RequestUri.Port);
            StringAssert.Contains(client.LastRequest.RequestUri.AbsolutePath, "/base/captures/2026/");
            StringAssert.Contains(client.LastRequest.RequestUri.AbsolutePath, "ftp%20adapter.png");
            Assert.AreEqual("deploy", client.LastRequest.Username);
            Assert.AreEqual("secret-pass", client.LastRequest.Password);
            Assert.IsTrue(client.LastRequest.EnableSsl);
            Assert.AreEqual(item.FilePath, client.LastRequest.FilePath);
            StringAssert.StartsWith(result.ShareUrl, "https://cdn.example.test/public/");
            StringAssert.Contains(result.ShareUrl, "ftp adapter.png");
            StringAssert.Contains(result.Message, "completed and copied URL");
        });
    }

    [TestMethod]
    public async Task FtpFtpsProvider_MissingPasswordFailsBeforeClient()
    {
        await WithTempPathsAsync(async paths =>
        {
            var client = new RecordingFtpUploadClient("226 Transfer complete");
            var settings = new AppSettings
            {
                FtpHost = "files.example.test",
                FtpUsername = "deploy"
            };
            var provider = new FtpFtpsShareProvider(settings, new SecretStore(paths), client);
            var item = CreateCaptureItem(paths, "ftp-missing-password.png", 16);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved password");
            Assert.IsNull(client.LastRequest);
        });
    }

    [TestMethod]
    public async Task SftpProvider_UploadsThroughClientWithPinnedHostKeyAndPublicUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            Directory.CreateDirectory(paths.TempRoot);
            var privateKeyPath = Path.Combine(paths.TempRoot, "id_ed25519");
            File.WriteAllText(privateKeyPath, "fake key");

            var client = new RecordingSftpClientAdapter(new SftpUploadResult(true, "upload ok"));
            var settings = new AppSettings
            {
                SftpHost = "files.example.test",
                SftpPort = 2222,
                SftpUsername = "deploy",
                SftpRemoteDirectory = "/captures/2026/",
                SftpPrivateKeyPath = privateKeyPath,
                SftpHostKeyFingerprint = "SHA256:ABCDEF",
                SftpPublicBaseUrl = "https://cdn.example.test/public"
            };
            var provider = new SftpShareProvider(paths, settings, client);
            var item = CreateCaptureItem(paths, "sftp adapter.png", 42);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(client.LastRequest);
            Assert.AreEqual("files.example.test", client.LastRequest.Host);
            Assert.AreEqual(2222, client.LastRequest.Port);
            Assert.AreEqual("deploy", client.LastRequest.Username);
            Assert.AreEqual(privateKeyPath, client.LastRequest.PrivateKeyPath);
            Assert.AreEqual("ABCDEF", client.LastRequest.HostKeySha256);
            StringAssert.Contains(client.LastRequest.RemotePath, "/captures/2026/");
            StringAssert.Contains(client.LastRequest.RemotePath, "sftp adapter.png");
            StringAssert.StartsWith(result.ShareUrl, "https://cdn.example.test/public/");
            StringAssert.Contains(Uri.UnescapeDataString(result.ShareUrl!), "sftp adapter.png");
            StringAssert.Contains(result.Message, "completed and copied URL");
        });
    }

    [TestMethod]
    public async Task SftpProvider_ClientFailureIncludesRedactedOutput()
    {
        await WithTempPathsAsync(async paths =>
        {
            Directory.CreateDirectory(paths.TempRoot);
            var privateKeyPath = Path.Combine(paths.TempRoot, "id_ed25519");
            File.WriteAllText(privateKeyPath, "fake key");

            var client = new RecordingSftpClientAdapter(new SftpUploadResult(false, "Host key verification failed."));
            var settings = new AppSettings
            {
                SftpHost = "files.example.test",
                SftpUsername = "deploy",
                SftpPrivateKeyPath = privateKeyPath,
                SftpHostKeyFingerprint = "ABCDEF"
            };
            var provider = new SftpShareProvider(paths, settings, client);
            var item = CreateCaptureItem(paths, "sftp-failed.png", 12);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            Assert.IsNotNull(client.LastRequest);
            StringAssert.Contains(result.Message, "Host key verification failed");
        });
    }

    [TestMethod]
    public async Task SftpProvider_MissingHostKeyPinFailsBeforeClient()
    {
        await WithTempPathsAsync(async paths =>
        {
            var privateKeyPath = Path.Combine(paths.TempRoot, "id_ed25519");
            File.WriteAllText(privateKeyPath, "fake key");
            var client = new RecordingSftpClientAdapter(new SftpUploadResult(true, string.Empty));
            var settings = new AppSettings
            {
                SftpHost = "files.example.test",
                SftpUsername = "deploy",
                SftpPrivateKeyPath = privateKeyPath
            };
            var provider = new SftpShareProvider(paths, settings, client);
            var item = CreateCaptureItem(paths, "sftp-missing-exe.png", 12);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "host key SHA-256 fingerprint");
            Assert.IsNull(client.LastRequest);
        });
    }

    [TestMethod]
    public async Task SftpProvider_PreCanceledTokenDoesNotRunProcess()
    {
        await WithTempPathsAsync(async paths =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var client = new RecordingSftpClientAdapter(new SftpUploadResult(true, string.Empty));
            var provider = new SftpShareProvider(paths, new AppSettings(), client);
            var item = CreateCaptureItem(paths, "sftp-canceled.png", 12);

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(
                () => provider.UploadAsync(ToRequest(item), cancellation.Token));
            Assert.IsNull(client.LastRequest);
        });
    }

    [TestMethod]
    public async Task ProviderCatalog_ExecutableCloudStorageEntriesUseConcreteAdapters()
    {
        await WithTempPathsAsync(paths =>
        {
            var providers = ShareProviderCatalog.CreateExecutable(paths, new AppSettings(), new SecretStore(paths));

            Assert.IsInstanceOfType<S3CompatibleShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.S3Compatible));
            Assert.IsInstanceOfType<ImgurShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.Imgur));
            Assert.IsInstanceOfType<CloudinaryShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.Cloudinary));
            Assert.IsInstanceOfType<SftpShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.Sftp));
            Assert.IsInstanceOfType<GitHubIssuesShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.GitHubIssues));
            Assert.IsInstanceOfType<JiraShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.Jira));
            Assert.IsInstanceOfType<AzureDevOpsShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.AzureDevOps));
            Assert.IsInstanceOfType<LinearShareProvider>(providers.Single(provider => provider.Destination == ShareDestination.Linear));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task S3CompatibleProvider_PutsSignedFileAndBuildsPublicUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
            {
                ReasonPhrase = "Created",
                Content = new StringContent("created")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                S3Endpoint = "https://s3.example.test/root",
                S3Bucket = "goatshot-bucket",
                S3Region = "us-west-2",
                S3KeyPrefix = "captures/bugs",
                S3PublicBaseUrl = "https://cdn.example.test/public"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveS3Credentials("ACCESS_KEY", "SECRET_KEY");
            var provider = new S3CompatibleShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "s3 adapter.png", 64);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Put, handler.LastRequest.Method);
            Assert.AreEqual("s3.example.test", handler.LastRequest.RequestUri?.Host);
            StringAssert.Contains(handler.LastRequest.RequestUri?.AbsolutePath ?? string.Empty, "/root/goatshot-bucket/captures/bugs/");
            StringAssert.Contains(handler.LastRequest.RequestUri?.AbsolutePath ?? string.Empty, "s3%20adapter.png");
            Assert.AreEqual("AWS4-HMAC-SHA256", handler.LastRequest.Headers.Authorization?.Scheme);
            StringAssert.Contains(handler.LastRequest.Headers.Authorization?.Parameter ?? string.Empty, "Credential=ACCESS_KEY/");
            Assert.IsTrue(handler.LastRequest.Headers.Contains("x-amz-content-sha256"));
            Assert.IsTrue(handler.LastRequest.Headers.Contains("x-amz-date"));
            Assert.AreEqual(64, handler.LastBodyBytes.Length);
            StringAssert.StartsWith(result.ShareUrl, "https://cdn.example.test/public/captures/bugs/");
            StringAssert.Contains(result.ShareUrl, "s3 adapter.png");
            StringAssert.Contains(result.Message, "S3-compatible upload completed and copied URL");
        });
    }

    [TestMethod]
    public async Task S3CompatibleProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden",
                Content = new StringContent("signature mismatch")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                S3Endpoint = "https://s3.example.test",
                S3Bucket = "goatshot-bucket"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveS3Credentials("ACCESS_KEY", "SECRET_KEY");
            var provider = new S3CompatibleShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "s3-failure.png", 17);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            StringAssert.Contains(result.Message, "S3-compatible upload failed: 403 Forbidden signature mismatch");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task ImgurProvider_PostsClientIdMultipartAndExtractsLink()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{"link":"https://i.imgur.example.test/abc.png"}}""")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                ImgurApiEndpoint = "https://imgur.example.test/3/image"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveImgurClientId("IMGUR_CLIENT_ID");
            var provider = new ImgurShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "imgur-adapter.png", 42);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://i.imgur.example.test/abc.png", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual(settings.ImgurApiEndpoint, handler.LastRequest.RequestUri?.ToString());
            Assert.AreEqual("Client-ID IMGUR_CLIENT_ID", handler.LastRequest.Headers.Authorization?.ToString());
            StringAssert.Contains(handler.LastContentType, "multipart/form-data");
            StringAssert.Contains(handler.LastBody, "imgur-adapter.png");
            StringAssert.Contains(handler.LastBody, "Receipts capture");
        });
    }

    [TestMethod]
    public async Task ImgurProvider_UnsupportedFileFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            using var httpClient = new HttpClient(handler);
            var secrets = new SecretStore(paths);
            secrets.SaveImgurClientId("IMGUR_CLIENT_ID");
            var provider = new ImgurShareProvider(new AppSettings(), secrets, httpClient);
            var item = CreateCaptureItem(paths, "imgur-unsupported.txt", 9);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "supports image files");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task CloudinaryProvider_PostsAuthenticatedMultipartAndExtractsSecureUrl()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"secure_url":"https://res.cloudinary.example.test/demo/image/upload/asset.png"}""")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                CloudinaryCloudName = "demo-cloud",
                CloudinaryApiBaseUrl = "https://cloudinary.example.test/v1_1",
                CloudinaryResourceType = "auto",
                CloudinaryFolder = "goatshot/captures"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveCloudinaryCredentials("cloud-key", "cloud-secret");
            var provider = new CloudinaryShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "cloudinary-adapter.png", 48);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://res.cloudinary.example.test/demo/image/upload/asset.png", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            StringAssert.Contains(handler.LastRequest.RequestUri?.AbsolutePath ?? string.Empty, "/v1_1/demo-cloud/auto/upload");
            Assert.AreEqual("Basic", handler.LastRequest.Headers.Authorization?.Scheme);
            Assert.AreEqual(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("cloud-key:cloud-secret")), handler.LastRequest.Headers.Authorization?.Parameter);
            StringAssert.Contains(handler.LastContentType, "multipart/form-data");
            StringAssert.Contains(handler.LastBody, "cloudinary-adapter.png");
            StringAssert.Contains(handler.LastBody, "goatshot/captures");
            StringAssert.Contains(handler.LastBody, "public_id");
        });
    }

    [TestMethod]
    public async Task CloudinaryProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent("invalid preset")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                CloudinaryCloudName = "demo-cloud",
                CloudinaryUploadPreset = "unsigned-preset"
            };
            var provider = new CloudinaryShareProvider(settings, new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "cloudinary-failure.png", 28);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            StringAssert.Contains(result.Message, "Cloudinary upload failed: 400 Bad Request invalid preset");
            Assert.IsNotNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task GitHubIssuesProvider_CreatesIssueWithTokenAndRedactedBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """
                    {"html_url":"https://github.example.test/owner/repo/issues/42"}
                    """)
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                GitHubApiBaseUrl = "https://github.example.test/api",
                GitHubRepository = "owner/repo",
                GitHubIssueTitleTemplate = "Bug {file}",
                GitHubLabels = "bug, goatshot, bug",
                GitHubAssignees = "octocat"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveGitHubToken("github-token");
            var provider = new GitHubIssuesShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "github adapter.png", 24);
            item.Notes = "password=super-secret";
            item.OcrText = "token github_pat_abcdefghijklmnopqrstuvwxyz_abcdefghijklmnopqrstuvwxyz";

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://github.example.test/owner/repo/issues/42", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual("/api/repos/owner/repo/issues", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.AreEqual("Bearer github-token", handler.LastRequest.Headers.Authorization?.ToString());
            Assert.AreEqual("Receipts/0.3.0", handler.LastRequest.Headers.UserAgent.ToString());
            StringAssert.Contains(handler.LastBody, "Bug github adapter.png");
            StringAssert.Contains(handler.LastBody, "goatshot");
            StringAssert.Contains(handler.LastBody, "octocat");
            StringAssert.Contains(handler.LastBody, "GitHub Issues issue creation does not upload the local capture file");
            Assert.IsFalse(handler.LastBody.Contains("super-secret", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(handler.LastBody.Contains("github_pat_", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(handler.LastBody.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task GitHubIssuesProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                ReasonPhrase = "Unprocessable Entity",
                Content = new StringContent("""{"message":"Validation Failed"}""")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                GitHubApiBaseUrl = "https://github.example.test",
                GitHubRepository = "owner/repo"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveGitHubToken("github-token");
            var provider = new GitHubIssuesShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "github failed.png", 24);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            StringAssert.Contains(result.Message, "GitHub issue creation failed: 422 Unprocessable Entity");
            StringAssert.Contains(result.Message, "Validation Failed");
        });
    }

    [TestMethod]
    public async Task GitHubIssuesProvider_MissingTokenFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
            using var httpClient = new HttpClient(handler);
            var provider = new GitHubIssuesShareProvider(
                new AppSettings { GitHubRepository = "owner/repo" },
                new SecretStore(paths),
                httpClient);
            var item = CreateCaptureItem(paths, "github missing token.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved personal access token");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task GitHubIssuesProvider_InvalidRepositoryFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
            using var httpClient = new HttpClient(handler);
            var secrets = new SecretStore(paths);
            secrets.SaveGitHubToken("github-token");
            var provider = new GitHubIssuesShareProvider(
                new AppSettings { GitHubRepository = "owner/repo/extra" },
                secrets,
                httpClient);
            var item = CreateCaptureItem(paths, "github invalid repo.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "owner/repo");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task JiraProvider_CreatesIssueWithBasicAuthAndAdfDescription()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """
                    {"key":"GOAT-42","self":"https://jira.example.test/rest/api/3/issue/10042"}
                    """)
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                JiraBaseUrl = "https://jira.example.test",
                JiraProjectKey = "GOAT",
                JiraIssueType = "Bug",
                JiraSummaryTemplate = "Jira {file}",
                JiraLabels = "goatshot,bug,goatshot",
                JiraAccountEmail = "user@example.test"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveJiraApiToken("jira-token");
            var provider = new JiraShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "jira adapter.png", 32);
            item.OcrText = "api_key=secret-value";

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://jira.example.test/browse/GOAT-42", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual("/rest/api/3/issue", handler.LastRequest.RequestUri!.AbsolutePath);
            StringAssert.Contains(handler.LastContentType, "application/json");
            StringAssert.StartsWith(handler.LastRequest.Headers.Authorization?.ToString() ?? string.Empty, "Basic ");
            StringAssert.Contains(handler.LastBody, "\"summary\":\"Jira jira adapter.png\"");
            StringAssert.Contains(handler.LastBody, "\"type\":\"doc\"");
            StringAssert.Contains(handler.LastBody, "\"key\":\"GOAT\"");
            StringAssert.Contains(handler.LastBody, "goatshot");
            Assert.IsFalse(handler.LastBody.Contains("secret-value", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(handler.LastBody.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task JiraProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent("""{"errorMessages":["summary required"]}""")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                JiraBaseUrl = "https://jira.example.test",
                JiraProjectKey = "GOAT",
                JiraAccountEmail = "user@example.test"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveJiraApiToken("jira-token");
            var provider = new JiraShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "jira failed.png", 24);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            StringAssert.Contains(result.Message, "Jira issue creation failed: 400 Bad Request");
            StringAssert.Contains(result.Message, "summary required");
        });
    }

    [TestMethod]
    public async Task JiraProvider_MissingTokenFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
            using var httpClient = new HttpClient(handler);
            var provider = new JiraShareProvider(
                new AppSettings
                {
                    JiraBaseUrl = "https://jira.example.test",
                    JiraProjectKey = "GOAT",
                    JiraAccountEmail = "user@example.test"
                },
                new SecretStore(paths),
                httpClient);
            var item = CreateCaptureItem(paths, "jira missing token.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved API token");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task AzureDevOpsProvider_CreatesWorkItemWithJsonPatchAndPat()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"id":42,"_links":{"html":{"href":"https://dev.azure.com/org/project/_workitems/edit/42"}}}
                    """)
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                AzureDevOpsBaseUrl = "https://dev.azure.test",
                AzureDevOpsOrganization = "org",
                AzureDevOpsProject = "project",
                AzureDevOpsWorkItemType = "Bug",
                AzureDevOpsTitleTemplate = "ADO {file}",
                AzureDevOpsTags = "goatshot,bug,goatshot",
                AzureDevOpsAssignedTo = "user@example.test"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveAzureDevOpsPat("ado-token");
            var provider = new AzureDevOpsShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "ado adapter.png", 40);
            item.Notes = "token=secret-value";

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://dev.azure.com/org/project/_workitems/edit/42", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual("/org/project/_apis/wit/workitems/$Bug", handler.LastRequest.RequestUri!.AbsolutePath);
            Assert.AreEqual("api-version=7.1", handler.LastRequest.RequestUri.Query.TrimStart('?'));
            StringAssert.Contains(handler.LastContentType, "application/json-patch+json");
            StringAssert.StartsWith(handler.LastRequest.Headers.Authorization?.ToString() ?? string.Empty, "Basic ");
            StringAssert.Contains(handler.LastBody, "\"path\":\"/fields/System.Title\"");
            StringAssert.Contains(handler.LastBody, "ADO ado adapter.png");
            StringAssert.Contains(handler.LastBody, "\"path\":\"/fields/System.Tags\"");
            StringAssert.Contains(handler.LastBody, "goatshot; bug");
            StringAssert.Contains(handler.LastBody, "\"path\":\"/fields/System.AssignedTo\"");
            Assert.IsFalse(handler.LastBody.Contains("secret-value", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(handler.LastBody.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task AzureDevOpsProvider_FailureIncludesStatusAndBody()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized",
                Content = new StringContent("invalid pat")
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                AzureDevOpsOrganization = "org",
                AzureDevOpsProject = "project"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveAzureDevOpsPat("ado-token");
            var provider = new AzureDevOpsShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "ado failed.png", 24);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            StringAssert.Contains(result.Message, "Azure DevOps work item creation failed: 401 Unauthorized invalid pat");
        });
    }

    [TestMethod]
    public async Task AzureDevOpsProvider_MissingPatFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            using var httpClient = new HttpClient(handler);
            var provider = new AzureDevOpsShareProvider(
                new AppSettings
                {
                    AzureDevOpsOrganization = "org",
                    AzureDevOpsProject = "project"
                },
                new SecretStore(paths),
                httpClient);
            var item = CreateCaptureItem(paths, "ado missing pat.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved personal access token");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task GooglePhotosProvider_UploadsMediaAndCreatesMediaItem()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, index, _, _) => index switch
            {
                0 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("upload-token-123")
                },
                1 => JsonResponse(
                    """
                    {"newMediaItemResults":[{"mediaItem":{"id":"media-id","productUrl":"https://photos.example.test/media-id"}}]}
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("unexpected request")
                }
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                GooglePhotosUploadApiBaseUrl = "https://photos.example.test/v1/uploads",
                GooglePhotosApiBaseUrl = "https://photos.example.test/v1",
                GooglePhotosAlbumId = "album-id",
                GooglePhotosDescriptionTemplate = "Photos {file}"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveGooglePhotosAccessToken("google-photos-access-token");
            var provider = new GooglePhotosShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "photos-adapter.png", 42);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://photos.example.test/media-id", result.ShareUrl);
            Assert.AreEqual(2, handler.Requests.Count);

            Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
            Assert.AreEqual("https://photos.example.test/v1/uploads", handler.Requests[0].Uri.ToString());
            Assert.AreEqual("Bearer google-photos-access-token", handler.Requests[0].Authorization);
            Assert.AreEqual("image/png", handler.Requests[0].ContentType);
            Assert.AreEqual(42, handler.Requests[0].BodyBytes.Length);
            CollectionAssert.Contains(handler.Requests[0].HeaderNames.ToList(), "X-Goog-Upload-File-Name");
            CollectionAssert.Contains(handler.Requests[0].HeaderNames.ToList(), "X-Goog-Upload-Protocol");

            Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
            Assert.AreEqual("https://photos.example.test/v1/mediaItems:batchCreate", handler.Requests[1].Uri.ToString());
            Assert.AreEqual("Bearer google-photos-access-token", handler.Requests[1].Authorization);
            StringAssert.Contains(handler.Requests[1].ContentType, "application/json");
            StringAssert.Contains(handler.Requests[1].BodyText, "\"albumId\":\"album-id\"");
            StringAssert.Contains(handler.Requests[1].BodyText, "\"description\":\"Photos photos-adapter.png\"");
            StringAssert.Contains(handler.Requests[1].BodyText, "\"uploadToken\":\"upload-token-123\"");
        });
    }

    [TestMethod]
    public async Task GooglePhotosProvider_RejectsUnsupportedFileBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, _, _, _) => JsonResponse("{}"));
            using var httpClient = new HttpClient(handler);
            var secrets = new SecretStore(paths);
            secrets.SaveGooglePhotosAccessToken("google-photos-access-token");
            var provider = new GooglePhotosShareProvider(new AppSettings(), secrets, httpClient);
            var item = CreateCaptureItem(paths, "photos-unsupported.pdf", 24);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "image and video files only");
            Assert.AreEqual(0, handler.Requests.Count);
        });
    }

    [TestMethod]
    public async Task GooglePhotosProvider_MissingTokenFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, _, _, _) => JsonResponse("{}"));
            using var httpClient = new HttpClient(handler);
            var provider = new GooglePhotosShareProvider(new AppSettings(), new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "photos-missing-token.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved OAuth access token");
            Assert.AreEqual(0, handler.Requests.Count);
        });
    }

    [TestMethod]
    public async Task YouTubeProvider_UploadsVideoWithOAuthMultipartRequest()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"id":"video-123"}
                    """)
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                YouTubeUploadApiBaseUrl = "https://youtube.example.test/upload/youtube/v3/videos",
                YouTubeTitleTemplate = "Unit {file}",
                YouTubeDescriptionTemplate = "Capture {id}",
                YouTubePrivacyStatus = "private",
                YouTubeCategoryId = "28"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveYouTubeAccessToken("youtube-access-token");
            var provider = new YouTubeShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "youtube-adapter.mp4", 64);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://youtu.be/video-123", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual(
                "https://youtube.example.test/upload/youtube/v3/videos?uploadType=multipart&part=snippet,status",
                handler.LastRequest.RequestUri?.ToString());
            Assert.AreEqual("Bearer youtube-access-token", handler.LastRequest.Headers.Authorization?.ToString());
            StringAssert.StartsWith(handler.LastContentType, "multipart/related");
            StringAssert.Contains(handler.LastBody, "\"title\":\"Unit youtube-adapter.mp4\"");
            StringAssert.Contains(handler.LastBody, "\"description\":\"Capture ");
            StringAssert.Contains(handler.LastBody, "\"categoryId\":\"28\"");
            StringAssert.Contains(handler.LastBody, "\"privacyStatus\":\"private\"");
            StringAssert.Contains(handler.LastBody, "Content-Type: video/mp4");
        });
    }

    [TestMethod]
    public async Task YouTubeProvider_RejectsNonVideoBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            using var httpClient = new HttpClient(handler);
            var secrets = new SecretStore(paths);
            secrets.SaveYouTubeAccessToken("youtube-access-token");
            var provider = new YouTubeShareProvider(new AppSettings(), secrets, httpClient);
            var item = CreateCaptureItem(paths, "youtube-unsupported.png", 24);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "video files only");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task YouTubeProvider_MissingTokenFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            using var httpClient = new HttpClient(handler);
            var provider = new YouTubeShareProvider(new AppSettings(), new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "youtube-missing-token.mp4", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved OAuth access token");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task OneNoteProvider_CreatesPageWithOAuthMultipartRequest()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """
                    {"links":{"oneNoteWebUrl":{"href":"https://onenote.example.test/page-id"}}}
                    """)
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                OneNoteGraphApiBaseUrl = "https://graph.example.test/v1.0/",
                OneNoteSectionId = "section id",
                OneNotePageTitleTemplate = "Page {file}"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveOneNoteAccessToken("onenote-access-token");
            var provider = new OneNoteShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "onenote-adapter.png", 42);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://onenote.example.test/page-id", result.ShareUrl);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual(HttpMethod.Post, handler.LastRequest.Method);
            Assert.AreEqual(
                "https://graph.example.test/v1.0/me/onenote/sections/section%20id/pages",
                handler.LastRequest.RequestUri?.AbsoluteUri);
            Assert.AreEqual("Bearer onenote-access-token", handler.LastRequest.Headers.Authorization?.ToString());
            StringAssert.StartsWith(handler.LastContentType, "multipart/form-data");
            StringAssert.Contains(handler.LastBody, "name=Presentation");
            StringAssert.Contains(handler.LastBody, "<title>Page onenote-adapter.png</title>");
            StringAssert.Contains(handler.LastBody, "src=\"name:file\"");
            StringAssert.Contains(handler.LastBody, "Content-Type: image/png");
            StringAssert.Contains(handler.LastBody, "filename=onenote-adapter.png");
        });
    }

    [TestMethod]
    public async Task OneNoteProvider_MissingTokenFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
            using var httpClient = new HttpClient(handler);
            var provider = new OneNoteShareProvider(new AppSettings(), new SecretStore(paths), httpClient);
            var item = CreateCaptureItem(paths, "onenote-missing-token.png", 24);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved OAuth access token");
            Assert.IsNull(handler.LastRequest);
        });
    }

    [TestMethod]
    public async Task LinearProvider_UploadsFileCreatesIssueAndAttachment()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, index, _, _) => index switch
            {
                0 => JsonResponse(
                    """
                    {"data":{"fileUpload":{"success":true,"uploadFile":{"uploadUrl":"https://upload.linear.example.test/file","assetUrl":"https://assets.linear.example.test/file.png","headers":[{"key":"x-amz-acl","value":"public-read"},{"key":"Content-Type","value":"image/png"}]}}}}
                    """),
                1 => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                },
                2 => JsonResponse(
                    """
                    {"data":{"issueCreate":{"success":true,"issue":{"id":"issue-id","identifier":"GOAT-42","url":"https://linear.example.test/GOAT-42","title":"Linear linear adapter.png"}}}}
                    """),
                3 => JsonResponse(
                    """
                    {"data":{"attachmentCreate":{"success":true,"attachment":{"id":"attachment-id","url":"https://assets.linear.example.test/file.png"}}}}
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("unexpected request")
                }
            });
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                LinearGraphqlEndpoint = "https://linear.example.test/graphql",
                LinearTeamId = "team-id",
                LinearIssueTitleTemplate = "Linear {file}",
                LinearCreateAttachment = true
            };
            var secrets = new SecretStore(paths);
            secrets.SaveLinearCredential("linear-key");
            var provider = new LinearShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "linear adapter.png", 44);
            item.Notes = "token=secret-value";

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("https://linear.example.test/GOAT-42", result.ShareUrl);
            StringAssert.Contains(result.Message, "Linear issue GOAT-42 created.");
            StringAssert.Contains(result.Message, "Linear attachment was linked");
            Assert.AreEqual(4, handler.Requests.Count);

            Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
            Assert.AreEqual("https://linear.example.test/graphql", handler.Requests[0].Uri.ToString());
            Assert.AreEqual("linear-key", handler.Requests[0].Authorization);
            StringAssert.Contains(handler.Requests[0].BodyText, "fileUpload");
            StringAssert.Contains(handler.Requests[0].BodyText, "linear adapter.png");

            Assert.AreEqual(HttpMethod.Put, handler.Requests[1].Method);
            Assert.AreEqual("https://upload.linear.example.test/file", handler.Requests[1].Uri.ToString());
            Assert.AreEqual(44, handler.Requests[1].BodyBytes.Length);
            StringAssert.Contains(handler.Requests[1].ContentType, "image/png");
            CollectionAssert.Contains(handler.Requests[1].HeaderNames.ToList(), "x-amz-acl");

            Assert.AreEqual(HttpMethod.Post, handler.Requests[2].Method);
            StringAssert.Contains(handler.Requests[2].BodyText, "issueCreate");
            StringAssert.Contains(handler.Requests[2].BodyText, "Linear linear adapter.png");
            StringAssert.Contains(handler.Requests[2].BodyText, "https://assets.linear.example.test/file.png");
            Assert.IsFalse(handler.Requests[2].BodyText.Contains(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(handler.Requests[2].BodyText.Contains("secret-value", StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(HttpMethod.Post, handler.Requests[3].Method);
            StringAssert.Contains(handler.Requests[3].BodyText, "attachmentCreate");
            StringAssert.Contains(handler.Requests[3].BodyText, "issue-id");
        });
    }

    [TestMethod]
    public async Task LinearProvider_MissingCredentialFailsBeforeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, _, _, _) => JsonResponse("{}"));
            using var httpClient = new HttpClient(handler);
            var provider = new LinearShareProvider(
                new AppSettings { LinearTeamId = "team-id" },
                new SecretStore(paths),
                httpClient);
            var item = CreateCaptureItem(paths, "linear missing credential.png", 12);

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(health.IsHealthy);
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "DPAPI-saved personal API key or OAuth access token");
            Assert.AreEqual(0, handler.Requests.Count);
        });
    }

    [TestMethod]
    public async Task LinearProvider_GraphQlErrorFailsBeforeSignedUpload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var handler = new SequencedHttpMessageHandler((_, _, _, _) => JsonResponse(
                """
                {"errors":[{"message":"No upload permission"}]}
                """));
            using var httpClient = new HttpClient(handler);
            var settings = new AppSettings
            {
                LinearGraphqlEndpoint = "https://linear.example.test/graphql",
                LinearTeamId = "team-id"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveLinearCredential("linear-key");
            var provider = new LinearShareProvider(settings, secrets, httpClient);
            var item = CreateCaptureItem(paths, "linear graphql error.png", 12);

            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.ShareUrl);
            Assert.AreEqual(1, handler.Requests.Count);
            StringAssert.Contains(result.Message, "Linear file upload request failed");
            StringAssert.Contains(result.Message, "No upload permission");
        });
    }

    [TestMethod]
    public async Task CustomScriptProvider_RunsCommandWithMetadataAndOcrPlaceholders()
    {
        await WithTempPathsAsync(async paths =>
        {
            var proofPath = Path.Combine(paths.TempRoot, "script-proof.txt");
            var settings = new AppSettings
            {
                CustomScriptCommand =
                    "$metadata = Get-Content -LiteralPath '{metadata}' -Raw; " +
                    "$ocr = Get-Content -LiteralPath '{ocr}' -Raw; " +
                    $"Set-Content -LiteralPath '{EscapePowerShellLiteral(proofPath)}' -Value ('id={{id}};type={{capture_type}};file={{file}};ocr=' + $ocr + ';metadata=' + $metadata)"
            };
            var provider = new CustomScriptShareProvider(paths, settings);
            var item = CreateCaptureItem(paths, "script-adapter.png", 44);
            item.OcrText = "OCR proof text";
            item.SourceApp = "UnitTestApp";

            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            var result = await provider.UploadAsync(ToRequest(item), CancellationToken.None);
            var proof = await File.ReadAllTextAsync(proofPath);

            Assert.AreEqual(ShareDestination.CustomScript, provider.Destination);
            Assert.IsTrue(health.IsHealthy, health.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("Custom script completed.", result.Message);
            Assert.IsNull(result.ShareUrl);
            StringAssert.Contains(proof, $"id={item.Id}");
            StringAssert.Contains(proof, "type=Imported");
            StringAssert.Contains(proof, $"file={item.FilePath}");
            StringAssert.Contains(proof, "ocr=OCR proof text");
            StringAssert.Contains(proof, "\"sourceApp\": \"UnitTestApp\"");
        });
    }

    [TestMethod]
    public async Task ProviderDiagnostics_RequiresCustomWebhookUrl()
    {
        await WithTempPathsAsync(paths =>
        {
            var missing = new ProviderDiagnosticsService(new AppSettings(), new SecretStore(paths))
                .GetDiagnostics()
                .Single(record => record.ProviderName == "Custom webhook");
            var configured = new ProviderDiagnosticsService(
                    new AppSettings { CustomWebhookUrl = "https://webhook.example.test/upload" },
                    new SecretStore(paths))
                .GetDiagnostics()
                .Single(record => record.ProviderName == "Custom webhook");

            Assert.IsFalse(missing.ReadyForLocalAttempt);
            CollectionAssert.Contains(missing.MissingSettings, "Custom webhook URL");
            Assert.IsTrue(configured.ReadyForLocalAttempt, configured.ReadinessSummary);
            CollectionAssert.Contains(configured.ConfiguredSettings, "Custom webhook URL");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ProviderDiagnostics_RequiresCustomScriptCommand()
    {
        await WithTempPathsAsync(paths =>
        {
            var missing = new ProviderDiagnosticsService(new AppSettings(), new SecretStore(paths))
                .GetDiagnostics()
                .Single(record => record.ProviderName == "Custom script");
            var configured = new ProviderDiagnosticsService(
                    new AppSettings { CustomScriptCommand = "Write-Output 'ok'" },
                    new SecretStore(paths))
                .GetDiagnostics()
                .Single(record => record.ProviderName == "Custom script");

            Assert.IsFalse(missing.ReadyForLocalAttempt);
            CollectionAssert.Contains(missing.MissingSettings, "Custom script command");
            Assert.IsTrue(configured.ReadyForLocalAttempt, configured.ReadinessSummary);
            CollectionAssert.Contains(configured.ConfiguredSettings, "Custom script command");
            return Task.CompletedTask;
        });
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static ShareUploadRequest ToRequest(CaptureItem item)
    {
        return new ShareUploadRequest(
            item.FilePath,
            item.Kind.ToString(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = item.Id,
                ["file"] = item.FilePath,
                ["fileName"] = item.FileName,
                ["captureType"] = item.Kind.ToString(),
                ["createdAt"] = item.CreatedAt.ToString("O"),
                ["bytes"] = item.Bytes.ToString(),
                ["width"] = item.Width.ToString(),
                ["height"] = item.Height.ToString(),
                ["sourceApp"] = item.SourceApp ?? string.Empty,
                ["sourceWindowTitle"] = item.SourceWindowTitle ?? string.Empty,
                ["bounds"] = item.Bounds?.Display ?? string.Empty,
                ["notes"] = item.Notes ?? string.Empty,
                ["ocrText"] = item.OcrText ?? string.Empty
            });
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private static async Task AssertPreCanceledAsync(
        IShareProvider provider,
        RecordingHttpMessageHandler handler,
        CaptureItem item,
        CancellationToken cancellationToken)
    {
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => provider.UploadAsync(ToRequest(item), cancellationToken));
        Assert.IsNull(handler.LastRequest);
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

    private sealed class RecordingShareProvider : IShareProvider
    {
        public ShareUploadRequest? LastRequest { get; private set; }
        public ShareDestination? Destination => ShareDestination.LocalFolder;
        public string ProviderName => "Local folder test adapter";
        public string AuthType => "None";
        public bool IsImplemented => true;
        public bool SupportsPublicLinks => true;
        public bool SupportsPrivateLinks => true;
        public bool SupportsExpiration => false;
        public bool SupportsPassword => false;

        public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProviderHealth(true, "ready"));
        }

        public Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ShareUploadResult(
                true,
                "https://example.test/facade-local.png",
                "adapter executed"));
        }
    }

    private sealed class RecordingClipboardShareSurface : IClipboardShareSurface
    {
        public string Text { get; private set; } = string.Empty;
        public string ImagePath { get; private set; } = string.Empty;
        public List<string> FileDropList { get; } = new();

        public void SetText(string text)
        {
            Text = text;
        }

        public void SetImage(string filePath)
        {
            ImagePath = filePath;
        }

        public void SetFileDropList(IEnumerable<string> paths)
        {
            FileDropList.Clear();
            FileDropList.AddRange(paths);
        }
    }

    private sealed class RecordingEmailHandoffSurface : IEmailHandoffSurface
    {
        public string Text { get; private set; } = string.Empty;
        public string MailtoUri { get; private set; } = string.Empty;

        public void SetText(string text)
        {
            Text = text;
        }

        public void OpenMailto(string mailtoUri)
        {
            MailtoUri = mailtoUri;
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public byte[] LastBodyBytes { get; private set; } = Array.Empty<byte>();
        public string LastContentType { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            LastBodyBytes = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastBody = System.Text.Encoding.UTF8.GetString(LastBodyBytes);

            return _response;
        }
    }

    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, string, byte[], HttpResponseMessage> _responseFactory;
        private readonly List<SequencedHttpRequest> _requests = new();

        public SequencedHttpMessageHandler(Func<HttpRequestMessage, int, string, byte[], HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public IReadOnlyList<SequencedHttpRequest> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bodyBytes = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var bodyText = System.Text.Encoding.UTF8.GetString(bodyBytes);
            var authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(authorization) &&
                request.Headers.TryGetValues("Authorization", out var authorizationValues))
            {
                authorization = string.Join(",", authorizationValues);
            }

            var headerNames = request.Headers
                .Select(header => header.Key)
                .Concat(request.Content?.Headers.Select(header => header.Key) ?? Enumerable.Empty<string>())
                .ToList();

            _requests.Add(new SequencedHttpRequest(
                request.Method,
                request.RequestUri!,
                authorization,
                request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                bodyText,
                bodyBytes,
                headerNames));

            return _responseFactory(request, _requests.Count - 1, bodyText, bodyBytes);
        }
    }

    private sealed record SequencedHttpRequest(
        HttpMethod Method,
        Uri Uri,
        string Authorization,
        string ContentType,
        string BodyText,
        byte[] BodyBytes,
        IReadOnlyList<string> HeaderNames);

    private sealed class RecordingFtpUploadClient : IFtpUploadClient
    {
        private readonly string _statusDescription;

        public RecordingFtpUploadClient(string statusDescription)
        {
            _statusDescription = statusDescription;
        }

        public FtpUploadRequest? LastRequest { get; private set; }

        public Task<FtpUploadResult> UploadAsync(FtpUploadRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new FtpUploadResult(_statusDescription));
        }
    }

    private sealed class RecordingSftpClientAdapter : ISftpClientAdapter
    {
        private readonly SftpUploadResult _result;

        public RecordingSftpClientAdapter(SftpUploadResult result)
        {
            _result = result;
        }

        public SftpUploadRequest? LastRequest { get; private set; }

        public Task<SftpUploadResult> UploadAsync(SftpUploadRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class UploadQueueServiceTests
{
    private static readonly string[] UploadSessionSecretValues =
    [
        "one-token-secret",
        "SESSION_CREATE_FIRST",
        "AUTH_CREATE_FIRST",
        "CHUNK_FAIL_SECRET",
        "CHUNK_AUTH_SECRET",
        "SESSION_CREATE_SECOND",
        "AUTH_CREATE_SECOND",
        "ITEM_SECRET",
        "ITEM_AUTH_SECRET",
        "SHARE_SECRET",
        "SHARE_AUTH_SECRET"
    ];

    [TestMethod]
    public async Task Queue_CanPersistCancelAndRetryItems()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = new UploadQueueService(paths, new UploadQueueSettings());
            var item = CreateCaptureItem(paths, "shot.png", 128);

            var queued = await service.EnqueueAsync(item, ShareDestination.GoogleDrive, CancellationToken.None);

            Assert.AreEqual("Queued", queued.Status);
            Assert.AreEqual("shot.png", queued.FileName);
            Assert.AreEqual(ShareDestination.GoogleDrive, queued.Destination);
            Assert.AreEqual(128, queued.Bytes);
            Assert.IsTrue(File.Exists(paths.UploadQueuePath));

            var reloaded = new UploadQueueService(paths, new UploadQueueSettings());
            var loaded = await reloaded.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(queued.Id, loaded[0].Id);

            var canceled = await reloaded.CancelAsync(queued.Id[..8], CancellationToken.None);

            Assert.IsNotNull(canceled);
            Assert.AreEqual("Canceled", canceled.Status);
            Assert.IsNotNull(canceled.CompletedAt);

            var retried = await reloaded.RetryAsync(queued.Id, CancellationToken.None);

            Assert.IsNotNull(retried);
            Assert.AreEqual("Queued", retried.Status);
            Assert.IsNull(retried.CompletedAt);
            Assert.IsNotNull(retried.NextAttemptAt);
        });
    }

    [TestMethod]
    public async Task Queue_TrimsToConfiguredHistoryLimit()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = new UploadQueueService(
                paths,
                new UploadQueueSettings
                {
                    HistoryLimit = 2
                });

            await service.EnqueueAsync(CreateCaptureItem(paths, "one.png", 1), ShareDestination.Dropbox, CancellationToken.None);
            await service.EnqueueAsync(CreateCaptureItem(paths, "two.png", 2), ShareDestination.Dropbox, CancellationToken.None);
            await service.EnqueueAsync(CreateCaptureItem(paths, "three.png", 3), ShareDestination.Dropbox, CancellationToken.None);

            var loaded = await service.ListAsync(CancellationToken.None);

            Assert.AreEqual(2, loaded.Count);
            CollectionAssert.DoesNotContain(loaded.Select(item => item.FileName).ToList(), "one.png");
        });
    }

    [TestMethod]
    public async Task Queue_DiagnosticsAndPresentation_ReportDueBackoffAndCancelableState()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new UploadQueueSettings
            {
                MaxAttempts = 4,
                MaxConcurrentUploads = 2,
                RetryBackoffSeconds = 15,
                EnableBackgroundProcessing = true
            };
            var service = new UploadQueueService(paths, settings);
            var queued = await service.EnqueueAsync(
                CreateCaptureItem(paths, "diagnostic-queued.png", 64),
                ShareDestination.CustomWebhook,
                CancellationToken.None);

            var diagnostics = service.GetDiagnostics();

            Assert.AreEqual(1, diagnostics.TotalItems);
            Assert.AreEqual(1, diagnostics.DueNow);
            Assert.AreEqual(15, diagnostics.RetryBackoffSeconds);
            Assert.AreEqual(4, diagnostics.MaxAttempts);
            Assert.AreEqual(2, diagnostics.MaxConcurrentUploads);
            Assert.IsTrue(diagnostics.BackgroundProcessingEnabled);
            Assert.IsTrue(diagnostics.StatusCounts.ContainsKey("Queued"));
            StringAssert.Contains(diagnostics.Summary, "1 item");
            StringAssert.Contains(diagnostics.QueuePath, "upload-queue.json");

            Assert.AreEqual("Pending", queued.StatusLabel);
            Assert.AreEqual("Attempts 0/4", queued.AttemptLabel);
            Assert.AreEqual("Due now", queued.NextAttemptLabel);
            Assert.IsTrue(queued.CanCancel);
            Assert.IsFalse(queued.CanRetry);

            var canceled = await service.CancelAsync(queued.Id, CancellationToken.None);
            Assert.IsNotNull(canceled);
            Assert.AreEqual("Canceled", canceled.StatusLabel);
            Assert.IsFalse(canceled.CanCancel);
            Assert.IsTrue(canceled.CanRetry);
        });
    }

    [TestMethod]
    public void ShareHistoryRedaction_CoversUploadSessionUrlsAndAuthorizationHeaders()
    {
        var redacted = ShareService.RedactHistoryText(
            "Authorization: Bearer bearer-token-123456 " +
            "Authorization: Basic YmFzaWMtdG9rZW4tc2VjcmV0 " +
            "uploadUrl=https://upload.example.test/session?tempauth=UPLOAD_TEMP_SECRET&authkey=UPLOAD_AUTH_SECRET " +
            "session_url=https://session.example.test/resume?token=SESSION_QUERY_SECRET&code=AUTH_CODE_SECRET " +
            "https://plain.example.test/file?tempauth=PLAIN_TEMP_SECRET&authkey=PLAIN_AUTH_SECRET");

        AssertNoRawUploadSecrets(
            redacted,
            "bearer-token-123456",
            "YmFzaWMtdG9rZW4tc2VjcmV0",
            "UPLOAD_TEMP_SECRET",
            "UPLOAD_AUTH_SECRET",
            "SESSION_QUERY_SECRET",
            "AUTH_CODE_SECRET",
            "PLAIN_TEMP_SECRET",
            "PLAIN_AUTH_SECRET");
        StringAssert.Contains(redacted, "Authorization: Bearer [REDACTED]");
        StringAssert.Contains(redacted, "Authorization: Basic [REDACTED]");
        StringAssert.Contains(redacted, "uploadUrl=[REDACTED]");
        StringAssert.Contains(redacted, "session_url=[REDACTED]");
        StringAssert.Contains(redacted, "tempauth=[REDACTED]");
        StringAssert.Contains(redacted, "authkey=[REDACTED]");
    }

    [TestMethod]
    public async Task Queue_ProcessDue_CopiesLocalFolderUploadAndMarksSucceeded()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.LibraryRoot, "Exports")
            };
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "queued-local.png", 32);

            await queue.EnqueueAsync(item, ShareDestination.LocalFolder, CancellationToken.None);

            var result = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var loaded = await queue.ListAsync(CancellationToken.None);
            var exported = Directory.GetFiles(settings.LocalExportFolder, "queued-local*.png");

            Assert.AreEqual(1, result.Processed);
            Assert.AreEqual(1, result.Succeeded);
            Assert.AreEqual("Succeeded", loaded[0].Status);
            Assert.AreEqual(1, loaded[0].Attempts);
            Assert.IsNotNull(loaded[0].CompletedAt);
            Assert.AreEqual(1, exported.Length);
            Assert.AreEqual(1, sharing.LoadHistory().Count);
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_ProcessesReservedUploadsConcurrently()
    {
        await WithTempPathsAsync(async paths =>
        {
            var provider = new BlockingShareProvider(targetStartCount: 2);
            var settings = new AppSettings();
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(
                paths,
                settings,
                new SecretStore(paths),
                new IShareProvider[] { provider });

            await queue.EnqueueAsync(CreateCaptureItem(paths, "queued-concurrent-one.png", 32), ShareDestination.LocalFolder, CancellationToken.None);
            await queue.EnqueueAsync(CreateCaptureItem(paths, "queued-concurrent-two.png", 32), ShareDestination.LocalFolder, CancellationToken.None);

            var processing = queue.ProcessDueAsync(sharing, 2, CancellationToken.None);

            await WaitWithTimeoutAsync(provider.AllStarted, "both uploads to start");
            var uploading = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(2, uploading.Count(item => item.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase)));

            provider.Release();
            var result = await processing;

            Assert.AreEqual(2, result.Processed);
            Assert.AreEqual(2, result.Succeeded);
            Assert.AreEqual(2, provider.UploadCount);
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_PreservesOperatorCancelDuringInFlightUpload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var provider = new BlockingShareProvider();
            var settings = new AppSettings();
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(
                paths,
                settings,
                new SecretStore(paths),
                new IShareProvider[] { provider });
            var queued = await queue.EnqueueAsync(
                CreateCaptureItem(paths, "queued-cancel-in-flight.png", 32),
                ShareDestination.LocalFolder,
                CancellationToken.None);

            var processing = queue.ProcessDueAsync(sharing, 1, CancellationToken.None);

            await WaitWithTimeoutAsync(provider.AllStarted, "the upload to start");
            var canceled = await queue.CancelAsync(queued.Id, CancellationToken.None);

            Assert.IsNotNull(canceled);
            Assert.AreEqual("Canceled", canceled.Status);

            provider.Release();
            var result = await processing;
            var loaded = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, result.Processed);
            Assert.AreEqual(0, result.Succeeded);
            Assert.AreEqual("Canceled", result.Items[0].Status);
            Assert.AreEqual("Canceled", loaded[0].Status);
            Assert.AreEqual("Canceled by operator.", loaded[0].LastMessage);
            Assert.AreEqual(1, provider.UploadCount);
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_StoresFailureAndSchedulesRetry()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            settings.UploadQueue.RetryBackoffSeconds = 1;
            settings.UploadQueue.MaxAttempts = 2;
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "queued-failing.png", 32);

            await queue.EnqueueAsync(item, ShareDestination.CustomWebhook, CancellationToken.None);

            var result = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var loaded = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, result.Processed);
            Assert.AreEqual(1, result.WaitingRetry);
            Assert.AreEqual("WaitingRetry", loaded[0].Status);
            Assert.AreEqual(1, loaded[0].Attempts);
            Assert.IsNotNull(loaded[0].NextAttemptAt);
            StringAssert.Contains(loaded[0].LastMessage, "No custom webhook URL is configured.");
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_FakeWebhookFailureCanBeRetriedToSuccess()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync((_, _, requestIndex) =>
                requestIndex == 0
                    ? new CapturedResponse(HttpStatusCode.InternalServerError, "temporary failure")
                    : new CapturedResponse(HttpStatusCode.OK, "ok"));
            var settings = new AppSettings
            {
                CustomWebhookUrl = server.BaseUri.ToString()
            };
            settings.UploadQueue.MaxAttempts = 3;
            settings.UploadQueue.RetryBackoffSeconds = 1;
            settings.UploadQueue.RetryFailedUploads = true;
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "queued-webhook.png", 36);

            await queue.EnqueueAsync(item, ShareDestination.CustomWebhook, CancellationToken.None);

            var first = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var afterFailure = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, first.Processed);
            Assert.AreEqual(1, first.WaitingRetry);
            Assert.AreEqual("WaitingRetry", afterFailure[0].Status);
            Assert.AreEqual(1, afterFailure[0].Attempts);
            Assert.IsNotNull(afterFailure[0].NextAttemptAt);
            StringAssert.Contains(afterFailure[0].LastMessage, "Webhook upload failed: 500");
            Assert.AreEqual(1, server.Requests.Count);
            StringAssert.Contains(server.Requests[0].BodyText, "queued-webhook.png");

            var retried = await queue.RetryAsync(afterFailure[0].Id, CancellationToken.None);
            Assert.IsNotNull(retried);
            Assert.AreEqual("Queued", retried.Status);

            var second = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var afterSuccess = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, second.Processed);
            Assert.AreEqual(1, second.Succeeded);
            Assert.AreEqual("Succeeded", afterSuccess[0].Status);
            Assert.AreEqual(2, afterSuccess[0].Attempts);
            Assert.IsNull(afterSuccess[0].NextAttemptAt);
            Assert.IsNotNull(afterSuccess[0].CompletedAt);
            StringAssert.Contains(afterSuccess[0].LastMessage, "Webhook upload completed: 200 OK");
            Assert.AreEqual(2, server.Requests.Count);
            Assert.AreEqual(2, sharing.LoadHistory().Count);
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_DoesNotSendCanceledFakeWebhookItem()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("ok");
            var settings = new AppSettings
            {
                CustomWebhookUrl = server.BaseUri.ToString()
            };
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var queued = await queue.EnqueueAsync(
                CreateCaptureItem(paths, "queued-canceled-webhook.png", 30),
                ShareDestination.CustomWebhook,
                CancellationToken.None);

            var canceled = await queue.CancelAsync(queued.Id, CancellationToken.None);
            var result = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var loaded = await queue.ListAsync(CancellationToken.None);

            Assert.IsNotNull(canceled);
            Assert.AreEqual("Canceled", canceled.Status);
            Assert.AreEqual(0, result.Processed);
            Assert.AreEqual("Canceled", loaded[0].Status);
            Assert.AreEqual("Canceled by operator.", loaded[0].LastMessage);
            Assert.AreEqual(0, server.Requests.Count);
            Assert.AreEqual(0, sharing.LoadHistory().Count);
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_OneDriveUploadSessionFailureRedactsAndRetriesToSuccess()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync((baseUri, _, requestIndex) =>
                requestIndex switch
                {
                    0 => new CapturedResponse(
                        HttpStatusCode.OK,
                        $$"""
                        {"uploadUrl":"{{new Uri(baseUri, "upload-session?tempauth=SESSION_CREATE_FIRST&authkey=AUTH_CREATE_FIRST")}}"}
                        """),
                    1 => new CapturedResponse(
                        HttpStatusCode.ServiceUnavailable,
                        """
                        temporary session failure uploadUrl=https://upload.example.test/session?tempauth=CHUNK_FAIL_SECRET&authkey=CHUNK_AUTH_SECRET
                        """,
                        "text/plain"),
                    2 => new CapturedResponse(
                        HttpStatusCode.OK,
                        $$"""
                        {"uploadUrl":"{{new Uri(baseUri, "upload-session?tempauth=SESSION_CREATE_SECOND&authkey=AUTH_CREATE_SECOND")}}"}
                        """),
                    3 => new CapturedResponse(
                        HttpStatusCode.Accepted,
                        """
                        {"nextExpectedRanges":["3276800-"]}
                        """),
                    4 => new CapturedResponse(
                        HttpStatusCode.Created,
                        """
                        {"id":"one-large","webUrl":"https://onedrive.example.test/item?tempauth=ITEM_SECRET&authkey=ITEM_AUTH_SECRET"}
                        """),
                    5 => new CapturedResponse(
                        HttpStatusCode.OK,
                        """
                        {"link":{"webUrl":"https://onedrive.example.test/share?tempauth=SHARE_SECRET&authkey=SHARE_AUTH_SECRET"}}
                        """),
                    _ => new CapturedResponse(HttpStatusCode.NotFound, "{}")
                });
            var settings = new AppSettings
            {
                OneDriveGraphApiBaseUrl = server.BaseUri.ToString(),
                OneDriveRemoteFolder = "/GoatShot",
                OneDriveCreateAnonymousViewLink = true
            };
            settings.UploadQueue.MaxAttempts = 3;
            settings.UploadQueue.RetryBackoffSeconds = 1;
            settings.UploadQueue.RetryFailedUploads = true;
            var secrets = new SecretStore(paths);
            secrets.SaveOneDriveAccessToken("one-token-secret");
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, secrets);
            var item = CreateCaptureItem(paths, "queued-onedrive-large.mp4", 5 * 1024 * 1024);

            await queue.EnqueueAsync(item, ShareDestination.OneDrive, CancellationToken.None);

            var first = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var afterFailure = await queue.ListAsync(CancellationToken.None);
            var historyAfterFailure = sharing.LoadHistory();

            Assert.AreEqual(1, first.Processed);
            Assert.AreEqual(1, first.WaitingRetry);
            Assert.AreEqual("WaitingRetry", afterFailure[0].Status);
            Assert.AreEqual(1, afterFailure[0].Attempts);
            Assert.IsNotNull(afterFailure[0].NextAttemptAt);
            StringAssert.Contains(afterFailure[0].LastMessage, "OneDrive upload session chunk failed");
            StringAssert.Contains(afterFailure[0].LastMessage, "[REDACTED]");
            AssertNoRawUploadSecrets(afterFailure[0].LastMessage, UploadSessionSecretValues);
            Assert.AreEqual(1, historyAfterFailure.Count);
            AssertNoRawUploadSecrets(historyAfterFailure[0].Message, UploadSessionSecretValues);

            var retried = await queue.RetryAsync(afterFailure[0].Id, CancellationToken.None);
            Assert.IsNotNull(retried);
            Assert.AreEqual("Queued", retried.Status);

            var second = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var afterSuccess = await queue.ListAsync(CancellationToken.None);
            var historyAfterSuccess = sharing.LoadHistory();

            Assert.AreEqual(1, second.Processed);
            Assert.AreEqual(1, second.Succeeded);
            Assert.AreEqual("Succeeded", afterSuccess[0].Status);
            Assert.AreEqual(2, afterSuccess[0].Attempts);
            Assert.IsNull(afterSuccess[0].NextAttemptAt);
            Assert.IsNotNull(afterSuccess[0].CompletedAt);
            StringAssert.Contains(afterSuccess[0].LastMessage, "OneDrive upload-session upload completed");
            StringAssert.Contains(afterSuccess[0].LastMessage, "[REDACTED]");
            AssertNoRawUploadSecrets(afterSuccess[0].LastMessage, UploadSessionSecretValues);
            Assert.AreEqual(2, historyAfterSuccess.Count);
            Assert.IsTrue(historyAfterSuccess[0].Succeeded);
            AssertNoRawUploadSecrets(historyAfterSuccess[0].Message, UploadSessionSecretValues);
            AssertNoRawUploadSecrets(historyAfterSuccess[0].Url, UploadSessionSecretValues);
            StringAssert.Contains(historyAfterSuccess[0].Url ?? string.Empty, "tempauth=[REDACTED]");
            StringAssert.Contains(historyAfterSuccess[0].Url ?? string.Empty, "authkey=[REDACTED]");

            var requests = server.Requests;
            Assert.AreEqual(6, requests.Count);
            var sessionCreates = requests
                .Where(request => request.Path.Contains("createUploadSession", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.AreEqual(2, sessionCreates.Count);
            Assert.IsTrue(sessionCreates.All(request => request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(sessionCreates.All(request => request.Authorization == "Bearer one-token-secret"));

            var chunks = requests
                .Where(request => request.Path.Equals("/upload-session", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.AreEqual(3, chunks.Count);
            Assert.IsTrue(chunks.All(request => request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("bytes 0-3276799/5242880", chunks[0].ContentRange);
            Assert.AreEqual("bytes 0-3276799/5242880", chunks[1].ContentRange);
            Assert.AreEqual("bytes 3276800-5242879/5242880", chunks[2].ContentRange);
            Assert.AreEqual("POST", requests[5].Method);
            StringAssert.Contains(requests[5].Path, "/me/drive/items/one-large/createLink");
        });
    }

    [TestMethod]
    public async Task Queue_ProcessDue_DoesNotSendCanceledOneDriveUploadSessionItem()
    {
        await WithTempPathsAsync(async paths =>
        {
            await using var server = await LocalHttpCaptureServer.StartAsync("{}");
            var settings = new AppSettings
            {
                OneDriveGraphApiBaseUrl = server.BaseUri.ToString(),
                OneDriveRemoteFolder = "/GoatShot"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveOneDriveAccessToken("one-token-secret");
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, secrets);
            var queued = await queue.EnqueueAsync(
                CreateCaptureItem(paths, "queued-canceled-onedrive-large.mp4", 5 * 1024 * 1024),
                ShareDestination.OneDrive,
                CancellationToken.None);

            var canceled = await queue.CancelAsync(queued.Id, CancellationToken.None);
            var result = await queue.ProcessDueAsync(sharing, 5, CancellationToken.None);
            var loaded = await queue.ListAsync(CancellationToken.None);

            Assert.IsNotNull(canceled);
            Assert.AreEqual("Canceled", canceled.Status);
            Assert.AreEqual(0, result.Processed);
            Assert.AreEqual("Canceled", loaded[0].Status);
            Assert.AreEqual("Canceled by operator.", loaded[0].LastMessage);
            Assert.AreEqual(0, server.Requests.Count);
            Assert.AreEqual(0, sharing.LoadHistory().Count);
        });
    }

    [TestMethod]
    public async Task Worker_ProcessOnce_UsesQueueSettingsAndProcessesDueItems()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.LibraryRoot, "WorkerExports")
            };
            settings.UploadQueue.EnableBackgroundProcessing = true;
            settings.UploadQueue.MaxConcurrentUploads = 1;

            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            using var worker = new UploadQueueWorkerService(settings.UploadQueue, queue, sharing);

            await queue.EnqueueAsync(CreateCaptureItem(paths, "worker-local.png", 48), ShareDestination.LocalFolder, CancellationToken.None);

            var result = await worker.ProcessOnceAsync(CancellationToken.None);
            var loaded = await queue.ListAsync(CancellationToken.None);

            Assert.AreEqual(1, result.Processed);
            Assert.AreEqual(1, result.Succeeded);
            Assert.AreEqual("Succeeded", loaded[0].Status);
            Assert.IsFalse(worker.IsRunning);
            StringAssert.Contains(worker.LastStatus, "Processed 1 queue item");
        });
    }

    [TestMethod]
    public void Worker_StartWhenDisabled_DoesNotRunTimer()
    {
        var settings = new UploadQueueSettings
        {
            EnableBackgroundProcessing = false
        };

        WithTempPathsAsync(paths =>
        {
            using var worker = new UploadQueueWorkerService(
                settings,
                new UploadQueueService(paths, settings),
                new ShareService(paths, new AppSettings(), new SecretStore(paths)));

            worker.Start();

            Assert.IsFalse(worker.IsRunning);
            Assert.AreEqual("Background upload queue worker is disabled.", worker.LastStatus);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task Worker_ProcessOnceAfterDispose_ReturnsWithoutThrowing()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            settings.UploadQueue.EnableBackgroundProcessing = true;
            var queue = new UploadQueueService(paths, settings.UploadQueue);
            var worker = new UploadQueueWorkerService(
                settings.UploadQueue,
                queue,
                new ShareService(paths, settings, new SecretStore(paths)));

            worker.Dispose();

            var result = await worker.ProcessOnceAsync(CancellationToken.None);

            Assert.AreEqual(0, result.Processed);
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

    private static async Task WaitWithTimeoutAsync(Task task, string condition)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (!ReferenceEquals(completed, task))
        {
            Assert.Fail($"Timed out waiting for {condition}.");
        }

        await task;
    }

    private sealed class BlockingShareProvider : IShareProvider
    {
        private readonly TaskCompletionSource<object?> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<ShareUploadRequest> _requests = new();
        private readonly object _gate = new();
        private readonly int _targetStartCount;

        public BlockingShareProvider(int targetStartCount = 1)
        {
            _targetStartCount = Math.Max(1, targetStartCount);
        }

        public ShareDestination? Destination => ShareDestination.LocalFolder;
        public string ProviderName => "Blocking local folder test adapter";
        public string AuthType => "None";
        public bool IsImplemented => true;
        public bool SupportsPublicLinks => true;
        public bool SupportsPrivateLinks => true;
        public bool SupportsExpiration => false;
        public bool SupportsPassword => false;
        public Task AllStarted => _allStarted.Task;

        public int UploadCount
        {
            get
            {
                lock (_gate)
                {
                    return _requests.Count;
                }
            }
        }

        public void Release()
        {
            _release.TrySetResult(null);
        }

        public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProviderHealth(true, "ready"));
        }

        public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _requests.Add(request);
                if (_requests.Count >= _targetStartCount)
                {
                    _allStarted.TrySetResult(null);
                }
            }

            await _release.Task.WaitAsync(cancellationToken);
            return new ShareUploadResult(
                true,
                $"https://example.test/{Path.GetFileName(request.FilePath)}",
                "blocking adapter completed");
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

    private static void AssertNoRawUploadSecrets(string? text, params string[] secretValues)
    {
        var inspected = text ?? string.Empty;
        foreach (var secretValue in secretValues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            Assert.IsFalse(
                inspected.Contains(secretValue, StringComparison.OrdinalIgnoreCase),
                $"Text leaked sensitive upload/session value '{secretValue}': {inspected}");
        }
    }
}

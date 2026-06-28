using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ShareHistorySearchTests
{
    [TestMethod]
    public async Task SearchHistory_FiltersByQueryDestinationStatusAndExternalFlag()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.DocumentsRoot, "exports")
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var localItem = CreateCaptureItem(paths, "alpha-export.png", 12);
            var webhookItem = CreateCaptureItem(paths, "beta-webhook.png", 16);

            var localResult = await sharing.ShareAsync(localItem, ShareDestination.LocalFolder, CancellationToken.None);
            var webhookResult = await sharing.ShareAsync(webhookItem, ShareDestination.CustomWebhook, CancellationToken.None);

            Assert.IsTrue(localResult.Succeeded, localResult.Message);
            Assert.IsFalse(webhookResult.Succeeded);

            var alpha = sharing.SearchHistory(query: "alpha export", limit: 10);
            Assert.AreEqual(1, alpha.Count);
            Assert.AreEqual(ShareDestination.LocalFolder, alpha[0].Destination);
            Assert.IsTrue(alpha[0].Succeeded);

            var failedExternalWebhooks = sharing.SearchHistory(
                destination: ShareDestination.CustomWebhook,
                succeeded: false,
                externalDestination: true,
                limit: 10);
            Assert.AreEqual(1, failedExternalWebhooks.Count);
            Assert.AreEqual("beta-webhook.png", failedExternalWebhooks[0].FileName);
            StringAssert.Contains(failedExternalWebhooks[0].Message, "webhook");

            var successfulLocal = sharing.SearchHistory(succeeded: true, externalDestination: false, limit: 10);
            Assert.AreEqual(1, successfulLocal.Count);
            Assert.AreEqual("alpha-export.png", successfulLocal[0].FileName);
        });
    }

    [TestMethod]
    public async Task LoadHistory_RemainsLimitOnlyCompatibilityWrapper()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                LocalExportFolder = Path.Combine(paths.DocumentsRoot, "exports")
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));

            await sharing.ShareAsync(CreateCaptureItem(paths, "first.png", 8), ShareDestination.LocalFolder, CancellationToken.None);
            await sharing.ShareAsync(CreateCaptureItem(paths, "second.png", 8), ShareDestination.LocalFolder, CancellationToken.None);

            var entries = sharing.LoadHistory(1);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("second.png", entries[0].FileName);
        });
    }

    [TestMethod]
    public void ShareHistoryActions_BuildMarkdownOpenabilityAndRetryModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(root, "failed-upload.png");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(filePath, [1, 2, 3, 4]);
            var entry = new ShareHistoryEntry
            {
                Id = "abcdef1234567890",
                CaptureItemId = "capture-1",
                FileName = "failed-upload.png",
                FilePath = filePath,
                Bytes = 4,
                Destination = ShareDestination.CustomWebhook,
                ExternalDestination = true,
                Succeeded = false,
                Message = "Webhook upload failed.",
                Url = "https://example.test/uploads/failed-upload.png"
            };

            var model = new ShareHistoryActionModel(entry);
            var retryItem = ShareHistoryActions.ToRetryCaptureItem(entry);

            Assert.AreEqual("abcdef123456", model.ShortId);
            Assert.IsTrue(model.CanCopyUrl);
            Assert.IsTrue(model.CanOpenUrl);
            Assert.IsTrue(model.CanCopyMarkdown);
            Assert.IsTrue(model.CanRetry);
            StringAssert.Contains(model.MarkdownLink, "![failed-upload]");
            StringAssert.Contains(model.MarkdownLink, entry.Url);
            Assert.AreEqual(entry.CaptureItemId, retryItem.Id);
            Assert.AreEqual(filePath, retryItem.FilePath);
            Assert.AreEqual(4, retryItem.Bytes);

            var found = ShareHistoryActions.FindEntry([entry], "abcdef");
            Assert.AreSame(entry, found);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
}

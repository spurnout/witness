using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceStoreBatchUpdateTests
{
    [TestMethod]
    public void UpdateItemsAsync_PersistsEveryItemInOneCall()
    {
        WithTempStore((store, paths) =>
        {
            var items = new List<CaptureItem>();
            for (var i = 0; i < 3; i++)
            {
                items.Add(store.AddImageFileAsync(WritePng(paths, $"batch-{i}.png"), CaptureKind.Imported).Result);
            }

            for (var i = 0; i < items.Count; i++)
            {
                items[i].OcrText = $"token{i}";
            }

            store.UpdateItemsAsync(items).Wait();

            var reloaded = store.Load();
            Assert.AreEqual(3, reloaded.Count);
            CollectionAssert.AreEquivalent(
                new[] { "token0", "token1", "token2" },
                reloaded.Select(item => item.OcrText).ToArray());
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_InsertsUnknownItemsLikeTheSingleItemPathDoes()
    {
        WithTempStore((store, paths) =>
        {
            var known = store.AddImageFileAsync(WritePng(paths, "known.png"), CaptureKind.Imported).Result;
            known.OcrText = "known";
            var fresh = new CaptureItem
            {
                Kind = CaptureKind.Imported,
                CreatedAt = DateTimeOffset.Now,
                FilePath = WritePng(paths, "fresh.png"),
                OcrText = "fresh"
            };

            store.UpdateItemsAsync([known, fresh]).Wait();

            var reloaded = store.Load();
            Assert.AreEqual(2, reloaded.Count);
            Assert.IsNotNull(reloaded.SingleOrDefault(item => item.OcrText == "fresh"));
            Assert.IsNotNull(reloaded.SingleOrDefault(item => item.OcrText == "known"));
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_UpsertsEachItemIntoTheMetadataIndex()
    {
        WithTempStore((store, paths) =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            store.AttachMetadataIndex(index);
            var item = store.AddImageFileAsync(WritePng(paths, "indexed.png"), CaptureKind.Imported).Result;
            item.OcrText = "zanzibar";

            store.UpdateItemsAsync([item]).Wait();

            Assert.IsTrue(
                index.SearchIds("zanzibar").Contains(item.Id, StringComparer.OrdinalIgnoreCase),
                "Batch updates must reach the FTS index like single-item updates do.");
        });
    }

    private static void WithTempStore(Action<WorkspaceStore, AppPaths> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var originalLocal = Environment.GetEnvironmentVariable("RECEIPTS_LOCAL_ROOT");
        var originalLibrary = Environment.GetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", Path.Combine(root, "library"));
            var paths = AppPaths.Create(new AppSettings());
            Directory.CreateDirectory(paths.TempRoot);
            body(new WorkspaceStore(paths, new AppSettings()), paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", originalLocal);
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", originalLibrary);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string WritePng(AppPaths paths, string name)
    {
        var path = Path.Combine(paths.TempRoot, name);
        using var bitmap = new Bitmap(1, 1);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceStoreBatchUpdateTests
{
    [TestMethod]
    public void ImportFileCopyAsync_ConcurrentSameNameImportsPreserveEveryFile()
    {
        WithTempStore((store, paths) =>
        {
            var inputs = Enumerable.Range(0, 16).Select(i =>
            {
                var directory = Path.Combine(paths.TempRoot, $"source-{i}");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "same.dat");
                File.WriteAllBytes(path, Enumerable.Repeat((byte)i, 64 * 1024).ToArray());
                return path;
            }).ToArray();

            var imported = Task.WhenAll(inputs.Select(path => store.ImportFileCopyAsync(path))).GetAwaiter().GetResult();

            Assert.AreEqual(inputs.Length, imported.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.AreEqual(inputs.Length, store.Load().Count);
            for (var i = 0; i < imported.Length; i++)
            {
                CollectionAssert.AreEqual(File.ReadAllBytes(inputs[i]), File.ReadAllBytes(imported[i].FilePath));
            }
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_FailedSerializationLeavesPreviousIndexIntact()
    {
        WithTempStore((store, paths) =>
        {
            var item = store.AddImageFileAsync(WritePng(paths, "unchanged.png"), CaptureKind.Imported).Result;
            var original = File.ReadAllBytes(paths.IndexPath);
            item.OcrWords = [new OcrRecognizedWord { X = double.NaN }];

            Assert.Throws<ArgumentException>(() => store.UpdateItemAsync(item).GetAwaiter().GetResult());

            CollectionAssert.AreEqual(original, File.ReadAllBytes(paths.IndexPath));
            Assert.AreEqual(0, Directory.GetFiles(paths.LocalRoot, "workspace-index.json.*.tmp").Length);
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_DoesNotOverwriteAnUnreadableIndex()
    {
        WithTempStore((store, paths) =>
        {
            const string damagedIndex = "[{\"id\":\"preserve-me\",";
            File.WriteAllText(paths.IndexPath, damagedIndex);

            Assert.ThrowsExactly<JsonException>(() => store.UpdateItemAsync(new CaptureItem
            {
                FilePath = Path.Combine(paths.ImagesRoot, "new.png")
            }).GetAwaiter().GetResult());

            Assert.AreEqual(damagedIndex, File.ReadAllText(paths.IndexPath));
        });
    }

    [TestMethod]
    public void DeleteItemAsync_DoesNotDeleteTheFileWhenTheIndexIsUnreadable()
    {
        WithTempStore((store, paths) =>
        {
            var item = store.AddImageFileAsync(WritePng(paths, "keep.png"), CaptureKind.Imported).Result;
            File.WriteAllText(paths.IndexPath, "null");

            Assert.ThrowsExactly<JsonException>(() => store.DeleteItemAsync(item, deleteFile: true).GetAwaiter().GetResult());

            Assert.IsTrue(File.Exists(item.FilePath));
            Assert.AreEqual("null", File.ReadAllText(paths.IndexPath));
        });
    }

    [TestMethod]
    public void AddImageFileAsync_ReimportDoesNotLeaveStaleSearchResults()
    {
        WithTempStore((store, paths) =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            store.AttachMetadataIndex(index);
            var path = WritePng(paths, "reimport.png");
            var original = store.AddImageFileAsync(path, CaptureKind.Imported, "obsoleteword").Result;
            var replacement = store.AddImageFileAsync(path, CaptureKind.Imported, "replacementword").Result;

            Assert.AreEqual(replacement.Id, store.Load().Single().Id);
            Assert.AreEqual(0, index.SearchIds("obsoleteword").Count);
            CollectionAssert.AreEqual(new[] { replacement.Id }, index.SearchIds("replacementword").ToArray());
        });
    }

    [TestMethod]
    public void AddImageFileAsync_SameStemFilesHaveIndependentThumbnails()
    {
        WithTempStore((store, paths) =>
        {
            var first = store.AddImageFileAsync(WritePng(paths, "same.png"), CaptureKind.Imported).Result;
            var before = File.ReadAllBytes(first.ThumbnailPath);
            var otherPath = Path.Combine(paths.TempRoot, "same.bmp");
            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, Color.Red);
                bitmap.Save(otherPath, ImageFormat.Bmp);
            }

            var second = store.AddImageFileAsync(otherPath, CaptureKind.Imported).Result;

            Assert.AreNotEqual(first.ThumbnailPath, second.ThumbnailPath);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(first.ThumbnailPath));
        });
    }

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
    public void UpdateItemsAsync_WithoutInsertMissing_SkipsItemsDeletedMidFlight()
    {
        WithTempStore((store, paths) =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            store.AttachMetadataIndex(index);
            var keep = store.AddImageFileAsync(WritePng(paths, "keep.png"), CaptureKind.Imported).Result;
            var deleted = store.AddImageFileAsync(WritePng(paths, "deleted.png"), CaptureKind.Imported).Result;
            store.DeleteItemAsync(deleted, deleteFile: false).Wait();

            keep.OcrText = "kept";
            deleted.OcrText = "resurrected";
            var applied = store.UpdateItemsAsync([keep, deleted], insertMissing: false).Result;

            // A batch writer holding a stale snapshot must not bring a deleted item back to life.
            CollectionAssert.AreEqual(new[] { keep.Id }, applied.Select(item => item.Id).ToArray());
            Assert.AreEqual(keep.Id, store.Load().Single().Id);
            Assert.AreEqual(0, index.SearchIds("resurrected").Count);
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

using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceStoreCacheTests
{
    [TestMethod]
    public void OcrWords_AreKeptOutOfTheIndexButSurviveAReload()
    {
        WithTempPaths(paths =>
        {
            var store = new WorkspaceStore(paths, new AppSettings());
            var item = NewItem(paths, "words.png");
            item.OcrText = "hello world";
            item.OcrRecognizedAt = DateTimeOffset.Now;
            item.OcrWords = [Word("hello", 0), Word("world", 6)];

            store.UpdateItemsAsync([item]).GetAwaiter().GetResult();

            Assert.IsFalse(
                File.ReadAllText(paths.IndexPath).Contains("ocrWords", StringComparison.OrdinalIgnoreCase),
                "Per-word boxes must not be rewritten into the index on every capture.");
            var reloaded = new WorkspaceStore(paths, new AppSettings()).Load().Single();
            CollectionAssert.AreEqual(new[] { "hello", "world" }, reloaded.OcrWords.Select(word => word.Text).ToArray());
        });
    }

    [TestMethod]
    public void LegacyInlineWords_AreReadAndMovedOutOnCompaction()
    {
        WithTempPaths(paths =>
        {
            var recognizedAt = DateTimeOffset.Now;
            var legacy = new[]
            {
                new CaptureItem
                {
                    Id = "legacy",
                    FilePath = Path.Combine(paths.ImagesRoot, "legacy.png"),
                    OcrText = "inline",
                    OcrRecognizedAt = recognizedAt,
                    OcrWords = [Word("inline", 0)]
                }
            };
            Directory.CreateDirectory(paths.LocalRoot);
            File.WriteAllText(paths.IndexPath, JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var store = new WorkspaceStore(paths, new AppSettings());
            Assert.AreEqual("inline", store.Load().Single().OcrWords.Single().Text);

            store.CompactLegacyIndex();

            Assert.IsFalse(File.ReadAllText(paths.IndexPath).Contains("ocrWords", StringComparison.OrdinalIgnoreCase));
            var reloaded = new WorkspaceStore(paths, new AppSettings()).Load().Single();
            Assert.AreEqual("inline", reloaded.OcrWords.Single().Text);
            Assert.AreEqual(recognizedAt, reloaded.OcrRecognizedAt);
        });
    }

    [TestMethod]
    public void StaleWordFiles_AreIgnoredWhenRecognitionChanged()
    {
        WithTempPaths(paths =>
        {
            var store = new WorkspaceStore(paths, new AppSettings());
            var item = NewItem(paths, "rerun.png");
            item.OcrRecognizedAt = DateTimeOffset.Now.AddMinutes(-5);
            item.OcrWords = [Word("old", 0)];
            store.UpdateItemsAsync([item]).GetAwaiter().GetResult();

            // Another writer (an older build, for example) records a newer recognition without words.
            var index = JsonSerializer.Deserialize<List<CaptureItem>>(
                File.ReadAllText(paths.IndexPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            index[0].OcrRecognizedAt = DateTimeOffset.Now;
            File.WriteAllText(paths.IndexPath, JsonSerializer.Serialize(index, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            Assert.AreEqual(0, new WorkspaceStore(paths, new AppSettings()).Load().Single().OcrWords.Count);
        });
    }

    [TestMethod]
    public void Load_SeesItemsWrittenByAnotherStoreInstance()
    {
        WithTempPaths(paths =>
        {
            var app = new WorkspaceStore(paths, new AppSettings());
            var cli = new WorkspaceStore(paths, new AppSettings());
            app.UpdateItemsAsync([NewItem(paths, "from-app.png")]).GetAwaiter().GetResult();
            Assert.AreEqual(1, app.Load().Count);

            cli.UpdateItemsAsync([NewItem(paths, "from-cli.png")]).GetAwaiter().GetResult();
            app.UpdateItemsAsync([NewItem(paths, "from-app-again.png")]).GetAwaiter().GetResult();

            CollectionAssert.AreEquivalent(
                new[] { "from-app.png", "from-cli.png", "from-app-again.png" },
                new WorkspaceStore(paths, new AppSettings()).Load().Select(item => item.FileName).ToArray());
        });
    }

    [TestMethod]
    public void Load_ReturnsCopiesSoUnsavedEditsDoNotLeakIntoTheIndex()
    {
        WithTempPaths(paths =>
        {
            var store = new WorkspaceStore(paths, new AppSettings());
            var item = NewItem(paths, "copy.png");
            item.Notes = "saved";
            store.UpdateItemsAsync([item]).GetAwaiter().GetResult();

            item.Notes = "caller edit after save";
            var loaded = store.Load().Single();
            loaded.Notes = "unsaved edit";
            store.UpdateItemsAsync([NewItem(paths, "other.png")]).GetAwaiter().GetResult();

            var reloaded = new WorkspaceStore(paths, new AppSettings()).Load()
                .Single(candidate => candidate.Id == item.Id);
            Assert.AreEqual("saved", reloaded.Notes);
        });
    }

    [TestMethod]
    public void DeleteItemAsync_RemovesTheWordFile()
    {
        WithTempPaths(paths =>
        {
            var store = new WorkspaceStore(paths, new AppSettings());
            var item = NewItem(paths, "gone.png");
            item.OcrRecognizedAt = DateTimeOffset.Now;
            item.OcrWords = [Word("gone", 0)];
            store.UpdateItemsAsync([item]).GetAwaiter().GetResult();
            Assert.AreEqual(1, Directory.GetFiles(paths.OcrWordsRoot).Length);

            store.DeleteItemAsync(item, deleteFile: false).GetAwaiter().GetResult();

            Assert.AreEqual(0, Directory.GetFiles(paths.OcrWordsRoot).Length);
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_UnserializableWordsLeaveTheIndexIntact()
    {
        WithTempPaths(paths =>
        {
            var store = new WorkspaceStore(paths, new AppSettings());
            var item = NewItem(paths, "nan.png");
            store.UpdateItemsAsync([item]).GetAwaiter().GetResult();
            var original = File.ReadAllBytes(paths.IndexPath);

            item.OcrRecognizedAt = DateTimeOffset.Now;
            item.OcrWords = [new OcrRecognizedWord { X = double.NaN }];

            Assert.Throws<ArgumentException>(() => store.UpdateItemAsync(item).GetAwaiter().GetResult());
            CollectionAssert.AreEqual(original, File.ReadAllBytes(paths.IndexPath));
            Assert.AreEqual(0, store.Load().Single().OcrWords.Count);
        });
    }

    [TestMethod]
    public void RebuildMetadataIndex_KeepsRowsWrittenAfterTheSnapshot()
    {
        WithTempPaths(paths =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            var store = new WorkspaceStore(paths, new AppSettings());
            store.AttachMetadataIndex(index);
            var first = NewItem(paths, "first.png");
            first.OcrText = "alpha";
            store.UpdateItemsAsync([first]).GetAwaiter().GetResult();

            var generation = index.Generation;
            var second = NewItem(paths, "second.png");
            second.OcrText = "bravo";
            store.UpdateItemsAsync([second]).GetAwaiter().GetResult();

            Assert.IsFalse(index.TryRebuild([first], generation), "A stale snapshot must not replace newer rows.");
            store.RebuildMetadataIndex();

            CollectionAssert.AreEqual(new[] { first.Id }, index.SearchIds("alpha").ToArray());
            CollectionAssert.AreEqual(new[] { second.Id }, index.SearchIds("bravo").ToArray());
        });
    }

    private static CaptureItem NewItem(AppPaths paths, string fileName) => new()
    {
        Kind = CaptureKind.Region,
        CreatedAt = DateTimeOffset.Now,
        FilePath = Path.Combine(paths.ImagesRoot, fileName)
    };

    private static OcrRecognizedWord Word(string text, int start) => new()
    {
        Text = text,
        StartIndex = start,
        Length = text.Length,
        X = start * 10,
        Y = 4,
        Width = text.Length * 10,
        Height = 12
    };

    private static void WithTempPaths(Action<AppPaths> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var originalLocal = Environment.GetEnvironmentVariable("RECEIPTS_LOCAL_ROOT");
        var originalLibrary = Environment.GetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", Path.Combine(root, "library"));
            body(AppPaths.Create(new AppSettings()));
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
}

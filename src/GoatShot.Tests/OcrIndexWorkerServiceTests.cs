using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OcrIndexWorkerServiceTests
{
    [TestMethod]
    public void IsIndexable_SkipsPrivateVideoAndAlreadyIndexedItems()
    {
        Assert.IsFalse(OcrIndexPolicy.IsIndexable(new CaptureItem { FilePath = "a.png", IsPrivate = true }));
        Assert.IsFalse(OcrIndexPolicy.IsIndexable(new CaptureItem { FilePath = "clip.mp4" }));
        Assert.IsFalse(OcrIndexPolicy.IsIndexable(new CaptureItem
        {
            FilePath = "done.png",
            OcrRecognizedAt = DateTimeOffset.Now
        }));
        Assert.IsTrue(OcrIndexPolicy.IsIndexable(new CaptureItem { FilePath = "fresh.png" }));
    }

    [TestMethod]
    public void SelectNextBatch_ReturnsNewestFirstAndHonorsSkipList()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => new CaptureItem
            {
                Id = $"item-{i}",
                FilePath = $"item-{i}.png",
                CreatedAt = DateTimeOffset.Now.AddMinutes(-i)
            })
            .ToList();

        var batch = OcrIndexPolicy.SelectNextBatch(
            items,
            batchSize: 2,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "item-0" });

        CollectionAssert.AreEqual(new[] { "item-1", "item-2" }, batch.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void ShouldRaiseOcrCompleted_FiresOnlyForItemsCapturedAfterWorkerStart()
    {
        var startedAt = DateTimeOffset.Now;

        Assert.IsTrue(OcrIndexPolicy.ShouldRaiseOcrCompleted(
            new CaptureItem { CreatedAt = startedAt.AddSeconds(5) }, startedAt));
        Assert.IsFalse(OcrIndexPolicy.ShouldRaiseOcrCompleted(
            new CaptureItem { CreatedAt = startedAt.AddMinutes(-5) }, startedAt));
    }

    [TestMethod]
    public void MergeScanNote_ReplacesThePreviousScanLine()
    {
        Assert.AreEqual(
            "Sensitive scan: 2 findings.",
            OcrIndexPolicy.MergeScanNote(null, "2 findings."));
        Assert.AreEqual(
            $"Source: App / Window{Environment.NewLine}Sensitive scan: 2 findings.",
            OcrIndexPolicy.MergeScanNote(
                $"Source: App / Window{Environment.NewLine}Sensitive scan: clean.",
                "2 findings."));
    }

    [TestMethod]
    public void ProcessOnceAsync_IndexesABatchAndPersistsResults()
    {
        WithTempStore((store, paths) =>
        {
            for (var i = 0; i < 3; i++)
            {
                _ = store.AddImageFileAsync(WritePng(paths, $"scan-{i}.png"), CaptureKind.Imported).Result;
            }

            var completed = new List<string>();
            using var worker = CreateWorker(
                store,
                recognize: (path, _) => Task.FromResult(SuccessResult($"words from {Path.GetFileName(path)}")),
                onCompleted: item =>
                {
                    completed.Add(item.Id);
                    return Task.CompletedTask;
                });
            var indexedEvents = 0;
            worker.ItemIndexed += (_, _) => indexedEvents++;

            var result = worker.ProcessOnceAsync().Result;

            Assert.AreEqual(3, result.Indexed);
            Assert.AreEqual(0, result.Failed);
            Assert.AreEqual(3, indexedEvents);
            var reloaded = store.Load();
            Assert.IsTrue(reloaded.All(item => item.OcrRecognizedAt is not null));
            Assert.IsTrue(reloaded.All(item => item.OcrText!.StartsWith("words from", StringComparison.Ordinal)));
            // The library predates the worker, so backfill must stay silent for automation.
            Assert.AreEqual(0, completed.Count);
        });
    }

    [TestMethod]
    public void ProcessOnceAsync_RecordsFailuresAndDoesNotRetryThemThisSession()
    {
        WithTempStore((store, paths) =>
        {
            _ = store.AddImageFileAsync(WritePng(paths, "good.png"), CaptureKind.Imported).Result;
            _ = store.AddImageFileAsync(WritePng(paths, "bad.png"), CaptureKind.Imported).Result;

            var calls = new List<string>();
            using var worker = CreateWorker(store, recognize: (path, _) =>
            {
                calls.Add(Path.GetFileName(path));
                return Task.FromResult(path.Contains("bad", StringComparison.OrdinalIgnoreCase)
                    ? new OcrRecognitionResult { Succeeded = false, Message = "Recognition failed." }
                    : SuccessResult("recovered text"));
            });

            var first = worker.ProcessOnceAsync().Result;
            var second = worker.ProcessOnceAsync().Result;

            Assert.AreEqual(1, first.Indexed);
            Assert.AreEqual(1, first.Failed);
            Assert.AreEqual(0, second.Indexed);
            Assert.AreEqual(0, second.Failed);
            Assert.AreEqual(2, calls.Count, "The failed item must not be retried within the session.");
        });
    }

    [TestMethod]
    public void ProcessOnceAsync_DoesNotResurrectItemsDeletedDuringThePass()
    {
        WithTempStore((store, paths) =>
        {
            var victim = store.AddImageFileAsync(WritePng(paths, "victim.png"), CaptureKind.Imported).Result;
            Thread.Sleep(20);
            var trigger = store.AddImageFileAsync(WritePng(paths, "trigger.png"), CaptureKind.Imported).Result;

            // The recognizer deletes the OTHER batch item mid-pass, simulating the user removing
            // a capture while the worker holds a stale snapshot of it.
            using var worker = CreateWorker(store, recognize: (path, _) =>
            {
                if (path.Contains("trigger", StringComparison.OrdinalIgnoreCase))
                {
                    store.DeleteItemAsync(victim, deleteFile: false).Wait();
                }

                return Task.FromResult(SuccessResult("text"));
            });
            var indexedIds = new List<string>();
            worker.ItemIndexed += (_, item) => indexedIds.Add(item.Id);

            var result = worker.ProcessOnceAsync().Result;

            Assert.AreEqual(1, result.Indexed);
            CollectionAssert.AreEqual(new[] { trigger.Id }, indexedIds.ToArray());
            Assert.AreEqual(trigger.Id, store.Load().Single().Id);
        });
    }

    [TestMethod]
    public void ProcessOnceAsync_DoesNothingWhenIndexingDisabled()
    {
        WithTempStore((store, paths) =>
        {
            _ = store.AddImageFileAsync(WritePng(paths, "idle.png"), CaptureKind.Imported).Result;

            using var worker = CreateWorker(
                store,
                recognize: (_, _) => Task.FromResult(SuccessResult("never")),
                settings: new AppSettings { EnableOcrIndexing = false });

            var result = worker.ProcessOnceAsync().Result;

            Assert.AreEqual(0, result.Indexed);
            Assert.IsNull(store.Load().Single().OcrRecognizedAt);
        });
    }

    [TestMethod]
    public void ProcessOnceAsync_HonorsCancellationBeforePersisting()
    {
        WithTempStore((store, paths) =>
        {
            _ = store.AddImageFileAsync(WritePng(paths, "cancelled.png"), CaptureKind.Imported).Result;

            using var worker = CreateWorker(store, recognize: (_, _) => Task.FromResult(SuccessResult("never")));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                worker.ProcessOnceAsync(cts.Token).Wait();
                Assert.Fail("A pre-cancelled token must cancel the pass.");
            }
            catch (AggregateException aggregate)
            {
                // TaskCanceledException derives from OperationCanceledException; either honors
                // the cancellation contract.
                Assert.IsInstanceOfType<OperationCanceledException>(aggregate.InnerException);
            }

            Assert.IsNull(store.Load().Single().OcrRecognizedAt);
        });
    }

    private static OcrIndexWorkerService CreateWorker(
        WorkspaceStore store,
        Func<string, CancellationToken, Task<OcrRecognitionResult>> recognize,
        Func<CaptureItem, Task>? onCompleted = null,
        AppSettings? settings = null)
    {
        return new OcrIndexWorkerService(settings ?? new AppSettings(), store, recognize, onCompleted);
    }

    private static OcrRecognitionResult SuccessResult(string text)
    {
        return new OcrRecognitionResult
        {
            Succeeded = true,
            Text = text,
            LanguageTag = "en-US",
            Message = "OCR completed.",
            LineCount = 1,
            Words =
            [
                new OcrRecognizedWord { Text = text, Length = text.Length, Width = 10, Height = 10 }
            ]
        };
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

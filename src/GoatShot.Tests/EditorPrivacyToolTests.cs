using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class EditorPrivacyToolTests
{
    [TestMethod]
    public void EditorToolCatalog_ExposesNewToolsAndKeyboardShortcuts()
    {
        CollectionAssert.Contains(EditorToolCatalog.Shortcuts.Select(tool => tool.Mode).ToArray(), AnnotationMode.Freehand);
        CollectionAssert.Contains(EditorToolCatalog.Shortcuts.Select(tool => tool.Mode).ToArray(), AnnotationMode.Spotlight);

        Assert.IsTrue(EditorToolCatalog.TryGetModeForShortcut("F", out var freehand));
        Assert.AreEqual(AnnotationMode.Freehand, freehand);
        Assert.IsTrue(EditorToolCatalog.TryGetModeForShortcut("S", out var spotlight));
        Assert.AreEqual(AnnotationMode.Spotlight, spotlight);
        Assert.IsTrue(EditorToolCatalog.IsPrivacyTool(AnnotationMode.Redact));
        Assert.IsFalse(EditorToolCatalog.IsPrivacyTool(AnnotationMode.Spotlight));
    }

    [TestMethod]
    public void SensitiveRegionReviewPlanner_BuildsPaddedBoxesWithoutRawValues()
    {
        var item = CreateSensitiveCaptureItem();

        var review = SensitiveRegionReviewPlanner.Build(item);

        Assert.IsTrue(review.Succeeded, review.Message);
        Assert.AreEqual(2, review.Boxes.Count);
        Assert.IsTrue(review.Message.Contains("Review 2 region", StringComparison.Ordinal), review.Message);
        Assert.IsFalse(review.Boxes.Any(box => box.Preview.Contains("jane@example.com", StringComparison.OrdinalIgnoreCase)));

        var email = review.Boxes.Single(box => box.Kind == "email address");
        Assert.AreEqual(6, email.X);
        Assert.AreEqual(26, email.Y);
        Assert.AreEqual(92, email.Width);
        Assert.AreEqual(20, email.Height);
    }

    [TestMethod]
    public void SensitiveRegionReviewPlanner_ExplainsMissingOcrBoxes()
    {
        var review = SensitiveRegionReviewPlanner.Build(new CaptureItem
        {
            OcrText = "Customer email: jane@example.com"
        });

        Assert.IsFalse(review.Succeeded);
        StringAssert.Contains(review.Message, "Run OCR");
    }

    [TestMethod]
    public async Task VisualRedactionService_FlattensDetectedReviewBoxes()
    {
        await WithTempServicesAsync(async services =>
        {
            var item = CreateSensitiveCaptureItem(services.Paths);
            var outputPath = Path.Combine(services.Paths.ImagesRoot, "redacted.png");

            var result = await services.VisualRedactions.RedactSensitiveOcrAsync(
                item,
                outputPath,
                VisualRedactionMode.Solid,
                addToWorkspace: false);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(2, result.RedactionCount);
            Assert.IsTrue(File.Exists(outputPath));
            using var redacted = new Bitmap(outputPath);
            Assert.AreEqual(Color.Black.ToArgb(), redacted.GetPixel(12, 32).ToArgb());
            Assert.AreEqual(Color.White.ToArgb(), redacted.GetPixel(2, 2).ToArgb());
        });
    }

    [TestMethod]
    public async Task VisualRedactionService_PreservesReceiptLineageTransitively()
    {
        await WithTempServicesAsync(async services =>
        {
            var item = CreateSensitiveCaptureItem(services.Paths);
            item.SourceReceiptId = "receipt-parent";
            item.ArtifactRole = "unique-frame";
            item.SourceAvailable = true;
            await File.WriteAllTextAsync(
                item.FilePath + ".receipt-lineage.json",
                JsonSerializer.Serialize(new ReceiptDerivativeLineage
                {
                    DerivativeId = item.Id,
                    SourceReceiptId = item.SourceReceiptId,
                    SourceReceiptPath = Path.Combine(services.Paths.ReceiptsRoot, "receipt-parent"),
                    ArtifactRole = item.ArtifactRole,
                    OutputPath = item.FilePath,
                    SourceSegmentIds = ["segment-1"],
                    StartMonotonicTicks = 10,
                    EndMonotonicTicks = 20
                }));

            var outputPath = Path.Combine(services.Paths.ImagesRoot, "redacted-linked.png");
            var result = await services.VisualRedactions.RedactSensitiveOcrAsync(
                item,
                outputPath,
                VisualRedactionMode.Solid,
                addToWorkspace: true);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(item.SourceReceiptId, result.Item.SourceReceiptId);
            Assert.AreEqual("ocr-redacted-image", result.Item.ArtifactRole);
            Assert.IsFalse(result.Item.IsOriginal);
            Assert.IsTrue(result.Item.SourceAvailable);

            var lineage = JsonSerializer.Deserialize<ReceiptDerivativeLineage>(
                await File.ReadAllTextAsync(outputPath + ".receipt-lineage.json"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(lineage);
            Assert.AreEqual(item.SourceReceiptId, lineage.SourceReceiptId);
            Assert.AreEqual(item.Id, lineage.ParentDerivativeId);
            Assert.AreEqual(item.FilePath, lineage.ParentDerivativePath);
            CollectionAssert.AreEqual(new[] { "segment-1" }, lineage.SourceSegmentIds);
            Assert.AreEqual(10L, lineage.StartMonotonicTicks);
            Assert.AreEqual(20L, lineage.EndMonotonicTicks);

            var persisted = services.WorkspaceStore.Load().Single(saved => saved.Id == result.Item.Id);
            Assert.AreEqual(item.SourceReceiptId, persisted.SourceReceiptId);
        });
    }

    private static CaptureItem CreateSensitiveCaptureItem(AppPaths? paths = null)
    {
        var text = "Customer email: jane@example.com" + Environment.NewLine + "API key: api_key=abcd1234abcd1234";
        var filePath = paths is null
            ? Path.Combine(Path.GetTempPath(), "editor-privacy-source.png")
            : Path.Combine(paths.ImagesRoot, "editor-privacy-source.png");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using (var bitmap = new Bitmap(180, 80, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            bitmap.Save(filePath, ImageFormat.Png);
        }

        var item = new CaptureItem
        {
            Kind = CaptureKind.ActiveWindow,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Width = 180,
            Height = 80,
            Bytes = new FileInfo(filePath).Length,
            OcrText = text,
            OcrRecognizedAt = DateTimeOffset.Now,
            OcrLanguageTag = "en-US"
        };
        item.OcrWords.Add(Word(text, "jane@example.com", line: 0, x: 10, y: 30, width: 84, height: 12));
        item.OcrWords.Add(Word(text, "api_key=abcd1234abcd1234", line: 1, x: 10, y: 54, width: 132, height: 12));
        return item;
    }

    private static OcrRecognizedWord Word(
        string text,
        string word,
        int line,
        double x,
        double y,
        double width,
        double height)
    {
        var start = text.IndexOf(word, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Missing OCR word '{word}'.");
        return new OcrRecognizedWord
        {
            Text = word,
            LineIndex = line,
            StartIndex = start,
            Length = word.Length,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
    }

    private static async Task WithTempServicesAsync(Func<AppServices, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            using var services = AppServices.Create();
            await action(services);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

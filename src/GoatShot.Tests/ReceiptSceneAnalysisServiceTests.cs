using System.Reflection;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReceiptSceneAnalysisServiceTests
{
    [TestMethod]
    public void CompareTexts_ReturnsNullForEquivalentWhitespaceAndCase()
    {
        var change = ReceiptSceneAnalysisService.CompareTexts(
            "Message   remains\r\nvisible",
            " message remains visible ");

        Assert.IsNull(change);
    }

    [TestMethod]
    public void CompareTexts_ClassifiesAddition()
    {
        var change = ReceiptSceneAnalysisService.CompareTexts("message", "message edited");

        Assert.IsNotNull(change);
        Assert.AreEqual(ReceiptOcrChangeKind.PossibleAddition, change.Kind);
        Assert.AreEqual("Possible addition", change.Label);
        Assert.AreEqual(ReceiptChangeReviewState.Pending, change.ReviewState);
        StringAssert.Contains(change.Explanation, "later frame");
    }

    [TestMethod]
    public void CompareTexts_ClassifiesDeletion()
    {
        var change = ReceiptSceneAnalysisService.CompareTexts("message deleted", "message");

        Assert.IsNotNull(change);
        Assert.AreEqual(ReceiptOcrChangeKind.PossibleDeletion, change.Kind);
        Assert.AreEqual("Possible deletion", change.Label);
        StringAssert.Contains(change.Explanation, "no longer");
    }

    [TestMethod]
    public void CompareTexts_ClassifiesReplacementAsEdit()
    {
        var change = ReceiptSceneAnalysisService.CompareTexts(
            "message original",
            "message revised");

        Assert.IsNotNull(change);
        Assert.AreEqual(ReceiptOcrChangeKind.PossibleEdit, change.Kind);
        Assert.AreEqual("Possible edit", change.Label);
        Assert.IsTrue(change.Similarity is >= 0d and < 1d);
        StringAssert.Contains(change.Explanation, "added and removed");
    }

    [TestMethod]
    public void CompareTexts_IgnoresPunctuationOnlyOcrNoise()
    {
        var change = ReceiptSceneAnalysisService.CompareTexts(
            "Message saved",
            "Message saved.");

        Assert.IsNull(change);
    }

    [TestMethod]
    public void CompareTexts_SensitivityControlsSmallTokenNoiseThreshold()
    {
        const string before = "one two three four five six seven eight nine message";
        const string after = "one two three four five six seven eight nine messagf";

        var defaultSensitivity = ReceiptSceneAnalysisService.CompareTexts(before, after, 0.65d);
        var highSensitivity = ReceiptSceneAnalysisService.CompareTexts(before, after, 0.95d);

        Assert.IsNull(defaultSensitivity);
        Assert.IsNotNull(highSensitivity);
        Assert.AreEqual(ReceiptOcrChangeKind.PossibleEdit, highSensitivity.Kind);
    }

    [TestMethod]
    public void ResolveSourceId_ChangesAtDeclaredMonotonicBoundary()
    {
        var track = new ReceiptTrackManifest
        {
            TrackId = "track-a",
            SourceKind = "monitor",
            SourceId = "source-default",
            SourceTransitions =
            [
                new ReceiptSourceTransitionManifest
                {
                    SourceId = "source-a",
                    EffectiveStartMonotonicTicks = 100
                },
                new ReceiptSourceTransitionManifest
                {
                    SourceId = "source-b",
                    EffectiveStartMonotonicTicks = 200
                }
            ]
        };

        Assert.AreEqual("source-default", ResolveSourceId(track, 99));
        Assert.AreEqual("source-a", ResolveSourceId(track, 100));
        Assert.AreEqual("source-a", ResolveSourceId(track, 199));
        Assert.AreEqual("source-b", ResolveSourceId(track, 200));
    }

    [TestMethod]
    public async Task AnalyzeAsync_SceneOnly_DoesNotInvokeOcr()
    {
        await WithReceiptAsync(async (root, manifest) =>
        {
            var ocr = new StubAnalysisOcr("unused", "unused");
            var media = new StubAnalysisMedia(distinct: true);
            var service = new ReceiptSceneAnalysisService(ocr, media);

            var analysis = await service.AnalyzeAsync(
                root,
                new ReceiptAnalysisOptions(
                    EnableSceneIndexing: true,
                    EnableOcrComparison: false,
                    Sensitivity: 0.7d));

            Assert.IsTrue(analysis.SceneIndexingEnabled);
            Assert.IsFalse(analysis.OcrComparisonEnabled);
            Assert.AreEqual(8, analysis.Scenes.Count);
            Assert.AreEqual(0, analysis.Frames.Count);
            Assert.AreEqual(0, analysis.Changes.Count);
            Assert.AreEqual(0, ocr.CallCount, "Scene-only indexing must never invoke local OCR.");
            Assert.AreEqual(7, media.DistinctCallCount);
            Assert.AreEqual(manifest.ReceiptId, analysis.ReceiptId);

            var persisted = JsonSerializer.Deserialize<ReceiptLocalAnalysis>(
                await File.ReadAllTextAsync(Path.Combine(
                    root,
                    "local-analysis",
                    ReceiptSceneAnalysisService.AnalysisFileName)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(persisted);
            Assert.IsTrue(persisted.SceneIndexingEnabled);
            Assert.IsFalse(persisted.OcrComparisonEnabled);
        });
    }

    [TestMethod]
    public async Task AnalyzeAsync_OcrOnly_DoesNotCreateSceneMarkers()
    {
        await WithReceiptAsync(async (root, _) =>
        {
            var ocr = new StubAnalysisOcr(
                "message", "message", "message", "message",
                "message edited", "message edited", "message edited", "message edited");
            var media = new StubAnalysisMedia(distinct: true);
            var service = new ReceiptSceneAnalysisService(ocr, media);

            var analysis = await service.AnalyzeAsync(
                root,
                new ReceiptAnalysisOptions(
                    EnableSceneIndexing: false,
                    EnableOcrComparison: true,
                    Sensitivity: 0.65d));

            Assert.IsFalse(analysis.SceneIndexingEnabled);
            Assert.IsTrue(analysis.OcrComparisonEnabled);
            Assert.AreEqual(0, analysis.Scenes.Count);
            Assert.AreEqual(8, analysis.Frames.Count);
            Assert.AreEqual(1, analysis.Changes.Count);
            Assert.AreEqual(ReceiptOcrChangeKind.PossibleAddition, analysis.Changes[0].Kind);
            Assert.AreEqual(8, ocr.CallCount);
            Assert.AreEqual(0, media.DistinctCallCount, "OCR-only analysis must not run scene differencing.");
        });
    }

    [TestMethod]
    public async Task AnalyzeAsync_AllAnalysisDisabled_DoesNotExtractFrames()
    {
        await WithReceiptAsync(async (root, _) =>
        {
            var ocr = new StubAnalysisOcr("unused");
            var media = new StubAnalysisMedia(distinct: true);
            var service = new ReceiptSceneAnalysisService(ocr, media);

            var analysis = await service.AnalyzeAsync(
                root,
                new ReceiptAnalysisOptions(
                    EnableSceneIndexing: false,
                    EnableOcrComparison: false));

            Assert.AreEqual(0, media.ExtractCallCount);
            Assert.AreEqual(0, ocr.CallCount);
            Assert.AreEqual(1, analysis.Warnings.Count);
            StringAssert.Contains(analysis.Warnings[0], "both disabled");
        });
    }

    [TestMethod]
    public void EnumerateAnalysisOffsets_SamplesInsideSegmentAndCapsWork()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(1500)
            },
            ReceiptSceneAnalysisService
                .EnumerateAnalysisOffsets(TimeSpan.FromSeconds(2).Ticks)
                .ToArray());
        Assert.AreEqual(
            120,
            ReceiptSceneAnalysisService
                .EnumerateAnalysisOffsets(TimeSpan.FromHours(1).Ticks)
                .Count);
    }

    private static string ResolveSourceId(ReceiptTrackManifest track, long monotonicTicks)
    {
        var method = typeof(ReceiptSceneAnalysisService).GetMethod(
            "ResolveSourceId",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "The source-boundary resolver must remain available to the analysis pipeline.");
        return (string)method.Invoke(null, [track, monotonicTicks])!;
    }

    private static async Task WithReceiptAsync(
        Func<string, ReceiptManifest, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var start = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
            var manifest = new ReceiptManifest
            {
                ReceiptId = "analysis-options-fixture",
                CreatedAtUtc = start,
                FinalizedAtUtc = start.AddSeconds(4),
                Tracks =
                [
                    new ReceiptTrackManifest
                    {
                        TrackId = "track-a",
                        SourceKind = "monitor",
                        SourceId = "display-a",
                        DisplayName = "Display A",
                        Bounds = new ReceiptCaptureBoundsManifest { Width = 1280, Height = 720 }
                    }
                ],
                Segments =
                [
                    new ReceiptSegmentManifest
                    {
                        SegmentId = "segment-a",
                        TrackId = "track-a",
                        RelativePath = "segments/track-a/segment-a.mp4",
                        StartMonotonicTicks = 100,
                        DurationTicks = TimeSpan.FromSeconds(2).Ticks
                    },
                    new ReceiptSegmentManifest
                    {
                        SegmentId = "segment-b",
                        TrackId = "track-a",
                        RelativePath = "segments/track-a/segment-b.mp4",
                        StartMonotonicTicks = 100 + TimeSpan.FromSeconds(2).Ticks,
                        DurationTicks = TimeSpan.FromSeconds(2).Ticks
                    }
                ]
            };
            await File.WriteAllBytesAsync(
                Path.Combine(root, ReceiptIntegrityService.ManifestFileName),
                ReceiptCanonicalJson.Serialize(manifest));
            await action(root, manifest);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StubAnalysisOcr(params string[] results) : IReceiptAnalysisOcr
    {
        private readonly Queue<string> _results = new(results);
        public int CallCount { get; private set; }

        public Task<OcrRecognitionResult> RecognizeFileAsync(
            string framePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new OcrRecognitionResult
            {
                Succeeded = true,
                Text = _results.Count > 0 ? _results.Dequeue() : string.Empty,
                LanguageTag = "en-US"
            });
        }
    }

    private sealed class StubAnalysisMedia(bool distinct) : IReceiptAnalysisMedia
    {
        public int ExtractCallCount { get; private set; }
        public int DistinctCallCount { get; private set; }

        public string? ResolveFfmpeg() => "fixture-ffmpeg";

        public async Task<bool> ExtractFrameAsync(
            string ffmpeg,
            string videoPath,
            string outputPath,
            TimeSpan at,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractCallCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, [1, 2, 3], cancellationToken);
            return true;
        }

        public Task<bool> AreFramesDistinctAsync(
            string root,
            string previousRelativePath,
            string currentPath,
            double sensitivity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DistinctCallCount++;
            return Task.FromResult(distinct);
        }
    }
}

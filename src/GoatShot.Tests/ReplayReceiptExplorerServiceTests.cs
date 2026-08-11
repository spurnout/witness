using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReplayReceiptExplorerServiceTests
{
    [TestMethod]
    public async Task ExtractUniqueFramesAsync_CreatesLinkedWorkspaceItemsAndLineageSidecars()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-fixture");
            var frameRoot = Path.Combine(receiptRoot, "local-analysis", "frames");
            Directory.CreateDirectory(frameRoot);
            var firstFrame = Path.Combine(frameRoot, "scene-a.png");
            var duplicateFrame = Path.Combine(frameRoot, "scene-b.png");
            var secondFrame = Path.Combine(frameRoot, "scene-c.png");
            WritePng(firstFrame, Color.DarkSlateBlue);
            WritePng(duplicateFrame, Color.DarkSlateBlue);
            WritePng(secondFrame, Color.DarkOrange);
            var firstOriginalHash = Sha256(firstFrame);
            var secondOriginalHash = Sha256(secondFrame);

            var manifest = BuildManifest();
            WriteReceiptSegments(receiptRoot, manifest);
            await File.WriteAllBytesAsync(
                Path.Combine(receiptRoot, ReceiptIntegrityService.ManifestFileName),
                ReceiptCanonicalJson.Serialize(manifest));
            var analysis = new ReceiptLocalAnalysis
            {
                ReceiptId = manifest.ReceiptId,
                AnalyzedAtUtc = manifest.FinalizedAtUtc,
                Sensitivity = 0.65d,
                Scenes =
                [
                    Scene("scene-a", "segment-a", "local-analysis/frames/scene-a.png", 0, distinct: true),
                    Scene("scene-b", "segment-a", "local-analysis/frames/scene-b.png", 10, distinct: false),
                    Scene("scene-c", "segment-b", "local-analysis/frames/scene-c.png", 20, distinct: true)
                ]
            };
            await File.WriteAllTextAsync(
                Path.Combine(receiptRoot, "local-analysis", ReceiptSceneAnalysisService.AnalysisFileName),
                JsonSerializer.Serialize(analysis, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var settings = new AppSettings();
            var workspace = new WorkspaceStore(paths, settings);
            var metadata = new WorkspaceMetadataIndex(paths);
            workspace.AttachMetadataIndex(metadata);
            var mediaTool = new StubReceiptMediaTool();
            var explorer = new ReplayReceiptExplorerService(paths, workspace, mediaTool);
            var document = await explorer.LoadAsync(receiptRoot);

            var result = await explorer.ExtractUniqueFramesAsync(document, "track-a");

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(2, result.OutputPaths.Count);
            Assert.AreEqual(2, mediaTool.CallCount);
            Assert.IsNotNull(document.Analysis);
            Assert.AreEqual(3, document.Analysis.Scenes.Count);
            foreach (var item in result.Items)
            {
                Assert.AreEqual(CaptureKind.VideoFrame, item.Kind);
                Assert.AreEqual(manifest.ReceiptId, item.SourceReceiptId);
                Assert.AreEqual("unique-frame", item.ArtifactRole);
                Assert.IsFalse(item.IsOriginal);
                Assert.IsTrue(item.SourceAvailable);
                StringAssert.Contains(item.Notes, "original receipt remains unchanged");
                Assert.IsTrue(File.Exists(item.FilePath));

                var lineagePath = item.FilePath + ".receipt-lineage.json";
                Assert.IsTrue(File.Exists(lineagePath));
                var lineage = JsonSerializer.Deserialize<ReceiptDerivativeLineage>(
                    await File.ReadAllTextAsync(lineagePath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.IsNotNull(lineage);
                Assert.AreEqual("receipts.derivative-lineage.v1", lineage.Schema);
                Assert.AreEqual(item.Id, lineage.DerivativeId);
                Assert.AreEqual(manifest.ReceiptId, lineage.SourceReceiptId);
                Assert.AreEqual(Path.GetFullPath(receiptRoot), lineage.SourceReceiptPath);
                Assert.AreEqual("unique-frame", lineage.ArtifactRole);
                Assert.AreEqual(item.FilePath, lineage.OutputPath);
                Assert.AreEqual(1, lineage.SourceSegmentIds.Count);
            }

            var persisted = workspace.Load();
            Assert.AreEqual(2, persisted.Count);
            Assert.IsTrue(persisted.All(item => item.SourceReceiptId == manifest.ReceiptId));
            Assert.AreEqual(firstOriginalHash, Sha256(firstFrame));
            Assert.AreEqual(secondOriginalHash, Sha256(secondFrame));
        });
    }

    [TestMethod]
    public async Task BuildTrackPlaybackAsync_IgnoresUnsignedPackagePlaybackCache()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-playback-cache");
            var manifest = BuildManifest();
            WriteReceiptSegments(receiptRoot, manifest);
            var injected = Path.Combine(receiptRoot, "local-analysis", "playback", "track-a.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(injected)!);
            await File.WriteAllTextAsync(injected, "fabricated playback");
            File.SetLastWriteTimeUtc(injected, DateTime.UtcNow.AddMinutes(5));
            var mediaTool = new StubReceiptMediaTool();
            var explorer = new ReplayReceiptExplorerService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                mediaTool);
            var receipt = new ReplayReceiptDocument(receiptRoot, manifest, null);

            var playback = await explorer.BuildTrackPlaybackAsync(receipt, "track-a");

            Assert.AreEqual(1, mediaTool.CallCount);
            Assert.IsTrue(File.Exists(playback));
            Assert.IsTrue(Path.GetFullPath(playback).StartsWith(
                Path.GetFullPath(paths.TempRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
            Assert.AreNotEqual(Path.GetFullPath(injected), Path.GetFullPath(playback));
            Assert.AreEqual("fabricated playback", await File.ReadAllTextAsync(injected));
        });
    }

    [TestMethod]
    public async Task BuildTrackPlaybackAsync_NormalizesSourceAndAudioTransitions()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-transition-playback");
            var manifest = BuildManifest();
            manifest.CaptureSettings = new ReceiptCaptureSettingsManifest
            {
                FramesPerSecond = 30,
                VideoBitrateBitsPerSecond = 6_000_000
            };
            manifest.Tracks[0].SourceTransitions =
            [
                new ReceiptSourceTransitionManifest
                {
                    SourceId = "display-a",
                    EffectiveStartMonotonicTicks = 0,
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1280, Height = 720 }
                },
                new ReceiptSourceTransitionManifest
                {
                    SourceId = "display-b",
                    EffectiveStartMonotonicTicks = 20,
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1024, Height = 768 }
                }
            ];
            manifest.Segments[1].IncludesMicrophone = true;
            WriteReceiptSegments(receiptRoot, manifest);
            var mediaTool = new StubReceiptMediaTool();
            var explorer = new ReplayReceiptExplorerService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                mediaTool);

            var output = await explorer.BuildTrackPlaybackAsync(
                new ReplayReceiptDocument(receiptRoot, manifest, null),
                "track-a");

            Assert.IsTrue(File.Exists(output));
            CollectionAssert.Contains(mediaTool.LastArguments, "-filter_complex");
            CollectionAssert.Contains(mediaTool.LastArguments, "libopenh264");
            CollectionAssert.Contains(mediaTool.LastArguments, "aac");
            var filter = mediaTool.LastArguments[Array.IndexOf(mediaTool.LastArguments, "-filter_complex") + 1];
            StringAssert.Contains(filter, "scale=1280:768");
            StringAssert.Contains(filter, "anullsrc");
            StringAssert.Contains(filter, "[1:a]");
        });
    }

    [TestMethod]
    public async Task LoadAsync_DropsTraversingUnsignedAnalysisFrames()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-unsafe-analysis");
            Directory.CreateDirectory(Path.Combine(receiptRoot, "local-analysis"));
            var manifest = BuildManifest();
            await File.WriteAllBytesAsync(
                Path.Combine(receiptRoot, ReceiptIntegrityService.ManifestFileName),
                ReceiptCanonicalJson.Serialize(manifest));
            var outside = Path.Combine(paths.ReceiptsRoot, "outside.png");
            WritePng(outside, Color.Red);
            var analysis = new ReceiptLocalAnalysis
            {
                ReceiptId = manifest.ReceiptId,
                Scenes =
                [
                    Scene(
                        "unsafe",
                        "segment-a",
                        Path.GetRelativePath(receiptRoot, outside),
                        0,
                        distinct: true)
                ]
            };
            await File.WriteAllTextAsync(
                Path.Combine(receiptRoot, "local-analysis", ReceiptSceneAnalysisService.AnalysisFileName),
                JsonSerializer.Serialize(analysis, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var explorer = new ReplayReceiptExplorerService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                new StubReceiptMediaTool());

            var document = await explorer.LoadAsync(receiptRoot);

            Assert.IsNotNull(document.Analysis);
            Assert.AreEqual(0, document.Analysis.Scenes.Count);
            Assert.IsTrue(document.Analysis.Warnings.Any(warning => warning.Contains("Ignored", StringComparison.Ordinal)));
        });
    }

    [TestMethod]
    public async Task ExtractUniqueFramesAsync_WithoutIndexedScenesDoesNotCreateDerivatives()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-empty-analysis");
            Directory.CreateDirectory(receiptRoot);
            var manifest = BuildManifest();
            var document = new ReplayReceiptDocument(
                receiptRoot,
                manifest,
                new ReceiptLocalAnalysis { ReceiptId = manifest.ReceiptId });
            var workspace = new WorkspaceStore(paths, new AppSettings());
            var explorer = new ReplayReceiptExplorerService(paths, workspace);

            var result = await explorer.ExtractUniqueFramesAsync(document, "track-a");

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.Items.Count);
            Assert.AreEqual(0, result.OutputPaths.Count);
            Assert.AreEqual(0, workspace.Load().Count);
            StringAssert.Contains(result.Message, "Run local analysis first");
        });
    }

    [TestMethod]
    public async Task ExportTracksAsync_ExportsEverySelectedTrackWithIndependentLineage()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-track-export");
            var manifest = BuildMultiTrackManifest();
            WriteReceiptSegments(receiptRoot, manifest);
            var originalHashes = manifest.Segments.ToDictionary(
                segment => segment.SegmentId,
                segment => Sha256(Path.Combine(receiptRoot, segment.RelativePath)),
                StringComparer.Ordinal);
            var workspace = new WorkspaceStore(paths, new AppSettings());
            var metadata = new WorkspaceMetadataIndex(paths);
            workspace.AttachMetadataIndex(metadata);
            var explorer = new ReplayReceiptExplorerService(paths, workspace);
            var receipt = new ReplayReceiptDocument(receiptRoot, manifest, null);

            var result = await explorer.ExportTracksAsync(receipt, ["track-b", "track-a"]);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(2, result.OutputPaths.Count);
            Assert.IsTrue(result.Items.All(item => item.ArtifactRole == "exported-track"));
            Assert.IsTrue(result.Items.All(item => item.SourceReceiptId == manifest.ReceiptId));
            foreach (var output in result.OutputPaths)
            {
                Assert.IsTrue(File.Exists(output));
                Assert.IsTrue(File.Exists(output + ".receipt-lineage.json"));
                var lineage = JsonSerializer.Deserialize<ReceiptDerivativeLineage>(
                    await File.ReadAllTextAsync(output + ".receipt-lineage.json"),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.IsNotNull(lineage);
                Assert.AreEqual(1, lineage.SourceSegmentIds.Count);
                Assert.IsNotNull(lineage.StartMonotonicTicks);
                Assert.IsNotNull(lineage.EndMonotonicTicks);
            }

            foreach (var segment in manifest.Segments)
            {
                Assert.AreEqual(
                    originalHashes[segment.SegmentId],
                    Sha256(Path.Combine(receiptRoot, segment.RelativePath)),
                    $"Original segment {segment.SegmentId} was modified.");
            }
        });
    }

    [TestMethod]
    public async Task ExportCompositeAsync_PreservesMonotonicOffsetsAndCreatesOneLinkedDerivative()
    {
        await WithTempPathsAsync(async paths =>
        {
            var receiptRoot = Path.Combine(paths.ReceiptsRoot, "receipt-composite-export");
            var manifest = BuildMultiTrackManifest();
            WriteReceiptSegments(receiptRoot, manifest);
            var originalHashes = manifest.Segments.ToDictionary(
                segment => segment.SegmentId,
                segment => Sha256(Path.Combine(receiptRoot, segment.RelativePath)),
                StringComparer.Ordinal);
            var workspace = new WorkspaceStore(paths, new AppSettings());
            var metadata = new WorkspaceMetadataIndex(paths);
            workspace.AttachMetadataIndex(metadata);
            var mediaTool = new StubReceiptMediaTool();
            var explorer = new ReplayReceiptExplorerService(paths, workspace, mediaTool);
            var receipt = new ReplayReceiptDocument(receiptRoot, manifest, null);

            var result = await explorer.ExportCompositeAsync(receipt, ["track-a", "track-b"]);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual("composite-video", result.Items[0].ArtifactRole);
            Assert.AreEqual(CaptureKind.RecordingMp4, result.Items[0].Kind);
            Assert.AreEqual(1, mediaTool.CallCount);
            var filterIndex = Array.IndexOf(mediaTool.LastArguments, "-filter_complex");
            Assert.IsTrue(filterIndex >= 0);
            var filter = mediaTool.LastArguments[filterIndex + 1];
            StringAssert.Contains(filter, "[1:v]setpts=PTS-STARTPTS+1/TB");
            StringAssert.Contains(filter, "xstack=inputs=2:layout=0_0|1280_0");
            StringAssert.Contains(filter, "color=c=black:s=1280x768");
            CollectionAssert.Contains(mediaTool.LastArguments, "3");
            CollectionAssert.Contains(mediaTool.LastArguments, "libopenh264");

            var lineage = JsonSerializer.Deserialize<ReceiptDerivativeLineage>(
                await File.ReadAllTextAsync(result.OutputPaths[0] + ".receipt-lineage.json"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(lineage);
            CollectionAssert.AreEquivalent(
                new[] { "segment-a", "segment-b" },
                lineage.SourceSegmentIds);
            Assert.AreEqual(0L, lineage.StartMonotonicTicks);
            Assert.AreEqual(TimeSpan.FromSeconds(3).Ticks, lineage.EndMonotonicTicks);

            foreach (var segment in manifest.Segments)
            {
                Assert.AreEqual(
                    originalHashes[segment.SegmentId],
                    Sha256(Path.Combine(receiptRoot, segment.RelativePath)),
                    $"Original segment {segment.SegmentId} was modified.");
            }
        });
    }

    private static ReceiptManifest BuildManifest()
    {
        var createdAtUtc = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        return new ReceiptManifest
        {
            ReceiptId = "receipt-explorer-fixture",
            CreatedAtUtc = createdAtUtc,
            FinalizedAtUtc = createdAtUtc.AddSeconds(4),
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
                    CapturedAtUtc = createdAtUtc,
                    DurationTicks = 20,
                    StartMonotonicTicks = 0
                },
                new ReceiptSegmentManifest
                {
                    SegmentId = "segment-b",
                    TrackId = "track-a",
                    SequenceNumber = 1,
                    RelativePath = "segments/track-a/segment-b.mp4",
                    CapturedAtUtc = createdAtUtc.AddSeconds(2),
                    DurationTicks = 20,
                    StartMonotonicTicks = 20
                }
            ]
        };
    }

    private static ReceiptManifest BuildMultiTrackManifest()
    {
        var start = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        return new ReceiptManifest
        {
            ReceiptId = "receipt-multi-track-fixture",
            CreatedAtUtc = start,
            FinalizedAtUtc = start.AddSeconds(3),
            CaptureSettings = new ReceiptCaptureSettingsManifest { FramesPerSecond = 30 },
            Tracks =
            [
                new ReceiptTrackManifest
                {
                    TrackId = "track-a",
                    SourceKind = "monitor",
                    SourceId = "display-a",
                    DisplayName = "Display A",
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1280, Height = 720 }
                },
                new ReceiptTrackManifest
                {
                    TrackId = "track-b",
                    SourceKind = "monitor",
                    SourceId = "display-b",
                    DisplayName = "Display B",
                    Bounds = new ReceiptCaptureBoundsManifest { Width = 1024, Height = 768 }
                }
            ],
            Segments =
            [
                new ReceiptSegmentManifest
                {
                    SegmentId = "segment-a",
                    TrackId = "track-a",
                    RelativePath = "segments/track-a/segment-a.mp4",
                    StartMonotonicTicks = 0,
                    DurationTicks = TimeSpan.FromSeconds(2).Ticks
                },
                new ReceiptSegmentManifest
                {
                    SegmentId = "segment-b",
                    TrackId = "track-b",
                    RelativePath = "segments/track-b/segment-b.mp4",
                    StartMonotonicTicks = TimeSpan.FromSeconds(1).Ticks,
                    DurationTicks = TimeSpan.FromSeconds(2).Ticks
                }
            ]
        };
    }

    private static void WriteReceiptSegments(string receiptRoot, ReceiptManifest manifest)
    {
        Directory.CreateDirectory(receiptRoot);
        foreach (var segment in manifest.Segments)
        {
            var path = Path.Combine(receiptRoot, segment.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes($"fixture:{segment.SegmentId}"));
        }
    }

    private static ReceiptSceneMarker Scene(
        string sceneId,
        string segmentId,
        string relativePath,
        long ticks,
        bool distinct) => new()
    {
        SceneId = sceneId,
        TrackId = "track-a",
        SegmentId = segmentId,
        MonotonicTicks = ticks,
        RelativeFramePath = relativePath,
        IsVisuallyDistinct = distinct
    };

    private static void WritePng(string path, Color color)
    {
        using var bitmap = new Bitmap(32, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var originalLocal = Environment.GetEnvironmentVariable("RECEIPTS_LOCAL_ROOT");
        var originalLibrary = Environment.GetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", Path.Combine(root, "library"));
            var paths = AppPaths.Create(new AppSettings());
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", originalLocal);
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", originalLibrary);
            DeleteDirectoryWithRetry(root);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class StubReceiptMediaTool : IReplayReceiptMediaTool
    {
        public int CallCount { get; private set; }
        public string[] LastArguments { get; private set; } = [];

        public async Task RunFfmpegAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastArguments = arguments.ToArray();
            var output = LastArguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (Path.GetExtension(output).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                WritePng(output, Color.Teal);
            }
            else
            {
                await File.WriteAllBytesAsync(output, [0, 0, 0, 24, 102, 116, 121, 112], cancellationToken);
            }
        }
    }
}

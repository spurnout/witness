using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReplayReceiptAcceptanceTests
{
    [TestMethod]
    public async Task ReplayReceipt_EndToEnd_SaveAnalyzeExtractTamperAndSaveAgainWhileBufferContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings
            {
                LibraryRoot = Path.Combine(root, "library"),
                Replay = new ReplayBufferSettings
                {
                    BufferDuration = TimeSpan.FromSeconds(60),
                    SaveDuration = TimeSpan.FromSeconds(60),
                    SegmentDuration = TimeSpan.FromSeconds(2),
                    MaxBufferBytes = 512L * 1024L * 1024L,
                    FramesPerSecond = 30,
                    CaptureSource = new ReplayCaptureSourceDescriptor(
                        ReplayCaptureSourceKind.SelectedMonitor,
                        "display-fixture",
                        "Fixture display",
                        new ReplayCaptureBounds(0, 0, 1280, 720)),
                    EnableSceneIndexing = true,
                    EnableLocalOcrIndexing = true,
                    AnalysisSensitivity = 0.65d
                }
            };
            var paths = AppPaths.Create(settings, Path.Combine(root, "local"));
            var storage = new FileReplayBufferStorage(paths.ReplayBufferRoot);
            var deviceKeys = new ReceiptDeviceKeyService();
            var integrity = new ReceiptIntegrityService(deviceKeys);
            var packagePublisher = new ReplayReceiptPackagePublisher(
                storage,
                integrity,
                deviceKeys,
                Path.Combine(paths.SecretsRoot, ReceiptDeviceKeyService.DefaultKeyFileName),
                settings);
            var publisher = new FirstPublicationBarrier(packagePublisher);
            var coordinator = new ReplayBufferCoordinator(settings.Replay, publisher, storage);

            Assert.AreEqual(TimeSpan.FromSeconds(60), coordinator.Settings.BufferDuration);
            Assert.IsTrue(coordinator.Arm().Succeeded);
            var fixtureStart = DateTimeOffset.UtcNow.AddMinutes(-10);
            var before = WriteBufferedSegment(paths.ReplayBufferRoot, "message-before", 0, fixtureStart);
            var edited = WriteBufferedSegment(paths.ReplayBufferRoot, "message-edited", 1, fixtureStart);
            var deleted = WriteBufferedSegment(paths.ReplayBufferRoot, "message-deleted", 2, fixtureStart);
            Assert.IsTrue(coordinator.AddFinalizedSegment(before).Accepted);
            Assert.IsTrue(coordinator.AddFinalizedSegment(edited).Accepted);
            Assert.IsTrue(coordinator.AddFinalizedSegment(deleted).Accepted);

            var firstDestination = Path.Combine(paths.ReceiptsRoot, "acceptance-first");
            var firstSaveTask = coordinator.SaveAsync(
                new ReplaySaveRequest(
                    firstDestination,
                    TimeSpan.FromSeconds(60),
                    "acceptance-first"),
                CancellationToken.None);
            await publisher.FirstPublicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(ReplayBufferState.Saving, coordinator.GetStatus().State);
            var whileSaving = WriteBufferedSegment(
                paths.ReplayBufferRoot,
                "buffer-kept-running",
                3,
                fixtureStart);
            Assert.IsTrue(coordinator.AddFinalizedSegment(whileSaving).Accepted);
            publisher.ReleaseFirstPublication.TrySetResult();

            var firstSave = await firstSaveTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(firstSave.Succeeded, firstSave.Message);
            Assert.IsTrue(firstSave.BufferContinued);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
            Assert.AreEqual(4, coordinator.GetStatus().SegmentCount);
            CollectionAssert.AreEqual(
                new[] { "message-before", "message-edited", "message-deleted" },
                firstSave.Segments.Select(segment => segment.SegmentId).ToArray());

            var analysisService = new ReceiptSceneAnalysisService(
                new FixtureOcr(
                    Repeat("message original", 4)
                        .Concat(Repeat("message edited", 4))
                        .Concat(Repeat(string.Empty, 4))
                        .ToArray()),
                new FixtureAnalysisMedia());
            var analysis = await analysisService.AnalyzeAsync(
                firstSave.PackagePath!,
                new ReceiptAnalysisOptions(
                    EnableSceneIndexing: true,
                    EnableOcrComparison: true,
                    Sensitivity: 0.65d));

            Assert.AreEqual(12, analysis.Frames.Count);
            Assert.AreEqual(3, analysis.Scenes.Count(scene => scene.IsVisuallyDistinct));
            CollectionAssert.AreEqual(
                new[] { ReceiptOcrChangeKind.PossibleEdit, ReceiptOcrChangeKind.PossibleDeletion },
                analysis.Changes.Select(change => change.Kind).ToArray());
            Assert.IsTrue(analysis.Changes.All(change =>
                change.ReviewState == ReceiptChangeReviewState.Pending));

            var workspace = new WorkspaceStore(paths, settings);
            workspace.AttachMetadataIndex(new WorkspaceMetadataIndex(paths));
            var explorerMedia = new FixtureExplorerMediaTool();
            var explorer = new ReplayReceiptExplorerService(paths, workspace, explorerMedia);
            var receipt = await explorer.LoadAsync(firstSave.PackagePath!);
            var playback = await explorer.BuildTrackPlaybackAsync(receipt, "track-fixture");
            Assert.IsTrue(File.Exists(playback));

            var editFinding = receipt.Analysis!.Changes.Single(change =>
                change.Kind == ReceiptOcrChangeKind.PossibleEdit);
            var beforeFrame = receipt.Analysis.Frames.Single(frame =>
                frame.FrameId.Equals(editFinding.BeforeFrameId, StringComparison.Ordinal));
            var afterFrame = receipt.Analysis.Frames.Single(frame =>
                frame.FrameId.Equals(editFinding.AfterFrameId, StringComparison.Ordinal));
            var beforePreview = await explorer.BuildAnalysisFramePreviewAsync(
                receipt,
                beforeFrame.TrackId,
                beforeFrame.SegmentId,
                beforeFrame.MonotonicTicks);
            var afterPreview = await explorer.BuildAnalysisFramePreviewAsync(
                receipt,
                afterFrame.TrackId,
                afterFrame.SegmentId,
                afterFrame.MonotonicTicks);
            AssertPng(beforePreview);
            AssertPng(afterPreview);

            var derivatives = await explorer.ExtractUniqueFramesAsync(receipt, "track-fixture");
            Assert.IsTrue(derivatives.Succeeded, derivatives.Message);
            Assert.AreEqual(3, derivatives.Items.Count);
            Assert.IsTrue(derivatives.Items.All(item =>
                item.SourceReceiptId == "acceptance-first" &&
                item.ArtifactRole == "unique-frame" &&
                !item.IsOriginal));
            foreach (var item in derivatives.Items)
            {
                AssertPng(item.FilePath);
                var lineagePath = item.FilePath + ".receipt-lineage.json";
                Assert.IsTrue(File.Exists(lineagePath));
                var lineage = JsonSerializer.Deserialize<ReceiptDerivativeLineage>(
                    await File.ReadAllTextAsync(lineagePath),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.IsNotNull(lineage);
                Assert.AreEqual("acceptance-first", lineage.SourceReceiptId);
                Assert.AreEqual(1, lineage.SourceSegmentIds.Count);
            }

            var keyPath = Path.Combine(paths.SecretsRoot, ReceiptDeviceKeyService.DefaultKeyFileName);
            var intact = await integrity.VerifyPackageAsync(firstSave.PackagePath!, keyPath);
            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, intact.Status);

            var signedSegment = firstSave.Segments[0].FullPath;
            await File.AppendAllBytesAsync(signedSegment, Encoding.UTF8.GetBytes("tampered"));
            var modified = await integrity.VerifyPackageAsync(firstSave.PackagePath!, keyPath);
            Assert.AreEqual(ReceiptVerificationStatus.Modified, modified.Status);

            var secondDestination = Path.Combine(paths.ReceiptsRoot, "acceptance-second");
            var secondSave = await coordinator.SaveAsync(
                new ReplaySaveRequest(
                    secondDestination,
                    TimeSpan.FromSeconds(60),
                    "acceptance-second"),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.IsTrue(secondSave.Succeeded, secondSave.Message);
            Assert.IsTrue(secondSave.BufferContinued);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
            Assert.AreEqual(4, secondSave.Segments.Count);
            var secondIntact = await integrity.VerifyPackageAsync(secondSave.PackagePath!, keyPath);
            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, secondIntact.Status);
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    private static ReplaySegmentMetadata WriteBufferedSegment(
        string bufferRoot,
        string segmentId,
        long sequence,
        DateTimeOffset fixtureStart)
    {
        var directory = Path.Combine(bufferRoot, "acceptance", "track-fixture");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{sequence:000}-{segmentId}.mp4");
        var payload = Encoding.UTF8.GetBytes($"deterministic replay fixture: {segmentId}");
        File.WriteAllBytes(path, payload);
        var source = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.SelectedMonitor,
            "display-fixture",
            "Fixture display",
            new ReplayCaptureBounds(0, 0, 1280, 720));
        var track = new ReplayTrackDescriptor(
            "track-fixture",
            "Fixture display",
            source,
            1280,
            720);
        return new ReplaySegmentMetadata(
            segmentId,
            sequence,
            track,
            path,
            fixtureStart.AddSeconds(sequence * 2),
            TimeSpan.FromSeconds(sequence * 2),
            TimeSpan.FromSeconds(2),
            payload.LongLength,
            EncodedFrameCount: 60);
    }

    private static IEnumerable<string> Repeat(string value, int count) =>
        Enumerable.Repeat(value, count);

    private static void AssertPng(string path)
    {
        Assert.IsTrue(File.Exists(path), $"Expected PNG output was not created: {path}");
        var signature = File.ReadAllBytes(path).Take(8).ToArray();
        CollectionAssert.AreEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            signature,
            $"Expected a PNG signature at {path}.");
    }

    private static void WritePng(string path, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(48, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
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
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class FirstPublicationBarrier(IReplaySnapshotPublisher inner) : IReplaySnapshotPublisher
    {
        private int _publicationCount;

        public TaskCompletionSource FirstPublicationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstPublication { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _publicationCount) == 1)
            {
                FirstPublicationStarted.TrySetResult();
                await ReleaseFirstPublication.Task.WaitAsync(cancellationToken);
            }

            return await inner.PublishAsync(publication, cancellationToken);
        }
    }

    private sealed class FixtureOcr(params string[] results) : IReceiptAnalysisOcr
    {
        private readonly Queue<string> _results = new(results);

        public Task<OcrRecognitionResult> RecognizeFileAsync(
            string framePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new OcrRecognitionResult
            {
                Succeeded = true,
                Text = _results.Count == 0 ? string.Empty : _results.Dequeue(),
                LanguageTag = "en-US"
            });
        }
    }

    private sealed class FixtureAnalysisMedia : IReceiptAnalysisMedia
    {
        public string? ResolveFfmpeg() => "fixture-ffmpeg";

        public Task<bool> ExtractFrameAsync(
            string ffmpeg,
            string videoPath,
            string outputPath,
            TimeSpan at,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var color = videoPath.Contains("message-before", StringComparison.Ordinal)
                ? Color.DarkSlateBlue
                : videoPath.Contains("message-edited", StringComparison.Ordinal)
                    ? Color.DarkOrange
                    : Color.Black;
            WritePng(outputPath, color);
            return Task.FromResult(true);
        }

        public Task<bool> AreFramesDistinctAsync(
            string root,
            string previousRelativePath,
            string currentPath,
            double sensitivity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = Path.Combine(root, previousRelativePath);
            return Task.FromResult(!File.ReadAllBytes(previous).SequenceEqual(File.ReadAllBytes(currentPath)));
        }
    }

    private sealed class FixtureExplorerMediaTool : IReplayReceiptMediaTool
    {
        public Task RunFfmpegAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (Path.GetExtension(output).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                WritePng(output, Color.Teal);
            }
            else
            {
                File.WriteAllBytes(output, [0, 0, 0, 24, 102, 116, 121, 112]);
            }

            return Task.CompletedTask;
        }
    }
}

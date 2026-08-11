using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayBufferSettingsTests
{
    [TestMethod]
    public void Defaults_MatchReplayProductDefaults()
    {
        var settings = new ReplayBufferSettings();

        Assert.AreEqual(TimeSpan.FromSeconds(60), settings.BufferDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(2), settings.SegmentDuration);
        Assert.AreEqual(512L * 1024L * 1024L, settings.MaxBufferBytes);
        Assert.AreEqual(30, settings.FramesPerSecond);
        Assert.AreEqual(ReplayCaptureSourceKind.FollowCursorMonitor, settings.CaptureSource.Kind);
        Assert.IsTrue(settings.EnableLocalOcrIndexing);
    }

    [TestMethod]
    public void Normalize_RepairsInvalidBoundsAndKeepsConfigurationFlexible()
    {
        var source = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.SeparateMonitorTracks,
            "desktop",
            "Separate monitors");
        var settings = new ReplayBufferSettings
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            SegmentDuration = TimeSpan.FromSeconds(3),
            MaxBufferBytes = 0,
            FramesPerSecond = 900,
            CaptureSource = source,
            EnableLocalOcrIndexing = false
        };

        var normalized = settings.Normalize();

        Assert.AreEqual(TimeSpan.FromSeconds(3), normalized.BufferDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(3), normalized.SegmentDuration);
        Assert.AreEqual(ReplayBufferSettings.DefaultMaxBufferBytes, normalized.MaxBufferBytes);
        Assert.AreEqual(120, normalized.FramesPerSecond);
        Assert.AreSame(source, normalized.CaptureSource);
        Assert.IsFalse(normalized.EnableLocalOcrIndexing);
    }
}

[TestClass]
public sealed class ReplaySegmentCatalogTests
{
    [TestMethod]
    public void Add_EvictsExpiredSegmentsAcrossTracksByMonotonicTime()
    {
        var released = new List<ReplaySegmentMetadata>();
        var catalog = new ReplaySegmentCatalog(
            new ReplayBufferSettings
            {
                BufferDuration = TimeSpan.FromSeconds(6),
                MaxBufferBytes = 1_000
            },
            released.Add);

        catalog.Add(CreateSegment("a-0", "track-a", 0, 10));
        catalog.Add(CreateSegment("b-0", "track-b", 0, 10));
        catalog.Add(CreateSegment("a-2", "track-a", 2, 10));
        catalog.Add(CreateSegment("b-4", "track-b", 4, 10));
        catalog.Add(CreateSegment("a-6", "track-a", 6, 10));

        var snapshot = catalog.GetSnapshot();

        CollectionAssert.AreEqual(
            new[] { "a-2", "b-4", "a-6" },
            snapshot.Segments.Select(segment => segment.SegmentId).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "a-0", "b-0" },
            released.Select(segment => segment.SegmentId).ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(6), snapshot.BufferedDuration);
    }

    [TestMethod]
    public void Add_EnforcesAggregateByteLimitWithoutSplittingSynchronizedCaptureSets()
    {
        var catalog = new ReplaySegmentCatalog(new ReplayBufferSettings
        {
            BufferDuration = TimeSpan.FromMinutes(5),
            MaxBufferBytes = 25
        });

        catalog.Add(CreateSegment("a", "track-a", 0, 10));
        catalog.Add(CreateSegment("b", "track-b", 0, 10));
        var result = catalog.Add(CreateSegment("c", "track-a", 2, 10));

        var snapshot = catalog.GetSnapshot();
        Assert.AreEqual(10L, snapshot.TotalBytes);
        CollectionAssert.AreEqual(
            new[] { "c" },
            snapshot.Segments.Select(segment => segment.SegmentId).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "a", "b" },
            result.EvictedSegments.Select(segment => segment.SegmentId).ToArray());
    }

    [TestMethod]
    public void AddCaptureSet_RejectsDuplicateTrackAtomicallyWithoutCatalogMutation()
    {
        var catalog = new ReplaySegmentCatalog(new ReplayBufferSettings
        {
            BufferDuration = TimeSpan.FromMinutes(5),
            MaxBufferBytes = 1_000
        });
        var first = CreateSegment("track-a", "track-a", 0, 10);
        var duplicate = CreateSegment("track-a", "track-b", 0, 10);

        var result = catalog.AddCaptureSet([first, duplicate]);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(0, catalog.GetSnapshot().Segments.Count);
        Assert.AreEqual(0L, catalog.GetSnapshot().TotalBytes);
    }

    [TestMethod]
    public void AcquireSnapshot_PinsFilesWithoutClearingTheLiveCatalog()
    {
        var released = new List<string>();
        var catalog = new ReplaySegmentCatalog(
            new ReplayBufferSettings
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                MaxBufferBytes = 1_000
            },
            segment => released.Add(segment.SegmentId));
        catalog.Add(CreateSegment("old", "track", 0, 10));

        using (var lease = catalog.AcquireSnapshot(TimeSpan.FromSeconds(2)))
        {
            var add = catalog.Add(CreateSegment("live", "track", 2, 10));

            Assert.IsTrue(add.Accepted);
            CollectionAssert.AreEqual(
                new[] { "old" },
                add.EvictedSegments.Select(segment => segment.SegmentId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "old" },
                lease.Segments.Select(segment => segment.SegmentId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "live" },
                catalog.GetSnapshot().Segments.Select(segment => segment.SegmentId).ToArray());
            Assert.AreEqual(0, released.Count, "Pinned files must not be released during publication.");
        }

        CollectionAssert.AreEqual(new[] { "old" }, released);
        CollectionAssert.AreEqual(
            new[] { "live" },
            catalog.GetSnapshot().Segments.Select(segment => segment.SegmentId).ToArray());
    }

    [TestMethod]
    public void Add_IsThreadSafeForConcurrentTrackFinalization()
    {
        var catalog = new ReplaySegmentCatalog(new ReplayBufferSettings
        {
            BufferDuration = TimeSpan.FromMinutes(10),
            MaxBufferBytes = 10_000
        });

        Parallel.For(0, 100, index =>
        {
            var result = catalog.Add(CreateSegment(
                $"segment-{index}",
                $"track-{index % 4}",
                index * 2,
                10,
                index));
            Assert.IsTrue(result.Accepted);
        });

        var snapshot = catalog.GetSnapshot();
        Assert.AreEqual(100, snapshot.Segments.Count);
        Assert.AreEqual(1_000L, snapshot.TotalBytes);
        Assert.AreEqual(100, snapshot.Segments.Select(segment => segment.SegmentId).Distinct().Count());
    }

    private static ReplaySegmentMetadata CreateSegment(
        string id,
        string trackId,
        int startSeconds,
        long bytes,
        long? sequence = null)
    {
        var source = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.SelectedMonitor,
            trackId,
            trackId);
        var track = new ReplayTrackDescriptor(trackId, trackId, source, 1920, 1080);
        return new ReplaySegmentMetadata(
            id,
            sequence ?? startSeconds / 2,
            track,
            Path.Combine(Path.GetTempPath(), "GoatShot.Tests", id + ".mp4"),
            DateTimeOffset.UnixEpoch.AddSeconds(startSeconds),
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(2),
            bytes);
    }
}

[TestClass]
public sealed class ReplayBufferCoordinatorTests
{
    [TestMethod]
    public async Task SaveAsync_PublishesStableSnapshotWhileLiveBufferContinues()
    {
        var publisher = new BlockingReplayPublisher();
        var files = new TestReplayFileManager();
        var coordinator = new ReplayBufferCoordinator(
            new ReplayBufferSettings
            {
                BufferDuration = TimeSpan.FromSeconds(60),
                MaxBufferBytes = 1_000
            },
            publisher,
            files);
        coordinator.Arm();
        coordinator.AddFinalizedSegment(CreateSegment("before", 0));

        var saveTask = coordinator.SaveAsync(
            new ReplaySaveRequest(Path.Combine(Path.GetTempPath(), "receipt"), ReceiptId: "receipt-1"),
            CancellationToken.None);
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ReplayBufferState.Saving, coordinator.GetStatus().State);
        var liveAdd = coordinator.AddFinalizedSegment(CreateSegment("during", 2));
        Assert.IsTrue(liveAdd.Accepted);
        publisher.Continue.TrySetResult();

        var result = await saveTask;

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.BufferContinued);
        Assert.AreEqual(ReplayBufferState.Armed, result.State);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
        Assert.AreEqual(2, coordinator.GetStatus().SegmentCount);
        CollectionAssert.AreEqual(
            new[] { "before" },
            publisher.Publication!.Segments.Select(segment => segment.SegmentId).ToArray());
    }

    [TestMethod]
    public async Task SaveAsync_FromPausedStateReturnsToPausedAndRejectsNewSegments()
    {
        var publisher = new BlockingReplayPublisher();
        var coordinator = new ReplayBufferCoordinator(
            new ReplayBufferSettings(),
            publisher,
            new TestReplayFileManager());
        coordinator.Arm();
        coordinator.AddFinalizedSegment(CreateSegment("before", 0));
        coordinator.Pause();

        var saveTask = coordinator.SaveAsync(
            new ReplaySaveRequest(Path.Combine(Path.GetTempPath(), "paused-receipt")),
            CancellationToken.None);
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var add = coordinator.AddFinalizedSegment(CreateSegment("ignored", 2));
        Assert.IsFalse(add.Accepted);
        publisher.Continue.TrySetResult();

        var result = await saveTask;
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.BufferContinued);
        Assert.AreEqual(ReplayBufferState.Paused, coordinator.GetStatus().State);
        Assert.AreEqual(1, coordinator.GetStatus().SegmentCount);
    }

    [TestMethod]
    public async Task SaveAsync_WithEmptyBufferRestoresArmedState()
    {
        var coordinator = new ReplayBufferCoordinator(
            new ReplayBufferSettings(),
            new ImmediateReplayPublisher(),
            new TestReplayFileManager());
        coordinator.Arm();

        var result = await coordinator.SaveAsync(
            new ReplaySaveRequest(Path.Combine(Path.GetTempPath(), "empty-receipt")),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
        StringAssert.Contains(result.Message, "does not contain");
    }

    [TestMethod]
    public async Task SaveAsync_WhenPublicationFailsKeepsArmedBufferRunning()
    {
        var coordinator = new ReplayBufferCoordinator(
            new ReplayBufferSettings(),
            new ThrowingReplayPublisher(),
            new TestReplayFileManager());
        coordinator.Arm();
        coordinator.AddFinalizedSegment(CreateSegment("before", 0));

        var result = await coordinator.SaveAsync(
            new ReplaySaveRequest(Path.Combine(Path.GetTempPath(), "failed-receipt")),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.BufferContinued);
        Assert.AreEqual(ReplayBufferState.Armed, result.State);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
        StringAssert.Contains(coordinator.GetStatus().LastError, "disk full");
        Assert.IsTrue(coordinator.AddFinalizedSegment(CreateSegment("after", 2)).Accepted);
        Assert.AreEqual(2, coordinator.GetStatus().SegmentCount);
    }

    [TestMethod]
    public void Commands_ExposeOffArmedPausedAndErrorTransitions()
    {
        var files = new TestReplayFileManager();
        var coordinator = new ReplayBufferCoordinator(
            new ReplayBufferSettings(),
            new ImmediateReplayPublisher(),
            files);

        Assert.AreEqual(ReplayBufferState.Off, coordinator.GetStatus().State);
        Assert.IsFalse(coordinator.AddFinalizedSegment(CreateSegment("off", 0)).Accepted);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.Arm().State);
        coordinator.AddFinalizedSegment(CreateSegment("buffered", 0));
        Assert.AreEqual(ReplayBufferState.Paused, coordinator.Pause().State);
        Assert.IsFalse(coordinator.AddFinalizedSegment(CreateSegment("paused", 2)).Accepted);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.Resume().State);
        Assert.AreEqual(ReplayBufferState.Error, coordinator.ReportError("encoder failed").State);
        Assert.AreEqual("encoder failed", coordinator.GetStatus().LastError);
        Assert.AreEqual(ReplayBufferState.Armed, coordinator.Arm().State);
        Assert.IsNull(coordinator.GetStatus().LastError);
        Assert.AreEqual(ReplayBufferState.Off, coordinator.Stop().State);
        Assert.AreEqual(0, coordinator.GetStatus().SegmentCount);
        CollectionAssert.Contains(files.DeletedSegmentIds, "buffered");
    }

    private static ReplaySegmentMetadata CreateSegment(string id, int startSeconds)
    {
        var source = ReplayCaptureSourceDescriptor.FollowCursorMonitor();
        var track = new ReplayTrackDescriptor("display", "Display", source, 1920, 1080);
        return new ReplaySegmentMetadata(
            id,
            startSeconds / 2,
            track,
            Path.Combine(Path.GetTempPath(), "GoatShot.Tests", id + ".mp4"),
            DateTimeOffset.UnixEpoch.AddSeconds(startSeconds),
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(2),
            10);
    }

    private sealed class BlockingReplayPublisher : IReplaySnapshotPublisher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReplaySnapshotPublication? Publication { get; private set; }

        public async Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken)
        {
            Publication = publication;
            Started.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return Result(publication);
        }
    }

    private sealed class ImmediateReplayPublisher : IReplaySnapshotPublisher
    {
        public Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken) => Task.FromResult(Result(publication));
    }

    private sealed class ThrowingReplayPublisher : IReplaySnapshotPublisher
    {
        public Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken) =>
            Task.FromException<ReplaySnapshotPublishResult>(new IOException("disk full"));
    }

    private sealed class TestReplayFileManager : IReplayBufferFileManager
    {
        public List<string> DeletedSegmentIds { get; } = [];

        public bool TryDeleteBufferedSegment(ReplaySegmentMetadata segment)
        {
            DeletedSegmentIds.Add(segment.SegmentId);
            return true;
        }

        public ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
            IReadOnlyCollection<string> residentFilePaths,
            TimeSpan minimumAge,
            DateTimeOffset nowUtc) => new(
                Array.Empty<string>(),
                residentFilePaths.ToArray(),
                Array.Empty<string>());
    }

    private static ReplaySnapshotPublishResult Result(ReplaySnapshotPublication publication)
    {
        var published = publication.Segments
            .Select(segment => new ReplayPublishedSegment(
                segment.SegmentId,
                segment.TrackId,
                Path.GetFileName(segment.FilePath),
                segment.FilePath,
                segment.ByteLength))
            .ToArray();
        return new ReplaySnapshotPublishResult(
            publication.ReceiptId,
            publication.DestinationDirectory,
            published);
    }
}

[TestClass]
public sealed class FileReplayBufferStorageTests
{
    [TestMethod]
    public void AppStartupCleanup_RemovesOwnedBuffersWithoutArmingReplay()
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileReplayBufferStorage(root);
            Directory.CreateDirectory(root);
            var abandoned = Path.Combine(root, "crashed-run", "track", "segment.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(abandoned)!);
            File.WriteAllBytes(abandoned, [1, 2, 3]);

            var result = AppServices.CleanupReplayBufferAtStartup(storage, DateTimeOffset.UtcNow);

            CollectionAssert.Contains(result.DeletedPaths.ToArray(), abandoned);
            Assert.IsFalse(File.Exists(abandoned));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PublishAsync_CopiesAllSegmentsThenAtomicallyPublishesPackage()
    {
        var root = CreateTempRoot();
        try
        {
            var buffer = Path.Combine(root, "buffer");
            Directory.CreateDirectory(buffer);
            var first = CreateFileSegment(buffer, "first", "track-a", 0, [1, 2, 3]);
            var second = CreateFileSegment(buffer, "second", "track-b", 0, [4, 5]);
            var destination = Path.Combine(root, "receipts", "receipt-1");
            var storage = new FileReplayBufferStorage(buffer);

            var result = await storage.PublishAsync(
                new ReplaySnapshotPublication(
                    "receipt-1",
                    destination,
                    DateTimeOffset.UtcNow,
                    [first, second]),
                CancellationToken.None);

            Assert.AreEqual(Path.GetFullPath(destination), result.PackagePath);
            Assert.AreEqual(2, result.Segments.Count);
            Assert.IsTrue(Directory.Exists(destination));
            Assert.IsTrue(result.Segments.All(segment => File.Exists(segment.FullPath)));
            Assert.AreEqual(0, Directory.GetDirectories(
                Path.GetDirectoryName(destination)!,
                ".receipt-1.staging-*",
                SearchOption.TopDirectoryOnly).Length);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task PublishAsync_WhenAnySegmentIsMissingLeavesNoPublishedOrStagingDirectory()
    {
        var root = CreateTempRoot();
        try
        {
            var buffer = Path.Combine(root, "buffer");
            Directory.CreateDirectory(buffer);
            var present = CreateFileSegment(buffer, "present", "track", 0, [1]);
            var missing = CreateFileSegment(buffer, "missing", "track", 2, [2]);
            File.Delete(missing.FilePath);
            var destination = Path.Combine(root, "receipts", "receipt-failed");
            var storage = new FileReplayBufferStorage(buffer);

            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => storage.PublishAsync(
                new ReplaySnapshotPublication(
                    "receipt-failed",
                    destination,
                    DateTimeOffset.UtcNow,
                    [present, missing]),
                CancellationToken.None));

            Assert.IsFalse(Directory.Exists(destination));
            Assert.AreEqual(0, Directory.GetDirectories(
                Path.GetDirectoryName(destination)!,
                ".receipt-failed.staging-*",
                SearchOption.TopDirectoryOnly).Length);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void CleanupAbandonedBufferFiles_DeletesOnlyOldUnreferencedBufferFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var buffer = Path.Combine(root, "buffer");
            Directory.CreateDirectory(buffer);
            var active = Path.Combine(buffer, "active.mp4");
            var abandoned = Path.Combine(buffer, "abandoned.mp4");
            var abandonedAudio = Path.Combine(buffer, "abandoned-system-audio.wav");
            var abandonedPcmSpool = Path.Combine(buffer, "segment.mp4.audio-fixture.pcm.tmp");
            var recent = Path.Combine(buffer, "recent.partial");
            var unrelated = Path.Combine(buffer, "keep.txt");
            File.WriteAllBytes(active, [1]);
            File.WriteAllBytes(abandoned, [2]);
            File.WriteAllBytes(abandonedAudio, [4]);
            File.WriteAllBytes(abandonedPcmSpool, [5]);
            File.WriteAllBytes(recent, [3]);
            File.WriteAllText(unrelated, "keep");
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(active, now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(abandoned, now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(abandonedAudio, now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(abandonedPcmSpool, now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(recent, now.UtcDateTime);
            var storage = new FileReplayBufferStorage(buffer);

            var result = storage.CleanupAbandonedBufferFiles(
                [active],
                TimeSpan.FromMinutes(5),
                now);

            Assert.IsTrue(File.Exists(active));
            Assert.IsFalse(File.Exists(abandoned));
            Assert.IsFalse(File.Exists(abandonedAudio));
            Assert.IsFalse(File.Exists(abandonedPcmSpool));
            Assert.IsTrue(File.Exists(recent));
            Assert.IsTrue(File.Exists(unrelated));
            CollectionAssert.Contains(result.DeletedPaths.ToList(), Path.GetFullPath(abandoned));
            CollectionAssert.Contains(result.DeletedPaths.ToList(), Path.GetFullPath(abandonedAudio));
            CollectionAssert.Contains(result.DeletedPaths.ToList(), Path.GetFullPath(abandonedPcmSpool));
            CollectionAssert.Contains(result.RetainedPaths.ToList(), Path.GetFullPath(active));
            CollectionAssert.Contains(result.RetainedPaths.ToList(), Path.GetFullPath(recent));
            Assert.AreEqual(0, result.Failures.Count);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void TryDeleteBufferedSegment_RejectsFilesOutsideOwnedBufferRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var buffer = Path.Combine(root, "buffer");
            var outside = Path.Combine(root, "outside.mp4");
            Directory.CreateDirectory(buffer);
            File.WriteAllBytes(outside, [1]);
            var storage = new FileReplayBufferStorage(buffer);
            var segment = CreateSegmentMetadata(outside, "outside", "track", 0, 1);

            var deleted = storage.TryDeleteBufferedSegment(segment);

            Assert.IsFalse(deleted);
            Assert.IsTrue(File.Exists(outside));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static ReplaySegmentMetadata CreateFileSegment(
        string buffer,
        string id,
        string trackId,
        int startSeconds,
        byte[] contents)
    {
        var path = Path.Combine(buffer, id + ".mp4");
        File.WriteAllBytes(path, contents);
        return CreateSegmentMetadata(path, id, trackId, startSeconds, contents.LongLength);
    }

    private static ReplaySegmentMetadata CreateSegmentMetadata(
        string path,
        string id,
        string trackId,
        int startSeconds,
        long byteLength)
    {
        var source = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.SelectedMonitor,
            trackId,
            trackId);
        var track = new ReplayTrackDescriptor(trackId, trackId, source, 1920, 1080);
        return new ReplaySegmentMetadata(
            id,
            startSeconds / 2,
            track,
            path,
            DateTimeOffset.UnixEpoch.AddSeconds(startSeconds),
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(2),
            byteLength);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

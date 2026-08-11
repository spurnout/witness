using System.Collections.Concurrent;
using System.Drawing;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayCaptureTargetMapperTests
{
    [TestMethod]
    public void Map_ResolvesEverySupportedCaptureStrategy()
    {
        var targets = Targets();
        var selectedMonitor = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedMonitor,
                "monitor:DISPLAY1",
                "Primary"),
            targets);
        var followCursor = ReplayCaptureTargetMapper.Map(
            ReplayCaptureSourceDescriptor.FollowCursorMonitor(),
            targets,
            cursorMonitorId: "monitor:DISPLAY2");
        var composite = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.AllMonitorsComposite,
                string.Empty,
                "Desktop"),
            targets);
        var separate = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SeparateMonitorTracks,
                string.Empty,
                "Separate monitors"),
            targets);
        var selectedWindow = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedWindow,
                "window:ABC",
                "Chat"),
            targets);
        var foregroundWindow = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.FollowForegroundWindow,
                string.Empty,
                "Foreground"),
            targets,
            foregroundWindowId: "window:ABC");
        var selectedRegion = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedRegion,
                "region:selected",
                "Selected region",
                new ReplayCaptureBounds(10, 20, 300, 200)),
            targets);
        var fixedRegion = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.FixedRegion,
                "region:fixed",
                "Fixed region",
                new ReplayCaptureBounds(30, 40, 500, 400)),
            targets);

        Assert.AreEqual("DISPLAY1", selectedMonitor.Single().CaptureRequest.MonitorName);
        Assert.AreEqual("follow-cursor-monitor", followCursor.Single().TrackId);
        Assert.AreEqual("monitor:DISPLAY2", followCursor.Single().Source.SourceId);
        Assert.AreEqual(CaptureKind.AllMonitors, composite.Single().CaptureRequest.Kind);
        Assert.AreEqual(-1280, composite.Single().Source.Bounds?.X);
        Assert.AreEqual(3200, composite.Single().Source.Bounds?.Width);
        Assert.AreEqual(2, separate.Count);
        Assert.IsTrue(separate.All(plan => plan.CaptureRequest.Kind == CaptureKind.ActiveMonitor));
        Assert.AreEqual(CaptureKind.ActiveWindow, selectedWindow.Single().CaptureRequest.Kind);
        Assert.AreEqual("selected-window", selectedWindow.Single().TrackId);
        Assert.AreEqual(CaptureKind.ActiveWindow, foregroundWindow.Single().CaptureRequest.Kind);
        Assert.AreEqual("follow-foreground-window", foregroundWindow.Single().TrackId);
        Assert.AreEqual(CaptureKind.Region, selectedRegion.Single().CaptureRequest.Kind);
        Assert.AreEqual(300, selectedRegion.Single().CaptureRequest.Bounds?.Width);
        Assert.AreEqual(CaptureKind.FixedRegion, fixedRegion.Single().CaptureRequest.Kind);
        Assert.AreEqual(500, fixedRegion.Single().CaptureRequest.Bounds?.Width);
    }

    [TestMethod]
    public void Map_SelectedTargetThatIsNoLongerAvailableFailsClearly()
    {
        var strategy = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.SelectedWindow,
            "window:MISSING",
            "Missing window");

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ReplayCaptureTargetMapper.Map(strategy, Targets()));

        StringAssert.Contains(error.Message, "could not resolve");
        StringAssert.Contains(error.Message, "window:MISSING");
    }

    [TestMethod]
    public void Map_SelectedMonitorAndWindowAcceptPrefixedAndBareSourceIds()
    {
        var prefixedTargets = Targets();
        var monitorFromBareId = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedMonitor,
                "DISPLAY1",
                "Primary"),
            prefixedTargets);
        var windowFromBareId = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedWindow,
                "ABC",
                "Chat"),
            prefixedTargets);
        IReadOnlyList<CaptureOverlayTarget> bareTargets =
        [
            new(
                "DISPLAY1",
                "Primary",
                CaptureOverlayTargetKind.Monitor,
                new CaptureBounds { X = 0, Y = 0, Width = 1920, Height = 1080 }),
            new(
                "ABC",
                "Chat",
                CaptureOverlayTargetKind.Window,
                new CaptureBounds { X = 100, Y = 120, Width = 900, Height = 700 })
        ];
        var monitorFromPrefixedId = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedMonitor,
                "monitor:DISPLAY1",
                "Primary"),
            bareTargets);
        var windowFromPrefixedId = ReplayCaptureTargetMapper.Map(
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedWindow,
                "window:ABC",
                "Chat"),
            bareTargets);

        Assert.AreEqual("monitor:DISPLAY1", monitorFromBareId.Single().Source.SourceId);
        Assert.AreEqual("window:ABC", windowFromBareId.Single().Source.SourceId);
        Assert.AreEqual("DISPLAY1", monitorFromPrefixedId.Single().Source.SourceId);
        Assert.AreEqual("ABC", windowFromPrefixedId.Single().Source.SourceId);
    }

    private static IReadOnlyList<CaptureOverlayTarget> Targets() =>
    [
        new(
            "monitor:DISPLAY1",
            "Primary",
            CaptureOverlayTargetKind.Monitor,
            new CaptureBounds { X = 0, Y = 0, Width = 1920, Height = 1080 }),
        new(
            "monitor:DISPLAY2",
            "Secondary",
            CaptureOverlayTargetKind.Monitor,
            new CaptureBounds { X = -1280, Y = 0, Width = 1280, Height = 1024 }),
        new(
            "window:ABC",
            "Chat",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 100, Y = 120, Width = 900, Height = 700 })
    ];
}

[TestClass]
public sealed class ReplayRecordingServiceTests
{
    [TestMethod]
    public void WindowsPrivacyGuard_NormalizesProcessNamesAndFailsClosedOnInspectionError()
    {
        var matching = new WindowsReplayPrivacyGuard(
            [@"C:\Program Files\Chat\Discord.exe"],
            () => "discord");
        var allowed = new WindowsReplayPrivacyGuard(
            ["discord.exe"],
            () => "notepad");
        var inspectionFailure = new WindowsReplayPrivacyGuard(
            ["discord"],
            () => throw new InvalidOperationException("foreground unavailable"));

        Assert.IsTrue(matching.EvaluateForegroundProcess().SuppressFrame);
        Assert.IsFalse(allowed.EvaluateForegroundProcess().SuppressFrame);
        var failedClosed = inspectionFailure.EvaluateForegroundProcess();
        Assert.IsTrue(failedClosed.SuppressFrame);
        StringAssert.Contains(failedClosed.Message, "blacked out");
    }

    [TestMethod]
    public void WindowsPrivacyGuard_MasksBackgroundWindowsAndSuppressesChosenExcludedWindow()
    {
        var visibleWindowCalls = 0;
        var now = DateTimeOffset.Parse("2026-08-11T08:00:00Z");
        var guard = new WindowsReplayPrivacyGuard(
            ["discord.exe"],
            () => "notepad",
            () =>
            {
                Interlocked.Increment(ref visibleWindowCalls);
                return
                [
                    new ReplayPrivacyWindow(
                        "window:ABC",
                        "Discord",
                        new ReplayCaptureBounds(100, 120, 400, 300))
                ];
            },
            () => now,
            TimeSpan.FromMilliseconds(500));
        var composite = new ReplayTrackDescriptor(
            "desktop",
            "Composite desktop",
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.AllMonitorsComposite,
                "desktop",
                "Composite desktop",
                new ReplayCaptureBounds(0, 0, 1920, 1080)),
            1280,
            720);
        var chosenWindow = new ReplayTrackDescriptor(
            "chosen-window",
            "Chosen window",
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedWindow,
                "window:ABC",
                "Chosen window",
                new ReplayCaptureBounds(100, 120, 400, 300)),
            400,
            300);

        var masked = guard.EvaluateCapture(composite);
        var suppressed = guard.EvaluateCapture(chosenWindow);

        Assert.IsFalse(masked.SuppressFrame);
        Assert.AreEqual(1, masked.MaskedDesktopBounds!.Count);
        Assert.AreEqual(new ReplayCaptureBounds(100, 120, 400, 300), masked.MaskedDesktopBounds![0]);
        Assert.IsTrue(suppressed.SuppressFrame);
        StringAssert.Contains(suppressed.Message, "entire frame");
        Assert.AreEqual(
            2,
            visibleWindowCalls,
            "Configured background-window exclusions must be re-enumerated for every frame.");
    }

    [TestMethod]
    public void WindowsPrivacyGuard_BlacksFirstFrameAfterExcludedAppBecomesForeground()
    {
        var foregroundChecks = 0;
        var visibleWindowChecks = 0;
        var now = DateTimeOffset.Parse("2026-08-11T08:00:00Z");
        var guard = new WindowsReplayPrivacyGuard(
            ["discord"],
            () => Interlocked.Increment(ref foregroundChecks) == 1 ? "notepad" : "discord",
            () =>
            {
                Interlocked.Increment(ref visibleWindowChecks);
                return [];
            },
            () => now,
            TimeSpan.FromMilliseconds(500));

        Assert.IsFalse(guard.EvaluateForegroundProcess().SuppressFrame);
        Assert.IsTrue(
            guard.EvaluateForegroundProcess().SuppressFrame,
            "The first frame after an excluded app takes focus must be redacted even while " +
            "the expensive visible-window snapshot remains cached.");
        Assert.AreEqual(2, foregroundChecks);
        Assert.AreEqual(
            0,
            visibleWindowChecks,
            "Foreground-only checks must not enumerate every visible window.");
    }

    [TestMethod]
    public void WindowsPrivacyGuard_MasksFirstFrameAfterExcludedBackgroundWindowAppears()
    {
        var visibleWindowChecks = 0;
        var guard = new WindowsReplayPrivacyGuard(
            ["discord"],
            () => "notepad",
            () => Interlocked.Increment(ref visibleWindowChecks) == 1
                ? []
                :
                [
                    new ReplayPrivacyWindow(
                        "window:NEW",
                        "discord",
                        new ReplayCaptureBounds(200, 160, 500, 320))
                ]);
        var composite = new ReplayTrackDescriptor(
            "desktop",
            "Composite desktop",
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.AllMonitorsComposite,
                "desktop",
                "Composite desktop",
                new ReplayCaptureBounds(0, 0, 1920, 1080)),
            1920,
            1080);

        Assert.IsFalse(guard.EvaluateCapture(composite).HasRedactions);
        var firstFrameAfterWindowAppears = guard.EvaluateCapture(composite);

        Assert.IsTrue(firstFrameAfterWindowAppears.HasRedactions);
        Assert.AreEqual(1, firstFrameAfterWindowAppears.MaskedDesktopBounds!.Count);
        Assert.AreEqual(2, visibleWindowChecks);
    }

    [TestMethod]
    public void DescribeUnsupportedProfileFeatures_AcceptsSharedRecordingProfileFeatures()
    {
        var warnings = ReplayRecordingService.DescribeUnsupportedProfileFeatures(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                EnableWebcamOverlay = true,
                ShowRecordingBorder = true,
                ShowRecordingTimer = true
            });

        Assert.AreEqual(0, warnings.Count);
        Assert.AreEqual(0, ReplayRecordingService.DescribeUnsupportedProfileFeatures(
            new RecordingSettings
            {
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            }).Count);
    }

    [TestMethod]
    public void MediaFoundationFactory_DefersUnavailableEncoderFailureUntilReplayIsArmed()
    {
        var factory = new MediaFoundationReplayVideoSegmentEncoderFactory(
            RecordingSettingsNormalizer.Normalize(new RecordingSettings(), fpsOverride: 30),
            new ProductionVideoEncoderSelection(
                ProductionVideoEncoderChoice.Unavailable,
                IsAvailable: false,
                IsHardwareAccelerated: false,
                Codec: "H.264",
                Provider: "Media Foundation",
                Summary: "No encoder transform is installed."));

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            factory.Start(Path.Combine(Path.GetTempPath(), "unavailable-replay.mp4"), 1920, 1080));

        StringAssert.Contains(error.Message, "unavailable");
        StringAssert.Contains(error.Message, "No encoder transform");
    }

    [TestMethod]
    public async Task SaveAsync_DoesNotStopCaptureAndNextSegmentRecordsSourceTransition()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var fileManager = new TestReplayFileManager();
            var publisher = new BlockingFirstReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(settings, publisher, fileManager);
            var frameSources = new ControlledFrameSourceFactory(
                [Source("follow-monitor", "monitor:DISPLAY1", "Display 1")],
                [Source("follow-monitor", "monitor:DISPLAY2", "Display 2")]);
            var encoder = new TestReplayEncoderFactory(settings.FramesPerSecond);
            var clock = new TestReplayClock();
            var profileWarnings = ReplayRecordingService.DescribeUnsupportedProfileFeatures(
                new RecordingSettings
                {
                    IncludeMicrophone = true,
                    IncludeSystemAudio = true,
                    EnableWebcamOverlay = true
                });
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: true,
                root,
                coordinator,
                frameSources,
                encoder,
                clock,
                TimeSpan.Zero,
                profileWarnings);
            var states = new ConcurrentBag<ReplayBufferState>();
            var statusMessages = new ConcurrentBag<string>();
            service.StatusChanged += (_, args) =>
            {
                states.Add(args.Status.State);
                statusMessages.Add(args.Message);
            };

            var armed = await service.ArmAsync();
            Assert.IsTrue(armed.Succeeded);
            Assert.IsFalse(armed.Message.Contains("video-only", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(statusMessages.Any(message =>
                message.Contains("not captured or muxed", StringComparison.Ordinal)));
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);

            var firstSave = service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-1"),
                ReceiptId: "receipt-1"));
            await publisher.FirstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ReplayBufferState.Saving, coordinator.GetStatus().State);

            frameSources.AllowSecondSegment.TrySetResult();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 2);
            Assert.AreEqual(
                ReplayBufferState.Saving,
                coordinator.GetStatus().State,
                "The capture producer must continue finalizing segments during publication.");
            publisher.AllowFirstPublish.TrySetResult();

            var firstResult = await firstSave;
            Assert.IsTrue(firstResult.Succeeded);
            Assert.IsTrue(firstResult.BufferContinued);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
            Assert.AreEqual(1, publisher.Publications[0].Segments.Count);

            var secondResult = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-2"),
                ReceiptId: "receipt-2"));
            Assert.IsTrue(secondResult.Succeeded);
            var secondSnapshot = publisher.Publications[1].Segments;
            Assert.AreEqual(2, secondSnapshot.Count);
            CollectionAssert.AreEqual(
                new[] { "monitor:DISPLAY1", "monitor:DISPLAY2" },
                secondSnapshot
                    .OrderBy(segment => segment.SequenceNumber)
                    .Select(segment => segment.Track.Source.SourceId)
                    .ToArray());
            Assert.IsTrue(secondSnapshot.All(segment => segment.TrackId == "follow-monitor"));
            Assert.IsTrue(secondSnapshot.All(segment => segment.Duration == TimeSpan.FromSeconds(2)));
            Assert.IsTrue(states.Contains(ReplayBufferState.Armed));
            Assert.IsTrue(states.Contains(ReplayBufferState.Saving));
            Assert.AreEqual(1, fileManager.CleanupCalls);

            var stopped = await service.StopAsync();
            Assert.IsTrue(stopped.Succeeded);
            Assert.AreEqual(ReplayBufferState.Off, coordinator.GetStatus().State);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task PrivacyExcludedForegroundFrames_AreBlackedOutWithoutEnteringError()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            settings.PrivacyExcludedProcessNames = ["discord.exe"];
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new ControlledFrameSourceFactory(
                [Source("track", "monitor:DISPLAY1", "Display 1")]);
            var encoder = new TestReplayEncoderFactory(settings.FramesPerSecond);
            var privacy = new SequenceReplayPrivacyGuard(true, true, false, false);
            var statuses = new ConcurrentQueue<ReplayRecordingStatusChangedEventArgs>();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero,
                privacyGuard: privacy);
            service.StatusChanged += (_, args) => statuses.Enqueue(args);

            var armed = await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);

            Assert.IsTrue(armed.Succeeded);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
            CollectionAssert.AreEqual(
                new[]
                {
                    Color.Black.ToArgb(),
                    Color.Black.ToArgb(),
                    Color.Red.ToArgb(),
                    Color.Red.ToArgb()
                },
                encoder.WrittenFrameColors.ToArray());
            Assert.IsFalse(statuses.Any(status => status.Status.State == ReplayBufferState.Error));
            Assert.IsTrue(statuses.Any(status =>
                status.Message.Contains("blacked out", StringComparison.Ordinal)));
            Assert.IsTrue(statuses.Any(status =>
                status.Message.Contains("privacy exclusion ended", StringComparison.OrdinalIgnoreCase)));

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_FinalizesSeparateSourcesAsSynchronizedTracks()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = WithCapture(
                Settings(),
                ReplayCaptureSourceKind.SeparateMonitorTracks,
                "Separate monitors");
            var fileManager = new TestReplayFileManager();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(settings, publisher, fileManager);
            var frameSources = new ControlledFrameSourceFactory(
                [
                    Source("monitor-track:1", "monitor:DISPLAY1", "Display 1"),
                    Source("monitor-track:2", "monitor:DISPLAY2", "Display 2")
                ]);
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 2);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-multitrack")));

            Assert.IsTrue(saved.Succeeded);
            Assert.AreEqual(2, publisher.Publications.Single().Segments.Count);
            var segments = publisher.Publications.Single().Segments;
            Assert.AreEqual(1, segments.Select(segment => segment.SequenceNumber).Distinct().Count());
            Assert.AreEqual(1, segments.Select(segment => segment.MonotonicStart).Distinct().Count());
            CollectionAssert.AreEquivalent(
                new[] { "monitor-track:1", "monitor-track:2" },
                segments.Select(segment => segment.TrackId).ToArray());
            Assert.IsTrue(segments.All(segment => segment.Duration == TimeSpan.FromSeconds(2)));

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_EncodesConfiguredResolutionWithoutChangingNativeSourceGeometryTruth()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            var encoder = new TestReplayEncoderFactory(settings.FramesPerSecond);
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new FixedResolutionFrameSourceFactory(1600, 900),
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero,
                recordingSettings: new RecordingSettings
                {
                    TargetWidth = 1280,
                    TargetHeight = 720,
                    ShowRecordingBorder = false,
                    ShowRecordingTimer = false
                });

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-resolution")));

            Assert.IsTrue(saved.Succeeded);
            Assert.IsTrue(encoder.StartedFrameSizes.Contains((1280, 720)));
            var segment = publisher.Publications.Single().Segments.Single();
            Assert.AreEqual(1280, segment.Track.PixelWidth);
            Assert.AreEqual(720, segment.Track.PixelHeight);
            Assert.AreEqual(1600, segment.Track.Source.Bounds!.Width);
            Assert.AreEqual(900, segment.Track.Source.Bounds.Height);
            Assert.AreEqual(4, segment.EncodedFrameCount);
            Assert.AreEqual(settings.SegmentDuration, segment.Duration);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void AutoResolution_PreservesNativeSourceSizeForRecordNowAndReplay()
    {
        var normalized = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            TargetWidth = 0,
            TargetHeight = 0
        });

        Assert.AreEqual(
            (3840, 2160),
            RecordingService.ResolveFrameSize(3840, 2160, normalized));
        Assert.AreEqual(
            (3840, 2160),
            ReplayRecordingService.ResolveEncodedFrameSize(3840, 2160, 0, 0));
    }

    [TestMethod]
    public async Task Producer_FinalizesEarlyAndReopensWhenSourceGeometryChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new GeometryTransitionFrameSourceFactory(),
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            var armed = await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount >= 2);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-geometry-transition")));

            Assert.IsTrue(armed.Succeeded);
            Assert.IsTrue(saved.Succeeded);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);
            var first = publisher.Publications.Single().Segments
                .OrderBy(segment => segment.SequenceNumber)
                .First();
            var second = publisher.Publications.Single().Segments
                .OrderBy(segment => segment.SequenceNumber)
                .Skip(1)
                .First();
            Assert.AreEqual(2, first.Track.PixelWidth);
            Assert.AreEqual(4, second.Track.PixelWidth);
            Assert.IsTrue(first.Duration < settings.SegmentDuration);
            Assert.IsTrue(second.MonotonicStart >= first.MonotonicEnd);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_FinalizesEarlyWhenFollowedLiveTargetChanges()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new LiveTargetTransitionFrameSourceFactory(),
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount >= 2);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-live-target-transition")));

            Assert.IsTrue(saved.Succeeded);
            var ordered = publisher.Publications.Single().Segments
                .OrderBy(segment => segment.SequenceNumber)
                .ToArray();
            Assert.AreEqual("monitor:DISPLAY1", ordered[0].Track.Source.SourceId);
            Assert.AreEqual("monitor:DISPLAY2", ordered[1].Track.Source.SourceId);
            Assert.IsTrue(ordered[0].Duration < settings.SegmentDuration);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_ThrottlesLiveTargetInspectionWithoutMissingSegmentCapture()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            settings.FramesPerSecond = 30;
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new CountingLiveFrameSourceFactory();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero,
                liveSourcePollingInterval: TimeSpan.FromMilliseconds(500));

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);

            Assert.AreEqual(
                3,
                frameSources.InspectionCount,
                "A two-second, 30 FPS segment should inspect live topology at 0.5, 1.0, and 1.5 seconds only.");

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task PauseResumeAndStop_ControlBackgroundProductionWithoutContinuousWork()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new ControlledFrameSourceFactory(
                [Source("track", "monitor:DISPLAY1", "Display 1")],
                [Source("track", "monitor:DISPLAY1", "Display 1")],
                [Source("track", "monitor:DISPLAY1", "Display 1")]);
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            var paused = service.Pause();
            frameSources.AllowSecondSegment.TrySetResult();
            await Task.Delay(50);

            Assert.IsTrue(paused.Succeeded);
            Assert.AreEqual(ReplayBufferState.Paused, coordinator.GetStatus().State);
            Assert.AreEqual(1, coordinator.GetStatus().SegmentCount);

            var resumed = service.Resume();
            Assert.IsTrue(resumed.Succeeded);
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 2);
            var stopped = await service.StopAsync();

            Assert.IsTrue(stopped.Succeeded);
            Assert.AreEqual(ReplayBufferState.Off, coordinator.GetStatus().State);
            Assert.AreEqual(0, coordinator.GetStatus().SegmentCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_DiscardsEntireSynchronizedSetWhenOneTrackFailsToFinalize()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = WithCapture(
                Settings(),
                ReplayCaptureSourceKind.SeparateMonitorTracks,
                "Separate monitors");
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new ControlledFrameSourceFactory(
                [
                    Source("track-1", "monitor:DISPLAY1", "Display 1"),
                    Source("track-2", "monitor:DISPLAY2", "Display 2")
                ]);
            var encoder = new FailingSecondTrackEncoderFactory(settings.FramesPerSecond);
            var messages = new ConcurrentQueue<string>();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero);
            service.StatusChanged += (_, args) => messages.Enqueue(args.Message);

            await service.ArmAsync();
            await encoder.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => !Directory.EnumerateFiles(
                root,
                "*",
                SearchOption.AllDirectories).Any(path =>
                    path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)));

            Assert.AreEqual(0, coordinator.GetStatus().SegmentCount);
            Assert.IsTrue(messages.Any(message =>
                message.Contains("synchronized capture gap", StringComparison.OrdinalIgnoreCase)));

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task ErrorRetry_PreservesSequenceAndMonotonicTimelineForExistingCatalog()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new RetryFrameSourceFactory(),
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().State == ReplayBufferState.Error);
            Assert.AreEqual(1, coordinator.GetStatus().SegmentCount);

            var retried = await service.ArmAsync();
            Assert.IsTrue(retried.Succeeded);
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 2);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-after-retry")));

            Assert.IsTrue(saved.Succeeded);
            var ordered = publisher.Publications.Single().Segments
                .OrderBy(segment => segment.SequenceNumber)
                .ToArray();
            CollectionAssert.AreEqual(new long[] { 0, 1 }, ordered.Select(segment => segment.SequenceNumber).ToArray());
            Assert.IsTrue(ordered[1].MonotonicStart >= ordered[0].MonotonicEnd);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task SaveAsync_RotatesActiveSegmentSnapshotsThroughBoundaryAndCaptureContinues()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new BlockingFirstReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            var clock = new BoundaryReplayClock();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new TwoSegmentFrameSourceFactory(),
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                clock,
                TimeSpan.Zero);

            await service.ArmAsync();
            await clock.FirstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var saveTask = service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-active-boundary")));
            clock.ReleaseFirstDelay.TrySetResult();

            await publisher.FirstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, publisher.Publications.Single().Segments.Count);
            Assert.IsTrue(
                publisher.Publications.Single().Segments.Single().Duration < settings.SegmentDuration,
                "The hotkey boundary must include and finalize the partial active segment.");
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount >= 2);
            publisher.AllowFirstPublish.TrySetResult();

            var saved = await saveTask;
            Assert.IsTrue(saved.Succeeded);
            Assert.IsTrue(saved.BufferContinued);
            Assert.AreEqual(1, saved.Segments.Count);
            Assert.AreEqual(ReplayBufferState.Armed, coordinator.GetStatus().State);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task PauseMidSegment_AllowsInFlightSetToFinalizeBeforeSave()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var publisher = new ImmediateReplayPublisher();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                publisher,
                new TestReplayFileManager());
            var clock = new BoundaryReplayClock();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                new TwoSegmentFrameSourceFactory(),
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                clock,
                TimeSpan.Zero);

            await service.ArmAsync();
            await clock.FirstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var paused = service.Pause();
            var saveTask = service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-paused-boundary")));
            clock.ReleaseFirstDelay.TrySetResult();
            var saved = await saveTask;

            Assert.IsTrue(paused.Succeeded);
            Assert.IsTrue(saved.Succeeded);
            Assert.IsFalse(saved.BufferContinued);
            Assert.AreEqual(ReplayBufferState.Paused, coordinator.GetStatus().State);
            Assert.AreEqual(1, publisher.Publications.Single().Segments.Count);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task SystemSuspend_StopsCaptureAndResumePreservesUserPausedIntent()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new TwoSegmentFrameSourceFactory();
            var clock = new BoundaryReplayClock();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                clock,
                TimeSpan.Zero);

            await service.ArmAsync();
            await clock.FirstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var suspended = service.SuspendForSystemEvent();
            clock.ReleaseFirstDelay.TrySetResult();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            var opensWhileSuspended = frameSources.OpenCount;
            await Task.Delay(50);

            Assert.IsTrue(suspended.Succeeded);
            Assert.IsTrue(service.GetStatus().SystemSuspended);
            Assert.AreEqual(opensWhileSuspended, frameSources.OpenCount);
            Assert.AreEqual(1, coordinator.GetStatus().SegmentCount);

            var userPaused = service.Pause();
            var resumedSystem = service.ResumeAfterSystemEvent();
            await Task.Delay(50);
            Assert.IsTrue(userPaused.Succeeded);
            Assert.IsTrue(resumedSystem.Succeeded);
            Assert.IsFalse(service.GetStatus().SystemSuspended);
            Assert.AreEqual(ReplayBufferState.Paused, coordinator.GetStatus().State);
            Assert.AreEqual(opensWhileSuspended, frameSources.OpenCount);

            service.Resume();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 2);
            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task SystemSuspendResume_WhileOffDoesNotArmOrOpenCaptureSources()
    {
        var root = CreateTempRoot();
        try
        {
            var settings = Settings();
            var coordinator = new ReplayBufferCoordinator(
                settings,
                new ImmediateReplayPublisher(),
                new TestReplayFileManager());
            var frameSources = new TwoSegmentFrameSourceFactory();
            await using var service = new ReplayRecordingService(
                settings,
                includeCursor: false,
                root,
                coordinator,
                frameSources,
                new TestReplayEncoderFactory(settings.FramesPerSecond),
                new TestReplayClock(),
                TimeSpan.Zero);

            service.SuspendForSystemEvent();
            Assert.IsTrue(service.GetStatus().SystemSuspended);
            service.ResumeAfterSystemEvent();

            Assert.AreEqual(ReplayBufferState.Off, service.GetStatus().State);
            Assert.IsFalse(service.GetStatus().SystemSuspended);
            Assert.AreEqual(0, frameSources.OpenCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static ReplayBufferSettings Settings() => new()
    {
        BufferDuration = TimeSpan.FromSeconds(60),
        SegmentDuration = TimeSpan.FromSeconds(2),
        MaxBufferBytes = 1_000_000,
        FramesPerSecond = 2,
        CaptureSource = ReplayCaptureSourceDescriptor.FollowCursorMonitor(),
        EnableLocalOcrIndexing = true
    };

    private static ReplayBufferSettings WithCapture(
        ReplayBufferSettings settings,
        ReplayCaptureSourceKind kind,
        string displayName)
    {
        settings.CaptureSource = new ReplayCaptureSourceDescriptor(kind, string.Empty, displayName);
        return settings;
    }

    private static TestFrameSource Source(string trackId, string sourceId, string displayName) =>
        new(
            trackId,
            displayName,
            new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.FollowCursorMonitor,
                sourceId,
                displayName));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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

    private sealed class ControlledFrameSourceFactory(
        params IReadOnlyList<TestFrameSource>[] segments) : IReplayFrameSourceFactory
    {
        private int _openCount;

        public TaskCompletionSource AllowSecondSegment { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _openCount);
            if (call == 2)
            {
                await AllowSecondSegment.Task.WaitAsync(cancellationToken);
            }

            if (call > segments.Length)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return segments[call - 1]
                .Select(source => (IReplayFrameSource)source.Clone())
                .ToArray();
        }
    }

    private sealed class CountingLiveFrameSourceFactory : IReplayFrameSourceFactory
    {
        private int _openCount;
        private int _inspectionCount;

        public int InspectionCount => Volatile.Read(ref _inspectionCount);

        public async Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _openCount) > 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return [Source("counting-track", "monitor:DISPLAY1", "Display 1")];
        }

        public bool HasLiveSourceSetChanged(
            ReplayCaptureSourceDescriptor strategy,
            IReadOnlyList<IReplayFrameSource> openSources)
        {
            Interlocked.Increment(ref _inspectionCount);
            return false;
        }
    }

    private sealed class RetryFrameSourceFactory : IReplayFrameSourceFactory
    {
        private int _openCount;

        public async Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _openCount);
            if (call == 2)
            {
                throw new InvalidOperationException("deterministic source reopen failure");
            }

            if (call > 3)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return [Source("retry-track", "monitor:DISPLAY1", "Display 1")];
        }
    }

    private sealed class TwoSegmentFrameSourceFactory : IReplayFrameSourceFactory
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public async Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _openCount);
            if (call > 2)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return [Source("boundary-track", "monitor:DISPLAY1", "Display 1")];
        }
    }

    private sealed class TestFrameSource(
        string trackId,
        string displayName,
        ReplayCaptureSourceDescriptor source) : IReplayFrameSource
    {
        public string TrackId => trackId;
        public string DisplayName => displayName;
        public ReplayCaptureSourceDescriptor Source => source;
        public double DpiScaleX => 1d;
        public double DpiScaleY => 1d;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = new Bitmap(2, 2);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Red);
            }

            return Task.FromResult(new CapturedBitmap(
                bitmap,
                CaptureKind.ActiveMonitor,
                new CaptureBounds { X = 10, Y = 20, Width = 2, Height = 2 },
                new CaptureSource { MonitorName = source.SourceId }));
        }

        public TestFrameSource Clone() => new(trackId, displayName, source);

        public void Dispose()
        {
        }
    }

    private sealed class GeometryTransitionFrameSourceFactory : IReplayFrameSourceFactory
    {
        private int _openCount;

        public Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openCount = Interlocked.Increment(ref _openCount);
            IReplayFrameSource source = openCount == 1
                ? new ResizingFrameSource()
                : new SizedFrameSource(4);
            return Task.FromResult<IReadOnlyList<IReplayFrameSource>>([source]);
        }
    }

    private sealed class FixedResolutionFrameSourceFactory(int width, int height) : IReplayFrameSourceFactory
    {
        private int _openCount;

        public async Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _openCount) > 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return [new RectangularFrameSource(width, height)];
        }
    }

    private sealed class RectangularFrameSource(int width, int height) : IReplayFrameSource
    {
        public string TrackId => "resolution-track";
        public string DisplayName => "Resolution source";
        public ReplayCaptureSourceDescriptor Source => new(
            ReplayCaptureSourceKind.SelectedMonitor,
            "monitor:resolution",
            DisplayName);
        public double DpiScaleX => 1d;
        public double DpiScaleY => 1d;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Red);
            }

            return Task.FromResult(new CapturedBitmap(
                bitmap,
                CaptureKind.ActiveMonitor,
                new CaptureBounds { X = 30, Y = 40, Width = width, Height = height },
                new CaptureSource { MonitorName = "monitor:resolution" }));
        }

        public void Dispose()
        {
        }
    }

    private sealed class LiveTargetTransitionFrameSourceFactory : IReplayFrameSourceFactory
    {
        private int _openCount;
        private int _inspectionCount;

        public Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
            ReplayCaptureSourceDescriptor strategy,
            bool includeCursor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = Interlocked.Increment(ref _openCount) == 1
                ? "monitor:DISPLAY1"
                : "monitor:DISPLAY2";
            return Task.FromResult<IReadOnlyList<IReplayFrameSource>>(
                [Source("follow-monitor", sourceId, sourceId)]);
        }

        public bool HasLiveSourceSetChanged(
            ReplayCaptureSourceDescriptor strategy,
            IReadOnlyList<IReplayFrameSource> openSources) =>
            Interlocked.Increment(ref _inspectionCount) == 1;
    }

    private sealed class ResizingFrameSource : IReplayFrameSource
    {
        private int _captureCount;

        public string TrackId => "geometry-track";
        public string DisplayName => "Geometry source";
        public ReplayCaptureSourceDescriptor Source => new(
            ReplayCaptureSourceKind.FollowForegroundWindow,
            "window:fixture",
            DisplayName);
        public double DpiScaleX => 1d;
        public double DpiScaleY => 1d;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = Interlocked.Increment(ref _captureCount) == 1 ? 2 : 4;
            return Task.FromResult(CreateSizedFrame(size));
        }

        public void Dispose()
        {
        }
    }

    private sealed class SizedFrameSource(int size) : IReplayFrameSource
    {
        public string TrackId => "geometry-track";
        public string DisplayName => "Geometry source";
        public ReplayCaptureSourceDescriptor Source => new(
            ReplayCaptureSourceKind.FollowForegroundWindow,
            "window:fixture",
            DisplayName);
        public double DpiScaleX => 1d;
        public double DpiScaleY => 1d;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSizedFrame(size));
        }

        public void Dispose()
        {
        }
    }

    private static CapturedBitmap CreateSizedFrame(int size)
    {
        var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Red);
        }

        return new CapturedBitmap(
            bitmap,
            CaptureKind.ActiveWindow,
            new CaptureBounds { X = 10, Y = 20, Width = size, Height = size },
            new CaptureSource { WindowTitle = "Geometry fixture" });
    }

    private sealed class TestReplayEncoderFactory(int framesPerSecond) : IReplayVideoSegmentEncoderFactory
    {
        public ConcurrentQueue<int> WrittenFrameColors { get; } = new();
        public ConcurrentQueue<(int Width, int Height)> StartedFrameSizes { get; } = new();

        public IReplayVideoSegmentSession Start(string outputPath, int width, int height)
        {
            StartedFrameSizes.Enqueue((width, height));
            return new TestReplayVideoSegmentSession(
                outputPath,
                framesPerSecond,
                width,
                height,
                WrittenFrameColors);
        }
    }

    private sealed class FailingSecondTrackEncoderFactory(int framesPerSecond) : IReplayVideoSegmentEncoderFactory
    {
        private int _sessionCount;

        public TaskCompletionSource FailureObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReplayVideoSegmentSession Start(string outputPath, int width, int height) =>
            new FailingReplayVideoSegmentSession(
                outputPath,
                framesPerSecond,
                width,
                height,
                failCompletion: Interlocked.Increment(ref _sessionCount) == 2,
                FailureObserved);
    }

    private sealed class FailingReplayVideoSegmentSession(
        string outputPath,
        int framesPerSecond,
        int width,
        int height,
        bool failCompletion,
        TaskCompletionSource failureObserved) : IReplayVideoSegmentSession
    {
        public string OutputPath { get; } = outputPath;
        public int FramesPerSecond { get; } = framesPerSecond;
        public int FrameCount { get; private set; }

        public void WriteFrame(Bitmap frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual(width, frame.Width);
            Assert.AreEqual(height, frame.Height);
            FrameCount++;
        }

        public RecordingResult Complete()
        {
            if (failCompletion)
            {
                failureObserved.TrySetResult();
                return new RecordingResult
                {
                    Succeeded = false,
                    OutputPath = OutputPath,
                    Message = "deterministic second-track finalize failure"
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllBytes(OutputPath, Enumerable.Repeat((byte)1, FrameCount).ToArray());
            return new RecordingResult
            {
                Succeeded = true,
                OutputPath = OutputPath,
                Message = "test segment finalized"
            };
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestReplayVideoSegmentSession(
        string outputPath,
        int framesPerSecond,
        int width,
        int height,
        ConcurrentQueue<int> writtenFrameColors) : IReplayVideoSegmentSession
    {
        public string OutputPath { get; } = outputPath;
        public int FramesPerSecond { get; } = framesPerSecond;
        public int FrameCount { get; private set; }

        public void WriteFrame(Bitmap frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual(width, frame.Width);
            Assert.AreEqual(height, frame.Height);
            writtenFrameColors.Enqueue(frame.GetPixel(0, 0).ToArgb());
            FrameCount++;
        }

        public RecordingResult Complete()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
            File.WriteAllBytes(OutputPath, Enumerable.Repeat((byte)1, FrameCount).ToArray());
            return new RecordingResult
            {
                Succeeded = true,
                OutputPath = OutputPath,
                Message = "test segment finalized"
            };
        }

        public void Dispose()
        {
        }
    }

    private sealed class SequenceReplayPrivacyGuard(params bool[] suppressions) : IReplayPrivacyGuard
    {
        private int _evaluationCount;

        public ReplayPrivacyDecision EvaluateForegroundProcess()
        {
            var index = Math.Min(
                Interlocked.Increment(ref _evaluationCount) - 1,
                suppressions.Length - 1);
            return suppressions[index]
                ? ReplayPrivacyDecision.Suppress(
                    "Replay privacy exclusion is active; buffered frames are blacked out.")
                : ReplayPrivacyDecision.Allow();
        }
    }

    private sealed class TestReplayClock : IReplayRecordingClock
    {
        private long _ticks;

        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref _ticks));

        public long GetTimestamp() => Interlocked.Read(ref _ticks);

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromTicks(endingTimestamp - startingTimestamp);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Add(ref _ticks, Math.Max(0, delay.Ticks));
            await Task.Yield();
        }
    }

    private sealed class BoundaryReplayClock : IReplayRecordingClock
    {
        private long _ticks;
        private int _delayCount;

        public TaskCompletionSource FirstDelayStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstDelay { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref _ticks));

        public long GetTimestamp() => Interlocked.Read(ref _ticks);

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromTicks(endingTimestamp - startingTimestamp);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _delayCount) == 1)
            {
                FirstDelayStarted.TrySetResult();
                await ReleaseFirstDelay.Task.WaitAsync(cancellationToken);
            }

            Interlocked.Add(ref _ticks, Math.Max(0, delay.Ticks));
            await Task.Yield();
        }
    }

    private sealed class TestReplayFileManager : IReplayBufferFileManager
    {
        public int CleanupCalls { get; private set; }

        public bool TryDeleteBufferedSegment(ReplaySegmentMetadata segment)
        {
            try
            {
                File.Delete(segment.FilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
            IReadOnlyCollection<string> residentFilePaths,
            TimeSpan minimumAge,
            DateTimeOffset nowUtc)
        {
            CleanupCalls++;
            return new ReplayBufferCleanupResult(
                Array.Empty<string>(),
                residentFilePaths.ToArray(),
                Array.Empty<string>());
        }
    }

    private sealed class BlockingFirstReplayPublisher : IReplaySnapshotPublisher
    {
        private int _publishCount;

        public TaskCompletionSource FirstPublishStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirstPublish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ReplaySnapshotPublication> Publications { get; } = [];

        public async Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken)
        {
            lock (Publications)
            {
                Publications.Add(publication);
            }

            if (Interlocked.Increment(ref _publishCount) == 1)
            {
                FirstPublishStarted.TrySetResult();
                await AllowFirstPublish.Task.WaitAsync(cancellationToken);
            }

            return Published(publication);
        }
    }

    private sealed class ImmediateReplayPublisher : IReplaySnapshotPublisher
    {
        public List<ReplaySnapshotPublication> Publications { get; } = [];

        public Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken)
        {
            Publications.Add(publication);
            return Task.FromResult(Published(publication));
        }
    }

    private static ReplaySnapshotPublishResult Published(ReplaySnapshotPublication publication) => new(
        publication.ReceiptId,
        publication.DestinationDirectory,
        publication.Segments.Select(segment => new ReplayPublishedSegment(
            segment.SegmentId,
            segment.TrackId,
            Path.GetFileName(segment.FilePath),
            segment.FilePath,
            segment.ByteLength)).ToArray());
}

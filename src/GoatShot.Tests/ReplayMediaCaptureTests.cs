using System.Collections.Concurrent;
using System.Drawing;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayMediaCaptureTests
{
    [TestMethod]
    public async Task Producer_MuxesRequestedAudioAndPrecomposesWebcamIntoFinalizedSegment()
    {
        var root = CreateTempRoot();
        try
        {
            var replaySettings = CreateReplaySettings();
            var publisher = new CapturingPublisher();
            var coordinator = new ReplayBufferCoordinator(
                replaySettings,
                publisher,
                new TestReplayFileManager());
            var audio = new SuccessfulAudioCaptureService();
            var camera = new SolidCameraOverlayService(Color.Red);
            var encoder = new InspectingEncoderFactory(replaySettings.FramesPerSecond);
            var recording = new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                MicrophoneDeviceId = "mic-fixture",
                SystemAudioDeviceId = "render-fixture",
                MicrophoneGain = 1.5d,
                SystemAudioGain = 0.75d,
                NoiseGateThresholdDb = -24d,
                EnableWebcamOverlay = true,
                WebcamDeviceId = "camera-fixture",
                WebcamOverlayPosition = "BottomRight",
                WebcamOverlayShape = "Rectangle",
                MirrorWebcam = false,
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            };

            await using var service = new ReplayRecordingService(
                replaySettings,
                includeCursor: false,
                root,
                coordinator,
                new SingleSegmentFrameSourceFactory(),
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero,
                recordingSettings: recording,
                audioCapture: audio,
                cameraOverlay: camera);

            var armed = await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            var saved = await service.SaveAsync(new ReplaySaveRequest(
                Path.Combine(root, "receipt-media")));

            Assert.IsTrue(armed.Succeeded);
            Assert.IsTrue(saved.Succeeded);
            var segment = publisher.Publications.Single().Segments.Single();
            Assert.IsTrue(segment.IncludesMicrophone);
            Assert.IsTrue(segment.IncludesSystemAudio);
            Assert.IsTrue(segment.IncludesWebcam);
            Assert.AreEqual(2, encoder.CompletedAudioSourceCounts.Single());
            Assert.IsTrue(
                encoder.WebcamColoredFrameCount > 0,
                "At least one encoded screen frame should contain the precomposed camera pixels.");
            Assert.AreEqual(2, audio.Requests.Count);
            Assert.AreEqual("mic-fixture", audio.Requests.Single(request =>
                request.Source == AudioCaptureSource.Microphone).DeviceId);
            Assert.AreEqual("render-fixture", audio.Requests.Single(request =>
                request.Source == AudioCaptureSource.SystemAudio).DeviceId);
            Assert.AreEqual("camera-fixture", camera.LastRequestedDeviceId);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_OmitsFailedRequestedAudioWithoutFalseSegmentClaim()
    {
        var root = CreateTempRoot();
        try
        {
            var replaySettings = CreateReplaySettings();
            var publisher = new CapturingPublisher();
            var coordinator = new ReplayBufferCoordinator(
                replaySettings,
                publisher,
                new TestReplayFileManager());
            var audio = new SelectiveAudioCaptureService(AudioCaptureSource.Microphone);
            var encoder = new InspectingEncoderFactory(replaySettings.FramesPerSecond);
            var recording = new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            };

            await using var service = new ReplayRecordingService(
                replaySettings,
                includeCursor: false,
                root,
                coordinator,
                new SingleSegmentFrameSourceFactory(),
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero,
                recordingSettings: recording,
                audioCapture: audio);

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            await service.SaveAsync(new ReplaySaveRequest(Path.Combine(root, "receipt-partial-audio")));

            var segment = publisher.Publications.Single().Segments.Single();
            Assert.IsTrue(segment.IncludesMicrophone);
            Assert.IsFalse(segment.IncludesSystemAudio);
            Assert.AreEqual(1, encoder.CompletedAudioSourceCounts.Single());

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public async Task Producer_DoesNotMuxAudioWhenPrivacyExclusionSuppressesAnySegmentFrame()
    {
        var root = CreateTempRoot();
        try
        {
            var replaySettings = CreateReplaySettings();
            var publisher = new CapturingPublisher();
            var coordinator = new ReplayBufferCoordinator(
                replaySettings,
                publisher,
                new TestReplayFileManager());
            var encoder = new InspectingEncoderFactory(replaySettings.FramesPerSecond);

            await using var service = new ReplayRecordingService(
                replaySettings,
                includeCursor: false,
                root,
                coordinator,
                new SingleSegmentFrameSourceFactory(),
                encoder,
                new TestReplayClock(),
                TimeSpan.Zero,
                privacyGuard: new AlwaysSuppressPrivacyGuard(),
                recordingSettings: new RecordingSettings
                {
                    IncludeMicrophone = true,
                    ShowRecordingBorder = false,
                    ShowRecordingTimer = false
                },
                audioCapture: new SuccessfulAudioCaptureService());

            await service.ArmAsync();
            await WaitUntilAsync(() => coordinator.GetStatus().SegmentCount == 1);
            await service.SaveAsync(new ReplaySaveRequest(Path.Combine(root, "receipt-private")));

            var segment = publisher.Publications.Single().Segments.Single();
            Assert.IsFalse(segment.IncludesMicrophone);
            Assert.AreEqual(0, encoder.CompletedAudioSourceCounts.Single());
            Assert.IsTrue(encoder.BlackFrameCount > 0);

            await service.StopAsync();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    public void ManifestCoverage_ReportsOnlyRequestedPartialMediaInputs()
    {
        var normalized = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            IncludeMicrophone = true,
            IncludeSystemAudio = true,
            EnableWebcamOverlay = true,
            ShowRecordingBorder = false,
            ShowRecordingTimer = false
        });

        var limitations = ReplayReceiptPackagePublisher.BuildCaptureLimitations(
            normalized,
            segmentCount: 4,
            systemAudioSegmentCount: 3,
            microphoneSegmentCount: 4,
            webcamSegmentCount: 0);

        StringAssert.Contains(limitations, "system audio is present in 3 of 4");
        StringAssert.Contains(limitations, "webcam overlay is present in 0 of 4");
        Assert.IsFalse(limitations.Contains("microphone audio", StringComparison.OrdinalIgnoreCase));
    }

    private static ReplayBufferSettings CreateReplaySettings() => new()
    {
        BufferDuration = TimeSpan.FromSeconds(60),
        SegmentDuration = TimeSpan.FromSeconds(2),
        MaxBufferBytes = 10_000_000,
        FramesPerSecond = 2,
        CaptureSource = ReplayCaptureSourceDescriptor.FollowCursorMonitor()
    };

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
        var root = Path.Combine(
            Path.GetTempPath(),
            "Receipts.Tests",
            Guid.NewGuid().ToString("N"));
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

    private sealed class SingleSegmentFrameSourceFactory : IReplayFrameSourceFactory
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

            return [new SolidFrameSource()];
        }
    }

    private sealed class SolidFrameSource : IReplayFrameSource
    {
        public string TrackId => "monitor-track";
        public string DisplayName => "Fixture monitor";
        public ReplayCaptureSourceDescriptor Source => new(
            ReplayCaptureSourceKind.FollowCursorMonitor,
            "monitor:fixture",
            DisplayName);
        public double DpiScaleX => 1d;
        public double DpiScaleY => 1d;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = new Bitmap(200, 200);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Blue);
            }

            return Task.FromResult(new CapturedBitmap(
                bitmap,
                CaptureKind.ActiveMonitor,
                new CaptureBounds { Width = 200, Height = 200 },
                new CaptureSource { MonitorName = "fixture" }));
        }

        public void Dispose()
        {
        }
    }

    private sealed class InspectingEncoderFactory(int framesPerSecond) : IReplayVideoSegmentEncoderFactory
    {
        public ConcurrentQueue<int> CompletedAudioSourceCounts { get; } = new();
        public int WebcamColoredFrameCount;
        public int BlackFrameCount;

        public IReplayVideoSegmentSession Start(string outputPath, int width, int height) =>
            new InspectingEncoderSession(this, outputPath, framesPerSecond, width, height);

        private sealed class InspectingEncoderSession(
            InspectingEncoderFactory owner,
            string outputPath,
            int framesPerSecond,
            int width,
            int height) : IReplayVideoSegmentSession
        {
            public string OutputPath { get; } = outputPath;
            public int FramesPerSecond { get; } = framesPerSecond;
            public int FrameCount { get; private set; }
            public long AudioBytes { get; private set; }

            public void WriteFrame(Bitmap frame, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.AreEqual(width, frame.Width);
                Assert.AreEqual(height, frame.Height);
                var sample = frame.GetPixel(width - 50, height - 50);
                if (sample.ToArgb() == Color.Black.ToArgb())
                {
                    Interlocked.Increment(ref owner.BlackFrameCount);
                }

                if (sample.R > sample.B)
                {
                    Interlocked.Increment(ref owner.WebcamColoredFrameCount);
                }

                FrameCount++;
            }

            public void WriteAudioPcm(ReadOnlyMemory<byte> pcm16) =>
                AudioBytes += pcm16.Length;

            public RecordingResult Complete() => Complete(false, 0, CancellationToken.None);

            public RecordingResult Complete(
                bool includeAudio,
                int audioSourceCount,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.AreEqual(includeAudio, audioSourceCount > 0);
                if (includeAudio)
                {
                    Assert.IsTrue(AudioBytes > 0);
                }

                owner.CompletedAudioSourceCounts.Enqueue(audioSourceCount);
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
                File.WriteAllBytes(OutputPath, Enumerable.Repeat((byte)7, Math.Max(1, FrameCount)).ToArray());
                return new RecordingResult
                {
                    Succeeded = true,
                    OutputPath = OutputPath,
                    Message = "fixture encoder finalized"
                };
            }

            public void Dispose()
            {
            }
        }
    }

    private class SuccessfulAudioCaptureService : IAudioCaptureService, IStreamingAudioCaptureService
    {
        public ConcurrentQueue<StreamingAudioCaptureRequest> Requests { get; } = new();

        public virtual IStreamingAudioCaptureSession StartStreaming(
            StreamingAudioCaptureRequest request,
            Action<StreamingAudioChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request);
            return new FixtureStreamingAudioSession(request, onChunk);
        }

        public Task<AudioCaptureResult> CaptureWavAsync(
            AudioCaptureRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AudioCaptureResult(
                false, null, "WAV capture is not used by recording tests.", TimeSpan.Zero, 0, null));

        public Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AudioCaptureDevice>>([]);

        public Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AudioCaptureDevice>>([]);

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealth(true, "fixture"));

        private sealed class FixtureStreamingAudioSession(
            StreamingAudioCaptureRequest request,
            Action<StreamingAudioChunk> onChunk) : IStreamingAudioCaptureSession
        {
            private bool _stopped;

            public AudioCaptureSource Source => request.Source;

            public Task<StreamingAudioCaptureResult> StopAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_stopped)
                {
                    onChunk(new StreamingAudioChunk(
                        Enumerable.Repeat((byte)1, 8_192).ToArray(),
                        TimeSpan.Zero));
                    _stopped = true;
                }

                return Task.FromResult(new StreamingAudioCaptureResult(
                    true,
                    "fixture streamed",
                    TimeSpan.FromMilliseconds(42),
                    8_192,
                    new AudioCaptureDevice(
                        request.DeviceId,
                        request.DeviceId,
                        IsDefault: false,
                        SupportsLoopback: request.Source == AudioCaptureSource.SystemAudio)));
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class SelectiveAudioCaptureService(
        AudioCaptureSource successfulSource) : SuccessfulAudioCaptureService
    {
        public override IStreamingAudioCaptureSession StartStreaming(
            StreamingAudioCaptureRequest request,
            Action<StreamingAudioChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            if (request.Source == successfulSource)
            {
                return base.StartStreaming(request, onChunk, cancellationToken);
            }

            Requests.Enqueue(request);
            throw new InvalidOperationException("fixture endpoint unavailable");
        }
    }

    private sealed class SolidCameraOverlayService(Color color) : ICameraOverlayService
    {
        public string LastRequestedDeviceId { get; private set; } = string.Empty;

        public Task<CameraOverlayFrameResult> CaptureFrameAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestedDeviceId = deviceId;
            var bitmap = new Bitmap(100, 100);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
            }

            return Task.FromResult(new CameraOverlayFrameResult(
                true,
                bitmap,
                new CameraOverlayDevice(deviceId, "Fixture camera", true),
                "fixture captured"));
        }

        public Task<IReadOnlyList<CameraOverlayDevice>> ListDevicesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CameraOverlayDevice>>([]);

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealth(true, "fixture"));
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

    private sealed class AlwaysSuppressPrivacyGuard : IReplayPrivacyGuard
    {
        public ReplayPrivacyDecision EvaluateForegroundProcess() =>
            ReplayPrivacyDecision.Suppress("fixture privacy suppression");
    }

    private sealed class TestReplayFileManager : IReplayBufferFileManager
    {
        public bool TryDeleteBufferedSegment(ReplaySegmentMetadata segment)
        {
            File.Delete(segment.FilePath);
            return true;
        }

        public ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
            IReadOnlyCollection<string> residentFilePaths,
            TimeSpan minimumAge,
            DateTimeOffset nowUtc) => new([], residentFilePaths.ToArray(), []);
    }

    private sealed class CapturingPublisher : IReplaySnapshotPublisher
    {
        public List<ReplaySnapshotPublication> Publications { get; } = [];

        public Task<ReplaySnapshotPublishResult> PublishAsync(
            ReplaySnapshotPublication publication,
            CancellationToken cancellationToken)
        {
            Publications.Add(publication);
            return Task.FromResult(new ReplaySnapshotPublishResult(
                publication.ReceiptId,
                publication.DestinationDirectory,
                publication.Segments.Select(segment => new ReplayPublishedSegment(
                    segment.SegmentId,
                    segment.TrackId,
                    Path.GetFileName(segment.FilePath),
                    segment.FilePath,
                    segment.ByteLength)).ToArray()));
        }
    }
}

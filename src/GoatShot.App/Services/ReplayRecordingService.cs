using System.Drawing;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public interface IReplayRecordingService : IAsyncDisposable
{
    event EventHandler<ReplayRecordingStatusChangedEventArgs>? StatusChanged;

    ReplayBufferStatus GetStatus();
    Task<ReplayCommandResult> ArmAsync(CancellationToken cancellationToken = default);
    ReplayCommandResult Pause();
    ReplayCommandResult Resume();
    ReplayCommandResult SuspendForSystemEvent();
    ReplayCommandResult ResumeAfterSystemEvent();
    Task<ReplayCommandResult> StopAsync(CancellationToken cancellationToken = default);
    Task<ReplaySaveResult> SaveAsync(ReplaySaveRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReplayRecordingService : IReplayRecordingService
{
    public static readonly TimeSpan DefaultAbandonedBufferMinimumAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WebcamRefreshInterval = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan DefaultLiveSourcePollingInterval = TimeSpan.FromMilliseconds(500);

    private readonly ReplayBufferSettings _settings;
    private readonly bool _includeCursor;
    private readonly string _bufferRoot;
    private readonly IReplayBufferCoordinator _coordinator;
    private readonly IReplayFrameSourceFactory _frameSources;
    private readonly IReplayVideoSegmentEncoderFactory _encoders;
    private readonly IReplayRecordingClock _clock;
    private readonly IReplayPrivacyGuard _privacyGuard;
    private readonly ReplayMediaProfile _mediaProfile;
    private readonly NormalizedRecordingSettings _normalizedRecordingSettings;
    private readonly IAudioCaptureService? _audioCapture;
    private readonly ICameraOverlayService? _cameraOverlay;
    private readonly TimeSpan _liveSourcePollingInterval;
    private readonly TimeSpan _abandonedBufferMinimumAge;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _saveBarrierGate = new();
    private readonly AsyncManualResetEvent _captureEnabled = new();
    private CancellationTokenSource? _workerCancellation;
    private Task? _worker;
    private string _runId = string.Empty;
    private long _runStartedTimestamp;
    private long _nextSegmentSequence;
    private bool _hasTimelineOrigin;
    private int _privacySuppressionActive;
    private int _systemCaptureSuspended;
    private int _userPauseRequested;
    private int _segmentCaptureActive;
    private int _segmentRotationRequested;
    private SegmentSaveBarrier? _pendingSaveBarrier;
    private bool _startupCleanupCompleted;
    private bool _disposed;

    public ReplayRecordingService(
        ReplayBufferSettings? settings,
        bool includeCursor,
        string bufferRoot,
        IReplayBufferCoordinator coordinator,
        IReplayFrameSourceFactory frameSources,
        IReplayVideoSegmentEncoderFactory encoders,
        IReplayRecordingClock? clock = null,
        TimeSpan? abandonedBufferMinimumAge = null,
        IReadOnlyList<string>? configurationWarnings = null,
        IReplayPrivacyGuard? privacyGuard = null,
        RecordingSettings? recordingSettings = null,
        IAudioCaptureService? audioCapture = null,
        ICameraOverlayService? cameraOverlay = null,
        TimeSpan? liveSourcePollingInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferRoot);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(frameSources);
        ArgumentNullException.ThrowIfNull(encoders);

        _settings = (settings ?? new ReplayBufferSettings()).Normalize();
        _includeCursor = includeCursor;
        _bufferRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bufferRoot));
        _coordinator = coordinator;
        _frameSources = frameSources;
        _encoders = encoders;
        _clock = clock ?? SystemReplayRecordingClock.Instance;
        _privacyGuard = privacyGuard ?? new WindowsReplayPrivacyGuard(
            _settings.PrivacyExcludedProcessNames);
        var effectiveRecordingSettings = recordingSettings ?? new RecordingSettings
        {
            ShowRecordingBorder = false,
            ShowRecordingTimer = false
        };
        _mediaProfile = ReplayMediaProfile.Create(effectiveRecordingSettings);
        _normalizedRecordingSettings = RecordingSettingsNormalizer.Normalize(
            effectiveRecordingSettings,
            _settings.FramesPerSecond);
        _audioCapture = audioCapture;
        _cameraOverlay = cameraOverlay;
        _liveSourcePollingInterval = liveSourcePollingInterval ?? DefaultLiveSourcePollingInterval;
        if (_liveSourcePollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(liveSourcePollingInterval),
                "Replay live-source polling interval must be positive.");
        }
        var warnings = new List<string>(configurationWarnings ?? Array.Empty<string>());
        if ((_mediaProfile.IncludeMicrophone || _mediaProfile.IncludeSystemAudio) && _audioCapture is null)
        {
            warnings.Add("Replay audio was requested, but no audio capture service is available.");
        }

        if (_mediaProfile.EnableWebcamOverlay && _cameraOverlay is null)
        {
            warnings.Add("Replay webcam overlay was requested, but no camera capture service is available.");
        }

        ConfigurationWarnings = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _abandonedBufferMinimumAge = abandonedBufferMinimumAge ?? DefaultAbandonedBufferMinimumAge;
        if (_abandonedBufferMinimumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(abandonedBufferMinimumAge),
                "Abandoned replay buffer cleanup age cannot be negative.");
        }
    }

    public ReplayRecordingService(
        ReplayBufferSettings replaySettings,
        RecordingSettings recordingSettings,
        bool includeCursor,
        FileReplayBufferStorage storage,
        ReplayReceiptPackagePublisher publisher,
        ScreenshotService screenshots,
        ProductionVideoEncoderSelection encoder,
        IAudioCaptureService audioCapture,
        ICameraOverlayService cameraOverlay,
        IReplayRecordingClock? clock = null)
        : this(
            replaySettings,
            includeCursor,
            storage?.BufferRoot ?? throw new ArgumentNullException(nameof(storage)),
            new ReplayBufferCoordinator(replaySettings, publisher, storage),
            new WindowsReplayFrameSourceFactory(screenshots),
            new MediaFoundationReplayVideoSegmentEncoderFactory(
                RecordingSettingsNormalizer.Normalize(
                    recordingSettings,
                    replaySettings.Normalize().FramesPerSecond),
                encoder),
            clock,
            abandonedBufferMinimumAge: null,
            configurationWarnings: DescribeUnsupportedProfileFeatures(recordingSettings),
            privacyGuard: new WindowsReplayPrivacyGuard(
                replaySettings.Normalize().PrivacyExcludedProcessNames),
            recordingSettings: recordingSettings,
            audioCapture: audioCapture,
            cameraOverlay: cameraOverlay)
    {
    }

    public event EventHandler<ReplayRecordingStatusChangedEventArgs>? StatusChanged;

    public ReplayBufferCleanupResult? StartupCleanupResult { get; private set; }
    public IReadOnlyList<string> ConfigurationWarnings { get; }

    public ReplayBufferStatus GetStatus() => _coordinator.GetStatus() with
    {
        SystemSuspended = Volatile.Read(ref _systemCaptureSuspended) != 0
    };

    public async Task<ReplayCommandResult> ArmAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_startupCleanupCompleted)
            {
                StartupCleanupResult = _coordinator.CleanupAbandonedBufferFiles(
                    _abandonedBufferMinimumAge,
                    _clock.UtcNow);
                _startupCleanupCompleted = true;
            }

            var priorState = _coordinator.GetStatus().State;
            if (priorState == ReplayBufferState.Error && _worker is not null)
            {
                try
                {
                    await _worker.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                _worker = null;
            }

            var result = _coordinator.Arm();
            if (!result.Succeeded)
            {
                PublishStatus(result.Message);
                return result;
            }

            Interlocked.Exchange(ref _userPauseRequested, 0);
            if (Volatile.Read(ref _systemCaptureSuspended) == 0)
            {
                _captureEnabled.Set();
            }
            else
            {
                _captureEnabled.Reset();
            }
            if (_worker is null || _worker.IsCompleted)
            {
                _workerCancellation?.Dispose();
                _workerCancellation = new CancellationTokenSource();
                _runId = $"run-{Guid.NewGuid():N}";
                if (!_hasTimelineOrigin)
                {
                    _runStartedTimestamp = _clock.GetTimestamp();
                    _nextSegmentSequence = 0;
                    _hasTimelineOrigin = true;
                }

                Interlocked.Exchange(ref _privacySuppressionActive, 0);
                _worker = Task.Run(
                    () => RunCaptureLoopAsync(_workerCancellation.Token),
                    CancellationToken.None);
            }

            var message = ConfigurationWarnings.Count == 0
                ? result.Message
                : $"{result.Message} {string.Join(" ", ConfigurationWarnings)}";
            var armedResult = result with { Message = message };
            PublishStatus(armedResult.Message);
            return armedResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = _coordinator.ReportError($"Replay startup failed: {ex.Message}");
            PublishStatus(result.Message);
            return result;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ReplayCommandResult Pause()
    {
        ThrowIfDisposed();
        _captureEnabled.Reset();
        var result = _coordinator.PauseAfterCurrentCapture();
        if (result.Succeeded)
        {
            Interlocked.Exchange(ref _userPauseRequested, 1);
            if (Volatile.Read(ref _segmentCaptureActive) != 0)
            {
                Interlocked.Exchange(ref _segmentRotationRequested, 1);
            }
        }
        else if (_coordinator.GetStatus().State == ReplayBufferState.Armed &&
                 Volatile.Read(ref _systemCaptureSuspended) == 0)
        {
            _captureEnabled.Set();
        }

        PublishStatus(result.Message);
        return result;
    }

    public ReplayCommandResult Resume()
    {
        ThrowIfDisposed();
        var result = _coordinator.Resume();
        if (result.Succeeded)
        {
            Interlocked.Exchange(ref _userPauseRequested, 0);
            if (Volatile.Read(ref _systemCaptureSuspended) == 0)
            {
                _captureEnabled.Set();
            }
            else
            {
                result = result with
                {
                    Message = "Replay remains suspended until the Windows session resumes."
                };
            }
        }

        PublishStatus(result.Message);
        return result;
    }

    public ReplayCommandResult SuspendForSystemEvent()
    {
        ThrowIfDisposed();
        var alreadySuspended = Interlocked.Exchange(ref _systemCaptureSuspended, 1) != 0;
        _captureEnabled.Reset();
        if (Volatile.Read(ref _segmentCaptureActive) != 0)
        {
            Interlocked.Exchange(ref _segmentRotationRequested, 1);
        }

        var status = _coordinator.GetStatus();
        var result = new ReplayCommandResult(
            true,
            status.State,
            alreadySuspended
                ? "Replay capture is already suspended for the Windows session."
                : "Replay capture suspended for the Windows session; the buffered history is retained.");
        PublishStatus(result.Message);
        return result;
    }

    public ReplayCommandResult ResumeAfterSystemEvent()
    {
        ThrowIfDisposed();
        var wasSuspended = Interlocked.Exchange(ref _systemCaptureSuspended, 0) != 0;
        var status = _coordinator.GetStatus();
        var shouldCapture = Volatile.Read(ref _userPauseRequested) == 0 &&
            status.State is ReplayBufferState.Armed or ReplayBufferState.Saving;
        if (shouldCapture)
        {
            _captureEnabled.Set();
        }
        else
        {
            _captureEnabled.Reset();
        }

        var message = !wasSuspended
            ? "Replay capture was not suspended for the Windows session."
            : shouldCapture
                ? "Windows session resumed; Replay capture is buffering again."
                : status.State == ReplayBufferState.Paused
                    ? "Windows session resumed; Replay remains paused by the user."
                    : "Windows session resumed; Replay capture remains inactive.";
        var result = new ReplayCommandResult(true, status.State, message);
        PublishStatus(result.Message);
        return result;
    }

    public async Task<ReplayCommandResult> StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_coordinator.GetStatus().State == ReplayBufferState.Saving)
            {
                var blocked = _coordinator.Stop();
                PublishStatus(blocked.Message);
                return blocked;
            }

            await StopWorkerCoreAsync().ConfigureAwait(false);
            var result = _coordinator.Stop();
            if (result.Succeeded)
            {
                Interlocked.Exchange(ref _userPauseRequested, 0);
                ResetTimelineAfterCatalogClear();
            }

            PublishStatus(result.Message);
            return result;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ReplaySaveResult> SaveAsync(
        ReplaySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var barrier = RequestActiveSegmentSaveBarrier();
        if (barrier is not null)
        {
            SegmentSaveBarrierResult barrierResult;
            try
            {
                barrierResult = await barrier.Finalized.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                barrier.ProducerMayContinue.TrySetResult();
                var canceledStatus = _coordinator.GetStatus();
                return new ReplaySaveResult(
                    false,
                    "Replay save was canceled before the active segment finalized.",
                    request.ReceiptId,
                    null,
                    [],
                    canceledStatus.State == ReplayBufferState.Armed,
                    canceledStatus.State);
            }

            if (!barrierResult.Succeeded)
            {
                barrier.ProducerMayContinue.TrySetResult();
                var failedStatus = _coordinator.GetStatus();
                return new ReplaySaveResult(
                    false,
                    "Replay save stopped because the active synchronized segment could not " +
                    $"be finalized through the hotkey boundary. {barrierResult.Message}",
                    request.ReceiptId,
                    null,
                    [],
                    failedStatus.State == ReplayBufferState.Armed,
                    failedStatus.State);
            }
        }

        Task<ReplaySaveResult> save;
        try
        {
            // ReplayBufferCoordinator acquires its immutable catalog snapshot
            // synchronously before its first asynchronous publication await. Only
            // then may the producer begin the post-hotkey segment.
            save = _coordinator.SaveAsync(request, cancellationToken);
        }
        finally
        {
            barrier?.ProducerMayContinue.TrySetResult();
        }

        PublishStatus("Saving replay receipt.");
        var result = await save.ConfigureAwait(false);
        PublishStatus(result.Message);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopWorkerCoreAsync().ConfigureAwait(false);
            if (_coordinator.GetStatus().State != ReplayBufferState.Saving)
            {
                var stopped = _coordinator.Stop();
                if (stopped.Succeeded)
                {
                    ResetTimelineAfterCatalogClear();
                }
            }

            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _workerCancellation?.Dispose();
        }
    }

    public static IReadOnlyList<string> DescribeUnsupportedProfileFeatures(
        RecordingSettings? recordingSettings)
    {
        return [];
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var webcamOverlaySource = CreateWebcamOverlaySource();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _captureEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (Volatile.Read(ref _systemCaptureSuspended) != 0)
                {
                    _captureEnabled.Reset();
                    continue;
                }

                var state = _coordinator.GetStatus().State;
                if (state is ReplayBufferState.Off or ReplayBufferState.Error)
                {
                    return;
                }

                if (state == ReplayBufferState.Paused)
                {
                    _captureEnabled.Reset();
                    continue;
                }

                SegmentCaptureResult result;
                result = await CaptureSegmentAsync(webcamOverlaySource, cancellationToken)
                    .ConfigureAwait(false);

                var saveBarrier = CompletePendingSaveBarrier(result);
                if (saveBarrier is not null)
                {
                    await saveBarrier.ProducerMayContinue.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                if (result.FinalizedCount > 0)
                {
                    PublishStatus(
                        $"Replay buffered {result.FinalizedCount} finalized segment(s)" +
                        (result.Failures.Count == 0
                            ? "."
                            : $"; {result.Failures.Count} capture issue(s): " +
                              string.Join(" ", result.Failures.Take(2))));
                }
                else if (result.Failures.Count > 0)
                {
                    PublishStatus(
                        "Replay recorded a synchronized capture gap; no partial track set was " +
                        "buffered. " + string.Join(" ", result.Failures.Take(2)));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var result = _coordinator.ReportError(
                $"Replay capture failed: {ex.GetType().Name}: {ex.Message}");
            _captureEnabled.Reset();
            PublishStatus(result.Message);
        }
        finally
        {
            var saveBarrier = CompletePendingSaveBarrier(new SegmentCaptureResult(
                0,
                ["Replay capture stopped before the active save boundary finalized."]));
            saveBarrier?.ProducerMayContinue.TrySetResult();
        }
    }

    private async Task<SegmentCaptureResult> CaptureSegmentAsync(
        RecordingService.NativeWebcamOverlaySource? webcamOverlaySource,
        CancellationToken cancellationToken)
    {
        var sources = await _frameSources
            .OpenSegmentSourcesAsync(_settings.CaptureSource, _includeCursor, cancellationToken)
            .ConfigureAwait(false);
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("Replay capture strategy did not open any frame sources.");
        }

        var writers = new List<TrackWriter>(sources.Count);
        var failures = new List<string>();
        StreamingRecordingAudioSession? streamingAudio = null;
        var audioCapturesCompleted = false;
        var privacySuppressedDuringSegment = false;
        try
        {
            var stateBeforeFirstFrame = _coordinator.GetStatus().State;
            if (stateBeforeFirstFrame is ReplayBufferState.Off or ReplayBufferState.Paused or
                ReplayBufferState.Error || Volatile.Read(ref _systemCaptureSuspended) != 0)
            {
                return new SegmentCaptureResult(0, []);
            }

            // Source discovery can fail or be canceled without consuming a
            // timeline identity. Once capture begins, however, every attempt owns
            // a unique sequence even if its synchronized set is later discarded.
            var sequence = Interlocked.Increment(ref _nextSegmentSequence) - 1;
            if (webcamOverlaySource is not null)
            {
                try
                {
                    var refreshed = await webcamOverlaySource
                        .RefreshIfDueAsync(_clock.UtcNow, cancellationToken)
                        .ConfigureAwait(false);
                    if (!refreshed && webcamOverlaySource.Current is null)
                    {
                        failures.Add(
                            $"Webcam overlay unavailable for this segment ({webcamOverlaySource.LastMessage}).");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(
                        $"Webcam overlay refresh failed for this segment ({ex.GetType().Name}: {ex.Message}).");
                }
            }

            foreach (var source in sources)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var first = await source
                        .CaptureFrameAsync(TimeSpan.FromSeconds(1), cancellationToken)
                        .ConfigureAwait(false);
                    var encodedSize = ResolveEncodedFrameSize(
                        first.Bitmap.Width,
                        first.Bitmap.Height,
                        _mediaProfile.TargetWidth,
                        _mediaProfile.TargetHeight);
                    var track = BuildTrackDescriptor(
                        source,
                        first,
                        encodedSize.Width,
                        encodedSize.Height);
                    var paths = BuildSegmentPaths(sequence, writers.Count);
                    var session = _encoders.Start(
                        paths.WorkingPath,
                        encodedSize.Width,
                        encodedSize.Height);
                    var writer = new TrackWriter(
                        source,
                        track,
                        session,
                        paths.WorkingPath,
                        paths.FinalPath,
                        first.Bitmap.Width,
                        first.Bitmap.Height);
                    try
                    {
                        using var decorated = RecordingService.DecorateRecordingFrame(
                            first,
                            _includeCursor,
                            _normalizedRecordingSettings,
                            TimeSpan.Zero);
                        var written = WriteFrame(
                            session,
                            decorated,
                            writer.Track,
                            webcamOverlaySource?.Current,
                            cancellationToken);
                        writer.IncludesWebcam = written.IncludesWebcam;
                        writer.WebcamFrameCount += written.IncludesWebcam ? 1 : 0;
                        privacySuppressedDuringSegment |= written.PrivacySuppressed;
                        writer.RememberLastCapturedFrame(decorated);
                        writers.Add(writer);
                    }
                    catch
                    {
                        session.Dispose();
                        TryDeleteOwnedFile(paths.WorkingPath);
                        throw;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{source.DisplayName}: {ex.Message}");
                }
            }

            if (writers.Count == 0)
            {
                audioCapturesCompleted = true;
                throw new InvalidOperationException(
                    "Replay could not start any capture tracks. " + string.Join(" ", failures));
            }

            if (writers.Count != sources.Count)
            {
                audioCapturesCompleted = true;
                failures.Add(
                    "Replay skipped the synchronized capture set because one or more configured " +
                    "tracks could not start; no partial set was added to the buffer.");
                return new SegmentCaptureResult(0, failures);
            }

            // The encoded timeline begins when every synchronized writer has accepted
            // its first frame. Start audio from the same boundary so device-open latency
            // does not shift PCM earlier than the corresponding video content.
            var segmentStartedTimestamp = _clock.GetTimestamp();
            var segmentStartedAtUtc = _clock.UtcNow;
            var monotonicStart = _clock.GetElapsedTime(_runStartedTimestamp, segmentStartedTimestamp);
            streamingAudio = StartReplayAudioCapture(writers, cancellationToken);

            // Only advertise an active save boundary after every synchronized
            // encoder has accepted its first frame. Source discovery/opening can
            // block and must not make Save wait on a segment that does not exist.
            Interlocked.Exchange(ref _segmentCaptureActive, 1);

            var expectedFrameCount = Math.Max(
                1,
                (int)Math.Ceiling(
                    _settings.SegmentDuration.TotalSeconds * _settings.FramesPerSecond));
            var endedEarly = false;
            var nextLiveSourcePoll = _liveSourcePollingInterval;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = _coordinator.GetStatus().State;
                if (state is ReplayBufferState.Off or ReplayBufferState.Paused or ReplayBufferState.Error ||
                    Volatile.Read(ref _systemCaptureSuspended) != 0)
                {
                    endedEarly = true;
                    break;
                }

                if (Volatile.Read(ref _segmentRotationRequested) != 0)
                {
                    endedEarly = true;
                    break;
                }

                var elapsed = _clock.GetElapsedTime(segmentStartedTimestamp, _clock.GetTimestamp());
                var nextFrameDue = TimeSpan.FromSeconds(
                    writers.Min(writer => writer.Session.FrameCount) /
                    (double)_settings.FramesPerSecond);
                var delay = nextFrameDue - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }

                var currentElapsed = _clock.GetElapsedTime(
                    segmentStartedTimestamp,
                    _clock.GetTimestamp());
                if (currentElapsed >= _settings.SegmentDuration)
                {
                    break;
                }

                if (currentElapsed >= nextLiveSourcePoll)
                {
                    nextLiveSourcePoll = currentElapsed + _liveSourcePollingInterval;
                    try
                    {
                        if (_frameSources.HasLiveSourceSetChanged(
                            _settings.CaptureSource,
                            writers.Select(writer => writer.Source).ToArray()))
                        {
                            failures.Add(
                                "Replay live source, display topology, bounds, or DPI changed; " +
                                "the current segment was finalized before reopening targets.");
                            endedEarly = true;
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(
                            $"Replay live source inspection failed ({ex.GetType().Name}); " +
                            "the current segment was finalized before reopening targets.");
                        endedEarly = true;
                        break;
                    }
                }

                if (webcamOverlaySource is not null)
                {
                    try
                    {
                        await webcamOverlaySource
                            .RefreshIfDueAsync(_clock.UtcNow, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add(
                            $"Webcam overlay refresh failed ({ex.GetType().Name}: {ex.Message}); " +
                            "the last good camera frame remains in use when available.");
                    }
                }

                var finalizeForSourceTransition = false;
                foreach (var writer in writers.Where(writer => writer.IsActive).ToArray())
                {
                    try
                    {
                        using var captured = await writer.Source
                            .CaptureFrameAsync(
                                TimeSpan.FromSeconds(1d / _settings.FramesPerSecond),
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (HasSourceGeometryChanged(writer, captured))
                        {
                            failures.Add(
                                $"{writer.Track.DisplayName}: source geometry or DPI changed; " +
                                "the current segment was finalized before reopening the source.");
                            finalizeForSourceTransition = true;
                            break;
                        }

                        using var decorated = RecordingService.DecorateRecordingFrame(
                            captured,
                            _includeCursor,
                            _normalizedRecordingSettings,
                            _clock.GetElapsedTime(
                                segmentStartedTimestamp,
                                _clock.GetTimestamp()));
                        var written = WriteFrame(
                            writer.Session,
                            decorated,
                            writer.Track,
                            webcamOverlaySource?.Current,
                            cancellationToken);
                        writer.IncludesWebcam |= written.IncludesWebcam;
                        writer.WebcamFrameCount += written.IncludesWebcam ? 1 : 0;
                        privacySuppressedDuringSegment |= written.PrivacySuppressed;
                        writer.RememberLastCapturedFrame(decorated);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add($"{writer.Track.DisplayName}: {ex.Message}");
                        // A display/window disappearing is a source transition, not a
                        // reason to discard already encoded frames or strand Replay in
                        // Error. Finalize every synchronized writer at this boundary;
                        // the next segment re-resolves the configured target strategy.
                        finalizeForSourceTransition = true;
                        break;
                    }
                }

                if (finalizeForSourceTransition)
                {
                    endedEarly = true;
                    break;
                }

                var desiredFrameCount = Math.Min(
                    expectedFrameCount,
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            _clock.GetElapsedTime(
                                    segmentStartedTimestamp,
                                    _clock.GetTimestamp())
                                .TotalSeconds * _settings.FramesPerSecond)));
                foreach (var writer in writers.Where(writer => writer.IsActive))
                {
                    while (writer.Session.FrameCount < desiredFrameCount &&
                           writer.LastCapturedFrame is not null)
                    {
                        var written = WriteFrame(
                            writer.Session,
                            writer.LastCapturedFrame,
                            writer.Track,
                            webcamOverlaySource?.Current,
                            cancellationToken);
                        writer.IncludesWebcam |= written.IncludesWebcam;
                        writer.WebcamFrameCount += written.IncludesWebcam ? 1 : 0;
                        privacySuppressedDuringSegment |= written.PrivacySuppressed;
                    }
                }
            }

            var finalElapsed = _clock.GetElapsedTime(
                segmentStartedTimestamp,
                _clock.GetTimestamp());
            var finalFrameCount = endedEarly
                ? Math.Min(
                    expectedFrameCount,
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            finalElapsed.TotalSeconds * _settings.FramesPerSecond)))
                : expectedFrameCount;
            foreach (var writer in writers.Where(writer => writer.IsActive))
            {
                while (writer.Session.FrameCount < finalFrameCount &&
                       writer.LastCapturedFrame is not null)
                {
                    var written = WriteFrame(
                        writer.Session,
                        writer.LastCapturedFrame,
                        writer.Track,
                        webcamOverlaySource?.Current,
                        cancellationToken);
                    writer.IncludesWebcam |= written.IncludesWebcam;
                    writer.WebcamFrameCount += written.IncludesWebcam ? 1 : 0;
                    privacySuppressedDuringSegment |= written.PrivacySuppressed;
                }
            }

            var audio = streamingAudio is null
                ? new StreamingRecordingAudioResult([], 0, [])
                : await streamingAudio.StopAsync(CancellationToken.None).ConfigureAwait(false);
            audioCapturesCompleted = true;
            var includeAudio = !privacySuppressedDuringSegment &&
                audio.SourcesWithPayload.Count > 0;
            if (privacySuppressedDuringSegment && audio.SourcesWithPayload.Count > 0)
            {
                failures.Add(
                    "Requested audio was omitted because a privacy exclusion was active " +
                    "during this segment.");
            }

            failures.AddRange(audio.Issues);

            var stagedTracks = new List<StagedTrack>(writers.Count);
            foreach (var writer in writers.Where(writer => writer.IsActive))
            {
                try
                {
                    var encoding = writer.Session.Complete(
                        includeAudio,
                        includeAudio ? audio.SourcesWithPayload.Count : 0,
                        cancellationToken);
                    if (!encoding.Succeeded || !File.Exists(writer.WorkingPath))
                    {
                        throw new IOException(
                            string.IsNullOrWhiteSpace(encoding.Message)
                                ? "Media Foundation did not finalize the replay segment."
                                : encoding.Message);
                    }

                    File.Move(writer.WorkingPath, writer.FinalPath);
                    var fileLength = new FileInfo(writer.FinalPath).Length;
                    var duration = TimeSpan.FromSeconds(
                        writer.Session.FrameCount / (double)Math.Max(1, writer.Session.FramesPerSecond));
                    var metadata = new ReplaySegmentMetadata(
                        Guid.NewGuid().ToString("N"),
                        sequence,
                        writer.Track,
                        writer.FinalPath,
                        segmentStartedAtUtc,
                        monotonicStart,
                        duration,
                        fileLength,
                        IncludesSystemAudio: includeAudio &&
                            audio.SourcesWithPayload.Contains(AudioCaptureSource.SystemAudio),
                        IncludesMicrophone: includeAudio &&
                            audio.SourcesWithPayload.Contains(AudioCaptureSource.Microphone),
                        IncludesWebcam: writer.IncludesWebcam &&
                            writer.WebcamFrameCount == writer.Session.FrameCount,
                        EncodedFrameCount: writer.Session.FrameCount,
                        WebcamFrameCount: writer.WebcamFrameCount,
                        PrivacyRedacted: privacySuppressedDuringSegment);
                    stagedTracks.Add(new StagedTrack(writer, metadata));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{writer.Track.DisplayName}: {ex.Message}");
                    break;
                }
            }

            if (stagedTracks.Count != writers.Count)
            {
                failures.Add(
                    "Replay skipped the synchronized capture set because a track did not finalize; " +
                    "all outputs from this sequence were discarded.");
                return new SegmentCaptureResult(0, failures);
            }

            var added = _coordinator.AddFinalizedSegments(
                stagedTracks.Select(staged => staged.Metadata).ToArray());
            if (!added.Accepted)
            {
                failures.Add(
                    $"Replay skipped the synchronized capture set because the buffer rejected it: " +
                    added.Message);
                return new SegmentCaptureResult(0, failures);
            }

            foreach (var staged in stagedTracks)
            {
                staged.Writer.Published = true;
            }

            if (!added.Retained)
            {
                failures.Add(
                    "The finalized synchronized capture set was immediately evicted because it " +
                    "exceeded the configured total storage cap.");
                return new SegmentCaptureResult(0, failures);
            }

            return new SegmentCaptureResult(stagedTracks.Count, failures);
        }
        finally
        {
            Interlocked.Exchange(ref _segmentCaptureActive, 0);
            if (!audioCapturesCompleted && streamingAudio is not null)
            {
                try
                {
                    await streamingAudio.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Cleanup must not replace the causal Replay failure.
                }
            }

            if (streamingAudio is not null)
            {
                await streamingAudio.DisposeAsync().ConfigureAwait(false);
            }

            foreach (var writer in writers)
            {
                writer.DisposeLastCapturedFrame();
                writer.Session.Dispose();
                TryDeleteOwnedFile(writer.WorkingPath);
                if (!writer.Published)
                {
                    TryDeleteOwnedFile(writer.FinalPath);
                }
            }

            foreach (var source in sources)
            {
                source.Dispose();
            }

        }
    }

    private ReplayTrackDescriptor BuildTrackDescriptor(
        IReplayFrameSource source,
        CapturedBitmap captured,
        int encodedWidth,
        int encodedHeight)
    {
        var bounds = new ReplayCaptureBounds(
            captured.Bounds.X,
            captured.Bounds.Y,
            captured.Bounds.Width,
            captured.Bounds.Height);
        return new ReplayTrackDescriptor(
            source.TrackId,
            source.DisplayName,
            source.Source with { Bounds = bounds },
            encodedWidth,
            encodedHeight,
            source.DpiScaleX,
            source.DpiScaleY);
    }

    private static bool HasSourceGeometryChanged(TrackWriter writer, CapturedBitmap captured)
    {
        var bounds = writer.Track.Source.Bounds;
        return captured.Bitmap.Width != writer.NativePixelWidth ||
            captured.Bitmap.Height != writer.NativePixelHeight ||
            bounds is not null &&
            (captured.Bounds.X != bounds.X ||
             captured.Bounds.Y != bounds.Y ||
             captured.Bounds.Width != bounds.Width ||
             captured.Bounds.Height != bounds.Height) ||
            Math.Abs(writer.Source.DpiScaleX - writer.Track.DpiScaleX) >= 0.001d ||
            Math.Abs(writer.Source.DpiScaleY - writer.Track.DpiScaleY) >= 0.001d;
    }

    private (string WorkingPath, string FinalPath) BuildSegmentPaths(long sequence, int trackIndex)
    {
        var runRoot = Path.Combine(_bufferRoot, _runId);
        Directory.CreateDirectory(runRoot);
        var stem = $"segment-{sequence:D12}-track-{trackIndex:D3}-{Guid.NewGuid():N}";
        return (
            Path.Combine(runRoot, stem + ".partial.mp4"),
            Path.Combine(runRoot, stem + ".mp4"));
    }

    private ReplayFrameWriteResult WriteFrame(
        IReplayVideoSegmentSession session,
        Bitmap capturedFrame,
        ReplayTrackDescriptor track,
        RecordingService.NativeWebcamOverlay? webcamOverlay,
        CancellationToken cancellationToken)
    {
        using var composited = webcamOverlay is null
            ? null
            : RecordingService.ComposeNativeWebcamOverlay(capturedFrame, webcamOverlay);
        var frame = composited ?? capturedFrame;
        var privacy = EvaluatePrivacy(track);
        UpdatePrivacyStatus(privacy);
        if (privacy.SuppressFrame)
        {
            using var blackout = new Bitmap(frame.Width, frame.Height);
            using (var graphics = Graphics.FromImage(blackout))
            {
                graphics.Clear(Color.Black);
            }

            using var resizedBlackout = ResizeEncodedFrame(
                blackout,
                track.PixelWidth,
                track.PixelHeight);
            session.WriteFrame(resizedBlackout, cancellationToken);
            return new ReplayFrameWriteResult(
                IncludesWebcam: false,
                PrivacySuppressed: true);
        }

        using var masked = privacy.MaskedDesktopBounds is { Count: > 0 }
            ? ApplyPrivacyMasks(frame, track, privacy.MaskedDesktopBounds)
            : null;
        using var resized = ResizeEncodedFrame(
            masked ?? frame,
            track.PixelWidth,
            track.PixelHeight);
        session.WriteFrame(resized, cancellationToken);
        return new ReplayFrameWriteResult(
            IncludesWebcam: webcamOverlay is not null,
            PrivacySuppressed: masked is not null);
    }

    private static Bitmap ApplyPrivacyMasks(
        Bitmap frame,
        ReplayTrackDescriptor track,
        IReadOnlyList<ReplayCaptureBounds> desktopMasks)
    {
        var source = track.Source.Bounds ?? new ReplayCaptureBounds(
            0,
            0,
            Math.Max(1, frame.Width),
            Math.Max(1, frame.Height));
        var masked = (Bitmap)frame.Clone();
        using var graphics = Graphics.FromImage(masked);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        foreach (var desktopMask in desktopMasks)
        {
            var left = Math.Max(source.X, desktopMask.X);
            var top = Math.Max(source.Y, desktopMask.Y);
            var right = Math.Min(source.X + source.Width, desktopMask.X + desktopMask.Width);
            var bottom = Math.Min(source.Y + source.Height, desktopMask.Y + desktopMask.Height);
            if (right <= left || bottom <= top)
            {
                continue;
            }

            var x = (int)Math.Floor((left - source.X) * frame.Width / (double)Math.Max(1, source.Width));
            var y = (int)Math.Floor((top - source.Y) * frame.Height / (double)Math.Max(1, source.Height));
            var maskRight = (int)Math.Ceiling((right - source.X) * frame.Width / (double)Math.Max(1, source.Width));
            var maskBottom = (int)Math.Ceiling((bottom - source.Y) * frame.Height / (double)Math.Max(1, source.Height));
            var rectangle = Rectangle.FromLTRB(
                Math.Clamp(x, 0, frame.Width),
                Math.Clamp(y, 0, frame.Height),
                Math.Clamp(maskRight, 0, frame.Width),
                Math.Clamp(maskBottom, 0, frame.Height));
            if (rectangle.Width > 0 && rectangle.Height > 0)
            {
                graphics.FillRectangle(Brushes.Black, rectangle);
            }
        }

        return masked;
    }

    internal static (int Width, int Height) ResolveEncodedFrameSize(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        if (targetWidth <= 0 && targetHeight <= 0)
        {
            return (MakeEven(sourceWidth), MakeEven(sourceHeight));
        }

        if (targetWidth > 0 && targetHeight > 0)
        {
            return (MakeEven(targetWidth), MakeEven(targetHeight));
        }

        if (targetWidth > 0)
        {
            var scale = targetWidth / (double)sourceWidth;
            return (MakeEven(targetWidth), MakeEven((int)Math.Round(sourceHeight * scale)));
        }

        var heightScale = targetHeight / (double)sourceHeight;
        return (MakeEven((int)Math.Round(sourceWidth * heightScale)), MakeEven(targetHeight));
    }

    private static Bitmap ResizeEncodedFrame(Bitmap source, int width, int height)
    {
        var resized = new Bitmap(
            Math.Max(2, width),
            Math.Max(2, height),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
        return resized;
    }

    private static int MakeEven(int value)
    {
        value = Math.Max(2, value);
        return value % 2 == 0 ? value : value - 1;
    }

    private RecordingService.NativeWebcamOverlaySource? CreateWebcamOverlaySource()
    {
        if (!_mediaProfile.EnableWebcamOverlay || _cameraOverlay is null)
        {
            return null;
        }

        return new RecordingService.NativeWebcamOverlaySource(
            _cameraOverlay,
            _mediaProfile.WebcamDeviceId,
            _mediaProfile.WebcamOverlayPosition,
            _mediaProfile.WebcamOverlayShape,
            _mediaProfile.MirrorWebcam,
            WebcamRefreshInterval);
    }

    private StreamingRecordingAudioSession? StartReplayAudioCapture(
        IReadOnlyList<TrackWriter> writers,
        CancellationToken cancellationToken)
    {
        if (_audioCapture is null ||
            (!_mediaProfile.IncludeMicrophone && !_mediaProfile.IncludeSystemAudio))
        {
            return null;
        }

        var requests = new List<StreamingAudioCaptureRequest>(2);
        if (_mediaProfile.IncludeMicrophone)
        {
            requests.Add(new StreamingAudioCaptureRequest(
                AudioCaptureSource.Microphone,
                _mediaProfile.MicrophoneDeviceId,
                new AudioCaptureProcessingSettings(
                    _mediaProfile.MicrophoneGain,
                    _mediaProfile.NoiseGateThresholdDb,
                    _mediaProfile.MicrophoneMuted)));
        }

        if (_mediaProfile.IncludeSystemAudio)
        {
            requests.Add(new StreamingAudioCaptureRequest(
                AudioCaptureSource.SystemAudio,
                _mediaProfile.SystemAudioDeviceId,
                new AudioCaptureProcessingSettings(
                    _mediaProfile.SystemAudioGain,
                    _mediaProfile.NoiseGateThresholdDb,
                    _mediaProfile.SystemAudioMuted)));
        }

        return StreamingRecordingAudioSession.Start(
            _audioCapture,
            requests,
            pcm =>
            {
                foreach (var writer in writers.Where(writer => writer.IsActive))
                {
                    writer.Session.WriteAudioPcm(pcm);
                }
            },
            cancellationToken);
    }

    private ReplayPrivacyDecision EvaluatePrivacy(ReplayTrackDescriptor track)
    {
        try
        {
            return _privacyGuard.EvaluateCapture(track);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ReplayPrivacyDecision.Suppress(
                $"Replay foreground privacy inspection failed ({ex.GetType().Name}); " +
                "frames are blacked out to preserve configured exclusions.");
        }
    }

    private void UpdatePrivacyStatus(ReplayPrivacyDecision privacy)
    {
        if (privacy.HasRedactions)
        {
            if (Interlocked.Exchange(ref _privacySuppressionActive, 1) == 0)
            {
                PublishStatus(string.IsNullOrWhiteSpace(privacy.Message)
                    ? "Replay privacy exclusion is active; excluded pixels are redacted."
                    : privacy.Message);
            }

            return;
        }

        if (Interlocked.Exchange(ref _privacySuppressionActive, 0) == 1)
        {
            PublishStatus("Replay privacy exclusion ended; live frames are buffering again.");
        }
    }

    private async Task StopWorkerCoreAsync()
    {
        _workerCancellation?.Cancel();
        _captureEnabled.Set();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _worker = null;
        _workerCancellation?.Dispose();
        _workerCancellation = null;
        _captureEnabled.Reset();
        Interlocked.Exchange(ref _privacySuppressionActive, 0);
    }

    private void TryDeleteOwnedFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var prefix = _bufferRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private void ResetTimelineAfterCatalogClear()
    {
        _hasTimelineOrigin = false;
        _runStartedTimestamp = 0;
        _nextSegmentSequence = 0;
        _runId = string.Empty;
    }

    private SegmentSaveBarrier? RequestActiveSegmentSaveBarrier()
    {
        var state = _coordinator.GetStatus().State;
        if (state is not (ReplayBufferState.Armed or ReplayBufferState.Paused) ||
            Volatile.Read(ref _segmentCaptureActive) == 0)
        {
            return null;
        }

        lock (_saveBarrierGate)
        {
            if (Volatile.Read(ref _segmentCaptureActive) == 0)
            {
                return null;
            }

            _pendingSaveBarrier ??= new SegmentSaveBarrier();
            Interlocked.Exchange(ref _segmentRotationRequested, 1);
            return _pendingSaveBarrier;
        }
    }

    private SegmentSaveBarrier? CompletePendingSaveBarrier(SegmentCaptureResult segment)
    {
        SegmentSaveBarrier? barrier;
        lock (_saveBarrierGate)
        {
            barrier = _pendingSaveBarrier;
            _pendingSaveBarrier = null;
            Interlocked.Exchange(ref _segmentRotationRequested, 0);
        }

        if (barrier is null)
        {
            return null;
        }

        var succeeded = segment.FinalizedCount > 0;
        var message = succeeded
            ? $"Finalized {segment.FinalizedCount} active synchronized track(s) through the save boundary."
            : segment.Failures.Count == 0
                ? "The active synchronized capture set did not produce a buffered segment."
                : string.Join(" ", segment.Failures.Take(2));
        barrier.Finalized.TrySetResult(new SegmentSaveBarrierResult(succeeded, message));
        return barrier;
    }

    private void PublishStatus(string message)
    {
        var handlers = StatusChanged;
        if (handlers is null)
        {
            return;
        }

        var args = new ReplayRecordingStatusChangedEventArgs(
            GetStatus(),
            message,
            _clock.UtcNow);
        foreach (EventHandler<ReplayRecordingStatusChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Status observers cannot interrupt capture or receipt finalization.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class TrackWriter(
        IReplayFrameSource source,
        ReplayTrackDescriptor track,
        IReplayVideoSegmentSession session,
        string workingPath,
        string finalPath,
        int nativePixelWidth,
        int nativePixelHeight)
    {
        public IReplayFrameSource Source { get; } = source;
        public ReplayTrackDescriptor Track { get; } = track;
        public IReplayVideoSegmentSession Session { get; } = session;
        public string WorkingPath { get; } = workingPath;
        public string FinalPath { get; } = finalPath;
        public int NativePixelWidth { get; } = nativePixelWidth;
        public int NativePixelHeight { get; } = nativePixelHeight;
        public bool IsActive { get; set; } = true;
        public bool Published { get; set; }
        public bool IncludesWebcam { get; set; }
        public int WebcamFrameCount { get; set; }
        public Bitmap? LastCapturedFrame { get; private set; }

        public void RememberLastCapturedFrame(Bitmap frame)
        {
            var replacement = (Bitmap)frame.Clone();
            var previous = LastCapturedFrame;
            LastCapturedFrame = replacement;
            previous?.Dispose();
        }

        public void DisposeLastCapturedFrame()
        {
            LastCapturedFrame?.Dispose();
            LastCapturedFrame = null;
        }
    }

    private sealed record ReplayFrameWriteResult(
        bool IncludesWebcam,
        bool PrivacySuppressed);

    private sealed record StagedTrack(
        TrackWriter Writer,
        ReplaySegmentMetadata Metadata);

    private sealed record SegmentSaveBarrierResult(bool Succeeded, string Message);

    private sealed class SegmentSaveBarrier
    {
        public TaskCompletionSource<SegmentSaveBarrierResult> Finalized { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProducerMayContinue { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ReplayMediaProfile(
        int TargetWidth,
        int TargetHeight,
        bool IncludeMicrophone,
        bool IncludeSystemAudio,
        string MicrophoneDeviceId,
        string SystemAudioDeviceId,
        double MicrophoneGain,
        double SystemAudioGain,
        double NoiseGateThresholdDb,
        bool MicrophoneMuted,
        bool SystemAudioMuted,
        bool EnableWebcamOverlay,
        string WebcamDeviceId,
        string WebcamOverlayPosition,
        string WebcamOverlayShape,
        bool MirrorWebcam)
    {
        public static ReplayMediaProfile Create(RecordingSettings? settings)
        {
            settings ??= new RecordingSettings
            {
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            };
            var normalized = RecordingSettingsNormalizer.Normalize(settings);
            return new ReplayMediaProfile(
                normalized.TargetWidth,
                normalized.TargetHeight,
                normalized.IncludeMicrophone,
                normalized.IncludeSystemAudio,
                settings.MicrophoneDeviceId,
                settings.SystemAudioDeviceId,
                normalized.MicrophoneGain,
                normalized.SystemAudioGain,
                normalized.NoiseGateThresholdDb,
                normalized.MicrophoneMuted,
                normalized.SystemAudioMuted,
                normalized.EnableWebcamOverlay,
                settings.WebcamDeviceId,
                settings.WebcamOverlayPosition,
                settings.WebcamOverlayShape,
                settings.MirrorWebcam);
        }
    }

    private sealed record SegmentCaptureResult(int FinalizedCount, IReadOnlyList<string> Failures);

    private sealed class AsyncManualResetEvent
    {
        private TaskCompletionSource<bool> _source = CreateSource();

        public Task WaitAsync(CancellationToken cancellationToken) =>
            _source.Task.WaitAsync(cancellationToken);

        public void Set() => _source.TrySetResult(true);

        public void Reset()
        {
            while (true)
            {
                var current = _source;
                if (!current.Task.IsCompleted)
                {
                    return;
                }

                var replacement = CreateSource();
                if (ReferenceEquals(Interlocked.CompareExchange(ref _source, replacement, current), current))
                {
                    return;
                }
            }
        }

        private static TaskCompletionSource<bool> CreateSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

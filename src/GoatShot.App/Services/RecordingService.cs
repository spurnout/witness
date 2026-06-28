using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AnimatedGif;
using GoatShot.App.Models;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class RecordingService : IDisposable
{
    private const int LegacyGifFrameDelayMs = 250;
    private const int MaxFrameWidth = 1280;
    private const int MaxMp4Frames = 1_800;
    private static readonly TimeSpan NativeWebcamRefreshInterval = TimeSpan.FromMilliseconds(750);

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly ScreenshotService _screenshots;
    private readonly WorkspaceStore _workspaceStore;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ICameraOverlayService _cameraOverlay;
    private readonly object _gate = new();
    private RecordingSession? _session;

    public RecordingService(
        AppPaths paths,
        AppSettings settings,
        ScreenshotService screenshots,
        WorkspaceStore workspaceStore,
        IAudioCaptureService audioCapture,
        ICameraOverlayService cameraOverlay)
    {
        _paths = paths;
        _settings = settings;
        _screenshots = screenshots;
        _workspaceStore = workspaceStore;
        _audioCapture = audioCapture;
        _cameraOverlay = cameraOverlay;
    }

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _session is not null;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _session?.IsPaused ?? false;
            }
        }
    }

    public Task<RecordingResult> ToggleGifRecordingAsync(CancellationToken cancellationToken = default)
    {
        return ToggleGifRecordingAsync(
            RecordingCaptureTarget.ActiveMonitor(),
            _settings.Recording,
            animationOptions: null,
            cancellationToken);
    }

    public Task<RecordingResult> ToggleGifRecordingAsync(
        RecordingCaptureTarget target,
        RecordingSettings recordingSettings,
        AnimationExportOptions? animationOptions = null,
        CancellationToken cancellationToken = default)
    {
        RecordingSession? sessionToStop = null;
        lock (_gate)
        {
            if (_session is null)
            {
                _session = StartSession(null, target, recordingSettings, animationOptions);
                return Task.FromResult(new RecordingResult
                {
                    IsRecording = true,
                    IsPaused = false,
                    Succeeded = true,
                    OutputPath = _session.OutputPath,
                    Message = $"Recording {_session.Target.DisplayName} to GIF. Press Pause / resume to pause, or Video again to finish."
                });
            }

            sessionToStop = _session;
            _session = null;
            sessionToStop.RequestStop();
        }

        return StopSessionAsync(sessionToStop, addToWorkspace: true, cancellationToken);
    }

    public RecordingResult TogglePauseRecording()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                return new RecordingResult
                {
                    Succeeded = false,
                    Message = "No active GIF recording to pause or resume."
                };
            }

            var paused = _session.TogglePaused();
            return new RecordingResult
            {
                IsRecording = true,
                IsPaused = paused,
                Succeeded = true,
                OutputPath = _session.OutputPath,
                Message = paused
                    ? "GIF recording paused. Press Pause / resume recording to continue."
                    : "GIF recording resumed."
            };
        }
    }

    public async Task<RecordingResult> RecordActiveMonitorGifAsync(
        TimeSpan duration,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        return await RecordGifAsync(
            duration,
            outputPath,
            RecordingCaptureTarget.ActiveMonitor(),
            addToWorkspace,
            cancellationToken);
    }

    public async Task<RecordingResult> RecordGifAsync(
        TimeSpan duration,
        string? outputPath,
        RecordingCaptureTarget target,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        return await RecordGifAsync(
            duration,
            outputPath,
            target,
            _settings.Recording,
            addToWorkspace,
            null,
            cancellationToken);
    }

    public async Task<RecordingResult> RecordGifAsync(
        TimeSpan duration,
        string? outputPath,
        RecordingCaptureTarget target,
        RecordingSettings recordingSettings,
        bool addToWorkspace = false,
        AnimationExportOptions? animationOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = "Recording duration must be greater than zero."
            };
        }

        RecordingSession session;
        lock (_gate)
        {
            if (_session is not null)
            {
                return new RecordingResult
                {
                    IsRecording = true,
                    Succeeded = false,
                    Message = "A desktop recording is already in progress."
                };
            }

            session = StartSession(outputPath, target, recordingSettings, animationOptions);
            _session = session;
        }

        try
        {
            var durationTask = Task.Delay(duration, cancellationToken);
            var completed = await Task.WhenAny(durationTask, session.CaptureTask);
            if (ReferenceEquals(completed, durationTask))
            {
                await durationTask;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    session.RequestStop();
                }
            }
        }

        return await StopSessionAsync(session, addToWorkspace, cancellationToken);
    }

    public async Task<RecordingResult> RecordActiveMonitorMp4Async(
        TimeSpan duration,
        string? outputPath = null,
        int fps = 10,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        var settings = CopyRecordingSettings(_settings.Recording);
        settings.FramesPerSecond = fps;
        return await RecordActiveMonitorMp4Async(duration, outputPath, settings, addToWorkspace, cancellationToken);
    }

    public async Task<RecordingResult> RecordActiveMonitorMp4Async(
        TimeSpan duration,
        string? outputPath,
        RecordingSettings recordingSettings,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        return await RecordMp4Async(
            duration,
            outputPath,
            recordingSettings,
            RecordingCaptureTarget.ActiveMonitor(),
            addToWorkspace,
            cancellationToken);
    }

    public async Task<RecordingResult> RecordMp4Async(
        TimeSpan duration,
        string? outputPath,
        RecordingSettings recordingSettings,
        RecordingCaptureTarget target,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = "Recording duration must be greater than zero."
            };
        }

        var capabilities = RecordingCapabilityProbe.Probe();
        var enginePlan = RecordingEnginePlanner.BuildPlan(
            recordingSettings,
            capabilities,
            productionEngineImplemented: NativeMediaFoundationMp4Encoder.ProductionEngineImplemented,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: NativeMediaFoundationMp4Encoder.ProductionAudioMixingImplemented,
            productionWebcamCompositorImplemented: NativeMediaFoundationMp4Encoder.ProductionWebcamCompositorImplemented);
        var ffmpeg = capabilities.Ffmpeg.Path;
        var canUseFfmpegFallback = enginePlan.FallbackAvailable && !string.IsNullOrWhiteSpace(ffmpeg);
        if (!enginePlan.CanRecord ||
            (enginePlan.Choice == RecordingEngineChoice.FfmpegFallback && !canUseFfmpegFallback))
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = $"MP4 recording is unavailable. {enginePlan.Summary}"
            };
        }

        target = target.Normalize();
        var normalized = RecordingSettingsNormalizer.Normalize(recordingSettings);
        var maxDuration = TimeSpan.FromSeconds(MaxMp4Frames / (double)normalized.FramesPerSecond);
        if (duration > maxDuration)
        {
            duration = maxDuration;
        }

        Directory.CreateDirectory(_paths.VideosRoot);
        outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_paths.VideosRoot, $"goatshot-recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var tempRoot = Path.Combine(_paths.TempRoot, $"mp4-recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var interval = TimeSpan.FromMilliseconds(1000d / normalized.FramesPerSecond);
        var encodedFrameCount = 0;
        var capturedFrameCount = 0;
        var expectedFrameCount = ExpectedMp4FrameCount(duration, normalized.FramesPerSecond);
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        string? lastFramePath = null;
        WindowsGraphicsCaptureFrameSource? wgcFrameSource = null;
        var frameSourceSummary = $"GDI screenshot frame capture from {target.DisplayName}";
        string? frameSourceFallbackNote = null;
        using var audioCaptureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pendingAudioCaptures = StartMp4AudioCaptures(recordingSettings, normalized, tempRoot, duration, audioCaptureCts.Token);
        NativeWebcamOverlaySource? nativeWebcamOverlaySource = null;
        var nativeWebcamMessage = normalized.EnableWebcamOverlay
            ? "requested but native camera frame capture was not attempted."
            : "not requested.";

        try
        {
            if (ShouldUseWindowsGraphicsCaptureFrameSource(recordingSettings, capabilities, target))
            {
                try
                {
                    wgcFrameSource = WindowsGraphicsCaptureFrameSource.Start(target, cancellationToken);
                    frameSourceSummary = $"Windows.Graphics.Capture frame capture from {wgcFrameSource.SourceName}";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    frameSourceFallbackNote = FormatFrameSourceFallbackNote(ex);
                }
            }

            if (normalized.EnableWebcamOverlay && enginePlan.ProductionEngineImplemented)
            {
                var nativeWebcam = await PrepareNativeWebcamOverlayAsync(
                    recordingSettings,
                    cancellationToken);
                nativeWebcamOverlaySource = nativeWebcam.Input;
                nativeWebcamMessage = nativeWebcam.Message;
            }

            while (stopwatch.Elapsed < duration && encodedFrameCount < expectedFrameCount)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var captureTimeout = encodedFrameCount == 0
                        ? TimeSpan.FromSeconds(1)
                        : interval;
                    using var captured = await CaptureMp4FrameAsync(
                        wgcFrameSource,
                        target,
                        captureTimeout,
                        ex =>
                        {
                            frameSourceFallbackNote ??= FormatFrameSourceFallbackNote(ex);
                            wgcFrameSource?.Dispose();
                            wgcFrameSource = null;
                        },
                        cancellationToken);
                    using var decorated = DecorateRecordingFrame(
                        captured,
                        _settings.IncludeCursor,
                        normalized,
                        DateTimeOffset.Now - startedAt);
                    if (nativeWebcamOverlaySource is not null)
                    {
                        await nativeWebcamOverlaySource.RefreshIfDueAsync(DateTimeOffset.Now, cancellationToken);
                    }

                    var nativeWebcamOverlay = nativeWebcamOverlaySource?.Current;
                    using var composited = nativeWebcamOverlay is null
                        ? CloneBitmapArgb(decorated)
                        : ComposeNativeWebcamOverlay(decorated, nativeWebcamOverlay);
                    using var frame = ResizeFrame(composited, normalized);
                    lastFramePath = SaveNextMp4Frame(frame, tempRoot, ref encodedFrameCount);
                    capturedFrameCount++;

                    var pacedFrameCount = DesiredPacedMp4FrameCount(
                        stopwatch.Elapsed,
                        duration,
                        normalized.FramesPerSecond,
                        encodedFrameCount);
                    DuplicateLastMp4FrameUntil(
                        lastFramePath,
                        tempRoot,
                        Math.Min(pacedFrameCount, expectedFrameCount),
                        ref encodedFrameCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new RecordingResult
                    {
                        Succeeded = false,
                        Message = $"MP4 frame capture/composition failed after {capturedFrameCount} captured frame(s), {encodedFrameCount} encoded frame(s): {ex.GetType().Name}: {ex.Message}"
                    };
                }

                var nextFrameDue = TimeSpan.FromSeconds(encodedFrameCount / (double)normalized.FramesPerSecond);
                var delay = nextFrameDue - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            if (encodedFrameCount == 0 || lastFramePath is null)
            {
                return new RecordingResult
                {
                    Succeeded = false,
                    Message = "MP4 recording stopped before any frames were captured."
                };
            }

            DuplicateLastMp4FrameUntil(
                lastFramePath,
                tempRoot,
                expectedFrameCount,
                ref encodedFrameCount);

            var audioCaptureSummary = await CompleteMp4AudioCapturesAsync(pendingAudioCaptures, audioCaptureCts);
            var webcamSummary = new Mp4WebcamCaptureSummary(null, nativeWebcamOverlaySource?.Summary ?? nativeWebcamMessage);
            RecordingResult encode;
            var nativeAudioInputs = audioCaptureSummary.Inputs
                .Select(input => new NativeMediaFoundationMp4Encoder.NativeMp4AudioInput(input.Source, input.Path))
                .ToList();
            var usedProductionEncoder = NativeMediaFoundationMp4Encoder.CanEncodeProduction(
                enginePlan,
                normalized,
                nativeAudioInputs,
                out var productionEligibility);
            if (usedProductionEncoder && normalized.EnableWebcamOverlay && nativeWebcamOverlaySource is null && canUseFfmpegFallback)
            {
                usedProductionEncoder = false;
                productionEligibility = nativeWebcamMessage;
            }
            if (usedProductionEncoder)
            {
                encode = await NativeMediaFoundationMp4Encoder.EncodePngFramesAsync(
                    tempRoot,
                    normalized,
                    enginePlan.ProductionEncoder,
                    outputPath,
                    nativeAudioInputs,
                    cancellationToken);
                if (!encode.Succeeded && canUseFfmpegFallback)
                {
                    var productionFailure = encode.Message;
                    webcamSummary = await PrepareMp4WebcamInputAsync(
                        ffmpeg!,
                        recordingSettings,
                        normalized,
                        cancellationToken);
                    encode = await EncodeMp4Async(
                        ffmpeg!,
                        tempRoot,
                        normalized,
                        enginePlan.FallbackVideoEncoder,
                        outputPath,
                        duration,
                        audioCaptureSummary.Inputs,
                        webcamSummary.Input,
                        cancellationToken);
                    if (encode.Succeeded)
                    {
                        encode.Message = $"{encode.Message} Retried after native production encode failed: {productionFailure}";
                        usedProductionEncoder = false;
                    }
                }
            }
            else
            {
                if (!canUseFfmpegFallback)
                {
                    return new RecordingResult
                    {
                        Succeeded = false,
                        Message = $"MP4 recording is unavailable. Production path was not usable ({productionEligibility}) and no FFmpeg fallback is available. {enginePlan.Summary}"
                    };
                }

                webcamSummary = await PrepareMp4WebcamInputAsync(
                    ffmpeg!,
                    recordingSettings,
                    normalized,
                    cancellationToken);
                encode = await EncodeMp4Async(
                    ffmpeg!,
                    tempRoot,
                    normalized,
                    enginePlan.FallbackVideoEncoder,
                    outputPath,
                    duration,
                    audioCaptureSummary.Inputs,
                    webcamSummary.Input,
                    cancellationToken);
            }

            if (!encode.Succeeded && webcamSummary.Input is not null && canUseFfmpegFallback)
            {
                var failedWithWebcam = encode.Message;
                var retry = await EncodeMp4Async(
                    ffmpeg!,
                    tempRoot,
                    normalized,
                    enginePlan.FallbackVideoEncoder,
                    outputPath,
                    duration,
                    audioCaptureSummary.Inputs,
                    webcamInput: null,
                    cancellationToken);
                if (retry.Succeeded)
                {
                    webcamSummary = webcamSummary with
                    {
                        Message = $"{webcamSummary.Message} Webcam overlay failed during FFmpeg encode and was omitted on retry ({failedWithWebcam})."
                    };
                    encode = retry;
                }
            }

            if (!encode.Succeeded)
            {
                return encode;
            }

            CaptureItem? item = null;
            if (addToWorkspace)
            {
                item = await _workspaceStore.AddImageFileAsync(
                    outputPath,
                    CaptureKind.RecordingMp4,
                    $"MP4 recording encoded through {(usedProductionEncoder ? "native Media Foundation production video path" : "FFmpeg fallback")} for {target.DisplayName}. Encoder: {encode.Message} Frame source: {frameSourceSummary}.{frameSourceFallbackNote ?? string.Empty} Audio: {audioCaptureSummary.Message} Webcam: {webcamSummary.Message} Engine plan: {enginePlan.Summary}");
            }

            var messageSuffix = usedProductionEncoder
                ? nativeAudioInputs.Count > 0
                    ? nativeWebcamOverlaySource is not null
                        ? "Native Media Foundation production recording encoded screen frames with continuously refreshed precomposited webcam overlay and muxed captured audio as AAC."
                        : "Native Media Foundation production recording encoded screen frames and muxed captured audio as AAC."
                    : nativeWebcamOverlaySource is not null
                        ? "Native Media Foundation production recording encoded screen frames with continuously refreshed precomposited webcam overlay."
                        : "Native Media Foundation production recording encoded video-only screen frames."
                : normalized.FallbackLimitSummary;
            var framePacingMessage = capturedFrameCount == encodedFrameCount
                ? $"Captured/encoded frames: {encodedFrameCount}."
                : $"Captured frames: {capturedFrameCount}; encoded paced frames: {encodedFrameCount}.";
            var frameSourceMessage = $"Frame source: {frameSourceSummary}.{frameSourceFallbackNote ?? string.Empty} {framePacingMessage}";
            var encoderMessage = $"Video encoder: {encode.Message}";
            var audioMessage = $"Audio: {audioCaptureSummary.Message}";
            var webcamMessage = $"Webcam: {webcamSummary.Message}";
            return new RecordingResult
            {
                Succeeded = true,
                OutputPath = outputPath,
                Item = item,
                Message = item is null
                    ? $"MP4 recording saved: {outputPath}. Target: {target.DisplayName}. {encoderMessage} {frameSourceMessage} {audioMessage} {webcamMessage} {messageSuffix} Engine plan: {enginePlan.Summary}"
                    : $"MP4 recording saved and indexed: {item.FileName}. Target: {target.DisplayName}. {encoderMessage} {frameSourceMessage} {audioMessage} {webcamMessage} {messageSuffix} Engine plan: {enginePlan.Summary}"
            };
        }
        finally
        {
            audioCaptureCts.Cancel();
            await SwallowPendingAudioCapturesAsync(pendingAudioCaptures);
            nativeWebcamOverlaySource?.Dispose();
            wgcFrameSource?.Dispose();
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Temp frames are best-effort cleanup.
            }
        }
    }

    private IReadOnlyList<PendingAudioCapture> StartMp4AudioCaptures(
        RecordingSettings recordingSettings,
        NormalizedRecordingSettings normalized,
        string tempRoot,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var captures = new List<PendingAudioCapture>();
        if (normalized.IncludeMicrophone)
        {
            var output = Path.Combine(tempRoot, "microphone.wav");
            captures.Add(new PendingAudioCapture(
                AudioCaptureSource.Microphone,
                output,
                duration,
                DateTimeOffset.Now,
                _audioCapture.CaptureWavAsync(
                    new AudioCaptureRequest(
                        AudioCaptureSource.Microphone,
                        duration,
                        output,
                        recordingSettings.MicrophoneDeviceId,
                        new AudioCaptureProcessingSettings(
                            recordingSettings.MicrophoneGain,
                            recordingSettings.NoiseGateThresholdDb,
                            recordingSettings.MicrophoneMuted)),
                    cancellationToken)));
        }

        if (normalized.IncludeSystemAudio)
        {
            var output = Path.Combine(tempRoot, "system-audio.wav");
            captures.Add(new PendingAudioCapture(
                AudioCaptureSource.SystemAudio,
                output,
                duration,
                DateTimeOffset.Now,
                _audioCapture.CaptureWavAsync(
                    new AudioCaptureRequest(
                        AudioCaptureSource.SystemAudio,
                        duration,
                        output,
                        recordingSettings.SystemAudioDeviceId,
                        new AudioCaptureProcessingSettings(
                            recordingSettings.SystemAudioGain,
                            recordingSettings.NoiseGateThresholdDb,
                            recordingSettings.SystemAudioMuted)),
                    cancellationToken)));
        }

        return captures;
    }

    private static async Task<Mp4AudioCaptureSummary> CompleteMp4AudioCapturesAsync(
        IReadOnlyList<PendingAudioCapture> pendingCaptures,
        CancellationTokenSource audioCaptureCts)
    {
        if (pendingCaptures.Count == 0)
        {
            return new Mp4AudioCaptureSummary([], "not requested.");
        }

        audioCaptureCts.CancelAfter(TimeSpan.FromSeconds(2));
        var inputs = new List<Mp4AudioInput>();
        var notes = new List<string>();
        foreach (var pending in pendingCaptures)
        {
            try
            {
                var result = await pending.Task;
                if (result.Succeeded && result.BytesWritten > 0 && File.Exists(pending.OutputPath))
                {
                    inputs.Add(new Mp4AudioInput(pending.Source, pending.OutputPath));
                    notes.Add(FormatAudioCaptureProof(pending, result, DateTimeOffset.Now));
                }
                else if (result.Succeeded)
                {
                    notes.Add($"{FormatAudioCaptureProof(pending, result, DateTimeOffset.Now)} No payload bytes were produced, so this track was omitted.");
                }
                else
                {
                    notes.Add($"{FormatAudioSource(pending.Source)} unavailable ({result.Message}); requested={FormatSeconds(pending.RequestedDuration.TotalSeconds)}, started={pending.StartedAt:O}");
                }
            }
            catch (OperationCanceledException)
            {
                notes.Add($"{FormatAudioSource(pending.Source)} capture timed out before muxing");
            }
            catch (Exception ex)
            {
                notes.Add($"{FormatAudioSource(pending.Source)} capture failed ({ex.GetType().Name}: {ex.Message})");
            }
        }

        var muxText = inputs.Count switch
        {
            0 => "No requested audio input produced payload bytes for MP4 muxing.",
            1 => $"{FormatAudioSource(inputs[0].Source)} will be muxed into the MP4.",
            _ => "Microphone and system-audio captures will be mixed into the MP4."
        };
        return new Mp4AudioCaptureSummary(inputs, $"{muxText} {string.Join(" ", notes)}");
    }

    private static string FormatAudioCaptureProof(
        PendingAudioCapture pending,
        AudioCaptureResult result,
        DateTimeOffset completedAt)
    {
        var elapsed = completedAt - pending.StartedAt;
        var durationDelta = Math.Abs(result.Duration.TotalSeconds - pending.RequestedDuration.TotalSeconds);
        var deltaText = $"duration-delta={FormatSeconds(durationDelta)}";
        return $"{FormatAudioSource(pending.Source)} captured bytes={result.BytesWritten}; requested={FormatSeconds(pending.RequestedDuration.TotalSeconds)}; wav-duration={FormatSeconds(result.Duration.TotalSeconds)}; elapsed={FormatSeconds(elapsed.TotalSeconds)}; {deltaText}; started={pending.StartedAt:O}; completed={completedAt:O}.";
    }

    private static string FormatSeconds(double seconds)
    {
        return $"{Math.Max(0d, seconds):0.###}s";
    }

    private static async Task SwallowPendingAudioCapturesAsync(IReadOnlyList<PendingAudioCapture> pendingCaptures)
    {
        foreach (var pending in pendingCaptures)
        {
            try
            {
                await pending.Task;
            }
            catch
            {
                // Audio capture failures are reported before encode when possible; cleanup should not mask recording errors.
            }
        }
    }

    private async Task<NativeWebcamOverlayCaptureSummary> PrepareNativeWebcamOverlayAsync(
        RecordingSettings recordingSettings,
        CancellationToken cancellationToken)
    {
        var source = new NativeWebcamOverlaySource(
            _cameraOverlay,
            recordingSettings.WebcamDeviceId,
            recordingSettings.WebcamOverlayPosition,
            recordingSettings.WebcamOverlayShape,
            recordingSettings.MirrorWebcam,
            NativeWebcamRefreshInterval);
        var initial = await source.RefreshNowAsync(DateTimeOffset.Now, cancellationToken);
        if (!initial)
        {
            var message = source.LastMessage;
            source.Dispose();
            return new NativeWebcamOverlayCaptureSummary(
                null,
                $"requested but native camera frame capture failed ({message}); FFmpeg fallback can retry when available.");
        }

        return new NativeWebcamOverlayCaptureSummary(
            source,
            source.Summary);
    }

    internal static Bitmap ComposeNativeWebcamOverlay(Bitmap frame, NativeWebcamOverlay overlay)
    {
        var output = CloneBitmapArgb(frame);
        var shape = NormalizeWebcamShape(overlay.Shape);
        using var prepared = PrepareNativeWebcamBitmap(overlay.Frame, overlay.Mirror);
        var target = ResolveNativeWebcamOverlayBounds(
            output.Width,
            output.Height,
            prepared.Width,
            prepared.Height,
            shape,
            overlay.Position);

        using var graphics = Graphics.FromImage(output);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        if (shape.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(target);
            var state = graphics.Save();
            graphics.SetClip(path);
            graphics.DrawImage(prepared, target);
            graphics.Restore(state);
            using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 8);
            using var border = new Pen(Color.FromArgb(245, 48, 230, 195), 4);
            graphics.DrawEllipse(shadow, target);
            graphics.DrawEllipse(border, target);
        }
        else
        {
            graphics.DrawImage(prepared, target);
            using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 8);
            using var border = new Pen(Color.FromArgb(245, 48, 230, 195), 4);
            graphics.DrawRectangle(shadow, target);
            graphics.DrawRectangle(border, target);
        }

        return output;
    }

    internal static Rectangle ResolveNativeWebcamOverlayBounds(
        int frameWidth,
        int frameHeight,
        int sourceWidth,
        int sourceHeight,
        string? shape,
        string? position)
    {
        frameWidth = Math.Max(1, frameWidth);
        frameHeight = Math.Max(1, frameHeight);
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        var margin = Math.Clamp(Math.Min(frameWidth, frameHeight) / 42, 12, 24);
        var normalizedShape = NormalizeWebcamShape(shape);
        int width;
        int height;
        if (normalizedShape.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            width = height = Math.Clamp(Math.Min(frameWidth, frameHeight) / 5, 96, 220);
        }
        else
        {
            width = Math.Clamp(frameWidth / 5, 140, 280);
            height = Math.Clamp((int)Math.Round(width * (sourceHeight / (double)sourceWidth)), 80, Math.Max(80, frameHeight / 3));
        }

        width = Math.Min(width, frameWidth);
        height = Math.Min(height, frameHeight);
        var normalizedPosition = NormalizeWebcamPosition(position);
        var x = normalizedPosition is "TopLeft" or "BottomLeft"
            ? margin
            : frameWidth - width - margin;
        var y = normalizedPosition is "TopLeft" or "TopRight"
            ? margin
            : frameHeight - height - margin;
        return new Rectangle(
            Math.Clamp(x, 0, Math.Max(0, frameWidth - width)),
            Math.Clamp(y, 0, Math.Max(0, frameHeight - height)),
            width,
            height);
    }

    private static Bitmap PrepareNativeWebcamBitmap(Bitmap source, bool mirror)
    {
        var prepared = CloneBitmapArgb(source);
        if (mirror)
        {
            prepared.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }

        return prepared;
    }

    private static Bitmap CloneBitmapArgb(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        clone.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var graphics = Graphics.FromImage(clone);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return clone;
    }

    internal static int ExpectedMp4FrameCount(TimeSpan duration, int framesPerSecond)
    {
        var fps = Math.Max(1, framesPerSecond);
        var frames = (int)Math.Ceiling(Math.Max(0d, duration.TotalSeconds) * fps);
        return Math.Clamp(frames, 1, MaxMp4Frames);
    }

    internal static int DesiredPacedMp4FrameCount(
        TimeSpan elapsed,
        TimeSpan duration,
        int framesPerSecond,
        int currentFrameCount)
    {
        var fps = Math.Max(1, framesPerSecond);
        var boundedElapsedSeconds = Math.Clamp(elapsed.TotalSeconds, 0d, Math.Max(0d, duration.TotalSeconds));
        var desired = (int)Math.Ceiling(boundedElapsedSeconds * fps);
        desired = Math.Max(currentFrameCount, desired);
        return Math.Clamp(desired, 0, ExpectedMp4FrameCount(duration, fps));
    }

    private static string SaveNextMp4Frame(Bitmap frame, string tempRoot, ref int frameCount)
    {
        frameCount++;
        var framePath = Path.Combine(tempRoot, $"frame{frameCount:000000}.png");
        frame.Save(framePath, ImageFormat.Png);
        return framePath;
    }

    private static void DuplicateLastMp4FrameUntil(
        string lastFramePath,
        string tempRoot,
        int desiredFrameCount,
        ref int frameCount)
    {
        while (frameCount < desiredFrameCount && frameCount < MaxMp4Frames)
        {
            frameCount++;
            File.Copy(lastFramePath, Path.Combine(tempRoot, $"frame{frameCount:000000}.png"), overwrite: true);
        }
    }

    private async Task<Mp4WebcamCaptureSummary> PrepareMp4WebcamInputAsync(
        string ffmpeg,
        RecordingSettings recordingSettings,
        NormalizedRecordingSettings normalized,
        CancellationToken cancellationToken)
    {
        if (!normalized.EnableWebcamOverlay)
        {
            return new Mp4WebcamCaptureSummary(null, "not requested.");
        }

        IReadOnlyList<CameraOverlayDevice> cameras;
        try
        {
            cameras = await _cameraOverlay.ListDevicesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Mp4WebcamCaptureSummary(
                null,
                $"requested but camera discovery failed ({ex.GetType().Name}: {ex.Message}).");
        }

        if (cameras.Count == 0)
        {
            return new Mp4WebcamCaptureSummary(null, "requested but no Windows camera devices were found.");
        }

        var requestedId = recordingSettings.WebcamDeviceId;
        var candidates = SelectWebcamCandidates(cameras, requestedId);
        if (candidates.Count == 0)
        {
            return new Mp4WebcamCaptureSummary(
                null,
                $"requested but selected camera is unavailable ({requestedId}).");
        }

        var dshowNames = await ListFfmpegDirectShowVideoDevicesAsync(ffmpeg, cancellationToken);
        var notes = new List<string>();
        foreach (var camera in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dshowName = ResolveFfmpegCameraDeviceName(camera, dshowNames);
            var probe = await ProbeFfmpegCameraAsync(ffmpeg, dshowName, cancellationToken);
            if (probe.Succeeded)
            {
                var input = new Mp4WebcamInput(
                    dshowName,
                    camera.DisplayName,
                    recordingSettings.WebcamOverlayPosition,
                    recordingSettings.WebcamOverlayShape,
                    recordingSettings.MirrorWebcam);
                var mapping = dshowName.Equals(camera.DisplayName, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $" via FFmpeg DirectShow device \"{dshowName}\"";
                var shape = NormalizeWebcamShape(recordingSettings.WebcamOverlayShape);
                return new Mp4WebcamCaptureSummary(
                    input,
                    $"overlaying {camera.DisplayName}{mapping} at {NormalizeWebcamPosition(recordingSettings.WebcamOverlayPosition)} as {shape}, mirror={(recordingSettings.MirrorWebcam ? "on" : "off")}.");
            }

            notes.Add($"{camera.DisplayName}: {probe.Message}");
            if (!string.IsNullOrWhiteSpace(requestedId))
            {
                break;
            }
        }

        return new Mp4WebcamCaptureSummary(
            null,
            $"requested but no probed camera produced frames, so screen recording continued without webcam. {string.Join(" ", notes)}");
    }

    private static IReadOnlyList<CameraOverlayDevice> SelectWebcamCandidates(
        IReadOnlyList<CameraOverlayDevice> cameras,
        string? requestedId)
    {
        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            return cameras
                .Where(camera =>
                    camera.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
                    camera.DisplayName.Equals(requestedId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return cameras
            .OrderByDescending(camera => camera.IsDefault)
            .ThenBy(camera => camera.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> ListFfmpegDirectShowVideoDevicesAsync(
        string ffmpeg,
        CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-hide_banner");
            start.ArgumentList.Add("-list_devices");
            start.ArgumentList.Add("true");
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add("dshow");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add("dummy");

            using var process = Process.Start(start);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ParseFfmpegDirectShowVideoDevices(stderr + Environment.NewLine + stdout);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    internal static IReadOnlyList<string> ParseFfmpegDirectShowVideoDevices(string text)
    {
        var names = new List<string>();
        using var reader = new StringReader(text ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.Contains("(video)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firstQuote = line.IndexOf('"');
            if (firstQuote < 0)
            {
                continue;
            }

            var secondQuote = line.IndexOf('"', firstQuote + 1);
            if (secondQuote <= firstQuote + 1)
            {
                continue;
            }

            var name = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1).Trim();
            if (!string.IsNullOrWhiteSpace(name) &&
                names.All(existing => !existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(name);
            }
        }

        return names;
    }

    internal static string ResolveFfmpegCameraDeviceName(
        CameraOverlayDevice camera,
        IReadOnlyList<string> dshowVideoDeviceNames)
    {
        var exact = dshowVideoDeviceNames.FirstOrDefault(name =>
            name.Equals(camera.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var normalizedCamera = NormalizeDeviceName(camera.DisplayName);
        var fuzzy = dshowVideoDeviceNames.FirstOrDefault(name =>
        {
            var normalizedName = NormalizeDeviceName(name);
            return normalizedName.Contains(normalizedCamera, StringComparison.OrdinalIgnoreCase) ||
                normalizedCamera.Contains(normalizedName, StringComparison.OrdinalIgnoreCase);
        });
        return string.IsNullOrWhiteSpace(fuzzy) ? camera.DisplayName : fuzzy;
    }

    private static string NormalizeDeviceName(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static async Task<(bool Succeeded, string Message)> ProbeFfmpegCameraAsync(
        string ffmpeg,
        string dshowDeviceName,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-hide_banner");
            start.ArgumentList.Add("-loglevel");
            start.ArgumentList.Add("warning");
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add("dshow");
            start.ArgumentList.Add("-t");
            start.ArgumentList.Add("0.5");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add($"video={dshowDeviceName}");
            start.ArgumentList.Add("-frames:v");
            start.ArgumentList.Add("1");
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add("null");
            start.ArgumentList.Add("NUL");

            using var process = Process.Start(start);
            if (process is null)
            {
                return (false, "FFmpeg camera probe could not be started.");
            }

            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode == 0)
            {
                return (true, "camera produced a video frame.");
            }

            return (false, ShortFfmpegMessage(stderr, stdout));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "FFmpeg camera probe timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<CapturedBitmap> CaptureMp4FrameAsync(
        WindowsGraphicsCaptureFrameSource? wgcFrameSource,
        RecordingCaptureTarget target,
        TimeSpan timeout,
        Action<Exception> onWgcFallback,
        CancellationToken cancellationToken)
    {
        if (wgcFrameSource is null)
        {
            return await _screenshots.CaptureRecordingTargetAsync(target);
        }

        try
        {
            return wgcFrameSource.CaptureFrame(_settings.IncludeCursor, timeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onWgcFallback(ex);
            return await _screenshots.CaptureRecordingTargetAsync(target);
        }
    }

    private static bool ShouldUseWindowsGraphicsCaptureFrameSource(
        RecordingSettings settings,
        RecordingCapabilitySnapshot capabilities,
        RecordingCaptureTarget target)
    {
        return settings.PreferProductionCaptureEngine &&
            capabilities.WindowsGraphicsCaptureSupported &&
            capabilities.Direct3D11Device.Succeeded &&
            WindowsGraphicsCaptureFrameSource.SupportsTarget(target) &&
            Environment.UserInteractive &&
            Forms.SystemInformation.UserInteractive;
    }

    private static string FormatFrameSourceFallbackNote(Exception ex)
    {
        var message = ex.Message.ReplaceLineEndings(" ").Trim();
        if (message.Length > 180)
        {
            message = message[..180] + "...";
        }

        return $" Windows.Graphics.Capture frame source was unavailable, so GDI screenshot frames were used instead ({ex.GetType().Name}: {message}).";
    }

    public void Dispose()
    {
        RecordingSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        session?.Dispose();
    }

    private RecordingSession StartSession(string? outputPath = null)
    {
        return StartSession(outputPath, RecordingCaptureTarget.ActiveMonitor());
    }

    private RecordingSession StartSession(string? outputPath, RecordingCaptureTarget target)
    {
        return StartSession(outputPath, target, _settings.Recording);
    }

    private RecordingSession StartSession(
        string? outputPath,
        RecordingCaptureTarget target,
        RecordingSettings recordingSettings,
        AnimationExportOptions? animationOptions = null)
    {
        Directory.CreateDirectory(_paths.VideosRoot);
        outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_paths.VideosRoot, $"goatshot-recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.gif")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var timingPlan = GifTimingPlanner.Plan(recordingSettings, animationOptions);
        var session = new RecordingSession(
            outputPath,
            RecordingSettingsNormalizer.Normalize(recordingSettings),
            target.Normalize(),
            timingPlan,
            animationOptions ?? new AnimationExportOptions());
        session.CaptureTask = Task.Run(() => CaptureLoopAsync(session));
        return session;
    }

    private async Task CaptureLoopAsync(RecordingSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        var captureInterval = TimeSpan.FromSeconds(1d / Math.Max(1, session.TimingPlan.CaptureFrameRate));
        var nextCaptureDue = TimeSpan.Zero;
        while (!session.StopRequested && session.FrameCount < session.TimingPlan.MaxFrames)
        {
            if (session.IsPaused)
            {
                try
                {
                    await Task.Delay(LegacyGifFrameDelayMs, session.StopToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                nextCaptureDue = stopwatch.Elapsed + captureInterval;
                continue;
            }

            try
            {
                var now = stopwatch.Elapsed;
                if (now < nextCaptureDue)
                {
                    var delay = nextCaptureDue - now;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, session.StopToken);
                    }
                }

                using var captured = await _screenshots.CaptureRecordingTargetAsync(session.Target);
                using var decorated = DecorateRecordingFrame(
                    captured,
                    _settings.IncludeCursor,
                    session.Settings,
                    DateTimeOffset.Now - session.StartedAt);
                session.AddFrame(ResizeFrame(decorated));
                DuplicateLastGifFrameUntilPaced(session, stopwatch.Elapsed);
                nextCaptureDue = TimeSpan.FromSeconds(session.FrameCount / (double)Math.Max(1, session.TimingPlan.CaptureFrameRate));
            }
            catch
            {
                // Keep recording resilient if a transient screen capture fails.
            }
        }
    }

    private static void DuplicateLastGifFrameUntilPaced(RecordingSession session, TimeSpan elapsed)
    {
        var desired = (int)Math.Ceiling(Math.Max(0d, elapsed.TotalSeconds) * session.TimingPlan.CaptureFrameRate);
        desired = Math.Clamp(desired, session.FrameCount, session.TimingPlan.MaxFrames);
        session.DuplicateLastFrameUntil(desired);
    }

    private async Task<RecordingResult> StopSessionAsync(
        RecordingSession session,
        bool addToWorkspace,
        CancellationToken cancellationToken)
    {
        session.RequestStop();
        await session.CaptureTask;

        if (session.FrameCount == 0)
        {
            session.Dispose();
            return new RecordingResult
            {
                Succeeded = false,
                Message = "Recording stopped before any frames were captured."
            };
        }

        await EncodeGifAsync(session, cancellationToken);
        CaptureItem? item = null;
        if (addToWorkspace)
        {
                item = await _workspaceStore.AddImageFileAsync(
                    session.OutputPath,
                    CaptureKind.RecordingGif,
                    $"GIF recording captured locally from {session.Target.DisplayName}. {session.TimingPlan.Message} Cursor/click visualization and configured fallback overlays were applied.");
        }

        var result = new RecordingResult
        {
            Succeeded = true,
            IsRecording = false,
            OutputPath = session.OutputPath,
            Item = item,
            Message = item is null
                ? $"GIF recording saved: {session.OutputPath}. Target: {session.Target.DisplayName}. {session.TimingPlan.Message}"
                : $"GIF recording saved and indexed: {item.FileName}. Target: {session.Target.DisplayName}. {session.TimingPlan.Message}"
        };
        var companionMessage = await TryWriteAnimationCompanionAsync(session, addToWorkspace, cancellationToken);
        if (!string.IsNullOrWhiteSpace(companionMessage))
        {
            result.Message += $" {companionMessage}";
        }

        session.Dispose();
        return result;
    }

    private static async Task EncodeGifAsync(RecordingSession session, CancellationToken cancellationToken)
    {
        using var gif = AnimatedGif.AnimatedGif.Create(session.OutputPath, session.TimingPlan.DelayForFrame(0), repeat: 0);
        var frames = session.Frames;
        for (var index = 0; index < frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await gif.AddFrameAsync(
                frames[index],
                session.TimingPlan.DelayForFrame(index),
                GifQuality.Bit8,
                cancellationToken);
        }
    }

    private async Task<string> TryWriteAnimationCompanionAsync(
        RecordingSession session,
        bool addToWorkspace,
        CancellationToken cancellationToken)
    {
        var companionFormat = GifTimingPlanner.NormalizeCompanionFormat(session.AnimationOptions.CompanionFormat);
        if (string.IsNullOrWhiteSpace(companionFormat))
        {
            return string.Empty;
        }

        var ffmpeg = FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return $"Companion {companionFormat.ToUpperInvariant()} was requested, but FFmpeg was not found.";
        }

        var companionPath = Path.ChangeExtension(session.OutputPath, companionFormat);
        var tempRoot = Path.Combine(_paths.TempRoot, "gif-companion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var frames = session.Frames;
            for (var index = 0; index < frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames[index].Save(Path.Combine(tempRoot, $"frame{index + 1:000000}.png"), ImageFormat.Png);
            }

            var arguments = BuildAnimationCompanionArguments(
                tempRoot,
                companionPath,
                companionFormat,
                session.TimingPlan.CaptureFrameRate);
            var result = await RunFfmpegAsync(ffmpeg, arguments, cancellationToken);
            if (!result.Succeeded || !File.Exists(companionPath))
            {
                return result.Succeeded
                    ? $"Companion {companionFormat.ToUpperInvariant()} was requested, but FFmpeg did not create an output."
                    : $"Companion {companionFormat.ToUpperInvariant()} was requested, but export failed: {result.Message}";
            }

            if (addToWorkspace)
            {
                await _workspaceStore.AddImageFileAsync(
                    companionPath,
                    CaptureKind.RecordingMp4,
                    $"High-frame-rate {companionFormat.ToUpperInvariant()} companion exported from GIF capture at {session.TimingPlan.CaptureFrameRate} fps.");
            }

            return $"Companion {companionFormat.ToUpperInvariant()} saved: {companionPath}.";
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup must not hide a successful recording.
            }
        }
    }

    internal static IReadOnlyList<string> BuildAnimationCompanionArguments(
        string frameDirectory,
        string outputPath,
        string companionFormat,
        int frameRate)
    {
        var inputPattern = Path.Combine(frameDirectory, "frame%06d.png");
        var fps = Math.Clamp(frameRate, GifTimingPlanner.MinimumFrameRate, GifTimingPlanner.MaximumSourceFrameRate);
        if (companionFormat.Equals("webm", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "-y",
                "-framerate",
                fps.ToString(CultureInfo.InvariantCulture),
                "-i",
                inputPattern,
                "-c:v",
                "libvpx-vp9",
                "-b:v",
                "0",
                "-crf",
                "28",
                "-pix_fmt",
                "yuv420p",
                outputPath
            ];
        }

        return
        [
            "-y",
            "-framerate",
            fps.ToString(CultureInfo.InvariantCulture),
            "-i",
            inputPattern,
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-crf",
            "18",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            outputPath
        ];
    }

    private static async Task<RecordingResult> RunFfmpegAsync(
        string ffmpeg,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = "FFmpeg could not be started."
            };
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = $"FFmpeg failed with exit code {process.ExitCode}. {ShortFfmpegMessage(stderr, stdout)}"
            };
        }

        return new RecordingResult
        {
            Succeeded = true,
            Message = "FFmpeg completed."
        };
    }

    private static async Task<RecordingResult> EncodeMp4Async(
        string ffmpeg,
        string frameDirectory,
        NormalizedRecordingSettings settings,
        FfmpegVideoEncoderSelection videoEncoder,
        string outputPath,
        TimeSpan duration,
        IReadOnlyList<Mp4AudioInput> audioInputs,
        Mp4WebcamInput? webcamInput,
        CancellationToken cancellationToken)
    {
        var attempts = BuildVideoEncoderAttempts(videoEncoder, settings);
        RecordingResult? firstFailure = null;
        foreach (var attempt in attempts)
        {
            var result = await RunFfmpegMp4EncodeAsync(
                ffmpeg,
                frameDirectory,
                settings,
                attempt,
                outputPath,
                duration,
                audioInputs,
                webcamInput,
                cancellationToken);
            if (result.Succeeded)
            {
                if (firstFailure is null)
                {
                    return result;
                }

                result.Message = $"{result.Message} Retried after: {firstFailure.Message}";
                return result;
            }

            firstFailure ??= result;
        }

        return firstFailure ?? new RecordingResult
        {
            Succeeded = false,
            Message = "No FFmpeg video encoder attempt was available."
        };
    }

    private static async Task<RecordingResult> RunFfmpegMp4EncodeAsync(
        string ffmpeg,
        string frameDirectory,
        NormalizedRecordingSettings settings,
        Mp4VideoEncoderAttempt videoEncoder,
        string outputPath,
        TimeSpan duration,
        IReadOnlyList<Mp4AudioInput> audioInputs,
        Mp4WebcamInput? webcamInput,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-framerate");
        start.ArgumentList.Add(settings.FramesPerSecond.ToString());
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(Path.Combine(frameDirectory, "frame%06d.png"));
        foreach (var audioInput in audioInputs)
        {
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(audioInput.Path);
        }

        var webcamInputIndex = audioInputs.Count + 1;
        if (webcamInput is not null)
        {
            start.ArgumentList.Add("-thread_queue_size");
            start.ArgumentList.Add("512");
            start.ArgumentList.Add("-rtbufsize");
            start.ArgumentList.Add("100M");
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add("dshow");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add($"video={webcamInput.DirectShowDeviceName}");
        }

        var filterParts = new List<string>();
        var videoMap = "0:v:0";
        if (webcamInput is not null)
        {
            filterParts.Add(BuildWebcamOverlayFilter(webcamInputIndex, webcamInput));
            videoMap = "[webcam_video]";
        }

        if (audioInputs.Count > 1)
        {
            filterParts.Add(BuildAudioMixFilter(audioInputs.Count));
        }

        if (filterParts.Count > 0)
        {
            start.ArgumentList.Add("-filter_complex");
            start.ArgumentList.Add(string.Join(";", filterParts));
        }

        start.ArgumentList.Add("-map");
        start.ArgumentList.Add(videoMap);
        if (audioInputs.Count == 1)
        {
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("1:a:0");
        }
        else if (audioInputs.Count > 1)
        {
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("[mixed_audio]");
        }

        AddVideoEncoderArguments(start, settings, videoEncoder);
        start.ArgumentList.Add("-pix_fmt");
        start.ArgumentList.Add("yuv420p");
        start.ArgumentList.Add("-t");
        start.ArgumentList.Add(duration.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        if (audioInputs.Count > 0)
        {
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("aac");
            start.ArgumentList.Add("-b:a");
            start.ArgumentList.Add("160k");
        }

        if (audioInputs.Count > 0 || webcamInput is not null)
        {
            start.ArgumentList.Add("-shortest");
        }

        start.ArgumentList.Add("-movflags");
        start.ArgumentList.Add("+faststart");
        start.ArgumentList.Add(outputPath);

        using var process = Process.Start(start);
        if (process is null)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = "FFmpeg could not be started."
            };
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = $"FFmpeg MP4 encode failed with {videoEncoder.EncoderName} ({videoEncoder.Provider}) and exit code {process.ExitCode}. {ShortFfmpegMessage(stderr, stdout)}"
            };
        }

        return new RecordingResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Message = $"MP4 encoded through FFmpeg fallback using {videoEncoder.EncoderName} ({videoEncoder.Provider}); hardware={(videoEncoder.IsHardwareAccelerated ? "yes" : "no")}; {RecordingSettingsNormalizer.FormatSummary(settings)}"
        };
    }

    private static IReadOnlyList<Mp4VideoEncoderAttempt> BuildVideoEncoderAttempts(
        FfmpegVideoEncoderSelection selection,
        NormalizedRecordingSettings settings)
    {
        var attempts = new List<Mp4VideoEncoderAttempt>();
        if (selection.IsAvailable && !string.IsNullOrWhiteSpace(selection.EncoderName))
        {
            attempts.Add(new Mp4VideoEncoderAttempt(
                selection.EncoderName,
                selection.Provider,
                selection.IsHardwareAccelerated,
                selection.TargetBitrateKbps));
        }

        if (!string.IsNullOrWhiteSpace(selection.RetryEncoderName) &&
            attempts.All(attempt => !attempt.EncoderName.Equals(selection.RetryEncoderName, StringComparison.OrdinalIgnoreCase)))
        {
            attempts.Add(new Mp4VideoEncoderAttempt(
                selection.RetryEncoderName,
                "FFmpeg libx264 software encoder",
                IsHardwareAccelerated: false,
                TargetBitrateKbps: settings.BitrateKbps));
        }

        if (attempts.Count == 0)
        {
            attempts.Add(new Mp4VideoEncoderAttempt(
                "libx264",
                "FFmpeg libx264 software encoder",
                IsHardwareAccelerated: false,
                TargetBitrateKbps: settings.BitrateKbps));
        }

        return attempts;
    }

    private static void AddVideoEncoderArguments(
        ProcessStartInfo start,
        NormalizedRecordingSettings settings,
        Mp4VideoEncoderAttempt videoEncoder)
    {
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add(videoEncoder.EncoderName);
        if (videoEncoder.IsHardwareAccelerated)
        {
            start.ArgumentList.Add("-b:v");
            start.ArgumentList.Add($"{Math.Max(800, videoEncoder.TargetBitrateKbps)}k");
            return;
        }

        if (settings.BitrateKbps > 0)
        {
            start.ArgumentList.Add("-b:v");
            start.ArgumentList.Add($"{settings.BitrateKbps}k");
        }
        else if (settings.UseVariableBitrate)
        {
            start.ArgumentList.Add("-crf");
            start.ArgumentList.Add(settings.Crf.ToString());
        }

        start.ArgumentList.Add("-preset");
        start.ArgumentList.Add(settings.QualityProfile.Equals("Small", StringComparison.OrdinalIgnoreCase)
            ? "ultrafast"
            : "veryfast");
    }

    internal static string? FindFfmpeg()
    {
        var configured = Environment.GetEnvironmentVariable("GOATSHOT_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.ExpandEnvironmentVariables(configured);
            return File.Exists(configured) ? configured : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    internal static string ShortFfmpegMessage(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 360 ? text : text[..360] + "...";
    }

    private static string BuildAudioMixFilter(int inputCount)
    {
        var labels = Enumerable.Range(1, inputCount)
            .Select(index => $"[{index}:a:0]");
        return string.Concat(labels) + $"amix=inputs={inputCount}:duration=longest:normalize=0[mixed_audio]";
    }

    internal static string BuildWebcamOverlayFilter(int webcamInputIndex, Mp4WebcamInput webcamInput)
    {
        var mirror = webcamInput.Mirror ? "hflip," : string.Empty;
        var shape = NormalizeWebcamShape(webcamInput.Shape);
        var cameraChain = shape.Equals("circle", StringComparison.OrdinalIgnoreCase)
            ? $"{mirror}crop='min(iw,ih)':'min(iw,ih)',scale=220:220,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(lte((X-W/2)*(X-W/2)+(Y-H/2)*(Y-H/2),(min(W,H)/2)*(min(W,H)/2)),255,0)'"
            : $"{mirror}scale=260:-2,format=rgba";
        var position = BuildWebcamOverlayPosition(webcamInput.Position);
        return $"[{webcamInputIndex}:v:0]setpts=PTS-STARTPTS,{cameraChain}[webcam_overlay];[0:v:0][webcam_overlay]overlay={position}:eof_action=pass[webcam_video]";
    }

    internal static string BuildWebcamOverlayPosition(string? position)
    {
        return NormalizeWebcamPosition(position) switch
        {
            "TopLeft" => "24:24",
            "TopRight" => "W-w-24:24",
            "BottomLeft" => "24:H-h-24",
            _ => "W-w-24:H-h-24"
        };
    }

    internal static string NormalizeWebcamPosition(string? position)
    {
        var normalized = NormalizeOptionName(position);
        return normalized switch
        {
            "topleft" or "lefttop" => "TopLeft",
            "topright" or "righttop" => "TopRight",
            "bottomleft" or "leftbottom" => "BottomLeft",
            _ => "BottomRight"
        };
    }

    internal static string NormalizeWebcamShape(string? shape)
    {
        var normalized = NormalizeOptionName(shape);
        return normalized switch
        {
            "rectangle" or "rect" or "square" or "box" => "rectangle",
            _ => "circle"
        };
    }

    private static string NormalizeOptionName(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string FormatAudioSource(AudioCaptureSource source)
    {
        return source == AudioCaptureSource.SystemAudio
            ? "system audio"
            : "microphone";
    }

    private static Bitmap ResizeFrame(Bitmap source)
    {
        if (source.Width <= MaxFrameWidth)
        {
            return (Bitmap)source.Clone();
        }

        var scale = MaxFrameWidth / (double)source.Width;
        var width = MaxFrameWidth;
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static Bitmap ResizeFrame(Bitmap source, NormalizedRecordingSettings settings)
    {
        var (width, height) = ResolveFrameSize(source.Width, source.Height, settings);
        if (source.Width == width && source.Height == height)
        {
            return (Bitmap)source.Clone();
        }

        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return resized;
    }

    private static (int Width, int Height) ResolveFrameSize(int sourceWidth, int sourceHeight, NormalizedRecordingSettings settings)
    {
        var targetWidth = settings.TargetWidth;
        var targetHeight = settings.TargetHeight;

        if (targetWidth <= 0 && targetHeight <= 0)
        {
            targetWidth = Math.Min(sourceWidth, MaxFrameWidth);
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

    private static int MakeEven(int value)
    {
        value = Math.Max(2, value);
        return value % 2 == 0 ? value : value - 1;
    }

    private static RecordingSettings CopyRecordingSettings(RecordingSettings source)
    {
        return new RecordingSettings
        {
            PreferProductionCaptureEngine = source.PreferProductionCaptureEngine,
            PreferHevcEncoding = source.PreferHevcEncoding,
            QualityProfile = source.QualityProfile,
            FramesPerSecond = source.FramesPerSecond,
            TargetWidth = source.TargetWidth,
            TargetHeight = source.TargetHeight,
            BitrateKbps = source.BitrateKbps,
            UseVariableBitrate = source.UseVariableBitrate,
            IncludeMicrophone = source.IncludeMicrophone,
            IncludeSystemAudio = source.IncludeSystemAudio,
            MicrophoneDeviceId = source.MicrophoneDeviceId,
            SystemAudioDeviceId = source.SystemAudioDeviceId,
            MicrophoneGain = source.MicrophoneGain,
            SystemAudioGain = source.SystemAudioGain,
            NoiseGateThresholdDb = source.NoiseGateThresholdDb,
            MicrophoneMuted = source.MicrophoneMuted,
            SystemAudioMuted = source.SystemAudioMuted,
            EnableWebcamOverlay = source.EnableWebcamOverlay,
            WebcamDeviceId = source.WebcamDeviceId,
            WebcamOverlayPosition = source.WebcamOverlayPosition,
            WebcamOverlayShape = source.WebcamOverlayShape,
            MirrorWebcam = source.MirrorWebcam,
            ShowCountdown = source.ShowCountdown,
            ShowRecordingBorder = source.ShowRecordingBorder,
            ShowRecordingTimer = source.ShowRecordingTimer,
            ShowKeystrokeOverlay = source.ShowKeystrokeOverlay,
            RecordingTimerPosition = source.RecordingTimerPosition,
            KeystrokeOverlayPosition = source.KeystrokeOverlayPosition,
            RecordingOverlayBadgeFontSize = source.RecordingOverlayBadgeFontSize,
            RecordingOverlayStyle = source.RecordingOverlayStyle
        };
    }

    private static Bitmap DecorateRecordingFrame(
        CapturedBitmap captured,
        bool includeCursorVisualization,
        NormalizedRecordingSettings? settings = null,
        TimeSpan? elapsed = null)
    {
        var frame = (Bitmap)captured.Bitmap.Clone();
        using var graphics = Graphics.FromImage(frame);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (settings?.ShowRecordingBorder == true)
        {
            DrawRecordingBorder(graphics, frame);
        }

        if (settings?.ShowRecordingTimer == true && elapsed.HasValue)
        {
            DrawTextBadge(
                graphics,
                frame,
                FormatElapsed(elapsed.Value),
                settings.RecordingTimerPosition,
                settings.RecordingOverlayBadgeFontSize,
                settings.RecordingOverlayStyle);
        }

        if (settings?.ShowKeystrokeOverlay == true)
        {
            var keys = CurrentKeyOverlay();
            if (!string.IsNullOrWhiteSpace(keys))
            {
                DrawTextBadge(
                    graphics,
                    frame,
                    keys,
                    settings.KeystrokeOverlayPosition,
                    settings.RecordingOverlayBadgeFontSize,
                    settings.RecordingOverlayStyle);
            }
        }

        if (includeCursorVisualization)
        {
            var cursor = Forms.Cursor.Position;
            var x = cursor.X - captured.Bounds.X;
            var y = cursor.Y - captured.Bounds.Y;
            if (x >= 0 && y >= 0 && x <= frame.Width && y <= frame.Height)
            {
                var leftDown = IsKeyDown(VkLButton);
                var rightDown = IsKeyDown(VkRButton);
                var middleDown = IsKeyDown(VkMButton);
                DrawCursorHalo(graphics, x, y);
                if (leftDown || rightDown || middleDown)
                {
                    DrawClickRings(graphics, x, y, leftDown, rightDown, middleDown);
                }
            }
        }

        return frame;
    }

    private static void DrawRecordingBorder(Graphics graphics, Bitmap frame)
    {
        using var shadowPen = new Pen(Color.FromArgb(150, 0, 0, 0), 10);
        using var accentPen = new Pen(Color.FromArgb(235, 48, 230, 195), 4);
        graphics.DrawRectangle(shadowPen, 5, 5, frame.Width - 10, frame.Height - 10);
        graphics.DrawRectangle(accentPen, 7, 7, frame.Width - 14, frame.Height - 14);
    }

    private static void DrawTextBadge(
        Graphics graphics,
        Bitmap frame,
        string text,
        string position,
        int fontSize,
        string style)
    {
        fontSize = Math.Clamp(fontSize, 10, 32);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = graphics.MeasureString(text, font);
        var rectWidth = (int)Math.Ceiling(size.Width + 18);
        var rectHeight = (int)Math.Ceiling(size.Height + 10);
        var origin = ResolveOverlayBadgeOrigin(frame.Width, frame.Height, rectWidth, rectHeight, position, 16);
        var rect = new RectangleF(origin.X, origin.Y, rectWidth, rectHeight);
        var palette = ResolveOverlayPalette(style);
        using var background = new SolidBrush(palette.Background);
        using var border = new Pen(palette.Border, 2);
        using var foreground = new SolidBrush(palette.Foreground);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        graphics.DrawString(text, font, foreground, origin.X + 9, origin.Y + 5);
    }

    internal static Point ResolveOverlayBadgeOrigin(
        int frameWidth,
        int frameHeight,
        int badgeWidth,
        int badgeHeight,
        string? position,
        int margin)
    {
        frameWidth = Math.Max(1, frameWidth);
        frameHeight = Math.Max(1, frameHeight);
        badgeWidth = Math.Clamp(badgeWidth, 1, frameWidth);
        badgeHeight = Math.Clamp(badgeHeight, 1, frameHeight);
        margin = Math.Clamp(margin, 0, Math.Min(frameWidth, frameHeight) / 2);
        var normalized = RecordingSettingsNormalizer.NormalizeOverlayPosition(position, "TopLeft");
        var x = normalized switch
        {
            "TopRight" or "BottomRight" => frameWidth - badgeWidth - margin,
            "TopCenter" or "BottomCenter" => (frameWidth - badgeWidth) / 2,
            _ => margin
        };
        var y = normalized switch
        {
            "BottomLeft" or "BottomRight" or "BottomCenter" => frameHeight - badgeHeight - margin,
            _ => margin
        };

        return new Point(Math.Max(0, x), Math.Max(0, y));
    }

    private static RecordingOverlayPalette ResolveOverlayPalette(string? style)
    {
        return RecordingSettingsNormalizer.NormalizeOverlayStyle(style) switch
        {
            "HighContrast" => new RecordingOverlayPalette(
                Color.FromArgb(230, 0, 0, 0),
                Color.FromArgb(255, 255, 255, 255),
                Color.FromArgb(255, 255, 255, 255)),
            "Subtle" => new RecordingOverlayPalette(
                Color.FromArgb(150, 6, 17, 22),
                Color.FromArgb(170, 185, 207, 218),
                Color.FromArgb(235, 244, 250, 255)),
            _ => new RecordingOverlayPalette(
                Color.FromArgb(190, 6, 17, 22),
                Color.FromArgb(220, 48, 230, 195),
                Color.FromArgb(244, 250, 255))
        };
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return elapsed.TotalHours >= 1d
            ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string CurrentKeyOverlay()
    {
        var keys = new List<string>();
        if (IsKeyDown(VkControl))
        {
            keys.Add("Ctrl");
        }

        if (IsKeyDown(VkShift))
        {
            keys.Add("Shift");
        }

        if (IsKeyDown(VkMenu))
        {
            keys.Add("Alt");
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            keys.Add("Win");
        }

        if (IsKeyDown(VkLButton))
        {
            keys.Add("Left click");
        }

        if (IsKeyDown(VkRButton))
        {
            keys.Add("Right click");
        }

        if (IsKeyDown(VkMButton))
        {
            keys.Add("Middle click");
        }

        return string.Join(" + ", keys);
    }

    private static void DrawCursorHalo(Graphics graphics, int x, int y)
    {
        using var shadowPen = new Pen(Color.FromArgb(150, 0, 0, 0), 8);
        using var haloPen = new Pen(Color.FromArgb(210, 48, 230, 195), 4);
        graphics.DrawEllipse(shadowPen, x - 18, y - 18, 36, 36);
        graphics.DrawEllipse(haloPen, x - 18, y - 18, 36, 36);
    }

    private static void DrawClickRings(Graphics graphics, int x, int y, bool leftDown, bool rightDown, bool middleDown)
    {
        var color = leftDown
            ? Color.FromArgb(235, 255, 72, 72)
            : rightDown
                ? Color.FromArgb(235, 255, 206, 84)
                : Color.FromArgb(235, 152, 119, 255);

        using var fill = new SolidBrush(Color.FromArgb(42, color));
        using var pen = new Pen(color, 6);
        graphics.FillEllipse(fill, x - 32, y - 32, 64, 64);
        graphics.DrawEllipse(pen, x - 32, y - 32, 64, 64);
        if (middleDown)
        {
            using var middlePen = new Pen(Color.FromArgb(235, 152, 119, 255), 4);
            graphics.DrawEllipse(middlePen, x - 42, y - 42, 84, 84);
        }
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private sealed record RecordingOverlayPalette(
        Color Background,
        Color Border,
        Color Foreground);

    private sealed class RecordingSession : IDisposable
    {
        private readonly object _frameGate = new();
        private readonly object _stateGate = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly List<Bitmap> _frames = new();
        private bool _isPaused;

        public RecordingSession(
            string outputPath,
            NormalizedRecordingSettings settings,
            RecordingCaptureTarget target,
            GifTimingPlan timingPlan,
            AnimationExportOptions animationOptions)
        {
            OutputPath = outputPath;
            Settings = settings;
            Target = target;
            TimingPlan = timingPlan;
            AnimationOptions = animationOptions;
            StartedAt = DateTimeOffset.Now;
        }

        public string OutputPath { get; }
        public NormalizedRecordingSettings Settings { get; }
        public RecordingCaptureTarget Target { get; }
        public GifTimingPlan TimingPlan { get; }
        public AnimationExportOptions AnimationOptions { get; }
        public DateTimeOffset StartedAt { get; }
        public Task CaptureTask { get; set; } = Task.CompletedTask;
        public CancellationToken StopToken => _stop.Token;
        public bool StopRequested => _stop.IsCancellationRequested;
        public bool IsPaused
        {
            get
            {
                lock (_stateGate)
                {
                    return _isPaused;
                }
            }
        }

        public int FrameCount
        {
            get
            {
                lock (_frameGate)
                {
                    return _frames.Count;
                }
            }
        }

        public IReadOnlyList<Bitmap> Frames
        {
            get
            {
                lock (_frameGate)
                {
                    return _frames.ToList();
                }
            }
        }

        public void AddFrame(Bitmap frame)
        {
            lock (_frameGate)
            {
                if (_frames.Count >= TimingPlan.MaxFrames)
                {
                    frame.Dispose();
                    return;
                }

                _frames.Add(frame);
            }
        }

        public void DuplicateLastFrameUntil(int desiredFrameCount)
        {
            lock (_frameGate)
            {
                if (_frames.Count == 0)
                {
                    return;
                }

                desiredFrameCount = Math.Clamp(desiredFrameCount, _frames.Count, TimingPlan.MaxFrames);
                var source = _frames[^1];
                while (_frames.Count < desiredFrameCount)
                {
                    _frames.Add((Bitmap)source.Clone());
                }
            }
        }

        public bool TogglePaused()
        {
            lock (_stateGate)
            {
                _isPaused = !_isPaused;
                return _isPaused;
            }
        }

        public void RequestStop()
        {
            if (!_stop.IsCancellationRequested)
            {
                _stop.Cancel();
            }
        }

        public void Dispose()
        {
            _stop.Dispose();
            lock (_frameGate)
            {
                foreach (var frame in _frames)
                {
                    frame.Dispose();
                }

                _frames.Clear();
            }
        }
    }

    private sealed record PendingAudioCapture(
        AudioCaptureSource Source,
        string OutputPath,
        TimeSpan RequestedDuration,
        DateTimeOffset StartedAt,
        Task<AudioCaptureResult> Task);

    private sealed record Mp4AudioInput(
        AudioCaptureSource Source,
        string Path);

    private sealed record Mp4AudioCaptureSummary(
        IReadOnlyList<Mp4AudioInput> Inputs,
        string Message);

    internal sealed record Mp4WebcamInput(
        string DirectShowDeviceName,
        string DisplayName,
        string Position,
        string Shape,
        bool Mirror);

    private sealed record Mp4WebcamCaptureSummary(
        Mp4WebcamInput? Input,
        string Message);

    internal sealed record NativeWebcamOverlay(
        Bitmap Frame,
        string DisplayName,
        string Position,
        string Shape,
        bool Mirror) : IDisposable
    {
        public void Dispose()
        {
            Frame.Dispose();
        }
    }

    internal sealed class NativeWebcamOverlaySource : IDisposable
    {
        private readonly ICameraOverlayService _cameraOverlay;
        private readonly string _deviceId;
        private readonly string _position;
        private readonly string _shape;
        private readonly bool _mirror;
        private readonly TimeSpan _refreshInterval;
        private DateTimeOffset _nextRefreshAt = DateTimeOffset.MinValue;
        private bool _disposed;

        public NativeWebcamOverlaySource(
            ICameraOverlayService cameraOverlay,
            string deviceId,
            string position,
            string shape,
            bool mirror,
            TimeSpan refreshInterval)
        {
            _cameraOverlay = cameraOverlay;
            _deviceId = deviceId;
            _position = position;
            _shape = shape;
            _mirror = mirror;
            _refreshInterval = refreshInterval <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(750)
                : refreshInterval;
        }

        public NativeWebcamOverlay? Current { get; private set; }
        public int CapturedFrameCount { get; private set; }
        public int RefreshFailureCount { get; private set; }
        public string LastMessage { get; private set; } = "not started";

        public string Summary
        {
            get
            {
                if (Current is null)
                {
                    return $"requested but no native camera frame is available ({LastMessage}).";
                }

                var refreshText = CapturedFrameCount == 1
                    ? "captured 1 native camera frame"
                    : $"captured {CapturedFrameCount} native camera frames";
                var failureText = RefreshFailureCount == 0
                    ? "no refresh failures"
                    : $"{RefreshFailureCount} refresh failure(s), kept the last good frame";
                return $"continuous native camera overlay from {Current.DisplayName} at {NormalizeWebcamPosition(Current.Position)} as {NormalizeWebcamShape(Current.Shape)}, mirror={(Current.Mirror ? "on" : "off")}; {refreshText} at a bounded {_refreshInterval.TotalMilliseconds:0}ms refresh interval; {failureText}.";
            }
        }

        public async Task<bool> RefreshNowAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using var result = await _cameraOverlay.CaptureFrameAsync(_deviceId, cancellationToken);
            if (!result.Succeeded || result.Frame is null)
            {
                RefreshFailureCount++;
                LastMessage = result.Message;
                _nextRefreshAt = now + _refreshInterval;
                return false;
            }

            var overlay = new NativeWebcamOverlay(
                (Bitmap)result.Frame.Clone(),
                result.Device?.DisplayName ?? "selected camera",
                _position,
                _shape,
                _mirror);
            var previous = Current;
            Current = overlay;
            previous?.Dispose();
            CapturedFrameCount++;
            LastMessage = result.Message;
            _nextRefreshAt = now + _refreshInterval;
            return true;
        }

        public async Task<bool> RefreshIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return now >= _nextRefreshAt
                ? await RefreshNowAsync(now, cancellationToken)
                : false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current?.Dispose();
            Current = null;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed record NativeWebcamOverlayCaptureSummary(
        NativeWebcamOverlaySource? Input,
        string Message);

    private sealed record Mp4VideoEncoderAttempt(
        string EncoderName,
        string Provider,
        bool IsHardwareAccelerated,
        int TargetBitrateKbps);
}

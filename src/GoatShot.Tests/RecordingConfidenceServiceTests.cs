using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingConfidenceServiceTests
{
    [TestMethod]
    public void BuildDeviceReport_MarksAllTracksDisabledWhenNothingIsRequested()
    {
        var report = RecordingConfidenceService.BuildDeviceReport(
            new RecordingSettings(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>());

        Assert.AreEqual(RecordingConfidenceLevel.Disabled, report.Overall);
        Assert.IsFalse(report.HasBlockers);
        Assert.AreEqual(3, report.Signals.Count);
        Assert.IsTrue(report.Signals.All(signal => signal.Level == RecordingConfidenceLevel.Disabled));
        StringAssert.Contains(report.Summary, "no recording device tracks");
    }

    [TestMethod]
    public void BuildDeviceReport_BlocksRequestedMissingEndpoints()
    {
        var report = RecordingConfidenceService.BuildDeviceReport(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                MicrophoneDeviceId = "missing-mic",
                IncludeSystemAudio = true,
                EnableWebcamOverlay = true
            },
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>());

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, report.Overall);
        Assert.AreEqual(3, report.Signals.Count(signal => signal.Level == RecordingConfidenceLevel.Blocked));
        Assert.IsTrue(report.ActionItems.Any(signal => signal.Area == "Microphone"));
        Assert.IsTrue(report.ActionItems.Any(signal => signal.Area == "System audio"));
        Assert.IsTrue(report.ActionItems.Any(signal => signal.Area == "Webcam"));
    }

    [TestMethod]
    public void BuildDeviceReport_WarnsWhenRequestedTrackIsMuted()
    {
        var report = RecordingConfidenceService.BuildDeviceReport(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                MicrophoneMuted = true
            },
            new[]
            {
                new AudioCaptureDevice("mic-1", "Studio Mic", IsDefault: true, SupportsLoopback: false, PeakLevel: 0.5, MeterStatus: "WASAPI endpoint active.")
            },
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>());

        Assert.AreEqual(RecordingConfidenceLevel.Warning, report.Overall);
        var microphone = report.Signals.Single(signal => signal.Area == "Microphone");
        Assert.AreEqual(RecordingConfidenceLevel.Warning, microphone.Level);
        StringAssert.Contains(microphone.Status, "muted");
        StringAssert.Contains(microphone.RecoveryAction, "Unmute");
    }

    [TestMethod]
    public void BuildDeviceReport_BlocksPermissionDeniedMeterStatus()
    {
        var report = RecordingConfidenceService.BuildDeviceReport(
            new RecordingSettings
            {
                IncludeMicrophone = true
            },
            new[]
            {
                new AudioCaptureDevice("mic-1", "Studio Mic", IsDefault: true, SupportsLoopback: false, MeterStatus: "Access is denied by Windows privacy settings.")
            },
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>());

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, report.Overall);
        var microphone = report.Signals.Single(signal => signal.Area == "Microphone");
        StringAssert.Contains(microphone.Status, "Access is denied");
        StringAssert.Contains(microphone.RecoveryAction, "privacy");
    }

    [TestMethod]
    public void BuildDeviceReport_NamesPermissionUnavailableAndDisconnectedStates()
    {
        var report = RecordingConfidenceService.BuildDeviceReport(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                EnableWebcamOverlay = true
            },
            new[]
            {
                new AudioCaptureDevice("mic-1", "Studio Mic", IsDefault: true, SupportsLoopback: false, MeterStatus: "Permission denied by Windows privacy settings.")
            },
            new[]
            {
                new AudioCaptureDevice("render-1", "Speakers", IsDefault: true, SupportsLoopback: true, MeterStatus: "Endpoint unavailable.")
            },
            new[]
            {
                new CameraOverlayDevice("cam-1", "USB Camera", IsDefault: true, Status: "Device disconnected.")
            });

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, report.Overall);
        var microphone = report.Signals.Single(signal => signal.Area == "Microphone");
        var systemAudio = report.Signals.Single(signal => signal.Area == "System audio");
        var webcam = report.Signals.Single(signal => signal.Area == "Webcam");

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, microphone.Level);
        StringAssert.Contains(microphone.Status, "permission is denied");
        StringAssert.Contains(microphone.RecoveryAction, "privacy");

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, systemAudio.Level);
        StringAssert.Contains(systemAudio.Status, "unavailable");
        StringAssert.Contains(systemAudio.RecoveryAction, "sound");

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, webcam.Level);
        StringAssert.Contains(webcam.Status, "disconnected");
        StringAssert.Contains(webcam.RecoveryAction, "Reconnect");
    }

    [TestMethod]
    public void BuildReadinessReport_WarnsWhenFallbackCanRecordButProductionIsNotSelected()
    {
        var settings = new RecordingSettings();
        var capabilities = Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true);
        var plan = RecordingEnginePlanner.BuildPlan(
            settings,
            capabilities,
            productionFrameCaptureImplemented: true);
        var devices = new RecordingDeviceSnapshot(
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>(),
            "Recording device selections: microphone=system default, system-audio=system default, webcam=system default.",
            Array.Empty<string>())
        {
            Confidence = RecordingConfidenceService.BuildDeviceReport(
                settings,
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<CameraOverlayDevice>())
        };

        var report = RecordingConfidenceService.BuildReadinessReport(settings, capabilities, plan, devices);

        Assert.AreEqual(RecordingConfidenceLevel.Warning, report.Overall);
        Assert.IsFalse(report.HasBlockers);
        Assert.IsTrue(report.Signals.Any(signal => signal.Area == "MP4 engine" && signal.Level == RecordingConfidenceLevel.Warning));
        Assert.IsTrue(report.Signals.Any(signal => signal.Area == "FFmpeg fallback" && signal.Level == RecordingConfidenceLevel.Ready));
    }

    [TestMethod]
    public void BuildReadinessReport_MarksProductionAndSecondaryFallbackReady()
    {
        var settings = new RecordingSettings();
        var capabilities = Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true);
        var plan = RecordingEnginePlanner.BuildPlan(
            settings,
            capabilities,
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);
        var devices = new RecordingDeviceSnapshot(
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>(),
            "Recording device selections: microphone=system default, system-audio=system default, webcam=system default.",
            Array.Empty<string>())
        {
            Confidence = RecordingConfidenceService.BuildDeviceReport(
                settings,
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<CameraOverlayDevice>())
        };

        var report = RecordingConfidenceService.BuildReadinessReport(settings, capabilities, plan, devices);

        Assert.AreEqual(RecordingConfidenceLevel.Ready, report.Overall);
        Assert.IsTrue(report.Signals.All(signal => signal.Level is RecordingConfidenceLevel.Ready or RecordingConfidenceLevel.Disabled));
        Assert.IsTrue(report.Signals.Any(signal => signal.Area == "FFmpeg fallback" && signal.Status.Contains("secondary path", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildReadinessReport_BlocksWhenNoEngineAndRequestedDeviceIsMissing()
    {
        var settings = new RecordingSettings
        {
            IncludeMicrophone = true
        };
        var capabilities = Capabilities(wgc: false, mediaFoundationH264: false, ffmpeg: false, direct3D11: false);
        var devices = new RecordingDeviceSnapshot(
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>(),
            "Recording device selections: microphone=system default, system-audio=system default, webcam=system default.",
            ["Configured microphone device is not currently available: missing"])
        {
            Confidence = RecordingConfidenceService.BuildDeviceReport(
                settings,
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<AudioCaptureDevice>(),
                Array.Empty<CameraOverlayDevice>())
        };
        var plan = RecordingEnginePlanner.BuildPlan(
            settings,
            capabilities,
            devices.Issues);

        var report = RecordingConfidenceService.BuildReadinessReport(settings, capabilities, plan, devices);

        Assert.AreEqual(RecordingConfidenceLevel.Blocked, report.Overall);
        Assert.IsTrue(report.HasBlockers);
        Assert.IsTrue(report.Signals.Any(signal => signal.Area == "MP4 engine" && signal.Level == RecordingConfidenceLevel.Blocked));
        Assert.IsTrue(report.Signals.Any(signal => signal.Area == "Microphone" && signal.Level == RecordingConfidenceLevel.Blocked));
    }

    [TestMethod]
    public void BuildEngineReport_AddsMetadataOnlyAudioSyncProofForRequestedAudio()
    {
        var settings = new RecordingSettings
        {
            IncludeMicrophone = true
        };
        var capabilities = Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true);
        var plan = RecordingEnginePlanner.BuildPlan(
            settings,
            capabilities,
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: true);

        var report = RecordingConfidenceService.BuildEngineReport(settings, capabilities, plan);

        var sync = report.Signals.Single(signal => signal.Area == "Audio sync proof");
        Assert.AreEqual(RecordingConfidenceLevel.Warning, sync.Level);
        StringAssert.Contains(sync.Status, "streamed as normalized PCM");
        StringAssert.Contains(sync.Status, "bounded chunks");
        StringAssert.Contains(sync.RecoveryAction, "diagnostics recording-media");
    }

    private static RecordingCapabilitySnapshot Capabilities(
        bool wgc,
        bool mediaFoundationH264,
        bool ffmpeg,
        bool direct3D11 = true)
    {
        var h264Count = mediaFoundationH264 ? 1 : 0;
        return new RecordingCapabilitySnapshot(
            wgc,
            wgc
                ? "Windows.Graphics.Capture API probe: supported on this machine."
                : "Windows.Graphics.Capture API probe: not supported on this machine.",
            direct3D11
                ? new Direct3D11DeviceProbe(true, 0xB100, "Direct3D11 device probe: hardware device created with BGRA support at feature level 11.1.")
                : new Direct3D11DeviceProbe(false, 0, "Direct3D11 device probe failed: HRESULT 0x887A0004."),
            new EncoderProbeResult(true, h264Count, string.Empty),
            new EncoderProbeResult(true, h264Count, string.Empty),
            new EncoderProbeResult(true, 0, string.Empty),
            new EncoderProbeResult(true, 0, string.Empty),
            ffmpeg
                ? new FfmpegEncoderProbe("C:\\tools\\ffmpeg.exe", [], ["libx264"], "FFmpeg fallback encoder probe: C:\\tools\\ffmpeg.exe; software encoders reported: libx264.")
                : new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found on PATH and GOATSHOT_FFMPEG_PATH is not set."));
    }
}

using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingPanelPresenterTests
{
    [TestMethod]
    public void Build_SummarizesIdleRecordingSetup()
    {
        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings
            {
                QualityProfile = "small",
                FramesPerSecond = 12,
                TargetWidth = 1280,
                TargetHeight = 720,
                ShowRecordingBorder = true,
                ShowRecordingTimer = true,
                RecordingTimerPosition = "top-right",
                RecordingOverlayBadgeFontSize = 18,
                RecordingOverlayStyle = "high-contrast"
            },
            EnginePlan(ffmpeg: true),
            gifRecording: false,
            gifPaused: false,
            profileCount: 3);

        Assert.AreEqual("State: idle.", snapshot.State);
        Assert.AreEqual("Source: active monitor quick recording.", snapshot.Source);
        StringAssert.Contains(snapshot.Quality, "Small");
        StringAssert.Contains(snapshot.Quality, "12 fps");
        StringAssert.Contains(snapshot.Quality, "1280x720");
        StringAssert.Contains(snapshot.Quality, "Saved profiles: 3");
        Assert.AreEqual("Audio: off.", snapshot.Audio);
        Assert.AreEqual("Camera: off; preview available from the recording panel.", snapshot.Camera);
        StringAssert.Contains(snapshot.Overlays, "timer=TopRight");
        StringAssert.Contains(snapshot.Overlays, "badge 18px HighContrast");
        StringAssert.Contains(snapshot.Output, "workspace library");
        StringAssert.Contains(snapshot.Engine, "FFmpeg fallback selected");
    }

    [TestMethod]
    public void Build_SummarizesActiveGifAudioCameraAndOverlayState()
    {
        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                MicrophoneGain = 1.25d,
                SystemAudioMuted = true,
                NoiseGateThresholdDb = -55d,
                EnableWebcamOverlay = true,
                WebcamDeviceId = "camera-1",
                WebcamOverlayPosition = "TopLeft",
                WebcamOverlayShape = "Rounded",
                MirrorWebcam = false,
                ShowRecordingBorder = false,
                ShowRecordingTimer = false,
                ShowKeystrokeOverlay = true,
                KeystrokeOverlayPosition = "bottom center"
            },
            EnginePlan(ffmpeg: true),
            gifRecording: true,
            gifPaused: false,
            profileCount: 1);

        Assert.AreEqual("State: GIF recording active.", snapshot.State);
        StringAssert.Contains(snapshot.Audio, "microphone gain 1.25x");
        StringAssert.Contains(snapshot.Audio, "noise gate -55 dB");
        StringAssert.Contains(snapshot.Audio, "system audio muted");
        StringAssert.Contains(snapshot.Camera, "selected camera");
        StringAssert.Contains(snapshot.Camera, "TopLeft");
        StringAssert.Contains(snapshot.Camera, "mirror=off");
        StringAssert.Contains(snapshot.Camera, "live preview available");
        StringAssert.Contains(snapshot.Overlays, "border=off");
        StringAssert.Contains(snapshot.Overlays, "timer=off");
        StringAssert.Contains(snapshot.Overlays, "keys=BottomCenter");
    }

    [TestMethod]
    public void Build_SummarizesPausedAndUnavailableEngine()
    {
        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings(),
            EnginePlan(ffmpeg: false),
            gifRecording: true,
            gifPaused: true,
            profileCount: 0);

        Assert.AreEqual("State: GIF recording paused.", snapshot.State);
        StringAssert.Contains(snapshot.Engine, "MP4 recording unavailable");
        StringAssert.Contains(snapshot.ToStatusText(), "State: GIF recording paused.");
        StringAssert.Contains(snapshot.ToStatusText(), "Engine: MP4 recording unavailable");
    }

    [TestMethod]
    public void Build_SummarizesProductionEngineWhenVideoOnlyRecorderIsSelected()
    {
        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings(),
            ProductionEnginePlan(),
            gifRecording: false,
            gifPaused: false,
            profileCount: 0);

        StringAssert.Contains(snapshot.Engine, "production recorder selected");
        StringAssert.Contains(snapshot.Engine, "media-foundation-h264");
    }

    [TestMethod]
    public void Build_IncludesConfidenceActionWhenProvided()
    {
        var confidence = new RecordingConfidenceReport(
            RecordingConfidenceLevel.Warning,
            "Recording engine confidence: usable with caution; 1 action item(s) should be reviewed.",
            new[]
            {
                new RecordingConfidenceSignal(
                    "MP4 engine",
                    RecordingConfidenceLevel.Warning,
                    "MP4 recording can proceed through the FFmpeg fallback.",
                    "Review production blockers when native WGC/D3D/Media Foundation recording is expected.")
            });

        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings(),
            EnginePlan(ffmpeg: true),
            gifRecording: false,
            gifPaused: false,
            profileCount: 0,
            confidence: confidence);

        StringAssert.Contains(snapshot.Confidence, "Confidence: Warning");
        StringAssert.Contains(snapshot.Confidence, "MP4 recording can proceed");
        StringAssert.Contains(snapshot.ToStatusText(), "Review production blockers");
    }

    [TestMethod]
    public void Build_IncludesMultipleRecordingConfidenceActions()
    {
        var confidence = new RecordingConfidenceReport(
            RecordingConfidenceLevel.Blocked,
            "Recording device confidence: blocked; 3 action item(s) need attention before the requested setup is reliable.",
            new[]
            {
                new RecordingConfidenceSignal(
                    "Microphone",
                    RecordingConfidenceLevel.Blocked,
                    "Microphone endpoint Studio Mic is selected, but permission is denied: Permission denied by Windows privacy settings.",
                    "Open Windows privacy settings, allow desktop apps to access microphone, then refresh recording devices."),
                new RecordingConfidenceSignal(
                    "System audio",
                    RecordingConfidenceLevel.Blocked,
                    "System audio endpoint Speakers is selected, but the endpoint is unavailable: Endpoint unavailable.",
                    "Close apps that may own the system audio device, check Windows sound/camera settings, then refresh recording devices."),
                new RecordingConfidenceSignal(
                    "Webcam",
                    RecordingConfidenceLevel.Blocked,
                    "Webcam overlay selected USB Camera, but the device appears disconnected: Device disconnected.",
                    "Reconnect or wake the webcam device, then refresh recording devices.")
            });

        var snapshot = RecordingPanelPresenter.Build(
            new RecordingSettings(),
            EnginePlan(ffmpeg: true),
            gifRecording: false,
            gifPaused: false,
            profileCount: 0,
            confidence: confidence);

        StringAssert.Contains(snapshot.Confidence, "Confidence: Blocked");
        StringAssert.Contains(snapshot.Confidence, "3 action item");
        StringAssert.Contains(snapshot.Confidence, "Microphone endpoint Studio Mic");
        StringAssert.Contains(snapshot.Confidence, "System audio endpoint Speakers");
        StringAssert.Contains(snapshot.Confidence, "Webcam overlay selected USB Camera");
    }

    private static RecordingEnginePlan EnginePlan(bool ffmpeg)
    {
        return RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            new RecordingCapabilitySnapshot(
                WindowsGraphicsCaptureSupported: true,
                WindowsGraphicsCaptureStatus: "Windows.Graphics.Capture API probe: supported on this machine.",
                Direct3D11Device: new Direct3D11DeviceProbe(true, 0xB100, "Direct3D11 device probe: hardware device created with BGRA support at feature level 11.1."),
                H264HardwareEncoder: new EncoderProbeResult(true, 1, string.Empty),
                H264InstalledEncoder: new EncoderProbeResult(true, 1, string.Empty),
                HevcHardwareEncoder: new EncoderProbeResult(true, 0, string.Empty),
                HevcInstalledEncoder: new EncoderProbeResult(true, 0, string.Empty),
                Ffmpeg: ffmpeg
                    ? new FfmpegEncoderProbe("C:\\tools\\ffmpeg.exe", ["h264_mf"], ["libx264"], "FFmpeg fallback encoder probe: available.")
                    : new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found.")),
            productionFrameCaptureImplemented: true);
    }

    private static RecordingEnginePlan ProductionEnginePlan()
    {
        return RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            new RecordingCapabilitySnapshot(
                WindowsGraphicsCaptureSupported: true,
                WindowsGraphicsCaptureStatus: "Windows.Graphics.Capture API probe: supported on this machine.",
                Direct3D11Device: new Direct3D11DeviceProbe(true, 0xB100, "Direct3D11 device probe: hardware device created with BGRA support at feature level 11.1."),
                H264HardwareEncoder: new EncoderProbeResult(true, 1, string.Empty),
                H264InstalledEncoder: new EncoderProbeResult(true, 1, string.Empty),
                HevcHardwareEncoder: new EncoderProbeResult(true, 0, string.Empty),
                HevcInstalledEncoder: new EncoderProbeResult(true, 0, string.Empty),
                Ffmpeg: new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found.")),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);
    }
}

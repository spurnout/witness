using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingDeviceDiagnosticsTests
{
    [TestMethod]
    public void DescribeRecordingDeviceSelection_ResolvesConfiguredDeviceNames()
    {
        var settings = new RecordingSettings
        {
            MicrophoneDeviceId = "mic-1",
            SystemAudioDeviceId = "render-1",
            WebcamDeviceId = "cam-1"
        };
        var microphones = new[]
        {
            new AudioCaptureDevice("mic-1", "Studio Mic", IsDefault: true, SupportsLoopback: false)
        };
        var systemAudio = new[]
        {
            new AudioCaptureDevice("render-1", "Speakers", IsDefault: true, SupportsLoopback: true)
        };
        var cameras = new[]
        {
            new CameraOverlayDevice("cam-1", "USB Camera", IsDefault: true)
        };

        var summary = DiagnosticsService.DescribeRecordingDeviceSelection(settings, microphones, systemAudio, cameras);

        StringAssert.Contains(summary, "microphone=Studio Mic");
        StringAssert.Contains(summary, "system-audio=Speakers");
        StringAssert.Contains(summary, "webcam=USB Camera");
    }

    [TestMethod]
    public void FindSelectionIssues_ReportsEnabledUnavailableDevices()
    {
        var settings = new RecordingSettings
        {
            IncludeMicrophone = true,
            MicrophoneDeviceId = "missing-mic",
            IncludeSystemAudio = true,
            SystemAudioDeviceId = "missing-render",
            EnableWebcamOverlay = true,
            WebcamDeviceId = "missing-camera"
        };

        var issues = DiagnosticsService.FindSelectionIssues(
            settings,
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<AudioCaptureDevice>(),
            Array.Empty<CameraOverlayDevice>());

        Assert.AreEqual(3, issues.Count);
        Assert.IsTrue(issues.Any(issue => issue.Contains("microphone", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("system-audio", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(issues.Any(issue => issue.Contains("webcam", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DescribeAudioCaptureReadiness_ReportsMeteredRequestedEndpoints()
    {
        var settings = new RecordingSettings
        {
            IncludeMicrophone = true,
            IncludeSystemAudio = true,
            MicrophoneDeviceId = "mic-1",
            SystemAudioDeviceId = "render-1"
        };
        var microphones = new[]
        {
            new AudioCaptureDevice("mic-1", "Studio Mic", IsDefault: true, SupportsLoopback: false, PeakLevel: 0.42, MeterStatus: "WASAPI endpoint active.")
        };
        var systemAudio = new[]
        {
            new AudioCaptureDevice("render-1", "Speakers", IsDefault: true, SupportsLoopback: true, PeakLevel: 0.12, MeterStatus: "WASAPI endpoint active.")
        };

        var summary = DiagnosticsService.DescribeAudioCaptureReadiness(settings, microphones, systemAudio);

        StringAssert.Contains(summary, "microphone requested and endpoint available");
        StringAssert.Contains(summary, "system-audio loopback requested and endpoint available");
        StringAssert.Contains(summary, "WASAPI peak meters are available for 2 endpoint");
        StringAssert.Contains(summary, "Short WAV proof capture is available");
        StringAssert.Contains(summary, "captured WAV payloads can be mixed and muxed as AAC");
        StringAssert.Contains(summary, "FFmpeg still available as fallback");
    }
}

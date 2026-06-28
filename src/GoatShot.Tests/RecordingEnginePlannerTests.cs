using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingEnginePlannerTests
{
    [TestMethod]
    public void BuildPlan_UsesFallbackWhenProductionPrerequisitesExistButEngineIsNotWired()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true));

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsTrue(plan.CanRecord);
        Assert.IsTrue(plan.ProductionPrerequisitesMet);
        Assert.IsFalse(plan.ProductionEngineImplemented);
        Assert.IsFalse(plan.ProductionFrameCaptureImplemented);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("frame capture", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("not wired", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_UsesFallbackWhenFrameCaptureIsWiredButProductionEncoderIsNotWired()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true),
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsTrue(plan.CanRecord);
        Assert.IsTrue(plan.ProductionPrerequisitesMet);
        Assert.IsTrue(plan.ProductionFrameCaptureImplemented);
        Assert.IsFalse(plan.ProductionEngineImplemented);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("Media Foundation production MP4 encoding", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(plan.ProductionBlockers.Any(blocker => blocker.Contains("frame capture is not wired", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_SelectsProductionWhenEngineIsWiredAndPrerequisitesAreMet()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Production, plan.Choice);
        Assert.IsTrue(plan.CanRecord);
        Assert.IsTrue(plan.ProductionPrerequisitesMet);
        Assert.IsTrue(plan.ProductionEngineImplemented);
        Assert.AreEqual(ProductionVideoEncoderChoice.HardwareH264, plan.ProductionEncoder.Choice);
        Assert.IsTrue(plan.ProductionEncoder.IsHardwareAccelerated);
    }

    [TestMethod]
    public void BuildPlan_SelectsSoftwareProductionEncoderWhenHardwareH264IsUnavailable()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false, h264Hardware: false),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Production, plan.Choice);
        Assert.IsTrue(plan.ProductionEncoder.IsAvailable);
        Assert.IsFalse(plan.ProductionEncoder.IsHardwareAccelerated);
        Assert.AreEqual(ProductionVideoEncoderChoice.SoftwareH264, plan.ProductionEncoder.Choice);
    }

    [TestMethod]
    public void BuildPlan_SelectsHevcProductionEncoderOnlyWhenRequestedAndAvailable()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                PreferHevcEncoding = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false, hevcHardware: true),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Production, plan.Choice);
        Assert.AreEqual(ProductionVideoEncoderChoice.HardwareHevc, plan.ProductionEncoder.Choice);
        Assert.AreEqual("HEVC", plan.ProductionEncoder.Codec);
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("HEVC", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_BlocksProductionWhenHevcIsRequestedButUnavailable()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                PreferHevcEncoding = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.AreEqual(ProductionVideoEncoderChoice.Unavailable, plan.ProductionEncoder.Choice);
        Assert.AreEqual("HEVC", plan.ProductionEncoder.Codec);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("HEVC was requested", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_UsesFallbackWhenProductionEngineIsVideoOnlyAndAudioIsRequested()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("audio sync", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("microphone", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("system audio", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_SelectsProductionWhenAudioMixingIsWired()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                IncludeMicrophone = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Production, plan.Choice);
        Assert.IsFalse(plan.ProductionBlockers.Any(blocker => blocker.Contains("audio sync", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("native production", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_UsesFallbackWhenProductionEngineIsVideoOnlyAndWebcamIsRequested()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                EnableWebcamOverlay = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("webcam compositing", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("webcam", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_SelectsProductionWhenWebcamCompositorIsWired()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                EnableWebcamOverlay = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true,
            productionWebcamCompositorImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Production, plan.Choice);
        Assert.IsFalse(plan.ProductionBlockers.Any(blocker => blocker.Contains("webcam compositing", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("periodically refreshed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_DoesNotSelectProductionWhenEncoderIsWiredButFrameCaptureIsNot()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: false),
            productionEngineImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.Unavailable, plan.Choice);
        Assert.IsFalse(plan.CanRecord);
        Assert.IsTrue(plan.ProductionPrerequisitesMet);
        Assert.IsTrue(plan.ProductionEngineImplemented);
        Assert.IsFalse(plan.ProductionFrameCaptureImplemented);
        Assert.AreEqual(ProductionVideoEncoderChoice.HardwareH264, plan.ProductionEncoder.Choice);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("frame capture", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_ReportsUnavailableWhenProductionAndFallbackAreBlocked()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: false, direct3D11: false, mediaFoundationH264: false, ffmpeg: false));

        Assert.AreEqual(RecordingEngineChoice.Unavailable, plan.Choice);
        Assert.IsFalse(plan.CanRecord);
        Assert.IsTrue(plan.ProductionBlockers.Count >= 3);
        Assert.IsTrue(plan.FallbackBlockers.Count >= 1);
    }

    [TestMethod]
    public void BuildPlan_Direct3D11FailureBlocksProductionPrerequisites()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, direct3D11: false, mediaFoundationH264: true, ffmpeg: true),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsFalse(plan.ProductionPrerequisitesMet);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("Direct3D11", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_CarriesAudioAndWebcamWarningsForFallback()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
                EnableWebcamOverlay = true
            },
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true));

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("microphone", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("system audio", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("webcam", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void FfmpegVideoEncoderSelector_PrefersMediaFoundationEncoderWithLibx264Retry()
    {
        var selection = FfmpegVideoEncoderSelector.Select(
            new FfmpegEncoderProbe(
                "C:\\tools\\ffmpeg.exe",
                ["h264_nvenc", "h264_mf"],
                ["libx264"],
                "ffmpeg encoders"),
            RecordingSettingsNormalizer.Normalize(new RecordingSettings
            {
                QualityProfile = "Balanced",
                FramesPerSecond = 30,
                TargetWidth = 1920,
                TargetHeight = 1080
            }));

        Assert.AreEqual(FfmpegVideoEncoderChoice.MediaFoundationH264, selection.Choice);
        Assert.AreEqual("h264_mf", selection.EncoderName);
        Assert.IsTrue(selection.IsHardwareAccelerated);
        Assert.AreEqual("libx264", selection.RetryEncoderName);
        Assert.AreEqual(6000, selection.TargetBitrateKbps);
    }

    [TestMethod]
    public void BuildPlan_BlocksFallbackWhenFfmpegHasNoSupportedH264Encoder()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings { PreferProductionCaptureEngine = false },
            new RecordingCapabilitySnapshot(
                false,
                "Windows.Graphics.Capture API probe: not supported on this machine.",
                new Direct3D11DeviceProbe(false, 0, "Direct3D11 device probe failed."),
                new EncoderProbeResult(true, 0, string.Empty),
                new EncoderProbeResult(true, 0, string.Empty),
                new EncoderProbeResult(true, 0, string.Empty),
                new EncoderProbeResult(true, 0, string.Empty),
                new FfmpegEncoderProbe("C:\\tools\\ffmpeg.exe", [], [], "ffmpeg encoders")));

        Assert.AreEqual(RecordingEngineChoice.Unavailable, plan.Choice);
        Assert.IsFalse(plan.FallbackAvailable);
        Assert.AreEqual(FfmpegVideoEncoderChoice.Unavailable, plan.FallbackVideoEncoder.Choice);
        Assert.IsTrue(plan.FallbackBlockers.Any(blocker => blocker.Contains("No supported FFmpeg H.264 encoder", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildPlan_DeviceIssuesBlockProductionPrerequisites()
    {
        var plan = RecordingEnginePlanner.BuildPlan(
            new RecordingSettings(),
            Capabilities(wgc: true, mediaFoundationH264: true, ffmpeg: true),
            ["Configured microphone device is not currently available: missing"]);

        Assert.AreEqual(RecordingEngineChoice.FfmpegFallback, plan.Choice);
        Assert.IsFalse(plan.ProductionPrerequisitesMet);
        Assert.IsTrue(plan.ProductionBlockers.Any(blocker => blocker.Contains("missing", StringComparison.OrdinalIgnoreCase)));
    }

    private static RecordingCapabilitySnapshot Capabilities(
        bool wgc,
        bool mediaFoundationH264,
        bool ffmpeg,
        bool direct3D11 = true,
        bool h264Hardware = true,
        bool hevcHardware = false,
        bool hevcInstalled = false)
    {
        var h264HardwareCount = mediaFoundationH264 && h264Hardware ? 1 : 0;
        var h264InstalledCount = mediaFoundationH264 ? 1 : 0;
        var hevcHardwareCount = hevcHardware ? 1 : 0;
        var hevcInstalledCount = hevcHardware || hevcInstalled ? 1 : 0;
        return new RecordingCapabilitySnapshot(
            wgc,
            wgc
                ? "Windows.Graphics.Capture API probe: supported on this machine."
                : "Windows.Graphics.Capture API probe: not supported on this machine.",
            direct3D11
                ? new Direct3D11DeviceProbe(true, 0xB100, "Direct3D11 device probe: hardware device created with BGRA support at feature level 11.1.")
                : new Direct3D11DeviceProbe(false, 0, "Direct3D11 device probe failed: HRESULT 0x887A0004."),
            new EncoderProbeResult(true, h264HardwareCount, string.Empty),
            new EncoderProbeResult(true, h264InstalledCount, string.Empty),
            new EncoderProbeResult(true, hevcHardwareCount, string.Empty),
            new EncoderProbeResult(true, hevcInstalledCount, string.Empty),
            ffmpeg
                ? new FfmpegEncoderProbe("C:\\tools\\ffmpeg.exe", [], ["libx264"], "FFmpeg fallback encoder probe: C:\\tools\\ffmpeg.exe; software encoders reported: libx264.")
                : new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found on PATH and GOATSHOT_FFMPEG_PATH is not set."));
    }
}

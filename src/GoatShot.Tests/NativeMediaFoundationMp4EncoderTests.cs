using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class NativeMediaFoundationMp4EncoderTests
{
    [TestMethod]
    public void CanEncodeVideoOnly_AcceptsProductionVideoOnlyPlan()
    {
        var plan = BuildProductionPlan(new RecordingSettings());
        var settings = RecordingSettingsNormalizer.Normalize(new RecordingSettings());

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeVideoOnly(plan, settings, out var reason);

        Assert.IsTrue(canEncode);
        StringAssert.Contains(reason, "video-only");
    }

    [TestMethod]
    public void CanEncodeVideoOnly_RejectsProductionPlanWhenAudioIsRequested()
    {
        var requested = new RecordingSettings { IncludeMicrophone = true };
        var plan = BuildProductionPlan(requested, productionAudioMixingImplemented: true);
        var settings = RecordingSettingsNormalizer.Normalize(requested);

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeVideoOnly(plan, settings, out var reason);

        Assert.IsFalse(canEncode);
        StringAssert.Contains(reason, "audio");
    }

    [TestMethod]
    public void CanEncodeProduction_AcceptsCapturedAudioPayloads()
    {
        var requested = new RecordingSettings { IncludeMicrophone = true };
        var plan = BuildProductionPlan(requested, productionAudioMixingImplemented: true);
        var settings = RecordingSettingsNormalizer.Normalize(requested);
        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeProduction(plan, settings, 1, out var reason);

        Assert.IsTrue(canEncode);
        StringAssert.Contains(reason, "AAC audio");
    }

    [TestMethod]
    public void CanEncodeVideoOnly_RejectsProductionPlanWhenWebcamIsRequested()
    {
        var requested = new RecordingSettings { EnableWebcamOverlay = true };
        var plan = BuildProductionPlan(requested, productionWebcamCompositorImplemented: true);
        var settings = RecordingSettingsNormalizer.Normalize(requested);

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeVideoOnly(plan, settings, out var reason);

        Assert.IsFalse(canEncode);
        StringAssert.Contains(reason, "webcam");
    }

    [TestMethod]
    public void CanEncodeProduction_AcceptsPrecompositedWebcamOverlay()
    {
        var requested = new RecordingSettings { EnableWebcamOverlay = true };
        var plan = BuildProductionPlan(requested, productionWebcamCompositorImplemented: true);
        var settings = RecordingSettingsNormalizer.Normalize(requested);

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeProduction(plan, settings, 0, out var reason);

        Assert.IsTrue(canEncode);
        StringAssert.Contains(reason, "precomposited webcam");
    }

    [TestMethod]
    public void ProductionMp4Route_WithAudioAndWebcam_StreamsWithoutImageFrameSpool()
    {
        var requested = new RecordingSettings
        {
            IncludeMicrophone = true,
            IncludeSystemAudio = true,
            EnableWebcamOverlay = true
        };
        var plan = BuildProductionPlan(
            requested,
            productionAudioMixingImplemented: true,
            productionWebcamCompositorImplemented: true);
        var normalized = RecordingSettingsNormalizer.Normalize(requested);

        var architecture = RecordingService.SelectMp4CaptureArchitecture(plan);
        var features = RecordingService.BuildProductionFeatureRouting(normalized);

        Assert.AreEqual(RecordingService.Mp4CaptureArchitecture.StreamingMediaFoundation, architecture);
        Assert.IsTrue(features.ReserveAudioStream, "Requested microphone/system audio must reserve the streaming AAC lane.");
        Assert.IsTrue(features.PrecomposeWebcam, "Requested webcam frames must be composited before each streamed video frame.");
        Assert.IsFalse(features.UsesTemporaryImageFrameSpool, "The production route must not create PNG frame batches.");
    }

    [TestMethod]
    public void FfmpegFallbackMp4Route_UsesIncrementalRawVideoLane()
    {
        var productionPlan = BuildProductionPlan(new RecordingSettings());
        var fallbackPlan = productionPlan with { Choice = RecordingEngineChoice.FfmpegFallback };

        var architecture = RecordingService.SelectMp4CaptureArchitecture(fallbackPlan);

        Assert.AreEqual(RecordingService.Mp4CaptureArchitecture.StreamingRawVideoFfmpeg, architecture);
    }

    private static RecordingEnginePlan BuildProductionPlan(
        RecordingSettings settings,
        bool productionAudioMixingImplemented = false,
        bool productionWebcamCompositorImplemented = false)
    {
        return RecordingEnginePlanner.BuildPlan(
            settings,
            new RecordingCapabilitySnapshot(
                true,
                "Windows.Graphics.Capture API probe: supported on this machine.",
                new Direct3D11DeviceProbe(true, 0xB100, "Direct3D11 device probe: hardware device created with BGRA support at feature level 11.1."),
                new EncoderProbeResult(true, 1, string.Empty),
                new EncoderProbeResult(true, 1, string.Empty),
                new EncoderProbeResult(true, 0, string.Empty),
                new EncoderProbeResult(true, 0, string.Empty),
                new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found on PATH and GOATSHOT_FFMPEG_PATH is not set.")),
            productionEngineImplemented: true,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: productionAudioMixingImplemented,
            productionWebcamCompositorImplemented: productionWebcamCompositorImplemented);
    }

}

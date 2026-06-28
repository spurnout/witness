using GoatShot.App.Models;
using GoatShot.App.Services;
using NAudio.Wave;

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
        var inputs = new[]
        {
            new NativeMediaFoundationMp4Encoder.NativeMp4AudioInput(AudioCaptureSource.Microphone, "microphone.wav")
        };

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeProduction(plan, settings, inputs, out var reason);

        Assert.IsTrue(canEncode);
        StringAssert.Contains(reason, "AAC audio");
    }

    [TestMethod]
    public void BuildNativeAudioPayload_MixesMonoWavToStereoPcm()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wav = Path.Combine(root, "tone.wav");
        try
        {
            WriteMonoPcm16Wav(wav);

            var payload = NativeMediaFoundationMp4Encoder.BuildNativeAudioPayload(
                [new NativeMediaFoundationMp4Encoder.NativeMp4AudioInput(AudioCaptureSource.Microphone, wav)]);

            Assert.IsNotNull(payload);
            Assert.AreEqual(48_000, payload.SampleRate);
            Assert.AreEqual(2, payload.Channels);
            Assert.AreEqual(16, payload.BitsPerSample);
            Assert.AreEqual(1, payload.SourceCount);
            Assert.IsTrue(payload.Pcm16.Length > 0);
            Assert.AreEqual(0, payload.Pcm16.Length % 4);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

        var canEncode = NativeMediaFoundationMp4Encoder.CanEncodeProduction(plan, settings, [], out var reason);

        Assert.IsTrue(canEncode);
        StringAssert.Contains(reason, "precomposited webcam");
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

    private static void WriteMonoPcm16Wav(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(48_000, 16, 1));
        for (var i = 0; i < 480; i++)
        {
            var sample = (short)(Math.Sin(i / 12d) * short.MaxValue * 0.25);
            writer.WriteByte((byte)(sample & 0xFF));
            writer.WriteByte((byte)((sample >> 8) & 0xFF));
        }
    }
}

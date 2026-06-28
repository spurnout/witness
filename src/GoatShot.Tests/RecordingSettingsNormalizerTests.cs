using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingSettingsNormalizerTests
{
    [TestMethod]
    public void Normalize_ClampsFallbackFpsAndDimensions()
    {
        var settings = new RecordingSettings
        {
            QualityProfile = "High",
            FramesPerSecond = 240,
            TargetWidth = 9000,
            TargetHeight = 1,
            BitrateKbps = 200_000,
            UseVariableBitrate = false,
            MicrophoneGain = 99d,
            SystemAudioGain = -1d,
            NoiseGateThresholdDb = 12d
        };

        var normalized = RecordingSettingsNormalizer.Normalize(settings);

        Assert.AreEqual("High quality", normalized.QualityProfile);
        Assert.AreEqual(RecordingSettingsNormalizer.MaxFallbackFps, normalized.FramesPerSecond);
        Assert.AreEqual(RecordingSettingsNormalizer.MaxTargetWidth, normalized.TargetWidth);
        Assert.AreEqual(16, normalized.TargetHeight);
        Assert.AreEqual(120_000, normalized.BitrateKbps);
        Assert.IsFalse(normalized.UseVariableBitrate);
        Assert.AreEqual(AudioSampleProcessor.MaxGain, normalized.MicrophoneGain);
        Assert.AreEqual(0d, normalized.SystemAudioGain);
        Assert.AreEqual(0d, normalized.NoiseGateThresholdDb);
    }

    [TestMethod]
    public void Normalize_ClampsOverlayBadgeSizeAndNormalizesStyle()
    {
        var large = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            RecordingOverlayBadgeFontSize = 99,
            RecordingOverlayStyle = "high-contrast"
        });
        var small = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            RecordingOverlayBadgeFontSize = 4,
            RecordingOverlayStyle = "minimal"
        });

        Assert.AreEqual(32, large.RecordingOverlayBadgeFontSize);
        Assert.AreEqual("HighContrast", large.RecordingOverlayStyle);
        Assert.AreEqual(10, small.RecordingOverlayBadgeFontSize);
        Assert.AreEqual("Subtle", small.RecordingOverlayStyle);
    }

    [TestMethod]
    public void NormalizeOverlayPosition_AcceptsHumanAliasesAndFallsBack()
    {
        Assert.AreEqual("TopRight", RecordingSettingsNormalizer.NormalizeOverlayPosition("top-right", "TopLeft"));
        Assert.AreEqual("BottomCenter", RecordingSettingsNormalizer.NormalizeOverlayPosition("bottom center", "TopLeft"));
        Assert.AreEqual("BottomLeft", RecordingSettingsNormalizer.NormalizeOverlayPosition("not-a-place", "BottomLeft"));
    }

    [TestMethod]
    public void Normalize_MapsQualityToCrf()
    {
        Assert.AreEqual(30, RecordingSettingsNormalizer.Normalize(new RecordingSettings { QualityProfile = "small" }).Crf);
        Assert.AreEqual(23, RecordingSettingsNormalizer.Normalize(new RecordingSettings { QualityProfile = "balanced" }).Crf);
        Assert.AreEqual(18, RecordingSettingsNormalizer.Normalize(new RecordingSettings { QualityProfile = "high quality" }).Crf);
        Assert.AreEqual(12, RecordingSettingsNormalizer.Normalize(new RecordingSettings { QualityProfile = "archive" }).Crf);
    }

    [TestMethod]
    public void Normalize_AllowsV1SixtyFpsProfiles()
    {
        var normalized = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            QualityProfile = "Archive",
            FramesPerSecond = 60,
            TargetWidth = 3840,
            TargetHeight = 2160,
            BitrateKbps = 45_000,
            UseVariableBitrate = false
        });

        Assert.AreEqual(60, normalized.FramesPerSecond);
        Assert.AreEqual(3840, normalized.TargetWidth);
        Assert.AreEqual(2160, normalized.TargetHeight);
        Assert.AreEqual("45000 kbps", normalized.BitrateLabel);
    }

    [TestMethod]
    public void Normalize_PreservesHevcPreferenceInSummary()
    {
        var normalized = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            PreferHevcEncoding = true
        });

        Assert.IsTrue(normalized.PreferHevcEncoding);
        StringAssert.Contains(RecordingSettingsNormalizer.FormatSummary(normalized), "HEVC preferred");
    }

    [TestMethod]
    public void FormatSummary_NamesFallbackAudioMuxingAndWebcamDirectShowAttempt()
    {
        var normalized = RecordingSettingsNormalizer.Normalize(new RecordingSettings
        {
            IncludeMicrophone = true,
            IncludeSystemAudio = true,
            EnableWebcamOverlay = true,
            MicrophoneGain = 1.5d,
            SystemAudioMuted = true,
            NoiseGateThresholdDb = -55d
        });

        var summary = RecordingSettingsNormalizer.FormatSummary(normalized);

        StringAssert.Contains(summary, "microphone");
        StringAssert.Contains(summary, "system audio");
        StringAssert.Contains(summary, "WASAPI WAV inputs");
        StringAssert.Contains(summary, "mixed into the MP4");
        StringAssert.Contains(summary, "DirectShow webcam overlay");
        StringAssert.Contains(summary, "matching camera can be probed");
        StringAssert.Contains(summary, "mic gain 1.5x");
        StringAssert.Contains(summary, "noise gate -55 dB");
        StringAssert.Contains(summary, "system audio muted");
    }
}

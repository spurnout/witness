using GoatShot.App.Models;
using GoatShot.App.Services;
using GoatShot.App.Windows;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingSummaryTileServiceTests
{
    [TestMethod]
    public void Create_SummarizesDefaultSettings()
    {
        var tiles = new RecordingSummaryTileService().Create(new RecordingSettings());

        Assert.AreEqual(3, tiles.Count);
        Assert.AreEqual("Active profile", tiles[0].Title);
        Assert.AreEqual("Balanced", tiles[0].Value);
        Assert.AreEqual("30 fps, H.264", tiles[0].Detail);
        Assert.AreEqual("Ready", tiles[0].Tone);
        Assert.AreEqual("Audio inputs", tiles[1].Title);
        Assert.AreEqual("0", tiles[1].Value);
        Assert.AreEqual("No audio inputs requested", tiles[1].Detail);
        Assert.AreEqual("Neutral", tiles[1].Tone);
        Assert.AreEqual("Camera & overlays", tiles[2].Title);
        Assert.AreEqual("3", tiles[2].Value);
        Assert.AreEqual("Countdown, Border, Timer", tiles[2].Detail);
        Assert.AreEqual("Neutral", tiles[2].Tone);
    }

    [TestMethod]
    public void Create_CountsAudioInputsAndNamesMutedTracks()
    {
        var tiles = new RecordingSummaryTileService().Create(new RecordingSettings
        {
            FramesPerSecond = 60,
            PreferHevcEncoding = true,
            IncludeMicrophone = true,
            MicrophoneMuted = true,
            IncludeSystemAudio = true
        });

        Assert.AreEqual("60 fps, HEVC preferred", tiles[0].Detail);
        Assert.AreEqual("2", tiles[1].Value);
        Assert.AreEqual("Microphone muted, System audio on", tiles[1].Detail);
        Assert.AreEqual("Ready", tiles[1].Tone);
    }

    [TestMethod]
    public void Create_WebcamOverlayDrivesCameraTileTone()
    {
        var tiles = new RecordingSummaryTileService().Create(new RecordingSettings
        {
            EnableWebcamOverlay = true,
            ShowCountdown = false,
            ShowRecordingBorder = false,
            ShowRecordingTimer = false,
            ShowKeystrokeOverlay = false
        });

        Assert.AreEqual("1", tiles[2].Value);
        Assert.AreEqual("Webcam", tiles[2].Detail);
        Assert.AreEqual("Ready", tiles[2].Tone);
    }

    [TestMethod]
    public void Create_ReportsNoOverlaysWhenEverythingIsDisabled()
    {
        var tiles = new RecordingSummaryTileService().Create(new RecordingSettings
        {
            ShowCountdown = false,
            ShowRecordingBorder = false,
            ShowRecordingTimer = false
        });

        Assert.AreEqual("0", tiles[2].Value);
        Assert.AreEqual("None enabled", tiles[2].Detail);
        Assert.AreEqual("Neutral", tiles[2].Tone);
    }

    [TestMethod]
    public void Create_FallsBackToBalancedProfileWhenBlank()
    {
        var tiles = new RecordingSummaryTileService().Create(new RecordingSettings
        {
            QualityProfile = "   "
        });

        Assert.AreEqual("Balanced", tiles[0].Value);
    }
}

[TestClass]
public sealed class SettingsSectionScrollSpyTests
{
    [TestMethod]
    public void PickActiveSectionKey_AllTopsBelowScrollLine_ReturnsFirstKey()
    {
        var active = SettingsSectionScrollSpy.PickActiveSectionKey(
            new[] { ("General", 100d), ("Recording", 450d), ("Sharing", 900d) },
            SettingsSectionScrollSpy.ActivationThreshold);

        Assert.AreEqual("General", active);
    }

    [TestMethod]
    public void PickActiveSectionKey_SeveralSectionsScrolledPast_ReturnsLastMatch()
    {
        var active = SettingsSectionScrollSpy.PickActiveSectionKey(
            new[] { ("General", -900d), ("Recording", -450d), ("Sharing", 40d), ("Automation", 320d) },
            SettingsSectionScrollSpy.ActivationThreshold);

        Assert.AreEqual("Sharing", active);
    }

    [TestMethod]
    public void PickActiveSectionKey_UnloadedTargets_FallBackToFirstKey()
    {
        var active = SettingsSectionScrollSpy.PickActiveSectionKey(
            new[]
            {
                ("General", double.PositiveInfinity),
                ("Recording", double.PositiveInfinity),
                ("Sharing", double.PositiveInfinity)
            },
            SettingsSectionScrollSpy.ActivationThreshold);

        Assert.AreEqual("General", active);
    }

    [TestMethod]
    public void PickActiveSectionKey_EmptyList_ReturnsNull()
    {
        var active = SettingsSectionScrollSpy.PickActiveSectionKey(
            Array.Empty<(string Key, double Top)>(),
            SettingsSectionScrollSpy.ActivationThreshold);

        Assert.IsNull(active);
    }
}

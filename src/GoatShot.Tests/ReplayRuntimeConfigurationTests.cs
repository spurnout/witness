using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayRuntimeConfigurationTests
{
    [TestMethod]
    public void Fingerprint_ChangesForLiveCaptureAndBufferSettings()
    {
        var settings = CreateSettings();
        var original = AppServices.BuildReplayConfigurationFingerprint(settings);

        settings.Replay.BufferDuration = TimeSpan.FromSeconds(90);
        Assert.AreNotEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));

        settings = CreateSettings();
        settings.Replay.CaptureSource = new ReplayCaptureSourceDescriptor(
            ReplayCaptureSourceKind.FixedRegion,
            "region-1",
            "Evidence region",
            new ReplayCaptureBounds(10, 20, 800, 600));
        Assert.AreNotEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));

        settings = CreateSettings();
        settings.Recording.QualityProfile = "High quality";
        Assert.AreNotEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));

        settings = CreateSettings();
        settings.IncludeCursor = false;
        Assert.AreNotEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));
    }

    [TestMethod]
    public void Fingerprint_DoesNotRestartForSaveAnalysisOrHotkeyOnlySettings()
    {
        var settings = CreateSettings();
        var original = AppServices.BuildReplayConfigurationFingerprint(settings);

        settings.Replay.SaveDuration = TimeSpan.FromSeconds(15);
        settings.Replay.EnableSceneIndexing = false;
        settings.Replay.EnableLocalOcrIndexing = false;
        settings.Replay.AnalysisSensitivity = 0.2d;
        settings.Replay.ToggleHotkey = "Alt+F10";
        settings.Replay.SaveHotkey = "Alt+F11";
        settings.Replay.AutoArmAtSignIn = true;

        Assert.AreEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));
    }

    [TestMethod]
    public void Fingerprint_TreatsPrivacyProcessOrderAsEquivalent()
    {
        var settings = CreateSettings();
        settings.Replay.PrivacyExcludedProcessNames = ["PasswordManager", "BankApp"];
        var original = AppServices.BuildReplayConfigurationFingerprint(settings);

        settings.Replay.PrivacyExcludedProcessNames = ["bankapp", "passwordmanager"];

        Assert.AreEqual(original, AppServices.BuildReplayConfigurationFingerprint(settings));
    }

    private static AppSettings CreateSettings() => new()
    {
        IncludeCursor = true,
        Recording = new RecordingSettings
        {
            QualityProfile = "Balanced",
            FramesPerSecond = 30
        },
        Replay = new ReplayBufferSettings
        {
            BufferDuration = TimeSpan.FromSeconds(60),
            SegmentDuration = TimeSpan.FromSeconds(2),
            SaveDuration = TimeSpan.FromSeconds(60),
            FramesPerSecond = 30,
            CaptureSource = ReplayCaptureSourceDescriptor.FollowCursorMonitor()
        }
    };
}

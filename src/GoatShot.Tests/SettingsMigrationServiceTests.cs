using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SettingsMigrationServiceTests
{
    [TestMethod]
    public void Migrate_RebrandsOnlyKnownLegacyDefaults()
    {
        var settings = new AppSettings
        {
            SlackMessageTemplate = "GoatShot capture ready: {file} ({bytes} bytes)",
            DiscordMessageTemplate = "My custom GoatShot archive: {file}",
            S3KeyPrefix = "goatshot/",
            DropboxRemoteFolder = "/GoatShot",
            OneDriveRemoteFolder = "/GoatShot"
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("Receipts capture ready: {file} ({bytes} bytes)", settings.SlackMessageTemplate);
        Assert.AreEqual("My custom GoatShot archive: {file}", settings.DiscordMessageTemplate);
        Assert.AreEqual("receipts/", settings.S3KeyPrefix);
        Assert.AreEqual("/Receipts", settings.DropboxRemoteFolder);
        Assert.AreEqual("/Receipts", settings.OneDriveRemoteFolder);
    }

    [TestMethod]
    public void Migrate_CarriesCustomizedLegacyReplayHotkeysIntoTheKeybindCatalog()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 16,
            Keybinds = null!
        };
        settings.Replay.ToggleHotkey = "Alt+F10";
        settings.Replay.SaveHotkey = "Alt+F11";

        SettingsMigrationService.Migrate(settings);

        var resolved = KeybindCatalog.Resolve(settings.Keybinds);
        Assert.AreEqual("Alt+F10", resolved.Single(keybind => keybind.Action == HotkeyAction.ToggleReplay).Gesture);
        Assert.AreEqual("Alt+F11", resolved.Single(keybind => keybind.Action == HotkeyAction.SaveReplay).Gesture);
    }

    [TestMethod]
    public void Migrate_NormalizesAnUnusablePostCaptureActionToQuietCopy()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 17,
            PostCaptureAction = "   "
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("CopyQuietly", settings.PostCaptureAction);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }

    [TestMethod]
    public void Migrate_KeepsAnExplicitPostCaptureChoiceAndCanonicalizesItsCasing()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 17,
            PostCaptureAction = "showactionswindow"
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("ShowActionsWindow", settings.PostCaptureAction);
    }

    [TestMethod]
    public void NewSettings_DefaultToQuietCopyWithHoverAutoSelectOn()
    {
        var settings = new AppSettings();

        Assert.AreEqual("CopyQuietly", settings.PostCaptureAction);
        Assert.IsTrue(settings.EnableCaptureHoverAutoSelect);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }

    [TestMethod]
    public void NewSettings_DefaultToBackgroundOcrIndexingOn()
    {
        var settings = new AppSettings();

        Assert.IsTrue(settings.EnableOcrIndexing);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }

    [TestMethod]
    public void Migrate_LeavesDefaultReplayHotkeysOutOfTheStoredOverrides()
    {
        var settings = new AppSettings { SettingsSchemaVersion = 16 };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual(0, settings.Keybinds.Count, "Unchanged defaults should not be persisted as overrides.");
    }

    [TestMethod]
    public void Migrate_DoesNotResurrectLegacyReplayHotkeysAfterTheUserUnbindsThem()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = SettingsMigrationService.CurrentSchemaVersion,
            Keybinds =
            [
                new KeybindAssignment { Action = HotkeyAction.ToggleReplay, Gesture = string.Empty }
            ]
        };
        settings.Replay.ToggleHotkey = "Alt+F10";

        SettingsMigrationService.Migrate(settings);

        var resolved = KeybindCatalog.Resolve(settings.Keybinds);
        Assert.IsFalse(resolved.Single(keybind => keybind.Action == HotkeyAction.ToggleReplay).IsBound);
    }

    [TestMethod]
    public void Migrate_MirrorsResolvedReplayGesturesBackOntoReplaySettings()
    {
        var settings = new AppSettings
        {
            Keybinds =
            [
                new KeybindAssignment { Action = HotkeyAction.SaveReplay, Gesture = "ctrl+alt+F9" }
            ]
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("Ctrl+Alt+F9", settings.Replay.SaveHotkey);
    }

    [TestMethod]
    public void Migrate_AddsCurrentVersionAndDefaultRoadmapSettings()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 0,
            Recording = null!,
            OAuth = null!,
            UploadQueue = null!,
            ManagedPolicy = null!,
            AutomationRules = null!,
            WatchFolders = null!,
            TrustedPluginIds = null!,
            EnabledPluginIds = null!,
            AllowedPluginActionIds = null!,
            UploadDenylistProcesses = null!,
            AiDenylistProcesses = null!,
            RecordingProfiles = null!
        };

        var result = SettingsMigrationService.Migrate(settings);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
        Assert.IsNotNull(settings.Recording);
        Assert.IsNotNull(settings.OAuth);
        Assert.IsNotNull(settings.UploadQueue);
        Assert.IsNotNull(settings.ManagedPolicy);
        Assert.AreEqual(0, settings.ManagedPolicy.AllowedShareDestinations.Count);
        Assert.AreEqual(0, settings.CaptureContextPadding);
        Assert.IsNotNull(settings.AutomationRules);
        Assert.IsNotNull(settings.WatchFolders);
        Assert.IsFalse(settings.EnableVirtualPrinterImport);
        Assert.AreEqual(string.Empty, settings.VirtualPrinterImportFolder);
        Assert.IsFalse(settings.VirtualPrinterImportIncludeSubdirectories);
        Assert.IsFalse(settings.EnableLocalPlugins);
        Assert.IsNotNull(settings.TrustedPluginIds);
        Assert.IsNotNull(settings.EnabledPluginIds);
        Assert.IsNotNull(settings.AllowedPluginActionIds);
        Assert.AreEqual(0, settings.TrustedPluginIds.Count);
        Assert.AreEqual(0, settings.EnabledPluginIds.Count);
        Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
        Assert.IsTrue(settings.Recording.PreferProductionCaptureEngine);
        Assert.IsFalse(settings.Recording.PreferHevcEncoding);
        Assert.AreEqual("Balanced", settings.Recording.QualityProfile);
        Assert.AreEqual(1d, settings.Recording.MicrophoneGain);
        Assert.AreEqual(1d, settings.Recording.SystemAudioGain);
        Assert.AreEqual(-96d, settings.Recording.NoiseGateThresholdDb);
        Assert.AreEqual("TopLeft", settings.Recording.RecordingTimerPosition);
        Assert.AreEqual("BottomLeft", settings.Recording.KeystrokeOverlayPosition);
        Assert.AreEqual(16, settings.Recording.RecordingOverlayBadgeFontSize);
        Assert.AreEqual("Neon", settings.Recording.RecordingOverlayStyle);
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "Small");
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "Balanced");
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "High quality");
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "Small Share");
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "1080p60");
        CollectionAssert.Contains(settings.RecordingProfiles.Select(profile => profile.Name).ToList(), "4K60");
        var profile1080p60 = settings.RecordingProfiles.Single(profile => profile.Name == "1080p60");
        Assert.AreEqual(60, profile1080p60.Settings.FramesPerSecond);
        Assert.AreEqual(1920, profile1080p60.Settings.TargetWidth);
        Assert.AreEqual(1080, profile1080p60.Settings.TargetHeight);
        Assert.AreEqual(12_000, profile1080p60.Settings.BitrateKbps);
        var profile4K60 = settings.RecordingProfiles.Single(profile => profile.Name == "4K60");
        Assert.AreEqual(60, profile4K60.Settings.FramesPerSecond);
        Assert.AreEqual(3840, profile4K60.Settings.TargetWidth);
        Assert.AreEqual(2160, profile4K60.Settings.TargetHeight);
        Assert.AreEqual(45_000, profile4K60.Settings.BitrateKbps);
        Assert.IsTrue(settings.UploadQueue.RetryFailedUploads);
        Assert.AreEqual("https://dev.azure.com", settings.AzureDevOpsBaseUrl);
        Assert.AreEqual("Bug", settings.AzureDevOpsWorkItemType);
        Assert.AreEqual("Receipts capture: {file}", settings.AzureDevOpsTitleTemplate);
        Assert.AreEqual("https://www.googleapis.com/upload/youtube/v3", settings.YouTubeUploadApiBaseUrl);
        Assert.AreEqual("Receipts recording: {file}", settings.YouTubeTitleTemplate);
        Assert.AreEqual("Uploaded from Receipts capture {id}.", settings.YouTubeDescriptionTemplate);
        Assert.AreEqual("unlisted", settings.YouTubePrivacyStatus);
        Assert.AreEqual("22", settings.YouTubeCategoryId);
        Assert.AreEqual("https://photoslibrary.googleapis.com/v1/uploads", settings.GooglePhotosUploadApiBaseUrl);
        Assert.AreEqual("https://photoslibrary.googleapis.com/v1", settings.GooglePhotosApiBaseUrl);
        Assert.AreEqual("Receipts capture: {file}", settings.GooglePhotosDescriptionTemplate);
        Assert.AreEqual("https://graph.microsoft.com/v1.0", settings.OneNoteGraphApiBaseUrl);
        Assert.AreEqual("Receipts capture: {file}", settings.OneNotePageTitleTemplate);
        CollectionAssert.Contains(settings.OAuth.Providers.Select(provider => provider.ProviderName).ToList(), "Google Photos");
        CollectionAssert.Contains(settings.OAuth.Providers.Select(provider => provider.ProviderName).ToList(), "YouTube");
        CollectionAssert.Contains(settings.OAuth.Providers.Select(provider => provider.ProviderName).ToList(), "OneNote");
        Assert.AreEqual("gemini-3.5-flash", settings.GeminiSpeechToTextModelId);
    }

    [TestMethod]
    public void Migrate_DoesNotOverwriteExistingRecordingChoices()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = SettingsMigrationService.CurrentSchemaVersion,
            Recording = new RecordingSettings
            {
                QualityProfile = "High quality",
                FramesPerSecond = 60,
                IncludeMicrophone = true,
                RecordingTimerPosition = "TopRight",
                KeystrokeOverlayPosition = "BottomCenter",
                RecordingOverlayBadgeFontSize = 20,
                RecordingOverlayStyle = "HighContrast"
            },
            RecordingProfiles =
            [
                new RecordingWorkflowProfile
                {
                    Name = "Support",
                    Description = "Existing support profile",
                    Settings = new RecordingSettings
                    {
                        QualityProfile = "Small",
                        FramesPerSecond = 12,
                        TargetWidth = 1280,
                        TargetHeight = 720,
                        RecordingTimerPosition = "BottomRight",
                        KeystrokeOverlayPosition = "TopCenter",
                        RecordingOverlayBadgeFontSize = 18,
                        RecordingOverlayStyle = "Subtle"
                    }
                }
            ]
        };

        var result = SettingsMigrationService.Migrate(settings);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual("High quality", settings.Recording.QualityProfile);
        Assert.AreEqual(60, settings.Recording.FramesPerSecond);
        Assert.IsTrue(settings.Recording.IncludeMicrophone);
        Assert.AreEqual("TopRight", settings.Recording.RecordingTimerPosition);
        Assert.AreEqual("BottomCenter", settings.Recording.KeystrokeOverlayPosition);
        Assert.AreEqual(20, settings.Recording.RecordingOverlayBadgeFontSize);
        Assert.AreEqual("HighContrast", settings.Recording.RecordingOverlayStyle);
        var support = settings.RecordingProfiles.Single(profile => profile.Name == "Support");
        Assert.AreEqual("Small", support.Settings.QualityProfile);
        Assert.AreEqual(12, support.Settings.FramesPerSecond);
        Assert.AreEqual("BottomRight", support.Settings.RecordingTimerPosition);
        Assert.AreEqual("TopCenter", support.Settings.KeystrokeOverlayPosition);
        Assert.AreEqual(18, support.Settings.RecordingOverlayBadgeFontSize);
        Assert.AreEqual("Subtle", support.Settings.RecordingOverlayStyle);
        Assert.IsTrue(settings.RecordingProfiles.Any(profile => profile.Name == "Small Share"));
        Assert.IsTrue(settings.RecordingProfiles.Any(profile => profile.Name == "1080p60"));
        Assert.IsTrue(settings.RecordingProfiles.Any(profile => profile.Name == "4K60"));
        Assert.IsTrue(settings.OAuth.Providers.Count >= 6);
    }

    [TestMethod]
    public void Migrate_ClampsCaptureContextPadding()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = SettingsMigrationService.CurrentSchemaVersion,
            CaptureContextPadding = 500
        };

        var result = SettingsMigrationService.Migrate(settings);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(CaptureOverlayGeometry.MaxContextPadding, settings.CaptureContextPadding);
    }
}

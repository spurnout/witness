using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AutomationServiceTests
{
    [TestMethod]
    public void EvaluateRule_UsesCurrentCaptureMetadataAndSensitiveText()
    {
        var item = CreateItem(
            sourceApp: "chrome.exe",
            windowTitle: "Checkout - Edge",
            monitorName: @"\\.\DISPLAY1",
            ocrText: "Customer email: test@example.com");
        var rule = new AutomationRule
        {
            Name = "Sensitive checkout capture",
            Trigger = AutomationTrigger.CaptureCreated,
            SourceAppContains = "chrome",
            WindowTitleContains = "checkout",
            MonitorContains = "display1",
            CaptureKind = "ActiveWindow",
            FileExtension = "png",
            MinFileSizeBytes = 100,
            MaxFileSizeBytes = 10_000,
            OcrContains = "customer",
            RequiresSensitiveData = true,
            Actions = [AutomationActionKind.ShowNotification]
        };

        var evaluation = AutomationService.EvaluateRule(rule, AutomationTrigger.CaptureCreated, item);

        Assert.IsTrue(evaluation.Matches, string.Join("; ", evaluation.Reasons));
        CollectionAssert.Contains(evaluation.Reasons, "Matched.");
    }

    [TestMethod]
    public void EvaluateRule_ExplainsSensitiveDataExclusion()
    {
        var item = CreateItem(ocrText: "Email: test@example.com");
        var rule = new AutomationRule
        {
            Trigger = AutomationTrigger.CaptureCreated,
            RequiresSensitiveData = false,
            Actions = [AutomationActionKind.ShowNotification]
        };

        var evaluation = AutomationService.EvaluateRule(rule, AutomationTrigger.CaptureCreated, item);

        Assert.IsFalse(evaluation.Matches);
        StringAssert.Contains(
            string.Join(" ", evaluation.Reasons),
            "excludes captures with sensitive OCR/text findings");
    }

    [TestMethod]
    public void EvaluateRule_MatchesHotkeyProfileMetadata()
    {
        var item = CreateItem(hotkeyProfile: "ClientSupport");
        var rule = new AutomationRule
        {
            Trigger = AutomationTrigger.CaptureCreated,
            HotkeyProfile = "clientsupport",
            Actions = [AutomationActionKind.ShowNotification]
        };

        var evaluation = AutomationService.EvaluateRule(rule, AutomationTrigger.CaptureCreated, item);

        Assert.IsTrue(evaluation.Matches, string.Join("; ", evaluation.Reasons));
    }

    [TestMethod]
    public void EvaluateRule_ExplainsMissingHotkeyProfile()
    {
        var item = CreateItem(hotkeyProfile: string.Empty);
        var rule = new AutomationRule
        {
            Trigger = AutomationTrigger.CaptureCreated,
            HotkeyProfile = "ClientSupport",
            Actions = [AutomationActionKind.ShowNotification]
        };

        var evaluation = AutomationService.EvaluateRule(rule, AutomationTrigger.CaptureCreated, item);

        Assert.IsFalse(evaluation.Matches);
        StringAssert.Contains(
            string.Join(" ", evaluation.Reasons),
            "Hotkey profile is not set");
    }

    [TestMethod]
    public async Task RunRulesAsync_DryRunPlansActionsAndIncludesSkippedReasons()
    {
        await WithTempServicesAsync(async services =>
        {
            services.Settings.AutomationRules =
            [
                new AutomationRule
                {
                    Id = "matching",
                    Name = "Matching rule",
                    Trigger = AutomationTrigger.CaptureCreated,
                    Actions = [AutomationActionKind.ShowNotification, AutomationActionKind.SaveToFolder]
                },
                new AutomationRule
                {
                    Id = "different-trigger",
                    Name = "Upload rule",
                    Trigger = AutomationTrigger.UploadCompleted,
                    Actions = [AutomationActionKind.ShowNotification]
                }
            ];

            var result = await services.Automation.RunRulesAsync(
                AutomationTrigger.CaptureCreated,
                CreateItem(),
                dryRun: true);

            Assert.AreEqual(2, result.TotalRules);
            Assert.AreEqual(1, result.MatchingRules);
            Assert.AreEqual(1, result.Rules.Count);
            Assert.AreEqual(2, result.Rules[0].Actions.Count);
            Assert.IsTrue(result.Rules[0].Actions.All(action => !action.Executed));
            Assert.IsTrue(result.Evaluations.Any(evaluation =>
                evaluation.RuleId == "different-trigger" &&
                evaluation.Reasons.Any(reason => reason.Contains("not CaptureCreated", StringComparison.Ordinal))));
        });
    }

    [TestMethod]
    public async Task RunRulesAsync_ExecutesLocalExportNotificationAndDocumentActions()
    {
        await WithTempServicesAsync(async services =>
        {
            services.Settings.LocalExportFolder = Path.Combine(services.Paths.LibraryRoot, "Exports");
            services.Settings.AutomationRules =
            [
                new AutomationRule
                {
                    Id = "local-safe",
                    Name = "Local safe actions",
                    Trigger = AutomationTrigger.CaptureCreated,
                    Actions =
                    [
                        AutomationActionKind.SaveToFolder,
                        AutomationActionKind.GenerateDocument,
                        AutomationActionKind.ShowNotification
                    ]
                }
            ];

            var statuses = new List<string>();
            services.Automation.StatusChanged += (_, message) => statuses.Add(message);
            var item = CreateItem(services.Paths, "local-safe.png");

            var result = await services.Automation.RunRulesAsync(
                AutomationTrigger.CaptureCreated,
                item,
                dryRun: false);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(3, result.Rules[0].Actions.Count);
            Assert.AreEqual(1, Directory.GetFiles(services.Settings.LocalExportFolder, "local-safe*.png").Length);
            Assert.IsTrue(result.Rules[0].Actions.Any(action =>
                action.Action == AutomationActionKind.GenerateDocument &&
                action.Succeeded &&
                !string.IsNullOrWhiteSpace(action.OutputPath) &&
                File.Exists(action.OutputPath)));
            Assert.IsTrue(statuses.Any(status => status.Contains("Automation notification", StringComparison.Ordinal)));
        });
    }

    [TestMethod]
    public async Task RunRulesAsync_AppliesParameterizedImageEffectCopy()
    {
        await WithTempServicesAsync(async services =>
        {
            services.Settings.AutomationRules =
            [
                new AutomationRule
                {
                    Id = "image-effect",
                    Name = "Solid left half",
                    Trigger = AutomationTrigger.CaptureCreated,
                    ImageEffectMode = VisualRedactionMode.Solid,
                    ImageEffectRegion = "0,0,50,100",
                    Actions = [AutomationActionKind.ApplyImageEffect]
                }
            ];

            var imported = new List<CaptureItem>();
            services.Automation.CaptureImported += (_, item) => imported.Add(item);
            var item = CreateImageItem(services.Paths, "effect-source.png");

            var result = await services.Automation.RunRulesAsync(
                AutomationTrigger.CaptureCreated,
                item,
                dryRun: false);

            Assert.IsTrue(result.Succeeded);
            var action = result.Rules.Single().Actions.Single();
            Assert.IsTrue(action.Succeeded, action.Message);
            Assert.IsTrue(File.Exists(action.OutputPath));
            Assert.AreEqual(1, imported.Count);

            using var image = new Bitmap(action.OutputPath!);
            Assert.AreEqual(Color.Black.ToArgb(), image.GetPixel(2, 5).ToArgb());
            Assert.AreEqual(Color.White.ToArgb(), image.GetPixel(15, 5).ToArgb());
        });
    }

    [TestMethod]
    public async Task WorkspaceStore_PersistsAndIndexesHotkeyProfile()
    {
        await WithTempServicesAsync(async services =>
        {
            var sourcePath = Path.Combine(services.Paths.ImagesRoot, "profiled.png");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, Enumerable.Range(0, 512).Select(index => (byte)(index % 255)).ToArray());

            var item = await services.WorkspaceStore.AddImageFileAsync(
                sourcePath,
                CaptureKind.ActiveWindow,
                "Profiled test capture.",
                hotkeyProfile: "ClientSupport");

            var loaded = services.WorkspaceStore.Load().Single(capture => capture.Id == item.Id);
            Assert.AreEqual("ClientSupport", loaded.HotkeyProfile);
            CollectionAssert.Contains(services.WorkspaceIndex.SearchIds("ClientSupport").ToArray(), item.Id);
        });
    }

    [TestMethod]
    public void WorkflowProfiles_CloneAllAutomationRuleConditions()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                new AutomationRule
                {
                    Id = "full-rule",
                    Name = "Full rule",
                    Trigger = AutomationTrigger.OcrCompleted,
                    SourceAppContains = "browser",
                    WindowTitleContains = "checkout",
                    CaptureKind = "ActiveWindow",
                    MonitorContains = "display",
                    HotkeyProfile = "support",
                    FileExtension = ".png",
                    MinFileSizeBytes = 10,
                    MaxFileSizeBytes = 20,
                    OcrContains = "error",
                    RequiresSensitiveData = true,
                    ImageEffectMode = VisualRedactionMode.Pixelate,
                    ImageEffectRegion = "10,20,30,40",
                    Actions = [AutomationActionKind.GenerateDocument]
                }
            ]
        };

        var profile = new WorkflowProfileService(settings, new SettingsStore()).CreateProfile();
        var rule = profile.AutomationRules.Single();

        Assert.AreEqual("ActiveWindow", rule.CaptureKind);
        Assert.AreEqual("display", rule.MonitorContains);
        Assert.AreEqual("support", rule.HotkeyProfile);
        Assert.AreEqual(10, rule.MinFileSizeBytes);
        Assert.AreEqual("error", rule.OcrContains);
        Assert.AreEqual(true, rule.RequiresSensitiveData);
        Assert.AreEqual(VisualRedactionMode.Pixelate, rule.ImageEffectMode);
        Assert.AreEqual("10,20,30,40", rule.ImageEffectRegion);
    }

    [TestMethod]
    public async Task WorkflowProfiles_ExportAndImportRecordingProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profilePath = Path.Combine(root, "recording-profile.json");
            var sourceSettings = new AppSettings
            {
                Recording = new RecordingSettings
                {
                    QualityProfile = "High quality",
                    FramesPerSecond = 24,
                    TargetWidth = 1920,
                    TargetHeight = 1080,
                    MicrophoneGain = 1.4d,
                    SystemAudioGain = 0.75d,
                    NoiseGateThresholdDb = -54d,
                    SystemAudioMuted = true,
                    ShowRecordingTimer = true,
                    RecordingTimerPosition = "TopRight",
                    KeystrokeOverlayPosition = "BottomCenter",
                    RecordingOverlayBadgeFontSize = 20,
                    RecordingOverlayStyle = "HighContrast"
                },
                RecordingProfiles =
                [
                    new RecordingWorkflowProfile
                    {
                        Name = "Docs",
                        Description = "Documentation clips",
                        Settings = new RecordingSettings
                        {
                            QualityProfile = "Small",
                            FramesPerSecond = 10,
                            TargetWidth = 1280,
                            TargetHeight = 720,
                            MicrophoneGain = 1.25d,
                            NoiseGateThresholdDb = -50d,
                            ShowRecordingBorder = true,
                            RecordingTimerPosition = "BottomRight",
                            KeystrokeOverlayPosition = "TopCenter",
                            RecordingOverlayBadgeFontSize = 18,
                            RecordingOverlayStyle = "Subtle"
                        }
                    }
                ]
            };

            var profile = new WorkflowProfileService(sourceSettings, new SettingsStore()).CreateProfile("recording export");
            await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));

            var importedSettings = new AppSettings
            {
                Recording = new RecordingSettings { QualityProfile = "Balanced", FramesPerSecond = 30 },
                RecordingProfiles = []
            };
            var store = new SettingsStore();
            store.UsePath(Path.Combine(root, "settings.json"));
            var result = await new WorkflowProfileService(importedSettings, store).ImportAsync(profilePath);

            Assert.IsTrue(result.Succeeded);
            Assert.IsNotNull(profile.Recording);
            Assert.AreEqual("High quality", profile.Recording.DefaultSettings.QualityProfile);
            Assert.AreEqual("Docs", profile.Recording.Profiles.Single().Name);
            Assert.AreEqual("High quality", importedSettings.Recording.QualityProfile);
            Assert.AreEqual(24, importedSettings.Recording.FramesPerSecond);
            Assert.AreEqual(1.4d, importedSettings.Recording.MicrophoneGain);
            Assert.AreEqual(0.75d, importedSettings.Recording.SystemAudioGain);
            Assert.AreEqual(-54d, importedSettings.Recording.NoiseGateThresholdDb);
            Assert.IsTrue(importedSettings.Recording.SystemAudioMuted);
            Assert.AreEqual("TopRight", importedSettings.Recording.RecordingTimerPosition);
            Assert.AreEqual("BottomCenter", importedSettings.Recording.KeystrokeOverlayPosition);
            Assert.AreEqual(20, importedSettings.Recording.RecordingOverlayBadgeFontSize);
            Assert.AreEqual("HighContrast", importedSettings.Recording.RecordingOverlayStyle);
            var importedProfile = importedSettings.RecordingProfiles.Single(profile => profile.Name == "Docs");
            Assert.AreEqual("Documentation clips", importedProfile.Description);
            Assert.AreEqual("Small", importedProfile.Settings.QualityProfile);
            Assert.AreEqual(1280, importedProfile.Settings.TargetWidth);
            Assert.AreEqual(1.25d, importedProfile.Settings.MicrophoneGain);
            Assert.AreEqual(-50d, importedProfile.Settings.NoiseGateThresholdDb);
            Assert.AreEqual("BottomRight", importedProfile.Settings.RecordingTimerPosition);
            Assert.AreEqual("TopCenter", importedProfile.Settings.KeystrokeOverlayPosition);
            Assert.AreEqual(18, importedProfile.Settings.RecordingOverlayBadgeFontSize);
            Assert.AreEqual("Subtle", importedProfile.Settings.RecordingOverlayStyle);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CaptureItem CreateItem(
        AppPaths? paths = null,
        string fileName = "shot.png",
        string sourceApp = "goatshot.tests",
        string windowTitle = "Test window",
        string monitorName = @"\\.\DISPLAY1",
        string ocrText = "plain text",
        string hotkeyProfile = "")
    {
        var filePath = paths is null
            ? Path.Combine(Path.GetTempPath(), fileName)
            : Path.Combine(paths.ImagesRoot, fileName);
        if (paths is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, Enumerable.Range(0, 512).Select(index => (byte)(index % 255)).ToArray());
        }

        return new CaptureItem
        {
            Kind = CaptureKind.ActiveWindow,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = paths is null ? 512 : new FileInfo(filePath).Length,
            Width = 16,
            Height = 16,
            SourceApp = sourceApp,
            SourceWindowTitle = windowTitle,
            SourceMonitorName = monitorName,
            HotkeyProfile = hotkeyProfile,
            OcrText = ocrText
        };
    }

    private static CaptureItem CreateImageItem(AppPaths paths, string fileName)
    {
        var filePath = Path.Combine(paths.ImagesRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using (var bitmap = new Bitmap(20, 10, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
            }

            bitmap.Save(filePath, ImageFormat.Png);
        }

        return new CaptureItem
        {
            Kind = CaptureKind.ActiveWindow,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = new FileInfo(filePath).Length,
            Width = 20,
            Height = 10,
            SourceApp = "goatshot.tests",
            SourceWindowTitle = "Test window",
            SourceMonitorName = @"\\.\DISPLAY1",
            OcrText = "plain text"
        };
    }

    private static async Task WithTempServicesAsync(Func<AppServices, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            using var services = AppServices.Create();
            await action(services);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }
}

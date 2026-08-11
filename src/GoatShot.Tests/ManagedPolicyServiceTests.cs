using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Data.Sqlite;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManagedPolicyServiceTests
{
    [TestMethod]
    public void LoadEffective_MergesExternalPolicyWithDenyWinsAndDestinationIntersection()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policyPath = Path.Combine(root, "managed-policy.json");
        File.WriteAllText(
            policyPath,
            """
            {
              "disableUploads": true,
              "allowedShareDestinations": ["Local folder", "S3-compatible"]
            }
            """);

        try
        {
            var settings = new AppSettings
            {
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableCustomScripts = true,
                    AllowedShareDestinations = ["Local folder", "Custom webhook"]
                }
            };

            var policy = ManagedPolicyService.LoadEffective(settings, policyPath);

            Assert.IsTrue(policy.DisableUploads);
            Assert.IsTrue(policy.DisableCustomScripts);
            Assert.AreEqual(1, policy.AllowedShareDestinations.Count);
            Assert.AreEqual(ShareDestination.LocalFolder, policy.AllowedShareDestinations[0]);
            StringAssert.Contains(policy.Source, policyPath);
            StringAssert.Contains(policy.Summary, "external sharing/upload disabled");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ShareService_BlocksExternalUploadWhenManagedPolicyDisablesUploads()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                CustomWebhookUrl = "https://example.test/upload",
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableUploads = true
                }
            };
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "policy-blocked-webhook.png", 12);

            var result = await sharing.ShareAsync(item, ShareDestination.CustomWebhook, CancellationToken.None);
            var history = sharing.SearchHistory(destination: ShareDestination.CustomWebhook, limit: 10);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "managed policy");
            Assert.AreEqual(1, history.Count);
            Assert.IsFalse(history[0].Succeeded);
            StringAssert.Contains(history[0].Message, "managed policy");
        });
    }

    [TestMethod]
    public async Task WorkflowDryRun_BlocksCustomScriptAndWebhookWhenPolicyDisablesThem()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                CustomScriptCommand = "Write-Output {file}",
                CustomWebhookUrl = "https://example.test/upload",
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableCustomScripts = true,
                    DisableCustomWebhooks = true
                }
            };
            var service = new WorkflowActionDryRunService(settings, paths);
            var item = CreateCaptureItem(paths, "policy-dry-run.png", 16);

            var script = service.PlanCustomScript(item);
            var webhook = service.PlanCustomWebhook(item);

            Assert.IsFalse(script.WouldExecute);
            Assert.IsFalse(webhook.WouldExecute);
            StringAssert.Contains(script.Message, "Custom scripts are disabled by managed policy");
            StringAssert.Contains(webhook.Message, "Custom webhooks are disabled by managed policy");
            Assert.AreEqual(AutomationActionKind.RunCustomScript, script.Action);
            Assert.AreEqual(AutomationActionKind.CallCustomWebhook, webhook.Action);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ProviderDiagnostics_MarksRestrictedDestinationBlockedByPolicy()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                CustomWebhookUrl = "https://example.test/upload",
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableCustomWebhooks = true
                }
            };
            var diagnostics = new ProviderDiagnosticsService(settings, new SecretStore(paths));

            var webhook = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "Custom webhook");

            Assert.AreEqual("Blocked by policy", webhook.Status);
            Assert.IsFalse(webhook.ReadyForLocalAttempt);
            StringAssert.Contains(webhook.ReadinessSummary, "managed policy");
            Assert.IsTrue(webhook.Notes.Any(note => note.Contains("Custom webhooks are disabled by managed policy", StringComparison.OrdinalIgnoreCase)));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task GeminiProvider_RefusesProviderCallsWhenAiDisabledByPolicy()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                AiEnabled = true,
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableAi = true
                }
            };
            var gemini = new GeminiImageProvider(settings, paths, new SecretStore(paths));

            var result = await gemini.AnalyzeImageAsync(
                Path.Combine(paths.ImagesRoot, "missing.png"),
                "explain",
                settings.GeminiDefaultModelId,
                CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "AI is disabled by managed policy");
        });
    }

    [TestMethod]
    public async Task Diagnostics_ReportsEffectivePolicyState()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableAi = true,
                    DisableUploads = true,
                    AllowedShareDestinations = ["Local folder"]
                }
            };
            var uploadQueue = new UploadQueueService(paths, settings.UploadQueue);
            using var uploadWorker = new UploadQueueWorkerService(
                settings.UploadQueue,
                uploadQueue,
                new ShareService(paths, settings, new SecretStore(paths)));
            var diagnostics = new DiagnosticsService(
                settings,
                paths,
                new SecretStore(paths),
                new StartupRegistrationService(),
                new WorkspaceMetadataIndex(paths),
                uploadQueue,
                uploadWorker,
                new EmptyAudioCaptureService(),
                new EmptyCameraOverlayService());

            var snapshot = diagnostics.GetSnapshot();

            StringAssert.Contains(snapshot.PolicyStatus, "AI disabled");
            StringAssert.Contains(snapshot.PolicyStatus, "external sharing/upload disabled");
            StringAssert.Contains(snapshot.PolicyStatus, "LocalFolder");
            StringAssert.Contains(snapshot.AiStatus, "AI is disabled by managed policy");
            StringAssert.Contains(snapshot.UploadQueueStatus, "blocked by managed policy");
            StringAssert.Contains(snapshot.RecordingEngine, "video hosted-person-mask");
            StringAssert.Contains(snapshot.RecordingEngine, "hash-pinned embedded ONNX model");
            StringAssert.Contains(snapshot.RecordingEngine, "DirectML with CPU fallback");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void LoadEffective_MergesPluginAndLocalAdminControlsWithDenyWins()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policyPath = Path.Combine(root, "admin-policy.json");
        File.WriteAllText(
            policyPath,
            """
            {
              "disableLocalPlugins": true,
              "disableBrowserExtension": true,
              "allowedPluginIds": ["sample.redaction-note"],
              "allowedPluginActionIds": ["sample.redaction-note:write-note"],
              "retentionDays": 14
            }
            """);

        try
        {
            var settings = new AppSettings
            {
                ManagedPolicy = new ManagedPolicySettings
                {
                    AllowedPluginIds = ["sample.redaction-note", "sample.other"],
                    AllowedPluginActionIds = ["sample.redaction-note:write-note", "sample.redaction-note:inspect"],
                    RetentionDays = 7
                }
            };

            var policy = ManagedPolicyService.LoadEffective(settings, policyPath);

            Assert.IsTrue(policy.DisableLocalPlugins);
            Assert.IsTrue(policy.DisableBrowserExtension);
            Assert.AreEqual(7, policy.RetentionDays);
            CollectionAssert.AreEquivalent(new[] { "sample.redaction-note" }, policy.AllowedPluginIds.ToArray());
            CollectionAssert.AreEquivalent(new[] { "sample.redaction-note:write-note" }, policy.AllowedPluginActionIds.ToArray());
            StringAssert.Contains(policy.Summary, "local plugins disabled");
            StringAssert.Contains(policy.Summary, "browser extension disabled");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LocalPluginDryRun_BlocksWhenManagedPolicyDisablesPlugins()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = ["sample.redaction-note"],
                EnabledPluginIds = ["sample.redaction-note"],
                AllowedPluginActionIds = ["sample.redaction-note:*"],
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableLocalPlugins = true
                }
            };
            await WritePluginManifestAsync(paths);
            var service = new LocalPluginService(paths, settings);

            var result = service.DryRunAction("sample.redaction-note", "write-note");

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Local plugins are disabled by managed policy");
        });
    }

    [TestMethod]
    public async Task LocalPluginDryRun_BlocksActionOutsideManagedPolicyAllowlist()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = ["sample.redaction-note"],
                EnabledPluginIds = ["sample.redaction-note"],
                AllowedPluginActionIds = ["sample.redaction-note:*"],
                ManagedPolicy = new ManagedPolicySettings
                {
                    AllowedPluginIds = ["sample.redaction-note"],
                    AllowedPluginActionIds = ["sample.redaction-note:inspect"]
                }
            };
            await WritePluginManifestAsync(paths);
            var service = new LocalPluginService(paths, settings);

            var result = service.DryRunAction("sample.redaction-note", "write-note");

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "not allowed by managed policy");
        });
    }

    [TestMethod]
    public async Task AdminPolicyBundle_ValidateExportImportAndExplainOmitSecrets()
    {
        await WithTempPathsAsync(paths =>
        {
            var bundlePath = Path.Combine(paths.LocalRoot, "admin-policy.json");
            File.WriteAllText(
                bundlePath,
                """
                {
                  "schemaVersion": "goatshot.admin-policy.v1",
                  "name": "Locked support workstation",
                  "description": "No token=super-secret should survive redaction in issues.",
                  "managedPolicy": {
                    "disableAi": true,
                    "disableUploads": true,
                    "disableCustomScripts": true,
                    "disableLocalPlugins": true,
                    "requirePrivateCaptureMode": true,
                    "allowedShareDestinations": ["Local folder"],
                    "allowedPluginIds": ["sample.redaction-note"],
                    "allowedPluginActionIds": ["sample.redaction-note:write-note"],
                    "retentionDays": 30
                  }
                }
                """);
            var settings = new AppSettings
            {
                CustomWebhookUrl = "https://example.test/upload?token=super-secret",
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableCustomWebhooks = true,
                    AllowedShareDestinations = ["Local folder", "Custom webhook"],
                    RetentionDays = 14
                }
            };
            var store = new SettingsStore();
            store.UsePath(paths.SettingsPath);
            var service = new AdminPolicyBundleService();

            var validation = service.ValidateFile(bundlePath);
            var import = service.Import(settings, store, bundlePath);
            var explain = service.Explain(settings);
            var exportPath = Path.Combine(paths.LocalRoot, "exported-admin-policy.json");
            var export = service.Export(settings, exportPath);
            var exported = File.ReadAllText(exportPath);

            Assert.IsTrue(validation.Succeeded, string.Join("; ", validation.Issues));
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Issues));
            Assert.IsTrue(settings.PrivateCaptureMode);
            Assert.IsFalse(settings.EnableLocalPlugins);
            Assert.IsTrue(settings.ManagedPolicy.DisableAi);
            Assert.IsTrue(settings.ManagedPolicy.DisableUploads);
            Assert.IsTrue(settings.ManagedPolicy.DisableCustomWebhooks);
            Assert.AreEqual(14, settings.ManagedPolicy.RetentionDays);
            CollectionAssert.AreEquivalent(new[] { "LocalFolder" }, settings.ManagedPolicy.AllowedShareDestinations.ToArray());
            Assert.IsTrue(explain.Succeeded, string.Join("; ", explain.Issues));
            Assert.IsTrue(export.Succeeded);
            Assert.IsFalse(exported.Contains("super-secret", StringComparison.Ordinal));
            StringAssert.Contains(exported, AdminPolicyBundleService.CurrentSchemaVersion);
            StringAssert.Contains(exported, "disableAi");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ManagedPolicyHelpers_BlockLocalFeatureLanes()
    {
        var settings = new AppSettings
        {
            ManagedPolicy = new ManagedPolicySettings
            {
                DisableAndroidCapture = true,
                DisableBrowserExtension = true,
                DisableVirtualPrinterImport = true
            }
        };

        Assert.IsFalse(ManagedPolicyService.IsAndroidCaptureAllowed(settings, out var androidReason));
        Assert.IsFalse(ManagedPolicyService.IsBrowserExtensionAllowed(settings, out var browserReason));
        Assert.IsFalse(ManagedPolicyService.IsVirtualPrinterImportAllowed(settings, out var printReason));
        StringAssert.Contains(androidReason, "Android capture is disabled");
        StringAssert.Contains(browserReason, "Browser extension handoff is disabled");
        StringAssert.Contains(printReason, "Virtual-printer import is disabled");
    }

    private static CaptureItem CreateCaptureItem(AppPaths paths, string fileName, int bytes)
    {
        var filePath = Path.Combine(paths.ImagesRoot, fileName);
        File.WriteAllBytes(filePath, Enumerable.Range(0, bytes).Select(index => (byte)(index % 255)).ToArray());

        return new CaptureItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CaptureKind.Imported,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = bytes,
            Width = 10,
            Height = 10
        };
    }

    private static async Task WritePluginManifestAsync(AppPaths paths)
    {
        var folder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "plugin.json"),
            """
            {
              "schemaVersion": "goatshot.plugin.v1",
              "id": "sample.redaction-note",
              "name": "Sample Redaction Note",
              "version": "0.1.0",
              "actions": [
                {
                  "id": "write-note",
                  "name": "Write note"
                },
                {
                  "id": "inspect",
                  "name": "Inspect"
                }
              ]
            }
            """);
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            var paths = AppPaths.Create(new AppSettings());
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                SqliteConnection.ClearAllPools();
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                        break;
                    }
                    catch (IOException) when (attempt < 4)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(100);
                    }
                }
            }
        }
    }

    private sealed class EmptyAudioCaptureService : IAudioCaptureService
    {
        public Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AudioCaptureDevice>>(Array.Empty<AudioCaptureDevice>());
        }

        public Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AudioCaptureDevice>>(Array.Empty<AudioCaptureDevice>());
        }

        public Task<AudioCaptureResult> CaptureWavAsync(AudioCaptureRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AudioCaptureResult(false, null, "not available", TimeSpan.Zero, 0, null));
        }

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProviderHealth(false, "not available"));
        }
    }

    private sealed class EmptyCameraOverlayService : ICameraOverlayService
    {
        public Task<IReadOnlyList<CameraOverlayDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CameraOverlayDevice>>(Array.Empty<CameraOverlayDevice>());
        }

        public Task<CameraOverlayFrameResult> CaptureFrameAsync(string deviceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CameraOverlayFrameResult(false, null, null, "not available"));
        }

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProviderHealth(false, "not available"));
        }
    }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RemotePluginPackageServiceTests
{
    [TestMethod]
    public async Task ValidateRegistryAsync_ParsesValidRegistry()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.ValidateRegistryAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.PluginCount);
            Assert.AreEqual("sample.redaction-note", result.Plugins.Single().Id);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("signature", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task ValidateRegistryAsync_AcceptsLegacyGoatShotRegistrySchema()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(
                paths,
                package,
                RemotePluginPackageService.LegacyRegistrySchemaVersion);
            var service = CreateService(paths);

            var result = await service.ValidateRegistryAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.PluginCount);
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_StagesPackageWithoutTrustingOrEnablingPlugin()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings();
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsTrue(File.Exists(result.StagedPackagePath));
            Assert.IsTrue(File.Exists(Path.Combine(result.StagedExtractPath, "plugin.json")));
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
            Assert.AreEqual(0, localPlugins.Discover().Count, "Staged packages must not be active local plugins.");
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_AcceptsLegacyPackagedPluginSchema()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage(
                [("plugin.json", ValidPluginManifest(
                    "sample.redaction-note",
                    "0.2.0",
                    LocalPluginService.LegacySchemaVersion))]);
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_VerifiesSignedPackageBeforeStaging()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = SignPackage(CreatePluginPackage());
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
            Assert.IsTrue(result.SignatureVerified);
            Assert.AreEqual("rsa-pss-sha256", result.SignatureAlgorithm);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SignaturePublicKeyFingerprint));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("signature verified", StringComparison.OrdinalIgnoreCase)));

            var stageManifest = JsonSerializer.Deserialize<RemotePluginStageManifest>(
                await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(result.StagedPackagePath)!, "stage-manifest.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(stageManifest);
            Assert.AreEqual(RemotePluginPackageService.CurrentStageSchemaVersion, stageManifest!.SchemaVersion);
            Assert.IsTrue(stageManifest!.SignatureVerified);
            Assert.AreEqual(result.SignaturePublicKeyFingerprint, stageManifest.SignaturePublicKeyFingerprint);
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_RejectsInvalidPackageSignature()
    {
        await WithTempPathsAsync(async paths =>
        {
            var signed = SignPackage(CreatePluginPackage());
            var registryPath = await WriteRegistryAsync(paths, signed with
            {
                Signature = Convert.ToBase64String("not the real signature"u8.ToArray())
            });
            var service = CreateService(paths);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Staged);
            Assert.IsFalse(result.SignatureVerified);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("signature verification failed", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.Exists(paths.PluginStagingRoot) &&
                Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task ValidateRegistryAsync_RejectsIncompleteSignatureMetadata()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage() with
            {
                Signature = Convert.ToBase64String([1, 2, 3])
            };
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.ValidateRegistryAsync(registryPath);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("signatureAlgorithm", StringComparison.Ordinal)));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("signaturePublicKeyPem", StringComparison.Ordinal)));
        });
    }

    [TestMethod]
    public async Task InstallStagedPackage_CopiesPackageToActiveRootWithoutTrustingEnablingOrExecuting()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "unrelated.plugin" },
                EnabledPluginIds = { "unrelated.plugin" },
                AllowedPluginActionIds = { "unrelated.plugin:*" }
            };
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins);

            var stage = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);
            var install = service.InstallStagedPackage("sample.redaction-note");

            Assert.IsTrue(stage.Succeeded, string.Join("; ", stage.Issues));
            Assert.IsTrue(install.Succeeded, string.Join("; ", install.Issues));
            Assert.IsTrue(install.Installed);
            Assert.IsFalse(install.WouldTrust);
            Assert.IsFalse(install.WouldEnable);
            Assert.IsFalse(install.WouldExecute);
            Assert.IsTrue(File.Exists(Path.Combine(paths.PluginsRoot, "sample.redaction-note", "plugin.json")));
            var installManifestPath = Path.Combine(paths.PluginsRoot, "sample.redaction-note", "receipts-plugin-install.json");
            Assert.IsTrue(File.Exists(installManifestPath));
            var installManifest = JsonSerializer.Deserialize<RemotePluginInstallManifest>(
                await File.ReadAllTextAsync(installManifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(installManifest);
            Assert.AreEqual(RemotePluginPackageService.CurrentInstallSchemaVersion, installManifest!.SchemaVersion);

            var plugin = localPlugins.Discover().Single(item =>
                item.PluginId.Equals("sample.redaction-note", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(plugin.IsValid, string.Join("; ", plugin.Issues));
            Assert.IsFalse(plugin.IsTrusted);
            Assert.IsFalse(plugin.IsEnabled);
            Assert.IsFalse(plugin.Actions.Single(action => action.ActionId == "write-note").IsAllowed);

            var dryRun = localPlugins.DryRunAction("sample.redaction-note", "write-note");
            Assert.IsFalse(dryRun.Succeeded);
            Assert.IsFalse(dryRun.WouldExecute);
            StringAssert.Contains(dryRun.Message, "not trusted");
            CollectionAssert.DoesNotContain(settings.TrustedPluginIds, "sample.redaction-note");
            CollectionAssert.DoesNotContain(settings.EnabledPluginIds, "sample.redaction-note");
            CollectionAssert.DoesNotContain(settings.AllowedPluginActionIds, "sample.redaction-note:*");
        });
    }

    [TestMethod]
    public async Task InstallStagedPackage_ReplaceClearsExistingTrustEnablementAndAllowlists()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "sample.redaction-note" },
                EnabledPluginIds = { "sample.redaction-note" },
                AllowedPluginActionIds = { "sample.redaction-note:*", "sample.redaction-note:write-note" }
            };
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var stage = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);
            var install = service.InstallStagedPackage("sample.redaction-note", replaceExisting: true);

            Assert.IsTrue(stage.Succeeded, string.Join("; ", stage.Issues));
            Assert.IsTrue(install.Succeeded, string.Join("; ", install.Issues));
            Assert.IsTrue(install.ReplacedExisting);
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);

            var plugin = localPlugins.Discover().Single(item =>
                item.PluginId.Equals("sample.redaction-note", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("0.2.0", plugin.Version);
            Assert.IsFalse(plugin.IsTrusted);
            Assert.IsFalse(plugin.IsEnabled);
            Assert.IsFalse(plugin.Actions.Single(action => action.ActionId == "write-note").IsAllowed);
        });
    }

    [TestMethod]
    public async Task InstallStagedPackage_RequiresReplaceForExistingActivePlugin()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var stage = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);
            var first = service.InstallStagedPackage("sample.redaction-note");
            var second = service.InstallStagedPackage("sample.redaction-note");
            var replacement = service.InstallStagedPackage("sample.redaction-note", replaceExisting: true);

            Assert.IsTrue(stage.Succeeded, string.Join("; ", stage.Issues));
            Assert.IsTrue(first.Succeeded, string.Join("; ", first.Issues));
            Assert.IsFalse(second.Succeeded);
            Assert.IsTrue(second.Issues.Any(issue => issue.Contains("--replace", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(replacement.Succeeded, string.Join("; ", replacement.Issues));
            Assert.IsTrue(replacement.ReplacedExisting);
        });
    }

    [TestMethod]
    public async Task InstallStagedPackage_InstallsNestedPackageRoot()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage(zipEntries:
            [
                ("sample.redaction-note/plugin.json", ValidPluginManifest("sample.redaction-note", "0.2.0")),
                ("sample.redaction-note/scripts/noop.ps1", "Write-Output 'ok'")
            ]);
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var stage = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);
            var install = service.InstallStagedPackage("sample.redaction-note");

            Assert.IsTrue(stage.Succeeded, string.Join("; ", stage.Issues));
            Assert.IsTrue(install.Succeeded, string.Join("; ", install.Issues));
            Assert.AreEqual(Path.Combine(paths.PluginsRoot, "sample.redaction-note"), install.InstalledDirectory);
            Assert.IsTrue(File.Exists(Path.Combine(paths.PluginsRoot, "sample.redaction-note", "plugin.json")));
            Assert.IsTrue(File.Exists(Path.Combine(paths.PluginsRoot, "sample.redaction-note", "scripts", "noop.ps1")));
            Assert.IsFalse(File.Exists(Path.Combine(paths.PluginsRoot, "sample.redaction-note", "sample.redaction-note", "plugin.json")));
        });
    }

    [TestMethod]
    public void InstallStagedPackage_ReportsMissingStagedPackage()
    {
        WithTempPathsAsync(paths =>
        {
            var service = CreateService(paths);

            var result = service.InstallStagedPackage("sample.redaction-note");

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Installed);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("No staged package", StringComparison.OrdinalIgnoreCase)));
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task PlanInstallAsync_RejectsChecksumMismatch()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package with
            {
                Sha256 = new string('0', 64)
            });
            var service = CreateService(paths);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Staged);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_RejectsZipTraversal()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage(zipEntries:
            [
                ("plugin.json", ValidPluginManifest("sample.redaction-note", "0.2.0")),
                ("../escape.txt", "nope")
            ]);
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Staged);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("unsafe", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task PlanInstallAsync_DownloadsRegistryAndPackageFromFakeHttp()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryJson = RegistryJson(package with
            {
                PackageUri = "plugin.zip"
            });
            using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, byte[]>
            {
                ["https://registry.example.test/registry.json"] = Encoding.UTF8.GetBytes(registryJson),
                ["https://registry.example.test/plugin.zip"] = package.Bytes
            }));
            var localPlugins = new LocalPluginService(paths, new AppSettings());
            var service = new RemotePluginPackageService(paths, localPlugins, httpClient);

            var result = await service.PlanInstallAsync(
                "sample.redaction-note",
                "https://registry.example.test/registry.json",
                stagePackage: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
            StringAssert.Contains(result.PackageUri, "https://registry.example.test/plugin.zip");
            Assert.IsTrue(File.Exists(result.StagedPackagePath));
        });
    }

    [TestMethod]
    public async Task CheckUpdatesAsync_ReportsNewerRegistryVersion()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var service = CreateService(paths);

            var result = await service.CheckUpdatesAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            var candidate = result.Candidates.Single();
            Assert.IsTrue(candidate.UpdateAvailable);
            Assert.AreEqual("0.1.0", candidate.InstalledVersion);
            Assert.AreEqual("0.2.0", candidate.RegistryVersion);
        });
    }

    [TestMethod]
    public async Task SummarizeUpdatesAsync_ReportsPolicyBlockedUpdateWithoutMutatingPluginState()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings();
            settings.ManagedPolicy.DisableLocalPlugins = true;
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.SummarizeUpdatesAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.BlockedCount);
            var plugin = result.Plugins.Single();
            Assert.AreEqual("blocked", plugin.Status);
            Assert.IsTrue(plugin.UpdateAvailable);
            Assert.IsTrue(plugin.PolicyBlocked);
            Assert.IsTrue(plugin.PolicyBlockReasons.Any(reason => reason.Contains("disabled", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldExecute);
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
        });
    }

    [TestMethod]
    public async Task SummarizeUpdatesAsync_ReportsIncompatibleRegistryVersion()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package with
            {
                MinGoatShotVersion = "999.0.0"
            });
            var service = CreateService(paths);

            var result = await service.SummarizeUpdatesAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.IncompatibleCount);
            var plugin = result.Plugins.Single();
            Assert.AreEqual("incompatible", plugin.Status);
            Assert.IsTrue(plugin.Incompatible);
            Assert.IsTrue(plugin.CompatibilityIssues.Any(issue => issue.Contains("999.0.0", StringComparison.Ordinal)));
            Assert.IsFalse(plugin.WouldTrust);
            Assert.IsFalse(plugin.WouldEnable);
            Assert.IsFalse(plugin.WouldExecute);
        });
    }

    [TestMethod]
    public async Task SummarizeUpdatesAsync_ReportsStagedPackageWithoutActiveInstall()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings();
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);
            var stage = await service.PlanInstallAsync("sample.redaction-note", registryPath, stagePackage: true);

            var result = await service.SummarizeUpdatesAsync(registryPath);

            Assert.IsTrue(stage.Succeeded, string.Join("; ", stage.Issues));
            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.StagedCount);
            var plugin = result.Plugins.Single();
            Assert.AreEqual("staged", plugin.Status);
            Assert.IsTrue(plugin.Staged);
            Assert.IsFalse(plugin.Installed);
            Assert.AreEqual("0.2.0", plugin.StagedVersion);
            Assert.IsTrue(File.Exists(plugin.StagedPackagePath));
            Assert.IsFalse(plugin.WouldTrust);
            Assert.IsFalse(plugin.WouldEnable);
            Assert.IsFalse(plugin.WouldExecute);
            Assert.AreEqual(0, localPlugins.Discover().Count, "A staged package must not become an active local plugin.");
        });
    }

    [TestMethod]
    public async Task SummarizeUpdatesAsync_RedactsRegistryPackageAndReleaseMetadata()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage() with
            {
                PackageUri = "https://cdn.example.test/plugin.zip?access_token=super-secret-token",
                ReleaseNotes = "Fixes capture flow for user@example.test with token=super-secret-token."
            };
            var registryJson = RegistryJson(package);
            using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, byte[]>
            {
                ["https://registry.example.test/registry.json?access_token=super-secret-token"] = Encoding.UTF8.GetBytes(registryJson)
            }));
            var settings = new AppSettings();
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, httpClient, settings: settings);

            var result = await service.SummarizeUpdatesAsync("https://registry.example.test/registry.json?access_token=super-secret-token");
            var serialized = JsonSerializer.Serialize(result);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsFalse(serialized.Contains("super-secret-token", StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("user@example.test", StringComparison.Ordinal));
            StringAssert.Contains(serialized, "[REDACTED");
        });
    }

    [TestMethod]
    public async Task ApplyUpdatesAsync_StagesAvailableUpdateWithoutTrustingEnablingOrExecuting()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "sample.redaction-note" },
                EnabledPluginIds = { "sample.redaction-note" },
                AllowedPluginActionIds = { "sample.redaction-note:write-note" }
            };
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.ApplyUpdatesAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.StagedCount);
            Assert.AreEqual(0, result.InstalledCount);
            Assert.AreEqual(0, result.FailedCount);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldExecute);
            var item = result.Items.Single();
            Assert.AreEqual("staged", item.Status);
            Assert.IsTrue(File.Exists(item.StagedPackagePath));
            Assert.AreEqual("sample.redaction-note", settings.TrustedPluginIds.Single());
            Assert.AreEqual("sample.redaction-note", settings.EnabledPluginIds.Single());
            Assert.AreEqual("sample.redaction-note:write-note", settings.AllowedPluginActionIds.Single());
            Assert.AreEqual("0.1.0", localPlugins.Discover().Single().Version);
        });
    }

    [TestMethod]
    public async Task ApplyUpdatesAsync_InstallsUpdateOnlyWhenRequestedAndClearsTrustState()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "sample.redaction-note" },
                EnabledPluginIds = { "sample.redaction-note" },
                AllowedPluginActionIds = { "sample.redaction-note:*" }
            };
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.ApplyUpdatesAsync(
                registryPath,
                installStaged: true,
                replaceExisting: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.StagedCount);
            Assert.AreEqual(1, result.InstalledCount);
            Assert.AreEqual(0, result.FailedCount);
            var item = result.Items.Single();
            Assert.AreEqual("installed", item.Status);
            Assert.IsTrue(item.ReplacedExisting);
            Assert.IsTrue(File.Exists(item.ManifestPath));
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
            var plugin = localPlugins.Discover().Single();
            Assert.AreEqual("0.2.0", plugin.Version);
            Assert.IsFalse(plugin.IsTrusted);
            Assert.IsFalse(plugin.IsEnabled);
            Assert.IsFalse(plugin.Actions.Single(action => action.ActionId == "write-note").IsAllowed);
        });
    }

    [TestMethod]
    public async Task ApplyUpdatesAsync_SkipsPolicyBlockedUpdateWithoutStagingPackage()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings();
            settings.ManagedPolicy.DisableLocalPlugins = true;
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.ApplyUpdatesAsync(registryPath, installStaged: true, replaceExisting: true);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(0, result.StagedCount);
            Assert.AreEqual(0, result.InstalledCount);
            Assert.AreEqual(1, result.SkippedCount);
            var item = result.Items.Single();
            Assert.AreEqual("skipped", item.Status);
            Assert.IsTrue(item.Issues.Any(issue => issue.Contains("disabled", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.Exists(paths.PluginStagingRoot) &&
                Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task PlanMarketplaceAsync_ReturnsReadOnlyGovernanceWithoutMutatingPluginState()
    {
        await WithTempPathsAsync(async paths =>
        {
            var pluginFolder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
            Directory.CreateDirectory(pluginFolder);
            await File.WriteAllTextAsync(
                Path.Combine(pluginFolder, "plugin.json"),
                ValidPluginManifest("sample.redaction-note", "0.1.0"));
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "sample.redaction-note" },
                EnabledPluginIds = { "sample.redaction-note" },
                AllowedPluginActionIds = { "sample.redaction-note:write-note" }
            };
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.PlanMarketplaceAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.PluginCount);
            Assert.AreEqual(1, result.ActivePluginCount);
            Assert.AreEqual(0, result.StagedPackageCount);
            Assert.AreEqual(1, result.AvailableCount);
            Assert.IsFalse(result.WouldStagePackage);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsFalse(result.WouldAutoUpdate);
            Assert.IsFalse(result.WouldPublish);
            Assert.IsFalse(result.WouldContactMarketplaceAccount);
            Assert.IsTrue(result.AuthorityBoundaries.Any(boundary => boundary.Contains("read-only", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.NonGoals.Any(nonGoal => nonGoal.Contains("No automatic self-registering updater service", StringComparison.OrdinalIgnoreCase)));

            var plugin = result.Plugins.Single();
            Assert.AreEqual("available", plugin.Status);
            Assert.IsTrue(plugin.UpdateAvailable);
            Assert.AreEqual("0.1.0", plugin.InstalledVersion);
            Assert.AreEqual("0.2.0", plugin.RegistryVersion);
            Assert.IsFalse(plugin.WouldStagePackage);
            Assert.IsFalse(plugin.WouldInstall);
            Assert.IsFalse(plugin.WouldTrust);
            Assert.IsFalse(plugin.WouldEnable);
            Assert.IsFalse(plugin.WouldAllowlist);
            Assert.IsFalse(plugin.WouldExecute);
            Assert.AreEqual("sample.redaction-note", settings.TrustedPluginIds.Single());
            Assert.AreEqual("sample.redaction-note", settings.EnabledPluginIds.Single());
            Assert.AreEqual("sample.redaction-note:write-note", settings.AllowedPluginActionIds.Single());
            Assert.IsFalse(Directory.Exists(paths.PluginStagingRoot) &&
                Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task PlanMarketplaceAsync_ReportsPolicyBlockedPluginWithoutStaging()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage();
            var registryPath = await WriteRegistryAsync(paths, package);
            var settings = new AppSettings();
            settings.ManagedPolicy.DisableLocalPlugins = true;
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, settings: settings);

            var result = await service.PlanMarketplaceAsync(registryPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.BlockedCount);
            var plugin = result.Plugins.Single();
            Assert.AreEqual("blocked", plugin.Status);
            Assert.IsTrue(plugin.PolicyBlocked);
            Assert.IsTrue(plugin.PolicyBlockReasons.Any(reason => reason.Contains("disabled", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.WouldStagePackage);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsFalse(Directory.Exists(paths.PluginStagingRoot) &&
                Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task PlanMarketplaceAsync_RedactsRegistryPackageAndFutureHostedMetadata()
    {
        await WithTempPathsAsync(async paths =>
        {
            var package = CreatePluginPackage() with
            {
                PackageUri = "https://cdn.example.test/plugin.zip?access_token=super-secret-token",
                ReleaseNotes = "Marketplace notes for user@example.test with token=super-secret-token."
            };
            var registryJson = RegistryJson(package);
            using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, byte[]>
            {
                ["https://registry.example.test/registry.json?access_token=super-secret-token"] = Encoding.UTF8.GetBytes(registryJson)
            }));
            var settings = new AppSettings();
            var localPlugins = new LocalPluginService(paths, settings);
            var service = new RemotePluginPackageService(paths, localPlugins, httpClient, settings: settings);

            var result = await service.PlanMarketplaceAsync("https://registry.example.test/registry.json?access_token=super-secret-token");
            var serialized = JsonSerializer.Serialize(result);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsFalse(serialized.Contains("super-secret-token", StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("user@example.test", StringComparison.Ordinal));
            StringAssert.Contains(serialized, "[REDACTED");
            Assert.IsFalse(result.WouldContactMarketplaceAccount);
            Assert.IsTrue(result.NonGoals.Any(nonGoal => nonGoal.Contains("ratings", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.PrivacyThreatNotes.Any(note => note.Contains("No plugin package is downloaded", StringComparison.OrdinalIgnoreCase)));
        });
    }

    private static RemotePluginPackageService CreateService(AppPaths paths)
    {
        return new RemotePluginPackageService(paths, new LocalPluginService(paths, new AppSettings()));
    }

    private static async Task<string> WriteRegistryAsync(
        AppPaths paths,
        PackageFixture package,
        string? schemaVersion = null)
    {
        var packageFolder = Path.Combine(paths.LocalRoot, "registry-fixtures");
        Directory.CreateDirectory(packageFolder);
        var packagePath = Path.Combine(packageFolder, "sample.redaction-note-0.2.0.zip");
        await File.WriteAllBytesAsync(packagePath, package.Bytes);
        var registryPath = Path.Combine(packageFolder, "registry.json");
        await File.WriteAllTextAsync(registryPath, RegistryJson(package with
        {
            PackageUri = Path.GetFileName(packagePath)
        }, schemaVersion));
        return registryPath;
    }

    private static string RegistryJson(PackageFixture package, string? schemaVersion = null)
    {
        return $$"""
            {
              "schemaVersion": "{{schemaVersion ?? RemotePluginPackageService.CurrentRegistrySchemaVersion}}",
              "source": "local test registry",
              "plugins": [
                {
                  "id": "sample.redaction-note",
                  "version": "0.2.0",
                  "name": "Sample Redaction Note",
                  "description": "Remote staging fixture. No package is trusted or enabled automatically.",
                  "capabilities": ["action"],
                  "permissions": ["filesystem:plugin-directory"],
                  "packageUri": "{{package.PackageUri}}",
                  "sha256": "{{package.Sha256}}",
                  "sizeBytes": {{package.Bytes.Length}},
                  "signature": {{JsonSerializer.Serialize(package.Signature)}},
                  "signatureAlgorithm": {{JsonSerializer.Serialize(package.SignatureAlgorithm)}},
                  "signaturePublicKeyPem": {{JsonSerializer.Serialize(package.SignaturePublicKeyPem)}},
                  "minGoatShotVersion": "{{package.MinGoatShotVersion}}",
                  "maxGoatShotVersion": "{{package.MaxGoatShotVersion}}",
                  "releaseNotes": "{{package.ReleaseNotes}}"
                }
              ]
            }
            """;
    }

    private static PackageFixture CreatePluginPackage(
        IReadOnlyList<(string Name, string Content)>? zipEntries = null)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in zipEntries ??
                [("plugin.json", ValidPluginManifest("sample.redaction-note", "0.2.0"))])
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        var bytes = memory.ToArray();
        return new PackageFixture(
            Bytes: bytes,
            Sha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            PackageUri: "sample.redaction-note-0.2.0.zip",
            MinGoatShotVersion: "0.0.1",
            MaxGoatShotVersion: "99.0.0",
            ReleaseNotes: "Test registry entry.",
            Signature: string.Empty,
            SignatureAlgorithm: string.Empty,
            SignaturePublicKeyPem: string.Empty);
    }

    private static PackageFixture SignPackage(
        PackageFixture package,
        string algorithm = "rsa-pss-sha256")
    {
        using var rsa = RSA.Create(2048);
        var padding = algorithm.Equals("rsa-pkcs1-sha256", StringComparison.OrdinalIgnoreCase)
            ? RSASignaturePadding.Pkcs1
            : RSASignaturePadding.Pss;
        var signature = rsa.SignData(package.Bytes, HashAlgorithmName.SHA256, padding);
        return package with
        {
            Signature = Convert.ToBase64String(signature),
            SignatureAlgorithm = algorithm,
            SignaturePublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem()
        };
    }

    private static string ValidPluginManifest(string id, string version, string? schemaVersion = null)
    {
        return $$"""
            {
              "schemaVersion": "{{schemaVersion ?? LocalPluginService.CurrentSchemaVersion}}",
              "id": "{{id}}",
              "name": "Sample Redaction Note",
              "version": "{{version}}",
              "description": "Local-only sample plugin. No network side effects.",
              "actions": [
                {
                  "id": "write-note",
                  "name": "Write local note"
                }
              ]
            }
            """;
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
            var settings = new AppSettings();
            var paths = AppPaths.Create(settings);

            await action(paths);
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

    private sealed record PackageFixture(
        byte[] Bytes,
        string Sha256,
        string PackageUri,
        string MinGoatShotVersion,
        string MaxGoatShotVersion,
        string ReleaseNotes,
        string Signature,
        string SignatureAlgorithm,
        string SignaturePublicKeyPem);

    private sealed class FakeHttpHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.ToString() ?? string.Empty;
            if (!responses.TryGetValue(key, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }
}

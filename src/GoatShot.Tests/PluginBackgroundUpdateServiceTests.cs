using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PluginBackgroundUpdateServiceTests
{
    [TestMethod]
    public async Task RunAsync_CheckOnlyWritesStateWithoutStagingOrMutatingPluginTrust()
    {
        await WithTempPathsAsync(async paths =>
        {
            await WriteInstalledPluginAsync(paths, "0.1.0");
            var packagePath = CreatePluginPackage(paths, "0.2.0");
            var registryPath = await WriteRegistryAsync(paths, packagePath, "0.2.0");
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = { "sample.redaction-note" },
                EnabledPluginIds = { "sample.redaction-note" },
                AllowedPluginActionIds = { "sample.redaction-note:write-note" }
            };
            var remote = new RemotePluginPackageService(paths, new LocalPluginService(paths, settings), settings: settings);
            var service = new PluginBackgroundUpdateService(paths, remote);
            var statePath = Path.Combine(paths.LocalRoot, "background-state.json");

            var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = registryPath,
                Mode = "check-only",
                StatePath = statePath,
                Force = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.AvailableCount);
            Assert.AreEqual(0, result.StagedCount);
            Assert.IsFalse(result.WouldStagePackage);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsTrue(File.Exists(statePath));
            var state = JsonSerializer.Deserialize<PluginBackgroundUpdateState>(
                await File.ReadAllTextAsync(statePath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(state);
            Assert.AreEqual(PluginBackgroundUpdateService.CurrentSchemaVersion, state!.SchemaVersion);
            Assert.IsFalse(Directory.EnumerateFiles(paths.PluginStagingRoot, "*", SearchOption.AllDirectories).Any());
            CollectionAssert.Contains(settings.TrustedPluginIds, "sample.redaction-note");
            CollectionAssert.Contains(settings.EnabledPluginIds, "sample.redaction-note");
            CollectionAssert.Contains(settings.AllowedPluginActionIds, "sample.redaction-note:write-note");
        });
    }

    [TestMethod]
    public async Task RunAsync_StageOnlyStagesPackageButDoesNotInstallTrustEnableAllowlistOrExecute()
    {
        await WithTempPathsAsync(async paths =>
        {
            await WriteInstalledPluginAsync(paths, "0.1.0");
            var packagePath = CreatePluginPackage(paths, "0.2.0");
            var registryPath = await WriteRegistryAsync(paths, packagePath, "0.2.0");
            var settings = new AppSettings
            {
                EnableLocalPlugins = true
            };
            var local = new LocalPluginService(paths, settings);
            var remote = new RemotePluginPackageService(paths, local, settings: settings);
            var service = new PluginBackgroundUpdateService(paths, remote);

            var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = registryPath,
                Mode = "stage-only",
                StatePath = Path.Combine(paths.LocalRoot, "background-state.json"),
                Force = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.AreEqual(1, result.StagedCount);
            Assert.AreEqual(0, result.InstalledCount);
            Assert.IsTrue(result.WouldStagePackage);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsTrue(Directory.EnumerateFiles(paths.PluginStagingRoot, "stage-manifest.json", SearchOption.AllDirectories).Any());
            Assert.AreEqual("0.1.0", local.Discover().Single(plugin => plugin.PluginId == "sample.redaction-note").Version);
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
        });
    }

    [TestMethod]
    public async Task RunAsync_SkipsWhenStateSaysNotDueUnlessForced()
    {
        await WithTempPathsAsync(async paths =>
        {
            await WriteInstalledPluginAsync(paths, "0.1.0");
            var packagePath = CreatePluginPackage(paths, "0.2.0");
            var registryPath = await WriteRegistryAsync(paths, packagePath, "0.2.0");
            var settings = new AppSettings();
            var remote = new RemotePluginPackageService(paths, new LocalPluginService(paths, settings), settings: settings);
            var service = new PluginBackgroundUpdateService(paths, remote);
            var statePath = Path.Combine(paths.LocalRoot, "background-state.json");

            var first = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = registryPath,
                Mode = "check-only",
                StatePath = statePath,
                IntervalHours = 24,
                Force = true
            });
            var second = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = registryPath,
                Mode = "check-only",
                StatePath = statePath,
                IntervalHours = 24
            });

            Assert.IsTrue(first.Succeeded, string.Join("; ", first.Issues));
            Assert.IsTrue(second.Succeeded, string.Join("; ", second.Issues));
            Assert.IsTrue(second.Skipped);
            Assert.IsFalse(second.Due);
            StringAssert.Contains(second.Message, "skipped");
        });
    }

    [TestMethod]
    public async Task RunAsync_AcceptsLegacyGoatShotStateSchema()
    {
        await WithTempPathsAsync(async paths =>
        {
            var statePath = Path.Combine(paths.LocalRoot, "legacy-background-state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            await File.WriteAllTextAsync(
                statePath,
                JsonSerializer.Serialize(new PluginBackgroundUpdateState
                {
                    SchemaVersion = PluginBackgroundUpdateService.LegacySchemaVersion,
                    NextRunUtc = DateTimeOffset.UtcNow.AddHours(1)
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var remote = new RemotePluginPackageService(paths, new LocalPluginService(paths, new AppSettings()));
            var service = new PluginBackgroundUpdateService(paths, remote);

            var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = "unused-while-not-due.json",
                Mode = "check-only",
                StatePath = statePath
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Skipped);
            Assert.IsFalse(result.Due);
        });
    }

    [TestMethod]
    public async Task RunAsync_RejectsInstallMode()
    {
        await WithTempPathsAsync(async paths =>
        {
            var remote = new RemotePluginPackageService(paths, new LocalPluginService(paths, new AppSettings()));
            var service = new PluginBackgroundUpdateService(paths, remote);

            var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = "registry.json",
                Mode = "install"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("check-only or stage-only", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldExecute);
        });
    }

    private static async Task WriteInstalledPluginAsync(AppPaths paths, string version)
    {
        var pluginRoot = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(Path.Combine(pluginRoot, "plugin.json"), PluginManifest(version));
    }

    private static string CreatePluginPackage(AppPaths paths, string version)
    {
        var packagePath = Path.Combine(paths.TempRoot, $"sample.redaction-note-{version}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry("plugin.json");
        using (var writer = new StreamWriter(manifest.Open()))
        {
            writer.Write(PluginManifest(version));
        }

        var script = archive.CreateEntry("scripts/noop.ps1");
        using (var writer = new StreamWriter(script.Open()))
        {
            writer.Write("Write-Output 'noop'");
        }

        return packagePath;
    }

    private static async Task<string> WriteRegistryAsync(AppPaths paths, string packagePath, string version)
    {
        var bytes = await File.ReadAllBytesAsync(packagePath);
        var registry = new
        {
            schemaVersion = RemotePluginPackageService.CurrentRegistrySchemaVersion,
            generatedAtUtc = DateTimeOffset.UtcNow,
            source = "test registry",
            plugins = new[]
            {
                new
                {
                    id = "sample.redaction-note",
                    version,
                    name = "Sample Redaction Note",
                    description = "Test plugin package.",
                    capabilities = new[] { "action" },
                    permissions = Array.Empty<string>(),
                    packageUri = packagePath,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    sizeBytes = bytes.LongLength,
                    minGoatShotVersion = "0.0.1",
                    maxGoatShotVersion = "99.0.0",
                    releaseNotes = "Test update."
                }
            }
        };
        var registryPath = Path.Combine(paths.TempRoot, "registry.json");
        await File.WriteAllTextAsync(registryPath, JsonSerializer.Serialize(registry, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
        return registryPath;
    }

    private static string PluginManifest(string version)
    {
        return $$"""
        {
          "schemaVersion": "receipts.plugin.v1",
          "id": "sample.redaction-note",
          "name": "Sample Redaction Note",
          "version": "{{version}}",
          "description": "Test plugin.",
          "actions": [
            {
              "id": "write-note",
              "name": "Write note",
              "description": "Test action.",
              "execution": {
                "command": "powershell",
                "arguments": ["-NoProfile", "-Command", "Write-Output noop"],
                "timeoutSeconds": 5
              }
            }
          ]
        }
        """;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-plugin-background-test-" + Guid.NewGuid().ToString("N"));
        var localRoot = Path.Combine(tempRoot, "local");
        var libraryRoot = Path.Combine(tempRoot, "library");
        var oldLocal = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var oldLibrary = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", localRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", libraryRoot);
            var settings = new AppSettings();
            var paths = AppPaths.Create(settings);
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", oldLocal);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", oldLibrary);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

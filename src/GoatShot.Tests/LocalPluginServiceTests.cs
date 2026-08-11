using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class LocalPluginServiceTests
{
    [TestMethod]
    public async Task Discover_ParsesValidManifestAndLeavesPluginUntrustedByDefault()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var plugins = service.Discover();

            Assert.AreEqual(1, plugins.Count);
            var plugin = plugins.Single();
            Assert.IsTrue(plugin.IsValid, string.Join("; ", plugin.Issues));
            Assert.AreEqual("sample.redaction-note", plugin.PluginId);
            Assert.AreEqual("Sample Redaction Note", plugin.Name);
            Assert.AreEqual("0.1.0", plugin.Version);
            Assert.IsFalse(plugin.IsTrusted);
            Assert.IsFalse(plugin.IsEnabled);
            Assert.AreEqual(4, plugin.Actions.Count);
            Assert.IsTrue(plugin.Actions.All(action => !action.IsAllowed));
            CollectionAssert.AreEquivalent(
                new[] { "action", "share", "workflow", "diagnostic" },
                plugin.Actions.Select(action => action.Scope).ToArray());
        });
    }

    [TestMethod]
    public async Task Discover_AcceptsLegacyGoatShotManifestSchema()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(
                paths,
                ValidManifest().Replace(
                    LocalPluginService.CurrentSchemaVersion,
                    LocalPluginService.LegacySchemaVersion,
                    StringComparison.Ordinal));
            var service = new LocalPluginService(paths, settings);

            var plugin = service.Discover().Single();

            Assert.IsTrue(plugin.IsValid, string.Join("; ", plugin.Issues));
        });
    }

    [TestMethod]
    public async Task Discover_ReportsInvalidManifestIssues()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(
                paths,
                """
                {
                  "schemaVersion": "goatshot.plugin.v0",
                  "id": "bad id",
                  "name": "",
                  "version": "",
                  "actions": []
                }
                """);
            var service = new LocalPluginService(paths, settings);

            var plugin = service.Discover().Single();

            Assert.IsFalse(plugin.IsValid);
            Assert.IsTrue(plugin.Issues.Any(issue => issue.Contains("Unsupported schemaVersion", StringComparison.Ordinal)));
            Assert.IsTrue(plugin.Issues.Any(issue => issue.Contains("Plugin id", StringComparison.Ordinal)));
            Assert.IsTrue(plugin.Issues.Any(issue => issue.Contains("Plugin name is required", StringComparison.Ordinal)));
            Assert.IsTrue(plugin.Issues.Any(issue => issue.Contains("Plugin version is required", StringComparison.Ordinal)));
            Assert.IsTrue(plugin.Issues.Any(issue => issue.Contains("at least one action", StringComparison.Ordinal)));
        });
    }

    [TestMethod]
    public async Task DryRunAction_BlocksUntilTrustedEnabledAndAllowlisted()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var untrusted = service.DryRunAction("sample.redaction-note", "write-note");
            Assert.IsFalse(untrusted.Succeeded);
            StringAssert.Contains(untrusted.Message, "not trusted");

            settings.TrustedPluginIds.Add("sample.redaction-note");
            var globallyDisabled = service.DryRunAction("sample.redaction-note", "write-note");
            Assert.IsFalse(globallyDisabled.Succeeded);
            StringAssert.Contains(globallyDisabled.Message, "globally disabled");

            settings.EnableLocalPlugins = true;
            settings.EnabledPluginIds.Add("sample.redaction-note");
            var notAllowlisted = service.DryRunAction("sample.redaction-note", "write-note");
            Assert.IsFalse(notAllowlisted.Succeeded);
            StringAssert.Contains(notAllowlisted.Message, "not allowlisted");

            settings.AllowedPluginActionIds.Add("sample.redaction-note:write-note");
            var allowed = service.DryRunAction("sample.redaction-note", "write-note");

            Assert.IsTrue(allowed.Succeeded, allowed.Message);
            Assert.IsTrue(allowed.WouldExecute);
            Assert.AreEqual("sample.redaction-note:write-note", allowed.QualifiedActionId);
            StringAssert.Contains(allowed.Message, "No plugin process was started");
            StringAssert.Contains(allowed.Message, "Command:");
        });
    }

    [TestMethod]
    public async Task DryRunAction_AllowsWildcardActionAllowlist()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = ["sample.redaction-note"],
                EnabledPluginIds = ["sample.redaction-note"],
                AllowedPluginActionIds = ["sample.redaction-note:*"]
            };
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = service.DryRunAction("sample.redaction-note", "inspect-capture");

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("sample.redaction-note:inspect-capture", result.QualifiedActionId);
            Assert.AreEqual("workflow", result.Scope);
        });
    }

    [TestMethod]
    public async Task DiagnosticsSummary_RedactsSensitiveIssueText()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(
                paths,
                """
                {
                  "schemaVersion": "goatshot.plugin.v1",
                  "id": "sample.redaction-note",
                  "name": "Sample",
                  "version": "0.1.0",
                  "actions": [
                    { "id": "https://example.test/run?access_token=super-secret-token", "name": "First" },
                    { "id": "https://example.test/run?access_token=super-secret-token", "name": "Second" }
                  ]
                }
                """);
            var service = new LocalPluginService(paths, settings);

            var summary = service.GetDiagnosticsSummary();

            Assert.IsFalse(summary.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(summary, "[REDACTED");
            StringAssert.Contains(summary, "plugin manifest issue");
        });
    }

    [TestMethod]
    public async Task DiagnosticsSnapshot_IncludesLocalPluginStatus()
    {
        await WithTempServicesAsync(async services =>
        {
            await WritePluginManifestAsync(services.Paths, ValidManifest());

            var snapshot = services.Diagnostics.GetSnapshot();

            StringAssert.Contains(snapshot.PluginStatus, "Local plugins root");
            StringAssert.Contains(snapshot.PluginStatus, "1 manifest");
            StringAssert.Contains(snapshot.PluginStatus, "globally disabled");
        });
    }

    [TestMethod]
    public async Task ExecuteActionAsync_BlocksWhenActionIsNotAllowlisted()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                EnableLocalPlugins = true,
                TrustedPluginIds = ["sample.redaction-note"],
                EnabledPluginIds = ["sample.redaction-note"]
            };
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = await service.ExecuteActionAsync("sample.redaction-note", "write-note");

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.StartedProcess);
            StringAssert.Contains(result.Message, "not allowlisted");
        });
    }

    [TestMethod]
    public async Task ExecuteActionAsync_RunsAllowlistedActionAndRedactsOutput()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = EnabledPluginSettings();
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = await service.ExecuteActionAsync("sample.redaction-note", "write-note");

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.StartedProcess);
            Assert.AreEqual(0, result.ExitCode);
            StringAssert.Contains(result.StandardOutput, "GoatShot plugin executed");
            Assert.IsFalse(result.StandardOutput.Contains("token=super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(result.StandardOutput, "[REDACTED");
        });
    }

    [TestMethod]
    public async Task ExecuteActionAsync_ReportsTimeout()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = EnabledPluginSettings();
            await WritePluginManifestAsync(paths, TimeoutManifest());
            var service = new LocalPluginService(paths, settings);

            var result = await service.ExecuteActionAsync("sample.redaction-note", "write-note");

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.StartedProcess);
            Assert.IsTrue(result.TimedOut);
            StringAssert.Contains(result.Message, "timed out");
        });
    }

    [TestMethod]
    public async Task Activate_RequiresExplicitRiskAcceptance()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = service.Activate(new LocalPluginActivationRequest
            {
                PluginId = "sample.redaction-note",
                Trust = true,
                Enable = true,
                EnableLocalPlugins = true,
                AllowAllActions = true
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsFalse(result.StartedProcess);
            StringAssert.Contains(result.Message, "--accept-risk");
            Assert.IsFalse(settings.EnableLocalPlugins);
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
        });
    }

    [TestMethod]
    public async Task Activate_TrustsEnablesAndAllowlistsWithoutExecuting()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = service.Activate(new LocalPluginActivationRequest
            {
                PluginId = "sample.redaction-note",
                AcceptRisk = true,
                Trust = true,
                Enable = true,
                EnableLocalPlugins = true,
                ActionIds = ["write-note"]
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsFalse(result.StartedProcess);
            Assert.IsTrue(settings.EnableLocalPlugins);
            CollectionAssert.Contains(settings.TrustedPluginIds, "sample.redaction-note");
            CollectionAssert.Contains(settings.EnabledPluginIds, "sample.redaction-note");
            CollectionAssert.Contains(settings.AllowedPluginActionIds, "sample.redaction-note:write-note");
            CollectionAssert.Contains(result.AllowedActions, "sample.redaction-note:write-note");
            CollectionAssert.Contains(result.ChangedSettings, "EnableLocalPlugins");
            CollectionAssert.Contains(result.ChangedSettings, "TrustedPluginIds");
            CollectionAssert.Contains(result.ChangedSettings, "EnabledPluginIds");
            CollectionAssert.Contains(result.ChangedSettings, "AllowedPluginActionIds");

            var dryRun = service.DryRunAction("sample.redaction-note", "write-note");
            Assert.IsTrue(dryRun.Succeeded, dryRun.Message);
            Assert.IsTrue(dryRun.WouldExecute);
        });
    }

    [TestMethod]
    public async Task Activate_BlocksActionOutsideManagedPolicyAllowlist()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings
            {
                ManagedPolicy = new ManagedPolicySettings
                {
                    AllowedPluginActionIds = ["sample.redaction-note:health-note"]
                }
            };
            await WritePluginManifestAsync(paths, ValidManifest());
            var service = new LocalPluginService(paths, settings);

            var result = service.Activate(new LocalPluginActivationRequest
            {
                PluginId = "sample.redaction-note",
                AcceptRisk = true,
                Trust = true,
                Enable = true,
                EnableLocalPlugins = true,
                ActionIds = ["write-note"]
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "not allowed by managed policy");
            Assert.IsFalse(settings.EnableLocalPlugins);
            Assert.AreEqual(0, settings.TrustedPluginIds.Count);
            Assert.AreEqual(0, settings.EnabledPluginIds.Count);
            Assert.AreEqual(0, settings.AllowedPluginActionIds.Count);
        });
    }

    private static string ValidManifest()
    {
        return """
            {
              "schemaVersion": "receipts.plugin.v1",
              "id": "sample.redaction-note",
              "name": "Sample Redaction Note",
              "version": "0.1.0",
              "description": "Local-only sample plugin. No network side effects.",
              "actions": [
                {
                  "id": "write-note",
                  "name": "Write local note",
                  "description": "Dry-run fixture for a local action.",
                  "execution": {
                    "command": "cmd.exe",
                    "arguments": ["/c", "echo GoatShot plugin executed token=super-secret-token"],
                    "timeoutSeconds": 5
                  }
                }
              ],
              "shareDestinations": [
                {
                  "id": "local-drop",
                  "name": "Local drop folder",
                  "description": "Dry-run fixture for a local share destination."
                }
              ],
              "workflowActions": [
                {
                  "id": "inspect-capture",
                  "name": "Inspect capture",
                  "description": "Dry-run fixture for a workflow action."
                }
              ],
              "diagnostics": [
                {
                  "id": "health-note",
                  "name": "Health note",
                  "description": "Dry-run fixture for diagnostics contribution."
                }
              ]
            }
            """;
    }

    private static string TimeoutManifest()
    {
        return """
            {
              "schemaVersion": "receipts.plugin.v1",
              "id": "sample.redaction-note",
              "name": "Sample Redaction Note",
              "version": "0.1.0",
              "actions": [
                {
                  "id": "write-note",
                  "name": "Write local note",
                  "execution": {
                    "command": "cmd.exe",
                    "arguments": ["/c", "ping -n 6 127.0.0.1 > nul"],
                    "timeoutSeconds": 1
                  }
                }
              ]
            }
            """;
    }

    private static AppSettings EnabledPluginSettings()
    {
        return new AppSettings
        {
            EnableLocalPlugins = true,
            TrustedPluginIds = ["sample.redaction-note"],
            EnabledPluginIds = ["sample.redaction-note"],
            AllowedPluginActionIds = ["sample.redaction-note:write-note"]
        };
    }

    private static async Task WritePluginManifestAsync(AppPaths paths, string json)
    {
        var folder = Path.Combine(paths.PluginsRoot, "sample.redaction-note");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "plugin.json"), json);
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

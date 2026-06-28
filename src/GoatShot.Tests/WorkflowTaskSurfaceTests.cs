using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class WorkflowTaskSurfaceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task RunRulesAsync_WritesWorkflowLogsWithSkippedReasons()
    {
        await WithTempServicesAsync(async services =>
        {
            services.Settings.AutomationRules =
            [
                new AutomationRule
                {
                    Id = "matched",
                    Name = "Matched capture",
                    Trigger = AutomationTrigger.CaptureCreated,
                    Actions = [AutomationActionKind.ShowNotification]
                },
                new AutomationRule
                {
                    Id = "skipped",
                    Name = "Upload-only",
                    Trigger = AutomationTrigger.UploadCompleted,
                    Actions = [AutomationActionKind.ShowNotification]
                }
            ];

            var item = CreateItem(services.Paths, "workflow-log.png");

            var result = await services.Automation.RunRulesAsync(
                AutomationTrigger.CaptureCreated,
                item,
                dryRun: true);

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.LogPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.MarkdownLogPath));
            Assert.IsTrue(File.Exists(result.LogPath));
            Assert.IsTrue(File.Exists(result.MarkdownLogPath));

            var markdown = await File.ReadAllTextAsync(result.MarkdownLogPath!);
            StringAssert.Contains(markdown, "Upload-only");
            StringAssert.Contains(markdown, "not CaptureCreated");
            StringAssert.Contains(markdown, "Dry run would execute ShowNotification");
        });
    }

    [TestMethod]
    public async Task WorkflowDryRuns_RedactSecretsAndDoNotExecute()
    {
        await WithTempServicesAsync(services =>
        {
            var item = CreateItem(services.Paths, "dry-run.png");
            services.Settings.CustomScriptCommand =
                "Invoke-WebRequest 'https://example.test/upload?access_token=super-secret-token' -InFile {file}";
            services.Settings.CustomWebhookUrl =
                "https://example.test/hooks/goatshot?token=super-secret-token";

            var script = services.WorkflowDryRuns.PlanCustomScript(item);
            var webhook = services.WorkflowDryRuns.PlanCustomWebhook(item);

            Assert.IsTrue(script.WouldExecute);
            Assert.IsTrue(webhook.WouldExecute);
            Assert.IsFalse(script.ResolvedCommand.Contains("super-secret-token", StringComparison.Ordinal));
            Assert.IsFalse(webhook.Target.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(script.ResolvedCommand, "[REDACTED");
            StringAssert.Contains(webhook.Target, "[REDACTED");
            StringAssert.Contains(script.Message, "No process was started");
            StringAssert.Contains(webhook.Message, "No HTTP request was sent");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task WorkflowProfiles_ValidateReportsErrorsAndWarnings()
    {
        await WithTempServicesAsync(async services =>
        {
            var profile = new WorkflowProfile
            {
                Name = "Validation fixture",
                IncludesSensitiveValues = true,
                AutomationRules =
                [
                    new AutomationRule
                    {
                        Id = "empty-actions",
                        Name = "Empty actions",
                        Trigger = AutomationTrigger.CaptureCreated,
                        Actions = []
                    },
                    new AutomationRule
                    {
                        Id = "risky-delete",
                        Name = "Risky delete",
                        Trigger = AutomationTrigger.UploadCompleted,
                        Actions =
                        [
                            AutomationActionKind.ShareDefaultDestination,
                            AutomationActionKind.DeleteLocalFile
                        ]
                    }
                ]
            };
            var path = Path.Combine(services.Paths.TempRoot, "workflow-validation.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, JsonOptions));

            var result = await services.WorkflowProfiles.ValidateAsync(path);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("has no actions", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("DeleteLocalFile", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("includes custom script/webhook values", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task WorkflowProfiles_PreserveFutureAutomationRuleFieldsThroughImportExport()
    {
        await WithTempServicesAsync(async services =>
        {
            var input = Path.Combine(services.Paths.TempRoot, "future-workflow.json");
            await File.WriteAllTextAsync(
                input,
                """
                {
                  "schemaVersion": 1,
                  "name": "Future rule profile",
                  "sharing": {},
                  "watchFolders": {},
                  "automationRules": [
                    {
                      "id": "future-rule",
                      "name": "Future rule",
                      "trigger": "CaptureCreated",
                      "actions": [ "ShowNotification" ],
                      "futureCondition": {
                        "operator": "containsAny",
                        "values": [ "checkout", "support" ]
                      }
                    }
                  ]
                }
                """);

            var import = await services.WorkflowProfiles.ImportAsync(input);
            Assert.IsTrue(import.Succeeded, import.Message);

            var output = Path.Combine(services.Paths.TempRoot, "future-workflow-exported.json");
            var export = await services.WorkflowProfiles.ExportAsync(output);
            Assert.IsTrue(export.Succeeded, export.Message);

            var json = await File.ReadAllTextAsync(output);
            StringAssert.Contains(json, "futureCondition");
            StringAssert.Contains(json, "containsAny");
        });
    }

    private static CaptureItem CreateItem(AppPaths paths, string fileName)
    {
        var path = Path.Combine(paths.ImagesRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Range(0, 512).Select(index => (byte)(index % 255)).ToArray());
        return new CaptureItem
        {
            Id = Path.GetFileNameWithoutExtension(fileName),
            Kind = CaptureKind.ActiveWindow,
            CreatedAt = DateTimeOffset.Now,
            FilePath = path,
            ThumbnailPath = path,
            Bytes = new FileInfo(path).Length,
            Width = 16,
            Height = 16,
            SourceApp = "goatshot.tests",
            SourceWindowTitle = "Workflow task surface test",
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

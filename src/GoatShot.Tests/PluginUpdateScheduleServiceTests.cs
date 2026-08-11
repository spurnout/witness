using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PluginUpdateScheduleServiceTests
{
    [TestMethod]
    public async Task CreatePlan_WritesSchedulerHandoffWithoutRegisteringOrExecuting()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var service = new PluginUpdateScheduleService(paths);

            var result = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                Mode = "stage-only",
                IntervalHours = 6,
                OutputPath = output,
                CliPath = "C:\\Tools\\GoatShot.exe",
                TaskName = "GoatShot Test Plugin Updates"
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            Assert.IsFalse(result.WouldRegisterTask);
            Assert.IsFalse(result.RegisteredTask);
            Assert.IsTrue(File.Exists(result.RunScriptPath));
            Assert.IsTrue(File.Exists(result.RegisterScriptPath));
            Assert.IsTrue(File.Exists(result.UnregisterScriptPath));
            Assert.IsTrue(File.Exists(result.ManifestPath));

            var runScript = File.ReadAllText(result.RunScriptPath);
            StringAssert.Contains(runScript, "--plugin-background-update");
            StringAssert.Contains(runScript, "stage-only");
            Assert.IsFalse(runScript.Contains("install-staged", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(runScript.Contains("plugins run", StringComparison.OrdinalIgnoreCase));

            var registerScript = File.ReadAllText(result.RegisterScriptPath);
            StringAssert.Contains(registerScript, "Register-ScheduledTask");
            StringAssert.Contains(registerScript, "GoatShot Test Plugin Updates");

            var manifest = JsonSerializer.Deserialize<PluginUpdateScheduleManifest>(
                File.ReadAllText(result.ManifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(manifest);
            Assert.AreEqual(PluginUpdateScheduleService.CurrentSchemaVersion, manifest!.SchemaVersion);
            Assert.AreEqual("receipts.plugin-update-schedule.v1", manifest.SchemaVersion);
            Assert.AreEqual("stage-only", manifest.Mode);
            Assert.AreEqual(6, manifest.IntervalHours);
            Assert.IsFalse(manifest.WouldInstall);
            Assert.IsFalse(manifest.WouldExecute);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task CreatePlan_RejectsSensitiveRegistryLocationWithoutAcceptance()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var service = new PluginUpdateScheduleService(paths);

            var result = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "https://example.test/registry.json?token=abcdefghijklmnop",
                OutputPath = output
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.SensitiveRegistryLocation);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("sensitive", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.Exists(output));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task CreatePlan_WithSensitiveAcceptanceKeepsManifestRedacted()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var service = new PluginUpdateScheduleService(paths);

            var result = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "https://example.test/registry.json?token=abcdefghijklmnop",
                OutputPath = output,
                AcceptSensitiveRegistryLocation = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.SensitiveRegistryLocation);
            Assert.IsFalse(result.RegistryLocationRedacted.Contains("abcdefghijklmnop", StringComparison.Ordinal));

            var manifestText = File.ReadAllText(result.ManifestPath);
            Assert.IsFalse(manifestText.Contains("abcdefghijklmnop", StringComparison.Ordinal));
            StringAssert.Contains(manifestText, "[REDACTED");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task RunTaskCommand_RegisterRequiresExplicitAcceptance()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var runner = new FakeTaskSchedulerRunner();
            var service = new PluginUpdateScheduleService(paths, runner);
            var plan = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                OutputPath = output
            });

            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = "register",
                ManifestPath = plan.ManifestPath
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("accept-task-registration", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual(0, runner.Calls.Count);
            Assert.IsFalse(result.RegisteredTask);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task RunTaskCommand_AcceptsLegacyGoatShotManifestSchema()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var service = new PluginUpdateScheduleService(paths);
            var plan = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                OutputPath = output
            });
            var manifestText = File.ReadAllText(plan.ManifestPath)
                .Replace(PluginUpdateScheduleService.CurrentSchemaVersion, PluginUpdateScheduleService.LegacySchemaVersion, StringComparison.Ordinal);
            File.WriteAllText(plan.ManifestPath, manifestText);

            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = "status",
                ManifestPath = plan.ManifestPath,
                DryRun = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task RunTaskCommand_RegisterDryRunDoesNotInvokeScheduler()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var runner = new FakeTaskSchedulerRunner();
            var service = new PluginUpdateScheduleService(paths, runner);
            var plan = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                OutputPath = output,
                TaskName = "GoatShot Dry Run Updates"
            });

            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = "register",
                ManifestPath = plan.ManifestPath,
                AcceptTaskRegistration = true,
                DryRun = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.WouldRegisterTask);
            Assert.IsFalse(result.RegisteredTask);
            Assert.AreEqual(0, runner.Calls.Count);
            StringAssert.Contains(result.Message, "dry-run");
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldExecute);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task RunTaskCommand_RegisterWithAcceptanceInvokesRegisterScriptOnly()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var runner = new FakeTaskSchedulerRunner
            {
                NextResult = new PluginUpdateTaskSchedulerProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "Registered scheduled task: GoatShot Test Plugin Updates"
                }
            };
            var service = new PluginUpdateScheduleService(paths, runner);
            var plan = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                OutputPath = output,
                TaskName = "GoatShot Test Plugin Updates"
            });

            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = "register",
                ManifestPath = plan.ManifestPath,
                AcceptTaskRegistration = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.WouldRegisterTask);
            Assert.IsTrue(result.RegisteredTask);
            Assert.AreEqual(1, runner.Calls.Count);
            Assert.AreEqual("powershell", runner.Calls[0].FileName);
            CollectionAssert.Contains(runner.Calls[0].Arguments.ToList(), "-File");
            CollectionAssert.Contains(runner.Calls[0].Arguments.ToList(), plan.RegisterScriptPath);
            CollectionAssert.DoesNotContain(runner.Calls[0].Arguments.ToList(), plan.RunScriptPath);
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldTrust);
            Assert.IsFalse(result.WouldEnable);
            Assert.IsFalse(result.WouldAllowlist);
            Assert.IsFalse(result.WouldExecute);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task RunTaskCommand_StatusRedactsSchedulerOutput()
    {
        await WithTempPathsAsync(paths =>
        {
            var output = Path.Combine(paths.LocalRoot, "schedule-output");
            var runner = new FakeTaskSchedulerRunner
            {
                NextResult = new PluginUpdateTaskSchedulerProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = "registered=true taskName=GoatShot Updates token=abcdefghijklmnop state=Ready"
                }
            };
            var service = new PluginUpdateScheduleService(paths, runner);
            var plan = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = "samples\\local-plugins\\registry.json",
                OutputPath = output,
                TaskName = "GoatShot Updates"
            });

            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = "status",
                ManifestPath = plan.ManifestPath
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.RegisteredTask);
            Assert.AreEqual(1, runner.Calls.Count);
            StringAssert.Contains(result.ProcessArguments, "[REDACTED-SCRIPT]");
            Assert.IsFalse(result.ProcessArguments.Contains("$taskName", StringComparison.Ordinal));
            Assert.IsFalse(result.StandardOutput.Contains("abcdefghijklmnop", StringComparison.Ordinal));
            StringAssert.Contains(result.StandardOutput, "[REDACTED");
            Assert.IsFalse(result.WouldInstall);
            Assert.IsFalse(result.WouldExecute);
            return Task.CompletedTask;
        });
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-plugin-schedule-test-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeTaskSchedulerRunner : IPluginUpdateTaskSchedulerRunner
    {
        public List<FakeTaskSchedulerCall> Calls { get; } = new();

        public PluginUpdateTaskSchedulerProcessResult NextResult { get; set; } = new()
        {
            ExitCode = 0,
            StandardOutput = "ok"
        };

        public PluginUpdateTaskSchedulerProcessResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            Calls.Add(new FakeTaskSchedulerCall(fileName, arguments.ToList(), timeout));
            return new PluginUpdateTaskSchedulerProcessResult
            {
                FileName = fileName,
                Arguments = arguments.ToList(),
                ExitCode = NextResult.ExitCode,
                TimedOut = NextResult.TimedOut,
                StandardOutput = NextResult.StandardOutput,
                StandardError = NextResult.StandardError
            };
        }
    }

    private sealed record FakeTaskSchedulerCall(
        string FileName,
        List<string> Arguments,
        TimeSpan Timeout);
}

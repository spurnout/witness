using System.Diagnostics;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class PluginUpdateScheduleService
{
    public const string CurrentSchemaVersion = "goatshot.plugin-update-schedule.v1";
    public const string StatusCommand = "status";
    public const string RegisterCommand = "register";
    public const string UnregisterCommand = "unregister";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly IPluginUpdateTaskSchedulerRunner _schedulerRunner;

    public PluginUpdateScheduleService(AppPaths paths)
        : this(paths, new PowerShellPluginUpdateTaskSchedulerRunner())
    {
    }

    public PluginUpdateScheduleService(AppPaths paths, IPluginUpdateTaskSchedulerRunner schedulerRunner)
    {
        _paths = paths;
        _schedulerRunner = schedulerRunner;
    }

    public PluginUpdateSchedulePlanResult CreatePlan(PluginUpdateSchedulePlanRequest request)
    {
        var outputRoot = ResolveOutputRoot(request.OutputPath);
        var mode = NormalizeMode(request.Mode);
        var intervalHours = NormalizeIntervalHours(request.IntervalHours);
        var registryScan = SensitiveTextDetector.Scan(request.RegistryLocation);
        var result = new PluginUpdateSchedulePlanResult
        {
            Succeeded = false,
            RegistryLocation = SensitiveTextDetector.Redact(request.RegistryLocation),
            RegistryLocationRedacted = registryScan.RedactedText,
            SensitiveRegistryLocation = registryScan.Findings.Count > 0,
            Mode = mode,
            IntervalHours = intervalHours,
            TaskName = string.IsNullOrWhiteSpace(request.TaskName)
                ? "GoatShot Plugin Background Updates"
                : request.TaskName.Trim(),
            OutputPath = outputRoot,
            LocalRoot = _paths.LocalRoot,
            LibraryRoot = _paths.LibraryRoot,
            CliPath = ResolveCliPath(request.CliPath),
            StatePath = ResolveStatePath(request.StatePath, outputRoot),
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            WouldRegisterTask = false,
            RegisteredTask = false,
            MutationBoundary = "Scheduled plugin update handoff can run background check-only or stage-only commands; install/trust/enable/allowlist/execute remain explicit operator actions."
        };

        if (string.IsNullOrWhiteSpace(request.RegistryLocation))
        {
            result.Issues.Add("A registry path or URL is required.");
        }

        if (mode is not PluginBackgroundUpdateService.CheckOnlyMode and not PluginBackgroundUpdateService.StageOnlyMode)
        {
            result.Issues.Add("Mode must be check-only or stage-only. Install, trust, enable, allowlist, execute, hosted marketplace, and remote execution modes are not allowed.");
        }

        if (registryScan.Findings.Count > 0 && !request.AcceptSensitiveRegistryLocation)
        {
            result.Issues.Add("The registry location appears to contain sensitive text. Use a tokenless local path or pass explicit sensitive-registry acceptance before writing scheduler scripts.");
        }

        if (result.Issues.Count > 0)
        {
            result.Message = "Plugin update schedule plan failed validation.";
            return result;
        }

        Directory.CreateDirectory(outputRoot);
        result.RunScriptPath = Path.Combine(outputRoot, "run-plugin-background-updates.ps1");
        result.RegisterScriptPath = Path.Combine(outputRoot, "register-plugin-background-updates.ps1");
        result.UnregisterScriptPath = Path.Combine(outputRoot, "unregister-plugin-background-updates.ps1");
        result.ManifestPath = Path.Combine(outputRoot, "plugin-update-schedule.json");
        result.LogPath = Path.Combine(outputRoot, "last-run.log");

        WriteRunScript(result, request.RegistryLocation);
        WriteRegisterScript(result);
        WriteUnregisterScript(result);
        WriteManifest(result);

        result.Succeeded = true;
        result.Message = "Plugin update schedule handoff was generated. No task was registered and no plugin code was run.";
        result.NextActions.Add($"Review {result.RunScriptPath} and {result.RegisterScriptPath}.");
        result.NextActions.Add("Run the registration script from an operator-approved Windows account if scheduled background checks are desired.");
        result.NextActions.Add("Review staged packages before using install-staged, plugins activate, dry-run, or run.");
        return result;
    }

    public PluginUpdateTaskSchedulerCommandResult RunTaskCommand(PluginUpdateTaskSchedulerCommandRequest request)
    {
        var command = NormalizeTaskCommand(request.Command);
        var manifestPath = ResolveManifestPath(request.ManifestPath);
        var result = new PluginUpdateTaskSchedulerCommandResult
        {
            Succeeded = false,
            Command = command,
            ManifestPath = manifestPath,
            DryRun = request.DryRun,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            WouldRegisterTask = command == RegisterCommand,
            WouldUnregisterTask = command == UnregisterCommand,
            MutationBoundary = "Plugin update task lifecycle can query, register, or unregister the Windows scheduled task only; plugin install/trust/enable/allowlist/execute remain separate explicit operator actions."
        };

        if (string.IsNullOrWhiteSpace(request.ManifestPath))
        {
            result.Issues.Add("A schedule manifest path is required.");
        }

        if (command is not StatusCommand and not RegisterCommand and not UnregisterCommand)
        {
            result.Issues.Add("Task command must be status, register, or unregister.");
        }

        var manifest = result.Issues.Count == 0
            ? ReadManifest(manifestPath, result)
            : null;

        if (manifest is not null)
        {
            ApplyManifest(result, manifest, request.TaskName);
        }

        if (command == RegisterCommand && !request.AcceptTaskRegistration)
        {
            result.Issues.Add("Task registration requires --accept-task-registration. No scheduled task was registered.");
        }

        if (command == UnregisterCommand && !request.AcceptTaskRemoval)
        {
            result.Issues.Add("Task removal requires --accept-task-removal. No scheduled task was removed.");
        }

        if (manifest is not null)
        {
            if ((command == RegisterCommand || command == StatusCommand) && !File.Exists(result.RegisterScriptPath))
            {
                result.Issues.Add("Register script from the manifest was not found.");
            }

            if (command == UnregisterCommand && !File.Exists(result.UnregisterScriptPath))
            {
                result.Issues.Add("Unregister script from the manifest was not found.");
            }

            if (!File.Exists(result.RunScriptPath))
            {
                result.Warnings.Add("Run script from the manifest was not found; registration would create a scheduled task that cannot run.");
            }
        }

        if (result.Issues.Count > 0)
        {
            result.Message = "Plugin update task command failed validation.";
            return result;
        }

        if (request.DryRun)
        {
            result.Succeeded = true;
            result.RegisteredTask = false;
            result.Message = command switch
            {
                RegisterCommand => "Plugin update task registration dry-run completed. No scheduled task was registered.",
                UnregisterCommand => "Plugin update task removal dry-run completed. No scheduled task was removed.",
                _ => "Plugin update task status dry-run completed. Task Scheduler was not queried."
            };
            AddTaskCommandNextActions(result);
            return result;
        }

        var process = command switch
        {
            StatusCommand => _schedulerRunner.Run(
                "powershell",
                new[]
                {
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    BuildStatusScript(result.TaskName)
                },
                TimeSpan.FromSeconds(NormalizeTimeoutSeconds(request.TimeoutSeconds))),
            RegisterCommand => _schedulerRunner.Run(
                "powershell",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", result.RegisterScriptPath },
                TimeSpan.FromSeconds(NormalizeTimeoutSeconds(request.TimeoutSeconds))),
            UnregisterCommand => _schedulerRunner.Run(
                "powershell",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", result.UnregisterScriptPath },
                TimeSpan.FromSeconds(NormalizeTimeoutSeconds(request.TimeoutSeconds))),
            _ => throw new InvalidOperationException("Unexpected task command.")
        };

        ApplyProcessResult(result, process);
        result.RegisteredTask = command switch
        {
            StatusCommand => result.StandardOutput.Contains("registered=true", StringComparison.OrdinalIgnoreCase),
            RegisterCommand => result.Succeeded,
            UnregisterCommand => false,
            _ => false
        };
        result.Message = BuildTaskCommandMessage(result);
        AddTaskCommandNextActions(result);
        return result;
    }

    private static void WriteRunScript(PluginUpdateSchedulePlanResult result, string registryLocation)
    {
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            $"$env:GOATSHOT_LOCAL_ROOT = {PowerShellString(result.LocalRoot)}",
            $"$env:GOATSHOT_LIBRARY_ROOT = {PowerShellString(result.LibraryRoot)}",
            $"$goatShot = {PowerShellString(result.CliPath)}",
            "$arguments = @(",
            "    '--plugin-background-update',",
            "    '--registry',",
            $"    {PowerShellString(registryLocation)},",
            "    '--mode',",
            $"    {PowerShellString(result.Mode)},",
            "    '--interval-hours',",
            $"    {PowerShellString(result.IntervalHours.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))},",
            "    '--state',",
            $"    {PowerShellString(result.StatePath)}",
            ")",
            $"& $goatShot @arguments *> {PowerShellString(result.LogPath)}",
            "exit $LASTEXITCODE"
        };
        File.WriteAllLines(result.RunScriptPath, lines);
    }

    private static void WriteRegisterScript(PluginUpdateSchedulePlanResult result)
    {
        var intervalMinutes = Math.Max(1, (int)Math.Ceiling(result.IntervalHours * 60d));
        var description = "GoatShot governed plugin background update handoff. Runs check-only or stage-only; does not install, trust, enable, allowlist, or execute plugin code.";
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            $"$taskName = {PowerShellString(result.TaskName)}",
            $"$scriptPath = {PowerShellString(result.RunScriptPath)}",
            "$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File ' + ('\"' + $scriptPath + '\"'))",
            "$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Minutes " + intervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") -RepetitionDuration (New-TimeSpan -Days 3650)",
            $"Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Description {PowerShellString(description)} -Force | Out-Null",
            "Write-Output \"Registered scheduled task: $taskName\""
        };
        File.WriteAllLines(result.RegisterScriptPath, lines);
    }

    private static void WriteUnregisterScript(PluginUpdateSchedulePlanResult result)
    {
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            $"Unregister-ScheduledTask -TaskName {PowerShellString(result.TaskName)} -Confirm:$false",
            $"Write-Output {PowerShellString("Unregistered scheduled task: " + result.TaskName)}"
        };
        File.WriteAllLines(result.UnregisterScriptPath, lines);
    }

    private static void WriteManifest(PluginUpdateSchedulePlanResult result)
    {
        var manifest = new PluginUpdateScheduleManifest
        {
            SchemaVersion = CurrentSchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RegistryLocation = result.RegistryLocationRedacted,
            Mode = result.Mode,
            IntervalHours = result.IntervalHours,
            TaskName = result.TaskName,
            LocalRoot = result.LocalRoot,
            LibraryRoot = result.LibraryRoot,
            CliPath = result.CliPath,
            StatePath = result.StatePath,
            RunScriptPath = result.RunScriptPath,
            RegisterScriptPath = result.RegisterScriptPath,
            UnregisterScriptPath = result.UnregisterScriptPath,
            LogPath = result.LogPath,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            WouldRegisterTask = false,
            RegisteredTask = false,
            MutationBoundary = result.MutationBoundary
        };
        File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static PluginUpdateScheduleManifest? ReadManifest(
        string manifestPath,
        PluginUpdateTaskSchedulerCommandResult result)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                result.Issues.Add("Schedule manifest was not found.");
                return null;
            }

            var manifest = JsonSerializer.Deserialize<PluginUpdateScheduleManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
            if (manifest is null)
            {
                result.Issues.Add("Schedule manifest could not be parsed.");
                return null;
            }

            if (!string.Equals(manifest.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                result.Issues.Add($"Schedule manifest schema is unsupported: {manifest.SchemaVersion}.");
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            result.Issues.Add("Schedule manifest could not be read: " + ex.Message);
            return null;
        }
    }

    private static void ApplyManifest(
        PluginUpdateTaskSchedulerCommandResult result,
        PluginUpdateScheduleManifest manifest,
        string? taskNameOverride)
    {
        result.RegistryLocation = manifest.RegistryLocation;
        result.Mode = manifest.Mode;
        result.IntervalHours = manifest.IntervalHours;
        result.TaskName = string.IsNullOrWhiteSpace(taskNameOverride)
            ? manifest.TaskName
            : taskNameOverride.Trim();
        result.RunScriptPath = manifest.RunScriptPath;
        result.RegisterScriptPath = manifest.RegisterScriptPath;
        result.UnregisterScriptPath = manifest.UnregisterScriptPath;
        result.LogPath = manifest.LogPath;
        result.StatePath = manifest.StatePath;
        result.OutputPath = Path.GetDirectoryName(result.ManifestPath) ?? string.Empty;
    }

    private static void ApplyProcessResult(
        PluginUpdateTaskSchedulerCommandResult result,
        PluginUpdateTaskSchedulerProcessResult process)
    {
        result.ProcessFileName = process.FileName;
        result.ProcessArguments = SafeArgumentsForDisplay(process.Arguments);
        result.ExitCode = process.ExitCode;
        result.TimedOut = process.TimedOut;
        result.StandardOutput = SensitiveTextDetector.Redact(process.StandardOutput);
        result.StandardError = SensitiveTextDetector.Redact(process.StandardError);
        result.Succeeded = !process.TimedOut && process.ExitCode == 0;
        if (process.TimedOut)
        {
            result.Issues.Add("Task Scheduler command timed out.");
        }
        else if (process.ExitCode != 0)
        {
            result.Issues.Add($"Task Scheduler command exited with code {process.ExitCode}.");
        }
    }

    private static string BuildTaskCommandMessage(PluginUpdateTaskSchedulerCommandResult result)
    {
        if (!result.Succeeded)
        {
            return result.Command switch
            {
                RegisterCommand => "Plugin update task registration failed.",
                UnregisterCommand => "Plugin update task removal failed.",
                _ => "Plugin update task status query failed."
            };
        }

        return result.Command switch
        {
            RegisterCommand => "Plugin update task was registered. No plugin was installed, trusted, enabled, allowlisted, or executed.",
            UnregisterCommand => "Plugin update task was unregistered. No plugin was installed, trusted, enabled, allowlisted, or executed.",
            _ => result.RegisteredTask
                ? "Plugin update task is registered."
                : "Plugin update task is not registered."
        };
    }

    private static void AddTaskCommandNextActions(PluginUpdateTaskSchedulerCommandResult result)
    {
        if (result.Command == StatusCommand)
        {
            result.NextActions.Add("Use register with --accept-task-registration only after reviewing the run and register scripts.");
        }
        else if (result.Command == RegisterCommand)
        {
            result.NextActions.Add("Monitor the generated last-run log and plugin background update state file.");
            result.NextActions.Add("Review staged packages before using install-staged, plugins activate, dry-run, or run.");
        }
        else
        {
            result.NextActions.Add("Regenerate the handoff with plugins schedule-updates before registering a replacement task.");
        }

        result.NextActions.Add("Install, trust, enable, allowlist, and execute remain explicit separate operator actions.");
    }

    private string ResolveOutputRoot(string? outputPath)
    {
        return string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_paths.LocalRoot, "plugin-update-schedule")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
    }

    private string ResolveCliPath(string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(cliPath));
        }

        var installedPath = new PersonalInstallService(new StartupRegistrationService()).InstalledExecutablePath;
        return File.Exists(installedPath)
            ? installedPath
            : Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GoatShot.exe");
    }

    private string ResolveStatePath(string? statePath, string outputRoot)
    {
        return string.IsNullOrWhiteSpace(statePath)
            ? Path.Combine(outputRoot, "plugin-background-updates-state.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(statePath));
    }

    private static string ResolveManifestPath(string? manifestPath)
    {
        return string.IsNullOrWhiteSpace(manifestPath)
            ? string.Empty
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(manifestPath));
    }

    private static string NormalizeMode(string? mode)
    {
        mode = (mode ?? PluginBackgroundUpdateService.CheckOnlyMode).Trim().ToLowerInvariant();
        return mode switch
        {
            "check" or "check-only" or "summary" or "passive" => PluginBackgroundUpdateService.CheckOnlyMode,
            "stage" or "stage-only" or "stage-updates" => PluginBackgroundUpdateService.StageOnlyMode,
            _ => mode
        };
    }

    private static double NormalizeIntervalHours(double intervalHours)
    {
        if (!double.IsFinite(intervalHours) || intervalHours <= 0d)
        {
            return 24d;
        }

        return Math.Min(24d * 30d, Math.Max(1d / 60d, intervalHours));
    }

    private static string NormalizeTaskCommand(string? command)
    {
        command = (command ?? StatusCommand).Trim().ToLowerInvariant();
        return command switch
        {
            "status" or "state" or "inspect" or "query" => StatusCommand,
            "register" or "install" or "enable" => RegisterCommand,
            "unregister" or "remove" or "disable" => UnregisterCommand,
            _ => command
        };
    }

    private static int NormalizeTimeoutSeconds(int timeoutSeconds)
    {
        return timeoutSeconds <= 0
            ? 30
            : Math.Min(300, Math.Max(5, timeoutSeconds));
    }

    private static string BuildStatusScript(string taskName)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "$taskName = " + PowerShellString(taskName),
            "$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue",
            "if ($null -eq $task) { Write-Output 'registered=false'; exit 0 }",
            "Write-Output ('registered=true taskName=' + $task.TaskName + ' state=' + $task.State + ' taskPath=' + $task.TaskPath)"
        });
    }

    private static string SafeArgumentsForDisplay(IReadOnlyList<string> arguments)
    {
        var safe = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument.Equals("-Command", StringComparison.OrdinalIgnoreCase))
            {
                safe.Add("-Command [REDACTED-SCRIPT]");
                i++;
                continue;
            }

            safe.Add(SafeArgumentForDisplay(argument));
        }

        return string.Join(" ", safe);
    }

    private static string SafeArgumentForDisplay(string argument)
    {
        if (argument.StartsWith("-Command", StringComparison.OrdinalIgnoreCase))
        {
            return "-Command [REDACTED-SCRIPT]";
        }

        return SensitiveTextDetector.Redact(argument);
    }

    private static string PowerShellString(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}

public interface IPluginUpdateTaskSchedulerRunner
{
    PluginUpdateTaskSchedulerProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout);
}

public sealed class PowerShellPluginUpdateTaskSchedulerRunner : IPluginUpdateTaskSchedulerRunner
{
    public PluginUpdateTaskSchedulerProcessResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            return new PluginUpdateTaskSchedulerProcessResult
            {
                FileName = fileName,
                Arguments = arguments.ToList(),
                ExitCode = -1,
                StandardError = "Failed to start PowerShell."
            };
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup after timeout.
            }
        }

        if (exited)
        {
            Task.WaitAll(stdoutTask, stderrTask);
        }

        return new PluginUpdateTaskSchedulerProcessResult
        {
            FileName = fileName,
            Arguments = arguments.ToList(),
            ExitCode = exited ? process.ExitCode : -1,
            TimedOut = !exited,
            StandardOutput = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty,
            StandardError = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : "Timed out before stderr could be read."
        };
    }
}

public sealed class PluginUpdateSchedulePlanRequest
{
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = PluginBackgroundUpdateService.CheckOnlyMode;
    public double IntervalHours { get; set; } = 24d;
    public string? OutputPath { get; set; }
    public string? StatePath { get; set; }
    public string? CliPath { get; set; }
    public string? TaskName { get; set; }
    public bool AcceptSensitiveRegistryLocation { get; set; }
}

public sealed class PluginUpdateTaskSchedulerCommandRequest
{
    public string Command { get; set; } = PluginUpdateScheduleService.StatusCommand;
    public string ManifestPath { get; set; } = string.Empty;
    public string? TaskName { get; set; }
    public bool AcceptTaskRegistration { get; set; }
    public bool AcceptTaskRemoval { get; set; }
    public bool DryRun { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class PluginUpdateSchedulePlanResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string RegistryLocationRedacted { get; set; } = string.Empty;
    public bool SensitiveRegistryLocation { get; set; }
    public string Mode { get; set; } = string.Empty;
    public double IntervalHours { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string LocalRoot { get; set; } = string.Empty;
    public string LibraryRoot { get; set; } = string.Empty;
    public string CliPath { get; set; } = string.Empty;
    public string StatePath { get; set; } = string.Empty;
    public string RunScriptPath { get; set; } = string.Empty;
    public string RegisterScriptPath { get; set; } = string.Empty;
    public string UnregisterScriptPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string MutationBoundary { get; set; } = string.Empty;
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public bool WouldRegisterTask { get; set; }
    public bool RegisteredTask { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
}

public sealed class PluginUpdateTaskSchedulerCommandResult
{
    public bool Succeeded { get; set; }
    public string Command { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public double IntervalHours { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string StatePath { get; set; } = string.Empty;
    public string RunScriptPath { get; set; } = string.Empty;
    public string RegisterScriptPath { get; set; } = string.Empty;
    public string UnregisterScriptPath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string ProcessFileName { get; set; } = string.Empty;
    public string ProcessArguments { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool DryRun { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public string MutationBoundary { get; set; } = string.Empty;
    public bool WouldRegisterTask { get; set; }
    public bool WouldUnregisterTask { get; set; }
    public bool RegisteredTask { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
}

public sealed class PluginUpdateTaskSchedulerProcessResult
{
    public string FileName { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
}

public sealed class PluginUpdateScheduleManifest
{
    public string SchemaVersion { get; set; } = PluginUpdateScheduleService.CurrentSchemaVersion;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public double IntervalHours { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string LocalRoot { get; set; } = string.Empty;
    public string LibraryRoot { get; set; } = string.Empty;
    public string CliPath { get; set; } = string.Empty;
    public string StatePath { get; set; } = string.Empty;
    public string RunScriptPath { get; set; } = string.Empty;
    public string RegisterScriptPath { get; set; } = string.Empty;
    public string UnregisterScriptPath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string MutationBoundary { get; set; } = string.Empty;
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public bool WouldRegisterTask { get; set; }
    public bool RegisteredTask { get; set; }
}

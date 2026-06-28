using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class LocalPluginService
{
    public const string CurrentSchemaVersion = "goatshot.plugin.v1";

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;

    public LocalPluginService(AppPaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public IReadOnlyList<LocalPluginRecord> Discover()
    {
        Directory.CreateDirectory(_paths.PluginsRoot);
        return FindManifestPaths()
            .Select(ReadManifest)
            .OrderBy(record => record.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.ManifestPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LocalPluginActivationResult Activate(LocalPluginActivationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            return LocalPluginActivationResult.Blocked(request.PluginId, "Plugin id is required.");
        }

        var plugin = Discover().FirstOrDefault(record =>
            record.PluginId.Equals(request.PluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            return LocalPluginActivationResult.Blocked(
                request.PluginId,
                $"Plugin was not found under {_paths.PluginsRoot}.");
        }

        if (!plugin.IsValid)
        {
            return LocalPluginActivationResult.Blocked(
                plugin.PluginId,
                "Plugin manifest is invalid: " + string.Join("; ", plugin.Issues));
        }

        var allowlistEntries = ResolveActivationAllowlistEntries(plugin, request, _settings, out var blockReason);
        if (!string.IsNullOrWhiteSpace(blockReason))
        {
            return LocalPluginActivationResult.Blocked(plugin.PluginId, blockReason);
        }

        var requestedMutation = request.Trust ||
            request.Enable ||
            request.EnableLocalPlugins ||
            request.AllowAllActions ||
            allowlistEntries.Count > 0;
        if (!requestedMutation)
        {
            return LocalPluginActivationResult.Blocked(
                plugin.PluginId,
                "No activation change was requested. Use --trust, --enable, --enable-local-plugins, --allow-action, or --allow-all-actions.");
        }

        if (!request.AcceptRisk)
        {
            return LocalPluginActivationResult.Blocked(
                plugin.PluginId,
                "Reviewed plugin activation requires --accept-risk. No settings were changed and no plugin code was run.");
        }

        var policy = ManagedPolicyService.LoadEffective(_settings);
        if (policy.DisableLocalPlugins &&
            (request.EnableLocalPlugins || request.Enable || allowlistEntries.Count > 0))
        {
            return LocalPluginActivationResult.Blocked(
                plugin.PluginId,
                $"Local plugins are disabled by managed policy ({policy.Source}).");
        }

        var alreadyTrusted = ContainsId(_settings.TrustedPluginIds, plugin.PluginId);
        if (!alreadyTrusted &&
            !request.Trust &&
            (request.Enable || allowlistEntries.Count > 0))
        {
            return LocalPluginActivationResult.Blocked(
                plugin.PluginId,
                "Trust is required before enablement or action allowlisting. Re-run with --trust after reviewing the plugin manifest.");
        }

        var result = new LocalPluginActivationResult
        {
            Succeeded = true,
            PluginId = plugin.PluginId,
            Name = plugin.Name,
            Version = plugin.Version,
            ManifestPath = plugin.ManifestPath,
            WasGloballyEnabled = _settings.EnableLocalPlugins,
            WasTrusted = alreadyTrusted,
            WasEnabled = ContainsId(_settings.EnabledPluginIds, plugin.PluginId),
            WouldExecute = false,
            StartedProcess = false
        };

        if (request.EnableLocalPlugins && !_settings.EnableLocalPlugins)
        {
            _settings.EnableLocalPlugins = true;
            result.ChangedSettings.Add("EnableLocalPlugins");
        }

        if (request.Trust && AddUniqueId(_settings.TrustedPluginIds, plugin.PluginId))
        {
            result.ChangedSettings.Add("TrustedPluginIds");
        }

        if (request.Enable && AddUniqueId(_settings.EnabledPluginIds, plugin.PluginId))
        {
            result.ChangedSettings.Add("EnabledPluginIds");
        }

        foreach (var entry in allowlistEntries)
        {
            if (AddUniqueId(_settings.AllowedPluginActionIds, entry))
            {
                result.AddedAllowlistEntries.Add(entry);
                result.ChangedSettings.Add("AllowedPluginActionIds");
            }
        }

        if (request.Enable && !_settings.EnableLocalPlugins)
        {
            result.Warnings.Add("Plugin is enabled, but local plugins remain globally disabled. Use --enable-local-plugins after reviewing the global local-plugin posture.");
        }

        var refreshed = Discover().FirstOrDefault(record =>
            record.PluginId.Equals(plugin.PluginId, StringComparison.OrdinalIgnoreCase));
        result.IsGloballyEnabled = _settings.EnableLocalPlugins;
        result.IsTrusted = ContainsId(_settings.TrustedPluginIds, plugin.PluginId);
        result.IsEnabled = refreshed?.IsEnabled ?? false;
        if (refreshed is not null)
        {
            result.AllowedActions.AddRange(refreshed.Actions
                .Where(action => action.IsAllowed)
                .Select(action => action.QualifiedActionId)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        result.ChangedSettings = result.ChangedSettings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.Message = result.ChangedSettings.Count == 0
            ? "Plugin activation metadata was already current. No plugin code was run."
            : "Plugin activation metadata updated. No plugin code was run.";
        return result;
    }

    public LocalPluginDryRunResult DryRunAction(string pluginId, string actionId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(actionId))
        {
            return LocalPluginDryRunResult.Blocked(pluginId, actionId, "Plugin id and action id are required.");
        }

        var plugin = Discover().FirstOrDefault(record =>
            record.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            return LocalPluginDryRunResult.Blocked(pluginId, actionId, $"Plugin was not found under {_paths.PluginsRoot}.");
        }

        if (!plugin.IsValid)
        {
            return LocalPluginDryRunResult.Blocked(plugin.PluginId, actionId, "Plugin manifest is invalid: " + string.Join("; ", plugin.Issues));
        }

        var action = plugin.Actions.FirstOrDefault(item =>
            item.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase) ||
            item.QualifiedActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            return LocalPluginDryRunResult.Blocked(plugin.PluginId, actionId, $"Plugin action was not found: {actionId}.");
        }

        if (!ManagedPolicyService.IsLocalPluginActionAllowed(_settings, plugin.PluginId, action.QualifiedActionId, out var managedPolicyReason))
        {
            return LocalPluginDryRunResult.Blocked(plugin.PluginId, action.ActionId, managedPolicyReason)
                .WithAction(action);
        }

        if (!plugin.IsTrusted)
        {
            return LocalPluginDryRunResult.Blocked(plugin.PluginId, action.ActionId, "Plugin is not trusted. Review the manifest and use plugins activate with --trust before enabling actions.")
                .WithAction(action);
        }

        if (!plugin.IsEnabled)
        {
            var reason = _settings.EnableLocalPlugins
                ? "Plugin is trusted but not enabled. Use plugins activate with --enable before running actions."
                : "Local plugins are globally disabled. Use plugins activate with --enable-local-plugins and --enable before running actions.";
            return LocalPluginDryRunResult.Blocked(plugin.PluginId, action.ActionId, reason)
                .WithAction(action);
        }

        if (!action.IsAllowed)
        {
            return LocalPluginDryRunResult.Blocked(
                    plugin.PluginId,
                    action.ActionId,
                    $"Plugin action is not allowlisted. Use plugins activate with --allow-action {action.ActionId} or --allow-all-actions after reviewing the manifest.")
                .WithAction(action);
        }

        return new LocalPluginDryRunResult
        {
            Succeeded = true,
            WouldExecute = true,
            PluginId = plugin.PluginId,
            ActionId = action.ActionId,
            QualifiedActionId = action.QualifiedActionId,
            Scope = action.Scope,
            Message = action.Execution is null
                ? $"Dry run approved for {action.QualifiedActionId}. No plugin process was started. This action has no execution block."
                : $"Dry run approved for {action.QualifiedActionId}. No plugin process was started. Command: {SensitiveTextDetector.Redact(action.Execution.Command)}."
        };
    }

    public async Task<LocalPluginExecutionResult> ExecuteActionAsync(
        string pluginId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var dryRun = DryRunAction(pluginId, actionId);
        if (!dryRun.Succeeded)
        {
            return LocalPluginExecutionResult.Blocked(dryRun.PluginId, dryRun.ActionId, dryRun.Message);
        }

        var plugin = Discover().First(record =>
            record.PluginId.Equals(dryRun.PluginId, StringComparison.OrdinalIgnoreCase));
        var action = plugin.Actions.First(item =>
            item.ActionId.Equals(dryRun.ActionId, StringComparison.OrdinalIgnoreCase));
        if (action.Execution is null)
        {
            return LocalPluginExecutionResult.Blocked(
                plugin.PluginId,
                action.ActionId,
                $"Plugin action {action.QualifiedActionId} has no execution block.");
        }

        var timeoutSeconds = Math.Clamp(action.Execution.TimeoutSeconds ?? 10, 1, 120);
        var command = ResolveCommandPath(plugin.SourceDirectory, action.Execution.Command);
        var workingDirectory = ResolveWorkingDirectory(plugin.SourceDirectory, action.Execution.WorkingDirectory);
        var start = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in action.Execution.Arguments ?? [])
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return LocalPluginExecutionResult.Blocked(plugin.PluginId, action.ActionId, "Plugin process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new LocalPluginExecutionResult
            {
                Succeeded = process.ExitCode == 0,
                StartedProcess = true,
                PluginId = plugin.PluginId,
                ActionId = action.ActionId,
                QualifiedActionId = action.QualifiedActionId,
                ExitCode = process.ExitCode,
                Command = SensitiveTextDetector.Redact(command),
                TimedOut = false,
                StandardOutput = RedactAndLimit(stdout),
                StandardError = RedactAndLimit(stderr),
                Message = process.ExitCode == 0
                    ? $"Plugin action {action.QualifiedActionId} completed."
                    : $"Plugin action {action.QualifiedActionId} exited with code {process.ExitCode}."
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new LocalPluginExecutionResult
            {
                Succeeded = false,
                StartedProcess = true,
                PluginId = plugin.PluginId,
                ActionId = action.ActionId,
                QualifiedActionId = action.QualifiedActionId,
                Command = SensitiveTextDetector.Redact(command),
                TimedOut = true,
                Message = $"Plugin action {action.QualifiedActionId} timed out after {timeoutSeconds} second(s)."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return LocalPluginExecutionResult.Blocked(
                plugin.PluginId,
                action.ActionId,
                $"Plugin action {action.QualifiedActionId} could not run: {SensitiveTextDetector.Redact(ex.Message)}");
        }
    }

    public string GetDiagnosticsSummary()
    {
        var plugins = Discover();
        var valid = plugins.Count(plugin => plugin.IsValid);
        var trusted = plugins.Count(plugin => plugin.IsTrusted);
        var enabled = plugins.Count(plugin => plugin.IsEnabled);
        var allowedActions = plugins.SelectMany(plugin => plugin.Actions).Count(action => action.IsAllowed);
        var issueCount = plugins.Sum(plugin => plugin.Issues.Count);
        var issueSummary = issueCount == 0
            ? "No plugin manifest issues detected."
            : $"{issueCount} plugin manifest issue(s): " + string.Join("; ", plugins
                .SelectMany(plugin => plugin.Issues.Select(issue => $"{plugin.PluginId}: {issue}"))
                .Select(SensitiveTextDetector.Redact)
                .Take(6));

        return $"Local plugins root: {_paths.PluginsRoot}. Discovery found {plugins.Count} manifest(s), {valid} valid, {trusted} trusted, {enabled} enabled, {allowedActions} allowlisted action(s). Local plugins are {(_settings.EnableLocalPlugins ? "globally enabled" : "globally disabled")} and discovered plugins are disabled/untrusted by default. {issueSummary} Remote plugin package validation/staging/install-staged, optional RSA SHA-256 package signature verification, explicit update apply, passive update summaries, governed background check/stage-only runs, Task Scheduler handoff generation, explicit scheduler task lifecycle commands, and reviewed activation metadata writes are available through the plugins CLI and Settings Plugins, but staged or newly installed packages are not trusted, enabled, allowlisted, installed, or executed automatically. Hosted marketplaces and automatic install/trust/enable/allowlist/execute update behavior are out of scope.";
    }

    private static List<string> ResolveActivationAllowlistEntries(
        LocalPluginRecord plugin,
        LocalPluginActivationRequest request,
        AppSettings settings,
        out string blockReason)
    {
        blockReason = string.Empty;
        var entries = new List<string>();
        if (request.AllowAllActions)
        {
            foreach (var action in plugin.Actions)
            {
                if (!ManagedPolicyService.IsLocalPluginActionAllowed(
                        settings,
                        plugin.PluginId,
                        action.QualifiedActionId,
                        out blockReason))
                {
                    return [];
                }
            }

            return [$"{plugin.PluginId}:*"];
        }

        foreach (var requestedAction in request.ActionIds.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var trimmed = requestedAction.Trim();
            var action = plugin.Actions.FirstOrDefault(item =>
                item.ActionId.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                item.QualifiedActionId.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                blockReason = $"Plugin action was not found: {SafeText(trimmed)}.";
                return [];
            }

            if (!ManagedPolicyService.IsLocalPluginActionAllowed(
                    settings,
                    plugin.PluginId,
                    action.QualifiedActionId,
                    out blockReason))
            {
                return [];
            }

            if (!entries.Any(value => value.Equals(action.QualifiedActionId, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(action.QualifiedActionId);
            }
        }

        return entries;
    }

    private IReadOnlyList<string> FindManifestPaths()
    {
        var paths = new List<string>();
        if (!Directory.Exists(_paths.PluginsRoot))
        {
            return paths;
        }

        paths.AddRange(Directory
            .EnumerateDirectories(_paths.PluginsRoot)
            .Select(directory => Path.Combine(directory, "plugin.json"))
            .Where(File.Exists));
        paths.AddRange(Directory.EnumerateFiles(_paths.PluginsRoot, "*.plugin.json", SearchOption.TopDirectoryOnly));
        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private LocalPluginRecord ReadManifest(string path)
    {
        LocalPluginManifest? manifest;
        try
        {
            var json = File.ReadAllText(path);
            manifest = JsonSerializer.Deserialize<LocalPluginManifest>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalPluginRecord
            {
                PluginId = Path.GetFileNameWithoutExtension(Path.GetDirectoryName(path)) ?? "invalid-plugin",
                Name = "Invalid plugin",
                Version = string.Empty,
                ManifestPath = path,
                IsValid = false,
                Issues = { SensitiveTextDetector.Redact(ex.Message) }
            };
        }

        if (manifest is null)
        {
            return new LocalPluginRecord
            {
                PluginId = "invalid-plugin",
                Name = "Invalid plugin",
                Version = string.Empty,
                ManifestPath = path,
                IsValid = false,
                Issues = { "Plugin manifest did not contain an object." }
            };
        }

        var record = new LocalPluginRecord
        {
            PluginId = SafeText(manifest.Id),
            Name = SafeText(manifest.Name),
            Version = SafeText(manifest.Version),
            ManifestPath = path,
            SourceDirectory = Path.GetDirectoryName(path) ?? _paths.PluginsRoot
        };
        Validate(manifest, record);
        record.IsTrusted = ContainsId(_settings.TrustedPluginIds, manifest.Id);
        record.IsEnabled = _settings.EnableLocalPlugins &&
            record.IsTrusted &&
            ContainsId(_settings.EnabledPluginIds, manifest.Id);
        record.Actions.AddRange(BuildActionStatuses(manifest, record));
        record.IsValid = record.Issues.Count == 0;
        return record;
    }

    private static void Validate(LocalPluginManifest manifest, LocalPluginRecord record)
    {
        if (!manifest.SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal))
        {
            record.Issues.Add($"Unsupported schemaVersion. Expected {CurrentSchemaVersion}.");
        }

        if (!IsValidId(manifest.Id))
        {
            record.Issues.Add("Plugin id must be 3-64 characters and contain only letters, numbers, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            record.Issues.Add("Plugin name is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            record.Issues.Add("Plugin version is required.");
        }

        var contributions = manifest.Actions.Count +
            manifest.ShareDestinations.Count +
            manifest.WorkflowActions.Count +
            manifest.Diagnostics.Count;
        if (contributions == 0)
        {
            record.Issues.Add("Plugin manifest must declare at least one action, share destination, workflow action, or diagnostic contribution.");
        }

        var duplicateIds = AllContributions(manifest)
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Id))
            .GroupBy(contribution => contribution.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var id in duplicateIds)
        {
            record.Issues.Add($"Duplicate plugin contribution id: {SafeText(id)}.");
        }

        foreach (var contribution in AllContributions(manifest))
        {
            if (!IsValidId(contribution.Id))
            {
                record.Issues.Add("Plugin contribution id must be 3-64 characters and contain only letters, numbers, dots, underscores, or hyphens.");
            }

            if (string.IsNullOrWhiteSpace(contribution.Name))
            {
                record.Issues.Add($"Plugin contribution '{SafeText(contribution.Id)}' requires a name.");
            }
        }
    }

    private List<LocalPluginActionStatus> BuildActionStatuses(LocalPluginManifest manifest, LocalPluginRecord record)
    {
        return AllContributions(manifest)
            .Select(contribution =>
            {
                var qualifiedId = $"{manifest.Id}:{contribution.Id}";
                return new LocalPluginActionStatus
                {
                    ActionId = SafeText(contribution.Id),
                    QualifiedActionId = SafeText(qualifiedId),
                    Name = SafeText(contribution.Name),
                    Scope = contribution.Scope,
                    Execution = ParseExecution(contribution.Contribution.ExtensionData),
                    IsAllowed = record.IsTrusted &&
                        record.IsEnabled &&
                        ManagedPolicyService.IsLocalPluginActionAllowed(_settings, record.PluginId, qualifiedId, out _) &&
                        (ContainsId(_settings.AllowedPluginActionIds, qualifiedId) ||
                            ContainsId(_settings.AllowedPluginActionIds, $"{manifest.Id}:*"))
                };
            })
            .ToList();
    }

    private static IEnumerable<(string Id, string Name, string Scope, LocalPluginContributionManifest Contribution)> AllContributions(LocalPluginManifest manifest)
    {
        foreach (var item in manifest.Actions)
        {
            yield return (item.Id, item.Name, "action", item);
        }

        foreach (var item in manifest.ShareDestinations)
        {
            yield return (item.Id, item.Name, "share", item);
        }

        foreach (var item in manifest.WorkflowActions)
        {
            yield return (item.Id, item.Name, "workflow", item);
        }

        foreach (var item in manifest.Diagnostics)
        {
            yield return (item.Id, item.Name, "diagnostic", item);
        }
    }

    private static LocalPluginActionExecutionManifest? ParseExecution(
        IReadOnlyDictionary<string, JsonElement> extensionData)
    {
        if (!extensionData.TryGetValue("execution", out var executionElement) ||
            executionElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            return executionElement.Deserialize<LocalPluginActionExecutionManifest>(JsonOptions);
        }
        catch (JsonException)
        {
            return new LocalPluginActionExecutionManifest();
        }
    }

    private static string ResolveCommandPath(string pluginRoot, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("Plugin execution command is required.");
        }

        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (Path.IsPathFullyQualified(command))
        {
            return Path.GetFullPath(command);
        }

        if (command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar))
        {
            var resolved = Path.GetFullPath(Path.Combine(pluginRoot, command));
            if (!IsInsideDirectory(resolved, pluginRoot))
            {
                throw new InvalidOperationException("Plugin execution command path must stay inside the plugin directory.");
            }

            return resolved;
        }

        return command;
    }

    private static string ResolveWorkingDirectory(string pluginRoot, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return pluginRoot;
        }

        var resolved = Path.GetFullPath(Path.Combine(pluginRoot, Environment.ExpandEnvironmentVariables(workingDirectory)));
        if (!IsInsideDirectory(resolved, pluginRoot))
        {
            throw new InvalidOperationException("Plugin working directory must stay inside the plugin directory.");
        }

        return resolved;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactAndLimit(string value)
    {
        var redacted = SensitiveTextDetector.Redact(value ?? string.Empty);
        return redacted.Length <= 4000 ? redacted : redacted[..4000] + "...";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort timeout cleanup only.
        }
    }

    private static bool ContainsId(IEnumerable<string>? values, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || values is null)
        {
            return false;
        }

        return values.Any(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AddUniqueId(List<string> values, string id)
    {
        if (ContainsId(values, id))
        {
            return false;
        }

        values.Add(id);
        return true;
    }

    private static bool IsValidId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && IdPattern.IsMatch(value.Trim());
    }

    private static string SafeText(string? value)
    {
        return SensitiveTextDetector.Redact(value?.Trim() ?? string.Empty);
    }
}

public sealed class LocalPluginManifest
{
    public string SchemaVersion { get; set; } = LocalPluginService.CurrentSchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<LocalPluginContributionManifest> Actions { get; set; } = new();
    public List<LocalPluginContributionManifest> ShareDestinations { get; set; } = new();
    public List<LocalPluginContributionManifest> WorkflowActions { get; set; } = new();
    public List<LocalPluginContributionManifest> Diagnostics { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}

public sealed class LocalPluginContributionManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}

public sealed class LocalPluginRecord
{
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public bool IsTrusted { get; set; }
    public bool IsEnabled { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<LocalPluginActionStatus> Actions { get; set; } = new();
}

public sealed class LocalPluginActionStatus
{
    public string ActionId { get; set; } = string.Empty;
    public string QualifiedActionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public LocalPluginActionExecutionManifest? Execution { get; set; }
}

public sealed class LocalPluginActionExecutionManifest
{
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public string WorkingDirectory { get; set; } = string.Empty;
    public int? TimeoutSeconds { get; set; }
}

public sealed class LocalPluginActivationRequest
{
    public string PluginId { get; set; } = string.Empty;
    public bool AcceptRisk { get; set; }
    public bool Trust { get; set; }
    public bool Enable { get; set; }
    public bool EnableLocalPlugins { get; set; }
    public bool AllowAllActions { get; set; }
    public List<string> ActionIds { get; set; } = new();
}

public sealed class LocalPluginActivationResult
{
    public bool Succeeded { get; set; }
    public bool WouldExecute { get; set; }
    public bool StartedProcess { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public bool WasGloballyEnabled { get; set; }
    public bool IsGloballyEnabled { get; set; }
    public bool WasTrusted { get; set; }
    public bool IsTrusted { get; set; }
    public bool WasEnabled { get; set; }
    public bool IsEnabled { get; set; }
    public List<string> AddedAllowlistEntries { get; set; } = new();
    public List<string> AllowedActions { get; set; } = new();
    public List<string> ChangedSettings { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Message { get; set; } = string.Empty;

    public static LocalPluginActivationResult Blocked(string? pluginId, string message)
    {
        return new LocalPluginActivationResult
        {
            Succeeded = false,
            WouldExecute = false,
            StartedProcess = false,
            PluginId = pluginId ?? string.Empty,
            Message = SensitiveTextDetector.Redact(message)
        };
    }
}

public sealed class LocalPluginDryRunResult
{
    public bool Succeeded { get; set; }
    public bool WouldExecute { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string QualifiedActionId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public static LocalPluginDryRunResult Blocked(string? pluginId, string? actionId, string message)
    {
        return new LocalPluginDryRunResult
        {
            Succeeded = false,
            WouldExecute = false,
            PluginId = pluginId ?? string.Empty,
            ActionId = actionId ?? string.Empty,
            Message = SensitiveTextDetector.Redact(message)
        };
    }

    public LocalPluginDryRunResult WithAction(LocalPluginActionStatus action)
    {
        ActionId = action.ActionId;
        QualifiedActionId = action.QualifiedActionId;
        Scope = action.Scope;
        return this;
    }
}

public sealed class LocalPluginExecutionResult
{
    public bool Succeeded { get; set; }
    public bool StartedProcess { get; set; }
    public bool TimedOut { get; set; }
    public int? ExitCode { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string QualifiedActionId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;

    public static LocalPluginExecutionResult Blocked(string? pluginId, string? actionId, string message)
    {
        return new LocalPluginExecutionResult
        {
            Succeeded = false,
            StartedProcess = false,
            PluginId = pluginId ?? string.Empty,
            ActionId = actionId ?? string.Empty,
            Message = SensitiveTextDetector.Redact(message)
        };
    }
}

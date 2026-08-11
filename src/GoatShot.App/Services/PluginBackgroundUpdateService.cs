using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class PluginBackgroundUpdateService
{
    public const string CheckOnlyMode = "check-only";
    public const string StageOnlyMode = "stage-only";
    public const string CurrentSchemaVersion = "receipts.plugin-background-updates.v1";
    public const string LegacySchemaVersion = "goatshot.plugin-background-updates.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly RemotePluginPackageService _remotePlugins;

    public PluginBackgroundUpdateService(AppPaths paths, RemotePluginPackageService remotePlugins)
    {
        _paths = paths;
        _remotePlugins = remotePlugins;
    }

    public async Task<PluginBackgroundUpdateRunResult> RunAsync(
        PluginBackgroundUpdateRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new PluginBackgroundUpdateRunResult
        {
            RegistryLocation = SensitiveTextDetector.Redact(request.RegistryLocation ?? string.Empty),
            Mode = NormalizeMode(request.Mode),
            IntervalHours = NormalizeIntervalHours(request.IntervalHours),
            StatePath = ResolveStatePath(request.StatePath),
            Forced = request.Force,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            MutationBoundary = "Background plugin updates can check registries or stage packages only; install/trust/enable/allowlist/execute remain explicit operator actions."
        };

        if (string.IsNullOrWhiteSpace(request.RegistryLocation))
        {
            result.Issues.Add("A registry path or URL is required.");
            result.Message = "Plugin background update run failed validation.";
            return result;
        }

        if (result.Mode is not CheckOnlyMode and not StageOnlyMode)
        {
            result.Issues.Add("Mode must be check-only or stage-only. Install, trust, enable, allowlist, execute, and hosted marketplace modes are not allowed.");
            result.Message = "Plugin background update run failed validation.";
            return result;
        }

        var previousState = ReadState(result.StatePath);
        result.PreviousLastRunUtc = previousState?.LastRunUtc;
        result.PreviousNextRunUtc = previousState?.NextRunUtc;
        result.Due = request.Force || previousState?.NextRunUtc is null || previousState.NextRunUtc <= DateTimeOffset.UtcNow;
        if (!result.Due)
        {
            result.Succeeded = true;
            result.Skipped = true;
            result.Message = $"Plugin background update run skipped until {previousState!.NextRunUtc:O}.";
            result.NextActions.Add("Use --force to run before the next scheduled check time.");
            result.NextActions.Add("No registry was contacted and no plugin package was staged.");
            return result;
        }

        result.StartedAtUtc = DateTimeOffset.UtcNow;
        if (result.Mode == CheckOnlyMode)
        {
            var summary = await _remotePlugins.SummarizeUpdatesAsync(request.RegistryLocation, cancellationToken);
            ApplySummary(result, summary);
            result.PerformedAction = "checked";
            result.Message = summary.Succeeded
                ? $"Plugin background update check completed: available={summary.AvailableCount}, staged={summary.StagedCount}, installed={summary.InstalledCount}, blocked={summary.BlockedCount}, incompatible={summary.IncompatibleCount}."
                : "Plugin background update check failed.";
        }
        else
        {
            var apply = await _remotePlugins.ApplyUpdatesAsync(
                request.RegistryLocation,
                installStaged: false,
                replaceExisting: false,
                cancellationToken);
            ApplyStageOnly(result, apply);
            result.PerformedAction = "stage-only";
            result.Message = apply.Succeeded
                ? $"Plugin background stage-only run completed: staged={apply.StagedCount}, skipped={apply.SkippedCount}, failed={apply.FailedCount}."
                : "Plugin background stage-only run failed.";
        }

        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        result.NextRunUtc = result.CompletedAtUtc.Value.AddHours(result.IntervalHours);
        result.Succeeded = result.Issues.Count == 0;
        result.NextActions.Add("Review update summaries and staged packages before installing, trusting, enabling, allowlisting, or running plugin actions.");
        result.NextActions.Add("Use explicit `receipts plugins install-staged`, trust, enable, and action allowlist steps after review.");
        result.NextActions.Add("Hosted marketplace accounts, publication, reviews, ratings, payments, and remote execution remain separate later-scope work.");

        WriteState(result);
        return result;
    }

    private void ApplySummary(PluginBackgroundUpdateRunResult result, RemotePluginUpdateSummaryResult summary)
    {
        result.RemoteSummary = summary;
        result.RegistryLocation = summary.RegistryLocation;
        result.PluginCount = summary.PluginCount;
        result.AvailableCount = summary.AvailableCount;
        result.StagedCount = summary.StagedCount;
        result.InstalledCount = summary.InstalledCount;
        result.BlockedCount = summary.BlockedCount;
        result.IncompatibleCount = summary.IncompatibleCount;
        result.WouldStagePackage = false;
        result.Issues.AddRange(summary.Issues);
        result.Warnings.AddRange(summary.Warnings);
    }

    private void ApplyStageOnly(PluginBackgroundUpdateRunResult result, RemotePluginUpdateApplyResult apply)
    {
        result.RemoteApply = apply;
        result.RegistryLocation = apply.RegistryLocation;
        result.PluginCount = apply.PluginCount;
        result.StagedCount = apply.StagedCount;
        result.InstalledCount = 0;
        result.SkippedCount = apply.SkippedCount;
        result.FailedCount = apply.FailedCount;
        result.WouldStagePackage = apply.StagedCount > 0;
        result.WouldInstall = false;
        result.Issues.AddRange(apply.Issues);
        result.Warnings.AddRange(apply.Warnings);
    }

    private PluginBackgroundUpdateState? ReadState(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<PluginBackgroundUpdateState>(File.ReadAllText(path), JsonOptions);
            return state is not null &&
                (state.SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal) ||
                 state.SchemaVersion.Equals(LegacySchemaVersion, StringComparison.Ordinal))
                    ? state
                    : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(PluginBackgroundUpdateRunResult result)
    {
        var state = new PluginBackgroundUpdateState
        {
            SchemaVersion = CurrentSchemaVersion,
            RegistryLocation = result.RegistryLocation,
            Mode = result.Mode,
            LastRunUtc = result.CompletedAtUtc,
            NextRunUtc = result.NextRunUtc,
            LastMessage = result.Message,
            AvailableCount = result.AvailableCount,
            StagedCount = result.StagedCount,
            InstalledCount = result.InstalledCount,
            BlockedCount = result.BlockedCount,
            IncompatibleCount = result.IncompatibleCount,
            FailedCount = result.FailedCount,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false
        };

        Directory.CreateDirectory(Path.GetDirectoryName(result.StatePath)!);
        File.WriteAllText(result.StatePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private string ResolveStatePath(string? statePath)
    {
        return string.IsNullOrWhiteSpace(statePath)
            ? Path.Combine(_paths.LocalRoot, "plugin-background-updates.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(statePath));
    }

    private static string NormalizeMode(string? mode)
    {
        mode = (mode ?? CheckOnlyMode).Trim().ToLowerInvariant();
        return mode switch
        {
            "check" or "check-only" or "summary" or "passive" => CheckOnlyMode,
            "stage" or "stage-only" or "stage-updates" => StageOnlyMode,
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
}

public sealed class PluginBackgroundUpdateRunRequest
{
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = PluginBackgroundUpdateService.CheckOnlyMode;
    public double IntervalHours { get; set; } = 24d;
    public bool Force { get; set; }
    public string? StatePath { get; set; }
}

public sealed class PluginBackgroundUpdateRunResult
{
    public bool Succeeded { get; set; }
    public bool Skipped { get; set; }
    public bool Due { get; set; }
    public bool Forced { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string StatePath { get; set; } = string.Empty;
    public double IntervalHours { get; set; }
    public DateTimeOffset? PreviousLastRunUtc { get; set; }
    public DateTimeOffset? PreviousNextRunUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? NextRunUtc { get; set; }
    public string PerformedAction { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MutationBoundary { get; set; } = string.Empty;
    public int PluginCount { get; set; }
    public int AvailableCount { get; set; }
    public int StagedCount { get; set; }
    public int InstalledCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int BlockedCount { get; set; }
    public int IncompatibleCount { get; set; }
    public bool WouldStagePackage { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public RemotePluginUpdateSummaryResult? RemoteSummary { get; set; }
    public RemotePluginUpdateApplyResult? RemoteApply { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
}

public sealed class PluginBackgroundUpdateState
{
    public string SchemaVersion { get; set; } = PluginBackgroundUpdateService.CurrentSchemaVersion;
    public string RegistryLocation { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTimeOffset? LastRunUtc { get; set; }
    public DateTimeOffset? NextRunUtc { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public int AvailableCount { get; set; }
    public int StagedCount { get; set; }
    public int InstalledCount { get; set; }
    public int BlockedCount { get; set; }
    public int IncompatibleCount { get; set; }
    public int FailedCount { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
}

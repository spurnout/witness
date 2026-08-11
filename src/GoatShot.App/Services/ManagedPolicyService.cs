using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ManagedPolicyService
{
    public const string PolicyPathEnvironmentVariable = "RECEIPTS_MANAGED_POLICY_PATH";
    public const string LegacyPolicyPathEnvironmentVariable = "GOATSHOT_MANAGED_POLICY_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static EffectiveManagedPolicy LoadEffective(AppSettings settings, string? policyPath = null)
    {
        var basePolicy = settings.ManagedPolicy ?? new ManagedPolicySettings();
        var source = "app settings";
        var externalPath = ResolvePolicyPath(policyPath);
        if (!string.IsNullOrWhiteSpace(externalPath) && File.Exists(externalPath))
        {
            try
            {
                var externalJson = File.ReadAllText(externalPath);
                var externalPolicy = JsonSerializer.Deserialize<ManagedPolicySettings>(externalJson, JsonOptions);
                if (externalPolicy is not null)
                {
                    source = $"app settings + {externalPath}";
                    basePolicy = MergeDenyWins(basePolicy, externalPolicy);
                }
            }
            catch
            {
                source = $"app settings; managed policy file unreadable: {externalPath}";
            }
        }

        var destinations = ParseAllowedDestinations(basePolicy.AllowedShareDestinations);
        return new EffectiveManagedPolicy(
            basePolicy.DisableAi,
            basePolicy.DisableUploads,
            basePolicy.DisableCustomScripts,
            basePolicy.DisableCustomWebhooks,
            basePolicy.DisableLocalPlugins,
            basePolicy.DisableBrowserExtension,
            basePolicy.DisableAndroidCapture,
            basePolicy.DisableVirtualPrinterImport,
            basePolicy.RequirePrivateCaptureMode,
            basePolicy.DisableDiagnosticsLogCapture,
            basePolicy.RetentionDays,
            destinations,
            NormalizeIds(basePolicy.AllowedPluginIds).ToList(),
            NormalizeIds(basePolicy.AllowedPluginActionIds).ToList(),
            source,
            BuildSummary(basePolicy, destinations, source));
    }

    public static bool IsAiAllowed(AppSettings settings, out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableAi)
        {
            reason = $"AI is disabled by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsLocalPluginActionAllowed(
        AppSettings settings,
        string pluginId,
        string qualifiedActionId,
        out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableLocalPlugins)
        {
            reason = $"Local plugins are disabled by managed policy ({policy.Source}).";
            return false;
        }

        if (policy.AllowedPluginIds.Count > 0 &&
            !policy.AllowedPluginIds.Any(value => value.Equals(pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            reason = $"Plugin {pluginId} is not allowed by managed policy ({policy.Source}).";
            return false;
        }

        if (policy.AllowedPluginActionIds.Count > 0 &&
            !policy.AllowedPluginActionIds.Any(value =>
                value.Equals(qualifiedActionId, StringComparison.OrdinalIgnoreCase) ||
                value.Equals($"{pluginId}:*", StringComparison.OrdinalIgnoreCase)))
        {
            reason = $"Plugin action {qualifiedActionId} is not allowed by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsAndroidCaptureAllowed(AppSettings settings, out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableAndroidCapture)
        {
            reason = $"Android capture is disabled by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsBrowserExtensionAllowed(AppSettings settings, out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableBrowserExtension)
        {
            reason = $"Browser extension handoff is disabled by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsVirtualPrinterImportAllowed(AppSettings settings, out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableVirtualPrinterImport)
        {
            reason = $"Virtual-printer import is disabled by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsShareDestinationAllowed(AppSettings settings, ShareDestination destination, out string reason)
    {
        var policy = LoadEffective(settings);
        if (policy.DisableUploads && ShareService.IsExternalDestination(destination))
        {
            reason = $"External sharing/upload is disabled by managed policy ({policy.Source}).";
            return false;
        }

        if (policy.DisableCustomScripts && destination == ShareDestination.CustomScript)
        {
            reason = $"Custom scripts are disabled by managed policy ({policy.Source}).";
            return false;
        }

        if (policy.DisableCustomWebhooks && destination == ShareDestination.CustomWebhook)
        {
            reason = $"Custom webhooks are disabled by managed policy ({policy.Source}).";
            return false;
        }

        if (policy.AllowedShareDestinations.Count > 0 &&
            !policy.AllowedShareDestinations.Contains(destination))
        {
            reason = $"Share destination {destination} is not allowed by managed policy ({policy.Source}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static ManagedPolicySettings MergeDenyWins(ManagedPolicySettings settingsPolicy, ManagedPolicySettings externalPolicy)
    {
        return new ManagedPolicySettings
        {
            DisableAi = settingsPolicy.DisableAi || externalPolicy.DisableAi,
            DisableUploads = settingsPolicy.DisableUploads || externalPolicy.DisableUploads,
            DisableCustomScripts = settingsPolicy.DisableCustomScripts || externalPolicy.DisableCustomScripts,
            DisableCustomWebhooks = settingsPolicy.DisableCustomWebhooks || externalPolicy.DisableCustomWebhooks,
            DisableLocalPlugins = settingsPolicy.DisableLocalPlugins || externalPolicy.DisableLocalPlugins,
            DisableBrowserExtension = settingsPolicy.DisableBrowserExtension || externalPolicy.DisableBrowserExtension,
            DisableAndroidCapture = settingsPolicy.DisableAndroidCapture || externalPolicy.DisableAndroidCapture,
            DisableVirtualPrinterImport = settingsPolicy.DisableVirtualPrinterImport || externalPolicy.DisableVirtualPrinterImport,
            RequirePrivateCaptureMode = settingsPolicy.RequirePrivateCaptureMode || externalPolicy.RequirePrivateCaptureMode,
            DisableDiagnosticsLogCapture = settingsPolicy.DisableDiagnosticsLogCapture || externalPolicy.DisableDiagnosticsLogCapture,
            RetentionDays = MergeRetentionDays(settingsPolicy.RetentionDays, externalPolicy.RetentionDays),
            AllowedShareDestinations = MergeAllowedDestinations(
                settingsPolicy.AllowedShareDestinations,
                externalPolicy.AllowedShareDestinations),
            AllowedPluginIds = MergeAllowedStrings(
                settingsPolicy.AllowedPluginIds,
                externalPolicy.AllowedPluginIds),
            AllowedPluginActionIds = MergeAllowedStrings(
                settingsPolicy.AllowedPluginActionIds,
                externalPolicy.AllowedPluginActionIds)
        };
    }

    public static ManagedPolicySettings MergeDenyWinsForImport(ManagedPolicySettings settingsPolicy, ManagedPolicySettings externalPolicy)
    {
        return MergeDenyWins(settingsPolicy, externalPolicy);
    }

    private static List<string> MergeAllowedDestinations(IReadOnlyList<string>? first, IReadOnlyList<string>? second)
    {
        var firstSet = NormalizeDestinations(first);
        var secondSet = NormalizeDestinations(second);
        if (firstSet.Count == 0)
        {
            return secondSet.ToList();
        }

        if (secondSet.Count == 0)
        {
            return firstSet.ToList();
        }

        firstSet.IntersectWith(secondSet);
        return firstSet.ToList();
    }

    private static List<string> MergeAllowedStrings(IReadOnlyList<string>? first, IReadOnlyList<string>? second)
    {
        var firstSet = NormalizeIds(first);
        var secondSet = NormalizeIds(second);
        if (firstSet.Count == 0)
        {
            return secondSet.ToList();
        }

        if (secondSet.Count == 0)
        {
            return firstSet.ToList();
        }

        firstSet.IntersectWith(secondSet);
        return firstSet.ToList();
    }

    private static HashSet<string> NormalizeIds(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static int MergeRetentionDays(int first, int second)
    {
        if (first <= 0)
        {
            return Math.Max(0, second);
        }

        if (second <= 0)
        {
            return Math.Max(0, first);
        }

        return Math.Min(first, second);
    }

    private static HashSet<string> NormalizeDestinations(IReadOnlyList<string>? values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (TryParseDestinationName(value, out var destination))
            {
                result.Add(destination.ToString());
            }
        }

        return result;
    }

    private static bool TryParseDestinationName(string value, out ShareDestination destination)
    {
        if (Enum.TryParse(value, ignoreCase: true, out destination))
        {
            return true;
        }

        var compact = value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<ShareDestination>())
        {
            var candidateCompact = candidate.ToString()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("/", string.Empty, StringComparison.Ordinal);
            if (candidateCompact.Equals(compact, StringComparison.OrdinalIgnoreCase))
            {
                destination = candidate;
                return true;
            }
        }

        var parsed = ShareService.ParseDestination(value);
        if (parsed != ShareDestination.ClipboardImage ||
            value.Equals("clipboard image", StringComparison.OrdinalIgnoreCase) ||
            value.Equals(nameof(ShareDestination.ClipboardImage), StringComparison.OrdinalIgnoreCase))
        {
            destination = parsed;
            return true;
        }

        destination = default;
        return false;
    }

    private static IReadOnlyList<ShareDestination> ParseAllowedDestinations(IReadOnlyList<string>? values)
    {
        return NormalizeDestinations(values)
            .Select(value => Enum.Parse<ShareDestination>(value))
            .OrderBy(destination => destination.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolvePolicyPath(string? policyPath)
    {
        policyPath = string.IsNullOrWhiteSpace(policyPath)
            ? BrandEnvironment.Resolve("MANAGED_POLICY_PATH").Value
            : policyPath;
        return string.IsNullOrWhiteSpace(policyPath)
            ? string.Empty
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(policyPath));
    }

    private static string BuildSummary(
        ManagedPolicySettings policy,
        IReadOnlyList<ShareDestination> allowedDestinations,
        string source)
    {
        var restrictions = new List<string>();
        if (policy.DisableAi)
        {
            restrictions.Add("AI disabled");
        }

        if (policy.DisableUploads)
        {
            restrictions.Add("external sharing/upload disabled");
        }

        if (policy.DisableCustomScripts)
        {
            restrictions.Add("custom scripts disabled");
        }

        if (policy.DisableCustomWebhooks)
        {
            restrictions.Add("custom webhooks disabled");
        }

        if (policy.DisableLocalPlugins)
        {
            restrictions.Add("local plugins disabled");
        }

        if (policy.DisableBrowserExtension)
        {
            restrictions.Add("browser extension disabled");
        }

        if (policy.DisableAndroidCapture)
        {
            restrictions.Add("Android capture disabled");
        }

        if (policy.DisableVirtualPrinterImport)
        {
            restrictions.Add("virtual-printer import disabled");
        }

        if (policy.RequirePrivateCaptureMode)
        {
            restrictions.Add("private capture mode required");
        }

        if (policy.DisableDiagnosticsLogCapture)
        {
            restrictions.Add("diagnostics log capture disabled");
        }

        if (policy.RetentionDays > 0)
        {
            restrictions.Add($"retention limited to {policy.RetentionDays} day(s)");
        }

        if (allowedDestinations.Count > 0)
        {
            restrictions.Add($"allowed destinations: {string.Join(", ", allowedDestinations)}");
        }

        if (policy.AllowedPluginIds.Count > 0)
        {
            restrictions.Add($"allowed plugins: {string.Join(", ", policy.AllowedPluginIds)}");
        }

        if (policy.AllowedPluginActionIds.Count > 0)
        {
            restrictions.Add($"allowed plugin actions: {string.Join(", ", policy.AllowedPluginActionIds)}");
        }

        return restrictions.Count == 0
            ? $"Managed policy source: {source}; no restrictions are active."
            : $"Managed policy source: {source}; {string.Join("; ", restrictions)}.";
    }
}

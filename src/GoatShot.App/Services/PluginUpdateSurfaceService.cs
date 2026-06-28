using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class PluginUpdateSurfaceService
{
    private static readonly string[] StatusOrder =
    [
        "available",
        "staged",
        "installed",
        "blocked",
        "incompatible",
        "not-installed"
    ];

    public PluginUpdateSurfaceSummary Create(RemotePluginUpdateSummaryResult result, string registryInput)
    {
        var registryForCommand = string.IsNullOrWhiteSpace(registryInput)
            ? result.RegistryLocation
            : registryInput.Trim();
        var summary = new PluginUpdateSurfaceSummary
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            RegistryLocation = result.RegistryLocation,
            CountsText = FormatCounts(result),
            MutationBoundary = "Passive check only: install=false, trust=false, enable=false, allowlist=false, execute=false.",
            CliCommand = $"goatshot plugins updates --registry {QuoteCliArgument(registryForCommand)} --json",
            PluginsRoot = result.PluginsRoot,
            StagingRoot = result.StagingRoot,
            Issues = result.Issues.ToList(),
            Warnings = result.Warnings.ToList(),
            NextActions = result.NextActions.ToList()
        };

        summary.Rows = result.Plugins
            .OrderBy(plugin => StatusIndex(plugin.Status))
            .ThenBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .Select(ToRow)
            .ToList();

        if (summary.Rows.Count == 0 && summary.Issues.Count == 0)
        {
            summary.NextActions.Add("No registry plugins were found. Choose another registry or validate the current one with the CLI.");
        }

        return summary;
    }

    private static PluginUpdateSurfaceRow ToRow(RemotePluginUpdateSummary plugin)
    {
        return new PluginUpdateSurfaceRow
        {
            Status = plugin.Status,
            PluginId = plugin.PluginId,
            Name = plugin.Name,
            VersionText = FormatVersions(plugin),
            GateText = FormatGates(plugin),
            Message = plugin.Message,
            NextAction = plugin.NextAction
        };
    }

    private static string FormatCounts(RemotePluginUpdateSummaryResult result)
    {
        return string.Join(
            " | ",
            $"Available {result.AvailableCount.ToString(CultureInfo.InvariantCulture)}",
            $"Staged {result.StagedCount.ToString(CultureInfo.InvariantCulture)}",
            $"Installed {result.InstalledCount.ToString(CultureInfo.InvariantCulture)}",
            $"Blocked {result.BlockedCount.ToString(CultureInfo.InvariantCulture)}",
            $"Incompatible {result.IncompatibleCount.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string FormatVersions(RemotePluginUpdateSummary plugin)
    {
        var installed = string.IsNullOrWhiteSpace(plugin.InstalledVersion)
            ? "none"
            : plugin.InstalledVersion;
        var staged = string.IsNullOrWhiteSpace(plugin.StagedVersion)
            ? "none"
            : plugin.StagedVersion;
        var registry = string.IsNullOrWhiteSpace(plugin.RegistryVersion)
            ? "unknown"
            : plugin.RegistryVersion;
        return $"installed {installed}, registry {registry}, staged {staged}";
    }

    private static string FormatGates(RemotePluginUpdateSummary plugin)
    {
        var gates = new List<string>();
        if (plugin.PolicyBlocked)
        {
            gates.Add("policy blocked");
        }

        if (plugin.Incompatible)
        {
            gates.Add("incompatible");
        }

        if (plugin.OperatorGateReasons.Count > 0)
        {
            gates.Add(string.Join("; ", plugin.OperatorGateReasons));
        }

        if (plugin.Staged)
        {
            gates.Add("staged package review required");
        }

        return string.Join(" | ", gates);
    }

    private static int StatusIndex(string status)
    {
        var index = Array.FindIndex(StatusOrder, value => value.Equals(status, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? StatusOrder.Length : index;
    }

    private static string QuoteCliArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"<registry.json-or-url>\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}

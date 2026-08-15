using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ProviderSetupSummaryService
{
    public ProviderSetupSummary Create(IReadOnlyList<ProviderDiagnosticRecord> diagnostics)
    {
        var records = diagnostics.ToList();
        var blocked = records
            .Where(record => record.Status.Equals("Blocked by policy", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var roadmap = records.Where(record => !record.CatalogImplemented).ToList();
        var ready = records
            .Where(record => record.ReadyForLocalAttempt && !blocked.Contains(record))
            .ToList();
        var needsConfiguration = records
            .Where(record => record.CatalogImplemented &&
                !record.ReadyForLocalAttempt &&
                !blocked.Contains(record))
            .ToList();
        var oauth = records
            .Where(record => record.CatalogImplemented &&
                record.AuthType.Contains("OAuth", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var summary = new ProviderSetupSummary
        {
            TotalProviders = records.Count,
            ReadyProviders = ready.Count,
            NeedsConfigurationProviders = needsConfiguration.Count,
            BlockedByPolicyProviders = blocked.Count,
            RoadmapProviders = roadmap.Count,
            OAuthProviderCount = oauth.Count,
            // States the total and the one number worth acting on. The breakdown lives in the tiles
            // below, which partition the same total -- restating all four here only invited the
            // reader to check arithmetic that previously did not add up.
            Headline =
                $"{records.Count} share destination{(records.Count == 1 ? string.Empty : "s")}, " +
                $"{ready.Count} ready to try.",
            Detail = BuildDetail(needsConfiguration, blocked, oauth)
        };

        // These four buckets partition the provider list: every destination appears in exactly one.
        // OAuth deliberately is not a tile here -- it cuts across ready and needs-setup, and showing
        // it alongside made the numbers look like they failed to add up.
        summary.Cards.Add(CreateCard(
            "Ready to try",
            ready.Count,
            ready,
            "Nothing is set up yet.",
            "Ready"));
        summary.Cards.Add(CreateCard(
            "Needs setup",
            needsConfiguration.Count,
            needsConfiguration,
            "Every available destination is set up.",
            "NeedsConfiguration"));
        summary.Cards.Add(CreateCard(
            "Blocked by policy",
            blocked.Count,
            blocked,
            "No destination is blocked on this machine.",
            "Blocked"));
        summary.Cards.Add(CreateCard(
            "Not available yet",
            roadmap.Count,
            roadmap,
            "Every destination in the list can be used.",
            "Roadmap"));

        return summary;
    }

    private static ProviderSetupSummaryCard CreateCard(
        string title,
        int count,
        IReadOnlyList<ProviderDiagnosticRecord> records,
        string fallbackDetail,
        string tone)
    {
        return new ProviderSetupSummaryCard
        {
            Title = title,
            CountLabel = count.ToString("N0"),
            Detail = records.Count == 0
                ? fallbackDetail
                : string.Join(", ", records.Select(record => record.ProviderName).Take(5)) +
                    (records.Count > 5 ? $", +{records.Count - 5} more" : string.Empty),
            Tone = tone
        };
    }

    private static string BuildDetail(
        IReadOnlyList<ProviderDiagnosticRecord> needsConfiguration,
        IReadOnlyList<ProviderDiagnosticRecord> blocked,
        IReadOnlyList<ProviderDiagnosticRecord> oauth)
    {
        var lead = blocked.Count > 0
            ? $"Policy blocks {blocked.Count} provider(s) on this machine. Ask your administrator before testing uploads."
            : needsConfiguration.Count > 0
                ? DescribeFirstMissingItem(needsConfiguration)
                : "Every available destination is set up.";

        // OAuth is reported here rather than as a tile: it describes how you sign in, not whether the
        // destination is ready, and several ready destinations use it too.
        return oauth.Count == 0
            ? lead
            : $"{lead} {oauth.Count} of these sign in through your browser, and will ask you to connect the first time you use them.";
    }

    private static string DescribeFirstMissingItem(IReadOnlyList<ProviderDiagnosticRecord> needsConfiguration)
    {
        var first = needsConfiguration[0];
        var missing = first.MissingSettings.Concat(first.MissingSecrets).FirstOrDefault();
        return string.IsNullOrWhiteSpace(missing)
            ? $"{first.ProviderName} needs setup before you can share to it."
            : $"{first.ProviderName} needs {missing}. Open its fields below for the full list.";
    }
}

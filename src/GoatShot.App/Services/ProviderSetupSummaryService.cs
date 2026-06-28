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
            Headline = $"{ready.Count} ready, {needsConfiguration.Count} need setup, {blocked.Count} policy-blocked, {roadmap.Count} roadmap.",
            Detail = BuildDetail(needsConfiguration, blocked, oauth)
        };

        summary.Cards.Add(CreateCard(
            "Ready to try",
            ready.Count,
            ready,
            "Local settings and saved secret markers are present.",
            "Ready"));
        summary.Cards.Add(CreateCard(
            "Need setup",
            needsConfiguration.Count,
            needsConfiguration,
            "Missing fields or saved secrets are listed in provider diagnostics.",
            "NeedsConfiguration"));
        summary.Cards.Add(CreateCard(
            "Policy blocked",
            blocked.Count,
            blocked,
            "Effective managed policy prevents local share attempts.",
            "Blocked"));
        summary.Cards.Add(CreateCard(
            "OAuth live proof pending",
            oauth.Count,
            oauth,
            "Local tokens or client IDs do not prove live consent or refresh behavior.",
            "OAuth"));
        summary.Cards.Add(CreateCard(
            "Roadmap",
            roadmap.Count,
            roadmap,
            "Adapter implementation is not available yet.",
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
        if (blocked.Count > 0)
        {
            return $"Policy blocks {blocked.Count} provider(s). Run provider diagnostics or admin-policy explain before testing uploads.";
        }

        if (needsConfiguration.Count > 0)
        {
            var first = needsConfiguration.First();
            var missing = first.MissingSettings.Concat(first.MissingSecrets).FirstOrDefault();
            return string.IsNullOrWhiteSpace(missing)
                ? $"{first.ProviderName} needs setup before a local share attempt."
                : $"{first.ProviderName} needs {missing}. Run provider diagnostics for the full list.";
        }

        if (oauth.Count > 0)
        {
            return "OAuth-backed providers may have local token markers, but live consent and refresh proof remain parked.";
        }

        return "Provider diagnostics are available from the CLI for detailed readiness and missing-item lists.";
    }
}

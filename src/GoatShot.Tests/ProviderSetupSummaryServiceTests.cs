using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ProviderSetupSummaryServiceTests
{
    [TestMethod]
    public void Create_GroupsProviderReadinessStates()
    {
        var summary = new ProviderSetupSummaryService().Create(new[]
        {
            Record("Local folder", "None", "Ready", ready: true),
            Record(
                "GitHub Issues",
                "Personal access token",
                "Needs configuration",
                missingSettings: new[] { "GitHub repository" },
                missingSecrets: new[] { "GitHub personal access token" }),
            Record("Dropbox", "OAuth", "Needs configuration", missingSecrets: new[] { "Dropbox access token" }),
            Record("Linear", "API key or OAuth", "Ready", ready: true),
            Record("YouTube", "OAuth", "Roadmap", implemented: false, missingSettings: new[] { "Provider adapter implementation" })
        });

        Assert.AreEqual(5, summary.TotalProviders);
        Assert.AreEqual(2, summary.ReadyProviders);
        Assert.AreEqual(2, summary.NeedsConfigurationProviders);
        Assert.AreEqual(0, summary.BlockedByPolicyProviders);
        Assert.AreEqual(1, summary.RoadmapProviders);
        Assert.AreEqual(2, summary.OAuthProviderCount);
        StringAssert.Contains(summary.Headline, "2 ready");
        StringAssert.Contains(summary.Headline, "2 need setup");
        StringAssert.Contains(summary.Detail, "GitHub repository");
        StringAssert.Contains(Card(summary, "Ready to try").Detail, "Local folder");
        StringAssert.Contains(Card(summary, "Need setup").Detail, "GitHub Issues");
        StringAssert.Contains(Card(summary, "OAuth live proof pending").Detail, "Dropbox");
        StringAssert.Contains(Card(summary, "Roadmap").Detail, "YouTube");
    }

    [TestMethod]
    public void Create_PrioritizesPolicyBlockedDetail()
    {
        var summary = new ProviderSetupSummaryService().Create(new[]
        {
            Record("Slack", "Incoming webhook", "Blocked by policy", missingSettings: new[] { "Slack webhook URL" }),
            Record("GitHub Issues", "Personal access token", "Needs configuration", missingSettings: new[] { "GitHub repository" })
        });

        Assert.AreEqual(1, summary.BlockedByPolicyProviders);
        Assert.AreEqual(1, summary.NeedsConfigurationProviders);
        StringAssert.Contains(summary.Detail, "Policy blocks 1 provider");
        StringAssert.Contains(Card(summary, "Policy blocked").Detail, "Slack");
    }

    [TestMethod]
    public void Create_DoesNotSurfaceSavedSecretMarkers()
    {
        var summary = new ProviderSetupSummaryService().Create(new[]
        {
            Record(
                "S3-compatible",
                "Access key",
                "Ready",
                ready: true,
                savedSecrets: new[] { "S3_SECRET_VALUE_SHOULD_NOT_APPEAR" })
        });

        var text = string.Join(
            " ",
            new[] { summary.Headline, summary.Detail }
                .Concat(summary.Cards.Select(card => $"{card.Title} {card.CountLabel} {card.Detail}")));

        Assert.IsFalse(text.Contains("S3_SECRET_VALUE_SHOULD_NOT_APPEAR", StringComparison.Ordinal));
    }

    private static ProviderSetupSummaryCard Card(ProviderSetupSummary summary, string title)
    {
        return summary.Cards.Single(card => card.Title == title);
    }

    private static ProviderDiagnosticRecord Record(
        string providerName,
        string authType,
        string status,
        bool ready = false,
        bool implemented = true,
        IReadOnlyList<string>? missingSettings = null,
        IReadOnlyList<string>? missingSecrets = null,
        IReadOnlyList<string>? savedSecrets = null)
    {
        return new ProviderDiagnosticRecord
        {
            ProviderName = providerName,
            DestinationName = providerName.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase),
            AuthType = authType,
            Status = status,
            CatalogImplemented = implemented,
            ReadyForLocalAttempt = ready,
            MissingSettings = missingSettings?.ToList() ?? new List<string>(),
            MissingSecrets = missingSecrets?.ToList() ?? new List<string>(),
            SavedSecrets = savedSecrets?.ToList() ?? new List<string>()
        };
    }
}

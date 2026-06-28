namespace GoatShot.App.Models;

public sealed class ProviderSetupSummary
{
    public int TotalProviders { get; set; }
    public int ReadyProviders { get; set; }
    public int NeedsConfigurationProviders { get; set; }
    public int BlockedByPolicyProviders { get; set; }
    public int RoadmapProviders { get; set; }
    public int OAuthProviderCount { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public List<ProviderSetupSummaryCard> Cards { get; set; } = new();
}

public sealed class ProviderSetupSummaryCard
{
    public string Title { get; set; } = string.Empty;
    public string CountLabel { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

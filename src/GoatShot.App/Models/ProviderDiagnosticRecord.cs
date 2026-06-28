namespace GoatShot.App.Models;

public sealed class ProviderDiagnosticRecord
{
    public string ProviderName { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ReadinessSummary { get; set; } = string.Empty;
    public bool CatalogImplemented { get; set; }
    public bool ReadyForLocalAttempt { get; set; }
    public bool SupportsPublicLinks { get; set; }
    public bool SupportsPrivateLinks { get; set; }
    public bool SupportsExpiration { get; set; }
    public bool SupportsPassword { get; set; }
    public List<string> ConfiguredSettings { get; set; } = new();
    public List<string> MissingSettings { get; set; } = new();
    public List<string> SavedSecrets { get; set; } = new();
    public List<string> MissingSecrets { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

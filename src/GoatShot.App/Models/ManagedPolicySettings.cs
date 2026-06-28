namespace GoatShot.App.Models;

public sealed class ManagedPolicySettings
{
    public bool DisableAi { get; set; }
    public bool DisableUploads { get; set; }
    public bool DisableCustomScripts { get; set; }
    public bool DisableCustomWebhooks { get; set; }
    public bool DisableLocalPlugins { get; set; }
    public bool DisableBrowserExtension { get; set; }
    public bool DisableAndroidCapture { get; set; }
    public bool DisableVirtualPrinterImport { get; set; }
    public bool RequirePrivateCaptureMode { get; set; }
    public bool DisableDiagnosticsLogCapture { get; set; }
    public int RetentionDays { get; set; }
    public List<string> AllowedShareDestinations { get; set; } = new();
    public List<string> AllowedPluginIds { get; set; } = new();
    public List<string> AllowedPluginActionIds { get; set; } = new();
}

public sealed record EffectiveManagedPolicy(
    bool DisableAi,
    bool DisableUploads,
    bool DisableCustomScripts,
    bool DisableCustomWebhooks,
    bool DisableLocalPlugins,
    bool DisableBrowserExtension,
    bool DisableAndroidCapture,
    bool DisableVirtualPrinterImport,
    bool RequirePrivateCaptureMode,
    bool DisableDiagnosticsLogCapture,
    int RetentionDays,
    IReadOnlyList<ShareDestination> AllowedShareDestinations,
    IReadOnlyList<string> AllowedPluginIds,
    IReadOnlyList<string> AllowedPluginActionIds,
    string Source,
    string Summary);

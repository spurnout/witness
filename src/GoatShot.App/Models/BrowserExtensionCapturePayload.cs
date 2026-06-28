namespace GoatShot.App.Models;

public sealed class BrowserExtensionCapturePayload
{
    public string SchemaVersion { get; set; } = string.Empty;
    public BrowserExtensionCaptureIntent Intent { get; set; } = new();
    public BrowserExtensionPageMetadata Page { get; set; } = new();
    public BrowserExtensionViewportMetadata Viewport { get; set; } = new();
    public BrowserExtensionFullPageMetadata FullPage { get; set; } = new();
    public BrowserExtensionSelectedElementMetadata SelectedElement { get; set; } = new();
    public BrowserExtensionStitchManifest Stitch { get; set; } = new();
    public BrowserExtensionStitchPackageExport StitchPackage { get; set; } = new();
    public BrowserExtensionConsent Consent { get; set; } = new();
    public List<BrowserExtensionConsoleEvent> ConsoleEvents { get; set; } = new();
    public List<BrowserExtensionNetworkEvent> NetworkEvents { get; set; } = new();
}

public sealed class BrowserExtensionCaptureIntent
{
    public string CaptureMode { get; set; } = "full-page";
    public bool FullPageCaptureRequested { get; set; }
    public bool IncludeDomMetadata { get; set; }
    public bool IncludeTelemetry { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class BrowserExtensionPageMetadata
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Referrer { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class BrowserExtensionViewportMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double DevicePixelRatio { get; set; } = 1d;
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
}

public sealed class BrowserExtensionFullPageMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int ScrollWidth { get; set; }
    public int ScrollHeight { get; set; }
}

public sealed class BrowserExtensionSelectedElementMetadata
{
    public bool Found { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public bool HasAccessibleName { get; set; }
    public BrowserExtensionRect AbsoluteRect { get; set; } = new();
    public BrowserExtensionRect ViewportRect { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class BrowserExtensionRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class BrowserExtensionStitchManifest
{
    public bool Requested { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CaptureApi { get; set; } = string.Empty;
    public int TileCount { get; set; }
    public int MaxTileCount { get; set; }
    public int OverlapPixels { get; set; }
    public int StickyHeaderMitigationPixels { get; set; }
    public bool HorizontalScrollIncluded { get; set; }
    public List<BrowserExtensionStitchTile> Tiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class BrowserExtensionStitchPackageExport
{
    public bool Requested { get; set; }
    public string Status { get; set; } = "not-requested";
    public string Source { get; set; } = string.Empty;
    public string DownloadRoot { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public List<long> DownloadIds { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public sealed class BrowserExtensionStitchTile
{
    public int Index { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ScrollX { get; set; }
    public int ScrollY { get; set; }
    public double DevicePixelRatio { get; set; } = 1d;
    public string CaptureState { get; set; } = "planned";
    public string ArtifactName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class BrowserExtensionConsent
{
    public bool ScreenshotConsented { get; set; }
    public bool TelemetryConsented { get; set; }
    public string ConsentText { get; set; } = string.Empty;
    public DateTimeOffset? ConsentedAt { get; set; }
}

public sealed class BrowserExtensionConsoleEvent
{
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}

public sealed class BrowserExtensionNetworkEvent
{
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string Initiator { get; set; } = string.Empty;
    public string ErrorText { get; set; } = string.Empty;
}

public sealed class BrowserExtensionContractValidationResult
{
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string PrivacyNote { get; set; } = "Browser extension payloads can contain page URLs, titles, console messages, and network metadata. Redact before persisting or sharing.";
    public bool IsValid => Issues.Count == 0;
}

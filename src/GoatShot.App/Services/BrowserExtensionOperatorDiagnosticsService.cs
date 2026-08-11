namespace GoatShot.App.Services;

public sealed class BrowserExtensionOperatorDiagnosticsService
{
    private readonly AppPaths _paths;

    public BrowserExtensionOperatorDiagnosticsService(AppPaths paths)
    {
        _paths = paths;
    }

    public BrowserExtensionOperatorDiagnostics Build(BrowserExtensionOperatorDiagnosticsRequest request)
    {
        var sourceDirectory = ResolveExtensionSourceDirectory(request.ExtensionSourceDirectory);
        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        var serviceWorkerPath = Path.Combine(sourceDirectory, "service-worker.js");
        var popupPath = Path.Combine(sourceDirectory, "popup.html");
        var optionsPath = Path.Combine(sourceDirectory, "options.html");
        var safeFixturePath = Path.Combine(sourceDirectory, "samples", "safe-fixture.html");
        var status = request.NativeHostStatus ?? new BrowserNativeHostStatus
        {
            HostName = BrowserNativeHostRegistrationService.HostName
        };

        var diagnostics = new BrowserExtensionOperatorDiagnostics
        {
            BridgeRoot = _paths.BrowserBridgeRoot,
            ExtensionSourceDirectory = sourceDirectory,
            ExtensionSourceExists = Directory.Exists(sourceDirectory),
            ManifestPath = manifestPath,
            ManifestExists = File.Exists(manifestPath),
            ServiceWorkerPath = serviceWorkerPath,
            ServiceWorkerExists = File.Exists(serviceWorkerPath),
            PopupPath = popupPath,
            PopupExists = File.Exists(popupPath),
            OptionsPath = optionsPath,
            OptionsExists = File.Exists(optionsPath),
            SafeFixturePath = safeFixturePath,
            SafeFixtureExists = File.Exists(safeFixturePath),
            NativeHostStatus = status,
            BrowserProofRequired = true
        };

        AddExtensionSourceEntries(diagnostics);
        AddNativeHostEntries(diagnostics, status);
        AddBridgeEntries(diagnostics);
        AddNextActions(diagnostics);

        return diagnostics;
    }

    private static void AddExtensionSourceEntries(BrowserExtensionOperatorDiagnostics diagnostics)
    {
        if (!diagnostics.ExtensionSourceExists)
        {
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Code = "extension-source-missing",
                Status = "blocked",
                Message = "The browser-extension source folder was not found.",
                RecoveryAction = "Run from a source checkout or use the portable package that includes the browser-extension folder."
            });
            diagnostics.Issues.Add("Browser extension source folder is missing.");
            return;
        }

        if (!diagnostics.ManifestExists || !diagnostics.ServiceWorkerExists)
        {
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Code = "extension-package-incomplete",
                Status = "blocked",
                Message = "The extension source folder is missing required manifest or service-worker files.",
                RecoveryAction = "Run `receipts browser-extension package --source <folder>` to validate the local extension package."
            });
            diagnostics.Issues.Add("Browser extension manifest or service worker is missing.");
        }
        else
        {
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Code = "extension-source-ready",
                Status = "ready",
                Message = "The local browser-extension source folder has the required manifest and service worker.",
                RecoveryAction = "Load this folder as an unpacked extension in Chrome or Edge for live fixture proof."
            });
        }

        diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
        {
            Code = diagnostics.SafeFixtureExists ? "safe-fixture-ready" : "safe-fixture-missing",
            Status = diagnostics.SafeFixtureExists ? "ready" : "warning",
            Message = diagnostics.SafeFixtureExists
                ? "The safe browser fixture is available for live proof."
                : "The safe browser fixture was not found.",
            RecoveryAction = diagnostics.SafeFixtureExists
                ? $"Open `{diagnostics.SafeFixturePath}` in a browser before running live extension proof."
                : "Restore browser-extension/samples/safe-fixture.html before live browser proof."
        });

        diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
        {
            Code = "extension-installed-not-detectable-from-desktop",
            Status = "manual",
            Message = "Desktop diagnostics cannot prove that Chrome, Edge, or Firefox has loaded the unpacked extension.",
            RecoveryAction = "Capture a browser screenshot of the loaded extension details page or popup Host Status result."
        });
        diagnostics.Warnings.Add("Browser extension installation is not detectable from the desktop app without browser-side proof.");
    }

    private static void AddNativeHostEntries(
        BrowserExtensionOperatorDiagnostics diagnostics,
        BrowserNativeHostStatus status)
    {
        var registrations = status.Registrations.Count == 0
            ? new List<BrowserNativeHostRegistrationState>()
            : status.Registrations;

        if (registrations.Count == 0)
        {
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Code = "native-host-status-unavailable",
                Status = "warning",
                Message = "Native host registration state was not supplied.",
                RecoveryAction = "Run `receipts browser-extension native-host status --json`."
            });
            diagnostics.Warnings.Add("Native host registration state was not supplied.");
            return;
        }

        foreach (var registration in registrations)
        {
            var configuredPath = !string.IsNullOrWhiteSpace(registration.ManifestPath);
            var missingManifest = configuredPath &&
                !registration.Installed &&
                registration.Message.Contains("missing", StringComparison.OrdinalIgnoreCase);
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Browser = registration.Browser.ToString(),
                Code = registration.Installed
                    ? "native-host-registered-browser-proof-needed"
                    : missingManifest
                        ? "native-host-manifest-missing"
                        : "native-host-missing",
                Status = registration.Installed ? "manual" : "blocked",
                Message = registration.Message,
                RecoveryAction = registration.Installed
                    ? "Use the extension popup Host Status button to prove the browser can reach the native host."
                    : missingManifest
                        ? "Reinstall the native host manifest for this browser."
                        : "Install the native host after loading the extension and copying its extension id."
            });
        }

        if (registrations.Any(entry => entry.Installed))
        {
            diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
            {
                Code = "native-host-unreachable-browser-proof-needed",
                Status = "manual",
                Message = "The native host has at least one desktop registration, but only the browser can prove GOATSHOT_PING reachability.",
                RecoveryAction = "Click Host Status in the extension popup/options page and save a screenshot of the diagnostic code."
            });
            diagnostics.Warnings.Add("Native-host reachability still requires browser-side Host Status proof.");
        }
        else
        {
            diagnostics.Issues.Add("No browser native-host registrations are currently installed.");
        }
    }

    private static void AddBridgeEntries(BrowserExtensionOperatorDiagnostics diagnostics)
    {
        diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
        {
            Code = "payload-rejected-diagnostics-available",
            Status = "ready",
            Message = "`browser-extension validate` and `browser-extension receive` report contract issues when payloads are rejected.",
            RecoveryAction = "Save rejected payload JSON plus redacted validation output for fixture proof."
        });
        diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
        {
            Code = "stitch-package-import-diagnostics-available",
            Status = "ready",
            Message = "`browser-extension receive --stitch-package` reports package manifest, tile, size, and import validation failures.",
            RecoveryAction = "Use the safe fixture package export before importing private browsing captures."
        });
        diagnostics.Entries.Add(new BrowserExtensionDiagnosticEntry
        {
            Code = "browser-download-package-boundary",
            Status = "manual",
            Message = "Browser downloads expose only a relative package folder hint to the extension; the operator still chooses the local package folder for native import.",
            RecoveryAction = "Record the downloaded `Receipts/<correlationId>/` folder path in live fixture notes (legacy exports may still use `GoatShot/`)."
        });
    }

    private static void AddNextActions(BrowserExtensionOperatorDiagnostics diagnostics)
    {
        if (!diagnostics.ExtensionSourceExists || !diagnostics.ManifestExists || !diagnostics.ServiceWorkerExists)
        {
            diagnostics.NextActions.Add("Restore or package the browser-extension source before live proof.");
        }

        if (diagnostics.NativeHostStatus.Registrations.Count == 0 ||
            diagnostics.NativeHostStatus.Registrations.All(entry => !entry.Installed))
        {
            diagnostics.NextActions.Add("Load the unpacked extension, copy its extension id, then install the native host manifest for the target browser.");
        }

        diagnostics.NextActions.Add("Open browser-extension/samples/safe-fixture.html in the target browser.");
        diagnostics.NextActions.Add("Use the extension popup Host Status button and save the diagnostic result.");
        diagnostics.NextActions.Add("Run a consented fixture capture and import the downloaded stitch package with `receipts browser-extension receive --stitch-package`.");
    }

    private static string ResolveExtensionSourceDirectory(string? sourceDirectory)
    {
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourceDirectory));
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
    }
}

public sealed class BrowserExtensionOperatorDiagnosticsRequest
{
    public string? ExtensionSourceDirectory { get; set; }
    public BrowserNativeHostStatus? NativeHostStatus { get; set; }
}

public sealed class BrowserExtensionOperatorDiagnostics
{
    public string BridgeRoot { get; set; } = string.Empty;
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public bool ExtensionSourceExists { get; set; }
    public string ManifestPath { get; set; } = string.Empty;
    public bool ManifestExists { get; set; }
    public string ServiceWorkerPath { get; set; } = string.Empty;
    public bool ServiceWorkerExists { get; set; }
    public string PopupPath { get; set; } = string.Empty;
    public bool PopupExists { get; set; }
    public string OptionsPath { get; set; } = string.Empty;
    public bool OptionsExists { get; set; }
    public string SafeFixturePath { get; set; } = string.Empty;
    public bool SafeFixtureExists { get; set; }
    public bool BrowserProofRequired { get; set; }
    public BrowserNativeHostStatus NativeHostStatus { get; set; } = new();
    public List<BrowserExtensionDiagnosticEntry> Entries { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
}

public sealed class BrowserExtensionDiagnosticEntry
{
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RecoveryAction { get; set; } = string.Empty;
}

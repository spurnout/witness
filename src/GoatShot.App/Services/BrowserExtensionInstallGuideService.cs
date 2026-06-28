using System.Text;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionInstallGuideService
{
    public BrowserExtensionInstallGuideResult CreateGuide(BrowserExtensionInstallGuideRequest request)
    {
        var browsers = NormalizeBrowsers(request.Browser);
        var status = request.NativeHostStatus ?? new BrowserNativeHostStatus();
        var extensionPath = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? "browser-extension"
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Browser Extension Install Guide");
        builder.AppendLine();
        builder.AppendLine($"Extension source: `{extensionPath}`");
        builder.AppendLine($"Native host: `{BrowserNativeHostRegistrationService.HostName}`");
        builder.AppendLine();
        builder.AppendLine("GoatShot can generate local extension ZIPs and user-scope native messaging host manifests. Browser extension installation itself is manual or browser-store managed.");
        builder.AppendLine();

        var entries = new List<BrowserExtensionInstallGuideEntry>();
        foreach (var browser in browsers)
        {
            var state = status.Registrations.FirstOrDefault(entry => entry.Browser == browser);
            var extensionId = ExtensionIdFor(browser, request);
            var entry = new BrowserExtensionInstallGuideEntry
            {
                Browser = browser.ToString(),
                NativeHostInstalled = state?.Installed == true,
                NativeHostManifestPath = state?.ManifestPath ?? string.Empty,
                ExtensionId = extensionId ?? string.Empty,
                ExtensionInstalledState = "not-detectable-from-desktop"
            };
            entry.NextActions.AddRange(NextActions(browser, extensionPath, extensionId, entry.NativeHostInstalled));
            entries.Add(entry);

            builder.AppendLine($"## {browser}");
            builder.AppendLine();
            builder.AppendLine($"Native host installed: `{entry.NativeHostInstalled}`");
            if (!string.IsNullOrWhiteSpace(entry.NativeHostManifestPath))
            {
                builder.AppendLine($"Native host manifest: `{entry.NativeHostManifestPath}`");
            }

            builder.AppendLine($"Extension id: `{(string.IsNullOrWhiteSpace(entry.ExtensionId) ? "unknown until loaded" : entry.ExtensionId)}`");
            builder.AppendLine();
            foreach (var action in entry.NextActions)
            {
                builder.AppendLine($"- {action}");
            }

            builder.AppendLine();
        }

        return new BrowserExtensionInstallGuideResult
        {
            Markdown = builder.ToString().TrimEnd() + Environment.NewLine,
            ExtensionSourceDirectory = extensionPath,
            Entries = entries
        };
    }

    private static IEnumerable<string> NextActions(
        BrowserNativeHostBrowser browser,
        string extensionPath,
        string? extensionId,
        bool nativeHostInstalled)
    {
        var browserPage = browser switch
        {
            BrowserNativeHostBrowser.Chrome => "chrome://extensions",
            BrowserNativeHostBrowser.Edge => "edge://extensions",
            BrowserNativeHostBrowser.Firefox => "about:debugging#/runtime/this-firefox",
            _ => "the browser extensions page"
        };
        yield return $"Open `{browserPage}` and load the unpacked extension from `{extensionPath}`.";
        yield return "Use the extension details page to copy the generated extension id.";
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            yield return "Run `goatshot browser-extension native-host install` again with the copied extension id so the browser can reach `com.goatshot.bridge`.";
        }
        else if (!nativeHostInstalled)
        {
            yield return $"Run `goatshot browser-extension native-host install --browser {browser.ToString().ToLowerInvariant()} --chrome-extension-id {extensionId}` for Chromium/Edge, or the Firefox id option for Firefox.";
        }
        else
        {
            yield return "Use the extension popup Host Status button to confirm the native host responds.";
        }

        yield return "Browser-store publication, automatic installation, and live store review are separate later lanes.";
    }

    private static List<BrowserNativeHostBrowser> NormalizeBrowsers(string? browser)
    {
        if (string.IsNullOrWhiteSpace(browser) || browser.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new List<BrowserNativeHostBrowser>
            {
                BrowserNativeHostBrowser.Chrome,
                BrowserNativeHostBrowser.Edge,
                BrowserNativeHostBrowser.Firefox
            };
        }

        return browser.Trim().ToLowerInvariant() switch
        {
            "chrome" or "chromium" => new List<BrowserNativeHostBrowser> { BrowserNativeHostBrowser.Chrome },
            "edge" or "msedge" => new List<BrowserNativeHostBrowser> { BrowserNativeHostBrowser.Edge },
            "firefox" => new List<BrowserNativeHostBrowser> { BrowserNativeHostBrowser.Firefox },
            _ => new List<BrowserNativeHostBrowser>()
        };
    }

    private static string? ExtensionIdFor(
        BrowserNativeHostBrowser browser,
        BrowserExtensionInstallGuideRequest request)
    {
        return browser switch
        {
            BrowserNativeHostBrowser.Chrome => request.ChromeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Edge => request.EdgeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Firefox => request.FirefoxExtensionId ?? request.ExtensionId,
            _ => request.ExtensionId
        };
    }
}

public sealed class BrowserExtensionInstallGuideRequest
{
    public string? Browser { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? ExtensionId { get; set; }
    public string? ChromeExtensionId { get; set; }
    public string? EdgeExtensionId { get; set; }
    public string? FirefoxExtensionId { get; set; }
    public BrowserNativeHostStatus? NativeHostStatus { get; set; }
}

public sealed class BrowserExtensionInstallGuideResult
{
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public string Markdown { get; set; } = string.Empty;
    public List<BrowserExtensionInstallGuideEntry> Entries { get; set; } = new();
}

public sealed class BrowserExtensionInstallGuideEntry
{
    public string Browser { get; set; } = string.Empty;
    public bool NativeHostInstalled { get; set; }
    public string NativeHostManifestPath { get; set; } = string.Empty;
    public string ExtensionId { get; set; } = string.Empty;
    public string ExtensionInstalledState { get; set; } = string.Empty;
    public List<string> NextActions { get; set; } = new();
}

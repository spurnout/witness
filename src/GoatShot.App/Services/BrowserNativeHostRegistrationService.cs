using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace GoatShot.App.Services;

public sealed class BrowserNativeHostRegistrationService
{
    public const string HostName = BrandIdentity.NativeMessagingHostName;
    public const string LegacyHostName = BrandIdentity.LegacyNativeMessagingHostName;
    private const string ChromeRegistrySubKey = @"Software\Google\Chrome\NativeMessagingHosts\" + HostName;
    private const string EdgeRegistrySubKey = @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName;
    private const string LegacyChromeRegistrySubKey = @"Software\Google\Chrome\NativeMessagingHosts\" + LegacyHostName;
    private const string LegacyEdgeRegistrySubKey = @"Software\Microsoft\Edge\NativeMessagingHosts\" + LegacyHostName;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly IBrowserNativeHostRegistry _registry;
    private readonly string _firefoxManifestRoot;

    public BrowserNativeHostRegistrationService(
        AppPaths paths,
        IBrowserNativeHostRegistry? registry = null,
        string? firefoxManifestRoot = null)
    {
        _paths = paths;
        _registry = registry ?? new CurrentUserBrowserNativeHostRegistry();
        _firefoxManifestRoot = string.IsNullOrWhiteSpace(firefoxManifestRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla",
                "NativeMessagingHosts")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(firefoxManifestRoot));
    }

    public string GetStatusSummary()
    {
        var status = GetStatus();
        var installed = status.Registrations.Count(entry => entry.Installed);
        return $"Browser native messaging host {HostName}: {installed}/{status.Registrations.Count} primary browser registration(s) installed. Install and uninstall operations manage {LegacyHostName} as a compatibility alias; the count reports the primary name only. Manifests root: {status.ManifestRoot}. Registration uses HKCU for Chromium/Edge and the user Firefox NativeMessagingHosts folder; local extension ZIP packaging is available through CLI, while browser-store publication and automatic extension installation remain separate proof lanes.";
    }

    public BrowserNativeHostStatus GetStatus()
    {
        return new BrowserNativeHostStatus
        {
            HostName = HostName,
            ManifestRoot = ManifestRoot,
            Registrations = new List<BrowserNativeHostRegistrationState>
            {
                ChromiumState(BrowserNativeHostBrowser.Chrome, ChromeRegistrySubKey),
                ChromiumState(BrowserNativeHostBrowser.Edge, EdgeRegistrySubKey),
                FirefoxState()
            }
        };
    }

    public async Task<IReadOnlyList<BrowserNativeHostInstallResult>> WriteManifestsAsync(
        BrowserNativeHostInstallRequest request,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ValidateHostExecutable(request.HostExecutablePath);
        var outputRoot = string.IsNullOrWhiteSpace(outputDirectory)
            ? ManifestRoot
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        var results = new List<BrowserNativeHostInstallResult>();
        foreach (var browser in NormalizeBrowsers(request.Browsers))
        {
            var manifest = CreateManifest(browser, request, HostName);
            var path = Path.Combine(outputRoot, browser.ToString().ToLowerInvariant(), $"{HostName}.json");
            await WriteManifestAsync(path, manifest, cancellationToken);
            var legacyPath = Path.Combine(outputRoot, browser.ToString().ToLowerInvariant(), $"{LegacyHostName}.json");
            await WriteManifestAsync(legacyPath, CreateManifest(browser, request, LegacyHostName), cancellationToken);
            results.Add(new BrowserNativeHostInstallResult
            {
                Browser = browser,
                Succeeded = true,
                ManifestPath = path,
                Message = $"Receipts native messaging manifest and GoatShot compatibility alias written for {browser}: {path}"
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<BrowserNativeHostInstallResult>> InstallAsync(
        BrowserNativeHostInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateHostExecutable(request.HostExecutablePath);
        var results = new List<BrowserNativeHostInstallResult>();
        foreach (var browser in NormalizeBrowsers(request.Browsers))
        {
            var manifest = CreateManifest(browser, request, HostName);
            var manifestPath = browser == BrowserNativeHostBrowser.Firefox
                ? Path.Combine(_firefoxManifestRoot, $"{HostName}.json")
                : Path.Combine(ManifestRoot, browser.ToString().ToLowerInvariant(), $"{HostName}.json");
            await WriteManifestAsync(manifestPath, manifest, cancellationToken);
            var legacyManifestPath = browser == BrowserNativeHostBrowser.Firefox
                ? Path.Combine(_firefoxManifestRoot, $"{LegacyHostName}.json")
                : Path.Combine(ManifestRoot, browser.ToString().ToLowerInvariant(), $"{LegacyHostName}.json");
            await WriteManifestAsync(
                legacyManifestPath,
                CreateManifest(browser, request, LegacyHostName),
                cancellationToken);

            if (browser == BrowserNativeHostBrowser.Chrome)
            {
                _registry.SetDefaultValue(ChromeRegistrySubKey, manifestPath);
                _registry.SetDefaultValue(LegacyChromeRegistrySubKey, legacyManifestPath);
            }
            else if (browser == BrowserNativeHostBrowser.Edge)
            {
                _registry.SetDefaultValue(EdgeRegistrySubKey, manifestPath);
                _registry.SetDefaultValue(LegacyEdgeRegistrySubKey, legacyManifestPath);
            }

            results.Add(new BrowserNativeHostInstallResult
            {
                Browser = browser,
                Succeeded = true,
                ManifestPath = manifestPath,
                RegistrySubKey = browser switch
                {
                    BrowserNativeHostBrowser.Chrome => ChromeRegistrySubKey,
                    BrowserNativeHostBrowser.Edge => EdgeRegistrySubKey,
                    _ => string.Empty
                },
                Message = browser == BrowserNativeHostBrowser.Firefox
                    ? $"Firefox Receipts native messaging manifest and GoatShot compatibility alias installed: {manifestPath}"
                    : $"{browser} Receipts native messaging host and GoatShot compatibility alias registered in HKCU: {manifestPath}"
            });
        }

        return results;
    }

    public IReadOnlyList<BrowserNativeHostInstallResult> Uninstall(IReadOnlyList<BrowserNativeHostBrowser>? browsers = null)
    {
        var results = new List<BrowserNativeHostInstallResult>();
        foreach (var browser in NormalizeBrowsers(browsers))
        {
            if (browser == BrowserNativeHostBrowser.Chrome)
            {
                _registry.DeleteSubKeyTree(ChromeRegistrySubKey);
                _registry.DeleteSubKeyTree(LegacyChromeRegistrySubKey);
                results.Add(Removed(browser, ChromeRegistrySubKey, "Chrome native messaging host registration removed from HKCU."));
            }
            else if (browser == BrowserNativeHostBrowser.Edge)
            {
                _registry.DeleteSubKeyTree(EdgeRegistrySubKey);
                _registry.DeleteSubKeyTree(LegacyEdgeRegistrySubKey);
                results.Add(Removed(browser, EdgeRegistrySubKey, "Edge native messaging host registration removed from HKCU."));
            }
            else
            {
                var path = Path.Combine(_firefoxManifestRoot, $"{HostName}.json");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                var legacyPath = Path.Combine(_firefoxManifestRoot, $"{LegacyHostName}.json");
                if (File.Exists(legacyPath))
                {
                    File.Delete(legacyPath);
                }

                results.Add(new BrowserNativeHostInstallResult
                {
                    Browser = browser,
                    Succeeded = true,
                    ManifestPath = path,
                    Message = "Firefox native messaging manifest removed from the user profile folder."
                });
            }
        }

        return results;
    }

    private BrowserNativeHostRegistrationState ChromiumState(BrowserNativeHostBrowser browser, string subKey)
    {
        var manifestPath = _registry.GetDefaultValue(subKey) ?? string.Empty;
        return new BrowserNativeHostRegistrationState
        {
            Browser = browser,
            Installed = !string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath),
            ManifestPath = manifestPath,
            RegistrySubKey = subKey,
            Message = string.IsNullOrWhiteSpace(manifestPath)
                ? $"{browser} native messaging host is not registered in HKCU."
                : File.Exists(manifestPath)
                    ? $"{browser} native messaging host is registered."
                    : $"{browser} registry points to a missing manifest."
        };
    }

    private BrowserNativeHostRegistrationState FirefoxState()
    {
        var manifestPath = Path.Combine(_firefoxManifestRoot, $"{HostName}.json");
        return new BrowserNativeHostRegistrationState
        {
            Browser = BrowserNativeHostBrowser.Firefox,
            Installed = File.Exists(manifestPath),
            ManifestPath = manifestPath,
            Message = File.Exists(manifestPath)
                ? "Firefox native messaging manifest is installed in the user profile folder."
                : "Firefox native messaging manifest is not installed in the user profile folder."
        };
    }

    private string ManifestRoot => Path.Combine(_paths.BrowserBridgeRoot, "native-host");

    private static BrowserNativeHostInstallResult Removed(
        BrowserNativeHostBrowser browser,
        string registrySubKey,
        string message)
    {
        return new BrowserNativeHostInstallResult
        {
            Browser = browser,
            Succeeded = true,
            RegistrySubKey = registrySubKey,
            Message = message
        };
    }

    private static async Task WriteManifestAsync(
        string path,
        BrowserNativeHostManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
    }

    private static BrowserNativeHostManifest CreateManifest(
        BrowserNativeHostBrowser browser,
        BrowserNativeHostInstallRequest request,
        string hostName)
    {
        var manifest = new BrowserNativeHostManifest
        {
            Name = hostName,
            Description = hostName == HostName
                ? "Receipts browser capture native messaging host."
                : "Receipts browser capture native messaging host (GoatShot compatibility alias).",
            Path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.HostExecutablePath)),
            Type = "stdio"
        };

        if (browser == BrowserNativeHostBrowser.Firefox)
        {
            manifest.AllowedExtensions = new List<string>
            {
                NormalizeFirefoxExtensionId(request.FirefoxExtensionId)
            };
        }
        else
        {
            var id = browser == BrowserNativeHostBrowser.Edge
                ? request.EdgeExtensionId ?? request.ChromeExtensionId
                : request.ChromeExtensionId;
            manifest.AllowedOrigins = new List<string>
            {
                $"chrome-extension://{NormalizeChromiumExtensionId(id, browser)}/"
            };
        }

        return manifest;
    }

    private static void ValidateHostExecutable(string hostExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(hostExecutablePath))
        {
            throw new ArgumentException("Native host executable path is required.");
        }

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(hostExecutablePath));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Native host executable was not found.", path);
        }
    }

    private static string NormalizeChromiumExtensionId(string? extensionId, BrowserNativeHostBrowser browser)
    {
        extensionId = (extensionId ?? string.Empty).Trim().ToLowerInvariant();
        if (extensionId.Length == 32 && extensionId.All(ch => ch is >= 'a' and <= 'p'))
        {
            return extensionId;
        }

        throw new ArgumentException($"{browser} native messaging registration requires a 32-character extension id using Chrome extension id characters a-p.");
    }

    private static string NormalizeFirefoxExtensionId(string? extensionId)
    {
        extensionId = string.IsNullOrWhiteSpace(extensionId)
            ? "receipts-page-capture@receipts.local"
            : extensionId.Trim();
        if (extensionId.Length > 0 && !extensionId.Any(char.IsWhiteSpace))
        {
            return extensionId;
        }

        throw new ArgumentException("Firefox native messaging registration requires an extension id without whitespace.");
    }

    public static IReadOnlyList<BrowserNativeHostBrowser> NormalizeBrowsers(IReadOnlyList<BrowserNativeHostBrowser>? browsers)
    {
        if (browsers is null || browsers.Count == 0)
        {
            return new[]
            {
                BrowserNativeHostBrowser.Chrome,
                BrowserNativeHostBrowser.Edge,
                BrowserNativeHostBrowser.Firefox
            };
        }

        return browsers.Distinct().ToList();
    }
}

public sealed class BrowserNativeHostManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "stdio";

    [JsonPropertyName("allowed_origins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedOrigins { get; set; }

    [JsonPropertyName("allowed_extensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedExtensions { get; set; }
}

public sealed class BrowserNativeHostInstallRequest
{
    public IReadOnlyList<BrowserNativeHostBrowser>? Browsers { get; set; }
    public string HostExecutablePath { get; set; } = string.Empty;
    public string? ChromeExtensionId { get; set; }
    public string? EdgeExtensionId { get; set; }
    public string? FirefoxExtensionId { get; set; }
}

public sealed class BrowserNativeHostInstallResult
{
    public BrowserNativeHostBrowser Browser { get; set; }
    public bool Succeeded { get; set; }
    public string ManifestPath { get; set; } = string.Empty;
    public string RegistrySubKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class BrowserNativeHostStatus
{
    public string HostName { get; set; } = string.Empty;
    public string ManifestRoot { get; set; } = string.Empty;
    public List<BrowserNativeHostRegistrationState> Registrations { get; set; } = new();
}

public sealed class BrowserNativeHostRegistrationState
{
    public BrowserNativeHostBrowser Browser { get; set; }
    public bool Installed { get; set; }
    public string ManifestPath { get; set; } = string.Empty;
    public string RegistrySubKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public enum BrowserNativeHostBrowser
{
    Chrome,
    Edge,
    Firefox
}

public interface IBrowserNativeHostRegistry
{
    string? GetDefaultValue(string subKey);
    void SetDefaultValue(string subKey, string value);
    void DeleteSubKeyTree(string subKey);
}

public sealed class CurrentUserBrowserNativeHostRegistry : IBrowserNativeHostRegistry
{
    public string? GetDefaultValue(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValue(null) as string;
    }

    public void SetDefaultValue(string subKey, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey);
        key.SetValue(null, value, RegistryValueKind.String);
    }

    public void DeleteSubKeyTree(string subKey)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }
}

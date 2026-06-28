using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionInstallPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionInstallPlanResult> CreateAsync(
        BrowserExtensionInstallPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-install-plan"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        var storePackageRoot = string.IsNullOrWhiteSpace(request.StorePackageRoot)
            ? string.Empty
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.StorePackageRoot));
        var status = request.NativeHostStatus ?? new BrowserNativeHostStatus();

        var result = new BrowserExtensionInstallPlanResult
        {
            SourceDirectory = source,
            OutputPath = outputRoot,
            StorePackageRoot = storePackageRoot,
            PolicyAllowed = request.PolicyAllowed,
            PolicyReason = SensitiveTextDetector.Redact(request.PolicyReason ?? string.Empty)
        };

        var browsers = NormalizeBrowsers(request.Browser);
        if (browsers.Count == 0)
        {
            result.Issues.Add("Install-plan browser must be one of: all, chrome, edge, firefox.");
        }

        foreach (var browser in browsers)
        {
            result.Entries.Add(BuildEntry(browser, source, storePackageRoot, status, request, result.PolicyAllowed, result.PolicyReason));
        }

        result.NonGoals.AddRange(BuildNonGoals());
        result.AuthorityBoundaries.AddRange(BuildAuthorityBoundaries());
        result.Succeeded = result.Issues.Count == 0 && result.Entries.Count > 0 && result.Entries.All(entry => entry.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"Browser extension install plan created: {outputRoot}"
            : $"Browser extension install plan created with blockers: {outputRoot}";

        Directory.CreateDirectory(outputRoot);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "install-plan.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "install-plan.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static BrowserExtensionInstallPlanEntry BuildEntry(
        BrowserNativeHostBrowser browser,
        string source,
        string storePackageRoot,
        BrowserNativeHostStatus status,
        BrowserExtensionInstallPlanRequest request,
        bool policyAllowed,
        string policyReason)
    {
        var browserName = browser.ToString().ToLowerInvariant();
        var extensionId = ExtensionIdFor(browser, request) ?? string.Empty;
        var nativeHostState = status.Registrations.FirstOrDefault(entry => entry.Browser == browser);
        var sourceExists = Directory.Exists(source) && File.Exists(Path.Combine(source, "manifest.json"));
        var storePackage = FindStorePackage(storePackageRoot, browserName);

        var entry = new BrowserExtensionInstallPlanEntry
        {
            Browser = browserName,
            ExtensionSourceExists = sourceExists,
            ExtensionSourceDirectory = source,
            StorePackagePath = storePackage.ExtensionPackagePath,
            StoreSubmissionBundlePath = storePackage.SubmissionBundlePath,
            StorePackageAvailable = !string.IsNullOrWhiteSpace(storePackage.ExtensionPackagePath),
            ExtensionId = extensionId,
            NativeHostInstalled = nativeHostState?.Installed == true,
            NativeHostManifestPath = nativeHostState?.ManifestPath ?? string.Empty,
            NativeHostMessage = nativeHostState?.Message ?? "Native-host status unavailable.",
            DesktopCanDetectInstalledExtension = false,
            AutomaticInstallStatus = "not-supported-read-only-plan"
        };

        entry.Issues.AddRange(storePackage.Issues);
        entry.Warnings.Add("GoatShot cannot prove the extension is installed from desktop state alone; browser-side Host Status evidence is still required.");
        entry.Warnings.Add($"{browser} extension installation is controlled by the browser, browser store, or enterprise policy, not by GoatShot.");

        if (!policyAllowed)
        {
            entry.Issues.Add(string.IsNullOrWhiteSpace(policyReason)
                ? "Browser extension handoff is disabled by managed policy."
                : policyReason);
            entry.Status = "blocked-by-managed-policy";
        }
        else if (!sourceExists && !entry.StorePackageAvailable)
        {
            entry.Issues.Add("Extension source or a generated store-package root is required before installation can be planned.");
            entry.Status = "blocked-missing-extension-source";
        }
        else
        {
            entry.Status = "manual-install-plan-ready";
        }

        entry.Commands.Add($"goatshot browser-extension install-guide --browser {browserName} --source {Quote(source)}");
        entry.Commands.Add("goatshot browser-extension native-host status --json");
        if (!string.IsNullOrWhiteSpace(extensionId))
        {
            entry.Commands.Add(NativeHostInstallCommand(browser, extensionId));
        }
        else
        {
            entry.Commands.Add("Load the extension manually first, then rerun this plan with the browser-generated extension id.");
        }

        entry.ManualSteps.AddRange(BuildManualSteps(browser, source, storePackage, extensionId));
        entry.RequiredEvidence.AddRange(new[]
        {
            "Browser extension details screenshot showing extension id.",
            "Popup/options screenshots showing consent defaults.",
            "Popup Host Status success screenshot from the same browser.",
            "Safe-fixture capture or stitch-package export proof if live capture is being validated."
        });

        return entry;
    }

    private static IReadOnlyList<string> BuildManualSteps(
        BrowserNativeHostBrowser browser,
        string source,
        BrowserExtensionInstallPlanPackage storePackage,
        string extensionId)
    {
        var steps = new List<string>();
        var page = browser switch
        {
            BrowserNativeHostBrowser.Chrome => "chrome://extensions",
            BrowserNativeHostBrowser.Edge => "edge://extensions",
            BrowserNativeHostBrowser.Firefox => "about:debugging#/runtime/this-firefox",
            _ => "the browser extensions page"
        };
        steps.Add($"Open `{page}` in the target browser.");
        if (!string.IsNullOrWhiteSpace(storePackage.ExtensionPackagePath))
        {
            steps.Add($"Review the generated local package `{storePackage.ExtensionPackagePath}` before use.");
        }

        steps.Add($"Load the unpacked extension from `{source}` or use the store-managed submission flow after review/signing.");
        steps.Add("Copy the browser-generated extension id from the browser details page.");
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            steps.Add("Rerun the native-host install command after the extension id is known.");
        }
        else
        {
            steps.Add("Install or refresh the native-host manifest with the supplied extension id.");
        }

        steps.Add("Use the extension popup Host Status button to prove browser-to-native reachability.");
        steps.Add("Keep store account submission/review/signing screenshots separate from local installation proof.");
        return steps;
    }

    private static BrowserExtensionInstallPlanPackage FindStorePackage(string storePackageRoot, string browser)
    {
        var result = new BrowserExtensionInstallPlanPackage();
        if (string.IsNullOrWhiteSpace(storePackageRoot))
        {
            return result;
        }

        if (!Directory.Exists(storePackageRoot))
        {
            result.Issues.Add($"Store-package root was not found: {storePackageRoot}");
            return result;
        }

        var browserRoot = Path.Combine(storePackageRoot, browser);
        if (Directory.Exists(browserRoot))
        {
            result.ExtensionPackagePath = Directory.GetFiles(browserRoot, $"goatshot-browser-extension-{browser}-v*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? string.Empty;
        }

        result.SubmissionBundlePath = Directory.GetFiles(storePackageRoot, $"goatshot-browser-extension-{browser}-store-submission.zip")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(result.ExtensionPackagePath) && string.IsNullOrWhiteSpace(result.SubmissionBundlePath))
        {
            result.Issues.Add($"No {browser} store-package artifacts were found under {storePackageRoot}.");
        }

        return result;
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
        BrowserExtensionInstallPlanRequest request)
    {
        return browser switch
        {
            BrowserNativeHostBrowser.Chrome => request.ChromeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Edge => request.EdgeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Firefox => request.FirefoxExtensionId ?? request.ExtensionId,
            _ => request.ExtensionId
        };
    }

    private static string NativeHostInstallCommand(BrowserNativeHostBrowser browser, string extensionId)
    {
        return browser switch
        {
            BrowserNativeHostBrowser.Chrome =>
                $"goatshot browser-extension native-host install --browser chrome --chrome-extension-id {extensionId} --json",
            BrowserNativeHostBrowser.Edge =>
                $"goatshot browser-extension native-host install --browser edge --edge-extension-id {extensionId} --json",
            BrowserNativeHostBrowser.Firefox =>
                $"goatshot browser-extension native-host install --browser firefox --firefox-extension-id {extensionId} --json",
            _ => "goatshot browser-extension native-host install --json"
        };
    }

    private static string BuildMarkdown(BrowserExtensionInstallPlanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Browser Extension Install Plan");
        builder.AppendLine();
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Store package root: `{(string.IsNullOrWhiteSpace(result.StorePackageRoot) ? "not supplied" : result.StorePackageRoot)}`");
        builder.AppendLine($"Policy allowed: `{result.PolicyAllowed}`");
        if (!string.IsNullOrWhiteSpace(result.PolicyReason))
        {
            builder.AppendLine($"Policy reason: `{result.PolicyReason}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        foreach (var nonGoal in result.NonGoals)
        {
            builder.AppendLine($"- {nonGoal}");
        }

        builder.AppendLine();
        builder.AppendLine("## Browser Plans");
        builder.AppendLine();
        foreach (var entry in result.Entries)
        {
            builder.AppendLine($"### {entry.Browser}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{entry.Status}`");
            builder.AppendLine($"Automatic install status: `{entry.AutomaticInstallStatus}`");
            builder.AppendLine($"Desktop can detect installed extension: `{entry.DesktopCanDetectInstalledExtension}`");
            builder.AppendLine($"Native host installed: `{entry.NativeHostInstalled}`");
            builder.AppendLine($"Extension id: `{(string.IsNullOrWhiteSpace(entry.ExtensionId) ? "unknown" : entry.ExtensionId)}`");
            builder.AppendLine($"Store package: `{(string.IsNullOrWhiteSpace(entry.StorePackagePath) ? "not found" : entry.StorePackagePath)}`");
            AppendList(builder, "Issues", entry.Issues, 4);
            AppendList(builder, "Warnings", entry.Warnings, 4);
            AppendList(builder, "Commands", entry.Commands, 4);
            AppendList(builder, "Manual Steps", entry.ManualSteps, 4);
            AppendList(builder, "Required Evidence", entry.RequiredEvidence, 4);
        }

        AppendList(builder, "Authority Boundaries", result.AuthorityBoundaries);
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values, int headingLevel = 2)
    {
        builder.AppendLine($"{new string('#', headingLevel)} {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
            builder.AppendLine();
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }

        builder.AppendLine();
    }

    private static async Task<string> WriteFileAsync(
        string root,
        string fileName,
        string contents,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, fileName);
        await File.WriteAllTextAsync(path, contents, cancellationToken);
        return path;
    }

    private static IReadOnlyList<string> BuildNonGoals() => new[]
    {
        "This plan does not install browser extensions.",
        "This plan does not change browser profiles, extension stores, registry keys, or Firefox native-host folders.",
        "This plan does not prove browser-store publication, signing, review approval, availability, or automatic installation.",
        "This plan does not prove live browser Host Status or live fixture capture."
    };

    private static IReadOnlyList<string> BuildAuthorityBoundaries() => new[]
    {
        "Chrome and Edge unpacked extension loading is controlled by the browser UI or enterprise policy.",
        "Firefox temporary/permanent add-on installation is controlled by Firefox, AMO signing, or enterprise policy.",
        "GoatShot can generate local packages, setup notes, native-host manifests, and validation artifacts, but it cannot honestly claim extension installation from desktop state alone.",
        "Native-host registration still requires the browser-generated extension id before reachability can be proven."
    };

    private static string Quote(string value) => $"\"{value}\"";

    private sealed class BrowserExtensionInstallPlanPackage
    {
        public string ExtensionPackagePath { get; set; } = string.Empty;
        public string SubmissionBundlePath { get; set; } = string.Empty;
        public List<string> Issues { get; set; } = new();
    }
}

public sealed class BrowserExtensionInstallPlanRequest
{
    public string? Browser { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? StorePackageRoot { get; set; }
    public string? OutputPath { get; set; }
    public string? ExtensionId { get; set; }
    public string? ChromeExtensionId { get; set; }
    public string? EdgeExtensionId { get; set; }
    public string? FirefoxExtensionId { get; set; }
    public BrowserNativeHostStatus? NativeHostStatus { get; set; }
    public bool PolicyAllowed { get; set; } = true;
    public string? PolicyReason { get; set; }
}

public sealed class BrowserExtensionInstallPlanResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string StorePackageRoot { get; set; } = string.Empty;
    public bool PolicyAllowed { get; set; }
    public string PolicyReason { get; set; } = string.Empty;
    public List<BrowserExtensionInstallPlanEntry> Entries { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public List<string> AuthorityBoundaries { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionInstallPlanEntry
{
    public string Browser { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AutomaticInstallStatus { get; set; } = string.Empty;
    public bool DesktopCanDetectInstalledExtension { get; set; }
    public bool ExtensionSourceExists { get; set; }
    public string ExtensionSourceDirectory { get; set; } = string.Empty;
    public bool StorePackageAvailable { get; set; }
    public string StorePackagePath { get; set; } = string.Empty;
    public string StoreSubmissionBundlePath { get; set; } = string.Empty;
    public string ExtensionId { get; set; } = string.Empty;
    public bool NativeHostInstalled { get; set; }
    public string NativeHostManifestPath { get; set; } = string.Empty;
    public string NativeHostMessage { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<string> ManualSteps { get; set; } = new();
    public List<string> RequiredEvidence { get; set; } = new();
}

using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionStoreReadinessService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionStoreReadinessResult> CreateAsync(
        BrowserExtensionStoreReadinessRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-store-readiness"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var result = new BrowserExtensionStoreReadinessResult
        {
            SourceDirectory = source,
            OutputPath = outputRoot,
            SupportUrl = SensitiveTextDetector.Redact(request.SupportUrl ?? string.Empty),
            PrivacyUrl = SensitiveTextDetector.Redact(request.PrivacyUrl ?? string.Empty)
        };

        var packageIssues = new BrowserExtensionPackageService().ValidateSource(source);
        result.Issues.AddRange(packageIssues);

        var facts = ReadManifestFacts(source, result.Issues);
        result.Manifest = facts;
        result.PermissionRationales.AddRange(BuildPermissionRationales(facts));

        var targets = NormalizeTargets(request.Target);
        if (targets.Count == 0)
        {
            result.Issues.Add("Store-readiness target must be one of: all, local, chrome, edge, firefox.");
        }

        foreach (var target in targets)
        {
            result.Targets.Add(BuildTargetReadiness(target, facts, packageIssues, request));
        }

        result.NextActions.AddRange(new[]
        {
            "Run local package validation before creating any browser-store submission package.",
            "Capture browser-specific screenshots from a live browser session after loading the unpacked extension.",
            "Keep native-host install proof and popup Host Status proof separate from store listing proof.",
            "Submit to each browser store manually with that store's current review, signing, and metadata requirements."
        });

        Directory.CreateDirectory(outputRoot);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "store-readiness.md",
            BuildStoreReadinessMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "permission-rationale.md",
            BuildPermissionRationaleMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "privacy-data-use.md",
            BuildPrivacyDataUseMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "screenshots-checklist.md",
            BuildScreenshotsChecklistMarkdown(result),
            cancellationToken));
        result.Succeeded = result.Issues.Count == 0 &&
            result.Targets.All(target => target.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"Browser extension store-readiness checklist created: {outputRoot}"
            : $"Browser extension store-readiness checklist created with issues: {outputRoot}";
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "store-readiness.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static BrowserExtensionManifestFacts ReadManifestFacts(string sourceDirectory, List<string> issues)
    {
        var facts = new BrowserExtensionManifestFacts();
        var manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            issues.Add($"manifest.json was not found: {manifestPath}");
            return facts;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            facts.ManifestPath = manifestPath;
            facts.Name = StringValue(root, "name");
            facts.Version = StringValue(root, "version");
            facts.Description = StringValue(root, "description");
            facts.ManifestVersion = IntValue(root, "manifest_version");
            facts.Permissions.AddRange(StringArray(root, "permissions"));
            facts.HostPermissions.AddRange(StringArray(root, "host_permissions"));
            facts.ContentScriptMatches.AddRange(ContentScriptMatches(root));
            facts.ContentScriptFiles.AddRange(ContentScriptFiles(root));
            facts.BackgroundServiceWorker = root.TryGetProperty("background", out var background)
                ? StringValue(background, "service_worker")
                : string.Empty;
            facts.ActionDefaultPopup = root.TryGetProperty("action", out var action)
                ? StringValue(action, "default_popup")
                : string.Empty;
            facts.OptionsPage = root.TryGetProperty("options_ui", out var optionsUi)
                ? StringValue(optionsUi, "page")
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            issues.Add($"manifest.json could not be read for store readiness: {SensitiveTextDetector.Redact(ex.Message)}");
        }

        return facts;
    }

    private static BrowserExtensionStoreTargetReadiness BuildTargetReadiness(
        BrowserExtensionStoreTarget target,
        BrowserExtensionManifestFacts facts,
        IReadOnlyList<string> packageIssues,
        BrowserExtensionStoreReadinessRequest request)
    {
        var readiness = new BrowserExtensionStoreTargetReadiness
        {
            Target = target.ToString().ToLowerInvariant()
        };

        readiness.Checks.Add("Package source validation uses the same required files as local ZIP creation.");
        readiness.Checks.Add("Manifest permissions are mapped to operator-facing permission and privacy copy.");
        readiness.Checks.Add("Native-host dependency is documented separately from extension listing proof.");
        readiness.Checks.Add("Screenshots are treated as manual browser proof, not generated evidence.");

        if (packageIssues.Count > 0)
        {
            readiness.Issues.Add("Fix local extension package validation issues before treating this target as submission-ready.");
        }

        if (facts.ManifestVersion != 3)
        {
            readiness.Issues.Add("The current prototype expects manifest_version 3 for store-readiness planning.");
        }

        if (!facts.Permissions.Contains("nativeMessaging", StringComparer.Ordinal))
        {
            readiness.Issues.Add("nativeMessaging permission must be declared and explained for native app handoff.");
        }

        if (!facts.Permissions.Contains("activeTab", StringComparer.Ordinal))
        {
            readiness.Issues.Add("activeTab permission must be declared and explained for visible-tab capture.");
        }

        if (!facts.Permissions.Contains("downloads", StringComparer.Ordinal))
        {
            readiness.Issues.Add("downloads permission must be declared and explained for stitch package export.");
        }

        if (!facts.Permissions.Contains("storage", StringComparer.Ordinal))
        {
            readiness.Issues.Add("storage permission must be declared and explained for popup/options settings.");
        }

        if (string.IsNullOrWhiteSpace(request.SupportUrl))
        {
            readiness.Warnings.Add("Support URL is not supplied; store listing metadata still needs a reviewed support destination.");
        }

        if (string.IsNullOrWhiteSpace(request.PrivacyUrl))
        {
            readiness.Warnings.Add("Privacy URL is not supplied; store listing metadata still needs a reviewed privacy destination.");
        }

        switch (target)
        {
            case BrowserExtensionStoreTarget.Local:
                readiness.Status = readiness.Issues.Count == 0
                    ? "local-package-ready"
                    : "blocked";
                readiness.ManualSteps.Add("Run `goatshot browser-extension package --source browser-extension` and load the ZIP or unpacked folder manually.");
                readiness.ManualSteps.Add("Use the popup Host Status button in the browser to prove native-host reachability.");
                readiness.Warnings.Add("Local package readiness does not prove browser-store publication, signing, or review.");
                break;
            case BrowserExtensionStoreTarget.Chrome:
                readiness.Status = readiness.Issues.Count == 0
                    ? "store-checklist-ready-manual-review-required"
                    : "blocked";
                readiness.ManualSteps.Add("Create browser-store listing copy from the generated permission and privacy files.");
                readiness.ManualSteps.Add("Attach live Chrome screenshots from the safe fixture, popup, options page, and Host Status proof.");
                readiness.ManualSteps.Add("Submit through the Chrome store flow manually; this artifact is not publication proof.");
                readiness.Warnings.Add("Chrome store review, signing, availability, and native-host dependency approval remain manual proof lanes.");
                break;
            case BrowserExtensionStoreTarget.Edge:
                readiness.Status = readiness.Issues.Count == 0
                    ? "store-checklist-ready-manual-review-required"
                    : "blocked";
                readiness.ManualSteps.Add("Create browser-store listing copy from the generated permission and privacy files.");
                readiness.ManualSteps.Add("Attach live Edge screenshots from the safe fixture, popup, options page, and Host Status proof.");
                readiness.ManualSteps.Add("Submit through the Edge add-ons flow manually; this artifact is not publication proof.");
                readiness.Warnings.Add("Edge add-ons review, signing, availability, and native-host dependency approval remain manual proof lanes.");
                break;
            case BrowserExtensionStoreTarget.Firefox:
                readiness.Status = readiness.Issues.Count == 0
                    ? "manual-browser-specific-review-required"
                    : "blocked";
                readiness.ManualSteps.Add("Review the current Firefox add-on packaging, native-messaging, and manifest behavior before submission.");
                readiness.ManualSteps.Add("Attach live Firefox screenshots from the safe fixture, popup/options equivalent, and Host Status proof when available.");
                readiness.ManualSteps.Add("Submit through the Firefox add-on flow manually only after browser-specific validation.");
                readiness.Warnings.Add("Firefox store readiness is intentionally conservative because browser-specific MV3/native-host behavior must be verified in a live browser.");
                break;
        }

        return readiness;
    }

    private static List<BrowserExtensionPermissionRationale> BuildPermissionRationales(BrowserExtensionManifestFacts facts)
    {
        var rationales = new List<BrowserExtensionPermissionRationale>();
        foreach (var permission in facts.Permissions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            rationales.Add(permission switch
            {
                "activeTab" => new BrowserExtensionPermissionRationale
                {
                    Permission = permission,
                    Purpose = "Capture the currently visible tab only after an operator action.",
                    UserBenefit = "Enables safe fixture proof, visible viewport capture, and full-page tile capture without browsing-history collection.",
                    DataBoundary = "The extension records page geometry and tile metadata; bitmap export requires explicit screenshot consent.",
                    ConsentBoundary = "Screenshot consent must be true before a capture handoff is accepted."
                },
                "downloads" => new BrowserExtensionPermissionRationale
                {
                    Permission = permission,
                    Purpose = "Export a local stitch package through the browser downloads flow.",
                    UserBenefit = "Allows large page captures to move through local files instead of embedding image bytes in native messaging payloads.",
                    DataBoundary = "The package is local-only and contains the manifest, stitched image, and tile images selected by the operator.",
                    ConsentBoundary = "Stitch package export requires screenshot consent and an explicit package-export option."
                },
                "nativeMessaging" => new BrowserExtensionPermissionRationale
                {
                    Permission = permission,
                    Purpose = "Connect the extension to GoatShot's local native host `com.goatshot.bridge`.",
                    UserBenefit = "Lets the browser hand off validated capture payloads and Host Status diagnostics to the native desktop app.",
                    DataBoundary = "Native payloads are validated and redacted before persistence; browser-store publication is not implied.",
                    ConsentBoundary = "Native handoff is driven by explicit extension actions and the local native-host installation."
                },
                "storage" => new BrowserExtensionPermissionRationale
                {
                    Permission = permission,
                    Purpose = "Save local popup/options defaults.",
                    UserBenefit = "Keeps capture preferences, consent defaults, and last-result feedback available between browser sessions.",
                    DataBoundary = "Storage is used for extension settings, not cookies, headers, DOM text, local storage dumps, or form values.",
                    ConsentBoundary = "Telemetry and screenshot behavior still require visible consent at capture time."
                },
                _ => new BrowserExtensionPermissionRationale
                {
                    Permission = permission,
                    Purpose = "Declared by the manifest and must be reviewed before store submission.",
                    UserBenefit = "Review required.",
                    DataBoundary = "No extra data boundary has been generated for this permission.",
                    ConsentBoundary = "Review required."
                }
            });
        }

        foreach (var hostPermission in facts.HostPermissions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            rationales.Add(new BrowserExtensionPermissionRationale
            {
                Permission = $"host:{hostPermission}",
                Purpose = "Make the content script available on operator-selected web pages.",
                UserBenefit = "Allows browser page geometry, safe fixture coverage, and selected-element bounds to be collected from normal pages.",
                DataBoundary = "The prototype does not collect cookies, headers, storage, form values, DOM text dumps, ids, classes, or raw selector paths.",
                ConsentBoundary = "Page capture and telemetry handoff require explicit consent and native validation."
            });
        }

        return rationales;
    }

    private static string BuildStoreReadinessMarkdown(BrowserExtensionStoreReadinessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Browser Extension Store Readiness");
        builder.AppendLine();
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Generated artifacts: `{result.OutputPath}`");
        builder.AppendLine();
        builder.AppendLine("## Proof Boundary");
        builder.AppendLine();
        builder.AppendLine("- This artifact is a store-readiness checklist and copy generator.");
        builder.AppendLine("- It is not browser-store publication, signing, approval, availability, automatic installation, or live browser Host Status proof.");
        builder.AppendLine("- Native-host install proof and popup Host Status screenshots remain separate manual evidence lanes.");
        builder.AppendLine();
        builder.AppendLine("## Manifest Snapshot");
        builder.AppendLine();
        builder.AppendLine($"- Name: `{EmptyIfMissing(result.Manifest.Name)}`");
        builder.AppendLine($"- Version: `{EmptyIfMissing(result.Manifest.Version)}`");
        builder.AppendLine($"- Manifest version: `{result.Manifest.ManifestVersion}`");
        builder.AppendLine($"- Background service worker: `{EmptyIfMissing(result.Manifest.BackgroundServiceWorker)}`");
        builder.AppendLine($"- Popup: `{EmptyIfMissing(result.Manifest.ActionDefaultPopup)}`");
        builder.AppendLine($"- Options page: `{EmptyIfMissing(result.Manifest.OptionsPage)}`");
        builder.AppendLine($"- Permissions: `{string.Join(", ", result.Manifest.Permissions)}`");
        builder.AppendLine($"- Host permissions: `{string.Join(", ", result.Manifest.HostPermissions)}`");
        builder.AppendLine();
        builder.AppendLine("## Target Readiness");
        builder.AppendLine();
        foreach (var target in result.Targets)
        {
            builder.AppendLine($"### {target.Target}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{target.Status}`");
            AppendList(builder, "Checks", target.Checks, 4);
            AppendList(builder, "Issues", target.Issues, 4);
            AppendList(builder, "Warnings", target.Warnings, 4);
            AppendList(builder, "Manual Steps", target.ManualSteps, 4);
            builder.AppendLine();
        }

        AppendList(builder, "Next Actions", result.NextActions);
        return builder.ToString();
    }

    private static string BuildPermissionRationaleMarkdown(BrowserExtensionStoreReadinessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Browser Extension Permission Rationale");
        builder.AppendLine();
        builder.AppendLine("Use this as draft store-review copy. Review it against the current browser-store form before submission.");
        builder.AppendLine();
        foreach (var rationale in result.PermissionRationales)
        {
            builder.AppendLine($"## {rationale.Permission}");
            builder.AppendLine();
            builder.AppendLine($"Purpose: {rationale.Purpose}");
            builder.AppendLine();
            builder.AppendLine($"User benefit: {rationale.UserBenefit}");
            builder.AppendLine();
            builder.AppendLine($"Data boundary: {rationale.DataBoundary}");
            builder.AppendLine();
            builder.AppendLine($"Consent boundary: {rationale.ConsentBoundary}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildPrivacyDataUseMarkdown(BrowserExtensionStoreReadinessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Browser Extension Privacy And Data Use");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("GoatShot's browser extension is an optional companion for local page capture. It collects page geometry and optional bounded telemetry only after visible operator consent, then hands data to the local GoatShot desktop app for validation and redaction.");
        builder.AppendLine();
        builder.AppendLine("## Collected With Screenshot Consent");
        builder.AppendLine();
        builder.AppendLine("- Page URL, title, referrer, content type, language, and capture timestamp.");
        builder.AppendLine("- Viewport, scroll, full-page dimensions, device pixel ratio, tile plans, and selected-element geometry.");
        builder.AppendLine("- Optional local stitch package files when the operator enables package export.");
        builder.AppendLine();
        builder.AppendLine("## Collected Only With Telemetry Consent");
        builder.AppendLine();
        builder.AppendLine("- Bounded console event summaries.");
        builder.AppendLine("- Bounded network URL/status/resource-type summaries.");
        builder.AppendLine();
        builder.AppendLine("## Not Collected In This Prototype");
        builder.AppendLine();
        builder.AppendLine("- Cookies.");
        builder.AppendLine("- Request or response headers.");
        builder.AppendLine("- Form values.");
        builder.AppendLine("- DOM text dumps.");
        builder.AppendLine("- Local storage or session storage.");
        builder.AppendLine("- Element ids, classes, or raw selector paths.");
        builder.AppendLine("- Automatic uploads.");
        builder.AppendLine();
        builder.AppendLine("## Review Notes");
        builder.AppendLine();
        builder.AppendLine("- Redacted payloads are generated by the native contract service before local persistence.");
        builder.AppendLine("- Browser downloads do not expose a stable absolute path to the native app, so stitch-package import remains explicit.");
        builder.AppendLine("- Browser-store submission still requires reviewed privacy and support URLs.");
        builder.AppendLine($"- Current support URL: `{EmptyIfMissing(result.SupportUrl)}`");
        builder.AppendLine($"- Current privacy URL: `{EmptyIfMissing(result.PrivacyUrl)}`");
        return builder.ToString();
    }

    private static string BuildScreenshotsChecklistMarkdown(BrowserExtensionStoreReadinessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Browser Extension Store Screenshot Checklist");
        builder.AppendLine();
        builder.AppendLine("Capture these from a live browser session after loading the extension. Store-review screenshots should not include private page content.");
        builder.AppendLine();
        builder.AppendLine("- Extension popup with screenshot consent visible.");
        builder.AppendLine("- Extension popup with telemetry consent visible but disabled by default.");
        builder.AppendLine("- Options page with safe defaults and package export controls.");
        builder.AppendLine("- Host Status success state after native-host installation.");
        builder.AppendLine("- Host Status failure or diagnostic state for recovery copy.");
        builder.AppendLine("- Safe fixture full-page capture setup showing tall-page and horizontal-scroll coverage.");
        builder.AppendLine("- Safe fixture selected-element capture state.");
        builder.AppendLine("- Browser downloads folder showing a local GoatShot stitch package from the safe fixture.");
        builder.AppendLine("- GoatShot native import result for the downloaded package.");
        builder.AppendLine("- Browser extension details page showing the extension id used for native-host registration.");
        builder.AppendLine();
        builder.AppendLine("Keep native-host and import proof in the manual validation folder; store screenshots are listing/review assets only.");
        return builder.ToString();
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

    private static List<BrowserExtensionStoreTarget> NormalizeTargets(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new List<BrowserExtensionStoreTarget>
            {
                BrowserExtensionStoreTarget.Local,
                BrowserExtensionStoreTarget.Chrome,
                BrowserExtensionStoreTarget.Edge,
                BrowserExtensionStoreTarget.Firefox
            };
        }

        var targets = new List<BrowserExtensionStoreTarget>();
        foreach (var part in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<BrowserExtensionStoreTarget>(part, ignoreCase: true, out var parsed))
            {
                targets.Add(parsed);
            }
        }

        return targets.Distinct().ToList();
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

    private static string EmptyIfMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

    private static string StringValue(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int IntValue(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var intValue)
            ? intValue
            : 0;

    private static IEnumerable<string> StringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return item.GetString() ?? string.Empty;
            }
        }
    }

    private static IEnumerable<string> ContentScriptMatches(JsonElement root)
    {
        foreach (var script in ContentScripts(root))
        {
            foreach (var value in StringArray(script, "matches"))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ContentScriptFiles(JsonElement root)
    {
        foreach (var script in ContentScripts(root))
        {
            foreach (var value in StringArray(script, "js"))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<JsonElement> ContentScripts(JsonElement root)
    {
        if (!root.TryGetProperty("content_scripts", out var contentScripts) ||
            contentScripts.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var script in contentScripts.EnumerateArray())
        {
            yield return script;
        }
    }
}

public enum BrowserExtensionStoreTarget
{
    Local,
    Chrome,
    Edge,
    Firefox
}

public sealed class BrowserExtensionStoreReadinessRequest
{
    public string? ExtensionSourceDirectory { get; set; }
    public string? OutputPath { get; set; }
    public string? Target { get; set; }
    public string? SupportUrl { get; set; }
    public string? PrivacyUrl { get; set; }
}

public sealed class BrowserExtensionStoreReadinessResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string PrivacyUrl { get; set; } = string.Empty;
    public BrowserExtensionManifestFacts Manifest { get; set; } = new();
    public List<BrowserExtensionStoreTargetReadiness> Targets { get; set; } = new();
    public List<BrowserExtensionPermissionRationale> PermissionRationales { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionManifestFacts
{
    public string ManifestPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ManifestVersion { get; set; }
    public string BackgroundServiceWorker { get; set; } = string.Empty;
    public string ActionDefaultPopup { get; set; } = string.Empty;
    public string OptionsPage { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public List<string> ContentScriptMatches { get; set; } = new();
    public List<string> ContentScriptFiles { get; set; } = new();
}

public sealed class BrowserExtensionStoreTargetReadiness
{
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> Checks { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ManualSteps { get; set; } = new();
}

public sealed class BrowserExtensionPermissionRationale
{
    public string Permission { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string UserBenefit { get; set; } = string.Empty;
    public string DataBoundary { get; set; } = string.Empty;
    public string ConsentBoundary { get; set; } = string.Empty;
}

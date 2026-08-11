using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionEnterprisePolicyPlanService
{
    private const string ChromeDefaultUpdateUrl = "https://clients2.google.com/service/update2/crx";
    private const string EdgeDefaultUpdateUrl = "https://edge.microsoft.com/extensionwebstorebase/v1/crx";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionEnterprisePolicyPlanResult> CreateAsync(
        BrowserExtensionEnterprisePolicyPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-enterprise-policy-plan"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var result = new BrowserExtensionEnterprisePolicyPlanResult
        {
            SourceDirectory = source,
            OutputPath = outputRoot,
            PolicyAllowed = request.PolicyAllowed,
            PolicyReason = SensitiveTextDetector.Redact(request.PolicyReason ?? string.Empty),
            WouldApplyPolicy = false,
            WouldWriteRegistry = false,
            WouldWriteEnterprisePolicyFile = false,
            WouldMutateBrowserProfile = false,
            WouldInstallExtension = false,
            WouldRegisterNativeHost = false,
            WouldContactStoreAccount = false
        };

        var browsers = NormalizeBrowsers(request.Browser);
        if (browsers.Count == 0)
        {
            result.Issues.Add("Enterprise policy target must be one of: all, chrome, edge, firefox.");
        }

        Directory.CreateDirectory(outputRoot);
        foreach (var browser in browsers)
        {
            var entry = await BuildEntryAsync(browser, source, outputRoot, request, result.PolicyAllowed, result.PolicyReason, cancellationToken);
            result.Entries.Add(entry);
            result.GeneratedFiles.AddRange(entry.GeneratedFiles);
        }

        result.AuthorityBoundaries.AddRange(BuildAuthorityBoundaries());
        result.NonGoals.AddRange(BuildNonGoals());
        result.SourceReferences.AddRange(BuildSourceReferences());
        result.Succeeded = result.Issues.Count == 0 &&
            result.Entries.Count > 0 &&
            result.Entries.All(entry => entry.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"Browser extension enterprise policy plan created: {outputRoot}. No enterprise policy was applied."
            : $"Browser extension enterprise policy plan created with blockers: {outputRoot}. No enterprise policy was applied.";

        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "enterprise-policy-plan.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "enterprise-policy-plan.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static async Task<BrowserExtensionEnterprisePolicyTarget> BuildEntryAsync(
        BrowserNativeHostBrowser browser,
        string source,
        string outputRoot,
        BrowserExtensionEnterprisePolicyPlanRequest request,
        bool policyAllowed,
        string policyReason,
        CancellationToken cancellationToken)
    {
        var browserName = browser.ToString().ToLowerInvariant();
        var extensionId = ExtensionIdFor(browser, request);
        var updateUrl = UpdateUrlFor(browser, request);
        var installUrl = FirefoxInstallUrlFor(request);
        var entry = new BrowserExtensionEnterprisePolicyTarget
        {
            Browser = browserName,
            ExtensionId = SensitiveTextDetector.Redact(extensionId ?? string.Empty),
            UpdateUrl = SensitiveTextDetector.Redact(updateUrl ?? string.Empty),
            FirefoxInstallUrl = SensitiveTextDetector.Redact(installUrl ?? string.Empty),
            SourceDirectory = source,
            SourceManifestExists = File.Exists(Path.Combine(source, "manifest.json")),
            PolicyTemplateKind = browser switch
            {
                BrowserNativeHostBrowser.Chrome => "windows-registry-extension-install-forcelist",
                BrowserNativeHostBrowser.Edge => "windows-registry-extension-install-forcelist",
                BrowserNativeHostBrowser.Firefox => "firefox-policies-json-extension-settings",
                _ => "unknown"
            },
            WouldApplyPolicy = false,
            WouldWriteRegistry = false,
            WouldWriteEnterprisePolicyFile = false,
            WouldMutateBrowserProfile = false,
            WouldInstallExtension = false,
            WouldRegisterNativeHost = false,
            WouldContactStoreAccount = false
        };

        if (!policyAllowed)
        {
            entry.Issues.Add(string.IsNullOrWhiteSpace(policyReason)
                ? "Browser extension handoff is disabled by managed policy."
                : policyReason);
            entry.Status = "blocked-by-managed-policy";
        }
        else if (browser is BrowserNativeHostBrowser.Chrome or BrowserNativeHostBrowser.Edge &&
            string.IsNullOrWhiteSpace(extensionId))
        {
            entry.Issues.Add($"{browser} enterprise force-install planning requires a reviewed extension id.");
            entry.Status = "blocked-missing-extension-id";
        }
        else if (browser is BrowserNativeHostBrowser.Firefox &&
            (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(installUrl)))
        {
            entry.Issues.Add("Firefox enterprise force-install planning requires a reviewed extension id and signed XPI/install URL.");
            entry.Status = "blocked-missing-firefox-install-url";
        }
        else
        {
            entry.Status = "enterprise-policy-template-ready";
        }

        entry.Warnings.Add("These files are local templates only; Receipts did not write HKLM/HKCU policy, Firefox distribution policy, browser profiles, or native-host registrations.");
        entry.Warnings.Add("Enterprise deployment still needs an administrator, MDM, Group Policy, or packaging system plus browser-side policy proof.");
        entry.RequiredEvidence.AddRange(BuildRequiredEvidence(browser));
        entry.ManualSteps.AddRange(BuildManualSteps(browser));
        entry.Commands.Add("receipts browser-extension native-host status --json");
        entry.Commands.Add($"receipts browser-extension proof validate --folder <enterprise-proof-folder> --browser {browserName} --extension-id <installed-extension-id> --json");

        var fileName = browser switch
        {
            BrowserNativeHostBrowser.Chrome => "chrome-extension-install-forcelist.reg",
            BrowserNativeHostBrowser.Edge => "edge-extension-install-forcelist.reg",
            BrowserNativeHostBrowser.Firefox => "firefox-policies.json",
            _ => $"{browserName}-enterprise-policy.txt"
        };
        var contents = browser switch
        {
            BrowserNativeHostBrowser.Chrome => BuildChromiumRegistryTemplate("Google\\Chrome", extensionId, updateUrl),
            BrowserNativeHostBrowser.Edge => BuildChromiumRegistryTemplate("Microsoft\\Edge", extensionId, updateUrl),
            BrowserNativeHostBrowser.Firefox => BuildFirefoxPoliciesJson(extensionId, installUrl),
            _ => string.Empty
        };
        entry.PolicyTemplatePath = await WriteFileAsync(outputRoot, fileName, contents, cancellationToken);
        entry.GeneratedFiles.Add(entry.PolicyTemplatePath);

        return entry;
    }

    private static string BuildChromiumRegistryTemplate(string policyVendorPath, string? extensionId, string? updateUrl)
    {
        var value = string.IsNullOrWhiteSpace(extensionId)
            ? "<reviewed-extension-id>;<reviewed-update-url>"
            : $"{extensionId};{(string.IsNullOrWhiteSpace(updateUrl) ? "<reviewed-update-url>" : updateUrl)}";
        return $"""
        Windows Registry Editor Version 5.00

        [HKEY_LOCAL_MACHINE\SOFTWARE\Policies\{policyVendorPath}\ExtensionInstallForcelist]
        "1"="{EscapeRegistryValue(value)}"

        """;
    }

    private static string BuildFirefoxPoliciesJson(string? extensionId, string? installUrl)
    {
        var id = string.IsNullOrWhiteSpace(extensionId) ? "receipts@example.invalid" : extensionId;
        var url = string.IsNullOrWhiteSpace(installUrl) ? "https://example.invalid/receipts-extension.xpi" : installUrl;
        var policy = new
        {
            policies = new Dictionary<string, object>
            {
                ["ExtensionSettings"] = new Dictionary<string, object>
                {
                    [id] = new Dictionary<string, object>
                    {
                        ["installation_mode"] = "force_installed",
                        ["install_url"] = url
                    }
                }
            }
        };
        return JsonSerializer.Serialize(policy, JsonOptions) + Environment.NewLine;
    }

    private static string BuildMarkdown(BrowserExtensionEnterprisePolicyPlanResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Browser Extension Enterprise Policy Plan");
        builder.AppendLine();
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Policy allowed: `{result.PolicyAllowed}`");
        if (!string.IsNullOrWhiteSpace(result.PolicyReason))
        {
            builder.AppendLine($"Policy reason: `{result.PolicyReason}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Mutation Boundary");
        builder.AppendLine();
        builder.AppendLine($"- Would apply policy: `{result.WouldApplyPolicy}`");
        builder.AppendLine($"- Would write registry: `{result.WouldWriteRegistry}`");
        builder.AppendLine($"- Would write enterprise policy file: `{result.WouldWriteEnterprisePolicyFile}`");
        builder.AppendLine($"- Would mutate browser profile: `{result.WouldMutateBrowserProfile}`");
        builder.AppendLine($"- Would install extension: `{result.WouldInstallExtension}`");
        builder.AppendLine($"- Would register native host: `{result.WouldRegisterNativeHost}`");
        builder.AppendLine($"- Would contact store account: `{result.WouldContactStoreAccount}`");
        builder.AppendLine();
        AppendList(builder, "Non-Goals", result.NonGoals);
        builder.AppendLine("## Targets");
        builder.AppendLine();
        foreach (var entry in result.Entries)
        {
            builder.AppendLine($"### {entry.Browser}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{entry.Status}`");
            builder.AppendLine($"Template kind: `{entry.PolicyTemplateKind}`");
            builder.AppendLine($"Extension id: `{Empty(entry.ExtensionId)}`");
            builder.AppendLine($"Policy template: `{entry.PolicyTemplatePath}`");
            builder.AppendLine($"Would apply policy: `{entry.WouldApplyPolicy}`");
            builder.AppendLine($"Would install extension: `{entry.WouldInstallExtension}`");
            AppendList(builder, "Issues", entry.Issues, 4);
            AppendList(builder, "Warnings", entry.Warnings, 4);
            AppendList(builder, "Manual Steps", entry.ManualSteps, 4);
            AppendList(builder, "Required Evidence", entry.RequiredEvidence, 4);
            AppendList(builder, "Commands", entry.Commands, 4);
        }

        AppendList(builder, "Authority Boundaries", result.AuthorityBoundaries);
        AppendList(builder, "Source References", result.SourceReferences);
        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildManualSteps(BrowserNativeHostBrowser browser)
    {
        var browserName = browser.ToString();
        return new[]
        {
            $"Review the generated {browserName} policy template and replace any placeholder values before enterprise use.",
            "Deploy policy through an administrator-controlled Group Policy, MDM, Intune, or packaging system.",
            "Deploy or verify the Receipts native messaging host manifest separately for the managed browser.",
            $"Open {browserName} policy/status pages on a managed test profile and save screenshots showing the policy is active.",
            "Use the extension popup Host Status button and safe fixture capture/import proof before claiming live compatibility."
        };
    }

    private static IReadOnlyList<string> BuildRequiredEvidence(BrowserNativeHostBrowser browser)
    {
        var browserName = browser.ToString();
        return new[]
        {
            $"{browserName} enterprise policy page screenshot showing force-install policy applied.",
            $"{browserName} extension details screenshot showing the managed Receipts extension installed and locked.",
            $"{browserName} popup Host Status success screenshot.",
            "Native-host registration status for the same browser and extension id.",
            "Safe-fixture capture/export/import proof from the managed profile.",
            "Administrator/MDM/Group Policy deployment note naming the policy channel used."
        };
    }

    private static IReadOnlyList<string> BuildAuthorityBoundaries() => new[]
    {
        "Chrome and Edge ExtensionInstallForcelist policy deployment is owned by enterprise policy channels such as Group Policy or MDM.",
        "Firefox ExtensionSettings deployment is owned by Firefox Enterprise Policies through policies.json, Group Policy templates, or MDM.",
        "Receipts can generate local templates and commands, but cannot claim enterprise deployment until browser policy pages and managed profile behavior are observed.",
        "Native-host deployment remains separate from extension force-install policy and must be proven independently."
    };

    private static IReadOnlyList<string> BuildNonGoals() => new[]
    {
        "This plan does not apply HKLM/HKCU registry policy.",
        "This plan does not write Firefox distribution policies into a browser install.",
        "This plan does not contact Chrome Web Store, Microsoft Edge Add-ons, Firefox AMO, or an MDM service.",
        "This plan does not install, sign, approve, publish, or update browser extensions.",
        "This plan does not register the Receipts native messaging host.",
        "This plan does not prove live browser Host Status, safe fixture capture, or automatic enterprise deployment."
    };

    private static IReadOnlyList<string> BuildSourceReferences() => new[]
    {
        "Chrome Enterprise ExtensionInstallForcelist: https://chromeenterprise.google/policies/extension-install-forcelist/",
        "Chrome Enterprise ExtensionSettings: https://chromeenterprise.google/policies/extension-settings/",
        "Microsoft Edge ExtensionInstallForcelist: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/extensioninstallforcelist",
        "Microsoft Edge ExtensionSettings: https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/extensionsettings",
        "Firefox Enterprise Policies: https://mozilla.github.io/policy-templates/",
        "Firefox enterprise extension guidance: https://extensionworkshop.com/documentation/enterprise/enterprise-development/"
    };

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
        BrowserExtensionEnterprisePolicyPlanRequest request)
    {
        return browser switch
        {
            BrowserNativeHostBrowser.Chrome => request.ChromeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Edge => request.EdgeExtensionId ?? request.ExtensionId,
            BrowserNativeHostBrowser.Firefox => request.FirefoxExtensionId ?? request.ExtensionId,
            _ => request.ExtensionId
        };
    }

    private static string? UpdateUrlFor(
        BrowserNativeHostBrowser browser,
        BrowserExtensionEnterprisePolicyPlanRequest request)
    {
        return browser switch
        {
            BrowserNativeHostBrowser.Chrome => request.ChromeUpdateUrl ?? request.UpdateUrl ?? ChromeDefaultUpdateUrl,
            BrowserNativeHostBrowser.Edge => request.EdgeUpdateUrl ?? request.UpdateUrl ?? EdgeDefaultUpdateUrl,
            _ => request.UpdateUrl
        };
    }

    private static string? FirefoxInstallUrlFor(BrowserExtensionEnterprisePolicyPlanRequest request) =>
        request.FirefoxInstallUrl ?? request.FirefoxXpiUrl ?? request.InstallUrl;

    private static async Task<string> WriteFileAsync(
        string root,
        string fileName,
        string contents,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents, cancellationToken);
        return path;
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

    private static string EscapeRegistryValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : value;
}

public sealed class BrowserExtensionEnterprisePolicyPlanRequest
{
    public string? Browser { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? OutputPath { get; set; }
    public string? ExtensionId { get; set; }
    public string? ChromeExtensionId { get; set; }
    public string? EdgeExtensionId { get; set; }
    public string? FirefoxExtensionId { get; set; }
    public string? UpdateUrl { get; set; }
    public string? ChromeUpdateUrl { get; set; }
    public string? EdgeUpdateUrl { get; set; }
    public string? InstallUrl { get; set; }
    public string? FirefoxInstallUrl { get; set; }
    public string? FirefoxXpiUrl { get; set; }
    public bool PolicyAllowed { get; set; } = true;
    public string? PolicyReason { get; set; }
}

public sealed class BrowserExtensionEnterprisePolicyPlanResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool PolicyAllowed { get; set; }
    public string PolicyReason { get; set; } = string.Empty;
    public bool WouldApplyPolicy { get; set; }
    public bool WouldWriteRegistry { get; set; }
    public bool WouldWriteEnterprisePolicyFile { get; set; }
    public bool WouldMutateBrowserProfile { get; set; }
    public bool WouldInstallExtension { get; set; }
    public bool WouldRegisterNativeHost { get; set; }
    public bool WouldContactStoreAccount { get; set; }
    public List<BrowserExtensionEnterprisePolicyTarget> Entries { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public List<string> AuthorityBoundaries { get; set; } = new();
    public List<string> SourceReferences { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionEnterprisePolicyTarget
{
    public string Browser { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public bool SourceManifestExists { get; set; }
    public string ExtensionId { get; set; } = string.Empty;
    public string UpdateUrl { get; set; } = string.Empty;
    public string FirefoxInstallUrl { get; set; } = string.Empty;
    public string PolicyTemplateKind { get; set; } = string.Empty;
    public string PolicyTemplatePath { get; set; } = string.Empty;
    public bool WouldApplyPolicy { get; set; }
    public bool WouldWriteRegistry { get; set; }
    public bool WouldWriteEnterprisePolicyFile { get; set; }
    public bool WouldMutateBrowserProfile { get; set; }
    public bool WouldInstallExtension { get; set; }
    public bool WouldRegisterNativeHost { get; set; }
    public bool WouldContactStoreAccount { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ManualSteps { get; set; } = new();
    public List<string> RequiredEvidence { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

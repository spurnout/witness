using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AdminPolicyBundleService
{
    public const string CurrentSchemaVersion = "receipts.admin-policy.v1";
    public const string LegacySchemaVersion = "goatshot.admin-policy.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AdminPolicyBundleValidationResult ValidateFile(string path)
    {
        var result = new AdminPolicyBundleValidationResult
        {
            Path = ResolvePath(path)
        };
        var bundle = ReadBundle(result.Path, result.Issues);
        if (bundle is null)
        {
            result.Message = "Admin policy bundle validation failed.";
            return result;
        }

        ValidateBundle(bundle, result);
        result.Succeeded = result.Issues.Count == 0;
        result.BundleName = bundle.Name;
        result.Message = result.Succeeded
            ? "Admin policy bundle is valid."
            : "Admin policy bundle validation failed.";
        return result;
    }

    public AdminPolicyBundleExportResult Export(AppSettings settings, string outputPath)
    {
        var path = ResolvePath(outputPath);
        var bundle = new AdminPolicyBundle
        {
            Name = "Receipts local admin policy",
            Description = "Exported local admin policy bundle. Secrets and DPAPI credentials are not included.",
            ExportedAtUtc = DateTimeOffset.UtcNow,
            ManagedPolicy = ClonePolicy(settings.ManagedPolicy)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, JsonOptions));
        return new AdminPolicyBundleExportResult
        {
            Succeeded = true,
            Path = path,
            Message = "Admin policy bundle exported without secrets or DPAPI credentials."
        };
    }

    public AdminPolicyBundleImportResult Import(
        AppSettings settings,
        SettingsStore settingsStore,
        string inputPath,
        bool replace = false)
    {
        var validation = ValidateFile(inputPath);
        var result = new AdminPolicyBundleImportResult
        {
            Succeeded = validation.Succeeded,
            Path = validation.Path,
            Issues = validation.Issues.ToList()
        };
        if (!validation.Succeeded)
        {
            result.Message = "Admin policy bundle import failed validation.";
            return result;
        }

        var bundle = ReadBundle(validation.Path, result.Issues);
        if (bundle is null)
        {
            result.Succeeded = false;
            result.Message = "Admin policy bundle import failed validation.";
            return result;
        }

        settings.ManagedPolicy = replace
            ? ClonePolicy(bundle.ManagedPolicy)
            : ManagedPolicyService.MergeDenyWinsForImport(settings.ManagedPolicy, bundle.ManagedPolicy);
        if (settings.ManagedPolicy.RequirePrivateCaptureMode)
        {
            settings.PrivateCaptureMode = true;
        }

        if (settings.ManagedPolicy.DisableLocalPlugins)
        {
            settings.EnableLocalPlugins = false;
        }

        settingsStore.Save(settings);
        result.Succeeded = true;
        result.Replaced = replace;
        result.EffectiveSummary = ManagedPolicyService.LoadEffective(settings).Summary;
        result.Message = replace
            ? "Admin policy bundle imported with replace semantics."
            : "Admin policy bundle imported with deny-wins merge semantics.";
        return result;
    }

    public AdminPolicyExplainResult Explain(AppSettings settings, string? bundlePath = null)
    {
        ManagedPolicySettings policy;
        var source = "current settings";
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            policy = ClonePolicy(settings.ManagedPolicy);
        }
        else
        {
            var resolved = ResolvePath(bundlePath);
            var bundle = ReadBundle(resolved, issues);
            if (bundle is null)
            {
                return new AdminPolicyExplainResult
                {
                    Succeeded = false,
                    Source = resolved,
                    Issues = issues,
                    Message = "Admin policy bundle explain failed validation."
                };
            }

            var validation = ValidateFile(resolved);
            issues.AddRange(validation.Issues);
            if (!validation.Succeeded)
            {
                return new AdminPolicyExplainResult
                {
                    Succeeded = false,
                    Source = resolved,
                    Issues = issues,
                    Message = "Admin policy bundle explain failed validation."
                };
            }

            policy = ManagedPolicyService.MergeDenyWinsForImport(settings.ManagedPolicy, bundle.ManagedPolicy);
            source = $"current settings + {resolved}";
        }

        var effective = ManagedPolicyService.LoadEffective(new AppSettings { ManagedPolicy = policy });
        return new AdminPolicyExplainResult
        {
            Succeeded = true,
            Source = source,
            EffectivePolicy = effective,
            Message = effective.Summary
        };
    }

    public AdminPolicyDiffResult Diff(AppSettings settings, string bundlePath)
    {
        var validation = ValidateFile(bundlePath);
        var result = new AdminPolicyDiffResult
        {
            Succeeded = validation.Succeeded,
            Path = validation.Path,
            Issues = validation.Issues.ToList()
        };
        if (!validation.Succeeded)
        {
            result.Message = "Admin policy bundle diff failed validation.";
            return result;
        }

        var bundle = ReadBundle(validation.Path, result.Issues);
        if (bundle is null)
        {
            result.Succeeded = false;
            result.Message = "Admin policy bundle diff failed validation.";
            return result;
        }

        AddDiff(result, "disableAi", settings.ManagedPolicy.DisableAi, bundle.ManagedPolicy.DisableAi);
        AddDiff(result, "disableUploads", settings.ManagedPolicy.DisableUploads, bundle.ManagedPolicy.DisableUploads);
        AddDiff(result, "disableCustomScripts", settings.ManagedPolicy.DisableCustomScripts, bundle.ManagedPolicy.DisableCustomScripts);
        AddDiff(result, "disableCustomWebhooks", settings.ManagedPolicy.DisableCustomWebhooks, bundle.ManagedPolicy.DisableCustomWebhooks);
        AddDiff(result, "disableLocalPlugins", settings.ManagedPolicy.DisableLocalPlugins, bundle.ManagedPolicy.DisableLocalPlugins);
        AddDiff(result, "disableBrowserExtension", settings.ManagedPolicy.DisableBrowserExtension, bundle.ManagedPolicy.DisableBrowserExtension);
        AddDiff(result, "disableAndroidCapture", settings.ManagedPolicy.DisableAndroidCapture, bundle.ManagedPolicy.DisableAndroidCapture);
        AddDiff(result, "disableVirtualPrinterImport", settings.ManagedPolicy.DisableVirtualPrinterImport, bundle.ManagedPolicy.DisableVirtualPrinterImport);
        AddDiff(result, "requirePrivateCaptureMode", settings.ManagedPolicy.RequirePrivateCaptureMode, bundle.ManagedPolicy.RequirePrivateCaptureMode);
        AddDiff(result, "disableDiagnosticsLogCapture", settings.ManagedPolicy.DisableDiagnosticsLogCapture, bundle.ManagedPolicy.DisableDiagnosticsLogCapture);
        AddDiff(result, "retentionDays", settings.ManagedPolicy.RetentionDays, bundle.ManagedPolicy.RetentionDays);
        AddListDiff(result, "allowedShareDestinations", settings.ManagedPolicy.AllowedShareDestinations, bundle.ManagedPolicy.AllowedShareDestinations);
        AddListDiff(result, "allowedPluginIds", settings.ManagedPolicy.AllowedPluginIds, bundle.ManagedPolicy.AllowedPluginIds);
        AddListDiff(result, "allowedPluginActionIds", settings.ManagedPolicy.AllowedPluginActionIds, bundle.ManagedPolicy.AllowedPluginActionIds);
        result.Message = result.Differences.Count == 0
            ? "Admin policy bundle matches current managed policy."
            : $"{result.Differences.Count} admin policy difference(s) found.";
        return result;
    }

    private static void ValidateBundle(AdminPolicyBundle bundle, AdminPolicyBundleValidationResult result)
    {
        if (!bundle.SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal) &&
            !bundle.SchemaVersion.Equals(LegacySchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add($"Unsupported schemaVersion. Expected {CurrentSchemaVersion}; legacy {LegacySchemaVersion} is also accepted.");
        }

        if (string.IsNullOrWhiteSpace(bundle.Name))
        {
            result.Issues.Add("Admin policy bundle name is required.");
        }

        if (bundle.ManagedPolicy is null)
        {
            result.Issues.Add("Admin policy bundle must contain managedPolicy.");
            return;
        }

        foreach (var destination in bundle.ManagedPolicy.AllowedShareDestinations ?? [])
        {
            if (string.IsNullOrWhiteSpace(destination) ||
                ShareService.ParseDestination(destination) == ShareDestination.ClipboardImage &&
                !destination.Equals("Clipboard image", StringComparison.OrdinalIgnoreCase) &&
                !destination.Equals(nameof(ShareDestination.ClipboardImage), StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add($"Unknown allowed share destination: {SensitiveTextDetector.Redact(destination)}.");
            }
        }

        foreach (var pluginId in bundle.ManagedPolicy.AllowedPluginIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                result.Issues.Add("Allowed plugin ids cannot contain blank values.");
            }
        }

        foreach (var actionId in bundle.ManagedPolicy.AllowedPluginActionIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                !actionId.Contains(':', StringComparison.Ordinal))
            {
                result.Issues.Add("Allowed plugin action ids must use 'plugin-id:action-id' or 'plugin-id:*'.");
            }
        }

        if (bundle.ManagedPolicy.RetentionDays < 0)
        {
            result.Issues.Add("retentionDays cannot be negative.");
        }
    }

    private static AdminPolicyBundle? ReadBundle(string path, List<string> issues)
    {
        try
        {
            return JsonSerializer.Deserialize<AdminPolicyBundle>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add(SensitiveTextDetector.Redact(ex.Message));
            return null;
        }
    }

    private static ManagedPolicySettings ClonePolicy(ManagedPolicySettings policy)
    {
        return new ManagedPolicySettings
        {
            DisableAi = policy.DisableAi,
            DisableUploads = policy.DisableUploads,
            DisableCustomScripts = policy.DisableCustomScripts,
            DisableCustomWebhooks = policy.DisableCustomWebhooks,
            DisableLocalPlugins = policy.DisableLocalPlugins,
            DisableBrowserExtension = policy.DisableBrowserExtension,
            DisableAndroidCapture = policy.DisableAndroidCapture,
            DisableVirtualPrinterImport = policy.DisableVirtualPrinterImport,
            RequirePrivateCaptureMode = policy.RequirePrivateCaptureMode,
            DisableDiagnosticsLogCapture = policy.DisableDiagnosticsLogCapture,
            RetentionDays = policy.RetentionDays,
            AllowedShareDestinations = policy.AllowedShareDestinations.ToList(),
            AllowedPluginIds = policy.AllowedPluginIds.ToList(),
            AllowedPluginActionIds = policy.AllowedPluginActionIds.ToList()
        };
    }

    private static void AddDiff<T>(AdminPolicyDiffResult result, string field, T current, T bundle)
    {
        if (!EqualityComparer<T>.Default.Equals(current, bundle))
        {
            result.Differences.Add(new AdminPolicyDifference(field, current?.ToString() ?? string.Empty, bundle?.ToString() ?? string.Empty));
        }
    }

    private static void AddListDiff(AdminPolicyDiffResult result, string field, IReadOnlyList<string> current, IReadOnlyList<string> bundle)
    {
        var currentText = string.Join(", ", current.Order(StringComparer.OrdinalIgnoreCase));
        var bundleText = string.Join(", ", bundle.Order(StringComparer.OrdinalIgnoreCase));
        if (!currentText.Equals(bundleText, StringComparison.Ordinal))
        {
            result.Differences.Add(new AdminPolicyDifference(field, currentText, bundleText));
        }
    }

    private static string ResolvePath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}

public sealed class AdminPolicyBundle
{
    public string SchemaVersion { get; set; } = AdminPolicyBundleService.CurrentSchemaVersion;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? ExportedAtUtc { get; set; }
    public ManagedPolicySettings ManagedPolicy { get; set; } = new();
}

public sealed class AdminPolicyBundleValidationResult
{
    public bool Succeeded { get; set; }
    public string Path { get; set; } = string.Empty;
    public string BundleName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}

public sealed class AdminPolicyBundleExportResult
{
    public bool Succeeded { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class AdminPolicyBundleImportResult
{
    public bool Succeeded { get; set; }
    public bool Replaced { get; set; }
    public string Path { get; set; } = string.Empty;
    public string EffectiveSummary { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}

public sealed class AdminPolicyExplainResult
{
    public bool Succeeded { get; set; }
    public string Source { get; set; } = string.Empty;
    public EffectiveManagedPolicy? EffectivePolicy { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}

public sealed class AdminPolicyDiffResult
{
    public bool Succeeded { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<AdminPolicyDifference> Differences { get; set; } = new();
}

public sealed record AdminPolicyDifference(string Field, string CurrentValue, string BundleValue);

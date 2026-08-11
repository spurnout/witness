using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class RemotePluginPackageService
{
    public const string CurrentRegistrySchemaVersion = "receipts.plugin-registry.v1";
    public const string LegacyRegistrySchemaVersion = "goatshot.plugin-registry.v1";
    public const string CurrentStageSchemaVersion = "receipts.plugin-stage.v1";
    public const string LegacyStageSchemaVersion = "goatshot.plugin-stage.v1";
    public const string CurrentInstallSchemaVersion = "receipts.plugin-install.v1";
    public const string LegacyInstallSchemaVersion = "goatshot.plugin-install.v1";
    public const long DefaultMaxPackageBytes = 50L * 1024L * 1024L;

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private static readonly HttpClient SharedHttpClient = new();

    private readonly AppPaths _paths;
    private readonly LocalPluginService _localPlugins;
    private readonly HttpClient _httpClient;
    private readonly long _maxPackageBytes;
    private readonly AppSettings _settings;

    public RemotePluginPackageService(
        AppPaths paths,
        LocalPluginService localPlugins,
        HttpClient? httpClient = null,
        long maxPackageBytes = DefaultMaxPackageBytes,
        AppSettings? settings = null)
    {
        _paths = paths;
        _localPlugins = localPlugins;
        _httpClient = httpClient ?? SharedHttpClient;
        _maxPackageBytes = maxPackageBytes;
        _settings = settings ?? new AppSettings();
    }

    public async Task<RemotePluginRegistryValidationResult> ValidateRegistryAsync(
        string registryLocation,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadRegistryAsync(registryLocation, cancellationToken);
        return loaded.Validation;
    }

    public async Task<RemotePluginPackagePlanResult> PlanInstallAsync(
        string pluginId,
        string registryLocation,
        bool stagePackage = false,
        CancellationToken cancellationToken = default)
    {
        var result = new RemotePluginPackagePlanResult
        {
            PluginId = SafeText(pluginId),
            RegistryLocation = SafeText(registryLocation),
            StagingRoot = _paths.PluginStagingRoot,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false
        };

        if (!IsValidId(pluginId))
        {
            result.Issues.Add("Plugin id is required and must be 3-64 characters using letters, numbers, dots, underscores, or hyphens.");
            result.Message = "Remote plugin plan failed validation.";
            return result;
        }

        var loaded = await LoadRegistryAsync(registryLocation, cancellationToken);
        result.RegistryLocation = loaded.Validation.RegistryLocation;
        result.Issues.AddRange(loaded.Validation.Issues);
        if (loaded.Registry is null || !loaded.Validation.Succeeded)
        {
            result.Message = "Remote plugin registry is invalid.";
            return result;
        }

        var entry = loaded.Registry.Plugins.FirstOrDefault(item =>
            item.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            result.Issues.Add($"Plugin '{SafeText(pluginId)}' was not found in the registry.");
            result.Message = "Remote plugin plan failed validation.";
            return result;
        }

        CopyEntry(entry, result);
        ValidateCompatibility(entry, result);
        ValidateEntry(entry, result.Issues, result.Warnings);
        if (result.Issues.Count > 0)
        {
            result.Message = "Remote plugin plan failed validation.";
            return result;
        }

        if (!stagePackage)
        {
            result.Succeeded = true;
            result.Message = $"Remote plugin {result.PluginId} {result.Version} can be staged after checksum and archive safety checks. It will remain disabled and untrusted.";
            return result;
        }

        RemotePluginPackageBytes package;
        try
        {
            package = await ReadPackageAsync(entry, loaded, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            result.Message = "Remote plugin package was not staged.";
            return result;
        }

        result.PackageUri = package.DisplayUri;
        result.SizeBytes = package.Bytes.LongLength;
        result.PackageSha256 = ComputeSha256(package.Bytes);

        if (entry.SizeBytes is > 0 && entry.SizeBytes.Value != package.Bytes.LongLength)
        {
            result.Issues.Add($"Package size mismatch. Registry declared {entry.SizeBytes.Value} byte(s), package read {package.Bytes.LongLength} byte(s).");
        }

        if (package.Bytes.LongLength > _maxPackageBytes)
        {
            result.Issues.Add($"Package is {package.Bytes.LongLength} byte(s), over the {_maxPackageBytes} byte safety limit.");
        }

        if (!result.PackageSha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Package SHA-256 did not match the registry manifest.");
        }

        var signature = VerifyPackageSignature(package.Bytes, entry);
        result.SignatureAlgorithm = signature.Algorithm;
        result.SignaturePublicKeyFingerprint = signature.PublicKeyFingerprint;
        result.SignatureVerified = signature.Verified;
        result.Issues.AddRange(signature.Issues);
        result.Warnings.AddRange(signature.Warnings);

        var archiveValidation = ValidateArchive(package.Bytes, entry);
        result.Issues.AddRange(archiveValidation.Issues);
        result.Warnings.AddRange(archiveValidation.Warnings);
        if (result.Issues.Count > 0)
        {
            result.Message = "Remote plugin package was not staged.";
            return result;
        }

        Directory.CreateDirectory(_paths.PluginStagingRoot);
        var stageDirectory = GetStageDirectory(entry.Id, entry.Version);
        ResetStageDirectory(stageDirectory);
        var packagePath = Path.Combine(stageDirectory, $"{SafePathSegment(entry.Id)}-{SafePathSegment(entry.Version)}.zip");
        await File.WriteAllBytesAsync(packagePath, package.Bytes, cancellationToken);
        var extractDirectory = Path.Combine(stageDirectory, "extracted");
        Directory.CreateDirectory(extractDirectory);
        ExtractArchive(package.Bytes, extractDirectory);

        var stageManifest = new RemotePluginStageManifest
        {
            SchemaVersion = CurrentStageSchemaVersion,
            PluginId = entry.Id,
            Version = entry.Version,
            Name = entry.Name,
            RegistryLocation = loaded.Validation.RegistryLocation,
            PackageUri = package.DisplayUri,
            PackageSha256 = result.PackageSha256,
            SignatureAlgorithm = result.SignatureAlgorithm,
            SignatureVerified = result.SignatureVerified,
            SignaturePublicKeyFingerprint = result.SignaturePublicKeyFingerprint,
            SizeBytes = package.Bytes.LongLength,
            StagedAtUtc = DateTimeOffset.UtcNow,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false,
            Notes =
            [
                "Package is staged only.",
                "Receipts did not trust, enable, allowlist, or execute plugin code.",
                "Operator review is still required before copying a plugin into the active plugins folder."
            ]
        };
        await File.WriteAllTextAsync(
            Path.Combine(stageDirectory, "stage-manifest.json"),
            JsonSerializer.Serialize(stageManifest, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
            cancellationToken);

        result.Succeeded = true;
        result.Staged = true;
        result.StagedPackagePath = packagePath;
        result.StagedExtractPath = extractDirectory;
        result.Message = $"Remote plugin {result.PluginId} {result.Version} was staged only. It remains disabled, untrusted, and unexecuted.";
        return result;
    }

    public async Task<RemotePluginUpdateCheckResult> CheckUpdatesAsync(
        string registryLocation,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadRegistryAsync(registryLocation, cancellationToken);
        var result = new RemotePluginUpdateCheckResult
        {
            RegistryLocation = loaded.Validation.RegistryLocation
        };
        result.Issues.AddRange(loaded.Validation.Issues);
        if (loaded.Registry is null || !loaded.Validation.Succeeded)
        {
            result.Message = "Remote plugin registry is invalid.";
            return result;
        }

        var installed = _localPlugins.Discover()
            .Where(plugin => plugin.IsValid)
            .GroupBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Version, VersionStringComparer.Instance).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in loaded.Registry.Plugins)
        {
            if (!installed.TryGetValue(entry.Id, out var local))
            {
                continue;
            }

            var comparison = CompareVersions(entry.Version, local.Version);
            result.Candidates.Add(new RemotePluginUpdateCandidate
            {
                PluginId = entry.Id,
                Name = entry.Name,
                InstalledVersion = local.Version,
                RegistryVersion = entry.Version,
                UpdateAvailable = comparison > 0,
                RegistryPackageUri = SensitiveTextDetector.Redact(entry.PackageUri),
                Message = comparison > 0
                    ? $"Update available for {entry.Id}: {local.Version} -> {entry.Version}."
                    : $"No newer registry version for {entry.Id}."
            });
        }

        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.Candidates.Any(candidate => candidate.UpdateAvailable)
            ? "Remote plugin updates are available. No update was installed automatically."
            : "No remote plugin updates were found for discovered local plugins.";
        return result;
    }

    public async Task<RemotePluginUpdateSummaryResult> SummarizeUpdatesAsync(
        string registryLocation,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadRegistryAsync(registryLocation, cancellationToken);
        var result = new RemotePluginUpdateSummaryResult
        {
            RegistryLocation = loaded.Validation.RegistryLocation,
            PluginsRoot = _paths.PluginsRoot,
            StagingRoot = _paths.PluginStagingRoot,
            GoatShotVersion = GetAppVersion(),
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false
        };
        result.Issues.AddRange(loaded.Validation.Issues);
        result.Warnings.AddRange(loaded.Validation.Warnings);
        if (loaded.Registry is null || !loaded.Validation.Succeeded)
        {
            result.Message = "Remote plugin registry is invalid.";
            return result;
        }

        var localPlugins = _localPlugins.Discover()
            .Where(plugin => plugin.IsValid)
            .GroupBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Version, VersionStringComparer.Instance).First(),
                StringComparer.OrdinalIgnoreCase);
        var stagedPackages = DiscoverLatestStagedPackages();
        var policy = ManagedPolicyService.LoadEffective(_settings);

        foreach (var entry in loaded.Registry.Plugins)
        {
            localPlugins.TryGetValue(entry.Id, out var local);
            stagedPackages.TryGetValue(entry.Id, out var staged);
            result.Plugins.Add(BuildUpdateSummary(entry, local, staged, policy, result.GoatShotVersion));
        }

        result.PluginCount = result.Plugins.Count;
        result.AvailableCount = result.Plugins.Count(plugin => plugin.Status.Equals("available", StringComparison.OrdinalIgnoreCase));
        result.StagedCount = result.Plugins.Count(plugin => plugin.Staged);
        result.InstalledCount = result.Plugins.Count(plugin => plugin.Installed);
        result.BlockedCount = result.Plugins.Count(plugin => plugin.PolicyBlocked);
        result.IncompatibleCount = result.Plugins.Count(plugin => plugin.Incompatible);
        result.Succeeded = result.Issues.Count == 0;
        result.Message = BuildUpdateSummaryMessage(result);
        result.NextActions.Add("Use `receipts plugins stage <plugin-id> --registry <registry>` to download a package into staging after review.");
        result.NextActions.Add("Use `receipts plugins install-staged <plugin-id>` only after reviewing the staged package.");
        result.NextActions.Add("Trust, enable, allowlist, and run plugin actions through separate explicit operator steps.");
        result.NextActions.Add("Receipts does not install plugin updates automatically and does not execute plugin code during update checks.");
        return result;
    }

    public async Task<RemotePluginUpdateApplyResult> ApplyUpdatesAsync(
        string registryLocation,
        bool installStaged = false,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        var summary = await SummarizeUpdatesAsync(registryLocation, cancellationToken);
        var result = new RemotePluginUpdateApplyResult
        {
            RegistryLocation = summary.RegistryLocation,
            PluginsRoot = _paths.PluginsRoot,
            StagingRoot = _paths.PluginStagingRoot,
            GoatShotVersion = summary.GoatShotVersion,
            InstallStaged = installStaged,
            ReplaceExisting = replaceExisting,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false
        };
        result.Issues.AddRange(summary.Issues);
        result.Warnings.AddRange(summary.Warnings);

        if (!summary.Succeeded)
        {
            result.Message = "Remote plugin update apply could not start because the update summary failed.";
            return result;
        }

        foreach (var plugin in summary.Plugins.OrderBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (plugin.PolicyBlocked || plugin.Incompatible)
            {
                result.Items.Add(new RemotePluginUpdateApplyItem
                {
                    PluginId = plugin.PluginId,
                    Name = plugin.Name,
                    Status = "skipped",
                    InstalledVersion = plugin.InstalledVersion,
                    RegistryVersion = plugin.RegistryVersion,
                    StagedVersion = plugin.StagedVersion,
                    Message = plugin.PolicyBlocked
                        ? "Skipped because managed policy blocks this plugin."
                        : "Skipped because this registry version is incompatible with this Receipts build.",
                    Issues = plugin.PolicyBlocked
                        ? plugin.PolicyBlockReasons.ToList()
                        : plugin.CompatibilityIssues.ToList()
                });
                continue;
            }

            if (!plugin.Installed)
            {
                result.Items.Add(new RemotePluginUpdateApplyItem
                {
                    PluginId = plugin.PluginId,
                    Name = plugin.Name,
                    Status = "skipped",
                    InstalledVersion = plugin.InstalledVersion,
                    RegistryVersion = plugin.RegistryVersion,
                    StagedVersion = plugin.StagedVersion,
                    Message = "Skipped because this plugin is not installed locally; apply-updates does not install new registry plugins."
                });
                continue;
            }

            if (!plugin.UpdateAvailable && !plugin.Staged)
            {
                result.Items.Add(new RemotePluginUpdateApplyItem
                {
                    PluginId = plugin.PluginId,
                    Name = plugin.Name,
                    Status = "unchanged",
                    InstalledVersion = plugin.InstalledVersion,
                    RegistryVersion = plugin.RegistryVersion,
                    StagedVersion = plugin.StagedVersion,
                    Message = "No newer registry version is available."
                });
                continue;
            }

            if (plugin.Staged && plugin.StagedMatchesRegistry)
            {
                var item = new RemotePluginUpdateApplyItem
                {
                    PluginId = plugin.PluginId,
                    Name = plugin.Name,
                    Status = installStaged ? "installing-staged" : "already-staged",
                    InstalledVersion = plugin.InstalledVersion,
                    RegistryVersion = plugin.RegistryVersion,
                    StagedVersion = plugin.StagedVersion,
                    Staged = true,
                    StagedPackagePath = plugin.StagedPackagePath,
                    StagedDirectory = plugin.StagedDirectory,
                    Message = installStaged
                        ? "Matching staged package found; installing because --install-staged was requested."
                        : "Matching staged package already exists. No install was performed."
                };
                if (installStaged)
                {
                    ApplyStagedInstall(item, replaceExisting);
                }

                result.Items.Add(item);
                continue;
            }

            if (!plugin.UpdateAvailable)
            {
                result.Items.Add(new RemotePluginUpdateApplyItem
                {
                    PluginId = plugin.PluginId,
                    Name = plugin.Name,
                    Status = "unchanged",
                    InstalledVersion = plugin.InstalledVersion,
                    RegistryVersion = plugin.RegistryVersion,
                    StagedVersion = plugin.StagedVersion,
                    Message = "No newer registry version is available."
                });
                continue;
            }

            var applyItem = new RemotePluginUpdateApplyItem
            {
                PluginId = plugin.PluginId,
                Name = plugin.Name,
                Status = "staging",
                InstalledVersion = plugin.InstalledVersion,
                RegistryVersion = plugin.RegistryVersion,
                StagedVersion = plugin.StagedVersion,
                Message = "Staging available update for operator review."
            };
            var stage = await PlanInstallAsync(plugin.PluginId, registryLocation, stagePackage: true, cancellationToken);
            applyItem.Staged = stage.Staged;
            applyItem.StageMessage = stage.Message;
            applyItem.StagedVersion = stage.Version;
            applyItem.StagedPackagePath = stage.StagedPackagePath;
            applyItem.StagedDirectory = stage.StagedExtractPath;
            applyItem.PackageSha256 = stage.PackageSha256;
            applyItem.Issues.AddRange(stage.Issues);
            applyItem.Warnings.AddRange(stage.Warnings);
            applyItem.Status = stage.Succeeded ? "staged" : "failed";
            applyItem.Message = stage.Succeeded
                ? "Update package was staged. It was not trusted, enabled, allowlisted, or executed."
                : "Update package was not staged.";

            if (stage.Succeeded && installStaged)
            {
                ApplyStagedInstall(applyItem, replaceExisting);
            }

            result.Items.Add(applyItem);
        }

        result.PluginCount = result.Items.Count;
        result.StagedCount = result.Items.Count(item => item.Staged);
        result.InstalledCount = result.Items.Count(item => item.Installed);
        result.SkippedCount = result.Items.Count(item =>
            item.Status.Equals("skipped", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Equals("unchanged", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Equals("already-staged", StringComparison.OrdinalIgnoreCase));
        result.FailedCount = result.Items.Count(item => item.Status.Equals("failed", StringComparison.OrdinalIgnoreCase));
        result.WouldStagePackage = result.StagedCount > 0;
        result.WouldInstall = installStaged && result.InstalledCount > 0;
        result.Succeeded = result.Issues.Count == 0 && result.FailedCount == 0;
        result.Message = BuildUpdateApplyMessage(result);
        result.NextActions.Add("Review staged or installed package files before trusting or enabling any plugin.");
        result.NextActions.Add("Trust, enable, action allowlists, and plugin execution remain separate operator actions.");
        result.NextActions.Add("No plugin code was executed by this update apply command.");
        return result;
    }

    public async Task<RemotePluginMarketplacePlanResult> PlanMarketplaceAsync(
        string registryLocation,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadRegistryAsync(registryLocation, cancellationToken);
        var result = new RemotePluginMarketplacePlanResult
        {
            RegistryLocation = loaded.Validation.RegistryLocation,
            PluginsRoot = _paths.PluginsRoot,
            StagingRoot = _paths.PluginStagingRoot,
            GoatShotVersion = GetAppVersion(),
            WouldStagePackage = false,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            WouldAutoUpdate = false,
            WouldPublish = false,
            WouldContactMarketplaceAccount = false
        };
        result.Issues.AddRange(loaded.Validation.Issues);
        result.Warnings.AddRange(loaded.Validation.Warnings);
        AddMarketplaceGovernance(result);

        if (loaded.Registry is null || !loaded.Validation.Succeeded)
        {
            result.Message = "Plugin marketplace plan could not be completed because the registry is invalid.";
            return result;
        }

        var localPlugins = _localPlugins.Discover()
            .Where(plugin => plugin.IsValid)
            .GroupBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Version, VersionStringComparer.Instance).First(),
                StringComparer.OrdinalIgnoreCase);
        var stagedPackages = DiscoverLatestStagedPackages();
        var policy = ManagedPolicyService.LoadEffective(_settings);

        result.ActivePluginCount = localPlugins.Count;
        result.StagedPackageCount = stagedPackages.Count;

        foreach (var entry in loaded.Registry.Plugins)
        {
            localPlugins.TryGetValue(entry.Id, out var local);
            stagedPackages.TryGetValue(entry.Id, out var staged);
            var update = BuildUpdateSummary(entry, local, staged, policy, result.GoatShotVersion);
            result.Plugins.Add(BuildMarketplacePlanItem(entry, update));
        }

        result.PluginCount = result.Plugins.Count;
        result.AvailableCount = result.Plugins.Count(plugin => plugin.Status.Equals("available", StringComparison.OrdinalIgnoreCase));
        result.StagedCount = result.Plugins.Count(plugin => plugin.Staged);
        result.InstalledCount = result.Plugins.Count(plugin => plugin.Installed);
        result.BlockedCount = result.Plugins.Count(plugin => plugin.PolicyBlocked);
        result.IncompatibleCount = result.Plugins.Count(plugin => plugin.Incompatible);
        result.ListedOnlyCount = result.Plugins.Count(plugin => plugin.Status.Equals("not-installed", StringComparison.OrdinalIgnoreCase));
        result.Succeeded = result.Issues.Count == 0;
        result.Message = BuildMarketplacePlanMessage(result);
        result.NextActions.Add("Review registry metadata, package provenance, permissions, and policy status before staging anything.");
        result.NextActions.Add("Use `receipts plugins stage <plugin-id> --registry <registry>` only for a package you choose to review locally.");
        result.NextActions.Add("Use `receipts plugins install-staged <plugin-id>` only after staged package review.");
        result.NextActions.Add("Trust, enable, action allowlists, and execution remain separate explicit operator steps.");
        result.NextActions.Add("Hosted marketplace accounts, ratings, payments, publication, remote execution, and automatic self-registering updater services remain later-scope.");
        return result;
    }

    public RemotePluginStageRemovalResult RemoveStagedPackage(string pluginId)
    {
        var result = new RemotePluginStageRemovalResult
        {
            PluginId = SafeText(pluginId),
            StagingRoot = _paths.PluginStagingRoot
        };
        if (!IsValidId(pluginId))
        {
            result.Issues.Add("Plugin id is required and must be 3-64 characters using letters, numbers, dots, underscores, or hyphens.");
            result.Message = "Staged package removal failed validation.";
            return result;
        }

        var pluginStageRoot = Path.Combine(_paths.PluginStagingRoot, SafePathSegment(pluginId));
        if (!Directory.Exists(pluginStageRoot))
        {
            result.Succeeded = true;
            result.Message = "No staged package folder was present.";
            return result;
        }

        var resolved = Path.GetFullPath(pluginStageRoot);
        if (!IsInsideDirectory(resolved, _paths.PluginStagingRoot))
        {
            result.Issues.Add("Resolved staged plugin path was outside the plugin staging root.");
            result.Message = "Staged package removal failed validation.";
            return result;
        }

        Directory.Delete(resolved, recursive: true);
        result.Succeeded = true;
        result.Removed = true;
        result.Message = "Removed staged plugin package folder. Active plugin files, trust, enablement, and allowlists were not changed.";
        return result;
    }

    public RemotePluginActiveInstallResult InstallStagedPackage(
        string pluginId,
        string? version = null,
        bool replaceExisting = false)
    {
        var result = new RemotePluginActiveInstallResult
        {
            PluginId = SafeText(pluginId),
            Version = SafeText(version),
            StagingRoot = _paths.PluginStagingRoot,
            PluginsRoot = _paths.PluginsRoot,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false
        };
        if (!IsValidId(pluginId))
        {
            result.Issues.Add("Plugin id is required and must be 3-64 characters using letters, numbers, dots, underscores, or hyphens.");
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        var stageDirectory = ResolveStageDirectory(pluginId, version, result);
        if (stageDirectory is null)
        {
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        result.StageDirectory = stageDirectory;
        var manifestPath = Path.Combine(stageDirectory, "stage-manifest.json");
        if (!File.Exists(manifestPath))
        {
            result.Issues.Add("Staged package is missing stage-manifest.json.");
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        RemotePluginStageManifest? stageManifest;
        try
        {
            stageManifest = JsonSerializer.Deserialize<RemotePluginStageManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        if (stageManifest is null)
        {
            result.Issues.Add("Stage manifest did not contain a JSON object.");
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        if (!stageManifest.SchemaVersion.Equals(CurrentStageSchemaVersion, StringComparison.Ordinal) &&
            !stageManifest.SchemaVersion.Equals(LegacyStageSchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add("Unsupported stage manifest schemaVersion.");
        }

        if (!stageManifest.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Stage manifest plugin id does not match the requested plugin id.");
        }

        if (!string.IsNullOrWhiteSpace(version) &&
            !stageManifest.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Stage manifest version does not match the requested version.");
        }

        if (result.Issues.Count > 0)
        {
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        result.PluginId = stageManifest.PluginId;
        result.Version = stageManifest.Version;
        result.Name = stageManifest.Name;
        result.PackageSha256 = stageManifest.PackageSha256;

        var extractDirectory = Path.Combine(stageDirectory, "extracted");
        if (!Directory.Exists(extractDirectory))
        {
            result.Issues.Add("Staged package is missing the extracted plugin folder.");
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        var pluginSource = FindPackagedPluginRoot(extractDirectory, stageManifest, result);
        if (pluginSource is null)
        {
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        result.SourceDirectory = pluginSource;
        var targetDirectory = Path.GetFullPath(Path.Combine(_paths.PluginsRoot, SafePathSegment(stageManifest.PluginId)));
        result.InstalledDirectory = targetDirectory;
        if (!IsInsideDirectory(targetDirectory, _paths.PluginsRoot))
        {
            result.Issues.Add("Resolved active plugin path was outside the plugin root.");
            result.Message = "Staged plugin install failed validation.";
            return result;
        }

        if (Directory.Exists(targetDirectory))
        {
            if (!replaceExisting)
            {
                result.Issues.Add("An active plugin folder already exists. Pass --replace to overwrite it after review.");
                result.Message = "Staged plugin was not installed.";
                return result;
            }

            try
            {
                Directory.Delete(targetDirectory, recursive: true);
                result.ReplacedExisting = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
                result.Message = "Staged plugin was not installed.";
                return result;
            }
        }

        try
        {
            Directory.CreateDirectory(_paths.PluginsRoot);
            CopyDirectory(pluginSource, targetDirectory);
            result.ManifestPath = Path.Combine(targetDirectory, "plugin.json");
            WriteInstallManifest(targetDirectory, stageManifest);
            ClearPluginTrustState(stageManifest.PluginId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            result.Message = "Staged plugin was not installed.";
            return result;
        }

        result.Succeeded = true;
        result.Installed = true;
        result.Message = $"Staged plugin {result.PluginId} {result.Version} was installed into the local plugin root. It was not trusted, enabled, allowlisted, or executed.";
        result.Warnings.Add("Review the installed plugin, then explicitly trust, enable, and allowlist actions before use.");
        return result;
    }

    private void ClearPluginTrustState(string pluginId)
    {
        _settings.TrustedPluginIds.RemoveAll(value => value.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        _settings.EnabledPluginIds.RemoveAll(value => value.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        _settings.AllowedPluginActionIds.RemoveAll(value =>
            value.Equals($"{pluginId}:*", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith($"{pluginId}:", StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyStagedInstall(RemotePluginUpdateApplyItem item, bool replaceExisting)
    {
        var install = InstallStagedPackage(item.PluginId, item.StagedVersion, replaceExisting);
        item.InstallMessage = install.Message;
        item.Installed = install.Installed;
        item.InstalledDirectory = install.InstalledDirectory;
        item.ManifestPath = install.ManifestPath;
        item.ReplacedExisting = install.ReplacedExisting;
        item.PackageSha256 = string.IsNullOrWhiteSpace(item.PackageSha256)
            ? install.PackageSha256
            : item.PackageSha256;
        item.Issues.AddRange(install.Issues);
        item.Warnings.AddRange(install.Warnings);
        item.Status = install.Succeeded ? "installed" : "failed";
        item.Message = install.Succeeded
            ? "Staged update was installed into the active plugin root. It was not trusted, enabled, allowlisted, or executed."
            : "Staged update was not installed.";
    }

    private static RemotePluginUpdateSummary BuildUpdateSummary(
        RemotePluginRegistryEntry entry,
        LocalPluginRecord? local,
        RemotePluginStagedPackageSummary? staged,
        EffectiveManagedPolicy policy,
        string goatShotVersion)
    {
        var summary = new RemotePluginUpdateSummary
        {
            PluginId = SafeText(entry.Id),
            Name = SafeText(entry.Name),
            RegistryVersion = SafeText(entry.Version),
            RegistryPackageUri = SensitiveTextDetector.Redact(entry.PackageUri),
            ExpectedSha256 = SafeText(entry.Sha256),
            SizeBytes = entry.SizeBytes,
            ReleaseNotes = SensitiveTextDetector.Redact(entry.ReleaseNotes),
            GoatShotVersion = goatShotVersion,
            MinGoatShotVersion = SafeText(entry.MinGoatShotVersion),
            MaxGoatShotVersion = SafeText(entry.MaxGoatShotVersion),
            Installed = local is not null,
            InstalledVersion = local?.Version ?? string.Empty,
            ActivePluginValid = local?.IsValid == true,
            ActivePluginTrusted = local?.IsTrusted == true,
            ActivePluginEnabled = local?.IsEnabled == true,
            AllowlistedActionCount = local?.Actions.Count(action => action.IsAllowed) ?? 0,
            Staged = staged is not null,
            StagedVersion = staged?.Version ?? string.Empty,
            StagedDirectory = staged?.StageDirectory ?? string.Empty,
            StagedPackagePath = staged?.PackagePath ?? string.Empty,
            StagedPackageSha256 = staged?.PackageSha256 ?? string.Empty,
            StagedAtUtc = staged?.StagedAtUtc,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false
        };

        if (local is not null)
        {
            summary.ActivePluginIssues.AddRange(local.Issues.Select(SafeText));
            summary.OperatorGateReasons.AddRange(BuildOperatorGateReasons(local));
        }

        summary.CompatibilityIssues.AddRange(BuildCompatibilityIssues(entry, goatShotVersion));
        summary.Incompatible = summary.CompatibilityIssues.Count > 0;
        summary.PolicyBlockReasons.AddRange(BuildPolicyBlockReasons(entry, policy));
        summary.PolicyBlocked = summary.PolicyBlockReasons.Count > 0;
        summary.UpdateAvailable = !string.IsNullOrWhiteSpace(summary.InstalledVersion) &&
            CompareVersions(summary.RegistryVersion, summary.InstalledVersion) > 0;
        summary.StagedMatchesRegistry = staged is not null &&
            staged.Version.Equals(entry.Version, StringComparison.OrdinalIgnoreCase);
        summary.Status = ChooseUpdateStatus(summary);
        summary.NextAction = NextActionFor(summary);
        summary.Message = summary.Status switch
        {
            "incompatible" => $"Plugin {summary.PluginId} {summary.RegistryVersion} is incompatible with this Receipts build.",
            "blocked" => $"Plugin {summary.PluginId} is blocked by managed policy.",
            "staged" => $"Plugin {summary.PluginId} {summary.StagedVersion} is staged for operator review. It was not installed, trusted, enabled, allowlisted, or executed.",
            "available" => $"Update available for {summary.PluginId}: {summary.InstalledVersion} -> {summary.RegistryVersion}. No update was installed automatically.",
            "installed" => $"Plugin {summary.PluginId} is installed at {summary.InstalledVersion}; no newer registry version is available.",
            _ => $"Plugin {summary.PluginId} {summary.RegistryVersion} is listed in the registry but is not installed locally."
        };

        return summary;
    }

    private static List<string> BuildCompatibilityIssues(RemotePluginRegistryEntry entry, string appVersion)
    {
        var issues = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.MinGoatShotVersion) &&
            CompareVersions(appVersion, entry.MinGoatShotVersion) < 0)
        {
            issues.Add($"Plugin requires Receipts {entry.MinGoatShotVersion} or newer. Current version is {appVersion}.");
        }

        if (!string.IsNullOrWhiteSpace(entry.MaxGoatShotVersion) &&
            CompareVersions(appVersion, entry.MaxGoatShotVersion) > 0)
        {
            issues.Add($"Plugin supports Receipts up to {entry.MaxGoatShotVersion}. Current version is {appVersion}.");
        }

        return issues;
    }

    private static List<string> BuildPolicyBlockReasons(
        RemotePluginRegistryEntry entry,
        EffectiveManagedPolicy policy)
    {
        var reasons = new List<string>();
        if (policy.DisableLocalPlugins)
        {
            reasons.Add($"Local plugins are disabled by managed policy ({policy.Source}).");
        }

        if (policy.AllowedPluginIds.Count > 0 &&
            !policy.AllowedPluginIds.Any(value => value.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add($"Plugin {SafeText(entry.Id)} is not allowed by managed policy ({policy.Source}).");
        }

        return reasons;
    }

    private static IEnumerable<string> BuildOperatorGateReasons(LocalPluginRecord local)
    {
        if (!local.IsTrusted)
        {
            yield return "Plugin is not trusted.";
        }

        if (!local.IsEnabled)
        {
            yield return "Plugin is not enabled.";
        }

        if (local.Actions.Count > 0 && local.Actions.All(action => !action.IsAllowed))
        {
            yield return "No plugin actions are allowlisted.";
        }
    }

    private Dictionary<string, RemotePluginStagedPackageSummary> DiscoverLatestStagedPackages()
    {
        var staged = new Dictionary<string, RemotePluginStagedPackageSummary>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_paths.PluginStagingRoot))
        {
            return staged;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(_paths.PluginStagingRoot, "stage-manifest.json", SearchOption.AllDirectories))
        {
            if (!IsInsideDirectory(manifestPath, _paths.PluginStagingRoot))
            {
                continue;
            }

            var manifest = TryReadStageManifest(manifestPath);
            if (manifest is null || !IsValidId(manifest.PluginId))
            {
                continue;
            }

            var stageDirectory = Path.GetFullPath(Path.GetDirectoryName(manifestPath) ?? _paths.PluginStagingRoot);
            if (!IsInsideDirectory(stageDirectory, _paths.PluginStagingRoot))
            {
                continue;
            }

            var packagePath = Directory
                .EnumerateFiles(stageDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
            var summary = new RemotePluginStagedPackageSummary
            {
                PluginId = SafeText(manifest.PluginId),
                Version = SafeText(manifest.Version),
                Name = SafeText(manifest.Name),
                StageDirectory = SensitiveTextDetector.Redact(stageDirectory),
                PackagePath = SensitiveTextDetector.Redact(packagePath),
                PackageSha256 = SafeText(manifest.PackageSha256),
                SizeBytes = manifest.SizeBytes,
                StagedAtUtc = manifest.StagedAtUtc,
                WouldTrust = false,
                WouldEnable = false,
                WouldExecute = false
            };

            if (!staged.TryGetValue(summary.PluginId, out var existing) ||
                CompareVersions(summary.Version, existing.Version) > 0)
            {
                staged[summary.PluginId] = summary;
            }
        }

        return staged;
    }

    private static string ChooseUpdateStatus(RemotePluginUpdateSummary summary)
    {
        if (summary.Incompatible)
        {
            return "incompatible";
        }

        if (summary.PolicyBlocked)
        {
            return "blocked";
        }

        if (summary.Staged && (!summary.Installed || summary.UpdateAvailable || summary.StagedMatchesRegistry))
        {
            return "staged";
        }

        if (summary.UpdateAvailable)
        {
            return "available";
        }

        if (summary.Installed)
        {
            return "installed";
        }

        return "not-installed";
    }

    private static string NextActionFor(RemotePluginUpdateSummary summary)
    {
        return summary.Status switch
        {
            "incompatible" => "Do not stage or install until compatibility is resolved.",
            "blocked" => "Review managed policy before staging or installing this plugin.",
            "staged" => "Review the staged package, then run `receipts plugins install-staged` if you choose to activate it locally.",
            "available" => "Run `receipts plugins stage` to download the update for review; no automatic install will occur.",
            "installed" => "No update action is needed.",
            _ => "Run `receipts plugins stage` only if you want to review and install this registry plugin locally."
        };
    }

    private static string BuildUpdateSummaryMessage(RemotePluginUpdateSummaryResult result)
    {
        if (result.Issues.Count > 0)
        {
            return "Remote plugin update summary could not be completed.";
        }

        if (result.AvailableCount > 0 || result.StagedCount > 0 || result.BlockedCount > 0 || result.IncompatibleCount > 0)
        {
            return $"Remote plugin update summary: available={result.AvailableCount}, staged={result.StagedCount}, installed={result.InstalledCount}, blocked={result.BlockedCount}, incompatible={result.IncompatibleCount}. No plugin was installed, trusted, enabled, allowlisted, or executed.";
        }

        return "Remote plugin update summary found no available updates. No plugin was installed, trusted, enabled, allowlisted, or executed.";
    }

    private static string BuildUpdateApplyMessage(RemotePluginUpdateApplyResult result)
    {
        if (result.Issues.Count > 0 || result.FailedCount > 0)
        {
            return $"Remote plugin update apply completed with failures: staged={result.StagedCount}, installed={result.InstalledCount}, skipped={result.SkippedCount}, failed={result.FailedCount}. No plugin was trusted, enabled, allowlisted, or executed.";
        }

        if (result.StagedCount > 0 || result.InstalledCount > 0)
        {
            return $"Remote plugin update apply completed: staged={result.StagedCount}, installed={result.InstalledCount}, skipped={result.SkippedCount}. No plugin was trusted, enabled, allowlisted, or executed.";
        }

        return $"Remote plugin update apply found no applicable updates. skipped={result.SkippedCount}. No plugin was trusted, enabled, allowlisted, or executed.";
    }

    private static RemotePluginMarketplacePlanItem BuildMarketplacePlanItem(
        RemotePluginRegistryEntry entry,
        RemotePluginUpdateSummary update)
    {
        var item = new RemotePluginMarketplacePlanItem
        {
            PluginId = update.PluginId,
            Name = update.Name,
            Description = SafeText(entry.Description),
            Status = update.Status,
            InstalledVersion = update.InstalledVersion,
            RegistryVersion = update.RegistryVersion,
            StagedVersion = update.StagedVersion,
            Installed = update.Installed,
            UpdateAvailable = update.UpdateAvailable,
            Staged = update.Staged,
            StagedMatchesRegistry = update.StagedMatchesRegistry,
            Incompatible = update.Incompatible,
            PolicyBlocked = update.PolicyBlocked,
            ActivePluginTrusted = update.ActivePluginTrusted,
            ActivePluginEnabled = update.ActivePluginEnabled,
            AllowlistedActionCount = update.AllowlistedActionCount,
            RegistryPackageUri = update.RegistryPackageUri,
            ExpectedSha256 = update.ExpectedSha256,
            SizeBytes = update.SizeBytes,
            ReleaseNotes = update.ReleaseNotes,
            MinGoatShotVersion = update.MinGoatShotVersion,
            MaxGoatShotVersion = update.MaxGoatShotVersion,
            StagedDirectory = update.StagedDirectory,
            StagedPackagePath = update.StagedPackagePath,
            StagedPackageSha256 = update.StagedPackageSha256,
            StagedAtUtc = update.StagedAtUtc,
            WouldStagePackage = false,
            WouldInstall = false,
            WouldTrust = false,
            WouldEnable = false,
            WouldAllowlist = false,
            WouldExecute = false,
            Message = update.Message,
            NextAction = update.NextAction
        };
        item.Capabilities.AddRange(entry.Capabilities.Select(SafeText));
        item.Permissions.AddRange(entry.Permissions.Select(SafeText));
        item.CompatibilityIssues.AddRange(update.CompatibilityIssues);
        item.PolicyBlockReasons.AddRange(update.PolicyBlockReasons);
        item.OperatorGateReasons.AddRange(update.OperatorGateReasons);
        item.ActivePluginIssues.AddRange(update.ActivePluginIssues);
        return item;
    }

    private static void AddMarketplaceGovernance(RemotePluginMarketplacePlanResult result)
    {
        result.AuthorityBoundaries.Add("Registry planning is read-only.");
        result.AuthorityBoundaries.Add("Package staging is an explicit later command.");
        result.AuthorityBoundaries.Add("Installing a staged package never implies trust, enablement, allowlisting, or execution.");
        result.AuthorityBoundaries.Add("Managed policy can block local plugins or specific plugin ids.");
        result.AuthorityBoundaries.Add("Hosted marketplace accounts, publication, reviews, ratings, payments, and remote execution are not part of this local planner.");

        result.OperatorGates.Add("Review plugin identity, version, permissions, capabilities, checksum, and compatibility before staging.");
        result.OperatorGates.Add("Review staged package contents before copying into the active local plugin folder.");
        result.OperatorGates.Add("Explicitly trust, enable, and allowlist actions before any plugin can run.");
        result.OperatorGates.Add("Run actions only through the local plugin execution gate.");

        result.PrivacyThreatNotes.Add("Registry URLs, package URLs, release notes, and diagnostics are redacted before reporting.");
        result.PrivacyThreatNotes.Add("No plugin package is downloaded by this planner.");
        result.PrivacyThreatNotes.Add("No plugin code is loaded or executed by this planner.");
        result.PrivacyThreatNotes.Add("Future hosted marketplace identity, account, rating, payment, and analytics flows require a separate privacy review.");

        result.NonGoals.Add("No automatic self-registering updater service and no automatic install/trust/enable/allowlist/execute updates.");
        result.NonGoals.Add("No hosted marketplace service.");
        result.NonGoals.Add("No marketplace accounts, ratings, payments, publication, or review workflow.");
        result.NonGoals.Add("No remote plugin execution.");
        result.NonGoals.Add("No package staging, local installation, trust, enablement, allowlisting, or action execution.");
    }

    private static string BuildMarketplacePlanMessage(RemotePluginMarketplacePlanResult result)
    {
        if (result.Issues.Count > 0)
        {
            return "Plugin marketplace plan could not be completed.";
        }

        return $"Plugin marketplace plan: listed={result.PluginCount}, active={result.ActivePluginCount}, staged={result.StagedPackageCount}, available={result.AvailableCount}, blocked={result.BlockedCount}, incompatible={result.IncompatibleCount}. No plugin was staged, installed, trusted, enabled, allowlisted, updated, published, or executed.";
    }

    private async Task<LoadedRemotePluginRegistry> LoadRegistryAsync(
        string registryLocation,
        CancellationToken cancellationToken)
    {
        var validation = new RemotePluginRegistryValidationResult
        {
            RegistryLocation = SafeText(registryLocation)
        };
        if (string.IsNullOrWhiteSpace(registryLocation))
        {
            validation.Issues.Add("Registry location is required.");
            validation.Message = "Remote plugin registry validation failed.";
            return new LoadedRemotePluginRegistry(null, validation, null, null);
        }

        string json;
        Uri? remoteUri = null;
        string? localPath = null;
        try
        {
            if (TryCreateHttpUri(registryLocation, out remoteUri))
            {
                using var response = await _httpClient.GetAsync(remoteUri, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    validation.Issues.Add($"Registry HTTP request failed with status {(int)response.StatusCode}.");
                    validation.Message = "Remote plugin registry validation failed.";
                    return new LoadedRemotePluginRegistry(null, validation, null, remoteUri);
                }

                json = await response.Content.ReadAsStringAsync(cancellationToken);
                validation.RegistryLocation = SensitiveTextDetector.Redact(remoteUri.ToString());
            }
            else
            {
                localPath = ResolveLocalPath(registryLocation, Directory.GetCurrentDirectory());
                validation.RegistryLocation = localPath;
                json = await File.ReadAllTextAsync(localPath, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            validation.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            validation.Message = "Remote plugin registry validation failed.";
            return new LoadedRemotePluginRegistry(null, validation, localPath, remoteUri);
        }

        RemotePluginRegistryManifest? registry = null;
        try
        {
            registry = JsonSerializer.Deserialize<RemotePluginRegistryManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            validation.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
        }

        if (registry is null)
        {
            validation.Issues.Add("Registry did not contain a JSON object.");
            validation.Message = "Remote plugin registry validation failed.";
            return new LoadedRemotePluginRegistry(null, validation, localPath, remoteUri);
        }

        ValidateRegistry(registry, validation);
        validation.Plugins.AddRange(registry.Plugins.Select(ToSummary));
        validation.PluginCount = validation.Plugins.Count;
        validation.Succeeded = validation.Issues.Count == 0;
        validation.Message = validation.Succeeded
            ? $"Remote plugin registry is valid. {validation.PluginCount} plugin package(s) declared."
            : "Remote plugin registry validation failed.";
        return new LoadedRemotePluginRegistry(registry, validation, localPath, remoteUri);
    }

    private static void ValidateRegistry(
        RemotePluginRegistryManifest registry,
        RemotePluginRegistryValidationResult validation)
    {
        if (!registry.SchemaVersion.Equals(CurrentRegistrySchemaVersion, StringComparison.Ordinal) &&
            !registry.SchemaVersion.Equals(LegacyRegistrySchemaVersion, StringComparison.Ordinal))
        {
            validation.Issues.Add($"Unsupported schemaVersion. Expected {CurrentRegistrySchemaVersion}.");
        }

        if (registry.Plugins.Count == 0)
        {
            validation.Issues.Add("Registry must declare at least one plugin package.");
        }

        var duplicates = registry.Plugins
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin.Id))
            .GroupBy(plugin => plugin.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var duplicate in duplicates)
        {
            validation.Issues.Add($"Duplicate plugin id in registry: {SafeText(duplicate)}.");
        }

        foreach (var entry in registry.Plugins)
        {
            ValidateEntry(entry, validation.Issues, validation.Warnings);
        }
    }

    private static void ValidateEntry(
        RemotePluginRegistryEntry entry,
        List<string> issues,
        List<string> warnings)
    {
        if (!IsValidId(entry.Id))
        {
            issues.Add("Registry plugin id must be 3-64 characters and contain only letters, numbers, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' requires a name.");
        }

        if (string.IsNullOrWhiteSpace(entry.Version))
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' requires a version.");
        }

        if (string.IsNullOrWhiteSpace(entry.PackageUri))
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' requires a packageUri.");
        }

        if (!Sha256Pattern.IsMatch(entry.Sha256.Trim()))
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' requires a 64-character SHA-256 hash.");
        }

        if (entry.SizeBytes is <= 0)
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' sizeBytes must be greater than zero when provided.");
        }

        if (entry.SizeBytes is > DefaultMaxPackageBytes)
        {
            issues.Add($"Registry plugin '{SafeText(entry.Id)}' declares a package over the {DefaultMaxPackageBytes} byte default safety limit.");
        }

        if (entry.Capabilities.Count == 0)
        {
            warnings.Add($"Registry plugin '{SafeText(entry.Id)}' declares no capabilities.");
        }

        if (entry.Permissions.Count == 0)
        {
            warnings.Add($"Registry plugin '{SafeText(entry.Id)}' declares no permissions.");
        }

        if (string.IsNullOrWhiteSpace(entry.Signature))
        {
            warnings.Add($"Registry plugin '{SafeText(entry.Id)}' has no signature metadata; staging is checksum-only.");
        }
        else
        {
            var signature = InspectSignatureMetadata(entry);
            issues.AddRange(signature.Issues);
            warnings.AddRange(signature.Warnings);
        }
    }

    private static RemotePluginSignatureCheck InspectSignatureMetadata(RemotePluginRegistryEntry entry)
    {
        var check = new RemotePluginSignatureCheck();
        if (string.IsNullOrWhiteSpace(entry.Signature))
        {
            check.Warnings.Add($"Registry plugin '{SafeText(entry.Id)}' has no signature metadata; staging is checksum-only.");
            return check;
        }

        check.Signed = true;
        check.Algorithm = NormalizeSignatureAlgorithm(entry.SignatureAlgorithm) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(check.Algorithm))
        {
            check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' signature requires signatureAlgorithm `rsa-pss-sha256` or `rsa-pkcs1-sha256`.");
        }

        if (string.IsNullOrWhiteSpace(entry.SignaturePublicKeyPem))
        {
            check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' signature requires signaturePublicKeyPem.");
        }

        try
        {
            check.SignatureBytes = Convert.FromBase64String(entry.Signature.Trim());
        }
        catch (FormatException)
        {
            check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' signature must be base64-encoded.");
        }

        if (!string.IsNullOrWhiteSpace(entry.SignaturePublicKeyPem))
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(entry.SignaturePublicKeyPem);
                check.PublicKeyFingerprint = ComputePublicKeyFingerprint(rsa);
                check.PublicKeyPem = entry.SignaturePublicKeyPem;
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' signaturePublicKeyPem could not be imported.");
            }
        }

        if (check.Issues.Count == 0)
        {
            check.MetadataValid = true;
            check.Warnings.Add($"Registry plugin '{SafeText(entry.Id)}' package signature will be verified before staging.");
        }

        return check;
    }

    private static RemotePluginSignatureCheck VerifyPackageSignature(
        byte[] packageBytes,
        RemotePluginRegistryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Signature))
        {
            return new RemotePluginSignatureCheck();
        }

        var check = InspectSignatureMetadata(entry);
        if (!check.MetadataValid)
        {
            return check;
        }

        check.Warnings.Clear();
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(check.PublicKeyPem);
            var padding = check.Algorithm.Equals("rsa-pss-sha256", StringComparison.Ordinal)
                ? RSASignaturePadding.Pss
                : RSASignaturePadding.Pkcs1;
            check.Verified = rsa.VerifyData(
                packageBytes,
                check.SignatureBytes,
                HashAlgorithmName.SHA256,
                padding);
            if (!check.Verified)
            {
                check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' package signature verification failed.");
            }
            else
            {
                check.Warnings.Add($"Registry plugin '{SafeText(entry.Id)}' package signature verified with {check.Algorithm}.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            check.Issues.Add($"Registry plugin '{SafeText(entry.Id)}' package signature verification failed.");
        }

        return check;
    }

    private static string? NormalizeSignatureAlgorithm(string? algorithm)
    {
        algorithm = (algorithm ?? string.Empty).Trim().ToLowerInvariant();
        return algorithm switch
        {
            "ps256" or "rsa-pss-sha256" or "rsa-pss" => "rsa-pss-sha256",
            "rs256" or "rsa-sha256" or "rsa-pkcs1-sha256" or "rsa-pkcs1" => "rsa-pkcs1-sha256",
            _ => null
        };
    }

    private static string ComputePublicKeyFingerprint(RSA rsa)
    {
        var subjectPublicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        return Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant();
    }

    private static void ValidateCompatibility(
        RemotePluginRegistryEntry entry,
        RemotePluginPackagePlanResult result)
    {
        var appVersion = GetAppVersion();
        result.GoatShotVersion = appVersion;
        if (!string.IsNullOrWhiteSpace(entry.MinGoatShotVersion) &&
            CompareVersions(appVersion, entry.MinGoatShotVersion) < 0)
        {
            result.Issues.Add($"Plugin requires Receipts {entry.MinGoatShotVersion} or newer. Current version is {appVersion}.");
        }

        if (!string.IsNullOrWhiteSpace(entry.MaxGoatShotVersion) &&
            CompareVersions(appVersion, entry.MaxGoatShotVersion) > 0)
        {
            result.Issues.Add($"Plugin supports Receipts up to {entry.MaxGoatShotVersion}. Current version is {appVersion}.");
        }
    }

    private async Task<RemotePluginPackageBytes> ReadPackageAsync(
        RemotePluginRegistryEntry entry,
        LoadedRemotePluginRegistry registry,
        CancellationToken cancellationToken)
    {
        var packageUri = ResolvePackageUri(entry.PackageUri, registry);
        if (TryCreateHttpUri(packageUri, out var httpUri))
        {
            using var response = await _httpClient.GetAsync(httpUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is not null &&
                response.Content.Headers.ContentLength.Value > _maxPackageBytes)
            {
                throw new InvalidOperationException($"Package content length exceeds {_maxPackageBytes} byte safety limit.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadBoundedAsync(stream, _maxPackageBytes, cancellationToken);
            return new RemotePluginPackageBytes(bytes, SensitiveTextDetector.Redact(httpUri.ToString()));
        }

        var localPath = ResolveLocalPath(packageUri, GetRegistryBaseDirectory(registry));
        var info = new FileInfo(localPath);
        if (info.Length > _maxPackageBytes)
        {
            throw new InvalidOperationException($"Package is over the {_maxPackageBytes} byte safety limit.");
        }

        return new RemotePluginPackageBytes(await File.ReadAllBytesAsync(localPath, cancellationToken), localPath);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes)
            {
                throw new InvalidOperationException($"Package is over the {maxBytes} byte safety limit.");
            }
        }

        return memory.ToArray();
    }

    private static RemotePluginArchiveValidationResult ValidateArchive(
        byte[] packageBytes,
        RemotePluginRegistryEntry entry)
    {
        var result = new RemotePluginArchiveValidationResult();
        try
        {
            using var memory = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            var manifestEntries = new List<ZipArchiveEntry>();
            long declaredSize = 0;
            foreach (var zipEntry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(zipEntry.Name))
                {
                    continue;
                }

                declaredSize += zipEntry.Length;
                if (declaredSize > DefaultMaxPackageBytes)
                {
                    result.Issues.Add($"Archive uncompressed content exceeds the {DefaultMaxPackageBytes} byte safety limit.");
                    break;
                }

                var pathIssue = GetArchivePathIssue(zipEntry.FullName);
                if (!string.IsNullOrWhiteSpace(pathIssue))
                {
                    result.Issues.Add(pathIssue);
                }

                if (zipEntry.FullName.Replace('\\', '/').EndsWith("/plugin.json", StringComparison.OrdinalIgnoreCase) ||
                    zipEntry.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestEntries.Add(zipEntry);
                }
            }

            if (manifestEntries.Count == 0)
            {
                result.Issues.Add("Archive must contain a plugin.json manifest.");
            }
            else if (manifestEntries.Count > 1)
            {
                result.Issues.Add("Archive must contain exactly one plugin.json manifest.");
            }
            else
            {
                ValidatePackagedManifest(manifestEntries.Single(), entry, result);
            }
        }
        catch (InvalidDataException ex)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
        }

        return result;
    }

    private static void ValidatePackagedManifest(
        ZipArchiveEntry manifestEntry,
        RemotePluginRegistryEntry registryEntry,
        RemotePluginArchiveValidationResult result)
    {
        try
        {
            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<LocalPluginManifest>(reader.ReadToEnd(), JsonOptions);
            if (manifest is null)
            {
                result.Issues.Add("Packaged plugin.json did not contain a JSON object.");
                return;
            }

            if (!LocalPluginService.IsSupportedSchemaVersion(manifest.SchemaVersion))
            {
                result.Issues.Add($"Packaged plugin manifest must use {LocalPluginService.CurrentSchemaVersion}; legacy {LocalPluginService.LegacySchemaVersion} is also accepted.");
            }

            if (!manifest.Id.Equals(registryEntry.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add("Packaged plugin id does not match the registry entry.");
            }

            if (!manifest.Version.Equals(registryEntry.Version, StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add("Packaged plugin version does not match the registry entry.");
            }
        }
        catch (JsonException ex)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
        }
    }

    private static string? GetArchivePathIssue(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(path))
        {
            return $"Archive entry has an unsafe absolute path: {SensitiveTextDetector.Redact(path)}.";
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            return $"Archive entry uses unsafe relative path segments: {SensitiveTextDetector.Redact(path)}.";
        }

        return null;
    }

    private static void ExtractArchive(byte[] packageBytes, string extractDirectory)
    {
        using var memory = new MemoryStream(packageBytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var zipEntry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(zipEntry.Name))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(extractDirectory, zipEntry.FullName));
            if (!IsInsideDirectory(targetPath, extractDirectory))
            {
                throw new InvalidOperationException("Archive extraction target left the plugin staging directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            zipEntry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string ResolvePackageUri(string packageUri, LoadedRemotePluginRegistry registry)
    {
        if (TryCreateHttpUri(packageUri, out _) ||
            Uri.TryCreate(packageUri, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile ||
            Path.IsPathFullyQualified(packageUri))
        {
            return packageUri;
        }

        if (registry.RemoteRegistryUri is not null)
        {
            return new Uri(registry.RemoteRegistryUri, packageUri).ToString();
        }

        return Path.Combine(GetRegistryBaseDirectory(registry), packageUri);
    }

    private static string GetRegistryBaseDirectory(LoadedRemotePluginRegistry registry)
    {
        if (!string.IsNullOrWhiteSpace(registry.LocalRegistryPath))
        {
            return Path.GetDirectoryName(registry.LocalRegistryPath) ?? Directory.GetCurrentDirectory();
        }

        return Directory.GetCurrentDirectory();
    }

    private string GetStageDirectory(string pluginId, string version)
    {
        return Path.Combine(_paths.PluginStagingRoot, SafePathSegment(pluginId), SafePathSegment(version));
    }

    private string? ResolveStageDirectory(
        string pluginId,
        string? version,
        RemotePluginActiveInstallResult result)
    {
        var pluginStageRoot = Path.GetFullPath(Path.Combine(_paths.PluginStagingRoot, SafePathSegment(pluginId)));
        if (!IsInsideDirectory(pluginStageRoot, _paths.PluginStagingRoot))
        {
            result.Issues.Add("Resolved staged plugin path was outside the plugin staging root.");
            return null;
        }

        if (!Directory.Exists(pluginStageRoot))
        {
            result.Issues.Add("No staged package folder was present for this plugin.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var requested = Path.GetFullPath(Path.Combine(pluginStageRoot, SafePathSegment(version)));
            if (!IsInsideDirectory(requested, pluginStageRoot))
            {
                result.Issues.Add("Resolved staged plugin version path was outside the plugin staging root.");
                return null;
            }

            if (!Directory.Exists(requested))
            {
                result.Issues.Add("Requested staged plugin version was not found.");
                return null;
            }

            return requested;
        }

        var candidates = Directory
            .EnumerateDirectories(pluginStageRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "stage-manifest.json")))
            .Select(directory => new
            {
                Directory = directory,
                Manifest = TryReadStageManifest(Path.Combine(directory, "stage-manifest.json"))
            })
            .Where(candidate => candidate.Manifest is not null &&
                candidate.Manifest.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Manifest!.Version, VersionStringComparer.Instance)
            .ToList();
        if (candidates.Count == 0)
        {
            result.Issues.Add("No staged package version with a valid stage manifest was found.");
            return null;
        }

        return Path.GetFullPath(candidates.First().Directory);
    }

    private static RemotePluginStageManifest? TryReadStageManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<RemotePluginStageManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? FindPackagedPluginRoot(
        string extractDirectory,
        RemotePluginStageManifest stageManifest,
        RemotePluginActiveInstallResult result)
    {
        var manifests = Directory
            .EnumerateFiles(extractDirectory, "plugin.json", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => IsInsideDirectory(path, extractDirectory))
            .ToList();
        if (manifests.Count == 0)
        {
            result.Issues.Add("Extracted package does not contain plugin.json.");
            return null;
        }

        if (manifests.Count > 1)
        {
            result.Issues.Add("Extracted package contains more than one plugin.json.");
            return null;
        }

        LocalPluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LocalPluginManifest>(
                File.ReadAllText(manifests.Single()),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            return null;
        }

        if (manifest is null)
        {
            result.Issues.Add("Extracted plugin.json did not contain a JSON object.");
            return null;
        }

        if (!LocalPluginService.IsSupportedSchemaVersion(manifest.SchemaVersion))
        {
            result.Issues.Add($"Extracted plugin manifest must use {LocalPluginService.CurrentSchemaVersion}; legacy {LocalPluginService.LegacySchemaVersion} is also accepted.");
        }

        if (!manifest.Id.Equals(stageManifest.PluginId, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Extracted plugin id does not match the staged plugin id.");
        }

        if (!manifest.Version.Equals(stageManifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Extracted plugin version does not match the staged plugin version.");
        }

        return result.Issues.Count == 0
            ? Path.GetDirectoryName(manifests.Single())
            : null;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            var target = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!IsInsideDirectory(target, targetDirectory))
            {
                throw new InvalidOperationException("Plugin install directory copy target left the active plugin directory.");
            }

            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.GetFullPath(Path.Combine(targetDirectory, relative));
            if (!IsInsideDirectory(target, targetDirectory))
            {
                throw new InvalidOperationException("Plugin install file copy target left the active plugin directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WriteInstallManifest(string targetDirectory, RemotePluginStageManifest stageManifest)
    {
        var installManifest = new RemotePluginInstallManifest
        {
            SchemaVersion = CurrentInstallSchemaVersion,
            PluginId = stageManifest.PluginId,
            Version = stageManifest.Version,
            Name = stageManifest.Name,
            RegistryLocation = stageManifest.RegistryLocation,
            PackageUri = stageManifest.PackageUri,
            PackageSha256 = stageManifest.PackageSha256,
            SignatureAlgorithm = stageManifest.SignatureAlgorithm,
            SignatureVerified = stageManifest.SignatureVerified,
            SignaturePublicKeyFingerprint = stageManifest.SignaturePublicKeyFingerprint,
            InstalledAtUtc = DateTimeOffset.UtcNow,
            Trusted = false,
            Enabled = false,
            Executed = false,
            Notes =
            [
                "Installed from a staged package.",
                "Receipts did not trust, enable, allowlist, or execute plugin code during installation.",
                "Operator review is required before any plugin action can run."
            ]
        };
        File.WriteAllText(
            Path.Combine(targetDirectory, "receipts-plugin-install.json"),
            JsonSerializer.Serialize(installManifest, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
    }

    private void ResetStageDirectory(string stageDirectory)
    {
        var full = Path.GetFullPath(stageDirectory);
        if (!IsInsideDirectory(full, _paths.PluginStagingRoot))
        {
            throw new InvalidOperationException("Stage directory resolved outside the plugin staging root.");
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }

        Directory.CreateDirectory(full);
    }

    private static void CopyEntry(RemotePluginRegistryEntry entry, RemotePluginPackagePlanResult result)
    {
        result.PluginId = SafeText(entry.Id);
        result.Version = SafeText(entry.Version);
        result.Name = SafeText(entry.Name);
        result.Description = SafeText(entry.Description);
        result.PackageUri = SensitiveTextDetector.Redact(entry.PackageUri);
        result.ExpectedSha256 = SafeText(entry.Sha256);
        result.SizeBytes = entry.SizeBytes;
        result.Capabilities.AddRange(entry.Capabilities.Select(SafeText));
        result.Permissions.AddRange(entry.Permissions.Select(SafeText));
        result.Signature = SafeText(entry.Signature);
        result.SignatureAlgorithm = NormalizeSignatureAlgorithm(entry.SignatureAlgorithm) ?? SafeText(entry.SignatureAlgorithm);
        result.MinGoatShotVersion = SafeText(entry.MinGoatShotVersion);
        result.MaxGoatShotVersion = SafeText(entry.MaxGoatShotVersion);
    }

    private static RemotePluginRegistryEntrySummary ToSummary(RemotePluginRegistryEntry entry)
    {
        return new RemotePluginRegistryEntrySummary
        {
            Id = SafeText(entry.Id),
            Version = SafeText(entry.Version),
            Name = SafeText(entry.Name),
            Description = SafeText(entry.Description),
            PackageUri = SensitiveTextDetector.Redact(entry.PackageUri),
            Sha256 = SafeText(entry.Sha256),
            SizeBytes = entry.SizeBytes,
            Capabilities = entry.Capabilities.Select(SafeText).ToList(),
            Permissions = entry.Permissions.Select(SafeText).ToList(),
            MinGoatShotVersion = SafeText(entry.MinGoatShotVersion),
            MaxGoatShotVersion = SafeText(entry.MaxGoatShotVersion)
        };
    }

    private static string ResolveLocalPath(string path, string baseDirectory)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(Path.IsPathFullyQualified(expanded)
            ? expanded
            : Path.Combine(baseDirectory, expanded));
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool IsValidId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && IdPattern.IsMatch(value.Trim());
    }

    private static string SafeText(string? value)
    {
        return SensitiveTextDetector.Redact(value?.Trim() ?? string.Empty);
    }

    private static string SafePathSegment(string value)
    {
        var cleaned = new string(value
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_')
            .ToArray())
            .Trim('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "plugin" : cleaned;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAppVersion()
    {
        var version = typeof(RemotePluginPackageService)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version)
            ? typeof(RemotePluginPackageService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : version.Split('+', StringSplitOptions.TrimEntries).FirstOrDefault() ?? version;
    }

    private static int CompareVersions(string? left, string? right)
    {
        var leftParts = VersionParts(left);
        var rightParts = VersionParts(right);
        var count = Math.Max(leftParts.Count, rightParts.Count);
        for (var i = 0; i < count; i++)
        {
            var leftValue = i < leftParts.Count ? leftParts[i] : 0;
            var rightValue = i < rightParts.Count ? rightParts[i] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static List<int> VersionParts(string? value)
    {
        return (value ?? string.Empty)
            .Split('-', '+')
            .FirstOrDefault()?
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var number) ? number : 0)
            .ToList() ?? [];
    }

    private sealed record LoadedRemotePluginRegistry(
        RemotePluginRegistryManifest? Registry,
        RemotePluginRegistryValidationResult Validation,
        string? LocalRegistryPath,
        Uri? RemoteRegistryUri);

    private sealed record RemotePluginPackageBytes(byte[] Bytes, string DisplayUri);

    private sealed class RemotePluginArchiveValidationResult
    {
        public List<string> Issues { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private sealed class RemotePluginSignatureCheck
    {
        public bool Signed { get; set; }
        public bool MetadataValid { get; set; }
        public bool Verified { get; set; }
        public string Algorithm { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
        public string PublicKeyFingerprint { get; set; } = string.Empty;
        public byte[] SignatureBytes { get; set; } = [];
        public List<string> Issues { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static readonly VersionStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            return CompareVersions(x, y);
        }
    }
}

public sealed class RemotePluginRegistryManifest
{
    public string SchemaVersion { get; set; } = RemotePluginPackageService.CurrentRegistrySchemaVersion;
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<RemotePluginRegistryEntry> Plugins { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}

public sealed class RemotePluginRegistryEntry
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string PackageUri { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string SignaturePublicKeyPem { get; set; } = string.Empty;
    public string MinGoatShotVersion { get; set; } = string.Empty;
    public string MaxGoatShotVersion { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();
}

public sealed class RemotePluginRegistryValidationResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public int PluginCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<RemotePluginRegistryEntrySummary> Plugins { get; set; } = new();
}

public sealed class RemotePluginRegistryEntrySummary
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PackageUri { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string MinGoatShotVersion { get; set; } = string.Empty;
    public string MaxGoatShotVersion { get; set; } = string.Empty;
}

public sealed class RemotePluginPackagePlanResult
{
    public bool Succeeded { get; set; }
    public bool Staged { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RegistryLocation { get; set; } = string.Empty;
    public string PackageUri { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string StagingRoot { get; set; } = string.Empty;
    public string StagedPackagePath { get; set; } = string.Empty;
    public string StagedExtractPath { get; set; } = string.Empty;
    public string GoatShotVersion { get; set; } = string.Empty;
    public string MinGoatShotVersion { get; set; } = string.Empty;
    public string MaxGoatShotVersion { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string SignaturePublicKeyFingerprint { get; set; } = string.Empty;
    public bool SignatureVerified { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class RemotePluginUpdateCheckResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<RemotePluginUpdateCandidate> Candidates { get; set; } = new();
}

public sealed class RemotePluginUpdateCandidate
{
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string RegistryVersion { get; set; } = string.Empty;
    public bool UpdateAvailable { get; set; }
    public string RegistryPackageUri { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class RemotePluginUpdateSummaryResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string PluginsRoot { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string GoatShotVersion { get; set; } = string.Empty;
    public int PluginCount { get; set; }
    public int AvailableCount { get; set; }
    public int StagedCount { get; set; }
    public int InstalledCount { get; set; }
    public int BlockedCount { get; set; }
    public int IncompatibleCount { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
    public List<RemotePluginUpdateSummary> Plugins { get; set; } = new();
}

public sealed class RemotePluginUpdateSummary
{
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string RegistryVersion { get; set; } = string.Empty;
    public string StagedVersion { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool Staged { get; set; }
    public bool StagedMatchesRegistry { get; set; }
    public bool Incompatible { get; set; }
    public bool PolicyBlocked { get; set; }
    public bool ActivePluginValid { get; set; }
    public bool ActivePluginTrusted { get; set; }
    public bool ActivePluginEnabled { get; set; }
    public int AllowlistedActionCount { get; set; }
    public string RegistryPackageUri { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string GoatShotVersion { get; set; } = string.Empty;
    public string MinGoatShotVersion { get; set; } = string.Empty;
    public string MaxGoatShotVersion { get; set; } = string.Empty;
    public string StagedDirectory { get; set; } = string.Empty;
    public string StagedPackagePath { get; set; } = string.Empty;
    public string StagedPackageSha256 { get; set; } = string.Empty;
    public DateTimeOffset? StagedAtUtc { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public List<string> CompatibilityIssues { get; set; } = new();
    public List<string> PolicyBlockReasons { get; set; } = new();
    public List<string> OperatorGateReasons { get; set; } = new();
    public List<string> ActivePluginIssues { get; set; } = new();
}

public sealed class RemotePluginUpdateApplyResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string PluginsRoot { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string GoatShotVersion { get; set; } = string.Empty;
    public int PluginCount { get; set; }
    public int StagedCount { get; set; }
    public int InstalledCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public bool InstallStaged { get; set; }
    public bool ReplaceExisting { get; set; }
    public bool WouldStagePackage { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
    public List<RemotePluginUpdateApplyItem> Items { get; set; } = new();
}

public sealed class RemotePluginUpdateApplyItem
{
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string RegistryVersion { get; set; } = string.Empty;
    public string StagedVersion { get; set; } = string.Empty;
    public bool Staged { get; set; }
    public bool Installed { get; set; }
    public bool ReplacedExisting { get; set; }
    public string StagedPackagePath { get; set; } = string.Empty;
    public string StagedDirectory { get; set; } = string.Empty;
    public string InstalledDirectory { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string StageMessage { get; set; } = string.Empty;
    public string InstallMessage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class RemotePluginMarketplacePlanResult
{
    public bool Succeeded { get; set; }
    public string RegistryLocation { get; set; } = string.Empty;
    public string PluginsRoot { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string GoatShotVersion { get; set; } = string.Empty;
    public int PluginCount { get; set; }
    public int ActivePluginCount { get; set; }
    public int StagedPackageCount { get; set; }
    public int ListedOnlyCount { get; set; }
    public int AvailableCount { get; set; }
    public int StagedCount { get; set; }
    public int InstalledCount { get; set; }
    public int BlockedCount { get; set; }
    public int IncompatibleCount { get; set; }
    public bool WouldStagePackage { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public bool WouldAutoUpdate { get; set; }
    public bool WouldPublish { get; set; }
    public bool WouldContactMarketplaceAccount { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> AuthorityBoundaries { get; set; } = new();
    public List<string> OperatorGates { get; set; } = new();
    public List<string> PrivacyThreatNotes { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
    public List<RemotePluginMarketplacePlanItem> Plugins { get; set; } = new();
}

public sealed class RemotePluginMarketplacePlanItem
{
    public string PluginId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string RegistryVersion { get; set; } = string.Empty;
    public string StagedVersion { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool Staged { get; set; }
    public bool StagedMatchesRegistry { get; set; }
    public bool Incompatible { get; set; }
    public bool PolicyBlocked { get; set; }
    public bool ActivePluginTrusted { get; set; }
    public bool ActivePluginEnabled { get; set; }
    public int AllowlistedActionCount { get; set; }
    public string RegistryPackageUri { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string MinGoatShotVersion { get; set; } = string.Empty;
    public string MaxGoatShotVersion { get; set; } = string.Empty;
    public string StagedDirectory { get; set; } = string.Empty;
    public string StagedPackagePath { get; set; } = string.Empty;
    public string StagedPackageSha256 { get; set; } = string.Empty;
    public DateTimeOffset? StagedAtUtc { get; set; }
    public bool WouldStagePackage { get; set; }
    public bool WouldInstall { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldAllowlist { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> CompatibilityIssues { get; set; } = new();
    public List<string> PolicyBlockReasons { get; set; } = new();
    public List<string> OperatorGateReasons { get; set; } = new();
    public List<string> ActivePluginIssues { get; set; } = new();
}

public sealed class RemotePluginStagedPackageSummary
{
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StageDirectory { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset StagedAtUtc { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
}

public sealed class RemotePluginStageRemovalResult
{
    public bool Succeeded { get; set; }
    public bool Removed { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}

public sealed class RemotePluginActiveInstallResult
{
    public bool Succeeded { get; set; }
    public bool Installed { get; set; }
    public bool ReplacedExisting { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string PluginsRoot { get; set; } = string.Empty;
    public string StageDirectory { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string InstalledDirectory { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class RemotePluginInstallManifest
{
    public string SchemaVersion { get; set; } = RemotePluginPackageService.CurrentInstallSchemaVersion;
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegistryLocation { get; set; } = string.Empty;
    public string PackageUri { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public bool SignatureVerified { get; set; }
    public string SignaturePublicKeyFingerprint { get; set; } = string.Empty;
    public DateTimeOffset InstalledAtUtc { get; set; }
    public bool Trusted { get; set; }
    public bool Enabled { get; set; }
    public bool Executed { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class RemotePluginStageManifest
{
    public string SchemaVersion { get; set; } = RemotePluginPackageService.CurrentStageSchemaVersion;
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RegistryLocation { get; set; } = string.Empty;
    public string PackageUri { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public bool SignatureVerified { get; set; }
    public string SignaturePublicKeyFingerprint { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset StagedAtUtc { get; set; }
    public bool WouldTrust { get; set; }
    public bool WouldEnable { get; set; }
    public bool WouldExecute { get; set; }
    public List<string> Notes { get; set; } = new();
}

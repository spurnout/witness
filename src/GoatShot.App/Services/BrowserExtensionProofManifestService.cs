using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionProofManifestService
{
    public const string SchemaVersion = "receipts.browser-proof.v1";
    public const string LegacySchemaVersion = "goatshot.browser-proof.v1";
    public const string ManifestFileName = "browser-proof-manifest.json";
    public const string ValidationFileName = "browser-proof-validation.md";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] RequiredScreenshotRoles =
    {
        "extension-details",
        "popup-consent-defaults",
        "options-consent-defaults",
        "host-status",
        "selected-element-mode",
        "package-export-toggle",
        "last-handoff-result"
    };

    private static readonly HashSet<string> ScreenshotExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    };

    public async Task<BrowserExtensionProofValidationResult> ValidateAsync(
        BrowserExtensionProofValidationRequest request,
        BrowserNativeHostStatus nativeHostStatus,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProofRootPath))
        {
            return BrowserExtensionProofValidationResult.Failed("Browser proof folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ProofRootPath));
        if (!Directory.Exists(root))
        {
            return BrowserExtensionProofValidationResult.Failed($"Browser proof folder was not found: {root}");
        }

        var existingManifest = request.IgnoreExistingManifest
            ? null
            : await ReadExistingManifestAsync(root, cancellationToken);
        var launchPlan = ReadJsonObject(Path.Combine(root, "browser-launch-plan.json"));
        var launchRun = ReadJsonObject(Path.Combine(root, "browser-launch-run.json"));

        var sourceDirectory = ResolveRequestPath(request.ExtensionSourceDirectory) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Extension.SourceDirectory,
                    JsonString(launchPlan, "extensionSourceDirectory"),
                    JsonString(launchRun, "extensionSource")));
        var browser = ResolveFirst(
            request.Browser,
            existingManifest?.Browser.Name,
            JsonString(launchPlan, "browser"),
            JsonString(launchRun, "browser"),
            "unknown");
        var browserVersion = ResolveFirst(
            request.BrowserVersion,
            existingManifest?.Browser.Version,
            JsonString(launchRun, "browserVersion"),
            JsonString(launchRun, "product"),
            ReadFirstExistingText(root, "browser-version.txt", "browser-version.md"));
        var extensionId = ResolveFirst(
            request.ExtensionId,
            existingManifest?.Extension.ExtensionId,
            ReadFirstExistingText(root, "extension-id.txt"));
        var extensionPackagePath = ResolveRequestPath(request.ExtensionPackagePath) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Extension.PackagePath,
                    FindFirst(root, "*.zip")));
        var fixtureUrl = ResolveFirst(
            request.FixtureUrl,
            existingManifest?.FixtureUrl,
            JsonString(launchPlan, "fixtureUrl"),
            JsonString(launchRun, "fixtureUrl"));
        var payloadPath = ResolveRequestPath(request.PayloadPath) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Artifacts.PayloadPath,
                    FindFirst(root, "*payload*.json", excludeNameContains: "redacted")));
        var redactedPayloadPath = ResolveRequestPath(request.RedactedPayloadPath) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Artifacts.RedactedPayloadPath,
                    FindFirst(root, "*redacted*payload*.json")));
        var stitchPackagePath = ResolveRequestPath(request.StitchPackagePath) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Artifacts.StitchPackagePath,
                    FindStitchPackage(root)));
        var importResultPath = ResolveRequestPath(request.ImportResultPath) ??
            ResolvePathOrNull(
                root,
                ResolveFirst(
                    existingManifest?.Artifacts.ImportResultPath,
                    FirstExisting(root, "import-result.json", "live-fixture-verify.json", "stitch-package-receive.json")));

        var manifest = new BrowserExtensionProofManifest
        {
            SchemaVersion = SchemaVersion,
            GeneratedAt = DateTimeOffset.Now,
            ProofRootPath = root,
            Browser = new BrowserExtensionProofBrowser
            {
                Name = browser ?? "unknown",
                Version = browserVersion ?? string.Empty
            },
            Extension = new BrowserExtensionProofExtension
            {
                ExtensionId = extensionId ?? string.Empty,
                SourceDirectory = sourceDirectory ?? string.Empty,
                PackagePath = extensionPackagePath ?? string.Empty
            },
            FixtureUrl = fixtureUrl ?? string.Empty,
            NativeHostStatus = nativeHostStatus,
            Artifacts = new BrowserExtensionProofArtifacts
            {
                PayloadPath = payloadPath ?? string.Empty,
                RedactedPayloadPath = redactedPayloadPath ?? string.Empty,
                StitchPackagePath = stitchPackagePath ?? string.Empty,
                ImportResultPath = importResultPath ?? string.Empty
            },
            Screenshots = CollectScreenshots(root, existingManifest)
        };

        if (Directory.Exists(manifest.Extension.SourceDirectory))
        {
            manifest.Extension.SourceSha256 = await HashDirectoryAsync(manifest.Extension.SourceDirectory, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Extension.PackagePath) &&
            File.Exists(manifest.Extension.PackagePath))
        {
            manifest.Extension.PackageSha256 = await HashFileAsync(manifest.Extension.PackagePath, cancellationToken);
        }

        var issues = new List<string>();
        var warnings = new List<string>();
        var missingEvidence = new List<string>();
        ValidateManifest(manifest, issues, warnings, missingEvidence);
        await ValidateRedactionAsync(manifest, issues, warnings, cancellationToken);

        var manifestPath = Path.Combine(root, ManifestFileName);
        var validationPath = Path.Combine(root, ValidationFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            validationPath,
            BuildValidationMarkdown(manifest, issues, warnings, missingEvidence),
            cancellationToken);

        return new BrowserExtensionProofValidationResult
        {
            Succeeded = issues.Count == 0,
            Message = issues.Count == 0
                ? $"Browser extension proof folder is complete: {root}"
                : $"Browser extension proof folder is incomplete: {root}",
            ProofRootPath = root,
            ManifestPath = manifestPath,
            ValidationPath = validationPath,
            Manifest = manifest,
            MissingEvidence = missingEvidence.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static void ValidateManifest(
        BrowserExtensionProofManifest manifest,
        List<string> issues,
        List<string> warnings,
        List<string> missingEvidence)
    {
        if (string.IsNullOrWhiteSpace(manifest.Browser.Name) ||
            manifest.Browser.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            AddMissing("browser name", "Browser name is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Browser.Version))
        {
            AddMissing("browser version", "Browser version is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Extension.ExtensionId))
        {
            AddMissing("extension id", "Browser-generated extension id is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Extension.SourceDirectory) ||
            !Directory.Exists(manifest.Extension.SourceDirectory))
        {
            AddMissing("extension source", "Extension source folder is missing.", issues, missingEvidence);
        }
        else if (string.IsNullOrWhiteSpace(manifest.Extension.SourceSha256))
        {
            AddMissing("extension source hash", "Extension source hash is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Extension.PackagePath) ||
            !File.Exists(manifest.Extension.PackagePath))
        {
            AddMissing("extension package", "Extension package ZIP/hash is missing.", issues, missingEvidence);
        }
        else if (string.IsNullOrWhiteSpace(manifest.Extension.PackageSha256))
        {
            AddMissing("extension package hash", "Extension package hash is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.FixtureUrl))
        {
            AddMissing("fixture url", "Safe fixture URL is missing.", issues, missingEvidence);
        }

        if (manifest.NativeHostStatus.Registrations.Count == 0)
        {
            AddMissing("native-host status", "Native-host registration status is missing.", issues, missingEvidence);
        }
        else if (manifest.NativeHostStatus.Registrations.All(registration => !registration.Installed))
        {
            AddMissing("native-host registration", "No browser native-host registration is installed.", issues, missingEvidence);
        }

        foreach (var role in RequiredScreenshotRoles)
        {
            if (!manifest.Screenshots.Any(screenshot =>
                    screenshot.Exists &&
                    screenshot.Role.Equals(role, StringComparison.OrdinalIgnoreCase)))
            {
                AddMissing($"screenshot:{role}", $"Required browser proof screenshot is missing: {role}.", issues, missingEvidence);
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.Artifacts.PayloadPath) ||
            !File.Exists(manifest.Artifacts.PayloadPath))
        {
            AddMissing("payload", "Browser capture payload artifact is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Artifacts.RedactedPayloadPath) ||
            !File.Exists(manifest.Artifacts.RedactedPayloadPath))
        {
            AddMissing("redacted payload", "Redacted browser payload artifact is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Artifacts.StitchPackagePath) ||
            !Directory.Exists(manifest.Artifacts.StitchPackagePath))
        {
            AddMissing("stitch package", "Downloaded stitch-package folder is missing.", issues, missingEvidence);
        }

        if (string.IsNullOrWhiteSpace(manifest.Artifacts.ImportResultPath) ||
            !File.Exists(manifest.Artifacts.ImportResultPath))
        {
            AddMissing("import result", "Native stitch-package import result JSON is missing.", issues, missingEvidence);
        }
        else
        {
            ValidateImportResult(manifest.Artifacts.ImportResultPath, issues, warnings);
        }

        if (manifest.Screenshots.Any(screenshot => screenshot.Role.Equals("unclassified", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("Some screenshots were collected but could not be mapped to a required proof role. Rename them with role hints such as host-status or popup-consent-defaults.");
        }
    }

    private static async Task ValidateRedactionAsync(
        BrowserExtensionProofManifest manifest,
        List<string> issues,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Artifacts.PayloadPath) &&
            File.Exists(manifest.Artifacts.PayloadPath))
        {
            var findings = await ScanSensitiveAsync(manifest.Artifacts.PayloadPath, cancellationToken);
            if (findings.Count > 0)
            {
                issues.Add($"Browser payload appears unredacted: {manifest.Artifacts.PayloadPath} ({SensitiveTextDetector.Summarize(findings)}).");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Artifacts.RedactedPayloadPath) &&
            File.Exists(manifest.Artifacts.RedactedPayloadPath))
        {
            var findings = await ScanSensitiveAsync(manifest.Artifacts.RedactedPayloadPath, cancellationToken);
            if (findings.Count > 0)
            {
                issues.Add($"Redacted browser payload still contains sensitive values: {manifest.Artifacts.RedactedPayloadPath} ({SensitiveTextDetector.Summarize(findings)}).");
            }
        }
        else if (!string.IsNullOrWhiteSpace(manifest.Artifacts.PayloadPath) &&
                 File.Exists(manifest.Artifacts.PayloadPath))
        {
            warnings.Add("Payload artifact exists but no separate redacted payload was found.");
        }
    }

    private static void ValidateImportResult(
        string importResultPath,
        List<string> issues,
        List<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(importResultPath));
            if (document.RootElement.TryGetProperty("succeeded", out var succeeded) &&
                succeeded.ValueKind is JsonValueKind.False)
            {
                issues.Add("Native import result is present but reports failure.");
            }
            else if (!document.RootElement.TryGetProperty("succeeded", out _))
            {
                warnings.Add("Native import result does not expose a `succeeded` field; treating file presence as weak evidence.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add($"Native import result JSON could not be read: {SensitiveTextDetector.Redact(ex.Message)}");
        }
    }

    private static async Task<IReadOnlyList<GoatShot.App.Models.SensitiveFinding>> ScanSensitiveAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > 1_000_000)
        {
            return Array.Empty<GoatShot.App.Models.SensitiveFinding>();
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return SensitiveTextDetector.Find(text)
            .Where(finding => !IsLocalNoise(finding))
            .ToList();
    }

    private static bool IsLocalNoise(GoatShot.App.Models.SensitiveFinding finding) =>
        finding.Kind.Equals("IP address", StringComparison.OrdinalIgnoreCase) &&
        (finding.Value.StartsWith("127.", StringComparison.Ordinal) ||
         finding.Value.Equals("0.0.0.0", StringComparison.Ordinal));

    private static void AddMissing(
        string evidence,
        string issue,
        List<string> issues,
        List<string> missingEvidence)
    {
        missingEvidence.Add(evidence);
        issues.Add(issue);
    }

    private static List<BrowserExtensionProofScreenshot> CollectScreenshots(
        string root,
        BrowserExtensionProofManifest? existingManifest)
    {
        var screenshots = new Dictionary<string, BrowserExtensionProofScreenshot>(StringComparer.OrdinalIgnoreCase);
        if (existingManifest is not null)
        {
            foreach (var screenshot in existingManifest.Screenshots)
            {
                var path = ResolvePath(root, screenshot.Path);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (screenshot.Role.Equals("unclassified", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                screenshots[path] = new BrowserExtensionProofScreenshot
                {
                    Role = string.IsNullOrWhiteSpace(screenshot.Role) ? InferScreenshotRole(path) : screenshot.Role,
                    Path = path,
                    Exists = File.Exists(path)
                };
            }
        }

        foreach (var path in AutoDiscoverScreenshotFiles(root)
                     .Where(path => ScreenshotExtensions.Contains(Path.GetExtension(path))))
        {
            screenshots[path] = new BrowserExtensionProofScreenshot
            {
                Role = InferScreenshotRole(path),
                Path = path,
                Exists = File.Exists(path)
            };
        }

        return screenshots.Values
            .OrderBy(screenshot => screenshot.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(screenshot => screenshot.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> AutoDiscoverScreenshotFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }

        foreach (var directoryName in new[] { "screenshots", "browser-screenshots" })
        {
            var directory = Path.Combine(root, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                yield return path;
            }
        }
    }

    private static string InferScreenshotRole(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant()
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
        if (name.Contains("extension-detail", StringComparison.Ordinal) ||
            name.Contains("details-page", StringComparison.Ordinal))
        {
            return "extension-details";
        }

        if (name.Contains("popup", StringComparison.Ordinal) &&
            (name.Contains("consent", StringComparison.Ordinal) ||
             name.Contains("default", StringComparison.Ordinal)))
        {
            return "popup-consent-defaults";
        }

        if (name.Contains("option", StringComparison.Ordinal) &&
            (name.Contains("consent", StringComparison.Ordinal) ||
             name.Contains("default", StringComparison.Ordinal)))
        {
            return "options-consent-defaults";
        }

        if (name.Contains("host-status", StringComparison.Ordinal) ||
            name.Contains("native-host", StringComparison.Ordinal))
        {
            return "host-status";
        }

        if (name.Contains("selected-element", StringComparison.Ordinal) ||
            name.Contains("element-mode", StringComparison.Ordinal))
        {
            return "selected-element-mode";
        }

        if (name.Contains("package-export", StringComparison.Ordinal) ||
            name.Contains("export-toggle", StringComparison.Ordinal))
        {
            return "package-export-toggle";
        }

        if (name.Contains("handoff", StringComparison.Ordinal) ||
            name.Contains("last-result", StringComparison.Ordinal) ||
            name.Contains("import-result", StringComparison.Ordinal))
        {
            return "last-handoff-result";
        }

        if (name.Contains("safe-fixture", StringComparison.Ordinal) ||
            name.Contains("fixture-page", StringComparison.Ordinal))
        {
            return "fixture-page";
        }

        return "unclassified";
    }

    private static async Task<BrowserExtensionProofManifest?> ReadExistingManifestAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<BrowserExtensionProofManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? ReadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? JsonString(JsonElement? element, string propertyName)
    {
        if (element is null ||
            element.Value.ValueKind != JsonValueKind.Object ||
            !element.Value.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ResolveFirst(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string ResolvePath(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathFullyQualified(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(root, expanded));
    }

    private static string? ResolveRequestPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string? ResolvePathOrNull(string root, string? path)
    {
        var resolved = ResolvePath(root, path);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    private static string? FirstExisting(string root, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ReadFirstExistingText(string root, params string[] names)
    {
        var path = FirstExisting(root, names);
        if (path is null)
        {
            return null;
        }

        try
        {
            return File.ReadLines(path).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFirst(string root, string pattern, string? excludeNameContains = null)
    {
        var files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => excludeNameContains is null ||
                           !Path.GetFileName(path).Contains(excludeNameContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return files.FirstOrDefault();
    }

    private static string? FindStitchPackage(string root)
    {
        var manifest = new[]
            {
                "receipts-stitch-package.json",
                "stitch-package.json",
                "goatshot-stitch-package.json"
            }
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault();
        return manifest is null ? null : Path.GetDirectoryName(manifest);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> HashDirectoryAsync(string root, CancellationToken cancellationToken)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => !IsIgnoredSourcePath(root, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path)
                .Replace('\\', '/')
                .ToLowerInvariant();
            incremental.AppendData(Encoding.UTF8.GetBytes(relative));
            incremental.AppendData(new byte[] { 0 });
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            incremental.AppendData(bytes);
            incremental.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(incremental.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsIgnoredSourcePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path)
            .Replace('\\', '/');
        return relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildValidationMarkdown(
        BrowserExtensionProofManifest manifest,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> missingEvidence)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Browser Extension Proof Validation");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {manifest.GeneratedAt:O}");
        builder.AppendLine($"Proof root: `{manifest.ProofRootPath}`");
        builder.AppendLine($"Status: `{(issues.Count == 0 ? "complete" : "incomplete")}`");
        builder.AppendLine();
        builder.AppendLine("## Proof Boundary");
        builder.AppendLine();
        builder.AppendLine("This validates local safe-fixture evidence only. It does not prove browser-store publication, review, signing, availability, or automatic extension installation.");
        builder.AppendLine();
        AppendList(builder, "Missing Evidence", missingEvidence);
        AppendList(builder, "Issues", issues);
        AppendList(builder, "Warnings", warnings);
        builder.AppendLine("## Recorded Evidence");
        builder.AppendLine();
        builder.AppendLine($"- Browser: `{manifest.Browser.Name}`");
        builder.AppendLine($"- Browser version: `{Empty(manifest.Browser.Version)}`");
        builder.AppendLine($"- Extension id: `{Empty(manifest.Extension.ExtensionId)}`");
        builder.AppendLine($"- Extension source: `{Empty(manifest.Extension.SourceDirectory)}`");
        builder.AppendLine($"- Extension source SHA-256: `{Empty(manifest.Extension.SourceSha256)}`");
        builder.AppendLine($"- Extension package: `{Empty(manifest.Extension.PackagePath)}`");
        builder.AppendLine($"- Extension package SHA-256: `{Empty(manifest.Extension.PackageSha256)}`");
        builder.AppendLine($"- Fixture URL: `{Empty(manifest.FixtureUrl)}`");
        builder.AppendLine($"- Payload: `{Empty(manifest.Artifacts.PayloadPath)}`");
        builder.AppendLine($"- Redacted payload: `{Empty(manifest.Artifacts.RedactedPayloadPath)}`");
        builder.AppendLine($"- Stitch package: `{Empty(manifest.Artifacts.StitchPackagePath)}`");
        builder.AppendLine($"- Import result: `{Empty(manifest.Artifacts.ImportResultPath)}`");
        builder.AppendLine();
        builder.AppendLine("## Screenshots");
        builder.AppendLine();
        if (manifest.Screenshots.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var screenshot in manifest.Screenshots)
            {
                builder.AppendLine($"- `{screenshot.Role}`: `{screenshot.Path}`");
            }
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
        }
        else
        {
            foreach (var value in values)
            {
                builder.AppendLine($"- {value}");
            }
        }

        builder.AppendLine();
    }

    private static string Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;
}

public sealed class BrowserExtensionProofValidationRequest
{
    public string ProofRootPath { get; set; } = string.Empty;
    public bool IgnoreExistingManifest { get; set; }
    public string? ExtensionSourceDirectory { get; set; }
    public string? ExtensionPackagePath { get; set; }
    public string? Browser { get; set; }
    public string? BrowserVersion { get; set; }
    public string? ExtensionId { get; set; }
    public string? FixtureUrl { get; set; }
    public string? PayloadPath { get; set; }
    public string? RedactedPayloadPath { get; set; }
    public string? StitchPackagePath { get; set; }
    public string? ImportResultPath { get; set; }
}

public sealed class BrowserExtensionProofValidationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProofRootPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ValidationPath { get; set; } = string.Empty;
    public BrowserExtensionProofManifest Manifest { get; set; } = new();
    public List<string> MissingEvidence { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static BrowserExtensionProofValidationResult Failed(string message)
    {
        return new BrowserExtensionProofValidationResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = new List<string> { SensitiveTextDetector.Redact(message) }
        };
    }
}

public sealed class BrowserExtensionProofManifest
{
    public string SchemaVersion { get; set; } = BrowserExtensionProofManifestService.SchemaVersion;
    public DateTimeOffset GeneratedAt { get; set; }
    public string ProofRootPath { get; set; } = string.Empty;
    public BrowserExtensionProofBrowser Browser { get; set; } = new();
    public BrowserExtensionProofExtension Extension { get; set; } = new();
    public string FixtureUrl { get; set; } = string.Empty;
    public BrowserNativeHostStatus NativeHostStatus { get; set; } = new();
    public BrowserExtensionProofArtifacts Artifacts { get; set; } = new();
    public List<BrowserExtensionProofScreenshot> Screenshots { get; set; } = new();
}

public sealed class BrowserExtensionProofBrowser
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public sealed class BrowserExtensionProofExtension
{
    public string ExtensionId { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
}

public sealed class BrowserExtensionProofArtifacts
{
    public string PayloadPath { get; set; } = string.Empty;
    public string RedactedPayloadPath { get; set; } = string.Empty;
    public string StitchPackagePath { get; set; } = string.Empty;
    public string ImportResultPath { get; set; } = string.Empty;
}

public sealed class BrowserExtensionProofScreenshot
{
    public string Role { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Exists { get; set; }
}

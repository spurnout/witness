using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionStitchPackageService
{
    public const string CurrentSchemaVersion = "receipts.browser-stitch-package.v1";
    public const string LegacySchemaVersion = "goatshot.browser-stitch-package.v1";
    public const long DefaultMaximumStitchedImageBytes = 250L * 1024 * 1024;
    public const long DefaultMaximumTileBytes = 50L * 1024 * 1024;
    public const long DefaultMaximumTotalTileBytes = 500L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    public BrowserExtensionStitchPackageValidationResult Validate(
        string packagePath,
        string? expectedCorrelationId = null)
    {
        var result = new BrowserExtensionStitchPackageValidationResult();
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            result.Issues.Add("Stitch package path is required.");
            result.Message = "Browser stitch package validation failed.";
            return result;
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(packagePath));
        var manifestPath = ResolveManifestPath(fullPath, result);
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            result.Message = "Browser stitch package validation failed.";
            return result;
        }

        var packageRoot = Path.GetDirectoryName(manifestPath)!;
        result.PackageRoot = packageRoot;
        result.ManifestPath = manifestPath;

        BrowserExtensionStitchPackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BrowserExtensionStitchPackageManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            result.Issues.Add($"Stitch package manifest could not be read: {SensitiveTextDetector.Redact(ex.Message)}");
            result.Message = "Browser stitch package validation failed.";
            return result;
        }

        if (manifest is null)
        {
            result.Issues.Add("Stitch package manifest was empty.");
            result.Message = "Browser stitch package validation failed.";
            return result;
        }

        result.Manifest = manifest;
        ValidateManifestFields(manifest, expectedCorrelationId, result);
        ValidateStitchedImage(packageRoot, manifest, result);
        ValidateTiles(packageRoot, manifest, result);

        result.Message = result.IsValid
            ? "Browser stitch package is valid."
            : "Browser stitch package validation failed.";
        return result;
    }

    private static string? ResolveManifestPath(
        string fullPath,
        BrowserExtensionStitchPackageValidationResult result)
    {
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            result.Issues.Add($"Stitch package path was not found: {fullPath}");
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(fullPath, "receipts-stitch-package.json"),
            Path.Combine(fullPath, "stitch-package.json"),
            Path.Combine(fullPath, "goatshot-stitch-package.json"),
        };
        var manifestPath = candidates.FirstOrDefault(File.Exists);
        if (manifestPath is null)
        {
            result.Issues.Add("Stitch package directory must contain receipts-stitch-package.json or stitch-package.json (legacy goatshot-stitch-package.json is also accepted).");
        }

        return manifestPath;
    }

    private static void ValidateManifestFields(
        BrowserExtensionStitchPackageManifest manifest,
        string? expectedCorrelationId,
        BrowserExtensionStitchPackageValidationResult result)
    {
        if (!manifest.SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal) &&
            !manifest.SchemaVersion.Equals(LegacySchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add($"Unsupported stitch package schemaVersion. Expected {CurrentSchemaVersion}; legacy {LegacySchemaVersion} is also accepted.");
        }

        if (!string.IsNullOrWhiteSpace(expectedCorrelationId) &&
            !string.IsNullOrWhiteSpace(manifest.CorrelationId) &&
            !manifest.CorrelationId.Equals(expectedCorrelationId, StringComparison.Ordinal))
        {
            result.Issues.Add("Stitch package correlationId does not match the browser payload correlationId.");
        }

        if (string.IsNullOrWhiteSpace(manifest.StitchedImagePath))
        {
            result.Issues.Add("Stitch package must declare stitchedImagePath.");
        }
    }

    private static void ValidateStitchedImage(
        string packageRoot,
        BrowserExtensionStitchPackageManifest manifest,
        BrowserExtensionStitchPackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.StitchedImagePath))
        {
            return;
        }

        if (!TryResolveInsidePackage(packageRoot, manifest.StitchedImagePath, out var imagePath))
        {
            result.Issues.Add("Stitched image path must stay inside the stitch package directory.");
            return;
        }

        result.StitchedImagePath = imagePath;
        if (!File.Exists(imagePath))
        {
            result.Issues.Add($"Stitched image file was not found: {manifest.StitchedImagePath}");
            return;
        }

        if (!SupportedImageExtensions.Contains(Path.GetExtension(imagePath)))
        {
            result.Issues.Add($"Unsupported stitched image extension: {Path.GetExtension(imagePath)}.");
        }

        var length = new FileInfo(imagePath).Length;
        result.StitchedImageBytes = length;
        if (length <= 0)
        {
            result.Issues.Add("Stitched image file is empty.");
        }
        else if (length > DefaultMaximumStitchedImageBytes)
        {
            result.Issues.Add($"Stitched image exceeds the {DefaultMaximumStitchedImageBytes} byte package limit.");
        }
    }

    private static void ValidateTiles(
        string packageRoot,
        BrowserExtensionStitchPackageManifest manifest,
        BrowserExtensionStitchPackageValidationResult result)
    {
        long totalTileBytes = 0;
        for (var i = 0; i < manifest.Tiles.Count; i++)
        {
            var tile = manifest.Tiles[i];
            if (tile.Index != i)
            {
                result.Warnings.Add("Stitch package tile indexes are not sequential.");
            }

            if (string.IsNullOrWhiteSpace(tile.Path))
            {
                result.Warnings.Add($"Stitch package tile {i} does not declare a path.");
                continue;
            }

            if (!TryResolveInsidePackage(packageRoot, tile.Path, out var tilePath))
            {
                result.Issues.Add($"Stitch package tile path must stay inside the package directory: {tile.Path}");
                continue;
            }

            if (!File.Exists(tilePath))
            {
                result.Issues.Add($"Stitch package tile was not found: {tile.Path}");
                continue;
            }

            if (!SupportedImageExtensions.Contains(Path.GetExtension(tilePath)))
            {
                result.Issues.Add($"Unsupported stitch package tile extension: {Path.GetExtension(tilePath)}.");
            }

            var length = new FileInfo(tilePath).Length;
            totalTileBytes += length;
            if (length <= 0)
            {
                result.Issues.Add($"Stitch package tile is empty: {tile.Path}");
            }
            else if (length > DefaultMaximumTileBytes)
            {
                result.Issues.Add($"Stitch package tile exceeds the {DefaultMaximumTileBytes} byte per-tile limit: {tile.Path}");
            }
        }

        result.TotalTileBytes = totalTileBytes;
        if (totalTileBytes > DefaultMaximumTotalTileBytes)
        {
            result.Issues.Add($"Stitch package tiles exceed the {DefaultMaximumTotalTileBytes} byte total tile limit.");
        }
    }

    private static bool TryResolveInsidePackage(
        string packageRoot,
        string relativeOrAbsolutePath,
        out string resolved)
    {
        resolved = string.Empty;
        var candidate = Path.IsPathFullyQualified(relativeOrAbsolutePath)
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(relativeOrAbsolutePath))
            : Path.GetFullPath(Path.Combine(packageRoot, relativeOrAbsolutePath));
        var rootWithSeparator = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.Equals(packageRoot, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }
}

public sealed class BrowserExtensionStitchPackageManifest
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string StitchedImagePath { get; set; } = string.Empty;
    public List<BrowserExtensionStitchPackageTile> Tiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class BrowserExtensionStitchPackageTile
{
    public int Index { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string CaptureState { get; set; } = string.Empty;
}

public sealed class BrowserExtensionStitchPackageValidationResult
{
    public string Message { get; set; } = string.Empty;
    public string PackageRoot { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string StitchedImagePath { get; set; } = string.Empty;
    public long StitchedImageBytes { get; set; }
    public long TotalTileBytes { get; set; }
    public BrowserExtensionStitchPackageManifest? Manifest { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool IsValid => Issues.Count == 0;
}

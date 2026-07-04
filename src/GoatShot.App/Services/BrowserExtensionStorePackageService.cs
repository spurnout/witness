using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionStorePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<BrowserExtensionStorePackageResult> CreateAsync(
        BrowserExtensionStorePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = string.IsNullOrWhiteSpace(request.ExtensionSourceDirectory)
            ? Path.GetFullPath("browser-extension")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ExtensionSourceDirectory));
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "browser-extension-store-package"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var result = new BrowserExtensionStorePackageResult
        {
            SourceDirectory = source,
            OutputPath = outputRoot,
            SupportUrl = SensitiveTextDetector.Redact(request.SupportUrl ?? string.Empty),
            PrivacyUrl = SensitiveTextDetector.Redact(request.PrivacyUrl ?? string.Empty),
            ReleaseNotes = SensitiveTextDetector.Redact(await ResolveReleaseNotesAsync(request, cancellationToken))
        };

        Directory.CreateDirectory(outputRoot);
        var targets = NormalizeTargets(request.Target);
        if (targets.Count == 0)
        {
            result.Issues.Add("Store-package target must be one of: all, chrome, edge, firefox.");
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetResult = await CreateTargetPackageAsync(
                source,
                outputRoot,
                target,
                request,
                result.ReleaseNotes,
                cancellationToken);
            result.Targets.Add(targetResult);
            result.GeneratedFiles.AddRange(targetResult.GeneratedFiles);
        }

        result.ManualEvidenceRequired.AddRange(BuildManualEvidenceRequired());
        result.PublicationNonGoals.AddRange(BuildPublicationNonGoals());
        result.Succeeded = result.Issues.Count == 0 &&
            result.Targets.Count > 0 &&
            result.Targets.All(target => target.Issues.Count == 0);
        result.Message = result.Succeeded
            ? $"Browser extension store-package artifacts created: {outputRoot}"
            : $"Browser extension store-package artifacts created with issues: {outputRoot}";
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "store-package-summary.md",
            BuildSummaryMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "store-package-summary.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static async Task<BrowserExtensionStorePackageTargetResult> CreateTargetPackageAsync(
        string source,
        string outputRoot,
        BrowserExtensionStorePackageTarget target,
        BrowserExtensionStorePackageRequest request,
        string releaseNotes,
        CancellationToken cancellationToken)
    {
        var targetName = target.ToString().ToLowerInvariant();
        var targetRoot = Path.Combine(outputRoot, targetName);
        Directory.CreateDirectory(targetRoot);

        var readinessOutput = Path.Combine(targetRoot, "readiness");
        var readiness = await new BrowserExtensionStoreReadinessService().CreateAsync(new BrowserExtensionStoreReadinessRequest
        {
            ExtensionSourceDirectory = source,
            OutputPath = readinessOutput,
            Target = targetName,
            SupportUrl = request.SupportUrl,
            PrivacyUrl = request.PrivacyUrl
        }, cancellationToken);

        var version = string.IsNullOrWhiteSpace(readiness.Manifest.Version)
            ? "unknown"
            : SafeFileToken(readiness.Manifest.Version);
        var extensionPackagePath = Path.Combine(targetRoot, $"goatshot-browser-extension-{targetName}-v{version}.zip");
        var package = await new BrowserExtensionPackageService().PackageAsync(
            source,
            extensionPackagePath,
            cancellationToken);

        var targetResult = new BrowserExtensionStorePackageTargetResult
        {
            Target = targetName,
            TargetFolder = targetRoot,
            ReadinessOutputPath = readinessOutput,
            ExtensionPackagePath = extensionPackagePath,
            ManifestName = readiness.Manifest.Name,
            ManifestVersion = readiness.Manifest.Version,
            SupportUrl = SensitiveTextDetector.Redact(request.SupportUrl ?? string.Empty),
            PrivacyUrl = SensitiveTextDetector.Redact(request.PrivacyUrl ?? string.Empty),
            ReleaseNotes = releaseNotes
        };

        targetResult.GeneratedFiles.AddRange(readiness.GeneratedFiles);
        targetResult.Warnings.AddRange(readiness.Warnings);
        foreach (var readinessTarget in readiness.Targets)
        {
            targetResult.ReadinessStatus = readinessTarget.Status;
            targetResult.Issues.AddRange(readinessTarget.Issues);
            targetResult.Warnings.AddRange(readinessTarget.Warnings);
        }

        targetResult.Issues.AddRange(readiness.Issues);
        targetResult.Issues.AddRange(package.Issues);
        if (!package.Succeeded)
        {
            targetResult.Issues.Add(package.Message);
        }

        if (package.Succeeded)
        {
            var packageInfo = new FileInfo(extensionPackagePath);
            targetResult.ExtensionPackageBytes = packageInfo.Length;
            targetResult.ExtensionPackageSha256 = await Sha256FileAsync(extensionPackagePath, cancellationToken);
            targetResult.SourceFileHashes.AddRange(await HashSourceFilesAsync(
                source,
                package.IncludedFiles,
                cancellationToken));
            targetResult.GeneratedFiles.Add(extensionPackagePath);
        }

        targetResult.Issues.AddRange(ValidateStoreMetadata(readiness, request, releaseNotes));
        targetResult.ManualEvidenceRequired.AddRange(BuildManualEvidenceRequired());
        targetResult.PublicationNonGoals.AddRange(BuildPublicationNonGoals());
        targetResult.Status = targetResult.Issues.Count == 0
            ? "store-package-created-manual-submission-required"
            : "blocked";

        // Build the submission bundle from the target folder BEFORE writing the
        // submission manifest/checklist. Those two files describe the bundle including
        // its own hash and size, which a zip cannot honestly contain about itself, so
        // they are written as authoritative sibling files on disk rather than embedded
        // as a stale, self-contradicting copy inside the zip.
        var submissionBundlePath = Path.Combine(outputRoot, $"goatshot-browser-extension-{targetName}-store-submission.zip");
        if (File.Exists(submissionBundlePath))
        {
            File.Delete(submissionBundlePath);
        }

        ZipFile.CreateFromDirectory(targetRoot, submissionBundlePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        targetResult.SubmissionBundlePath = submissionBundlePath;
        targetResult.SubmissionBundleBytes = new FileInfo(submissionBundlePath).Length;
        targetResult.SubmissionBundleSha256 = await Sha256FileAsync(submissionBundlePath, cancellationToken);
        targetResult.GeneratedFiles.Add(submissionBundlePath);

        var submissionManifestPath = Path.Combine(targetRoot, "store-submission-manifest.json");
        var submissionChecklistPath = Path.Combine(targetRoot, "store-submission-checklist.md");
        targetResult.SubmissionManifestPath = submissionManifestPath;
        targetResult.GeneratedFiles.Add(submissionManifestPath);
        targetResult.GeneratedFiles.Add(submissionChecklistPath);

        await WriteFileAsync(
            targetRoot,
            "store-submission-manifest.json",
            JsonSerializer.Serialize(targetResult, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await WriteFileAsync(
            targetRoot,
            "store-submission-checklist.md",
            BuildTargetChecklistMarkdown(targetResult),
            cancellationToken);

        return targetResult;
    }

    private static IReadOnlyList<string> ValidateStoreMetadata(
        BrowserExtensionStoreReadinessResult readiness,
        BrowserExtensionStorePackageRequest request,
        string releaseNotes)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(readiness.Manifest.Name))
        {
            issues.Add("Extension manifest name is required for store submission packaging.");
        }

        if (string.IsNullOrWhiteSpace(readiness.Manifest.Version))
        {
            issues.Add("Extension manifest version is required for store submission packaging.");
        }

        if (!readiness.Manifest.Permissions.Contains("nativeMessaging", StringComparer.Ordinal))
        {
            issues.Add("nativeMessaging permission and native-host disclosure are required.");
        }

        if (!readiness.PermissionRationales.Any(rationale =>
                rationale.Permission.Equals("nativeMessaging", StringComparison.Ordinal)))
        {
            issues.Add("Native-host permission rationale must be generated.");
        }

        if (string.IsNullOrWhiteSpace(request.SupportUrl))
        {
            issues.Add("Support URL or reviewed support placeholder is required for store submission packaging.");
        }

        if (string.IsNullOrWhiteSpace(request.PrivacyUrl))
        {
            issues.Add("Privacy URL or reviewed privacy placeholder is required for store submission packaging.");
        }

        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            issues.Add("Release notes or a release-notes placeholder are required for store submission packaging.");
        }

        if (!readiness.GeneratedFiles.Any(file =>
                file.EndsWith("privacy-data-use.md", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Privacy/data-use copy must be generated.");
        }

        if (!readiness.GeneratedFiles.Any(file =>
                file.EndsWith("screenshots-checklist.md", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("Screenshot checklist must be generated.");
        }

        return issues;
    }

    private static async Task<string> ResolveReleaseNotesAsync(
        BrowserExtensionStorePackageRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ReleaseNotes))
        {
            return request.ReleaseNotes;
        }

        if (!string.IsNullOrWhiteSpace(request.ReleaseNotesPath))
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.ReleaseNotesPath));
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path, cancellationToken);
            }
        }

        return string.Empty;
    }

    private static List<BrowserExtensionStorePackageTarget> NormalizeTargets(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return new List<BrowserExtensionStorePackageTarget>
            {
                BrowserExtensionStorePackageTarget.Chrome,
                BrowserExtensionStorePackageTarget.Edge,
                BrowserExtensionStorePackageTarget.Firefox
            };
        }

        var targets = new List<BrowserExtensionStorePackageTarget>();
        foreach (var part in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<BrowserExtensionStorePackageTarget>(part, ignoreCase: true, out var parsed))
            {
                targets.Add(parsed);
            }
        }

        return targets.Distinct().ToList();
    }

    private static async Task<List<BrowserExtensionStorePackageFileHash>> HashSourceFilesAsync(
        string source,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var hashes = new List<BrowserExtensionStorePackageFileHash>();
        foreach (var relativePath in relativePaths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(source, relativePath);
            hashes.Add(new BrowserExtensionStorePackageFileHash
            {
                Path = relativePath.Replace('\\', '/'),
                Sha256 = await Sha256FileAsync(fullPath, cancellationToken),
                Bytes = new FileInfo(fullPath).Length
            });
        }

        return hashes;
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

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

    private static string BuildSummaryMarkdown(BrowserExtensionStorePackageResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Browser Extension Store Package Summary");
        builder.AppendLine();
        builder.AppendLine($"Source: `{result.SourceDirectory}`");
        builder.AppendLine($"Output: `{result.OutputPath}`");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        foreach (var nonGoal in BuildPublicationNonGoals())
        {
            builder.AppendLine($"- {nonGoal}");
        }

        builder.AppendLine();
        builder.AppendLine("## Targets");
        builder.AppendLine();
        foreach (var target in result.Targets)
        {
            builder.AppendLine($"### {target.Target}");
            builder.AppendLine();
            builder.AppendLine($"Status: `{target.Status}`");
            builder.AppendLine($"Extension package: `{target.ExtensionPackagePath}`");
            builder.AppendLine($"Submission bundle: `{target.SubmissionBundlePath}`");
            builder.AppendLine($"Manifest: `{target.SubmissionManifestPath}`");
            AppendList(builder, "Issues", target.Issues, 4);
            AppendList(builder, "Warnings", target.Warnings, 4);
            builder.AppendLine();
        }

        AppendList(builder, "Manual Evidence Required", result.ManualEvidenceRequired);
        return builder.ToString();
    }

    private static string BuildTargetChecklistMarkdown(BrowserExtensionStorePackageTargetResult target)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# GoatShot {target.Target} Store Submission Package");
        builder.AppendLine();
        builder.AppendLine($"Status: `{target.Status}`");
        builder.AppendLine();
        builder.AppendLine("This output prepares a local package for manual submission review. It is not publication, signing, approval, or automatic-install proof.");
        builder.AppendLine();
        builder.AppendLine("## Generated Package");
        builder.AppendLine();
        builder.AppendLine($"- Extension package: `{target.ExtensionPackagePath}`");
        builder.AppendLine($"- Extension package SHA-256: `{target.ExtensionPackageSha256}`");
        builder.AppendLine($"- Submission bundle: `{target.SubmissionBundlePath}`");
        builder.AppendLine($"- Submission bundle SHA-256: `{target.SubmissionBundleSha256}`");
        builder.AppendLine($"- Manifest: `{target.SubmissionManifestPath}`");
        builder.AppendLine();
        builder.AppendLine("## Metadata");
        builder.AppendLine();
        builder.AppendLine($"- Extension name: `{EmptyIfMissing(target.ManifestName)}`");
        builder.AppendLine($"- Extension version: `{EmptyIfMissing(target.ManifestVersion)}`");
        builder.AppendLine($"- Support URL: `{EmptyIfMissing(target.SupportUrl)}`");
        builder.AppendLine($"- Privacy URL: `{EmptyIfMissing(target.PrivacyUrl)}`");
        builder.AppendLine($"- Release notes: `{EmptyIfMissing(target.ReleaseNotes)}`");
        builder.AppendLine();
        AppendList(builder, "Issues", target.Issues);
        AppendList(builder, "Warnings", target.Warnings);
        AppendList(builder, "Manual Evidence Required", target.ManualEvidenceRequired);
        AppendList(builder, "Publication Non-Goals", target.PublicationNonGoals);
        builder.AppendLine("## Source Hashes");
        builder.AppendLine();
        foreach (var sourceHash in target.SourceFileHashes)
        {
            builder.AppendLine($"- `{sourceHash.Path}` `{sourceHash.Sha256}` {sourceHash.Bytes} bytes");
        }

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

    private static IReadOnlyList<string> BuildManualEvidenceRequired() => new[]
    {
        "Live browser extension details screenshot with extension id.",
        "Popup/options screenshots showing consent defaults.",
        "Popup Host Status success screenshot from the target browser.",
        "Safe-fixture capture screenshot or exported stitch package from a real browser session.",
        "Native import result from the real browser-exported stitch package.",
        "Store account submission/review status from the target browser store."
    };

    private static IReadOnlyList<string> BuildPublicationNonGoals() => new[]
    {
        "This package is not browser-store publication proof.",
        "This package is not browser-store signing or review approval.",
        "This package is not automatic extension installation.",
        "This package is not live native-host Host Status proof.",
        "This package does not bypass GoatShot desktop policy, local consent, or native payload validation."
    };

    private static string EmptyIfMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

    private static string SafeFileToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '-');
        }

        return builder.ToString();
    }
}

public enum BrowserExtensionStorePackageTarget
{
    Chrome,
    Edge,
    Firefox
}

public sealed class BrowserExtensionStorePackageRequest
{
    public string? ExtensionSourceDirectory { get; set; }
    public string? OutputPath { get; set; }
    public string? Target { get; set; }
    public string? SupportUrl { get; set; }
    public string? PrivacyUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? ReleaseNotesPath { get; set; }
}

public sealed class BrowserExtensionStorePackageResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string PrivacyUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public List<BrowserExtensionStorePackageTargetResult> Targets { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ManualEvidenceRequired { get; set; } = new();
    public List<string> PublicationNonGoals { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionStorePackageTargetResult
{
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TargetFolder { get; set; } = string.Empty;
    public string ReadinessStatus { get; set; } = string.Empty;
    public string ReadinessOutputPath { get; set; } = string.Empty;
    public string ExtensionPackagePath { get; set; } = string.Empty;
    public string ExtensionPackageSha256 { get; set; } = string.Empty;
    public long ExtensionPackageBytes { get; set; }
    public string SubmissionBundlePath { get; set; } = string.Empty;
    public string SubmissionBundleSha256 { get; set; } = string.Empty;
    public long SubmissionBundleBytes { get; set; }
    public string SubmissionManifestPath { get; set; } = string.Empty;
    public string ManifestName { get; set; } = string.Empty;
    public string ManifestVersion { get; set; } = string.Empty;
    public string SupportUrl { get; set; } = string.Empty;
    public string PrivacyUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public List<BrowserExtensionStorePackageFileHash> SourceFileHashes { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ManualEvidenceRequired { get; set; } = new();
    public List<string> PublicationNonGoals { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class BrowserExtensionStorePackageFileHash
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Bytes { get; set; }
}

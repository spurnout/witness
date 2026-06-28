using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoatShot.App.Services;

public sealed class PersonSegmentationModelPackageService
{
    public const string CurrentManifestSchemaVersion = "goatshot.person-segmentation-model.v1";
    public const long DefaultMaxModelBytes = 750L * 1024L * 1024L;

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9._-]{2,63}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
    private static readonly HttpClient SharedHttpClient = new();

    private readonly AppPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly long _maxModelBytes;

    public PersonSegmentationModelPackageService(
        AppPaths paths,
        HttpClient? httpClient = null,
        long maxModelBytes = DefaultMaxModelBytes)
    {
        _paths = paths;
        _httpClient = httpClient ?? SharedHttpClient;
        _maxModelBytes = maxModelBytes;
    }

    public async Task<PersonSegmentationModelPackageResult> ValidateManifestAsync(
        string manifestLocation,
        CancellationToken cancellationToken = default)
    {
        return await PlanOrStageAsync(new PersonSegmentationModelPackageRequest
        {
            ManifestLocation = manifestLocation,
            StageModel = false
        }, cancellationToken);
    }

    public async Task<PersonSegmentationModelPackageResult> StageModelAsync(
        PersonSegmentationModelPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        request.StageModel = true;
        return await PlanOrStageAsync(request, cancellationToken);
    }

    private async Task<PersonSegmentationModelPackageResult> PlanOrStageAsync(
        PersonSegmentationModelPackageRequest request,
        CancellationToken cancellationToken)
    {
        var result = NewResult(request);
        if (string.IsNullOrWhiteSpace(request.ManifestLocation))
        {
            result.Issues.Add("A person-segmentation model manifest is required. Pass --manifest <manifest.json>.");
            result.Message = "Person-segmentation model package validation failed.";
            return result;
        }

        LoadedPersonSegmentationModelManifest loaded;
        try
        {
            loaded = await LoadManifestAsync(request.ManifestLocation, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or JsonException or NotSupportedException or UriFormatException)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            result.Message = "Person-segmentation model manifest could not be loaded.";
            return result;
        }

        result.ManifestLocation = loaded.DisplayLocation;
        CopyManifest(loaded.Manifest, result);
        ValidateManifest(loaded.Manifest, result);
        if (result.Issues.Count > 0)
        {
            result.Message = "Person-segmentation model manifest failed validation.";
            return result;
        }

        var modelReference = ResolveModelReference(loaded);
        result.ModelUri = modelReference.DisplayUri;
        result.WouldDownloadModel = modelReference.IsRemote;

        if (!request.StageModel)
        {
            result.Succeeded = true;
            result.Message = $"Person-segmentation model {result.ModelId} {result.Version} can be staged after checksum and size validation. It will not be trusted, enabled, executed, or registered as a runner.";
            AddNextActions(result);
            return result;
        }

        if (modelReference.IsRemote && !request.AcceptDownload)
        {
            result.Issues.Add("Remote model acquisition requires --accept-download after reviewing the manifest source, license, size, and checksum.");
            result.Message = "Person-segmentation model package was not staged.";
            AddNextActions(result);
            return result;
        }

        byte[] modelBytes;
        try
        {
            modelBytes = await ReadModelBytesAsync(modelReference, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            result.Issues.Add(SensitiveTextDetector.Redact(ex.Message));
            result.Message = "Person-segmentation model package was not staged.";
            return result;
        }

        result.DidDownloadModel = modelReference.IsRemote;
        result.SizeBytes = modelBytes.LongLength;
        result.ModelSha256 = ComputeSha256(modelBytes);
        if (loaded.Manifest.SizeBytes is > 0 && loaded.Manifest.SizeBytes.Value != modelBytes.LongLength)
        {
            result.Issues.Add($"Model size mismatch. Manifest declared {loaded.Manifest.SizeBytes.Value} byte(s), package read {modelBytes.LongLength} byte(s).");
        }

        if (modelBytes.LongLength > _maxModelBytes)
        {
            result.Issues.Add($"Model package is {modelBytes.LongLength} byte(s), over the {_maxModelBytes} byte safety limit.");
        }

        if (!result.ModelSha256.Equals(loaded.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add("Model SHA-256 did not match the manifest.");
        }

        if (result.Issues.Count > 0)
        {
            result.Message = "Person-segmentation model package was not staged.";
            return result;
        }

        Directory.CreateDirectory(_paths.PersonSegmentationModelStagingRoot);
        var stageDirectory = GetStageDirectory(result.ModelId, result.Version);
        ResetStageDirectory(stageDirectory);
        var modelPath = Path.Combine(
            stageDirectory,
            $"{SafePathSegment(result.ModelId)}-{SafePathSegment(result.Version)}{SafeModelExtension(modelReference.FileName)}");
        await File.WriteAllBytesAsync(modelPath, modelBytes, cancellationToken);

        var stageManifestPath = Path.Combine(stageDirectory, "model-stage-manifest.json");
        var stageManifest = new PersonSegmentationModelStageManifest
        {
            SchemaVersion = "goatshot.person-segmentation-model-stage.v1",
            ModelId = result.ModelId,
            Version = result.Version,
            Name = result.Name,
            ManifestLocation = result.ManifestLocation,
            ModelUri = result.ModelUri,
            ModelSha256 = result.ModelSha256,
            SizeBytes = modelBytes.LongLength,
            StagedModelPath = modelPath,
            RunnerHint = result.RunnerHint,
            License = result.License,
            Source = result.Source,
            StagedAtUtc = DateTimeOffset.UtcNow,
            DidDownloadModel = result.DidDownloadModel,
            WouldRunModel = false,
            WouldTrustModel = false,
            WouldEnableModel = false,
            WouldRegisterRunner = false,
            WouldContactHostedSegmentationService = false,
            WouldCertifyModel = false,
            Notes =
            [
                "Model package is staged only.",
                "GoatShot did not run inference, trust, enable, register, or certify this model.",
                "Operator review and a separate runner/inference integration are still required before use."
            ]
        };
        await File.WriteAllTextAsync(stageManifestPath, JsonSerializer.Serialize(stageManifest, JsonOptions), cancellationToken);

        result.Succeeded = true;
        result.Staged = true;
        result.StagedDirectory = stageDirectory;
        result.StagedModelPath = modelPath;
        result.StagedManifestPath = stageManifestPath;
        result.GeneratedFiles.Add(modelPath);
        result.GeneratedFiles.Add(stageManifestPath);
        result.Message = $"Person-segmentation model {result.ModelId} {result.Version} was staged only. It remains untrusted, disabled, unregistered, and unexecuted.";
        AddNextActions(result);
        return result;
    }

    private PersonSegmentationModelPackageResult NewResult(PersonSegmentationModelPackageRequest request)
    {
        return new PersonSegmentationModelPackageResult
        {
            ManifestLocation = SafeText(request.ManifestLocation),
            StagingRoot = _paths.PersonSegmentationModelStagingRoot,
            MaxModelBytes = _maxModelBytes,
            WouldRunModel = false,
            WouldTrustModel = false,
            WouldEnableModel = false,
            WouldRegisterRunner = false,
            WouldContactHostedSegmentationService = false,
            WouldCertifyModel = false
        };
    }

    private async Task<LoadedPersonSegmentationModelManifest> LoadManifestAsync(
        string manifestLocation,
        CancellationToken cancellationToken)
    {
        var normalized = Environment.ExpandEnvironmentVariables(manifestLocation.Trim());
        if (TryCreateHttpUri(normalized, out var manifestUri))
        {
            var json = await _httpClient.GetStringAsync(manifestUri, cancellationToken);
            var manifest = JsonSerializer.Deserialize<PersonSegmentationModelManifest>(json, JsonOptions) ??
                throw new JsonException("Manifest JSON did not contain an object.");
            return new LoadedPersonSegmentationModelManifest(
                manifest,
                SensitiveTextDetector.Redact(manifestUri.ToString()),
                LocalDirectory: null,
                ManifestUri: manifestUri);
        }

        var path = Uri.TryCreate(normalized, UriKind.Absolute, out var fileUri) && fileUri.IsFile
            ? fileUri.LocalPath
            : Path.GetFullPath(normalized);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Manifest was not found: {path}", path);
        }

        var contents = await File.ReadAllTextAsync(path, cancellationToken);
        var loaded = JsonSerializer.Deserialize<PersonSegmentationModelManifest>(contents, JsonOptions) ??
            throw new JsonException("Manifest JSON did not contain an object.");
        return new LoadedPersonSegmentationModelManifest(
            loaded,
            SensitiveTextDetector.Redact(path),
            Path.GetDirectoryName(path),
            ManifestUri: null);
    }

    private static void CopyManifest(
        PersonSegmentationModelManifest manifest,
        PersonSegmentationModelPackageResult result)
    {
        result.ModelId = SafeText(manifest.ModelId);
        result.Version = SafeText(manifest.Version);
        result.Name = SafeText(manifest.Name);
        result.ModelUri = SafeText(manifest.ModelUri);
        result.DeclaredSha256 = SafeText(manifest.Sha256);
        result.DeclaredSizeBytes = manifest.SizeBytes;
        result.RunnerHint = SafeText(manifest.RunnerHint);
        result.License = SafeText(manifest.License);
        result.Source = SafeText(manifest.Source);
        result.Notes = SafeText(manifest.Notes);
    }

    private static void ValidateManifest(
        PersonSegmentationModelManifest manifest,
        PersonSegmentationModelPackageResult result)
    {
        if (!string.Equals(manifest.SchemaVersion, CurrentManifestSchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add($"schemaVersion must be {CurrentManifestSchemaVersion}.");
        }

        if (!IsValidId(manifest.ModelId))
        {
            result.Issues.Add("modelId is required and must be 3-64 characters using letters, numbers, dots, underscores, or hyphens.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64)
        {
            result.Issues.Add("version is required and must be 64 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ModelUri))
        {
            result.Issues.Add("modelUri is required.");
        }
        else if (!IsSupportedModelUri(manifest.ModelUri))
        {
            result.Issues.Add("modelUri must be a local path, file URI, or HTTP(S) URI.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Sha256Pattern.IsMatch(manifest.Sha256.Trim()))
        {
            result.Issues.Add("sha256 is required and must be a 64-character hexadecimal SHA-256 digest.");
        }

        if (manifest.SizeBytes is <= 0)
        {
            result.Issues.Add("sizeBytes must be greater than zero when provided.");
        }
    }

    private static ModelReference ResolveModelReference(LoadedPersonSegmentationModelManifest loaded)
    {
        var modelUri = Environment.ExpandEnvironmentVariables(loaded.Manifest.ModelUri.Trim());
        if (Path.IsPathFullyQualified(modelUri))
        {
            var qualifiedLocalPath = Path.GetFullPath(modelUri);
            return new ModelReference(Uri: null, qualifiedLocalPath, SensitiveTextDetector.Redact(qualifiedLocalPath), IsRemote: false);
        }

        if (TryCreateHttpUri(modelUri, out var remoteUri))
        {
            return new ModelReference(remoteUri, LocalPath: null, SensitiveTextDetector.Redact(remoteUri.ToString()), IsRemote: true);
        }

        if (Uri.TryCreate(modelUri, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile)
            {
                return new ModelReference(absoluteUri, absoluteUri.LocalPath, SensitiveTextDetector.Redact(absoluteUri.LocalPath), IsRemote: false);
            }

            return new ModelReference(absoluteUri, LocalPath: null, SensitiveTextDetector.Redact(absoluteUri.ToString()), IsRemote: false);
        }

        if (loaded.ManifestUri is not null && TryCreateHttpUri(loaded.ManifestUri.ToString(), out var manifestUri))
        {
            var resolvedUri = new Uri(manifestUri, modelUri);
            return new ModelReference(resolvedUri, LocalPath: null, SensitiveTextDetector.Redact(resolvedUri.ToString()), IsRemote: true);
        }

        var localPath = string.IsNullOrWhiteSpace(loaded.LocalDirectory)
            ? Path.GetFullPath(modelUri)
            : Path.GetFullPath(Path.Combine(loaded.LocalDirectory, modelUri));
        return new ModelReference(Uri: null, localPath, SensitiveTextDetector.Redact(localPath), IsRemote: false);
    }

    private async Task<byte[]> ReadModelBytesAsync(ModelReference reference, CancellationToken cancellationToken)
    {
        if (reference.IsRemote)
        {
            if (reference.Uri is null)
            {
                throw new InvalidOperationException("Remote model URI could not be resolved.");
            }

            var bytes = await _httpClient.GetByteArrayAsync(reference.Uri, cancellationToken);
            if (bytes.LongLength > _maxModelBytes)
            {
                throw new InvalidOperationException($"Model package is {bytes.LongLength} byte(s), over the {_maxModelBytes} byte safety limit.");
            }

            return bytes;
        }

        if (string.IsNullOrWhiteSpace(reference.LocalPath))
        {
            throw new InvalidOperationException("Local model path could not be resolved.");
        }

        if (!File.Exists(reference.LocalPath))
        {
            throw new FileNotFoundException($"Model file was not found: {reference.LocalPath}", reference.LocalPath);
        }

        var info = new FileInfo(reference.LocalPath);
        if (info.Length > _maxModelBytes)
        {
            throw new InvalidOperationException($"Model package is {info.Length} byte(s), over the {_maxModelBytes} byte safety limit.");
        }

        return await File.ReadAllBytesAsync(reference.LocalPath, cancellationToken);
    }

    private string GetStageDirectory(string modelId, string version)
    {
        return Path.Combine(
            _paths.PersonSegmentationModelStagingRoot,
            SafePathSegment(modelId),
            SafePathSegment(version));
    }

    private static void ResetStageDirectory(string stageDirectory)
    {
        if (Directory.Exists(stageDirectory))
        {
            Directory.Delete(stageDirectory, recursive: true);
        }

        Directory.CreateDirectory(stageDirectory);
    }

    private static bool IsValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IdPattern.IsMatch(value.Trim());

    private static bool IsSupportedModelUri(string modelUri)
    {
        var expanded = Environment.ExpandEnvironmentVariables(modelUri.Trim());
        if (Path.IsPathFullyQualified(expanded))
        {
            return true;
        }

        if (Uri.TryCreate(expanded, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ||
                uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string SafePathSegment(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe.Trim('.', ' ');
    }

    private static string SafeModelExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16)
        {
            return ".model";
        }

        return new string(extension.Select(character =>
            char.IsLetterOrDigit(character) || character == '.' ? character : '-').ToArray());
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string SafeText(string? value) =>
        SensitiveTextDetector.Redact((value ?? string.Empty).Trim());

    private static void AddNextActions(PersonSegmentationModelPackageResult result)
    {
        result.NextActions.Add("Review the staged model, license, source, checksum, and runner compatibility before use.");
        result.NextActions.Add("Use a separate explicit runner/inference integration to generate masks; staging does not enable inference.");
        result.NextActions.Add("Quality-certify generated masks with `goatshot video mask-quality` against reviewed reference masks.");
    }

    private sealed record LoadedPersonSegmentationModelManifest(
        PersonSegmentationModelManifest Manifest,
        string DisplayLocation,
        string? LocalDirectory,
        Uri? ManifestUri);

    private sealed record ModelReference(Uri? Uri, string? LocalPath, string DisplayUri, bool IsRemote)
    {
        public string FileName => Uri?.Segments.LastOrDefault() ?? Path.GetFileName(LocalPath ?? string.Empty);
    }
}

public sealed class PersonSegmentationModelPackageRequest
{
    public string? ManifestLocation { get; set; }
    public bool StageModel { get; set; }
    public bool AcceptDownload { get; set; }
}

public sealed class PersonSegmentationModelManifest
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ModelUri { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public string RunnerHint { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class PersonSegmentationModelPackageResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Staged { get; set; }
    public bool WouldDownloadModel { get; set; }
    public bool DidDownloadModel { get; set; }
    public string ManifestLocation { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModelUri { get; set; } = string.Empty;
    public string DeclaredSha256 { get; set; } = string.Empty;
    public long? DeclaredSizeBytes { get; set; }
    public long SizeBytes { get; set; }
    public string ModelSha256 { get; set; } = string.Empty;
    public string RunnerHint { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string StagingRoot { get; set; } = string.Empty;
    public string StagedDirectory { get; set; } = string.Empty;
    public string StagedModelPath { get; set; } = string.Empty;
    public string StagedManifestPath { get; set; } = string.Empty;
    public long MaxModelBytes { get; set; }
    public bool WouldRunModel { get; set; }
    public bool WouldTrustModel { get; set; }
    public bool WouldEnableModel { get; set; }
    public bool WouldRegisterRunner { get; set; }
    public bool WouldContactHostedSegmentationService { get; set; }
    public bool WouldCertifyModel { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
    public List<string> NextActions { get; set; } = new();
}

public sealed class PersonSegmentationModelStageManifest
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ManifestLocation { get; set; } = string.Empty;
    public string ModelUri { get; set; } = string.Empty;
    public string ModelSha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StagedModelPath { get; set; } = string.Empty;
    public string RunnerHint { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset StagedAtUtc { get; set; }
    public bool DidDownloadModel { get; set; }
    public bool WouldRunModel { get; set; }
    public bool WouldTrustModel { get; set; }
    public bool WouldEnableModel { get; set; }
    public bool WouldRegisterRunner { get; set; }
    public bool WouldContactHostedSegmentationService { get; set; }
    public bool WouldCertifyModel { get; set; }
    public List<string> Notes { get; set; } = new();
}

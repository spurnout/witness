using System.Reflection;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BundledToolResolver
{
    private const string PayloadMagic = "GOATSHOTASSET1!!";
    private const int PayloadFooterLength = sizeof(long) + 16;
    public const string ManifestResourceName = "GoatShot.EmbeddedAssets.Manifest";
    public const string DistributionManifestResourceName = "GoatShot.EmbeddedAssets.Manifest.Distribution";

    private static readonly SemaphoreSlim ExtractionGate = new(1, 1);
    private readonly string _runtimeRoot;
    private readonly Assembly _assembly;
    private readonly string _executablePath;
    private EmbeddedAssetManifest? _manifest;

    public BundledToolResolver(string runtimeRoot, Assembly? assembly = null, string? executablePath = null)
    {
        _runtimeRoot = Path.GetFullPath(runtimeRoot);
        _assembly = assembly ?? typeof(BundledToolResolver).Assembly;
        _executablePath = Path.GetFullPath(executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The running executable path is unavailable."));
    }

    public EmbeddedAssetManifest Manifest => _manifest ??= ReadManifest();
    public string ActiveRuntimeDirectory => Path.Combine(_runtimeRoot, SanitizeBuildId(Manifest.BuildId));

    public async Task<BundledRuntimeStatus> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (Manifest.Assets.Count == 0)
        {
            return new BundledRuntimeStatus(true, ActiveRuntimeDirectory, Manifest, [], "No distribution assets are embedded in this development build.");
        }

        await ExtractionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var validation = ValidateExisting();
            if (validation.Succeeded)
            {
                return validation;
            }

            return await ExtractAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExtractionGate.Release();
        }
    }

    public async Task<BundledRuntimeStatus> RepairAsync(CancellationToken cancellationToken = default)
    {
        await ExtractionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(ActiveRuntimeDirectory))
            {
                var quarantine = ActiveRuntimeDirectory + $".corrupt-{Guid.NewGuid():N}";
                Directory.Move(ActiveRuntimeDirectory, quarantine);
                Directory.Delete(quarantine, recursive: true);
            }

            return Manifest.Assets.Count == 0
                ? new BundledRuntimeStatus(true, ActiveRuntimeDirectory, Manifest, [], "No distribution assets are embedded in this development build.")
                : await ExtractAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExtractionGate.Release();
        }
    }

    public string? Resolve(string assetId)
    {
        var asset = Manifest.Assets.FirstOrDefault(entry => entry.Id.Equals(assetId, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            return null;
        }

        var path = SafeAssetPath(ActiveRuntimeDirectory, asset.RelativePath);
        return File.Exists(path) && HashMatches(path, asset.Sha256) ? path : null;
    }

    public BundledRuntimeStatus ValidateExisting()
    {
        var issues = new List<string>();
        foreach (var asset in Manifest.Assets)
        {
            var path = SafeAssetPath(ActiveRuntimeDirectory, asset.RelativePath);
            if (!File.Exists(path))
            {
                issues.Add($"Missing embedded asset: {asset.Id}");
            }
            else if (!HashMatches(path, asset.Sha256))
            {
                issues.Add($"Embedded asset hash mismatch: {asset.Id}");
            }
        }

        return new BundledRuntimeStatus(
            issues.Count == 0,
            ActiveRuntimeDirectory,
            Manifest,
            issues,
            issues.Count == 0 ? "Bundled runtime assets are ready." : "Bundled runtime assets need repair.");
    }

    private Task<BundledRuntimeStatus> ExtractAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Extract(cancellationToken), cancellationToken);

    private BundledRuntimeStatus Extract(CancellationToken cancellationToken)
    {
        StartupTrace.Write("Bundled extraction start");
        var parent = Path.GetDirectoryName(ActiveRuntimeDirectory)!;
        Directory.CreateDirectory(parent);
        var staging = ActiveRuntimeDirectory + $".staging-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            using var executable = new FileStream(
                _executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 1024 * 1024,
                useAsync: false);
            using var payload = OpenAssetPayload(executable);
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
            StartupTrace.Write($"Bundled archive opened entries={archive.Entries.Count}");
            foreach (var asset in Manifest.Assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var output = SafeAssetPath(staging, asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var archivePath = asset.RelativePath.Replace('\\', '/');
                var entry = archive.Entries.FirstOrDefault(candidate =>
                    candidate.FullName.Replace('\\', '/').Equals(archivePath, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"Embedded executable is missing asset payload: {asset.Id}");
                if (entry.Length != asset.Size)
                {
                    throw new InvalidOperationException($"Embedded asset size does not match its locked manifest: {asset.Id}");
                }
                using (var source = entry.Open())
                using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                    destination.Flush(flushToDisk: true);
                }
                if (!HashMatches(output, asset.Sha256))
                {
                    throw new InvalidOperationException($"Embedded asset failed SHA-256 validation: {asset.Id}");
                }
            }

            if (Directory.Exists(ActiveRuntimeDirectory))
            {
                Directory.Delete(ActiveRuntimeDirectory, recursive: true);
            }

            Directory.Move(staging, ActiveRuntimeDirectory);
            StartupTrace.Write("Bundled extraction complete");
            return ValidateExisting();
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    private EmbeddedAssetManifest ReadManifest()
    {
        var resourceName = _assembly.GetManifestResourceNames().Contains(DistributionManifestResourceName, StringComparer.Ordinal)
            ? DistributionManifestResourceName
            : ManifestResourceName;
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded asset manifest is missing.");
        var manifest = JsonSerializer.Deserialize<EmbeddedAssetManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The embedded asset manifest is invalid.");
        if (!manifest.SchemaVersion.Equals("goatshot.embedded-assets.v1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported embedded asset manifest schema: {manifest.SchemaVersion}");
        }

        foreach (var asset in manifest.Assets)
        {
            _ = SafeAssetPath(Path.GetTempPath(), asset.RelativePath);
            if (asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException($"Embedded asset has an invalid SHA-256 digest: {asset.Id}");
            }
            if (string.IsNullOrWhiteSpace(asset.ResourceName))
            {
                throw new InvalidOperationException($"Embedded asset is missing its executable resource mapping: {asset.Id}");
            }
        }

        var duplicateIds = manifest.Assets
            .GroupBy(asset => asset.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException($"Embedded asset manifest contains duplicate IDs: {string.Join(", ", duplicateIds)}");
        }

        return manifest;
    }

    private static Stream OpenAssetPayload(FileStream executable)
    {
        if (executable.Length < PayloadFooterLength)
        {
            throw new InvalidOperationException("The executable does not contain an embedded asset payload footer.");
        }

        executable.Position = executable.Length - PayloadFooterLength;
        Span<byte> footer = stackalloc byte[PayloadFooterLength];
        executable.ReadExactly(footer);
        var payloadLength = BitConverter.ToInt64(footer[..sizeof(long)]);
        var magic = System.Text.Encoding.ASCII.GetString(footer[sizeof(long)..]);
        var payloadOffset = executable.Length - PayloadFooterLength - payloadLength;
        if (!magic.Equals(PayloadMagic, StringComparison.Ordinal) || payloadLength <= 0 || payloadOffset < 0)
        {
            throw new InvalidOperationException("The executable embedded asset payload footer is invalid.");
        }

        return new BoundedReadStream(executable, payloadOffset, payloadLength);
    }

    private static string SafeAssetPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Embedded asset path must be relative: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Embedded asset path escapes the runtime root: {relativePath}");
        }

        return fullPath;
    }

    private static bool HashMatches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeBuildId(string buildId)
    {
        var safe = new string((buildId ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public BoundedReadStream(Stream inner, long start, long length)
    {
        _inner = inner;
        _start = start;
        _length = length;
        _inner.Position = _start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var allowed = (int)Math.Min(count, _length - _position);
        if (allowed <= 0) return 0;
        _inner.Position = _start + _position;
        var read = _inner.Read(buffer, offset, allowed);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var allowed = (int)Math.Min(buffer.Length, _length - _position);
        if (allowed <= 0) return 0;
        _inner.Position = _start + _position;
        var read = _inner.Read(buffer[..allowed]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (target < 0 || target > _length) throw new IOException("Attempted to seek outside the embedded asset payload.");
        _position = target;
        _inner.Position = _start + target;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public sealed class EmbeddedAssetManifest
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string ManifestSha256 { get; set; } = string.Empty;
    public List<EmbeddedAssetEntry> Assets { get; set; } = new();
}

public sealed class EmbeddedAssetEntry
{
    public string Id { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string BuildOptions { get; set; } = string.Empty;
    public string ExtractionTarget { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
}

public sealed record BundledRuntimeStatus(
    bool Succeeded,
    string RuntimeDirectory,
    EmbeddedAssetManifest Manifest,
    IReadOnlyList<string> Issues,
    string Message);

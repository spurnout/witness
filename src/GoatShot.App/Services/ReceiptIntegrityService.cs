using System.Security.Cryptography;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ReceiptIntegrityService
{
    public const string ManifestFileName = "receipt.json";
    public static readonly string ChainSeedSha256 = new('0', 64);

    private readonly ReceiptDeviceKeyService _deviceKeys;

    public ReceiptIntegrityService(ReceiptDeviceKeyService? deviceKeys = null)
    {
        _deviceKeys = deviceKeys ?? new ReceiptDeviceKeyService();
    }

    public async Task<ReceiptManifest> SealAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        string deviceKeyPath,
        CancellationToken cancellationToken = default)
    {
        return await SealCoreAsync(
            manifest,
            receiptRoot,
            sealedManifest => _deviceKeys.SignManifest(deviceKeyPath, sealedManifest),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ReceiptManifest> SealWithKeyAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        ReceiptDeviceKeyService.CapturedSigningKey signingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        return await SealCoreAsync(
            manifest,
            receiptRoot,
            signingKey.SignManifest,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReceiptManifest> SealCoreAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        Func<ReceiptManifest, ReceiptSignatureManifest> signManifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(signManifest);
        var validatedRoot = ValidateReceiptRoot(receiptRoot);
        var sealedManifest = CloneManifest(manifest);
        sealedManifest.Signature = null;

        var finalizedAtUtc = DateTimeOffset.UtcNow;
        if (sealedManifest.CreatedAtUtc == default)
        {
            sealedManifest.CreatedAtUtc = finalizedAtUtc;
        }

        if (sealedManifest.FinalizedAtUtc == default)
        {
            sealedManifest.FinalizedAtUtc = finalizedAtUtc;
        }

        NormalizeOrderingAndPaths(sealedManifest);
        var validationIssues = ValidateStructure(sealedManifest, requireSignature: false);
        if (validationIssues.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", validationIssues));
        }

        var previousChainHash = ChainSeedSha256;
        foreach (var segment in sealedManifest.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packagePath = ResolvePackagePath(validatedRoot, segment.RelativePath);
            var fileHash = await HashRequiredFileAsync(packagePath, cancellationToken);
            segment.SizeBytes = fileHash.SizeBytes;
            segment.Sha256 = fileHash.Sha256;
            segment.PreviousChainSha256 = previousChainHash;
            segment.ChainSha256 = ComputeChainHash(segment, previousChainHash);
            previousChainHash = segment.ChainSha256;
        }

        foreach (var artifact in sealedManifest.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packagePath = ResolvePackagePath(validatedRoot, artifact.RelativePath);
            var fileHash = await HashRequiredFileAsync(packagePath, cancellationToken);
            artifact.SizeBytes = fileHash.SizeBytes;
            artifact.Sha256 = fileHash.Sha256;
        }

        sealedManifest.Signature = signManifest(sealedManifest);
        return sealedManifest;
    }

    public async Task<ReceiptManifest> SealAndWriteAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        string deviceKeyPath,
        CancellationToken cancellationToken = default)
    {
        var sealedManifest = await SealAsync(manifest, receiptRoot, deviceKeyPath, cancellationToken);
        await WriteManifestAsync(sealedManifest, receiptRoot, cancellationToken);
        return sealedManifest;
    }

    internal async Task<ReceiptManifest> SealAndWriteWithKeyAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        ReceiptDeviceKeyService.CapturedSigningKey signingKey,
        CancellationToken cancellationToken = default)
    {
        var sealedManifest = await SealWithKeyAsync(
            manifest,
            receiptRoot,
            signingKey,
            cancellationToken).ConfigureAwait(false);
        await WriteManifestAsync(sealedManifest, receiptRoot, cancellationToken)
            .ConfigureAwait(false);
        return sealedManifest;
    }

    public async Task WriteManifestAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var validatedRoot = ValidateReceiptRoot(receiptRoot);
        Directory.CreateDirectory(validatedRoot);
        var targetPath = Path.Combine(validatedRoot, ManifestFileName);
        var temporaryPath = Path.Combine(validatedRoot, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                ReceiptCanonicalJson.Serialize(manifest),
                cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ReceiptVerificationResult> VerifyPackageAsync(
        string receiptRoot,
        string? knownDeviceKeyPath = null,
        CancellationToken cancellationToken = default)
    {
        string validatedRoot;
        try
        {
            validatedRoot = ValidateReceiptRoot(receiptRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unverifiable(string.Empty, string.Empty, ex.Message);
        }

        var manifestPath = Path.Combine(validatedRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Unverifiable(string.Empty, string.Empty, $"Required manifest '{ManifestFileName}' is missing.");
        }

        try
        {
            EnsureNoReparsePoints(validatedRoot, manifestPath);
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            var manifest = ReceiptCanonicalJson.Deserialize(manifestBytes);
            var canonicalBytes = ReceiptCanonicalJson.Serialize(manifest);
            if (!manifestBytes.AsSpan().SequenceEqual(canonicalBytes))
            {
                return BuildResult(
                    ReceiptVerificationStatus.Modified,
                    manifest.ReceiptId ?? string.Empty,
                    manifest.Signature?.KeyFingerprintSha256 ?? string.Empty,
                    ["The stored receipt manifest is not the exact signed canonical JSON representation."]);
            }

            return await VerifyAsync(manifest, validatedRoot, knownDeviceKeyPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Text.Json.JsonException)
        {
            return Unverifiable(string.Empty, string.Empty, $"Receipt manifest could not be read: {ex.Message}");
        }
    }

    public async Task<ReceiptVerificationResult> VerifyAsync(
        ReceiptManifest manifest,
        string receiptRoot,
        string? knownDeviceKeyPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var receiptId = manifest.ReceiptId ?? string.Empty;
        var fingerprint = manifest.Signature?.KeyFingerprintSha256?.Trim().ToLowerInvariant() ?? string.Empty;

        string validatedRoot;
        try
        {
            validatedRoot = ValidateReceiptRoot(receiptRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unverifiable(receiptId, fingerprint, ex.Message);
        }

        if (!string.Equals(manifest.Schema, ReceiptManifestSchemas.V1, StringComparison.Ordinal))
        {
            return Unverifiable(receiptId, fingerprint, $"Unsupported receipt schema '{manifest.Schema}'.");
        }

        var signatureValidation = VerifyManifestSignature(manifest);
        if (signatureValidation.Status is not null)
        {
            return new ReceiptVerificationResult
            {
                Status = signatureValidation.Status.Value,
                ReceiptId = receiptId,
                SignerFingerprintSha256 = fingerprint,
                Issues = signatureValidation.Issues
            };
        }

        var structureIssues = ValidateStructure(manifest, requireSignature: true);
        if (structureIssues.Count > 0)
        {
            return new ReceiptVerificationResult
            {
                Status = ReceiptVerificationStatus.Unverifiable,
                ReceiptId = receiptId,
                SignerFingerprintSha256 = fingerprint,
                Issues = structureIssues
            };
        }

        var issues = new List<string>();
        var hasMissingFiles = false;
        var hasModifiedContent = false;
        var hasUnverifiableFiles = false;
        var previousChainHash = ChainSeedSha256;

        foreach (var segment in manifest.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!segment.PreviousChainSha256.Equals(previousChainHash, StringComparison.Ordinal) ||
                !segment.ChainSha256.Equals(ComputeChainHash(segment, previousChainHash), StringComparison.Ordinal))
            {
                hasModifiedContent = true;
                issues.Add($"Segment '{segment.SegmentId}' does not match the signed hash chain.");
            }

            previousChainHash = segment.ChainSha256;
            var fileResult = await VerifyFileAsync(
                validatedRoot,
                segment.RelativePath,
                segment.SizeBytes,
                segment.Sha256,
                cancellationToken);
            RecordFileResult(
                fileResult,
                segment.RelativePath,
                issues,
                ref hasMissingFiles,
                ref hasModifiedContent,
                ref hasUnverifiableFiles);
        }

        foreach (var artifact in manifest.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileResult = await VerifyFileAsync(
                validatedRoot,
                artifact.RelativePath,
                artifact.SizeBytes,
                artifact.Sha256,
                cancellationToken);
            RecordFileResult(
                fileResult,
                artifact.RelativePath,
                issues,
                ref hasMissingFiles,
                ref hasModifiedContent,
                ref hasUnverifiableFiles);
        }

        VerifyPackageInventory(
            manifest,
            validatedRoot,
            issues,
            ref hasModifiedContent,
            ref hasUnverifiableFiles);

        if (hasModifiedContent)
        {
            return BuildResult(ReceiptVerificationStatus.Modified, receiptId, fingerprint, issues);
        }

        if (hasUnverifiableFiles)
        {
            return BuildResult(ReceiptVerificationStatus.Unverifiable, receiptId, fingerprint, issues);
        }

        if (hasMissingFiles)
        {
            return BuildResult(ReceiptVerificationStatus.Incomplete, receiptId, fingerprint, issues);
        }

        var isKnownDevice = false;
        if (!string.IsNullOrWhiteSpace(knownDeviceKeyPath))
        {
            try
            {
                isKnownDevice = _deviceKeys.IsKnownFingerprint(knownDeviceKeyPath, fingerprint);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException)
            {
                issues.Add($"The local device key history could not be read: {ex.Message}");
            }
        }

        return BuildResult(
            isKnownDevice
                ? ReceiptVerificationStatus.IntactKnownDevice
                : ReceiptVerificationStatus.IntactUnknownDevice,
            receiptId,
            fingerprint,
            issues);
    }

    private static ReceiptManifest CloneManifest(ReceiptManifest manifest) =>
        ReceiptCanonicalJson.Deserialize(ReceiptCanonicalJson.Serialize(manifest));

    private static string ValidateReceiptRoot(string receiptRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptRoot);
        return Path.GetFullPath(receiptRoot);
    }

    private static void NormalizeOrderingAndPaths(ReceiptManifest manifest)
    {
        if (manifest.Tracks is null || manifest.Segments is null || manifest.Artifacts is null ||
            manifest.Tracks.Any(track => track is null) ||
            manifest.Segments.Any(segment => segment is null) ||
            manifest.Artifacts.Any(artifact => artifact is null))
        {
            return;
        }

        foreach (var track in manifest.Tracks)
        {
            if (track?.SourceTransitions is not null)
            {
                track.SourceTransitions = track.SourceTransitions
                    .OrderBy(transition => transition.EffectiveStartMonotonicTicks)
                    .ThenBy(transition => transition.SourceId, StringComparer.Ordinal)
                    .ToList();
            }
        }

        foreach (var segment in manifest.Segments.Where(segment => segment is not null))
        {
            segment.RelativePath = NormalizeRelativePath(segment.RelativePath);
        }

        foreach (var artifact in manifest.Artifacts.Where(artifact => artifact is not null))
        {
            artifact.RelativePath = NormalizeRelativePath(artifact.RelativePath);
        }

        manifest.Tracks = manifest.Tracks
            .OrderBy(track => track.TrackId, StringComparer.Ordinal)
            .ToList();
        manifest.Segments = manifest.Segments
            .OrderBy(segment => segment.TrackId, StringComparer.Ordinal)
            .ThenBy(segment => segment.StartMonotonicTicks)
            .ThenBy(segment => segment.SequenceNumber)
            .ThenBy(segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
        manifest.Artifacts = manifest.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> ValidateStructure(ReceiptManifest manifest, bool requireSignature)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.ReceiptId))
        {
            issues.Add("Receipt ID is required.");
        }

        if (manifest.CreatedAtUtc == default || manifest.FinalizedAtUtc == default ||
            manifest.FinalizedAtUtc < manifest.CreatedAtUtc)
        {
            issues.Add("Receipt creation and finalization timestamps are invalid.");
        }

        if (manifest.Application is null || manifest.CaptureSettings is null)
        {
            issues.Add("Application and capture settings metadata are required.");
        }

        if (manifest.Tracks is null || manifest.Segments is null || manifest.Artifacts is null)
        {
            issues.Add("Track, segment, and artifact collections are required.");
            return issues;
        }

        if (manifest.Tracks.Any(track => track is null) ||
            manifest.Segments.Any(segment => segment is null) ||
            manifest.Artifacts.Any(artifact => artifact is null))
        {
            issues.Add("Receipt collections cannot contain null entries.");
            return issues;
        }

        if (manifest.Tracks.Count == 0 || manifest.Segments.Count == 0)
        {
            issues.Add("A receipt must contain at least one track and one segment.");
        }

        AddDuplicateIssue(manifest.Tracks.Select(track => track.TrackId), "track ID", issues);
        AddDuplicateIssue(manifest.Segments.Select(segment => segment.SegmentId), "segment ID", issues);
        AddDuplicateIssue(manifest.Artifacts.Select(artifact => artifact.ArtifactId), "artifact ID", issues);

        var trackIds = manifest.Tracks.Select(track => track.TrackId).ToHashSet(StringComparer.Ordinal);
        foreach (var track in manifest.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.TrackId) || string.IsNullOrWhiteSpace(track.SourceKind) ||
                track.Bounds is null || track.Bounds.Width <= 0 || track.Bounds.Height <= 0 ||
                track.DpiX <= 0 || track.DpiY <= 0)
            {
                issues.Add($"Track '{track.TrackId}' has invalid source, bounds, or DPI metadata.");
            }

            if (track.SourceTransitions is null || track.SourceTransitions.Any(transition => transition is null))
            {
                issues.Add($"Track '{track.TrackId}' has an invalid source transition collection.");
            }
        }

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedSequences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in manifest.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.SegmentId) ||
                string.IsNullOrWhiteSpace(segment.TrackId) ||
                !trackIds.Contains(segment.TrackId) ||
                segment.SequenceNumber < 0 ||
                segment.StartMonotonicTicks < 0 ||
                segment.DurationTicks <= 0 ||
                segment.CapturedAtUtc == default)
            {
                issues.Add($"Segment '{segment.SegmentId}' has invalid identity, timing, or track metadata.");
            }

            if (!usedSequences.Add($"{segment.TrackId}\n{segment.SequenceNumber}"))
            {
                issues.Add($"Track '{segment.TrackId}' contains duplicate sequence number {segment.SequenceNumber}.");
            }

            ValidateRelativePath(segment.RelativePath, usedPaths, issues);
            if (requireSignature &&
                (segment.SizeBytes < 0 || !IsSha256(segment.Sha256) ||
                 !IsSha256(segment.PreviousChainSha256) || !IsSha256(segment.ChainSha256)))
            {
                issues.Add($"Segment '{segment.SegmentId}' has invalid signed hash metadata.");
            }
        }

        foreach (var artifact in manifest.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.ArtifactId) || string.IsNullOrWhiteSpace(artifact.Role))
            {
                issues.Add("Every artifact requires an ID and role.");
            }

            ValidateRelativePath(artifact.RelativePath, usedPaths, issues);
            if (requireSignature && (artifact.SizeBytes < 0 || !IsSha256(artifact.Sha256)))
            {
                issues.Add($"Artifact '{artifact.ArtifactId}' has invalid signed hash metadata.");
            }
        }

        if (!IsCanonicallyOrdered(manifest))
        {
            issues.Add("Receipt tracks, segments, artifacts, or source transitions are not in canonical order.");
        }

        if (requireSignature && manifest.Signature is null)
        {
            issues.Add("Receipt signature is required.");
        }

        return issues;
    }

    private static SignatureValidation VerifyManifestSignature(ReceiptManifest manifest)
    {
        var signature = manifest.Signature;
        if (signature is null ||
            !string.Equals(signature.Algorithm, ReceiptSignatureAlgorithms.EcdsaP256Sha256, StringComparison.Ordinal) ||
            !string.Equals(signature.Canonicalization, ReceiptSignatureAlgorithms.CanonicalJsonV1, StringComparison.Ordinal))
        {
            return SignatureValidation.Unverifiable("Receipt signature metadata is missing or unsupported.");
        }

        try
        {
            var publicKey = Convert.FromBase64String(signature.PublicKeySpkiBase64);
            var signatureBytes = Convert.FromBase64String(signature.SignatureBase64);
            var fingerprint = ReceiptDeviceKeyService.ComputeFingerprint(publicKey);
            if (!fingerprint.Equals(signature.KeyFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            {
                return SignatureValidation.Unverifiable("Receipt signer fingerprint does not match the embedded public key.");
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || !ReceiptDeviceKeyService.IsP256(verifier))
            {
                return SignatureValidation.Unverifiable("Receipt signer key is not a valid ECDSA P-256 public key.");
            }

            return verifier.VerifyData(
                ReceiptCanonicalJson.SerializeForSignature(manifest),
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence)
                ? SignatureValidation.Valid()
                : SignatureValidation.Modified("Receipt manifest signature verification failed.");
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or System.Text.Json.JsonException)
        {
            return SignatureValidation.Unverifiable($"Receipt signature could not be evaluated: {ex.Message}");
        }
    }

    private static async Task<FileVerification> VerifyFileAsync(
        string receiptRoot,
        string relativePath,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        string packagePath;
        try
        {
            packagePath = ResolvePackagePath(receiptRoot, relativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException or PathTooLongException)
        {
            return FileVerification.Unverifiable(ex.Message);
        }

        if (!File.Exists(packagePath))
        {
            return FileVerification.Missing();
        }

        try
        {
            EnsureNoReparsePoints(receiptRoot, packagePath);
        }
        catch (InvalidDataException ex)
        {
            return FileVerification.Unverifiable(ex.Message);
        }

        try
        {
            var actual = await HashRequiredFileAsync(packagePath, cancellationToken);
            return actual.SizeBytes == expectedSizeBytes &&
                   actual.Sha256.Equals(expectedSha256, StringComparison.Ordinal)
                ? FileVerification.Intact()
                : FileVerification.Modified();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileVerification.Unverifiable(ex.Message);
        }
    }

    private static void RecordFileResult(
        FileVerification fileResult,
        string relativePath,
        List<string> issues,
        ref bool hasMissingFiles,
        ref bool hasModifiedContent,
        ref bool hasUnverifiableFiles)
    {
        switch (fileResult.Kind)
        {
            case FileVerificationKind.Missing:
                hasMissingFiles = true;
                issues.Add($"Required receipt file '{relativePath}' is missing.");
                break;
            case FileVerificationKind.Modified:
                hasModifiedContent = true;
                issues.Add($"Receipt file '{relativePath}' does not match its signed size or SHA-256 hash.");
                break;
            case FileVerificationKind.Unverifiable:
                hasUnverifiableFiles = true;
                issues.Add($"Receipt file '{relativePath}' could not be verified: {fileResult.Error}");
                break;
        }
    }

    private static void VerifyPackageInventory(
        ReceiptManifest manifest,
        string receiptRoot,
        List<string> issues,
        ref bool hasModifiedContent,
        ref bool hasUnverifiableFiles)
    {
        try
        {
            var expectedPaths = manifest.Segments
                .Select(segment => ResolvePackagePath(receiptRoot, segment.RelativePath))
                .Concat(manifest.Artifacts.Select(artifact =>
                    ResolvePackagePath(receiptRoot, artifact.RelativePath)))
                .Append(Path.Combine(receiptRoot, ManifestFileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var localAnalysisRoot = Path.GetFullPath(Path.Combine(receiptRoot, "local-analysis"));
            var localAnalysisPrefix = localAnalysisRoot + Path.DirectorySeparatorChar;
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(receiptRoot);
            while (pendingDirectories.Count > 0)
            {
                var directory = pendingDirectories.Pop();
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    var fullPath = Path.GetFullPath(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        hasModifiedContent = true;
                        var relativeReparsePath = Path.GetRelativePath(receiptRoot, fullPath).Replace('\\', '/');
                        issues.Add(
                            $"Receipt package entry '{relativeReparsePath}' is a reparse point and cannot be trusted.");
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pendingDirectories.Push(fullPath);
                        continue;
                    }

                    if (expectedPaths.Contains(fullPath))
                    {
                        continue;
                    }

                    if (fullPath.StartsWith(localAnalysisPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hasModifiedContent = true;
                    var relativePath = Path.GetRelativePath(receiptRoot, fullPath).Replace('\\', '/');
                    issues.Add($"Receipt package file '{relativePath}' is not listed in the signed manifest.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            hasUnverifiableFiles = true;
            issues.Add($"Receipt package inventory could not be verified: {ex.Message}");
        }
    }

    private static async Task<FileHash> HashRequiredFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A required receipt file is missing.", path);
        }

        var before = new FileInfo(path);
        var initialSize = before.Length;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var after = new FileInfo(path);
        after.Refresh();
        if (after.Length != initialSize || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw new IOException($"Receipt file '{path}' changed while it was being hashed.");
        }

        return new FileHash(initialSize, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string ComputeChainHash(ReceiptSegmentManifest segment, string previousChainHash)
    {
        var payload = new SegmentChainPayload
        {
            PreviousChainSha256 = previousChainHash,
            SegmentId = segment.SegmentId,
            TrackId = segment.TrackId,
            SequenceNumber = segment.SequenceNumber,
            RelativePath = segment.RelativePath,
            CapturedAtUtc = segment.CapturedAtUtc,
            StartMonotonicTicks = segment.StartMonotonicTicks,
            DurationTicks = segment.DurationTicks,
            SizeBytes = segment.SizeBytes,
            Sha256 = segment.Sha256
        };
        return Convert.ToHexString(SHA256.HashData(ReceiptCanonicalJson.SerializeValue(payload))).ToLowerInvariant();
    }

    private static string ResolvePackagePath(string receiptRoot, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var root = Path.GetFullPath(receiptRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Receipt path '{relativePath}' escapes the receipt package.");
        }

        return candidate;
    }

    private static void EnsureNoReparsePoints(string receiptRoot, string candidate)
    {
        var root = Path.GetFullPath(receiptRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Receipt path '{relative.Replace('\\', '/')}' contains a reparse point.");
            }
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Receipt file paths must be non-empty package-relative paths.");
        }

        var parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidDataException($"Receipt path '{relativePath}' is invalid.");
        }

        return string.Join('/', parts);
    }

    private static void ValidateRelativePath(
        string relativePath,
        HashSet<string> usedPaths,
        List<string> issues)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);
            if (!normalized.Equals(relativePath, StringComparison.Ordinal))
            {
                issues.Add($"Receipt path '{relativePath}' is not normalized.");
            }

            if (!usedPaths.Add(normalized))
            {
                issues.Add($"Receipt path '{relativePath}' is used more than once.");
            }
        }
        catch (InvalidDataException ex)
        {
            issues.Add(ex.Message);
        }
    }

    private static void AddDuplicateIssue(
        IEnumerable<string> values,
        string label,
        List<string> issues)
    {
        if (values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Count())
        {
            issues.Add($"Every {label} must be non-empty and unique.");
        }
    }

    private static bool IsCanonicallyOrdered(ReceiptManifest manifest)
    {
        var tracksOrdered = manifest.Tracks
            .Select(track => track.TrackId)
            .SequenceEqual(manifest.Tracks
                .OrderBy(track => track.TrackId, StringComparer.Ordinal)
                .Select(track => track.TrackId), StringComparer.Ordinal);
        var segmentsOrdered = manifest.Segments.SequenceEqual(manifest.Segments
            .OrderBy(segment => segment.TrackId, StringComparer.Ordinal)
            .ThenBy(segment => segment.StartMonotonicTicks)
            .ThenBy(segment => segment.SequenceNumber)
            .ThenBy(segment => segment.SegmentId, StringComparer.Ordinal));
        var artifactsOrdered = manifest.Artifacts.SequenceEqual(manifest.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.ArtifactId, StringComparer.Ordinal));
        var transitionsOrdered = manifest.Tracks.All(track =>
            track.SourceTransitions is not null &&
            track.SourceTransitions.SequenceEqual(track.SourceTransitions
                .OrderBy(transition => transition.EffectiveStartMonotonicTicks)
                .ThenBy(transition => transition.SourceId, StringComparer.Ordinal)));
        return tracksOrdered && segmentsOrdered && artifactsOrdered && transitionsOrdered;
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ReceiptVerificationResult BuildResult(
        ReceiptVerificationStatus status,
        string receiptId,
        string fingerprint,
        List<string> issues) => new()
    {
        Status = status,
        ReceiptId = receiptId,
        SignerFingerprintSha256 = fingerprint,
        Issues = issues
    };

    private static ReceiptVerificationResult Unverifiable(
        string receiptId,
        string fingerprint,
        string issue) => BuildResult(
            ReceiptVerificationStatus.Unverifiable,
            receiptId,
            fingerprint,
            [issue]);

    private sealed class SegmentChainPayload
    {
        public string PreviousChainSha256 { get; init; } = string.Empty;
        public string SegmentId { get; init; } = string.Empty;
        public string TrackId { get; init; } = string.Empty;
        public long SequenceNumber { get; init; }
        public string RelativePath { get; init; } = string.Empty;
        public DateTimeOffset CapturedAtUtc { get; init; }
        public long StartMonotonicTicks { get; init; }
        public long DurationTicks { get; init; }
        public long SizeBytes { get; init; }
        public string Sha256 { get; init; } = string.Empty;
    }

    private sealed record FileHash(long SizeBytes, string Sha256);

    private enum FileVerificationKind
    {
        Intact,
        Missing,
        Modified,
        Unverifiable
    }

    private sealed record FileVerification(FileVerificationKind Kind, string Error)
    {
        public static FileVerification Intact() => new(FileVerificationKind.Intact, string.Empty);
        public static FileVerification Missing() => new(FileVerificationKind.Missing, string.Empty);
        public static FileVerification Modified() => new(FileVerificationKind.Modified, string.Empty);
        public static FileVerification Unverifiable(string error) => new(FileVerificationKind.Unverifiable, error);
    }

    private sealed record SignatureValidation(ReceiptVerificationStatus? Status, List<string> Issues)
    {
        public static SignatureValidation Valid() => new(null, []);
        public static SignatureValidation Modified(string issue) =>
            new(ReceiptVerificationStatus.Modified, [issue]);
        public static SignatureValidation Unverifiable(string issue) =>
            new(ReceiptVerificationStatus.Unverifiable, [issue]);
    }
}

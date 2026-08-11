using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class FileReplayBufferStorage : IReplaySnapshotPublisher, IReplayBufferFileManager
{
    private static readonly HashSet<string> BufferedFileExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".partial",
            ".tmp",
            ".wav"
        };
    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

    private readonly string _bufferRoot;
    private readonly string _bufferRootPrefix;

    public FileReplayBufferStorage(string bufferRoot)
    {
        if (string.IsNullOrWhiteSpace(bufferRoot))
        {
            throw new ArgumentException("Replay buffer root is required.", nameof(bufferRoot));
        }

        _bufferRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bufferRoot));
        _bufferRootPrefix = _bufferRoot + Path.DirectorySeparatorChar;
    }

    public string BufferRoot => _bufferRoot;

    public async Task<ReplaySnapshotPublishResult> PublishAsync(
        ReplaySnapshotPublication publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.Segments.Count == 0)
        {
            throw new ArgumentException("A replay snapshot must contain at least one segment.", nameof(publication));
        }

        if (string.IsNullOrWhiteSpace(publication.ReceiptId))
        {
            throw new ArgumentException("A replay receipt ID is required.", nameof(publication));
        }

        if (string.IsNullOrWhiteSpace(publication.DestinationDirectory))
        {
            throw new ArgumentException("A replay destination directory is required.", nameof(publication));
        }

        var destination = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(publication.DestinationDirectory));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Replay receipt destination already exists: {destination}");
        }

        var parent = Directory.GetParent(destination)
            ?? throw new IOException($"Replay receipt destination has no parent directory: {destination}");
        Directory.CreateDirectory(parent.FullName);

        var staging = Path.Combine(
            parent.FullName,
            $".{Path.GetFileName(destination)}.staging-{Guid.NewGuid():N}");
        var stagedSegments = new List<StagedSegment>();

        try
        {
            Directory.CreateDirectory(staging);
            var ordered = publication.Segments
                .OrderBy(segment => segment.MonotonicStart)
                .ThenBy(segment => segment.TrackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(segment => segment.SequenceNumber)
                .ThenBy(segment => segment.SegmentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = ordered[index];
                var sourcePath = Path.GetFullPath(segment.FilePath);
                if (!IsBufferedFile(sourcePath))
                {
                    throw new InvalidOperationException(
                        $"Replay segment is outside the owned buffer root: {sourcePath}");
                }

                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"Replay segment file was not found: {sourcePath}",
                        sourcePath);
                }

                var actualByteLength = new FileInfo(sourcePath).Length;
                if (actualByteLength != segment.ByteLength)
                {
                    throw new InvalidDataException(
                        $"Replay segment byte length changed before publication: {sourcePath}");
                }

                var trackFolder = Path.Combine(
                    staging,
                    "segments",
                    SanitizePathComponent(segment.TrackId));
                Directory.CreateDirectory(trackFolder);
                var relativePath = Path.Combine(
                    "segments",
                    SanitizePathComponent(segment.TrackId),
                    $"{index:D6}-{SanitizePathComponent(segment.SegmentId)}.mp4");
                var stagedPath = Path.Combine(staging, relativePath);
                await CopyFinalizedSegmentAsync(sourcePath, stagedPath, cancellationToken)
                    .ConfigureAwait(false);
                stagedSegments.Add(new StagedSegment(segment, relativePath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);

            var published = stagedSegments
                .Select(staged => new ReplayPublishedSegment(
                    staged.Metadata.SegmentId,
                    staged.Metadata.TrackId,
                    staged.RelativePath,
                    Path.Combine(destination, staged.RelativePath),
                    staged.Metadata.ByteLength))
                .ToArray();
            return new ReplaySnapshotPublishResult(
                publication.ReceiptId,
                destination,
                published);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    public bool TryDeleteBufferedSegment(ReplaySegmentMetadata segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        try
        {
            var path = Path.GetFullPath(segment.FilePath);
            if (!IsBufferedFile(path) || !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
        IReadOnlyCollection<string> residentFilePaths,
        TimeSpan minimumAge,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(residentFilePaths);
        if (minimumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Cleanup age cannot be negative.");
        }

        if (!Directory.Exists(_bufferRoot))
        {
            return new ReplayBufferCleanupResult(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        var resident = residentFilePaths
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleted = new List<string>();
        var retained = new List<string>();
        var failures = new List<string>();
        var cutoffUtc = nowUtc.UtcDateTime - minimumAge;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var path in Directory.EnumerateFiles(_bufferRoot, "*", options))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!IsBufferedFile(fullPath))
                {
                    continue;
                }

                if (resident.Contains(fullPath) || File.GetLastWriteTimeUtc(fullPath) > cutoffUtc)
                {
                    retained.Add(fullPath);
                    continue;
                }

                File.Delete(fullPath);
                deleted.Add(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }

        DeleteEmptyDirectories();
        return new ReplayBufferCleanupResult(deleted, retained, failures);
    }

    private bool IsBufferedFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_bufferRootPrefix, StringComparison.OrdinalIgnoreCase) &&
            BufferedFileExtensions.Contains(Path.GetExtension(fullPath));
    }

    private void DeleteEmptyDirectories()
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directory in Directory
                     .EnumerateDirectories(_bufferRoot, "*", options)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A writer may have populated the directory between enumeration and deletion.
            }
        }
    }

    private static async Task CopyFinalizedSegmentAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static string SanitizePathComponent(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "unknown";
        }

        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The incomplete staging path is intentionally never published as a receipt.
        }
    }

    private sealed record StagedSegment(ReplaySegmentMetadata Metadata, string RelativePath);
}

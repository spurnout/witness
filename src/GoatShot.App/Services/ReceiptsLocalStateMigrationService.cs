using System.Text.Json;

namespace GoatShot.App.Services;

/// <summary>
/// Copies the small, durable GoatShot application state into the Receipts local-data root.
/// Media libraries and disposable/runtime directories are intentionally outside the allowlist.
/// Call this before opening settings or the SQLite workspace database.
/// </summary>
public sealed class ReceiptsLocalStateMigrationService
{
    private const long DefaultMaximumIndividualFileBytes = 128L * 1024 * 1024;
    private const long DefaultMaximumTotalBytes = 512L * 1024 * 1024;
    private const long DefaultMaximumPluginIndividualFileBytes = 32L * 1024 * 1024;
    private const long DefaultMaximumPluginTotalBytes = 128L * 1024 * 1024;
    private const int CopyBufferSize = 128 * 1024;

    private static readonly string[] DurableRootFiles =
    [
        "settings.json",
        "workspace-index.json",
        "workspace.sqlite",
        "workspace.sqlite-wal",
        "workspace.sqlite-shm",
        "ai-action-history.json",
        "share-history.json",
        "upload-queue.json",
        "plugin-background-updates.json"
    ];

    private static readonly string[] DurablePluginScheduleFiles =
    [
        "plugin-update-schedule.json",
        "plugin-background-updates-state.json"
    ];

    private static readonly string[] DurableAdbAuthorizationFiles =
    [
        "adbkey.pk8",
        "adbkey.pub"
    ];

    private static readonly HashSet<string> ExcludedPluginDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugin-staging",
        "staging",
        ".staging",
        "temp",
        ".temp",
        "tmp",
        ".tmp"
    };

    private static readonly JsonSerializerOptions MarkerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly long _maximumIndividualFileBytes;
    private readonly long _maximumTotalBytes;
    private readonly long _maximumPluginIndividualFileBytes;
    private readonly long _maximumPluginTotalBytes;

    public ReceiptsLocalStateMigrationService(
        string legacyRoot,
        string receiptsRoot,
        long maximumIndividualFileBytes = DefaultMaximumIndividualFileBytes,
        long maximumTotalBytes = DefaultMaximumTotalBytes,
        long maximumPluginIndividualFileBytes = DefaultMaximumPluginIndividualFileBytes,
        long maximumPluginTotalBytes = DefaultMaximumPluginTotalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptsRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIndividualFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPluginIndividualFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPluginTotalBytes);

        LegacyRoot = Path.GetFullPath(legacyRoot);
        ReceiptsRoot = Path.GetFullPath(receiptsRoot);
        _maximumIndividualFileBytes = maximumIndividualFileBytes;
        _maximumTotalBytes = maximumTotalBytes;
        _maximumPluginIndividualFileBytes = maximumPluginIndividualFileBytes;
        _maximumPluginTotalBytes = maximumPluginTotalBytes;
    }

    public string LegacyRoot { get; }

    public string ReceiptsRoot { get; }

    public string MarkerPath => Path.Combine(ReceiptsRoot, BrandIdentity.LocalStateMigrationMarkerFileName);

    public static ReceiptsLocalStateMigrationService CreateForCurrentUser(
        Func<string, string?>? readVariable = null,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        var receiptsRoot = BrandEnvironment.ResolveLocalRoot(readVariable, getFolderPath).Value;
        var legacyRoot = BrandEnvironment.ResolveLegacyLocalRoot(readVariable, getFolderPath);
        return new ReceiptsLocalStateMigrationService(legacyRoot, receiptsRoot);
    }

    public async Task<LocalStateMigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (PathsEqual(LegacyRoot, ReceiptsRoot))
        {
            return Result(LocalStateMigrationStatus.NotNeeded, []);
        }

        if (File.Exists(MarkerPath))
        {
            return Result(LocalStateMigrationStatus.AlreadyCompleted, []);
        }

        Directory.CreateDirectory(ReceiptsRoot);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var files = new List<LocalStateMigrationFileResult>();

        if (Directory.Exists(LegacyRoot))
        {
            IReadOnlyList<MigrationCandidate> candidates;
            try
            {
                candidates = EnumerateCandidates(files, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                files.Add(new LocalStateMigrationFileResult(
                    ".",
                    LocalStateMigrationFileDisposition.Failed,
                    0,
                    exception.Message));
                return Result(LocalStateMigrationStatus.Failed, files);
            }

            long acceptedBytes = 0;
            long acceptedPluginBytes = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (candidate.Length > _maximumIndividualFileBytes ||
                    candidate.Length > _maximumTotalBytes - acceptedBytes ||
                    (candidate.Kind == MigrationCandidateKind.Plugin &&
                     (candidate.Length > _maximumPluginIndividualFileBytes ||
                      candidate.Length > _maximumPluginTotalBytes - acceptedPluginBytes)))
                {
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        LocalStateMigrationFileDisposition.SkippedSafetyLimit,
                        candidate.Length,
                        "The file exceeds the local-state migration safety limit."));
                    continue;
                }

                try
                {
                    if (IsReparsePoint(candidate.SourcePath))
                    {
                        files.Add(ReparsePointResult(candidate.RelativePath, candidate.Length));
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        LocalStateMigrationFileDisposition.Failed,
                        candidate.Length,
                        exception.Message));
                    continue;
                }

                var destinationPath = Path.Combine(ReceiptsRoot, candidate.RelativePath);
                if (File.Exists(destinationPath))
                {
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        LocalStateMigrationFileDisposition.SkippedExistingDestination,
                        candidate.Length));
                    continue;
                }

                try
                {
                    var maximumCopyBytes = Math.Min(
                        _maximumIndividualFileBytes,
                        _maximumTotalBytes - acceptedBytes);
                    if (candidate.Kind == MigrationCandidateKind.Plugin)
                    {
                        maximumCopyBytes = Math.Min(
                            maximumCopyBytes,
                            Math.Min(
                                _maximumPluginIndividualFileBytes,
                                _maximumPluginTotalBytes - acceptedPluginBytes));
                    }

                    var copy = await CopyAtomicallyWithoutOverwriteAsync(
                        candidate.SourcePath,
                        destinationPath,
                        maximumCopyBytes,
                        cancellationToken).ConfigureAwait(false);
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        copy.Copied
                            ? LocalStateMigrationFileDisposition.Copied
                            : LocalStateMigrationFileDisposition.SkippedExistingDestination,
                        copy.SourceBytes));
                    if (copy.Copied)
                    {
                        acceptedBytes += copy.SourceBytes;
                        if (candidate.Kind == MigrationCandidateKind.Plugin)
                        {
                            acceptedPluginBytes += copy.SourceBytes;
                        }
                    }
                }
                catch (MigrationSafetyLimitException exception)
                {
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        LocalStateMigrationFileDisposition.SkippedSafetyLimit,
                        candidate.Length,
                        exception.Message));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    files.Add(new LocalStateMigrationFileResult(
                        candidate.RelativePath,
                        LocalStateMigrationFileDisposition.Failed,
                        candidate.Length,
                        exception.Message));
                }
            }
        }

        if (files.Any(file => file.Disposition == LocalStateMigrationFileDisposition.Failed))
        {
            return Result(LocalStateMigrationStatus.Failed, files);
        }

        try
        {
            await WriteMarkerWithoutOverwriteAsync(startedAtUtc, files, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(MarkerPath))
            {
                files.Add(new LocalStateMigrationFileResult(
                    BrandIdentity.LocalStateMigrationMarkerFileName,
                    LocalStateMigrationFileDisposition.Failed,
                    0,
                    exception.Message));
                return Result(LocalStateMigrationStatus.Failed, files);
            }
        }

        return Result(LocalStateMigrationStatus.Completed, files);
    }

    private IReadOnlyList<MigrationCandidate> EnumerateCandidates(
        List<LocalStateMigrationFileResult> files,
        CancellationToken cancellationToken)
    {
        var candidates = new List<MigrationCandidate>();
        foreach (var fileName in DurableRootFiles)
        {
            AddFileCandidate(candidates, files, fileName, MigrationCandidateKind.DurableState);
        }

        var secretsRoot = Path.Combine(LegacyRoot, "secrets");
        if (Directory.Exists(secretsRoot))
        {
            if (IsReparsePoint(secretsRoot))
            {
                files.Add(ReparsePointResult("secrets"));
            }
            else
            {
                foreach (var secret in Directory.EnumerateFiles(secretsRoot, "*.dpapi", SearchOption.TopDirectoryOnly)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFileCandidate(
                        candidates,
                        files,
                        Path.Combine("secrets", Path.GetFileName(secret)),
                        MigrationCandidateKind.DurableState);
                }
            }
        }

        AddKnownDirectoryCandidates(
            candidates,
            files,
            "plugin-update-schedule",
            DurablePluginScheduleFiles,
            MigrationCandidateKind.DurableState);
        AddKnownDirectoryCandidates(
            candidates,
            files,
            "adb-authorization",
            DurableAdbAuthorizationFiles,
            MigrationCandidateKind.DurableState);
        AddPluginCandidates(candidates, files, cancellationToken);

        return candidates;
    }

    private void AddKnownDirectoryCandidates(
        List<MigrationCandidate> candidates,
        List<LocalStateMigrationFileResult> files,
        string directoryRelativePath,
        IEnumerable<string> fileNames,
        MigrationCandidateKind kind)
    {
        var sourceRoot = Path.Combine(LegacyRoot, directoryRelativePath);
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        if (IsReparsePoint(sourceRoot))
        {
            files.Add(ReparsePointResult(directoryRelativePath));
            return;
        }

        foreach (var fileName in fileNames)
        {
            AddFileCandidate(candidates, files, Path.Combine(directoryRelativePath, fileName), kind);
        }
    }

    private void AddPluginCandidates(
        List<MigrationCandidate> candidates,
        List<LocalStateMigrationFileResult> files,
        CancellationToken cancellationToken)
    {
        const string pluginsRelativePath = "plugins";
        var pluginsRoot = Path.Combine(LegacyRoot, pluginsRelativePath);
        if (!Directory.Exists(pluginsRoot))
        {
            return;
        }

        if (IsReparsePoint(pluginsRoot))
        {
            files.Add(ReparsePointResult(pluginsRelativePath));
            return;
        }

        AddPluginDirectoryCandidates(
            candidates,
            files,
            pluginsRoot,
            pluginsRelativePath,
            cancellationToken);
    }

    private void AddPluginDirectoryCandidates(
        List<MigrationCandidate> candidates,
        List<LocalStateMigrationFileResult> files,
        string sourceDirectory,
        string relativeDirectory,
        CancellationToken cancellationToken)
    {
        string[] entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            files.Add(new LocalStateMigrationFileResult(
                relativeDirectory,
                LocalStateMigrationFileDisposition.Failed,
                0,
                exception.Message));
            return;
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            var relativePath = Path.Combine(relativeDirectory, name);

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                files.Add(new LocalStateMigrationFileResult(
                    relativePath,
                    LocalStateMigrationFileDisposition.Failed,
                    0,
                    exception.Message));
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                files.Add(ReparsePointResult(relativePath));
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (ExcludedPluginDirectoryNames.Contains(name))
                {
                    files.Add(new LocalStateMigrationFileResult(
                        relativePath,
                        LocalStateMigrationFileDisposition.SkippedTransientData,
                        0,
                        "Plugin staging and temporary directories are not durable migration state."));
                    continue;
                }

                AddPluginDirectoryCandidates(candidates, files, entry, relativePath, cancellationToken);
                continue;
            }

            AddFileCandidate(candidates, files, relativePath, MigrationCandidateKind.Plugin);
        }
    }

    private void AddFileCandidate(
        List<MigrationCandidate> candidates,
        List<LocalStateMigrationFileResult> files,
        string relativePath,
        MigrationCandidateKind kind)
    {
        var sourcePath = Path.Combine(LegacyRoot, relativePath);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            if (IsReparsePoint(sourcePath))
            {
                files.Add(ReparsePointResult(relativePath));
                return;
            }

            candidates.Add(new MigrationCandidate(sourcePath, relativePath, new FileInfo(sourcePath).Length, kind));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            files.Add(new LocalStateMigrationFileResult(
                relativePath,
                LocalStateMigrationFileDisposition.Failed,
                0,
                exception.Message));
        }
    }

    private async Task WriteMarkerWithoutOverwriteAsync(
        DateTimeOffset startedAtUtc,
        IReadOnlyCollection<LocalStateMigrationFileResult> files,
        CancellationToken cancellationToken)
    {
        var marker = new LocalStateMigrationMarker
        {
            Schema = BrandIdentity.LocalStateMigrationSchema,
            SourceProduct = BrandIdentity.LegacyProductName,
            DestinationProduct = BrandIdentity.ProductName,
            SourceRoot = LegacyRoot,
            DestinationRoot = ReceiptsRoot,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CopiedFiles = files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.Copied)
                .Select(file => file.RelativePath)
                .ToArray(),
            SkippedExistingFiles = files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedExistingDestination)
                .Select(file => file.RelativePath)
                .ToArray(),
            SkippedSafetyLimitFiles = files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedSafetyLimit)
                .Select(file => file.RelativePath)
                .ToArray(),
            SkippedReparsePointPaths = files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedReparsePoint)
                .Select(file => file.RelativePath)
                .ToArray(),
            SkippedTransientPaths = files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedTransientData)
                .Select(file => file.RelativePath)
                .ToArray()
        };

        var json = JsonSerializer.Serialize(marker, MarkerJsonOptions);
        await WriteTextAtomicallyWithoutOverwriteAsync(MarkerPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AtomicCopyResult> CopyAtomicallyWithoutOverwriteAsync(
        string sourcePath,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.migration.tmp");

        try
        {
            long copiedBytes;
            using (var source = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       CopyBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       CopyBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[CopyBufferSize];
                copiedBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (read > maximumBytes - copiedBytes)
                    {
                        throw new MigrationSafetyLimitException(
                            "The file grew beyond the local-state migration safety limit while it was copied.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copiedBytes += read;
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                File.Move(temporaryPath, destinationPath);
                return new AtomicCopyResult(true, copiedBytes);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                return new AtomicCopyResult(false, copiedBytes);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task WriteTextAtomicallyWithoutOverwriteAsync(
        string destinationPath,
        string value,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.migration.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, value, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporaryPath, destinationPath);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another startup completed the same idempotent migration.
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale temporary file must not mask the migration result.
        }
    }

    private LocalStateMigrationResult Result(
        LocalStateMigrationStatus status,
        IReadOnlyList<LocalStateMigrationFileResult> files)
    {
        return new LocalStateMigrationResult(status, LegacyRoot, ReceiptsRoot, MarkerPath, files);
    }

    private static LocalStateMigrationFileResult ReparsePointResult(string relativePath, long sourceBytes = 0)
    {
        return new LocalStateMigrationFileResult(
            relativePath,
            LocalStateMigrationFileDisposition.SkippedReparsePoint,
            sourceBytes,
            "Reparse points are not eligible for local-state migration.");
    }

    private sealed record MigrationCandidate(
        string SourcePath,
        string RelativePath,
        long Length,
        MigrationCandidateKind Kind);

    private sealed record AtomicCopyResult(bool Copied, long SourceBytes);

    private enum MigrationCandidateKind
    {
        DurableState,
        Plugin
    }

    private sealed class MigrationSafetyLimitException : IOException
    {
        public MigrationSafetyLimitException(string message)
            : base(message)
        {
        }
    }

    private sealed class LocalStateMigrationMarker
    {
        public string Schema { get; init; } = string.Empty;
        public string SourceProduct { get; init; } = string.Empty;
        public string DestinationProduct { get; init; } = string.Empty;
        public string SourceRoot { get; init; } = string.Empty;
        public string DestinationRoot { get; init; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset CompletedAtUtc { get; init; }
        public IReadOnlyList<string> CopiedFiles { get; init; } = [];
        public IReadOnlyList<string> SkippedExistingFiles { get; init; } = [];
        public IReadOnlyList<string> SkippedSafetyLimitFiles { get; init; } = [];
        public IReadOnlyList<string> SkippedReparsePointPaths { get; init; } = [];
        public IReadOnlyList<string> SkippedTransientPaths { get; init; } = [];
    }
}

public enum LocalStateMigrationStatus
{
    Completed,
    AlreadyCompleted,
    NotNeeded,
    Failed
}

public enum LocalStateMigrationFileDisposition
{
    Copied,
    SkippedExistingDestination,
    SkippedSafetyLimit,
    SkippedReparsePoint,
    SkippedTransientData,
    Failed
}

public sealed record LocalStateMigrationFileResult(
    string RelativePath,
    LocalStateMigrationFileDisposition Disposition,
    long SourceBytes,
    string? Detail = null);

public sealed record LocalStateMigrationResult(
    LocalStateMigrationStatus Status,
    string LegacyRoot,
    string ReceiptsRoot,
    string MarkerPath,
    IReadOnlyList<LocalStateMigrationFileResult> Files)
{
    public bool Succeeded => Status != LocalStateMigrationStatus.Failed;

    public int CopiedFileCount => Files.Count(file => file.Disposition == LocalStateMigrationFileDisposition.Copied);
}

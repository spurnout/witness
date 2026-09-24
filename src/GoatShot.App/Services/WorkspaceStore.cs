using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WorkspaceStore
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".avi",
        ".mkv",
        ".webm"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// The index is rewritten on every capture, so it is compact and leaves per-word OCR boxes to
    /// per-capture files under <see cref="AppPaths.OcrWordsRoot"/>. Reading still accepts inline
    /// words written by older versions; they move out on the next save.
    /// </summary>
    private static readonly JsonSerializerOptions IndexJsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitOcrWordsFromIndex }
        }
    };

    private static readonly JsonSerializerOptions OcrWordsJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private WorkspaceMetadataIndex? _metadataIndex;

    // Persisted state as last read or written, guarded by _gate. Items in it are never handed to
    // callers; LoadCore clones them and SaveCore stores clones, so caller edits stay unsaved until
    // they are passed back in. The stamp detects writes by other processes (CLI, browser host).
    private List<CaptureItem>? _cache;
    private IndexFileStamp _cacheStamp;
    private readonly HashSet<string> _pendingOcrSidecars = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceStore(AppPaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public void AttachMetadataIndex(WorkspaceMetadataIndex metadataIndex)
    {
        _metadataIndex = metadataIndex;
    }

    public IReadOnlyList<CaptureItem> Load()
    {
        lock (_gate)
        {
            try
            {
                return LoadCore().OrderByDescending(item => item.CreatedAt).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                StartupTrace.Write($"Workspace index could not be read: {ex.Message}");
                return [];
            }
        }
    }

    /// <summary>
    /// Rewrites an index that still carries inline OCR words from an older version, so the
    /// one-time move to per-capture word files happens at startup rather than on a capture.
    /// </summary>
    public void CompactLegacyIndex()
    {
        lock (_gate)
        {
            try
            {
                var items = LoadCore();
                if (_pendingOcrSidecars.Count > 0)
                {
                    SaveCore(items);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                StartupTrace.Write($"Workspace index could not be compacted: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Repopulates the search index from the library without holding up startup. A capture,
    /// edit, or delete that lands while the rebuild snapshot is being written invalidates it,
    /// and the rebuild retries so it can never erase a newer search row.
    /// </summary>
    public void RebuildMetadataIndex()
    {
        if (_metadataIndex is not { } index)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            List<CaptureItem> snapshot;
            long generation;
            lock (_gate)
            {
                snapshot = LoadCoreOrEmpty();
                generation = index.Generation;
            }

            if (index.TryRebuild(snapshot, generation))
            {
                return;
            }
        }

        lock (_gate)
        {
            index.Rebuild(LoadCoreOrEmpty());
        }
    }

    private List<CaptureItem> LoadCoreOrEmpty()
    {
        try
        {
            return LoadCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            StartupTrace.Write($"Workspace index could not be read: {ex.Message}");
            return [];
        }
    }

    /// <summary>A working copy of the persisted items that the caller may mutate and save.</summary>
    private List<CaptureItem> LoadCore()
    {
        return CurrentItemsCore().Select(item => item.Clone()).ToList();
    }

    private List<CaptureItem> CurrentItemsCore()
    {
        var stamp = IndexFileStamp.Read(_paths.IndexPath);
        if (_cache is not null && stamp == _cacheStamp)
        {
            return _cache;
        }

        var items = ReadIndexFile();
        HydrateOcrWords(items, _cache);
        _cache = items;
        _cacheStamp = stamp;
        return _cache;
    }

    private List<CaptureItem> ReadIndexFile()
    {
        if (!File.Exists(_paths.IndexPath))
        {
            return [];
        }

        using var stream = new FileStream(_paths.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var items = JsonSerializer.Deserialize<List<CaptureItem>>(stream, IndexJsonOptions)
            ?? throw new JsonException("The workspace index must contain a capture list.");
        if (items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.FilePath)))
        {
            throw new JsonException("The workspace index contains an invalid capture record.");
        }

        return items;
    }

    private void SaveCore(List<CaptureItem> items)
    {
        var persisted = items
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => item.Clone())
            .ToList();
        var previous = _cache?.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, CaptureItem>(StringComparer.OrdinalIgnoreCase);

        // Words first: a failure here leaves the previous index untouched, and a crash between the
        // two writes leaves an index that still points at (or still inlines) valid words.
        var clearedWords = WriteOcrSidecars(persisted, previous);
        AtomicJsonFile.Write(_paths.IndexPath, persisted, IndexJsonOptions);
        _cache = persisted;
        _cacheStamp = IndexFileStamp.Read(_paths.IndexPath);
        _pendingOcrSidecars.Clear();

        var keptIds = persisted.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in clearedWords.Concat(previous.Keys.Where(id => !keptIds.Contains(id))))
        {
            TryDeleteOcrSidecar(id);
        }
    }

    private List<string> WriteOcrSidecars(
        IReadOnlyList<CaptureItem> items,
        IReadOnlyDictionary<string, CaptureItem> previous)
    {
        var cleared = new List<string>();
        foreach (var item in items)
        {
            previous.TryGetValue(item.Id, out var before);
            if (item.OcrWords.Count == 0)
            {
                if (before is { OcrWords.Count: > 0 })
                {
                    cleared.Add(item.Id);
                }

                continue;
            }

            var unchanged = before is not null &&
                !_pendingOcrSidecars.Contains(item.Id) &&
                before.OcrRecognizedAt == item.OcrRecognizedAt &&
                before.OcrWords.SequenceEqual(item.OcrWords, ReferenceEqualityComparer.Instance);
            if (unchanged)
            {
                continue;
            }

            AtomicJsonFile.Write(
                GetOcrSidecarPath(item.Id),
                new OcrWordsSidecar
                {
                    Id = item.Id,
                    RecognizedAt = item.OcrRecognizedAt,
                    Words = item.OcrWords
                },
                OcrWordsJsonOptions,
                flushToDisk: false);
        }

        return cleared;
    }

    /// <summary>
    /// Attaches stored OCR words to freshly read items. Words already held for an unchanged
    /// recognition are reused, so a reload after another process writes the index only reads the
    /// word files that actually changed.
    /// </summary>
    private void HydrateOcrWords(List<CaptureItem> items, List<CaptureItem>? previousItems)
    {
        var previous = previousItems?.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string>? sidecarNames = null;
        foreach (var item in items)
        {
            if (item.OcrWords.Count > 0)
            {
                // Inline words from an older index; the next save moves them to a word file.
                _pendingOcrSidecars.Add(item.Id);
                continue;
            }

            if (item.OcrRecognizedAt is null)
            {
                continue;
            }

            if (previous is not null &&
                previous.TryGetValue(item.Id, out var before) &&
                before.OcrRecognizedAt == item.OcrRecognizedAt &&
                before.OcrWords.Count > 0)
            {
                item.OcrWords = before.OcrWords;
                continue;
            }

            sidecarNames ??= ListOcrSidecarNames();
            var path = GetOcrSidecarPath(item.Id);
            if (!sidecarNames.Contains(Path.GetFileName(path)))
            {
                continue;
            }

            var sidecar = TryReadOcrSidecar(path);
            if (sidecar is not null &&
                sidecar.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) &&
                sidecar.RecognizedAt == item.OcrRecognizedAt)
            {
                item.OcrWords = sidecar.Words;
            }
        }
    }

    private HashSet<string> ListOcrSidecarNames()
    {
        try
        {
            return Directory.Exists(_paths.OcrWordsRoot)
                ? Directory.EnumerateFiles(_paths.OcrWordsRoot, "*.json")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static OcrWordsSidecar? TryReadOcrSidecar(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<OcrWordsSidecar>(stream, OcrWordsJsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Words are rebuildable: live text re-recognizes a capture that has none.
            return null;
        }
    }

    private void TryDeleteOcrSidecar(string id)
    {
        try
        {
            File.Delete(GetOcrSidecarPath(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An orphaned word file is ignored on load because its id no longer exists.
        }
    }

    private string GetOcrSidecarPath(string id)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToUpperInvariant())));
        return Path.Combine(_paths.OcrWordsRoot, $"{hash}.json");
    }

    private static void OmitOcrWordsFromIndex(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(CaptureItem))
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider is System.Reflection.PropertyInfo { Name: nameof(CaptureItem.OcrWords) })
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }

    public async Task<CaptureItem> SaveCaptureAsync(
        CapturedBitmap captured,
        bool privateCapture,
        string? hotkeyProfile = null)
    {
        return await Task.Run(() =>
        {
            var id = FileNameTemplateService.Render(_settings.FileNameTemplate, captured, NextCounter());
            var root = privateCapture ? _paths.TempRoot : _paths.ImagesRoot;
            Directory.CreateDirectory(root);

            string path;
            using (var output = CreateUniqueFile(Path.Combine(root, $"{id}.png")))
            {
                path = output.Name;
                captured.Bitmap.Save(output, ImageFormat.Png);
            }

            // The pixels are still in memory; re-decoding the PNG just written would double the cost.
            var item = BuildItem(path, captured.Kind, captured.Bounds, privateCapture, captured.Source, hotkeyProfile, captured.Bitmap);
            if (!privateCapture)
            {
                AddToIndex(item);
            }

            return item;
        });
    }

    public async Task<CaptureItem> AddImageFileAsync(
        string path,
        CaptureKind kind,
        string? notes = null,
        CaptureSource? source = null,
        string? hotkeyProfile = null)
    {
        return await Task.Run(() =>
        {
            var item = BuildItem(path, kind, null, privateCapture: false, source, hotkeyProfile);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                item.Notes = notes;
            }

            AddToIndex(item);
            return item;
        });
    }

    public async Task<CaptureItem> ImportFileCopyAsync(
        string sourcePath,
        string? notes = null,
        CaptureKind? kind = null,
        CaptureSource? source = null)
    {
        return await Task.Run(async () =>
        {
            var extension = Path.GetExtension(sourcePath);
            var root = extension switch
            {
                _ when ImageExtensions.Contains(extension) => _paths.ImagesRoot,
                _ when VideoExtensions.Contains(extension) => _paths.VideosRoot,
                _ when DocumentExtensions.Contains(extension) => _paths.DocumentsRoot,
                _ => _paths.DocumentsRoot
            };
            Directory.CreateDirectory(root);
            string target;
            await using (var input = File.OpenRead(sourcePath))
            await using (var output = CreateUniqueFile(Path.Combine(root, Path.GetFileName(sourcePath))))
            {
                target = output.Name;
                await input.CopyToAsync(output);
            }

            return await AddImageFileAsync(
                target,
                kind ?? CaptureKind.Imported,
                notes ?? "Imported into the Receipts library.",
                source);
        });
    }

    public Task UpdateItemAsync(CaptureItem item) => UpdateItemsAsync([item]);

    /// <summary>
    /// Batched metadata write: one JSON rewrite regardless of how many items changed. The
    /// background OCR worker persists whole chunks through this so a large backfill stays
    /// O(chunks), not O(items), on the index file. Callers that hold their snapshot open across
    /// real work pass <paramref name="insertMissing"/> false: an item that vanished from the
    /// store in the meantime was deleted by the user and must not be resurrected. Returns the
    /// items that were actually written.
    /// </summary>
    public Task<IReadOnlyList<CaptureItem>> UpdateItemsAsync(
        IReadOnlyList<CaptureItem> items,
        bool insertMissing = true) => UpdateItemsCoreAsync(items, insertMissing, ocrOnly: false);

    /// <summary>Merge background recognition into current records without replacing operator edits.</summary>
    public Task<IReadOnlyList<CaptureItem>> UpdateOcrItemsAsync(IReadOnlyList<CaptureItem> items) =>
        UpdateItemsCoreAsync(items, insertMissing: false, ocrOnly: true);

    private async Task<IReadOnlyList<CaptureItem>> UpdateItemsCoreAsync(
        IReadOnlyList<CaptureItem> items,
        bool insertMissing,
        bool ocrOnly)
    {
        if (items.Count == 0)
        {
            return [];
        }

        return await Task.Run(() =>
        {
            var applied = new List<CaptureItem>();
            lock (_gate)
            {
                var existingItems = LoadCore();
                foreach (var item in items)
                {
                    var index = existingItems.FindIndex(existing =>
                        existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                        existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (index >= 0)
                    {
                        if (ocrOnly)
                        {
                            var current = existingItems[index];
                            if (current.IsPrivate || current.OcrRecognizedAt is not null ||
                                !current.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                                !current.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            current.OcrText = item.OcrText;
                            current.OcrLanguageTag = item.OcrLanguageTag;
                            current.OcrRecognizedAt = item.OcrRecognizedAt;
                            current.OcrWords = item.OcrWords;
                            current.Notes = OcrIndexPolicy.MergeScanNote(current.Notes, SensitiveTextDetector.Scan(item.OcrText).Summary);
                        }
                        else
                        {
                            existingItems[index] = item;
                        }
                    }
                    else if (insertMissing)
                    {
                        index = existingItems.Count;
                        existingItems.Add(item);
                    }
                    else
                    {
                        continue;
                    }

                    applied.Add(existingItems[index]);
                }

                if (applied.Count == 0)
                {
                    return (IReadOnlyList<CaptureItem>)applied;
                }

                SaveCore(existingItems);
                // Keep JSON and search mutations ordered, including against deletes and imports.
                _metadataIndex?.UpsertBatch(applied);
            }

            return (IReadOnlyList<CaptureItem>)applied;
        });
    }

    public async Task<CaptureItem> LinkReceiptDerivativeAsync(
        CaptureItem source,
        CaptureItem derivative,
        string artifactRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(derivative);

        var sourceReceiptId = source.SourceReceiptId;
        if (string.IsNullOrWhiteSpace(sourceReceiptId))
        {
            return derivative;
        }

        derivative.SourceReceiptId = sourceReceiptId;
        derivative.ArtifactRole = string.IsNullOrWhiteSpace(artifactRole)
            ? "edited-image"
            : artifactRole.Trim();
        derivative.IsOriginal = false;
        derivative.SourceAvailable = source.SourceAvailable;
        await UpdateItemAsync(derivative).ConfigureAwait(false);

        var parentLineage = await TryReadDerivativeLineageAsync(source.FilePath, cancellationToken)
            .ConfigureAwait(false);
        var sourceReceiptPath = parentLineage?.SourceReceiptPath;
        if (string.IsNullOrWhiteSpace(sourceReceiptPath))
        {
            sourceReceiptPath = Load()
                .FirstOrDefault(item =>
                    item.Kind == CaptureKind.ReplayReceipt &&
                    item.ReceiptId?.Equals(sourceReceiptId, StringComparison.OrdinalIgnoreCase) == true)
                ?.FilePath ?? string.Empty;
        }

        var lineage = new ReceiptDerivativeLineage
        {
            DerivativeId = derivative.Id,
            SourceReceiptId = sourceReceiptId,
            SourceReceiptPath = sourceReceiptPath,
            ArtifactRole = derivative.ArtifactRole,
            OutputPath = derivative.FilePath,
            ParentDerivativeId = source.Id,
            ParentDerivativePath = source.FilePath,
            SourceSegmentIds = parentLineage?.SourceSegmentIds.ToList() ?? [],
            StartMonotonicTicks = parentLineage?.StartMonotonicTicks,
            EndMonotonicTicks = parentLineage?.EndMonotonicTicks
        };
        await File.WriteAllTextAsync(
                derivative.FilePath + ".receipt-lineage.json",
                JsonSerializer.Serialize(lineage, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
        return derivative;
    }

    public async Task DeleteItemAsync(CaptureItem item, bool deleteFile)
    {
        await Task.Run(() =>
        {
            lock (_gate)
            {
                var items = LoadCore();
                items.RemoveAll(existing =>
                    existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                    existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));

                SaveCore(items);
                _metadataIndex?.Delete(item);
            }

            if (deleteFile && File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
        });
    }

    private CaptureItem BuildItem(
        string path,
        CaptureKind kind,
        CaptureBounds? bounds,
        bool privateCapture,
        CaptureSource? source,
        string? hotkeyProfile = null,
        Image? decodedImage = null)
    {
        var info = new FileInfo(path);
        var width = 0;
        var height = 0;
        string thumbnailPath;

        try
        {
            if (decodedImage is not null)
            {
                width = decodedImage.Width;
                height = decodedImage.Height;
                thumbnailPath = CreateThumbnail(path, decodedImage);
            }
            else
            {
                using var image = Image.FromFile(path);
                width = image.Width;
                height = image.Height;
                thumbnailPath = CreateThumbnail(path, image);
            }
        }
        catch
        {
            thumbnailPath = TryCreateVideoThumbnail(path, out width, out height)
                ? GetThumbnailPath(path)
                : CreatePlaceholderThumbnail(path, kind);
        }

        return new CaptureItem
        {
            Kind = kind,
            CreatedAt = DateTimeOffset.Now,
            FilePath = path,
            ThumbnailPath = thumbnailPath,
            Bounds = bounds,
            Width = width,
            Height = height,
            Bytes = info.Exists ? info.Length : 0,
            IsPrivate = privateCapture,
            SourceApp = source?.ProcessName,
            SourceWindowTitle = source?.WindowTitle,
            SourceMonitorName = source?.MonitorName,
            SourceUrl = source?.SourceUrl,
            HotkeyProfile = HotkeyProfileNames.Normalize(hotkeyProfile),
            Notes = privateCapture
                ? "Private capture: not added to persistent workspace index."
                : $"Local-first capture stored in the workspace library.{Environment.NewLine}Source: {source?.AppLabel ?? "unknown"} / {source?.WindowLabel ?? "untitled"}"
        };
    }

    private static async Task<ReceiptDerivativeLineage?> TryReadDerivativeLineageAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var lineagePath = sourcePath + ".receipt-lineage.json";
        if (!File.Exists(lineagePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                lineagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<ReceiptDerivativeLineage>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string CreateThumbnail(string imagePath, Image image)
    {
        var thumbnailPath = GetThumbnailPath(imagePath);
        Directory.CreateDirectory(_paths.ThumbnailRoot);

        const int maxSide = 320;
        var scale = Math.Min(maxSide / (double)image.Width, maxSide / (double)image.Height);
        scale = Math.Min(scale, 1d);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var thumbnail = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(8, 16, 22));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, 0, 0, width, height);
        thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
        return thumbnailPath;
    }

    private bool TryCreateVideoThumbnail(string videoPath, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!VideoExtensions.Contains(Path.GetExtension(videoPath)))
        {
            return false;
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return false;
        }

        Directory.CreateDirectory(_paths.TempRoot);
        var framePath = Path.Combine(_paths.TempRoot, $"video-thumbnail-{Guid.NewGuid():N}.png");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add("error");
            start.ArgumentList.Add("-nostats");
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-ss");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(videoPath);
            start.ArgumentList.Add("-frames:v");
            start.ArgumentList.Add("1");
            start.ArgumentList.Add("-q:v");
            start.ArgumentList.Add("3");
            start.ArgumentList.Add(framePath);

            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(5_000))
            {
                TryKill(process);
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(framePath))
            {
                return false;
            }

            using var frame = Image.FromFile(framePath);
            width = frame.Width;
            height = frame.Height;
            CreateThumbnail(videoPath, frame);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(framePath))
                {
                    File.Delete(framePath);
                }
            }
            catch
            {
                // Thumbnail extraction should never prevent indexing.
            }
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for a timed-out FFmpeg thumbnail probe.
        }
    }

    private string CreatePlaceholderThumbnail(string filePath, CaptureKind kind)
    {
        var thumbnailPath = GetThumbnailPath(filePath);
        Directory.CreateDirectory(_paths.ThumbnailRoot);

        using var thumbnail = new Bitmap(320, 180);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(8, 16, 22));
        using var borderPen = new Pen(Color.FromArgb(39, 66, 82), 2);
        using var accentBrush = new SolidBrush(Color.FromArgb(48, 230, 195));
        using var textBrush = new SolidBrush(Color.White);
        using var mutedBrush = new SolidBrush(Color.FromArgb(148, 169, 184));
        using var titleFont = new Font("Segoe UI", 18, FontStyle.Bold);
        using var captionFont = new Font("Segoe UI", 10, FontStyle.Regular);
        graphics.DrawRectangle(borderPen, 8, 8, 304, 164);
        graphics.FillRectangle(accentBrush, 24, 28, 52, 52);
        graphics.DrawString(kind.ToString(), titleFont, textBrush, 90, 30);
        graphics.DrawString(Path.GetFileName(filePath), captionFont, mutedBrush, 24, 104);
        thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
        return thumbnailPath;
    }

    private void AddToIndex(CaptureItem item)
    {
        lock (_gate)
        {
            var items = LoadCore();
            items.RemoveAll(existing => existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, item);
            SaveCore(items);
            _metadataIndex?.Upsert(item);
        }
    }

    private string GetThumbnailPath(string filePath)
    {
        var canonicalPath = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPath)));
        return Path.Combine(_paths.ThumbnailRoot, $"{hash}.jpg");
    }

    private int NextCounter()
    {
        lock (_gate)
        {
            try
            {
                // Count the cached items instead of cloning (or re-reading) the whole library.
                return CurrentItemsCore().Count + 1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return 1;
            }
        }
    }

    private static FileStream CreateUniqueFile(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? path
                : i < 10_000 ? Path.Combine(directory, $"{name}-{i}{extension}")
                : Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
            try
            {
                // Reserve the name atomically: parallel captures/imports must never overwrite it.
                return new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 80 or 183)
            {
                // Windows ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS. Other I/O errors propagate.
            }
        }
    }
}

internal readonly record struct IndexFileStamp(bool Exists, long Length, DateTime LastWriteUtc)
{
    public static IndexFileStamp Read(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? new IndexFileStamp(true, info.Length, info.LastWriteTimeUtc)
            : default;
    }
}

internal sealed class OcrWordsSidecar
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset? RecognizedAt { get; set; }
    public List<OcrRecognizedWord> Words { get; set; } = [];
}

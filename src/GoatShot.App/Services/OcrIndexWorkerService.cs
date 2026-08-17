using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// Decision rules for the background OCR indexer, kept pure so batching, gating, and note
/// handling stay testable without a timer or a real OCR engine.
/// </summary>
public static class OcrIndexPolicy
{
    private const string ScanNotePrefix = "Sensitive scan:";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    public static bool IsIndexable(CaptureItem item)
    {
        return !item.IsPrivate &&
            item.OcrRecognizedAt is null &&
            ImageExtensions.Contains(Path.GetExtension(item.FilePath));
    }

    /// <summary>Newest first so fresh captures become searchable before deep history.</summary>
    public static IReadOnlyList<CaptureItem> SelectNextBatch(
        IEnumerable<CaptureItem> items,
        int batchSize,
        IReadOnlySet<string> skippedIds)
    {
        return items
            .Where(IsIndexable)
            .Where(item => !skippedIds.Contains(item.Id))
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Max(1, batchSize))
            .ToList();
    }

    /// <summary>
    /// Backfilled history stays silent for automation: a first launch over a large library must
    /// not fire an OcrCompleted rule (which can share or upload) once per historical capture.
    /// </summary>
    public static bool ShouldRaiseOcrCompleted(CaptureItem item, DateTimeOffset workerStartedAt)
    {
        return item.CreatedAt >= workerStartedAt;
    }

    /// <summary>Replaces any previous scan line the way the manual OCR path does.</summary>
    public static string? MergeScanNote(string? existingNotes, string scanSummary)
    {
        var kept = (existingNotes ?? string.Empty)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.TrimStart().StartsWith(ScanNotePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        kept.Add($"{ScanNotePrefix} {scanSummary}");
        return string.Join(Environment.NewLine, kept);
    }
}

public sealed record OcrIndexPassResult(int Scanned, int Indexed, int Failed, string Message);

/// <summary>
/// Background OCR indexer. The library itself is the queue: any non-private image without
/// OcrRecognizedAt is pending, which makes passes idempotent and needs no durable queue file.
/// Lifecycle mirrors <see cref="UploadQueueWorkerService"/>.
/// </summary>
public sealed class OcrIndexWorkerService : IDisposable
{
    private static readonly TimeSpan FirstDue = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NudgeDue = TimeSpan.FromSeconds(2);
    private const int BatchSize = 3;
    private const int ConsecutiveFailedPassLimit = 3;

    private readonly AppSettings _settings;
    private readonly WorkspaceStore _workspaceStore;
    private readonly Func<string, CancellationToken, Task<OcrRecognitionResult>> _recognizeAsync;
    private readonly Func<CaptureItem, Task>? _onOcrCompletedAsync;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _failedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private System.Threading.Timer? _timer;
    private int _consecutiveFailedPasses;
    private bool _disposed;

    public OcrIndexWorkerService(
        AppSettings settings,
        WorkspaceStore workspaceStore,
        Func<string, CancellationToken, Task<OcrRecognitionResult>> recognizeAsync,
        Func<CaptureItem, Task>? onOcrCompletedAsync = null)
    {
        _settings = settings;
        _workspaceStore = workspaceStore;
        _recognizeAsync = recognizeAsync;
        _onOcrCompletedAsync = onOcrCompletedAsync;
        LastStatus = _settings.EnableOcrIndexing
            ? "OCR indexing is configured but stopped."
            : "OCR indexing is disabled.";
    }

    public event EventHandler<string>? StatusChanged;

    /// <summary>Raised per indexed item with the updated instance, off the UI thread.</summary>
    public event EventHandler<CaptureItem>? ItemIndexed;

    public bool IsRunning { get; private set; }
    public string LastStatus { get; private set; }

    public void Start()
    {
        if (_disposed || !_settings.EnableOcrIndexing)
        {
            Stop("OCR indexing is disabled.");
            return;
        }

        if (IsRunning)
        {
            return;
        }

        _timer = new System.Threading.Timer(OnTimer, null, FirstDue, Period);
        IsRunning = true;
        SetStatus("OCR indexing is running in the background.");
    }

    public void Restart()
    {
        Stop(_settings.EnableOcrIndexing ? "Restarting OCR indexing." : "OCR indexing is disabled.");
        _consecutiveFailedPasses = 0;
        Start();
    }

    public void Stop(string? reason = null)
    {
        _timer?.Dispose();
        _timer = null;
        IsRunning = false;
        SetStatus(reason ?? "OCR indexing stopped.");
    }

    /// <summary>
    /// Pulls the next pass forward (a capture just landed). Never runs OCR on the caller's
    /// thread — the timer callback owns all recognition work.
    /// </summary>
    public void Nudge()
    {
        try
        {
            _timer?.Change(NudgeDue, Period);
        }
        catch (ObjectDisposedException)
        {
            // Stop/Dispose raced the nudge; the worker is going away and owes nothing.
        }
    }

    public async Task<OcrIndexPassResult> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new OcrIndexPassResult(0, 0, 0, "OCR indexing is shut down.");
        }

        if (!_settings.EnableOcrIndexing)
        {
            return new OcrIndexPassResult(0, 0, 0, "OCR indexing is disabled.");
        }

        var entered = false;
        try
        {
            if (!await _runGate.WaitAsync(0, cancellationToken))
            {
                return new OcrIndexPassResult(0, 0, 0, "OCR indexing pass skipped; a previous run is still active.");
            }

            entered = true;
            cancellationToken.ThrowIfCancellationRequested();

            var batch = OcrIndexPolicy.SelectNextBatch(_workspaceStore.Load(), BatchSize, _failedIds);
            if (batch.Count == 0)
            {
                return new OcrIndexPassResult(0, 0, 0, "OCR index is up to date.");
            }

            var indexed = new List<CaptureItem>();
            var failed = 0;
            foreach (var item in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(item.FilePath))
                {
                    _failedIds.Add(item.Id);
                    failed++;
                    continue;
                }

                var result = await _recognizeAsync(item.FilePath, cancellationToken);
                if (!result.Succeeded)
                {
                    _failedIds.Add(item.Id);
                    failed++;
                    continue;
                }

                item.OcrText = result.Text;
                item.OcrLanguageTag = result.LanguageTag;
                item.OcrRecognizedAt = DateTimeOffset.Now;
                item.OcrWords = result.Words.ToList();
                item.Notes = OcrIndexPolicy.MergeScanNote(item.Notes, SensitiveTextDetector.Scan(result.Text).Summary);
                indexed.Add(item);
            }

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CaptureItem> applied = [];
            if (indexed.Count > 0)
            {
                // insertMissing: false — an item deleted while this pass held its snapshot must
                // stay deleted, not come back OCR'd and searchable with its file already gone.
                applied = await _workspaceStore.UpdateItemsAsync(indexed, insertMissing: false);
                foreach (var item in applied)
                {
                    ItemIndexed?.Invoke(this, item);
                    if (_onOcrCompletedAsync is not null &&
                        OcrIndexPolicy.ShouldRaiseOcrCompleted(item, _startedAt))
                    {
                        await _onOcrCompletedAsync(item);
                    }
                }
            }

            TrackPassHealth(applied.Count, failed);
            var message = $"OCR indexed {applied.Count} capture(s); {failed} failed.";
            if (applied.Count > 0)
            {
                SetStatus(message);
            }

            return new OcrIndexPassResult(batch.Count, applied.Count, failed, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (entered && !_disposed)
            {
                try
                {
                    _runGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown raced the release; nothing left to guard.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _timer?.Dispose();
        _timer = null;
        _shutdown.Dispose();
        _runGate.Dispose();
    }

    /// <summary>
    /// A machine where recognition keeps failing outright (no OCR language pack, broken WinRT)
    /// must not hot-loop forever; three all-failure passes in a row parks the worker until the
    /// next launch or settings change.
    /// </summary>
    private void TrackPassHealth(int indexedCount, int failedCount)
    {
        if (indexedCount > 0)
        {
            _consecutiveFailedPasses = 0;
            return;
        }

        if (failedCount == 0)
        {
            return;
        }

        _consecutiveFailedPasses++;
        if (_consecutiveFailedPasses >= ConsecutiveFailedPassLimit)
        {
            Stop("OCR indexing paused: recognition keeps failing on this device.");
        }
    }

    private async void OnTimer(object? state)
    {
        try
        {
            await ProcessOnceAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown; the timer is already being disposed.
        }
        catch (Exception ex)
        {
            SetStatus($"OCR indexing pass failed: {ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        LastStatus = message;
        StatusChanged?.Invoke(this, message);
    }
}

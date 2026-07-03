using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class UploadQueueService : IUploadQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly UploadQueueSettings _settings;
    private readonly object _gate = new();

    public UploadQueueService(AppPaths paths, UploadQueueSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public Task<UploadQueueItem> EnqueueAsync(
        CaptureItem item,
        ShareDestination destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.Now;
        var queueItem = new UploadQueueItem
        {
            CaptureItemId = item.Id,
            FileName = item.FileName,
            FilePath = item.FilePath,
            Bytes = item.Bytes,
            Destination = destination,
            Status = "Queued",
            Attempts = 0,
            MaxAttempts = Math.Max(1, _settings.MaxAttempts),
            CreatedAt = now,
            UpdatedAt = now,
            NextAttemptAt = now,
            LastMessage = "Queued for upload."
        };

        lock (_gate)
        {
            var items = LoadCore();
            items.Insert(0, queueItem);
            SaveCore(Trim(items));
        }

        return Task.FromResult(queueItem);
    }

    public Task<IReadOnlyList<UploadQueueItem>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<UploadQueueItem>>(
                LoadCore()
                    .OrderByDescending(item => item.CreatedAt)
                    .ToList());
        }
    }

    public Task<UploadQueueItem?> CancelAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = LoadCore();
            var item = Find(items, id);
            if (item is null)
            {
                return Task.FromResult<UploadQueueItem?>(null);
            }

            if (IsTerminal(item.Status))
            {
                item.LastMessage = $"Cannot cancel because item is already {item.Status}.";
                item.UpdatedAt = DateTimeOffset.Now;
            }
            else
            {
                item.Status = "Canceled";
                item.LastMessage = "Canceled by operator.";
                item.UpdatedAt = DateTimeOffset.Now;
                item.CompletedAt = item.UpdatedAt;
                item.NextAttemptAt = null;
            }

            SaveCore(items);
            return Task.FromResult<UploadQueueItem?>(item);
        }
    }

    public Task<UploadQueueItem?> RetryAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var items = LoadCore();
            var item = Find(items, id);
            if (item is null)
            {
                return Task.FromResult<UploadQueueItem?>(null);
            }

            var now = DateTimeOffset.Now;
            item.Status = "Queued";
            item.UpdatedAt = now;
            item.CompletedAt = null;
            item.NextAttemptAt = now;
            item.LastMessage = "Requeued by operator.";
            SaveCore(items);
            return Task.FromResult<UploadQueueItem?>(item);
        }
    }

    public async Task<UploadQueueProcessResult> ProcessDueAsync(
        ShareService sharing,
        int maxItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reserved = ReserveDueItems(maxItems);
        cancellationToken.ThrowIfCancellationRequested();
        var processed = await Task.WhenAll(reserved.Select(item => ProcessOneAsync(item, sharing, cancellationToken)));

        return new UploadQueueProcessResult
        {
            Processed = processed.Length,
            Succeeded = processed.Count(item => item.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)),
            Failed = processed.Count(item => item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)),
            WaitingRetry = processed.Count(item => item.Status.Equals("WaitingRetry", StringComparison.OrdinalIgnoreCase)),
            Items = processed
        };
    }

    public string GetStatusSummary()
    {
        return GetDiagnostics().Summary;
    }

    public UploadQueueDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            return UploadQueuePresentation.BuildDiagnostics(LoadCore(), _settings, _paths.UploadQueuePath);
        }
    }

    private static UploadQueueItem? Find(IEnumerable<UploadQueueItem> items, string id)
    {
        return items.FirstOrDefault(item =>
            item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            item.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTerminal(string status)
    {
        return status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) ||
               status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
    }

    private List<UploadQueueItem> ReserveDueItems(int maxItems)
    {
        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            var items = LoadCore();
            var due = items
                .Where(item => IsDue(item, now))
                .OrderBy(item => item.NextAttemptAt ?? item.CreatedAt)
                .Take(Math.Clamp(maxItems, 1, 100))
                .ToList();

            foreach (var item in due)
            {
                item.Status = "Uploading";
                item.Attempts++;
                item.UpdatedAt = now;
                item.NextAttemptAt = null;
                item.LastMessage = $"Uploading attempt {item.Attempts} of {item.MaxAttempts}.";
            }

            if (due.Count > 0)
            {
                SaveCore(items);
            }

            return due.Select(Clone).ToList();
        }
    }

    private async Task<UploadQueueItem> ProcessOneAsync(
        UploadQueueItem reserved,
        ShareService sharing,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(reserved.FilePath))
        {
            return UpdateProcessedItem(
                reserved,
                "Failed",
                $"Source file no longer exists: {reserved.FilePath}",
                completed: true,
                nextAttemptAt: null);
        }

        ShareResult result;
        try
        {
            result = await sharing.ShareAsync(ToCaptureItem(reserved), reserved.Destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new ShareResult
            {
                Succeeded = false,
                Message = $"Upload queue processing failed: {ex.Message}"
            };
        }

        var redactedMessage = ShareService.RedactHistoryText(result.Message);
        if (result.Succeeded)
        {
            return UpdateProcessedItem(
                reserved,
                "Succeeded",
                string.IsNullOrWhiteSpace(redactedMessage) ? "Upload succeeded." : redactedMessage,
                completed: true,
                nextAttemptAt: null);
        }

        if (!_settings.RetryFailedUploads || reserved.Attempts >= reserved.MaxAttempts)
        {
            return UpdateProcessedItem(
                reserved,
                "Failed",
                string.IsNullOrWhiteSpace(redactedMessage) ? "Upload failed." : redactedMessage,
                completed: true,
                nextAttemptAt: null);
        }

        var nextAttemptAt = DateTimeOffset.Now.AddSeconds(BackoffSeconds(reserved.Attempts));
        return UpdateProcessedItem(
            reserved,
            "WaitingRetry",
            $"{(string.IsNullOrWhiteSpace(redactedMessage) ? "Upload failed." : redactedMessage)} Next retry: {nextAttemptAt.LocalDateTime:g}.",
            completed: false,
            nextAttemptAt);
    }

    private UploadQueueItem UpdateProcessedItem(
        UploadQueueItem processed,
        string status,
        string message,
        bool completed,
        DateTimeOffset? nextAttemptAt)
    {
        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            var items = LoadCore();
            var existing = Find(items, processed.Id);
            if (existing is not null && IsTerminal(existing.Status))
            {
                return Clone(existing);
            }

            var item = existing ?? processed;
            item.Status = status;
            item.Attempts = processed.Attempts;
            item.MaxAttempts = Math.Max(1, processed.MaxAttempts);
            item.LastMessage = ShareService.RedactHistoryText(message);
            item.UpdatedAt = now;
            item.CompletedAt = completed ? now : null;
            item.NextAttemptAt = nextAttemptAt;

            if (items.All(existing => !existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
            {
                items.Insert(0, item);
            }

            SaveCore(items);
            return Clone(item);
        }
    }

    private int BackoffSeconds(int attempts)
    {
        var baseSeconds = Math.Max(1, _settings.RetryBackoffSeconds);
        var multiplier = Math.Pow(2, Math.Max(0, attempts - 1));
        return (int)Math.Clamp(baseSeconds * multiplier, 1, 3_600);
    }

    private static bool IsDue(UploadQueueItem item, DateTimeOffset now)
    {
        if (IsTerminal(item.Status) ||
            item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (item.Attempts >= Math.Max(1, item.MaxAttempts))
        {
            return false;
        }

        return item.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
               (item.Status.Equals("WaitingRetry", StringComparison.OrdinalIgnoreCase) &&
                (item.NextAttemptAt is null || item.NextAttemptAt <= now));
    }

    private static CaptureItem ToCaptureItem(UploadQueueItem item)
    {
        return new CaptureItem
        {
            Id = string.IsNullOrWhiteSpace(item.CaptureItemId) ? item.Id : item.CaptureItemId,
            Kind = CaptureKind.Imported,
            CreatedAt = item.CreatedAt,
            FilePath = item.FilePath,
            ThumbnailPath = item.FilePath,
            Bytes = item.Bytes,
            Notes = $"Queued upload {item.Id}"
        };
    }

    private static UploadQueueItem Clone(UploadQueueItem item)
    {
        return new UploadQueueItem
        {
            Id = item.Id,
            CaptureItemId = item.CaptureItemId,
            FileName = item.FileName,
            FilePath = item.FilePath,
            Bytes = item.Bytes,
            Destination = item.Destination,
            Status = item.Status,
            Attempts = item.Attempts,
            MaxAttempts = item.MaxAttempts,
            LastMessage = item.LastMessage,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            NextAttemptAt = item.NextAttemptAt,
            CompletedAt = item.CompletedAt
        };
    }

    private List<UploadQueueItem> LoadCore()
    {
        try
        {
            if (!File.Exists(_paths.UploadQueuePath))
            {
                return new List<UploadQueueItem>();
            }

            var json = File.ReadAllText(_paths.UploadQueuePath);
            return JsonSerializer.Deserialize<List<UploadQueueItem>>(json, JsonOptions) ?? new List<UploadQueueItem>();
        }
        catch
        {
            return new List<UploadQueueItem>();
        }
    }

    private void SaveCore(List<UploadQueueItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.UploadQueuePath)!);
        File.WriteAllText(_paths.UploadQueuePath, JsonSerializer.Serialize(items, JsonOptions));
    }

    private List<UploadQueueItem> Trim(List<UploadQueueItem> items)
    {
        return items
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(_settings.HistoryLimit, 1, 5_000))
            .ToList();
    }
}

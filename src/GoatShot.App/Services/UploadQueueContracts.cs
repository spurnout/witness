using GoatShot.App.Models;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public interface IUploadQueue
{
    Task<UploadQueueItem> EnqueueAsync(CaptureItem item, ShareDestination destination, CancellationToken cancellationToken);
    Task<IReadOnlyList<UploadQueueItem>> ListAsync(CancellationToken cancellationToken);
    Task<UploadQueueItem?> CancelAsync(string id, CancellationToken cancellationToken);
    Task<UploadQueueItem?> RetryAsync(string id, CancellationToken cancellationToken);
    Task<UploadQueueProcessResult> ProcessDueAsync(ShareService sharing, int maxItems, CancellationToken cancellationToken);
}

public sealed class UploadQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CaptureItemId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public ShareDestination Destination { get; set; }
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string LastMessage { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonIgnore]
    public string ShortId => Id[..Math.Min(12, Id.Length)];

    [JsonIgnore]
    public string StatusLabel => UploadQueuePresentation.FormatStatus(this);

    [JsonIgnore]
    public string AttemptLabel => UploadQueuePresentation.FormatAttempts(this);

    [JsonIgnore]
    public string NextAttemptLabel => UploadQueuePresentation.FormatNextAttempt(this);

    [JsonIgnore]
    public string DetailLabel => UploadQueuePresentation.FormatDetail(this);

    [JsonIgnore]
    public bool CanCancel => UploadQueuePresentation.CanCancel(this);

    [JsonIgnore]
    public bool CanRetry => UploadQueuePresentation.CanRetry(this);
}

public sealed class UploadQueueProcessResult
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int WaitingRetry { get; set; }
    public IReadOnlyList<UploadQueueItem> Items { get; set; } = Array.Empty<UploadQueueItem>();

    public string Message => Processed == 0
        ? "No due upload queue items."
        : $"Processed {Processed} queue item(s): succeeded={Succeeded}, failed={Failed}, waiting-retry={WaitingRetry}.";
}

public sealed class UploadQueueDiagnostics
{
    public int TotalItems { get; init; }
    public int DueNow { get; init; }
    public int RetryBackoffSeconds { get; init; }
    public int MaxAttempts { get; init; }
    public int MaxConcurrentUploads { get; init; }
    public bool RetryFailedUploads { get; init; }
    public bool BackgroundProcessingEnabled { get; init; }
    public string QueuePath { get; init; } = string.Empty;
    public DateTimeOffset? NextDueAt { get; init; }
    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();
    public string RedactionNotice { get; init; } = "Queue status text is redacted before it is persisted or displayed.";

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (TotalItems == 0)
            {
                return "Upload queue is enabled and empty.";
            }

            var counts = StatusCounts.Count == 0
                ? "no status counts"
                : string.Join(", ", StatusCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key.ToLowerInvariant()}={pair.Value}"));
            var due = DueNow == 0
                ? "no due items"
                : $"{DueNow} due now";
            var next = NextDueAt is null
                ? "no scheduled retry"
                : $"next retry {NextDueAt.Value.LocalDateTime:g}";

            return $"Upload queue has {TotalItems} item(s): {counts}; {due}; {next}.";
        }
    }
}

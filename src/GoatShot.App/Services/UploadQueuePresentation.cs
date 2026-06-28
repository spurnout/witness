using System.Globalization;

namespace GoatShot.App.Services;

public static class UploadQueuePresentation
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Succeeded",
        "Canceled"
    };

    public static string FormatStatus(UploadQueueItem item)
    {
        return item.Status.Trim().ToLowerInvariant() switch
        {
            "queued" => "Pending",
            "uploading" => "Uploading now",
            "waitingretry" => "Waiting to retry",
            "failed" => "Failed",
            "succeeded" => "Completed",
            "canceled" => "Canceled",
            var value when string.IsNullOrWhiteSpace(value) => "Unknown",
            _ => item.Status
        };
    }

    public static string FormatAttempts(UploadQueueItem item)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Attempts {Math.Max(0, item.Attempts)}/{Math.Max(1, item.MaxAttempts)}");
    }

    public static string FormatNextAttempt(UploadQueueItem item)
    {
        if (item.CompletedAt is not null && TerminalStatuses.Contains(item.Status))
        {
            return $"Completed {item.CompletedAt.Value.LocalDateTime:g}";
        }

        if (item.NextAttemptAt is null)
        {
            return "No retry scheduled";
        }

        var now = DateTimeOffset.Now;
        if (item.NextAttemptAt <= now)
        {
            return "Due now";
        }

        var remaining = item.NextAttemptAt.Value - now;
        var relative = remaining.TotalMinutes >= 1
            ? $"{Math.Ceiling(remaining.TotalMinutes).ToString("0", CultureInfo.InvariantCulture)} min"
            : $"{Math.Ceiling(Math.Max(1, remaining.TotalSeconds)).ToString("0", CultureInfo.InvariantCulture)} sec";

        return $"Next retry {item.NextAttemptAt.Value.LocalDateTime:g} ({relative})";
    }

    public static string FormatDetail(UploadQueueItem item)
    {
        var parts = new List<string>
        {
            FormatStatus(item),
            FormatAttempts(item),
            FormatNextAttempt(item)
        };

        if (!string.IsNullOrWhiteSpace(item.LastMessage))
        {
            parts.Add(ShareService.RedactHistoryText(item.LastMessage));
        }

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static bool CanCancel(UploadQueueItem item)
    {
        return !TerminalStatuses.Contains(item.Status) &&
               !item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanRetry(UploadQueueItem item)
    {
        return item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
               item.Status.Equals("WaitingRetry", StringComparison.OrdinalIgnoreCase) ||
               item.Status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
    }

    public static UploadQueueDiagnostics BuildDiagnostics(
        IReadOnlyList<UploadQueueItem> items,
        Models.UploadQueueSettings settings,
        string queuePath)
    {
        var now = DateTimeOffset.Now;
        var statusCounts = items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "Unknown" : item.Status.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var dueItems = items
            .Where(item => IsDue(item, now))
            .ToList();

        var nextDue = items
            .Where(item => !IsTerminal(item.Status))
            .Select(item => item.NextAttemptAt)
            .Where(value => value is not null)
            .Min();

        return new UploadQueueDiagnostics
        {
            TotalItems = items.Count,
            DueNow = dueItems.Count,
            RetryBackoffSeconds = Math.Max(1, settings.RetryBackoffSeconds),
            MaxAttempts = Math.Max(1, settings.MaxAttempts),
            MaxConcurrentUploads = Math.Max(1, settings.MaxConcurrentUploads),
            RetryFailedUploads = settings.RetryFailedUploads,
            BackgroundProcessingEnabled = settings.EnableBackgroundProcessing,
            QueuePath = queuePath,
            NextDueAt = nextDue,
            StatusCounts = statusCounts
        };
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

    private static bool IsTerminal(string status)
    {
        return TerminalStatuses.Contains(status);
    }
}

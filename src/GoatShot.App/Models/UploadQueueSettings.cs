namespace GoatShot.App.Models;

public sealed class UploadQueueSettings
{
    public bool EnableQueue { get; set; } = true;
    public bool EnableBackgroundProcessing { get; set; }
    public bool RetryFailedUploads { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 10;
    public int MaxConcurrentUploads { get; set; } = 2;
    public int BackgroundPollSeconds { get; set; } = 30;
    public bool PersistQueueAcrossRestarts { get; set; } = true;
    public int HistoryLimit { get; set; } = 500;
}

using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class UploadQueueWorkerService : IDisposable
{
    private readonly UploadQueueSettings _settings;
    private readonly UploadQueueService _queue;
    private readonly ShareService _sharing;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public UploadQueueWorkerService(
        UploadQueueSettings settings,
        UploadQueueService queue,
        ShareService sharing)
    {
        _settings = settings;
        _queue = queue;
        _sharing = sharing;
        LastStatus = _settings.EnableBackgroundProcessing
            ? "Background upload queue worker is configured but stopped."
            : "Background upload queue worker is disabled.";
    }

    public event EventHandler<string>? StatusChanged;

    public bool IsRunning { get; private set; }
    public string LastStatus { get; private set; }

    public void Start()
    {
        if (_disposed || !_settings.EnableQueue || !_settings.EnableBackgroundProcessing)
        {
            Stop("Background upload queue worker is disabled.");
            return;
        }

        if (IsRunning)
        {
            return;
        }

        var interval = PollInterval();
        _timer = new System.Threading.Timer(
            OnTimer,
            null,
            TimeSpan.FromSeconds(3),
            interval);
        IsRunning = true;
        SetStatus($"Background upload queue worker running every {interval.TotalSeconds:0} second(s).");
    }

    public void Restart()
    {
        Stop(_settings.EnableBackgroundProcessing
            ? "Restarting background upload queue worker."
            : "Background upload queue worker is disabled.");
        Start();
    }

    public void Stop(string? reason = null)
    {
        _timer?.Dispose();
        _timer = null;
        IsRunning = false;
        SetStatus(reason ?? "Background upload queue worker stopped.");
    }

    public async Task<UploadQueueProcessResult> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new UploadQueueProcessResult();
        }

        if (!_settings.EnableQueue)
        {
            SetStatus("Upload queue is disabled.");
            return new UploadQueueProcessResult();
        }

        var entered = false;
        try
        {
            if (!await _runGate.WaitAsync(0, cancellationToken))
            {
                SetStatus("Background upload queue worker skipped because a previous run is still active.");
                return new UploadQueueProcessResult();
            }

            entered = true;

            var result = await _queue.ProcessDueAsync(
                _sharing,
                Math.Max(1, _settings.MaxConcurrentUploads),
                cancellationToken);

            if (result.Processed > 0)
            {
                SetStatus(result.Message);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return new UploadQueueProcessResult();
        }
        catch (Exception ex)
        {
            SetStatus($"Background upload queue worker failed: {ex.Message}");
            return new UploadQueueProcessResult();
        }
        finally
        {
            if (entered)
            {
                try
                {
                    _runGate.Release();
                }
                catch (ObjectDisposedException) when (_disposed)
                {
                }
            }
        }
    }

    public string GetStatusSummary()
    {
        return IsRunning
            ? LastStatus
            : _settings.EnableBackgroundProcessing
                ? "Background upload queue worker is stopped."
                : "Background upload queue worker is disabled.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _runGate.Dispose();
    }

    private void OnTimer(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _ = ProcessTimerAsync();
    }

    private async Task ProcessTimerAsync()
    {
        try
        {
            await ProcessOnceAsync();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                SetStatus($"Background upload queue worker failed: {ex.Message}");
            }
        }
    }

    private TimeSpan PollInterval()
    {
        return TimeSpan.FromSeconds(Math.Clamp(_settings.BackgroundPollSeconds, 5, 3_600));
    }

    private void SetStatus(string message)
    {
        LastStatus = message;
        StatusChanged?.Invoke(this, message);
    }
}

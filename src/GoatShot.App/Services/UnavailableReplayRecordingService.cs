using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class UnavailableReplayRecordingService : IReplayRecordingService
{
    private readonly string _reason;
    private ReplayBufferStatus _status;

    public UnavailableReplayRecordingService(string reason)
    {
        _reason = string.IsNullOrWhiteSpace(reason)
            ? "No compatible local Media Foundation encoder is available."
            : reason.Trim();
        _status = new ReplayBufferStatus(ReplayBufferState.Off, 0, 0, TimeSpan.Zero, null);
    }

    public event EventHandler<ReplayRecordingStatusChangedEventArgs>? StatusChanged;

    public ReplayBufferStatus GetStatus() => _status;

    public Task<ReplayCommandResult> ArmAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _status = _status with { State = ReplayBufferState.Error, LastError = _reason };
        Publish(_reason);
        return Task.FromResult(new ReplayCommandResult(false, ReplayBufferState.Error, _reason));
    }

    public ReplayCommandResult Pause() => Failure("Replay is unavailable and cannot be paused.");
    public ReplayCommandResult Resume() => Failure("Replay is unavailable and cannot be resumed.");
    public ReplayCommandResult SuspendForSystemEvent() =>
        Success("Replay is unavailable; no capture was active when the Windows session suspended.");
    public ReplayCommandResult ResumeAfterSystemEvent() =>
        Success("Windows session resumed; Replay remains unavailable.");

    public Task<ReplayCommandResult> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _status = new ReplayBufferStatus(ReplayBufferState.Off, 0, 0, TimeSpan.Zero, null);
        var result = new ReplayCommandResult(true, ReplayBufferState.Off, "Replay is off.");
        Publish(result.Message);
        return Task.FromResult(result);
    }

    public Task<ReplaySaveResult> SaveAsync(
        ReplaySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ReplaySaveResult(
            false,
            _reason,
            null,
            null,
            [],
            false,
            _status.State));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ReplayCommandResult Failure(string message)
    {
        var result = new ReplayCommandResult(false, _status.State, message);
        Publish(message);
        return result;
    }

    private ReplayCommandResult Success(string message)
    {
        var result = new ReplayCommandResult(true, _status.State, message);
        Publish(message);
        return result;
    }

    private void Publish(string message) => StatusChanged?.Invoke(
        this,
        new ReplayRecordingStatusChangedEventArgs(_status, message, DateTimeOffset.UtcNow));
}

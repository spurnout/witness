using GoatShot.App.Models;

namespace GoatShot.App.Services;

public interface IReplaySnapshotPublisher
{
    Task<ReplaySnapshotPublishResult> PublishAsync(
        ReplaySnapshotPublication publication,
        CancellationToken cancellationToken);
}

public interface IReplayBufferFileManager
{
    bool TryDeleteBufferedSegment(ReplaySegmentMetadata segment);

    ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
        IReadOnlyCollection<string> residentFilePaths,
        TimeSpan minimumAge,
        DateTimeOffset nowUtc);
}

public interface IReplayBufferCoordinator
{
    ReplayBufferSettings Settings { get; }
    ReplayBufferStatus GetStatus();
    ReplayCommandResult Arm();
    ReplayCommandResult Pause();
    ReplayCommandResult PauseAfterCurrentCapture();
    ReplayCommandResult Resume();
    ReplayCommandResult Stop();
    ReplayCommandResult ReportError(string message);
    ReplaySegmentAddResult AddFinalizedSegment(ReplaySegmentMetadata segment);
    ReplaySegmentAddResult AddFinalizedSegments(IReadOnlyList<ReplaySegmentMetadata> segments);
    Task<ReplaySaveResult> SaveAsync(ReplaySaveRequest request, CancellationToken cancellationToken);

    ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
        TimeSpan minimumAge,
        DateTimeOffset nowUtc);
}

public sealed record ReplaySnapshotPublication(
    string ReceiptId,
    string DestinationDirectory,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ReplaySegmentMetadata> Segments);

public sealed class ReplayBufferCoordinator : IReplayBufferCoordinator
{
    private readonly object _gate = new();
    private readonly ReplayBufferSettings _settings;
    private readonly IReplaySnapshotPublisher _publisher;
    private readonly IReplayBufferFileManager _fileManager;
    private readonly ReplaySegmentCatalog _catalog;
    private ReplayBufferState _state = ReplayBufferState.Off;
    private ReplayBufferState _stateAfterSave = ReplayBufferState.Off;
    private bool _acceptSegmentsWhileSaving;
    private bool _acceptFinalCaptureSetWhilePaused;
    private string? _lastError;

    public ReplayBufferCoordinator(
        ReplayBufferSettings? settings,
        IReplaySnapshotPublisher publisher,
        IReplayBufferFileManager fileManager)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(fileManager);

        _settings = (settings ?? new ReplayBufferSettings()).Normalize();
        _publisher = publisher;
        _fileManager = fileManager;
        _catalog = new ReplaySegmentCatalog(_settings, segment =>
        {
            _fileManager.TryDeleteBufferedSegment(segment);
        });
    }

    public ReplayBufferSettings Settings => _settings.Normalize();

    public ReplayBufferStatus GetStatus()
    {
        lock (_gate)
        {
            var catalog = _catalog.GetSnapshot();
            return new ReplayBufferStatus(
                _state,
                catalog.Segments.Count,
                catalog.TotalBytes,
                catalog.BufferedDuration,
                _lastError);
        }
    }

    public ReplayCommandResult Arm()
    {
        lock (_gate)
        {
            if (_state == ReplayBufferState.Saving)
            {
                return Failure("Replay cannot be armed while a save is in progress.");
            }

            _state = ReplayBufferState.Armed;
            _acceptFinalCaptureSetWhilePaused = false;
            _lastError = null;
            return Success("Replay buffer armed.");
        }
    }

    public ReplayCommandResult Pause()
    {
        return PauseCore(acceptFinalCaptureSet: false);
    }

    public ReplayCommandResult PauseAfterCurrentCapture()
    {
        return PauseCore(acceptFinalCaptureSet: true);
    }

    private ReplayCommandResult PauseCore(bool acceptFinalCaptureSet)
    {
        lock (_gate)
        {
            if (_state == ReplayBufferState.Paused)
            {
                _acceptFinalCaptureSetWhilePaused |= acceptFinalCaptureSet;
                return Success("Replay buffer is already paused.");
            }

            if (_state != ReplayBufferState.Armed)
            {
                return Failure("Replay can only be paused while armed.");
            }

            _state = ReplayBufferState.Paused;
            _acceptFinalCaptureSetWhilePaused = acceptFinalCaptureSet;
            return Success("Replay buffer paused.");
        }
    }

    public ReplayCommandResult Resume()
    {
        lock (_gate)
        {
            if (_state == ReplayBufferState.Armed)
            {
                return Success("Replay buffer is already armed.");
            }

            if (_state != ReplayBufferState.Paused)
            {
                return Failure("Replay can only be resumed while paused.");
            }

            _state = ReplayBufferState.Armed;
            _acceptFinalCaptureSetWhilePaused = false;
            return Success("Replay buffer resumed.");
        }
    }

    public ReplayCommandResult Stop()
    {
        lock (_gate)
        {
            if (_state == ReplayBufferState.Saving)
            {
                return Failure("Replay cannot be stopped while a save is in progress.");
            }

            _catalog.Clear();
            _state = ReplayBufferState.Off;
            _acceptFinalCaptureSetWhilePaused = false;
            _lastError = null;
            return Success("Replay buffer stopped and unsaved segments released.");
        }
    }

    public ReplayCommandResult ReportError(string message)
    {
        lock (_gate)
        {
            _lastError = string.IsNullOrWhiteSpace(message)
                ? "Replay buffer encountered an unknown error."
                : message.Trim();
            _state = ReplayBufferState.Error;
            _acceptSegmentsWhileSaving = false;
            _acceptFinalCaptureSetWhilePaused = false;
            return Failure(_lastError);
        }
    }

    public ReplaySegmentAddResult AddFinalizedSegment(ReplaySegmentMetadata segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return AddFinalizedSegments([segment]);
    }

    public ReplaySegmentAddResult AddFinalizedSegments(
        IReadOnlyList<ReplaySegmentMetadata> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        lock (_gate)
        {
            var acceptsPausedCaptureSet =
                _state == ReplayBufferState.Paused && _acceptFinalCaptureSetWhilePaused;
            var acceptsSegment = _state == ReplayBufferState.Armed ||
                (_state == ReplayBufferState.Saving && _acceptSegmentsWhileSaving) ||
                acceptsPausedCaptureSet;
            if (!acceptsSegment)
            {
                return ReplaySegmentAddResult.Rejected(
                    $"Replay capture set was ignored while the buffer state was {_state}.");
            }

            if (acceptsPausedCaptureSet)
            {
                // The producer may publish exactly the synchronized set that was
                // already in flight when Pause was requested. Never leave a broad
                // "accept while paused" window for a later capture set.
                _acceptFinalCaptureSetWhilePaused = false;
            }

            try
            {
                return _catalog.AddCaptureSet(segments);
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException or OverflowException)
            {
                return ReplaySegmentAddResult.Rejected(ex.Message);
            }
        }
    }

    public async Task<ReplaySaveResult> SaveAsync(
        ReplaySaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DestinationDirectory))
        {
            return FailedSave("A replay receipt destination directory is required.");
        }

        var duration = request.Duration ?? _settings.BufferDuration;
        if (duration <= TimeSpan.Zero)
        {
            return FailedSave("Replay save duration must be positive.");
        }

        ReplayBufferState returnState;
        lock (_gate)
        {
            if (_state is not (ReplayBufferState.Armed or ReplayBufferState.Paused))
            {
                return FailedSave($"Replay cannot be saved while the buffer state is {_state}.");
            }

            returnState = _state;
            _stateAfterSave = returnState;
            _acceptSegmentsWhileSaving = returnState == ReplayBufferState.Armed;
            _state = ReplayBufferState.Saving;
        }

        using var snapshot = _catalog.AcquireSnapshot(duration);
        if (snapshot.Segments.Count == 0)
        {
            RestoreStateAfterSave(returnState);
            return FailedSave("Replay buffer does not contain any finalized segments.");
        }

        var receiptId = string.IsNullOrWhiteSpace(request.ReceiptId)
            ? Guid.NewGuid().ToString("N")
            : request.ReceiptId.Trim();
        var publication = new ReplaySnapshotPublication(
            receiptId,
            request.DestinationDirectory,
            DateTimeOffset.UtcNow,
            snapshot.Segments);

        try
        {
            var published = await _publisher
                .PublishAsync(publication, cancellationToken)
                .ConfigureAwait(false);
            var restoredState = RestoreStateAfterSave(returnState);
            return new ReplaySaveResult(
                true,
                $"Saved replay receipt with {published.Segments.Count} segment(s).",
                published.ReceiptId,
                published.PackagePath,
                published.Segments,
                restoredState == ReplayBufferState.Armed,
                restoredState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var restoredState = RestoreStateAfterSave(returnState);
            return new ReplaySaveResult(
                false,
                "Replay save was canceled.",
                receiptId,
                null,
                Array.Empty<ReplayPublishedSegment>(),
                restoredState == ReplayBufferState.Armed,
                restoredState);
        }
        catch (Exception ex)
        {
            var message = $"Replay save failed: {ex.Message}";
            var restoredState = RestoreStateAfterSave(returnState, message);
            lock (_gate)
            {
                return new ReplaySaveResult(
                    false,
                    message,
                    receiptId,
                    null,
                    Array.Empty<ReplayPublishedSegment>(),
                    restoredState == ReplayBufferState.Armed,
                    restoredState);
            }
        }
    }

    public ReplayBufferCleanupResult CleanupAbandonedBufferFiles(
        TimeSpan minimumAge,
        DateTimeOffset nowUtc)
    {
        if (minimumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Cleanup age cannot be negative.");
        }

        IReadOnlyList<string> residentFilePaths;
        lock (_gate)
        {
            residentFilePaths = _catalog.GetResidentFilePaths();
        }

        return _fileManager.CleanupAbandonedBufferFiles(
            residentFilePaths,
            minimumAge,
            nowUtc);
    }

    private ReplayBufferState RestoreStateAfterSave(
        ReplayBufferState expectedState,
        string? lastError = null)
    {
        lock (_gate)
        {
            if (_state == ReplayBufferState.Saving && _stateAfterSave == expectedState)
            {
                _state = expectedState;
                _acceptSegmentsWhileSaving = false;
                _acceptFinalCaptureSetWhilePaused = false;
            }

            _lastError = lastError;

            return _state;
        }
    }

    private ReplayCommandResult Success(string message) => new(true, _state, message);
    private ReplayCommandResult Failure(string message) => new(false, _state, message);

    private ReplaySaveResult FailedSave(string message)
    {
        lock (_gate)
        {
            return new ReplaySaveResult(
                false,
                message,
                null,
                null,
                Array.Empty<ReplayPublishedSegment>(),
                _state == ReplayBufferState.Armed,
                _state);
        }
    }
}

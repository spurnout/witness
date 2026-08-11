using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayWindowsLifecycleControllerTests
{
    [TestMethod]
    public void OverlappingLockPowerAndDisplayEvents_ResumeOnlyAfterEveryBoundaryClears()
    {
        var replay = new TrackingReplayRecordingService(ReplayBufferState.Armed);
        var events = new TestReplayWindowsLifecycleEventSource();
        using var controller = new ReplayWindowsLifecycleController(() => replay, events);

        controller.Start();
        controller.Start();
        events.Raise(ReplayWindowsLifecycleEventKind.SessionLocked);
        events.Raise(ReplayWindowsLifecycleEventKind.PowerSuspending);

        Assert.AreEqual(1, replay.SystemSuspendCalls);
        Assert.AreEqual(0, replay.SystemResumeCalls);
        Assert.AreEqual(
            ReplayWindowsSuspensionReason.SessionLocked |
                ReplayWindowsSuspensionReason.PowerSuspended,
            controller.ActiveReasons);

        events.Raise(ReplayWindowsLifecycleEventKind.SessionUnlocked);
        Assert.AreEqual(0, replay.SystemResumeCalls);
        Assert.AreEqual(
            ReplayWindowsSuspensionReason.PowerSuspended,
            controller.ActiveReasons);

        events.Raise(ReplayWindowsLifecycleEventKind.PowerResumed);
        Assert.AreEqual(1, replay.SystemResumeCalls);
        Assert.AreEqual(ReplayWindowsSuspensionReason.None, controller.ActiveReasons);
        Assert.AreEqual(ReplayBufferState.Armed, replay.GetStatus().State);

        events.Raise(ReplayWindowsLifecycleEventKind.DisplayChanging);
        Assert.IsTrue(replay.GetStatus().SystemSuspended);
        events.Raise(ReplayWindowsLifecycleEventKind.DisplayChanged);

        Assert.AreEqual(2, replay.SystemSuspendCalls);
        Assert.AreEqual(2, replay.SystemResumeCalls);
        Assert.IsFalse(replay.GetStatus().SystemSuspended);
        Assert.AreEqual(1, events.StartCalls);
        Assert.AreEqual(0, replay.ArmCalls);
        Assert.AreEqual(0, replay.UserResumeCalls);
    }

    [TestMethod]
    [DataRow(ReplayBufferState.Off)]
    [DataRow(ReplayBufferState.Paused)]
    public void LockUnlock_DoesNotAccidentallyArmOffOrUserPausedReplay(
        ReplayBufferState initialState)
    {
        var replay = new TrackingReplayRecordingService(initialState);
        var events = new TestReplayWindowsLifecycleEventSource();
        using var controller = new ReplayWindowsLifecycleController(() => replay, events);
        controller.Start();

        events.Raise(ReplayWindowsLifecycleEventKind.SessionLocked);
        Assert.IsTrue(replay.GetStatus().SystemSuspended);
        events.Raise(ReplayWindowsLifecycleEventKind.SessionUnlocked);

        Assert.AreEqual(initialState, replay.GetStatus().State);
        Assert.IsFalse(replay.GetStatus().SystemSuspended);
        Assert.AreEqual(0, replay.ArmCalls);
        Assert.AreEqual(0, replay.UserResumeCalls);
        Assert.AreEqual(1, replay.SystemSuspendCalls);
        Assert.AreEqual(1, replay.SystemResumeCalls);
    }

    [TestMethod]
    public void ActiveBoundary_SuspendsReplacementReplayAndDisposeUnsubscribes()
    {
        var first = new TrackingReplayRecordingService(ReplayBufferState.Armed);
        var second = new TrackingReplayRecordingService(ReplayBufferState.Armed);
        IReplayRecordingService current = first;
        var events = new TestReplayWindowsLifecycleEventSource();
        var controller = new ReplayWindowsLifecycleController(() => current, events);
        controller.Start();
        events.Raise(ReplayWindowsLifecycleEventKind.SessionLocked);

        current = second;
        controller.RefreshCurrentReplay();

        Assert.AreEqual(1, first.SystemSuspendCalls);
        Assert.AreEqual(1, second.SystemSuspendCalls);
        Assert.IsTrue(second.GetStatus().SystemSuspended);

        controller.Dispose();
        events.Raise(ReplayWindowsLifecycleEventKind.SessionUnlocked);

        Assert.AreEqual(0, first.SystemResumeCalls);
        Assert.AreEqual(0, second.SystemResumeCalls);
        Assert.AreEqual(1, events.DisposeCalls);
        Assert.AreEqual(0, events.SubscriberCount);
    }

    private sealed class TestReplayWindowsLifecycleEventSource : IReplayWindowsLifecycleEventSource
    {
        private EventHandler<ReplayWindowsLifecycleEventArgs>? _eventOccurred;

        public int StartCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int SubscriberCount => _eventOccurred?.GetInvocationList().Length ?? 0;

        public event EventHandler<ReplayWindowsLifecycleEventArgs>? EventOccurred
        {
            add => _eventOccurred += value;
            remove => _eventOccurred -= value;
        }

        public void Start() => StartCalls++;

        public void Raise(ReplayWindowsLifecycleEventKind kind) =>
            _eventOccurred?.Invoke(this, new ReplayWindowsLifecycleEventArgs(kind));

        public void Dispose()
        {
            DisposeCalls++;
            _eventOccurred = null;
        }
    }

    private sealed class TrackingReplayRecordingService : IReplayRecordingService
    {
        private ReplayBufferState _state;
        private bool _systemSuspended;

        public TrackingReplayRecordingService(ReplayBufferState initialState)
        {
            _state = initialState;
        }

        public int ArmCalls { get; private set; }
        public int UserResumeCalls { get; private set; }
        public int SystemSuspendCalls { get; private set; }
        public int SystemResumeCalls { get; private set; }

        public event EventHandler<ReplayRecordingStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public ReplayBufferStatus GetStatus() =>
            new(_state, 0, 0, TimeSpan.Zero, null, _systemSuspended);

        public Task<ReplayCommandResult> ArmAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArmCalls++;
            _state = ReplayBufferState.Armed;
            return Task.FromResult(Result("armed"));
        }

        public ReplayCommandResult Pause()
        {
            _state = ReplayBufferState.Paused;
            return Result("paused");
        }

        public ReplayCommandResult Resume()
        {
            UserResumeCalls++;
            _state = ReplayBufferState.Armed;
            return Result("resumed");
        }

        public ReplayCommandResult SuspendForSystemEvent()
        {
            SystemSuspendCalls++;
            _systemSuspended = true;
            return Result("system suspended");
        }

        public ReplayCommandResult ResumeAfterSystemEvent()
        {
            SystemResumeCalls++;
            _systemSuspended = false;
            return Result("system resumed");
        }

        public Task<ReplayCommandResult> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = ReplayBufferState.Off;
            return Task.FromResult(Result("stopped"));
        }

        public Task<ReplaySaveResult> SaveAsync(
            ReplaySaveRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReplaySaveResult(
                false,
                "not used",
                null,
                null,
                [],
                _state == ReplayBufferState.Armed,
                _state));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private ReplayCommandResult Result(string message) => new(true, _state, message);
    }
}

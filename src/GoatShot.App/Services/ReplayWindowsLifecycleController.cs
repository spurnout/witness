using Microsoft.Win32;

namespace GoatShot.App.Services;

[Flags]
internal enum ReplayWindowsSuspensionReason
{
    None = 0,
    SessionLocked = 1,
    PowerSuspended = 2,
    DisplayChanging = 4
}

internal enum ReplayWindowsLifecycleEventKind
{
    SessionLocked,
    SessionUnlocked,
    PowerSuspending,
    PowerResumed,
    DisplayChanging,
    DisplayChanged
}

internal sealed class ReplayWindowsLifecycleEventArgs(
    ReplayWindowsLifecycleEventKind kind) : EventArgs
{
    public ReplayWindowsLifecycleEventKind Kind { get; } = kind;
}

internal interface IReplayWindowsLifecycleEventSource : IDisposable
{
    event EventHandler<ReplayWindowsLifecycleEventArgs>? EventOccurred;

    void Start();
}

internal sealed class WindowsReplaySystemEventSource : IReplayWindowsLifecycleEventSource
{
    private readonly object _gate = new();
    private bool _started;

    public event EventHandler<ReplayWindowsLifecycleEventArgs>? EventOccurred;

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            try
            {
                SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
                SystemEvents.DisplaySettingsChanging += SystemEvents_DisplaySettingsChanging;
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                _started = true;
            }
            catch
            {
                UnsubscribeCore();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            UnsubscribeCore();
        }
    }

    private void UnsubscribeCore()
    {
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.DisplaySettingsChanging -= SystemEvents_DisplaySettingsChanging;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        _started = false;
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var kind = e.Reason switch
        {
            SessionSwitchReason.SessionLock => ReplayWindowsLifecycleEventKind.SessionLocked,
            SessionSwitchReason.SessionUnlock => ReplayWindowsLifecycleEventKind.SessionUnlocked,
            _ => (ReplayWindowsLifecycleEventKind?)null
        };
        if (kind.HasValue)
        {
            Publish(kind.Value);
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var kind = e.Mode switch
        {
            PowerModes.Suspend => ReplayWindowsLifecycleEventKind.PowerSuspending,
            PowerModes.Resume => ReplayWindowsLifecycleEventKind.PowerResumed,
            _ => (ReplayWindowsLifecycleEventKind?)null
        };
        if (kind.HasValue)
        {
            Publish(kind.Value);
        }
    }

    private void SystemEvents_DisplaySettingsChanging(object? sender, EventArgs e) =>
        Publish(ReplayWindowsLifecycleEventKind.DisplayChanging);

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) =>
        Publish(ReplayWindowsLifecycleEventKind.DisplayChanged);

    private void Publish(ReplayWindowsLifecycleEventKind kind) =>
        EventOccurred?.Invoke(this, new ReplayWindowsLifecycleEventArgs(kind));
}

internal sealed class ReplayWindowsLifecycleController : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IReplayRecordingService> _replay;
    private readonly IReplayWindowsLifecycleEventSource _events;
    private readonly Action<string>? _trace;
    private ReplayWindowsSuspensionReason _activeReasons;
    private IReplayRecordingService? _suspendedReplay;
    private bool _started;
    private bool _disposed;

    public ReplayWindowsLifecycleController(
        Func<IReplayRecordingService> replay,
        IReplayWindowsLifecycleEventSource events,
        Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(events);
        _replay = replay;
        _events = events;
        _trace = trace;
    }

    internal ReplayWindowsSuspensionReason ActiveReasons
    {
        get
        {
            lock (_gate)
            {
                return _activeReasons;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _events.EventOccurred += Events_EventOccurred;
            try
            {
                _events.Start();
                _started = true;
            }
            catch
            {
                _events.EventOccurred -= Events_EventOccurred;
                throw;
            }
        }
    }

    public void RefreshCurrentReplay()
    {
        lock (_gate)
        {
            if (_disposed || _activeReasons == ReplayWindowsSuspensionReason.None)
            {
                return;
            }

            EnsureCurrentReplaySuspended("Replay service replacement");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _events.EventOccurred -= Events_EventOccurred;
            _events.Dispose();
            _started = false;
            _activeReasons = ReplayWindowsSuspensionReason.None;
            _suspendedReplay = null;
        }
    }

    private void Events_EventOccurred(object? sender, ReplayWindowsLifecycleEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            var (reason, suspending) = Map(e.Kind);
            if (suspending)
            {
                _activeReasons |= reason;
                EnsureCurrentReplaySuspended(e.Kind.ToString());
                return;
            }

            var previous = _activeReasons;
            _activeReasons &= ~reason;
            if (previous == ReplayWindowsSuspensionReason.None ||
                _activeReasons != ReplayWindowsSuspensionReason.None)
            {
                return;
            }

            try
            {
                var replay = _replay();
                var result = replay.ResumeAfterSystemEvent();
                _suspendedReplay = null;
                _trace?.Invoke(
                    $"Replay Windows lifecycle {e.Kind}: {result.Message}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _suspendedReplay = null;
                _trace?.Invoke(
                    $"Replay Windows lifecycle {e.Kind} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void EnsureCurrentReplaySuspended(string source)
    {
        try
        {
            var replay = _replay();
            if (ReferenceEquals(replay, _suspendedReplay))
            {
                return;
            }

            var result = replay.SuspendForSystemEvent();
            _suspendedReplay = replay;
            _trace?.Invoke(
                $"Replay Windows lifecycle {source}: {result.Message}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _suspendedReplay = null;
            _trace?.Invoke(
                $"Replay Windows lifecycle {source} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static (ReplayWindowsSuspensionReason Reason, bool Suspending) Map(
        ReplayWindowsLifecycleEventKind kind) => kind switch
        {
            ReplayWindowsLifecycleEventKind.SessionLocked =>
                (ReplayWindowsSuspensionReason.SessionLocked, true),
            ReplayWindowsLifecycleEventKind.SessionUnlocked =>
                (ReplayWindowsSuspensionReason.SessionLocked, false),
            ReplayWindowsLifecycleEventKind.PowerSuspending =>
                (ReplayWindowsSuspensionReason.PowerSuspended, true),
            ReplayWindowsLifecycleEventKind.PowerResumed =>
                (ReplayWindowsSuspensionReason.PowerSuspended, false),
            ReplayWindowsLifecycleEventKind.DisplayChanging =>
                (ReplayWindowsSuspensionReason.DisplayChanging, true),
            ReplayWindowsLifecycleEventKind.DisplayChanged =>
                (ReplayWindowsSuspensionReason.DisplayChanging, false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Replay Windows lifecycle event.")
        };
}

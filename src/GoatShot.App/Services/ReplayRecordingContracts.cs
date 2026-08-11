using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public interface IReplayFrameSourceFactory
{
    Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
        ReplayCaptureSourceDescriptor strategy,
        bool includeCursor,
        CancellationToken cancellationToken);

    bool HasLiveSourceSetChanged(
        ReplayCaptureSourceDescriptor strategy,
        IReadOnlyList<IReplayFrameSource> openSources) => false;
}

public interface IReplayFrameSource : IDisposable
{
    string TrackId { get; }
    string DisplayName { get; }
    ReplayCaptureSourceDescriptor Source { get; }
    double DpiScaleX { get; }
    double DpiScaleY { get; }

    Task<CapturedBitmap> CaptureFrameAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IReplayVideoSegmentEncoderFactory
{
    IReplayVideoSegmentSession Start(string outputPath, int width, int height);
}

public interface IReplayVideoSegmentSession : IDisposable
{
    string OutputPath { get; }
    int FramesPerSecond { get; }
    int FrameCount { get; }
    void WriteFrame(Bitmap frame, CancellationToken cancellationToken);
    void WriteAudioPcm(ReadOnlyMemory<byte> pcm16)
    {
        if (!pcm16.IsEmpty)
        {
            throw new NotSupportedException("This replay segment encoder does not support streaming audio.");
        }
    }

    RecordingResult Complete();

    RecordingResult Complete(
        bool includeAudio,
        int audioSourceCount,
        CancellationToken cancellationToken)
    {
        if (includeAudio && audioSourceCount > 0)
        {
            throw new NotSupportedException(
                "This replay segment encoder does not support streaming audio.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Complete();
    }
}

public interface IReplayRecordingClock
{
    DateTimeOffset UtcNow { get; }
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IReplayPrivacyGuard
{
    ReplayPrivacyDecision EvaluateForegroundProcess();

    ReplayPrivacyDecision EvaluateCapture(ReplayTrackDescriptor track) =>
        EvaluateForegroundProcess();
}

public sealed record ReplayPrivacyDecision(
    bool SuppressFrame,
    string Message,
    IReadOnlyList<ReplayCaptureBounds>? MaskedDesktopBounds = null)
{
    public bool HasRedactions => SuppressFrame || MaskedDesktopBounds is { Count: > 0 };

    public static ReplayPrivacyDecision Allow() => new(false, string.Empty);

    public static ReplayPrivacyDecision Suppress(string message) => new(true, message);

    public static ReplayPrivacyDecision Mask(
        IReadOnlyList<ReplayCaptureBounds> desktopBounds,
        string message) => new(false, message, desktopBounds);
}

public sealed record ReplayPrivacyWindow(
    string SourceId,
    string ProcessName,
    ReplayCaptureBounds Bounds);

public sealed record ReplayCaptureTargetPlan(
    string TrackId,
    string DisplayName,
    ReplayCaptureSourceDescriptor Source,
    CaptureEngineRequest CaptureRequest);

public sealed class ReplayRecordingStatusChangedEventArgs(
    ReplayBufferStatus status,
    string message,
    DateTimeOffset occurredAtUtc) : EventArgs
{
    public ReplayBufferStatus Status { get; } = status;
    public string Message { get; } = message;
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
}

public static class ReplayCaptureTargetMapper
{
    public static IReadOnlyList<ReplayCaptureTargetPlan> Map(
        ReplayCaptureSourceDescriptor strategy,
        IReadOnlyList<CaptureOverlayTarget> liveTargets,
        string? cursorMonitorId = null,
        string? foregroundWindowId = null)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(liveTargets);

        var monitors = liveTargets
            .Where(target => target.Kind == CaptureOverlayTargetKind.Monitor)
            .ToArray();
        return strategy.Kind switch
        {
            ReplayCaptureSourceKind.SelectedMonitor =>
                [MonitorPlan(FindTarget(strategy, monitors), strategy.Kind, "selected-monitor")],
            ReplayCaptureSourceKind.FollowCursorMonitor =>
                [MonitorPlan(
                    FindTargetById(cursorMonitorId, monitors, "cursor monitor"),
                    strategy.Kind,
                    "follow-cursor-monitor")],
            ReplayCaptureSourceKind.AllMonitorsComposite =>
                [CompositePlan(strategy, monitors)],
            ReplayCaptureSourceKind.SeparateMonitorTracks =>
                monitors.Select(target => MonitorPlan(
                    target,
                    strategy.Kind,
                    $"monitor-track:{target.Id}")).ToArray(),
            ReplayCaptureSourceKind.SelectedWindow =>
                [WindowPlan(FindTarget(strategy, WindowTargets(liveTargets)), strategy.Kind, "selected-window")],
            ReplayCaptureSourceKind.FollowForegroundWindow =>
                [WindowPlan(
                    FindTargetById(foregroundWindowId, WindowTargets(liveTargets), "foreground window"),
                    strategy.Kind,
                    "follow-foreground-window")],
            ReplayCaptureSourceKind.SelectedRegion =>
                [RegionPlan(strategy, CaptureKind.Region, "selected-region")],
            ReplayCaptureSourceKind.FixedRegion =>
                [RegionPlan(strategy, CaptureKind.FixedRegion, "fixed-region")],
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy.Kind, "Unsupported replay capture strategy.")
        };
    }

    private static IReadOnlyList<CaptureOverlayTarget> WindowTargets(
        IReadOnlyList<CaptureOverlayTarget> liveTargets) => liveTargets
        .Where(target => target.Kind == CaptureOverlayTargetKind.Window)
        .ToArray();

    private static ReplayCaptureTargetPlan MonitorPlan(
        CaptureOverlayTarget target,
        ReplayCaptureSourceKind kind,
        string trackId)
    {
        var monitorName = target.Id.StartsWith("monitor:", StringComparison.OrdinalIgnoreCase)
            ? target.Id["monitor:".Length..]
            : target.Id;
        return new ReplayCaptureTargetPlan(
            trackId,
            target.DisplayName,
            Source(kind, target),
            new CaptureEngineRequest(
                CaptureKind.ActiveMonitor,
                Bounds: CopyBounds(target.Bounds),
                MonitorName: monitorName));
    }

    private static ReplayCaptureTargetPlan CompositePlan(
        ReplayCaptureSourceDescriptor strategy,
        IReadOnlyList<CaptureOverlayTarget> monitors)
    {
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Replay could not resolve any active monitors for composite capture.");
        }

        var left = monitors.Min(target => target.Bounds.X);
        var top = monitors.Min(target => target.Bounds.Y);
        var right = monitors.Max(target => target.Bounds.X + target.Bounds.Width);
        var bottom = monitors.Max(target => target.Bounds.Y + target.Bounds.Height);
        var bounds = new ReplayCaptureBounds(left, top, right - left, bottom - top);
        return new ReplayCaptureTargetPlan(
            "all-monitors-composite",
            string.IsNullOrWhiteSpace(strategy.DisplayName) ? "All monitors" : strategy.DisplayName,
            strategy with
            {
                SourceId = "desktop:composite",
                Bounds = bounds
            },
            new CaptureEngineRequest(
                CaptureKind.AllMonitors,
                Bounds: ToCaptureBounds(bounds)));
    }

    private static ReplayCaptureTargetPlan WindowPlan(
        CaptureOverlayTarget target,
        ReplayCaptureSourceKind kind,
        string trackId)
    {
        return new ReplayCaptureTargetPlan(
            trackId,
            target.DisplayName,
            Source(kind, target),
            new CaptureEngineRequest(
                CaptureKind.ActiveWindow,
                Bounds: CopyBounds(target.Bounds),
                TargetWindowTitle: target.DisplayName));
    }

    private static ReplayCaptureTargetPlan RegionPlan(
        ReplayCaptureSourceDescriptor strategy,
        CaptureKind captureKind,
        string trackId)
    {
        var bounds = strategy.Bounds
            ?? throw new InvalidOperationException($"Replay {strategy.Kind} capture requires explicit bounds.");
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"Replay {strategy.Kind} capture bounds must be positive.");
        }

        return new ReplayCaptureTargetPlan(
            trackId,
            string.IsNullOrWhiteSpace(strategy.DisplayName) ? strategy.Kind.ToString() : strategy.DisplayName,
            strategy,
            new CaptureEngineRequest(captureKind, ToCaptureBounds(bounds)));
    }

    private static ReplayCaptureSourceDescriptor Source(
        ReplayCaptureSourceKind kind,
        CaptureOverlayTarget target) => new(
            kind,
            target.Id,
            target.DisplayName,
            new ReplayCaptureBounds(
                target.Bounds.X,
                target.Bounds.Y,
                target.Bounds.Width,
                target.Bounds.Height));

    private static CaptureOverlayTarget FindTarget(
        ReplayCaptureSourceDescriptor strategy,
        IReadOnlyList<CaptureOverlayTarget> candidates)
    {
        var byId = candidates.FirstOrDefault(target =>
            SourceIdsEqual(target.Id, strategy.SourceId));
        if (byId is not null)
        {
            return byId;
        }

        var byDisplayName = candidates.FirstOrDefault(target =>
            target.DisplayName.Equals(strategy.DisplayName, StringComparison.OrdinalIgnoreCase));
        return byDisplayName ?? throw new InvalidOperationException(
            $"Replay could not resolve configured {strategy.Kind} source '{strategy.SourceId}'.");
    }

    private static CaptureOverlayTarget FindTargetById(
        string? targetId,
        IReadOnlyList<CaptureOverlayTarget> candidates,
        string label)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException($"Replay could not resolve the current {label}.");
        }

        return candidates.FirstOrDefault(target => SourceIdsEqual(target.Id, targetId))
            ?? throw new InvalidOperationException(
                $"Replay could not match the current {label} target '{targetId}'.");
    }

    private static bool SourceIdsEqual(string candidateId, string configuredId)
    {
        if (candidateId.Equals(configuredId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return BareSourceId(candidateId).Equals(
            BareSourceId(configuredId),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BareSourceId(string sourceId)
    {
        var separator = sourceId.IndexOf(':');
        return separator >= 0 && separator < sourceId.Length - 1
            ? sourceId[(separator + 1)..]
            : sourceId;
    }

    private static CaptureBounds CopyBounds(CaptureBounds bounds) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height
    };

    private static CaptureBounds ToCaptureBounds(ReplayCaptureBounds bounds) => new()
    {
        X = bounds.X,
        Y = bounds.Y,
        Width = bounds.Width,
        Height = bounds.Height
    };
}

public sealed class WindowsReplayPrivacyGuard : IReplayPrivacyGuard
{
    private readonly HashSet<string> _excludedProcessNames;
    private readonly Func<string?> _foregroundProcessName;
    private readonly Func<IReadOnlyList<ReplayPrivacyWindow>> _visibleWindows;

    public WindowsReplayPrivacyGuard(
        IEnumerable<string>? excludedProcessNames,
        Func<string?>? foregroundProcessName = null,
        Func<IReadOnlyList<ReplayPrivacyWindow>>? visibleWindows = null,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? refreshInterval = null)
    {
        _excludedProcessNames = (excludedProcessNames ?? Array.Empty<string>())
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _foregroundProcessName = foregroundProcessName ?? GetForegroundProcessName;
        _visibleWindows = visibleWindows ?? EnumerateVisibleWindows;
        var effectiveRefreshInterval = refreshInterval ?? TimeSpan.FromMilliseconds(500);
        if (effectiveRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "Replay privacy inspection interval must be positive.");
        }
    }

    public ReplayPrivacyDecision EvaluateForegroundProcess()
    {
        if (_excludedProcessNames.Count == 0)
        {
            return ReplayPrivacyDecision.Allow();
        }

        string? foregroundProcessName;
        try
        {
            // Foreground checks are intentionally not part of the 500 ms visible-
            // window cache. This inexpensive lookup runs for every captured frame
            // so a newly focused excluded app never receives a grace interval.
            foregroundProcessName = _foregroundProcessName();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ReplayPrivacyDecision.Suppress(
                $"Replay could not inspect the foreground privacy exclusion " +
                $"({ex.GetType().Name}: {ex.Message}); frames are blacked out to preserve " +
                "the configured privacy exclusions.");
        }

        var normalized = NormalizeProcessName(foregroundProcessName);
        if (normalized.Length == 0)
        {
            return ReplayPrivacyDecision.Suppress(
                "Replay could not identify the foreground process; frames are blacked out " +
                "to preserve the configured privacy exclusions.");
        }

        return _excludedProcessNames.Contains(normalized)
            ? ReplayPrivacyDecision.Suppress(
                $"Replay privacy exclusion is active for '{foregroundProcessName}'; " +
                "buffered frames are blacked out.")
            : ReplayPrivacyDecision.Allow();
    }

    public ReplayPrivacyDecision EvaluateCapture(ReplayTrackDescriptor track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (_excludedProcessNames.Count == 0)
        {
            return ReplayPrivacyDecision.Allow();
        }

        var foreground = EvaluateForegroundProcess();
        if (foreground.SuppressFrame)
        {
            return foreground;
        }

        var snapshot = GetSnapshot();
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            return ReplayPrivacyDecision.Suppress(
                $"Replay could not inspect configured background-window privacy exclusions " +
                $"({snapshot.Error}); frames are blacked out to preserve the configured " +
                "privacy exclusions.");
        }

        var excludedWindows = snapshot.Windows
            .Where(window => _excludedProcessNames.Contains(
                NormalizeProcessName(window.ProcessName)))
            .ToArray();
        if (track.Source.Kind is ReplayCaptureSourceKind.SelectedWindow or
            ReplayCaptureSourceKind.FollowForegroundWindow)
        {
            var targetExcluded = excludedWindows.Any(window =>
                SourceIdsEqual(window.SourceId, track.Source.SourceId));
            if (targetExcluded)
            {
                return ReplayPrivacyDecision.Suppress(
                    "Replay target belongs to a privacy-excluded process; the entire frame is blacked out.");
            }
        }

        var sourceBounds = track.Source.Bounds;
        if (sourceBounds is null || excludedWindows.Length == 0)
        {
            return ReplayPrivacyDecision.Allow();
        }

        var masks = excludedWindows
            .Select(window => Intersect(sourceBounds, window.Bounds))
            .Where(bounds => bounds is not null)
            .Select(bounds => bounds!)
            .Distinct()
            .ToArray();
        return masks.Length == 0
            ? ReplayPrivacyDecision.Allow()
            : ReplayPrivacyDecision.Mask(
                masks,
                $"Replay masked {masks.Length} privacy-excluded background window area(s).");
    }

    private PrivacySnapshot GetSnapshot()
    {
        try
        {
            // With exclusions configured, every frame gets a fresh background-
            // window snapshot. A time cache can expose the first frames of a newly
            // opened excluded window. Event-driven invalidation can optimize this
            // later without weakening the fail-closed boundary.
            return new PrivacySnapshot(_visibleWindows(), string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PrivacySnapshot([], $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ReplayCaptureBounds? Intersect(
        ReplayCaptureBounds source,
        ReplayCaptureBounds window)
    {
        var left = Math.Max(source.X, window.X);
        var top = Math.Max(source.Y, window.Y);
        var right = Math.Min(source.X + source.Width, window.X + window.Width);
        var bottom = Math.Min(source.Y + source.Height, window.Y + window.Height);
        return right <= left || bottom <= top
            ? null
            : new ReplayCaptureBounds(left, top, right - left, bottom - top);
    }

    private static bool SourceIdsEqual(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
        BareSourceId(left).Equals(BareSourceId(right), StringComparison.OrdinalIgnoreCase);

    private static string BareSourceId(string value)
    {
        var separator = value.IndexOf(':');
        return separator >= 0 && separator < value.Length - 1
            ? value[(separator + 1)..]
            : value;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(processName.Trim());
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static string? GetForegroundProcessName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero ||
            GetWindowThreadProcessId(foregroundWindow, out var processId) == 0 ||
            processId == 0)
        {
            return null;
        }

        using var process = Process.GetProcessById(checked((int)processId));
        return process.ProcessName;
    }

    private static IReadOnlyList<ReplayPrivacyWindow> EnumerateVisibleWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var windows = new List<ReplayPrivacyWindow>();
        Exception? inspectionError = null;
        var enumerated = EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) ||
                    GetWindowThreadProcessId(window, out var processId) == 0 ||
                    processId == 0 ||
                    !GetWindowRect(window, out var rect))
                {
                    return true;
                }

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0)
                {
                    return true;
                }

                try
                {
                    using var process = Process.GetProcessById(checked((int)processId));
                    windows.Add(new ReplayPrivacyWindow(
                        $"window:{window.ToInt64():X}",
                        process.ProcessName,
                        new ReplayCaptureBounds(rect.Left, rect.Top, width, height)));
                }
                catch (Exception ex) when (
                    ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    inspectionError = ex;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        if (inspectionError is not null)
        {
            throw new InvalidOperationException(
                "A visible window process could not be inspected safely.",
                inspectionError);
        }

        if (!enumerated)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "EnumWindows failed while inspecting Replay privacy exclusions.");
        }

        return windows;
    }

    private sealed record PrivacySnapshot(
        IReadOnlyList<ReplayPrivacyWindow> Windows,
        string Error);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);
}

public sealed class WindowsReplayFrameSourceFactory : IReplayFrameSourceFactory
{
    private readonly ScreenshotService _screenshots;
    private readonly ICaptureEngine _captureEngine;

    public WindowsReplayFrameSourceFactory(
        ScreenshotService screenshots,
        ICaptureEngine? captureEngine = null)
    {
        ArgumentNullException.ThrowIfNull(screenshots);
        _screenshots = screenshots;
        _captureEngine = captureEngine ?? new WindowsGraphicsCaptureEngine();
    }

    public Task<IReadOnlyList<IReplayFrameSource>> OpenSegmentSourcesAsync(
        ReplayCaptureSourceDescriptor strategy,
        bool includeCursor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plans = ResolveLivePlans(strategy);
        if (plans.Count == 0)
        {
            throw new InvalidOperationException("Replay capture strategy did not resolve any sources.");
        }

        var sources = new List<IReplayFrameSource>(plans.Count);
        try
        {
            foreach (var plan in plans)
            {
                var dpi = ResolveDpiScale(plan);
                sources.Add(OpenSource(plan, includeCursor, dpi.X, dpi.Y, cancellationToken));
            }

            return Task.FromResult<IReadOnlyList<IReplayFrameSource>>(sources);
        }
        catch
        {
            foreach (var source in sources)
            {
                source.Dispose();
            }

            throw;
        }
    }

    public bool HasLiveSourceSetChanged(
        ReplayCaptureSourceDescriptor strategy,
        IReadOnlyList<IReplayFrameSource> openSources)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(openSources);
        if (strategy.Kind is ReplayCaptureSourceKind.SelectedRegion or ReplayCaptureSourceKind.FixedRegion)
        {
            return false;
        }

        IReadOnlyList<ReplayCaptureTargetPlan> plans;
        try
        {
            plans = ResolveLivePlans(strategy);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            return true;
        }

        if (plans.Count != openSources.Count)
        {
            return true;
        }

        var sourceByTrack = openSources.ToDictionary(source => source.TrackId, StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            if (!sourceByTrack.TryGetValue(plan.TrackId, out var source) ||
                !SameSource(plan.Source, source.Source))
            {
                return true;
            }

            var dpi = ResolveDpiScale(plan);
            if (Math.Abs(dpi.X - source.DpiScaleX) >= 0.001d ||
                Math.Abs(dpi.Y - source.DpiScaleY) >= 0.001d)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ReplayCaptureTargetPlan> ResolveLivePlans(
        ReplayCaptureSourceDescriptor strategy)
    {
        var liveTargets = CaptureOverlayTargetCatalog.BuildLiveTargets();
        var cursorMonitorId = $"monitor:{Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName}";
        var foregroundHandle = GetForegroundWindow();
        var foregroundWindowId = foregroundHandle == IntPtr.Zero
            ? null
            : $"window:{foregroundHandle.ToInt64():X}";
        return ReplayCaptureTargetMapper.Map(
            strategy,
            liveTargets,
            cursorMonitorId,
            foregroundWindowId);
    }

    private static bool SameSource(
        ReplayCaptureSourceDescriptor left,
        ReplayCaptureSourceDescriptor right) =>
        left.Kind == right.Kind &&
        left.SourceId.Equals(right.SourceId, StringComparison.OrdinalIgnoreCase) &&
        left.Bounds == right.Bounds;

    private IReplayFrameSource OpenSource(
        ReplayCaptureTargetPlan plan,
        bool includeCursor,
        double dpiScaleX,
        double dpiScaleY,
        CancellationToken cancellationToken)
    {
        if (plan.CaptureRequest.Kind == CaptureKind.AllMonitors)
        {
            return new DelegateReplayFrameSource(
                plan,
                dpiScaleX,
                dpiScaleY,
                token => CaptureWithCancellationAsync(_screenshots.CaptureAllMonitorsAsync, token));
        }

        try
        {
            var wgc = plan.CaptureRequest.Kind switch
            {
                CaptureKind.ActiveMonitor => WindowsGraphicsCaptureFrameSource.StartActiveMonitor(
                    plan.CaptureRequest.MonitorName,
                    cancellationToken),
                CaptureKind.ActiveWindow when TryParseWindowHandle(plan.Source.SourceId, out var windowHandle) =>
                    WindowsGraphicsCaptureFrameSource.StartWindow(windowHandle, cancellationToken),
                CaptureKind.ActiveWindow => WindowsGraphicsCaptureFrameSource.Start(
                    RecordingCaptureTarget.ActiveWindow(), cancellationToken),
                CaptureKind.Region when plan.CaptureRequest.Bounds is not null =>
                    WindowsGraphicsCaptureFrameSource.Start(
                        RecordingCaptureTarget.Region(plan.CaptureRequest.Bounds),
                        cancellationToken),
                CaptureKind.FixedRegion when plan.CaptureRequest.Bounds is not null =>
                    WindowsGraphicsCaptureFrameSource.Start(
                        RecordingCaptureTarget.FixedRegion(plan.CaptureRequest.Bounds),
                        cancellationToken),
                _ => null
            };

            if (wgc is not null)
            {
                return new WindowsGraphicsReplayFrameSource(plan, wgc, includeCursor, dpiScaleX, dpiScaleY);
            }
        }
        catch (Exception ex) when (
            plan.Source.Kind == ReplayCaptureSourceKind.SelectedWindow &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Replay could not bind the chosen window '{plan.DisplayName}' by handle. " +
                "A screen-region fallback is not used because it could capture unrelated overlapping content.",
                ex);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The existing capture engine remains the fallback for unsupported WGC targets.
        }

        return new DelegateReplayFrameSource(
            plan,
            dpiScaleX,
            dpiScaleY,
            async token =>
            {
                var captured = await _captureEngine
                    .CaptureAsync(plan.CaptureRequest with { IncludeCursor = includeCursor }, token)
                    .ConfigureAwait(false);
                return captured ?? throw new InvalidOperationException(
                    $"Replay capture engine did not return a frame for {plan.DisplayName}.");
            });
    }

    private static bool TryParseWindowHandle(string sourceId, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!sourceId.StartsWith("window:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawHandle = sourceId["window:".Length..].Split(':')[0];
        if (!long.TryParse(
            rawHandle,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) || parsed == 0)
        {
            return false;
        }

        handle = new IntPtr(parsed);
        return true;
    }

    private static async Task<CapturedBitmap> CaptureWithCancellationAsync(
        Func<Task<CapturedBitmap>> capture,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = await capture().ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static (double X, double Y) ResolveDpiScale(ReplayCaptureTargetPlan plan)
    {
        try
        {
            if (plan.Source.SourceId.StartsWith("window:", StringComparison.OrdinalIgnoreCase))
            {
                var rawHandle = plan.Source.SourceId["window:".Length..].Split(':')[0];
                if (long.TryParse(rawHandle, System.Globalization.NumberStyles.HexNumber, null, out var parsedHandle))
                {
                    var windowDpi = GetDpiForWindow(new IntPtr(parsedHandle));
                    if (windowDpi > 0)
                    {
                        var scale = windowDpi / 96d;
                        return (scale, scale);
                    }
                }
            }

            var bounds = plan.Source.Bounds;
            if (bounds is not null)
            {
                var monitor = MonitorFromPoint(
                    new NativePoint(
                        bounds.X + Math.Max(0, bounds.Width / 2),
                        bounds.Y + Math.Max(0, bounds.Height / 2)),
                    MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero &&
                    GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out var dpiY) == 0 &&
                    dpiX > 0 && dpiY > 0)
                {
                    return (dpiX / 96d, dpiY / 96d);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows builds fall back to the logical 96-DPI scale.
        }

        return (1d, 1d);
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    private enum MonitorDpiType
    {
        Effective = 0
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    private sealed class WindowsGraphicsReplayFrameSource(
        ReplayCaptureTargetPlan plan,
        WindowsGraphicsCaptureFrameSource frameSource,
        bool includeCursor,
        double dpiScaleX,
        double dpiScaleY) : IReplayFrameSource
    {
        public string TrackId => plan.TrackId;
        public string DisplayName => plan.DisplayName;
        public ReplayCaptureSourceDescriptor Source => plan.Source;
        public double DpiScaleX => dpiScaleX;
        public double DpiScaleY => dpiScaleY;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) => Task.FromResult(
                frameSource.CaptureFrame(includeCursor, timeout, cancellationToken));

        public void Dispose() => frameSource.Dispose();
    }

    private sealed class DelegateReplayFrameSource(
        ReplayCaptureTargetPlan plan,
        double dpiScaleX,
        double dpiScaleY,
        Func<CancellationToken, Task<CapturedBitmap>> capture) : IReplayFrameSource
    {
        public string TrackId => plan.TrackId;
        public string DisplayName => plan.DisplayName;
        public ReplayCaptureSourceDescriptor Source => plan.Source;
        public double DpiScaleX => dpiScaleX;
        public double DpiScaleY => dpiScaleY;

        public Task<CapturedBitmap> CaptureFrameAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken) => capture(cancellationToken);

        public void Dispose()
        {
        }
    }
}

public sealed class MediaFoundationReplayVideoSegmentEncoderFactory : IReplayVideoSegmentEncoderFactory
{
    private readonly NormalizedRecordingSettings _settings;
    private readonly ProductionVideoEncoderSelection _encoder;
    private readonly bool _reserveAudioStream;

    public MediaFoundationReplayVideoSegmentEncoderFactory(
        NormalizedRecordingSettings settings,
        ProductionVideoEncoderSelection encoder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(encoder);
        _settings = settings;
        _encoder = encoder;
        _reserveAudioStream = settings.IncludeMicrophone || settings.IncludeSystemAudio;
    }

    public IReplayVideoSegmentSession Start(string outputPath, int width, int height)
    {
        if (!_encoder.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Replay Media Foundation encoder is unavailable. {_encoder.Summary}");
        }

        return new MediaFoundationReplayVideoSegmentSession(
            NativeMediaFoundationMp4Encoder.StartStreamingVideo(
                outputPath,
                _settings,
                _encoder,
                width,
                height,
                _reserveAudioStream));
    }

    private sealed class MediaFoundationReplayVideoSegmentSession(
        NativeMediaFoundationMp4Encoder.StreamingVideoSession session) : IReplayVideoSegmentSession
    {
        public string OutputPath => session.OutputPath;
        public int FramesPerSecond => session.FramesPerSecond;
        public int FrameCount => session.FrameCount;

        public void WriteFrame(Bitmap frame, CancellationToken cancellationToken) =>
            session.WriteFrame(frame, cancellationToken);

        public void WriteAudioPcm(ReadOnlyMemory<byte> pcm16) =>
            session.WriteAudioPcm(pcm16);

        public RecordingResult Complete() => session.Complete();

        public RecordingResult Complete(
            bool includeAudio,
            int audioSourceCount,
            CancellationToken cancellationToken) =>
            session.Complete(includeAudio, audioSourceCount, cancellationToken);

        public void Dispose() => session.Dispose();
    }
}

public sealed class SystemReplayRecordingClock : IReplayRecordingClock
{
    public static readonly SystemReplayRecordingClock Instance = new();

    private SystemReplayRecordingClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
        Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

namespace GoatShot.App.Models;

public enum RecordingMode
{
    RecordNow,
    Replay
}

public enum ReplayBufferState
{
    Off,
    Armed,
    Paused,
    Saving,
    Error
}

public enum ReplayCaptureSourceKind
{
    SelectedMonitor,
    FollowCursorMonitor,
    AllMonitorsComposite,
    SeparateMonitorTracks,
    SelectedWindow,
    FollowForegroundWindow,
    SelectedRegion,
    FixedRegion
}

public sealed record ReplayCaptureBounds(int X, int Y, int Width, int Height);

public sealed record ReplayCaptureSourceDescriptor(
    ReplayCaptureSourceKind Kind,
    string SourceId,
    string DisplayName,
    ReplayCaptureBounds? Bounds = null)
{
    public static ReplayCaptureSourceDescriptor FollowCursorMonitor() => new(
        ReplayCaptureSourceKind.FollowCursorMonitor,
        string.Empty,
        "Follow cursor monitor");
}

public sealed record ReplayTrackDescriptor(
    string TrackId,
    string DisplayName,
    ReplayCaptureSourceDescriptor Source,
    int PixelWidth,
    int PixelHeight,
    double DpiScaleX = 1d,
    double DpiScaleY = 1d);

public sealed record ReplaySegmentMetadata(
    string SegmentId,
    long SequenceNumber,
    ReplayTrackDescriptor Track,
    string FilePath,
    DateTimeOffset StartedAtUtc,
    TimeSpan MonotonicStart,
    TimeSpan Duration,
    long ByteLength,
    bool IncludesSystemAudio = false,
    bool IncludesMicrophone = false,
    bool IncludesWebcam = false,
    int EncodedFrameCount = 0,
    int WebcamFrameCount = 0,
    bool PrivacyRedacted = false)
{
    public string TrackId => Track.TrackId;
    public DateTimeOffset EndedAtUtc => StartedAtUtc + Duration;
    public TimeSpan MonotonicEnd => MonotonicStart + Duration;
}

public sealed class ReplayBufferSettings
{
    public static readonly TimeSpan DefaultBufferDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultSegmentDuration = TimeSpan.FromSeconds(2);
    public const long DefaultMaxBufferBytes = 512L * 1024L * 1024L;
    public const int DefaultFramesPerSecond = 30;

    public TimeSpan BufferDuration { get; set; } = DefaultBufferDuration;
    public TimeSpan SegmentDuration { get; set; } = DefaultSegmentDuration;
    public long MaxBufferBytes { get; set; } = DefaultMaxBufferBytes;
    public int FramesPerSecond { get; set; } = DefaultFramesPerSecond;
    public TimeSpan SaveDuration { get; set; } = DefaultBufferDuration;
    public bool ConsentGranted { get; set; }
    public bool AutoArmAtSignIn { get; set; }
    public ReplayCaptureSourceDescriptor CaptureSource { get; set; } =
        ReplayCaptureSourceDescriptor.FollowCursorMonitor();
    public bool EnableLocalOcrIndexing { get; set; } = true;
    public bool EnableSceneIndexing { get; set; } = true;
    public double AnalysisSensitivity { get; set; } = 0.65d;
    public List<string> PrivacyExcludedProcessNames { get; set; } = [];
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+Shift+R";
    public string SaveHotkey { get; set; } = "Ctrl+Shift+PrintScreen";

    public ReplayBufferSettings Normalize()
    {
        var segmentDuration = SegmentDuration > TimeSpan.Zero
            ? SegmentDuration
            : DefaultSegmentDuration;
        var bufferDuration = BufferDuration > TimeSpan.Zero
            ? BufferDuration
            : DefaultBufferDuration;

        return new ReplayBufferSettings
        {
            BufferDuration = bufferDuration < segmentDuration ? segmentDuration : bufferDuration,
            SegmentDuration = segmentDuration,
            MaxBufferBytes = MaxBufferBytes > 0 ? MaxBufferBytes : DefaultMaxBufferBytes,
            FramesPerSecond = Math.Clamp(FramesPerSecond, 1, 120),
            SaveDuration = SaveDuration > TimeSpan.Zero
                ? (SaveDuration > bufferDuration ? bufferDuration : SaveDuration)
                : bufferDuration,
            ConsentGranted = ConsentGranted,
            AutoArmAtSignIn = AutoArmAtSignIn,
            CaptureSource = CaptureSource ?? ReplayCaptureSourceDescriptor.FollowCursorMonitor(),
            EnableLocalOcrIndexing = EnableLocalOcrIndexing,
            EnableSceneIndexing = EnableSceneIndexing,
            AnalysisSensitivity = Math.Clamp(AnalysisSensitivity, 0.05d, 1d),
            PrivacyExcludedProcessNames = PrivacyExcludedProcessNames?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            ToggleHotkey = string.IsNullOrWhiteSpace(ToggleHotkey)
                ? "Ctrl+Alt+Shift+R"
                : ToggleHotkey.Trim(),
            SaveHotkey = string.IsNullOrWhiteSpace(SaveHotkey)
                ? "Ctrl+Shift+PrintScreen"
                : SaveHotkey.Trim()
        };
    }
}

public sealed record ReplayBufferStatus(
    ReplayBufferState State,
    int SegmentCount,
    long TotalBytes,
    TimeSpan BufferedDuration,
    string? LastError,
    bool SystemSuspended = false);

public sealed record ReplayCommandResult(
    bool Succeeded,
    ReplayBufferState State,
    string Message);

public sealed record ReplayReconfigurationResult(
    bool Changed,
    bool BufferRestarted,
    ReplayBufferState PreviousState,
    ReplayBufferState CurrentState,
    string Message);

public sealed record ReplaySegmentAddResult(
    bool Accepted,
    bool Retained,
    IReadOnlyList<ReplaySegmentMetadata> EvictedSegments,
    string Message)
{
    public static ReplaySegmentAddResult Rejected(string message) =>
        new(false, false, Array.Empty<ReplaySegmentMetadata>(), message);
}

public sealed record ReplaySaveRequest(
    string DestinationDirectory,
    TimeSpan? Duration = null,
    string? ReceiptId = null);

public sealed record ReplayPublishedSegment(
    string SegmentId,
    string TrackId,
    string RelativePath,
    string FullPath,
    long ByteLength);

public sealed record ReplaySnapshotPublishResult(
    string ReceiptId,
    string PackagePath,
    IReadOnlyList<ReplayPublishedSegment> Segments);

public sealed record ReplaySaveResult(
    bool Succeeded,
    string Message,
    string? ReceiptId,
    string? PackagePath,
    IReadOnlyList<ReplayPublishedSegment> Segments,
    bool BufferContinued,
    ReplayBufferState State);

public sealed record ReplayBufferCleanupResult(
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> RetainedPaths,
    IReadOnlyList<string> Failures);

using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record ReplaySegmentCatalogSnapshot(
    IReadOnlyList<ReplaySegmentMetadata> Segments,
    long TotalBytes,
    TimeSpan BufferedDuration);

public sealed class ReplaySegmentSnapshotLease : IDisposable
{
    private Action? _release;

    internal ReplaySegmentSnapshotLease(
        string snapshotId,
        IReadOnlyList<ReplaySegmentMetadata> segments,
        TimeSpan bufferedDuration,
        Action release)
    {
        SnapshotId = snapshotId;
        Segments = segments;
        BufferedDuration = bufferedDuration;
        _release = release;
    }

    public string SnapshotId { get; }
    public IReadOnlyList<ReplaySegmentMetadata> Segments { get; }
    public TimeSpan BufferedDuration { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

public sealed class ReplaySegmentCatalog
{
    private readonly object _gate = new();
    private readonly ReplayBufferSettings _settings;
    private readonly Action<ReplaySegmentMetadata>? _onReleased;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _active = [];
    private long _totalBytes;

    public ReplaySegmentCatalog(
        ReplayBufferSettings? settings = null,
        Action<ReplaySegmentMetadata>? onReleased = null)
    {
        _settings = (settings ?? new ReplayBufferSettings()).Normalize();
        _onReleased = onReleased;
    }

    public ReplaySegmentAddResult Add(ReplaySegmentMetadata segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return AddCaptureSet([segment]);
    }

    public ReplaySegmentAddResult AddCaptureSet(
        IReadOnlyList<ReplaySegmentMetadata> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            return ReplaySegmentAddResult.Rejected(
                "A replay capture set must contain at least one segment track.");
        }

        var normalized = segments.Select(ValidateAndNormalize).ToArray();
        var captureSequence = normalized[0].SequenceNumber;
        var captureStart = normalized[0].MonotonicStart;
        if (normalized.Any(segment =>
                segment.SequenceNumber != captureSequence ||
                segment.MonotonicStart != captureStart))
        {
            return ReplaySegmentAddResult.Rejected(
                "Every track in a replay capture set must share its sequence and monotonic start.");
        }

        List<ReplaySegmentMetadata> evicted;
        List<ReplaySegmentMetadata> released;
        bool retained;

        lock (_gate)
        {
            var batchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in normalized)
            {
                if (!batchIds.Add(segment.SegmentId) || _entries.ContainsKey(segment.SegmentId))
                {
                    return ReplaySegmentAddResult.Rejected(
                        $"Replay segment '{segment.SegmentId}' is already cataloged or duplicated in its capture set.");
                }

                if (!batchPaths.Add(segment.FilePath) ||
                    _entries.Values.Any(entry => string.Equals(
                        entry.Metadata.FilePath,
                        segment.FilePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return ReplaySegmentAddResult.Rejected(
                        $"Replay segment path '{segment.FilePath}' is already cataloged or duplicated in its capture set.");
                }
            }

            var batchBytes = normalized.Aggregate(
                0L,
                static (total, segment) => checked(total + segment.ByteLength));
            _ = checked(_totalBytes + batchBytes);
            var addedEntries = normalized.Select(segment => new Entry(segment)).ToArray();
            foreach (var entry in addedEntries)
            {
                _entries.Add(entry.Metadata.SegmentId, entry);
                _active.Add(entry);
            }

            _active.Sort(EntryComparer.Instance);
            _totalBytes = checked(_totalBytes + batchBytes);

            (evicted, released) = ApplyBoundsCore();
            retained = addedEntries.All(entry => !entry.Retired);
        }

        NotifyReleased(released);
        return new ReplaySegmentAddResult(
            true,
            retained,
            evicted,
            retained
                ? $"Replay capture set added with {normalized.Length} synchronized track(s)."
                : "Replay capture set exceeded the configured buffer bounds and was evicted atomically.");
    }

    public ReplaySegmentCatalogSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var segments = _active.Select(entry => entry.Metadata).ToArray();
            return new ReplaySegmentCatalogSnapshot(
                segments,
                _totalBytes,
                CalculateBufferedDuration(segments));
        }
    }

    public IReadOnlyList<string> GetResidentFilePaths()
    {
        lock (_gate)
        {
            return _entries.Values
                .Select(entry => entry.Metadata.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ReplaySegmentSnapshotLease AcquireSnapshot(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Replay save duration must be positive.");
        }

        lock (_gate)
        {
            if (_active.Count == 0)
            {
                return new ReplaySegmentSnapshotLease(
                    Guid.NewGuid().ToString("N"),
                    Array.Empty<ReplaySegmentMetadata>(),
                    TimeSpan.Zero,
                    static () => { });
            }

            var latestEnd = _active.Max(entry => entry.Metadata.MonotonicEnd);
            var cutoff = latestEnd - duration;
            var selectedSets = _active
                .GroupBy(CaptureSetKeyFor)
                .Where(group => group.Max(entry => entry.Metadata.MonotonicEnd) > cutoff)
                .Select(group => group.Key)
                .ToHashSet();
            var selected = _active
                .Where(entry => selectedSets.Contains(CaptureSetKeyFor(entry)))
                .ToArray();

            foreach (var entry in selected)
            {
                entry.PinCount++;
            }

            var segmentIds = selected.Select(entry => entry.Metadata.SegmentId).ToArray();
            var segments = selected.Select(entry => entry.Metadata).ToArray();
            return new ReplaySegmentSnapshotLease(
                Guid.NewGuid().ToString("N"),
                segments,
                CalculateBufferedDuration(segments),
                () => ReleaseSnapshot(segmentIds));
        }
    }

    public IReadOnlyList<ReplaySegmentMetadata> Clear()
    {
        List<ReplaySegmentMetadata> evicted;
        List<ReplaySegmentMetadata> released;

        lock (_gate)
        {
            evicted = [];
            released = [];
            foreach (var entry in _active.ToArray())
            {
                RetireCore(entry, evicted, released);
            }
        }

        NotifyReleased(released);
        return evicted;
    }

    private (List<ReplaySegmentMetadata> Evicted, List<ReplaySegmentMetadata> Released) ApplyBoundsCore()
    {
        var evicted = new List<ReplaySegmentMetadata>();
        var released = new List<ReplaySegmentMetadata>();
        if (_active.Count == 0)
        {
            return (evicted, released);
        }

        var latestEnd = _active.Max(entry => entry.Metadata.MonotonicEnd);
        var cutoff = latestEnd - _settings.BufferDuration;
        var expiredSets = _active
            .GroupBy(CaptureSetKeyFor)
            .Where(group => group.Max(entry => entry.Metadata.MonotonicEnd) <= cutoff)
            .Select(group => group.ToArray())
            .ToArray();
        foreach (var captureSet in expiredSets)
        {
            foreach (var entry in captureSet)
            {
                RetireCore(entry, evicted, released);
            }
        }

        while (_totalBytes > _settings.MaxBufferBytes && _active.Count > 0)
        {
            var oldestSet = CaptureSetKeyFor(_active[0]);
            foreach (var entry in _active
                         .Where(candidate => CaptureSetKeyFor(candidate) == oldestSet)
                         .ToArray())
            {
                RetireCore(entry, evicted, released);
            }
        }

        return (evicted, released);
    }

    private static CaptureSetKey CaptureSetKeyFor(Entry entry) => new(
        entry.Metadata.SequenceNumber,
        entry.Metadata.MonotonicStart);

    private void RetireCore(
        Entry entry,
        ICollection<ReplaySegmentMetadata> evicted,
        ICollection<ReplaySegmentMetadata> released)
    {
        if (entry.Retired)
        {
            return;
        }

        entry.Retired = true;
        _active.Remove(entry);
        _totalBytes -= entry.Metadata.ByteLength;
        evicted.Add(entry.Metadata);

        if (entry.PinCount == 0)
        {
            _entries.Remove(entry.Metadata.SegmentId);
            released.Add(entry.Metadata);
        }
    }

    private void ReleaseSnapshot(IReadOnlyList<string> segmentIds)
    {
        List<ReplaySegmentMetadata> released;

        lock (_gate)
        {
            released = [];
            foreach (var segmentId in segmentIds)
            {
                if (!_entries.TryGetValue(segmentId, out var entry))
                {
                    continue;
                }

                entry.PinCount--;
                if (entry.PinCount < 0)
                {
                    throw new InvalidOperationException(
                        $"Replay segment '{segmentId}' was released more than once.");
                }

                if (entry.Retired && entry.PinCount == 0)
                {
                    _entries.Remove(segmentId);
                    released.Add(entry.Metadata);
                }
            }
        }

        NotifyReleased(released);
    }

    private void NotifyReleased(IEnumerable<ReplaySegmentMetadata> released)
    {
        if (_onReleased is null)
        {
            return;
        }

        foreach (var segment in released)
        {
            try
            {
                _onReleased(segment);
            }
            catch
            {
                // Cleanup is best effort; catalog bounds and ownership must remain consistent.
            }
        }
    }

    private static ReplaySegmentMetadata ValidateAndNormalize(ReplaySegmentMetadata segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(segment.Track);
        ArgumentNullException.ThrowIfNull(segment.Track.Source);

        if (string.IsNullOrWhiteSpace(segment.SegmentId))
        {
            throw new ArgumentException("Replay segment ID is required.", nameof(segment));
        }

        if (segment.SequenceNumber < 0)
        {
            throw new ArgumentException("Replay segment sequence number cannot be negative.", nameof(segment));
        }

        if (string.IsNullOrWhiteSpace(segment.Track.TrackId))
        {
            throw new ArgumentException("Replay track ID is required.", nameof(segment));
        }

        if (string.IsNullOrWhiteSpace(segment.FilePath))
        {
            throw new ArgumentException("Replay segment file path is required.", nameof(segment));
        }

        if (segment.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Replay segment duration must be positive.", nameof(segment));
        }

        if (segment.MonotonicStart < TimeSpan.Zero)
        {
            throw new ArgumentException("Replay segment monotonic start cannot be negative.", nameof(segment));
        }

        if (segment.ByteLength < 0)
        {
            throw new ArgumentException("Replay segment byte length cannot be negative.", nameof(segment));
        }

        if (segment.Track.PixelWidth <= 0 || segment.Track.PixelHeight <= 0)
        {
            throw new ArgumentException("Replay track dimensions must be positive.", nameof(segment));
        }

        if (segment.Track.DpiScaleX <= 0 || segment.Track.DpiScaleY <= 0)
        {
            throw new ArgumentException("Replay track DPI scales must be positive.", nameof(segment));
        }

        return segment with
        {
            SegmentId = segment.SegmentId.Trim(),
            FilePath = Path.GetFullPath(segment.FilePath),
            Track = segment.Track with
            {
                TrackId = segment.Track.TrackId.Trim(),
                DisplayName = segment.Track.DisplayName.Trim(),
                Source = segment.Track.Source with
                {
                    SourceId = segment.Track.Source.SourceId.Trim(),
                    DisplayName = segment.Track.Source.DisplayName.Trim()
                }
            }
        };
    }

    private static TimeSpan CalculateBufferedDuration(IReadOnlyList<ReplaySegmentMetadata> segments)
    {
        if (segments.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var earliestStart = segments.Min(segment => segment.MonotonicStart);
        var latestEnd = segments.Max(segment => segment.MonotonicEnd);
        return latestEnd - earliestStart;
    }

    private sealed class Entry(ReplaySegmentMetadata metadata)
    {
        public ReplaySegmentMetadata Metadata { get; } = metadata;
        public int PinCount { get; set; }
        public bool Retired { get; set; }
    }

    private readonly record struct CaptureSetKey(long SequenceNumber, TimeSpan MonotonicStart);

    private sealed class EntryComparer : IComparer<Entry>
    {
        public static readonly EntryComparer Instance = new();

        public int Compare(Entry? x, Entry? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var time = x.Metadata.MonotonicStart.CompareTo(y.Metadata.MonotonicStart);
            if (time != 0)
            {
                return time;
            }

            var track = string.Compare(
                x.Metadata.TrackId,
                y.Metadata.TrackId,
                StringComparison.OrdinalIgnoreCase);
            if (track != 0)
            {
                return track;
            }

            var sequence = x.Metadata.SequenceNumber.CompareTo(y.Metadata.SequenceNumber);
            return sequence != 0
                ? sequence
                : string.Compare(
                    x.Metadata.SegmentId,
                    y.Metadata.SegmentId,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}

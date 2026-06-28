namespace GoatShot.App.Models;

public sealed class RecordingCaptureTarget
{
    public RecordingCaptureTargetKind Kind { get; init; } = RecordingCaptureTargetKind.ActiveMonitor;
    public CaptureBounds? Bounds { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool IsActiveMonitor => Kind == RecordingCaptureTargetKind.ActiveMonitor;
    public bool UsesExplicitBounds => Bounds is not null &&
        (Kind == RecordingCaptureTargetKind.Region || Kind == RecordingCaptureTargetKind.FixedRegion);

    public string DisplayName => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : Kind switch
        {
            RecordingCaptureTargetKind.AllMonitors => "all monitors",
            RecordingCaptureTargetKind.ActiveWindow => "active window",
            RecordingCaptureTargetKind.Region => Bounds is null ? "selected region" : $"region {Bounds.Display}",
            RecordingCaptureTargetKind.FixedRegion => Bounds is null ? "fixed region" : $"fixed region {Bounds.Display}",
            _ => "active monitor"
        };

    public CaptureKind FrameCaptureKind => Kind switch
    {
        RecordingCaptureTargetKind.AllMonitors => CaptureKind.AllMonitors,
        RecordingCaptureTargetKind.ActiveWindow => CaptureKind.ActiveWindow,
        RecordingCaptureTargetKind.Region => CaptureKind.Region,
        RecordingCaptureTargetKind.FixedRegion => CaptureKind.FixedRegion,
        _ => CaptureKind.ActiveMonitor
    };

    public static RecordingCaptureTarget ActiveMonitor() => new()
    {
        Kind = RecordingCaptureTargetKind.ActiveMonitor,
        Label = "active monitor"
    };

    public static RecordingCaptureTarget AllMonitors() => new()
    {
        Kind = RecordingCaptureTargetKind.AllMonitors,
        Label = "all monitors"
    };

    public static RecordingCaptureTarget ActiveWindow() => new()
    {
        Kind = RecordingCaptureTargetKind.ActiveWindow,
        Label = "active window"
    };

    public static RecordingCaptureTarget Region(CaptureBounds bounds) => new()
    {
        Kind = RecordingCaptureTargetKind.Region,
        Bounds = CopyBounds(bounds)
    };

    public static RecordingCaptureTarget FixedRegion(CaptureBounds bounds) => new()
    {
        Kind = RecordingCaptureTargetKind.FixedRegion,
        Bounds = CopyBounds(bounds)
    };

    public RecordingCaptureTarget Normalize()
    {
        return new RecordingCaptureTarget
        {
            Kind = Kind,
            Bounds = Bounds is null ? null : CopyBounds(Bounds),
            Label = Label.Trim()
        };
    }

    private static CaptureBounds CopyBounds(CaptureBounds bounds)
    {
        return new CaptureBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height)
        };
    }
}

public enum RecordingCaptureTargetKind
{
    ActiveMonitor,
    AllMonitors,
    ActiveWindow,
    Region,
    FixedRegion
}

using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class CaptureOverlayGeometry
{
    public const int DefaultSnapThreshold = 12;
    public const int DefaultLensRadius = 9;
    public const int MaxContextPadding = 200;

    public static CaptureOverlaySelection ResolveSelection(
        double startX,
        double startY,
        double endX,
        double endY,
        CaptureOverlayGeometryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = Normalize(options.VirtualBounds);
        var raw = ClampToBounds(
            Normalize(
                (int)Math.Round(startX),
                (int)Math.Round(startY),
                (int)Math.Round(endX),
                (int)Math.Round(endY)),
            normalized);

        var threshold = Math.Max(0, options.SnapThreshold);
        var target = FindBestTarget(raw, options.Targets, threshold);
        var snapped = target is not null
            ? ClampToBounds(Copy(target.Bounds), normalized)
            : SnapEdges(raw, options.Targets, normalized, threshold);

        var padding = Math.Clamp(options.ContextPadding, 0, MaxContextPadding);
        var final = ApplyPadding(snapped, padding, normalized);
        return new CaptureOverlaySelection(
            raw,
            snapped,
            final,
            target,
            BuildStatusText(snapped, final, target, padding));
    }

    public static CaptureBounds ApplyPadding(CaptureBounds bounds, int padding, CaptureBounds containingBounds)
    {
        var normalized = Normalize(bounds);
        var container = Normalize(containingBounds);
        padding = Math.Clamp(padding, 0, MaxContextPadding);
        return ClampToBounds(
            new CaptureBounds
            {
                X = normalized.X - padding,
                Y = normalized.Y - padding,
                Width = normalized.Width + (padding * 2),
                Height = normalized.Height + (padding * 2)
            },
            container);
    }

    public static CaptureOverlayLensCrop ResolveLensCrop(
        CaptureBounds virtualBounds,
        int cursorScreenX,
        int cursorScreenY,
        int sourcePixelWidth,
        int sourcePixelHeight,
        int radius = DefaultLensRadius)
    {
        if (sourcePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePixelWidth), "Source width must be positive.");
        }

        if (sourcePixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePixelHeight), "Source height must be positive.");
        }

        var normalized = Normalize(virtualBounds);
        radius = Math.Max(1, radius);
        var pixelX = (int)Math.Round((cursorScreenX - normalized.X) * (sourcePixelWidth / (double)normalized.Width));
        var pixelY = (int)Math.Round((cursorScreenY - normalized.Y) * (sourcePixelHeight / (double)normalized.Height));
        pixelX = Math.Clamp(pixelX, 0, sourcePixelWidth - 1);
        pixelY = Math.Clamp(pixelY, 0, sourcePixelHeight - 1);

        var left = Math.Clamp(pixelX - radius, 0, Math.Max(0, sourcePixelWidth - 1));
        var top = Math.Clamp(pixelY - radius, 0, Math.Max(0, sourcePixelHeight - 1));
        var right = Math.Clamp(pixelX + radius + 1, left + 1, sourcePixelWidth);
        var bottom = Math.Clamp(pixelY + radius + 1, top + 1, sourcePixelHeight);

        if (right - left < Math.Min(sourcePixelWidth, (radius * 2) + 1))
        {
            left = Math.Clamp(right - ((radius * 2) + 1), 0, left);
            right = Math.Clamp(left + ((radius * 2) + 1), left + 1, sourcePixelWidth);
        }

        if (bottom - top < Math.Min(sourcePixelHeight, (radius * 2) + 1))
        {
            top = Math.Clamp(bottom - ((radius * 2) + 1), 0, top);
            bottom = Math.Clamp(top + ((radius * 2) + 1), top + 1, sourcePixelHeight);
        }

        return new CaptureOverlayLensCrop(left, top, right - left, bottom - top, cursorScreenX, cursorScreenY);
    }

    public static CaptureOverlayTarget? FindNearestChooserTarget(
        int screenX,
        int screenY,
        IEnumerable<CaptureOverlayTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return targets
            .Where(target => target.ShowInChooser)
            .OrderBy(target => DistanceToBounds(screenX, screenY, target.Bounds))
            .ThenBy(target => target.Kind)
            .FirstOrDefault();
    }

    private static CaptureBounds SnapEdges(
        CaptureBounds raw,
        IReadOnlyList<CaptureOverlayTarget> targets,
        CaptureBounds virtualBounds,
        int threshold)
    {
        var left = raw.X;
        var right = raw.X + raw.Width;
        var top = raw.Y;
        var bottom = raw.Y + raw.Height;

        var verticalEdges = new List<int> { virtualBounds.X, virtualBounds.X + virtualBounds.Width };
        var horizontalEdges = new List<int> { virtualBounds.Y, virtualBounds.Y + virtualBounds.Height };
        foreach (var target in targets)
        {
            var bounds = Normalize(target.Bounds);
            verticalEdges.Add(bounds.X);
            verticalEdges.Add(bounds.X + bounds.Width);
            horizontalEdges.Add(bounds.Y);
            horizontalEdges.Add(bounds.Y + bounds.Height);
        }

        left = SnapValue(left, verticalEdges, threshold);
        right = SnapValue(right, verticalEdges, threshold);
        top = SnapValue(top, horizontalEdges, threshold);
        bottom = SnapValue(bottom, horizontalEdges, threshold);

        var snapped = Normalize(left, top, right, bottom);
        return ClampToBounds(snapped, virtualBounds);
    }

    private static CaptureOverlayTarget? FindBestTarget(
        CaptureBounds raw,
        IReadOnlyList<CaptureOverlayTarget> targets,
        int threshold)
    {
        return targets
            .Select(target => new
            {
                Target = target,
                Score = ScoreTarget(raw, target.Bounds, threshold)
            })
            .Where(candidate => candidate.Score < int.MaxValue)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Target.Kind)
            .FirstOrDefault()
            ?.Target;
    }

    private static int ScoreTarget(CaptureBounds raw, CaptureBounds targetBounds, int threshold)
    {
        var target = Normalize(targetBounds);
        var edgeScore =
            Math.Abs(raw.X - target.X) +
            Math.Abs((raw.X + raw.Width) - (target.X + target.Width)) +
            Math.Abs(raw.Y - target.Y) +
            Math.Abs((raw.Y + raw.Height) - (target.Y + target.Height));

        if (edgeScore <= threshold * 4)
        {
            return edgeScore;
        }

        if (ContainsPoint(target, raw.X + (raw.Width / 2), raw.Y + (raw.Height / 2)))
        {
            var intersection = IntersectionArea(raw, target);
            var rawArea = Math.Max(1, raw.Width * raw.Height);
            var targetArea = Math.Max(1, target.Width * target.Height);
            if (intersection / (double)rawArea >= 0.82 &&
                intersection / (double)targetArea >= 0.60)
            {
                return edgeScore + threshold;
            }
        }

        return int.MaxValue;
    }

    private static int SnapValue(int value, IEnumerable<int> edges, int threshold)
    {
        var bestValue = value;
        var bestDistance = threshold + 1;
        foreach (var edge in edges)
        {
            var distance = Math.Abs(value - edge);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestValue = edge;
            }
        }

        return bestDistance <= threshold ? bestValue : value;
    }

    private static CaptureBounds ClampToBounds(CaptureBounds bounds, CaptureBounds container)
    {
        bounds = Normalize(bounds);
        container = Normalize(container);
        var left = Math.Clamp(bounds.X, container.X, container.X + container.Width - 1);
        var top = Math.Clamp(bounds.Y, container.Y, container.Y + container.Height - 1);
        var right = Math.Clamp(bounds.X + bounds.Width, left + 1, container.X + container.Width);
        var bottom = Math.Clamp(bounds.Y + bounds.Height, top + 1, container.Y + container.Height);
        return new CaptureBounds
        {
            X = left,
            Y = top,
            Width = Math.Max(1, right - left),
            Height = Math.Max(1, bottom - top)
        };
    }

    private static CaptureBounds Normalize(CaptureBounds bounds)
    {
        return Normalize(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);
    }

    private static CaptureBounds Normalize(int startX, int startY, int endX, int endY)
    {
        var left = Math.Min(startX, endX);
        var top = Math.Min(startY, endY);
        var right = Math.Max(startX, endX);
        var bottom = Math.Max(startY, endY);
        return new CaptureBounds
        {
            X = left,
            Y = top,
            Width = Math.Max(1, right - left),
            Height = Math.Max(1, bottom - top)
        };
    }

    private static bool ContainsPoint(CaptureBounds bounds, int x, int y)
    {
        return x >= bounds.X &&
            x <= bounds.X + bounds.Width &&
            y >= bounds.Y &&
            y <= bounds.Y + bounds.Height;
    }

    private static int IntersectionArea(CaptureBounds first, CaptureBounds second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        return Math.Max(0, right - left) * Math.Max(0, bottom - top);
    }

    private static int DistanceToBounds(int x, int y, CaptureBounds bounds)
    {
        bounds = Normalize(bounds);
        if (ContainsPoint(bounds, x, y))
        {
            return 0;
        }

        var dx = x < bounds.X ? bounds.X - x : Math.Max(0, x - (bounds.X + bounds.Width));
        var dy = y < bounds.Y ? bounds.Y - y : Math.Max(0, y - (bounds.Y + bounds.Height));
        return dx + dy;
    }

    private static CaptureBounds Copy(CaptureBounds bounds)
    {
        return new CaptureBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private static string BuildStatusText(
        CaptureBounds snapped,
        CaptureBounds final,
        CaptureOverlayTarget? target,
        int padding)
    {
        var targetText = target is null ? "Free region" : target.DisplayName;
        var paddingText = padding > 0 ? $" + {padding}px context" : "no context padding";
        return $"{targetText}: {snapped.Width} x {snapped.Height}, {paddingText}. Final: {final.Width} x {final.Height}.";
    }
}

public sealed record CaptureOverlayGeometryOptions(
    CaptureBounds VirtualBounds,
    IReadOnlyList<CaptureOverlayTarget> Targets,
    int SnapThreshold = CaptureOverlayGeometry.DefaultSnapThreshold,
    int ContextPadding = 0);

public sealed record CaptureOverlaySelection(
    CaptureBounds RawBounds,
    CaptureBounds SnappedBounds,
    CaptureBounds FinalBounds,
    CaptureOverlayTarget? Target,
    string StatusText);

public sealed record CaptureOverlayLensCrop(
    int X,
    int Y,
    int Width,
    int Height,
    int CursorScreenX,
    int CursorScreenY);

public sealed record CaptureOverlayTarget(
    string Id,
    string DisplayName,
    CaptureOverlayTargetKind Kind,
    CaptureBounds Bounds,
    bool ShowInChooser = true)
{
    public override string ToString() => DisplayName;
}

public enum CaptureOverlayTargetKind
{
    Window,
    ContentArea,
    ControlArea,
    Monitor
}

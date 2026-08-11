using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record ReplayReceiptDocument(
    string PackagePath,
    ReceiptManifest Manifest,
    ReceiptLocalAnalysis? Analysis);

public sealed record ReplayDerivativeResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<CaptureItem> Items,
    IReadOnlyList<string> OutputPaths)
{
    public static ReplayDerivativeResult Failed(string message) =>
        new(false, message, [], []);
}

internal sealed record ReplayCompositeExportPlan(
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> TrackIds,
    IReadOnlyList<string> SourceSegmentIds,
    int Width,
    int Height,
    TimeSpan Duration);

public sealed class ReplayReceiptExplorerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspace;
    private readonly IReplayReceiptMediaTool _mediaTool;

    public ReplayReceiptExplorerService(AppPaths paths, WorkspaceStore workspace)
        : this(paths, workspace, new ReplayReceiptMediaTool())
    {
    }

    internal ReplayReceiptExplorerService(
        AppPaths paths,
        WorkspaceStore workspace,
        IReplayReceiptMediaTool mediaTool)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(mediaTool);
        _paths = paths;
        _workspace = workspace;
        _mediaTool = mediaTool;
    }

    public async Task<ReplayReceiptDocument> LoadAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(packagePath);
        var manifestPath = Path.Combine(root, ReceiptIntegrityService.ManifestFileName);
        var manifest = ReceiptCanonicalJson.Deserialize(
            await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        ReceiptLocalAnalysis? analysis = null;
        var analysisPath = Path.Combine(root, "local-analysis", ReceiptSceneAnalysisService.AnalysisFileName);
        if (File.Exists(analysisPath))
        {
            try
            {
                if (ContainsReparsePoint(Path.Combine(root, "local-analysis"), analysisPath))
                {
                    analysis = IgnoredLocalAnalysis(
                        manifest.ReceiptId,
                        "Ignored local analysis stored through a reparse point.");
                }
                else
                {
                    analysis = JsonSerializer.Deserialize<ReceiptLocalAnalysis>(
                        await File.ReadAllTextAsync(analysisPath, cancellationToken).ConfigureAwait(false),
                        JsonOptions);
                    analysis = SanitizeLocalAnalysis(root, manifest, analysis);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                analysis = IgnoredLocalAnalysis(
                    manifest.ReceiptId,
                    $"Ignored unreadable local analysis: {ex.Message}");
            }
        }

        return new ReplayReceiptDocument(root, manifest, analysis);
    }

    public async Task<string> BuildTrackPlaybackAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        CancellationToken cancellationToken = default)
    {
        var segments = receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(trackId, StringComparison.Ordinal))
            .OrderBy(segment => segment.StartMonotonicTicks)
            .ToArray();
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Receipt track '{trackId}' contains no segments.");
        }

        if (segments.Length == 1)
        {
            return Path.Combine(receipt.PackagePath, segments[0].RelativePath);
        }

        var cacheRoot = Path.Combine(
            _paths.TempRoot,
            "receipt-playback",
            SanitizeFileName(receipt.Manifest.ReceiptId));
        Directory.CreateDirectory(cacheRoot);
        var output = UniquePath(Path.Combine(
            cacheRoot,
            $"{SanitizeFileName(trackId)}-{Guid.NewGuid():N}.mp4"));
        if (RequiresNormalizedTrackPlayback(receipt, trackId, segments))
        {
            var arguments = BuildNormalizedTrackPlaybackArguments(receipt, trackId, output);
            await _mediaTool.RunFfmpegAsync(arguments, cancellationToken).ConfigureAwait(false);
            return output;
        }

        var listPath = Path.Combine(cacheRoot, $".{SanitizeFileName(trackId)}-{Guid.NewGuid():N}.txt");
        try
        {
            var lines = segments.Select(segment =>
                $"file '{EscapeConcatPath(Path.Combine(receipt.PackagePath, segment.RelativePath))}'");
            await File.WriteAllLinesAsync(listPath, lines, cancellationToken).ConfigureAwait(false);
            await _mediaTool.RunFfmpegAsync(
                ["-y", "-f", "concat", "-safe", "0", "-i", listPath, "-c", "copy", "-movflags", "+faststart", output],
                cancellationToken).ConfigureAwait(false);
            return output;
        }
        finally
        {
            TryDeleteFile(listPath);
        }
    }

    public async Task<ReplayDerivativeResult> SaveFrameAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        TimeSpan at,
        CancellationToken cancellationToken = default)
    {
        var playback = await BuildTrackPlaybackAsync(receipt, trackId, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_paths.ImagesRoot);
        var output = UniquePath(Path.Combine(
            _paths.ImagesRoot,
            $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-frame-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png"));
        try
        {
            await ExtractFrameAsync(playback, at, output, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDisposableMedia(playback);
        }
        var item = await AddDerivativeAsync(
            receipt,
            output,
            CaptureKind.VideoFrame,
            "extracted-frame",
            receipt.Manifest.Segments.Where(segment => segment.TrackId == trackId).Select(segment => segment.SegmentId),
            cancellationToken).ConfigureAwait(false);
        return new ReplayDerivativeResult(true, $"Saved linked frame {item.FileName}.", [item], [output]);
    }

    public async Task<string> BuildAnalysisFramePreviewAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        string segmentId,
        long monotonicTicks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var segment = receipt.Manifest.Segments.FirstOrDefault(candidate =>
            candidate.TrackId.Equals(trackId, StringComparison.Ordinal) &&
            candidate.SegmentId.Equals(segmentId, StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"Local analysis references unknown signed segment '{segmentId}' in track '{trackId}'.");
        var root = Path.Combine(
            _paths.TempRoot,
            "receipt-analysis-previews",
            SanitizeFileName(receipt.Manifest.ReceiptId));
        Directory.CreateDirectory(root);
        var output = UniquePath(Path.Combine(
            root,
            $"{SanitizeFileName(segmentId)}-{monotonicTicks}-{Guid.NewGuid():N}.png"));
        await ExtractFrameAsync(
            Path.Combine(receipt.PackagePath, segment.RelativePath),
            OffsetWithinSegment(segment, monotonicTicks),
            output,
            cancellationToken).ConfigureAwait(false);
        return output;
    }

    public async Task<ReplayDerivativeResult> ExtractUniqueFramesAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        TimeSpan? start = null,
        TimeSpan? end = null,
        CancellationToken cancellationToken = default)
    {
        var trackSegments = receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(trackId, StringComparison.Ordinal))
            .OrderBy(segment => segment.StartMonotonicTicks)
            .ToArray();
        if (trackSegments.Length == 0)
        {
            return ReplayDerivativeResult.Failed("The selected receipt track has no segments.");
        }

        var origin = trackSegments[0].StartMonotonicTicks;
        var scenes = receipt.Analysis?.Scenes
            .Where(scene => scene.TrackId.Equals(trackId, StringComparison.Ordinal) &&
                scene.IsVisuallyDistinct)
            .Where(scene => !start.HasValue || scene.MonotonicTicks - origin >= start.Value.Ticks)
            .Where(scene => !end.HasValue || scene.MonotonicTicks - origin <= end.Value.Ticks)
            .ToArray() ?? [];
        if (scenes.Length == 0)
        {
            return ReplayDerivativeResult.Failed(
                "No indexed unique frames were found in the selected range. Run local analysis first.");
        }

        var items = new List<CaptureItem>();
        var paths = new List<string>();
        foreach (var scene in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = ResolveSceneSegment(receipt, trackId, scene);
            var source = Path.Combine(receipt.PackagePath, segment.RelativePath);
            var offset = OffsetWithinSegment(segment, scene.MonotonicTicks);
            var output = UniquePath(Path.Combine(
                _paths.ImagesRoot,
                $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-scene-{paths.Count + 1:000}.png"));
            await ExtractFrameAsync(source, offset, output, cancellationToken).ConfigureAwait(false);
            var item = await AddDerivativeAsync(
                receipt,
                output,
                CaptureKind.VideoFrame,
                "unique-frame",
                [scene.SegmentId],
                cancellationToken).ConfigureAwait(false);
            items.Add(item);
            paths.Add(output);
        }

        return new ReplayDerivativeResult(
            true,
            $"Extracted {items.Count} unique linked frame(s).",
            items,
            paths);
    }

    public async Task<ReplayDerivativeResult> ExportContactSheetAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        CancellationToken cancellationToken = default)
    {
        var scenes = receipt.Analysis?.Scenes
            .Where(scene => scene.TrackId.Equals(trackId, StringComparison.Ordinal) && scene.IsVisuallyDistinct)
            .Take(24)
            .ToArray() ?? [];
        if (scenes.Length == 0)
        {
            return ReplayDerivativeResult.Failed("Run local scene analysis before exporting a contact sheet.");
        }

        var temporaryRoot = Path.Combine(
            _paths.TempRoot,
            "receipt-contact-sheet",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            const int cellWidth = 360;
            const int cellHeight = 232;
            const int columns = 3;
            var rows = (int)Math.Ceiling(scenes.Length / (double)columns);
            using var sheet = new Bitmap(cellWidth * columns, cellHeight * rows);
            using var graphics = Graphics.FromImage(sheet);
            graphics.Clear(Color.FromArgb(8, 16, 22));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var labelBrush = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 10, FontStyle.Regular);
            for (var index = 0; index < scenes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scene = scenes[index];
                var segment = ResolveSceneSegment(receipt, trackId, scene);
                var source = Path.Combine(receipt.PackagePath, segment.RelativePath);
                var extracted = Path.Combine(temporaryRoot, $"scene-{index + 1:000}.png");
                await ExtractFrameAsync(
                    source,
                    OffsetWithinSegment(segment, scene.MonotonicTicks),
                    extracted,
                    cancellationToken).ConfigureAwait(false);
                var x = (index % columns) * cellWidth;
                var y = (index / columns) * cellHeight;
                using var image = Image.FromFile(extracted);
                graphics.DrawImage(image, new Rectangle(x + 8, y + 8, cellWidth - 16, cellHeight - 40));
                graphics.DrawString($"Scene {index + 1} · {TimeSpan.FromTicks(scene.MonotonicTicks):mm\\:ss}", font, labelBrush, x + 8, y + cellHeight - 27);
            }

            var output = UniquePath(Path.Combine(
                _paths.ImagesRoot,
                $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-contact-sheet.png"));
            sheet.Save(output, ImageFormat.Png);
            var item = await AddDerivativeAsync(
                receipt,
                output,
                CaptureKind.EditedImage,
                "contact-sheet",
                scenes.Select(scene => scene.SegmentId),
                cancellationToken).ConfigureAwait(false);
            return new ReplayDerivativeResult(true, $"Saved linked contact sheet {item.FileName}.", [item], [output]);
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    public async Task<ReplayDerivativeResult> ExportTrackAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        CancellationToken cancellationToken = default) =>
        await ExportTracksAsync(receipt, [trackId], cancellationToken).ConfigureAwait(false);

    public async Task<ReplayDerivativeResult> ExportTracksAsync(
        ReplayReceiptDocument receipt,
        IEnumerable<string> trackIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var selectedTracks = ResolveSelectedTracks(receipt.Manifest, trackIds);
        if (selectedTracks.Length == 0)
        {
            return ReplayDerivativeResult.Failed("Select at least one receipt track to export.");
        }

        Directory.CreateDirectory(_paths.VideosRoot);
        var items = new List<CaptureItem>();
        var outputs = new List<string>();
        foreach (var track in selectedTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playback = await BuildTrackPlaybackAsync(receipt, track.TrackId, cancellationToken).ConfigureAwait(false);
            var output = UniquePath(Path.Combine(
                _paths.VideosRoot,
                $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-{SanitizeFileName(track.TrackId)}.mp4"));
            try
            {
                File.Copy(playback, output, overwrite: false);
            }
            finally
            {
                TryDeleteDisposableMedia(playback);
            }
            var item = await AddDerivativeAsync(
                receipt,
                output,
                CaptureKind.RecordingMp4,
                "exported-track",
                receipt.Manifest.Segments
                    .Where(segment => segment.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
                    .Select(segment => segment.SegmentId),
                cancellationToken).ConfigureAwait(false);
            items.Add(item);
            outputs.Add(output);
        }

        return new ReplayDerivativeResult(
            true,
            selectedTracks.Length == 1
                ? $"Exported linked track {items[0].FileName}."
                : $"Exported {selectedTracks.Length} linked track videos.",
            items,
            outputs);
    }

    public async Task<ReplayDerivativeResult> ExportCompositeAsync(
        ReplayReceiptDocument receipt,
        IEnumerable<string> trackIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var selectedTracks = ResolveSelectedTracks(receipt.Manifest, trackIds);
        if (selectedTracks.Length == 0)
        {
            return ReplayDerivativeResult.Failed("Select at least one receipt track to composite.");
        }

        Directory.CreateDirectory(_paths.VideosRoot);
        var output = UniquePath(Path.Combine(
            _paths.VideosRoot,
            $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-composite.mp4"));
        var plan = BuildCompositeExportPlan(
            receipt,
            selectedTracks.Select(track => track.TrackId),
            output);
        await _mediaTool.RunFfmpegAsync(plan.Arguments, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(output))
        {
            throw new IOException("FFmpeg completed without producing the composite replay video.");
        }

        var item = await AddDerivativeAsync(
            receipt,
            output,
            CaptureKind.RecordingMp4,
            "composite-video",
            plan.SourceSegmentIds,
            cancellationToken).ConfigureAwait(false);
        return new ReplayDerivativeResult(
            true,
            $"Exported a linked {plan.TrackIds.Count}-track composite video {item.FileName}.",
            [item],
            [output]);
    }

    internal static ReplayCompositeExportPlan BuildCompositeExportPlan(
        ReplayReceiptDocument receipt,
        IEnumerable<string> trackIds,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(trackIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var tracks = ResolveSelectedTracks(receipt.Manifest, trackIds);
        if (tracks.Length == 0)
        {
            throw new ArgumentException("At least one known receipt track is required.", nameof(trackIds));
        }

        var segmentsByTrack = tracks.ToDictionary(
            track => track.TrackId,
            track => receipt.Manifest.Segments
                .Where(segment => segment.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
                .OrderBy(segment => segment.StartMonotonicTicks)
                .ToArray(),
            StringComparer.Ordinal);
        var emptyTrack = tracks.FirstOrDefault(track => segmentsByTrack[track.TrackId].Length == 0);
        if (emptyTrack is not null)
        {
            throw new InvalidOperationException($"Receipt track '{emptyTrack.TrackId}' contains no segments.");
        }

        var allSegments = tracks
            .SelectMany(track => segmentsByTrack[track.TrackId])
            .ToArray();
        var globalStart = allSegments.Min(segment => segment.StartMonotonicTicks);
        var globalEnd = allSegments.Max(segment => checked(
            segment.StartMonotonicTicks + Math.Max(1L, segment.DurationTicks)));
        var duration = TimeSpan.FromTicks(Math.Max(1L, globalEnd - globalStart));
        var columns = (int)Math.Ceiling(Math.Sqrt(tracks.Length));
        var rows = (int)Math.Ceiling(tracks.Length / (double)columns);
        var maximumSourceWidth = tracks.Max(GetMaximumTrackWidth);
        var maximumSourceHeight = tracks.Max(GetMaximumTrackHeight);
        var cellWidth = EvenFloor(Math.Min(Math.Max(2, maximumSourceWidth), Math.Max(2, 3840 / columns)));
        var cellHeight = EvenFloor(Math.Min(Math.Max(2, maximumSourceHeight), Math.Max(2, 2160 / rows)));
        var width = cellWidth * columns;
        var height = cellHeight * rows;
        var fps = Math.Clamp(receipt.Manifest.CaptureSettings.FramesPerSecond, 1, 120);
        var sourceBitrate = receipt.Manifest.CaptureSettings.VideoBitrateBitsPerSecond > 0
            ? receipt.Manifest.CaptureSettings.VideoBitrateBitsPerSecond
            : 8_000_000;
        var outputBitrate = Math.Clamp(
            (long)sourceBitrate * Math.Min(2, tracks.Length),
            2_000_000L,
            40_000_000L);
        var arguments = new List<string> { "-y" };
        foreach (var segment in allSegments)
        {
            var sourcePath = Path.GetFullPath(Path.Combine(receipt.PackagePath, segment.RelativePath));
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Receipt segment '{segment.SegmentId}' is unavailable for composite export.",
                    sourcePath);
            }

            arguments.Add("-i");
            arguments.Add(sourcePath);
        }

        var filters = new StringBuilder();
        var inputIndex = 0;
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            var baseLabel = $"track{trackIndex}base";
            filters.Append("color=c=black:s=")
                .Append(cellWidth)
                .Append('x')
                .Append(cellHeight)
                .Append(":r=")
                .Append(fps)
                .Append(":d=")
                .Append(FormatSeconds(duration))
                .Append('[')
                .Append(baseLabel)
                .Append("]; ");
            var currentLabel = baseLabel;
            var trackSegments = segmentsByTrack[tracks[trackIndex].TrackId];
            for (var segmentIndex = 0; segmentIndex < trackSegments.Length; segmentIndex++)
            {
                var segment = trackSegments[segmentIndex];
                var offset = TimeSpan.FromTicks(Math.Max(0L, segment.StartMonotonicTicks - globalStart));
                var segmentDuration = TimeSpan.FromTicks(Math.Max(1L, segment.DurationTicks));
                var end = offset + segmentDuration;
                var scaledLabel = $"track{trackIndex}segment{segmentIndex}";
                var overlayLabel = $"track{trackIndex}overlay{segmentIndex}";
                filters.Append('[')
                    .Append(inputIndex++)
                    .Append(":v]setpts=PTS-STARTPTS+")
                    .Append(FormatSeconds(offset))
                    .Append("/TB,scale=")
                    .Append(cellWidth)
                    .Append(':')
                    .Append(cellHeight)
                    .Append(":force_original_aspect_ratio=decrease,pad=")
                    .Append(cellWidth)
                    .Append(':')
                    .Append(cellHeight)
                    .Append(":(ow-iw)/2:(oh-ih)/2:black[")
                    .Append(scaledLabel)
                    .Append("];[")
                    .Append(currentLabel)
                    .Append("][")
                    .Append(scaledLabel)
                    .Append("]overlay=eof_action=pass:repeatlast=0:shortest=0:enable='between(t,")
                    .Append(FormatSeconds(offset))
                    .Append(',')
                    .Append(FormatSeconds(end))
                    .Append(")'[")
                    .Append(overlayLabel)
                    .Append("]; ");
                currentLabel = overlayLabel;
            }

            filters.Append('[')
                .Append(currentLabel)
                .Append("]trim=duration=")
                .Append(FormatSeconds(duration))
                .Append(",setpts=PTS-STARTPTS[track")
                .Append(trackIndex)
                .Append("]; ");
        }

        if (tracks.Length == 1)
        {
            filters.Append("[track0]format=yuv420p[outv]");
        }
        else
        {
            for (var index = 0; index < tracks.Length; index++)
            {
                filters.Append("[track").Append(index).Append(']');
            }

            filters.Append("xstack=inputs=")
                .Append(tracks.Length)
                .Append(":layout=");
            for (var index = 0; index < tracks.Length; index++)
            {
                if (index > 0)
                {
                    filters.Append('|');
                }

                filters.Append((index % columns) * cellWidth)
                    .Append('_')
                    .Append((index / columns) * cellHeight);
            }

            filters.Append(":fill=black:shortest=1,format=yuv420p[outv]");
        }

        arguments.AddRange(
        [
            "-filter_complex", filters.ToString(),
            "-map", "[outv]",
            "-an",
            "-c:v", "libopenh264",
            "-b:v", outputBitrate.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-t", FormatSeconds(duration),
            "-movflags", "+faststart",
            Path.GetFullPath(outputPath)
        ]);

        return new ReplayCompositeExportPlan(
            arguments,
            tracks.Select(track => track.TrackId).ToArray(),
            allSegments.Select(segment => segment.SegmentId).ToArray(),
            width,
            height,
            duration);
    }

    public async Task<ReplayDerivativeResult> CreateStepGuideAsync(
        ReplayReceiptDocument receipt,
        string trackId,
        CancellationToken cancellationToken = default)
    {
        var scenes = receipt.Analysis?.Scenes
            .Where(scene => scene.TrackId.Equals(trackId, StringComparison.Ordinal) && scene.IsVisuallyDistinct)
            .ToArray() ?? [];
        if (scenes.Length == 0)
        {
            return ReplayDerivativeResult.Failed("Run local scene analysis before creating a guide.");
        }

        var guideRoot = Path.Combine(_paths.DocumentsRoot, $"receipt-{ShortId(receipt.Manifest.ReceiptId)}-guide");
        var uniqueRoot = UniqueDirectory(guideRoot);
        Directory.CreateDirectory(uniqueRoot);
        var builder = new StringBuilder()
            .AppendLine($"# Receipt {receipt.Manifest.ReceiptId} step guide")
            .AppendLine()
            .AppendLine("> Derived locally from a signed receipt. The guide is not itself the original receipt.")
            .AppendLine();
        for (var index = 0; index < scenes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = $"step-{index + 1:000}.png";
            var scene = scenes[index];
            var segment = ResolveSceneSegment(receipt, trackId, scene);
            await ExtractFrameAsync(
                Path.Combine(receipt.PackagePath, segment.RelativePath),
                OffsetWithinSegment(segment, scene.MonotonicTicks),
                Path.Combine(uniqueRoot, fileName),
                cancellationToken).ConfigureAwait(false);
            builder.AppendLine($"## Step {index + 1}")
                .AppendLine()
                .AppendLine($"![Step {index + 1}]({fileName})")
                .AppendLine();
        }

        var output = Path.Combine(uniqueRoot, "guide.md");
        await File.WriteAllTextAsync(output, builder.ToString(), cancellationToken).ConfigureAwait(false);
        var item = await AddDerivativeAsync(
            receipt,
            output,
            CaptureKind.Imported,
            "step-guide",
            scenes.Select(scene => scene.SegmentId),
            cancellationToken).ConfigureAwait(false);
        return new ReplayDerivativeResult(true, $"Created linked {scenes.Length}-step guide.", [item], [output]);
    }

    private static ReceiptTrackManifest[] ResolveSelectedTracks(
        ReceiptManifest manifest,
        IEnumerable<string> trackIds)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(trackIds);
        var selectedIds = trackIds
            .Where(trackId => !string.IsNullOrWhiteSpace(trackId))
            .ToHashSet(StringComparer.Ordinal);
        return manifest.Tracks
            .Where(track => selectedIds.Contains(track.TrackId))
            .ToArray();
    }

    private static int GetMaximumTrackWidth(ReceiptTrackManifest track) =>
        Math.Max(
            Math.Max(2, track.Bounds.Width),
            track.SourceTransitions
                .Select(transition => transition.Bounds.Width)
                .DefaultIfEmpty(2)
                .Max());

    private static int GetMaximumTrackHeight(ReceiptTrackManifest track) =>
        Math.Max(
            Math.Max(2, track.Bounds.Height),
            track.SourceTransitions
                .Select(transition => transition.Bounds.Height)
                .DefaultIfEmpty(2)
                .Max());

    private static int EvenFloor(int value)
    {
        value = Math.Max(2, value);
        return value % 2 == 0 ? value : value - 1;
    }

    private static string FormatSeconds(TimeSpan value) =>
        Math.Max(0d, value.TotalSeconds).ToString("0.######", CultureInfo.InvariantCulture);

    private async Task<CaptureItem> AddDerivativeAsync(
        ReplayReceiptDocument receipt,
        string outputPath,
        CaptureKind kind,
        string role,
        IEnumerable<string> sourceSegmentIds,
        CancellationToken cancellationToken)
    {
        var sourceIds = sourceSegmentIds.Distinct(StringComparer.Ordinal).ToList();
        var sourceIdSet = sourceIds.ToHashSet(StringComparer.Ordinal);
        var sourceSegments = receipt.Manifest.Segments
            .Where(segment => sourceIdSet.Contains(segment.SegmentId))
            .ToArray();
        var item = await _workspace.AddImageFileAsync(
            outputPath,
            kind,
            $"Derivative of signed receipt {receipt.Manifest.ReceiptId}; original receipt remains unchanged.")
            .ConfigureAwait(false);
        item.SourceReceiptId = receipt.Manifest.ReceiptId;
        item.ArtifactRole = role;
        item.IsOriginal = false;
        item.SourceAvailable = Directory.Exists(receipt.PackagePath);
        await _workspace.UpdateItemAsync(item).ConfigureAwait(false);
        var lineage = new ReceiptDerivativeLineage
        {
            DerivativeId = item.Id,
            SourceReceiptId = receipt.Manifest.ReceiptId,
            SourceReceiptPath = receipt.PackagePath,
            ArtifactRole = role,
            OutputPath = outputPath,
            SourceSegmentIds = sourceIds,
            StartMonotonicTicks = sourceSegments.Length == 0
                ? null
                : sourceSegments.Min(segment => segment.StartMonotonicTicks),
            EndMonotonicTicks = sourceSegments.Length == 0
                ? null
                : sourceSegments.Max(segment => checked(
                    segment.StartMonotonicTicks + Math.Max(1L, segment.DurationTicks)))
        };
        await File.WriteAllTextAsync(
            outputPath + ".receipt-lineage.json",
            JsonSerializer.Serialize(lineage, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return item;
    }

    private static ReceiptLocalAnalysis? SanitizeLocalAnalysis(
        string packageRoot,
        ReceiptManifest manifest,
        ReceiptLocalAnalysis? analysis)
    {
        if (analysis is null)
        {
            return null;
        }

        if (!string.Equals(analysis.ReceiptId, manifest.ReceiptId, StringComparison.Ordinal))
        {
            return new ReceiptLocalAnalysis
            {
                ReceiptId = manifest.ReceiptId,
                Warnings = ["Ignored local analysis because its receipt ID did not match the signed manifest."]
            };
        }

        analysis.Frames ??= [];
        analysis.Scenes ??= [];
        analysis.Changes ??= [];
        analysis.Warnings ??= [];
        var originalFrameCount = analysis.Frames.Count;
        var originalSceneCount = analysis.Scenes.Count;
        analysis.Frames = analysis.Frames
            .Where(frame => IsSafeLocalAnalysisFramePath(packageRoot, frame.RelativeFramePath))
            .ToList();
        analysis.Scenes = analysis.Scenes
            .Where(scene => IsSafeLocalAnalysisFramePath(packageRoot, scene.RelativeFramePath))
            .ToList();
        var retainedFrameIds = analysis.Frames
            .Select(frame => frame.FrameId)
            .ToHashSet(StringComparer.Ordinal);
        analysis.Changes = analysis.Changes
            .Where(change => retainedFrameIds.Contains(change.BeforeFrameId) &&
                retainedFrameIds.Contains(change.AfterFrameId))
            .ToList();
        if (analysis.Frames.Count != originalFrameCount || analysis.Scenes.Count != originalSceneCount)
        {
            analysis.Warnings.Add(
                "Ignored local analysis frames outside this receipt's local-analysis/frames directory. " +
                "Local analysis is rebuildable and is not part of the signed original.");
        }

        return analysis;
    }

    private static ReceiptLocalAnalysis IgnoredLocalAnalysis(string receiptId, string warning) => new()
    {
        ReceiptId = receiptId,
        Warnings = [warning + " Local analysis is rebuildable and is not part of the signed original."]
    };

    private static bool IsSafeLocalAnalysisFramePath(string packageRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var framesRoot = Path.GetFullPath(Path.Combine(packageRoot, "local-analysis", "frames"));
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var prefix = framesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(candidate) &&
            !ContainsReparsePoint(framesRoot, candidate);
    }

    private static bool ContainsReparsePoint(string allowedRoot, string candidate)
    {
        var current = new FileInfo(candidate) as FileSystemInfo;
        var root = Path.GetFullPath(allowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var currentPath = Path.GetFullPath(current.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (currentPath.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }

        return false;
    }

    private static bool HasMonotonicGap(IReadOnlyList<ReceiptSegmentManifest> segments)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            var previousEnd = checked(
                segments[index - 1].StartMonotonicTicks + Math.Max(1L, segments[index - 1].DurationTicks));
            if (segments[index].StartMonotonicTicks > previousEnd)
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresNormalizedTrackPlayback(
        ReplayReceiptDocument receipt,
        string trackId,
        IReadOnlyList<ReceiptSegmentManifest> segments)
    {
        var track = receipt.Manifest.Tracks.FirstOrDefault(candidate =>
            candidate.TrackId.Equals(trackId, StringComparison.Ordinal));
        var audioLayouts = segments
            .Select(segment => (segment.IncludesSystemAudio, segment.IncludesMicrophone))
            .Distinct()
            .Count();
        return HasMonotonicGap(segments) ||
            track?.SourceTransitions.Count > 1 ||
            audioLayouts > 1;
    }

    internal static IReadOnlyList<string> BuildNormalizedTrackPlaybackArguments(
        ReplayReceiptDocument receipt,
        string trackId,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var track = receipt.Manifest.Tracks.FirstOrDefault(candidate =>
            candidate.TrackId.Equals(trackId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Receipt track '{trackId}' is unavailable.");
        var segments = receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(trackId, StringComparison.Ordinal))
            .OrderBy(segment => segment.StartMonotonicTicks)
            .ToArray();
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Receipt track '{trackId}' contains no segments.");
        }

        var arguments = new List<string> { "-y" };
        foreach (var segment in segments)
        {
            arguments.Add("-i");
            arguments.Add(Path.GetFullPath(Path.Combine(receipt.PackagePath, segment.RelativePath)));
        }

        var width = EvenFloor(GetMaximumTrackWidth(track));
        var height = EvenFloor(GetMaximumTrackHeight(track));
        var fps = Math.Clamp(receipt.Manifest.CaptureSettings.FramesPerSecond, 1, 120);
        var bitrate = Math.Clamp(
            receipt.Manifest.CaptureSettings.VideoBitrateBitsPerSecond > 0
                ? receipt.Manifest.CaptureSettings.VideoBitrateBitsPerSecond
                : 8_000_000,
            1_000_000,
            40_000_000);
        var includeAudio = segments.Any(segment =>
            segment.IncludesSystemAudio || segment.IncludesMicrophone);
        var filters = new StringBuilder();
        var pieces = new List<(string Video, string? Audio)>();
        var cursorTicks = segments[0].StartMonotonicTicks;
        var pieceIndex = 0;
        for (var inputIndex = 0; inputIndex < segments.Length; inputIndex++)
        {
            var segment = segments[inputIndex];
            if (segment.StartMonotonicTicks > cursorTicks)
            {
                var gap = TimeSpan.FromTicks(segment.StartMonotonicTicks - cursorTicks);
                var gapVideo = $"gapv{pieceIndex}";
                filters.Append("color=c=black:s=")
                    .Append(width).Append('x').Append(height)
                    .Append(":r=").Append(fps)
                    .Append(":d=").Append(FormatSeconds(gap))
                    .Append('[').Append(gapVideo).Append("]; ");
                string? gapAudio = null;
                if (includeAudio)
                {
                    gapAudio = $"gapa{pieceIndex}";
                    filters.Append("anullsrc=r=48000:cl=stereo:d=")
                        .Append(FormatSeconds(gap))
                        .Append('[').Append(gapAudio).Append("]; ");
                }

                pieces.Add((gapVideo, gapAudio));
                pieceIndex++;
            }

            var duration = TimeSpan.FromTicks(Math.Max(1L, segment.DurationTicks));
            var video = $"segmentv{pieceIndex}";
            filters.Append('[').Append(inputIndex).Append(":v]")
                .Append("fps=").Append(fps)
                .Append(",scale=").Append(width).Append(':').Append(height)
                .Append(":force_original_aspect_ratio=decrease,pad=")
                .Append(width).Append(':').Append(height)
                .Append(":(ow-iw)/2:(oh-ih)/2:black,trim=duration=")
                .Append(FormatSeconds(duration))
                .Append(",setpts=PTS-STARTPTS[").Append(video).Append("]; ");
            string? audio = null;
            if (includeAudio)
            {
                audio = $"segmenta{pieceIndex}";
                if (segment.IncludesSystemAudio || segment.IncludesMicrophone)
                {
                    filters.Append('[').Append(inputIndex).Append(":a]")
                        .Append("aformat=sample_rates=48000:channel_layouts=stereo,")
                        .Append("aresample=48000:async=1:first_pts=0,atrim=duration=")
                        .Append(FormatSeconds(duration))
                        .Append(",asetpts=PTS-STARTPTS[").Append(audio).Append("]; ");
                }
                else
                {
                    filters.Append("anullsrc=r=48000:cl=stereo:d=")
                        .Append(FormatSeconds(duration))
                        .Append('[').Append(audio).Append("]; ");
                }
            }

            pieces.Add((video, audio));
            pieceIndex++;
            cursorTicks = Math.Max(
                cursorTicks,
                checked(segment.StartMonotonicTicks + Math.Max(1L, segment.DurationTicks)));
        }

        foreach (var piece in pieces)
        {
            filters.Append('[').Append(piece.Video).Append(']');
            if (includeAudio)
            {
                filters.Append('[').Append(piece.Audio).Append(']');
            }
        }

        filters.Append("concat=n=").Append(pieces.Count)
            .Append(includeAudio ? ":v=1:a=1[outv][outa]" : ":v=1:a=0[outv]");
        arguments.AddRange(["-filter_complex", filters.ToString(), "-map", "[outv]"]);
        if (includeAudio)
        {
            arguments.AddRange(["-map", "[outa]", "-c:a", "aac", "-b:a", "192k"]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(
        [
            "-c:v", "libopenh264",
            "-b:v", bitrate.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            "-r", fps.ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart",
            Path.GetFullPath(outputPath)
        ]);
        return arguments;
    }

    private static ReceiptSegmentManifest ResolveSceneSegment(
        ReplayReceiptDocument receipt,
        string trackId,
        ReceiptSceneMarker scene) =>
        receipt.Manifest.Segments.FirstOrDefault(segment =>
            segment.TrackId.Equals(trackId, StringComparison.Ordinal) &&
            segment.SegmentId.Equals(scene.SegmentId, StringComparison.Ordinal))
        ?? throw new InvalidDataException(
            $"Local scene '{scene.SceneId}' does not reference a signed segment in track '{trackId}'.");

    private static TimeSpan OffsetWithinSegment(ReceiptSegmentManifest segment, long monotonicTicks)
    {
        var maximumOffset = Math.Max(0L, segment.DurationTicks - 1L);
        return TimeSpan.FromTicks(Math.Clamp(
            monotonicTicks - segment.StartMonotonicTicks,
            0L,
            maximumOffset));
    }

    private async Task ExtractFrameAsync(
        string playback,
        TimeSpan at,
        string output,
        CancellationToken cancellationToken)
    {
        await _mediaTool.RunFfmpegAsync(
            [
                "-y", "-ss", Math.Max(0, at.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture),
                "-i", playback, "-frames:v", "1", "-q:v", "2", output
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeConcatPath(string path) =>
        Path.GetFullPath(path).Replace("'", "'\\''", StringComparison.Ordinal);

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private static string UniqueDirectory(string path) => UniquePath(path);
    private static string ShortId(string value)
    {
        var sanitized = SanitizeFileName(value);
        return sanitized[..Math.Min(8, sanitized.Length)];
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".."
            ? "track"
            : sanitized;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale concat list is harmless and can be removed by local cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Disposable previews are removed by normal temp cleanup if still in use.
        }
        catch (UnauthorizedAccessException)
        {
            // Disposable previews are removed by normal temp cleanup if still in use.
        }
    }

    private void TryDeleteDisposableMedia(string path)
    {
        var root = Path.GetFullPath(_paths.TempRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(candidate);
        }
    }
}

internal interface IReplayReceiptMediaTool
{
    Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed class ReplayReceiptMediaTool : IReplayReceiptMediaTool
{
    public async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var ffmpeg = RecordingService.FindFfmpeg()
            ?? throw new InvalidOperationException(
                "FFmpeg was not found. Set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias).");
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg failed with exit code {process.ExitCode}: {stderr.ReplaceLineEndings(" ").Trim()}");
        }
    }
}

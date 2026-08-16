using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ReceiptSceneAnalysisService
{
    public const string AnalysisFileName = "analysis.json";
    private const int AnalysisFramesPerSecond = 2;
    private const int MaximumAnalysisFramesPerSegment = 120;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IReceiptAnalysisOcr _ocr;
    private readonly IReceiptAnalysisMedia _media;

    public ReceiptSceneAnalysisService(OcrService ocr)
        : this(new ReceiptAnalysisOcr(ocr), new ReceiptAnalysisMedia())
    {
    }

    internal ReceiptSceneAnalysisService(
        IReceiptAnalysisOcr ocr,
        IReceiptAnalysisMedia media)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(media);
        _ocr = ocr;
        _media = media;
    }

    public async Task<ReceiptLocalAnalysis> AnalyzeAsync(
        string receiptRoot,
        double sensitivity,
        CancellationToken cancellationToken = default) => await AnalyzeAsync(
            receiptRoot,
            new ReceiptAnalysisOptions(
                EnableSceneIndexing: true,
                EnableOcrComparison: true,
                Sensitivity: sensitivity),
            cancellationToken).ConfigureAwait(false);

    public async Task<ReceiptLocalAnalysis> AnalyzeAsync(
        string receiptRoot,
        ReceiptAnalysisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = Path.GetFullPath(receiptRoot);
        var manifestPath = Path.Combine(root, ReceiptIntegrityService.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The signed receipt manifest was not found.", manifestPath);
        }

        var manifest = ReceiptCanonicalJson.Deserialize(
            await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        options = options.Normalize();
        var analysis = new ReceiptLocalAnalysis
        {
            ReceiptId = manifest.ReceiptId,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            Sensitivity = options.Sensitivity,
            SceneIndexingEnabled = options.EnableSceneIndexing,
            OcrComparisonEnabled = options.EnableOcrComparison
        };
        var analysisRoot = Path.Combine(root, "local-analysis");
        if (!options.EnableSceneIndexing && !options.EnableOcrComparison)
        {
            analysis.Warnings.Add("Local scene indexing and OCR comparison were both disabled.");
            await WriteAsync(analysis, analysisRoot, cancellationToken).ConfigureAwait(false);
            return analysis;
        }

        var frameRoot = Path.Combine(analysisRoot, "frames");
        Directory.CreateDirectory(frameRoot);
        var ffmpeg = _media.ResolveFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            analysis.Warnings.Add("FFmpeg was not found, so the enabled local replay analysis could not extract frames.");
            await WriteAsync(analysis, analysisRoot, cancellationToken).ConfigureAwait(false);
            return analysis;
        }

        var tracks = manifest.Tracks.ToDictionary(track => track.TrackId, StringComparer.Ordinal);
        var previousSceneFrames = new Dictionary<string, ReceiptAnalysisFrameState>(StringComparer.Ordinal);
        foreach (var segment in manifest.Segments
                     .OrderBy(segment => segment.TrackId, StringComparer.Ordinal)
                     .ThenBy(segment => segment.StartMonotonicTicks))
        {
            var safeSegment = SanitizeFileName(segment.SegmentId);
            foreach (var offset in EnumerateAnalysisOffsets(segment.DurationTicks))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monotonicTicks = segment.StartMonotonicTicks + offset.Ticks;
                var sourceId = ResolveSourceId(tracks[segment.TrackId], monotonicTicks);
                var relative = Path.Combine(
                    "local-analysis",
                    "frames",
                    $"{safeSegment}-{offset.Ticks:D19}.png");
                var framePath = Path.Combine(root, relative);
                var extracted = await _media.ExtractFrameAsync(
                    ffmpeg,
                    Path.Combine(root, segment.RelativePath),
                    framePath,
                    offset,
                    cancellationToken).ConfigureAwait(false);
                if (!extracted)
                {
                    analysis.Warnings.Add(
                        $"Could not extract an analysis frame from segment {segment.SegmentId} " +
                        $"at {offset.TotalSeconds:0.###} seconds.");
                    continue;
                }

                if (options.EnableOcrComparison)
                {
                    var ocr = await _ocr.RecognizeFileAsync(framePath, cancellationToken).ConfigureAwait(false);
                    analysis.Frames.Add(new ReceiptOcrFrame
                    {
                        TrackId = segment.TrackId,
                        SegmentId = segment.SegmentId,
                        SourceId = sourceId,
                        MonotonicTicks = monotonicTicks,
                        RelativeFramePath = relative.Replace('\\', '/'),
                        Text = ocr.Succeeded ? ocr.Text : string.Empty,
                        LanguageTag = ocr.LanguageTag,
                        OcrSucceeded = ocr.Succeeded
                    });
                }

                if (options.EnableSceneIndexing)
                {
                    previousSceneFrames.TryGetValue(segment.TrackId, out var previous);
                    var sourceChanged = previous is null ||
                        !previous.SourceId.Equals(sourceId, StringComparison.Ordinal);
                    var visuallyDistinct = previous is null ||
                        await _media.AreFramesDistinctAsync(
                            root,
                            previous.RelativeFramePath,
                            framePath,
                            options.Sensitivity,
                            cancellationToken)
                            .ConfigureAwait(false);
                    analysis.Scenes.Add(new ReceiptSceneMarker
                    {
                        TrackId = segment.TrackId,
                        SegmentId = segment.SegmentId,
                        MonotonicTicks = monotonicTicks,
                        RelativeFramePath = relative.Replace('\\', '/'),
                        IsSourceTransition = sourceChanged,
                        IsVisuallyDistinct = visuallyDistinct
                    });
                    previousSceneFrames[segment.TrackId] = new ReceiptAnalysisFrameState(
                        sourceId,
                        relative.Replace('\\', '/'));
                }
            }
        }

        if (options.EnableOcrComparison)
        {
            foreach (var trackFrames in analysis.Frames
                         .GroupBy(frame => frame.TrackId, StringComparer.Ordinal))
            {
                ReceiptOcrFrame? previous = null;
                foreach (var frame in trackFrames.OrderBy(frame => frame.MonotonicTicks))
                {
                    if (previous is not null &&
                        previous.SourceId.Equals(frame.SourceId, StringComparison.Ordinal) &&
                        previous.OcrSucceeded && frame.OcrSucceeded)
                    {
                        var change = CompareTexts(previous.Text, frame.Text, options.Sensitivity);
                        if (change is not null)
                        {
                            change.TrackId = frame.TrackId;
                            change.SourceId = frame.SourceId;
                            change.BeforeFrameId = previous.FrameId;
                            change.AfterFrameId = frame.FrameId;
                            analysis.Changes.Add(change);
                        }
                    }

                    previous = frame;
                }
            }
        }

        await WriteAsync(analysis, analysisRoot, cancellationToken).ConfigureAwait(false);
        return analysis;
    }

    public async Task SaveReviewStateAsync(
        string receiptRoot,
        string changeId,
        ReceiptChangeReviewState state,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetFullPath(receiptRoot), "local-analysis", AnalysisFileName);
        var analysis = JsonSerializer.Deserialize<ReceiptLocalAnalysis>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            JsonOptions) ?? throw new InvalidDataException("Receipt analysis was empty.");
        var change = analysis.Changes.SingleOrDefault(candidate =>
            candidate.ChangeId.Equals(changeId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"OCR change '{changeId}' was not found.");
        change.ReviewState = state;
        await WriteAsync(analysis, Path.GetDirectoryName(path)!, cancellationToken).ConfigureAwait(false);
    }

    internal static ReceiptOcrChange? CompareTexts(
        string before,
        string after,
        double sensitivity = 0.65d)
    {
        var normalizedBefore = NormalizeText(before);
        var normalizedAfter = NormalizeText(after);
        if (normalizedBefore.Equals(normalizedAfter, StringComparison.Ordinal))
        {
            return null;
        }

        var similarity = Similarity(normalizedBefore, normalizedAfter);
        var beforeTokens = Tokenize(normalizedBefore);
        var afterTokens = Tokenize(normalizedAfter);
        var added = afterTokens.Except(beforeTokens, StringComparer.Ordinal).ToArray();
        var deleted = beforeTokens.Except(afterTokens, StringComparer.Ordinal).ToArray();
        if (Math.Max(beforeTokens.Count, afterTokens.Count) > 0 &&
            (added.Length + deleted.Length) / (double)Math.Max(beforeTokens.Count, afterTokens.Count) <
            Math.Clamp(1d - sensitivity, 0.08d, 0.75d))
        {
            return null;
        }

        var kind = deleted.Length == 0 && added.Length > 0
            ? ReceiptOcrChangeKind.PossibleAddition
            : added.Length == 0 && deleted.Length > 0
                ? ReceiptOcrChangeKind.PossibleDeletion
                : ReceiptOcrChangeKind.PossibleEdit;
        return new ReceiptOcrChange
        {
            Kind = kind,
            BeforeText = before,
            AfterText = after,
            Similarity = similarity,
            Explanation = kind switch
            {
                ReceiptOcrChangeKind.PossibleAddition =>
                    $"Local OCR found text in the later frame that was not recognized before ({added.Length} token(s)).",
                ReceiptOcrChangeKind.PossibleDeletion =>
                    $"Local OCR no longer found text recognized in the earlier frame ({deleted.Length} token(s)).",
                _ =>
                    $"Local OCR found both added and removed text ({added.Length} added, {deleted.Length} removed token(s))."
            }
        };
    }

    private static string ResolveSourceId(ReceiptTrackManifest track, long monotonicTicks)
    {
        return track.SourceTransitions
            .Where(transition => transition.EffectiveStartMonotonicTicks <= monotonicTicks)
            .OrderByDescending(transition => transition.EffectiveStartMonotonicTicks)
            .Select(transition => transition.SourceId)
            .FirstOrDefault() ?? track.SourceId;
    }

    internal static IReadOnlyList<TimeSpan> EnumerateAnalysisOffsets(long durationTicks)
    {
        var duration = TimeSpan.FromTicks(Math.Max(1, durationTicks));
        var requestedFrames = duration.TotalSeconds >=
            MaximumAnalysisFramesPerSegment / (double)AnalysisFramesPerSecond
                ? MaximumAnalysisFramesPerSegment
                : Math.Max(1, (int)Math.Ceiling(
                    duration.TotalSeconds * AnalysisFramesPerSecond));
        var offsets = new TimeSpan[requestedFrames];
        var ticksPerSample = duration.Ticks / requestedFrames;
        for (var index = 0; index < requestedFrames; index++)
        {
            offsets[index] = TimeSpan.FromTicks(ticksPerSample * index);
        }

        return offsets;
    }

    internal static async Task<bool> ExtractFrameAsync(
        string ffmpeg,
        string videoPath,
        string outputPath,
        TimeSpan at,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("error");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-ss");
        start.ArgumentList.Add(at.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(videoPath);
        start.ArgumentList.Add("-frames:v");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add(outputPath);
        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 && File.Exists(outputPath);
    }

    internal static async Task<bool> AreFramesDistinctAsync(
        string root,
        string previousRelativePath,
        string currentPath,
        double sensitivity,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var before = new System.Drawing.Bitmap(Path.Combine(root, previousRelativePath));
            using var after = new System.Drawing.Bitmap(currentPath);
            using var beforeSmall = new System.Drawing.Bitmap(before, 16, 16);
            using var afterSmall = new System.Drawing.Bitmap(after, 16, 16);
            double difference = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var left = beforeSmall.GetPixel(x, y);
                    var right = afterSmall.GetPixel(x, y);
                    difference += Math.Abs(left.R - right.R) +
                        Math.Abs(left.G - right.G) +
                        Math.Abs(left.B - right.B);
                }
            }

            difference /= 16d * 16d * 3d * 255d;
            return difference >= Math.Clamp((1d - sensitivity) * 0.35d, 0.025d, 0.25d);
        }, cancellationToken).ConfigureAwait(false);
    }

    private sealed record ReceiptAnalysisFrameState(string SourceId, string RelativeFramePath);

    private static async Task WriteAsync(
        ReceiptLocalAnalysis analysis,
        string analysisRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(analysisRoot);
        var target = Path.Combine(analysisRoot, AnalysisFileName);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(analysis, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// The exact normalize-then-tokenize pipeline <see cref="CompareTexts"/> uses, exposed so the
    /// capture comparison feature computes its highlight sets with identical arithmetic.
    /// </summary>
    internal static IReadOnlySet<string> TokenizeForComparison(string? text) =>
        Tokenize(NormalizeText(text ?? string.Empty));

    private static string NormalizeText(string value) => string.Join(
        ' ',
        (value ?? string.Empty)
            .ReplaceLineEndings(" ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .ToLowerInvariant();

    private static IReadOnlySet<string> Tokenize(string text) => text
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
        .Where(token => token.Length > 0)
        .ToHashSet(StringComparer.Ordinal);

    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 1d;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return 1d - (previous[^1] / (double)Math.Max(left.Length, right.Length));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }
}

internal interface IReceiptAnalysisOcr
{
    Task<OcrRecognitionResult> RecognizeFileAsync(
        string framePath,
        CancellationToken cancellationToken);
}

internal sealed class ReceiptAnalysisOcr(OcrService ocr) : IReceiptAnalysisOcr
{
    private readonly OcrService _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));

    public Task<OcrRecognitionResult> RecognizeFileAsync(
        string framePath,
        CancellationToken cancellationToken) =>
        _ocr.RecognizeFileAsync(framePath, cancellationToken: cancellationToken);
}

internal interface IReceiptAnalysisMedia
{
    string? ResolveFfmpeg();

    Task<bool> ExtractFrameAsync(
        string ffmpeg,
        string videoPath,
        string outputPath,
        TimeSpan at,
        CancellationToken cancellationToken);

    Task<bool> AreFramesDistinctAsync(
        string root,
        string previousRelativePath,
        string currentPath,
        double sensitivity,
        CancellationToken cancellationToken);
}

internal sealed class ReceiptAnalysisMedia : IReceiptAnalysisMedia
{
    public string? ResolveFfmpeg() => RecordingService.FindFfmpeg();

    public Task<bool> ExtractFrameAsync(
        string ffmpeg,
        string videoPath,
        string outputPath,
        TimeSpan at,
        CancellationToken cancellationToken) =>
        ReceiptSceneAnalysisService.ExtractFrameAsync(
            ffmpeg,
            videoPath,
            outputPath,
            at,
            cancellationToken);

    public Task<bool> AreFramesDistinctAsync(
        string root,
        string previousRelativePath,
        string currentPath,
        double sensitivity,
        CancellationToken cancellationToken) =>
        ReceiptSceneAnalysisService.AreFramesDistinctAsync(
            root,
            previousRelativePath,
            currentPath,
            sensitivity,
            cancellationToken);
}

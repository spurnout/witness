using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AdvancedVideoEditPlannerService
{
    private static readonly IReadOnlyList<string> DefaultFillerTerms =
    [
        "um",
        "uh",
        "erm",
        "ah",
        "like",
        "you know",
        "sort of",
        "kind of"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;

    public AdvancedVideoEditPlannerService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<AdvancedVideoEditPlan> PlanSilenceRemovalAsync(
        CaptureItem item,
        double noiseThresholdDb = -35d,
        TimeSpan? minimumSilence = null,
        TimeSpan? padding = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed("silence-removal", $"Video file not found: {item.FilePath}");
        }

        minimumSilence ??= TimeSpan.FromSeconds(0.7d);
        padding ??= TimeSpan.FromSeconds(0.15d);
        if (minimumSilence.Value <= TimeSpan.Zero)
        {
            return Failed("silence-removal", "Minimum silence duration must be greater than zero.");
        }

        if (padding.Value < TimeSpan.Zero)
        {
            return Failed("silence-removal", "Silence padding must be zero or greater.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("silence-removal", "FFmpeg was not found. Install FFmpeg or set GOATSHOT_FFMPEG_PATH to enable audio-level silence planning.");
        }

        var detection = await RunSilenceDetectAsync(
            ffmpeg,
            item.FilePath,
            noiseThresholdDb,
            minimumSilence.Value,
            cancellationToken);
        if (!detection.Succeeded)
        {
            return Failed("silence-removal", detection.Message, item.FilePath);
        }

        var spans = ParseSilenceDetectOutput(detection.Output);
        var cuts = BuildSilenceCuts(spans, padding.Value).ToList();
        return new AdvancedVideoEditPlan
        {
            Succeeded = true,
            PlanType = "silence-removal",
            SourcePath = item.FilePath,
            Message = cuts.Count == 0
                ? "No removable silence ranges were detected."
                : $"Detected {cuts.Count} preview cut(s) from audio-level silence analysis.",
            Cuts = cuts,
            Warnings =
            [
                "Preview only: no media was modified.",
                "Review every cut before export; quiet but meaningful speech can be misclassified as silence.",
                $"Silence threshold: {noiseThresholdDb.ToString("0.###", CultureInfo.InvariantCulture)} dB; minimum silence: {minimumSilence.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s; padding: {padding.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s."
            ]
        };
    }

    public async Task<AdvancedVideoEditPlan> PlanTranscriptTermsAsync(
        string srtPath,
        IReadOnlyList<string> terms,
        string planType = "text-based-edit",
        TimeSpan? padding = null,
        CancellationToken cancellationToken = default)
    {
        srtPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(srtPath));
        if (!File.Exists(srtPath))
        {
            return Failed(planType, $"SRT file not found: {srtPath}");
        }

        var text = await File.ReadAllTextAsync(srtPath, cancellationToken);
        var segments = ParseSrtOrTimestampedTranscript(text);
        if (segments.Count == 0)
        {
            return Failed(planType, "No timestamped transcript or SRT segments were found.", srtPath);
        }

        var normalizedTerms = NormalizeTerms(terms);
        if (normalizedTerms.Count == 0)
        {
            return Failed(planType, "At least one transcript term is required.", srtPath);
        }

        var cuts = BuildTranscriptTermCuts(segments, normalizedTerms, padding ?? TimeSpan.FromMilliseconds(250)).ToList();
        return new AdvancedVideoEditPlan
        {
            Succeeded = true,
            PlanType = planType,
            SourcePath = srtPath,
            Message = cuts.Count == 0
                ? "No transcript segments matched the requested term(s)."
                : $"Found {cuts.Count} preview cut(s) from transcript term matching.",
            Terms = normalizedTerms.ToList(),
            Cuts = cuts,
            Warnings =
            [
                "Preview only: no media was modified.",
                "Transcript timing can be imprecise; review every range before export.",
                "This planner removes whole caption segments, not individual words inside a segment."
            ]
        };
    }

    public Task<AdvancedVideoEditPlan> PlanFillerWordRemovalAsync(
        string srtPath,
        IReadOnlyList<string>? fillerTerms = null,
        TimeSpan? padding = null,
        CancellationToken cancellationToken = default)
    {
        return PlanTranscriptTermsAsync(
            srtPath,
            fillerTerms is { Count: > 0 } ? fillerTerms : DefaultFillerTerms,
            "filler-word-removal",
            padding,
            cancellationToken);
    }

    public AdvancedVideoEditPlan BuildCompositeLayoutPlan(CompositeVideoLayoutRequest request)
    {
        var width = request.Width > 0 ? request.Width : 1920;
        var height = request.Height > 0 ? request.Height : 1080;
        var preset = NormalizeCompositePreset(request.Preset);
        var screenPath = ResolveOptionalPath(request.ScreenPath);
        var cameraPath = ResolveOptionalPath(request.CameraPath);
        var recipe = BuildCompositeRecipe(preset, width, height, screenPath, cameraPath);
        var capability = ProbeWebcamBackgroundProcessing();
        return new AdvancedVideoEditPlan
        {
            Succeeded = true,
            PlanType = "composite-layout",
            SourcePath = screenPath ?? cameraPath ?? string.Empty,
            Message = $"Composite layout recipe created: {recipe.Name}.",
            CompositeRecipes = [recipe],
            CapabilityProbe = capability,
            Warnings =
            [
                "Preview only: no media was modified.",
                "Run a short preview export before using the recipe on long recordings.",
                "Webcam background blur/removal is not auto-applied; it requires an explicit capability probe and preview approval."
            ]
        };
    }

    public AdvancedVideoCapabilityProbe ProbeWebcamBackgroundProcessing()
    {
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return new AdvancedVideoCapabilityProbe
            {
                Capability = "webcam-background-processing",
                Available = false,
                PreviewRequired = true,
                Message = "FFmpeg was not found; background blur/removal remains unavailable."
            };
        }

        return new AdvancedVideoCapabilityProbe
        {
            Capability = "webcam-background-processing",
            Available = true,
            PreviewRequired = true,
            Message = "FFmpeg keyed-background processing, deterministic foreground-mask generation, reviewed external mask/matte compositing, explicit local external-runner person mask generation, governed hosted service handoff, stage-only model validation/staging, and supplied mask-quality evaluation are available. GoatShot still does not bundle, trust, enable, register, run inference from, host, provide or prove first-party hosted accounts for, or broadly quality-certify a segmentation model."
        };
    }

    public AdvancedVideoEditPlan BuildWebcamBackgroundPlan(WebcamBackgroundProcessingRequest request)
    {
        var sourcePath = ResolveOptionalPath(request.SourcePath);
        var maskPath = ResolveOptionalPath(request.MaskPath);
        var mode = NormalizeBackgroundMode(request.Mode);
        var usesExternalMask = !string.IsNullOrWhiteSpace(maskPath);
        var recipe = new WebcamBackgroundProcessingRecipe
        {
            Name = BuildBackgroundRecipeName(mode, usesExternalMask),
            Mode = mode,
            SourcePath = sourcePath,
            ProcessingKind = usesExternalMask ? "external-mask" : "keyed",
            MaskPath = maskPath,
            InvertMask = request.InvertMask,
            KeyColor = NormalizeFfmpegColor(request.KeyColor, "0x00ff00"),
            Similarity = Clamp(request.Similarity ?? 0.18d, 0.01d, 1d),
            Blend = Clamp(request.Blend ?? 0.08d, 0d, 1d),
            BlurStrength = Math.Clamp(request.BlurStrength ?? 12, 1, 64),
            BackgroundColor = NormalizeFfmpegColor(request.BackgroundColor, "0x101820"),
            Notes = usesExternalMask
                ? "Uses a reviewed external luma mask/person matte with FFmpeg compositing. GoatShot can generate deterministic masks or run an explicitly accepted local external person-segmentation runner separately, but this recipe does not itself generate a mask or bundle a model."
                : "Uses FFmpeg chromakey/colorkey-style processing for keyed webcam backgrounds. It is not a general person-segmentation model."
        };
        return new AdvancedVideoEditPlan
        {
            Succeeded = true,
            PlanType = "webcam-background",
            SourcePath = sourcePath ?? string.Empty,
            Message = $"Webcam background recipe created: {recipe.Name}.",
            BackgroundRecipe = recipe,
            CapabilityProbe = ProbeWebcamBackgroundProcessing(),
            Warnings =
            [
                "Preview only: no media was modified.",
                usesExternalMask
                    ? "The mask/matte is treated as already-reviewed foreground evidence; white/luma keeps the subject by default."
                    : "This is keyed-background processing for green-screen/key-color footage, not automatic human segmentation.",
                usesExternalMask
                    ? "Mask files can come from GoatShot's deterministic mask generator, an explicitly accepted local external person-segmentation runner, or another reviewed source; they must align with the source clip and avoid private/unsafe content."
                    : "Use --mask with an externally generated person matte when chromakey footage is not available.",
                "Review a short preview export before applying to long recordings.",
                "Plan files may contain local media paths."
            ]
        };
    }

    public async Task<string> WritePlanAsync(
        AdvancedVideoEditPlan plan,
        string? outputPath,
        CancellationToken cancellationToken = default)
    {
        outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_paths.DocumentsRoot, $"advanced-video-plan-{plan.PlanType}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        return outputPath;
    }

    public static IReadOnlyList<DetectedSilenceSpan> ParseSilenceDetectOutput(string output)
    {
        var spans = new List<DetectedSilenceSpan>();
        double? pendingStart = null;
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var start = Regex.Match(line, @"silence_start:\s*(?<value>-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (start.Success && double.TryParse(start.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds))
            {
                pendingStart = Math.Max(0d, startSeconds);
                continue;
            }

            var end = Regex.Match(
                line,
                @"silence_end:\s*(?<end>-?\d+(?:\.\d+)?)(?:\s*\|\s*silence_duration:\s*(?<duration>-?\d+(?:\.\d+)?))?",
                RegexOptions.IgnoreCase);
            if (!end.Success ||
                !double.TryParse(end.Groups["end"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var endSeconds))
            {
                continue;
            }

            double? durationSeconds = null;
            if (end.Groups["duration"].Success &&
                double.TryParse(end.Groups["duration"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration))
            {
                durationSeconds = parsedDuration;
            }

            var resolvedStart = pendingStart ?? (durationSeconds.HasValue ? endSeconds - durationSeconds.Value : 0d);
            resolvedStart = Math.Max(0d, resolvedStart);
            endSeconds = Math.Max(resolvedStart, endSeconds);
            spans.Add(new DetectedSilenceSpan(
                spans.Count + 1,
                resolvedStart,
                endSeconds,
                Math.Max(0d, durationSeconds ?? endSeconds - resolvedStart)));
            pendingStart = null;
        }

        return spans;
    }

    public static IReadOnlyList<VideoEditCut> BuildSilenceCuts(
        IReadOnlyList<DetectedSilenceSpan> spans,
        TimeSpan padding)
    {
        var paddingSeconds = Math.Max(0d, padding.TotalSeconds);
        return spans
            .Select(span =>
            {
                var start = span.StartSeconds + paddingSeconds;
                var end = span.EndSeconds - paddingSeconds;
                return new VideoEditCut
                {
                    Index = span.Index,
                    StartSeconds = RoundSeconds(start),
                    EndSeconds = RoundSeconds(end),
                    DurationSeconds = RoundSeconds(Math.Max(0d, end - start)),
                    Reason = "Detected silence",
                    Source = "ffmpeg silencedetect",
                    SourceText = $"silence {span.StartSeconds:0.###}-{span.EndSeconds:0.###}s"
                };
            })
            .Where(cut => cut.DurationSeconds > 0d)
            .ToList();
    }

    public static IReadOnlyList<VideoEditCut> BuildTranscriptTermCuts(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<string> terms,
        TimeSpan padding)
    {
        var normalized = NormalizeTerms(terms);
        var paddingSeconds = Math.Max(0d, padding.TotalSeconds);
        var cuts = new List<VideoEditCut>();
        foreach (var segment in segments)
        {
            var text = segment.Text ?? string.Empty;
            var matched = normalized
                .Where(term => Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(term)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();
            if (matched.Count == 0)
            {
                continue;
            }

            var start = Math.Max(0d, segment.Start.TotalSeconds - paddingSeconds);
            var end = segment.End.TotalSeconds + paddingSeconds;
            cuts.Add(new VideoEditCut
            {
                Index = cuts.Count + 1,
                StartSeconds = RoundSeconds(start),
                EndSeconds = RoundSeconds(end),
                DurationSeconds = RoundSeconds(Math.Max(0d, end - start)),
                Reason = "Transcript term match: " + string.Join(", ", matched),
                Source = "Timestamped transcript/SRT",
                SourceText = text
            });
        }

        return cuts;
    }

    public static IReadOnlyList<TranscriptSegment> ParseSrtOrTimestampedTranscript(string text)
    {
        var srtSegments = TranscriptionService.ParseSrt(text);
        return srtSegments.Count > 0
            ? srtSegments
            : ParseTimestampedTranscript(text);
    }

    public static IReadOnlyList<TranscriptSegment> ParseTimestampedTranscript(string text)
    {
        var matches = new List<(TimeSpan Start, string Text)>();
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line, @"^\[(?<time>[^\]]+)\]\s*(?<text>.+)$");
            if (!match.Success || !TryParseLooseTimestamp(match.Groups["time"].Value, out var start))
            {
                continue;
            }

            matches.Add((start, match.Groups["text"].Value.Trim()));
        }

        var segments = new List<TranscriptSegment>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Start;
            var end = i < matches.Count - 1 && matches[i + 1].Start > start
                ? matches[i + 1].Start
                : start + TimeSpan.FromSeconds(2);
            segments.Add(new TranscriptSegment
            {
                Index = i + 1,
                Start = start,
                End = end,
                Text = matches[i].Text
            });
        }

        return segments;
    }

    public static IReadOnlyList<string> NormalizeTerms(IEnumerable<string>? terms)
    {
        return (terms ?? [])
            .SelectMany(term => term.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(term => term.Trim().ToLowerInvariant())
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<(bool Succeeded, string Output, string Message)> RunSilenceDetectAsync(
        string ffmpeg,
        string inputPath,
        double noiseThresholdDb,
        TimeSpan minimumSilence,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-hide_banner");
        start.ArgumentList.Add("-nostats");
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(inputPath);
        start.ArgumentList.Add("-af");
        start.ArgumentList.Add($"silencedetect=noise={noiseThresholdDb.ToString("0.###", CultureInfo.InvariantCulture)}dB:d={minimumSilence.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("null");
        start.ArgumentList.Add("-");

        using var process = Process.Start(start);
        if (process is null)
        {
            return (false, string.Empty, "FFmpeg could not be started for silence detection.");
        }

        string stderr;
        string stdout;
        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            stderr = await stderrTask;
            stdout = await stdoutTask;
        }
        catch (OperationCanceledException)
        {
            try
            {
                // Silence detection decodes the whole file; don't orphan ffmpeg on cancel.
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup on cancellation.
            }

            throw;
        }

        var output = stderr + Environment.NewLine + stdout;
        return process.ExitCode == 0
            ? (true, output, "Silence detection completed.")
            : (false, output, $"FFmpeg silence detection failed with exit code {process.ExitCode}. {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
    }

    private static CompositeVideoLayoutRecipe BuildCompositeRecipe(
        string preset,
        int width,
        int height,
        string? screenPath,
        string? cameraPath)
    {
        var screenInput = string.IsNullOrWhiteSpace(screenPath) ? "<screen.mp4>" : screenPath;
        var cameraInput = string.IsNullOrWhiteSpace(cameraPath) ? "<camera.mp4>" : cameraPath;
        return preset switch
        {
            "side-by-side" => new CompositeVideoLayoutRecipe
            {
                Name = "Side by side",
                Preset = preset,
                Width = width,
                Height = height,
                ScreenPath = screenPath,
                CameraPath = cameraPath,
                FfmpegFilterComplex = $"[0:v]scale={width / 2}:{height}:force_original_aspect_ratio=decrease,pad={width / 2}:{height}:(ow-iw)/2:(oh-ih)/2[screen];[1:v]scale={width / 2}:{height}:force_original_aspect_ratio=decrease,pad={width / 2}:{height}:(ow-iw)/2:(oh-ih)/2[camera];[screen][camera]hstack=inputs=2[outv]",
                ExportCommandPreview = $"ffmpeg -i \"{screenInput}\" -i \"{cameraInput}\" -filter_complex \"<side-by-side filter>\" -map \"[outv]\" -map 0:a? output.mp4",
                Notes = "Screen and camera receive equal horizontal space."
            },
            "stacked" => new CompositeVideoLayoutRecipe
            {
                Name = "Stacked screen over camera",
                Preset = preset,
                Width = width,
                Height = height,
                ScreenPath = screenPath,
                CameraPath = cameraPath,
                FfmpegFilterComplex = $"[0:v]scale={width}:{height * 3 / 4}:force_original_aspect_ratio=decrease,pad={width}:{height * 3 / 4}:(ow-iw)/2:(oh-ih)/2[screen];[1:v]scale={width}:{height / 4}:force_original_aspect_ratio=decrease,pad={width}:{height / 4}:(ow-iw)/2:(oh-ih)/2[camera];[screen][camera]vstack=inputs=2[outv]",
                ExportCommandPreview = $"ffmpeg -i \"{screenInput}\" -i \"{cameraInput}\" -filter_complex \"<stacked filter>\" -map \"[outv]\" -map 0:a? output.mp4",
                Notes = "Screen stays primary; camera spans the lower band."
            },
            _ => new CompositeVideoLayoutRecipe
            {
                Name = "Picture in picture",
                Preset = "picture-in-picture",
                Width = width,
                Height = height,
                ScreenPath = screenPath,
                CameraPath = cameraPath,
                FfmpegFilterComplex = $"[0:v]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2[screen];[1:v]scale={width / 4}:-2[camera];[screen][camera]overlay=W-w-48:H-h-48[outv]",
                ExportCommandPreview = $"ffmpeg -i \"{screenInput}\" -i \"{cameraInput}\" -filter_complex \"<picture-in-picture filter>\" -map \"[outv]\" -map 0:a? output.mp4",
                Notes = "Camera overlays the lower-right corner with a 48 px margin."
            }
        };
    }

    private static string NormalizeCompositePreset(string? preset)
    {
        preset = (preset ?? "picture-in-picture").Trim().ToLowerInvariant();
        return preset switch
        {
            "pip" or "picture" or "picture-in-picture" => "picture-in-picture",
            "side" or "side-by-side" or "split" => "side-by-side",
            "stack" or "stacked" or "vertical" => "stacked",
            _ => "picture-in-picture"
        };
    }

    private static string NormalizeBackgroundMode(string? mode)
    {
        mode = (mode ?? "blur").Trim().ToLowerInvariant();
        return mode switch
        {
            "replace" or "replacement" or "solid" or "color" => "replace",
            "remove" or "removal" or "transparent" => "remove",
            _ => "blur"
        };
    }

    private static string BuildBackgroundRecipeName(string mode, bool usesExternalMask)
    {
        if (usesExternalMask)
        {
            return mode switch
            {
                "replace" => "External mask background replacement",
                "remove" => "External mask background removal",
                _ => "External mask background blur"
            };
        }

        return mode switch
        {
            "replace" => "Keyed background replacement",
            "remove" => "Keyed background removal",
            _ => "Keyed background blur"
        };
    }

    internal static string NormalizeFfmpegColor(string? color, string fallback)
    {
        color = string.IsNullOrWhiteSpace(color)
            ? fallback
            : color.Trim().ToLowerInvariant();
        return color switch
        {
            "green" => "0x00ff00",
            "blue" => "0x0000ff",
            "black" => "0x000000",
            "white" => "0xffffff",
            _ when color.StartsWith('#') && color.Length == 7 => $"0x{color[1..]}",
            _ when color.Length == 6 && color.All(Uri.IsHexDigit) => $"0x{color}",
            _ when color.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && color.Length == 8 => color,
            _ => fallback
        };
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static string? ResolveOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static bool TryParseLooseTimestamp(string value, out TimeSpan time)
    {
        value = value.Trim().Replace(',', '.');
        var parts = value.Split(':');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (parts.Length == 3 &&
            double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
            double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        time = TimeSpan.Zero;
        return false;
    }

    private static double RoundSeconds(double value)
    {
        return Math.Round(Math.Max(0d, value), 3, MidpointRounding.AwayFromZero);
    }

    private static AdvancedVideoEditPlan Failed(string planType, string message, string? sourcePath = null)
    {
        return new AdvancedVideoEditPlan
        {
            Succeeded = false,
            PlanType = planType,
            SourcePath = sourcePath ?? string.Empty,
            Message = message,
            Warnings = ["Preview only: no media was modified."]
        };
    }
}

public sealed class AdvancedVideoEditPlan
{
    public bool Succeeded { get; set; }
    public string PlanType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string SourcePath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Mode { get; set; } = "preview-only";
    public string PrivacyNote { get; set; } = "Plan files may include transcript excerpts and local file paths. Redact private content before sharing externally.";
    public string ReviewRequired { get; set; } = "Review and accept the plan before exporting edited media.";
    public List<string> Terms { get; set; } = new();
    public List<VideoEditCut> Cuts { get; set; } = new();
    public List<CompositeVideoLayoutRecipe> CompositeRecipes { get; set; } = new();
    public WebcamBackgroundProcessingRecipe? BackgroundRecipe { get; set; }
    public AdvancedVideoCapabilityProbe? CapabilityProbe { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class VideoEditCut
{
    public int Index { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
}

public sealed record DetectedSilenceSpan(
    int Index,
    double StartSeconds,
    double EndSeconds,
    double DurationSeconds);

public sealed class CompositeVideoLayoutRequest
{
    public string? Preset { get; set; }
    public string? ScreenPath { get; set; }
    public string? CameraPath { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
}

public sealed class CompositeVideoLayoutRecipe
{
    public string Name { get; set; } = string.Empty;
    public string Preset { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ScreenPath { get; set; }
    public string? CameraPath { get; set; }
    public string FfmpegFilterComplex { get; set; } = string.Empty;
    public string ExportCommandPreview { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class WebcamBackgroundProcessingRequest
{
    public string? SourcePath { get; set; }
    public string? Mode { get; set; }
    public string? MaskPath { get; set; }
    public bool InvertMask { get; set; }
    public string? KeyColor { get; set; }
    public double? Similarity { get; set; }
    public double? Blend { get; set; }
    public int? BlurStrength { get; set; }
    public string? BackgroundColor { get; set; }
}

public sealed class WebcamBackgroundProcessingRecipe
{
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "blur";
    public string? SourcePath { get; set; }
    public string ProcessingKind { get; set; } = "keyed";
    public string? MaskPath { get; set; }
    public bool InvertMask { get; set; }
    public string KeyColor { get; set; } = "0x00ff00";
    public double Similarity { get; set; } = 0.18d;
    public double Blend { get; set; } = 0.08d;
    public int BlurStrength { get; set; } = 12;
    public string BackgroundColor { get; set; } = "0x101820";
    public string Notes { get; set; } = string.Empty;
}

public sealed class WebcamMaskGenerationOptions
{
    public string? Method { get; set; }
    public string? KeyColor { get; set; }
    public double? Similarity { get; set; }
    public double? Blend { get; set; }
    public int? LumaThreshold { get; set; }
    public bool InvertMask { get; set; }
}

public sealed class PersonSegmentationMaskGenerationOptions
{
    public string? RunnerPath { get; set; }
    public string? ArgumentsTemplate { get; set; }
    public string? ModelPath { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool AcceptExternalRunner { get; set; }
}

public sealed class AdvancedVideoCapabilityProbe
{
    public string Capability { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool PreviewRequired { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}

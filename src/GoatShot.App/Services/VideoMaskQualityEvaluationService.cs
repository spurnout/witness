using System.Drawing;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class VideoMaskQualityEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<VideoMaskQualityEvaluationResult> EvaluateAsync(
        VideoMaskQualityEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.GetFullPath(Path.Combine("artifacts", "video-mask-quality"))
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));

        var threshold = Math.Clamp(request.ForegroundThreshold ?? 128, 0, 255);
        var result = new VideoMaskQualityEvaluationResult
        {
            OutputPath = outputRoot,
            GeneratedMaskPath = NormalizeDisplayPath(request.GeneratedMaskPath),
            ReferenceMaskPath = NormalizeDisplayPath(request.ReferenceMaskPath),
            SourceVideoPath = NormalizeDisplayPath(request.SourceVideoPath),
            OperatorName = SensitiveTextDetector.Redact(request.OperatorName ?? string.Empty),
            Note = SensitiveTextDetector.Redact(request.Note ?? string.Empty),
            EvaluatedAt = request.EvaluatedAt ?? DateTimeOffset.Now,
            ForegroundThreshold = threshold,
            MinimumIntersectionOverUnion = ClampUnit(request.MinimumIntersectionOverUnion ?? 0.90d),
            MinimumPrecision = ClampUnit(request.MinimumPrecision ?? 0.90d),
            MinimumRecall = ClampUnit(request.MinimumRecall ?? 0.90d),
            MaxFrames = request.MaxFrames is > 0 ? request.MaxFrames : null,
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? 600, 5, 3600),
            WouldDownloadModel = false,
            WouldRunSegmentationModel = false,
            WouldContactHostedService = false,
            WouldMutateSourceMedia = false,
            WouldCertifyWholeModel = false
        };

        var generatedPath = NormalizeInputPath(request.GeneratedMaskPath);
        var referencePath = NormalizeInputPath(request.ReferenceMaskPath);
        var generatedIsVideo = !string.IsNullOrWhiteSpace(generatedPath) && LooksLikeVideoPath(generatedPath);
        var referenceIsVideo = !string.IsNullOrWhiteSpace(referencePath) && LooksLikeVideoPath(referencePath);

        if (string.IsNullOrWhiteSpace(generatedPath))
        {
            result.Issues.Add("Generated mask is required. Pass --generated-mask <image-or-video>.");
        }
        else if (!File.Exists(generatedPath))
        {
            result.Issues.Add($"Generated mask was not found: {SensitiveTextDetector.Redact(generatedPath)}");
        }

        if (string.IsNullOrWhiteSpace(referencePath))
        {
            result.Issues.Add("Reference mask is required. Pass --reference-mask <image-or-video>.");
        }
        else if (!File.Exists(referencePath))
        {
            result.Issues.Add($"Reference mask was not found: {SensitiveTextDetector.Redact(referencePath)}");
        }

        if (result.Issues.Count == 0 && generatedIsVideo != referenceIsVideo)
        {
            result.Issues.Add("Generated and reference masks must both be still images or both be video files for frame-by-frame evaluation.");
        }

        if (result.Issues.Count == 0)
        {
            try
            {
                if (generatedIsVideo && referenceIsVideo)
                {
                    await EvaluateVideoFramesAsync(result, generatedPath!, referencePath!, outputRoot, threshold, request, cancellationToken);
                }
                else
                {
                    result.EvaluationMode = "still-image";
                    result.GeneratedFrameCount = 1;
                    result.ReferenceFrameCount = 1;
                    EvaluatePixels(result, generatedPath!, referencePath!, threshold, frameIndex: 1);
                    CompleteAggregateMetrics(result);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or OutOfMemoryException or IOException or UnauthorizedAccessException)
            {
                result.Issues.Add($"Mask quality could not be evaluated: {ex.Message}");
            }
        }

        result.QualityPassed = result.Issues.Count == 0 &&
            result.IntersectionOverUnion >= result.MinimumIntersectionOverUnion &&
            result.Precision >= result.MinimumPrecision &&
            result.Recall >= result.MinimumRecall;
        if (result.Issues.Count == 0 && !result.QualityPassed)
        {
            result.Issues.Add("Mask quality did not meet the configured IoU, precision, and recall thresholds.");
        }

        result.Succeeded = result.Issues.Count == 0;
        result.Message = result.QualityPassed
            ? $"Mask quality evaluation passed. IoU={FormatMetric(result.IntersectionOverUnion)}, precision={FormatMetric(result.Precision)}, recall={FormatMetric(result.Recall)}."
            : result.Succeeded
                ? "Mask quality evaluation completed without runtime issues but did not pass the configured thresholds."
                : "Mask quality evaluation has blockers.";

        Directory.CreateDirectory(outputRoot);
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "mask-quality-evaluation.md",
            BuildMarkdown(result),
            cancellationToken));
        result.GeneratedFiles.Add(await WriteFileAsync(
            outputRoot,
            "mask-quality-evaluation.json",
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken));

        return result;
    }

    private static async Task EvaluateVideoFramesAsync(
        VideoMaskQualityEvaluationResult result,
        string generatedPath,
        string referencePath,
        string outputRoot,
        int threshold,
        VideoMaskQualityEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        result.EvaluationMode = "video-frames";
        var ffmpeg = NormalizeInputPath(request.FfmpegPath) ?? RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
        {
            result.Issues.Add("FFmpeg is required to evaluate mask videos frame-by-frame. Install ffmpeg.exe, add it to PATH, set GOATSHOT_FFMPEG_PATH, or evaluate extracted still frames.");
            return;
        }

        result.FfmpegPath = NormalizeDisplayPath(ffmpeg);

        Directory.CreateDirectory(outputRoot);
        var frameRoot = Path.Combine(outputRoot, "extracted-frames");
        if (Directory.Exists(frameRoot))
        {
            Directory.Delete(frameRoot, recursive: true);
        }

        var generatedFrameRoot = Path.Combine(frameRoot, "generated");
        var referenceFrameRoot = Path.Combine(frameRoot, "reference");
        Directory.CreateDirectory(generatedFrameRoot);
        Directory.CreateDirectory(referenceFrameRoot);
        result.ExtractedFramesRoot = NormalizeDisplayPath(frameRoot);

        var generatedExtraction = await ExtractVideoFramesAsync(
            ffmpeg,
            generatedPath,
            generatedFrameRoot,
            result.MaxFrames,
            result.TimeoutSeconds,
            cancellationToken);
        if (!generatedExtraction.Succeeded)
        {
            result.Issues.Add($"Generated mask video frames could not be extracted: {generatedExtraction.Message}");
            return;
        }

        var referenceExtraction = await ExtractVideoFramesAsync(
            ffmpeg,
            referencePath,
            referenceFrameRoot,
            result.MaxFrames,
            result.TimeoutSeconds,
            cancellationToken);
        if (!referenceExtraction.Succeeded)
        {
            result.Issues.Add($"Reference mask video frames could not be extracted: {referenceExtraction.Message}");
            return;
        }

        var generatedFrames = Directory.GetFiles(generatedFrameRoot, "*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var referenceFrames = Directory.GetFiles(referenceFrameRoot, "*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.GeneratedFrameCount = generatedFrames.Length;
        result.ReferenceFrameCount = referenceFrames.Length;

        if (generatedFrames.Length == 0)
        {
            result.Issues.Add("Generated mask video did not produce any frames.");
        }

        if (referenceFrames.Length == 0)
        {
            result.Issues.Add("Reference mask video did not produce any frames.");
        }

        if (generatedFrames.Length != referenceFrames.Length)
        {
            result.Issues.Add($"Generated and reference mask videos must have the same frame count. Generated={generatedFrames.Length}; reference={referenceFrames.Length}.");
        }

        var pairCount = Math.Min(generatedFrames.Length, referenceFrames.Length);
        for (var index = 0; index < pairCount; index++)
        {
            EvaluatePixels(result, generatedFrames[index], referenceFrames[index], threshold, frameIndex: index + 1);
        }

        CompleteAggregateMetrics(result);
    }

    private static void EvaluatePixels(
        VideoMaskQualityEvaluationResult result,
        string generatedPath,
        string referencePath,
        int threshold,
        int frameIndex)
    {
        using var generated = new Bitmap(generatedPath);
        using var reference = new Bitmap(referencePath);
        var frame = new VideoMaskFrameQualityMetrics
        {
            FrameIndex = frameIndex,
            GeneratedFramePath = NormalizeDisplayPath(generatedPath),
            ReferenceFramePath = NormalizeDisplayPath(referencePath),
            Width = generated.Width,
            Height = generated.Height,
            ReferenceWidth = reference.Width,
            ReferenceHeight = reference.Height
        };

        if (result.Width == 0 && result.Height == 0)
        {
            result.Width = generated.Width;
            result.Height = generated.Height;
            result.ReferenceWidth = reference.Width;
            result.ReferenceHeight = reference.Height;
        }

        if (generated.Width != reference.Width || generated.Height != reference.Height)
        {
            result.Issues.Add($"Mask dimensions must match. Frame={frameIndex}; generated={generated.Width}x{generated.Height}; reference={reference.Width}x{reference.Height}.");
            result.Frames.Add(frame);
            return;
        }

        for (var y = 0; y < generated.Height; y++)
        {
            for (var x = 0; x < generated.Width; x++)
            {
                var predictedForeground = IsForeground(generated.GetPixel(x, y), threshold);
                var referenceForeground = IsForeground(reference.GetPixel(x, y), threshold);

                if (predictedForeground && referenceForeground)
                {
                    frame.TruePositivePixels++;
                }
                else if (predictedForeground)
                {
                    frame.FalsePositivePixels++;
                }
                else if (referenceForeground)
                {
                    frame.FalseNegativePixels++;
                }
                else
                {
                    frame.TrueNegativePixels++;
                }
            }
        }

        frame.TotalPixels = frame.TruePositivePixels +
            frame.TrueNegativePixels +
            frame.FalsePositivePixels +
            frame.FalseNegativePixels;
        frame.GeneratedForegroundPixels = frame.TruePositivePixels + frame.FalsePositivePixels;
        frame.ReferenceForegroundPixels = frame.TruePositivePixels + frame.FalseNegativePixels;
        frame.IntersectionOverUnion = DivideOrDefault(
            frame.TruePositivePixels,
            frame.TruePositivePixels + frame.FalsePositivePixels + frame.FalseNegativePixels,
            bothEmptyValue: 1d);
        frame.DiceCoefficient = DivideOrDefault(
            2d * frame.TruePositivePixels,
            (2d * frame.TruePositivePixels) + frame.FalsePositivePixels + frame.FalseNegativePixels,
            bothEmptyValue: 1d);
        frame.Precision = DivideOrDefault(
            frame.TruePositivePixels,
            frame.TruePositivePixels + frame.FalsePositivePixels,
            bothEmptyValue: frame.ReferenceForegroundPixels == 0 ? 1d : 0d);
        frame.Recall = DivideOrDefault(
            frame.TruePositivePixels,
            frame.TruePositivePixels + frame.FalseNegativePixels,
            bothEmptyValue: frame.GeneratedForegroundPixels == 0 ? 1d : 0d);
        frame.Accuracy = DivideOrDefault(
            frame.TruePositivePixels + frame.TrueNegativePixels,
            frame.TotalPixels,
            bothEmptyValue: 0d);

        result.Frames.Add(frame);
        result.EvaluatedFrameCount++;
        result.TruePositivePixels += frame.TruePositivePixels;
        result.FalsePositivePixels += frame.FalsePositivePixels;
        result.FalseNegativePixels += frame.FalseNegativePixels;
        result.TrueNegativePixels += frame.TrueNegativePixels;
    }

    private static void CompleteAggregateMetrics(VideoMaskQualityEvaluationResult result)
    {
        result.TotalPixels = result.TruePositivePixels +
            result.TrueNegativePixels +
            result.FalsePositivePixels +
            result.FalseNegativePixels;
        result.GeneratedForegroundPixels = result.TruePositivePixels + result.FalsePositivePixels;
        result.ReferenceForegroundPixels = result.TruePositivePixels + result.FalseNegativePixels;
        result.IntersectionOverUnion = DivideOrDefault(
            result.TruePositivePixels,
            result.TruePositivePixels + result.FalsePositivePixels + result.FalseNegativePixels,
            bothEmptyValue: 1d);
        result.DiceCoefficient = DivideOrDefault(
            2d * result.TruePositivePixels,
            (2d * result.TruePositivePixels) + result.FalsePositivePixels + result.FalseNegativePixels,
            bothEmptyValue: 1d);
        result.Precision = DivideOrDefault(
            result.TruePositivePixels,
            result.TruePositivePixels + result.FalsePositivePixels,
            bothEmptyValue: result.ReferenceForegroundPixels == 0 ? 1d : 0d);
        result.Recall = DivideOrDefault(
            result.TruePositivePixels,
            result.TruePositivePixels + result.FalseNegativePixels,
            bothEmptyValue: result.GeneratedForegroundPixels == 0 ? 1d : 0d);
        result.Accuracy = DivideOrDefault(
            result.TruePositivePixels + result.TrueNegativePixels,
            result.TotalPixels,
            bothEmptyValue: 0d);
    }

    private static string BuildMarkdown(VideoMaskQualityEvaluationResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GoatShot Mask Quality Evaluation");
        builder.AppendLine();
        builder.AppendLine($"Generated mask: `{EmptyIfMissing(result.GeneratedMaskPath)}`");
        builder.AppendLine($"Reference mask: `{EmptyIfMissing(result.ReferenceMaskPath)}`");
        builder.AppendLine($"Evaluation mode: `{EmptyIfMissing(result.EvaluationMode)}`");
        if (!string.IsNullOrWhiteSpace(result.SourceVideoPath))
        {
            builder.AppendLine($"Source video: `{result.SourceVideoPath}`");
        }

        if (!string.IsNullOrWhiteSpace(result.FfmpegPath))
        {
            builder.AppendLine($"FFmpeg: `{result.FfmpegPath}`");
        }

        if (!string.IsNullOrWhiteSpace(result.ExtractedFramesRoot))
        {
            builder.AppendLine($"Extracted frames root: `{result.ExtractedFramesRoot}`");
        }

        builder.AppendLine($"Quality passed: `{result.QualityPassed}`");
        builder.AppendLine($"Evaluated at: `{result.EvaluatedAt:O}`");
        if (!string.IsNullOrWhiteSpace(result.OperatorName))
        {
            builder.AppendLine($"Operator: `{result.OperatorName}`");
        }

        if (!string.IsNullOrWhiteSpace(result.Note))
        {
            builder.AppendLine($"Note: {result.Note}");
        }

        builder.AppendLine();
        builder.AppendLine("## Thresholds");
        builder.AppendLine($"- Foreground threshold: `{result.ForegroundThreshold}`");
        builder.AppendLine($"- Minimum IoU: `{FormatMetric(result.MinimumIntersectionOverUnion)}`");
        builder.AppendLine($"- Minimum precision: `{FormatMetric(result.MinimumPrecision)}`");
        builder.AppendLine($"- Minimum recall: `{FormatMetric(result.MinimumRecall)}`");
        builder.AppendLine($"- Max frames: `{(result.MaxFrames.HasValue ? result.MaxFrames.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "all")}`");
        builder.AppendLine($"- Timeout seconds: `{result.TimeoutSeconds}`");

        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine($"- Dimensions: `{result.Width}x{result.Height}`");
        builder.AppendLine($"- Reference dimensions: `{result.ReferenceWidth}x{result.ReferenceHeight}`");
        builder.AppendLine($"- Generated video frames: `{result.GeneratedFrameCount}`");
        builder.AppendLine($"- Reference video frames: `{result.ReferenceFrameCount}`");
        builder.AppendLine($"- Evaluated frames: `{result.EvaluatedFrameCount}`");
        builder.AppendLine($"- Total pixels: `{result.TotalPixels}`");
        builder.AppendLine($"- Generated foreground pixels: `{result.GeneratedForegroundPixels}`");
        builder.AppendLine($"- Reference foreground pixels: `{result.ReferenceForegroundPixels}`");
        builder.AppendLine($"- True positives: `{result.TruePositivePixels}`");
        builder.AppendLine($"- False positives: `{result.FalsePositivePixels}`");
        builder.AppendLine($"- False negatives: `{result.FalseNegativePixels}`");
        builder.AppendLine($"- True negatives: `{result.TrueNegativePixels}`");
        builder.AppendLine($"- IoU: `{FormatMetric(result.IntersectionOverUnion)}`");
        builder.AppendLine($"- Dice: `{FormatMetric(result.DiceCoefficient)}`");
        builder.AppendLine($"- Precision: `{FormatMetric(result.Precision)}`");
        builder.AppendLine($"- Recall: `{FormatMetric(result.Recall)}`");
        builder.AppendLine($"- Accuracy: `{FormatMetric(result.Accuracy)}`");

        if (result.Frames.Count > 1)
        {
            builder.AppendLine();
            builder.AppendLine("## Frame Metrics");
            foreach (var frame in result.Frames)
            {
                builder.AppendLine($"- Frame `{frame.FrameIndex}`: IoU `{FormatMetric(frame.IntersectionOverUnion)}`, precision `{FormatMetric(frame.Precision)}`, recall `{FormatMetric(frame.Recall)}`, accuracy `{FormatMetric(frame.Accuracy)}`");
            }
        }

        if (result.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Issues");
            foreach (var issue in result.Issues)
            {
                builder.AppendLine($"- {issue}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Mutation Boundary");
        builder.AppendLine($"- Would download model: `{result.WouldDownloadModel}`");
        builder.AppendLine($"- Would run segmentation model: `{result.WouldRunSegmentationModel}`");
        builder.AppendLine($"- Would contact hosted service: `{result.WouldContactHostedService}`");
        builder.AppendLine($"- Would mutate source media: `{result.WouldMutateSourceMedia}`");
        builder.AppendLine($"- Would certify whole model: `{result.WouldCertifyWholeModel}`");

        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine("- This evaluation compares two still mask images, extracted frames, or matching mask-video frame sequences.");
        builder.AppendLine("- It does not generate masks, download models, run model inference, contact hosted AI services, or modify source media.");
        builder.AppendLine("- Passing thresholds support only the reviewed evidence supplied to this command, not broad model quality certification.");
        return builder.ToString();
    }

    private static async Task<FrameExtractionResult> ExtractVideoFramesAsync(
        string ffmpeg,
        string videoPath,
        string frameRoot,
        int? maxFrames,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var pattern = Path.Combine(frameRoot, "frame-%06d.png");
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        if (maxFrames is > 0)
        {
            process.StartInfo.ArgumentList.Add("-frames:v");
            process.StartInfo.ArgumentList.Add(maxFrames.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        process.StartInfo.ArgumentList.Add("-vsync");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add(pattern);

        try
        {
            if (!process.Start())
            {
                return new FrameExtractionResult(false, $"could not start {SensitiveTextDetector.Redact(ffmpeg)}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new FrameExtractionResult(false, $"could not start {SensitiveTextDetector.Redact(ffmpeg)}: {ex.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new FrameExtractionResult(false, $"ffmpeg frame extraction timed out after {timeoutSeconds} seconds");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            return new FrameExtractionResult(false, $"ffmpeg exited {process.ExitCode}. {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
        }

        return new FrameExtractionResult(true, "ok");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static bool IsForeground(Color color, int threshold)
    {
        var luminance = (0.2126d * color.R) + (0.7152d * color.G) + (0.0722d * color.B);
        return luminance >= threshold;
    }

    private static string? NormalizeInputPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string NormalizeDisplayPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : SensitiveTextDetector.Redact(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
    }

    private static bool LooksLikeVideoPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    private static double DivideOrDefault(double numerator, double denominator, double bothEmptyValue)
    {
        return denominator <= 0d ? bothEmptyValue : numerator / denominator;
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }

        return Math.Clamp(value, 0d, 1d);
    }

    private static string FormatMetric(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static string EmptyIfMissing(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

    private static async Task<string> WriteFileAsync(
        string root,
        string fileName,
        string contents,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, fileName);
        await File.WriteAllTextAsync(path, contents, cancellationToken);
        return path;
    }

    private sealed record FrameExtractionResult(bool Succeeded, string Message);
}

public sealed class VideoMaskQualityEvaluationRequest
{
    public string? GeneratedMaskPath { get; set; }
    public string? ReferenceMaskPath { get; set; }
    public string? SourceVideoPath { get; set; }
    public string? OutputPath { get; set; }
    public string? OperatorName { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? EvaluatedAt { get; set; }
    public int? ForegroundThreshold { get; set; }
    public double? MinimumIntersectionOverUnion { get; set; }
    public double? MinimumPrecision { get; set; }
    public double? MinimumRecall { get; set; }
    public int? MaxFrames { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? FfmpegPath { get; set; }
}

public sealed class VideoMaskQualityEvaluationResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool QualityPassed { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string GeneratedMaskPath { get; set; } = string.Empty;
    public string ReferenceMaskPath { get; set; } = string.Empty;
    public string SourceVideoPath { get; set; } = string.Empty;
    public string EvaluationMode { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public string ExtractedFramesRoot { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; set; }
    public int ForegroundThreshold { get; set; }
    public double MinimumIntersectionOverUnion { get; set; }
    public double MinimumPrecision { get; set; }
    public double MinimumRecall { get; set; }
    public int? MaxFrames { get; set; }
    public int TimeoutSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ReferenceWidth { get; set; }
    public int ReferenceHeight { get; set; }
    public int GeneratedFrameCount { get; set; }
    public int ReferenceFrameCount { get; set; }
    public int EvaluatedFrameCount { get; set; }
    public long TotalPixels { get; set; }
    public long GeneratedForegroundPixels { get; set; }
    public long ReferenceForegroundPixels { get; set; }
    public long TruePositivePixels { get; set; }
    public long FalsePositivePixels { get; set; }
    public long FalseNegativePixels { get; set; }
    public long TrueNegativePixels { get; set; }
    public double IntersectionOverUnion { get; set; }
    public double DiceCoefficient { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double Accuracy { get; set; }
    public bool WouldDownloadModel { get; set; }
    public bool WouldRunSegmentationModel { get; set; }
    public bool WouldContactHostedService { get; set; }
    public bool WouldMutateSourceMedia { get; set; }
    public bool WouldCertifyWholeModel { get; set; }
    public List<VideoMaskFrameQualityMetrics> Frames { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
}

public sealed class VideoMaskFrameQualityMetrics
{
    public int FrameIndex { get; set; }
    public string GeneratedFramePath { get; set; } = string.Empty;
    public string ReferenceFramePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int ReferenceWidth { get; set; }
    public int ReferenceHeight { get; set; }
    public long TotalPixels { get; set; }
    public long GeneratedForegroundPixels { get; set; }
    public long ReferenceForegroundPixels { get; set; }
    public long TruePositivePixels { get; set; }
    public long FalsePositivePixels { get; set; }
    public long FalseNegativePixels { get; set; }
    public long TrueNegativePixels { get; set; }
    public double IntersectionOverUnion { get; set; }
    public double DiceCoefficient { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double Accuracy { get; set; }
}

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class VideoToolService
{
    private const int DefaultPersonSegmentationTimeoutSeconds = 600;
    private const int MaxPersonSegmentationTimeoutSeconds = 7200;

    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;
    private readonly HttpClient _httpClient;
    private readonly PersonSegmentationInferenceService? _personSegmentation;

    public VideoToolService(
        AppPaths paths,
        WorkspaceStore workspaceStore,
        HttpClient? httpClient = null,
        PersonSegmentationInferenceService? personSegmentation = null)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
        _httpClient = httpClient ?? new HttpClient();
        _personSegmentation = personSegmentation;
    }

    public async Task<VideoToolResult> ExportFrameAsync(
        CaptureItem item,
        TimeSpan at,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video frame export.");
        }

        at = at < TimeSpan.Zero ? TimeSpan.Zero : at;
        outputPath = ResolveOutputPath(
            outputPath,
            _paths.ImagesRoot,
            $"video-frame-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = await RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-ss",
                FormatTime(at),
                "-i",
                item.FilePath,
                "-frames:v",
                "1",
                "-q:v",
                "2",
                outputPath
            ],
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a frame output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.VideoFrame,
                $"Frame exported from {item.FileName} at {FormatTime(at)}.");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Video frame exported: {outputPath}"
                : $"Video frame exported and indexed: {saved.FileName}"
        };
    }

    public async Task<VideoToolResult> TrimAsync(
        CaptureItem item,
        TimeSpan start,
        TimeSpan duration,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        if (duration <= TimeSpan.Zero)
        {
            return Failed("Trim duration must be greater than zero.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video trimming.");
        }

        start = start < TimeSpan.Zero ? TimeSpan.Zero : start;
        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"trimmed-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = await RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-ss",
                FormatTime(start),
                "-i",
                item.FilePath,
                "-t",
                FormatTime(duration),
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-movflags",
                "+faststart",
                outputPath
            ],
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a trimmed video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.TrimmedVideo,
                $"Trimmed from {item.FileName}; start {FormatTime(start)}, duration {FormatTime(duration)}.");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Trimmed video exported: {outputPath}"
                : $"Trimmed video exported and indexed: {saved.FileName}"
        };
    }

    public async Task<VideoToolResult> MuteAsync(
        CaptureItem item,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video mute export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"muted-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = await RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-i",
                item.FilePath,
                "-map",
                "0:v:0",
                "-an",
                "-c:v",
                "copy",
                "-movflags",
                "+faststart",
                outputPath
            ],
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a muted video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.MutedVideo,
                $"Muted copy exported from {item.FileName}.");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Muted video exported: {outputPath}"
                : $"Muted video exported and indexed: {saved.FileName}"
        };
    }

    public async Task<VideoToolResult> ChangeSpeedAsync(
        CaptureItem item,
        double factor,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        if (factor <= 0)
        {
            return Failed("Speed factor must be greater than zero.");
        }

        factor = Math.Clamp(factor, 0.1d, 8d);
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video speed export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"speed-{FormatFactor(factor)}x-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var setPts = $"{(1d / factor).ToString("0.######", CultureInfo.InvariantCulture)}*PTS";
        var hasAudio = await HasAudioStreamAsync(ffmpeg, item.FilePath, cancellationToken);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildChangeSpeedArguments(item.FilePath, outputPath, setPts, factor, hasAudio),
            cancellationToken);
        var audioNote = hasAudio
            ? "Audio was tempo-adjusted and preserved."
            : "No audio stream was detected, so the export is video-only.";
        if (!result.Succeeded && hasAudio)
        {
            var audioFailure = result.Message;
            result = await RunFfmpegAsync(
                ffmpeg,
                BuildChangeSpeedArguments(item.FilePath, outputPath, setPts, factor, includeAudio: false),
                cancellationToken);
            audioNote = $"Audio tempo-adjust failed and was omitted on retry ({audioFailure}).";
        }

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a speed-adjusted video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.SpeedAdjustedVideo,
                $"Speed-adjusted copy exported from {item.FileName}; factor {FormatFactor(factor)}x. {audioNote}");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Speed-adjusted video exported: {outputPath}"
                : $"Speed-adjusted video exported and indexed: {saved.FileName}"
        };
    }

    internal static IReadOnlyList<string> BuildChangeSpeedArguments(
        string inputPath,
        string outputPath,
        string setPts,
        double factor,
        bool includeAudio)
    {
        if (!includeAudio)
        {
            return
            [
                "-y",
                "-i",
                inputPath,
                "-map",
                "0:v:0",
                "-filter:v",
                $"setpts={setPts}",
                "-an",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-movflags",
                "+faststart",
                outputPath
            ];
        }

        return
        [
            "-y",
            "-i",
            inputPath,
            "-filter_complex",
            $"[0:v:0]setpts={setPts}[vout];[0:a:0]{BuildAtempoFilter(factor)}[aout]",
            "-map",
            "[vout]",
            "-map",
            "[aout]",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-b:a",
            "160k",
            "-shortest",
            "-movflags",
            "+faststart",
            outputPath
        ];
    }

    internal static string BuildAtempoFilter(double factor)
    {
        factor = Math.Clamp(factor, 0.1d, 8d);
        var remaining = factor;
        var filters = new List<double>();
        while (remaining > 2d)
        {
            filters.Add(2d);
            remaining /= 2d;
        }

        while (remaining < 0.5d)
        {
            filters.Add(0.5d);
            remaining /= 0.5d;
        }

        filters.Add(remaining);
        return string.Join(",", filters.Select(value => $"atempo={FormatFactor(value)}"));
    }

    public async Task<VideoToolResult> CropAsync(
        CaptureItem item,
        int x,
        int y,
        int width,
        int height,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0)
        {
            return Failed("Video crop width and height must be greater than zero.");
        }

        if (x < 0 || y < 0)
        {
            return Failed("Video crop x and y must be zero or greater.");
        }

        var filter = FormattableString.Invariant($"crop={width}:{height}:{x}:{y}");
        return await CropWithFilterAsync(
            item,
            filter,
            outputPath,
            $"cropped-{width}x{height}-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4",
            $"Cropped copy exported from {item.FileName}; x {x}, y {y}, size {width}x{height}.",
            addToWorkspace,
            cancellationToken);
    }

    public async Task<VideoToolResult> CropCenterAsync(
        CaptureItem item,
        double scale = 0.75d,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (scale <= 0d)
        {
            return Failed("Center crop scale must be greater than zero.");
        }

        scale = Math.Clamp(scale, 0.1d, 1d);
        var scaleText = scale.ToString("0.######", CultureInfo.InvariantCulture);
        var filter = $"crop=trunc(iw*{scaleText}/2)*2:trunc(ih*{scaleText}/2)*2:(iw-ow)/2:(ih-oh)/2";
        return await CropWithFilterAsync(
            item,
            filter,
            outputPath,
            $"cropped-center-{FormatFactor(scale)}-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4",
            $"Center-cropped copy exported from {item.FileName}; scale {FormatFactor(scale)}.",
            addToWorkspace,
            cancellationToken);
    }

    public async Task<VideoToolResult> ConvertAsync(
        CaptureItem item,
        string? format,
        string? outputPath = null,
        AnimationExportOptions? animationOptions = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var normalizedFormat = NormalizeFormat(format, outputPath);
        if (normalizedFormat is null)
        {
            return Failed("Video convert format must be mp4, gif, or webm.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video conversion.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"converted-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{normalizedFormat}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        animationOptions ??= new AnimationExportOptions();
        var result = await RunFfmpegAsync(
            ffmpeg,
            ConvertArguments(item.FilePath, normalizedFormat, outputPath, animationOptions),
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a converted video output.")
                : result;
        }

        var companionMessage = string.Empty;
        if (normalizedFormat.Equals("gif", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(GifTimingPlanner.NormalizeCompanionFormat(animationOptions.CompanionFormat)))
        {
            companionMessage = await TryConvertAnimationCompanionAsync(
                ffmpeg,
                item.FilePath,
                outputPath,
                animationOptions,
                addToWorkspace,
                cancellationToken);
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.ConvertedVideo,
                $"Converted copy exported from {item.FileName}; format {normalizedFormat}. {GifTimingPlanner.Plan(null, animationOptions).Message}");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Converted video exported: {outputPath}{FormatCompanionMessage(companionMessage)}"
                : $"Converted video exported and indexed: {saved.FileName}{FormatCompanionMessage(companionMessage)}"
        };
    }

    public async Task<VideoToolResult> ChangeVolumeAsync(
        CaptureItem item,
        double gain,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (gain < 0d)
        {
            return Failed("Volume gain must be zero or greater.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video volume export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"volume-{FormatFactor(gain)}x-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = gain == 0d
            ? await RunFfmpegAsync(
                ffmpeg,
                [
                    "-y",
                    "-i",
                    item.FilePath,
                    "-map",
                    "0:v:0",
                    "-an",
                    "-c:v",
                    "copy",
                    "-movflags",
                    "+faststart",
                    outputPath
                ],
                cancellationToken)
            : await RunFfmpegAsync(
                ffmpeg,
                [
                    "-y",
                    "-i",
                    item.FilePath,
                    "-map",
                    "0:v:0",
                    "-map",
                    "0:a:0",
                    "-c:v",
                    "copy",
                    "-filter:a",
                    $"volume={FormatFactor(gain)}",
                    "-c:a",
                    "aac",
                    "-movflags",
                    "+faststart",
                    outputPath
                ],
                cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a volume-adjusted video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.VolumeAdjustedVideo,
                $"Volume-adjusted copy exported from {item.FileName}; gain {FormatFactor(gain)}x.");
        }

        return Succeeded(outputPath, saved, "Volume-adjusted video exported");
    }

    public async Task<VideoToolResult> DenoiseAudioAsync(
        CaptureItem item,
        double noiseReductionDb = 12d,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(noiseReductionDb) || noiseReductionDb <= 0d || noiseReductionDb > 40d)
        {
            return Failed("Audio noise reduction must be greater than 0 dB and no more than 40 dB.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable audio denoise export.");
        }

        var hasAudio = await HasAudioStreamAsync(ffmpeg, item.FilePath, cancellationToken);
        if (!hasAudio)
        {
            return Failed("No audio stream was detected; audio denoise export requires a video with audio.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"denoised-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var normalizedReduction = Math.Clamp(noiseReductionDb, 1d, 40d);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildDenoiseAudioArguments(item.FilePath, outputPath, normalizedReduction),
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a denoised video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.DenoisedVideo,
                $"Audio-denoised copy exported from {item.FileName}; FFmpeg afftdn noise reduction {FormatFactor(normalizedReduction)} dB.");
        }

        return Succeeded(outputPath, saved, "Audio-denoised video exported");
    }

    internal static IReadOnlyList<string> BuildDenoiseAudioArguments(
        string inputPath,
        string outputPath,
        double noiseReductionDb)
    {
        return
        [
            "-y",
            "-i",
            inputPath,
            "-map",
            "0:v:0",
            "-map",
            "0:a:0",
            "-c:v",
            "copy",
            "-filter:a",
            $"afftdn=nr={FormatFactor(noiseReductionDb)}",
            "-c:a",
            "aac",
            "-movflags",
            "+faststart",
            outputPath
        ];
    }

    public async Task<VideoToolResult> ResizeAsync(
        CaptureItem item,
        int? width,
        int? height,
        string? qualityProfile,
        int? crf,
        string? bitrate,
        int? fps,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (width is <= 0 || height is <= 0)
        {
            return Failed("Resize width and height must be greater than zero when provided.");
        }

        if (fps is <= 0)
        {
            return Failed("Video export FPS must be greater than zero when provided.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video resize/export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"resized-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var arguments = new List<string>
        {
            "-y",
            "-i",
            item.FilePath,
            "-map",
            "0:v:0",
            "-map",
            "0:a?",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p"
        };

        var videoFilter = BuildScaleFilter(width, height);
        if (!string.IsNullOrWhiteSpace(videoFilter))
        {
            arguments.Add("-filter:v");
            arguments.Add(videoFilter);
        }

        if (fps is not null)
        {
            arguments.Add("-r");
            arguments.Add(fps.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(bitrate))
        {
            arguments.Add("-b:v");
            arguments.Add(bitrate.Trim());
        }
        else
        {
            arguments.Add("-crf");
            arguments.Add((crf ?? QualityProfileCrf(qualityProfile)).ToString(CultureInfo.InvariantCulture));
        }

        arguments.AddRange([
            "-c:a",
            "aac",
            "-movflags",
            "+faststart",
            outputPath
        ]);

        var result = await RunFfmpegAsync(ffmpeg, arguments, cancellationToken);
        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a resized/exported video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.ResizedVideo,
                $"Resized/exported copy from {item.FileName}; quality {NormalizeQualityProfile(qualityProfile)}.");
        }

        return Succeeded(outputPath, saved, "Resized/exported video created");
    }

    public async Task<VideoToolResult> CutMiddleAsync(
        CaptureItem item,
        TimeSpan start,
        TimeSpan duration,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (start < TimeSpan.Zero)
        {
            return Failed("Cut start must be zero or greater.");
        }

        if (duration <= TimeSpan.Zero)
        {
            return Failed("Cut duration must be greater than zero.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable middle-cut export.");
        }

        var end = start + duration;
        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"cut-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var hasAudio = await HasAudioStreamAsync(ffmpeg, item.FilePath, cancellationToken);
        var filter = BuildMiddleCutFilter(start, end, hasAudio);
        var arguments = BuildMiddleCutArguments(item.FilePath, outputPath, filter, hasAudio);
        var result = await RunFfmpegAsync(
            ffmpeg,
            arguments,
            cancellationToken);
        var audioNote = hasAudio
            ? "Audio was cut and preserved."
            : "No audio stream was detected, so the export is video-only.";
        if (!result.Succeeded && hasAudio)
        {
            var audioFailure = result.Message;
            filter = BuildMiddleCutFilter(start, end, includeAudio: false);
            result = await RunFfmpegAsync(
                ffmpeg,
                BuildMiddleCutArguments(item.FilePath, outputPath, filter, includeAudio: false),
                cancellationToken);
            audioNote = $"Audio cut failed and was omitted on retry ({audioFailure}).";
        }

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a middle-cut video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.CutVideo,
                $"Middle-cut copy exported from {item.FileName}; removed {FormatTime(start)} through {FormatTime(end)}. {audioNote}");
        }

        return Succeeded(outputPath, saved, "Middle-cut video exported");
    }

    public async Task<VideoToolResult> ApplyAdvancedCutPlanAsync(
        CaptureItem item,
        AdvancedVideoEditPlan plan,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            return Failed("Advanced video plan is required.");
        }

        if (!plan.Succeeded)
        {
            return Failed("Advanced video plan did not succeed and cannot be exported.");
        }

        if (plan.Cuts.Count == 0)
        {
            return Failed("Advanced video plan has no reviewed cut ranges to export.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable reviewed cut-plan export.");
        }

        var durationSeconds = await GetVideoDurationSecondsAsync(ffmpeg, item.FilePath, cancellationToken);
        var ranges = NormalizeCutRanges(plan.Cuts, durationSeconds);
        if (ranges.Count == 0)
        {
            return Failed("Advanced video plan has no valid positive-duration cut ranges to export.");
        }

        var keepSegments = BuildKeepSegments(ranges, durationSeconds);
        if (keepSegments.Count == 0)
        {
            return Failed("Advanced video plan removes the entire known video duration.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"advanced-cut-{SafeFilePart(plan.PlanType)}-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var hasAudio = await HasAudioStreamAsync(ffmpeg, item.FilePath, cancellationToken);
        var filter = BuildAdvancedCutFilter(keepSegments, hasAudio);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildAdvancedCutArguments(item.FilePath, outputPath, filter, hasAudio),
            cancellationToken);
        var audioNote = hasAudio
            ? "Audio was cut and preserved."
            : "No audio stream was detected, so the export is video-only.";
        if (!result.Succeeded && hasAudio)
        {
            var audioFailure = result.Message;
            filter = BuildAdvancedCutFilter(keepSegments, includeAudio: false);
            result = await RunFfmpegAsync(
                ffmpeg,
                BuildAdvancedCutArguments(item.FilePath, outputPath, filter, includeAudio: false),
                cancellationToken);
            audioNote = $"Audio cut failed and was omitted on retry ({audioFailure}).";
        }

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create an advanced video cut-plan output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.CutVideo,
                $"Advanced {plan.PlanType} plan exported from {item.FileName}; removed {ranges.Count} reviewed cut range(s). {audioNote}");
        }

        return Succeeded(outputPath, saved, "Advanced video cut-plan exported");
    }

    public async Task<VideoToolResult> ApplyCompositeLayoutPlanAsync(
        AdvancedVideoEditPlan plan,
        string? screenPath = null,
        string? cameraPath = null,
        string? outputPath = null,
        bool addToWorkspace = false,
        string? audioSource = null,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            return Failed("Composite layout plan is required.");
        }

        if (!plan.Succeeded)
        {
            return Failed("Composite layout plan did not succeed and cannot be exported.");
        }

        if (!plan.PlanType.Equals("composite-layout", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Advanced video plan is not a composite layout plan.");
        }

        var recipe = plan.CompositeRecipes.FirstOrDefault();
        if (recipe is null)
        {
            return Failed("Composite layout plan has no reviewed layout recipe to export.");
        }

        if (string.IsNullOrWhiteSpace(recipe.FfmpegFilterComplex))
        {
            return Failed("Composite layout recipe has no FFmpeg filter to export.");
        }

        var resolvedScreenPath = ResolveCompositeInputPath(screenPath, recipe.ScreenPath);
        var resolvedCameraPath = ResolveCompositeInputPath(cameraPath, recipe.CameraPath);
        if (string.IsNullOrWhiteSpace(resolvedScreenPath))
        {
            return Failed("Composite layout export requires --screen or a screenPath in the plan.");
        }

        if (string.IsNullOrWhiteSpace(resolvedCameraPath))
        {
            return Failed("Composite layout export requires --camera or a cameraPath in the plan.");
        }

        if (!File.Exists(resolvedScreenPath))
        {
            return Failed($"Screen video file not found: {resolvedScreenPath}");
        }

        if (!File.Exists(resolvedCameraPath))
        {
            return Failed($"Camera video file not found: {resolvedCameraPath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable composite layout export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"composite-{SafeFilePart(recipe.Preset)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var normalizedAudio = NormalizeCompositeAudioSource(audioSource);
        var audioInputPath = normalizedAudio switch
        {
            "camera" => resolvedCameraPath,
            "screen" => resolvedScreenPath,
            _ => null
        };
        var includeAudio = !string.IsNullOrWhiteSpace(audioInputPath) &&
            await HasAudioStreamAsync(ffmpeg, audioInputPath, cancellationToken);

        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildCompositeLayoutArguments(
                resolvedScreenPath,
                resolvedCameraPath,
                outputPath,
                recipe.FfmpegFilterComplex,
                includeAudio ? normalizedAudio : "none"),
            cancellationToken);
        var audioNote = includeAudio
            ? $"Audio was copied from the {normalizedAudio} input."
            : normalizedAudio == "none"
                ? "Audio was intentionally omitted."
                : $"No audio stream was detected on the {normalizedAudio} input, so the export is video-only.";

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a composite layout video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.CompositeVideo,
                $"Composite {recipe.Preset} layout exported from screen {Path.GetFileName(resolvedScreenPath)} and camera {Path.GetFileName(resolvedCameraPath)}. {audioNote}");
        }

        return Succeeded(outputPath, saved, "Composite layout video exported");
    }

    public async Task<VideoToolResult> ApplyWebcamBackgroundPlanAsync(
        CaptureItem item,
        AdvancedVideoEditPlan plan,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            return Failed("Webcam background plan is required.");
        }

        if (!plan.Succeeded)
        {
            return Failed("Webcam background plan did not succeed and cannot be exported.");
        }

        if (!plan.PlanType.Equals("webcam-background", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Advanced video plan is not a webcam background plan.");
        }

        var recipe = plan.BackgroundRecipe;
        if (recipe is null)
        {
            return Failed("Webcam background plan has no reviewed background recipe to export.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var usesExternalMask = IsExternalMaskBackgroundRecipe(recipe);
        var maskPath = ResolveBackgroundMaskPath(recipe);
        if (usesExternalMask && string.IsNullOrWhiteSpace(maskPath))
        {
            return Failed("External mask background plan requires a maskPath.");
        }

        if (usesExternalMask && !File.Exists(maskPath))
        {
            return Failed($"External mask file not found: {maskPath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable webcam background export.");
        }

        var dimensions = await GetVideoDimensionsAsync(ffmpeg, item.FilePath, cancellationToken);
        if (dimensions is null && (usesExternalMask || recipe.Mode is "remove" or "replace"))
        {
            return Failed(usesExternalMask
                ? "FFprobe could not determine source video dimensions required for external mask compositing."
                : "FFprobe could not determine video dimensions required for solid-background replacement/removal.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"webcam-background-{SafeFilePart(recipe.ProcessingKind)}-{SafeFilePart(recipe.Mode)}-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var hasAudio = await HasAudioStreamAsync(ffmpeg, item.FilePath, cancellationToken);
        var filter = BuildWebcamBackgroundFilter(recipe, dimensions?.Width, dimensions?.Height);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildWebcamBackgroundArguments(item.FilePath, maskPath, outputPath, filter, hasAudio),
            cancellationToken);
        var audioNote = hasAudio
            ? "Audio was preserved."
            : "No audio stream was detected, so the export is video-only.";
        if (!result.Succeeded && hasAudio)
        {
            var audioFailure = result.Message;
            result = await RunFfmpegAsync(
                ffmpeg,
                BuildWebcamBackgroundArguments(item.FilePath, maskPath, outputPath, filter, includeAudio: false),
                cancellationToken);
            audioNote = $"Audio mapping failed and was omitted on retry ({audioFailure}).";
        }

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a webcam background output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.BackgroundProcessedVideo,
                usesExternalMask
                    ? $"Webcam background {recipe.Mode} export from {item.FileName}. {audioNote} External mask: {Path.GetFileName(maskPath)}."
                    : $"Webcam background {recipe.Mode} export from {item.FileName}. {audioNote} Key color: {recipe.KeyColor}.");
        }

        return Succeeded(outputPath, saved, "Webcam background video exported");
    }

    public async Task<VideoToolResult> GenerateForegroundMaskAsync(
        CaptureItem item,
        WebcamMaskGenerationOptions options,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable foreground mask generation.");
        }

        options ??= new WebcamMaskGenerationOptions();
        var method = NormalizeForegroundMaskMethod(options.Method);
        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"foreground-mask-{SafeFilePart(method)}-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var filter = BuildForegroundMaskFilter(options);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildForegroundMaskArguments(item.FilePath, outputPath, filter),
            cancellationToken);
        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a foreground mask output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.ForegroundMaskVideo,
                $"Foreground mask generated from {item.FileName} using deterministic {method} processing. This is not model-grade AI/person segmentation.");
        }

        return Succeeded(outputPath, saved, "Foreground mask video exported");
    }

    public async Task<VideoToolResult> GeneratePersonSegmentationMaskAsync(
        CaptureItem item,
        PersonSegmentationMaskGenerationOptions options,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        options ??= new PersonSegmentationMaskGenerationOptions();
        if (!options.AcceptExternalRunner && string.IsNullOrWhiteSpace(options.RunnerPath))
        {
            return await GenerateBundledPersonSegmentationMaskAsync(
                item,
                options,
                outputPath,
                addToWorkspace,
                cancellationToken);
        }

        if (!options.AcceptExternalRunner)
        {
            return Failed("Person-segmentation mask generation runs an external local model runner and requires --accept-external-runner.");
        }

        var runnerPath = ResolvePersonSegmentationRunnerPath(options.RunnerPath);
        if (string.IsNullOrWhiteSpace(runnerPath))
        {
            return Failed("Person-segmentation runner was not found. Pass --runner <exe> or set RECEIPTS_PERSON_SEGMENTER_EXE (GOATSHOT_PERSON_SEGMENTER_EXE remains a compatibility alias).");
        }

        var argumentsTemplate = string.IsNullOrWhiteSpace(options.ArgumentsTemplate)
            ? "--input \"{input}\" --output \"{output}\""
            : options.ArgumentsTemplate.Trim();
        if (!HasPersonSegmentationPlaceholder(argumentsTemplate, "{input}") ||
            !HasPersonSegmentationPlaceholder(argumentsTemplate, "{output}"))
        {
            return Failed("Person-segmentation runner arguments must include {input} and {output} placeholders.");
        }

        string? modelPath = null;
        if (!string.IsNullOrWhiteSpace(options.ModelPath))
        {
            modelPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.ModelPath));
            if (!File.Exists(modelPath))
            {
                return Failed($"Person-segmentation model file not found: {modelPath}");
            }
        }
        else if (HasPersonSegmentationPlaceholder(argumentsTemplate, "{model}"))
        {
            return Failed("Person-segmentation runner arguments include {model}, so --model must point to a local model file.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"person-segmentation-mask-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        IReadOnlyList<string> arguments;
        try
        {
            arguments = BuildPersonSegmentationRunnerArguments(
                argumentsTemplate,
                item.FilePath,
                outputPath,
                modelPath);
        }
        catch (ArgumentException ex)
        {
            return Failed(ex.Message);
        }

        var timeoutSeconds = Math.Clamp(
            options.TimeoutSeconds ?? DefaultPersonSegmentationTimeoutSeconds,
            5,
            MaxPersonSegmentationTimeoutSeconds);
        var result = await RunPersonSegmentationRunnerAsync(
            runnerPath,
            arguments,
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        if (!File.Exists(outputPath))
        {
            return Failed("Person-segmentation runner completed but did not create the requested mask output.");
        }

        if (new FileInfo(outputPath).Length == 0)
        {
            return Failed("Person-segmentation runner created an empty mask output.");
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.PersonSegmentationMaskVideo,
                $"Person-segmentation mask generated from {item.FileName} by local external model runner {Path.GetFileName(runnerPath)}.");
        }

        return Succeeded(outputPath, saved, "Person-segmentation mask video exported");
    }

    private async Task<VideoToolResult> GenerateBundledPersonSegmentationMaskAsync(
        CaptureItem item,
        PersonSegmentationMaskGenerationOptions options,
        string? outputPath,
        bool addToWorkspace,
        CancellationToken cancellationToken)
    {
        if (_personSegmentation is null || !_personSegmentation.GetStatus().Available)
        {
            return Failed("Bundled person segmentation is unavailable. Run Repair from Settings, or configure an explicit external runner override.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg is unavailable. Run Repair from Settings or configure an explicit FFmpeg override.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"person-segmentation-mask-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var workRoot = Path.Combine(_paths.TempRoot, $"person-segmentation-{Guid.NewGuid():N}");
        var framesRoot = Path.Combine(workRoot, "frames");
        var masksRoot = Path.Combine(workRoot, "masks");
        Directory.CreateDirectory(framesRoot);
        Directory.CreateDirectory(masksRoot);
        try
        {
            var maximumFrames = Math.Clamp(options.MaxFrames ?? 18_000, 1, 108_000);
            var extraction = await RunFfmpegAsync(ffmpeg,
                ["-y", "-i", item.FilePath, "-vsync", "0", "-frames:v", maximumFrames.ToString(CultureInfo.InvariantCulture), Path.Combine(framesRoot, "frame-%08d.png")],
                cancellationToken);
            if (!extraction.Succeeded)
            {
                return extraction;
            }

            var frames = Directory.GetFiles(framesRoot, "frame-*.png").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            if (frames.Count == 0)
            {
                return Failed("FFmpeg extracted no frames for person segmentation.");
            }

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var maskPath = Path.Combine(masksRoot, Path.GetFileName(frame));
                var mask = await _personSegmentation.GenerateMaskAsync(frame, maskPath, cancellationToken);
                if (!mask.Succeeded)
                {
                    return Failed(mask.Message);
                }
            }

            var frameRate = await GetVideoFrameRateAsync(ffmpeg, item.FilePath, cancellationToken) ?? 30d;
            var encode = await RunFfmpegAsync(ffmpeg,
                [
                    "-y", "-framerate", frameRate.ToString("0.######", CultureInfo.InvariantCulture),
                    "-i", Path.Combine(masksRoot, "frame-%08d.png"),
                    "-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p", "-movflags", "+faststart", outputPath
                ],
                cancellationToken);
            if (!encode.Succeeded || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                return encode.Succeeded ? Failed("Bundled person segmentation did not produce a mask video.") : encode;
            }

            CaptureItem? saved = null;
            if (addToWorkspace)
            {
                saved = await _workspaceStore.AddImageFileAsync(
                    outputPath,
                    CaptureKind.PersonSegmentationMaskVideo,
                    $"Person-segmentation mask generated from {item.FileName} with the bundled ONNX model ({_personSegmentation.GetStatus().ExecutionProvider}).");
            }

            return Succeeded(outputPath, saved, $"Bundled person-segmentation mask video exported ({frames.Count} frames)");
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    public async Task<VideoToolResult> GenerateHostedPersonSegmentationMaskAsync(
        CaptureItem item,
        HostedPersonSegmentationMaskGenerationOptions options,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        options ??= new HostedPersonSegmentationMaskGenerationOptions();
        if (!options.AcceptHostedService)
        {
            return Failed("Hosted person-segmentation uploads source media to an operator-configured service and requires --accept-hosted-service.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return Failed("Hosted person-segmentation requires --endpoint <https-url>.");
        }

        if (!Uri.TryCreate(options.Endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            return Failed("Hosted person-segmentation endpoint must be an absolute HTTP(S) URL.");
        }

        string? apiToken = null;
        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable))
        {
            var variable = options.ApiKeyEnvironmentVariable.Trim();
            apiToken = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return Failed($"Hosted person-segmentation API token environment variable was not set: {variable}");
            }
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"hosted-person-segmentation-mask-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var timeoutSeconds = Math.Clamp(
            options.TimeoutSeconds ?? DefaultPersonSegmentationTimeoutSeconds,
            5,
            MaxPersonSegmentationTimeoutSeconds);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    string.IsNullOrWhiteSpace(options.AuthorizationScheme) ? "Bearer" : options.AuthorizationScheme.Trim(),
                    apiToken.Trim());
            }

            using var form = new MultipartFormDataContent();
            // Stream the recording instead of buffering it: source videos can be
            // hundreds of MB, and ReadAllBytes + ByteArrayContent held 2-3 copies.
            await using var sourceStream = File.OpenRead(item.FilePath);
            using var videoContent = new StreamContent(sourceStream);
            videoContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(videoContent, "sourceVideo", Path.GetFileName(item.FilePath));
            form.Add(new StringContent("mask-video"), "responseKind");
            if (!string.IsNullOrWhiteSpace(options.ModelId))
            {
                form.Add(new StringContent(options.ModelId.Trim()), "modelId");
            }

            request.Content = form;
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                var message = string.IsNullOrEmpty(body)
                    ? response.ReasonPhrase ?? "no response body"
                    : body[..Math.Min(body.Length, 512)];
                return Failed($"Hosted person-segmentation service returned {(int)response.StatusCode} {response.StatusCode}. {SensitiveTextDetector.Redact(message)}");
            }

            await using (var maskStream = File.Create(outputPath))
            {
                await response.Content.CopyToAsync(maskStream, linked.Token);
            }

            if (new FileInfo(outputPath).Length == 0)
            {
                File.Delete(outputPath);
                return Failed("Hosted person-segmentation service returned an empty mask video.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed($"Hosted person-segmentation service timed out after {timeoutSeconds} seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failed($"Hosted person-segmentation request failed. {SensitiveTextDetector.Redact(ex.Message)}");
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return Failed("Hosted person-segmentation service did not create a usable mask output.");
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.PersonSegmentationMaskVideo,
                $"Person-segmentation mask generated from {item.FileName} by an explicitly accepted hosted segmentation service.");
        }

        return Succeeded(outputPath, saved, "Hosted person-segmentation mask video exported");
    }

    internal static string BuildMiddleCutFilter(TimeSpan start, TimeSpan end, bool includeAudio)
    {
        if (!includeAudio)
        {
            return string.Join(
                string.Empty,
                $"[0:v:0]trim=start=0:end={FormatTime(start)},setpts=PTS-STARTPTS[v0];",
                $"[0:v:0]trim=start={FormatTime(end)},setpts=PTS-STARTPTS[v1];",
                "[v0][v1]concat=n=2:v=1:a=0[outv]");
        }

        return string.Join(
            string.Empty,
            $"[0:v:0]trim=start=0:end={FormatTime(start)},setpts=PTS-STARTPTS[v0];",
            $"[0:a:0]atrim=start=0:end={FormatTime(start)},asetpts=PTS-STARTPTS[a0];",
            $"[0:v:0]trim=start={FormatTime(end)},setpts=PTS-STARTPTS[v1];",
            $"[0:a:0]atrim=start={FormatTime(end)},asetpts=PTS-STARTPTS[a1];",
            "[v0][a0][v1][a1]concat=n=2:v=1:a=1[outv][outa]");
    }

    internal static IReadOnlyList<string> BuildMiddleCutArguments(
        string inputPath,
        string outputPath,
        string filter,
        bool includeAudio)
    {
        var arguments = new List<string>
        {
            "-y",
            "-i",
            inputPath,
            "-filter_complex",
            filter,
            "-map",
            "[outv]"
        };
        if (includeAudio)
        {
            arguments.AddRange([
                "-map",
                "[outa]"
            ]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange([
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p"
        ]);
        if (includeAudio)
        {
            arguments.AddRange([
                "-c:a",
                "aac",
                "-b:a",
                "160k"
            ]);
        }

        arguments.AddRange([
            "-movflags",
            "+faststart",
            outputPath
        ]);
        return arguments;
    }

    internal static IReadOnlyList<VideoCutRange> NormalizeCutRanges(
        IEnumerable<VideoEditCut> cuts,
        double? durationSeconds = null)
    {
        var duration = durationSeconds is > 0d
            ? durationSeconds.Value
            : (double?)null;
        var ordered = cuts
            .Select(cut =>
            {
                var start = Math.Max(0d, cut.StartSeconds);
                var end = Math.Max(0d, cut.EndSeconds);
                if (duration.HasValue)
                {
                    start = Math.Min(start, duration.Value);
                    end = Math.Min(end, duration.Value);
                }

                return new VideoCutRange(RoundVideoSeconds(start), RoundVideoSeconds(end));
            })
            .Where(range => range.EndSeconds - range.StartSeconds > 0.001d)
            .OrderBy(range => range.StartSeconds)
            .ThenBy(range => range.EndSeconds)
            .ToList();

        var merged = new List<VideoCutRange>();
        foreach (var range in ordered)
        {
            if (merged.Count == 0 || range.StartSeconds > merged[^1].EndSeconds + 0.001d)
            {
                merged.Add(range);
                continue;
            }

            merged[^1] = merged[^1] with
            {
                EndSeconds = Math.Max(merged[^1].EndSeconds, range.EndSeconds)
            };
        }

        return merged;
    }

    internal static IReadOnlyList<VideoKeepSegment> BuildKeepSegments(
        IReadOnlyList<VideoCutRange> cutRanges,
        double? durationSeconds = null)
    {
        var duration = durationSeconds is > 0d
            ? RoundVideoSeconds(durationSeconds.Value)
            : (double?)null;
        var segments = new List<VideoKeepSegment>();
        var cursor = 0d;
        foreach (var range in cutRanges)
        {
            if (range.StartSeconds > cursor + 0.001d)
            {
                segments.Add(new VideoKeepSegment(RoundVideoSeconds(cursor), range.StartSeconds));
            }

            cursor = Math.Max(cursor, range.EndSeconds);
        }

        if (duration.HasValue)
        {
            if (duration.Value > cursor + 0.001d)
            {
                segments.Add(new VideoKeepSegment(RoundVideoSeconds(cursor), duration.Value));
            }
        }
        else
        {
            segments.Add(new VideoKeepSegment(RoundVideoSeconds(cursor), null));
        }

        return segments;
    }

    internal static string BuildAdvancedCutFilter(
        IReadOnlyList<VideoKeepSegment> keepSegments,
        bool includeAudio)
    {
        if (keepSegments.Count == 0)
        {
            throw new ArgumentException("At least one keep segment is required.", nameof(keepSegments));
        }

        var filters = new List<string>();
        for (var i = 0; i < keepSegments.Count; i++)
        {
            var segment = keepSegments[i];
            var videoLabel = keepSegments.Count == 1 ? "outv" : $"v{i}";
            filters.Add($"[0:v:0]{BuildTrimFilter("trim", segment)},setpts=PTS-STARTPTS[{videoLabel}]");
            if (includeAudio)
            {
                var audioLabel = keepSegments.Count == 1 ? "outa" : $"a{i}";
                filters.Add($"[0:a:0]{BuildTrimFilter("atrim", segment)},asetpts=PTS-STARTPTS[{audioLabel}]");
            }
        }

        if (keepSegments.Count > 1)
        {
            var labels = string.Concat(Enumerable.Range(0, keepSegments.Count)
                .Select(index => includeAudio
                    ? $"[v{index}][a{index}]"
                    : $"[v{index}]"));
            filters.Add(includeAudio
                ? $"{labels}concat=n={keepSegments.Count}:v=1:a=1[outv][outa]"
                : $"{labels}concat=n={keepSegments.Count}:v=1:a=0[outv]");
        }

        return string.Join(";", filters);
    }

    internal static IReadOnlyList<string> BuildAdvancedCutArguments(
        string inputPath,
        string outputPath,
        string filter,
        bool includeAudio)
    {
        return BuildMiddleCutArguments(inputPath, outputPath, filter, includeAudio);
    }

    internal static IReadOnlyList<string> BuildCompositeLayoutArguments(
        string screenPath,
        string cameraPath,
        string outputPath,
        string filter,
        string audioSource)
    {
        var normalizedAudio = NormalizeCompositeAudioSource(audioSource);
        var arguments = new List<string>
        {
            "-y",
            "-i",
            screenPath,
            "-i",
            cameraPath,
            "-filter_complex",
            filter,
            "-map",
            "[outv]"
        };

        if (normalizedAudio == "none")
        {
            arguments.Add("-an");
        }
        else
        {
            arguments.AddRange([
                "-map",
                normalizedAudio == "camera" ? "1:a:0" : "0:a:0"
            ]);
        }

        arguments.AddRange([
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p"
        ]);
        if (normalizedAudio != "none")
        {
            arguments.AddRange([
                "-c:a",
                "aac",
                "-b:a",
                "160k"
            ]);
        }

        arguments.AddRange([
            "-shortest",
            "-movflags",
            "+faststart",
            outputPath
        ]);
        return arguments;
    }

    internal static string BuildWebcamBackgroundFilter(
        WebcamBackgroundProcessingRecipe recipe,
        int? width = null,
        int? height = null)
    {
        if (IsExternalMaskBackgroundRecipe(recipe))
        {
            return BuildExternalMaskBackgroundFilter(recipe, width, height);
        }

        var keyColor = AdvancedVideoEditPlannerService.NormalizeFfmpegColor(recipe.KeyColor, "0x00ff00");
        var similarity = FormatFactor(recipe.Similarity);
        var blend = FormatFactor(recipe.Blend);
        var blur = Math.Clamp(recipe.BlurStrength, 1, 64);
        if (recipe.Mode.Equals("blur", StringComparison.OrdinalIgnoreCase))
        {
            return $"[0:v]split[bg][fg];[bg]boxblur={blur}:1[blurred];[fg]chromakey={keyColor}:{similarity}:{blend}[keyed];[blurred][keyed]overlay=0:0:format=auto[outv]";
        }

        if (width is null or <= 0 || height is null or <= 0)
        {
            throw new ArgumentException("Width and height are required for solid-background webcam processing.");
        }

        var background = AdvancedVideoEditPlannerService.NormalizeFfmpegColor(
            recipe.Mode.Equals("remove", StringComparison.OrdinalIgnoreCase)
                ? "0x101820"
                : recipe.BackgroundColor,
            "0x101820");
        return $"color=c={background}:s={width}x{height}[bg];[0:v]chromakey={keyColor}:{similarity}:{blend}[keyed];[bg][keyed]overlay=0:0:format=auto:shortest=1:eof_action=endall[outv]";
    }

    internal static IReadOnlyList<string> BuildWebcamBackgroundArguments(
        string inputPath,
        string? maskPath,
        string outputPath,
        string filter,
        bool includeAudio)
    {
        var arguments = new List<string>
        {
            "-y",
            "-i",
            inputPath
        };
        if (!string.IsNullOrWhiteSpace(maskPath))
        {
            arguments.AddRange([
                "-i",
                maskPath
            ]);
        }

        arguments.AddRange([
            "-filter_complex",
            filter,
            "-map",
            "[outv]"
        ]);
        if (includeAudio)
        {
            arguments.AddRange([
                "-map",
                "0:a:0"
            ]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange([
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p"
        ]);
        if (includeAudio)
        {
            arguments.AddRange([
                "-c:a",
                "aac",
                "-b:a",
                "160k"
            ]);
        }

        arguments.AddRange([
            "-shortest",
            "-movflags",
            "+faststart",
            outputPath
        ]);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildWebcamBackgroundArguments(
        string inputPath,
        string outputPath,
        string filter,
        bool includeAudio)
    {
        return BuildWebcamBackgroundArguments(inputPath, null, outputPath, filter, includeAudio);
    }

    internal static string BuildForegroundMaskFilter(WebcamMaskGenerationOptions options)
    {
        options ??= new WebcamMaskGenerationOptions();
        var filter = NormalizeForegroundMaskMethod(options.Method) switch
        {
            "alpha" => "format=rgba,alphaextract,format=gray",
            "luma" => $"format=gray,lut=y='if(gte(val\\,{Math.Clamp(options.LumaThreshold ?? 128, 0, 255)})\\,255\\,0)'",
            _ => BuildKeyedForegroundMaskFilter(options)
        };

        return options.InvertMask
            ? $"{filter},negate"
            : filter;
    }

    internal static IReadOnlyList<string> BuildForegroundMaskArguments(
        string inputPath,
        string outputPath,
        string filter)
    {
        return
        [
            "-y",
            "-i",
            inputPath,
            "-vf",
            filter,
            "-an",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            outputPath
        ];
    }

    internal static IReadOnlyList<string> BuildPersonSegmentationRunnerArguments(
        string argumentsTemplate,
        string inputPath,
        string outputPath,
        string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(argumentsTemplate))
        {
            throw new ArgumentException("Person-segmentation runner arguments cannot be empty.");
        }

        var expanded = argumentsTemplate
            .Replace("{input}", inputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{output}", outputPath, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            expanded = expanded.Replace("{model}", modelPath, StringComparison.OrdinalIgnoreCase);
        }

        if (HasPersonSegmentationPlaceholder(expanded, "{model}"))
        {
            throw new ArgumentException("Person-segmentation runner arguments include {model}, so --model must point to a local model file.");
        }

        return SplitProcessArguments(expanded);
    }

    private static string BuildKeyedForegroundMaskFilter(WebcamMaskGenerationOptions options)
    {
        var keyColor = AdvancedVideoEditPlannerService.NormalizeFfmpegColor(options.KeyColor, "0x00ff00");
        var similarity = FormatFactor(Math.Clamp(options.Similarity ?? 0.18d, 0.01d, 1d));
        var blend = FormatFactor(Math.Clamp(options.Blend ?? 0.08d, 0d, 1d));
        return $"chromakey={keyColor}:{similarity}:{blend},format=rgba,alphaextract,format=gray";
    }

    private static string NormalizeForegroundMaskMethod(string? method)
    {
        method = (method ?? "keyed").Trim().ToLowerInvariant();
        return method switch
        {
            "alpha" or "alphaextract" or "existing-alpha" => "alpha",
            "luma" or "luminance" or "threshold" => "luma",
            _ => "keyed"
        };
    }

    private static bool HasPersonSegmentationPlaceholder(string value, string placeholder)
    {
        return value.Contains(placeholder, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePersonSegmentationRunnerPath(string? runnerPath)
    {
        runnerPath = string.IsNullOrWhiteSpace(runnerPath)
            ? BrandEnvironment.Resolve("PERSON_SEGMENTER_EXE").Value
            : runnerPath;
        if (string.IsNullOrWhiteSpace(runnerPath))
        {
            return null;
        }

        runnerPath = Environment.ExpandEnvironmentVariables(runnerPath.Trim().Trim('"'));
        if (Path.IsPathFullyQualified(runnerPath) || runnerPath.Contains(Path.DirectorySeparatorChar) || runnerPath.Contains(Path.AltDirectorySeparatorChar))
        {
            var fullPath = Path.GetFullPath(runnerPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        return FindExecutableOnPath(runnerPath);
    }

    private static string? FindExecutableOnPath(string executableName)
    {
        var extensions = Path.HasExtension(executableName)
            ? [string.Empty]
            : new[] { ".exe" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim(), executableName + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> SplitProcessArguments(string value)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (inQuotes)
        {
            throw new ArgumentException("Person-segmentation runner arguments contain an unterminated quote.");
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static string BuildExternalMaskBackgroundFilter(
        WebcamBackgroundProcessingRecipe recipe,
        int? width,
        int? height)
    {
        if (width is null or <= 0 || height is null or <= 0)
        {
            throw new ArgumentException("Width and height are required for external mask webcam processing.");
        }

        var maskFilter = recipe.InvertMask
            ? $"format=gray,scale={width}:{height},negate"
            : $"format=gray,scale={width}:{height}";
        if (recipe.Mode.Equals("blur", StringComparison.OrdinalIgnoreCase))
        {
            var blur = Math.Clamp(recipe.BlurStrength, 1, 64);
            return $"[0:v]split[fgsrc][bgsrc];[bgsrc]boxblur={blur}:1[bg];[1:v]{maskFilter}[mask];[fgsrc]format=rgba[fg];[fg][mask]alphamerge[subject];[bg][subject]overlay=0:0:format=auto:shortest=1:eof_action=endall[outv]";
        }

        var foreground = $"[1:v]{maskFilter}[mask];[0:v]format=rgba[fg];[fg][mask]alphamerge[subject]";
        var background = AdvancedVideoEditPlannerService.NormalizeFfmpegColor(
            recipe.Mode.Equals("remove", StringComparison.OrdinalIgnoreCase)
                ? "0x101820"
                : recipe.BackgroundColor,
            "0x101820");
        return $"color=c={background}:s={width}x{height}[bg];{foreground};[bg][subject]overlay=0:0:format=auto:shortest=1:eof_action=endall[outv]";
    }

    private static bool IsExternalMaskBackgroundRecipe(WebcamBackgroundProcessingRecipe recipe)
    {
        return (recipe.ProcessingKind ?? string.Empty).Equals("external-mask", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(recipe.MaskPath);
    }

    private static string? ResolveBackgroundMaskPath(WebcamBackgroundProcessingRecipe recipe)
    {
        return string.IsNullOrWhiteSpace(recipe.MaskPath)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(recipe.MaskPath));
    }

    internal static string NormalizeCompositeAudioSource(string? audioSource)
    {
        audioSource = (audioSource ?? "screen").Trim().ToLowerInvariant();
        return audioSource switch
        {
            "camera" or "webcam" or "cam" => "camera",
            "none" or "off" or "mute" or "muted" or "no-audio" => "none",
            _ => "screen"
        };
    }

    public async Task<VideoToolResult> MergeAsync(
        IReadOnlyList<CaptureItem> items,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (items.Count < 2)
        {
            return Failed("Video merge requires at least two input clips.");
        }

        foreach (var item in items)
        {
            if (!File.Exists(item.FilePath))
            {
                return Failed($"Video file not found: {item.FilePath}");
            }
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video merge.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"merged-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var listPath = WriteConcatList(items.Select(item => item.FilePath));
        try
        {
            var copyResult = await RunConcatAsync(ffmpeg, listPath, outputPath, reencode: false, cancellationToken);
            var result = copyResult.Succeeded && File.Exists(outputPath)
                ? copyResult
                : await RunConcatAsync(ffmpeg, listPath, outputPath, reencode: true, cancellationToken);

            if (!result.Succeeded || !File.Exists(outputPath))
            {
                return result.Succeeded
                    ? Failed("FFmpeg did not create a merged video output.")
                    : result;
            }
        }
        finally
        {
            TryDelete(listPath);
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.MergedVideo,
                $"Merged {items.Count} clips: {string.Join(", ", items.Select(item => item.FileName))}.");
        }

        return Succeeded(outputPath, saved, "Merged video exported");
    }

    public async Task<VideoToolResult> AddIntroOutroAsync(
        CaptureItem item,
        string? introImagePath,
        string? outroImagePath,
        int width,
        int height,
        TimeSpan stillDuration,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(introImagePath) && string.IsNullOrWhiteSpace(outroImagePath))
        {
            return Failed("Intro/outro export requires an intro image, an outro image, or both.");
        }

        if (width <= 0 || height <= 0)
        {
            return Failed("Intro/outro width and height must be greater than zero.");
        }

        if (stillDuration <= TimeSpan.Zero)
        {
            return Failed("Intro/outro still duration must be greater than zero.");
        }

        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        if (!string.IsNullOrWhiteSpace(introImagePath) && !File.Exists(introImagePath))
        {
            return Failed($"Intro image not found: {introImagePath}");
        }

        if (!string.IsNullOrWhiteSpace(outroImagePath) && !File.Exists(outroImagePath))
        {
            return Failed($"Outro image not found: {outroImagePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable intro/outro export.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"intro-outro-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var tempRoot = Path.Combine(_paths.TempRoot, $"intro-outro-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var segments = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(introImagePath))
            {
                var introSegment = Path.Combine(tempRoot, "intro.mp4");
                var introResult = await CreateStillVideoAsync(ffmpeg, introImagePath, introSegment, width, height, stillDuration, cancellationToken);
                if (!introResult.Succeeded)
                {
                    return introResult;
                }

                segments.Add(introSegment);
            }

            var normalizedMain = Path.Combine(tempRoot, "main.mp4");
            var mainResult = await ResizeAsync(
                item,
                width,
                height,
                "balanced",
                crf: null,
                bitrate: null,
                fps: null,
                outputPath: normalizedMain,
                addToWorkspace: false,
                cancellationToken: cancellationToken);
            if (!mainResult.Succeeded)
            {
                return mainResult;
            }

            segments.Add(normalizedMain);

            if (!string.IsNullOrWhiteSpace(outroImagePath))
            {
                var outroSegment = Path.Combine(tempRoot, "outro.mp4");
                var outroResult = await CreateStillVideoAsync(ffmpeg, outroImagePath, outroSegment, width, height, stillDuration, cancellationToken);
                if (!outroResult.Succeeded)
                {
                    return outroResult;
                }

                segments.Add(outroSegment);
            }

            var listPath = WriteConcatList(segments);
            try
            {
                var result = await RunConcatAsync(ffmpeg, listPath, outputPath, reencode: true, cancellationToken);
                if (!result.Succeeded || !File.Exists(outputPath))
                {
                    return result.Succeeded
                        ? Failed("FFmpeg did not create an intro/outro video output.")
                        : result;
                }
            }
            finally
            {
                TryDelete(listPath);
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.IntroOutroVideo,
                $"Intro/outro copy exported from {item.FileName}.");
        }

        return Succeeded(outputPath, saved, "Intro/outro video exported");
    }

    public async Task<VideoToolResult> BurnSubtitlesAsync(
        CaptureItem item,
        string subtitlePath,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        if (!File.Exists(subtitlePath))
        {
            return Failed($"Subtitle file not found: {subtitlePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable subtitle burn-in.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.VideosRoot,
            $"subtitled-{Path.GetFileNameWithoutExtension(item.FileName)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = await RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-i",
                item.FilePath,
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-filter:v",
                $"subtitles='{EscapeSubtitleFilterPath(subtitlePath)}'",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-movflags",
                "+faststart",
                outputPath
            ],
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a subtitled video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.SubtitledVideo,
                $"Subtitle-burned copy exported from {item.FileName}; subtitles {Path.GetFileName(subtitlePath)}.");
        }

        return Succeeded(outputPath, saved, "Subtitled video exported");
    }

    public async Task<VideoToolResult> CopySubtitleAsync(
        string subtitlePath,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(subtitlePath))
        {
            return Failed($"Subtitle file not found: {subtitlePath}");
        }

        if (!Path.GetExtension(subtitlePath).Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Subtitle copy currently supports .srt files.");
        }

        outputPath = ResolveOutputPath(
            outputPath,
            _paths.DocumentsRoot,
            $"subtitles-{Path.GetFileNameWithoutExtension(subtitlePath)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.srt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await using (var input = File.OpenRead(subtitlePath))
        await using (var output = File.Create(outputPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.SubtitleFile,
                $"Subtitle file copied from {subtitlePath}.");
        }

        return Succeeded(outputPath, saved, "Subtitle file copied");
    }

    private async Task<VideoToolResult> CropWithFilterAsync(
        CaptureItem item,
        string filter,
        string? outputPath,
        string defaultFileName,
        string workspaceNote,
        bool addToWorkspace,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found. Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) to enable video crop export.");
        }

        outputPath = ResolveOutputPath(outputPath, _paths.VideosRoot, defaultFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var result = await RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-i",
                item.FilePath,
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-filter:v",
                filter,
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-movflags",
                "+faststart",
                outputPath
            ],
            cancellationToken);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            return result.Succeeded
                ? Failed("FFmpeg did not create a cropped video output.")
                : result;
        }

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                outputPath,
                CaptureKind.CroppedVideo,
                workspaceNote);
        }

        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"Cropped video exported: {outputPath}"
                : $"Cropped video exported and indexed: {saved.FileName}"
        };
    }

    private static async Task<VideoToolResult> RunFfmpegAsync(
        string ffmpeg,
        IReadOnlyList<string> arguments,
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
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return Failed("FFmpeg could not be started.");
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
                // Without this, cancelling an export leaves an orphaned ffmpeg.exe running.
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup on cancellation.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            return Failed($"FFmpeg failed with exit code {process.ExitCode}. {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            Message = "FFmpeg completed."
        };
    }

    private static async Task<VideoToolResult> RunPersonSegmentationRunnerAsync(
        string runnerPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = runnerPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return Failed("Person-segmentation runner could not be started.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        string stderr;
        string stdout;
        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            stderr = await stderrTask;
            stdout = await stdoutTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup after timeout.
            }

            return Failed($"Person-segmentation runner timed out after {timeout.TotalSeconds:0} seconds.");
        }

        if (process.ExitCode != 0)
        {
            return Failed($"Person-segmentation runner failed with exit code {process.ExitCode}. {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
        }

        return new VideoToolResult
        {
            Succeeded = true,
            Message = "Person-segmentation runner completed."
        };
    }

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private static async Task<(int ExitCode, string StdOut)?> RunProbeProcessAsync(
        ProcessStartInfo start,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        // Metadata probes should be sub-second; bound them so a stalled input
        // (network mount, corrupt container) cannot hang the caller or orphan ffprobe.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            _ = await stderrTask;
            return (process.ExitCode, stdout);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup for a stalled probe.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }

    private static async Task<bool> HasAudioStreamAsync(
        string ffmpeg,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobe = FindFfprobeForFfmpeg(ffmpeg);
        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            return false;
        }

        var start = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-v",
                "error",
                "-select_streams",
                "a:0",
                "-show_entries",
                "stream=codec_type",
                "-of",
                "csv=p=0",
                inputPath
            }
        };

        try
        {
            var probe = await RunProbeProcessAsync(start, cancellationToken);
            return probe is { ExitCode: 0 } result &&
                result.StdOut.Contains("audio", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<double?> GetVideoDurationSecondsAsync(
        string ffmpeg,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobe = FindFfprobeForFfmpeg(ffmpeg);
        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            return null;
        }

        var start = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-v",
                "error",
                "-show_entries",
                "format=duration",
                "-of",
                "default=noprint_wrappers=1:nokey=1",
                inputPath
            }
        };

        try
        {
            var probe = await RunProbeProcessAsync(start, cancellationToken);
            return probe is { ExitCode: 0 } result &&
                double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0d
                ? seconds
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<VideoDimensions?> GetVideoDimensionsAsync(
        string ffmpeg,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobe = FindFfprobeForFfmpeg(ffmpeg);
        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            return null;
        }

        var start = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-show_entries",
                "stream=width,height",
                "-of",
                "csv=p=0:s=x",
                inputPath
            }
        };

        try
        {
            var probe = await RunProbeProcessAsync(start, cancellationToken);
            var parts = (probe?.StdOut ?? string.Empty).Trim().Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return probe is { ExitCode: 0 } &&
                parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
                width > 0 &&
                height > 0
                ? new VideoDimensions(width, height)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<double?> GetVideoFrameRateAsync(
        string ffmpeg,
        string inputPath,
        CancellationToken cancellationToken)
    {
        var ffprobe = FindFfprobeForFfmpeg(ffmpeg);
        if (string.IsNullOrWhiteSpace(ffprobe)) return null;
        var start = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=avg_frame_rate", "-of", "default=noprint_wrappers=1:nokey=1", inputPath }
        };
        try
        {
            var probe = await RunProbeProcessAsync(start, cancellationToken);
            var parts = (probe?.StdOut ?? string.Empty).Trim().Split('/');
            if (probe is not { ExitCode: 0 } || parts.Length == 0 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return null;
            var denominator = parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 1d;
            return numerator > 0 && denominator > 0 ? numerator / denominator : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    internal static string? FindFfprobeForFfmpeg(string ffmpeg)
    {
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            try
            {
                var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? string.Empty, "ffprobe.exe");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
            catch
            {
                // Fall through to PATH probing.
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffprobe.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string ResolveOutputPath(string? outputPath, string root, string fileName)
    {
        return string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(root, fileName)
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatFactor(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string BuildTrimFilter(string filterName, VideoKeepSegment segment)
    {
        var filter = $"{filterName}=start={FormatSeconds(segment.StartSeconds)}";
        if (segment.EndSeconds.HasValue)
        {
            filter += $":end={FormatSeconds(segment.EndSeconds.Value)}";
        }

        return filter;
    }

    private static string FormatSeconds(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static double RoundVideoSeconds(double value)
    {
        return Math.Round(Math.Max(0d, value), 3, MidpointRounding.AwayFromZero);
    }

    private static string SafeFilePart(string value)
    {
        value = string.IsNullOrWhiteSpace(value)
            ? "plan"
            : value.Trim().ToLowerInvariant();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value.Replace(' ', '-');
    }

    private static string? NormalizeFormat(string? format, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(format) && !string.IsNullOrWhiteSpace(outputPath))
        {
            format = Path.GetExtension(outputPath);
        }

        format = (format ?? "mp4").Trim().TrimStart('.').ToLowerInvariant();
        return format is "mp4" or "gif" or "webm" ? format : null;
    }

    private static string? ResolveCompositeInputPath(string? overridePath, string? planPath)
    {
        var path = string.IsNullOrWhiteSpace(overridePath) ? planPath : overridePath;
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private async Task<string> TryConvertAnimationCompanionAsync(
        string ffmpeg,
        string inputPath,
        string gifOutputPath,
        AnimationExportOptions animationOptions,
        bool addToWorkspace,
        CancellationToken cancellationToken)
    {
        var companionFormat = GifTimingPlanner.NormalizeCompanionFormat(animationOptions.CompanionFormat);
        if (string.IsNullOrWhiteSpace(companionFormat))
        {
            return string.Empty;
        }

        var plan = GifTimingPlanner.Plan(null, animationOptions);
        var companionPath = Path.ChangeExtension(gifOutputPath, companionFormat);
        var result = await RunFfmpegAsync(
            ffmpeg,
            BuildConvertCompanionArguments(inputPath, companionFormat, companionPath, plan.CaptureFrameRate),
            cancellationToken);
        if (!result.Succeeded || !File.Exists(companionPath))
        {
            return result.Succeeded
                ? $"Companion {companionFormat.ToUpperInvariant()} was requested, but FFmpeg did not create an output."
                : $"Companion {companionFormat.ToUpperInvariant()} export failed: {result.Message}";
        }

        if (addToWorkspace)
        {
            await _workspaceStore.AddImageFileAsync(
                companionPath,
                CaptureKind.ConvertedVideo,
                $"High-frame-rate {companionFormat.ToUpperInvariant()} companion exported from GIF conversion at {plan.CaptureFrameRate} fps.");
        }

        return $"Companion {companionFormat.ToUpperInvariant()} saved: {companionPath}.";
    }

    private static string FormatCompanionMessage(string message)
    {
        return string.IsNullOrWhiteSpace(message) ? string.Empty : $" {message}";
    }

    internal static IReadOnlyList<string> ConvertArguments(
        string inputPath,
        string format,
        string outputPath,
        AnimationExportOptions? animationOptions = null)
    {
        animationOptions ??= new AnimationExportOptions();
        var timingPlan = GifTimingPlanner.Plan(null, animationOptions);
        return format switch
        {
            "gif" =>
            [
                "-y",
                "-i",
                inputPath,
                "-filter_complex",
                $"[0:v]fps={timingPlan.EffectiveGifFrameRate.ToString(CultureInfo.InvariantCulture)},split[v0][v1];[v0]palettegen=stats_mode=full[p];[v1][p]paletteuse=dither=sierra2_4a",
                "-an",
                outputPath
            ],
            "webm" =>
            [
                "-y",
                "-i",
                inputPath,
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:v",
                "libvpx-vp9",
                "-b:v",
                "0",
                "-crf",
                "32",
                "-c:a",
                "libopus",
                outputPath
            ],
            _ =>
            [
                "-y",
                "-i",
                inputPath,
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-movflags",
                "+faststart",
                outputPath
            ]
        };
    }

    internal static IReadOnlyList<string> BuildConvertCompanionArguments(
        string inputPath,
        string format,
        string outputPath,
        int frameRate)
    {
        var fps = Math.Clamp(frameRate, GifTimingPlanner.MinimumFrameRate, GifTimingPlanner.MaximumSourceFrameRate)
            .ToString(CultureInfo.InvariantCulture);
        if (format.Equals("webm", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "-y",
                "-i",
                inputPath,
                "-filter:v",
                $"fps={fps}",
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:v",
                "libvpx-vp9",
                "-b:v",
                "0",
                "-crf",
                "28",
                "-c:a",
                "libopus",
                outputPath
            ];
        }

        return
        [
            "-y",
            "-i",
            inputPath,
            "-filter:v",
            $"fps={fps}",
            "-map",
            "0:v:0",
            "-map",
            "0:a?",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-crf",
            "18",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-movflags",
            "+faststart",
            outputPath
        ];
    }

    private static VideoToolResult Succeeded(string outputPath, CaptureItem? saved, string messagePrefix)
    {
        return new VideoToolResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            Item = saved,
            Message = saved is null
                ? $"{messagePrefix}: {outputPath}"
                : $"{messagePrefix} and indexed: {saved.FileName}"
        };
    }

    private static string BuildScaleFilter(int? width, int? height)
    {
        if (width is null && height is null)
        {
            return string.Empty;
        }

        var w = width is null
            ? "-2"
            : $"trunc({width.Value}/2)*2";
        var h = height is null
            ? "-2"
            : $"trunc({height.Value}/2)*2";
        return $"scale={w}:{h}";
    }

    private static int QualityProfileCrf(string? qualityProfile)
    {
        return NormalizeQualityProfile(qualityProfile) switch
        {
            "small" or "small file" => 30,
            "high" or "high quality" => 18,
            "near-lossless" or "lossless" => 12,
            _ => 23
        };
    }

    private static string NormalizeQualityProfile(string? qualityProfile)
    {
        return string.IsNullOrWhiteSpace(qualityProfile)
            ? "balanced"
            : qualityProfile.Trim().ToLowerInvariant();
    }

    private string WriteConcatList(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(_paths.TempRoot);
        var listPath = Path.Combine(_paths.TempRoot, $"concat-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(
            listPath,
            paths.Select(path => $"file '{EscapeConcatPath(path)}'"));
        return listPath;
    }

    private static string EscapeConcatPath(string path)
    {
        return Path.GetFullPath(path)
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private static Task<VideoToolResult> RunConcatAsync(
        string ffmpeg,
        string listPath,
        string outputPath,
        bool reencode,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            listPath
        };

        if (reencode)
        {
            arguments.AddRange([
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-movflags",
                "+faststart"
            ]);
        }
        else
        {
            arguments.AddRange([
                "-c",
                "copy",
                "-movflags",
                "+faststart"
            ]);
        }

        arguments.Add(outputPath);
        return RunFfmpegAsync(ffmpeg, arguments, cancellationToken);
    }

    private static Task<VideoToolResult> CreateStillVideoAsync(
        string ffmpeg,
        string imagePath,
        string outputPath,
        int width,
        int height,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return RunFfmpegAsync(
            ffmpeg,
            [
                "-y",
                "-loop",
                "1",
                "-t",
                FormatTime(duration),
                "-i",
                imagePath,
                "-filter:v",
                $"scale=trunc({width}/2)*2:trunc({height}/2)*2,fps=30,format=yuv420p",
                "-an",
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-pix_fmt",
                "yuv420p",
                outputPath
            ],
            cancellationToken);
    }

    private static string EscapeSubtitleFilterPath(string path)
    {
        return Path.GetFullPath(path)
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary FFmpeg inputs.
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
        catch
        {
            // Best-effort cleanup for temporary FFmpeg outputs.
        }
    }

    private static VideoToolResult Failed(string message)
    {
        return new VideoToolResult
        {
            Succeeded = false,
            Message = message
        };
    }
}

internal sealed record VideoCutRange(double StartSeconds, double EndSeconds);

internal sealed record VideoKeepSegment(double StartSeconds, double? EndSeconds);

internal sealed record VideoDimensions(int Width, int Height);

public sealed class HostedPersonSegmentationMaskGenerationOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKeyEnvironmentVariable { get; set; }
    public string? AuthorizationScheme { get; set; } = "Bearer";
    public string? ModelId { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool AcceptHostedService { get; set; }
}

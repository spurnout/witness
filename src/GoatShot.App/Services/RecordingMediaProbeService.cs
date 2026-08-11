using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed record RecordingMediaProbeResult(
    string FilePath,
    bool FfprobeAvailable,
    bool Succeeded,
    bool Skipped,
    string Message,
    string? FfprobePath,
    string? VideoCodec,
    int? Width,
    int? Height,
    double? ContainerDurationSeconds,
    double? VideoDurationSeconds,
    int? VideoFrames,
    int AudioStreamCount,
    IReadOnlyList<double> AudioDurationSeconds,
    double? MaxAudioVideoDeltaSeconds)
{
    public bool HasAudio => AudioStreamCount > 0;

    public string SyncSummary
    {
        get
        {
            if (Skipped)
            {
                return Message;
            }

            if (!Succeeded)
            {
                return Message;
            }

            if (AudioStreamCount == 0)
            {
                return "No audio streams were reported; A/V sync proof is not applicable.";
            }

            if (!MaxAudioVideoDeltaSeconds.HasValue)
            {
                return "Audio streams were reported, but duration metadata was incomplete; run a manual playback check for sync.";
            }

            return MaxAudioVideoDeltaSeconds.Value <= 0.25d
                ? $"A/V duration delta is {FormatSeconds(MaxAudioVideoDeltaSeconds.Value)}, within the 0.25s local proof threshold."
                : $"A/V duration delta is {FormatSeconds(MaxAudioVideoDeltaSeconds.Value)}, above the 0.25s local proof threshold.";
        }
    }

    private static string FormatSeconds(double seconds) =>
        $"{Math.Max(0d, seconds):0.###}s";
}

public static class RecordingMediaProbeService
{
    public static async Task<RecordingMediaProbeResult> ProbeAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(inputPath));
        if (!File.Exists(fullPath))
        {
            return Failed(fullPath, $"Recording media file not found: {fullPath}");
        }

        var ffprobe = FindFfprobe();
        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            return Skipped(fullPath, "ffprobe unavailable; recording metadata proof skipped. Install ffprobe.exe beside ffmpeg.exe, add it to PATH, or set RECEIPTS_FFPROBE_PATH (GOATSHOT_FFPROBE_PATH remains a compatibility alias).");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
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
                    "-print_format",
                    "json",
                    "-show_format",
                    "-show_streams",
                    fullPath
                }
            });

            if (process is null)
            {
                return Failed(fullPath, $"ffprobe could not be started: {ffprobe}", ffprobe);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return Failed(fullPath, $"ffprobe failed with exit code {process.ExitCode}: {detail.Trim()}", ffprobe);
            }

            return ParseFfprobeJson(fullPath, ffprobe, stdout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(fullPath, $"ffprobe metadata extraction failed: {ex.GetType().Name}: {ex.Message}", ffprobe);
        }
    }

    public static RecordingMediaProbeResult ParseFfprobeJson(
        string filePath,
        string? ffprobePath,
        string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? videoCodec = null;
        int? width = null;
        int? height = null;
        double? videoDuration = null;
        int? frames = null;
        var audioDurations = new List<double>();
        var audioStreamCount = 0;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = GetString(stream, "codec_type");
                if (codecType.Equals("video", StringComparison.OrdinalIgnoreCase) && videoCodec is null)
                {
                    videoCodec = GetString(stream, "codec_name");
                    width = GetInt(stream, "width");
                    height = GetInt(stream, "height");
                    videoDuration = GetDouble(stream, "duration");
                    frames = GetInt(stream, "nb_frames");
                    continue;
                }

                if (codecType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                {
                    audioStreamCount++;
                    var duration = GetDouble(stream, "duration");
                    if (duration.HasValue)
                    {
                        audioDurations.Add(duration.Value);
                    }
                }
            }
        }

        var containerDuration = root.TryGetProperty("format", out var format)
            ? GetDouble(format, "duration")
            : null;
        var referenceDuration = videoDuration ?? containerDuration;
        double? maxDelta = null;
        if (referenceDuration.HasValue && audioDurations.Count > 0)
        {
            maxDelta = audioDurations
                .Select(duration => Math.Abs(duration - referenceDuration.Value))
                .DefaultIfEmpty(0d)
                .Max();
        }

        var resolution = width.HasValue && height.HasValue
            ? $"{width.Value}x{height.Value}"
            : "unknown size";
        var durationText = referenceDuration.HasValue
            ? $"{referenceDuration.Value:0.###}s"
            : "unknown duration";
        var audioText = audioStreamCount == 0
            ? "no audio streams"
            : $"{audioStreamCount} audio stream(s)";
        var deltaText = maxDelta.HasValue
            ? $"; max A/V duration delta {maxDelta.Value:0.###}s"
            : string.Empty;

        return new RecordingMediaProbeResult(
            Path.GetFullPath(filePath),
            FfprobeAvailable: !string.IsNullOrWhiteSpace(ffprobePath),
            Succeeded: true,
            Skipped: false,
            $"ffprobe metadata: codec={videoCodec ?? "unknown"}, {resolution}, duration={durationText}, frames={(frames?.ToString(CultureInfo.InvariantCulture) ?? "unknown")}, {audioText}{deltaText}.",
            ffprobePath,
            videoCodec,
            width,
            height,
            containerDuration,
            videoDuration,
            frames,
            audioStreamCount,
            audioDurations,
            maxDelta);
    }

    public static string? FindFfprobe()
    {
        var configured = BrandEnvironment.Resolve("FFPROBE_PATH").Value;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.ExpandEnvironmentVariables(configured);
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        var sibling = VideoToolService.FindFfprobeForFfmpeg(ffmpeg ?? string.Empty);
        return string.IsNullOrWhiteSpace(sibling) ? null : sibling;
    }

    private static RecordingMediaProbeResult Skipped(string filePath, string message) =>
        new(
            filePath,
            FfprobeAvailable: false,
            Succeeded: false,
            Skipped: true,
            message,
            FfprobePath: null,
            VideoCodec: null,
            Width: null,
            Height: null,
            ContainerDurationSeconds: null,
            VideoDurationSeconds: null,
            VideoFrames: null,
            AudioStreamCount: 0,
            AudioDurationSeconds: Array.Empty<double>(),
            MaxAudioVideoDeltaSeconds: null);

    private static RecordingMediaProbeResult Failed(string filePath, string message, string? ffprobePath = null) =>
        new(
            filePath,
            FfprobeAvailable: !string.IsNullOrWhiteSpace(ffprobePath),
            Succeeded: false,
            Skipped: false,
            message,
            ffprobePath,
            VideoCodec: null,
            Width: null,
            Height: null,
            ContainerDurationSeconds: null,
            VideoDurationSeconds: null,
            VideoFrames: null,
            AudioStreamCount: 0,
            AudioDurationSeconds: Array.Empty<double>(),
            MaxAudioVideoDeltaSeconds: null);

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? string.Empty,
                JsonValueKind.Number => property.GetRawText(),
                _ => string.Empty
            }
            : string.Empty;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

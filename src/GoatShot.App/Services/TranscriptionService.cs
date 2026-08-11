using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed partial class TranscriptionService
{
    private readonly AppPaths _paths;
    private readonly GeminiImageProvider? _gemini;
    private readonly AppSettings? _settings;

    public TranscriptionService(AppPaths paths, GeminiImageProvider? gemini = null, AppSettings? settings = null)
    {
        _paths = paths;
        _gemini = gemini;
        _settings = settings;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string videoPath,
        string? srtPath = null,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        return await TranscribeAsync(
            new TranscriptionRequest(videoPath, srtPath, outputPath),
            cancellationToken);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        request = request with
        {
            WhisperExecutablePath = FirstNonEmpty(request.WhisperExecutablePath, _settings?.ExternalWhisperExecutablePath),
            WhisperModelPath = FirstNonEmpty(request.WhisperModelPath, _settings?.ExternalWhisperModelPath),
            Language = FirstNonEmpty(request.Language, _settings?.ExternalWhisperLanguage)
        };
        var videoPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.VideoPath));
        if (!File.Exists(videoPath))
        {
            return Failed($"Video file not found: {videoPath}");
        }

        var resolvedSrt = ResolveSrtPath(videoPath, request.SrtPath);
        if (!string.IsNullOrWhiteSpace(resolvedSrt) && File.Exists(resolvedSrt))
        {
            return await ImportSrtAsync(resolvedSrt, request.OutputPath, cancellationToken);
        }

        var embedded = await TryExtractEmbeddedSrtAsync(videoPath, request.OutputPath, cancellationToken);
        if (embedded.Succeeded)
        {
            return embedded;
        }

        TranscriptionResult? providerResult = null;
        if (request.UseAiProvider)
        {
            providerResult = await TryTranscribeWithProviderAsync(request with { VideoPath = videoPath }, cancellationToken);
            if (providerResult.Succeeded)
            {
                return providerResult;
            }
        }

        var whisper = await TryTranscribeWithLocalWhisperAsync(request with { VideoPath = videoPath }, cancellationToken);
        if (whisper.Succeeded)
        {
            return whisper;
        }

        var providerMessage = request.UseAiProvider
            ? $" Provider STT: {providerResult?.Message ?? "not attempted"}"
            : " Provider STT was not requested; pass --ai --provider gemini to send extracted audio to configured Gemini for short recordings.";
        return Failed(
            "Automatic speech-to-text could not run. Provide --srt, embed an SRT/subtitle stream, or explicitly configure an external Whisper executable/model. Receipts never downloads or bundles Whisper. " +
            $"Embedded subtitle extraction: {embedded.Message} External Whisper: {whisper.Message}{providerMessage}");
    }

    public async Task<TranscriptionResult> ImportSrtAsync(
        string srtPath,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        srtPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(srtPath));
        if (!File.Exists(srtPath))
        {
            return Failed($"SRT file not found: {srtPath}");
        }

        var text = await File.ReadAllTextAsync(srtPath, cancellationToken);
        var segments = ParseSrt(text).ToList();
        if (segments.Count == 0)
        {
            return Failed("No transcript segments were found in the SRT file.");
        }

        outputPath = ResolveOutputPath(outputPath, srtPath);
        var transcript = BuildTranscriptText(segments);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, transcript, Encoding.UTF8, cancellationToken);

        return new TranscriptionResult
        {
            Succeeded = true,
            Message = $"Transcript created from {segments.Count} SRT segment(s).",
            TranscriptPath = outputPath,
            SrtPath = srtPath,
            Segments = segments,
            Text = transcript
        };
    }

    public static IReadOnlyList<TranscriptSegment> ParseSrt(string text)
    {
        var segments = new List<TranscriptSegment>();
        var blocks = text
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2)
            {
                continue;
            }

            var timingIndex = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0)
            {
                continue;
            }

            var timing = TimingRegex().Match(lines[timingIndex]);
            if (!timing.Success ||
                !TryParseSrtTime(timing.Groups["start"].Value, out var start) ||
                !TryParseSrtTime(timing.Groups["end"].Value, out var end))
            {
                continue;
            }

            var textLines = lines.Skip(timingIndex + 1).ToList();
            if (textLines.Count == 0)
            {
                continue;
            }

            segments.Add(new TranscriptSegment
            {
                Index = segments.Count + 1,
                Start = start,
                End = end,
                Text = string.Join(" ", textLines).Trim()
            });
        }

        return segments;
    }

    public static string BuildTranscriptText(IReadOnlyList<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append('[');
            builder.Append(FormatTimestamp(segment.Start));
            builder.Append("] ");
            builder.AppendLine(segment.Text);
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static bool TryParseSrtTime(string value, out TimeSpan time)
    {
        value = value.Replace(',', '.');
        return TimeSpan.TryParseExact(
            value,
            @"hh\:mm\:ss\.fff",
            CultureInfo.InvariantCulture,
            out time);
    }

    private string ResolveOutputPath(string? outputPath, string srtPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        return Path.Combine(
            _paths.DocumentsRoot,
            $"{Path.GetFileNameWithoutExtension(srtPath)}-transcript-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
    }

    private async Task<TranscriptionResult> TryExtractEmbeddedSrtAsync(
        string videoPath,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found, so embedded subtitle extraction was skipped.");
        }

        var tempRoot = Path.Combine(_paths.TempRoot, $"transcribe-subtitle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var srtPath = Path.Combine(tempRoot, "embedded.srt");
        try
        {
            if (!await HasEmbeddedSubtitleStreamAsync(ffmpeg, videoPath, cancellationToken))
            {
                return Failed("No embedded subtitle stream was found.");
            }

            var result = await RunProcessAsync(
                ffmpeg,
                [
                    "-y",
                    "-i",
                    videoPath,
                    "-map",
                    "0:s:0",
                    "-c:s",
                    "srt",
                    srtPath
                ],
                TimeSpan.FromSeconds(30),
                cancellationToken);
            if (!result.Succeeded || !File.Exists(srtPath) || new FileInfo(srtPath).Length == 0)
            {
                return Failed(result.Succeeded
                    ? "No embedded subtitle stream produced SRT text."
                    : result.Message);
            }

            var transcript = await ImportSrtAsync(srtPath, outputPath, cancellationToken);
            if (!transcript.Succeeded)
            {
                return transcript;
            }

            transcript.SrtPath = null;
            transcript.Message = $"Transcript created from embedded subtitle stream. {transcript.Message}";
            return transcript;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<TranscriptionResult> TryTranscribeWithProviderAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (_gemini is null)
        {
            return Failed("Gemini provider is not configured in this transcription service.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found, so audio extraction for provider speech-to-text could not run.");
        }

        var tempRoot = Path.Combine(_paths.TempRoot, $"transcribe-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var wavPath = Path.Combine(tempRoot, "audio.wav");
        try
        {
            var audio = await RunProcessAsync(
                ffmpeg,
                [
                    "-y",
                    "-i",
                    request.VideoPath,
                    "-vn",
                    "-ac",
                    "1",
                    "-ar",
                    "16000",
                    "-c:a",
                    "pcm_s16le",
                    wavPath
                ],
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (!audio.Succeeded || !File.Exists(wavPath) || new FileInfo(wavPath).Length == 0)
            {
                return Failed(audio.Succeeded
                    ? "FFmpeg did not create an audio WAV for provider speech-to-text."
                    : audio.Message);
            }

            var result = await _gemini.TranscribeAudioAsync(
                wavPath,
                BuildProviderSpeechPrompt(request.Language, request.ProviderPrompt),
                request.AiModelId ?? string.Empty,
                cancellationToken);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
            {
                return Failed(result.Message);
            }

            var parsed = ParseProviderTranscription(result.Text);
            var transcript = parsed.Segments.Count > 0
                ? BuildTranscriptText(parsed.Segments)
                : parsed.Text.Trim();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return Failed("Provider speech-to-text returned no transcript text.");
            }

            var outputPath = ResolveOutputPath(request.OutputPath, request.VideoPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, transcript, Encoding.UTF8, cancellationToken);

            return new TranscriptionResult
            {
                Succeeded = true,
                Message = $"Transcript created with Gemini provider speech-to-text. {result.Message}",
                TranscriptPath = outputPath,
                Segments = parsed.Segments,
                Text = transcript,
                UsedAiProvider = true,
                ProviderName = "Gemini",
                ModelId = result.ModelId
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<TranscriptionResult> TryTranscribeWithLocalWhisperAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var engine = ResolveLocalSpeechEngine(request);
        if (engine is null)
        {
            return Failed("No configured external Whisper executable was found.");
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return Failed("FFmpeg was not found, so audio extraction for external Whisper could not run.");
        }

        var tempRoot = Path.Combine(_paths.TempRoot, $"transcribe-whisper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var wavPath = Path.Combine(tempRoot, "audio.wav");
        try
        {
            var audio = await RunProcessAsync(
                ffmpeg,
                [
                    "-y",
                    "-i",
                    request.VideoPath,
                    "-vn",
                    "-ac",
                    "1",
                    "-ar",
                    "16000",
                    "-c:a",
                    "pcm_s16le",
                    wavPath
                ],
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (!audio.Succeeded || !File.Exists(wavPath) || new FileInfo(wavPath).Length == 0)
            {
                return Failed(audio.Succeeded
                    ? "FFmpeg did not create an audio WAV for external Whisper."
                    : audio.Message);
            }

            var srtPath = await RunLocalWhisperAsync(engine, wavPath, tempRoot, request, cancellationToken);
            if (string.IsNullOrWhiteSpace(srtPath) || !File.Exists(srtPath))
            {
                return Failed($"{engine.KindLabel} did not produce an SRT file.");
            }

            var transcript = await ImportSrtAsync(srtPath, request.OutputPath, cancellationToken);
            if (!transcript.Succeeded)
            {
                return transcript;
            }

            transcript.SrtPath = null;
            transcript.Message = $"Transcript created with {engine.KindLabel}. {transcript.Message}";
            return transcript;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<string?> RunLocalWhisperAsync(
        LocalSpeechEngine engine,
        string wavPath,
        string outputDirectory,
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var outputPrefix = Path.Combine(outputDirectory, "whisper-output");
        var arguments = engine.Kind == LocalSpeechEngineKind.WhisperCpp
            ? BuildWhisperCppArguments(wavPath, outputPrefix, request.WhisperModelPath ?? engine.ModelPath, request.Language)
            : BuildOpenAiWhisperArguments(wavPath, outputDirectory, request.WhisperModelPath ?? engine.ModelPath, request.Language);
        var result = await RunProcessAsync(
            engine.ExecutablePath,
            arguments,
            ComputeLocalWhisperTimeout(wavPath),
            cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var expected = engine.Kind == LocalSpeechEngineKind.WhisperCpp
            ? outputPrefix + ".srt"
            : Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(wavPath) + ".srt");
        if (File.Exists(expected))
        {
            return expected;
        }

        return Directory.EnumerateFiles(outputDirectory, "*.srt", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static readonly TimeSpan MinimumLocalWhisperTimeout = TimeSpan.FromMinutes(15);

    private static TimeSpan ComputeLocalWhisperTimeout(string wavPath)
    {
        // A fixed 15-minute ceiling kills legitimate CPU-only transcriptions of long
        // recordings mid-run; scale with the source audio length (4x realtime) instead.
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            var scaled = TimeSpan.FromTicks(reader.TotalTime.Ticks * 4);
            return scaled > MinimumLocalWhisperTimeout ? scaled : MinimumLocalWhisperTimeout;
        }
        catch
        {
            return MinimumLocalWhisperTimeout;
        }
    }

    internal static IReadOnlyList<string> BuildOpenAiWhisperArguments(
        string wavPath,
        string outputDirectory,
        string? model,
        string? language)
    {
        var args = new List<string>
        {
            wavPath,
            "--output_format",
            "srt",
            "--output_dir",
            outputDirectory,
            "--model",
            string.IsNullOrWhiteSpace(model) ? "base" : model.Trim()
        };
        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add("--language");
            args.Add(language.Trim());
        }

        return args;
    }

    internal static IReadOnlyList<string> BuildWhisperCppArguments(
        string wavPath,
        string outputPrefix,
        string? modelPath,
        string? language)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            args.Add("-m");
            args.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(modelPath)));
        }

        args.Add("-f");
        args.Add(wavPath);
        args.Add("-osrt");
        args.Add("-of");
        args.Add(outputPrefix);
        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add("-l");
            args.Add(language.Trim());
        }

        return args;
    }

    internal static LocalSpeechEngine? ResolveLocalSpeechEngine(TranscriptionRequest request)
    {
        var configured = FirstNonEmpty(
            request.WhisperExecutablePath,
            BrandEnvironment.Resolve("WHISPER_EXE").Value,
            BrandEnvironment.Resolve("WHISPER_PATH").Value);
        var model = FirstNonEmpty(
            request.WhisperModelPath,
            BrandEnvironment.Resolve("WHISPER_MODEL").Value,
            BrandEnvironment.Resolve("WHISPER_MODEL_PATH").Value);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
            if (File.Exists(configured))
            {
                return BuildLocalSpeechEngine(configured, model);
            }
        }

        foreach (var name in new[] { "whisper-cli.exe", "whisper-cli", "whisper.exe", "whisper" })
        {
            var candidate = FindExecutableOnPath(name);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return BuildLocalSpeechEngine(candidate, model);
            }
        }

        return null;
    }

    private static LocalSpeechEngine BuildLocalSpeechEngine(string executablePath, string? model)
    {
        var fileName = Path.GetFileName(executablePath);
        var kind = fileName.Contains("whisper-cli", StringComparison.OrdinalIgnoreCase)
            ? LocalSpeechEngineKind.WhisperCpp
            : LocalSpeechEngineKind.OpenAiWhisper;
        return new LocalSpeechEngine(executablePath, kind, model);
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
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

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string BuildProviderSpeechPrompt(string? language, string? providerPrompt)
    {
        var languageText = string.IsNullOrWhiteSpace(language)
            ? "Infer the spoken language."
            : $"The expected language is {language.Trim()}.";
        var customInstruction = string.IsNullOrWhiteSpace(providerPrompt)
            ? "Transcribe the speech accurately for a bug report or documentation workflow."
            : providerPrompt.Trim();

        return $$"""
        Process this GoatShot recording audio and return only a JSON object with this shape:
        {"transcript":"full transcript text","segments":[{"start":"0:00","end":"0:02","text":"spoken text"}]}

        Requirements:
        - Include approximate timestamps for each segment when speech timing is clear.
        - Use M:SS or H:MM:SS timestamp format.
        - Do not add facts that are not present in the audio.
        - Preserve uncertainty instead of guessing unclear speech.
        - {{languageText}}
        - {{customInstruction}}
        """;
    }

    internal static ProviderParsedTranscription ParseProviderTranscription(string providerText)
    {
        var srtSegments = ParseSrt(providerText);
        if (srtSegments.Count > 0)
        {
            return new ProviderParsedTranscription(srtSegments.ToList(), BuildTranscriptText(srtSegments));
        }

        var json = ExtractJsonObject(providerText);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var transcript = GetString(root, "transcript") ?? GetString(root, "text") ?? string.Empty;
                var segments = new List<TranscriptSegment>();
                if (root.TryGetProperty("segments", out var segmentsElement) &&
                    segmentsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var segment in segmentsElement.EnumerateArray())
                    {
                        var text = GetString(segment, "text") ?? GetString(segment, "transcript");
                        var startValue = GetString(segment, "start") ?? GetString(segment, "startTime") ?? GetString(segment, "timestamp");
                        var endValue = GetString(segment, "end") ?? GetString(segment, "endTime");
                        if (string.IsNullOrWhiteSpace(text) ||
                            !TryParseProviderTimestamp(startValue, out var start))
                        {
                            continue;
                        }

                        var end = TryParseProviderTimestamp(endValue, out var parsedEnd)
                            ? parsedEnd
                            : start;
                        segments.Add(new TranscriptSegment
                        {
                            Index = segments.Count + 1,
                            Start = start,
                            End = end < start ? start : end,
                            Text = text.Trim()
                        });
                    }
                }

                if (segments.Count > 0 && string.IsNullOrWhiteSpace(transcript))
                {
                    transcript = BuildTranscriptText(segments);
                }

                return new ProviderParsedTranscription(segments, transcript.Trim());
            }
            catch (JsonException)
            {
                // Fall through and store the provider text as an unsegmented transcript.
            }
        }

        return new ProviderParsedTranscription(new List<TranscriptSegment>(), CleanProviderText(providerText));
    }

    private static string? ExtractJsonObject(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : null;
    }

    private static string CleanProviderText(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                text = text[(firstNewline + 1)..lastFence].Trim();
            }
        }

        return text;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static bool TryParseProviderTimestamp(string? value, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim().TrimStart('[').TrimEnd(']').TrimEnd('s', 'S').Replace(',', '.');
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            time = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executablePath,
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
                return new ProcessResult(false, $"Could not start {executablePath}.");
            }

            var stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode == 0
                ? new ProcessResult(true, "Process completed.")
                : new ProcessResult(false, $"{Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}: {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(false, $"{Path.GetFileName(executablePath)} timed out after {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<bool> HasEmbeddedSubtitleStreamAsync(
        string ffmpeg,
        string videoPath,
        CancellationToken cancellationToken)
    {
        var ffprobe = VideoToolService.FindFfprobeForFfmpeg(ffmpeg);
        if (string.IsNullOrWhiteSpace(ffprobe))
        {
            return false;
        }

        var result = await RunProcessCaptureAsync(
            ffprobe,
            [
                "-v",
                "error",
                "-select_streams",
                "s:0",
                "-show_entries",
                "stream=codec_type",
                "-of",
                "csv=p=0",
                videoPath
            ],
            TimeSpan.FromSeconds(15),
            cancellationToken);
        return result.Succeeded &&
            result.Output.Contains("subtitle", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessCaptureResult> RunProcessCaptureAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executablePath,
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
                return new ProcessCaptureResult(false, string.Empty, $"Could not start {executablePath}.");
            }

            var stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode == 0
                ? new ProcessCaptureResult(true, stdout, "Process completed.")
                : new ProcessCaptureResult(false, stdout, $"{Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}: {RecordingService.ShortFfmpegMessage(stderr, stdout)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessCaptureResult(false, string.Empty, $"{Path.GetFileName(executablePath)} timed out after {timeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessCaptureResult(false, string.Empty, $"{ex.GetType().Name}: {ex.Message}");
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
            // Temporary transcription files are best-effort cleanup.
        }
    }

    private static string? ResolveSrtPath(string videoPath, string? srtPath)
    {
        if (!string.IsNullOrWhiteSpace(srtPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(srtPath));
        }

        var sidecar = Path.ChangeExtension(videoPath, ".srt");
        return File.Exists(sidecar) ? sidecar : null;
    }

    private static TranscriptionResult Failed(string message)
    {
        return new TranscriptionResult
        {
            Succeeded = false,
            Message = message
        };
    }

    [GeneratedRegex(@"(?<start>\d{2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(?<end>\d{2}:\d{2}:\d{2}[,.]\d{3})")]
    private static partial Regex TimingRegex();
}

public sealed record TranscriptionRequest(
    string VideoPath,
    string? SrtPath = null,
    string? OutputPath = null,
    string? WhisperExecutablePath = null,
    string? WhisperModelPath = null,
    string? Language = null,
    bool UseAiProvider = false,
    string? AiModelId = null,
    string? ProviderPrompt = null);

internal sealed record ProviderParsedTranscription(
    List<TranscriptSegment> Segments,
    string Text);

public enum LocalSpeechEngineKind
{
    OpenAiWhisper,
    WhisperCpp
}

public sealed record LocalSpeechEngine(
    string ExecutablePath,
    LocalSpeechEngineKind Kind,
    string? ModelPath)
{
    public string KindLabel => Kind == LocalSpeechEngineKind.WhisperCpp
        ? "whisper.cpp CLI"
        : "OpenAI Whisper CLI";
}

internal sealed record ProcessResult(bool Succeeded, string Message);

internal sealed record ProcessCaptureResult(bool Succeeded, string Output, string Message);

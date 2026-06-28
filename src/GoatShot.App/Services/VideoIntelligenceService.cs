using System.Net;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class VideoIntelligenceService
{
    private readonly AppPaths _paths;
    private readonly TranscriptionService _transcription;
    private readonly GeminiImageProvider? _gemini;

    public VideoIntelligenceService(
        AppPaths paths,
        TranscriptionService transcription,
        GeminiImageProvider? gemini = null)
    {
        _paths = paths;
        _transcription = transcription;
        _gemini = gemini;
    }

    public async Task<VideoIntelligenceResult> GenerateAsync(
        CaptureItem item,
        string? transcriptPath = null,
        string? srtPath = null,
        string? outputPath = null,
        string? format = null,
        string? whisperExecutablePath = null,
        string? whisperModelPath = null,
        string? language = null,
        bool useAiProvider = false,
        string? modelId = null,
        string? providerPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Video file not found: {item.FilePath}");
        }

        var segments = new List<TranscriptSegment>();
        var transcriptText = string.Empty;
        var usedProviderTranscription = false;
        var transcriptionProviderName = "Local";
        var transcriptionModelId = "local-transcription";
        var transcriptionMessage = "Transcript came from a local source.";
        if (!string.IsNullOrWhiteSpace(srtPath))
        {
            var transcript = await _transcription.ImportSrtAsync(srtPath, cancellationToken: cancellationToken);
            if (!transcript.Succeeded)
            {
                return Failed(transcript.Message);
            }

            segments = transcript.Segments;
            transcriptText = transcript.Text;
            transcriptionMessage = transcript.Message;
        }
        else if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            transcriptPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(transcriptPath));
            if (!File.Exists(transcriptPath))
            {
                return Failed($"Transcript file not found: {transcriptPath}");
            }

            transcriptText = await File.ReadAllTextAsync(transcriptPath, cancellationToken);
            transcriptionMessage = $"Transcript loaded from {transcriptPath}.";
        }
        else
        {
            var transcript = await _transcription.TranscribeAsync(
                new TranscriptionRequest(
                    item.FilePath,
                    SrtPath: null,
                    OutputPath: null,
                    WhisperExecutablePath: whisperExecutablePath,
                    WhisperModelPath: whisperModelPath,
                    Language: language,
                    UseAiProvider: useAiProvider,
                    AiModelId: modelId,
                    ProviderPrompt: providerPrompt),
                cancellationToken);
            if (!transcript.Succeeded)
            {
                return Failed(transcript.Message);
            }

            segments = transcript.Segments;
            transcriptText = transcript.Text;
            usedProviderTranscription = transcript.UsedAiProvider;
            transcriptionProviderName = transcript.ProviderName;
            transcriptionModelId = transcript.ModelId;
            transcriptionMessage = transcript.Message;
        }

        var title = BuildTitle(item);
        var summary = BuildSummary(item, transcriptText);
        var chapters = BuildChapters(segments, transcriptText);
        var usedAiProvider = false;
        var providerName = "Local";
        var resolvedModelId = "local-transcript-draft";
        var providerMessage = "Local deterministic transcript draft.";

        if (useAiProvider)
        {
            var provider = await TryGenerateProviderDraftAsync(
                item,
                transcriptText,
                segments,
                title,
                summary,
                chapters,
                modelId,
                providerPrompt,
                cancellationToken);
            title = provider.Title;
            summary = provider.Summary;
            chapters = provider.Chapters;
            usedAiProvider = provider.UsedAiProvider;
            providerName = provider.ProviderName;
            resolvedModelId = provider.ModelId;
            providerMessage = provider.Message;
        }

        var resolvedFormat = DocumentExportWriter.FormatFromPath(outputPath, format);
        var resolvedOutput = ResolveOutputPath(item, outputPath, resolvedFormat);
        var markdown = BuildMarkdown(
            item,
            title,
            summary,
            chapters,
            transcriptText,
            segments.Count,
            usedAiProvider,
            providerName,
            resolvedModelId,
            providerMessage,
            usedProviderTranscription,
            transcriptionProviderName,
            transcriptionModelId,
            transcriptionMessage);
        var html = DocumentExportWriter.MarkdownToSimpleHtml(title, markdown);
        await DocumentExportWriter.WriteAsync(resolvedOutput, resolvedFormat, title, markdown, html, cancellationToken);

        return new VideoIntelligenceResult
        {
            Succeeded = true,
            Message = usedAiProvider
                ? $"AI-assisted video intelligence draft exported as {resolvedFormat}. {providerMessage}"
                : $"Video intelligence draft exported as {resolvedFormat}. {providerMessage}",
            OutputPath = resolvedOutput,
            Title = title,
            Summary = summary,
            Chapters = chapters,
            UsedAiProvider = usedAiProvider,
            ProviderName = providerName,
            ModelId = resolvedModelId,
            ProviderMessage = providerMessage
        };
    }

    private async Task<ProviderVideoDraft> TryGenerateProviderDraftAsync(
        CaptureItem item,
        string transcriptText,
        IReadOnlyList<TranscriptSegment> segments,
        string localTitle,
        string localSummary,
        List<string> localChapters,
        string? modelId,
        string? providerPrompt,
        CancellationToken cancellationToken)
    {
        if (_gemini is null)
        {
            return ProviderVideoDraft.Local(
                localTitle,
                localSummary,
                localChapters,
                "Provider AI was requested, but Gemini is not configured in this service. Local draft used.");
        }

        var prompt = BuildProviderPrompt(item, transcriptText, segments, providerPrompt);
        var result = await _gemini.GenerateTextAsync(prompt, modelId ?? string.Empty, cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
        {
            return ProviderVideoDraft.Local(
                localTitle,
                localSummary,
                localChapters,
                $"Provider AI was requested, but local draft was used because {result.Message}");
        }

        var parsed = ParseProviderDraft(result.Text, localTitle, localSummary, localChapters);
        parsed.UsedAiProvider = true;
        parsed.ProviderName = "Gemini";
        parsed.ModelId = result.ModelId;
        parsed.Message = result.Message;
        return parsed;
    }

    private string ResolveOutputPath(CaptureItem item, string? outputPath, string format)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        var extension = DocumentExportWriter.ExtensionForFormat(format);
        return Path.Combine(
            _paths.DocumentsRoot,
            $"video-summary-{SafeFileName(Path.GetFileNameWithoutExtension(item.FileName))}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{extension}");
    }

    private static string BuildTitle(CaptureItem item)
    {
        var name = Path.GetFileNameWithoutExtension(item.FileName)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(name)
            ? $"GoatShot Recording {item.CreatedAt:yyyy-MM-dd HH:mm}"
            : CultureTitle(name);
    }

    private static string BuildSummary(CaptureItem item, string transcriptText)
    {
        var clean = NormalizeText(transcriptText);
        if (clean.Length == 0)
        {
            return $"Recording captured as {item.FileName}. Add a transcript or SRT file to generate a richer summary.";
        }

        var firstSentence = clean.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSentence))
        {
            firstSentence = clean.Length <= 240 ? clean : clean[..240].TrimEnd() + "...";
        }

        return $"This recording appears to cover: {firstSentence.TrimEnd('.')}.";
    }

    private static List<string> BuildChapters(IReadOnlyList<TranscriptSegment> segments, string transcriptText)
    {
        if (segments.Count > 0)
        {
            return segments
                .Where((_, index) => index % Math.Max(1, segments.Count / 6) == 0)
                .Take(8)
                .Select(segment => $"{TranscriptionService.FormatTimestamp(segment.Start)} - {Shorten(segment.Text, 80)}")
                .ToList();
        }

        var clean = NormalizeText(transcriptText);
        if (clean.Length == 0)
        {
            return ["0:00 - Recording overview"];
        }

        return clean
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(6)
            .Select((sentence, index) => $"{index}:00 - {Shorten(sentence, 80)}")
            .ToList();
    }

    private static string BuildMarkdown(
        CaptureItem item,
        string title,
        string summary,
        IReadOnlyList<string> chapters,
        string transcriptText,
        int segmentCount,
        bool usedAiProvider,
        string providerName,
        string modelId,
        string providerMessage,
        bool usedProviderTranscription,
        string transcriptionProviderName,
        string transcriptionModelId,
        string transcriptionMessage)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(summary);
        builder.AppendLine();
        builder.AppendLine("## Chapters");
        builder.AppendLine();
        foreach (var chapter in chapters)
        {
            builder.AppendLine("- " + chapter);
        }

        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        builder.AppendLine("- Video file: `" + item.FilePath + "`");
        builder.AppendLine("- Workspace item: " + item.Id);
        builder.AppendLine("- Captured at: " + item.CreatedAt.LocalDateTime.ToString("g"));
        builder.AppendLine("- Transcript segments: " + segmentCount);
        builder.AppendLine("- Transcription source: " + (usedProviderTranscription ? $"{transcriptionProviderName} ({transcriptionModelId})" : "Local transcript source"));
        builder.AppendLine("- Transcription status: " + transcriptionMessage);
        builder.AppendLine("- Draft source: " + (usedAiProvider ? $"{providerName} ({modelId})" : "Local deterministic transcript draft"));
        builder.AppendLine("- Provider status: " + providerMessage);
        builder.AppendLine();
        builder.AppendLine("## Transcript Context");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(Shorten(NormalizeText(transcriptText), 4_000));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine();
        if (usedProviderTranscription && usedAiProvider)
        {
            builder.AppendLine("This AI-assisted workflow sent extracted audio to the configured provider for speech-to-text and redacted transcript text for title/summary/chapter drafting. The video file itself was not uploaded by this workflow.");
        }
        else if (usedProviderTranscription)
        {
            builder.AppendLine("This workflow sent extracted audio to the configured provider for speech-to-text, then created the title/summary/chapter draft locally. The video file itself was not uploaded by this workflow.");
        }
        else if (usedAiProvider)
        {
            builder.AppendLine("This AI-assisted draft sent redacted transcript text to the configured provider. The video file itself was not uploaded by this workflow.");
        }
        else
        {
            builder.AppendLine("This is a local deterministic draft from provided transcript/SRT text. It does not upload media or transcript content to an AI provider.");
        }

        return builder.ToString();
    }

    private static string BuildProviderPrompt(
        CaptureItem item,
        string transcriptText,
        IReadOnlyList<TranscriptSegment> segments,
        string? providerPrompt)
    {
        var transcript = Shorten(NormalizeText(transcriptText), 8_000);
        var segmentHints = segments.Count == 0
            ? "No structured SRT segments were available."
            : string.Join(
                Environment.NewLine,
                segments.Take(16).Select(segment => $"{TranscriptionService.FormatTimestamp(segment.Start)} - {NormalizeText(segment.Text)}"));
        var customInstruction = string.IsNullOrWhiteSpace(providerPrompt)
            ? "Create a concise support-friendly recording summary."
            : providerPrompt.Trim();

        return $$"""
        You are drafting a GoatShot video intelligence report from redacted transcript text.
        Return only a JSON object with this shape:
        {"title":"short title","summary":"3-5 sentence summary","chapters":["0:00 - first chapter","0:30 - second chapter"]}

        Requirements:
        - Use timestamps from the segment hints when possible.
        - Do not claim access to the video pixels or audio beyond this transcript.
        - Keep sensitive values redacted if they appear redacted.
        - {{customInstruction}}

        Video file name: {{item.FileName}}

        Segment hints:
        {{segmentHints}}

        Transcript:
        {{transcript}}
        """;
    }

    private static ProviderVideoDraft ParseProviderDraft(
        string providerText,
        string localTitle,
        string localSummary,
        List<string> localChapters)
    {
        var json = ExtractJsonObject(providerText);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ProviderVideoDraftJson>(
                    json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (parsed is not null)
                {
                    var title = string.IsNullOrWhiteSpace(parsed.Title) ? localTitle : NormalizeText(parsed.Title);
                    var summary = string.IsNullOrWhiteSpace(parsed.Summary) ? localSummary : NormalizeText(parsed.Summary);
                    var chapters = parsed.Chapters?
                        .Select(NormalizeText)
                        .Where(chapter => !string.IsNullOrWhiteSpace(chapter))
                        .Take(12)
                        .ToList();

                    return new ProviderVideoDraft(
                        title,
                        summary,
                        chapters is { Count: > 0 } ? chapters : localChapters);
                }
            }
            catch (JsonException)
            {
                // Fall back to using the provider text as summary.
            }
        }

        var clean = NormalizeText(providerText);
        return new ProviderVideoDraft(
            localTitle,
            string.IsNullOrWhiteSpace(clean) ? localSummary : Shorten(clean, 1_200),
            localChapters);
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

    private static string NormalizeText(string? text)
    {
        return SensitiveTextDetector.Scan(text ?? string.Empty).RedactedText
            .ReplaceLineEndings(" ")
            .Trim();
    }

    private static string Shorten(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }

    private static string CultureTitle(string value)
    {
        return string.Join(
            " ",
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }

    private static VideoIntelligenceResult Failed(string message)
    {
        return new VideoIntelligenceResult
        {
            Succeeded = false,
            Message = message
        };
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private sealed class ProviderVideoDraftJson
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Chapters { get; set; } = new();
    }

    private sealed class ProviderVideoDraft
    {
        public ProviderVideoDraft(string title, string summary, List<string> chapters)
        {
            Title = title;
            Summary = summary;
            Chapters = chapters;
        }

        public string Title { get; set; }
        public string Summary { get; set; }
        public List<string> Chapters { get; set; }
        public bool UsedAiProvider { get; set; }
        public string ProviderName { get; set; } = "Local";
        public string ModelId { get; set; } = "local-transcript-draft";
        public string Message { get; set; } = "Local deterministic transcript draft.";

        public static ProviderVideoDraft Local(
            string title,
            string summary,
            List<string> chapters,
            string message)
        {
            return new ProviderVideoDraft(title, summary, chapters)
            {
                Message = message
            };
        }
    }
}

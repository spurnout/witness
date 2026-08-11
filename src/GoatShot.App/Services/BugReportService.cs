using System.Net;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class BugReportService
{
    private const int MaxIncludedTextChars = 4_000;

    private readonly AppPaths _paths;
    private readonly DiagnosticsService _diagnostics;

    public BugReportService(AppPaths paths, DiagnosticsService diagnostics)
    {
        _paths = paths;
        _diagnostics = diagnostics;
    }

    public async Task<BugReportExportResult> ExportAsync(
        CaptureItem item,
        string? outputPath = null,
        string? format = null,
        BugReportEnrichment? enrichment = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFormat = ResolveFormat(outputPath, format);
        var resolvedOutput = ResolveOutputPath(item, outputPath, resolvedFormat);

        var snapshot = _diagnostics.GetSnapshot();
        var markdown = BuildMarkdown(item, snapshot, enrichment);
        var html = BuildHtml(item, snapshot, enrichment);
        await DocumentExportWriter.WriteAsync(
            resolvedOutput,
            resolvedFormat,
            $"Bug Report - {item.FileName}",
            markdown,
            html,
            cancellationToken);

        return new BugReportExportResult
        {
            Succeeded = true,
            Path = resolvedOutput,
            Format = resolvedFormat,
            Message = $"Bug report exported as {resolvedFormat}."
        };
    }

    private string ResolveOutputPath(CaptureItem item, string? outputPath, string format)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        var extension = DocumentExportWriter.ExtensionForFormat(format);
        var fileName = SafeFileName($"bug-report-{item.CreatedAt:yyyyMMdd-HHmmss}-{Path.GetFileNameWithoutExtension(item.FileName)}{extension}");
        return Path.Combine(_paths.DocumentsRoot, fileName);
    }

    private static string ResolveFormat(string? outputPath, string? format)
    {
        return DocumentExportWriter.FormatFromPath(outputPath, format);
    }

    private static string BuildMarkdown(CaptureItem item, DiagnosticSnapshot snapshot, BugReportEnrichment? enrichment)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Bug Report - {item.FileName}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("- Title: [enter concise issue title]");
        builder.AppendLine("- Severity: [low / medium / high]");
        builder.AppendLine("- Capture type: " + item.Kind);
        builder.AppendLine("- Captured at: " + item.CreatedAt.LocalDateTime.ToString("g"));
        builder.AppendLine("- Source app: " + EmptyFallback(item.SourceApp, "unknown"));
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine("- OS: " + snapshot.OsDescription);
        builder.AppendLine("- Runtime: " + snapshot.RuntimeDescription);
        builder.AppendLine("- Capture engine: " + snapshot.CaptureEngine);
        builder.AppendLine("- Recording engine: " + snapshot.RecordingEngine);
        builder.AppendLine("- Encoder status: " + snapshot.EncoderStatus);
        builder.AppendLine();
        builder.AppendLine("## Steps To Reproduce");
        builder.AppendLine();
        builder.AppendLine("1. [describe the first action]");
        builder.AppendLine("2. [describe what happened next]");
        builder.AppendLine("3. [describe the failing action]");
        builder.AppendLine();
        builder.AppendLine("## Expected Result");
        builder.AppendLine();
        builder.AppendLine("[describe what should have happened]");
        builder.AppendLine();
        builder.AppendLine("## Actual Result");
        builder.AppendLine();
        builder.AppendLine("[describe what actually happened]");
        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        builder.AppendLine("- Attachment path: `" + item.FilePath + "`");
        builder.AppendLine("- Dimensions: " + item.SizeLabel);
        builder.AppendLine("- Bytes: " + item.BytesLabel);
        builder.AppendLine("- Bounds: " + (item.Bounds?.Display ?? "n/a"));
        builder.AppendLine("- Workspace item: " + item.Id);
        builder.AppendLine();
        AppendOptionalMarkdownSection(builder, "Redacted Notes", item.Notes);
        AppendOptionalMarkdownSection(builder, "Redacted OCR / Text Context", item.OcrText);
        AppendRecordingEnrichmentMarkdown(builder, enrichment);
        builder.AppendLine("## Privacy");
        builder.AppendLine();
        builder.AppendLine("Notes, OCR/text context, transcript previews, recording context, and AI history previews above are passed through Receipts sensitive-text redaction before export. The attached media file is referenced by path and is not modified by this report export.");
        return builder.ToString();
    }

    private static string BuildHtml(CaptureItem item, DiagnosticSnapshot snapshot, BugReportEnrichment? enrichment)
    {
        var notes = RedactedTextBlock(item.Notes);
        var ocr = RedactedTextBlock(item.OcrText);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>" + Html($"Bug Report - {item.FileName}") + "</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;max-width:920px;margin:32px auto;padding:0 20px;line-height:1.5;color:#102027;background:#f8fbfc}");
        builder.AppendLine("h1,h2{color:#06131a} code,pre{background:#e9f1f4;border-radius:6px} code{padding:2px 5px} pre{padding:12px;overflow:auto;white-space:pre-wrap}");
        builder.AppendLine(".meta{display:grid;grid-template-columns:180px 1fr;gap:6px 14px}.label{font-weight:700;color:#31515d}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<h1>" + Html($"Bug Report - {item.FileName}") + "</h1>");
        builder.AppendLine("<h2>Summary</h2>");
        builder.AppendLine("<div class=\"meta\">");
        AddMeta(builder, "Title", "[enter concise issue title]");
        AddMeta(builder, "Severity", "[low / medium / high]");
        AddMeta(builder, "Capture type", item.Kind.ToString());
        AddMeta(builder, "Captured at", item.CreatedAt.LocalDateTime.ToString("g"));
        AddMeta(builder, "Source app", EmptyFallback(item.SourceApp, "unknown"));
        builder.AppendLine("</div>");
        builder.AppendLine("<h2>Environment</h2>");
        builder.AppendLine("<div class=\"meta\">");
        AddMeta(builder, "OS", snapshot.OsDescription);
        AddMeta(builder, "Runtime", snapshot.RuntimeDescription);
        AddMeta(builder, "Capture engine", snapshot.CaptureEngine);
        AddMeta(builder, "Recording engine", snapshot.RecordingEngine);
        AddMeta(builder, "Encoder status", snapshot.EncoderStatus);
        builder.AppendLine("</div>");
        builder.AppendLine("<h2>Steps To Reproduce</h2>");
        builder.AppendLine("<ol><li>[describe the first action]</li><li>[describe what happened next]</li><li>[describe the failing action]</li></ol>");
        builder.AppendLine("<h2>Expected Result</h2><p>[describe what should have happened]</p>");
        builder.AppendLine("<h2>Actual Result</h2><p>[describe what actually happened]</p>");
        builder.AppendLine("<h2>Evidence</h2>");
        builder.AppendLine("<div class=\"meta\">");
        AddMeta(builder, "Attachment path", item.FilePath);
        AddMeta(builder, "Dimensions", item.SizeLabel);
        AddMeta(builder, "Bytes", item.BytesLabel);
        AddMeta(builder, "Bounds", item.Bounds?.Display ?? "n/a");
        AddMeta(builder, "Workspace item", item.Id);
        builder.AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(notes))
        {
            builder.AppendLine("<h2>Redacted Notes</h2><pre>" + Html(notes) + "</pre>");
        }

        if (!string.IsNullOrWhiteSpace(ocr))
        {
            builder.AppendLine("<h2>Redacted OCR / Text Context</h2><pre>" + Html(ocr) + "</pre>");
        }

        AppendRecordingEnrichmentHtml(builder, enrichment);
        builder.AppendLine("<h2>Privacy</h2>");
        builder.AppendLine("<p>Notes, OCR/text context, transcript previews, recording context, and AI history previews above are passed through Receipts sensitive-text redaction before export. The attached media file is referenced by path and is not modified by this report export.</p>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static void AppendRecordingEnrichmentMarkdown(StringBuilder builder, BugReportEnrichment? enrichment)
    {
        if (!HasEnrichment(enrichment))
        {
            return;
        }

        builder.AppendLine("## Recording Intelligence");
        builder.AppendLine();
        AppendPathBullet(builder, "Transcript", enrichment?.TranscriptPath);
        AppendPathBullet(builder, "SRT", enrichment?.SrtPath);
        AppendPathBullet(builder, "Video summary", enrichment?.VideoSummaryPath);

        if (enrichment?.KeyframePaths.Count > 0)
        {
            builder.AppendLine("- Keyframes:");
            foreach (var keyframe in enrichment.KeyframePaths.Where(File.Exists).Take(12))
            {
                builder.AppendLine("  - `" + keyframe + "`");
            }
        }

        AppendOptionalMarkdownSection(builder, "Redacted Recording Context", enrichment?.ContextNotes);
        AppendFilePreviewMarkdown(builder, "Redacted Transcript Preview", enrichment?.TranscriptPath);
        AppendAiHistoryMarkdown(builder, enrichment?.AiHistory);
    }

    private static void AppendRecordingEnrichmentHtml(StringBuilder builder, BugReportEnrichment? enrichment)
    {
        if (!HasEnrichment(enrichment))
        {
            return;
        }

        builder.AppendLine("<h2>Recording Intelligence</h2>");
        builder.AppendLine("<div class=\"meta\">");
        AddOptionalMeta(builder, "Transcript", enrichment?.TranscriptPath);
        AddOptionalMeta(builder, "SRT", enrichment?.SrtPath);
        AddOptionalMeta(builder, "Video summary", enrichment?.VideoSummaryPath);
        builder.AppendLine("</div>");

        var keyframes = enrichment?.KeyframePaths.Where(File.Exists).Take(12).ToList() ?? new List<string>();
        if (keyframes.Count > 0)
        {
            builder.AppendLine("<h3>Keyframes</h3><ul>");
            foreach (var keyframe in keyframes)
            {
                builder.AppendLine("<li><code>" + Html(keyframe) + "</code></li>");
            }

            builder.AppendLine("</ul>");
        }

        var context = RedactedTextBlock(enrichment?.ContextNotes);
        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine("<h3>Redacted Recording Context</h3><pre>" + Html(context) + "</pre>");
        }

        var transcriptPreview = RedactedFilePreview(enrichment?.TranscriptPath);
        if (!string.IsNullOrWhiteSpace(transcriptPreview))
        {
            builder.AppendLine("<h3>Redacted Transcript Preview</h3><pre>" + Html(transcriptPreview) + "</pre>");
        }

        var entries = enrichment?.AiHistory.Take(12).ToList() ?? new List<AiActionHistoryEntry>();
        if (entries.Count > 0)
        {
            builder.AppendLine("<h3>AI Review State</h3><ul>");
            foreach (var entry in entries)
            {
                builder.AppendLine("<li>" +
                    Html($"{entry.CreatedAt.LocalDateTime:g} {entry.Action} {entry.ReviewStatus} {entry.ModelId}: {entry.Message}") +
                    "</li>");
            }

            builder.AppendLine("</ul>");
        }
    }

    private static void AppendOptionalMarkdownSection(StringBuilder builder, string title, string? text)
    {
        var redacted = RedactedTextBlock(text);
        if (string.IsNullOrWhiteSpace(redacted))
        {
            return;
        }

        builder.AppendLine("## " + title);
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(redacted);
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static string RedactedTextBlock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Redact(text.Trim());
        if (redacted.Length <= MaxIncludedTextChars)
        {
            return redacted;
        }

        return redacted[..MaxIncludedTextChars] + Environment.NewLine + "[TRUNCATED]";
    }

    private static void AppendPathBullet(StringBuilder builder, string label, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            builder.AppendLine("- " + label + ": `" + path + "`");
        }
    }

    private static void AppendFilePreviewMarkdown(StringBuilder builder, string title, string? path)
    {
        var preview = RedactedFilePreview(path);
        if (!string.IsNullOrWhiteSpace(preview))
        {
            AppendOptionalMarkdownSection(builder, title, preview);
        }
    }

    private static void AppendAiHistoryMarkdown(StringBuilder builder, IReadOnlyList<AiActionHistoryEntry>? entries)
    {
        if (entries is not { Count: > 0 })
        {
            return;
        }

        builder.AppendLine("## AI Review State");
        builder.AppendLine();
        foreach (var entry in entries.Take(12))
        {
            builder.AppendLine("- " +
                $"{entry.CreatedAt.LocalDateTime:g} {entry.Action} {entry.ReviewStatus} {entry.ModelId}: " +
                SensitiveTextDetector.Redact(entry.Message));
        }

        builder.AppendLine();
    }

    private static string RedactedFilePreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return RedactedTextBlock(File.ReadAllText(path));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasEnrichment(BugReportEnrichment? enrichment)
    {
        return enrichment is not null &&
            (!string.IsNullOrWhiteSpace(enrichment.TranscriptPath) ||
             !string.IsNullOrWhiteSpace(enrichment.SrtPath) ||
             !string.IsNullOrWhiteSpace(enrichment.VideoSummaryPath) ||
             !string.IsNullOrWhiteSpace(enrichment.ContextNotes) ||
             enrichment.KeyframePaths.Count > 0 ||
             enrichment.AiHistory.Count > 0);
    }

    private static void AddOptionalMeta(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            AddMeta(builder, label, value);
        }
    }

    private static void AddMeta(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("<div class=\"label\">" + Html(label) + "</div><div>" + Html(value) + "</div>");
    }

    private static string EmptyFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : SensitiveTextDetector.Redact(value);
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}

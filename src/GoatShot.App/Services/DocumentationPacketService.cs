using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class DocumentationPacketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly AiActionHistoryService _aiHistory;
    private readonly BugReportService _bugReports;

    public DocumentationPacketService(
        AppPaths paths,
        AiActionHistoryService aiHistory,
        BugReportService bugReports)
    {
        _paths = paths;
        _aiHistory = aiHistory;
        _bugReports = bugReports;
    }

    public async Task<DocumentationPacketResult> CreateAsync(
        DocumentationPacketRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Item is null || string.IsNullOrWhiteSpace(request.Item.FilePath))
        {
            return Failed("A source capture or recording item is required.");
        }

        var item = request.Item;
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Source media file not found: {item.FilePath}");
        }

        var packetDirectory = ResolvePacketDirectory(item, request.OutputDirectory);
        Directory.CreateDirectory(packetDirectory);

        var transcriptPath = ResolveOptionalPath(request.TranscriptPath);
        var srtPath = ResolveOptionalPath(request.SrtPath);
        var videoSummaryPath = ResolveOptionalPath(request.VideoSummaryPath);
        var keyframes = request.KeyframePaths
            .Select(ResolveOptionalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var relatedHistory = FindRelatedHistory(item);

        var enrichment = new BugReportEnrichment
        {
            TranscriptPath = transcriptPath,
            SrtPath = srtPath,
            VideoSummaryPath = videoSummaryPath,
            KeyframePaths = keyframes,
            ContextNotes = request.ContextNotes,
            AiHistory = relatedHistory.ToList()
        };

        var bugReportPath = ResolveOptionalPath(request.BugReportPath);
        if (request.GenerateBugReport)
        {
            var bugFormat = string.IsNullOrWhiteSpace(request.BugReportFormat)
                ? "markdown"
                : request.BugReportFormat;
            bugReportPath = Path.Combine(
                packetDirectory,
                "bug-report" + DocumentExportWriter.ExtensionForFormat(bugFormat));
            var bugResult = await _bugReports.ExportAsync(
                item,
                bugReportPath,
                bugFormat,
                enrichment,
                cancellationToken);
            if (!bugResult.Succeeded)
            {
                return Failed($"Documentation packet bug report export failed: {bugResult.Message}");
            }
        }

        var files = BuildFiles(
            item,
            transcriptPath,
            srtPath,
            videoSummaryPath,
            bugReportPath,
            keyframes);
        var manifest = new DocumentationPacketManifest
        {
            SchemaVersion = 1,
            CreatedAt = DateTimeOffset.Now,
            PacketDirectory = packetDirectory,
            Source = DocumentationPacketSource.From(item),
            Files = files,
            TranscriptPreview = RedactedFilePreview(transcriptPath),
            ContextPreview = RedactedAndLimit(request.ContextNotes, 2_000),
            AiHistory = relatedHistory.Select(DocumentationPacketAiHistoryEntry.From).ToList(),
            Privacy = "Manifest previews are redacted and truncated. Linked media files are referenced by path and are not copied or modified."
        };

        var manifestPath = Path.Combine(packetDirectory, "manifest.json");
        var indexPath = Path.Combine(packetDirectory, "README.md");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(indexPath, BuildIndexMarkdown(manifest), Encoding.UTF8, cancellationToken);

        return new DocumentationPacketResult
        {
            Succeeded = true,
            Message = $"Documentation packet created with {files.Count} linked file(s).",
            PacketDirectory = packetDirectory,
            ManifestPath = manifestPath,
            IndexPath = indexPath,
            BugReportPath = bugReportPath,
            LinkedFiles = files.Where(file => file.Exists).Select(file => file.Path).ToList()
        };
    }

    private IReadOnlyList<AiActionHistoryEntry> FindRelatedHistory(CaptureItem item)
    {
        return _aiHistory.Load(500)
            .Where(entry =>
                entry.CaptureItemId.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                entry.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                entry.FileName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(25)
            .ToList();
    }

    private string ResolvePacketDirectory(CaptureItem item, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        }

        var name = SafeFileName(Path.GetFileNameWithoutExtension(item.FileName));
        return Path.Combine(
            _paths.DocumentsRoot,
            $"documentation-packet-{name}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    }

    private static List<DocumentationPacketFile> BuildFiles(
        CaptureItem item,
        string? transcriptPath,
        string? srtPath,
        string? videoSummaryPath,
        string? bugReportPath,
        IReadOnlyList<string> keyframePaths)
    {
        var files = new List<DocumentationPacketFile>
        {
            DocumentationPacketFile.From("source-media", item.FilePath)
        };
        AddOptional(files, "transcript", transcriptPath);
        AddOptional(files, "srt", srtPath);
        AddOptional(files, "video-summary", videoSummaryPath);
        AddOptional(files, "bug-report", bugReportPath);
        foreach (var keyframe in keyframePaths)
        {
            AddOptional(files, "keyframe", keyframe);
        }

        return files;
    }

    private static void AddOptional(List<DocumentationPacketFile> files, string role, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            files.Add(DocumentationPacketFile.From(role, path));
        }
    }

    private static string? ResolveOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string BuildIndexMarkdown(DocumentationPacketManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Documentation Packet - {manifest.Source.FileName}");
        builder.AppendLine();
        builder.AppendLine("- Created: " + manifest.CreatedAt.LocalDateTime.ToString("g"));
        builder.AppendLine("- Source: `" + manifest.Source.FilePath + "`");
        builder.AppendLine("- Workspace item: " + manifest.Source.Id);
        builder.AppendLine("- Capture kind: " + manifest.Source.Kind);
        builder.AppendLine();
        builder.AppendLine("## Linked Files");
        builder.AppendLine();
        foreach (var file in manifest.Files)
        {
            builder.AppendLine($"- {file.Role}: `{file.Path}` ({(file.Exists ? $"{file.Bytes} bytes" : "missing")})");
        }

        if (!string.IsNullOrWhiteSpace(manifest.TranscriptPreview))
        {
            builder.AppendLine();
            builder.AppendLine("## Redacted Transcript Preview");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(manifest.TranscriptPreview);
            builder.AppendLine("```");
        }

        if (!string.IsNullOrWhiteSpace(manifest.ContextPreview))
        {
            builder.AppendLine();
            builder.AppendLine("## Redacted Context Preview");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(manifest.ContextPreview);
            builder.AppendLine("```");
        }

        if (manifest.AiHistory.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## AI Review State");
            builder.AppendLine();
            foreach (var entry in manifest.AiHistory)
            {
                builder.AppendLine($"- {entry.CreatedAt.LocalDateTime:g} {entry.Action} {entry.ReviewStatus} {entry.ModelId}: {entry.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine();
        builder.AppendLine(manifest.Privacy);
        return builder.ToString();
    }

    private static string RedactedFilePreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return RedactedAndLimit(File.ReadAllText(path), 4_000);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string RedactedAndLimit(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Scan(text).RedactedText.ReplaceLineEndings(Environment.NewLine).Trim();
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + Environment.NewLine + "[TRUNCATED]";
    }

    private static string Sha256OrEmpty(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "capture" : safe;
    }

    private static DocumentationPacketResult Failed(string message)
    {
        return new DocumentationPacketResult
        {
            Succeeded = false,
            Message = message
        };
    }

    private sealed class DocumentationPacketManifest
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string PacketDirectory { get; set; } = string.Empty;
        public DocumentationPacketSource Source { get; set; } = new();
        public List<DocumentationPacketFile> Files { get; set; } = new();
        public string TranscriptPreview { get; set; } = string.Empty;
        public string ContextPreview { get; set; } = string.Empty;
        public List<DocumentationPacketAiHistoryEntry> AiHistory { get; set; } = new();
        public string Privacy { get; set; } = string.Empty;
    }

    private sealed class DocumentationPacketSource
    {
        public string Id { get; set; } = string.Empty;
        public CaptureKind Kind { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public long Bytes { get; set; }
        public string SourceApp { get; set; } = string.Empty;
        public string SourceWindowTitle { get; set; } = string.Empty;

        public static DocumentationPacketSource From(CaptureItem item)
        {
            return new DocumentationPacketSource
            {
                Id = item.Id,
                Kind = item.Kind,
                FileName = item.FileName,
                FilePath = item.FilePath,
                CreatedAt = item.CreatedAt,
                Bytes = item.Bytes,
                SourceApp = SensitiveTextDetector.Redact(item.SourceApp ?? string.Empty),
                SourceWindowTitle = SensitiveTextDetector.Redact(item.SourceWindowTitle ?? string.Empty)
            };
        }
    }

    private sealed class DocumentationPacketFile
    {
        public string Role { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;

        public static DocumentationPacketFile From(string role, string path)
        {
            var exists = File.Exists(path);
            return new DocumentationPacketFile
            {
                Role = role,
                Path = path,
                Exists = exists,
                Bytes = exists ? new FileInfo(path).Length : 0,
                Sha256 = exists ? Sha256OrEmpty(path) : string.Empty
            };
        }
    }

    private sealed class DocumentationPacketAiHistoryEntry
    {
        public string Id { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public AiActionKind Action { get; set; }
        public string ModelId { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public AiActionReviewStatus ReviewStatus { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public string? ParentEntryId { get; set; }

        public static DocumentationPacketAiHistoryEntry From(AiActionHistoryEntry entry)
        {
            return new DocumentationPacketAiHistoryEntry
            {
                Id = entry.Id,
                CreatedAt = entry.CreatedAt,
                Action = entry.Action,
                ModelId = entry.ModelId,
                Succeeded = entry.Succeeded,
                ReviewStatus = entry.ReviewStatus,
                Message = SensitiveTextDetector.Redact(entry.Message),
                OutputPath = entry.OutputPath,
                ParentEntryId = entry.ParentEntryId
            };
        }
    }
}

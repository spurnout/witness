using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GoatShot.App.Services;

public static partial class DocumentExportWriter
{
    public static async Task WriteAsync(
        string path,
        string format,
        string title,
        string markdown,
        string html,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        switch (NormalizeFormat(format))
        {
            case "html":
                await File.WriteAllTextAsync(path, html, Encoding.UTF8, cancellationToken);
                break;
            case "pdf":
                await File.WriteAllBytesAsync(path, BuildPdfBytes(title, MarkdownToPlainText(markdown)), cancellationToken);
                break;
            case "docx":
                await WriteDocxAsync(path, title, markdown, cancellationToken);
                break;
            default:
                await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken);
                break;
        }
    }

    public static string NormalizeFormat(string? format)
    {
        return (format ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "html" or "htm" => "html",
            "pdf" => "pdf",
            "docx" or "word" => "docx",
            _ => "markdown"
        };
    }

    public static string FormatFromPath(string? path, string? requestedFormat = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedFormat))
        {
            return NormalizeFormat(requestedFormat);
        }

        return Path.GetExtension(path ?? string.Empty).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "html",
            ".pdf" => "pdf",
            ".docx" => "docx",
            _ => "markdown"
        };
    }

    public static string ExtensionForFormat(string format)
    {
        return NormalizeFormat(format) switch
        {
            "html" => ".html",
            "pdf" => ".pdf",
            "docx" => ".docx",
            _ => ".md"
        };
    }

    public static string MarkdownToPlainText(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var text = line.TrimEnd();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                text = text.TrimStart('#').Trim();
            }

            if (text.StartsWith("- ", StringComparison.Ordinal))
            {
                text = "* " + text[2..];
            }

            text = InlineCodeRegex().Replace(text, "$1");
            builder.AppendLine(text);
        }

        return builder.ToString().Trim();
    }

    public static string MarkdownToSimpleHtml(string title, string markdown)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>" + Html(title) + "</title>");
        builder.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:920px;margin:32px auto;padding:0 20px;line-height:1.5;color:#102027;background:#f8fbfc}h1,h2,h3{color:#06131a}code,pre{background:#e9f1f4;border-radius:6px}code{padding:2px 5px}pre{padding:12px;overflow:auto;white-space:pre-wrap}</style>");
        builder.AppendLine("</head><body>");

        var inList = false;
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (inList)
                {
                    builder.AppendLine("</ul>");
                    inList = false;
                }

                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseList(builder, ref inList);
                builder.AppendLine("<h3>" + Html(line[4..]) + "</h3>");
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseList(builder, ref inList);
                builder.AppendLine("<h2>" + Html(line[3..]) + "</h2>");
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                CloseList(builder, ref inList);
                builder.AppendLine("<h1>" + Html(line[2..]) + "</h1>");
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    builder.AppendLine("<ul>");
                    inList = true;
                }

                builder.AppendLine("<li>" + Html(line[2..]) + "</li>");
            }
            else
            {
                CloseList(builder, ref inList);
                builder.AppendLine("<p>" + Html(line) + "</p>");
            }
        }

        CloseList(builder, ref inList);
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static async Task WriteDocxAsync(
        string path,
        string title,
        string markdown,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        await WriteZipEntryAsync(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """,
            cancellationToken);
        await WriteZipEntryAsync(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """,
            cancellationToken);
        await WriteZipEntryAsync(
            archive,
            "word/document.xml",
            BuildDocumentXml(title, MarkdownToPlainText(markdown)),
            cancellationToken);
    }

    private static async Task WriteZipEntryAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string BuildDocumentXml(string title, string plainText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.AppendLine("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        AppendParagraph(builder, title, bold: true);
        foreach (var line in plainText.ReplaceLineEndings("\n").Split('\n'))
        {
            AppendParagraph(builder, line, bold: false);
        }

        builder.AppendLine("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>");
        builder.AppendLine("</w:body></w:document>");
        return builder.ToString();
    }

    private static void AppendParagraph(StringBuilder builder, string text, bool bold)
    {
        builder.Append("<w:p><w:r>");
        if (bold)
        {
            builder.Append("<w:rPr><w:b/></w:rPr>");
        }

        builder.Append("<w:t xml:space=\"preserve\">");
        builder.Append(WebUtility.HtmlEncode(text));
        builder.AppendLine("</w:t></w:r></w:p>");
    }

    private static byte[] BuildPdfBytes(string title, string text)
    {
        var safeLines = WrapLines((title + Environment.NewLine + Environment.NewLine + text).ReplaceLineEndings("\n"), 86)
            .Take(52)
            .ToList();
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 10 Tf");
        content.AppendLine("50 770 Td");
        foreach (var line in safeLines)
        {
            content.AppendLine($"({EscapePdf(line)}) Tj");
            content.AppendLine("0 -14 Td");
        }

        content.AppendLine("ET");
        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"),
            Encoding.ASCII.GetBytes("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"),
            Encoding.ASCII.GetBytes("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n"),
            Encoding.ASCII.GetBytes("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"),
            Encoding.ASCII.GetBytes($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n{content}endstream\nendobj\n")
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n"));
        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(stream.Position);
            writer.Write(obj);
        }

        var xrefOffset = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes($"xref\n0 {objects.Count + 1}\n"));
        writer.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1))
        {
            writer.Write(Encoding.ASCII.GetBytes($"{offset:0000000000} 00000 n \n"));
        }

        writer.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));
        writer.Flush();
        return stream.ToArray();
    }

    private static IEnumerable<string> WrapLines(string text, int maxLength)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            while (line.Length > maxLength)
            {
                var split = line.LastIndexOf(' ', Math.Min(maxLength, line.Length - 1));
                if (split <= 0)
                {
                    split = maxLength;
                }

                yield return line[..split].TrimEnd();
                line = line[split..].TrimStart();
            }

            yield return line;
        }
    }

    private static string EscapePdf(string value)
    {
        return new string(value.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray())
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static void CloseList(StringBuilder builder, ref bool inList)
    {
        if (!inList)
        {
            return;
        }

        builder.AppendLine("</ul>");
        inList = false;
    }

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();
}

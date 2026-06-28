using System.Text;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static partial class FileNameTemplateService
{
    public static string Render(string template, CapturedBitmap captured, int counter)
    {
        var now = DateTimeOffset.Now;
        var source = captured.Source;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = now.ToString("yyyy-MM-dd"),
            ["time"] = now.ToString("HHmmss"),
            ["datetime"] = now.ToString("yyyy-MM-dd-HHmmss"),
            ["capture_type"] = captured.Kind.ToString().ToLowerInvariant(),
            ["app"] = source?.ProcessName ?? "unknown",
            ["process"] = source?.ProcessName ?? "unknown",
            ["window_title"] = source?.WindowTitle ?? "untitled",
            ["monitor"] = source?.MonitorName ?? "monitor",
            ["width"] = captured.Bounds.Width.ToString(),
            ["height"] = captured.Bounds.Height.ToString(),
            ["counter"] = counter.ToString("0000"),
            ["project"] = "default"
        };

        var rendered = TokenRegex().Replace(
            string.IsNullOrWhiteSpace(template) ? "{datetime}-{capture_type}-{counter}" : template,
            match => values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);

        return SanitizeFileName(rendered);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '-' : ch);
        }

        var sanitized = WhitespaceRegex().Replace(builder.ToString().Trim(), "-");
        sanitized = DashRegex().Replace(sanitized, "-").Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? $"capture-{DateTimeOffset.Now:yyyyMMdd-HHmmss}" : sanitized;
    }

    [GeneratedRegex(@"\{(?<name>[a-zA-Z0-9_]+)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex DashRegex();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static partial class FileNameTemplateService
{
    /// <summary>
    /// Upper bound for the rendered stem. Tokens such as {window_title} are attacker- or
    /// accident-controlled and can run to hundreds of characters; the app manifest does not opt into
    /// long paths, so an uncapped stem overflows MAX_PATH and the capture is lost. This leaves room
    /// for the library root, the "-9999" uniqueness suffix, and the extension.
    /// </summary>
    public const int MaxRenderedLength = 120;

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public static string Render(string template, CapturedBitmap captured, int counter)
    {
        var now = DateTimeOffset.Now;
        var source = captured.Source;

        // InvariantCulture throughout: the current culture's calendar would render {date} as a
        // Buddhist or Hijri year on some systems, which breaks both sorting and the documented format.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = now.ToString("HHmmss", CultureInfo.InvariantCulture),
            ["datetime"] = now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture),
            ["capture_type"] = captured.Kind.ToString().ToLowerInvariant(),
            ["app"] = source?.ProcessName ?? "unknown",
            ["process"] = source?.ProcessName ?? "unknown",
            ["window_title"] = source?.WindowTitle ?? "untitled",
            ["monitor"] = source?.MonitorName ?? "monitor",
            ["width"] = captured.Bounds.Width.ToString(CultureInfo.InvariantCulture),
            ["height"] = captured.Bounds.Height.ToString(CultureInfo.InvariantCulture),
            ["counter"] = counter.ToString("0000", CultureInfo.InvariantCulture),
            ["project"] = "default"
        };

        var rendered = TokenRegex().Replace(
            string.IsNullOrWhiteSpace(template) ? "{datetime}-{capture_type}-{counter}" : template,
            match => values.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);

        return SanitizeFileName(rendered);
    }

    /// <summary>
    /// True when a name resolves to a legacy Windows character device. Writing to "NUL.png" targets
    /// the device rather than a file, so the bytes vanish without an error.
    /// </summary>
    public static bool IsReservedDeviceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var stem = value.AsSpan();
        var dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        foreach (var reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

        if (sanitized.Length > MaxRenderedLength)
        {
            sanitized = sanitized[..MaxRenderedLength].Trim('-', '.');
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return $"capture-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        }

        return IsReservedDeviceName(sanitized) ? $"{sanitized}-capture" : sanitized;
    }

    [GeneratedRegex(@"\{(?<name>[a-zA-Z0-9_]+)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex DashRegex();
}

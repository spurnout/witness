using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoatShot.App.Services;

internal static class ShareProviderPayloads
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static StringContent JsonContent(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value, CompactJsonOptions), Encoding.UTF8, "application/json");
    }

    public static string ExpandMessage(string? template, ShareUploadRequest request, string providerName)
    {
        var resolved = string.IsNullOrWhiteSpace(template)
            ? "GoatShot capture ready: {file} ({bytes} bytes)"
            : template.Trim();

        return resolved
            .Replace("{provider}", providerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{file}", MetadataValue(request, "fileName", Path.GetFileName(request.FilePath)), StringComparison.OrdinalIgnoreCase)
            .Replace("{path}", request.FilePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", MetadataValue(request, "id"), StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", request.CaptureType, StringComparison.OrdinalIgnoreCase)
            .Replace("{bytes}", MetadataValue(request, "bytes"), StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", MetadataValue(request, "width"), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", MetadataValue(request, "height"), StringComparison.OrdinalIgnoreCase)
            .Replace("{created_at}", MetadataValue(request, "createdAt"), StringComparison.OrdinalIgnoreCase);
    }

    public static string DetectContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    public static string? FirstUrl(string text)
    {
        var match = Regex.Match(text, @"https?://[^\s""'<>]+");
        return match.Success ? match.Value : null;
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}

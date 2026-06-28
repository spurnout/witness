using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class FileInspectorService
{
    public async Task<FileInspectionResult> InspectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(filePath));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File not found.", fullPath);
        }

        var info = new FileInfo(fullPath);
        return new FileInspectionResult
        {
            FilePath = fullPath,
            FileName = info.Name,
            Extension = info.Extension,
            Bytes = info.Length,
            BytesLabel = FormatBytes(info.Length),
            CreatedAt = info.CreationTime,
            ModifiedAt = info.LastWriteTime,
            Hashes = await ComputeHashesAsync(fullPath, cancellationToken),
            Image = TryReadImageMetadata(fullPath)
        };
    }

    public static string FormatReport(FileInspectionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"File: {result.FilePath}");
        builder.AppendLine($"Size: {result.BytesLabel} ({result.Bytes} bytes)");
        builder.AppendLine($"Created: {result.CreatedAt.LocalDateTime:g}");
        builder.AppendLine($"Modified: {result.ModifiedAt.LocalDateTime:g}");
        builder.AppendLine($"Extension: {(string.IsNullOrWhiteSpace(result.Extension) ? "n/a" : result.Extension)}");
        builder.AppendLine();
        builder.AppendLine("Hashes:");
        foreach (var hash in result.Hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"{hash.Key.ToUpperInvariant()}: {hash.Value}");
        }

        if (result.Image is null)
        {
            builder.AppendLine();
            builder.AppendLine("Image metadata: not an image or unreadable by Windows imaging.");
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine();
        builder.AppendLine("Image metadata:");
        builder.AppendLine($"Dimensions: {result.Image.Width} x {result.Image.Height}");
        builder.AppendLine($"Format: {result.Image.Format}");
        builder.AppendLine($"Pixel format: {result.Image.PixelFormat}");
        builder.AppendLine($"DPI: {result.Image.HorizontalDpi:0.##} x {result.Image.VerticalDpi:0.##}");
        builder.AppendLine($"Metadata properties: {result.Image.PropertyCount}");

        if (result.Image.Properties.Count > 0)
        {
            builder.AppendLine("Property IDs:");
            foreach (var property in result.Image.Properties)
            {
                builder.AppendLine($"- {property.Id}, type {property.Type}, {property.Length} bytes");
            }

            if (result.Image.PropertyListTruncated)
            {
                builder.AppendLine("- ...");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<Dictionary<string, string>> ComputeHashesAsync(string path, CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        var buffer = new byte[1024 * 128];
        await using var stream = File.OpenRead(path);
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read);
            sha256.AppendData(chunk);
            sha1.AppendData(chunk);
            md5.AppendData(chunk);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sha256"] = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
            ["sha1"] = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
            ["md5"] = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant()
        };
    }

    private static FileImageMetadata? TryReadImageMetadata(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            var properties = SafePropertyItems(image);
            return new FileImageMetadata
            {
                Width = image.Width,
                Height = image.Height,
                Format = FormatName(image.RawFormat),
                PixelFormat = image.PixelFormat.ToString(),
                HorizontalDpi = image.HorizontalResolution,
                VerticalDpi = image.VerticalResolution,
                PropertyCount = properties.Count,
                PropertyListTruncated = properties.Count > 40,
                Properties = properties
                    .Take(40)
                    .Select(property => new FileImageProperty
                    {
                        Id = $"0x{property.Id:X4}",
                        Type = property.Type,
                        Length = property.Len
                    })
                    .ToList()
            };
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<PropertyItem> SafePropertyItems(Image image)
    {
        try
        {
            return image.PropertyItems;
        }
        catch
        {
            return Array.Empty<PropertyItem>();
        }
    }

    private static string FormatName(ImageFormat format)
    {
        if (format.Guid == ImageFormat.Png.Guid)
        {
            return "PNG";
        }

        if (format.Guid == ImageFormat.Jpeg.Guid)
        {
            return "JPEG";
        }

        if (format.Guid == ImageFormat.Bmp.Guid)
        {
            return "BMP";
        }

        if (format.Guid == ImageFormat.Gif.Guid)
        {
            return "GIF";
        }

        if (format.Guid == ImageFormat.Tiff.Guid)
        {
            return "TIFF";
        }

        if (format.Guid == ImageFormat.Icon.Guid)
        {
            return "ICO";
        }

        return format.Guid.ToString("N");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024 * 1024
            ? $"{bytes / 1024d:0.#} KB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }
}

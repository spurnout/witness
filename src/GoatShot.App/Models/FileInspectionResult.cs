namespace GoatShot.App.Models;

public sealed class FileInspectionResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string BytesLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public FileImageMetadata? Image { get; set; }
}

public sealed class FileImageMetadata
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public string PixelFormat { get; set; } = string.Empty;
    public float HorizontalDpi { get; set; }
    public float VerticalDpi { get; set; }
    public int PropertyCount { get; set; }
    public bool PropertyListTruncated { get; set; }
    public List<FileImageProperty> Properties { get; set; } = new();
}

public sealed class FileImageProperty
{
    public string Id { get; set; } = string.Empty;
    public short Type { get; set; }
    public int Length { get; set; }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WorkspaceStore
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".avi",
        ".mkv",
        ".webm"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private WorkspaceMetadataIndex? _metadataIndex;

    public WorkspaceStore(AppPaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public void AttachMetadataIndex(WorkspaceMetadataIndex metadataIndex)
    {
        _metadataIndex = metadataIndex;
    }

    public IReadOnlyList<CaptureItem> Load()
    {
        if (!File.Exists(_paths.IndexPath))
        {
            return Array.Empty<CaptureItem>();
        }

        try
        {
            var json = File.ReadAllText(_paths.IndexPath);
            var items = JsonSerializer.Deserialize<List<CaptureItem>>(json, JsonOptions) ?? new List<CaptureItem>();
            return items
                .OrderByDescending(item => item.CreatedAt)
                .ToList();
        }
        catch
        {
            return Array.Empty<CaptureItem>();
        }
    }

    public async Task<CaptureItem> SaveCaptureAsync(
        CapturedBitmap captured,
        bool privateCapture,
        string? hotkeyProfile = null)
    {
        return await Task.Run(() =>
        {
            var now = DateTimeOffset.Now;
            var id = FileNameTemplateService.Render(_settings.FileNameTemplate, captured, NextCounter());
            var root = privateCapture ? _paths.TempRoot : _paths.ImagesRoot;
            Directory.CreateDirectory(root);

            var path = MakeUniquePath(Path.Combine(root, $"{id}.png"));
            captured.Bitmap.Save(path, ImageFormat.Png);

            var item = BuildItem(path, captured.Kind, captured.Bounds, privateCapture, captured.Source, hotkeyProfile);
            if (!privateCapture)
            {
                AddToIndex(item);
            }

            return item;
        });
    }

    public async Task<CaptureItem> AddImageFileAsync(
        string path,
        CaptureKind kind,
        string? notes = null,
        CaptureSource? source = null,
        string? hotkeyProfile = null)
    {
        return await Task.Run(() =>
        {
            var item = BuildItem(path, kind, null, privateCapture: false, source, hotkeyProfile);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                item.Notes = notes;
            }

            AddToIndex(item);
            return item;
        });
    }

    public async Task<CaptureItem> ImportFileCopyAsync(
        string sourcePath,
        string? notes = null,
        CaptureKind? kind = null,
        CaptureSource? source = null)
    {
        return await Task.Run(async () =>
        {
            var extension = Path.GetExtension(sourcePath);
            var root = extension switch
            {
                _ when ImageExtensions.Contains(extension) => _paths.ImagesRoot,
                _ when VideoExtensions.Contains(extension) => _paths.VideosRoot,
                _ when DocumentExtensions.Contains(extension) => _paths.DocumentsRoot,
                _ => _paths.DocumentsRoot
            };
            Directory.CreateDirectory(root);
            var target = MakeUniquePath(Path.Combine(root, Path.GetFileName(sourcePath)));
            await using (var input = File.OpenRead(sourcePath))
            await using (var output = File.Create(target))
            {
                await input.CopyToAsync(output);
            }

            return await AddImageFileAsync(
                target,
                kind ?? CaptureKind.Imported,
                notes ?? "Imported into the GoatShot library.",
                source);
        });
    }

    public async Task UpdateItemAsync(CaptureItem item)
    {
        await Task.Run(() =>
        {
            lock (_gate)
            {
                var items = Load().ToList();
                var index = items.FindIndex(existing =>
                    existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                    existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    items[index] = item;
                }
                else
                {
                    items.Insert(0, item);
                }

                var json = JsonSerializer.Serialize(
                    items.OrderByDescending(existing => existing.CreatedAt).ToList(),
                    JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(_paths.IndexPath)!);
                File.WriteAllText(_paths.IndexPath, json);
            }

            _metadataIndex?.Upsert(item);
        });
    }

    public async Task DeleteItemAsync(CaptureItem item, bool deleteFile)
    {
        await Task.Run(() =>
        {
            lock (_gate)
            {
                var items = Load().ToList();
                items.RemoveAll(existing =>
                    existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                    existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));

                var json = JsonSerializer.Serialize(
                    items.OrderByDescending(existing => existing.CreatedAt).ToList(),
                    JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(_paths.IndexPath)!);
                File.WriteAllText(_paths.IndexPath, json);
            }

            _metadataIndex?.Delete(item);

            if (deleteFile && File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
        });
    }

    private CaptureItem BuildItem(
        string path,
        CaptureKind kind,
        CaptureBounds? bounds,
        bool privateCapture,
        CaptureSource? source,
        string? hotkeyProfile = null)
    {
        var info = new FileInfo(path);
        var width = 0;
        var height = 0;
        string thumbnailPath;

        try
        {
            using var image = Image.FromFile(path);
            width = image.Width;
            height = image.Height;
            thumbnailPath = CreateThumbnail(path, image);
        }
        catch
        {
            thumbnailPath = TryCreateVideoThumbnail(path, out width, out height)
                ? Path.Combine(_paths.ThumbnailRoot, $"{Path.GetFileNameWithoutExtension(path)}.jpg")
                : CreatePlaceholderThumbnail(path, kind);
        }

        return new CaptureItem
        {
            Kind = kind,
            CreatedAt = DateTimeOffset.Now,
            FilePath = path,
            ThumbnailPath = thumbnailPath,
            Bounds = bounds,
            Width = width,
            Height = height,
            Bytes = info.Exists ? info.Length : 0,
            IsPrivate = privateCapture,
            SourceApp = source?.ProcessName,
            SourceWindowTitle = source?.WindowTitle,
            SourceMonitorName = source?.MonitorName,
            HotkeyProfile = HotkeyProfileNames.Normalize(hotkeyProfile),
            Notes = privateCapture
                ? "Private capture: not added to persistent workspace index."
                : $"Local-first capture stored in the workspace library.{Environment.NewLine}Source: {source?.AppLabel ?? "unknown"} / {source?.WindowLabel ?? "untitled"}"
        };
    }

    private string CreateThumbnail(string imagePath, Image image)
    {
        var id = Path.GetFileNameWithoutExtension(imagePath);
        var thumbnailPath = Path.Combine(_paths.ThumbnailRoot, $"{id}.jpg");
        Directory.CreateDirectory(_paths.ThumbnailRoot);

        const int maxSide = 320;
        var scale = Math.Min(maxSide / (double)image.Width, maxSide / (double)image.Height);
        scale = Math.Min(scale, 1d);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var thumbnail = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(8, 16, 22));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(image, 0, 0, width, height);
        thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
        return thumbnailPath;
    }

    private bool TryCreateVideoThumbnail(string videoPath, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!VideoExtensions.Contains(Path.GetExtension(videoPath)))
        {
            return false;
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return false;
        }

        Directory.CreateDirectory(_paths.TempRoot);
        var framePath = Path.Combine(_paths.TempRoot, $"video-thumbnail-{Guid.NewGuid():N}.png");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add("error");
            start.ArgumentList.Add("-nostats");
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-ss");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(videoPath);
            start.ArgumentList.Add("-frames:v");
            start.ArgumentList.Add("1");
            start.ArgumentList.Add("-q:v");
            start.ArgumentList.Add("3");
            start.ArgumentList.Add(framePath);

            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(5_000))
            {
                TryKill(process);
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(framePath))
            {
                return false;
            }

            using var frame = Image.FromFile(framePath);
            width = frame.Width;
            height = frame.Height;
            CreateThumbnail(videoPath, frame);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(framePath))
                {
                    File.Delete(framePath);
                }
            }
            catch
            {
                // Thumbnail extraction should never prevent indexing.
            }
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for a timed-out FFmpeg thumbnail probe.
        }
    }

    private string CreatePlaceholderThumbnail(string filePath, CaptureKind kind)
    {
        var id = Path.GetFileNameWithoutExtension(filePath);
        var thumbnailPath = Path.Combine(_paths.ThumbnailRoot, $"{id}.jpg");
        Directory.CreateDirectory(_paths.ThumbnailRoot);

        using var thumbnail = new Bitmap(320, 180);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(8, 16, 22));
        using var borderPen = new Pen(Color.FromArgb(39, 66, 82), 2);
        using var accentBrush = new SolidBrush(Color.FromArgb(48, 230, 195));
        using var textBrush = new SolidBrush(Color.White);
        using var mutedBrush = new SolidBrush(Color.FromArgb(148, 169, 184));
        using var titleFont = new Font("Segoe UI", 18, FontStyle.Bold);
        using var captionFont = new Font("Segoe UI", 10, FontStyle.Regular);
        graphics.DrawRectangle(borderPen, 8, 8, 304, 164);
        graphics.FillRectangle(accentBrush, 24, 28, 52, 52);
        graphics.DrawString(kind.ToString(), titleFont, textBrush, 90, 30);
        graphics.DrawString(Path.GetFileName(filePath), captionFont, mutedBrush, 24, 104);
        thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
        return thumbnailPath;
    }

    private void AddToIndex(CaptureItem item)
    {
        lock (_gate)
        {
            var items = Load().ToList();
            items.RemoveAll(existing => existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, item);
            var json = JsonSerializer.Serialize(items, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.IndexPath)!);
            File.WriteAllText(_paths.IndexPath, json);
        }

        _metadataIndex?.Upsert(item);
    }

    private int NextCounter()
    {
        return Load().Count + 1;
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}

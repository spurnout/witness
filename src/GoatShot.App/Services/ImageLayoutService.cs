using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ImageLayoutService
{
    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;

    public ImageLayoutService(AppPaths paths, WorkspaceStore workspaceStore)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
    }

    public async Task<ImageLayoutResult> CombineAsync(
        IReadOnlyList<string> imagePaths,
        string orientation = "vertical",
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        var paths = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count < 2)
        {
            return Failed("Image combine requires at least two image paths.");
        }

        if (paths.Any(path => !File.Exists(path)))
        {
            return Failed("One or more image paths do not exist.");
        }

        var horizontal = orientation.Equals("horizontal", StringComparison.OrdinalIgnoreCase);
        if (!horizontal && !orientation.Equals("vertical", StringComparison.OrdinalIgnoreCase))
        {
            return Failed("Image combine orientation must be vertical or horizontal.");
        }

        var target = ResolveOutputPath(outputPath, $"combined-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png");
        await Task.Run(() => CombineImages(paths, target, horizontal), cancellationToken);

        var result = new ImageLayoutResult
        {
            Succeeded = true,
            Message = $"Combined {paths.Count} image{(paths.Count == 1 ? string.Empty : "s")} into {Path.GetFileName(target)}.",
            OutputPaths = [target]
        };

        if (addToWorkspace)
        {
            result.Items.Add(await _workspaceStore.AddImageFileAsync(
                target,
                CaptureKind.EditedImage,
                $"Combined {paths.Count} local images into one {orientation.ToLowerInvariant()} sheet."));
        }

        return result;
    }

    public async Task<ImageLayoutResult> SplitGridAsync(
        string imagePath,
        int rows,
        int columns,
        string? outputDirectory = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (rows <= 0 || columns <= 0)
        {
            return Failed("Image split rows and columns must be greater than zero.");
        }

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(imagePath));
        if (!File.Exists(path))
        {
            return Failed($"Image not found: {path}");
        }

        var outputRoot = string.IsNullOrWhiteSpace(outputDirectory)
            ? _paths.ImagesRoot
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        Directory.CreateDirectory(outputRoot);

        var outputs = await Task.Run(() => SplitImage(path, rows, columns, outputRoot), cancellationToken);
        var result = new ImageLayoutResult
        {
            Succeeded = outputs.Count > 0,
            Message = $"Split {Path.GetFileName(path)} into {outputs.Count} image tiles.",
            OutputPaths = outputs
        };

        if (addToWorkspace)
        {
            foreach (var output in outputs)
            {
                result.Items.Add(await _workspaceStore.AddImageFileAsync(
                    output,
                    CaptureKind.EditedImage,
                    $"Split tile from {Path.GetFileName(path)}."));
            }
        }

        return result;
    }

    public async Task<ImageLayoutResult> StitchAsync(
        IReadOnlyList<string> imagePaths,
        ScrollingCaptureOptions options,
        string? outputPath = null,
        bool addToWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        var paths = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count < 2)
        {
            return Failed("Image stitch requires at least two image paths.");
        }

        if (paths.Any(path => !File.Exists(path)))
        {
            return Failed("One or more image paths do not exist.");
        }

        options = options.Normalize();
        var target = ResolveOutputPath(outputPath, $"stitched-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png");
        await Task.Run(() => StitchImages(paths, target, options), cancellationToken);

        var result = new ImageLayoutResult
        {
            Succeeded = true,
            Message = $"Stitched {paths.Count} image{(paths.Count == 1 ? string.Empty : "s")} into {Path.GetFileName(target)}. Use this manual stitch path when simulated scrolling cannot control a browser, document, or large table cleanly.",
            OutputPaths = [target]
        };

        if (addToWorkspace)
        {
            result.Items.Add(await _workspaceStore.AddImageFileAsync(
                target,
                CaptureKind.ScrollingWindow,
                $"Manual {options.Axis.ToString().ToLowerInvariant()} scrolling stitch from {paths.Count} local images; sticky leading edge {options.StickyPixels}px."));
        }

        return result;
    }

    private static void CombineImages(IReadOnlyList<string> paths, string target, bool horizontal)
    {
        using var images = new DisposableImageList(paths);
        var width = horizontal ? images.Sum(image => image.Width) : images.Max(image => image.Width);
        var height = horizontal ? images.Max(image => image.Height) : images.Sum(image => image.Height);

        using var combined = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(combined))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            var offset = 0;
            foreach (var image in images)
            {
                var x = horizontal ? offset : (width - image.Width) / 2;
                var y = horizontal ? (height - image.Height) / 2 : offset;
                graphics.DrawImage(image, x, y, image.Width, image.Height);
                offset += horizontal ? image.Width : image.Height;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        combined.Save(target, ImageFormat.Png);
    }

    private static void StitchImages(IReadOnlyList<string> paths, string target, ScrollingCaptureOptions options)
    {
        using var images = new DisposableImageList(paths);
        using var stitched = ScrollingStitcher.Stitch(images, options);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        stitched.Save(target, ImageFormat.Png);
    }

    private static List<string> SplitImage(string path, int rows, int columns, string outputRoot)
    {
        using var image = Image.FromFile(path);
        var outputs = new List<string>();
        var name = Path.GetFileNameWithoutExtension(path);

        for (var row = 0; row < rows; row++)
        {
            var top = image.Height * row / rows;
            var bottom = image.Height * (row + 1) / rows;
            for (var column = 0; column < columns; column++)
            {
                var left = image.Width * column / columns;
                var right = image.Width * (column + 1) / columns;
                var width = Math.Max(1, right - left);
                var height = Math.Max(1, bottom - top);
                var target = MakeUniquePath(Path.Combine(outputRoot, $"{name}-r{row + 1}-c{column + 1}.png"));

                using var tile = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(tile))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(
                        image,
                        new Rectangle(0, 0, width, height),
                        new Rectangle(left, top, width, height),
                        GraphicsUnit.Pixel);
                }

                tile.Save(target, ImageFormat.Png);
                outputs.Add(target);
            }
        }

        return outputs;
    }

    private string ResolveOutputPath(string? outputPath, string fallbackFileName)
    {
        var target = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_paths.ImagesRoot, fallbackFileName)
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return MakeUniquePath(target);
    }

    private static ImageLayoutResult Failed(string message)
    {
        return new ImageLayoutResult
        {
            Succeeded = false,
            Message = message
        };
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

    private sealed class DisposableImageList : List<Bitmap>, IDisposable
    {
        public DisposableImageList(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                using var image = Image.FromFile(path);
                Add(new Bitmap(image));
            }
        }

        public void Dispose()
        {
            foreach (var image in this)
            {
                image.Dispose();
            }
        }
    }
}

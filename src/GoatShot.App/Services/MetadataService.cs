using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class MetadataService
{
    private readonly AppPaths _paths;

    public MetadataService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<CaptureItem> StripImageMetadataAsync(CaptureItem item, WorkspaceStore workspaceStore)
    {
        Directory.CreateDirectory(_paths.ImagesRoot);
        var target = Path.Combine(
            _paths.ImagesRoot,
            $"{Path.GetFileNameWithoutExtension(item.FilePath)}-metadata-stripped-{DateTimeOffset.Now:HHmmss}.png");

        await StripImageMetadataToAsync(item.FilePath, target);
        return await workspaceStore.AddImageFileAsync(
            target,
            CaptureKind.EditedImage,
            "Metadata stripped by re-encoding a flattened PNG copy.");
    }

    public static async Task StripImageMetadataToAsync(string sourcePath, string targetPath)
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
            using var image = Image.FromFile(sourcePath);
            using var bitmap = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }

            bitmap.Save(targetPath, ImageFormat.Png);
        });
    }
}

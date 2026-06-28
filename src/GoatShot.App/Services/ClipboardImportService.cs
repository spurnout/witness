using System.Windows.Media.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ClipboardImportService
{
    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;

    public ClipboardImportService(AppPaths paths, WorkspaceStore workspaceStore)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
    }

    public async Task<ClipboardImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        var image = ClipboardInterop.GetImage();
        if (image is not null)
        {
            var item = await ImportImageAsync(image, cancellationToken);
            return new ClipboardImportResult
            {
                Succeeded = true,
                Message = $"Imported clipboard image: {item.FileName}",
                Items = [item]
            };
        }

        var files = ClipboardInterop.GetFileDropList()
            .Where(File.Exists)
            .ToList();
        if (files.Count == 0)
        {
            return new ClipboardImportResult
            {
                Succeeded = false,
                Message = "Clipboard does not contain an image or file list."
            };
        }

        var imported = new List<CaptureItem>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            imported.Add(await _workspaceStore.ImportFileCopyAsync(
                file,
                "Imported from Windows clipboard file list."));
        }

        return new ClipboardImportResult
        {
            Succeeded = imported.Count > 0,
            Message = $"Imported {imported.Count} clipboard file{(imported.Count == 1 ? string.Empty : "s")}.",
            Items = imported
        };
    }

    private async Task<CaptureItem> ImportImageAsync(BitmapSource image, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.ImagesRoot);
        var target = MakeUniquePath(Path.Combine(
            _paths.ImagesRoot,
            $"clipboard-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png"));

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(target);
            encoder.Save(stream);
        }, cancellationToken);

        return await _workspaceStore.AddImageFileAsync(
            target,
            CaptureKind.ClipboardImage,
            "Imported from Windows clipboard image data.");
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

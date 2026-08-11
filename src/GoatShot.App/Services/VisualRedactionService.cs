using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class VisualRedactionService
{
    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;

    public VisualRedactionService(AppPaths paths, WorkspaceStore workspaceStore)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
    }

    public async Task<VisualRedactionResult> RedactSensitiveOcrAsync(
        CaptureItem item,
        string? outputPath = null,
        VisualRedactionMode mode = VisualRedactionMode.Solid,
        bool addToWorkspace = true,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(item.FilePath))
        {
            return Failed($"Capture file not found: {item.FilePath}");
        }

        if (string.IsNullOrWhiteSpace(item.OcrText) || item.OcrWords.Count == 0)
        {
            return Failed("Run OCR on this capture before visual redaction.");
        }

        var scan = SensitiveTextDetector.Scan(item.OcrText);
        if (scan.Findings.Count == 0)
        {
            return new VisualRedactionResult
            {
                Succeeded = false,
                RedactionCount = 0,
                SensitiveScan = scan,
                Message = "No sensitive OCR text was detected."
            };
        }

        var boxes = SensitiveRegionReviewPlanner.BuildBoxes(item, scan.Findings)
            .Select(box => new RectangleF(
                (float)box.X,
                (float)box.Y,
                (float)box.Width,
                (float)box.Height))
            .ToList();
        if (boxes.Count == 0)
        {
            return new VisualRedactionResult
            {
                Succeeded = false,
                RedactionCount = 0,
                SensitiveScan = scan,
                Message = "Sensitive text was detected, but no OCR word boxes overlapped the findings."
            };
        }

        var target = ResolveOutputPath(item, outputPath);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var image = Image.FromFile(item.FilePath);
            using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }

            ImageRegionEffects.Apply(bitmap, boxes, mode);
            bitmap.Save(target, ImageFormat.Png);
        }, cancellationToken);

        CaptureItem? saved = null;
        if (addToWorkspace)
        {
            saved = await _workspaceStore.AddImageFileAsync(
                target,
                CaptureKind.EditedImage,
                $"Flattened {mode.ToString().ToLowerInvariant()} redaction from OCR sensitive scan. {scan.Summary}");
            saved = await _workspaceStore.LinkReceiptDerivativeAsync(
                item,
                saved,
                "ocr-redacted-image",
                cancellationToken);
            saved.OcrText = scan.RedactedText;
            saved.OcrLanguageTag = item.OcrLanguageTag;
            saved.OcrRecognizedAt = item.OcrRecognizedAt;
            await _workspaceStore.UpdateItemAsync(saved);
        }

        return new VisualRedactionResult
        {
            Succeeded = true,
            OutputPath = target,
            Item = saved,
            RedactionCount = boxes.Count,
            SensitiveScan = scan,
            Message = $"Flattened {boxes.Count} {mode.ToString().ToLowerInvariant()} redaction block(s) into {Path.GetFileName(target)}."
        };
    }

    private string ResolveOutputPath(CaptureItem item, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        Directory.CreateDirectory(_paths.ImagesRoot);
        return Path.Combine(
            _paths.ImagesRoot,
            $"{Path.GetFileNameWithoutExtension(item.FilePath)}-ocr-redacted-{DateTimeOffset.Now:HHmmss}.png");
    }

    private static VisualRedactionResult Failed(string message)
    {
        return new VisualRedactionResult
        {
            Succeeded = false,
            Message = message
        };
    }
}

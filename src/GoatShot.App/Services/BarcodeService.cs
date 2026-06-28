using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace GoatShot.App.Services;

public sealed class BarcodeService
{
    private static readonly BarcodeFormat[] SupportedFormats =
    {
        BarcodeFormat.QR_CODE,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E
    };

    public async Task<BarcodeDecodeResult> DecodeFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("File not found.", path);
            }

            using var bitmap = (Bitmap)Image.FromFile(path);
            var reader = CreateReader();
            var rawResults = reader.DecodeMultiple(bitmap) ?? Array.Empty<Result>();
            if (rawResults.Length == 0)
            {
                var single = reader.Decode(bitmap);
                rawResults = single is null ? Array.Empty<Result>() : new[] { single };
            }

            var items = rawResults
                .Where(result => !string.IsNullOrWhiteSpace(result.Text))
                .GroupBy(result => $"{result.BarcodeFormat}:{result.Text}", StringComparer.Ordinal)
                .Select(group => ToItem(group.First()))
                .ToList();

            return new BarcodeDecodeResult
            {
                SourcePath = path,
                Items = items,
                Message = items.Count == 0
                    ? "No QR or barcode value found."
                    : $"Decoded {items.Count} QR/barcode value(s)."
            };
        }, cancellationToken);
    }

    public async Task<string> GenerateQrCodeAsync(string text, string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("QR text is required.", nameof(text));
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions
                {
                    Width = 640,
                    Height = 640,
                    Margin = 2
                }
            };

            using var bitmap = writer.Write(text);
            bitmap.Save(outputPath, ImageFormat.Png);
            return outputPath;
        }, cancellationToken);
    }

    private static BarcodeReader CreateReader()
    {
        return new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = SupportedFormats
            }
        };
    }

    private static BarcodeDecodeItem ToItem(Result result)
    {
        return new BarcodeDecodeItem
        {
            Format = result.BarcodeFormat.ToString(),
            Text = result.Text,
            Points = result.ResultPoints?
                .Select(point => new BarcodePoint { X = point.X, Y = point.Y })
                .ToList() ?? new List<BarcodePoint>()
        };
    }
}

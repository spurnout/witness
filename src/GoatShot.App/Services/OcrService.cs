using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace GoatShot.App.Services;

public sealed class OcrService
{
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;

    public OcrService(AppSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    public async Task<OcrRecognitionResult> RecognizeFileAsync(
        string filePath,
        string? languageTag = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(filePath));
        if (!File.Exists(resolvedPath))
        {
            return Failed($"Image file not found: {resolvedPath}");
        }

        OcrEngine? engine;
        try
        {
            engine = CreateEngine(languageTag);
        }
        catch (Exception ex)
        {
            return Failed($"Windows OCR engine could not be created: {ex.Message}");
        }

        if (engine is null)
        {
            return Failed("Windows OCR is unavailable for the requested language on this device.");
        }

        string? resizedPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preparedPath = PrepareImageForOcr(resolvedPath, out resizedPath, out var scaleX, out var scaleY);
            var file = await StorageFile.GetFileFromPathAsync(preparedPath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await engine.RecognizeAsync(bitmap);
            var textBuilder = new System.Text.StringBuilder();
            var words = new List<OcrRecognizedWord>();
            for (var lineIndex = 0; lineIndex < result.Lines.Count; lineIndex++)
            {
                if (lineIndex > 0)
                {
                    textBuilder.AppendLine();
                }

                var line = result.Lines[lineIndex];
                for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
                {
                    if (wordIndex > 0)
                    {
                        textBuilder.Append(' ');
                    }

                    var word = line.Words[wordIndex];
                    var startIndex = textBuilder.Length;
                    textBuilder.Append(word.Text);
                    words.Add(new OcrRecognizedWord
                    {
                        Text = word.Text,
                        LineIndex = lineIndex,
                        StartIndex = startIndex,
                        Length = word.Text.Length,
                        X = word.BoundingRect.X / scaleX,
                        Y = word.BoundingRect.Y / scaleY,
                        Width = word.BoundingRect.Width / scaleX,
                        Height = word.BoundingRect.Height / scaleY,
                        Confidence = null
                    });
                }
            }

            var text = textBuilder.ToString();
            return new OcrRecognitionResult
            {
                Succeeded = true,
                Text = text,
                LanguageTag = engine.RecognizerLanguage?.LanguageTag ?? string.Empty,
                LineCount = result.Lines.Count,
                Words = words,
                Message = text.Length == 0
                    ? "Windows OCR completed but did not recognize any text."
                    : $"Windows OCR recognized {result.Lines.Count} line(s) and {words.Count} word(s)."
            };
        }
        catch (Exception ex)
        {
            return Failed($"Windows OCR failed: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(resizedPath))
            {
                TryDelete(resizedPath);
            }
        }
    }

    public IReadOnlyList<string> AvailableLanguageTags()
    {
        return OcrEngine.AvailableRecognizerLanguages
            .Select(language => language.LanguageTag)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private OcrEngine? CreateEngine(string? languageTag)
    {
        var preferredLanguage = string.IsNullOrWhiteSpace(languageTag)
            ? _settings.OcrLanguageTag
            : languageTag;

        if (string.IsNullOrWhiteSpace(preferredLanguage))
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }

        var language = new Language(preferredLanguage.Trim());
        if (!OcrEngine.IsLanguageSupported(language))
        {
            return null;
        }

        return OcrEngine.TryCreateFromLanguage(language);
    }

    private string PrepareImageForOcr(string path, out string? resizedPath, out double scaleX, out double scaleY)
    {
        resizedPath = null;
        scaleX = 1d;
        scaleY = 1d;
        using var image = Image.FromFile(path);
        var maxDimension = (int)Math.Min(int.MaxValue, OcrEngine.MaxImageDimension);
        if (maxDimension <= 0 || (image.Width <= maxDimension && image.Height <= maxDimension))
        {
            return path;
        }

        var scale = Math.Min(maxDimension / (double)image.Width, maxDimension / (double)image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        scaleX = width / (double)image.Width;
        scaleY = height / (double)image.Height;
        resizedPath = Path.Combine(_paths.TempRoot, $"ocr-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(_paths.TempRoot);

        using var resized = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(image, 0, 0, width, height);
        resized.Save(resizedPath, ImageFormat.Png);
        return resizedPath;
    }

    private static OcrRecognitionResult Failed(string message)
    {
        return new OcrRecognitionResult
        {
            Succeeded = false,
            Message = message
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for temporary OCR resize files.
        }
    }
}

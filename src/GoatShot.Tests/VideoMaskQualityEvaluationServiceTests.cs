using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class VideoMaskQualityEvaluationServiceTests
{
    [TestMethod]
    public async Task EvaluateAsync_PerfectMaskPassesAndWritesBoundaryReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(root, "generated.png");
            var reference = Path.Combine(root, "reference.png");
            WriteMask(generated, 4, 4, (0, 0), (1, 0), (0, 1), (1, 1));
            WriteMask(reference, 4, 4, (0, 0), (1, 0), (0, 1), (1, 1));

            var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
            {
                GeneratedMaskPath = generated,
                ReferenceMaskPath = reference,
                OutputPath = root,
                Note = "reviewed token=super-secret-token-1234567890"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.QualityPassed);
            Assert.AreEqual(16, result.TotalPixels);
            Assert.AreEqual(4, result.TruePositivePixels);
            Assert.AreEqual(12, result.TrueNegativePixels);
            Assert.AreEqual(0, result.FalsePositivePixels);
            Assert.AreEqual(0, result.FalseNegativePixels);
            Assert.AreEqual(1d, result.IntersectionOverUnion, 0.0001d);
            Assert.IsFalse(result.WouldDownloadModel);
            Assert.IsFalse(result.WouldRunSegmentationModel);
            Assert.IsFalse(result.WouldContactHostedService);
            Assert.IsFalse(result.WouldMutateSourceMedia);
            Assert.IsFalse(result.WouldCertifyWholeModel);
            AssertGeneratedFile(root, "mask-quality-evaluation.md");
            AssertGeneratedFile(root, "mask-quality-evaluation.json");

            var report = File.ReadAllText(Path.Combine(root, "mask-quality-evaluation.md"));
            StringAssert.Contains(report, "Quality passed: `True`");
            Assert.IsFalse(report.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(report, "REDACTED");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_RejectsBelowThresholdWithKnownMetrics()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(root, "generated.png");
            var reference = Path.Combine(root, "reference.png");
            WriteMask(generated, 4, 4, (0, 0), (1, 0), (0, 1), (2, 2));
            WriteMask(reference, 4, 4, (0, 0), (1, 0), (0, 1), (1, 1));

            var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
            {
                GeneratedMaskPath = generated,
                ReferenceMaskPath = reference,
                OutputPath = root,
                MinimumIntersectionOverUnion = 0.90d,
                MinimumPrecision = 0.90d,
                MinimumRecall = 0.90d
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.QualityPassed);
            Assert.AreEqual(3, result.TruePositivePixels);
            Assert.AreEqual(1, result.FalsePositivePixels);
            Assert.AreEqual(1, result.FalseNegativePixels);
            Assert.AreEqual(11, result.TrueNegativePixels);
            Assert.AreEqual(0.6d, result.IntersectionOverUnion, 0.0001d);
            Assert.AreEqual(0.75d, result.Precision, 0.0001d);
            Assert.AreEqual(0.75d, result.Recall, 0.0001d);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("did not meet", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "mask-quality-evaluation.md");
            AssertGeneratedFile(root, "mask-quality-evaluation.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_RejectsMismatchedDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(root, "generated.png");
            var reference = Path.Combine(root, "reference.png");
            WriteMask(generated, 4, 4, (0, 0));
            WriteMask(reference, 5, 4, (0, 0));

            var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
            {
                GeneratedMaskPath = generated,
                ReferenceMaskPath = reference,
                OutputPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.QualityPassed);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("dimensions must match", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "mask-quality-evaluation.md");
            AssertGeneratedFile(root, "mask-quality-evaluation.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_RejectsMaskVideosWhenFfmpegIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var generated = Path.Combine(root, "generated.mp4");
            var reference = Path.Combine(root, "reference.mp4");
            File.WriteAllBytes(generated, [1, 2, 3, 4]);
            File.WriteAllBytes(reference, [1, 2, 3, 4]);

            var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
            {
                GeneratedMaskPath = generated,
                ReferenceMaskPath = reference,
                OutputPath = root,
                FfmpegPath = Path.Combine(root, "missing-ffmpeg.exe")
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.WouldRunSegmentationModel);
            Assert.IsFalse(result.WouldContactHostedService);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("FFmpeg is required", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "mask-quality-evaluation.md");
            AssertGeneratedFile(root, "mask-quality-evaluation.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task EvaluateAsync_MaskVideosCompareExtractedFramesWhenFfmpegIsAvailable()
    {
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            Assert.Inconclusive("FFmpeg is unavailable on this machine.");
        }

        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var generatedFrames = Path.Combine(root, "generated-frames");
            var referenceFrames = Path.Combine(root, "reference-frames");
            WriteMask(Path.Combine(generatedFrames, "frame-000001.png"), 4, 4, (0, 0), (1, 0));
            WriteMask(Path.Combine(generatedFrames, "frame-000002.png"), 4, 4, (0, 0), (1, 0), (2, 0));
            WriteMask(Path.Combine(referenceFrames, "frame-000001.png"), 4, 4, (0, 0), (1, 0));
            WriteMask(Path.Combine(referenceFrames, "frame-000002.png"), 4, 4, (0, 0), (1, 0), (2, 0));

            var generatedVideo = Path.Combine(root, "generated.mkv");
            var referenceVideo = Path.Combine(root, "reference.mkv");
            await CreateLosslessVideoAsync(ffmpeg, generatedFrames, generatedVideo);
            await CreateLosslessVideoAsync(ffmpeg, referenceFrames, referenceVideo);

            var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
            {
                GeneratedMaskPath = generatedVideo,
                ReferenceMaskPath = referenceVideo,
                OutputPath = root,
                FfmpegPath = ffmpeg
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.QualityPassed);
            Assert.AreEqual("video-frames", result.EvaluationMode);
            Assert.AreEqual(2, result.GeneratedFrameCount);
            Assert.AreEqual(2, result.ReferenceFrameCount);
            Assert.AreEqual(2, result.EvaluatedFrameCount);
            Assert.AreEqual(32, result.TotalPixels);
            Assert.AreEqual(5, result.TruePositivePixels);
            Assert.AreEqual(27, result.TrueNegativePixels);
            Assert.AreEqual(1d, result.IntersectionOverUnion, 0.0001d);
            Assert.AreEqual(2, result.Frames.Count);
            Assert.IsFalse(result.WouldDownloadModel);
            Assert.IsFalse(result.WouldRunSegmentationModel);
            Assert.IsFalse(result.WouldContactHostedService);
            Assert.IsFalse(result.WouldMutateSourceMedia);
            Assert.IsFalse(result.WouldCertifyWholeModel);

            var report = File.ReadAllText(Path.Combine(root, "mask-quality-evaluation.md"));
            StringAssert.Contains(report, "Evaluation mode: `video-frames`");
            StringAssert.Contains(report, "Evaluated frames: `2`");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static void WriteMask(string path, int width, int height, params (int X, int Y)[] foregroundPixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
        }

        foreach (var pixel in foregroundPixels)
        {
            bitmap.SetPixel(pixel.X, pixel.Y, Color.White);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertGeneratedFile(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        Assert.IsTrue(File.Exists(path), $"{fileName} was not generated.");
        Assert.IsTrue(new FileInfo(path).Length > 0, $"{fileName} was empty.");
    }

    private static async Task CreateLosslessVideoAsync(string ffmpeg, string frameRoot, string outputPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-framerate");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(Path.Combine(frameRoot, "frame-%06d.png"));
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("ffv1");
        process.StartInfo.ArgumentList.Add(outputPath);

        Assert.IsTrue(process.Start(), "FFmpeg did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.AreEqual(0, process.ExitCode, $"FFmpeg failed: {stderr}{Environment.NewLine}{stdout}");
        Assert.IsTrue(File.Exists(outputPath), "Video was not generated.");
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

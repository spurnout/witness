using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GoatShot.App.Services;

public sealed class PersonSegmentationInferenceService : IDisposable
{
    public const int PersonClassIndex = 15;
    public const int InferenceSize = 520;
    private static readonly float[] Means = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StandardDeviations = [0.229f, 0.224f, 0.225f];
    private readonly BundledToolResolver _bundledTools;
    private readonly object _sessionLock = new();
    private InferenceSession? _session;
    private string _executionProvider = string.Empty;

    public PersonSegmentationInferenceService(BundledToolResolver bundledTools)
    {
        _bundledTools = bundledTools;
    }

    public PersonSegmentationInferenceStatus GetStatus()
    {
        var model = _bundledTools.Resolve("person-segmentation-model");
        return new PersonSegmentationInferenceStatus(
            !string.IsNullOrWhiteSpace(model),
            model ?? string.Empty,
            string.IsNullOrWhiteSpace(_executionProvider) ? "Not initialized" : _executionProvider,
            string.IsNullOrWhiteSpace(model)
                ? "The bundled person-segmentation model is unavailable; run Repair from Settings."
                : "Bundled person segmentation is ready. Inference initializes on first use.");
    }

    public Task<PersonSegmentationInferenceResult> GenerateMaskAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GenerateMask(inputPath, outputPath, cancellationToken), cancellationToken);
    }

    private PersonSegmentationInferenceResult GenerateMask(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            return PersonSegmentationInferenceResult.Failed($"Input image not found: {inputPath}");
        }

        var session = GetOrCreateSession();
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new Bitmap(inputPath);
        using var resized = new Bitmap(source, new Size(InferenceSize, InferenceSize));
        var input = CreateInputTensor(resized);
        // Every other failure here returns a Failed result; a model with an unexpected input count
        // should not be the one case that throws out of the task instead.
        if (session.InputMetadata.Count != 1)
        {
            return PersonSegmentationInferenceResult.Failed(
                $"The bundled model exposes {session.InputMetadata.Count} inputs; exactly one is required. " +
                "Run Repair from Settings to restore the expected model.");
        }

        var inputName = session.InputMetadata.Keys.First();
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var output = results.FirstOrDefault(result => result.Name.Equals("out", StringComparison.OrdinalIgnoreCase))
            ?? results.First();
        var scores = output.AsTensor<float>();
        if (scores.Dimensions.Length != 4 || scores.Dimensions[1] <= PersonClassIndex)
        {
            return PersonSegmentationInferenceResult.Failed("The bundled model returned an unexpected segmentation tensor.");
        }

        using var inferenceMask = CreatePersonMask(scores);
        using var outputMask = new Bitmap(inferenceMask, source.Size);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        outputMask.Save(fullOutput, ImageFormat.Png);
        return new PersonSegmentationInferenceResult(
            true,
            fullOutput,
            _executionProvider,
            source.Width,
            source.Height,
            "Person segmentation mask generated with the bundled ONNX model.");
    }

    private InferenceSession GetOrCreateSession()
    {
        lock (_sessionLock)
        {
            if (_session is not null)
            {
                return _session;
            }

            var modelPath = _bundledTools.Resolve("person-segmentation-model")
                ?? throw new InvalidOperationException("The bundled person-segmentation model is unavailable. Run Repair from Settings.");
            try
            {
                var directMl = new SessionOptions
                {
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                directMl.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(modelPath, directMl);
                _executionProvider = "DirectML";
            }
            catch (OnnxRuntimeException)
            {
                _session = new InferenceSession(modelPath, new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                });
                _executionProvider = "CPU fallback";
            }

            return _session;
        }
    }

    private static DenseTensor<float> CreateInputTensor(Bitmap image)
    {
        using var rgb = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(rgb))
        {
            graphics.DrawImageUnscaled(image, 0, 0);
        }

        var tensor = new DenseTensor<float>([1, 3, image.Height, image.Width]);
        var data = rgb.LockBits(
            new Rectangle(0, 0, rgb.Width, rgb.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < rgb.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                for (var x = 0; x < rgb.Width; x++)
                {
                    var pixel = x * 3;
                    var red = row[pixel + 2] / 255f;
                    var green = row[pixel + 1] / 255f;
                    var blue = row[pixel] / 255f;
                    tensor[0, 0, y, x] = (red - Means[0]) / StandardDeviations[0];
                    tensor[0, 1, y, x] = (green - Means[1]) / StandardDeviations[1];
                    tensor[0, 2, y, x] = (blue - Means[2]) / StandardDeviations[2];
                }
            }
        }
        finally
        {
            rgb.UnlockBits(data);
        }

        return tensor;
    }

    private static Bitmap CreatePersonMask(Tensor<float> scores)
    {
        var height = scores.Dimensions[2];
        var width = scores.Dimensions[3];
        var mask = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = mask.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < height; y++)
            {
                Array.Clear(row);
                for (var x = 0; x < width; x++)
                {
                    var bestClass = 0;
                    var bestScore = float.NegativeInfinity;
                    for (var classIndex = 0; classIndex < scores.Dimensions[1]; classIndex++)
                    {
                        var score = scores[0, classIndex, y, x];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestClass = classIndex;
                        }
                    }

                    var value = bestClass == PersonClassIndex ? (byte)255 : (byte)0;
                    var pixel = x * 3;
                    row[pixel] = value;
                    row[pixel + 1] = value;
                    row[pixel + 2] = value;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + (y * data.Stride), row.Length);
            }
        }
        finally
        {
            mask.UnlockBits(data);
        }

        return mask;
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}

public sealed record PersonSegmentationInferenceStatus(
    bool Available,
    string ModelPath,
    string ExecutionProvider,
    string Message);

public sealed record PersonSegmentationInferenceResult(
    bool Succeeded,
    string OutputPath,
    string ExecutionProvider,
    int Width,
    int Height,
    string Message)
{
    public static PersonSegmentationInferenceResult Failed(string message) => new(false, string.Empty, string.Empty, 0, 0, message);
}

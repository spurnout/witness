using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// Incremental FFmpeg fallback that writes BGRA frames directly to stdin. At most one
/// uncompressed frame is materialized at a time; no PNG frame directory is created.
/// </summary>
internal sealed class FfmpegRawVideoSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Task<string> _stderr;
    private readonly Task<string> _stdout;
    private bool _completed;
    private bool _disposed;

    private FfmpegRawVideoSession(
        Process process,
        int width,
        int height,
        int framesPerSecond,
        string outputPath,
        string encoderName,
        string encoderProvider)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _stderr = process.StandardError.ReadToEndAsync();
        _stdout = process.StandardOutput.ReadToEndAsync();
        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        OutputPath = outputPath;
        EncoderName = encoderName;
        EncoderProvider = encoderProvider;
    }

    public int Width { get; }
    public int Height { get; }
    public int FramesPerSecond { get; }
    public int FrameCount { get; private set; }
    public string OutputPath { get; }
    public string EncoderName { get; }
    public string EncoderProvider { get; }

    public static FfmpegRawVideoSession Start(
        string ffmpeg,
        string outputPath,
        NormalizedRecordingSettings settings,
        string encoderName,
        string encoderProvider,
        bool hardwareAccelerated,
        int targetBitrateKbps,
        int width,
        int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(settings);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("rawvideo");
        start.ArgumentList.Add("-pixel_format");
        start.ArgumentList.Add("bgra");
        start.ArgumentList.Add("-video_size");
        start.ArgumentList.Add($"{width}x{height}");
        start.ArgumentList.Add("-framerate");
        start.ArgumentList.Add(Math.Max(1, settings.FramesPerSecond).ToString());
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("pipe:0");
        start.ArgumentList.Add("-an");
        start.ArgumentList.Add("-c:v");
        start.ArgumentList.Add(string.IsNullOrWhiteSpace(encoderName) ? "libx264" : encoderName);
        if (hardwareAccelerated)
        {
            start.ArgumentList.Add("-b:v");
            start.ArgumentList.Add($"{Math.Max(800, targetBitrateKbps)}k");
        }
        else
        {
            if (settings.BitrateKbps > 0)
            {
                start.ArgumentList.Add("-b:v");
                start.ArgumentList.Add($"{settings.BitrateKbps}k");
            }
            else if (settings.UseVariableBitrate)
            {
                start.ArgumentList.Add("-crf");
                start.ArgumentList.Add(settings.Crf.ToString());
            }

            start.ArgumentList.Add("-preset");
            start.ArgumentList.Add(settings.QualityProfile.Equals("Small", StringComparison.OrdinalIgnoreCase)
                ? "ultrafast"
                : "veryfast");
        }

        start.ArgumentList.Add("-pix_fmt");
        start.ArgumentList.Add("yuv420p");
        start.ArgumentList.Add("-movflags");
        start.ArgumentList.Add("+faststart");
        start.ArgumentList.Add(outputPath);

        var process = Process.Start(start) ??
            throw new InvalidOperationException("FFmpeg rawvideo process could not be started.");
        return new FfmpegRawVideoSession(
            process,
            width,
            height,
            Math.Max(1, settings.FramesPerSecond),
            outputPath,
            encoderName,
            encoderProvider);
    }

    public async Task WriteFrameAsync(Bitmap frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The FFmpeg rawvideo session is already complete.");
        }

        if (frame.Width != Width || frame.Height != Height)
        {
            throw new InvalidOperationException(
                $"Rawvideo frame dimensions changed from {Width}x{Height} to {frame.Width}x{frame.Height}.");
        }

        var bytes = CopyBgra(frame);
        await _input.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        FrameCount++;
    }

    public async Task<RecordingResult> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            return BuildResult(_process.ExitCode, await _stderr.ConfigureAwait(false), await _stdout.ConfigureAwait(false));
        }

        await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        _input.Dispose();
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
        return BuildResult(
            _process.ExitCode,
            await _stderr.ConfigureAwait(false),
            await _stdout.ConfigureAwait(false));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            _input.Dispose();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Recording failure cleanup is best effort.
        }

        _process.Dispose();
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private RecordingResult BuildResult(int exitCode, string stderr, string stdout)
    {
        var succeeded = exitCode == 0 && FrameCount > 0 && File.Exists(OutputPath);
        return new RecordingResult
        {
            Succeeded = succeeded,
            OutputPath = OutputPath,
            Message = succeeded
                ? $"FFmpeg rawvideo pipe encoded {FrameCount} frame(s) with {EncoderName} ({EncoderProvider})."
                : $"FFmpeg rawvideo pipe failed with {EncoderName} ({EncoderProvider}) and exit code {exitCode}. " +
                  RecordingService.ShortFfmpegMessage(stderr, stdout)
        };
    }

    private static byte[] CopyBgra(Bitmap bitmap)
    {
        var rowBytes = checked(bitmap.Width * 4);
        var output = new byte[checked(rowBytes * bitmap.Height)];
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var absoluteStride = Math.Abs(stride);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = stride < 0
                    ? IntPtr.Add(data.Scan0, (bitmap.Height - 1 - y) * absoluteStride)
                    : IntPtr.Add(data.Scan0, y * stride);
                Marshal.Copy(sourceRow, output, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return output;
    }
}

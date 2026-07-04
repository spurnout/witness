using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GoatShot.App.Services;

public static class NativeMediaFoundationMp4Encoder
{
    private const int MfVersion = 0x00020070;
    private const int MfVideoInterlaceProgressive = 2;
    private const long HnsPerSecond = 10_000_000L;
    private const int NativeAudioSampleRate = 48_000;
    private const int NativeAudioChannels = 2;
    private const int NativeAudioBitsPerSample = 16;
    private const int NativeAudioAacBytesPerSecond = 16_000;
    private const int NativeAudioFramesPerSample = 2_048;

    public const bool ProductionEngineImplemented = true;
    public const bool ProductionAudioMixingImplemented = true;
    public const bool ProductionWebcamCompositorImplemented = true;

    public sealed record NativeMp4AudioInput(AudioCaptureSource Source, string Path);

    public static bool CanEncodeVideoOnly(
        RecordingEnginePlan plan,
        NormalizedRecordingSettings settings,
        out string reason)
    {
        if (plan.Choice != RecordingEngineChoice.Production)
        {
            reason = $"production engine was not selected ({plan.ChoiceLabel}).";
            return false;
        }

        if (settings.IncludeMicrophone || settings.IncludeSystemAudio)
        {
            reason = "the video-only helper does not accept audio; use the production encoder with captured audio inputs.";
            return false;
        }

        if (settings.EnableWebcamOverlay)
        {
            reason = "the video-only helper does not accept webcam overlay; use the production encoder after precompositing webcam frames.";
            return false;
        }

        reason = "production video-only encoding is available.";
        return true;
    }

    public static bool CanEncodeProduction(
        RecordingEnginePlan plan,
        NormalizedRecordingSettings settings,
        IReadOnlyList<NativeMp4AudioInput> audioInputs,
        out string reason)
    {
        if (plan.Choice != RecordingEngineChoice.Production)
        {
            reason = $"production engine was not selected ({plan.ChoiceLabel}).";
            return false;
        }

        if (settings.EnableWebcamOverlay)
        {
            reason = audioInputs.Count == 0
                ? "production video and precomposited webcam overlay encoding are available."
                : "production video, precomposited webcam overlay, and AAC audio muxing are available.";
            return true;
        }

        if ((settings.IncludeMicrophone || settings.IncludeSystemAudio) && audioInputs.Count == 0)
        {
            reason = "requested audio did not produce payload bytes; native production can continue video-only.";
            return true;
        }

        reason = audioInputs.Count == 0
            ? "production video-only encoding is available."
            : "production video and AAC audio muxing are available.";
        return true;
    }

    public static async Task<RecordingResult> EncodePngFramesAsync(
        string frameDirectory,
        NormalizedRecordingSettings settings,
        ProductionVideoEncoderSelection encoder,
        string outputPath,
        IReadOnlyList<NativeMp4AudioInput>? audioInputs,
        CancellationToken cancellationToken)
    {
        var frames = Directory
            .EnumerateFiles(frameDirectory, "frame*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (frames.Count == 0)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = "Native Media Foundation encode could not start because no PNG frames were captured."
            };
        }

        try
        {
            await Task.Run(
                () => EncodePngFrames(frames, settings, encoder, outputPath, audioInputs ?? [], cancellationToken),
                cancellationToken);

            var audioText = audioInputs is { Count: > 0 }
                ? $"audioInputs={audioInputs.Count}; "
                : string.Empty;
            return new RecordingResult
            {
                Succeeded = true,
                OutputPath = outputPath,
                Message = $"MP4 encoded through native Media Foundation {encoder.Codec} ({encoder.ChoiceLabel}); hardware={(encoder.IsHardwareAccelerated ? "yes" : "no")}; frames={frames.Count}; {audioText}{FormatNativeSummary(settings, audioInputs?.Count ?? 0)}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RecordingResult
            {
                Succeeded = false,
                Message = $"Native Media Foundation MP4 encode failed ({ex.GetType().Name}: {ex.Message})"
            };
        }
    }

    private static void EncodePngFrames(
        IReadOnlyList<string> frames,
        NormalizedRecordingSettings settings,
        ProductionVideoEncoderSelection encoder,
        string outputPath,
        IReadOnlyList<NativeMp4AudioInput> audioInputs,
        CancellationToken cancellationToken)
    {
        using var first = new Bitmap(frames[0]);
        var width = first.Width;
        var height = first.Height;
        var fps = Math.Max(1, settings.FramesPerSecond);
        var frameDuration = HnsPerSecond / fps;
        var bitrate = Math.Max(750_000, settings.BitrateKbps * 1_000);

        CheckHResult(MFStartup(MfVersion, 0), "Media Foundation startup");
        IMFSinkWriter? writer = null;
        IMFMediaType? outputType = null;
        IMFMediaType? inputType = null;
        IMFMediaType? audioOutputType = null;
        IMFMediaType? audioInputType = null;
        NativeAudioPayload? audioPayload = null;
        try
        {
            audioPayload = BuildNativeAudioPayload(audioInputs, cancellationToken);
            CheckHResult(MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, IntPtr.Zero, out writer), "create sink writer");
            var videoSubtype = VideoSubtypeForEncoder(encoder);
            outputType = CreateVideoType(videoSubtype, width, height, fps, bitrate, includeBitrate: true);
            CheckHResult(writer.AddStream(outputType, out var streamIndex), $"add {encoder.Codec} stream");
            int? audioStreamIndex = null;
            if (audioPayload is not null)
            {
                audioOutputType = CreateAudioType(MfAudioFormatAac, audioPayload.SampleRate, audioPayload.Channels, NativeAudioBitsPerSample, NativeAudioAacBytesPerSecond, includeAverageBytesPerSecond: true);
                CheckHResult(writer.AddStream(audioOutputType, out var audioIndex), "add AAC audio stream");
                audioStreamIndex = audioIndex;
            }

            inputType = CreateVideoType(MfVideoFormatRgb32, width, height, fps, bitrate, includeBitrate: false);
            CheckHResult(inputType.SetUINT32(MfMtDefaultStride, width * 4), "set input stride");
            CheckHResult(writer.SetInputMediaType(streamIndex, inputType, IntPtr.Zero), "set RGB32 input type");
            if (audioPayload is not null && audioStreamIndex.HasValue)
            {
                audioInputType = CreateAudioType(MfAudioFormatPcm, audioPayload.SampleRate, audioPayload.Channels, NativeAudioBitsPerSample, audioPayload.AverageBytesPerSecond, includeAverageBytesPerSecond: false);
                CheckHResult(writer.SetInputMediaType(audioStreamIndex.Value, audioInputType, IntPtr.Zero), "set PCM audio input type");
            }

            CheckHResult(writer.BeginWriting(), "begin writing");

            for (var index = 0; index < frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = new Bitmap(frames[index]);
                if (source.Width != width || source.Height != height)
                {
                    throw new InvalidOperationException("Captured MP4 frames changed size during recording.");
                }

                using var sample = CreateSampleFromBitmap(source, index * frameDuration, frameDuration);
                CheckHResult(writer.WriteSample(streamIndex, sample.Sample), $"write frame {index + 1}");
            }

            if (audioPayload is not null && audioStreamIndex.HasValue)
            {
                var videoDurationHns = frames.Count * frameDuration;
                WriteAudioSamples(writer, audioStreamIndex.Value, audioPayload, videoDurationHns, cancellationToken);
            }

            CheckHResult(writer.Finalize_(), "finalize MP4");
        }
        finally
        {
            if (writer is not null)
            {
                Marshal.FinalReleaseComObject(writer);
            }

            if (outputType is not null)
            {
                Marshal.FinalReleaseComObject(outputType);
            }

            if (inputType is not null)
            {
                Marshal.FinalReleaseComObject(inputType);
            }

            if (audioOutputType is not null)
            {
                Marshal.FinalReleaseComObject(audioOutputType);
            }

            if (audioInputType is not null)
            {
                Marshal.FinalReleaseComObject(audioInputType);
            }

            _ = MFShutdown();
        }
    }

    private static IMFMediaType CreateVideoType(
        Guid subtype,
        int width,
        int height,
        int fps,
        int bitrate,
        bool includeBitrate)
    {
        CheckHResult(MFCreateMediaType(out var mediaType), "create media type");
        CheckHResult(mediaType.SetGUID(MfMtMajorType, MfMediaTypeVideo), "set major type");
        CheckHResult(mediaType.SetGUID(MfMtSubtype, subtype), "set subtype");
        if (includeBitrate)
        {
            CheckHResult(mediaType.SetUINT32(MfMtAvgBitrate, bitrate), "set bitrate");
        }

        CheckHResult(mediaType.SetUINT32(MfMtInterlaceMode, MfVideoInterlaceProgressive), "set interlace mode");
        CheckHResult(mediaType.SetUINT64(MfMtFrameSize, PackRatio(width, height)), "set frame size");
        CheckHResult(mediaType.SetUINT64(MfMtFrameRate, PackRatio(fps, 1)), "set frame rate");
        CheckHResult(mediaType.SetUINT64(MfMtPixelAspectRatio, PackRatio(1, 1)), "set pixel aspect ratio");
        return mediaType;
    }

    private static Guid VideoSubtypeForEncoder(ProductionVideoEncoderSelection encoder)
    {
        return encoder.Codec.Equals("HEVC", StringComparison.OrdinalIgnoreCase)
            ? MfVideoFormatHevc
            : MfVideoFormatH264;
    }

    private static IMFMediaType CreateAudioType(
        Guid subtype,
        int sampleRate,
        int channels,
        int bitsPerSample,
        int averageBytesPerSecond,
        bool includeAverageBytesPerSecond)
    {
        CheckHResult(MFCreateMediaType(out var mediaType), "create audio media type");
        CheckHResult(mediaType.SetGUID(MfMtMajorType, MfMediaTypeAudio), "set audio major type");
        CheckHResult(mediaType.SetGUID(MfMtSubtype, subtype), "set audio subtype");
        CheckHResult(mediaType.SetUINT32(MfMtAudioBitsPerSample, bitsPerSample), "set audio bits per sample");
        CheckHResult(mediaType.SetUINT32(MfMtAudioSamplesPerSecond, sampleRate), "set audio sample rate");
        CheckHResult(mediaType.SetUINT32(MfMtAudioNumChannels, channels), "set audio channel count");
        var blockAlignment = channels * (bitsPerSample / 8);
        if (subtype == MfAudioFormatPcm)
        {
            CheckHResult(mediaType.SetUINT32(MfMtAudioBlockAlignment, blockAlignment), "set PCM audio block alignment");
            CheckHResult(mediaType.SetUINT32(MfMtAudioAvgBytesPerSecond, sampleRate * blockAlignment), "set PCM average bytes per second");
        }
        else if (includeAverageBytesPerSecond)
        {
            CheckHResult(mediaType.SetUINT32(MfMtAudioAvgBytesPerSecond, averageBytesPerSecond), "set AAC average bytes per second");
        }

        return mediaType;
    }

    internal static NativeAudioPayload? BuildNativeAudioPayload(
        IReadOnlyList<NativeMp4AudioInput> audioInputs,
        CancellationToken cancellationToken = default)
    {
        var existingInputs = audioInputs
            .Where(input => !string.IsNullOrWhiteSpace(input.Path) && File.Exists(input.Path))
            .ToList();
        if (existingInputs.Count == 0)
        {
            return null;
        }

        var providers = new List<ISampleProvider>();
        var readers = new List<AudioFileReader>();
        try
        {
            foreach (var input in existingInputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = new AudioFileReader(input.Path);
                readers.Add(reader);
                ISampleProvider provider = reader;
                provider = EnsureStereo(provider);
                if (provider.WaveFormat.SampleRate != NativeAudioSampleRate)
                {
                    provider = new WdlResamplingSampleProvider(provider, NativeAudioSampleRate);
                }

                providers.Add(provider);
            }

            if (providers.Count == 0)
            {
                return null;
            }

            var samplesPerChunk = NativeAudioFramesPerSample * NativeAudioChannels;
            var providerBuffers = providers.Select(_ => new float[samplesPerChunk]).ToList();
            var mix = new float[samplesPerChunk];
            using var pcm = new MemoryStream();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(mix);
                var maxRead = 0;
                for (var providerIndex = 0; providerIndex < providers.Count; providerIndex++)
                {
                    var buffer = providerBuffers[providerIndex];
                    var read = providers[providerIndex].Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        continue;
                    }

                    maxRead = Math.Max(maxRead, read);
                    for (var sample = 0; sample < read; sample++)
                    {
                        mix[sample] += buffer[sample] / providers.Count;
                    }
                }

                if (maxRead == 0)
                {
                    break;
                }

                WritePcm16Samples(pcm, mix, maxRead);
            }

            if (pcm.Length == 0)
            {
                return null;
            }

            return new NativeAudioPayload(
                pcm.ToArray(),
                NativeAudioSampleRate,
                NativeAudioChannels,
                NativeAudioBitsPerSample,
                existingInputs.Count);
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private static ISampleProvider EnsureStereo(ISampleProvider provider)
    {
        return provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => new StereoDownmixSampleProvider(provider)
        };
    }

    private static void WritePcm16Samples(Stream output, float[] samples, int sampleCount)
    {
        Span<byte> bytes = stackalloc byte[2];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = Math.Clamp(samples[i], -1f, 1f);
            var pcm = (short)Math.Round(value * short.MaxValue);
            bytes[0] = (byte)(pcm & 0xFF);
            bytes[1] = (byte)((pcm >> 8) & 0xFF);
            output.Write(bytes);
        }
    }

    private static void WriteAudioSamples(
        IMFSinkWriter writer,
        int streamIndex,
        NativeAudioPayload payload,
        long maxDurationHns,
        CancellationToken cancellationToken)
    {
        var bytesPerFrame = payload.Channels * (payload.BitsPerSample / 8);
        var bytesPerSample = NativeAudioFramesPerSample * bytesPerFrame;
        // Trim audio to the encoded video duration so the MP4 does not trail off with
        // audio-only content; the FFmpeg fallback enforces the same bound via -shortest.
        var maxAudioFrames = maxDurationHns * payload.SampleRate / HnsPerSecond;
        long framesWritten = 0;
        for (var offset = 0; offset < payload.Pcm16.Length; offset += bytesPerSample)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byteCount = Math.Min(bytesPerSample, payload.Pcm16.Length - offset);
            var frameCount = (int)Math.Min(byteCount / bytesPerFrame, maxAudioFrames - framesWritten);
            if (frameCount <= 0)
            {
                break;
            }

            byteCount = frameCount * bytesPerFrame;
            var sampleTime = framesWritten * HnsPerSecond / payload.SampleRate;
            var sampleDuration = frameCount * HnsPerSecond / payload.SampleRate;
            using var sample = CreateSampleFromBytes(payload.Pcm16, offset, byteCount, sampleTime, sampleDuration);
            CheckHResult(writer.WriteSample(streamIndex, sample.Sample), $"write audio sample at frame {framesWritten}");
            framesWritten += frameCount;
        }
    }

    private static string FormatNativeSummary(NormalizedRecordingSettings settings, int audioInputCount)
    {
        var overlays = settings.ShowRecordingTimer || settings.ShowKeystrokeOverlay || settings.ShowRecordingBorder
            ? $"Overlays: border={(settings.ShowRecordingBorder ? "on" : "off")}, timer={(settings.ShowRecordingTimer ? settings.RecordingTimerPosition : "off")}, keys={(settings.ShowKeystrokeOverlay ? settings.KeystrokeOverlayPosition : "off")}, badge {settings.RecordingOverlayBadgeFontSize}px {settings.RecordingOverlayStyle}."
            : "Overlays: off.";
        var audio = audioInputCount > 0
            ? "Native AAC audio mux/mix enabled for captured WAV input(s)."
            : "Native video-only production encode.";
        var webcam = settings.EnableWebcamOverlay
            ? "Webcam overlay frames are precomposited before native encode with bounded native camera-frame refresh."
            : "Native webcam overlay not requested.";
        return $"{settings.QualityProfile}, {settings.FramesPerSecond} fps, {settings.SizeLabel}, {settings.BitrateLabel}. {overlays} {audio} {webcam}";
    }

    private static ComSample CreateSampleFromBitmap(Bitmap source, long sampleTime, long sampleDuration)
    {
        // No format clone needed: CopyBitmapToBgraBuffer locks with Format32bppArgb,
        // and GDI+ converts on read for other source formats.
        var rowBytes = checked(source.Width * 4);
        var bufferBytes = checked(rowBytes * source.Height);
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            CheckHResult(MFCreateMemoryBuffer(bufferBytes, out buffer), "create frame buffer");
            CheckHResult(buffer.Lock(out var destination, out _, out _), "lock frame buffer");
            try
            {
                CopyBitmapToBgraBuffer(source, destination, rowBytes);
            }
            finally
            {
                CheckHResult(buffer.Unlock(), "unlock frame buffer");
            }

            CheckHResult(buffer.SetCurrentLength(bufferBytes), "set frame buffer length");
            CheckHResult(MFCreateSample(out sample), "create sample");
            CheckHResult(sample.AddBuffer(buffer), "add sample buffer");
            CheckHResult(sample.SetSampleTime(sampleTime), "set sample time");
            CheckHResult(sample.SetSampleDuration(sampleDuration), "set sample duration");

            var wrapped = new ComSample(sample, buffer);
            sample = null;
            buffer = null;
            return wrapped;
        }
        finally
        {
            if (sample is not null)
            {
                Marshal.FinalReleaseComObject(sample);
            }

            if (buffer is not null)
            {
                Marshal.FinalReleaseComObject(buffer);
            }
        }
    }

    private static ComSample CreateSampleFromBytes(byte[] source, int offset, int count, long sampleTime, long sampleDuration)
    {
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            CheckHResult(MFCreateMemoryBuffer(count, out buffer), "create audio buffer");
            CheckHResult(buffer.Lock(out var destination, out _, out _), "lock audio buffer");
            try
            {
                Marshal.Copy(source, offset, destination, count);
            }
            finally
            {
                CheckHResult(buffer.Unlock(), "unlock audio buffer");
            }

            CheckHResult(buffer.SetCurrentLength(count), "set audio buffer length");
            CheckHResult(MFCreateSample(out sample), "create audio sample");
            CheckHResult(sample.AddBuffer(buffer), "add audio sample buffer");
            CheckHResult(sample.SetSampleTime(sampleTime), "set audio sample time");
            CheckHResult(sample.SetSampleDuration(sampleDuration), "set audio sample duration");

            var wrapped = new ComSample(sample, buffer);
            sample = null;
            buffer = null;
            return wrapped;
        }
        finally
        {
            if (sample is not null)
            {
                Marshal.FinalReleaseComObject(sample);
            }

            if (buffer is not null)
            {
                Marshal.FinalReleaseComObject(buffer);
            }
        }
    }

    private static void CopyBitmapToBgraBuffer(Bitmap bitmap, IntPtr destination, int rowBytes)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var absoluteStride = Math.Abs(stride);
            var row = new byte[rowBytes];
            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = stride < 0
                    ? IntPtr.Add(data.Scan0, (bitmap.Height - 1 - y) * absoluteStride)
                    : IntPtr.Add(data.Scan0, y * stride);
                Marshal.Copy(sourceRow, row, 0, rowBytes);
                for (var alpha = 3; alpha < rowBytes; alpha += 4)
                {
                    row[alpha] = byte.MaxValue;
                }

                Marshal.Copy(row, 0, IntPtr.Add(destination, y * rowBytes), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    internal sealed record NativeAudioPayload(
        byte[] Pcm16,
        int SampleRate,
        int Channels,
        int BitsPerSample,
        int SourceCount)
    {
        public int AverageBytesPerSecond => SampleRate * Channels * (BitsPerSample / 8);
        public double DurationSeconds => Pcm16.Length / (double)AverageBytesPerSecond;
    }

    private sealed class StereoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private readonly float[] _sourceBuffer;

        public StereoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = Math.Max(1, source.WaveFormat.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, NativeAudioChannels);
            _sourceBuffer = new float[NativeAudioFramesPerSample * _sourceChannels];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var requestedFrames = Math.Min(count / NativeAudioChannels, NativeAudioFramesPerSample);
            if (requestedFrames <= 0)
            {
                return 0;
            }

            var sourceSamplesRequested = requestedFrames * _sourceChannels;
            var read = _source.Read(_sourceBuffer, 0, sourceSamplesRequested);
            var framesRead = read / _sourceChannels;
            for (var frame = 0; frame < framesRead; frame++)
            {
                var baseIndex = frame * _sourceChannels;
                var left = _sourceBuffer[baseIndex];
                var right = _sourceChannels > 1 ? _sourceBuffer[baseIndex + 1] : left;
                if (_sourceChannels > 2)
                {
                    for (var channel = 2; channel < _sourceChannels; channel++)
                    {
                        if ((channel & 1) == 0)
                        {
                            left += _sourceBuffer[baseIndex + channel] * 0.5f;
                        }
                        else
                        {
                            right += _sourceBuffer[baseIndex + channel] * 0.5f;
                        }
                    }
                }

                var target = offset + frame * NativeAudioChannels;
                buffer[target] = Math.Clamp(left, -1f, 1f);
                buffer[target + 1] = Math.Clamp(right, -1f, 1f);
            }

            return framesRead * NativeAudioChannels;
        }
    }

    private static ulong PackRatio(int numerator, int denominator)
    {
        return ((ulong)(uint)numerator << 32) | (uint)denominator;
    }

    private static void CheckHResult(int hr, string operation)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr:X8}.", Marshal.GetExceptionForHR(hr));
        }
    }

    private sealed class ComSample : IDisposable
    {
        private IMFSample? _sample;
        private IMFMediaBuffer? _buffer;

        public ComSample(IMFSample sample, IMFMediaBuffer buffer)
        {
            _sample = sample;
            _buffer = buffer;
        }

        public IMFSample Sample => _sample ?? throw new ObjectDisposedException(nameof(ComSample));

        public void Dispose()
        {
            if (_sample is not null)
            {
                Marshal.FinalReleaseComObject(_sample);
                _sample = null;
            }

            if (_buffer is not null)
            {
                Marshal.FinalReleaseComObject(_buffer);
                _buffer = null;
            }
        }
    }

    private static readonly Guid MfMtMajorType = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    private static readonly Guid MfMtSubtype = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    private static readonly Guid MfMtAvgBitrate = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    private static readonly Guid MfMtInterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    private static readonly Guid MfMtFrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    private static readonly Guid MfMtFrameRate = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    private static readonly Guid MfMtPixelAspectRatio = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
    private static readonly Guid MfMtDefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    private static readonly Guid MfMtAudioBitsPerSample = new("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
    private static readonly Guid MfMtAudioSamplesPerSecond = new("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
    private static readonly Guid MfMtAudioNumChannels = new("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
    private static readonly Guid MfMtAudioAvgBytesPerSecond = new("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
    private static readonly Guid MfMtAudioBlockAlignment = new("322DE230-9EEB-43BD-AB7A-FF412251541D");

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatHevc = new("43564548-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatAac = new("00001610-0000-0010-8000-00AA00389B71");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSinkWriterFromURL(
        string outputUrl,
        IntPtr byteStream,
        IntPtr attributes,
        out IMFSinkWriter sinkWriter);

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig]
        int GetItem(in Guid guidKey, IntPtr value);

        [PreserveSig]
        int GetItemType(in Guid guidKey, out int type);

        [PreserveSig]
        int CompareItem(in Guid guidKey, IntPtr value, out bool result);

        [PreserveSig]
        int Compare(IMFAttributes attributes, int matchType, out bool result);

        [PreserveSig]
        int GetUINT32(in Guid guidKey, out int value);

        [PreserveSig]
        int GetUINT64(in Guid guidKey, out ulong value);

        [PreserveSig]
        int GetDouble(in Guid guidKey, out double value);

        [PreserveSig]
        int GetGUID(in Guid guidKey, out Guid value);

        [PreserveSig]
        int GetStringLength(in Guid guidKey, out int length);

        [PreserveSig]
        int GetString(in Guid guidKey, IntPtr value, int size, out int length);

        [PreserveSig]
        int GetAllocatedString(in Guid guidKey, out IntPtr value, out int length);

        [PreserveSig]
        int GetBlobSize(in Guid guidKey, out int size);

        [PreserveSig]
        int GetBlob(in Guid guidKey, IntPtr buffer, int bufferSize, out int blobSize);

        [PreserveSig]
        int GetAllocatedBlob(in Guid guidKey, out IntPtr buffer, out int size);

        [PreserveSig]
        int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr unknown);

        [PreserveSig]
        int SetItem(in Guid guidKey, IntPtr value);

        [PreserveSig]
        int DeleteItem(in Guid guidKey);

        [PreserveSig]
        int DeleteAllItems();

        [PreserveSig]
        int SetUINT32(in Guid guidKey, int value);

        [PreserveSig]
        int SetUINT64(in Guid guidKey, ulong value);

        [PreserveSig]
        int SetDouble(in Guid guidKey, double value);

        [PreserveSig]
        int SetGUID(in Guid guidKey, in Guid value);

        [PreserveSig]
        int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [PreserveSig]
        int SetBlob(in Guid guidKey, IntPtr buffer, int bufferSize);

        [PreserveSig]
        int SetUnknown(in Guid guidKey, IntPtr unknown);

        [PreserveSig]
        int LockStore();

        [PreserveSig]
        int UnlockStore();

        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetItemByIndex(int index, out Guid guidKey, IntPtr value);

        [PreserveSig]
        int CopyAllItems(IMFAttributes destination);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType : IMFAttributes
    {
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig]
        int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

        [PreserveSig]
        int Unlock();

        [PreserveSig]
        int GetCurrentLength(out int currentLength);

        [PreserveSig]
        int SetCurrentLength(int currentLength);

        [PreserveSig]
        int GetMaxLength(out int maxLength);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig]
        int GetItem(in Guid guidKey, IntPtr value);

        [PreserveSig]
        int GetItemType(in Guid guidKey, out int type);

        [PreserveSig]
        int CompareItem(in Guid guidKey, IntPtr value, out bool result);

        [PreserveSig]
        int Compare(IMFAttributes attributes, int matchType, out bool result);

        [PreserveSig]
        int GetUINT32(in Guid guidKey, out int value);

        [PreserveSig]
        int GetUINT64(in Guid guidKey, out ulong value);

        [PreserveSig]
        int GetDouble(in Guid guidKey, out double value);

        [PreserveSig]
        int GetGUID(in Guid guidKey, out Guid value);

        [PreserveSig]
        int GetStringLength(in Guid guidKey, out int length);

        [PreserveSig]
        int GetString(in Guid guidKey, IntPtr value, int size, out int length);

        [PreserveSig]
        int GetAllocatedString(in Guid guidKey, out IntPtr value, out int length);

        [PreserveSig]
        int GetBlobSize(in Guid guidKey, out int size);

        [PreserveSig]
        int GetBlob(in Guid guidKey, IntPtr buffer, int bufferSize, out int blobSize);

        [PreserveSig]
        int GetAllocatedBlob(in Guid guidKey, out IntPtr buffer, out int size);

        [PreserveSig]
        int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr unknown);

        [PreserveSig]
        int SetItem(in Guid guidKey, IntPtr value);

        [PreserveSig]
        int DeleteItem(in Guid guidKey);

        [PreserveSig]
        int DeleteAllItems();

        [PreserveSig]
        int SetUINT32(in Guid guidKey, int value);

        [PreserveSig]
        int SetUINT64(in Guid guidKey, ulong value);

        [PreserveSig]
        int SetDouble(in Guid guidKey, double value);

        [PreserveSig]
        int SetGUID(in Guid guidKey, in Guid value);

        [PreserveSig]
        int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [PreserveSig]
        int SetBlob(in Guid guidKey, IntPtr buffer, int bufferSize);

        [PreserveSig]
        int SetUnknown(in Guid guidKey, IntPtr unknown);

        [PreserveSig]
        int LockStore();

        [PreserveSig]
        int UnlockStore();

        [PreserveSig]
        int GetCount(out int count);

        [PreserveSig]
        int GetItemByIndex(int index, out Guid guidKey, IntPtr value);

        [PreserveSig]
        int CopyAllItems(IMFAttributes destination);

        [PreserveSig]
        int GetSampleFlags(out int flags);

        [PreserveSig]
        int SetSampleFlags(int flags);

        [PreserveSig]
        int GetSampleTime(out long sampleTime);

        [PreserveSig]
        int SetSampleTime(long sampleTime);

        [PreserveSig]
        int GetSampleDuration(out long sampleDuration);

        [PreserveSig]
        int SetSampleDuration(long sampleDuration);

        [PreserveSig]
        int GetBufferCount(out int count);

        [PreserveSig]
        int GetBufferByIndex(int index, out IMFMediaBuffer buffer);

        [PreserveSig]
        int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);

        [PreserveSig]
        int AddBuffer(IMFMediaBuffer buffer);

        [PreserveSig]
        int RemoveBufferByIndex(int index);

        [PreserveSig]
        int RemoveAllBuffers();

        [PreserveSig]
        int GetTotalLength(out int totalLength);

        [PreserveSig]
        int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig]
        int AddStream(IMFMediaType targetMediaType, out int streamIndex);

        [PreserveSig]
        int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IntPtr encodingParameters);

        [PreserveSig]
        int BeginWriting();

        [PreserveSig]
        int WriteSample(int streamIndex, IMFSample sample);

        [PreserveSig]
        int SendStreamTick(int streamIndex, long timestamp);

        [PreserveSig]
        int PlaceMarker(int streamIndex, IntPtr context);

        [PreserveSig]
        int NotifyEndOfSegment(int streamIndex);

        [PreserveSig]
        int Flush(int streamIndex);

        [PreserveSig]
        int Finalize_();

        [PreserveSig]
        int GetServiceForStream(int streamIndex, in Guid serviceGuid, in Guid interfaceId, out IntPtr service);

        [PreserveSig]
        int GetStatistics(int streamIndex, IntPtr statistics);
    }
}

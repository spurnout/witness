using NAudio.Wave;

namespace GoatShot.App.Services;

/// <summary>
/// Incrementally converts a WASAPI format to the recording contract's 48 kHz stereo PCM16.
/// The resampler retains only the one-frame interpolation boundary plus the current callback.
/// </summary>
internal sealed class StreamingPcmNormalizer
{
    private readonly WaveFormat _sourceFormat;
    private readonly List<float> _stereoFrames = [];
    private double _nextSourceFrame;
    private long _outputFrames;

    public StreamingPcmNormalizer(WaveFormat sourceFormat)
    {
        ArgumentNullException.ThrowIfNull(sourceFormat);
        sourceFormat = sourceFormat is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : sourceFormat;
        if (sourceFormat.SampleRate <= 0 || sourceFormat.Channels <= 0)
        {
            throw new ArgumentException("Audio source format must have a positive sample rate and channel count.", nameof(sourceFormat));
        }

        if (!CanDecode(sourceFormat))
        {
            throw new NotSupportedException(
                $"Streaming PCM normalization does not support {sourceFormat.Encoding}/{sourceFormat.BitsPerSample}-bit audio.");
        }

        _sourceFormat = sourceFormat;
    }

    public static bool CanDecode(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32 ||
        format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 16 or 24 or 32;

    public IReadOnlyList<StreamingAudioChunk> Push(byte[] buffer, int bytesRecorded)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (bytesRecorded <= 0)
        {
            return [];
        }

        var boundedLength = Math.Min(bytesRecorded, buffer.Length);
        var bytesPerSourceSample = _sourceFormat.BitsPerSample / 8;
        var bytesPerFrame = bytesPerSourceSample * _sourceFormat.Channels;
        var frameCount = boundedLength / bytesPerFrame;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * bytesPerFrame;
            var left = ReadSample(buffer, frameOffset, bytesPerSourceSample);
            var right = _sourceFormat.Channels > 1
                ? ReadSample(buffer, frameOffset + bytesPerSourceSample, bytesPerSourceSample)
                : left;
            for (var channel = 2; channel < _sourceFormat.Channels; channel++)
            {
                var value = ReadSample(
                    buffer,
                    frameOffset + channel * bytesPerSourceSample,
                    bytesPerSourceSample) * 0.5f;
                if ((channel & 1) == 0)
                {
                    left += value;
                }
                else
                {
                    right += value;
                }
            }

            _stereoFrames.Add(Math.Clamp(left, -1f, 1f));
            _stereoFrames.Add(Math.Clamp(right, -1f, 1f));
        }

        return Drain(flush: false);
    }

    public IReadOnlyList<StreamingAudioChunk> Flush() => Drain(flush: true);

    private IReadOnlyList<StreamingAudioChunk> Drain(bool flush)
    {
        const int outputFramesPerChunk = 2_048;
        var chunks = new List<StreamingAudioChunk>();
        var output = new byte[outputFramesPerChunk * StreamingRecordingAudioSession.BytesPerFrame];
        var outputFrameCount = 0;
        var availableFrames = _stereoFrames.Count / 2;
        var step = _sourceFormat.SampleRate / (double)StreamingRecordingAudioSession.SampleRate;

        while (_nextSourceFrame < availableFrames &&
               (flush || _nextSourceFrame + 1d < availableFrames))
        {
            var lower = (int)Math.Floor(_nextSourceFrame);
            var upper = Math.Min(availableFrames - 1, lower + 1);
            var fraction = (float)(_nextSourceFrame - lower);
            for (var channel = 0; channel < StreamingRecordingAudioSession.Channels; channel++)
            {
                var a = _stereoFrames[lower * 2 + channel];
                var b = _stereoFrames[upper * 2 + channel];
                var sample = a + (b - a) * fraction;
                var pcm = (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
                var byteOffset = outputFrameCount * StreamingRecordingAudioSession.BytesPerFrame + channel * 2;
                output[byteOffset] = (byte)(pcm & 0xff);
                output[byteOffset + 1] = (byte)((pcm >> 8) & 0xff);
            }

            outputFrameCount++;
            _nextSourceFrame += step;
            if (outputFrameCount == outputFramesPerChunk)
            {
                chunks.Add(CreateChunk(output, outputFrameCount));
                _outputFrames += outputFrameCount;
                output = new byte[outputFramesPerChunk * StreamingRecordingAudioSession.BytesPerFrame];
                outputFrameCount = 0;
            }
        }

        if (outputFrameCount > 0)
        {
            chunks.Add(CreateChunk(output, outputFrameCount));
            _outputFrames += outputFrameCount;
        }

        var consumedFrames = Math.Min(
            availableFrames,
            Math.Max(0, (int)Math.Floor(_nextSourceFrame)));
        if (consumedFrames > 0)
        {
            _stereoFrames.RemoveRange(0, consumedFrames * 2);
            _nextSourceFrame -= consumedFrames;
        }

        return chunks;
    }

    private StreamingAudioChunk CreateChunk(byte[] buffer, int frameCount)
    {
        var byteCount = frameCount * StreamingRecordingAudioSession.BytesPerFrame;
        if (byteCount != buffer.Length)
        {
            Array.Resize(ref buffer, byteCount);
        }

        return new StreamingAudioChunk(
            buffer,
            TimeSpan.FromSeconds(_outputFrames / (double)StreamingRecordingAudioSession.SampleRate));
    }

    private float ReadSample(byte[] buffer, int offset, int bytesPerSample)
    {
        if (_sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(buffer, offset) / 32768f,
            3 => ReadPcm24(buffer, offset) / 8_388_608f,
            4 => BitConverter.ToInt32(buffer, offset) / 2_147_483_648f,
            _ => 0f
        };
    }

    private static int ReadPcm24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        return (value & 0x0080_0000) != 0 ? value | unchecked((int)0xff00_0000) : value;
    }
}

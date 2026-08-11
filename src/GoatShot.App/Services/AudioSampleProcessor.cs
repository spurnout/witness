using NAudio.Wave;

namespace GoatShot.App.Services;

public static class AudioSampleProcessor
{
    public const double DisabledNoiseGateDb = -96d;
    public const double MaxGain = 4d;

    public static AudioCaptureProcessingSettings Normalize(AudioCaptureProcessingSettings? settings)
    {
        if (settings is null)
        {
            return new AudioCaptureProcessingSettings();
        }

        var gain = double.IsFinite(settings.Gain)
            ? Math.Clamp(settings.Gain, 0d, MaxGain)
            : 1d;
        var gate = double.IsFinite(settings.NoiseGateThresholdDb)
            ? Math.Clamp(settings.NoiseGateThresholdDb, DisabledNoiseGateDb, 0d)
            : DisabledNoiseGateDb;
        return new AudioCaptureProcessingSettings(gain, gate, settings.Muted);
    }

    public static bool HasProcessing(AudioCaptureProcessingSettings? settings)
    {
        var normalized = Normalize(settings);
        return normalized.Muted ||
            Math.Abs(normalized.Gain - 1d) > 0.001d ||
            normalized.NoiseGateThresholdDb > DisabledNoiseGateDb + 0.001d;
    }

    public static bool CanProcess(WaveFormat format)
    {
        format = NormalizeWaveFormat(format);
        return format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16 ||
            format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
    }

    public static string Describe(AudioCaptureProcessingSettings? settings)
    {
        var normalized = Normalize(settings);
        if (!HasProcessing(normalized))
        {
            return "audio processing disabled";
        }

        if (normalized.Muted)
        {
            return "muted";
        }

        var parts = new List<string>();
        if (Math.Abs(normalized.Gain - 1d) > 0.001d)
        {
            parts.Add($"gain {normalized.Gain:0.##}x");
        }

        if (normalized.NoiseGateThresholdDb > DisabledNoiseGateDb + 0.001d)
        {
            parts.Add($"noise gate {normalized.NoiseGateThresholdDb:0.#} dB");
        }

        return parts.Count == 0 ? "audio processing disabled" : string.Join(", ", parts);
    }

    public static int ApplyInPlace(
        byte[] buffer,
        int bytesRecorded,
        WaveFormat format,
        AudioCaptureProcessingSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(format);

        var normalized = Normalize(settings);
        format = NormalizeWaveFormat(format);
        var safeBytes = Math.Clamp(bytesRecorded, 0, buffer.Length);
        if (safeBytes == 0 || !HasProcessing(normalized))
        {
            return 0;
        }

        if (normalized.Muted)
        {
            Array.Clear(buffer, 0, safeBytes);
            return safeBytes;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            return ApplyPcm16(buffer, safeBytes, normalized);
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            return ApplyFloat32(buffer, safeBytes, normalized);
        }

        return 0;
    }

    private static WaveFormat NormalizeWaveFormat(WaveFormat format) =>
        format is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : format;

    private static int ApplyPcm16(
        byte[] buffer,
        int bytesRecorded,
        AudioCaptureProcessingSettings settings)
    {
        var samples = bytesRecorded / sizeof(short);
        for (var i = 0; i < samples; i++)
        {
            var offset = i * sizeof(short);
            var sample = BitConverter.ToInt16(buffer, offset) / 32768d;
            var processed = ProcessSample(sample, settings);
            var scaled = (short)Math.Clamp(
                Math.Round(processed * short.MaxValue),
                short.MinValue,
                short.MaxValue);
            var bytes = BitConverter.GetBytes(scaled);
            buffer[offset] = bytes[0];
            buffer[offset + 1] = bytes[1];
        }

        return samples;
    }

    private static int ApplyFloat32(
        byte[] buffer,
        int bytesRecorded,
        AudioCaptureProcessingSettings settings)
    {
        var samples = bytesRecorded / sizeof(float);
        for (var i = 0; i < samples; i++)
        {
            var offset = i * sizeof(float);
            var sample = BitConverter.ToSingle(buffer, offset);
            var processed = (float)ProcessSample(sample, settings);
            var bytes = BitConverter.GetBytes(processed);
            buffer[offset] = bytes[0];
            buffer[offset + 1] = bytes[1];
            buffer[offset + 2] = bytes[2];
            buffer[offset + 3] = bytes[3];
        }

        return samples;
    }

    private static double ProcessSample(double sample, AudioCaptureProcessingSettings settings)
    {
        var gateAmplitude = settings.NoiseGateThresholdDb > DisabledNoiseGateDb + 0.001d
            ? Math.Pow(10d, settings.NoiseGateThresholdDb / 20d)
            : 0d;
        if (gateAmplitude > 0d && Math.Abs(sample) < gateAmplitude)
        {
            return 0d;
        }

        return Math.Clamp(sample * settings.Gain, -1d, 1d);
    }
}

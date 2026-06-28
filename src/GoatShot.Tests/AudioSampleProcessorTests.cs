using GoatShot.App.Services;
using NAudio.Wave;

namespace GoatShot.Tests;

[TestClass]
public sealed class AudioSampleProcessorTests
{
    [TestMethod]
    public void ApplyInPlace_Pcm16AppliesGainAndNoiseGate()
    {
        var buffer = new byte[sizeof(short) * 3];
        WriteInt16(buffer, 0, 1000);
        WriteInt16(buffer, 2, 8000);
        WriteInt16(buffer, 4, -8000);

        var processed = AudioSampleProcessor.ApplyInPlace(
            buffer,
            buffer.Length,
            new WaveFormat(48_000, 16, 1),
            new AudioCaptureProcessingSettings(2d, -18d));

        Assert.AreEqual(3, processed);
        Assert.AreEqual(0, BitConverter.ToInt16(buffer, 0));
        Assert.AreEqual(16000, BitConverter.ToInt16(buffer, 2));
        Assert.AreEqual(-16000, BitConverter.ToInt16(buffer, 4));
    }

    [TestMethod]
    public void ApplyInPlace_Float32CanMuteUnsupportedTrackSafely()
    {
        var buffer = new byte[sizeof(float) * 2];
        WriteFloat(buffer, 0, 0.5f);
        WriteFloat(buffer, 4, -0.5f);

        var processed = AudioSampleProcessor.ApplyInPlace(
            buffer,
            buffer.Length,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1),
            new AudioCaptureProcessingSettings(Muted: true));

        Assert.AreEqual(buffer.Length, processed);
        Assert.AreEqual(0f, BitConverter.ToSingle(buffer, 0));
        Assert.AreEqual(0f, BitConverter.ToSingle(buffer, 4));
    }

    [TestMethod]
    public void Normalize_ClampsUnsafeProcessingValues()
    {
        var normalized = AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(99d, 12d));

        Assert.AreEqual(AudioSampleProcessor.MaxGain, normalized.Gain);
        Assert.AreEqual(0d, normalized.NoiseGateThresholdDb);
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        var bytes = BitConverter.GetBytes(value);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
    }

    private static void WriteFloat(byte[] buffer, int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
        buffer[offset + 2] = bytes[2];
        buffer[offset + 3] = bytes[3];
    }
}

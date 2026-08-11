using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(CancellationToken cancellationToken);
    Task<AudioCaptureResult> CaptureWavAsync(AudioCaptureRequest request, CancellationToken cancellationToken);
    Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Opens event-driven PCM capture sessions for screen-recording encoders. Implementations
/// must keep chunks bounded and must not retain the full recording in memory.
/// </summary>
public interface IStreamingAudioCaptureService
{
    IStreamingAudioCaptureSession StartStreaming(
        StreamingAudioCaptureRequest request,
        Action<StreamingAudioChunk> onChunk,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A live audio endpoint. Stopping finalizes and reports any PCM already delivered, including
/// a valid partial capture when a Replay segment rotates before its nominal duration.
/// </summary>
public interface IStreamingAudioCaptureSession : IAsyncDisposable
{
    AudioCaptureSource Source { get; }
    Task<StreamingAudioCaptureResult> StopAsync(CancellationToken cancellationToken = default);
}

public sealed record AudioCaptureDevice(
    string Id,
    string DisplayName,
    bool IsDefault,
    bool SupportsLoopback,
    double? PeakLevel = null,
    string MeterStatus = "");

[JsonConverter(typeof(JsonStringEnumConverter<AudioCaptureSource>))]
public enum AudioCaptureSource
{
    Microphone,
    SystemAudio
}

public sealed record AudioCaptureRequest(
    AudioCaptureSource Source,
    TimeSpan Duration,
    string OutputPath,
    string DeviceId = "",
    AudioCaptureProcessingSettings? Processing = null);

public sealed record AudioCaptureProcessingSettings(
    double Gain = 1d,
    double NoiseGateThresholdDb = -96d,
    bool Muted = false);

public sealed record StreamingAudioCaptureRequest(
    AudioCaptureSource Source,
    string DeviceId = "",
    AudioCaptureProcessingSettings? Processing = null);

/// <summary>
/// A normalized PCM chunk. Recording capture always emits signed 16-bit little-endian,
/// 48 kHz stereo data so downstream mixers and encoders share one deterministic contract.
/// </summary>
public sealed record StreamingAudioChunk(
    ReadOnlyMemory<byte> Pcm16,
    TimeSpan Timestamp,
    int SampleRate = StreamingRecordingAudioSession.SampleRate,
    int Channels = StreamingRecordingAudioSession.Channels,
    int BitsPerSample = StreamingRecordingAudioSession.BitsPerSample);

public sealed record StreamingAudioCaptureResult(
    bool Succeeded,
    string Message,
    TimeSpan Duration,
    long PcmBytesProduced,
    AudioCaptureDevice? Device);

public sealed record AudioCaptureResult(
    bool Succeeded,
    string? OutputPath,
    string Message,
    TimeSpan Duration,
    long BytesWritten,
    AudioCaptureDevice? Device);

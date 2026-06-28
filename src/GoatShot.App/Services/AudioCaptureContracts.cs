using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(CancellationToken cancellationToken);
    Task<AudioCaptureResult> CaptureWavAsync(AudioCaptureRequest request, CancellationToken cancellationToken);
    Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken);
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

public sealed record AudioCaptureResult(
    bool Succeeded,
    string? OutputPath,
    string Message,
    TimeSpan Duration,
    long BytesWritten,
    AudioCaptureDevice? Device);

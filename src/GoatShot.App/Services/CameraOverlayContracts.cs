using System.Drawing;

namespace GoatShot.App.Services;

public interface ICameraOverlayService
{
    Task<IReadOnlyList<CameraOverlayDevice>> ListDevicesAsync(CancellationToken cancellationToken);
    Task<CameraOverlayFrameResult> CaptureFrameAsync(string deviceId, CancellationToken cancellationToken);
    Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken);
}

public sealed record CameraOverlayDevice(
    string Id,
    string DisplayName,
    bool IsDefault,
    string Status = "");

public sealed record CameraOverlayFrameResult(
    bool Succeeded,
    Bitmap? Frame,
    CameraOverlayDevice? Device,
    string Message) : IDisposable
{
    public void Dispose()
    {
        Frame?.Dispose();
    }
}

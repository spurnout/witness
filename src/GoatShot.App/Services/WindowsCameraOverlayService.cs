using System.Drawing;
using Windows.Graphics.Imaging;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace GoatShot.App.Services;

public sealed class WindowsCameraOverlayService : ICameraOverlayService
{
    public async Task<IReadOnlyList<CameraOverlayDevice>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        cancellationToken.ThrowIfCancellationRequested();

        return devices
            .Select((device, index) => new CameraOverlayDevice(
                device.Id,
                string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                IsDefault: index == 0))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CameraOverlayFrameResult> CaptureFrameAsync(string deviceId, CancellationToken cancellationToken)
    {
        IReadOnlyList<CameraOverlayDevice> devices;
        try
        {
            devices = await ListDevicesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed($"Windows camera device discovery failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (devices.Count == 0)
        {
            return Failed("No Windows camera devices were found.");
        }

        var device = ResolveDevice(devices, deviceId);
        if (device is null)
        {
            return Failed($"Selected camera is unavailable: {deviceId}.");
        }

        using var mediaCapture = new MediaCapture();
        try
        {
            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                VideoDeviceId = device.Id
            });

            using var randomAccessStream = new InMemoryRandomAccessStream();
            await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), randomAccessStream);
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = await ReadAllBytesAsync(randomAccessStream, cancellationToken);
            if (bytes.Length == 0)
            {
                return Failed($"Camera {device.DisplayName} produced an empty frame.");
            }

            using var memory = new MemoryStream(bytes);
            using var decoded = new Bitmap(memory);
            return new CameraOverlayFrameResult(
                true,
                (Bitmap)decoded.Clone(),
                device,
                $"Captured a native camera frame from {device.DisplayName}.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed($"Camera access was denied by Windows privacy settings or consent prompt: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed($"Native camera frame capture failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cameras = await ListDevicesAsync(cancellationToken);
            return new ProviderHealth(
                cameras.Count > 0,
                $"Windows camera device discovery found {cameras.Count} video capture device(s).");
        }
        catch (Exception ex)
        {
            return new ProviderHealth(false, $"Windows camera device discovery failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static CameraOverlayDevice? ResolveDevice(IReadOnlyList<CameraOverlayDevice> devices, string deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var requested = deviceId.Trim();
            return devices.FirstOrDefault(device =>
                device.Id.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                device.DisplayName.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                requested.Contains(device.Id, StringComparison.OrdinalIgnoreCase) ||
                device.Id.Contains(requested, StringComparison.OrdinalIgnoreCase));
        }

        return devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        stream.Seek(0);
        var size = checked((uint)stream.Size);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static CameraOverlayFrameResult Failed(string message)
    {
        return new CameraOverlayFrameResult(false, null, null, message);
    }
}

using GoatShot.App.Models;

namespace GoatShot.App.Services;

public interface ICaptureEngine
{
    string EngineName { get; }
    bool IsProductionEngine { get; }
    Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken);
    Task<CapturedBitmap?> CaptureAsync(CaptureEngineRequest request, CancellationToken cancellationToken);
}

public sealed record CaptureEngineRequest(
    CaptureKind Kind,
    CaptureBounds? Bounds = null,
    bool IncludeCursor = true,
    string? TargetWindowTitle = null,
    string? TargetProcessName = null,
    string? MonitorName = null);

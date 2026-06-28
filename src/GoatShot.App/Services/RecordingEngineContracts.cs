using GoatShot.App.Models;

namespace GoatShot.App.Services;

public interface IRecordingEngine
{
    string EngineName { get; }
    bool IsProductionEngine { get; }
    Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken);
    Task<RecordingResult> RecordAsync(RecordingEngineRequest request, CancellationToken cancellationToken);
}

public sealed record RecordingEngineRequest(
    CaptureKind Mode,
    TimeSpan Duration,
    string OutputPath,
    RecordingSettings Settings,
    bool AddToWorkspace);

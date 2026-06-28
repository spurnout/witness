using GoatShot.App.Models;

namespace GoatShot.App.Services;

public interface IAiImageProvider
{
    string ProviderName { get; }
    Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken cancellationToken);
    Task<AiImageEditResult> EditImageAsync(AiImageEditRequest request, CancellationToken cancellationToken);
    Task<AiImageAnalysisResult> AnalyzeImageAsync(string imagePath, string prompt, string modelId, CancellationToken cancellationToken);
}

public sealed record AiImageEditRequest(
    string ImagePath,
    string Prompt,
    string ModelId,
    bool RequiresUserConfirmation);

public sealed record AiImageEditResult(
    bool Succeeded,
    string? OutputImagePath,
    string Message);

public sealed record AiImageAnalysisResult(
    bool Succeeded,
    string? Text,
    string Message);

public sealed record AiModelDescriptor(
    string Id,
    string DisplayName,
    bool SupportsImageEdit,
    bool SupportsImageAnalysis,
    string Source);

public interface IShareProvider
{
    ShareDestination? Destination { get; }
    string ProviderName { get; }
    string AuthType { get; }
    bool IsImplemented { get; }
    bool SupportsPublicLinks { get; }
    bool SupportsPrivateLinks { get; }
    bool SupportsExpiration { get; }
    bool SupportsPassword { get; }
    Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken);
    Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken);
}

public sealed record ShareUploadRequest(
    string FilePath,
    string CaptureType,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ShareUploadResult(
    bool Succeeded,
    string? ShareUrl,
    string Message);

public sealed record ProviderHealth(
    bool IsHealthy,
    string Message);

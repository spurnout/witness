namespace GoatShot.App.Models;

public sealed record CaptureTaskActionViewModel(
    CaptureTaskActionKind Kind,
    string Title,
    string Description,
    bool IsEnabled,
    string DisabledReason);

public sealed record CaptureTaskViewModel(
    string Title,
    string FileName,
    string FilePath,
    string CaptureType,
    string Size,
    string PrivacySummary,
    IReadOnlyList<CaptureTaskActionViewModel> Actions);

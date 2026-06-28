namespace GoatShot.App.Services;

public sealed record RecordingDeviceSnapshot(
    IReadOnlyList<AudioCaptureDevice> Microphones,
    IReadOnlyList<AudioCaptureDevice> SystemAudioDevices,
    IReadOnlyList<CameraOverlayDevice> Cameras,
    string SelectionSummary,
    IReadOnlyList<string> Issues,
    string AudioCaptureSummary = "")
{
    public RecordingConfidenceReport Confidence { get; init; } = RecordingConfidenceReport.Empty;
}

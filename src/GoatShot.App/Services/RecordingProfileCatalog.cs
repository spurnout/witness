using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class RecordingProfileCatalog
{
    public static IReadOnlyList<RecordingWorkflowProfile> CreateDefaultProfiles()
    {
        return new[]
        {
            Create(
                "Small",
                "Legacy low-bitrate quick-share MP4 profile with compact dimensions.",
                new RecordingSettings
                {
                    QualityProfile = "Small",
                    FramesPerSecond = 10,
                    TargetWidth = 1280,
                    TargetHeight = 720,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true
                }),
            Create(
                "Balanced",
                "Legacy default MP4 profile for bug reports and documentation clips.",
                new RecordingSettings
                {
                    QualityProfile = "Balanced",
                    FramesPerSecond = 15,
                    TargetWidth = 1920,
                    TargetHeight = 1080,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true
                }),
            Create(
                "High quality",
                "Legacy higher-fidelity local recording profile.",
                new RecordingSettings
                {
                    QualityProfile = "High quality",
                    FramesPerSecond = 30,
                    TargetWidth = 0,
                    TargetHeight = 0,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true
                }),
            Create(
                "Small Share",
                "Compact 720p profile for fast sharing and low upload weight.",
                new RecordingSettings
                {
                    QualityProfile = "Small",
                    FramesPerSecond = 15,
                    TargetWidth = 1280,
                    TargetHeight = 720,
                    BitrateKbps = 2500,
                    UseVariableBitrate = false,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true,
                    RecordingOverlayStyle = "Subtle"
                }),
            Create(
                "1080p60",
                "High-motion 1080p60 profile for product walkthroughs and reproduction clips.",
                new RecordingSettings
                {
                    QualityProfile = "High quality",
                    FramesPerSecond = 60,
                    TargetWidth = 1920,
                    TargetHeight = 1080,
                    BitrateKbps = 12000,
                    UseVariableBitrate = false,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true
                }),
            Create(
                "4K60",
                "High-fidelity 4K60 local profile for detailed desktop or design proof.",
                new RecordingSettings
                {
                    QualityProfile = "Archive",
                    FramesPerSecond = 60,
                    TargetWidth = 3840,
                    TargetHeight = 2160,
                    BitrateKbps = 45000,
                    UseVariableBitrate = false,
                    ShowRecordingBorder = true,
                    ShowRecordingTimer = true
                })
        };
    }

    private static RecordingWorkflowProfile Create(
        string name,
        string description,
        RecordingSettings settings)
    {
        return new RecordingWorkflowProfile
        {
            Name = name,
            Description = description,
            Settings = settings
        };
    }
}

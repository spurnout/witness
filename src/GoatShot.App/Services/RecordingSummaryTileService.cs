using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class RecordingSummaryTileService
{
    private const string ReadyTone = "Ready";
    private const string NeutralTone = "Neutral";

    public IReadOnlyList<SettingsSummaryTile> Create(RecordingSettings settings)
    {
        return new[]
        {
            CreateActiveProfileTile(settings),
            CreateAudioInputsTile(settings),
            CreateCameraAndOverlaysTile(settings)
        };
    }

    private static SettingsSummaryTile CreateActiveProfileTile(RecordingSettings settings)
    {
        var profile = string.IsNullOrWhiteSpace(settings.QualityProfile)
            ? "Balanced"
            : settings.QualityProfile.Trim();
        var encoder = settings.PreferHevcEncoding ? "HEVC preferred" : "H.264";
        return new SettingsSummaryTile(
            "Active profile",
            profile,
            $"{settings.FramesPerSecond.ToString(CultureInfo.InvariantCulture)} fps, {encoder}",
            ReadyTone);
    }

    private static SettingsSummaryTile CreateAudioInputsTile(RecordingSettings settings)
    {
        var states = new List<string>();
        if (settings.IncludeMicrophone)
        {
            states.Add(settings.MicrophoneMuted ? "Microphone muted" : "Microphone on");
        }

        if (settings.IncludeSystemAudio)
        {
            states.Add(settings.SystemAudioMuted ? "System audio muted" : "System audio on");
        }

        return new SettingsSummaryTile(
            "Audio inputs",
            states.Count.ToString(CultureInfo.InvariantCulture),
            states.Count == 0 ? "No audio inputs requested" : string.Join(", ", states),
            states.Count > 0 ? ReadyTone : NeutralTone);
    }

    private static SettingsSummaryTile CreateCameraAndOverlaysTile(RecordingSettings settings)
    {
        var enabled = new List<string>();
        if (settings.EnableWebcamOverlay)
        {
            enabled.Add("Webcam");
        }

        if (settings.ShowCountdown)
        {
            enabled.Add("Countdown");
        }

        if (settings.ShowRecordingBorder)
        {
            enabled.Add("Border");
        }

        if (settings.ShowRecordingTimer)
        {
            enabled.Add("Timer");
        }

        if (settings.ShowKeystrokeOverlay)
        {
            enabled.Add("Keystrokes");
        }

        return new SettingsSummaryTile(
            "Camera & overlays",
            enabled.Count.ToString(CultureInfo.InvariantCulture),
            enabled.Count == 0 ? "None enabled" : string.Join(", ", enabled),
            settings.EnableWebcamOverlay ? ReadyTone : NeutralTone);
    }
}

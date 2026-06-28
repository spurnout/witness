using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record RecordingPanelSnapshot(
    string State,
    string Source,
    string Quality,
    string Audio,
    string Camera,
    string Overlays,
    string Output,
    string Engine,
    string Confidence = "")
{
    public string ToStatusText()
    {
        var lines = new[]
        {
            State,
            Source,
            Quality,
            Audio,
            Camera,
            Overlays,
            Output,
            Engine,
            Confidence
        };
        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}

public static class RecordingPanelPresenter
{
    public static RecordingPanelSnapshot Build(
        RecordingSettings? settings,
        RecordingEnginePlan? enginePlan,
        bool gifRecording,
        bool gifPaused,
        int profileCount,
        string quickMp4DurationLabel = "5s",
        RecordingConfidenceReport? confidence = null)
    {
        var rawSettings = settings ?? new RecordingSettings();
        var normalized = RecordingSettingsNormalizer.Normalize(rawSettings);
        return new RecordingPanelSnapshot(
            BuildState(gifRecording, gifPaused),
            "Source: active monitor quick recording.",
            $"Profile: {normalized.QualityProfile}, {normalized.FramesPerSecond} fps, {normalized.SizeLabel}, {normalized.BitrateLabel}. Saved profiles: {profileCount}.",
            BuildAudio(rawSettings, normalized),
            BuildCamera(rawSettings),
            BuildOverlays(normalized),
            $"Output: GIF and {quickMp4DurationLabel} MP4 captures save into the workspace library.",
            BuildEngine(enginePlan),
            BuildConfidence(confidence));
    }

    private static string BuildState(bool gifRecording, bool gifPaused)
    {
        if (!gifRecording)
        {
            return "State: idle.";
        }

        return gifPaused
            ? "State: GIF recording paused."
            : "State: GIF recording active.";
    }

    private static string BuildAudio(RecordingSettings settings, NormalizedRecordingSettings normalized)
    {
        var tracks = new List<string>();
        if (normalized.IncludeMicrophone)
        {
            tracks.Add($"microphone {AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(
                normalized.MicrophoneGain,
                normalized.NoiseGateThresholdDb,
                normalized.MicrophoneMuted))}");
        }

        if (normalized.IncludeSystemAudio)
        {
            tracks.Add($"system audio {AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(
                normalized.SystemAudioGain,
                normalized.NoiseGateThresholdDb,
                normalized.SystemAudioMuted))}");
        }

        if (tracks.Count == 0)
        {
            return "Audio: off.";
        }

        var deviceText = new List<string>();
        if (normalized.IncludeMicrophone && !string.IsNullOrWhiteSpace(settings.MicrophoneDeviceId))
        {
            deviceText.Add("microphone device set");
        }

        if (normalized.IncludeSystemAudio && !string.IsNullOrWhiteSpace(settings.SystemAudioDeviceId))
        {
            deviceText.Add("system-audio device set");
        }

        return deviceText.Count == 0
            ? $"Audio: {string.Join("; ", tracks)}."
            : $"Audio: {string.Join("; ", tracks)} ({string.Join(", ", deviceText)}).";
    }

    private static string BuildCamera(RecordingSettings settings)
    {
        if (!settings.EnableWebcamOverlay)
        {
            return "Camera: off; preview available from the recording panel.";
        }

        var device = string.IsNullOrWhiteSpace(settings.WebcamDeviceId)
            ? "default camera"
            : "selected camera";
        return $"Camera: {device}, {settings.WebcamOverlayPosition}, {settings.WebcamOverlayShape}, mirror={(settings.MirrorWebcam ? "on" : "off")}; live preview available.";
    }

    private static string BuildOverlays(NormalizedRecordingSettings settings)
    {
        var timer = settings.ShowRecordingTimer ? settings.RecordingTimerPosition : "off";
        var keys = settings.ShowKeystrokeOverlay ? settings.KeystrokeOverlayPosition : "off";
        return $"Overlays: border={(settings.ShowRecordingBorder ? "on" : "off")}, timer={timer}, keys={keys}, badge {settings.RecordingOverlayBadgeFontSize}px {settings.RecordingOverlayStyle}.";
    }

    private static string BuildEngine(RecordingEnginePlan? enginePlan)
    {
        if (enginePlan is null)
        {
            return "Engine: recording diagnostics pending; production video-only encoder and FFmpeg fallback are probed at runtime.";
        }

        return enginePlan.Choice switch
        {
            RecordingEngineChoice.Production => $"Engine: production recorder selected, {enginePlan.ProductionEncoder.ChoiceLabel}.",
            RecordingEngineChoice.FfmpegFallback => enginePlan.ProductionPrerequisitesMet
                ? $"Engine: FFmpeg fallback selected; production video-only prerequisites ready but requested options need fallback. Fallback encoder: {enginePlan.FallbackVideoEncoder.EncoderName}."
                : $"Engine: FFmpeg fallback selected. Fallback encoder: {enginePlan.FallbackVideoEncoder.EncoderName}.",
            _ => "Engine: MP4 recording unavailable; check diagnostics."
        };
    }

    private static string BuildConfidence(RecordingConfidenceReport? confidence)
    {
        if (confidence is null || confidence.Signals.Count == 0)
        {
            return string.Empty;
        }

        var action = confidence.ActionItems.FirstOrDefault();
        if (action is null)
        {
            return $"Confidence: {confidence.Overall}. {confidence.Summary}";
        }

        var actions = confidence.ActionItems
            .Take(3)
            .Select(item => $"{item.Area}: {item.Status} Action: {item.RecoveryAction}")
            .ToList();
        var remaining = confidence.ActionItems.Count > actions.Count
            ? $" +{confidence.ActionItems.Count - actions.Count} more action item(s)."
            : string.Empty;
        return $"Confidence: {confidence.Overall}. {confidence.Summary} {string.Join(" ", actions)}{remaining}";
    }
}

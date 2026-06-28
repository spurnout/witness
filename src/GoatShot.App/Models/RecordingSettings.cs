namespace GoatShot.App.Models;

public sealed class RecordingSettings
{
    public bool PreferProductionCaptureEngine { get; set; } = true;
    public bool PreferHevcEncoding { get; set; }
    public string QualityProfile { get; set; } = "Balanced";
    public int FramesPerSecond { get; set; } = 30;
    public int TargetWidth { get; set; } = 0;
    public int TargetHeight { get; set; } = 0;
    public int BitrateKbps { get; set; } = 0;
    public bool UseVariableBitrate { get; set; } = true;
    public bool IncludeMicrophone { get; set; }
    public bool IncludeSystemAudio { get; set; }
    public string MicrophoneDeviceId { get; set; } = string.Empty;
    public string SystemAudioDeviceId { get; set; } = string.Empty;
    public double MicrophoneGain { get; set; } = 1d;
    public double SystemAudioGain { get; set; } = 1d;
    public double NoiseGateThresholdDb { get; set; } = -96d;
    public bool MicrophoneMuted { get; set; }
    public bool SystemAudioMuted { get; set; }
    public bool EnableWebcamOverlay { get; set; }
    public string WebcamDeviceId { get; set; } = string.Empty;
    public string WebcamOverlayPosition { get; set; } = "BottomRight";
    public string WebcamOverlayShape { get; set; } = "Circle";
    public bool MirrorWebcam { get; set; } = true;
    public bool ShowCountdown { get; set; } = true;
    public bool ShowRecordingBorder { get; set; } = true;
    public bool ShowRecordingTimer { get; set; } = true;
    public bool ShowKeystrokeOverlay { get; set; }
    public string RecordingTimerPosition { get; set; } = "TopLeft";
    public string KeystrokeOverlayPosition { get; set; } = "BottomLeft";
    public int RecordingOverlayBadgeFontSize { get; set; } = 16;
    public string RecordingOverlayStyle { get; set; } = "Neon";
}

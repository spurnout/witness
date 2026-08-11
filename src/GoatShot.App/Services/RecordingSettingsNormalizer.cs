using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record NormalizedRecordingSettings(
    string QualityProfile,
    int FramesPerSecond,
    int TargetWidth,
    int TargetHeight,
    int BitrateKbps,
    bool UseVariableBitrate,
    bool PreferHevcEncoding,
    int Crf,
    bool IncludeMicrophone,
    bool IncludeSystemAudio,
    double MicrophoneGain,
    double SystemAudioGain,
    double NoiseGateThresholdDb,
    bool MicrophoneMuted,
    bool SystemAudioMuted,
    bool EnableWebcamOverlay,
    bool ShowCountdown,
    bool ShowRecordingBorder,
    bool ShowRecordingTimer,
    bool ShowKeystrokeOverlay,
    string RecordingTimerPosition,
    string KeystrokeOverlayPosition,
    int RecordingOverlayBadgeFontSize,
    string RecordingOverlayStyle)
{
    public string SizeLabel => TargetWidth > 0 || TargetHeight > 0
        ? $"{(TargetWidth > 0 ? TargetWidth.ToString(CultureInfo.InvariantCulture) : "auto")}x{(TargetHeight > 0 ? TargetHeight.ToString(CultureInfo.InvariantCulture) : "auto")}"
        : "source";

    public string BitrateLabel => BitrateKbps > 0
        ? $"{BitrateKbps.ToString(CultureInfo.InvariantCulture)} kbps"
        : UseVariableBitrate
            ? $"CRF {Crf.ToString(CultureInfo.InvariantCulture)}"
            : "profile default";

    public string FallbackLimitSummary
    {
        get
        {
            var audio = (IncludeMicrophone, IncludeSystemAudio) switch
            {
                (true, true) => " Requested microphone and system audio are streamed as normalized WASAPI PCM, mixed within a bounded buffer, and muxed into the MP4 when samples are available.",
                (true, false) => " Requested microphone audio is streamed as normalized WASAPI PCM and muxed into the MP4 when samples are available.",
                (false, true) => " Requested system audio is streamed as normalized WASAPI loopback PCM and muxed into the MP4 when samples are available.",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(audio))
            {
                var processing = new List<string>();
                if (IncludeMicrophone)
                {
                    processing.Add($"mic {AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(MicrophoneGain, NoiseGateThresholdDb, MicrophoneMuted))}");
                }

                if (IncludeSystemAudio)
                {
                    processing.Add($"system audio {AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(SystemAudioGain, NoiseGateThresholdDb, SystemAudioMuted))}");
                }

                audio += $" Audio controls: {string.Join("; ", processing)}.";
            }

            var video = EnableWebcamOverlay
                ? "FFmpeg fallback records screen frames and attempts a DirectShow webcam overlay when a matching camera can be probed."
                : "FFmpeg fallback records screen frames.";
            return video + audio;
        }
    }
}

public static class RecordingSettingsNormalizer
{
    public const int MinFallbackFps = 1;
    public const int MaxFallbackFps = 120;
    public const int MaxTargetWidth = 3840;
    public const int MaxTargetHeight = 2160;

    public static NormalizedRecordingSettings Normalize(RecordingSettings? settings, int? fpsOverride = null)
    {
        settings ??= new RecordingSettings();
        var quality = NormalizeQualityProfile(settings.QualityProfile);
        var fps = Math.Clamp(fpsOverride ?? settings.FramesPerSecond, MinFallbackFps, MaxFallbackFps);
        var width = ClampDimension(settings.TargetWidth, MaxTargetWidth);
        var height = ClampDimension(settings.TargetHeight, MaxTargetHeight);
        var bitrate = Math.Clamp(settings.BitrateKbps, 0, 120_000);
        var crf = CrfForQuality(quality);
        var microphoneProcessing = AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(
            settings.MicrophoneGain,
            settings.NoiseGateThresholdDb,
            settings.MicrophoneMuted));
        var systemProcessing = AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(
            settings.SystemAudioGain,
            settings.NoiseGateThresholdDb,
            settings.SystemAudioMuted));

        return new NormalizedRecordingSettings(
            quality,
            fps,
            width,
            height,
            bitrate,
            settings.UseVariableBitrate,
            settings.PreferHevcEncoding,
            crf,
            settings.IncludeMicrophone,
            settings.IncludeSystemAudio,
            microphoneProcessing.Gain,
            systemProcessing.Gain,
            microphoneProcessing.NoiseGateThresholdDb,
            microphoneProcessing.Muted,
            systemProcessing.Muted,
            settings.EnableWebcamOverlay,
            settings.ShowCountdown,
            settings.ShowRecordingBorder,
            settings.ShowRecordingTimer,
            settings.ShowKeystrokeOverlay,
            NormalizeOverlayPosition(settings.RecordingTimerPosition, "TopLeft"),
            NormalizeOverlayPosition(settings.KeystrokeOverlayPosition, "BottomLeft"),
            Math.Clamp(settings.RecordingOverlayBadgeFontSize, 10, 32),
            NormalizeOverlayStyle(settings.RecordingOverlayStyle));
    }

    public static string FormatSummary(NormalizedRecordingSettings settings)
    {
        var overlays = settings.ShowRecordingTimer || settings.ShowKeystrokeOverlay || settings.ShowRecordingBorder
            ? $" Overlays: border={(settings.ShowRecordingBorder ? "on" : "off")}, timer={(settings.ShowRecordingTimer ? settings.RecordingTimerPosition : "off")}, keys={(settings.ShowKeystrokeOverlay ? settings.KeystrokeOverlayPosition : "off")}, badge {settings.RecordingOverlayBadgeFontSize}px {settings.RecordingOverlayStyle}."
            : " Overlays: off.";
        var codec = settings.PreferHevcEncoding
            ? " Codec: HEVC preferred when Media Foundation support is available."
            : " Codec: H.264 default.";
        return $"{settings.QualityProfile}, {settings.FramesPerSecond} fps, {settings.SizeLabel}, {settings.BitrateLabel}.{codec}{overlays} {settings.FallbackLimitSummary}";
    }

    public static string NormalizeQualityProfile(string? qualityProfile)
    {
        var value = (qualityProfile ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "small" or "draft" or "low" => "Small",
            "high" or "high quality" or "quality" => "High quality",
            "archive" or "near-lossless" or "near lossless" or "lossless" => "Archive",
            _ => "Balanced"
        };
    }

    public static int CrfForQuality(string? qualityProfile)
    {
        return NormalizeQualityProfile(qualityProfile) switch
        {
            "Small" => 30,
            "High quality" => 18,
            "Archive" => 12,
            _ => 23
        };
    }

    public static string NormalizeOverlayPosition(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "topleft" or "lefttop" => "TopLeft",
            "topright" or "righttop" => "TopRight",
            "bottomleft" or "leftbottom" => "BottomLeft",
            "bottomright" or "rightbottom" => "BottomRight",
            "top" or "topcenter" or "centertop" => "TopCenter",
            "bottom" or "bottomcenter" or "centerbottom" => "BottomCenter",
            _ => NormalizeOverlayPositionFallback(fallback)
        };
    }

    public static string NormalizeOverlayStyle(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "highcontrast" or "contrast" => "HighContrast",
            "subtle" or "minimal" => "Subtle",
            _ => "Neon"
        };
    }

    private static int ClampDimension(int value, int max)
    {
        if (value <= 0)
        {
            return 0;
        }

        return Math.Clamp(value, 16, max);
    }

    private static string NormalizeOverlayPositionFallback(string fallback)
    {
        return fallback switch
        {
            "TopRight" => "TopRight",
            "BottomLeft" => "BottomLeft",
            "BottomRight" => "BottomRight",
            "TopCenter" => "TopCenter",
            "BottomCenter" => "BottomCenter",
            _ => "TopLeft"
        };
    }
}

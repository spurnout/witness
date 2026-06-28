using GoatShot.App.Models;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

[JsonConverter(typeof(JsonStringEnumConverter<RecordingEngineChoice>))]
public enum RecordingEngineChoice
{
    Production,
    FfmpegFallback,
    Unavailable
}

public sealed record RecordingEnginePlan(
    RecordingEngineChoice Choice,
    bool ProductionRequested,
    bool ProductionPrerequisitesMet,
    bool ProductionEngineImplemented,
    bool ProductionFrameCaptureImplemented,
    ProductionVideoEncoderSelection ProductionEncoder,
    FfmpegVideoEncoderSelection FallbackVideoEncoder,
    bool FallbackAvailable,
    IReadOnlyList<string> ProductionBlockers,
    IReadOnlyList<string> FallbackBlockers,
    IReadOnlyList<string> Warnings,
    string Summary)
{
    public bool CanRecord => Choice is RecordingEngineChoice.Production or RecordingEngineChoice.FfmpegFallback;

    public string ChoiceLabel => Choice switch
    {
        RecordingEngineChoice.Production => "production",
        RecordingEngineChoice.FfmpegFallback => "ffmpeg-fallback",
        _ => "unavailable"
    };
}

public static class RecordingEnginePlanner
{
    public static RecordingEnginePlan BuildPlan(
        RecordingSettings? settings,
        RecordingCapabilitySnapshot capabilities,
        IEnumerable<string>? deviceIssues = null,
        bool productionEngineImplemented = false,
        bool productionFrameCaptureImplemented = false,
        bool productionAudioMixingImplemented = false,
        bool productionWebcamCompositorImplemented = false)
    {
        settings ??= new RecordingSettings();
        var normalized = RecordingSettingsNormalizer.Normalize(settings);
        var productionVideoEncoder = capabilities.SelectProductionVideoEncoder(settings);
        var fallbackVideoEncoder = FfmpegVideoEncoderSelector.Select(capabilities.Ffmpeg, normalized);
        var productionBlockers = new List<string>();
        var fallbackBlockers = new List<string>();
        var warnings = new List<string>();

        if (!settings.PreferProductionCaptureEngine)
        {
            warnings.Add("Production recording preference is disabled in settings.");
        }

        if (!capabilities.WindowsGraphicsCaptureSupported)
        {
            productionBlockers.Add(capabilities.WindowsGraphicsCaptureStatus);
        }

        if (!capabilities.Direct3D11Device.Succeeded)
        {
            productionBlockers.Add(capabilities.Direct3D11Device.Status);
        }

        if (!productionVideoEncoder.IsAvailable)
        {
            productionBlockers.Add(productionVideoEncoder.Summary);
        }
        else if (settings.PreferHevcEncoding)
        {
            warnings.Add($"{productionVideoEncoder.Summary} H.264 remains the default when HEVC preference is disabled.");
        }

        foreach (var issue in deviceIssues ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(issue))
            {
                productionBlockers.Add(issue);
            }
        }

        if (!productionFrameCaptureImplemented)
        {
            productionBlockers.Add("WGC/Direct3D11 frame capture is not wired into recording yet.");
        }
        else if (!productionEngineImplemented)
        {
            productionBlockers.Add("Media Foundation production MP4 encoding is not wired yet; WGC frame capture can feed the FFmpeg fallback.");
        }

        if ((normalized.IncludeMicrophone || normalized.IncludeSystemAudio) && !productionAudioMixingImplemented)
        {
            productionBlockers.Add("Production audio sync/mixing is not wired yet; requested audio requires the FFmpeg fallback.");
        }

        if (normalized.EnableWebcamOverlay && !productionWebcamCompositorImplemented)
        {
            productionBlockers.Add("Production webcam compositing is not wired yet; requested webcam overlay requires the FFmpeg fallback.");
        }

        if (!capabilities.Ffmpeg.Available)
        {
            fallbackBlockers.Add(capabilities.Ffmpeg.Status);
        }
        else if (!fallbackVideoEncoder.IsAvailable)
        {
            fallbackBlockers.Add(fallbackVideoEncoder.Summary);
        }

        if (normalized.IncludeMicrophone)
        {
            warnings.Add(productionAudioMixingImplemented
                ? "The native production recorder can mux captured microphone WAV payloads as AAC when payload bytes are available."
                : "The FFmpeg fallback captures requested microphone audio through a temporary WASAPI WAV file and muxes it when payload bytes are available; production audio sync/mixing remains pending.");
        }

        if (normalized.IncludeSystemAudio)
        {
            warnings.Add(productionAudioMixingImplemented
                ? "The native production recorder can mux captured system-audio loopback WAV payloads as AAC when payload bytes are available."
                : "The FFmpeg fallback captures requested system audio through a temporary WASAPI loopback WAV file and muxes it when payload bytes are available; production audio sync/mixing remains pending.");
        }

        if (normalized.EnableWebcamOverlay)
        {
            warnings.Add(productionWebcamCompositorImplemented
                ? "The native production recorder can precompose a bounded, periodically refreshed native camera overlay before MP4 encoding; FFmpeg DirectShow remains available as fallback."
                : "The FFmpeg fallback attempts requested webcam overlay through FFmpeg DirectShow after probing the selected/default camera; native camera-frame precomposition remains pending.");
        }

        var productionPrerequisitesMet =
            settings.PreferProductionCaptureEngine &&
            capabilities.WindowsGraphicsCaptureSupported &&
            capabilities.Direct3D11Device.Succeeded &&
            productionVideoEncoder.IsAvailable &&
            productionBlockers.All(blocker => blocker.Contains("not wired", StringComparison.OrdinalIgnoreCase));
        var productionUsable =
            productionPrerequisitesMet &&
            productionFrameCaptureImplemented &&
            productionEngineImplemented &&
            productionBlockers.Count == 0;
        var fallbackAvailable = capabilities.Ffmpeg.Available && fallbackVideoEncoder.IsAvailable;

        var choice = productionUsable
            ? RecordingEngineChoice.Production
            : fallbackAvailable
                ? RecordingEngineChoice.FfmpegFallback
                : RecordingEngineChoice.Unavailable;

        return new RecordingEnginePlan(
            choice,
            settings.PreferProductionCaptureEngine,
            productionPrerequisitesMet,
            productionEngineImplemented,
            productionFrameCaptureImplemented,
            productionVideoEncoder,
            fallbackVideoEncoder,
            fallbackAvailable,
            productionBlockers,
            fallbackBlockers,
            warnings,
            BuildSummary(choice, productionBlockers, fallbackBlockers, warnings));
    }

    private static string BuildSummary(
        RecordingEngineChoice choice,
        IReadOnlyList<string> productionBlockers,
        IReadOnlyList<string> fallbackBlockers,
        IReadOnlyList<string> warnings)
    {
        var summary = choice switch
        {
            RecordingEngineChoice.Production => "Production recording selected.",
            RecordingEngineChoice.FfmpegFallback => "FFmpeg fallback recording selected.",
            _ => "No MP4 recording engine is currently available."
        };

        if (productionBlockers.Count > 0)
        {
            summary += $" Production blockers: {string.Join(" ", productionBlockers)}";
        }

        if (fallbackBlockers.Count > 0)
        {
            summary += $" Fallback blockers: {string.Join(" ", fallbackBlockers)}";
        }

        if (warnings.Count > 0)
        {
            summary += $" Notes: {string.Join(" ", warnings)}";
        }

        return summary;
    }
}

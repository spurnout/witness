using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

[JsonConverter(typeof(JsonStringEnumConverter<RecordingConfidenceLevel>))]
public enum RecordingConfidenceLevel
{
    Disabled,
    Ready,
    Warning,
    Blocked
}

public sealed record RecordingConfidenceSignal(
    string Area,
    RecordingConfidenceLevel Level,
    string Status,
    string RecoveryAction)
{
    public bool NeedsAction => Level is RecordingConfidenceLevel.Warning or RecordingConfidenceLevel.Blocked;

    public string ToStatusText()
    {
        var recovery = string.IsNullOrWhiteSpace(RecoveryAction)
            ? string.Empty
            : $" Action: {RecoveryAction}";
        return $"{Area}: {Level}. {Status}{recovery}";
    }
}

public sealed record RecordingConfidenceReport(
    RecordingConfidenceLevel Overall,
    string Summary,
    IReadOnlyList<RecordingConfidenceSignal> Signals)
{
    public static RecordingConfidenceReport Empty { get; } = new(
        RecordingConfidenceLevel.Disabled,
        "Recording confidence has not been evaluated yet.",
        Array.Empty<RecordingConfidenceSignal>());

    public bool HasBlockers => Signals.Any(signal => signal.Level == RecordingConfidenceLevel.Blocked);
    public bool HasWarnings => Signals.Any(signal => signal.Level == RecordingConfidenceLevel.Warning);

    public IReadOnlyList<RecordingConfidenceSignal> ActionItems =>
        Signals.Where(signal => signal.NeedsAction).ToList();

    public string ToStatusText()
    {
        if (Signals.Count == 0)
        {
            return Summary;
        }

        return Summary + Environment.NewLine +
            string.Join(Environment.NewLine, Signals.Select(signal => $"- {signal.ToStatusText()}"));
    }
}

public sealed record RecordingReadinessSnapshot(
    RecordingEnginePlan EnginePlan,
    RecordingDeviceSnapshot Devices,
    RecordingConfidenceReport Confidence);

public static class RecordingConfidenceService
{
    public static RecordingConfidenceReport BuildDeviceReport(
        RecordingSettings? settings,
        IReadOnlyList<AudioCaptureDevice> microphones,
        IReadOnlyList<AudioCaptureDevice> systemAudioDevices,
        IReadOnlyList<CameraOverlayDevice> cameras,
        IEnumerable<string>? discoveryIssues = null)
    {
        settings ??= new RecordingSettings();
        var issues = (discoveryIssues ?? Array.Empty<string>())
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .ToList();
        var signals = new List<RecordingConfidenceSignal>
        {
            BuildAudioTrackSignal(
                "Microphone",
                settings.IncludeMicrophone,
                settings.MicrophoneDeviceId,
                settings.MicrophoneMuted,
                microphones,
                "Enable Mic audio only when spoken narration is expected.",
                "Connect or enable a microphone, then allow desktop apps to access microphone input.",
                "Switch to the system default microphone or refresh device selections in Settings.",
                issues),
            BuildAudioTrackSignal(
                "System audio",
                settings.IncludeSystemAudio,
                settings.SystemAudioDeviceId,
                settings.SystemAudioMuted,
                systemAudioDevices,
                "Enable System audio only when app or desktop audio should be recorded.",
                "Enable a render endpoint/speaker device, then refresh recording devices.",
                "Switch to the system default system-audio endpoint or refresh device selections in Settings.",
                issues),
            BuildCameraSignal(settings.EnableWebcamOverlay, settings.WebcamDeviceId, cameras, issues)
        };

        foreach (var issue in issues)
        {
            if (signals.Any(signal => signal.Status.Contains(issue, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var issueStatus = ClassifyDeviceStatus(issue);
            signals.Add(new RecordingConfidenceSignal(
                "Device discovery",
                issueStatus.Level == RecordingConfidenceLevel.Ready ? RecordingConfidenceLevel.Warning : issueStatus.Level,
                issue,
                RecoveryForDeviceStatus("device discovery", issue)));
        }

        return BuildReport(signals, "Recording device confidence");
    }

    public static RecordingConfidenceReport BuildEngineReport(
        RecordingSettings? settings,
        RecordingCapabilitySnapshot capabilities,
        RecordingEnginePlan enginePlan)
    {
        settings ??= new RecordingSettings();
        var signals = new List<RecordingConfidenceSignal>
        {
            BuildMp4EngineSignal(enginePlan),
            BuildProductionPipelineSignal(enginePlan),
            BuildFfmpegFallbackSignal(enginePlan)
        };

        if (settings.IncludeMicrophone || settings.IncludeSystemAudio)
        {
            signals.Add(new RecordingConfidenceSignal(
                "Audio mux",
                enginePlan.ProductionBlockers.Any(blocker => blocker.Contains("audio", StringComparison.OrdinalIgnoreCase))
                    ? RecordingConfidenceLevel.Warning
                    : RecordingConfidenceLevel.Ready,
                enginePlan.Warnings.FirstOrDefault(warning => warning.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
                    warning.Contains("microphone", StringComparison.OrdinalIgnoreCase) ||
                    warning.Contains("system", StringComparison.OrdinalIgnoreCase)) ??
                    "Requested audio tracks can be routed through the selected recording engine.",
                "Run `record audio` for a short WAV proof when verifying a new microphone or loopback device."));
            signals.Add(new RecordingConfidenceSignal(
                "Audio sync proof",
                RecordingConfidenceLevel.Warning,
                "Requested audio tracks are streamed as normalized PCM and mixed in bounded chunks; Receipts does not retain private sample content in diagnostics.",
                "After a safe short MP4 recording, run `diagnostics recording-media <mp4>` when ffprobe is available and compare the reported A/V duration delta before relying on long-run sync."));
        }

        if (settings.EnableWebcamOverlay)
        {
            signals.Add(new RecordingConfidenceSignal(
                "Webcam overlay",
                enginePlan.ProductionBlockers.Any(blocker => blocker.Contains("webcam", StringComparison.OrdinalIgnoreCase))
                    ? RecordingConfidenceLevel.Warning
                    : RecordingConfidenceLevel.Ready,
                enginePlan.Warnings.FirstOrDefault(warning => warning.Contains("webcam", StringComparison.OrdinalIgnoreCase)) ??
                    "Requested webcam overlay can be routed through the selected recording engine.",
                "Use the recording panel preview before a live recording when camera framing matters."));
        }

        return BuildReport(signals, "Recording engine confidence");
    }

    public static RecordingConfidenceReport BuildReadinessReport(
        RecordingSettings? settings,
        RecordingCapabilitySnapshot capabilities,
        RecordingEnginePlan enginePlan,
        RecordingDeviceSnapshot devices)
    {
        var signals = new List<RecordingConfidenceSignal>();
        signals.AddRange(BuildEngineReport(settings, capabilities, enginePlan).Signals);
        signals.AddRange(devices.Confidence.Signals);
        return BuildReport(signals, "Recording readiness confidence");
    }

    private static RecordingConfidenceSignal BuildAudioTrackSignal(
        string area,
        bool requested,
        string selectedId,
        bool muted,
        IReadOnlyList<AudioCaptureDevice> devices,
        string disabledRecovery,
        string missingRecovery,
        string unavailableRecovery,
        IReadOnlyList<string> discoveryIssues)
    {
        if (!requested)
        {
            return new RecordingConfidenceSignal(
                area,
                RecordingConfidenceLevel.Disabled,
                $"{area} recording is not requested.",
                disabledRecovery);
        }

        if (devices.Count == 0)
        {
            var discoveryIssue = FindRelevantDiscoveryIssue(area, discoveryIssues);
            if (!string.IsNullOrWhiteSpace(discoveryIssue))
            {
                var issueStatus = ClassifyDeviceStatus(discoveryIssue);
                return new RecordingConfidenceSignal(
                    area,
                    issueStatus.Level == RecordingConfidenceLevel.Ready ? RecordingConfidenceLevel.Blocked : issueStatus.Level,
                    $"{area} recording is requested, but device discovery reported {DescribeDeviceStatus(issueStatus, discoveryIssue)}: {discoveryIssue}",
                    RecoveryForDeviceStatus(area, discoveryIssue));
            }

            return new RecordingConfidenceSignal(
                area,
                RecordingConfidenceLevel.Blocked,
                $"{area} recording is requested, but no matching Windows audio endpoint was discovered.",
                missingRecovery);
        }

        var device = SelectAudioDevice(selectedId, devices);
        if (device is null)
        {
            return new RecordingConfidenceSignal(
                area,
                RecordingConfidenceLevel.Blocked,
                $"{area} recording is requested, but the selected endpoint is unavailable or disconnected: {selectedId}.",
                unavailableRecovery);
        }

        var meterStatus = ClassifyDeviceStatus(device.MeterStatus);
        if (meterStatus.Level == RecordingConfidenceLevel.Blocked)
        {
            return new RecordingConfidenceSignal(
                area,
                RecordingConfidenceLevel.Blocked,
                $"{area} endpoint {device.DisplayName} is selected, but {DescribeDeviceStatus(meterStatus, device.MeterStatus)}: {device.MeterStatus}",
                RecoveryForDeviceStatus(area, device.MeterStatus));
        }

        if (muted)
        {
            return new RecordingConfidenceSignal(
                area,
                RecordingConfidenceLevel.Warning,
                $"{area} endpoint {device.DisplayName} is available, but this track is muted.",
                $"Unmute {area.ToLowerInvariant()} before recording if sound should be present.");
        }

        var peak = device.PeakLevel.HasValue ? $" peak={device.PeakLevel.Value:P0}" : string.Empty;
        var status = string.IsNullOrWhiteSpace(device.MeterStatus) ? string.Empty : $" Meter: {device.MeterStatus}";
        var level = meterStatus.Level == RecordingConfidenceLevel.Warning
            ? RecordingConfidenceLevel.Warning
            : RecordingConfidenceLevel.Ready;
        var recovery = level == RecordingConfidenceLevel.Warning
            ? RecoveryForDeviceStatus(area, device.MeterStatus)
            : "No action needed.";
        return new RecordingConfidenceSignal(
            area,
            level,
            $"{area} endpoint {device.DisplayName} is available.{peak}{status}",
            recovery);
    }

    private static RecordingConfidenceSignal BuildCameraSignal(
        bool requested,
        string selectedId,
        IReadOnlyList<CameraOverlayDevice> cameras,
        IReadOnlyList<string> discoveryIssues)
    {
        if (!requested)
        {
            return new RecordingConfidenceSignal(
                "Webcam",
                RecordingConfidenceLevel.Disabled,
                "Webcam overlay is not requested.",
                "Enable Webcam overlay only when face-camera context is needed.");
        }

        if (cameras.Count == 0)
        {
            var discoveryIssue = FindRelevantDiscoveryIssue("Camera", discoveryIssues);
            if (!string.IsNullOrWhiteSpace(discoveryIssue))
            {
                var issueStatus = ClassifyDeviceStatus(discoveryIssue);
                return new RecordingConfidenceSignal(
                    "Webcam",
                    issueStatus.Level == RecordingConfidenceLevel.Ready ? RecordingConfidenceLevel.Blocked : issueStatus.Level,
                    $"Webcam overlay is requested, but camera discovery reported {DescribeDeviceStatus(issueStatus, discoveryIssue)}: {discoveryIssue}",
                    RecoveryForDeviceStatus("webcam", discoveryIssue));
            }

            return new RecordingConfidenceSignal(
                "Webcam",
                RecordingConfidenceLevel.Blocked,
                "Webcam overlay is requested, but no camera was discovered.",
                "Connect or enable a camera, allow desktop apps to access it, then refresh recording devices.");
        }

        var camera = SelectCamera(selectedId, cameras);
        if (camera is null)
        {
            return new RecordingConfidenceSignal(
                "Webcam",
                RecordingConfidenceLevel.Blocked,
                $"Webcam overlay is requested, but the selected camera is unavailable or disconnected: {selectedId}.",
                "Switch to the system default webcam or refresh device selections in Settings.");
        }

        var cameraStatus = ClassifyDeviceStatus(camera.Status);
        if (cameraStatus.Level == RecordingConfidenceLevel.Blocked)
        {
            return new RecordingConfidenceSignal(
                "Webcam",
                RecordingConfidenceLevel.Blocked,
                $"Webcam overlay selected {camera.DisplayName}, but {DescribeDeviceStatus(cameraStatus, camera.Status)}: {camera.Status}",
                RecoveryForDeviceStatus("webcam", camera.Status));
        }

        if (cameraStatus.Level == RecordingConfidenceLevel.Warning)
        {
            return new RecordingConfidenceSignal(
                "Webcam",
                RecordingConfidenceLevel.Warning,
                $"Webcam overlay can use {camera.DisplayName}, but camera status should be reviewed: {camera.Status}",
                RecoveryForDeviceStatus("webcam", camera.Status));
        }

        return new RecordingConfidenceSignal(
            "Webcam",
            RecordingConfidenceLevel.Ready,
            $"Webcam overlay can use {camera.DisplayName}.",
            "Use the live preview before recording to confirm framing, mirror, shape, and position.");
    }

    private static RecordingConfidenceSignal BuildMp4EngineSignal(RecordingEnginePlan plan)
    {
        return plan.Choice switch
        {
            RecordingEngineChoice.Production => new RecordingConfidenceSignal(
                "MP4 engine",
                RecordingConfidenceLevel.Ready,
                "Native Media Foundation production MP4 recording is selected.",
                "No action needed."),
            RecordingEngineChoice.FfmpegFallback => new RecordingConfidenceSignal(
                "MP4 engine",
                RecordingConfidenceLevel.Warning,
                "MP4 recording can proceed through the FFmpeg fallback.",
                "Review production blockers when native WGC/D3D/Media Foundation recording is expected."),
            _ => new RecordingConfidenceSignal(
                "MP4 engine",
                RecordingConfidenceLevel.Blocked,
                "No MP4 recording engine is currently available.",
                "Resolve production blockers or install/configure FFmpeg with an H.264 encoder.")
        };
    }

    private static RecordingConfidenceSignal BuildProductionPipelineSignal(RecordingEnginePlan plan)
    {
        if (plan.Choice == RecordingEngineChoice.Production)
        {
            return new RecordingConfidenceSignal(
                "Production pipeline",
                RecordingConfidenceLevel.Ready,
                $"{plan.ProductionEncoder.Summary} WGC/D3D frame capture prerequisites are ready.",
                "No action needed.");
        }

        var blockerText = plan.ProductionBlockers.Count == 0
            ? "Production recorder was not selected for the current settings."
            : string.Join(" ", plan.ProductionBlockers);
        return new RecordingConfidenceSignal(
            "Production pipeline",
            plan.Choice == RecordingEngineChoice.Unavailable
                ? RecordingConfidenceLevel.Blocked
                : RecordingConfidenceLevel.Warning,
            blockerText,
            "Use `diagnostics recording` after changing capture, encoder, audio, webcam, or device settings.");
    }

    private static RecordingConfidenceSignal BuildFfmpegFallbackSignal(RecordingEnginePlan plan)
    {
        if (plan.FallbackAvailable)
        {
            return new RecordingConfidenceSignal(
                "FFmpeg fallback",
                RecordingConfidenceLevel.Ready,
                plan.Choice == RecordingEngineChoice.FfmpegFallback
                    ? plan.FallbackVideoEncoder.Summary
                    : $"{plan.FallbackVideoEncoder.Summary} It is available as a secondary path.",
                plan.Choice == RecordingEngineChoice.FfmpegFallback
                    ? "No action needed unless native production recording is required."
                    : "Keep FFmpeg available as a secondary path for unusual targets or troubleshooting.");
        }

        return new RecordingConfidenceSignal(
            "FFmpeg fallback",
            plan.Choice == RecordingEngineChoice.Production
                ? RecordingConfidenceLevel.Warning
                : RecordingConfidenceLevel.Blocked,
            plan.FallbackBlockers.Count == 0
                ? "FFmpeg fallback is not available."
                : string.Join(" ", plan.FallbackBlockers),
            "Install FFmpeg or set RECEIPTS_FFMPEG_PATH (or the legacy GOATSHOT_FFMPEG_PATH alias) when fallback recording/video tools are needed.");
    }

    private static RecordingConfidenceReport BuildReport(
        IReadOnlyList<RecordingConfidenceSignal> signals,
        string label)
    {
        var overall = signals.Any(signal => signal.Level == RecordingConfidenceLevel.Blocked)
            ? RecordingConfidenceLevel.Blocked
            : signals.Any(signal => signal.Level == RecordingConfidenceLevel.Warning)
                ? RecordingConfidenceLevel.Warning
                : signals.Any(signal => signal.Level == RecordingConfidenceLevel.Ready)
                    ? RecordingConfidenceLevel.Ready
                    : RecordingConfidenceLevel.Disabled;

        var actionCount = signals.Count(signal => signal.NeedsAction);
        var summary = overall switch
        {
            RecordingConfidenceLevel.Blocked => $"{label}: blocked; {actionCount} action item(s) need attention before the requested setup is reliable.",
            RecordingConfidenceLevel.Warning => $"{label}: usable with caution; {actionCount} action item(s) should be reviewed.",
            RecordingConfidenceLevel.Ready => $"{label}: ready.",
            _ => $"{label}: no recording device tracks are requested."
        };

        return new RecordingConfidenceReport(overall, summary, signals);
    }

    private static AudioCaptureDevice? SelectAudioDevice(
        string selectedId,
        IReadOnlyList<AudioCaptureDevice> devices)
    {
        if (string.IsNullOrWhiteSpace(selectedId))
        {
            return devices.FirstOrDefault(device => device.IsDefault) ?? devices[0];
        }

        return devices.FirstOrDefault(device => device.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private static CameraOverlayDevice? SelectCamera(
        string selectedId,
        IReadOnlyList<CameraOverlayDevice> cameras)
    {
        if (string.IsNullOrWhiteSpace(selectedId))
        {
            return cameras.FirstOrDefault(camera => camera.IsDefault) ?? cameras[0];
        }

        return cameras.FirstOrDefault(camera => camera.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindRelevantDiscoveryIssue(
        string area,
        IReadOnlyList<string> discoveryIssues)
    {
        var terms = area.Equals("System audio", StringComparison.OrdinalIgnoreCase)
            ? new[] { "system", "audio", "loopback", "render" }
            : area.Equals("Microphone", StringComparison.OrdinalIgnoreCase)
                ? new[] { "microphone", "mic", "input" }
                : new[] { "camera", "webcam", "video" };
        return discoveryIssues.FirstOrDefault(issue =>
            terms.Any(term => issue.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static RecordingDeviceStatus ClassifyDeviceStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return RecordingDeviceStatus.Ready;
        }

        var text = status.ToLowerInvariant();
        if (text.Contains("denied", StringComparison.Ordinal) ||
            text.Contains("permission", StringComparison.Ordinal) ||
            text.Contains("unauthorized", StringComparison.Ordinal) ||
            text.Contains("access is denied", StringComparison.Ordinal) ||
            text.Contains("privacy", StringComparison.Ordinal))
        {
            return RecordingDeviceStatus.PermissionDenied;
        }

        if (text.Contains("disconnect", StringComparison.Ordinal) ||
            text.Contains("unplug", StringComparison.Ordinal) ||
            text.Contains("removed", StringComparison.Ordinal) ||
            text.Contains("not present", StringComparison.Ordinal) ||
            text.Contains("not found", StringComparison.Ordinal))
        {
            return RecordingDeviceStatus.Disconnected;
        }

        if (text.Contains("unavailable", StringComparison.Ordinal) ||
            text.Contains("failed", StringComparison.Ordinal) ||
            text.Contains("error", StringComparison.Ordinal) ||
            text.Contains("busy", StringComparison.Ordinal))
        {
            return RecordingDeviceStatus.Unavailable;
        }

        if (text.Contains("not active", StringComparison.Ordinal) ||
            text.Contains("silent", StringComparison.Ordinal) ||
            text.Contains("no signal", StringComparison.Ordinal))
        {
            return RecordingDeviceStatus.Inactive;
        }

        return RecordingDeviceStatus.Ready;
    }

    private static string DescribeDeviceStatus(RecordingDeviceStatus status, string rawStatus)
    {
        return status.Kind switch
        {
            RecordingDeviceStatusKind.PermissionDenied => "permission is denied",
            RecordingDeviceStatusKind.Disconnected => "the device appears disconnected",
            RecordingDeviceStatusKind.Unavailable => "the endpoint is unavailable",
            RecordingDeviceStatusKind.Inactive => "the endpoint is not active",
            _ => string.IsNullOrWhiteSpace(rawStatus) ? "the endpoint is ready" : "the endpoint reports status"
        };
    }

    private static string RecoveryForDeviceStatus(string area, string status)
    {
        var classified = ClassifyDeviceStatus(status);
        var lowerArea = area.ToLowerInvariant();
        return classified.Kind switch
        {
            RecordingDeviceStatusKind.PermissionDenied =>
                $"Open Windows privacy settings, allow desktop apps to access {lowerArea}, then refresh recording devices.",
            RecordingDeviceStatusKind.Disconnected =>
                $"Reconnect or wake the {lowerArea} device, then refresh recording devices.",
            RecordingDeviceStatusKind.Unavailable =>
                $"Close apps that may own the {lowerArea} device, check Windows sound/camera settings, then refresh recording devices.",
            RecordingDeviceStatusKind.Inactive =>
                $"Play or speak a short test signal, run `record audio` if applicable, then refresh the endpoint if it stays inactive.",
            _ => "Refresh recording devices after checking Windows privacy settings and hardware connections."
        };
    }

    private sealed record RecordingDeviceStatus(
        RecordingDeviceStatusKind Kind,
        RecordingConfidenceLevel Level)
    {
        public static RecordingDeviceStatus Ready { get; } = new(
            RecordingDeviceStatusKind.Ready,
            RecordingConfidenceLevel.Ready);

        public static RecordingDeviceStatus PermissionDenied { get; } = new(
            RecordingDeviceStatusKind.PermissionDenied,
            RecordingConfidenceLevel.Blocked);

        public static RecordingDeviceStatus Disconnected { get; } = new(
            RecordingDeviceStatusKind.Disconnected,
            RecordingConfidenceLevel.Blocked);

        public static RecordingDeviceStatus Unavailable { get; } = new(
            RecordingDeviceStatusKind.Unavailable,
            RecordingConfidenceLevel.Blocked);

        public static RecordingDeviceStatus Inactive { get; } = new(
            RecordingDeviceStatusKind.Inactive,
            RecordingConfidenceLevel.Warning);
    }

    private enum RecordingDeviceStatusKind
    {
        Ready,
        PermissionDenied,
        Disconnected,
        Unavailable,
        Inactive
    }
}

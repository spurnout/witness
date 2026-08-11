using System.Diagnostics;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class DiagnosticsService
{
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly SecretStore _secretStore;
    private readonly StartupRegistrationService _startup;
    private readonly WorkspaceMetadataIndex _workspaceIndex;
    private readonly UploadQueueService _uploadQueue;
    private readonly UploadQueueWorkerService _uploadQueueWorker;
    private readonly IAudioCaptureService _audioCapture;
    private readonly ICameraOverlayService _cameraOverlay;

    public DiagnosticsService(
        AppSettings settings,
        AppPaths paths,
        SecretStore secretStore,
        StartupRegistrationService startup,
        WorkspaceMetadataIndex workspaceIndex,
        UploadQueueService uploadQueue,
        UploadQueueWorkerService uploadQueueWorker,
        IAudioCaptureService audioCapture,
        ICameraOverlayService cameraOverlay)
    {
        _settings = settings;
        _paths = paths;
        _secretStore = secretStore;
        _startup = startup;
        _workspaceIndex = workspaceIndex;
        _uploadQueue = uploadQueue;
        _uploadQueueWorker = uploadQueueWorker;
        _audioCapture = audioCapture;
        _cameraOverlay = cameraOverlay;
    }

    public DiagnosticSnapshot GetSnapshot()
    {
        var capabilities = RecordingCapabilityProbe.Probe(refresh: true);
        var productionEncoder = capabilities.SelectProductionVideoEncoder(_settings.Recording);
        var recordingPlan = RecordingEnginePlanner.BuildPlan(
            _settings.Recording,
            capabilities,
            productionEngineImplemented: NativeMediaFoundationMp4Encoder.ProductionEngineImplemented,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: NativeMediaFoundationMp4Encoder.ProductionAudioMixingImplemented,
            productionWebcamCompositorImplemented: NativeMediaFoundationMp4Encoder.ProductionWebcamCompositorImplemented);
        var recordingConfidence = RecordingConfidenceService.BuildEngineReport(
            _settings.Recording,
            capabilities,
            recordingPlan);
        var policy = ManagedPolicyService.LoadEffective(_settings);
        var aiStatus = policy.DisableAi
            ? $"AI is disabled by managed policy ({policy.Source}). Gemini provider calls, provider-backed transcription, and AI drafting actions are blocked until policy changes."
            : _settings.AiEnabled
                ? _secretStore.HasGeminiApiKey
                    ? $"AI is enabled with a saved Gemini key for image editing, screenshot explanation, explicit per-action provider-backed speech-to-text for short extracted WAV audio, AI-assisted video title/summary/chapter drafting from redacted transcript text, redacted local action history, prompt reuse, and accept/reject review status. Default image model: {_settings.GeminiDefaultModelId}. Default speech model: {_settings.GeminiSpeechToTextModelId}. Offline transcription supports provided/sidecar SRT, embedded subtitle extraction, and an explicitly configured external Whisper executable/model; GoatShot does not ship or download Whisper."
                    : "AI is enabled, but no Gemini API key is saved. Local prompt history/review, offline transcription from SRT/embedded subtitles or a configured external Whisper installation, and transcript-based local video summary drafts are still available."
                : "AI is disabled by default. Gemini image editing, screenshot explanation, provider-backed speech-to-text, and AI-assisted video drafting require explicit configuration and per-action consent before screenshots, audio, or redacted transcript text leave the machine; redacted action history, prompt reuse, accept/reject review status, and offline transcription from SRT/embedded subtitles or an external Whisper installation do not upload media.";
        return new DiagnosticSnapshot
        {
            OsDescription = RuntimeInformation.OSDescription,
            RuntimeDescription = RuntimeInformation.FrameworkDescription,
            CaptureEngine = $"Active default path: Win32/GDI bitmap capture, polished frozen-screen region overlay with snapping, pixel lens, chooser, keyboard move/resize, and configurable context padding, scrolling capture, stitching tools, local synthetic fixtures, and clipboard import. Android screenshot, bounded screenrecord, polling preview, and raw H.264 preview use the in-process Windows WinUSB/ADB transport with per-user authorization keys; an explicitly configured adb.exe is troubleshooting-only. Live safe-device proof remains claim-gated. File inspection, local QR/barcode decode, color picker, and pixel ruler are implemented. Opt-in production still capture uses Windows.Graphics.Capture + Direct3D11 for supported targets, with explicit desktop-duplication diagnostics and safe app-owned proof renderers. MP4 recording can use Windows.Graphics.Capture + Direct3D11 frame capture for active monitor, active window, and explicit single-monitor region/fixed-region targets; all-monitor recording uses screenshot-frame capture across the virtual desktop. {WindowsCaptureStatus(capabilities)}",
            RecordingEngine = $"Implemented: short active-monitor GIF recording with pause/resume and cursor/click visualization through periodic local GDI captures, CLI GIF/MP4 recording targets for active monitor, all monitors, active window, explicit or interactive region, and fixed region, desktop 5-second MP4 controls for active monitor and all monitors, named recording profiles ({_settings.RecordingProfiles.Count(profile => !string.IsNullOrWhiteSpace(profile.Name))} configured), optional native Media Foundation MP4 production encoding, native AAC mux/mix, bounded webcam overlay, live webcam preview, and bundled LGPL FFmpeg/ffprobe fallback and editing tools in Personal V1 distribution builds. Person segmentation uses the hash-pinned embedded ONNX model through DirectML with CPU fallback; explicit `video person-mask --runner` and `video hosted-person-mask` integrations remain advanced overrides. Configured MP4 profile: {FormatConfiguredRecordingProfile(_settings.Recording)} {DescribeRecordingDeviceSelection(_settings.Recording)} Cursor visualization is {(_settings.IncludeCursor ? "enabled" : "disabled")} by the capture cursor setting. App-owned visual and focus/name audits are automated; live narrated screen-reader, 200% scaling, multi-monitor, long-recording, audio, and webcam observations remain claim-gated until matching operator evidence is recorded.",
            RecordingReadiness = $"Recording engine plan: choice={recordingPlan.ChoiceLabel}. {FormatProductionPrerequisites(capabilities, _settings.Recording)} Production encoder selection: {recordingPlan.ProductionEncoder.Summary} FFmpeg fallback encoder selection: {recordingPlan.FallbackVideoEncoder.Summary} {recordingPlan.Summary} {recordingConfidence.Summary}",
            EncoderStatus = $"{capabilities.EncoderStatus} Production encoder selection: {productionEncoder.Summary} FFmpeg fallback encoder selection: {recordingPlan.FallbackVideoEncoder.Summary} Native Media Foundation MP4 encoding is wired for production recording when WGC/D3D frame capture and the selected Media Foundation encoder prerequisites are met; H.264 is the default codec, HEVC is explicit opt-in only when a local HEVC transform is reported, captured WASAPI WAV payloads can be mixed and muxed as AAC, and ffprobe metadata proof is available through `diagnostics recording-media <mp4>` when ffprobe is installed; webcam overlay can precompose bounded, periodically refreshed native camera frames before encode; desktop live preview is wired through the WPF recording panel.",
            OcrStatus = $"Implemented: local Windows.Media.Ocr fallback for image files and selected captures. Language: {(_settings.OcrLanguageTag.Length == 0 ? "Windows user profile" : _settings.OcrLanguageTag)}.",
            AiStatus = aiStatus,
            PolicyStatus = policy.Summary,
            StartupStatus = _startup.GetState().Message,
            MetadataIndexStatus = _workspaceIndex.GetStatus(),
            UploadQueueStatus = _settings.UploadQueue.EnableQueue
                ? policy.DisableUploads
                    ? $"{_uploadQueue.GetStatusSummary()} {_uploadQueueWorker.GetStatusSummary()} External upload processing is blocked by managed policy."
                    : $"{_uploadQueue.GetStatusSummary()} {_uploadQueueWorker.GetStatusSummary()}"
                : "Upload queue is disabled in settings.",
            PrintImportStatus = DescribePrintImportStatus(),
            PluginStatus = new LocalPluginService(_paths, _settings).GetDiagnosticsSummary(),
            BrowserBridgeStatus = $"{new BrowserExtensionNativeBridgeService(_paths, new WorkspaceStore(_paths, _settings)).GetStatusSummary()} {new BrowserNativeHostRegistrationService(_paths).GetStatusSummary()}",
            SharingStatus = $"Implemented: clipboard image/file/path, markdown link, local folder export, custom script, custom webhook upload, WebDAV HTTP PUT upload, Slack incoming-webhook notification, Discord webhook file upload, Microsoft Teams incoming-webhook notification, FTP/FTPS upload, S3-compatible signed PUT upload, Imgur Client-ID upload, in-process SSH.NET SFTP upload with strict host-key pinning, Cloudinary upload, issue-tracker adapters, configured cloud-provider adapters, email handoff, provider-readiness diagnostics, searchable redacted share/upload history, local upload queue state, desktop queue controls, watch-folder automation, and rule automation with {_settings.AutomationRules.Count(rule => rule.IsEnabled)} enabled rule(s). {policy.Summary} SFTP client status: {SftpClientStatus()} GitHub token saved: {_secretStore.HasGitHubToken}. Jira token saved: {_secretStore.HasJiraApiToken}. Azure DevOps PAT saved: {_secretStore.HasAzureDevOpsPat}. WebDAV password saved: {_secretStore.HasWebDavPassword}. FTP/FTPS password saved: {_secretStore.HasFtpPassword}. Watch folders are {(_settings.EnableWatchFolders ? "enabled" : "disabled")} with {_settings.WatchFolders.Count} configured path(s). OAuth and live provider proof remain parked."
        };
    }

    private string DescribePrintImportStatus()
    {
        var diagnostics = new VirtualPrinterImportService(
                _paths,
                _settings,
                new WorkspaceStore(_paths, _settings))
            .GetFeasibilityDiagnostics();
        return $"Virtual printer/file-drop import is {(diagnostics.Enabled ? "enabled" : "disabled")}. Watched folder state: {diagnostics.WatchedFolderState}. Policy status: {diagnostics.PolicyStatus}. Drop folder: {diagnostics.DropFolder}. Include subdirectories: {diagnostics.IncludeSubdirectories}. Supported extensions: {string.Join(", ", diagnostics.SupportedExtensions)}. {diagnostics.DropFolderStatus} Installed printer probe: {diagnostics.InstalledPrinterProbeStatus} Microsoft Print to PDF detected: {diagnostics.MicrosoftPrintToPdfDetected}. Current process elevated: {diagnostics.CurrentProcessElevated}. Driver install implemented: {diagnostics.DriverInstallImplemented}; requires admin: {diagnostics.DriverInstallRequiresAdmin}; signed driver required: {diagnostics.DriverSigningRequired}. {diagnostics.Summary}";
    }

    public async Task<RecordingEnginePlan> GetRecordingEnginePlanAsync(CancellationToken cancellationToken = default)
    {
        return (await GetRecordingReadinessSnapshotAsync(cancellationToken)).EnginePlan;
    }

    public async Task<RecordingReadinessSnapshot> GetRecordingReadinessSnapshotAsync(CancellationToken cancellationToken = default) =>
        await GetRecordingReadinessSnapshotAsync(null, cancellationToken);

    public async Task<RecordingReadinessSnapshot> GetRecordingReadinessSnapshotAsync(
        RecordingSettings? recordingSettings,
        CancellationToken cancellationToken = default)
    {
        var capabilities = RecordingCapabilityProbe.Probe(refresh: true);
        var settings = recordingSettings ?? _settings.Recording;
        var devices = await GetRecordingDeviceSnapshotAsync(settings, cancellationToken);
        var plan = RecordingEnginePlanner.BuildPlan(
            settings,
            capabilities,
            devices.Issues,
            productionEngineImplemented: NativeMediaFoundationMp4Encoder.ProductionEngineImplemented,
            productionFrameCaptureImplemented: true,
            productionAudioMixingImplemented: NativeMediaFoundationMp4Encoder.ProductionAudioMixingImplemented,
            productionWebcamCompositorImplemented: NativeMediaFoundationMp4Encoder.ProductionWebcamCompositorImplemented);
        var confidence = RecordingConfidenceService.BuildReadinessReport(
            settings,
            capabilities,
            plan,
            devices);
        return new RecordingReadinessSnapshot(plan, devices, confidence);
    }

    public async Task<RecordingDeviceSnapshot> GetRecordingDeviceSnapshotAsync(CancellationToken cancellationToken = default) =>
        await GetRecordingDeviceSnapshotAsync(null, cancellationToken);

    public async Task<RecordingDeviceSnapshot> GetRecordingDeviceSnapshotAsync(
        RecordingSettings? recordingSettings,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        var microphones = await SafeListAsync(
            () => _audioCapture.ListInputDevicesAsync(cancellationToken),
            "Microphone device discovery failed",
            issues);
        var systemAudioDevices = await SafeListAsync(
            () => _audioCapture.ListLoopbackDevicesAsync(cancellationToken),
            "System-audio device discovery failed",
            issues);
        var cameras = await SafeListAsync(
            () => _cameraOverlay.ListDevicesAsync(cancellationToken),
            "Camera device discovery failed",
            issues);

        var settings = recordingSettings ?? _settings.Recording ?? new RecordingSettings();
        issues.AddRange(FindSelectionIssues(settings, microphones, systemAudioDevices, cameras));

        return new RecordingDeviceSnapshot(
            microphones,
            systemAudioDevices,
            cameras,
            DescribeRecordingDeviceSelection(settings, microphones, systemAudioDevices, cameras),
            issues,
            DescribeAudioCaptureReadiness(settings, microphones, systemAudioDevices))
        {
            Confidence = RecordingConfidenceService.BuildDeviceReport(
                settings,
                microphones,
                systemAudioDevices,
                cameras,
                issues)
        };
    }

    public static string DescribeRecordingDeviceSelection(
        RecordingSettings? settings,
        IReadOnlyList<AudioCaptureDevice>? microphones = null,
        IReadOnlyList<AudioCaptureDevice>? systemAudioDevices = null,
        IReadOnlyList<CameraOverlayDevice>? cameras = null)
    {
        settings ??= new RecordingSettings();
        return $"Recording device selections: microphone={DescribeSelectedDevice(settings.MicrophoneDeviceId, microphones)}, system-audio={DescribeSelectedDevice(settings.SystemAudioDeviceId, systemAudioDevices)}, webcam={DescribeSelectedDevice(settings.WebcamDeviceId, cameras)}.";
    }

    public static IReadOnlyList<string> FindSelectionIssues(
        RecordingSettings? settings,
        IReadOnlyList<AudioCaptureDevice> microphones,
        IReadOnlyList<AudioCaptureDevice> systemAudioDevices,
        IReadOnlyList<CameraOverlayDevice> cameras)
    {
        settings ??= new RecordingSettings();
        var issues = new List<string>();
        AddSelectionIssue(issues, "microphone", settings.IncludeMicrophone, settings.MicrophoneDeviceId, microphones.Select(device => device.Id));
        AddSelectionIssue(issues, "system-audio", settings.IncludeSystemAudio, settings.SystemAudioDeviceId, systemAudioDevices.Select(device => device.Id));
        AddSelectionIssue(issues, "webcam", settings.EnableWebcamOverlay, settings.WebcamDeviceId, cameras.Select(device => device.Id));
        return issues;
    }

    public static string DescribeAudioCaptureReadiness(
        RecordingSettings? settings,
        IReadOnlyList<AudioCaptureDevice> microphones,
        IReadOnlyList<AudioCaptureDevice> systemAudioDevices)
    {
        settings ??= new RecordingSettings();
        var microphone = DescribeAudioEndpointReadiness(
            "microphone",
            settings.IncludeMicrophone,
            settings.MicrophoneDeviceId,
            microphones);
        var systemAudio = DescribeAudioEndpointReadiness(
            "system-audio loopback",
            settings.IncludeSystemAudio,
            settings.SystemAudioDeviceId,
            systemAudioDevices);
        var metered = microphones.Concat(systemAudioDevices).Count(device => device.PeakLevel.HasValue);
        var meterText = metered == 0
            ? "WASAPI peak meters are not currently available."
            : $"WASAPI peak meters are available for {metered} endpoint(s).";
        var micControls = AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(
            settings.MicrophoneGain,
            settings.NoiseGateThresholdDb,
            settings.MicrophoneMuted));
        var systemControls = AudioSampleProcessor.Describe(new AudioCaptureProcessingSettings(
            settings.SystemAudioGain,
            settings.NoiseGateThresholdDb,
            settings.SystemAudioMuted));
        return $"Audio capture readiness: {microphone}; {systemAudio}. {meterText} Audio controls: microphone {micControls}; system-audio {systemControls}. Short WAV proof capture is available through `record audio`; MP4 result messages include metadata-only audio timing, requested duration, WAV duration, byte count, and duration-delta fields; captured WAV payloads can be mixed and muxed as AAC in native Media Foundation MP4 recording when payload bytes are available, with FFmpeg still available as fallback.";
    }

    private static string FormatProductionPrerequisites(
        RecordingCapabilitySnapshot capabilities,
        RecordingSettings? settings)
    {
        settings ??= new RecordingSettings();
        var selectedEncoder = capabilities.SelectProductionVideoEncoder(settings);
        var codec = settings.PreferHevcEncoding ? "HEVC preferred" : "H.264 default";
        return $"WGC={(capabilities.WindowsGraphicsCaptureSupported ? "available" : "unavailable")}, " +
            $"D3D11={(capabilities.Direct3D11Device.Succeeded ? $"available feature-level {capabilities.Direct3D11Device.FeatureLevelLabel}" : "unavailable")}, " +
            $"Media Foundation H.264={(capabilities.MediaFoundationH264Available ? "available" : "unavailable")}, " +
            $"Media Foundation HEVC={(capabilities.MediaFoundationHevcAvailable ? "available" : "unavailable")}, " +
            $"selected production codec={selectedEncoder.Codec} ({selectedEncoder.ChoiceLabel}, {codec}), " +
            $"FFmpeg fallback={(capabilities.Ffmpeg.Available ? "available" : "unavailable")}.";
    }

    private static string FormatConfiguredRecordingProfile(RecordingSettings? settings)
    {
        var normalized = RecordingSettingsNormalizer.Normalize(settings);
        var overlays = normalized.ShowRecordingTimer || normalized.ShowKeystrokeOverlay || normalized.ShowRecordingBorder
            ? $"Overlays: border={(normalized.ShowRecordingBorder ? "on" : "off")}, timer={(normalized.ShowRecordingTimer ? normalized.RecordingTimerPosition : "off")}, keys={(normalized.ShowKeystrokeOverlay ? normalized.KeystrokeOverlayPosition : "off")}, badge {normalized.RecordingOverlayBadgeFontSize}px {normalized.RecordingOverlayStyle}."
            : "Overlays: off.";
        var requestedTracks = new List<string>();
        if (normalized.IncludeMicrophone)
        {
            requestedTracks.Add("microphone");
        }

        if (normalized.IncludeSystemAudio)
        {
            requestedTracks.Add("system audio");
        }

        if (normalized.EnableWebcamOverlay)
        {
            requestedTracks.Add("webcam overlay");
        }

        var audioRequested = normalized.IncludeMicrophone || normalized.IncludeSystemAudio;
        var route = requestedTracks.Count == 0
            ? "Video-only MP4 can use native Media Foundation production encoding when prerequisites are met."
            : normalized.EnableWebcamOverlay
                ? $"Requested {string.Join(", ", requestedTracks)} include webcam overlay, so MP4 recording can use bounded native camera-frame refresh/precomposition when camera access succeeds, with FFmpeg DirectShow still available as fallback."
                : audioRequested
                    ? $"Requested {string.Join(", ", requestedTracks)} can use native Media Foundation production encoding with AAC audio when payload bytes are available."
                    : $"Requested {string.Join(", ", requestedTracks)} route MP4 recording through the FFmpeg fallback.";
        return $"{normalized.QualityProfile}, {normalized.FramesPerSecond} fps, {normalized.SizeLabel}, {normalized.BitrateLabel}. {overlays} {route}";
    }

    private static async Task<IReadOnlyList<T>> SafeListAsync<T>(
        Func<Task<IReadOnlyList<T>>> action,
        string label,
        ICollection<string> issues)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            issues.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<T>();
        }
    }

    private static string DescribeSelectedDevice<T>(string? selectedId, IReadOnlyList<T>? devices)
        where T : notnull
    {
        if (string.IsNullOrWhiteSpace(selectedId))
        {
            return "system default";
        }

        if (devices is null)
        {
            return $"configured ({selectedId})";
        }

        var displayName = devices
            .Select(device => device switch
            {
                AudioCaptureDevice audio => audio.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase) ? audio.DisplayName : null,
                CameraOverlayDevice camera => camera.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase) ? camera.DisplayName : null,
                _ => null
            })
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(displayName)
            ? $"configured but unavailable ({selectedId})"
            : displayName;
    }

    private static void AddSelectionIssue(
        ICollection<string> issues,
        string label,
        bool enabled,
        string selectedId,
        IEnumerable<string> availableIds)
    {
        if (!enabled || string.IsNullOrWhiteSpace(selectedId))
        {
            return;
        }

        if (!availableIds.Any(id => id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"Configured {label} device is not currently available: {selectedId}");
        }
    }

    private static string DescribeAudioEndpointReadiness(
        string label,
        bool requested,
        string selectedId,
        IReadOnlyList<AudioCaptureDevice> devices)
    {
        if (!requested)
        {
            return $"{label} not requested";
        }

        if (devices.Count == 0)
        {
            return $"{label} requested but no active endpoint was found";
        }

        var device = string.IsNullOrWhiteSpace(selectedId)
            ? devices.FirstOrDefault(endpoint => endpoint.IsDefault) ?? devices[0]
            : devices.FirstOrDefault(endpoint => endpoint.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            return $"{label} requested but selected endpoint is unavailable";
        }

        var peak = device.PeakLevel.HasValue
            ? $" peak={device.PeakLevel.Value:P0}"
            : string.Empty;
        return $"{label} requested and endpoint available ({device.DisplayName}{peak})";
    }

    private string SftpClientStatus()
    {
        var keyExists = !string.IsNullOrWhiteSpace(_settings.SftpPrivateKeyPath) &&
            File.Exists(Environment.ExpandEnvironmentVariables(_settings.SftpPrivateKeyPath));
        var hostKeyPinned = !string.IsNullOrWhiteSpace(_settings.SftpHostKeyFingerprint);
        return $"Embedded SSH.NET client available; private key {(keyExists ? "found" : "not configured or missing")}; host key fingerprint {(hostKeyPinned ? "pinned" : "not pinned")}.";
    }

    private static string WindowsCaptureStatus(RecordingCapabilitySnapshot capabilities)
    {
        return $"{capabilities.WindowsGraphicsCaptureStatus} {capabilities.Direct3D11Device.Status} {DisplayStatus()} {DisplayAdapterStatus()}";
    }

    private static string ProbeWindowsGraphicsCapture()
    {
        try
        {
            return global::Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()
                ? "Windows.Graphics.Capture API probe: supported on this machine."
                : "Windows.Graphics.Capture API probe: not supported on this machine.";
        }
        catch (Exception ex)
        {
            return $"Windows.Graphics.Capture API probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string DisplayStatus()
    {
        try
        {
            var screens = Forms.Screen.AllScreens
                .Select((screen, index) => $"{index + 1}:{screen.DeviceName} {screen.Bounds.Width}x{screen.Bounds.Height}@{screen.Bounds.X},{screen.Bounds.Y}{(screen.Primary ? " primary" : string.Empty)}")
                .ToList();

            return screens.Count == 0
                ? "Display probe: no screens reported by Windows Forms."
                : $"Display probe: {string.Join("; ", screens)}.";
        }
        catch (Exception ex)
        {
            return $"Display probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string DisplayAdapterStatus()
    {
        try
        {
            var adapters = new List<string>();
            for (uint index = 0; index < 16; index++)
            {
                var device = new DisplayDevice
                {
                    cb = Marshal.SizeOf<DisplayDevice>()
                };

                if (!EnumDisplayDevices(null, index, ref device, 0))
                {
                    break;
                }

                if ((device.StateFlags & DisplayDeviceActive) == 0)
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(device.DeviceString)
                    ? device.DeviceName
                    : device.DeviceString;
                if (!string.IsNullOrWhiteSpace(name) &&
                    adapters.All(existing => !existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    adapters.Add(name.Trim());
                }
            }

            return adapters.Count == 0
                ? "Display adapter probe: no active display adapters reported."
                : $"Display adapter probe: {string.Join("; ", adapters)}.";
        }
        catch (Exception ex)
        {
            return $"Display adapter probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string EncoderStatus()
    {
        return $"{MediaFoundationEncoderStatus()} {FfmpegEncoderStatus()}";
    }

    private static string MediaFoundationEncoderStatus()
    {
        var startup = MFStartup(MfVersion, 0);
        if (startup < 0)
        {
            return $"Media Foundation encoder probe failed to start: HRESULT {FormatHResult(startup)}.";
        }

        try
        {
            var h264Hardware = EnumerateEncoderTransforms(MfVideoFormatH264, hardwareOnly: true);
            var hevcHardware = EnumerateEncoderTransforms(MfVideoFormatHevc, hardwareOnly: true);
            var h264Installed = EnumerateEncoderTransforms(MfVideoFormatH264, hardwareOnly: false);
            var hevcInstalled = EnumerateEncoderTransforms(MfVideoFormatHevc, hardwareOnly: false);

            return "Media Foundation encoder probe: " +
                $"H.264 hardware={FormatEncoderProbe(h264Hardware)}, " +
                $"HEVC hardware={FormatEncoderProbe(hevcHardware)}, " +
                $"H.264 installed={FormatEncoderProbe(h264Installed)}, " +
                $"HEVC installed={FormatEncoderProbe(hevcInstalled)}. " +
                "Native production MP4 encoding uses the selected Media Foundation encoder when WGC/D3D frame capture is available; H.264 is the default and HEVC is opt-in only when a local HEVC transform is reported. The native path can mux captured WAV payloads as AAC and can precompose bounded, periodically refreshed native camera frames before encode; desktop live webcam preview is available in the recording panel.";
        }
        catch (Exception ex)
        {
            return $"Media Foundation encoder probe failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _ = MFShutdown();
        }
    }

    private static EncoderProbeResult EnumerateEncoderTransforms(Guid videoSubtype, bool hardwareOnly)
    {
        var outputType = new MftRegisterTypeInfo
        {
            MajorType = MfMediaTypeVideo,
            Subtype = videoSubtype
        };
        var category = MftCategoryVideoEncoder;
        var flags = hardwareOnly ? MftEnumFlagHardware | MftEnumFlagSortAndFilter : MftEnumFlagAll;

        var hr = MFTEnumEx(ref category, flags, IntPtr.Zero, ref outputType, out var activates, out var count);
        if (hr < 0)
        {
            return new EncoderProbeResult(false, 0, FormatHResult(hr));
        }

        ReleaseActivateArray(activates, count);
        return new EncoderProbeResult(true, count, string.Empty);
    }

    private static string FormatEncoderProbe(EncoderProbeResult result)
    {
        return result.Succeeded ? result.Count.ToString() : $"failed({result.Error})";
    }

    private static string FfmpegEncoderStatus()
    {
        var configured = Environment.GetEnvironmentVariable("GOATSHOT_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.ExpandEnvironmentVariables(configured);
            if (!File.Exists(configured))
            {
                return $"FFmpeg fallback encoder configured but missing: {configured}";
            }
        }
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return "FFmpeg fallback encoder probe: ffmpeg.exe was not found on PATH and GOATSHOT_FFMPEG_PATH is not set.";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-hide_banner",
                    "-encoders"
                }
            });

            if (process is null)
            {
                return $"FFmpeg fallback encoder probe: could not start {ffmpeg}.";
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return $"FFmpeg fallback encoder probe timed out: {ffmpeg}.";
            }

            var encoders = stdout.GetAwaiter().GetResult() + Environment.NewLine + stderr.GetAwaiter().GetResult();
            var hardware = CommonHardwareFfmpegEncoders
                .Where(encoder => encoders.Contains(encoder, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var software = new[]
            {
                "libx264",
                "libx265"
            }.Where(encoder => encoders.Contains(encoder, StringComparison.OrdinalIgnoreCase)).ToList();

            var hardwareText = hardware.Count == 0
                ? "no common hardware H.264/HEVC encoders reported"
                : $"hardware encoders reported: {string.Join(", ", hardware)}";
            var softwareText = software.Count == 0
                ? "software x264/x265 encoders not reported"
                : $"software encoders reported: {string.Join(", ", software)}";

            return $"FFmpeg fallback encoder probe: {ffmpeg}; {hardwareText}; {softwareText}.";
        }
        catch (Exception ex)
        {
            return $"FFmpeg fallback encoder probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void ReleaseActivateArray(IntPtr activates, int count)
    {
        if (activates == IntPtr.Zero)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var activate = Marshal.ReadIntPtr(activates, index * IntPtr.Size);
            if (activate != IntPtr.Zero)
            {
                _ = Marshal.Release(activate);
            }
        }

        Marshal.FreeCoTaskMem(activates);
    }

    private static string FormatHResult(int hr)
    {
        return $"0x{unchecked((uint)hr):X8}";
    }

    private const int DisplayDeviceActive = 0x00000001;
    private const int MfVersion = 0x00020070;
    private const int MftEnumFlagSyncMft = 0x00000001;
    private const int MftEnumFlagAsyncMft = 0x00000002;
    private const int MftEnumFlagHardware = 0x00000004;
    private const int MftEnumFlagLocalMft = 0x00000010;
    private const int MftEnumFlagSortAndFilter = 0x00000040;
    private const int MftEnumFlagAll = MftEnumFlagSyncMft | MftEnumFlagAsyncMft | MftEnumFlagHardware | MftEnumFlagLocalMft | MftEnumFlagSortAndFilter;

    private static readonly Guid MftCategoryVideoEncoder = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatHevc = new("43564548-0000-0010-8000-00aa00389b71");
    private static readonly string[] CommonHardwareFfmpegEncoders =
    [
        "h264_nvenc",
        "hevc_nvenc",
        "h264_qsv",
        "hevc_qsv",
        "h264_amf",
        "hevc_amf",
        "h264_mf",
        "hevc_mf"
    ];

    private readonly record struct EncoderProbeResult(bool Succeeded, int Count, string Error);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftRegisterTypeInfo
    {
        public Guid MajorType;
        public Guid Subtype;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DisplayDevice lpDisplayDevice,
        uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(
        ref Guid guidCategory,
        int flags,
        IntPtr inputType,
        ref MftRegisterTypeInfo outputType,
        out IntPtr activates,
        out int count);
}

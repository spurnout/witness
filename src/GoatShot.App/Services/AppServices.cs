using GoatShot.App.Models;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class AppServices : IDisposable
{
    private readonly Func<IReplayRecordingService> _replayFactory;
    private readonly SemaphoreSlim _replayReconfigurationGate = new(1, 1);
    private string _replayConfigurationFingerprint;

    private AppServices(
        AppSettings settings,
        AppPaths paths,
        BundledToolResolver bundledTools,
        SettingsStore settingsStore,
        WorkspaceStore workspaceStore,
        WorkspaceMetadataIndex workspaceIndex,
        WorkflowProfileService workflowProfiles,
        WorkflowRunLogService workflowRunLogs,
        WorkflowActionDryRunService workflowDryRuns,
        ScreenshotService screenshots,
        ShareService sharing,
        UploadQueueService uploadQueue,
        UploadQueueWorkerService uploadQueueWorker,
        MetadataService metadata,
        FileInspectorService fileInspector,
        ClipboardImportService clipboardImports,
        ColorPickerService colorPicker,
        BarcodeService barcodes,
        OcrService ocr,
        VisualRedactionService visualRedactions,
        AutomationService automation,
        WatchFolderService watchFolders,
        SecretStore secretStore,
        ProviderDiagnosticsService providerDiagnostics,
        LocalPluginService localPlugins,
        RemotePluginPackageService remotePlugins,
        PersonSegmentationModelPackageService personSegmentationModels,
        PersonSegmentationInferenceService personSegmentation,
        BrowserExtensionNativeBridgeService browserExtensionBridge,
        BrowserNativeHostRegistrationService browserNativeHosts,
        OAuthFlowService oauthFlow,
        OAuthBrowserFlowService oauthBrowserFlow,
        StartupRegistrationService startup,
        PersonalInstallService personalInstall,
        GeminiImageProvider gemini,
        AiActionHistoryService aiHistory,
        IAudioCaptureService audioCapture,
        ICameraOverlayService cameraOverlay,
        RecordingService recording,
        ReceiptDeviceKeyService receiptDeviceKeys,
        ReceiptIntegrityService receiptIntegrity,
        IReplayRecordingService replay,
        Func<IReplayRecordingService> replayFactory,
        ReceiptSceneAnalysisService receiptAnalysis,
        ReplayReceiptExplorerService receiptExplorer,
        VideoToolService videoTools,
        ImageLayoutService imageLayouts,
        TranscriptionService transcription,
        VideoIntelligenceService videoIntelligence,
        DiagnosticsService diagnostics,
        DiagnosticBundleService diagnosticBundles,
        BugReportService bugReports,
        DocumentationPacketService documentationPackets,
        StepRecorderService stepRecorder,
        HotkeyService hotkeys)
    {
        Settings = settings;
        Paths = paths;
        BundledTools = bundledTools;
        SettingsStore = settingsStore;
        WorkspaceStore = workspaceStore;
        WorkspaceIndex = workspaceIndex;
        WorkflowProfiles = workflowProfiles;
        WorkflowRunLogs = workflowRunLogs;
        WorkflowDryRuns = workflowDryRuns;
        Screenshots = screenshots;
        Sharing = sharing;
        UploadQueue = uploadQueue;
        UploadQueueWorker = uploadQueueWorker;
        Metadata = metadata;
        FileInspector = fileInspector;
        ClipboardImports = clipboardImports;
        ColorPicker = colorPicker;
        Barcodes = barcodes;
        Ocr = ocr;
        VisualRedactions = visualRedactions;
        Automation = automation;
        WatchFolders = watchFolders;
        SecretStore = secretStore;
        ProviderDiagnostics = providerDiagnostics;
        LocalPlugins = localPlugins;
        RemotePlugins = remotePlugins;
        PersonSegmentationModels = personSegmentationModels;
        PersonSegmentation = personSegmentation;
        BrowserExtensionBridge = browserExtensionBridge;
        BrowserNativeHosts = browserNativeHosts;
        OAuthFlow = oauthFlow;
        OAuthBrowserFlow = oauthBrowserFlow;
        Startup = startup;
        PersonalInstall = personalInstall;
        Gemini = gemini;
        AiHistory = aiHistory;
        AudioCapture = audioCapture;
        CameraOverlay = cameraOverlay;
        Recording = recording;
        ReceiptDeviceKeys = receiptDeviceKeys;
        ReceiptIntegrity = receiptIntegrity;
        Replay = replay;
        _replayFactory = replayFactory;
        _replayConfigurationFingerprint = BuildReplayConfigurationFingerprint(settings);
        ReceiptAnalysis = receiptAnalysis;
        ReceiptExplorer = receiptExplorer;
        VideoTools = videoTools;
        ImageLayouts = imageLayouts;
        Transcription = transcription;
        VideoIntelligence = videoIntelligence;
        Diagnostics = diagnostics;
        DiagnosticBundles = diagnosticBundles;
        BugReports = bugReports;
        DocumentationPackets = documentationPackets;
        StepRecorder = stepRecorder;
        Hotkeys = hotkeys;
    }

    public AppSettings Settings { get; }
    public AppPaths Paths { get; }
    public BundledToolResolver BundledTools { get; }
    public SettingsStore SettingsStore { get; }
    public WorkspaceStore WorkspaceStore { get; }
    public WorkspaceMetadataIndex WorkspaceIndex { get; }
    public WorkflowProfileService WorkflowProfiles { get; }
    public WorkflowRunLogService WorkflowRunLogs { get; }
    public WorkflowActionDryRunService WorkflowDryRuns { get; }
    public ScreenshotService Screenshots { get; }
    public ShareService Sharing { get; }
    public UploadQueueService UploadQueue { get; }
    public UploadQueueWorkerService UploadQueueWorker { get; }
    public MetadataService Metadata { get; }
    public FileInspectorService FileInspector { get; }
    public ClipboardImportService ClipboardImports { get; }
    public ColorPickerService ColorPicker { get; }
    public BarcodeService Barcodes { get; }
    public OcrService Ocr { get; }
    public VisualRedactionService VisualRedactions { get; }
    public AutomationService Automation { get; }
    public WatchFolderService WatchFolders { get; }
    public SecretStore SecretStore { get; }
    public ProviderDiagnosticsService ProviderDiagnostics { get; }
    public LocalPluginService LocalPlugins { get; }
    public RemotePluginPackageService RemotePlugins { get; }
    public PersonSegmentationModelPackageService PersonSegmentationModels { get; }
    public PersonSegmentationInferenceService PersonSegmentation { get; }
    public BrowserExtensionNativeBridgeService BrowserExtensionBridge { get; }
    public BrowserNativeHostRegistrationService BrowserNativeHosts { get; }
    public OAuthFlowService OAuthFlow { get; }
    public OAuthBrowserFlowService OAuthBrowserFlow { get; }
    public StartupRegistrationService Startup { get; }
    public PersonalInstallService PersonalInstall { get; }
    public GeminiImageProvider Gemini { get; }
    public AiActionHistoryService AiHistory { get; }
    public IAudioCaptureService AudioCapture { get; }
    public ICameraOverlayService CameraOverlay { get; }
    public RecordingService Recording { get; }
    public ReceiptDeviceKeyService ReceiptDeviceKeys { get; }
    public ReceiptIntegrityService ReceiptIntegrity { get; }
    public IReplayRecordingService Replay { get; private set; }
    public event EventHandler? ReplayServiceChanged;
    public ReceiptSceneAnalysisService ReceiptAnalysis { get; }
    public ReplayReceiptExplorerService ReceiptExplorer { get; }
    public VideoToolService VideoTools { get; }
    public ImageLayoutService ImageLayouts { get; }
    public TranscriptionService Transcription { get; }
    public VideoIntelligenceService VideoIntelligence { get; }
    public DiagnosticsService Diagnostics { get; }
    public DiagnosticBundleService DiagnosticBundles { get; }
    public BugReportService BugReports { get; }
    public DocumentationPacketService DocumentationPackets { get; }
    public StepRecorderService StepRecorder { get; }
    public HotkeyService Hotkeys { get; }
    public TrayService? Tray { get; private set; }

    public static AppServices Create()
    {
        StartupTrace.Write("Receipts legacy-state migration");
        var migrationService = ReceiptsLocalStateMigrationService.CreateForCurrentUser();
        var migration = migrationService
            .MigrateAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        StartupTrace.Write($"Receipts migration status={migration.Status} copied={migration.CopiedFileCount}");
        StartupTrace.Write("AppServices settings load");
        var settingsStore = new SettingsStore();
        var legacySettingsPath = Path.Combine(migration.LegacyRoot, "settings.json");
        var useLegacyStateFallback = ShouldUseLegacyStateFallback(migration);
        if (useLegacyStateFallback)
        {
            StartupTrace.Write(
                "Receipts migration did not copy legacy settings; using the legacy state root for this " +
                "startup so configured library data remains visible and the migration can be retried.");
            settingsStore.UsePath(legacySettingsPath);
        }

        var settings = settingsStore.Load();
        var paths = AppPaths.Create(
            settings,
            useLegacyStateFallback ? migration.LegacyRoot : null);
        settingsStore.UsePath(paths.SettingsPath);
        settingsStore.Save(settings);
        StartupTrace.Write("AppServices bundled resolver");
        var bundledTools = new BundledToolResolver(Path.Combine(paths.LocalRoot, "runtime"));
        bundledTools.EnsureReadyAsync().GetAwaiter().GetResult();
        StartupTrace.Write("AppServices bundled runtime ready");
        SetBundledToolEnvironment("FFMPEG_PATH", bundledTools.Resolve("ffmpeg"));
        SetBundledToolEnvironment("FFPROBE_PATH", bundledTools.Resolve("ffprobe"));

        var workspaceIndex = new WorkspaceMetadataIndex(paths);
        var workspaceStore = new WorkspaceStore(paths, settings);
        workspaceStore.AttachMetadataIndex(workspaceIndex);
        workspaceIndex.Rebuild(workspaceStore.Load());
        var workflowProfiles = new WorkflowProfileService(settings, settingsStore);
        var workflowRunLogs = new WorkflowRunLogService(paths);
        var workflowDryRuns = new WorkflowActionDryRunService(settings, paths);
        var screenshots = new ScreenshotService(settings);
        var metadata = new MetadataService(paths);
        var fileInspector = new FileInspectorService();
        var clipboardImports = new ClipboardImportService(paths, workspaceStore);
        var colorPicker = new ColorPickerService();
        var barcodes = new BarcodeService();
        var ocr = new OcrService(settings, paths);
        var visualRedactions = new VisualRedactionService(paths, workspaceStore);
        var secretStore = new SecretStore(paths);
        var providerDiagnostics = new ProviderDiagnosticsService(settings, secretStore);
        var localPlugins = new LocalPluginService(paths, settings);
        var remotePlugins = new RemotePluginPackageService(paths, localPlugins, settings: settings, settingsStore: settingsStore);
        var personSegmentationModels = new PersonSegmentationModelPackageService(paths);
        var personSegmentation = new PersonSegmentationInferenceService(bundledTools);
        var browserExtensionBridge = new BrowserExtensionNativeBridgeService(paths, workspaceStore);
        var browserNativeHosts = new BrowserNativeHostRegistrationService(paths);
        var sharing = new ShareService(paths, settings, secretStore);
        var uploadQueue = new UploadQueueService(paths, settings.UploadQueue);
        var uploadQueueWorker = new UploadQueueWorkerService(settings.UploadQueue, uploadQueue, sharing);
        var startup = new StartupRegistrationService();
        var personalInstall = new PersonalInstallService(startup);
        var audioCapture = new WindowsAudioCaptureService();
        var cameraOverlay = new WindowsCameraOverlayService();
        var diagnostics = new DiagnosticsService(
            settings,
            paths,
            secretStore,
            startup,
            workspaceIndex,
            uploadQueue,
            uploadQueueWorker,
            audioCapture,
            cameraOverlay);
        var bugReports = new BugReportService(paths, diagnostics);
        var automation = new AutomationService(
            settings,
            paths,
            workspaceStore,
            sharing,
            metadata,
            ocr,
            visualRedactions,
            bugReports,
            workflowRunLogs);
        var watchFolders = new WatchFolderService(settings, paths, automation);
        var oauthFlow = new OAuthFlowService();
        var oauthBrowserFlow = new OAuthBrowserFlowService(oauthFlow, new OAuthCallbackServerService());
        var gemini = new GeminiImageProvider(settings, paths, secretStore);
        var aiHistory = new AiActionHistoryService(paths);
        var recording = new RecordingService(paths, settings, screenshots, workspaceStore, audioCapture, cameraOverlay);
        settings.Replay = (settings.Replay ?? new ReplayBufferSettings()).Normalize();
        var receiptDeviceKeys = new ReceiptDeviceKeyService();
        var receiptIntegrity = new ReceiptIntegrityService(receiptDeviceKeys);
        var replayStorage = new FileReplayBufferStorage(paths.ReplayBufferRoot);
        var receiptKeyPath = Path.Combine(paths.SecretsRoot, ReceiptDeviceKeyService.DefaultKeyFileName);
        var replayPublisher = new ReplayReceiptPackagePublisher(
            replayStorage,
            receiptIntegrity,
            receiptDeviceKeys,
            receiptKeyPath,
            settings);
        IReplayRecordingService CreateReplayService()
        {
            var replayEncoder = RecordingCapabilityProbe.Probe()
                .SelectProductionVideoEncoder(settings.Recording);
            return replayEncoder.IsAvailable
                ? new ReplayRecordingService(
                    settings.Replay,
                    settings.Recording,
                    settings.IncludeCursor,
                    replayStorage,
                    replayPublisher,
                    screenshots,
                    replayEncoder,
                    audioCapture,
                    cameraOverlay)
                : new UnavailableReplayRecordingService(replayEncoder.Summary);
        }

        var replay = CreateReplayService();
        var receiptAnalysis = new ReceiptSceneAnalysisService(ocr);
        var receiptExplorer = new ReplayReceiptExplorerService(paths, workspaceStore);
        var videoTools = new VideoToolService(paths, workspaceStore, personSegmentation: personSegmentation);
        var imageLayouts = new ImageLayoutService(paths, workspaceStore);
        var transcription = new TranscriptionService(paths, gemini, settings);
        var videoIntelligence = new VideoIntelligenceService(paths, transcription, gemini);
        var diagnosticBundles = new DiagnosticBundleService(paths, settings, workspaceStore, diagnostics, providerDiagnostics);
        var documentationPackets = new DocumentationPacketService(paths, aiHistory, bugReports);
        var stepRecorder = new StepRecorderService(paths, screenshots, workspaceStore, ocr);
        var hotkeys = new HotkeyService(settings.Keybinds);

        StartupTrace.Write("AppServices graph ready");
        return new AppServices(
            settings,
            paths,
            bundledTools,
            settingsStore,
            workspaceStore,
            workspaceIndex,
            workflowProfiles,
            workflowRunLogs,
            workflowDryRuns,
            screenshots,
            sharing,
            uploadQueue,
            uploadQueueWorker,
            metadata,
            fileInspector,
            clipboardImports,
            colorPicker,
            barcodes,
            ocr,
            visualRedactions,
            automation,
            watchFolders,
            secretStore,
            providerDiagnostics,
            localPlugins,
            remotePlugins,
            personSegmentationModels,
            personSegmentation,
            browserExtensionBridge,
            browserNativeHosts,
            oauthFlow,
            oauthBrowserFlow,
            startup,
            personalInstall,
            gemini,
            aiHistory,
            audioCapture,
            cameraOverlay,
            recording,
            receiptDeviceKeys,
            receiptIntegrity,
            replay,
            CreateReplayService,
            receiptAnalysis,
            receiptExplorer,
            videoTools,
            imageLayouts,
            transcription,
            videoIntelligence,
            diagnostics,
            diagnosticBundles,
            bugReports,
            documentationPackets,
            stepRecorder,
            hotkeys);
    }

    internal static bool ShouldUseLegacyStateFallback(LocalStateMigrationResult migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        return migration.Status == LocalStateMigrationStatus.Failed &&
            File.Exists(Path.Combine(migration.LegacyRoot, "settings.json"));
    }

    internal static ReplayBufferCleanupResult CleanupReplayBufferAtStartup(
        FileReplayBufferStorage storage,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return storage.CleanupAbandonedBufferFiles(
            Array.Empty<string>(),
            TimeSpan.Zero,
            nowUtc);
    }

    private static void SetBundledToolEnvironment(string suffix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || BrandEnvironment.Resolve(suffix).IsConfigured)
        {
            return;
        }

        // Set only the Receipts name. Compatibility-aware consumers fall back to
        // GOATSHOT_* when an operator explicitly configured the legacy alias.
        Environment.SetEnvironmentVariable(BrandIdentity.EnvironmentVariable(suffix), value);
    }

    public void AttachTray(MainWindow window)
    {
        Tray ??= new TrayService(window, Hotkeys);
    }

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
    }

    public async Task<ReplayReconfigurationResult> ReconfigureReplayIfNeededAsync(
        CancellationToken cancellationToken = default)
    {
        await _replayReconfigurationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nextFingerprint = BuildReplayConfigurationFingerprint(Settings);
            var current = Replay;
            var initialState = current.GetStatus().State;
            if (string.Equals(
                nextFingerprint,
                _replayConfigurationFingerprint,
                StringComparison.Ordinal))
            {
                return new ReplayReconfigurationResult(
                    false,
                    false,
                    initialState,
                    initialState,
                    "Settings saved. Replay capture did not need a buffer restart.");
            }

            ReplayCommandResult stop;
            ReplayBufferState previousState;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                previousState = current.GetStatus().State;
                if (previousState == ReplayBufferState.Saving)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                stop = await current.StopAsync(cancellationToken).ConfigureAwait(false);
                if (stop.Succeeded)
                {
                    break;
                }

                if (current.GetStatus().State != ReplayBufferState.Saving)
                {
                    return new ReplayReconfigurationResult(
                        false,
                        false,
                        previousState,
                        current.GetStatus().State,
                        $"Replay settings were saved but could not be applied: {stop.Message}");
                }
            }

            await current.DisposeAsync().ConfigureAwait(false);
            var replacement = _replayFactory();
            Replay = replacement;
            PublishReplayServiceChanged();
            _replayConfigurationFingerprint = nextFingerprint;

            var wasActive = previousState is ReplayBufferState.Armed or ReplayBufferState.Paused;
            if (wasActive && Settings.Replay.ConsentGranted)
            {
                var armed = await replacement.ArmAsync(cancellationToken).ConfigureAwait(false);
                if (!armed.Succeeded)
                {
                    return new ReplayReconfigurationResult(
                        true,
                        false,
                        previousState,
                        replacement.GetStatus().State,
                        $"Replay settings applied and the old unsaved buffer was cleared, but Replay could not re-arm: {armed.Message}");
                }

                if (previousState == ReplayBufferState.Paused)
                {
                    replacement.Pause();
                }
            }

            var currentState = replacement.GetStatus().State;
            var bufferRestarted = wasActive && Settings.Replay.ConsentGranted;
            var message = bufferRestarted
                ? $"Replay settings applied. The unsaved rolling buffer restarted in {currentState} state."
                : wasActive
                    ? "Replay settings applied. The unsaved rolling buffer was cleared and Replay stopped because consent is disabled."
                    : "Replay settings applied; the next arm will use the updated capture profile.";
            return new ReplayReconfigurationResult(
                true,
                bufferRestarted,
                previousState,
                currentState,
                message);
        }
        finally
        {
            _replayReconfigurationGate.Release();
        }
    }

    internal static string BuildReplayConfigurationFingerprint(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var replay = (settings.Replay ?? new ReplayBufferSettings()).Normalize();
        var recording = RecordingSettingsNormalizer.Normalize(
            settings.Recording,
            replay.FramesPerSecond);
        var source = replay.CaptureSource;
        var privacyProcesses = replay.PrivacyExcludedProcessNames
            .Select(value => value.ToUpperInvariant())
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            BufferDurationTicks = replay.BufferDuration.Ticks,
            SegmentDurationTicks = replay.SegmentDuration.Ticks,
            replay.MaxBufferBytes,
            replay.FramesPerSecond,
            Source = new
            {
                source.Kind,
                source.SourceId,
                source.DisplayName,
                source.Bounds
            },
            PrivacyExcludedProcessNames = privacyProcesses,
            settings.IncludeCursor,
            Recording = recording,
            settings.Recording.MicrophoneDeviceId,
            settings.Recording.SystemAudioDeviceId,
            settings.Recording.WebcamDeviceId,
            settings.Recording.WebcamOverlayPosition,
            settings.Recording.WebcamOverlayShape,
            settings.Recording.MirrorWebcam
        });
    }

    private void PublishReplayServiceChanged()
    {
        var handlers = ReplayServiceChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Replay replacement must remain transactional even if an observer
                // cannot refresh its process-local projection.
            }
        }
    }

    public void Dispose()
    {
        Tray?.Dispose();
        UploadQueueWorker.Dispose();
        StepRecorder.Dispose();
        Replay.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        Recording.Dispose();
        PersonSegmentation.Dispose();
        WatchFolders.Dispose();
        Hotkeys.Dispose();
        _replayReconfigurationGate.Dispose();
    }
}

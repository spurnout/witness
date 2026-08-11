using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AppServices : IDisposable
{
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
        StartupTrace.Write("AppServices settings load");
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var paths = AppPaths.Create(settings);
        settingsStore.UsePath(paths.SettingsPath);
        settingsStore.Save(settings);
        StartupTrace.Write("AppServices bundled resolver");
        var bundledTools = new BundledToolResolver(Path.Combine(paths.LocalRoot, "runtime"));
        bundledTools.EnsureReadyAsync().GetAwaiter().GetResult();
        StartupTrace.Write("AppServices bundled runtime ready");
        SetBundledToolEnvironment("GOATSHOT_FFMPEG_PATH", bundledTools.Resolve("ffmpeg"));
        SetBundledToolEnvironment("GOATSHOT_FFPROBE_PATH", bundledTools.Resolve("ffprobe"));

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
        var remotePlugins = new RemotePluginPackageService(paths, localPlugins, settings: settings);
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
        var videoTools = new VideoToolService(paths, workspaceStore, personSegmentation: personSegmentation);
        var imageLayouts = new ImageLayoutService(paths, workspaceStore);
        var transcription = new TranscriptionService(paths, gemini, settings);
        var videoIntelligence = new VideoIntelligenceService(paths, transcription, gemini);
        var diagnosticBundles = new DiagnosticBundleService(paths, settings, workspaceStore, diagnostics, providerDiagnostics);
        var documentationPackets = new DocumentationPacketService(paths, aiHistory, bugReports);
        var stepRecorder = new StepRecorderService(paths, screenshots, workspaceStore, ocr);
        var hotkeys = new HotkeyService();

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

    private static void SetBundledToolEnvironment(string variable, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }

    public void AttachTray(MainWindow window)
    {
        Tray ??= new TrayService(window);
    }

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
    }

    public void Dispose()
    {
        Tray?.Dispose();
        UploadQueueWorker.Dispose();
        StepRecorder.Dispose();
        Recording.Dispose();
        PersonSegmentation.Dispose();
        WatchFolders.Dispose();
        Hotkeys.Dispose();
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Services;
using GoatShot.App.Windows;

namespace GoatShot.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly ObservableCollection<CaptureItem> _captures = new();
    private readonly ObservableCollection<UploadQueueItem> _uploadQueueItems = new();
    private List<CaptureItem> _allCaptures = new();
    private bool _allowClose;
    private bool _recordingControlsReady;
    private bool _settingsPrewarmQueued;
    private SettingsWindow? _prewarmedSettingsWindow;
    private ShareHistoryWindow? _shareHistoryWindow;
    private AiHistoryWindow? _aiHistoryWindow;
    private CancellationTokenSource? _webcamPreviewCts;
    private Task? _webcamPreviewTask;
    private readonly bool _auditMode;
    private const string SensitiveScanNotePrefix = "Sensitive scan:";
    private const string AiAnalysisNotePrefix = "AI analysis:";

    public MainWindow(AppServices services, bool auditMode = false)
    {
        _services = services;
        _auditMode = auditMode;
        InitializeComponent();
        WpfAccessibilityNameHelper.ApplyGeneratedNames(this);
        CaptureList.ItemsSource = _captures;
        QueueList.ItemsSource = _uploadQueueItems;
    }

    public async void CaptureRegionCommand(string? hotkeyProfile = null) => await CaptureRegionAsync(hotkeyProfile);
    public async void CaptureWindowCommand(string? hotkeyProfile = null) => await CaptureActiveWindowAsync(hotkeyProfile);
    public async void CaptureScrollingWindowCommand() => await CaptureScrollingWindowAsync();
    public async void CaptureHorizontalScrollingWindowCommand() => await CaptureScrollingWindowAsync(ScrollingCaptureAxis.Horizontal);
    public async void CaptureFullscreenCommand(string? hotkeyProfile = null) => await CaptureAndStoreAsync(async () => await _services.Screenshots.CaptureFullScreenAsync(), hotkeyProfile);
    public async void CaptureAllMonitorsCommand(string? hotkeyProfile = null) => await CaptureAndStoreAsync(async () => await _services.Screenshots.CaptureAllMonitorsAsync(), hotkeyProfile);
    public async void CaptureMonitorCommand(string? hotkeyProfile = null) => await CaptureAndStoreAsync(async () => await _services.Screenshots.CaptureActiveMonitorAsync(), hotkeyProfile);
    public async void CaptureFixedRegionCommand(int width, int height, string? hotkeyProfile = null) => await CaptureAndStoreAsync(async () => await _services.Screenshots.CaptureFixedRegionAtCursorAsync(width, height), hotkeyProfile);
    public async void CaptureLastRegionCommand(string? hotkeyProfile = null) => await CaptureAndStoreAsync(() => _services.Screenshots.CaptureLastRegionAsync(this), hotkeyProfile);
    public async void ToggleStepRecorderCommand() => await ToggleStepRecorderAsync();
    public async void RecordShortMp4Command() => await RecordShortMp4Async(RecordingCaptureTarget.ActiveMonitor());
    public void ToggleRecordingPauseCommand()
    {
        var result = _services.Recording.TogglePauseRecording();
        SetStatus(result.Message);
        RefreshRecordingSetupText();
    }

    public async void ToggleRecordingCommand(string? hotkeyProfile = null)
    {
        await ToggleRecordingAsync(RecordingCaptureTarget.ActiveMonitor(), hotkeyProfile);
    }

    private async Task ToggleRecordingAsync(
        RecordingCaptureTarget target,
        string? hotkeyProfile = null)
    {
        var recordingSettings = ReadRecordingSettingsFromControls();
        _services.SaveSettings();
        var result = await _services.Recording.ToggleGifRecordingAsync(
            target,
            recordingSettings,
            new AnimationExportOptions
            {
                FrameRate = recordingSettings.FramesPerSecond,
                GifTimingMode = "Smooth",
                Quality = "High"
            });
        if (result.Item is not null)
        {
            if (!string.IsNullOrWhiteSpace(hotkeyProfile))
            {
                result.Item.HotkeyProfile = HotkeyProfileNames.Normalize(hotkeyProfile);
                await _services.WorkspaceStore.UpdateItemAsync(result.Item);
            }

            _allCaptures.Insert(0, result.Item);
            ApplyFilter();
            CaptureList.SelectedItem = result.Item;
            await _services.Automation.ProcessRecordingCompletedAsync(result.Item);
        }

        SetStatus(result.Message);
        RefreshRecordingSetupText();
    }
    public void PickColorCommand() => PickColor();
    public void OpenPixelRulerCommand() => OpenPixelRuler();
    public async void ImportClipboardCommand() => await ImportClipboardAsync();
    public void OpenSettingsCommand(string? sectionKey = null) => OpenSettings(sectionKey);
    public void ShowWorkspaceCommand()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitCommand()
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var startupOptions = AppStartupOptions.Parse(
            Environment.GetCommandLineArgs().Skip(1),
            Environment.GetEnvironmentVariable("GOATSHOT_OPEN_SETTINGS"),
            Environment.GetEnvironmentVariable("GOATSHOT_SETTINGS_SECTION"));
        if (startupOptions.OpenSettings)
        {
            OpenSettings(startupOptions.SettingsSection);
        }

        LoadCaptures();
        RefreshDiagnostics();
        _ = RefreshUploadQueueAsync();
        LoadRecordingControls();

        if (_auditMode)
        {
            return;
        }

        _services.Hotkeys.Attach(this);
        _services.Hotkeys.ActionTriggered += Hotkeys_ActionTriggered;
        HotkeyText.Text = string.Join(Environment.NewLine, _services.Hotkeys.RegistrationReport);
        EmptyStateHotkeyRun.Text = _services.Hotkeys.IsRegistered(HotkeyAction.AllInOneCapture) ? HotkeyService.AllInOneCaptureLabel : "the Screen button";

        _services.Automation.StatusChanged += Service_StatusChanged;
        _services.Automation.CaptureImported += Automation_CaptureImported;
        _services.WatchFolders.StatusChanged += Service_StatusChanged;
        _services.WatchFolders.Restart();
        _services.UploadQueueWorker.StatusChanged += Service_StatusChanged;
        _services.UploadQueueWorker.Start();
        _services.StepRecorder.StatusChanged += Service_StatusChanged;
        _services.StepRecorder.StepCaptured += StepRecorder_StepCaptured;

        _services.AttachTray(this);
        QueueSettingsPrewarm();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        StopWebcamPreview("Preview stopped.");
        if (_auditMode)
        {
            return;
        }

        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        SetStatus("GoatShot is still running in the tray.");
    }

    private void Hotkeys_ActionTriggered(object? sender, HotkeyAction action)
    {
        Dispatcher.Invoke(() =>
        {
            switch (action)
            {
                case HotkeyAction.AllInOneCapture:
                case HotkeyAction.RegionCapture:
                    CaptureRegionCommand(HotkeyProfileNames.ForAction(action));
                    break;
                case HotkeyAction.WindowCapture:
                    CaptureWindowCommand(HotkeyProfileNames.ForAction(action));
                    break;
                case HotkeyAction.LastRegionCapture:
                    CaptureLastRegionCommand(HotkeyProfileNames.ForAction(action));
                    break;
                case HotkeyAction.ToggleRecording:
                    ToggleRecordingCommand(HotkeyProfileNames.ForAction(action));
                    break;
                case HotkeyAction.OcrRegion:
                    OcrRegionCommand(HotkeyProfileNames.ForAction(action));
                    break;
                case HotkeyAction.ColorPicker:
                    PickColor();
                    break;
                case HotkeyAction.PixelRuler:
                    OpenPixelRuler();
                    break;
            }
        });
    }

    private void LoadCaptures()
    {
        _allCaptures = _services.WorkspaceStore.Load()
            .Where(item => File.Exists(item.FilePath))
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allCaptures
            : FilterCaptures(query);

        _captures.Clear();
        foreach (var capture in filtered)
        {
            _captures.Add(capture);
        }

        EmptyState.Visibility = _captures.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IReadOnlyList<CaptureItem> FilterCaptures(string query)
    {
        var byId = _allCaptures.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var indexed = _services.WorkspaceIndex.SearchIds(query)
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();

        if (indexed.Count > 0)
        {
            return indexed;
        }

        return _allCaptures.Where(item =>
                item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.HotkeyProfile?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.OcrText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private async Task<CaptureItem?> CaptureRegionAsync(string? hotkeyProfile = null)
    {
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(120);
        }

        try
        {
            return await CaptureAndStoreAsync(() => _services.Screenshots.CaptureRegionAsync(this), hotkeyProfile);
        }
        finally
        {
            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }

    private async Task<CaptureItem?> CaptureActiveWindowAsync(string? hotkeyProfile = null)
    {
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            SetStatus("Hiding GoatShot before active-window capture...");
            Hide();
            await Task.Delay(220);
        }

        try
        {
            return await CaptureAndStoreAsync(
                async () => await _services.Screenshots.CaptureActiveWindowAsync(),
                hotkeyProfile);
        }
        finally
        {
            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }

    private async Task<CaptureItem?> CaptureAndStoreAsync(
        Func<Task<CapturedBitmap?>> capture,
        string? hotkeyProfile = null)
    {
        SetStatus("Capturing...");
        using var captured = await capture();
        if (captured is null)
        {
            SetStatus("Capture canceled.");
            return null;
        }

        var item = await _services.WorkspaceStore.SaveCaptureAsync(
            captured,
            _services.Settings.PrivateCaptureMode,
            hotkeyProfile);
        if (!item.IsPrivate)
        {
            _allCaptures.Insert(0, item);
            ApplyFilter();
            CaptureList.SelectedItem = item;
        }

        if (_services.Settings.AutoCopyImageAfterCapture)
        {
            CopyImageToClipboard(item);
        }

        await _services.Automation.ProcessCaptureCreatedAsync(item);

        SetStatus(item.IsPrivate
            ? $"Private capture saved temporarily: {item.FilePath}"
            : $"Captured {item.Kind}: {item.FileName}");
        ShowCaptureTaskWindow(item);
        return item;
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e) => CaptureRegionCommand();
    private void ScreenAction_Click(object sender, RoutedEventArgs e) => RunSelectedScreenCapture();
    private void CaptureWindow_Click(object sender, RoutedEventArgs e) => CaptureWindowCommand();
    private async void CaptureScrollingWindow_Click(object sender, RoutedEventArgs e) => await CaptureScrollingWindowAsync();
    private async void CaptureHorizontalScrollingWindow_Click(object sender, RoutedEventArgs e) => await CaptureScrollingWindowAsync(ScrollingCaptureAxis.Horizontal);
    private void CaptureFullscreen_Click(object sender, RoutedEventArgs e) => CaptureFullscreenCommand();
    private void CaptureAllMonitors_Click(object sender, RoutedEventArgs e) => CaptureAllMonitorsCommand();
    private void CaptureMonitor_Click(object sender, RoutedEventArgs e) => CaptureMonitorCommand();
    private void CapturePreset1280x720_Click(object sender, RoutedEventArgs e) => CaptureFixedRegionCommand(1280, 720);
    private void CapturePreset1920x1080_Click(object sender, RoutedEventArgs e) => CaptureFixedRegionCommand(1920, 1080);
    private void CaptureLastRegion_Click(object sender, RoutedEventArgs e) => CaptureLastRegionCommand();

    private async void CaptureDelayed_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Delayed capture starts in 3 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(3));
        CaptureRegionCommand();
    }

    private async Task<CaptureItem?> CaptureScrollingWindowAsync(ScrollingCaptureAxis axis = ScrollingCaptureAxis.Vertical)
    {
        var options = ScrollingCaptureProfiles.For(axis == ScrollingCaptureAxis.Horizontal ? "table" : "browser");
        options.Axis = axis;
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
        }

        var axisText = axis == ScrollingCaptureAxis.Horizontal ? "horizontal scrolling" : "scrolling";
        SetStatus($"Focus the {axisText} target window. Capture starts in 2 seconds with the {options.Profile} profile. If the target does not move, capture overlapping frames and use image stitch with the same profile.");
        await Task.Delay(TimeSpan.FromSeconds(2));

        try
        {
            return await CaptureAndStoreAsync(async () => await _services.Screenshots.CaptureScrollingActiveWindowAsync(options));
        }
        finally
        {
            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }

    private void Recording_Click(object sender, RoutedEventArgs e) => ToggleRecordingCommand();

    private async void VideoAction_Click(object sender, RoutedEventArgs e) => await RunSelectedVideoCaptureAsync();

    private void PauseRecording_Click(object sender, RoutedEventArgs e) => ToggleRecordingPauseCommand();

    private async void RecordMp4_Click(object sender, RoutedEventArgs e) => await RecordShortMp4Async(RecordingCaptureTarget.ActiveMonitor());

    private async void RecordAllMonitorsMp4_Click(object sender, RoutedEventArgs e) => await RecordShortMp4Async(RecordingCaptureTarget.AllMonitors());

    private void RunSelectedScreenCapture()
    {
        switch (SelectedComboTag(ScreenCaptureTypeBox, "Region"))
        {
            case "ActiveWindow":
                CaptureWindowCommand();
                break;
            case "ScrollingWindow":
                _ = CaptureScrollingWindowAsync();
                break;
            case "HorizontalScrolling":
                _ = CaptureScrollingWindowAsync(ScrollingCaptureAxis.Horizontal);
                break;
            case "Fullscreen":
                CaptureFullscreenCommand();
                break;
            case "AllMonitors":
                CaptureAllMonitorsCommand();
                break;
            case "ActiveMonitor":
                CaptureMonitorCommand();
                break;
            case "LastRegion":
                CaptureLastRegionCommand();
                break;
            case "DelayedRegion":
                CaptureDelayed_Click(this, new RoutedEventArgs());
                break;
            case "Preset1280":
                CaptureFixedRegionCommand(1280, 720);
                break;
            case "Preset1920":
                CaptureFixedRegionCommand(1920, 1080);
                break;
            default:
                CaptureRegionCommand();
                break;
        }
    }

    private async Task RunSelectedVideoCaptureAsync()
    {
        if (_services.Recording.IsRecording)
        {
            await ToggleRecordingAsync(RecordingCaptureTarget.ActiveMonitor());
            return;
        }

        var mode = SelectedComboTag(VideoCaptureTypeBox, "GifMonitor");
        var target = await ResolveVideoTargetAsync(mode);
        if (target is null)
        {
            SetStatus("Video target selection canceled.");
            return;
        }

        if (mode.StartsWith("Mp4", StringComparison.OrdinalIgnoreCase))
        {
            await RecordShortMp4Async(target);
            return;
        }

        await ToggleRecordingAsync(target);
    }

    private async Task<RecordingCaptureTarget?> ResolveVideoTargetAsync(string mode)
    {
        return mode switch
        {
            "GifRegion" or "Mp4Region" => await SelectRecordingRegionTargetAsync(),
            "GifWindow" or "Mp4Window" => RecordingCaptureTarget.ActiveWindow(),
            "GifAllMonitors" or "Mp4AllMonitors" => RecordingCaptureTarget.AllMonitors(),
            _ => RecordingCaptureTarget.ActiveMonitor()
        };
    }

    private async Task<RecordingCaptureTarget?> SelectRecordingRegionTargetAsync()
    {
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(120);
        }

        try
        {
            var bounds = await _services.Screenshots.SelectRegionBoundsAsync(this);
            return bounds is null ? null : RecordingCaptureTarget.Region(bounds);
        }
        finally
        {
            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }

    private static string SelectedComboTag(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    }

    private void ApplyRecordingProfile_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingProfileBox.SelectedItem is not ComboBoxItem { Tag: RecordingWorkflowProfile profile })
        {
            SetStatus("Select a recording profile first.");
            return;
        }

        _services.Settings.Recording = CloneRecordingSettings(profile.Settings);
        LoadRecordingControls();
        _services.SaveSettings();
        SetStatus($"Recording profile applied: {profile.Name}");
    }

    private void RecordingControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!_recordingControlsReady)
        {
            return;
        }

        ReadRecordingSettingsFromControls();
        ValidateRecordingInputs();
        if (sender == RecordingWebcamBox && RecordingWebcamBox.IsChecked != true)
        {
            StopWebcamPreview("Preview stopped.");
        }
    }

    private void ToggleWebcamPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_webcamPreviewCts is not null)
        {
            StopWebcamPreview("Preview stopped.");
            return;
        }

        StartWebcamPreview();
    }

    private void StartWebcamPreview()
    {
        var settings = ReadRecordingSettingsFromControls();
        if (RecordingWebcamBox.IsChecked != true)
        {
            RecordingWebcamBox.IsChecked = true;
            settings.EnableWebcamOverlay = true;
            _services.SaveSettings();
        }

        var cts = new CancellationTokenSource();
        _webcamPreviewCts = cts;
        WebcamPreviewButton.Content = "Stop preview";
        WebcamPreviewStatusText.Text = "Starting camera preview...";
        WebcamPreviewImage.Source = null;
        SetStatus("Starting camera preview...");
        _webcamPreviewTask = RunWebcamPreviewAsync(cts, settings.WebcamDeviceId);
    }

    private void StopWebcamPreview(string? message = null)
    {
        var cts = _webcamPreviewCts;
        _webcamPreviewCts = null;
        if (cts is not null && !cts.IsCancellationRequested)
        {
            cts.Cancel();
        }

        if (WebcamPreviewButton is not null)
        {
            WebcamPreviewButton.Content = "Start preview";
        }

        if (WebcamPreviewImage is not null)
        {
            WebcamPreviewImage.Source = null;
        }

        if (!string.IsNullOrWhiteSpace(message) && WebcamPreviewStatusText is not null)
        {
            WebcamPreviewStatusText.Text = message;
            SetStatus(message);
        }
    }

    private async Task RunWebcamPreviewAsync(CancellationTokenSource owner, string deviceId)
    {
        var frameCount = 0;
        try
        {
            while (!owner.IsCancellationRequested)
            {
                using var result = await _services.CameraOverlay.CaptureFrameAsync(deviceId, owner.Token);
                if (result.Succeeded && result.Frame is not null)
                {
                    frameCount++;
                    var source = ImageInterop.ToBitmapSource(result.Frame);
                    var device = result.Device?.DisplayName ?? "selected camera";
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var status = $"Previewing {device}; frame {frameCount}.";
                        WebcamPreviewImage.Source = source;
                        WebcamPreviewStatusText.Text = status;
                        SetStatus(status);
                    });
                    await Task.Delay(TimeSpan.FromMilliseconds(750), owner.Token);
                    continue;
                }

                var message = string.IsNullOrWhiteSpace(result.Message)
                    ? "Camera preview unavailable."
                    : result.Message;
                await Dispatcher.InvokeAsync(() =>
                {
                    WebcamPreviewStatusText.Text = message;
                    WebcamPreviewImage.Source = null;
                    SetStatus(message);
                });
                await Task.Delay(TimeSpan.FromSeconds(2), owner.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop requested by the user, tray hide, or app shutdown.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var status = $"Camera preview failed: {ex.GetType().Name}: {ex.Message}";
                WebcamPreviewStatusText.Text = status;
                WebcamPreviewImage.Source = null;
                SetStatus(status);
            });
        }
        finally
        {
            var ownsCurrent = ReferenceEquals(_webcamPreviewCts, owner);
            if (ownsCurrent)
            {
                _webcamPreviewCts = null;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (ownsCurrent)
                {
                    WebcamPreviewButton.Content = "Start preview";
                }

                if (owner.IsCancellationRequested && ownsCurrent)
                {
                    WebcamPreviewStatusText.Text = "Preview stopped.";
                    WebcamPreviewImage.Source = null;
                }
            });
            owner.Dispose();
        }
    }

    private async Task RecordShortMp4Async(RecordingCaptureTarget target)
    {
        var recordingSettings = ReadRecordingSettingsFromControls();
        _services.SaveSettings();

        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
        }

        try
        {
            if (recordingSettings.ShowCountdown)
            {
                SetStatus($"MP4 recording for {target.DisplayName} starts in 1 second...");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            else
            {
                SetStatus($"MP4 recording for {target.DisplayName} started...");
            }

            var result = await _services.Recording.RecordMp4Async(
                TimeSpan.FromSeconds(5),
                null,
                recordingSettings,
                target,
                addToWorkspace: true);

            if (result.Item is not null)
            {
                _allCaptures.Insert(0, result.Item);
                ApplyFilter();
                CaptureList.SelectedItem = result.Item;
                await _services.Automation.ProcessRecordingCompletedAsync(result.Item);
            }

            SetStatus(result.Message);
            RefreshRecordingSetupText();
            if (!result.Succeeded)
            {
                System.Windows.MessageBox.Show(result.Message, "GoatShot MP4 recording", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }

    private void LoadRecordingControls()
    {
        _recordingControlsReady = false;
        var settings = _services.Settings.Recording ??= new RecordingSettings();
        PopulateRecordingProfiles();
        SelectComboBoxItem(RecordingQualityBox, RecordingSettingsNormalizer.NormalizeQualityProfile(settings.QualityProfile));
        RecordingFpsBox.Text = settings.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        RecordingSizeBox.Text = settings.TargetWidth > 0 || settings.TargetHeight > 0
            ? $"{(settings.TargetWidth > 0 ? settings.TargetWidth.ToString(CultureInfo.InvariantCulture) : "auto")}x{(settings.TargetHeight > 0 ? settings.TargetHeight.ToString(CultureInfo.InvariantCulture) : "auto")}"
            : string.Empty;
        RecordingBitrateBox.Text = settings.BitrateKbps > 0
            ? $"{settings.BitrateKbps.ToString(CultureInfo.InvariantCulture)}k"
            : string.Empty;
        RecordingMicBox.IsChecked = settings.IncludeMicrophone;
        RecordingSystemAudioBox.IsChecked = settings.IncludeSystemAudio;
        RecordingWebcamBox.IsChecked = settings.EnableWebcamOverlay;
        RecordingCountdownBox.IsChecked = settings.ShowCountdown;
        RecordingBorderBox.IsChecked = settings.ShowRecordingBorder;
        RecordingTimerBox.IsChecked = settings.ShowRecordingTimer;
        RecordingKeystrokeBox.IsChecked = settings.ShowKeystrokeOverlay;
        _recordingControlsReady = true;
        RefreshRecordingSetupText();
        ValidateRecordingInputs();
    }

    private RecordingSettings ReadRecordingSettingsFromControls()
    {
        var settings = _services.Settings.Recording ??= new RecordingSettings();
        settings.QualityProfile = (RecordingQualityBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Balanced";
        settings.FramesPerSecond = int.TryParse(RecordingFpsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fps)
            ? fps
            : settings.FramesPerSecond;
        var (width, height) = ParseRecordingSize(RecordingSizeBox.Text);
        settings.TargetWidth = width;
        settings.TargetHeight = height;
        settings.BitrateKbps = ParseRecordingBitrateKbps(RecordingBitrateBox.Text);
        settings.UseVariableBitrate = settings.BitrateKbps <= 0;
        settings.IncludeMicrophone = RecordingMicBox.IsChecked == true;
        settings.IncludeSystemAudio = RecordingSystemAudioBox.IsChecked == true;
        settings.EnableWebcamOverlay = RecordingWebcamBox.IsChecked == true;
        settings.ShowCountdown = RecordingCountdownBox.IsChecked != false;
        settings.ShowRecordingBorder = RecordingBorderBox.IsChecked == true;
        settings.ShowRecordingTimer = RecordingTimerBox.IsChecked == true;
        settings.ShowKeystrokeOverlay = RecordingKeystrokeBox.IsChecked == true;
        RefreshRecordingSetupText();
        return settings;
    }

    private void RefreshRecordingSetupText()
    {
        if (RecordingSetupText is null)
        {
            return;
        }

        var enginePlan = BuildCurrentRecordingEnginePlan(out var capabilities);
        var confidence = enginePlan is not null && capabilities is not null
            ? RecordingConfidenceService.BuildEngineReport(_services.Settings.Recording, capabilities, enginePlan)
            : null;
        var snapshot = RecordingPanelPresenter.Build(
            _services.Settings.Recording,
            enginePlan,
            _services.Recording.IsRecording,
            _services.Recording.IsPaused,
            _services.Settings.RecordingProfiles.Count(profile => !string.IsNullOrWhiteSpace(profile.Name)),
            confidence: confidence);
        RecordingSetupText.Text = snapshot.ToStatusText();
    }

    private void PopulateRecordingProfiles()
    {
        RecordingProfileBox.Items.Clear();
        foreach (var profile in _services.Settings.RecordingProfiles
                     .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                     .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            RecordingProfileBox.Items.Add(new ComboBoxItem
            {
                Content = profile.Name,
                Tag = profile
            });
        }

        RecordingProfileBox.SelectedIndex = RecordingProfileBox.Items.Count > 0 ? 0 : -1;
    }

    private RecordingEnginePlan? BuildCurrentRecordingEnginePlan(out RecordingCapabilitySnapshot? capabilities)
    {
        capabilities = null;
        try
        {
            capabilities = RecordingCapabilityProbe.Probe();
            return RecordingEnginePlanner.BuildPlan(
                _services.Settings.Recording,
                capabilities,
                productionEngineImplemented: NativeMediaFoundationMp4Encoder.ProductionEngineImplemented,
                productionFrameCaptureImplemented: true,
                productionAudioMixingImplemented: NativeMediaFoundationMp4Encoder.ProductionAudioMixingImplemented,
                productionWebcamCompositorImplemented: NativeMediaFoundationMp4Encoder.ProductionWebcamCompositorImplemented);
        }
        catch
        {
            capabilities = null;
            return null;
        }
    }

    private static void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if ((item.Content?.ToString() ?? string.Empty).Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    private static (int Width, int Height) ParseRecordingSize(string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        var parts = text.Split('x', 'X');
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        var width = ParseAutoDimension(parts[0]);
        var height = ParseAutoDimension(parts[1]);
        return (width, height);
    }

    private static int ParseAutoDimension(string text)
    {
        text = text.Trim();
        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;
    }

    private static int ParseRecordingBitrateKbps(string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var multiplier = 1d;
        if (text.EndsWith("mbps", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000d;
            text = text[..^4];
        }
        else if (text.EndsWith('m') || text.EndsWith('M'))
        {
            multiplier = 1_000d;
            text = text[..^1];
        }
        else if (text.EndsWith("kbps", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }
        else if (text.EndsWith('k') || text.EndsWith('K'))
        {
            text = text[..^1];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? (int)Math.Round(parsed * multiplier)
            : 0;
    }

    private static RecordingSettings CloneRecordingSettings(RecordingSettings source)
    {
        return new RecordingSettings
        {
            PreferProductionCaptureEngine = source.PreferProductionCaptureEngine,
            PreferHevcEncoding = source.PreferHevcEncoding,
            QualityProfile = source.QualityProfile,
            FramesPerSecond = source.FramesPerSecond,
            TargetWidth = source.TargetWidth,
            TargetHeight = source.TargetHeight,
            BitrateKbps = source.BitrateKbps,
            UseVariableBitrate = source.UseVariableBitrate,
            IncludeMicrophone = source.IncludeMicrophone,
            IncludeSystemAudio = source.IncludeSystemAudio,
            MicrophoneDeviceId = source.MicrophoneDeviceId,
            SystemAudioDeviceId = source.SystemAudioDeviceId,
            MicrophoneGain = source.MicrophoneGain,
            SystemAudioGain = source.SystemAudioGain,
            NoiseGateThresholdDb = source.NoiseGateThresholdDb,
            MicrophoneMuted = source.MicrophoneMuted,
            SystemAudioMuted = source.SystemAudioMuted,
            EnableWebcamOverlay = source.EnableWebcamOverlay,
            WebcamDeviceId = source.WebcamDeviceId,
            WebcamOverlayPosition = source.WebcamOverlayPosition,
            WebcamOverlayShape = source.WebcamOverlayShape,
            MirrorWebcam = source.MirrorWebcam,
            ShowCountdown = source.ShowCountdown,
            ShowRecordingBorder = source.ShowRecordingBorder,
            ShowRecordingTimer = source.ShowRecordingTimer,
            ShowKeystrokeOverlay = source.ShowKeystrokeOverlay,
            RecordingTimerPosition = source.RecordingTimerPosition,
            KeystrokeOverlayPosition = source.KeystrokeOverlayPosition,
            RecordingOverlayBadgeFontSize = source.RecordingOverlayBadgeFontSize,
            RecordingOverlayStyle = source.RecordingOverlayStyle
        };
    }

    private async void StepRecorder_Click(object sender, RoutedEventArgs e) => await ToggleStepRecorderAsync();

    private async Task ToggleStepRecorderAsync()
    {
        if (!_services.StepRecorder.IsRecording)
        {
            try
            {
                _services.StepRecorder.Start();
                SetStatus("Step recorder armed. Click through the target workflow, then press this button again to export.");
            }
            catch (Exception ex)
            {
                SetStatus($"Step recorder failed to start: {ex.Message}");
                System.Windows.MessageBox.Show(ex.Message, "GoatShot step recorder", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return;
        }

        SetStatus("Stopping step recorder and exporting guide...");
        var result = await _services.StepRecorder.StopAndExportAsync();
        if (!result.Succeeded)
        {
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot step recorder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var outputs = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.MarkdownPath))
        {
            outputs.Add(result.MarkdownPath);
        }

        if (!string.IsNullOrWhiteSpace(result.HtmlPath))
        {
            outputs.Add(result.HtmlPath);
        }

        if (outputs.Count > 0)
        {
            ClipboardInterop.SetFileDropList(outputs);
        }

        SetStatus($"{result.Message} Document files copied to clipboard.");
    }
    private async void RunOcr_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        await RecognizeAndStoreOcrAsync(item, copyText: true);
    }

    private async void RedactOcrSecrets_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.OcrText) || item.OcrWords.Count == 0 || item.OcrWords.Any(word => word.Length == 0))
        {
            var recognized = await RecognizeAndStoreOcrAsync(item, copyText: false);
            if (!recognized)
            {
                return;
            }
        }

        SetStatus("Creating flattened OCR redaction copy...");
        var result = await _services.VisualRedactions.RedactSensitiveOcrAsync(item);
        if (!result.Succeeded)
        {
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot OCR redaction", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (result.Item is not null)
        {
            _allCaptures.Insert(0, result.Item);
            ApplyFilter();
            CaptureList.SelectedItem = result.Item;
            await _services.Automation.ProcessCaptureEditedAsync(result.Item);
        }

        SetStatus(result.Message);
    }

    private async void OcrRegionCommand(string? hotkeyProfile = null)
    {
        var item = await CaptureRegionAsync(hotkeyProfile);
        if (item is not null)
        {
            await RecognizeAndStoreOcrAsync(item, copyText: true);
        }
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        OpenEditorForItem(item);
    }

    private void OpenEditorForItem(CaptureItem item)
    {
        var editor = new EditorWindow(item, _services);
        editor.CaptureSaved += async (_, saved) =>
        {
            _allCaptures.Insert(0, saved);
            ApplyFilter();
            CaptureList.SelectedItem = saved;
            SetStatus($"Edited copy saved: {saved.FileName}");
            await _services.Automation.ProcessCaptureEditedAsync(saved);
        };
        editor.Owner = this;
        editor.Show();
    }

    private void CaptureTask_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        ShowCaptureTaskWindow(item);
    }

    private async void ImportClipboard_Click(object sender, RoutedEventArgs e)
    {
        await ImportClipboardAsync();
    }

    private async void CombineSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = CaptureList.SelectedItems
            .OfType<CaptureItem>()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedImages = _captures
            .Where(item => selectedIds.Contains(item.Id) && IsImageFile(item.FilePath))
            .ToList();

        if (selectedImages.Count < 2)
        {
            SetStatus("Select at least two image captures to combine.");
            return;
        }

        SetStatus("Combining selected image captures...");
        var result = await _services.ImageLayouts.CombineAsync(
            selectedImages.Select(item => item.FilePath).ToList(),
            orientation: "vertical",
            addToWorkspace: true);

        HandleImageLayoutResult(result);
    }

    private void PinCapture_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        var pinned = new PinnedCaptureWindow(item)
        {
            Owner = this
        };
        pinned.Show();
        SetStatus($"Pinned capture: {item.FileName}");
    }

    private async void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelectedAsync(ShareDestination.ClipboardImage);
    }

    private async void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelectedAsync(ShareDestination.ClipboardFile);
    }

    private async void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelectedAsync(ShareDestination.ClipboardPath);
    }

    private async void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelectedAsync(ShareDestination.MarkdownImageLink);
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
    }

    private async void QuickShare_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelectedAsync(_services.Sharing.ResolveDefaultDestination());
    }

    private void ShareHistory_Click(object sender, RoutedEventArgs e)
    {
        // Modeless so the operator can review share history while continuing to work in
        // the main window (e.g. copy a URL and reuse it elsewhere).
        if (_shareHistoryWindow is not null)
        {
            _shareHistoryWindow.Activate();
            return;
        }

        var window = new ShareHistoryWindow(_services)
        {
            Owner = this
        };
        _shareHistoryWindow = window;
        window.Closed += (_, _) =>
        {
            _shareHistoryWindow = null;
            if (window.QueueChanged)
            {
                _ = RefreshUploadQueueAsync();
                RefreshDiagnostics();
            }

            SetStatus("Share history closed.");
        };
        window.Show();
        SetStatus("Share history opened.");
    }

    private async void ExportBugReport_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        await ExportBugReportForItemAsync(item);
    }

    private async Task ExportBugReportForItemAsync(CaptureItem item)
    {
        SetStatus("Exporting bug report...");
        try
        {
            var result = await _services.BugReports.ExportAsync(item);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Path))
            {
                SetStatus(result.Message);
                System.Windows.MessageBox.Show(result.Message, "GoatShot bug report", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClipboardInterop.SetFileDropList([result.Path]);
            SetStatus($"Bug report copied to clipboard: {result.Path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Bug report export failed: {ex.Message}");
            System.Windows.MessageBox.Show(ex.Message, "GoatShot bug report", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AiExplain_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        await AiExplainItemAsync(item);
    }

    private async Task AiExplainItemAsync(CaptureItem item)
    {
        if (!_services.Settings.AiEnabled)
        {
            SetStatus("AI is disabled. Enable Gemini in Settings first.");
            return;
        }

        if (!IsImageFile(item.FilePath))
        {
            SetStatus("AI explain currently supports image captures only.");
            return;
        }

        if (IsAiDenied(item))
        {
            SetStatus($"AI blocked by denylist for source app: {item.SourceApp}");
            return;
        }

        if (_services.Settings.ConfirmBeforeAiRequest)
        {
            var confirm = System.Windows.MessageBox.Show(
                "This sends the selected image to Gemini for explanation. Continue?",
                "Confirm Gemini analysis",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                SetStatus("Gemini analysis canceled.");
                return;
            }
        }

        const string prompt = "Explain this screenshot for a bug report or support note. Summarize the visible UI, likely user action, important text, and any visible risk or sensitive data. Do not invent hidden context.";
        SetStatus("Sending screenshot to Gemini for explanation...");
        var model = _services.Settings.GeminiDefaultModelId;
        var result = await _services.Gemini.AnalyzeImageAsync(
            item.FilePath,
            prompt,
            model,
            CancellationToken.None);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
        {
            await _services.AiHistory.RecordAsync(
                AiActionKind.ImageAnalysis,
                item,
                model,
                prompt,
                succeeded: false,
                result.Message);
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot AI explain", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var redacted = SensitiveTextDetector.Scan(result.Text).RedactedText.Trim();
        await _services.AiHistory.RecordAsync(
            AiActionKind.ImageAnalysis,
            item,
            model,
            prompt,
            succeeded: true,
            result.Message,
            textOutput: redacted);
        item.Notes = MergePrefixedNote(
            item.Notes,
            AiAnalysisNotePrefix,
            $"{AiAnalysisNotePrefix} {DateTimeOffset.Now.LocalDateTime:g}{Environment.NewLine}{redacted}");
        if (!item.IsPrivate)
        {
            await _services.WorkspaceStore.UpdateItemAsync(item);
        }

        ClipboardInterop.SetText(redacted);
        ApplyFilter();
        CaptureList.SelectedItem = item;
        UpdateCaptureDetails(item);
        SetStatus("Gemini explanation saved to notes and copied to clipboard.");
        await _services.Automation.ProcessAiActionCompletedAsync(item);
    }

    private void AiHistory_Click(object sender, RoutedEventArgs e)
    {
        // Modeless: AI history is a review-while-working surface (copy a prompt to reuse).
        if (_aiHistoryWindow is not null)
        {
            _aiHistoryWindow.Activate();
            return;
        }

        var window = new AiHistoryWindow(_services)
        {
            Owner = this
        };
        _aiHistoryWindow = window;
        window.Closed += (_, _) =>
        {
            _aiHistoryWindow = null;
            SetStatus("AI review history closed.");
        };
        window.Show();
        SetStatus("AI review history opened.");
    }

    // ---- Command palette (Ctrl+K) --------------------------------------------------
    // Most tools live inside collapsed expanders; the palette makes every action
    // reachable by name from the keyboard without hunting through the rail.
    public static readonly System.Windows.Input.RoutedUICommand OpenCommandPaletteCommand =
        new("Open command palette", "OpenCommandPalette", typeof(MainWindow));

    private sealed record CommandPaletteEntry(string Name, string Group, Action Invoke)
    {
        public string Display => $"{Name}  ·  {Group}";
    }

    private List<CommandPaletteEntry>? _commandPaletteEntries;

    private List<CommandPaletteEntry> CommandPaletteEntries => _commandPaletteEntries ??= BuildCommandPaletteEntries();

    private List<CommandPaletteEntry> BuildCommandPaletteEntries()
    {
        var empty = new RoutedEventArgs();
        CommandPaletteEntry E(string name, string group, Action invoke) => new(name, group, invoke);
        return new List<CommandPaletteEntry>
        {
            E("Capture region", "Capture", () => CaptureRegion_Click(this, empty)),
            E("Capture active window", "Capture", () => CaptureWindow_Click(this, empty)),
            E("Capture scrolling window", "Capture", () => CaptureScrollingWindow_Click(this, empty)),
            E("Capture horizontal scrolling window", "Capture", () => CaptureHorizontalScrollingWindow_Click(this, empty)),
            E("Capture fullscreen", "Capture", () => CaptureFullscreen_Click(this, empty)),
            E("Capture all monitors", "Capture", () => CaptureAllMonitors_Click(this, empty)),
            E("Capture active monitor", "Capture", () => CaptureMonitor_Click(this, empty)),
            E("Capture last region", "Capture", () => CaptureLastRegion_Click(this, empty)),
            E("Capture delayed region", "Capture", () => CaptureDelayed_Click(this, empty)),
            E("Import from clipboard", "Capture", () => ImportClipboard_Click(this, empty)),
            E("Start / stop GIF recording", "Record", () => Recording_Click(this, empty)),
            E("Pause / resume recording", "Record", () => PauseRecording_Click(this, empty)),
            E("Record quick MP4 (5s)", "Record", () => RecordMp4_Click(this, empty)),
            E("Record all monitors MP4 (5s)", "Record", () => RecordAllMonitorsMp4_Click(this, empty)),
            E("Start / stop step recorder", "Record", () => StepRecorder_Click(this, empty)),
            E("Open editor", "Workspace", () => OpenEditor_Click(this, empty)),
            E("Quick actions", "Workspace", () => CaptureTask_Click(this, empty)),
            E("Combine selected captures", "Workspace", () => CombineSelected_Click(this, empty)),
            E("Pin capture to screen", "Workspace", () => PinCapture_Click(this, empty)),
            E("Copy image", "Clipboard", () => CopyImage_Click(this, empty)),
            E("Copy file", "Clipboard", () => CopyFile_Click(this, empty)),
            E("Copy path", "Clipboard", () => CopyPath_Click(this, empty)),
            E("Copy markdown", "Clipboard", () => CopyMarkdown_Click(this, empty)),
            E("Show in Explorer", "Workspace", () => ShowInExplorer_Click(this, empty)),
            E("Quick share", "Share", () => QuickShare_Click(this, empty)),
            E("Share history", "Share", () => ShareHistory_Click(this, empty)),
            E("Export bug report", "Share", () => ExportBugReport_Click(this, empty)),
            E("Explain with AI", "AI", () => AiExplain_Click(this, empty)),
            E("AI history", "AI", () => AiHistory_Click(this, empty)),
            E("Export video frame", "Video", () => ExportVideoFrame_Click(this, empty)),
            E("Trim first 5s", "Video", () => TrimVideo_Click(this, empty)),
            E("Mute video", "Video", () => MuteVideo_Click(this, empty)),
            E("Set volume 150%", "Video", () => VolumeVideo_Click(this, empty)),
            E("Speed 2x", "Video", () => SpeedVideo_Click(this, empty)),
            E("Crop center 75%", "Video", () => CropVideo_Click(this, empty)),
            E("Resize to 720p", "Video", () => ResizeVideo_Click(this, empty)),
            E("Cut 2s from middle", "Video", () => CutVideo_Click(this, empty)),
            E("Convert to smooth GIF 60", "Video", () => ConvertVideo_Click(this, empty)),
            E("Strip metadata", "Privacy", () => StripMetadata_Click(this, empty)),
            E("File details", "Workspace", () => FileDetails_Click(this, empty)),
            E("Run OCR", "Analyze", () => RunOcr_Click(this, empty)),
            E("Redact OCR secrets", "Privacy", () => RedactOcrSecrets_Click(this, empty)),
            E("Scan for sensitive text", "Analyze", () => ScanText_Click(this, empty)),
            E("Decode QR / barcode", "Analyze", () => DecodeBarcode_Click(this, empty)),
            E("Pick color", "Analyze", () => ColorPicker_Click(this, empty)),
            E("Pixel ruler", "Analyze", () => PixelRuler_Click(this, empty)),
            E("Open settings", "App", () => Settings_Click(this, empty)),
            E("Open diagnostics", "App", () => Diagnostics_Click(this, empty)),
            E("Copy diagnostic bundle", "App", () => DiagnosticBundle_Click(this, empty))
        };
    }

    private void OpenCommandPalette_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        ShowCommandPalette();
    }

    private void ShowCommandPalette()
    {
        if (CommandPaletteOverlay is null)
        {
            return;
        }

        FilterCommandPalette(string.Empty);
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteSearch.Text = string.Empty;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            CommandPaletteSearch.Focus();
            System.Windows.Input.Keyboard.Focus(CommandPaletteSearch);
        }));
    }

    private void HideCommandPalette()
    {
        if (CommandPaletteOverlay is null)
        {
            return;
        }

        CommandPaletteOverlay.Visibility = Visibility.Collapsed;
    }

    private void FilterCommandPalette(string query)
    {
        query = query?.Trim() ?? string.Empty;
        IEnumerable<CommandPaletteEntry> matches = CommandPaletteEntries;
        if (query.Length > 0)
        {
            matches = matches.Where(entry =>
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        CommandPaletteList.ItemsSource = matches.ToList();
        if (CommandPaletteList.Items.Count > 0)
        {
            CommandPaletteList.SelectedIndex = 0;
        }
    }

    private void CommandPaletteOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Click on the dimmed backdrop (not the panel) closes the palette.
        if (ReferenceEquals(e.OriginalSource, CommandPaletteOverlay))
        {
            HideCommandPalette();
        }
    }

    private void CommandPaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCommandPalette(CommandPaletteSearch.Text);
    }

    private void CommandPaletteSearch_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Escape:
                HideCommandPalette();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter:
                ExecuteCommandPaletteSelection();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Down:
                if (CommandPaletteList.Items.Count > 0)
                {
                    CommandPaletteList.SelectedIndex = Math.Min(
                        CommandPaletteList.SelectedIndex + 1,
                        CommandPaletteList.Items.Count - 1);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }

                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up:
                if (CommandPaletteList.Items.Count > 0)
                {
                    CommandPaletteList.SelectedIndex = Math.Max(CommandPaletteList.SelectedIndex - 1, 0);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }

                e.Handled = true;
                break;
        }
    }

    private void CommandPaletteList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ExecuteCommandPaletteSelection();
    }

    private void ExecuteCommandPaletteSelection()
    {
        var entry = CommandPaletteList.SelectedItem as CommandPaletteEntry
            ?? CommandPaletteList.Items.OfType<CommandPaletteEntry>().FirstOrDefault();
        if (entry is null)
        {
            return;
        }

        HideCommandPalette();
        try
        {
            entry.Invoke();
        }
        catch (Exception ex)
        {
            SetStatus($"Command failed: {ex.Message}");
        }
    }

    // ---- Recording input validation ------------------------------------------------
    private void ValidateRecordingInputs()
    {
        if (RecordingValidationText is null)
        {
            return;
        }

        var hints = new List<string>();
        var fpsValid = IsFpsInputValid(RecordingFpsBox.Text);
        var sizeValid = IsSizeInputValid(RecordingSizeBox.Text);
        var bitrateValid = IsBitrateInputValid(RecordingBitrateBox.Text);

        SetFieldInvalid(RecordingFpsBox, !fpsValid);
        SetFieldInvalid(RecordingSizeBox, !sizeValid);
        SetFieldInvalid(RecordingBitrateBox, !bitrateValid);

        if (!fpsValid)
        {
            hints.Add("FPS must be a whole number like 30.");
        }

        if (!sizeValid)
        {
            hints.Add("Size must be WIDTHxHEIGHT like 1280x720, 'auto', or blank.");
        }

        if (!bitrateValid)
        {
            hints.Add("Bitrate must look like 6000k or 8M, or be blank.");
        }

        if (hints.Count == 0)
        {
            RecordingValidationText.Visibility = Visibility.Collapsed;
            RecordingValidationText.Text = string.Empty;
        }
        else
        {
            RecordingValidationText.Text = string.Join(" ", hints);
            RecordingValidationText.Visibility = Visibility.Visible;
        }
    }

    private void SetFieldInvalid(System.Windows.Controls.TextBox box, bool invalid)
    {
        box.BorderBrush = (System.Windows.Media.Brush)FindResource(invalid ? "WarnBrush" : "BorderBrush");
        box.BorderThickness = new Thickness(invalid ? 1.5 : 1);
    }

    private static bool IsFpsInputValid(string text)
    {
        text = text.Trim();
        return !string.IsNullOrEmpty(text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value > 0;
    }

    private static bool IsSizeInputValid(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var parts = text.Split('x', 'X');
        return parts.Length == 2 && IsDimensionToken(parts[0]) && IsDimensionToken(parts[1]);
    }

    private static bool IsDimensionToken(string text)
    {
        text = text.Trim();
        return text.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0);
    }

    private static bool IsBitrateInputValid(string text)
    {
        return string.IsNullOrWhiteSpace(text) || ParseRecordingBitrateKbps(text) > 0;
    }

    private async void ExportVideoFrame_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting video frame at 1 second...");
        var result = await _services.VideoTools.ExportFrameAsync(
            item,
            TimeSpan.FromSeconds(1),
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void TrimVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Trimming first 5 seconds...");
        var result = await _services.VideoTools.TrimAsync(
            item,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5),
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void MuteVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting muted video copy...");
        var result = await _services.VideoTools.MuteAsync(
            item,
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void VolumeVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting 150% volume video copy...");
        var result = await _services.VideoTools.ChangeVolumeAsync(
            item,
            1.5d,
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void SpeedVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting 2x speed video copy...");
        var result = await _services.VideoTools.ChangeSpeedAsync(
            item,
            2d,
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void CropVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting centered cropped video copy...");
        var result = await _services.VideoTools.CropCenterAsync(
            item,
            0.75d,
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void ResizeVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting resized 720p video copy...");
        var result = await _services.VideoTools.ResizeAsync(
            item,
            1280,
            720,
            "balanced",
            crf: null,
            bitrate: null,
            fps: null,
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void CutVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Exporting middle-cut video copy...");
        var result = await _services.VideoTools.CutMiddleAsync(
            item,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private async void ConvertVideo_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Converting selected video to smooth 60 fps GIF...");
        var result = await _services.VideoTools.ConvertAsync(
            item,
            "gif",
            animationOptions: new AnimationExportOptions
            {
                FrameRate = 60,
                GifTimingMode = "Smooth",
                Quality = "High"
            },
            addToWorkspace: true);

        HandleVideoToolResult(result);
    }

    private void HandleVideoToolResult(VideoToolResult result)
    {
        if (!result.Succeeded)
        {
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot video tools", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (result.Item is not null)
        {
            _allCaptures.Insert(0, result.Item);
            ApplyFilter();
            CaptureList.SelectedItem = result.Item;
        }

        if (!string.IsNullOrWhiteSpace(result.OutputPath))
        {
            ClipboardInterop.SetFileDropList([result.OutputPath]);
        }

        SetStatus($"{result.Message} Output copied to clipboard.");
    }

    private void HandleImageLayoutResult(ImageLayoutResult result)
    {
        if (!result.Succeeded)
        {
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot image tools", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var item in result.Items.AsEnumerable().Reverse())
        {
            _allCaptures.RemoveAll(existing => existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            _allCaptures.Insert(0, item);
        }

        ApplyFilter();
        if (result.Items.Count > 0)
        {
            CaptureList.SelectedItem = result.Items[0];
        }

        if (result.OutputPaths.Count > 0)
        {
            ClipboardInterop.SetFileDropList(result.OutputPaths);
        }

        SetStatus($"{result.Message} Output copied to clipboard.");
    }

    private async void StripMetadata_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Stripping image metadata...");
        var saved = await _services.Metadata.StripImageMetadataAsync(item, _services.WorkspaceStore);
        _allCaptures.Insert(0, saved);
        ApplyFilter();
        CaptureList.SelectedItem = saved;
        SetStatus($"Metadata-stripped copy saved: {saved.FileName}");
    }

    private async void FileDetails_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        try
        {
            SetStatus("Inspecting file metadata and hashes...");
            var result = await _services.FileInspector.InspectAsync(item.FilePath);
            var report = FileInspectorService.FormatReport(result);
            ClipboardInterop.SetText(report);
            SetStatus($"File details copied: {item.FileName}");
            System.Windows.MessageBox.Show(report, "GoatShot file details", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"File inspection failed: {ex.Message}");
            System.Windows.MessageBox.Show(ex.Message, "GoatShot file details", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ImportClipboardAsync()
    {
        SetStatus("Importing from Windows clipboard...");
        try
        {
            var result = await _services.ClipboardImports.ImportAsync();
            if (!result.Succeeded || result.Items.Count == 0)
            {
                SetStatus(result.Message);
                System.Windows.MessageBox.Show(result.Message, "GoatShot clipboard import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var item in result.Items.AsEnumerable().Reverse())
            {
                _allCaptures.RemoveAll(existing => existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
                _allCaptures.Insert(0, item);
            }

            ApplyFilter();
            CaptureList.SelectedItem = result.Items[0];
            SetStatus(result.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"Clipboard import failed: {ex.Message}");
            System.Windows.MessageBox.Show(ex.Message, "GoatShot clipboard import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ScanText_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        var dialog = new InputDialog(
            "Paste OCR/plain text from this capture. GoatShot stores it locally and copies a redacted version when sensitive data is found.",
            item.OcrText ?? string.Empty)
        {
            Owner = this,
            Width = 560,
            Height = 360,
            ResizeMode = ResizeMode.CanResize
        };

        if (dialog.ShowDialog() != true)
        {
            SetStatus("Text scan canceled.");
            return;
        }

        item.OcrText = dialog.ResponseText;
        var scan = SensitiveTextDetector.Scan(item.OcrText);
        item.Notes = MergeSensitiveScanNote(item.Notes, $"{SensitiveScanNotePrefix} {scan.Summary}");
        await _services.WorkspaceStore.UpdateItemAsync(item);

        ApplyFilter();
        CaptureList.SelectedItem = item;
        UpdateCaptureDetails(item);

        if (scan.Findings.Count > 0)
        {
            ClipboardInterop.SetText(scan.RedactedText);
            SetStatus($"{scan.Summary} Redacted text copied to clipboard.");
            return;
        }

        SetStatus("No sensitive data detected in stored text.");
    }

    private async Task<bool> RecognizeAndStoreOcrAsync(CaptureItem item, bool copyText)
    {
        SetStatus("Running local Windows OCR...");
        var result = await _services.Ocr.RecognizeFileAsync(item.FilePath);
        if (!result.Succeeded)
        {
            SetStatus(result.Message);
            System.Windows.MessageBox.Show(result.Message, "GoatShot OCR", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        item.OcrText = result.Text;
        item.OcrLanguageTag = result.LanguageTag;
        item.OcrRecognizedAt = DateTimeOffset.Now;
        item.OcrWords = result.Words.ToList();

        var scan = SensitiveTextDetector.Scan(item.OcrText);
        item.Notes = MergeSensitiveScanNote(item.Notes, $"{SensitiveScanNotePrefix} {scan.Summary}");
        if (!item.IsPrivate)
        {
            await _services.WorkspaceStore.UpdateItemAsync(item);
        }

        ApplyFilter();
        CaptureList.SelectedItem = item;
        UpdateCaptureDetails(item);
        await _services.Automation.ProcessOcrCompletedAsync(item);

        if (!copyText || string.IsNullOrWhiteSpace(result.Text))
        {
            SetStatus(result.Message);
            return true;
        }

        if (scan.Findings.Count > 0)
        {
            ClipboardInterop.SetText(scan.RedactedText);
            SetStatus($"{result.Message} {scan.Summary} Redacted OCR text copied to clipboard.");
            return true;
        }

        ClipboardInterop.SetText(result.Text);
        SetStatus($"{result.Message} OCR text copied to clipboard.");
        return true;
    }

    private async void DecodeBarcode_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        SetStatus("Decoding QR/barcode values...");
        try
        {
            var result = await _services.Barcodes.DecodeFileAsync(item.FilePath);
            if (!result.Succeeded)
            {
                SetStatus(result.Message);
                return;
            }

            var first = result.Items[0];
            ClipboardInterop.SetText(first.Text);
            var message = string.Join(
                $"{Environment.NewLine}{Environment.NewLine}",
                result.Items.Select((decoded, index) =>
                    $"{index + 1}. {decoded.Format}{Environment.NewLine}{decoded.Text}"));

            if (first.IsHttpUrl)
            {
                var open = System.Windows.MessageBox.Show(
                    $"{message}{Environment.NewLine}{Environment.NewLine}The first value was copied to the clipboard. Open it in your default browser?",
                    "Decoded QR/barcode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (open == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(first.Text) { UseShellExecute = true });
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"{message}{Environment.NewLine}{Environment.NewLine}The first value was copied to the clipboard.",
                    "Decoded QR/barcode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            SetStatus($"{result.Message} First value copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus($"QR/barcode decode failed: {ex.Message}");
        }
    }

    private void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        PickColor();
    }

    private void PixelRuler_Click(object sender, RoutedEventArgs e)
    {
        OpenPixelRuler();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiagnostics();
        System.Windows.MessageBox.Show(DiagnosticsText.Text, "GoatShot diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void DiagnosticBundle_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Creating diagnostic bundle...");
        try
        {
            var result = await _services.DiagnosticBundles.CreateAsync();
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Path))
            {
                SetStatus(result.Message);
                System.Windows.MessageBox.Show(result.Message, "GoatShot diagnostic bundle", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClipboardInterop.SetFileDropList([result.Path]);
            SetStatus($"Diagnostic bundle copied to clipboard: {result.Path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Diagnostic bundle failed: {ex.Message}");
            System.Windows.MessageBox.Show(ex.Message, "GoatShot diagnostic bundle", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshQueue_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUploadQueueAsync();
        SetStatus("Upload queue refreshed.");
    }

    private async void ProcessQueue_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Processing due upload queue items...");
        var result = await _services.UploadQueue.ProcessDueAsync(
            _services.Sharing,
            Math.Max(1, _services.Settings.UploadQueue.MaxConcurrentUploads),
            CancellationToken.None);
        await NotifyUploadQueueCompletionsAsync(result);
        await RefreshUploadQueueAsync();
        RefreshDiagnostics();
        SetStatus(result.Message);
    }

    private async void CancelQueueItem_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItem();
        if (item is null)
        {
            return;
        }

        var updated = await _services.UploadQueue.CancelAsync(item.Id, CancellationToken.None);
        await RefreshUploadQueueAsync();
        RefreshDiagnostics();
        SetStatus(updated is null ? $"Queue item not found: {item.Id}" : $"{updated.FileName}: {updated.LastMessage}");
    }

    private async void RetryQueueItem_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItem();
        if (item is null)
        {
            return;
        }

        var updated = await _services.UploadQueue.RetryAsync(item.Id, CancellationToken.None);
        await RefreshUploadQueueAsync();
        RefreshDiagnostics();
        SetStatus(updated is null ? $"Queue item not found: {item.Id}" : $"{updated.FileName}: {updated.LastMessage}");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async Task RefreshUploadQueueAsync()
    {
        var items = await _services.UploadQueue.ListAsync(CancellationToken.None);
        _uploadQueueItems.Clear();
        foreach (var item in items.Take(25))
        {
            _uploadQueueItems.Add(item);
        }

        QueueStatusText.Text = $"{_services.UploadQueue.GetStatusSummary()} Showing {_uploadQueueItems.Count} most recent item(s).";
    }

    private void CaptureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            PreviewImage.Source = null;
            DetailsText.Text = "No capture selected.";
            PreviewHint.Text = "Select a capture";
            return;
        }

        UpdateCaptureDetails(item);
    }

    private void UpdateCaptureDetails(CaptureItem item)
    {
        PreviewImage.Source = LoadPreviewImage(item);
        PreviewHint.Text = item.FileName;
        DetailsText.Text =
            $"Kind: {item.Kind}{Environment.NewLine}" +
            $"Created: {item.CreatedAt.LocalDateTime:g}{Environment.NewLine}" +
            $"Size: {item.SizeLabel}{Environment.NewLine}" +
            $"Bytes: {item.BytesLabel}{Environment.NewLine}" +
            $"Bounds: {item.Bounds?.Display ?? "n/a"}{Environment.NewLine}" +
            $"Storage: {(item.IsPrivate ? "temporary private file" : "workspace index")}{Environment.NewLine}" +
            $"Source app: {OcrValue(item.SourceApp)}{Environment.NewLine}" +
            $"Source window: {OcrSummary(item.SourceWindowTitle)}{Environment.NewLine}" +
            $"Monitor: {OcrValue(item.SourceMonitorName)}{Environment.NewLine}" +
            $"Path: {item.FilePath}{Environment.NewLine}{Environment.NewLine}" +
            $"OCR text: {OcrSummary(item.OcrText)}{Environment.NewLine}" +
            $"OCR language: {OcrValue(item.OcrLanguageTag)}{Environment.NewLine}" +
            $"OCR words: {item.OcrWords.Count}{Environment.NewLine}" +
            $"OCR recognized: {(item.OcrRecognizedAt?.LocalDateTime.ToString("g") ?? "n/a")}{Environment.NewLine}{Environment.NewLine}" +
            $"{item.Notes}";
    }

    private CaptureItem? SelectedCapture()
    {
        if (CaptureList.SelectedItem is CaptureItem item)
        {
            return item;
        }

        SetStatus("Select a capture first.");
        return null;
    }

    private UploadQueueItem? SelectedQueueItem()
    {
        if (QueueList.SelectedItem is UploadQueueItem item)
        {
            return item;
        }

        SetStatus("Select an upload queue item first.");
        return null;
    }

    private async Task NotifyUploadQueueCompletionsAsync(UploadQueueProcessResult result)
    {
        if (result.Succeeded == 0)
        {
            return;
        }

        var byId = _services.WorkspaceStore.Load()
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var queued in result.Items.Where(item => item.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(queued.CaptureItemId) &&
                byId.TryGetValue(queued.CaptureItemId, out var capture))
            {
                await _services.Automation.ProcessUploadCompletedAsync(capture);
            }
        }
    }

    private void CopyImageToClipboard(CaptureItem item)
    {
        ClipboardInterop.SetImage(ImageInterop.LoadBitmapImage(item.FilePath));
    }

    private async Task ShareSelectedAsync(ShareDestination destination)
    {
        var item = SelectedCapture();
        if (item is null)
        {
            return;
        }

        await ShareItemAsync(item, destination);
    }

    private async Task ShareItemAsync(CaptureItem item, ShareDestination destination)
    {
        if (ShareTaskWindowModels.ShouldConfirm(_services.Settings, destination))
        {
            var confirmWindow = new UploadConfirmationWindow(
                ShareTaskWindowModels.BuildConfirmation(item, destination))
            {
                Owner = this
            };

            if (confirmWindow.ShowDialog() != true)
            {
                SetStatus("Share canceled.");
                return;
            }
        }

        var retry = true;
        while (retry)
        {
            retry = false;
            SetStatus($"Sharing via {destination}...");
            var result = await _services.Sharing.ShareAsync(item, destination);
            SetStatus(result.Message);
            if (result.Succeeded)
            {
                await _services.Automation.ProcessUploadCompletedAsync(item);
            }

            if (ShouldShowShareResultWindow(destination, result))
            {
                var qrPath = await GenerateShareQrCodeAsync(item, destination, result);
                var resultWindow = new UploadResultWindow(
                    ShareTaskWindowModels.BuildResult(item, destination, result, qrPath))
                {
                    Owner = this
                };
                retry = resultWindow.ShowDialog() == true;
            }
        }
    }

    private void ShowCaptureTaskWindow(CaptureItem item)
    {
        if (_auditMode)
        {
            return;
        }

        var window = new CaptureTaskWindow(CaptureTaskWindowModels.Build(item, _services.Settings))
        {
            Owner = this
        };
        window.ActionRequested += async (_, action) => await HandleCaptureTaskActionAsync(item, action);
        window.Show();
        SetStatus($"Capture actions available: {item.FileName}");
    }

    private async Task HandleCaptureTaskActionAsync(CaptureItem item, CaptureTaskActionKind action)
    {
        CaptureList.SelectedItem = item;
        switch (action)
        {
            case CaptureTaskActionKind.OpenFile:
                Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
                SetStatus($"Opened capture: {item.FileName}");
                break;
            case CaptureTaskActionKind.OpenEditor:
                OpenEditorForItem(item);
                break;
            case CaptureTaskActionKind.Copy:
                if (IsImageFile(item.FilePath))
                {
                    CopyImageToClipboard(item);
                    SetStatus($"Image copied: {item.FileName}");
                }
                else
                {
                    ClipboardInterop.SetFileDropList([item.FilePath]);
                    SetStatus($"File copied: {item.FileName}");
                }

                break;
            case CaptureTaskActionKind.Share:
                await ShareItemAsync(item, _services.Sharing.ResolveDefaultDestination());
                break;
            case CaptureTaskActionKind.AiExplain:
                await AiExplainItemAsync(item);
                break;
            case CaptureTaskActionKind.ExportDocument:
                await ExportBugReportForItemAsync(item);
                break;
            case CaptureTaskActionKind.DryRunScript:
                ShowWorkflowDryRunPlan(_services.WorkflowDryRuns.PlanCustomScript(item), "Custom script dry run");
                break;
            case CaptureTaskActionKind.DryRunWebhook:
                ShowWorkflowDryRunPlan(_services.WorkflowDryRuns.PlanCustomWebhook(item), "Custom webhook dry run");
                break;
            case CaptureTaskActionKind.DeleteLocalFile:
                await DeleteLocalCaptureWithConfirmationAsync(item);
                break;
        }
    }

    private void ShowWorkflowDryRunPlan(WorkflowActionDryRunPlan plan, string title)
    {
        var details = $"{plan.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Target: {plan.Target}{Environment.NewLine}" +
            $"Would execute: {plan.WouldExecute}{Environment.NewLine}" +
            $"Payload: {plan.PayloadSummary}{Environment.NewLine}{Environment.NewLine}" +
            $"{plan.ResolvedCommand}";
        SetStatus(plan.Message);
        System.Windows.MessageBox.Show(details, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task DeleteLocalCaptureWithConfirmationAsync(CaptureItem item)
    {
        var confirm = System.Windows.MessageBox.Show(
            $"Delete this local capture from disk and the workspace?{Environment.NewLine}{Environment.NewLine}{item.FilePath}",
            "Delete local capture",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            SetStatus("Delete canceled.");
            return;
        }

        await _services.WorkspaceStore.DeleteItemAsync(item, deleteFile: true);
        _allCaptures.RemoveAll(existing =>
            existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
            existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
        ApplyFilter();
        PreviewImage.Source = null;
        PreviewHint.Text = "Select a capture";
        DetailsText.Text = string.Empty;
        SetStatus($"Deleted local capture: {item.FileName}");
    }

    private static bool ShouldShowShareResultWindow(ShareDestination destination, ShareResult result)
    {
        return ShareService.IsExternalDestination(destination) || !string.IsNullOrWhiteSpace(result.Url);
    }

    private async Task<string?> GenerateShareQrCodeAsync(
        CaptureItem item,
        ShareDestination destination,
        ShareResult result)
    {
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Url))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_services.Paths.TempRoot);
            var destinationName = string.Join(
                "-",
                destination.ToString()
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
            var output = Path.Combine(
                _services.Paths.TempRoot,
                $"{item.Id}-{destinationName}-share-qr.png");
            return await _services.Barcodes.GenerateQrCodeAsync(result.Url, output);
        }
        catch (Exception ex)
        {
            SetStatus($"{result.Message} QR generation failed: {ex.Message}");
            return null;
        }
    }

    private void PickColor()
    {
        var picked = _services.ColorPicker.PickUnderCursor();
        ClipboardInterop.SetText(picked.Hex);
        SetStatus($"Picked color {picked.Display}; hex copied to clipboard.");
    }

    private void OpenPixelRuler()
    {
        var ruler = new PixelRulerWindow
        {
            Owner = this
        };
        ruler.Show();
        SetStatus("Pixel ruler open.");
    }

    private void OpenSettings(string? sectionKey = null)
    {
        SetStatus("Opening settings...");
        var settings = TakeSettingsWindow();
        settings.Owner = this;
        settings.SelectSection(sectionKey);
        try
        {
            settings.ShowDialog();
        }
        finally
        {
            _services.WatchFolders.Restart();
            _services.UploadQueueWorker.Restart();
            RefreshDiagnostics();
            QueueSettingsPrewarm();
            SetStatus("Settings updated.");
        }
    }

    private SettingsWindow TakeSettingsWindow()
    {
        if (_prewarmedSettingsWindow is { } settings)
        {
            _prewarmedSettingsWindow = null;
            return settings;
        }

        return new SettingsWindow(_services);
    }

    private void QueueSettingsPrewarm()
    {
        if (_auditMode || _settingsPrewarmQueued || _prewarmedSettingsWindow is not null)
        {
            return;
        }

        _settingsPrewarmQueued = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _settingsPrewarmQueued = false;
            if (_prewarmedSettingsWindow is not null)
            {
                return;
            }

            try
            {
                _prewarmedSettingsWindow = new SettingsWindow(_services)
                {
                    Owner = this
                };
                _prewarmedSettingsWindow.Closed += (_, _) =>
                {
                    _prewarmedSettingsWindow = null;
                };
            }
            catch (Exception ex)
            {
                SetStatus($"Settings prewarm failed: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RefreshDiagnostics()
    {
        var snapshot = _services.Diagnostics.GetSnapshot();
        DiagnosticsText.Text =
            $"{snapshot.CaptureEngine}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.RecordingEngine}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.OcrStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.AiStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.PolicyStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.StartupStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.MetadataIndexStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.UploadQueueStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.PrintImportStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.PluginStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.BrowserBridgeStatus}{Environment.NewLine}{Environment.NewLine}" +
            $"{snapshot.SharingStatus}";
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private static string OcrSummary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "not indexed";
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 140 ? normalized : $"{normalized[..140]}...";
    }

    private static string OcrValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "n/a" : value;
    }

    private bool IsAiDenied(CaptureItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceApp))
        {
            return false;
        }

        return _services.Settings.AiDenylistProcesses
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Select(rule => rule.Trim())
            .Any(rule =>
                item.SourceApp.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(item.SourceApp).Equals(rule, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static BitmapImage? LoadPreviewImage(CaptureItem item)
    {
        try
        {
            return ImageInterop.LoadBitmapImage(item.FilePath);
        }
        catch
        {
            return File.Exists(item.ThumbnailPath)
                ? ImageInterop.LoadBitmapImage(item.ThumbnailPath)
                : null;
        }
    }

    private static string MergeSensitiveScanNote(string? notes, string scanNote)
    {
        return MergePrefixedNote(notes, SensitiveScanNotePrefix, scanNote);
    }

    private static string MergePrefixedNote(string? notes, string prefix, string note)
    {
        var lines = (notes ?? string.Empty)
            .ReplaceLineEndings(Environment.NewLine)
            .Split(Environment.NewLine, StringSplitOptions.None)
            .Where(line => !line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(note);
        return string.Join(Environment.NewLine, lines);
    }

    private void Service_StatusChanged(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            SetStatus(message);
            if (sender is UploadQueueWorkerService)
            {
                _ = RefreshUploadQueueAsync();
                RefreshDiagnostics();
            }
        });
    }

    private void Automation_CaptureImported(object? sender, CaptureItem item)
    {
        Dispatcher.Invoke(() =>
        {
            _allCaptures.RemoveAll(existing => existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            _allCaptures.Insert(0, item);
            ApplyFilter();
            CaptureList.SelectedItem = item;
        });
    }

    private void StepRecorder_StepCaptured(object? sender, StepRecordingStep step)
    {
        Dispatcher.Invoke(() =>
        {
            var item = _services.WorkspaceStore.Load()
                .FirstOrDefault(item => item.Id.Equals(step.CaptureItemId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return;
            }

            _allCaptures.RemoveAll(existing => existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            _allCaptures.Insert(0, item);
            ApplyFilter();
            CaptureList.SelectedItem = item;
        });
    }
}

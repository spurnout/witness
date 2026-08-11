using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Services;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace GoatShot.App.Windows;

public partial class FrameExplorerWindow : Window
{
    private readonly CaptureItem _item;
    private readonly ReplayReceiptExplorerService _explorer;
    private readonly ReceiptSceneAnalysisService _analysisService;
    private readonly ReceiptIntegrityService _integrity;
    private readonly WorkspaceStore _workspaceStore;
    private readonly string _deviceKeyPath;
    private readonly string _tempRoot;
    private readonly HashSet<string> _disposableMediaPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<SceneView> _scenes = [];
    private readonly ObservableCollection<ReceiptOcrChange> _changes = [];
    private readonly ObservableCollection<TrackExportChoice> _exportTracks = [];
    private readonly DispatcherTimer _timelineTimer;
    private ReplayReceiptDocument? _receipt;
    private bool _isPlaying;
    private bool _settingTimeline;
    private bool _suppressTrackSelection;
    private bool _receiptIsVerified;
    private int _changePreviewVersion;
    private TimeSpan? _selectedRangeStart;
    private TimeSpan? _selectedRangeEnd;
    private bool _isSyntheticRenderProof;
    private string? _currentPlaybackPath;
    private readonly SemaphoreSlim _verificationGate = new(1, 1);

    public FrameExplorerWindow(AppServices services, CaptureItem item)
        : this(services, item, autoLoad: true)
    {
    }

    internal FrameExplorerWindow(AppServices services, CaptureItem item, bool autoLoad)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(item);
        _item = item;
        _explorer = new ReplayReceiptExplorerService(services.Paths, services.WorkspaceStore);
        _analysisService = new ReceiptSceneAnalysisService(services.Ocr);
        _integrity = services.ReceiptIntegrity;
        _workspaceStore = services.WorkspaceStore;
        _deviceKeyPath = Path.Combine(services.Paths.SecretsRoot, ReceiptDeviceKeyService.DefaultKeyFileName);
        _tempRoot = Path.GetFullPath(services.Paths.TempRoot);
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);
        SceneList.ItemsSource = _scenes;
        ChangeList.ItemsSource = _changes;
        ExportTrackList.ItemsSource = _exportTracks;
        AnalyzeScenesBox.IsChecked = services.Settings.Replay.EnableSceneIndexing;
        AnalyzeOcrBox.IsChecked = services.Settings.Replay.EnableLocalOcrIndexing;
        _timelineTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, UpdateTimeline, Dispatcher);
        if (autoLoad)
        {
            Loaded += async (_, _) => await LoadReceiptAsync();
        }

        Closed += (_, _) =>
        {
            _timelineTimer.Stop();
            Player.Stop();
            Player.Source = null;
            foreach (var path in _disposableMediaPaths.ToArray())
            {
                TryDeleteDisposableMedia(path);
            }
        };
    }

    internal async Task PrepareRenderProofAsync(string previewFramePath)
    {
        _isSyntheticRenderProof = true;
        await LoadReceiptAsync(skipVerification: true, preparePlayback: false);
        _receiptIsVerified = true;
        Player.Stop();
        Player.Source = null;
        Player.Visibility = Visibility.Collapsed;
        PlayerPreviewImage.Source = LoadImage(previewFramePath);
        PlayerPreviewImage.Visibility = Visibility.Visible;
        TrackExportOptionsExpander.IsExpanded = true;
        if (SceneList.Items.Count > 1)
        {
            SceneList.SelectedIndex = 1;
        }

        if (ChangeList.Items.Count > 0)
        {
            ChangeList.SelectedIndex = 0;
        }

        SetStatus("Synthetic render preview · two replay tracks · local scene index · one unconfirmed possible edit.");
    }

    internal void ShowRenderTimelineHoverPreview()
    {
        UpdateLayout();
        ShowTimelineHoverPreview(
            TimeSpan.FromSeconds(2.4d),
            Math.Max(0d, TimelineSlider.ActualWidth * 0.62d));
    }

    private async Task LoadReceiptAsync(
        string? selectedTrackId = null,
        bool skipVerification = false,
        bool preparePlayback = true)
    {
        try
        {
            if (!skipVerification && !await EnsureReceiptIntactAsync("Opening Frame Explorer"))
            {
                return;
            }

            SetStatus("Loading signed replay receipt…");
            _receipt = await _explorer.LoadAsync(_item.FilePath);
            ReceiptSummaryText.Text = $"Receipt {_receipt.Manifest.ReceiptId} · {_receipt.Manifest.Segments.Count} finalized segment(s) · {_receipt.Manifest.Tracks.Count} track(s)";
            _suppressTrackSelection = true;
            TrackBox.ItemsSource = _receipt.Manifest.Tracks;
            var selectedTrack = _receipt.Manifest.Tracks.FirstOrDefault(track =>
                track.TrackId.Equals(selectedTrackId, StringComparison.Ordinal)) ?? _receipt.Manifest.Tracks.FirstOrDefault();
            TrackBox.SelectedItem = selectedTrack;
            _suppressTrackSelection = false;
            if (_exportTracks.Count == 0)
            {
                foreach (var track in _receipt.Manifest.Tracks)
                {
                    _exportTracks.Add(new TrackExportChoice(track));
                }
            }

            RefreshAnalysisViews();
            SetOriginalControlsEnabled(true);
            if (preparePlayback && selectedTrack is not null)
            {
                await PrepareTrackPlaybackAsync(selectedTrack);
            }

            SetStatus(_receipt.Analysis?.Warnings.Count > 0
                ? $"Replay receipt loaded. {string.Join(" ", _receipt.Analysis.Warnings.Take(2))}"
                : _receipt.Analysis?.OcrComparisonEnabled == true
                    ? "Replay receipt loaded. OCR findings are local suggestions and require human confirmation."
                    : "Replay receipt loaded. Local scene indexing and OCR comparison remain independently configurable.");
        }
        catch (Exception ex)
        {
            _suppressTrackSelection = false;
            SetStatus($"Frame Explorer could not load this receipt: {ex.Message}");
        }
    }

    private async void TrackBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTrackSelection || _receipt is null || TrackBox.SelectedItem is not ReceiptTrackManifest track)
        {
            return;
        }

        if (!await EnsureReceiptIntactAsync("Preparing track playback"))
        {
            return;
        }

        await PrepareTrackPlaybackAsync(track);
    }

    private async Task PrepareTrackPlaybackAsync(ReceiptTrackManifest track)
    {
        try
        {
            var receipt = _receipt ?? throw new InvalidOperationException(
                "The replay receipt must be loaded before preparing playback.");
            _timelineTimer.Stop();
            _isPlaying = false;
            PlayButton.Content = "Play";
            TimelineHoverPreviewBorder.Visibility = Visibility.Collapsed;
            SetStatus($"Preparing {track.DisplayName}…");
            var path = await _explorer.BuildTrackPlaybackAsync(receipt, track.TrackId);
            Player.Stop();
            Player.Source = null;
            if (_currentPlaybackPath is not null)
            {
                TryDeleteDisposableMedia(_currentPlaybackPath);
            }

            _currentPlaybackPath = TrackDisposableMedia(path) ? path : null;
            Player.Source = new Uri(path, UriKind.Absolute);
            Player.Position = TimeSpan.Zero;
            Player.Play();
            Player.Pause();
            ClearSelectedRange();
            UpdateTimelineExtent();
            RefreshAnalysisViews();
            SetStatus($"Ready to browse {track.DisplayName}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Track playback could not be prepared: {ex.Message}");
        }
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        UpdateTimelineExtent();
        UpdateTimeText();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        PlayButton.Content = "Play";
        _timelineTimer.Stop();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!_receiptIsVerified || Player.Source is null)
        {
            SetStatus("Verify the original receipt before playback.");
            return;
        }

        if (_isPlaying)
        {
            Player.Pause();
            _timelineTimer.Stop();
            _isPlaying = false;
            PlayButton.Content = "Play";
        }
        else
        {
            Player.Play();
            _timelineTimer.Start();
            _isPlaying = true;
            PlayButton.Content = "Pause";
        }
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void NextFrame_Click(object sender, RoutedEventArgs e) => StepFrame(1);

    private void StepFrame(int direction)
    {
        if (!_receiptIsVerified)
        {
            SetStatus("Verify the original receipt before stepping through frames.");
            return;
        }

        var fps = Math.Max(1, _receipt?.Manifest.CaptureSettings.FramesPerSecond ?? 30);
        Player.Pause();
        _isPlaying = false;
        PlayButton.Content = "Play";
        _timelineTimer.Stop();
        var next = Player.Position + TimeSpan.FromSeconds(direction / (double)fps);
        Player.Position = ClampPosition(next, GetEffectiveDuration());
        UpdateTimeline(null, EventArgs.Empty);
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingTimeline || Player.Source is null)
        {
            return;
        }

        Player.Position = ClampPosition(
            TimeSpan.FromSeconds(Math.Max(0d, e.NewValue)),
            GetEffectiveDuration());
        UpdateTimeText();
    }

    private void TimelineSlider_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var duration = GetEffectiveDuration();
        if (duration <= TimeSpan.Zero || TimelineSlider.ActualWidth <= 0d)
        {
            TimelineHoverPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var horizontalPosition = Math.Clamp(
            e.GetPosition(TimelineSlider).X,
            0d,
            TimelineSlider.ActualWidth);
        var ratio = horizontalPosition / TimelineSlider.ActualWidth;
        ShowTimelineHoverPreview(
            TimeSpan.FromTicks((long)Math.Round(duration.Ticks * ratio)),
            horizontalPosition);
    }

    private void TimelineSlider_MouseLeave(object sender, WpfMouseEventArgs e) =>
        TimelineHoverPreviewBorder.Visibility = Visibility.Collapsed;

    private void ShowTimelineHoverPreview(TimeSpan position, double horizontalPosition)
    {
        if (_receipt is null || TrackBox.SelectedItem is not ReceiptTrackManifest track)
        {
            TimelineHoverPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var duration = GetEffectiveDuration();
        position = ClampPosition(position, duration);
        var origin = _receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
            .Select(segment => segment.StartMonotonicTicks)
            .DefaultIfEmpty(0L)
            .Min();
        var hoverFrame = FindNearestHoverFrame(
            _receipt.Analysis,
            track.TrackId,
            origin,
            position);
        TimelineHoverImage.Source = hoverFrame is null
            ? null
            : LoadImage(Path.Combine(_receipt.PackagePath, hoverFrame.RelativeFramePath));
        var nearestTime = hoverFrame is null
            ? string.Empty
            : ClampPosition(
                TimeSpan.FromTicks(Math.Max(0L, hoverFrame.MonotonicTicks - origin)),
                duration).ToString("mm\\:ss\\.fff");
        TimelineHoverText.Text = hoverFrame is null
            ? $"{position:mm\\:ss\\.fff} · No local indexed frame"
            : $"{position:mm\\:ss\\.fff} · Unsigned local preview · {hoverFrame.Label} {nearestTime}";
        AutomationProperties.SetName(
            TimelineHoverPreviewBorder,
            hoverFrame is null
                ? $"Timeline hover at {position:mm\\:ss\\.fff}. No local indexed frame is available."
                : $"Timeline hover at {position:mm\\:ss\\.fff}. Nearest locally indexed {hoverFrame.Label.ToLowerInvariant()} is at {nearestTime}. This unsigned local preview is rebuildable and is not original evidence. No OCR was run for this hover preview.");
        var availableWidth = Math.Max(0d, TimelineSlider.ActualWidth - TimelineHoverPreviewBorder.Width);
        var left = Math.Clamp(
            horizontalPosition - (TimelineHoverPreviewBorder.Width / 2d),
            0d,
            availableWidth);
        TimelineHoverPreviewBorder.RenderTransform = new TranslateTransform(left, 0d);
        TimelineHoverPreviewBorder.Visibility = Visibility.Visible;
    }

    private void UpdateTimeline(object? sender, EventArgs e)
    {
        UpdateTimelineExtent();
        var position = ClampPosition(Player.Position, GetEffectiveDuration());
        if (position != Player.Position)
        {
            Player.Position = position;
        }

        _settingTimeline = true;
        TimelineSlider.Value = Math.Min(TimelineSlider.Maximum, Math.Max(0d, position.TotalSeconds));
        _settingTimeline = false;
        UpdateTimeText();
    }

    private void UpdateTimeText()
    {
        var duration = GetEffectiveDuration();
        var position = ClampPosition(Player.Position, duration);
        TimeText.Text = $"{position:mm\\:ss\\.fff} / {duration:mm\\:ss\\.fff}";
    }

    private void UpdateTimelineExtent()
    {
        var duration = GetEffectiveDuration();
        TimelineSlider.Maximum = Math.Max(0.001d, duration.TotalSeconds);
    }

    private TimeSpan GetEffectiveDuration()
    {
        if (Player.NaturalDuration.HasTimeSpan && Player.NaturalDuration.TimeSpan > TimeSpan.Zero)
        {
            return Player.NaturalDuration.TimeSpan;
        }

        return CalculateTrackDuration(
            _receipt?.Manifest,
            (TrackBox.SelectedItem as ReceiptTrackManifest)?.TrackId);
    }

    private async void SaveFrame_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelection(out var receipt, out var track))
        {
            return;
        }

        await RunActionAsync(
            "Saving linked frame…",
            () => _explorer.SaveFrameAsync(receipt, track.TrackId, Player.Position));
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_receipt is null)
        {
            return;
        }

        if (!await EnsureReceiptIntactAsync("Running local analysis"))
        {
            return;
        }

        try
        {
            var options = new ReceiptAnalysisOptions(
                EnableSceneIndexing: AnalyzeScenesBox.IsChecked == true,
                EnableOcrComparison: AnalyzeOcrBox.IsChecked == true,
                Sensitivity: Math.Clamp(
                    _receipt.Manifest.CaptureSettings.AdditionalSettings.TryGetValue("analysisSensitivity", out var value) &&
                    double.TryParse(value, out var parsed) ? parsed : 0.65d,
                    0.05d,
                    1d));
            if (!options.EnableSceneIndexing && !options.EnableOcrComparison)
            {
                SetStatus("Enable scene indexing, OCR comparison, or both before running local analysis.");
                return;
            }

            SetStatus(options switch
            {
                { EnableSceneIndexing: true, EnableOcrComparison: true } =>
                    "Extracting scene frames and comparing local OCR. The replay buffer is not analyzed continuously.",
                { EnableSceneIndexing: true } =>
                    "Indexing local scene changes without running OCR. The replay buffer is not analyzed continuously.",
                _ => "Comparing local OCR without creating scene markers. The replay buffer is not analyzed continuously."
            });
            await _analysisService.AnalyzeAsync(
                _receipt.PackagePath,
                options);
            await LoadReceiptAsync((TrackBox.SelectedItem as ReceiptTrackManifest)?.TrackId);
            SetStatus(options.EnableOcrComparison
                ? "Local analysis completed. Review each possible OCR change against its before and after frames."
                : "Local scene indexing completed without OCR.");
        }
        catch (Exception ex)
        {
            SetStatus($"Local analysis failed: {ex.Message}");
        }
    }

    private async void ExtractUnique_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelection(out var receipt, out var track))
        {
            var range = NormalizeSelectedRange(_selectedRangeStart, _selectedRangeEnd);
            await RunActionAsync(
                range.Start.HasValue || range.End.HasValue
                    ? "Extracting unique linked frames from the selected range…"
                    : "Extracting unique linked frames from the full track…",
                () => _explorer.ExtractUniqueFramesAsync(
                    receipt,
                    track.TrackId,
                    range.Start,
                    range.End));
        }
    }

    private void MarkRangeStart_Click(object sender, RoutedEventArgs e)
    {
        _selectedRangeStart = ClampPosition(Player.Position, GetEffectiveDuration());
        UpdateSelectedRangeText();
    }

    private void MarkRangeEnd_Click(object sender, RoutedEventArgs e)
    {
        _selectedRangeEnd = ClampPosition(Player.Position, GetEffectiveDuration());
        UpdateSelectedRangeText();
    }

    private void ClearRange_Click(object sender, RoutedEventArgs e) => ClearSelectedRange();

    private void ClearSelectedRange()
    {
        _selectedRangeStart = null;
        _selectedRangeEnd = null;
        UpdateSelectedRangeText();
    }

    private void UpdateSelectedRangeText()
    {
        var range = NormalizeSelectedRange(_selectedRangeStart, _selectedRangeEnd);
        SelectedRangeText.Text = range switch
        {
            { Start: null, End: null } => "Range: full track",
            { Start: not null, End: null } => $"Range: {range.Start:mm\\:ss\\.fff} → end",
            { Start: null, End: not null } => $"Range: start → {range.End:mm\\:ss\\.fff}",
            _ => $"Range: {range.Start:mm\\:ss\\.fff} → {range.End:mm\\:ss\\.fff}"
        };
    }

    private async void ContactSheet_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelection(out var receipt, out var track))
        {
            await RunActionAsync("Creating linked contact sheet…", () => _explorer.ExportContactSheetAsync(receipt, track.TrackId));
        }
    }

    private async void ExportTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_receipt is not null)
        {
            var trackIds = GetCheckedExportTrackIds();
            await RunActionAsync(
                "Exporting checked tracks as linked derivatives…",
                () => _explorer.ExportTracksAsync(_receipt, trackIds));
        }
    }

    private async void ExportComposite_Click(object sender, RoutedEventArgs e)
    {
        if (_receipt is not null)
        {
            var trackIds = GetCheckedExportTrackIds();
            await RunActionAsync(
                "Building a synchronized linked composite MP4…",
                () => _explorer.ExportCompositeAsync(_receipt, trackIds));
        }
    }

    private async void StepGuide_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelection(out var receipt, out var track))
        {
            await RunActionAsync("Creating linked step-by-step guide…", () => _explorer.CreateStepGuideAsync(receipt, track.TrackId));
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Verifying signed manifest and every original segment…");
        var check = await VerifyAndPersistReceiptAsync();
        if (!check.Result.IsIntact || check.PersistenceError is not null)
        {
            InvalidateLoadedReceipt();
        }
        else
        {
            _receiptIsVerified = true;
            SetOriginalControlsEnabled(true);
        }

        var persistence = check.PersistenceError is null
            ? string.Empty
            : $" Integrity status could not be saved to the library: {check.PersistenceError}";
        SetStatus($"{VerificationLabel(check.Result.Status)}. {string.Join(" ", check.Result.Issues)}{persistence}".Trim());
    }

    private void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SceneList.SelectedItem is not SceneView scene || _receipt is null ||
            TrackBox.SelectedItem is not ReceiptTrackManifest track)
        {
            return;
        }

        var origin = _receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
            .Min(segment => segment.StartMonotonicTicks);
        Player.Position = ClampPosition(
            TimeSpan.FromTicks(Math.Max(0, scene.Marker.MonotonicTicks - origin)),
            GetEffectiveDuration());
        UpdateTimeline(null, EventArgs.Empty);
    }

    private async void ChangeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_receipt?.Analysis is null || ChangeList.SelectedItem is not ReceiptOcrChange change)
        {
            return;
        }

        var before = _receipt.Analysis.Frames.FirstOrDefault(frame => frame.FrameId == change.BeforeFrameId);
        var after = _receipt.Analysis.Frames.FirstOrDefault(frame => frame.FrameId == change.AfterFrameId);
        BeforeText.Text = change.BeforeText;
        AfterText.Text = change.AfterText;
        BeforeImage.Source = null;
        AfterImage.Source = null;
        if (before is null || after is null)
        {
            SetStatus($"{change.Label}: the local OCR index no longer references both supporting frames.");
            return;
        }

        if (_isSyntheticRenderProof)
        {
            BeforeImage.Source = LoadImage(Path.Combine(_receipt.PackagePath, before.RelativeFramePath));
            AfterImage.Source = LoadImage(Path.Combine(_receipt.PackagePath, after.RelativeFramePath));
            SetStatus($"{change.Label}: synthetic local-analysis render fixture.");
            return;
        }

        if (!await EnsureReceiptIntactAsync("Loading OCR comparison frames"))
        {
            return;
        }

        var receipt = _receipt;
        if (receipt is null)
        {
            return;
        }

        var previewVersion = ++_changePreviewVersion;
        try
        {
            SetStatus("Re-extracting comparison frames from verified original segments…");
            var beforePath = await _explorer.BuildAnalysisFramePreviewAsync(
                receipt,
                before.TrackId,
                before.SegmentId,
                before.MonotonicTicks);
            var afterPath = await _explorer.BuildAnalysisFramePreviewAsync(
                receipt,
                after.TrackId,
                after.SegmentId,
                after.MonotonicTicks);
            TrackDisposableMedia(beforePath);
            TrackDisposableMedia(afterPath);
            if (previewVersion != _changePreviewVersion)
            {
                return;
            }

            BeforeImage.Source = LoadImage(beforePath);
            AfterImage.Source = LoadImage(afterPath);
            SetStatus(
                $"{change.Label}: local OCR inference only. Supporting pixels were re-extracted from " +
                "verified original segments; confirm or dismiss after reviewing both frames.");
        }
        catch (Exception ex)
        {
            SetStatus($"Comparison frames could not be re-extracted from the verified original: {ex.Message}");
        }
    }

    private async void ConfirmChange_Click(object sender, RoutedEventArgs e) =>
        await ReviewSelectedChangeAsync(ReceiptChangeReviewState.Confirmed);

    private async void DismissChange_Click(object sender, RoutedEventArgs e) =>
        await ReviewSelectedChangeAsync(ReceiptChangeReviewState.Dismissed);

    private async Task ReviewSelectedChangeAsync(ReceiptChangeReviewState state)
    {
        if (_receipt is null || ChangeList.SelectedItem is not ReceiptOcrChange change)
        {
            return;
        }

        if (!await EnsureReceiptIntactAsync("Reviewing a possible OCR change"))
        {
            return;
        }

        try
        {
            await _analysisService.SaveReviewStateAsync(_receipt.PackagePath, change.ChangeId, state);
            change.ReviewState = state;
            ChangeList.Items.Refresh();
            SetStatus(state == ReceiptChangeReviewState.Confirmed
                ? "Possible OCR change marked as human-confirmed. This does not turn it into independent proof of remote state."
                : "Possible OCR change dismissed.");
        }
        catch (Exception ex)
        {
            SetStatus($"Review state could not be saved: {ex.Message}");
        }
    }

    private void RefreshAnalysisViews()
    {
        _scenes.Clear();
        _changes.Clear();
        if (_receipt?.Analysis is null || TrackBox.SelectedItem is not ReceiptTrackManifest track)
        {
            return;
        }

        var origin = _receipt.Manifest.Segments
            .Where(segment => segment.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
            .Select(segment => segment.StartMonotonicTicks)
            .DefaultIfEmpty(0)
            .Min();
        foreach (var marker in _receipt.Analysis.Scenes
                     .Where(scene => scene.TrackId.Equals(track.TrackId, StringComparison.Ordinal))
                     .OrderBy(scene => scene.MonotonicTicks))
        {
            _scenes.Add(new SceneView(
                marker,
                string.Empty,
                TimeSpan.FromTicks(Math.Max(0, marker.MonotonicTicks - origin)).ToString("mm\\:ss\\.fff"),
                marker.IsSourceTransition
                    ? "Source transition · local index marker"
                    : marker.IsVisuallyDistinct ? "Scene change · local index marker" : "Indexed marker"));
        }

        foreach (var change in _receipt.Analysis.Changes.Where(change =>
                     change.TrackId.Equals(track.TrackId, StringComparison.Ordinal)))
        {
            _changes.Add(change);
        }
    }

    private bool TryGetSelection(out ReplayReceiptDocument receipt, out ReceiptTrackManifest track)
    {
        receipt = _receipt!;
        track = (ReceiptTrackManifest)TrackBox.SelectedItem!;
        if (_receipt is null || TrackBox.SelectedItem is not ReceiptTrackManifest selected)
        {
            SetStatus("Select a replay track first.");
            return false;
        }

        receipt = _receipt;
        track = selected;
        return true;
    }

    private string[] GetCheckedExportTrackIds() => _exportTracks
        .Where(choice => choice.IsSelected)
        .Select(choice => choice.Track.TrackId)
        .ToArray();

    private async Task<bool> EnsureReceiptIntactAsync(string operation)
    {
        if (_isSyntheticRenderProof)
        {
            return true;
        }

        var check = await VerifyAndPersistReceiptAsync();
        if (check.PersistenceError is null && CanUseOriginal(check.Result.Status))
        {
            _receiptIsVerified = true;
            SetOriginalControlsEnabled(true);
            return true;
        }

        InvalidateLoadedReceipt();
        var issues = check.Result.Issues.Count == 0
            ? string.Empty
            : $" {string.Join(" ", check.Result.Issues)}";
        var persistence = check.PersistenceError is null
            ? string.Empty
            : $" Library status could not be persisted: {check.PersistenceError}";
        SetStatus(
            $"{VerificationLabel(check.Result.Status)}. {operation} is blocked because the original " +
            $"receipt is not currently verified intact.{issues}{persistence}");
        return false;
    }

    private async Task<ReceiptVerificationCheck> VerifyAndPersistReceiptAsync()
    {
        await _verificationGate.WaitAsync();
        try
        {
            ReceiptVerificationResult result;
            try
            {
                result = await _integrity.VerifyPackageAsync(_item.FilePath, _deviceKeyPath);
            }
            catch (Exception ex)
            {
                result = new ReceiptVerificationResult
                {
                    Status = ReceiptVerificationStatus.Unverifiable,
                    Issues = [$"Receipt verification failed: {ex.Message}"]
                };
            }

            _item.IntegrityStatus = VerificationLabel(result.Status);
            try
            {
                await _workspaceStore.UpdateItemAsync(_item);
                return new ReceiptVerificationCheck(result, null);
            }
            catch (Exception ex)
            {
                return new ReceiptVerificationCheck(result, ex.Message);
            }
        }
        finally
        {
            _verificationGate.Release();
        }
    }

    private void InvalidateLoadedReceipt()
    {
        _receiptIsVerified = false;
        _timelineTimer.Stop();
        _isPlaying = false;
        Player.Stop();
        Player.Source = null;
        if (_currentPlaybackPath is not null)
        {
            TryDeleteDisposableMedia(_currentPlaybackPath);
            _currentPlaybackPath = null;
        }

        PlayerPreviewImage.Source = null;
        TimelineHoverPreviewBorder.Visibility = Visibility.Collapsed;
        _receipt = null;
        _suppressTrackSelection = true;
        TrackBox.ItemsSource = null;
        TrackBox.SelectedItem = null;
        _suppressTrackSelection = false;
        _exportTracks.Clear();
        _scenes.Clear();
        _changes.Clear();
        BeforeImage.Source = null;
        AfterImage.Source = null;
        BeforeText.Text = string.Empty;
        AfterText.Text = string.Empty;
        PlayButton.Content = "Play";
        ReceiptSummaryText.Text = "Original receipt media is unavailable until verification succeeds.";
        SetOriginalControlsEnabled(false);
    }

    private void SetOriginalControlsEnabled(bool enabled)
    {
        TrackBox.IsEnabled = enabled;
        Player.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        TimelineSlider.IsEnabled = enabled;
        AnalyzeScenesBox.IsEnabled = enabled;
        AnalyzeOcrBox.IsEnabled = enabled;
        ScenesTab.IsEnabled = enabled;
        TextChangesTab.IsEnabled = enabled;
        TrackExportOptionsExpander.IsEnabled = enabled;
    }

    private async Task RunActionAsync(string pending, Func<Task<ReplayDerivativeResult>> action)
    {
        try
        {
            if (!await EnsureReceiptIntactAsync("Creating a derivative"))
            {
                return;
            }

            SetStatus(pending);
            var result = await action();
            SetStatus(result.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"Action failed: {ex.Message}");
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private bool TrackDisposableMedia(string path)
    {
        var candidate = Path.GetFullPath(path);
        var prefix = _tempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _disposableMediaPaths.Add(candidate);
        return true;
    }

    private void TryDeleteDisposableMedia(string path)
    {
        var candidate = Path.GetFullPath(path);
        if (!_disposableMediaPaths.Contains(candidate))
        {
            return;
        }

        try
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }

            _disposableMediaPaths.Remove(candidate);
        }
        catch (IOException)
        {
            // The media stack can release a file shortly after the window closes; temp cleanup can retry later.
        }
        catch (UnauthorizedAccessException)
        {
            // Temp cleanup can retry later.
        }
    }

    private static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static string VerificationLabel(ReceiptVerificationStatus status) =>
        ReceiptVerificationPresentation.FormatStatus(status);

    internal static bool CanUseOriginal(ReceiptVerificationStatus status) =>
        status is ReceiptVerificationStatus.IntactKnownDevice or
            ReceiptVerificationStatus.IntactUnknownDevice;

    internal static TimeSpan CalculateTrackDuration(
        ReceiptManifest? manifest,
        string? trackId)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(trackId))
        {
            return TimeSpan.Zero;
        }

        var segments = manifest.Segments
            .Where(segment => segment.TrackId.Equals(trackId, StringComparison.Ordinal))
            .ToArray();
        if (segments.Length == 0)
        {
            return TimeSpan.Zero;
        }

        var start = segments.Min(segment => (decimal)segment.StartMonotonicTicks);
        var end = segments.Max(segment =>
            (decimal)segment.StartMonotonicTicks + Math.Max(0L, segment.DurationTicks));
        var durationTicks = Math.Clamp(end - start, 0m, long.MaxValue);
        return TimeSpan.FromTicks((long)durationTicks);
    }

    internal static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || position <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > duration ? duration : position;
    }

    internal static (TimeSpan? Start, TimeSpan? End) NormalizeSelectedRange(
        TimeSpan? start,
        TimeSpan? end)
    {
        start = start.HasValue && start.Value < TimeSpan.Zero ? TimeSpan.Zero : start;
        end = end.HasValue && end.Value < TimeSpan.Zero ? TimeSpan.Zero : end;
        return start.HasValue && end.HasValue && start.Value > end.Value
            ? (end, start)
            : (start, end);
    }

    internal static TimelineHoverFrame? FindNearestHoverFrame(
        ReceiptLocalAnalysis? analysis,
        string trackId,
        long trackOriginMonotonicTicks,
        TimeSpan position)
    {
        if (analysis is null || string.IsNullOrWhiteSpace(trackId))
        {
            return null;
        }

        var targetTicks = (decimal)trackOriginMonotonicTicks + Math.Max(0L, position.Ticks);
        var scenes = analysis.Scenes
            .Where(scene => scene.TrackId.Equals(trackId, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(scene.RelativeFramePath))
            .Select(scene => new TimelineHoverFrame(
                scene.RelativeFramePath,
                scene.MonotonicTicks,
                scene.IsSourceTransition
                    ? "Source transition"
                    : scene.IsVisuallyDistinct ? "Scene" : "Indexed frame"))
            .ToArray();
        var candidates = scenes.Length > 0
            ? scenes
            : analysis.Frames
                .Where(frame => frame.TrackId.Equals(trackId, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(frame.RelativeFramePath))
                .Select(frame => new TimelineHoverFrame(
                    frame.RelativeFramePath,
                    frame.MonotonicTicks,
                    "Indexed frame"))
                .ToArray();
        return candidates
            .OrderBy(candidate => Math.Abs((decimal)candidate.MonotonicTicks - targetTicks))
            .ThenBy(candidate => candidate.MonotonicTicks)
            .FirstOrDefault();
    }

    private sealed record SceneView(
        ReceiptSceneMarker Marker,
        string FramePath,
        string TimeLabel,
        string Label);

    internal sealed record TimelineHoverFrame(
        string RelativeFramePath,
        long MonotonicTicks,
        string Label);

    private sealed record ReceiptVerificationCheck(
        ReceiptVerificationResult Result,
        string? PersistenceError);

    private sealed class TrackExportChoice(ReceiptTrackManifest track)
    {
        public ReceiptTrackManifest Track { get; } = track;
        public string DisplayName => string.IsNullOrWhiteSpace(Track.DisplayName)
            ? Track.TrackId
            : Track.DisplayName;
        public string AutomationName => $"Include {DisplayName} in export";
        public bool IsSelected { get; set; } = true;
    }
}

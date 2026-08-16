using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace GoatShot.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly List<AutomationRule> _automationRuleDrafts = new();
    private readonly RecordingSummaryTileService _recordingSummaryTiles = new();
    private readonly AutomationSummaryTileService _automationSummaryTiles = new();
    private bool _settingsSectionNavigationReady;
    private bool _suppressSectionSelectionSync;
    private bool _automationRuleSelectionUpdating;
    private bool _deferredStartupStarted;
    private bool _settingsLoading;
    private bool _summaryTileRefreshReady;
    private string? _selectedAutomationRuleId;
    private string _lastPluginUpdateCliCommand = "receipts plugins updates --registry \"<registry.json-or-url>\" --json";

    public SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);
        InitializeSettingsSectionNavigation();
        LoadSettings();
        _summaryTileRefreshReady = true;
        _settingsSectionNavigationReady = true;
        RecordingDeviceStatusText.Text = "Settings loaded. Recording devices refresh after the window opens.";
        ContentRendered += SettingsWindow_ContentRendered;
    }

    private bool CanRefreshSummaryTiles => _summaryTileRefreshReady && !_settingsLoading;

    private void SettingsWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_deferredStartupStarted)
        {
            return;
        }

        _deferredStartupStarted = true;
        _ = Dispatcher.BeginInvoke(
            async () => await RunDeferredStartupAsync(),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunDeferredStartupAsync()
    {
        WpfAccessibilityNameHelper.ApplyGeneratedNames(this);
        await RefreshRecordingDevicesAsync();
        await RefreshPluginUpdatesForDefaultRegistryAsync();
        // Arm only after deferred population finishes, so programmatic fills are not read as edits.
        ArmDirtyTracking();
    }

    public void SelectSection(string? key, bool alignSectionToTop = false)
    {
        var section = SettingsSectionCatalog.Find(key);
        if (section is null)
        {
            return;
        }

        foreach (var item in SettingsSectionBox.Items.OfType<ListBoxItem>())
        {
            if ((item.Tag as string)?.Equals(section.Key, StringComparison.OrdinalIgnoreCase) == true)
            {
                SettingsSectionBox.SelectedItem = item;
                SettingsSectionBox.ScrollIntoView(item);
                break;
            }
        }

        Dispatcher.BeginInvoke(() =>
        {
            var target = FindSettingsSectionTarget(section.Key);
            if (target is null)
            {
                return;
            }

            target.BringIntoView();
            if (alignSectionToTop)
            {
                target.UpdateLayout();
                var top = target.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y +
                    SettingsScroll.VerticalOffset;
                SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 10));
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void SelectAutomationAdvancedRuleFields()
    {
        SelectSection("Automation", alignSectionToTop: true);
        Dispatcher.BeginInvoke(() =>
        {
            RuleCaptureKindBox.BringIntoView();
            RuleCaptureKindBox.UpdateLayout();
            var top = RuleCaptureKindBox.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y +
                SettingsScroll.VerticalOffset;
            SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 170));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void SelectReplaySettings()
    {
        SelectSection("Recording", alignSectionToTop: true);
        Dispatcher.BeginInvoke(() =>
        {
            ReplaySettingsSection.BringIntoView();
            ReplaySettingsSection.UpdateLayout();
            var top = ReplaySettingsSection.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y +
                SettingsScroll.VerticalOffset;
            SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 10));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void SelectAutomationImageEffectRuleFields()
    {
        SelectSection("Automation", alignSectionToTop: true);
        Dispatcher.BeginInvoke(() =>
        {
            RuleImageEffectModeBox.BringIntoView();
            RuleImageEffectModeBox.UpdateLayout();
            var top = RuleImageEffectModeBox.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y +
                SettingsScroll.VerticalOffset;
            SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 520));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void InitializeSettingsSectionNavigation()
    {
        SettingsSectionBox.Items.Clear();
        foreach (var section in SettingsSectionCatalog.All)
        {
            var item = new ListBoxItem
            {
                Content = section.Label,
                Tag = section.Key,
                ToolTip = section.Description
            };
            System.Windows.Automation.AutomationProperties.SetName(item, $"{section.Label} settings section");
            SettingsSectionBox.Items.Add(item);

            // The rail and the heading are the same name by construction, so they cannot drift apart
            // the way "Sharing Providers" / "Sharing" and "Automation" / "Automation / Watch Folders" did.
            if (FindSettingsSectionTarget(section.Key) is TextBlock heading)
            {
                heading.Text = section.Label;
            }
        }

        SettingsSectionBox.SelectedIndex = SettingsSectionBox.Items.Count > 0 ? 0 : -1;
    }

    private void SettingsSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSectionSelectionSync)
        {
            return;
        }

        if (!_settingsSectionNavigationReady ||
            SettingsSectionBox.SelectedItem is not ListBoxItem item ||
            item.Tag is not string key)
        {
            return;
        }

        var target = FindSettingsSectionTarget(key);
        target?.BringIntoView();
    }

    // Jumps to a provider's first field inside the long "Advanced provider configuration"
    // list. The ComboBox supports type-ahead, so an operator can type "jira" and land on
    // the Jira block without scrolling past a dozen other providers.
    private void ProviderJump_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderJumpBox.SelectedItem is ComboBoxItem { Tag: string anchorName } &&
            FindName(anchorName) is FrameworkElement anchor)
        {
            anchor.BringIntoView();
            anchor.Focus();
        }
    }

    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_settingsSectionNavigationReady || e.VerticalChange == 0)
        {
            return;
        }

        var sectionTops = SettingsSectionCatalog.All
            .Select(section => (section.Key, Top: GetSettingsSectionTop(section.Key)))
            .ToList();
        var activeKey = SettingsSectionScrollSpy.PickActiveSectionKey(
            sectionTops,
            SettingsSectionScrollSpy.ActivationThreshold);
        if (activeKey is null)
        {
            return;
        }

        var activeItem = SettingsSectionBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                (item.Tag as string)?.Equals(activeKey, StringComparison.OrdinalIgnoreCase) == true);
        if (activeItem is null || ReferenceEquals(SettingsSectionBox.SelectedItem, activeItem))
        {
            return;
        }

        _suppressSectionSelectionSync = true;
        try
        {
            SettingsSectionBox.SelectedItem = activeItem;
            SettingsSectionBox.ScrollIntoView(activeItem);
        }
        finally
        {
            _suppressSectionSelectionSync = false;
        }
    }

    private double GetSettingsSectionTop(string key)
    {
        var target = FindSettingsSectionTarget(key);
        if (target is null || !target.IsLoaded)
        {
            return double.PositiveInfinity;
        }

        return target.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y;
    }

    private FrameworkElement? FindSettingsSectionTarget(string key)
    {
        return SettingsSectionCatalog.Find(key)?.Key switch
        {
            "General" => GeneralSettingsSection,
            "Keybinds" => KeybindsSettingsSection,
            "Recording" => RecordingSettingsSection,
            "Sharing" => SharingSettingsSection,
            "Automation" => AutomationSettingsSection,
            "Plugins" => PluginsSettingsSection,
            "Ai" => AiSettingsSection,
            _ => null
        };
    }

    private void LoadSettings()
    {
        _settingsLoading = true;
        try
        {
            LoadSettingsCore();
        }
        finally
        {
            _settingsLoading = false;
        }
    }

    private void LoadSettingsCore()
    {
        var settings = _services.Settings;
        LibraryRootBox.Text = settings.LibraryRoot;
        FileNameTemplateBox.Text = settings.FileNameTemplate;
        PopulatePostCaptureActionBox();
        SelectComboBoxItemByTag(PostCaptureActionBox, PostCaptureActionCatalog.Normalize(settings.PostCaptureAction));
        UpdatePostCaptureActionHelpText();
        CaptureActionsAutoDismissBox.Text = CaptureTaskAutoDismiss
            .NormalizeSeconds(settings.CaptureActionsAutoDismissSeconds)
            .ToString(CultureInfo.InvariantCulture);
        CopyAfterCaptureBox.IsChecked = settings.AutoCopyImageAfterCapture;
        IncludeCursorBox.IsChecked = settings.IncludeCursor;
        HoverAutoSelectBox.IsChecked = settings.EnableCaptureHoverAutoSelect;
        CaptureContextPaddingBox.Text = settings.CaptureContextPadding.ToString(CultureInfo.InvariantCulture);
        PrivateModeBox.IsChecked = settings.PrivateCaptureMode;
        RunAtStartupBox.IsChecked = _services.Startup.GetState().IsRegistered;
        RefreshPersonalInstallStatus();
        ManagedPolicyStatusText.Text = ManagedPolicyService.LoadEffective(settings).Summary;
        OcrLanguageBox.Text = settings.OcrLanguageTag;
        RefreshLegacyLibraryNotice();
        LoadKeybinds(settings);
        LoadRecordingSettings(settings.Recording ??= new RecordingSettings());
        LoadReplaySettings(settings.Replay ??= new ReplayBufferSettings());
        ConfirmUploadBox.IsChecked = settings.ConfirmBeforeUpload;
        QueueBackgroundProcessingBox.IsChecked = settings.UploadQueue.EnableBackgroundProcessing;
        QueuePollSecondsBox.Text = settings.UploadQueue.BackgroundPollSeconds.ToString();
        AiEnabledBox.IsChecked = settings.AiEnabled;
        ConfirmAiBox.IsChecked = settings.ConfirmBeforeAiRequest;
        GeminiEndpointBox.Text = settings.GeminiApiEndpoint;
        GeminiDefaultModelBox.Text = settings.GeminiDefaultModelId;
        GeminiSpeechModelBox.Text = settings.GeminiSpeechToTextModelId;
        TranscriptionProviderBox.SelectedValue = settings.TranscriptionProvider;
        if (TranscriptionProviderBox.SelectedIndex < 0) TranscriptionProviderBox.SelectedIndex = 0;
        ExternalWhisperExecutableBox.Text = settings.ExternalWhisperExecutablePath;
        ExternalWhisperModelBox.Text = settings.ExternalWhisperModelPath;
        ExternalWhisperLanguageBox.Text = settings.ExternalWhisperLanguage;
        GeminiManifestBox.Text = settings.GeminiModelManifestPath;
        GeminiKeyStatusText.Text = _services.SecretStore.HasGeminiApiKey
            ? "A Gemini API key is saved for the current Windows user."
            : "No Gemini API key is saved.";
        InitializePluginUpdateSurface();
        LocalExportFolderBox.Text = settings.LocalExportFolder;
        CustomScriptBox.Text = settings.CustomScriptCommand;
        CustomWebhookBox.Text = settings.CustomWebhookUrl;
        SlackWebhookBox.Text = settings.SlackWebhookUrl;
        SlackMessageTemplateBox.Text = settings.SlackMessageTemplate;
        DiscordWebhookBox.Text = settings.DiscordWebhookUrl;
        DiscordMessageTemplateBox.Text = settings.DiscordMessageTemplate;
        TeamsWebhookBox.Text = settings.TeamsWebhookUrl;
        TeamsMessageTemplateBox.Text = settings.TeamsMessageTemplate;
        S3EndpointBox.Text = settings.S3Endpoint;
        S3BucketBox.Text = settings.S3Bucket;
        S3RegionBox.Text = settings.S3Region;
        S3PrefixBox.Text = settings.S3KeyPrefix;
        S3PublicBaseUrlBox.Text = settings.S3PublicBaseUrl;
        S3KeyStatusText.Text = _services.SecretStore.HasS3Credentials
            ? "S3-compatible upload credentials are saved with Windows DPAPI for the current user."
            : "No S3-compatible upload credentials are saved.";
        ImgurEndpointBox.Text = settings.ImgurApiEndpoint;
        ImgurKeyStatusText.Text = _services.SecretStore.HasImgurClientId
            ? "An Imgur Client ID is saved with Windows DPAPI for the current user."
            : "No Imgur Client ID is saved.";
        SftpHostBox.Text = settings.SftpHost;
        SftpPortBox.Text = settings.SftpPort.ToString();
        SftpUsernameBox.Text = settings.SftpUsername;
        SftpRemoteDirectoryBox.Text = settings.SftpRemoteDirectory;
        SftpPrivateKeyPathBox.Text = settings.SftpPrivateKeyPath;
        SftpHostKeyFingerprintBox.Text = settings.SftpHostKeyFingerprint;
        SftpPublicBaseUrlBox.Text = settings.SftpPublicBaseUrl;
        WebDavBaseUrlBox.Text = settings.WebDavBaseUrl;
        WebDavRemoteDirectoryBox.Text = settings.WebDavRemoteDirectory;
        WebDavUsernameBox.Text = settings.WebDavUsername;
        WebDavPublicBaseUrlBox.Text = settings.WebDavPublicBaseUrl;
        WebDavPasswordStatusText.Text = _services.SecretStore.HasWebDavPassword
            ? "A WebDAV password is saved with Windows DPAPI for the current user."
            : "No WebDAV password is saved.";
        FtpHostBox.Text = settings.FtpHost;
        FtpPortBox.Text = settings.FtpPort.ToString(CultureInfo.InvariantCulture);
        FtpUseFtpsBox.IsChecked = settings.FtpUseFtps;
        FtpUsernameBox.Text = settings.FtpUsername;
        FtpRemoteDirectoryBox.Text = settings.FtpRemoteDirectory;
        FtpPublicBaseUrlBox.Text = settings.FtpPublicBaseUrl;
        FtpPasswordStatusText.Text = _services.SecretStore.HasFtpPassword
            ? "An FTP/FTPS password is saved with Windows DPAPI for the current user."
            : "No FTP/FTPS password is saved.";
        CloudinaryApiBaseUrlBox.Text = settings.CloudinaryApiBaseUrl;
        CloudinaryCloudNameBox.Text = settings.CloudinaryCloudName;
        CloudinaryResourceTypeBox.Text = settings.CloudinaryResourceType;
        CloudinaryUploadPresetBox.Text = settings.CloudinaryUploadPreset;
        CloudinaryFolderBox.Text = settings.CloudinaryFolder;
        CloudinaryKeyStatusText.Text = _services.SecretStore.HasCloudinaryCredentials
            ? "Cloudinary API key/secret credentials are saved with Windows DPAPI for the current user."
            : "No Cloudinary API key/secret credentials are saved. Unsigned upload can still work with an upload preset.";
        DropboxContentApiBaseUrlBox.Text = settings.DropboxContentApiBaseUrl;
        DropboxApiBaseUrlBox.Text = settings.DropboxApiBaseUrl;
        DropboxRemoteFolderBox.Text = settings.DropboxRemoteFolder;
        DropboxOAuthClientIdBox.Text = FindOAuthProvider("Dropbox")?.ClientId ?? string.Empty;
        DropboxTokenStatusText.Text = FormatOAuthCredentialStatus(
            "Dropbox",
            _services.SecretStore.HasDropboxAccessToken,
            "Dropbox OAuth access token");
        GoogleDriveUploadApiBaseUrlBox.Text = settings.GoogleDriveUploadApiBaseUrl;
        GoogleDriveApiBaseUrlBox.Text = settings.GoogleDriveApiBaseUrl;
        GoogleDriveFolderIdBox.Text = settings.GoogleDriveFolderId;
        GoogleDriveAnyoneReaderBox.IsChecked = settings.GoogleDriveCreateAnyoneReaderLink;
        GoogleDriveOAuthClientIdBox.Text = FindOAuthProvider("Google Drive")?.ClientId ?? string.Empty;
        GoogleDriveTokenStatusText.Text = FormatOAuthCredentialStatus(
            "Google Drive",
            _services.SecretStore.HasGoogleDriveAccessToken,
            "Google Drive OAuth access token");
        GooglePhotosUploadApiBaseUrlBox.Text = settings.GooglePhotosUploadApiBaseUrl;
        GooglePhotosApiBaseUrlBox.Text = settings.GooglePhotosApiBaseUrl;
        GooglePhotosAlbumIdBox.Text = settings.GooglePhotosAlbumId;
        GooglePhotosDescriptionTemplateBox.Text = settings.GooglePhotosDescriptionTemplate;
        GooglePhotosOAuthClientIdBox.Text = FindOAuthProvider("Google Photos")?.ClientId ?? string.Empty;
        GooglePhotosTokenStatusText.Text = FormatOAuthCredentialStatus(
            "Google Photos",
            _services.SecretStore.HasGooglePhotosAccessToken,
            "Google Photos OAuth access token");
        OneDriveGraphApiBaseUrlBox.Text = settings.OneDriveGraphApiBaseUrl;
        OneDriveRemoteFolderBox.Text = settings.OneDriveRemoteFolder;
        OneDriveAnonymousViewLinkBox.IsChecked = settings.OneDriveCreateAnonymousViewLink;
        OneDriveOAuthClientIdBox.Text = FindOAuthProvider("OneDrive")?.ClientId ?? string.Empty;
        OneDriveTokenStatusText.Text = FormatOAuthCredentialStatus(
            "OneDrive",
            _services.SecretStore.HasOneDriveAccessToken,
            "OneDrive Microsoft Graph OAuth access token");
        YouTubeUploadApiBaseUrlBox.Text = settings.YouTubeUploadApiBaseUrl;
        YouTubeTitleTemplateBox.Text = settings.YouTubeTitleTemplate;
        YouTubeDescriptionTemplateBox.Text = settings.YouTubeDescriptionTemplate;
        SelectComboBoxValue(YouTubePrivacyStatusBox, string.IsNullOrWhiteSpace(settings.YouTubePrivacyStatus) ? "unlisted" : settings.YouTubePrivacyStatus);
        YouTubeCategoryIdBox.Text = settings.YouTubeCategoryId;
        YouTubeOAuthClientIdBox.Text = FindOAuthProvider("YouTube")?.ClientId ?? string.Empty;
        YouTubeTokenStatusText.Text = FormatOAuthCredentialStatus(
            "YouTube",
            _services.SecretStore.HasYouTubeAccessToken,
            "YouTube OAuth access token");
        OneNoteGraphApiBaseUrlBox.Text = settings.OneNoteGraphApiBaseUrl;
        OneNoteSectionIdBox.Text = settings.OneNoteSectionId;
        OneNotePageTitleTemplateBox.Text = settings.OneNotePageTitleTemplate;
        OneNoteOAuthClientIdBox.Text = FindOAuthProvider("OneNote")?.ClientId ?? string.Empty;
        OneNoteTokenStatusText.Text = FormatOAuthCredentialStatus(
            "OneNote",
            _services.SecretStore.HasOneNoteAccessToken,
            "OneNote Microsoft Graph OAuth access token");
        LinearGraphqlEndpointBox.Text = settings.LinearGraphqlEndpoint;
        LinearTeamIdBox.Text = settings.LinearTeamId;
        LinearIssueIdBox.Text = settings.LinearIssueId;
        LinearIssueTitleTemplateBox.Text = settings.LinearIssueTitleTemplate;
        LinearCreateAttachmentBox.IsChecked = settings.LinearCreateAttachment;
        LinearUseOAuthBearerTokenBox.IsChecked = settings.LinearUseOAuthBearerToken;
        LinearCredentialStatusText.Text = _services.SecretStore.HasLinearCredential
            ? "A Linear API key or OAuth access token is saved with Windows DPAPI for the current user."
            : "No Linear API key or OAuth access token is saved.";
        GitHubApiBaseUrlBox.Text = settings.GitHubApiBaseUrl;
        GitHubRepositoryBox.Text = settings.GitHubRepository;
        GitHubIssueTitleTemplateBox.Text = settings.GitHubIssueTitleTemplate;
        GitHubLabelsBox.Text = settings.GitHubLabels;
        GitHubAssigneesBox.Text = settings.GitHubAssignees;
        GitHubTokenStatusText.Text = _services.SecretStore.HasGitHubToken
            ? "A GitHub personal access token is saved with Windows DPAPI for the current user."
            : "No GitHub personal access token is saved.";
        JiraBaseUrlBox.Text = settings.JiraBaseUrl;
        JiraProjectKeyBox.Text = settings.JiraProjectKey;
        JiraIssueTypeBox.Text = settings.JiraIssueType;
        JiraSummaryTemplateBox.Text = settings.JiraSummaryTemplate;
        JiraLabelsBox.Text = settings.JiraLabels;
        JiraAccountEmailBox.Text = settings.JiraAccountEmail;
        JiraApiTokenStatusText.Text = _services.SecretStore.HasJiraApiToken
            ? "A Jira API token is saved with Windows DPAPI for the current user."
            : "No Jira API token is saved.";
        AzureDevOpsBaseUrlBox.Text = settings.AzureDevOpsBaseUrl;
        AzureDevOpsOrganizationBox.Text = settings.AzureDevOpsOrganization;
        AzureDevOpsProjectBox.Text = settings.AzureDevOpsProject;
        AzureDevOpsWorkItemTypeBox.Text = settings.AzureDevOpsWorkItemType;
        AzureDevOpsTitleTemplateBox.Text = settings.AzureDevOpsTitleTemplate;
        AzureDevOpsTagsBox.Text = settings.AzureDevOpsTags;
        AzureDevOpsAssignedToBox.Text = settings.AzureDevOpsAssignedTo;
        AzureDevOpsPatStatusText.Text = _services.SecretStore.HasAzureDevOpsPat
            ? "An Azure DevOps personal access token is saved with Windows DPAPI for the current user."
            : "No Azure DevOps personal access token is saved.";
        UploadDenylistBox.Text = string.Join(Environment.NewLine, settings.UploadDenylistProcesses);
        AiDenylistBox.Text = string.Join(Environment.NewLine, settings.AiDenylistProcesses);
        EnableWatchFoldersBox.IsChecked = settings.EnableWatchFolders;
        WatchSubdirectoriesBox.IsChecked = settings.WatchFolderIncludeSubdirectories;
        WatchAutoImportBox.IsChecked = settings.WatchFolderAutoImport;
        WatchCopyAfterImportBox.IsChecked = settings.WatchFolderCopyImageAfterImport;
        WatchStripMetadataBox.IsChecked = settings.WatchFolderStripMetadataAfterImport;
        WatchShareAfterImportBox.IsChecked = settings.WatchFolderShareAfterImport;
        WatchFoldersBox.Text = string.Join(Environment.NewLine, settings.WatchFolders);
        LoadAutomationRules(settings);

        SelectComboBoxItem(DefaultShareBox, settings.DefaultShareDestination);
        DefaultShareBox.SelectedIndex = DefaultShareBox.SelectedIndex < 0 ? 0 : DefaultShareBox.SelectedIndex;
        RefreshProviderSetupSummary();
        RefreshRecordingSummaryTiles();
        RefreshAutomationSummaryTiles();
    }

    private void RefreshProviderSetupSummary()
    {
        var summary = new ProviderSetupSummaryService()
            .Create(_services.ProviderDiagnostics.GetDiagnostics());
        ProviderSetupHeadlineText.Text = summary.Headline;
        ProviderSetupDetailText.Text = summary.Detail;
        ProviderSetupSummaryGrid.Children.Clear();
        foreach (var card in summary.Cards)
        {
            ProviderSetupSummaryGrid.Children.Add(CreateProviderSetupCard(card));
        }
    }

    private Border CreateProviderSetupCard(ProviderSetupSummaryCard card)
    {
        return CreateSummaryTileCard(card.Title, card.CountLabel, card.Detail, card.Tone);
    }

    private Border CreateSummaryTileCard(string title, string value, string detail, string tone)
    {
        var border = new Border
        {
            Background = BrushFromHex("#101B23"),
            BorderBrush = CardBrush(tone),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 92
        };
        System.Windows.Automation.AutomationProperties.SetName(
            border,
            $"{title}: {value}. {detail}");

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = (MediaBrush)FindResource("MutedInkBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 2)
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            Foreground = (MediaBrush)FindResource("MutedInkBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        border.Child = stack;
        return border;
    }

    private void RefreshRecordingSummaryTiles()
    {
        var draft = new RecordingSettings();
        ApplyRecordingSettings(draft);
        RecordingSummaryGrid.Children.Clear();
        foreach (var tile in _recordingSummaryTiles.Create(draft))
        {
            RecordingSummaryGrid.Children.Add(CreateSummaryTileCard(tile.Title, tile.Value, tile.Detail, tile.Tone));
        }
    }

    private void RecordingSummaryInput_Changed(object sender, RoutedEventArgs e)
    {
        if (!CanRefreshSummaryTiles)
        {
            return;
        }

        RefreshRecordingSummaryTiles();
    }

    private void RefreshAutomationSummaryTiles()
    {
        var tiles = _automationSummaryTiles.Create(
            _automationRuleDrafts,
            EnableWatchFoldersBox.IsChecked == true,
            SplitLines(WatchFoldersBox.Text));
        AutomationSummaryGrid.Children.Clear();
        foreach (var tile in tiles)
        {
            AutomationSummaryGrid.Children.Add(CreateSummaryTileCard(tile.Title, tile.Value, tile.Detail, tile.Tone));
        }
    }

    private void AutomationSummaryInput_Changed(object sender, RoutedEventArgs e)
    {
        if (!CanRefreshSummaryTiles)
        {
            return;
        }

        RefreshAutomationSummaryTiles();
    }

    private MediaBrush CardBrush(string tone)
    {
        return tone switch
        {
            "Ready" => (MediaBrush)FindResource("AccentBrush"),
            "Blocked" or "OAuth" => (MediaBrush)FindResource("WarnBrush"),
            "NeedsConfiguration" => BrushFromHex("#7BA7FF"),
            _ => (MediaBrush)FindResource("BorderBrush")
        };
    }

    private static SolidColorBrush BrushFromHex(string value)
    {
        return new SolidColorBrush((MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value));
    }

    private void InitializePluginUpdateSurface()
    {
        var registry = ResolveDefaultPluginRegistryPath();
        PluginUpdateRegistryBox.Text = registry;
        _lastPluginUpdateCliCommand = new PluginUpdateSurfaceService()
            .Create(new RemotePluginUpdateSummaryResult(), registry)
            .CliCommand;
        PluginUpdateStatusText.Text = "Choose a plugin registry and refresh for a passive update summary. No plugin will be installed, trusted, enabled, allowlisted, or executed.";
        PluginUpdateCountsText.Text = "Available 0 | Staged 0 | Installed 0 | Blocked 0 | Incompatible 0";
        PluginUpdateSummaryList.Items.Clear();
        PluginUpdateSummaryList.Items.Add("No update summary loaded yet.");
        PluginUpdateNextActionsText.Text = "Safe actions here are refresh, copy CLI command, and open the staging folder for review.";
    }

    private async Task RefreshPluginUpdatesForDefaultRegistryAsync()
    {
        var registry = PluginUpdateRegistryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(registry))
        {
            return;
        }

        var candidate = Path.GetFullPath(Environment.ExpandEnvironmentVariables(registry));
        if (!File.Exists(candidate))
        {
            return;
        }

        await RefreshPluginUpdatesAsync();
    }

    private async void RefreshPluginUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPluginUpdatesAsync();
    }

    private async Task RefreshPluginUpdatesAsync()
    {
        var registry = PluginUpdateRegistryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(registry))
        {
            PluginUpdateStatusText.Text = "Enter a plugin registry JSON path or URL before refreshing.";
            return;
        }

        PluginUpdateStatusText.Text = "Checking plugin registry without installing, trusting, enabling, allowlisting, or executing plugin code...";
        try
        {
            var result = await _services.RemotePlugins.SummarizeUpdatesAsync(registry);
            var surface = new PluginUpdateSurfaceService().Create(result, registry);
            ApplyPluginUpdateSurface(surface);
        }
        catch (Exception exception)
        {
            PluginUpdateStatusText.Text = $"Plugin update check failed: {exception.Message}";
            PluginUpdateCountsText.Text = "Available 0 | Staged 0 | Installed 0 | Blocked 0 | Incompatible 0";
            PluginUpdateSummaryList.Items.Clear();
            PluginUpdateSummaryList.Items.Add("Update summary unavailable.");
            PluginUpdateNextActionsText.Text = "Validate the registry path or URL with `receipts plugins registry validate` before retrying.";
        }
    }

    private void ApplyPluginUpdateSurface(PluginUpdateSurfaceSummary surface)
    {
        _lastPluginUpdateCliCommand = surface.CliCommand;
        var issues = surface.Issues.Count == 0
            ? string.Empty
            : " Issues: " + string.Join(" ", surface.Issues);
        var warnings = surface.Warnings.Count == 0
            ? string.Empty
            : " Warnings: " + string.Join(" ", surface.Warnings);
        PluginUpdateStatusText.Text = $"{surface.Message} Registry: {surface.RegistryLocation}.{issues}{warnings}";
        PluginUpdateCountsText.Text = $"{surface.CountsText} | {surface.MutationBoundary}";
        PluginUpdateSummaryList.Items.Clear();
        if (surface.Rows.Count == 0)
        {
            PluginUpdateSummaryList.Items.Add("No registry plugin entries to show.");
        }
        else
        {
            foreach (var row in surface.Rows)
            {
                PluginUpdateSummaryList.Items.Add(row.ToString());
            }
        }

        PluginUpdateNextActionsText.Text = surface.NextActions.Count == 0
            ? "No follow-up actions reported."
            : string.Join(Environment.NewLine, surface.NextActions.Select(action => "- " + action));
    }

    private void CopyPluginUpdatesCli_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardInterop.SetText(_lastPluginUpdateCliCommand);
            PluginUpdateStatusText.Text = $"Copied CLI command. {PluginUpdateStatusText.Text}";
        }
        catch (Exception exception)
        {
            PluginUpdateStatusText.Text = $"Could not copy plugin update CLI command: {exception.Message}";
        }
    }

    private void OpenPluginStagingFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_services.Paths.PluginStagingRoot);
            Process.Start(new ProcessStartInfo(_services.Paths.PluginStagingRoot)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            PluginUpdateStatusText.Text = $"Could not open plugin staging folder: {exception.Message}";
        }
    }

    private static string ResolveDefaultPluginRegistryPath()
    {
        var current = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "samples", "local-plugins", "registry.json"));
        if (File.Exists(current))
        {
            return current;
        }

        var packaged = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples", "local-plugins", "registry.json"));
        return File.Exists(packaged)
            ? packaged
            : "samples\\local-plugins\\registry.json";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentSettingsFromControls();
        var settings = _services.Settings;

        bool startupSucceeded;
        string startupMessage;
        if (settings.RunAtStartup && !File.Exists(_services.PersonalInstall.InstalledExecutablePath))
        {
            var installResult = _services.PersonalInstall.InstallOrUpdate();
            startupSucceeded = installResult.Succeeded;
            startupMessage = installResult.Message;
        }
        else
        {
            var registrationResult = _services.Startup.SetEnabled(settings.RunAtStartup, _services.PersonalInstall.InstalledExecutablePath);
            startupSucceeded = registrationResult.Succeeded;
            startupMessage = registrationResult.Message;
        }

        if (!startupSucceeded)
        {
            System.Windows.MessageBox.Show(
                startupMessage,
                "Startup registration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            settings.RunAtStartup = _services.Startup.GetState(_services.PersonalInstall.InstalledExecutablePath).IsRegistered;
        }

        _services.SaveSettings();
        _services.Hotkeys.Reload(settings.Keybinds);
        ClearSettingsDirty();
        DialogResult = true;
    }

    private async void InstallOrRepair_Click(object sender, RoutedEventArgs e)
    {
        var install = _services.PersonalInstall.InstallOrUpdate();
        var runtime = await _services.BundledTools.RepairAsync();
        RefreshPersonalInstallStatus();
        System.Windows.MessageBox.Show(
            $"{install.Message}{Environment.NewLine}{runtime.Message}",
            "Receipts install and repair",
            MessageBoxButton.OK,
            install.Succeeded && runtime.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void DisableStartup_Click(object sender, RoutedEventArgs e)
    {
        var result = _services.PersonalInstall.DisableStartup();
        RunAtStartupBox.IsChecked = false;
        RefreshPersonalInstallStatus();
        System.Windows.MessageBox.Show(result.Message, "Receipts startup", MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = System.Windows.MessageBox.Show(
            "Remove the installed Receipts program, startup registration, extracted runtime tools, and browser native-host registrations? Captures and settings will be preserved.",
            "Uninstall Receipts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _services.BrowserNativeHosts.Uninstall();
        var result = _services.PersonalInstall.BeginUninstall();
        System.Windows.MessageBox.Show(result.Message, "Uninstall Receipts", MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (result.Succeeded)
        {
            System.Windows.Application.Current.Shutdown(0);
        }
    }

    private void RefreshPersonalInstallStatus()
    {
        var install = _services.PersonalInstall.GetState();
        var runtime = _services.BundledTools.ValidateExisting();
        var segmentation = _services.PersonSegmentation.GetStatus();
        var ffmpegAsset = runtime.Manifest.Assets.FirstOrDefault(asset => asset.Id == "ffmpeg");
        var whisper = string.IsNullOrWhiteSpace(_services.Settings.ExternalWhisperExecutablePath)
            ? "external Whisper not configured"
            : File.Exists(Environment.ExpandEnvironmentVariables(_services.Settings.ExternalWhisperExecutablePath))
                ? "external Whisper configured"
                : "external Whisper path missing";

        var rows = PersonalInstallStatusPresenter.Build(
            install,
            install.InstalledVersion.Length > 0 && _services.PersonalInstall.IsNewerThanInstalled(),
            runtime.Succeeded,
            runtime.Message,
            ffmpegAsset?.Version,
            segmentation.Available,
            segmentation.Message,
            _services.Settings.TranscriptionProvider,
            whisper);
        RenderInstallStatusRows(rows);
    }

    private void RenderInstallStatusRows(IReadOnlyList<InstallStatusRow> rows)
    {
        PersonalInstallStatusGrid.Children.Clear();
        PersonalInstallStatusGrid.RowDefinitions.Clear();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            PersonalInstallStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Label,
                Foreground = (MediaBrush)FindResource("MutedInkBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 3, 12, 3),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(label, index);
            Grid.SetColumn(label, 0);
            PersonalInstallStatusGrid.Children.Add(label);

            var value = new TextBlock
            {
                Text = row.Value,
                Foreground = InstallStatusBrush(row.Tone),
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap
            };
            // Announce label and value together so the row is meaningful to a screen reader.
            System.Windows.Automation.AutomationProperties.SetName(value, $"{row.Label}: {row.Value}");
            Grid.SetRow(value, index);
            Grid.SetColumn(value, 1);
            PersonalInstallStatusGrid.Children.Add(value);
        }
    }

    private MediaBrush InstallStatusBrush(InstallStatusTone tone) => tone switch
    {
        InstallStatusTone.Ok => (MediaBrush)FindResource("AccentBrush"),
        InstallStatusTone.Attention => (MediaBrush)FindResource("WarnBrush"),
        _ => (MediaBrush)FindResource("InkBrush")
    };

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // Explains why an upgraded install still points at a folder named for the old brand, and offers
    // the move rather than performing it silently.
    private void RefreshLegacyLibraryNotice()
    {
        var plan = LegacyLibraryRelocationService.Describe(LibraryRootBox.Text);
        if (plan is null)
        {
            LegacyLibraryNotice.Visibility = Visibility.Collapsed;
            return;
        }

        LegacyLibraryNoticeText.Text =
            $"This folder is still named {BrandIdentity.LegacyLibraryDirectoryName}, from before the app was " +
            $"renamed to {BrandIdentity.ProductName}. Your captures are fine where they are. " +
            $"You can move them to {plan.Target} if you would rather the names matched.";
        LegacyLibraryNotice.Visibility = Visibility.Visible;
    }

    private void MoveLegacyLibrary_Click(object sender, RoutedEventArgs e)
    {
        var plan = LegacyLibraryRelocationService.Describe(LibraryRootBox.Text);
        if (plan is null)
        {
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            this,
            plan.Prompt,
            "Move capture library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var result = LegacyLibraryRelocationService.Relocate(plan);
        if (result.Succeeded)
        {
            LibraryRootBox.Text = result.LibraryRoot;
            RefreshLegacyLibraryNotice();
        }

        System.Windows.MessageBox.Show(
            this,
            result.Succeeded
                ? $"{result.Message} Save to apply the new location."
                : result.Message,
            "Move capture library",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static List<string> SplitLines(string text)
    {
        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GeminiApiKeyBox.Password))
        {
            GeminiKeyStatusText.Text = "Enter a Gemini API key before saving.";
            return;
        }

        _services.SecretStore.SaveGeminiApiKey(GeminiApiKeyBox.Password);
        GeminiApiKeyBox.Clear();
        GeminiKeyStatusText.Text = "Gemini API key saved with Windows DPAPI for the current user.";
    }

    private void ClearGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearGeminiApiKey();
        GeminiApiKeyBox.Clear();
        GeminiKeyStatusText.Text = "Gemini API key cleared.";
    }

    private void SaveS3Keys_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(S3AccessKeyBox.Password) ||
            string.IsNullOrWhiteSpace(S3SecretKeyBox.Password))
        {
            S3KeyStatusText.Text = "Enter both S3 access key ID and secret access key before saving.";
            return;
        }

        _services.SecretStore.SaveS3Credentials(S3AccessKeyBox.Password, S3SecretKeyBox.Password);
        S3AccessKeyBox.Clear();
        S3SecretKeyBox.Clear();
        S3KeyStatusText.Text = "S3-compatible upload credentials saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearS3Keys_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearS3Credentials();
        S3AccessKeyBox.Clear();
        S3SecretKeyBox.Clear();
        S3KeyStatusText.Text = "S3-compatible upload credentials cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveImgurClientId_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ImgurClientIdBox.Password))
        {
            ImgurKeyStatusText.Text = "Enter an Imgur Client ID before saving.";
            return;
        }

        _services.SecretStore.SaveImgurClientId(ImgurClientIdBox.Password);
        ImgurClientIdBox.Clear();
        ImgurKeyStatusText.Text = "Imgur Client ID saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearImgurClientId_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearImgurClientId();
        ImgurClientIdBox.Clear();
        ImgurKeyStatusText.Text = "Imgur Client ID cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveCloudinaryKeys_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CloudinaryApiKeyBox.Password) ||
            string.IsNullOrWhiteSpace(CloudinaryApiSecretBox.Password))
        {
            CloudinaryKeyStatusText.Text = "Enter both Cloudinary API key and API secret before saving.";
            return;
        }

        _services.SecretStore.SaveCloudinaryCredentials(CloudinaryApiKeyBox.Password, CloudinaryApiSecretBox.Password);
        CloudinaryApiKeyBox.Clear();
        CloudinaryApiSecretBox.Clear();
        CloudinaryKeyStatusText.Text = "Cloudinary API key/secret credentials saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearCloudinaryKeys_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearCloudinaryCredentials();
        CloudinaryApiKeyBox.Clear();
        CloudinaryApiSecretBox.Clear();
        CloudinaryKeyStatusText.Text = "Cloudinary API key/secret credentials cleared. Unsigned upload can still work with an upload preset.";
        RefreshProviderSetupSummary();
    }

    private void SaveDropboxToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DropboxAccessTokenBox.Password))
        {
            DropboxTokenStatusText.Text = "Enter a Dropbox OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveDropboxAccessToken(DropboxAccessTokenBox.Password);
        DropboxAccessTokenBox.Clear();
        DropboxTokenStatusText.Text = "Dropbox OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearDropboxToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("Dropbox");
        DropboxAccessTokenBox.Clear();
        DropboxTokenStatusText.Text = "Dropbox OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectDropboxOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("Dropbox", DropboxOAuthClientIdBox, DropboxTokenStatusText);
    }

    private void SaveGoogleDriveToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GoogleDriveAccessTokenBox.Password))
        {
            GoogleDriveTokenStatusText.Text = "Enter a Google Drive OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveGoogleDriveAccessToken(GoogleDriveAccessTokenBox.Password);
        GoogleDriveAccessTokenBox.Clear();
        GoogleDriveTokenStatusText.Text = "Google Drive OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearGoogleDriveToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("Google Drive");
        GoogleDriveAccessTokenBox.Clear();
        GoogleDriveTokenStatusText.Text = "Google Drive OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectGoogleDriveOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("Google Drive", GoogleDriveOAuthClientIdBox, GoogleDriveTokenStatusText);
    }

    private void SaveGooglePhotosToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GooglePhotosAccessTokenBox.Password))
        {
            GooglePhotosTokenStatusText.Text = "Enter a Google Photos OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveGooglePhotosAccessToken(GooglePhotosAccessTokenBox.Password);
        GooglePhotosAccessTokenBox.Clear();
        GooglePhotosTokenStatusText.Text = "Google Photos OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearGooglePhotosToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("Google Photos");
        GooglePhotosAccessTokenBox.Clear();
        GooglePhotosTokenStatusText.Text = "Google Photos OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectGooglePhotosOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("Google Photos", GooglePhotosOAuthClientIdBox, GooglePhotosTokenStatusText);
    }

    private void SaveOneDriveToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OneDriveAccessTokenBox.Password))
        {
            OneDriveTokenStatusText.Text = "Enter a OneDrive Microsoft Graph OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveOneDriveAccessToken(OneDriveAccessTokenBox.Password);
        OneDriveAccessTokenBox.Clear();
        OneDriveTokenStatusText.Text = "OneDrive Microsoft Graph OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearOneDriveToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("OneDrive");
        OneDriveAccessTokenBox.Clear();
        OneDriveTokenStatusText.Text = "OneDrive Microsoft Graph OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectOneDriveOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("OneDrive", OneDriveOAuthClientIdBox, OneDriveTokenStatusText);
    }

    private void SaveYouTubeToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(YouTubeAccessTokenBox.Password))
        {
            YouTubeTokenStatusText.Text = "Enter a YouTube OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveYouTubeAccessToken(YouTubeAccessTokenBox.Password);
        YouTubeAccessTokenBox.Clear();
        YouTubeTokenStatusText.Text = "YouTube OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearYouTubeToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("YouTube");
        YouTubeAccessTokenBox.Clear();
        YouTubeTokenStatusText.Text = "YouTube OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectYouTubeOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("YouTube", YouTubeOAuthClientIdBox, YouTubeTokenStatusText);
    }

    private void SaveOneNoteToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OneNoteAccessTokenBox.Password))
        {
            OneNoteTokenStatusText.Text = "Enter a OneNote Microsoft Graph OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveOneNoteAccessToken(OneNoteAccessTokenBox.Password);
        OneNoteAccessTokenBox.Clear();
        OneNoteTokenStatusText.Text = "OneNote Microsoft Graph OAuth access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearOneNoteToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearOAuthToken("OneNote");
        OneNoteAccessTokenBox.Clear();
        OneNoteTokenStatusText.Text = "OneNote OAuth access token cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ConnectOneNoteOAuth_Click(object sender, RoutedEventArgs e)
    {
        await ConnectOAuthProviderAsync("OneNote", OneNoteOAuthClientIdBox, OneNoteTokenStatusText);
    }

    private void SaveLinearCredential_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LinearCredentialBox.Password))
        {
            LinearCredentialStatusText.Text = "Enter a Linear API key or OAuth access token before saving.";
            return;
        }

        _services.SecretStore.SaveLinearCredential(LinearCredentialBox.Password);
        LinearCredentialBox.Clear();
        LinearCredentialStatusText.Text = "Linear credential saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearLinearCredential_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearLinearCredential();
        LinearCredentialBox.Clear();
        LinearCredentialStatusText.Text = "Linear credential cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveGitHubToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GitHubTokenBox.Password))
        {
            GitHubTokenStatusText.Text = "Enter a GitHub personal access token before saving.";
            return;
        }

        _services.SecretStore.SaveGitHubToken(GitHubTokenBox.Password);
        GitHubTokenBox.Clear();
        GitHubTokenStatusText.Text = "GitHub personal access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearGitHubToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearGitHubToken();
        GitHubTokenBox.Clear();
        GitHubTokenStatusText.Text = "GitHub personal access token cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveJiraApiToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(JiraApiTokenBox.Password))
        {
            JiraApiTokenStatusText.Text = "Enter a Jira API token before saving.";
            return;
        }

        _services.SecretStore.SaveJiraApiToken(JiraApiTokenBox.Password);
        JiraApiTokenBox.Clear();
        JiraApiTokenStatusText.Text = "Jira API token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearJiraApiToken_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearJiraApiToken();
        JiraApiTokenBox.Clear();
        JiraApiTokenStatusText.Text = "Jira API token cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveAzureDevOpsPat_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AzureDevOpsPatBox.Password))
        {
            AzureDevOpsPatStatusText.Text = "Enter an Azure DevOps personal access token before saving.";
            return;
        }

        _services.SecretStore.SaveAzureDevOpsPat(AzureDevOpsPatBox.Password);
        AzureDevOpsPatBox.Clear();
        AzureDevOpsPatStatusText.Text = "Azure DevOps personal access token saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearAzureDevOpsPat_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearAzureDevOpsPat();
        AzureDevOpsPatBox.Clear();
        AzureDevOpsPatStatusText.Text = "Azure DevOps personal access token cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveWebDavPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WebDavPasswordBox.Password))
        {
            WebDavPasswordStatusText.Text = "Enter a WebDAV password before saving.";
            return;
        }

        _services.SecretStore.SaveWebDavPassword(WebDavPasswordBox.Password);
        WebDavPasswordBox.Clear();
        WebDavPasswordStatusText.Text = "WebDAV password saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearWebDavPassword_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearWebDavPassword();
        WebDavPasswordBox.Clear();
        WebDavPasswordStatusText.Text = "WebDAV password cleared.";
        RefreshProviderSetupSummary();
    }

    private void SaveFtpPassword_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FtpPasswordBox.Password))
        {
            FtpPasswordStatusText.Text = "Enter an FTP/FTPS password before saving.";
            return;
        }

        _services.SecretStore.SaveFtpPassword(FtpPasswordBox.Password);
        FtpPasswordBox.Clear();
        FtpPasswordStatusText.Text = "FTP/FTPS password saved with Windows DPAPI for the current user.";
        RefreshProviderSetupSummary();
    }

    private void ClearFtpPassword_Click(object sender, RoutedEventArgs e)
    {
        _services.SecretStore.ClearFtpPassword();
        FtpPasswordBox.Clear();
        FtpPasswordStatusText.Text = "FTP/FTPS password cleared.";
        RefreshProviderSetupSummary();
    }

    private async void ValidateGemini_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentSettingsWithoutClosing();
        GeminiKeyStatusText.Text = "Checking Gemini credentials and model list...";
        var health = await _services.Gemini.ValidateCredentialsAsync(CancellationToken.None);
        var models = await _services.Gemini.ListModelsAsync(CancellationToken.None);
        var imageModels = models.Where(model => model.SupportsImageEdit).Take(6).Select(model => model.Id);
        GeminiKeyStatusText.Text = $"{health.Message}{Environment.NewLine}Image-capable models: {string.Join(", ", imageModels)}";
    }

    private void SaveCurrentSettingsWithoutClosing()
    {
        var settings = _services.Settings;
        settings.AiEnabled = AiEnabledBox.IsChecked == true;
        settings.ConfirmBeforeAiRequest = ConfirmAiBox.IsChecked == true;
        settings.GeminiApiEndpoint = string.IsNullOrWhiteSpace(GeminiEndpointBox.Text)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : GeminiEndpointBox.Text.Trim();
        settings.GeminiDefaultModelId = string.IsNullOrWhiteSpace(GeminiDefaultModelBox.Text)
            ? "gemini-3.1-flash-image"
            : GeminiDefaultModelBox.Text.Trim();
        settings.GeminiSpeechToTextModelId = string.IsNullOrWhiteSpace(GeminiSpeechModelBox.Text)
            ? "gemini-3.5-flash"
            : GeminiSpeechModelBox.Text.Trim();
        settings.TranscriptionProvider = TranscriptionProviderBox.SelectedValue as string ?? "external-whisper";
        settings.ExternalWhisperExecutablePath = ExternalWhisperExecutableBox.Text.Trim();
        settings.ExternalWhisperModelPath = ExternalWhisperModelBox.Text.Trim();
        settings.ExternalWhisperLanguage = string.IsNullOrWhiteSpace(ExternalWhisperLanguageBox.Text)
            ? "en"
            : ExternalWhisperLanguageBox.Text.Trim();
        settings.GeminiModelManifestPath = GeminiManifestBox.Text.Trim();
        _services.SaveSettings();
        RefreshProviderSetupSummary();
    }

    private async void ExportWorkflowProfile_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentSettingsFromControls();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Receipts workflow profile",
            Filter = "Receipts workflow profile (*.receipts-workflow.json)|*.receipts-workflow.json|Legacy GoatShot workflow profile (*.goatshot-workflow.json)|*.goatshot-workflow.json|JSON files (*.json)|*.json",
            FileName = "receipts-workflow.receipts-workflow.json",
            AddExtension = true,
            DefaultExt = ".receipts-workflow.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = await _services.WorkflowProfiles.ExportAsync(
            dialog.FileName,
            "Receipts workflow profile",
            includeSensitiveValues: false);
        System.Windows.MessageBox.Show(
            result.Message,
            "Workflow profile export",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void ImportWorkflowProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Receipts workflow profile",
            Filter = "Receipts workflow profile (*.receipts-workflow.json)|*.receipts-workflow.json|Legacy GoatShot workflow profile (*.goatshot-workflow.json)|*.goatshot-workflow.json|JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = await _services.WorkflowProfiles.ImportAsync(
            dialog.FileName,
            new WorkflowProfileImportOptions
            {
                IncludeSensitiveValues = false,
                ReplaceAutomationRules = true
            });

        LoadSettings();
        _services.WatchFolders.Restart();
        System.Windows.MessageBox.Show(
            result.Message,
            "Workflow profile import",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ApplyCurrentSettingsFromControls()
    {
        var settings = _services.Settings;
        settings.LibraryRoot = LibraryRootBox.Text.Trim();
        settings.FileNameTemplate = FileNameTemplateBox.Text.Trim();
        settings.AutoCopyImageAfterCapture = CopyAfterCaptureBox.IsChecked == true;
        settings.PostCaptureAction = PostCaptureActionCatalog.Normalize(SelectedComboBoxTag(PostCaptureActionBox));
        settings.CaptureActionsAutoDismissSeconds = CaptureTaskAutoDismiss.NormalizeSeconds(
            int.TryParse(
                CaptureActionsAutoDismissBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var autoDismissSeconds)
                ? autoDismissSeconds
                : CaptureTaskAutoDismiss.DefaultSeconds);
        settings.EnableCaptureHoverAutoSelect = HoverAutoSelectBox.IsChecked == true;
        settings.IncludeCursor = IncludeCursorBox.IsChecked == true;
        settings.CaptureContextPadding = int.TryParse(
            CaptureContextPaddingBox.Text.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var captureContextPadding)
            ? Math.Clamp(captureContextPadding, 0, CaptureOverlayGeometry.MaxContextPadding)
            : 0;
        settings.PrivateCaptureMode = PrivateModeBox.IsChecked == true;
        settings.RunAtStartup = RunAtStartupBox.IsChecked == true;
        settings.OcrLanguageTag = OcrLanguageBox.Text.Trim();
        ApplyKeybindSettings(settings);
        ApplyRecordingSettings(settings.Recording ??= new RecordingSettings());
        ApplyReplaySettings(settings);
        settings.ConfirmBeforeUpload = ConfirmUploadBox.IsChecked == true;
        settings.UploadQueue.EnableBackgroundProcessing = QueueBackgroundProcessingBox.IsChecked == true;
        settings.UploadQueue.BackgroundPollSeconds = int.TryParse(QueuePollSecondsBox.Text.Trim(), out var queuePollSeconds)
            ? Math.Clamp(queuePollSeconds, 5, 3_600)
            : 30;
        settings.AiEnabled = AiEnabledBox.IsChecked == true;
        settings.ConfirmBeforeAiRequest = ConfirmAiBox.IsChecked == true;
        settings.GeminiApiEndpoint = string.IsNullOrWhiteSpace(GeminiEndpointBox.Text)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : GeminiEndpointBox.Text.Trim();
        settings.GeminiDefaultModelId = string.IsNullOrWhiteSpace(GeminiDefaultModelBox.Text)
            ? "gemini-3.1-flash-image"
            : GeminiDefaultModelBox.Text.Trim();
        settings.GeminiSpeechToTextModelId = string.IsNullOrWhiteSpace(GeminiSpeechModelBox.Text)
            ? "gemini-3.5-flash"
            : GeminiSpeechModelBox.Text.Trim();
        settings.TranscriptionProvider = TranscriptionProviderBox.SelectedValue as string ?? "external-whisper";
        settings.ExternalWhisperExecutablePath = ExternalWhisperExecutableBox.Text.Trim();
        settings.ExternalWhisperModelPath = ExternalWhisperModelBox.Text.Trim();
        settings.ExternalWhisperLanguage = string.IsNullOrWhiteSpace(ExternalWhisperLanguageBox.Text)
            ? "en"
            : ExternalWhisperLanguageBox.Text.Trim();
        settings.GeminiModelManifestPath = GeminiManifestBox.Text.Trim();
        settings.LocalExportFolder = LocalExportFolderBox.Text.Trim();
        settings.CustomScriptCommand = CustomScriptBox.Text.Trim();
        settings.CustomWebhookUrl = CustomWebhookBox.Text.Trim();
        settings.SlackWebhookUrl = SlackWebhookBox.Text.Trim();
        settings.SlackMessageTemplate = string.IsNullOrWhiteSpace(SlackMessageTemplateBox.Text)
            ? "Receipts capture ready: {file} ({bytes} bytes)"
            : SlackMessageTemplateBox.Text.Trim();
        settings.DiscordWebhookUrl = DiscordWebhookBox.Text.Trim();
        settings.DiscordMessageTemplate = string.IsNullOrWhiteSpace(DiscordMessageTemplateBox.Text)
            ? "Receipts capture: {file}"
            : DiscordMessageTemplateBox.Text.Trim();
        settings.TeamsWebhookUrl = TeamsWebhookBox.Text.Trim();
        settings.TeamsMessageTemplate = string.IsNullOrWhiteSpace(TeamsMessageTemplateBox.Text)
            ? "Receipts capture ready: {file} ({bytes} bytes)"
            : TeamsMessageTemplateBox.Text.Trim();
        settings.S3Endpoint = S3EndpointBox.Text.Trim();
        settings.S3Bucket = S3BucketBox.Text.Trim();
        settings.S3Region = string.IsNullOrWhiteSpace(S3RegionBox.Text)
            ? "us-east-1"
            : S3RegionBox.Text.Trim();
        settings.S3KeyPrefix = S3PrefixBox.Text.Trim();
        settings.S3PublicBaseUrl = S3PublicBaseUrlBox.Text.Trim();
        settings.ImgurApiEndpoint = string.IsNullOrWhiteSpace(ImgurEndpointBox.Text)
            ? "https://api.imgur.com/3/image"
            : ImgurEndpointBox.Text.Trim();
        settings.SftpHost = SftpHostBox.Text.Trim();
        settings.SftpPort = int.TryParse(SftpPortBox.Text.Trim(), out var sftpPort) && sftpPort > 0
            ? sftpPort
            : 22;
        settings.SftpUsername = SftpUsernameBox.Text.Trim();
        settings.SftpRemoteDirectory = string.IsNullOrWhiteSpace(SftpRemoteDirectoryBox.Text)
            ? "/"
            : SftpRemoteDirectoryBox.Text.Trim();
        settings.SftpPrivateKeyPath = SftpPrivateKeyPathBox.Text.Trim();
        settings.SftpHostKeyFingerprint = SftpHostKeyFingerprintBox.Text.Trim();
        settings.SftpPublicBaseUrl = SftpPublicBaseUrlBox.Text.Trim();
        settings.WebDavBaseUrl = WebDavBaseUrlBox.Text.Trim();
        settings.WebDavRemoteDirectory = string.IsNullOrWhiteSpace(WebDavRemoteDirectoryBox.Text)
            ? "/"
            : WebDavRemoteDirectoryBox.Text.Trim();
        settings.WebDavUsername = WebDavUsernameBox.Text.Trim();
        settings.WebDavPublicBaseUrl = WebDavPublicBaseUrlBox.Text.Trim();
        settings.FtpHost = FtpHostBox.Text.Trim();
        settings.FtpPort = int.TryParse(FtpPortBox.Text.Trim(), out var ftpPort) && ftpPort > 0
            ? ftpPort
            : 21;
        settings.FtpUseFtps = FtpUseFtpsBox.IsChecked == true;
        settings.FtpUsername = FtpUsernameBox.Text.Trim();
        settings.FtpRemoteDirectory = string.IsNullOrWhiteSpace(FtpRemoteDirectoryBox.Text)
            ? "/"
            : FtpRemoteDirectoryBox.Text.Trim();
        settings.FtpPublicBaseUrl = FtpPublicBaseUrlBox.Text.Trim();
        settings.CloudinaryApiBaseUrl = string.IsNullOrWhiteSpace(CloudinaryApiBaseUrlBox.Text)
            ? "https://api.cloudinary.com"
            : CloudinaryApiBaseUrlBox.Text.Trim();
        settings.CloudinaryCloudName = CloudinaryCloudNameBox.Text.Trim();
        settings.CloudinaryResourceType = string.IsNullOrWhiteSpace(CloudinaryResourceTypeBox.Text)
            ? "auto"
            : CloudinaryResourceTypeBox.Text.Trim();
        settings.CloudinaryUploadPreset = CloudinaryUploadPresetBox.Text.Trim();
        settings.CloudinaryFolder = CloudinaryFolderBox.Text.Trim();
        settings.DropboxContentApiBaseUrl = string.IsNullOrWhiteSpace(DropboxContentApiBaseUrlBox.Text)
            ? "https://content.dropboxapi.com"
            : DropboxContentApiBaseUrlBox.Text.Trim();
        settings.DropboxApiBaseUrl = string.IsNullOrWhiteSpace(DropboxApiBaseUrlBox.Text)
            ? "https://api.dropboxapi.com"
            : DropboxApiBaseUrlBox.Text.Trim();
        settings.DropboxRemoteFolder = string.IsNullOrWhiteSpace(DropboxRemoteFolderBox.Text)
            ? "/Receipts"
            : DropboxRemoteFolderBox.Text.Trim();
        SetOAuthClientId("Dropbox", DropboxOAuthClientIdBox.Text);
        settings.GoogleDriveUploadApiBaseUrl = string.IsNullOrWhiteSpace(GoogleDriveUploadApiBaseUrlBox.Text)
            ? "https://www.googleapis.com/upload/drive/v3"
            : GoogleDriveUploadApiBaseUrlBox.Text.Trim();
        settings.GoogleDriveApiBaseUrl = string.IsNullOrWhiteSpace(GoogleDriveApiBaseUrlBox.Text)
            ? "https://www.googleapis.com/drive/v3"
            : GoogleDriveApiBaseUrlBox.Text.Trim();
        settings.GoogleDriveFolderId = GoogleDriveFolderIdBox.Text.Trim();
        settings.GoogleDriveCreateAnyoneReaderLink = GoogleDriveAnyoneReaderBox.IsChecked == true;
        SetOAuthClientId("Google Drive", GoogleDriveOAuthClientIdBox.Text);
        settings.GooglePhotosUploadApiBaseUrl = string.IsNullOrWhiteSpace(GooglePhotosUploadApiBaseUrlBox.Text)
            ? "https://photoslibrary.googleapis.com/v1/uploads"
            : GooglePhotosUploadApiBaseUrlBox.Text.Trim();
        settings.GooglePhotosApiBaseUrl = string.IsNullOrWhiteSpace(GooglePhotosApiBaseUrlBox.Text)
            ? "https://photoslibrary.googleapis.com/v1"
            : GooglePhotosApiBaseUrlBox.Text.Trim();
        settings.GooglePhotosAlbumId = GooglePhotosAlbumIdBox.Text.Trim();
        settings.GooglePhotosDescriptionTemplate = string.IsNullOrWhiteSpace(GooglePhotosDescriptionTemplateBox.Text)
            ? "Receipts capture: {file}"
            : GooglePhotosDescriptionTemplateBox.Text.Trim();
        SetOAuthClientId("Google Photos", GooglePhotosOAuthClientIdBox.Text);
        settings.OneDriveGraphApiBaseUrl = string.IsNullOrWhiteSpace(OneDriveGraphApiBaseUrlBox.Text)
            ? "https://graph.microsoft.com/v1.0"
            : OneDriveGraphApiBaseUrlBox.Text.Trim();
        settings.OneDriveRemoteFolder = string.IsNullOrWhiteSpace(OneDriveRemoteFolderBox.Text)
            ? "/Receipts"
            : OneDriveRemoteFolderBox.Text.Trim();
        settings.OneDriveCreateAnonymousViewLink = OneDriveAnonymousViewLinkBox.IsChecked == true;
        SetOAuthClientId("OneDrive", OneDriveOAuthClientIdBox.Text);
        settings.YouTubeUploadApiBaseUrl = string.IsNullOrWhiteSpace(YouTubeUploadApiBaseUrlBox.Text)
            ? "https://www.googleapis.com/upload/youtube/v3"
            : YouTubeUploadApiBaseUrlBox.Text.Trim();
        settings.YouTubeTitleTemplate = string.IsNullOrWhiteSpace(YouTubeTitleTemplateBox.Text)
            ? "Receipts recording: {file}"
            : YouTubeTitleTemplateBox.Text.Trim();
        settings.YouTubeDescriptionTemplate = string.IsNullOrWhiteSpace(YouTubeDescriptionTemplateBox.Text)
            ? "Uploaded from Receipts capture {id}."
            : YouTubeDescriptionTemplateBox.Text.Trim();
        settings.YouTubePrivacyStatus = ComboBoxText(YouTubePrivacyStatusBox, "unlisted").Trim().ToLowerInvariant();
        settings.YouTubeCategoryId = string.IsNullOrWhiteSpace(YouTubeCategoryIdBox.Text)
            ? "22"
            : YouTubeCategoryIdBox.Text.Trim();
        SetOAuthClientId("YouTube", YouTubeOAuthClientIdBox.Text);
        settings.OneNoteGraphApiBaseUrl = string.IsNullOrWhiteSpace(OneNoteGraphApiBaseUrlBox.Text)
            ? "https://graph.microsoft.com/v1.0"
            : OneNoteGraphApiBaseUrlBox.Text.Trim();
        settings.OneNoteSectionId = OneNoteSectionIdBox.Text.Trim();
        settings.OneNotePageTitleTemplate = string.IsNullOrWhiteSpace(OneNotePageTitleTemplateBox.Text)
            ? "Receipts capture: {file}"
            : OneNotePageTitleTemplateBox.Text.Trim();
        SetOAuthClientId("OneNote", OneNoteOAuthClientIdBox.Text);
        settings.LinearGraphqlEndpoint = string.IsNullOrWhiteSpace(LinearGraphqlEndpointBox.Text)
            ? "https://api.linear.app/graphql"
            : LinearGraphqlEndpointBox.Text.Trim();
        settings.LinearTeamId = LinearTeamIdBox.Text.Trim();
        settings.LinearIssueId = LinearIssueIdBox.Text.Trim();
        settings.LinearIssueTitleTemplate = string.IsNullOrWhiteSpace(LinearIssueTitleTemplateBox.Text)
            ? "Receipts capture: {file}"
            : LinearIssueTitleTemplateBox.Text.Trim();
        settings.LinearCreateAttachment = LinearCreateAttachmentBox.IsChecked == true;
        settings.LinearUseOAuthBearerToken = LinearUseOAuthBearerTokenBox.IsChecked == true;
        settings.GitHubApiBaseUrl = string.IsNullOrWhiteSpace(GitHubApiBaseUrlBox.Text)
            ? "https://api.github.com"
            : GitHubApiBaseUrlBox.Text.Trim();
        settings.GitHubRepository = GitHubRepositoryBox.Text.Trim();
        settings.GitHubIssueTitleTemplate = string.IsNullOrWhiteSpace(GitHubIssueTitleTemplateBox.Text)
            ? "Receipts capture: {file}"
            : GitHubIssueTitleTemplateBox.Text.Trim();
        settings.GitHubLabels = GitHubLabelsBox.Text.Trim();
        settings.GitHubAssignees = GitHubAssigneesBox.Text.Trim();
        settings.JiraBaseUrl = JiraBaseUrlBox.Text.Trim();
        settings.JiraProjectKey = JiraProjectKeyBox.Text.Trim();
        settings.JiraIssueType = string.IsNullOrWhiteSpace(JiraIssueTypeBox.Text)
            ? "Bug"
            : JiraIssueTypeBox.Text.Trim();
        settings.JiraSummaryTemplate = string.IsNullOrWhiteSpace(JiraSummaryTemplateBox.Text)
            ? "Receipts capture: {file}"
            : JiraSummaryTemplateBox.Text.Trim();
        settings.JiraLabels = JiraLabelsBox.Text.Trim();
        settings.JiraAccountEmail = JiraAccountEmailBox.Text.Trim();
        settings.AzureDevOpsBaseUrl = string.IsNullOrWhiteSpace(AzureDevOpsBaseUrlBox.Text)
            ? "https://dev.azure.com"
            : AzureDevOpsBaseUrlBox.Text.Trim();
        settings.AzureDevOpsOrganization = AzureDevOpsOrganizationBox.Text.Trim();
        settings.AzureDevOpsProject = AzureDevOpsProjectBox.Text.Trim();
        settings.AzureDevOpsWorkItemType = string.IsNullOrWhiteSpace(AzureDevOpsWorkItemTypeBox.Text)
            ? "Bug"
            : AzureDevOpsWorkItemTypeBox.Text.Trim();
        settings.AzureDevOpsTitleTemplate = string.IsNullOrWhiteSpace(AzureDevOpsTitleTemplateBox.Text)
            ? "Receipts capture: {file}"
            : AzureDevOpsTitleTemplateBox.Text.Trim();
        settings.AzureDevOpsTags = AzureDevOpsTagsBox.Text.Trim();
        settings.AzureDevOpsAssignedTo = AzureDevOpsAssignedToBox.Text.Trim();
        settings.DefaultShareDestination =
            (DefaultShareBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Clipboard image";
        settings.UploadDenylistProcesses = SplitLines(UploadDenylistBox.Text);
        settings.AiDenylistProcesses = SplitLines(AiDenylistBox.Text);
        settings.EnableWatchFolders = EnableWatchFoldersBox.IsChecked == true;
        settings.WatchFolderIncludeSubdirectories = WatchSubdirectoriesBox.IsChecked == true;
        settings.WatchFolderAutoImport = WatchAutoImportBox.IsChecked != false;
        settings.WatchFolderCopyImageAfterImport = WatchCopyAfterImportBox.IsChecked == true;
        settings.WatchFolderStripMetadataAfterImport = WatchStripMetadataBox.IsChecked == true;
        settings.WatchFolderShareAfterImport = WatchShareAfterImportBox.IsChecked == true;
        settings.WatchFolders = SplitLines(WatchFoldersBox.Text);
        SaveAutomationRules(settings);
    }

    private void ApplyRecordingPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = (sender as FrameworkElement)?.Tag as string ?? string.Empty;
        switch (preset)
        {
            case "QuickMp4":
                ApplyRecordingPresetValues("Balanced", 30, "1280x720", includeMic: false, includeSystemAudio: false, includeWebcam: false, showTimer: false, showKeys: false);
                RecordingDeviceStatusText.Text = "Quick MP4 preset applied: balanced 30 fps source recording.";
                break;
            case "SmoothGif60":
                ApplyRecordingPresetValues("High quality", 60, "1280xauto", includeMic: false, includeSystemAudio: false, includeWebcam: false, showTimer: true, showKeys: false);
                RecordingDeviceStatusText.Text = "Smooth GIF 60 preset applied: GIF export uses an approximate 10/20 ms delay pattern.";
                break;
            case "UltraSmooth120":
                ApplyRecordingPresetValues("Archive", 120, "1280xauto", includeMic: false, includeSystemAudio: false, includeWebcam: false, showTimer: true, showKeys: false);
                RecordingDeviceStatusText.Text = "Ultra Smooth 120 preset applied: use MP4/WebM companion output for true 120 fps playback; GIF output is capped by 10 ms timing.";
                break;
            case "TutorialMic":
                ApplyRecordingPresetValues("High quality", 60, "1280x720", includeMic: true, includeSystemAudio: false, includeWebcam: false, showTimer: true, showKeys: true);
                RecordingCountdownBox.IsChecked = true;
                RecordingDeviceStatusText.Text = "Tutorial with mic preset applied: microphone, countdown, timer, and keystroke overlay enabled.";
                break;
            case "WebcamWalkthrough":
                ApplyRecordingPresetValues("High quality", 60, "1280x720", includeMic: true, includeSystemAudio: false, includeWebcam: true, showTimer: true, showKeys: false);
                RecordingCountdownBox.IsChecked = true;
                RecordingDeviceStatusText.Text = "Webcam walkthrough preset applied: webcam overlay and microphone enabled.";
                break;
            default:
                RecordingDeviceStatusText.Text = "Advanced custom selected. Tune the controls below, then save settings.";
                RecordingQualityBox.BringIntoView();
                break;
        }

        RefreshRecordingSummaryTiles();
    }

    private void ApplyRecordingPresetValues(
        string quality,
        int fps,
        string targetSize,
        bool includeMic,
        bool includeSystemAudio,
        bool includeWebcam,
        bool showTimer,
        bool showKeys)
    {
        SelectComboBoxItem(RecordingQualityBox, quality);
        RecordingFpsBox.Text = Math.Clamp(fps, RecordingSettingsNormalizer.MinFallbackFps, RecordingSettingsNormalizer.MaxFallbackFps)
            .ToString(CultureInfo.InvariantCulture);
        RecordingSizeBox.Text = targetSize;
        RecordingBitrateBox.Text = string.Empty;
        RecordingMicBox.IsChecked = includeMic;
        RecordingMicMutedBox.IsChecked = false;
        RecordingSystemAudioBox.IsChecked = includeSystemAudio;
        RecordingSystemAudioMutedBox.IsChecked = false;
        RecordingWebcamBox.IsChecked = includeWebcam;
        RecordingCountdownBox.IsChecked = false;
        RecordingBorderBox.IsChecked = true;
        RecordingTimerBox.IsChecked = showTimer;
        RecordingKeystrokeBox.IsChecked = showKeys;
    }

    private void LoadRecordingSettings(RecordingSettings settings)
    {
        PreferProductionRecorderBox.IsChecked = settings.PreferProductionCaptureEngine;
        PreferHevcRecorderBox.IsChecked = settings.PreferHevcEncoding;
        SelectComboBoxItem(RecordingQualityBox, RecordingSettingsNormalizer.NormalizeQualityProfile(settings.QualityProfile));
        RecordingFpsBox.Text = settings.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        RecordingSizeBox.Text = settings.TargetWidth > 0 || settings.TargetHeight > 0
            ? $"{(settings.TargetWidth > 0 ? settings.TargetWidth.ToString(CultureInfo.InvariantCulture) : "auto")}x{(settings.TargetHeight > 0 ? settings.TargetHeight.ToString(CultureInfo.InvariantCulture) : "auto")}"
            : string.Empty;
        RecordingBitrateBox.Text = settings.BitrateKbps > 0
            ? $"{settings.BitrateKbps.ToString(CultureInfo.InvariantCulture)}k"
            : string.Empty;
        RecordingMicBox.IsChecked = settings.IncludeMicrophone;
        SetDeviceComboFallback(RecordingMicDeviceBox, settings.MicrophoneDeviceId, "System default microphone");
        RecordingMicGainBox.Text = settings.MicrophoneGain.ToString("0.##", CultureInfo.InvariantCulture);
        RecordingMicMutedBox.IsChecked = settings.MicrophoneMuted;
        RecordingSystemAudioBox.IsChecked = settings.IncludeSystemAudio;
        SetDeviceComboFallback(RecordingSystemAudioDeviceBox, settings.SystemAudioDeviceId, "System default system audio");
        RecordingSystemAudioGainBox.Text = settings.SystemAudioGain.ToString("0.##", CultureInfo.InvariantCulture);
        RecordingSystemAudioMutedBox.IsChecked = settings.SystemAudioMuted;
        RecordingNoiseGateBox.Text = settings.NoiseGateThresholdDb.ToString("0.#", CultureInfo.InvariantCulture);
        RecordingWebcamBox.IsChecked = settings.EnableWebcamOverlay;
        SetDeviceComboFallback(RecordingWebcamDeviceBox, settings.WebcamDeviceId, "System default webcam");
        RecordingCountdownBox.IsChecked = settings.ShowCountdown;
        RecordingBorderBox.IsChecked = settings.ShowRecordingBorder;
        RecordingTimerBox.IsChecked = settings.ShowRecordingTimer;
        RecordingKeystrokeBox.IsChecked = settings.ShowKeystrokeOverlay;
        SelectComboBoxItem(
            RecordingTimerPositionBox,
            RecordingSettingsNormalizer.NormalizeOverlayPosition(settings.RecordingTimerPosition, "TopLeft"));
        SelectComboBoxItem(
            RecordingKeystrokePositionBox,
            RecordingSettingsNormalizer.NormalizeOverlayPosition(settings.KeystrokeOverlayPosition, "BottomLeft"));
        RecordingOverlayBadgeSizeBox.Text = Math.Clamp(settings.RecordingOverlayBadgeFontSize, 10, 32)
            .ToString(CultureInfo.InvariantCulture);
        SelectComboBoxItem(
            RecordingOverlayStyleBox,
            RecordingSettingsNormalizer.NormalizeOverlayStyle(settings.RecordingOverlayStyle));
    }

    private void ApplyRecordingSettings(RecordingSettings settings)
    {
        settings.PreferProductionCaptureEngine = PreferProductionRecorderBox.IsChecked != false;
        settings.PreferHevcEncoding = PreferHevcRecorderBox.IsChecked == true;
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
        settings.MicrophoneDeviceId = SelectedDeviceId(RecordingMicDeviceBox);
        settings.IncludeSystemAudio = RecordingSystemAudioBox.IsChecked == true;
        settings.SystemAudioDeviceId = SelectedDeviceId(RecordingSystemAudioDeviceBox);
        var micProcessing = AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(
            ParseRecordingDouble(RecordingMicGainBox.Text, settings.MicrophoneGain),
            ParseRecordingDouble(RecordingNoiseGateBox.Text, settings.NoiseGateThresholdDb),
            RecordingMicMutedBox.IsChecked == true));
        var systemProcessing = AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(
            ParseRecordingDouble(RecordingSystemAudioGainBox.Text, settings.SystemAudioGain),
            ParseRecordingDouble(RecordingNoiseGateBox.Text, settings.NoiseGateThresholdDb),
            RecordingSystemAudioMutedBox.IsChecked == true));
        settings.MicrophoneGain = micProcessing.Gain;
        settings.SystemAudioGain = systemProcessing.Gain;
        settings.NoiseGateThresholdDb = micProcessing.NoiseGateThresholdDb;
        settings.MicrophoneMuted = micProcessing.Muted;
        settings.SystemAudioMuted = systemProcessing.Muted;
        settings.EnableWebcamOverlay = RecordingWebcamBox.IsChecked == true;
        settings.WebcamDeviceId = SelectedDeviceId(RecordingWebcamDeviceBox);
        settings.ShowCountdown = RecordingCountdownBox.IsChecked != false;
        settings.ShowRecordingBorder = RecordingBorderBox.IsChecked != false;
        settings.ShowRecordingTimer = RecordingTimerBox.IsChecked != false;
        settings.ShowKeystrokeOverlay = RecordingKeystrokeBox.IsChecked == true;
        settings.RecordingTimerPosition = RecordingSettingsNormalizer.NormalizeOverlayPosition(
            (RecordingTimerPositionBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
            "TopLeft");
        settings.KeystrokeOverlayPosition = RecordingSettingsNormalizer.NormalizeOverlayPosition(
            (RecordingKeystrokePositionBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
            "BottomLeft");
        settings.RecordingOverlayBadgeFontSize = Math.Clamp(
            int.TryParse(RecordingOverlayBadgeSizeBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var badgeSize)
                ? badgeSize
                : settings.RecordingOverlayBadgeFontSize,
            10,
            32);
        settings.RecordingOverlayStyle = RecordingSettingsNormalizer.NormalizeOverlayStyle(
            (RecordingOverlayStyleBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
    }

    private void LoadReplaySettings(ReplayBufferSettings replay)
    {
        replay = replay.Normalize();
        ReplayConsentBox.IsChecked = replay.ConsentGranted;
        ReplayAutoArmBox.IsChecked = replay.AutoArmAtSignIn;
        foreach (var item in ReplaySourceKindBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString()?.Equals(replay.CaptureSource.Kind.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                ReplaySourceKindBox.SelectedItem = item;
                break;
            }
        }

        if (ReplaySourceKindBox.SelectedIndex < 0)
        {
            ReplaySourceKindBox.SelectedIndex = 1;
        }

        ReplaySourceIdBox.Text = replay.CaptureSource.SourceId;
        ReplayBoundsBox.Text = replay.CaptureSource.Bounds is null
            ? string.Empty
            : $"{replay.CaptureSource.Bounds.X},{replay.CaptureSource.Bounds.Y},{replay.CaptureSource.Bounds.Width},{replay.CaptureSource.Bounds.Height}";
        ReplayBufferSecondsBox.Text = replay.BufferDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ReplaySaveSecondsBox.Text = replay.SaveDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ReplaySegmentSecondsBox.Text = replay.SegmentDuration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ReplayStorageMbBox.Text = (replay.MaxBufferBytes / 1024d / 1024d).ToString("0.##", CultureInfo.InvariantCulture);
        ReplaySceneIndexBox.IsChecked = replay.EnableSceneIndexing;
        ReplayOcrIndexBox.IsChecked = replay.EnableLocalOcrIndexing;
        ReplayAnalysisSensitivityBox.Text = replay.AnalysisSensitivity.ToString("0.##", CultureInfo.InvariantCulture);
        ReplayPrivacyProcessesBox.Text = string.Join(Environment.NewLine, replay.PrivacyExcludedProcessNames);
        RefreshReplayHotkeySummary();
        RefreshReplaySourceChoices(replay.CaptureSource.SourceId);
        UpdateReplaySourceHelp();
    }

    private void ApplyReplaySettings(AppSettings settings)
    {
        var existing = settings.Replay ?? new ReplayBufferSettings();
        var sourceKindText = (ReplaySourceKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var sourceKind = Enum.TryParse<ReplayCaptureSourceKind>(sourceKindText, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : ReplayCaptureSourceKind.FollowCursorMonitor;
        var bounds = ParseReplayBounds(ReplayBoundsBox.Text);
        var sourceId = ReplaySourceIdBox.Text.Trim();
        var displayName = (ReplaySourceKindBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? "Follow cursor monitor";
        settings.Replay = new ReplayBufferSettings
        {
            ConsentGranted = ReplayConsentBox.IsChecked == true,
            AutoArmAtSignIn = ReplayAutoArmBox.IsChecked == true,
            CaptureSource = new ReplayCaptureSourceDescriptor(sourceKind, sourceId, displayName, bounds),
            BufferDuration = TimeSpan.FromSeconds(ParseReplayDouble(ReplayBufferSecondsBox.Text, existing.BufferDuration.TotalSeconds, 2d, 86_400d)),
            SaveDuration = TimeSpan.FromSeconds(ParseReplayDouble(ReplaySaveSecondsBox.Text, existing.SaveDuration.TotalSeconds, 2d, 86_400d)),
            SegmentDuration = TimeSpan.FromSeconds(ParseReplayDouble(ReplaySegmentSecondsBox.Text, existing.SegmentDuration.TotalSeconds, 0.5d, 30d)),
            MaxBufferBytes = (long)Math.Round(ParseReplayDouble(ReplayStorageMbBox.Text, existing.MaxBufferBytes / 1024d / 1024d, 16d, 102_400d) * 1024d * 1024d),
            FramesPerSecond = Math.Clamp(settings.Recording.FramesPerSecond, 1, 120),
            EnableSceneIndexing = ReplaySceneIndexBox.IsChecked != false,
            EnableLocalOcrIndexing = ReplayOcrIndexBox.IsChecked != false,
            AnalysisSensitivity = ParseReplayDouble(ReplayAnalysisSensitivityBox.Text, existing.AnalysisSensitivity, 0.05d, 1d),
            PrivacyExcludedProcessNames = ReplayPrivacyProcessesBox.Text
                .ReplaceLineEndings(Environment.NewLine)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            // Replay gestures are owned by the Keybinds section; carry the current values through so
            // Normalize() cannot resurrect a default over a chord the user just rebound.
            ToggleHotkey = existing.ToggleHotkey,
            SaveHotkey = existing.SaveHotkey
        }.Normalize();
    }

    private void ReplaySourceKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoading)
        {
            RefreshReplaySourceChoices(ReplaySourceIdBox.Text.Trim());
            UpdateReplaySourceHelp();
        }
    }

    private void ReplaySourcePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || ReplaySourcePickerBox.SelectedItem is not CaptureOverlayTarget target)
        {
            return;
        }

        ReplaySourceIdBox.Text = target.Id;
        ReplayBoundsBox.Text = string.Join(
            ',',
            target.Bounds.X,
            target.Bounds.Y,
            target.Bounds.Width,
            target.Bounds.Height);
        UpdateReplaySourceHelp();
    }

    private async void ReplayPickRegion_Click(object sender, RoutedEventArgs e)
    {
        var source = (ReplaySourceKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (source is not ("SelectedRegion" or "FixedRegion"))
        {
            ReplaySourceHelpText.Text = "Choose Selected region or Fixed region before picking screen bounds.";
            return;
        }

        var bounds = await _services.Screenshots.SelectRegionBoundsAsync(this);
        if (bounds is null)
        {
            ReplaySourceHelpText.Text = "Region selection was canceled; existing Replay bounds were kept.";
            return;
        }

        ReplayBoundsBox.Text = string.Join(
            ',',
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);
        ReplaySourceHelpText.Text =
            $"Selected {bounds.Width} × {bounds.Height} at ({bounds.X}, {bounds.Y}). " +
            "Saving settings restarts an armed buffer so the new region takes effect.";
    }

    private void RefreshReplaySourceChoices(string? selectedSourceId = null)
    {
        if (ReplaySourcePickerBox is null || ReplaySourceKindBox is null)
        {
            return;
        }

        var sourceKindText = (ReplaySourceKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var sourceKind = Enum.TryParse<ReplayCaptureSourceKind>(sourceKindText, true, out var parsed)
            ? parsed
            : ReplayCaptureSourceKind.FollowCursorMonitor;
        var targetKind = sourceKind switch
        {
            ReplayCaptureSourceKind.SelectedMonitor => CaptureOverlayTargetKind.Monitor,
            ReplayCaptureSourceKind.SelectedWindow => CaptureOverlayTargetKind.Window,
            _ => (CaptureOverlayTargetKind?)null
        };
        if (targetKind is null)
        {
            ReplaySourcePickerBox.ItemsSource = null;
            ReplaySourcePickerBox.IsEnabled = false;
            ReplayPickRegionButton.IsEnabled = sourceKind is
                ReplayCaptureSourceKind.SelectedRegion or ReplayCaptureSourceKind.FixedRegion;
            return;
        }

        var targets = CaptureOverlayTargetCatalog.BuildLiveTargets()
            .Where(target => target.Kind == targetKind.Value)
            .ToArray();
        ReplaySourcePickerBox.ItemsSource = targets;
        ReplaySourcePickerBox.IsEnabled = targets.Length > 0;
        ReplayPickRegionButton.IsEnabled = false;
        ReplaySourcePickerBox.SelectedItem = targets.FirstOrDefault(target =>
            target.Id.Equals(selectedSourceId, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateReplaySourceHelp()
    {
        if (ReplaySourceHelpText is null)
        {
            return;
        }

        var source = (ReplaySourceKindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "FollowCursorMonitor";
        ReplaySourceHelpText.Text = source switch
        {
            "SelectedMonitor" => "Choose a live monitor from the list. The chosen monitor remains selected until you change it; display removal finalizes the segment and reports an error.",
            "FollowCursorMonitor" => "The active monitor is resolved again at every segment boundary, so moving between monitors preserves a source transition without locking the profile.",
            "AllMonitorsComposite" => "Every display is captured into one virtual-desktop video track.",
            "SeparateMonitorTracks" => "Each connected display gets a synchronized track, with the storage cap enforced across all tracks.",
            "SelectedWindow" => "Choose a live top-level window from the list. Source and size changes finalize the current segment.",
            "FollowForegroundWindow" => "The foreground window is resolved again at segment boundaries and transitions are retained in the receipt timeline.",
            "SelectedRegion" => "Use Pick region to draw screen bounds. The selection remains editable and takes effect when Replay is re-armed.",
            "FixedRegion" => "Use Pick region to draw fixed screen bounds. The same coordinates are retained until you edit them.",
            _ => string.Empty
        };
    }

    private static ReplayCaptureBounds? ParseReplayBounds(string value)
    {
        var parts = (value ?? string.Empty)
            .Split([',', 'x', 'X', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
            width > 0 && height > 0
                ? new ReplayCaptureBounds(x, y, width, height)
                : null;
    }

    private static double ParseReplayDouble(string value, double fallback, double minimum, double maximum) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : Math.Clamp(fallback, minimum, maximum);

    private async void RefreshRecordingDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRecordingDevicesAsync();
    }

    private async Task RefreshRecordingDevicesAsync()
    {
        RecordingDeviceStatusText.Text = "Checking local recording devices...";
        try
        {
            var settings = _services.Settings.Recording ??= new RecordingSettings();
            var result = await Task.Run(async () =>
            {
                var microphones = await _services.AudioCapture.ListInputDevicesAsync(CancellationToken.None);
                var systemAudio = await _services.AudioCapture.ListLoopbackDevicesAsync(CancellationToken.None);
                var cameras = await _services.CameraOverlay.ListDevicesAsync(CancellationToken.None);
                var issues = DiagnosticsService.FindSelectionIssues(settings, microphones, systemAudio, cameras);
                var confidence = RecordingConfidenceService.BuildDeviceReport(
                    settings,
                    microphones,
                    systemAudio,
                    cameras,
                    issues);

                return new RecordingDeviceDiscoveryResult(microphones, systemAudio, cameras, confidence);
            });

            FillAudioDeviceCombo(
                RecordingMicDeviceBox,
                result.Microphones,
                settings.MicrophoneDeviceId,
                "System default microphone",
                "Saved microphone");
            FillAudioDeviceCombo(
                RecordingSystemAudioDeviceBox,
                result.SystemAudio,
                settings.SystemAudioDeviceId,
                "System default system audio",
                "Saved system-audio device");
            FillCameraDeviceCombo(
                RecordingWebcamDeviceBox,
                result.Cameras,
                settings.WebcamDeviceId,
                "System default webcam",
                "Saved webcam");

            RecordingDeviceStatusText.Text =
                $"Found {result.Microphones.Count} microphone(s), {result.SystemAudio.Count} system-audio device(s), and {result.Cameras.Count} webcam(s)." +
                Environment.NewLine +
                result.Confidence.ToStatusText();
        }
        catch (Exception ex)
        {
            RecordingDeviceStatusText.Text = $"Recording device discovery failed: {ex.Message}";
        }
    }

    private static void FillAudioDeviceCombo(
        System.Windows.Controls.ComboBox comboBox,
        IReadOnlyList<AudioCaptureDevice> devices,
        string selectedId,
        string defaultLabel,
        string unavailableLabel)
    {
        FillDeviceCombo(
            comboBox,
            devices.Select(device => new DeviceChoice(
                device.Id,
                $"{device.DisplayName}{(device.IsDefault ? " (default)" : string.Empty)}")),
            selectedId,
            defaultLabel,
            unavailableLabel);
    }

    private static void FillCameraDeviceCombo(
        System.Windows.Controls.ComboBox comboBox,
        IReadOnlyList<CameraOverlayDevice> devices,
        string selectedId,
        string defaultLabel,
        string unavailableLabel)
    {
        FillDeviceCombo(
            comboBox,
            devices.Select(device => new DeviceChoice(
                device.Id,
                $"{device.DisplayName}{(device.IsDefault ? " (default)" : string.Empty)}")),
            selectedId,
            defaultLabel,
            unavailableLabel);
    }

    private static void FillDeviceCombo(
        System.Windows.Controls.ComboBox comboBox,
        IEnumerable<DeviceChoice> devices,
        string selectedId,
        string defaultLabel,
        string unavailableLabel)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(new ComboBoxItem { Content = defaultLabel, Tag = string.Empty });

        var selected = string.Empty;
        foreach (var device in devices)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = device.DisplayName, Tag = device.Id });
            if (device.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selected = device.Id;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedId) && string.IsNullOrWhiteSpace(selected))
        {
            comboBox.Items.Add(new ComboBoxItem { Content = $"{unavailableLabel} (currently unavailable)", Tag = selectedId });
            selected = selectedId;
        }

        SelectComboBoxItemByTag(comboBox, selected);
    }

    private static void SetDeviceComboFallback(
        System.Windows.Controls.ComboBox comboBox,
        string selectedId,
        string defaultLabel)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add(new ComboBoxItem { Content = defaultLabel, Tag = string.Empty });
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            comboBox.Items.Add(new ComboBoxItem { Content = "Saved device (refresh to resolve)", Tag = selectedId });
            comboBox.SelectedIndex = 1;
            return;
        }

        comboBox.SelectedIndex = 0;
    }

    /// <summary>Fills the dropdown from the catalog so the option list has one source of truth.</summary>
    private void PopulatePostCaptureActionBox()
    {
        if (PostCaptureActionBox.Items.Count > 0)
        {
            return;
        }

        foreach (var option in PostCaptureActionCatalog.Options)
        {
            PostCaptureActionBox.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.StorageValue
            });
        }
    }

    private void PostCaptureAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePostCaptureActionHelpText();
    }

    private void UpdatePostCaptureActionHelpText()
    {
        var action = PostCaptureActionCatalog.Parse(SelectedComboBoxTag(PostCaptureActionBox));
        PostCaptureActionHelpText.Text = PostCaptureActionCatalog.Describe(action).Description +
            " Copying is governed by the checkbox below, so clearing that box while Copy quietly is selected saves the capture without touching the clipboard.";
    }

    private static string SelectedComboBoxTag(System.Windows.Controls.ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private static void SelectComboBoxItemByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if ((item.Tag?.ToString() ?? string.Empty).Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
    }

    private static string SelectedDeviceId(System.Windows.Controls.ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private sealed record DeviceChoice(string Id, string DisplayName);

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

    private static void SelectComboBoxValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        SelectComboBoxItem(comboBox, value);
    }

    private static string ComboBoxText(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        var content = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return string.IsNullOrWhiteSpace(comboBox.Text) ? fallback : comboBox.Text;
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

        return (ParseAutoDimension(parts[0]), ParseAutoDimension(parts[1]));
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

    private static double ParseRecordingDouble(string text, double fallback)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private async Task ConnectOAuthProviderAsync(string providerName, System.Windows.Controls.TextBox clientIdBox, TextBlock statusText)
    {
        ApplyCurrentSettingsFromControls();
        _services.SaveSettings();

        var provider = FindOAuthProvider(providerName);
        if (provider is null)
        {
            statusText.Text = $"{providerName} OAuth provider settings were not found.";
            return;
        }

        provider.ClientId = clientIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(provider.ClientId))
        {
            statusText.Text = $"Enter a {providerName} OAuth client ID before connecting.";
            return;
        }

        statusText.Text = $"Opening browser for {providerName} authorization. Receipts is waiting for the local callback...";
        var result = await _services.OAuthBrowserFlow.AuthorizeAsync(
            provider,
            _services.Settings.OAuth.LocalCallbackPort,
            OpenBrowser,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        if (!result.Succeeded || result.Token is null)
        {
            statusText.Text = result.Message;
            return;
        }

        _services.SecretStore.SaveOAuthToken(provider.ProviderName, result.Token);
        provider.RefreshTokenStored = !string.IsNullOrWhiteSpace(result.Token.RefreshToken) ||
            _services.SecretStore.ReadOAuthToken(provider.ProviderName)?.RefreshToken.Length > 0;
        _services.SaveSettings();
        statusText.Text = FormatOAuthCredentialStatus(
            provider.ProviderName,
            HasProviderAccessToken(provider.ProviderName),
            $"{provider.ProviderName} OAuth access token");
        RefreshProviderSetupSummary();
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private OAuthProviderSettings? FindOAuthProvider(string providerName)
    {
        return _services.Settings.OAuth.Providers.FirstOrDefault(
            provider => provider.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));
    }

    private void SetOAuthClientId(string providerName, string clientId)
    {
        var provider = FindOAuthProvider(providerName);
        if (provider is not null)
        {
            provider.ClientId = clientId.Trim();
        }
    }

    private string FormatOAuthCredentialStatus(string providerName, bool hasAccessToken, string accessTokenLabel)
    {
        var token = _services.SecretStore.ReadOAuthToken(providerName);
        if (token is not null)
        {
            var refresh = string.IsNullOrWhiteSpace(token.RefreshToken)
                ? "Refresh token: missing."
                : "Refresh token: saved.";
            var expires = token.ExpiresAt is null
                ? "Access token expiration: not reported."
                : $"Access token expires: {token.ExpiresAt.Value.LocalDateTime:g}.";
            return $"{providerName} OAuth is connected with Windows DPAPI-saved tokens. {refresh} {expires}";
        }

        return hasAccessToken
            ? $"A {accessTokenLabel} is saved with Windows DPAPI for the current user."
            : $"No {accessTokenLabel} is saved.";
    }

    private bool HasProviderAccessToken(string providerName)
    {
        return providerName.Trim().ToLowerInvariant() switch
        {
            "dropbox" => _services.SecretStore.HasDropboxAccessToken,
            "google drive" or "googledrive" or "gdrive" => _services.SecretStore.HasGoogleDriveAccessToken,
            "google photos" or "googlephotos" or "gphotos" or "photos" => _services.SecretStore.HasGooglePhotosAccessToken,
            "onedrive" or "one drive" or "microsoft onedrive" => _services.SecretStore.HasOneDriveAccessToken,
            "youtube" or "you tube" => _services.SecretStore.HasYouTubeAccessToken,
            "onenote" or "one note" or "microsoft onenote" => _services.SecretStore.HasOneNoteAccessToken,
            _ => false
        };
    }

    private void LoadAutomationRules(AppSettings settings)
    {
        PopulateWorkflowTemplateBox();
        _automationRuleDrafts.Clear();
        _automationRuleDrafts.AddRange(SettingsAutomationRuleManager.CloneRulesForManager(settings.AutomationRules));

        var preferredRuleId = _automationRuleDrafts.FirstOrDefault()?.Id;
        RefreshAutomationRuleList(preferredRuleId);
        if (preferredRuleId is not null)
        {
            LoadAutomationRuleEditor(_automationRuleDrafts.First(rule =>
                rule.Id.Equals(preferredRuleId, StringComparison.OrdinalIgnoreCase)));
            RuleManagerStatusText.Text = $"{_automationRuleDrafts.Count} workflow rule(s) loaded.";
        }
        else
        {
            _selectedAutomationRuleId = null;
            LoadAutomationRuleEditor(new QuickAutomationRuleDraft());
            RuleManagerStatusText.Text = "No saved workflow rules.";
        }
    }

    private void SaveAutomationRules(AppSettings settings)
    {
        StoreSelectedAutomationRuleDraft();

        if (_selectedAutomationRuleId is null)
        {
            var draft = CreateAutomationRuleDraftFromEditor();
            if (SettingsAutomationRuleManager.HasEditorActions(draft))
            {
                draft.Id = SettingsQuickAutomationRule.RuleId;
                _automationRuleDrafts.Add(SettingsAutomationRuleManager.CreateRuleFromDraft(draft));
            }
        }

        settings.AutomationRules = SettingsAutomationRuleManager.PrepareRulesForSave(_automationRuleDrafts);
    }

    private void LoadAutomationRuleEditor(AutomationRule rule)
    {
        LoadAutomationRuleEditor(SettingsAutomationRuleManager.ToDraft(rule));
    }

    private void LoadAutomationRuleEditor(QuickAutomationRuleDraft draft)
    {
        RuleNameBox.Text = string.Empty;
        RuleEnabledBox.IsChecked = true;
        SelectComboBoxItem(RuleTriggerBox, AutomationTrigger.CaptureCreated.ToString());
        RuleSourceAppBox.Text = string.Empty;
        RuleWindowTitleBox.Text = string.Empty;
        SelectComboBoxItem(RuleCaptureKindBox, "Any");
        RuleMonitorContainsBox.Text = string.Empty;
        RuleHotkeyProfileBox.Text = string.Empty;
        RuleFileExtensionBox.Text = string.Empty;
        RuleMinFileSizeBox.Text = string.Empty;
        RuleMaxFileSizeBox.Text = string.Empty;
        RuleOcrContainsBox.Text = string.Empty;
        SelectComboBoxItem(RuleSensitiveConditionBox, "Any");
        SelectComboBoxItem(RuleImageEffectModeBox, VisualRedactionMode.Solid.ToString());
        RuleImageEffectRegionBox.Text = "full";
        RuleCopyCaptureBox.IsChecked = false;
        RuleCopyFileBox.IsChecked = false;
        RuleCopyPathBox.IsChecked = false;
        RuleSaveFolderBox.IsChecked = false;
        RuleRunOcrBox.IsChecked = false;
        RuleRedactSensitiveBox.IsChecked = false;
        RuleApplyImageEffectBox.IsChecked = false;
        RuleStripCaptureBox.IsChecked = false;
        RuleShareCaptureBox.IsChecked = false;
        RuleGenerateDocumentBox.IsChecked = false;
        RuleCustomScriptBox.IsChecked = false;
        RuleCustomWebhookBox.IsChecked = false;
        RuleNotifyBox.IsChecked = false;
        RuleNameBox.Text = draft.Name;
        RuleEnabledBox.IsChecked = draft.IsEnabled;
        SelectComboBoxItem(RuleTriggerBox, draft.Trigger.ToString());
        RuleSourceAppBox.Text = draft.SourceAppContains;
        RuleWindowTitleBox.Text = draft.WindowTitleContains;
        SelectComboBoxItem(RuleCaptureKindBox, string.IsNullOrWhiteSpace(draft.CaptureKind) ? "Any" : draft.CaptureKind);
        RuleMonitorContainsBox.Text = draft.MonitorContains;
        RuleHotkeyProfileBox.Text = draft.HotkeyProfile;
        RuleFileExtensionBox.Text = draft.FileExtension;
        RuleMinFileSizeBox.Text = draft.MinFileSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleMaxFileSizeBox.Text = draft.MaxFileSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        RuleOcrContainsBox.Text = draft.OcrContains;
        SelectComboBoxItem(
            RuleSensitiveConditionBox,
            draft.RequiresSensitiveData switch
            {
                true => "Requires sensitive data",
                false => "Excludes sensitive data",
                _ => "Any"
            });
        SelectComboBoxItem(RuleImageEffectModeBox, draft.ImageEffectMode?.ToString() ?? VisualRedactionMode.Solid.ToString());
        RuleImageEffectRegionBox.Text = string.IsNullOrWhiteSpace(draft.ImageEffectRegion) ? "full" : draft.ImageEffectRegion;
        SetActionChecks(draft.Actions);
    }

    private void StoreSelectedAutomationRuleDraft()
    {
        if (_selectedAutomationRuleId is null)
        {
            return;
        }

        var rule = _automationRuleDrafts.FirstOrDefault(candidate =>
            candidate.Id.Equals(_selectedAutomationRuleId, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            return;
        }

        SettingsAutomationRuleManager.ApplyDraft(rule, CreateAutomationRuleDraftFromEditor());
    }

    private QuickAutomationRuleDraft CreateAutomationRuleDraftFromEditor()
    {
        return new QuickAutomationRuleDraft
        {
            Id = _selectedAutomationRuleId ?? string.Empty,
            Name = RuleNameBox.Text.Trim(),
            IsEnabled = RuleEnabledBox.IsChecked == true,
            Trigger = ParseAutomationTriggerSelection(),
            SourceAppContains = RuleSourceAppBox.Text.Trim(),
            WindowTitleContains = RuleWindowTitleBox.Text.Trim(),
            CaptureKind = ParseCaptureKindSelection(),
            MonitorContains = RuleMonitorContainsBox.Text.Trim(),
            HotkeyProfile = RuleHotkeyProfileBox.Text.Trim(),
            FileExtension = RuleFileExtensionBox.Text.Trim(),
            MinFileSizeBytes = ParseOptionalByteCount(RuleMinFileSizeBox.Text),
            MaxFileSizeBytes = ParseOptionalByteCount(RuleMaxFileSizeBox.Text),
            OcrContains = RuleOcrContainsBox.Text.Trim(),
            RequiresSensitiveData = ParseSensitiveConditionSelection(),
            ImageEffectMode = ParseImageEffectModeSelection(),
            ImageEffectRegion = RuleImageEffectRegionBox.Text.Trim(),
            Actions = CollectRuleActions()
        };
    }

    private void RefreshAutomationRuleList(string? preferredRuleId = null)
    {
        _automationRuleSelectionUpdating = true;
        AutomationRuleListBox.Items.Clear();
        foreach (var item in SettingsAutomationRuleManager.CreateListItems(_automationRuleDrafts))
        {
            AutomationRuleListBox.Items.Add(new ListBoxItem
            {
                Content = item.DisplayText,
                Tag = item.Id,
                ToolTip = item.Summary
            });
        }

        var selectedId = preferredRuleId ?? _selectedAutomationRuleId;
        ListBoxItem? selectedItem = null;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            selectedItem = AutomationRuleListBox.Items
                .OfType<ListBoxItem>()
                .FirstOrDefault(item =>
                    (item.Tag as string)?.Equals(selectedId, StringComparison.OrdinalIgnoreCase) == true);
        }

        selectedItem ??= AutomationRuleListBox.Items.OfType<ListBoxItem>().FirstOrDefault();
        AutomationRuleListBox.SelectedItem = selectedItem;
        _selectedAutomationRuleId = selectedItem?.Tag as string;
        _automationRuleSelectionUpdating = false;
        RefreshAutomationSummaryTiles();
    }

    private string? GetSelectedAutomationRuleId()
    {
        return (AutomationRuleListBox.SelectedItem as ListBoxItem)?.Tag as string;
    }

    private AutomationRule? FindAutomationRuleDraft(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : _automationRuleDrafts.FirstOrDefault(rule =>
                rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private void AutomationRuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_automationRuleSelectionUpdating)
        {
            return;
        }

        StoreSelectedAutomationRuleDraft();
        _selectedAutomationRuleId = GetSelectedAutomationRuleId();
        var selectedRule = FindAutomationRuleDraft(_selectedAutomationRuleId);
        if (selectedRule is not null)
        {
            LoadAutomationRuleEditor(selectedRule);
            RuleManagerStatusText.Text = $"Editing {selectedRule.Name}.";
        }
    }

    private void NewAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        StoreSelectedAutomationRuleDraft();
        var rule = SettingsAutomationRuleManager.CreateNewRule(_automationRuleDrafts);
        _automationRuleDrafts.Add(rule);
        RefreshAutomationRuleList(rule.Id);
        LoadAutomationRuleEditor(rule);
        RuleManagerStatusText.Text = $"Created draft rule {rule.Name}.";
        RuleNameBox.Focus();
    }

    private void AddAutomationTemplate_Click(object sender, RoutedEventArgs e)
    {
        StoreSelectedAutomationRuleDraft();
        var templateId = (WorkflowTemplateBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            RuleManagerStatusText.Text = "Select a workflow template before adding.";
            return;
        }

        try
        {
            var rule = WorkflowRuleTemplateCatalog.CreateRule(
                templateId,
                _automationRuleDrafts,
                enabled: false);
            _automationRuleDrafts.Add(rule);
            RefreshAutomationRuleList(rule.Id);
            LoadAutomationRuleEditor(rule);
            RuleManagerStatusText.Text = $"Added disabled template rule {rule.Name}; review it before saving.";
            RuleNameBox.Focus();
        }
        catch (ArgumentException ex)
        {
            RuleManagerStatusText.Text = ex.Message;
        }
    }

    private void AddAutomationRecipe_Click(object sender, RoutedEventArgs e)
    {
        StoreSelectedAutomationRuleDraft();
        var templateId = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            RuleManagerStatusText.Text = "Select a recipe before adding.";
            return;
        }

        try
        {
            var rule = WorkflowRuleTemplateCatalog.CreateRule(
                templateId,
                _automationRuleDrafts,
                enabled: false);
            _automationRuleDrafts.Add(rule);
            SelectWorkflowTemplate(templateId);
            RefreshAutomationRuleList(rule.Id);
            LoadAutomationRuleEditor(rule);
            RuleManagerStatusText.Text = $"Added disabled recipe {rule.Name}; review it before saving.";
            RuleNameBox.Focus();
        }
        catch (ArgumentException ex)
        {
            RuleManagerStatusText.Text = ex.Message;
        }
    }

    private void DuplicateAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        StoreSelectedAutomationRuleDraft();
        var selectedRule = FindAutomationRuleDraft(_selectedAutomationRuleId);
        if (selectedRule is null)
        {
            RuleManagerStatusText.Text = "Select a workflow rule before duplicating.";
            return;
        }

        var duplicate = SettingsAutomationRuleManager.DuplicateRule(selectedRule, _automationRuleDrafts);
        _automationRuleDrafts.Add(duplicate);
        RefreshAutomationRuleList(duplicate.Id);
        LoadAutomationRuleEditor(duplicate);
        RuleManagerStatusText.Text = $"Created draft rule {duplicate.Name}.";
        RuleNameBox.Focus();
    }

    private void DeleteAutomationRule_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _selectedAutomationRuleId;
        var selectedRule = FindAutomationRuleDraft(selectedId);
        if (selectedRule is null)
        {
            RuleManagerStatusText.Text = "Select a workflow rule before deleting.";
            return;
        }

        _automationRuleDrafts.Remove(selectedRule);
        var nextRuleId = _automationRuleDrafts.FirstOrDefault()?.Id;
        RefreshAutomationRuleList(nextRuleId);
        var nextRule = FindAutomationRuleDraft(_selectedAutomationRuleId);
        if (nextRule is not null)
        {
            LoadAutomationRuleEditor(nextRule);
        }
        else
        {
            _selectedAutomationRuleId = null;
            LoadAutomationRuleEditor(new QuickAutomationRuleDraft());
        }

        RuleManagerStatusText.Text = $"Deleted draft rule {selectedRule.Name}.";
    }

    private void ApplyAutomationRuleEditor_Click(object sender, RoutedEventArgs e)
    {
        StoreSelectedAutomationRuleDraft();
        RefreshAutomationRuleList(_selectedAutomationRuleId);
        var selectedRule = FindAutomationRuleDraft(_selectedAutomationRuleId);
        RuleManagerStatusText.Text = selectedRule is null
            ? "No workflow rule selected."
            : $"Updated draft rule {selectedRule.Name}.";
    }

    private void SetActionChecks(IReadOnlyCollection<AutomationActionKind> actions)
    {
        RuleCopyCaptureBox.IsChecked = actions.Contains(AutomationActionKind.CopyImageToClipboard);
        RuleCopyFileBox.IsChecked = actions.Contains(AutomationActionKind.CopyFileToClipboard);
        RuleCopyPathBox.IsChecked = actions.Contains(AutomationActionKind.CopyPathToClipboard);
        RuleSaveFolderBox.IsChecked = actions.Contains(AutomationActionKind.SaveToFolder);
        RuleRunOcrBox.IsChecked = actions.Contains(AutomationActionKind.RunOcr);
        RuleRedactSensitiveBox.IsChecked = actions.Contains(AutomationActionKind.RedactDetectedSensitiveData);
        RuleApplyImageEffectBox.IsChecked = actions.Contains(AutomationActionKind.ApplyImageEffect);
        RuleStripCaptureBox.IsChecked = actions.Contains(AutomationActionKind.StripMetadataCopy);
        RuleShareCaptureBox.IsChecked = actions.Contains(AutomationActionKind.ShareDefaultDestination);
        RuleGenerateDocumentBox.IsChecked = actions.Contains(AutomationActionKind.GenerateDocument);
        RuleCustomScriptBox.IsChecked = actions.Contains(AutomationActionKind.RunCustomScript);
        RuleCustomWebhookBox.IsChecked = actions.Contains(AutomationActionKind.CallCustomWebhook);
        RuleNotifyBox.IsChecked = actions.Contains(AutomationActionKind.ShowNotification);
    }

    private List<AutomationActionKind> CollectRuleActions()
    {
        var actions = new List<AutomationActionKind>();
        AddIfChecked(RuleCopyCaptureBox, AutomationActionKind.CopyImageToClipboard, actions);
        AddIfChecked(RuleCopyFileBox, AutomationActionKind.CopyFileToClipboard, actions);
        AddIfChecked(RuleCopyPathBox, AutomationActionKind.CopyPathToClipboard, actions);
        AddIfChecked(RuleSaveFolderBox, AutomationActionKind.SaveToFolder, actions);
        AddIfChecked(RuleRunOcrBox, AutomationActionKind.RunOcr, actions);
        AddIfChecked(RuleRedactSensitiveBox, AutomationActionKind.RedactDetectedSensitiveData, actions);
        AddIfChecked(RuleApplyImageEffectBox, AutomationActionKind.ApplyImageEffect, actions);
        AddIfChecked(RuleStripCaptureBox, AutomationActionKind.StripMetadataCopy, actions);
        AddIfChecked(RuleShareCaptureBox, AutomationActionKind.ShareDefaultDestination, actions);
        AddIfChecked(RuleGenerateDocumentBox, AutomationActionKind.GenerateDocument, actions);
        AddIfChecked(RuleCustomScriptBox, AutomationActionKind.RunCustomScript, actions);
        AddIfChecked(RuleCustomWebhookBox, AutomationActionKind.CallCustomWebhook, actions);
        AddIfChecked(RuleNotifyBox, AutomationActionKind.ShowNotification, actions);
        return actions;
    }

    private static void AddIfChecked(System.Windows.Controls.CheckBox box, AutomationActionKind action, ICollection<AutomationActionKind> actions)
    {
        if (box.IsChecked == true)
        {
            actions.Add(action);
        }
    }

    private AutomationTrigger ParseAutomationTriggerSelection()
    {
        var selected = (RuleTriggerBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return Enum.TryParse<AutomationTrigger>(selected, ignoreCase: true, out var trigger)
            ? trigger
            : AutomationTrigger.CaptureCreated;
    }

    private bool? ParseSensitiveConditionSelection()
    {
        var selected = (RuleSensitiveConditionBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return selected switch
        {
            "Requires sensitive data" => true,
            "Excludes sensitive data" => false,
            _ => null
        };
    }

    private string ParseCaptureKindSelection()
    {
        var selected = (RuleCaptureKindBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return string.IsNullOrWhiteSpace(selected) ||
            selected.Equals("Any", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : selected.Trim();
    }

    private VisualRedactionMode? ParseImageEffectModeSelection()
    {
        var selected = (RuleImageEffectModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return Enum.TryParse<VisualRedactionMode>(selected, ignoreCase: true, out var mode)
            ? mode
            : null;
    }

    private static long? ParseOptionalByteCount(string text)
    {
        text = text.Trim();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }

    private void PopulateWorkflowTemplateBox()
    {
        WorkflowTemplateBox.Items.Clear();
        foreach (var template in WorkflowRuleTemplateCatalog.ListTemplates())
        {
            WorkflowTemplateBox.Items.Add(new ComboBoxItem
            {
                Content = $"{template.Name} ({template.Category})",
                Tag = template.Id,
                ToolTip = $"{template.Trigger}: {template.ActionSummary}. {template.Description}"
            });
        }

        WorkflowTemplateBox.SelectedIndex = WorkflowTemplateBox.Items.Count > 0 ? 0 : -1;
    }

    private void SelectWorkflowTemplate(string templateId)
    {
        foreach (var item in WorkflowTemplateBox.Items.OfType<ComboBoxItem>())
        {
            if ((item.Tag as string)?.Equals(templateId, StringComparison.OrdinalIgnoreCase) == true)
            {
                WorkflowTemplateBox.SelectedItem = item;
                return;
            }
        }
    }

    private sealed record RecordingDeviceDiscoveryResult(
        IReadOnlyList<AudioCaptureDevice> Microphones,
        IReadOnlyList<AudioCaptureDevice> SystemAudio,
        IReadOnlyList<CameraOverlayDevice> Cameras,
        RecordingConfidenceReport Confidence);
}

internal static class SettingsSectionScrollSpy
{
    public const double ActivationThreshold = 60;

    public static string? PickActiveSectionKey(
        IReadOnlyList<(string Key, double Top)> orderedSectionTops,
        double threshold)
    {
        if (orderedSectionTops.Count == 0)
        {
            return null;
        }

        var activeKey = orderedSectionTops[0].Key;
        foreach (var (key, top) in orderedSectionTops)
        {
            if (top <= threshold)
            {
                activeKey = key;
            }
        }

        return activeKey;
    }
}

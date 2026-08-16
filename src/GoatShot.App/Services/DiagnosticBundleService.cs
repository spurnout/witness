using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class DiagnosticBundleService
{
    private const int MaxLogFiles = 10;
    private const int MaxLogFileBytes = 256 * 1024;
    private const int MaxWorkspaceItems = 25;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly WorkspaceStore _workspaceStore;
    private readonly DiagnosticsService _diagnostics;
    private readonly ProviderDiagnosticsService _providerDiagnostics;

    public DiagnosticBundleService(
        AppPaths paths,
        AppSettings settings,
        WorkspaceStore workspaceStore,
        DiagnosticsService diagnostics,
        ProviderDiagnosticsService providerDiagnostics)
    {
        _paths = paths;
        _settings = settings;
        _workspaceStore = workspaceStore;
        _diagnostics = diagnostics;
        _providerDiagnostics = providerDiagnostics;
    }

    public Task<DiagnosticBundleResult> CreateAsync(string? outputPath = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolvedOutput = ResolveOutputPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutput)!);
            if (File.Exists(resolvedOutput))
            {
                File.Delete(resolvedOutput);
            }

            var entries = new List<string>();
            using (var archive = ZipFile.Open(resolvedOutput, ZipArchiveMode.Create))
            {
                AddJsonEntry(archive, "manifest.json", BuildManifest(), entries);
                AddTextEntry(archive, "diagnostics.txt", FormatSnapshot(_diagnostics.GetSnapshot()), entries);
                AddJsonEntry(archive, "provider-diagnostics.json", _providerDiagnostics.GetDiagnostics(), entries);
                AddTextEntry(archive, "paths.txt", BuildPathReport(), entries);
                AddJsonEntry(archive, "settings.redacted.json", BuildRedactedSettings(), entries);
                AddJsonEntry(archive, "workspace-summary.json", BuildWorkspaceSummary(), entries);
                AddTextEntry(archive, "assemblies.txt", BuildAssemblyReport(), entries);
                AddTextEntry(archive, "privacy-notes.txt", BuildPrivacyNotes(), entries);
                AddRedactedLogs(archive, entries, cancellationToken);
            }

            return new DiagnosticBundleResult
            {
                Succeeded = true,
                Path = resolvedOutput,
                Message = $"Diagnostic bundle created with {entries.Count} file(s).",
                Entries = entries
            };
        }, cancellationToken);
    }

    private string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        var fileName = $"receipts-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";
        return Path.Combine(_paths.LogsRoot, fileName);
    }

    private object BuildManifest()
    {
        var assembly = typeof(DiagnosticBundleService).Assembly.GetName();
        return new
        {
            bundleVersion = 1,
            generatedAt = DateTimeOffset.Now,
            application = BrandIdentity.ProductName,
            assemblyVersion = assembly.Version?.ToString() ?? "unknown",
            supportBoundary = "Includes diagnostics, provider readiness markers, recording readiness, redacted settings, workspace metadata summary, assembly versions, and redacted logs only.",
            excludedByDefault = new[]
            {
                "capture image/video files",
                "thumbnails",
                "OCR text bodies",
                "DPAPI secret files",
                "secret values",
                "AI request payloads"
            }
        };
    }

    private static string FormatSnapshot(DiagnosticSnapshot snapshot)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Receipts diagnostic snapshot",
                $"Generated at: {DateTimeOffset.Now:O}",
                string.Empty,
                $"OS: {snapshot.OsDescription}",
                $"Runtime: {snapshot.RuntimeDescription}",
                string.Empty,
                "Capture engine:",
                snapshot.CaptureEngine,
                string.Empty,
                "Recording engine:",
                snapshot.RecordingEngine,
                string.Empty,
                "Recording readiness:",
                snapshot.RecordingReadiness,
                string.Empty,
                "Encoder status:",
                snapshot.EncoderStatus,
                string.Empty,
                "OCR status:",
                snapshot.OcrStatus,
                string.Empty,
                "AI status:",
                snapshot.AiStatus,
                string.Empty,
                "Managed policy:",
                snapshot.PolicyStatus,
                string.Empty,
                "Startup status:",
                snapshot.StartupStatus,
                string.Empty,
                "Metadata index:",
                snapshot.MetadataIndexStatus,
                string.Empty,
                "Upload queue:",
                snapshot.UploadQueueStatus,
                string.Empty,
                "Print import:",
                snapshot.PrintImportStatus,
                string.Empty,
                "Local plugins:",
                snapshot.PluginStatus,
                string.Empty,
                "Browser extension bridge:",
                snapshot.BrowserBridgeStatus,
                string.Empty,
                "Sharing status:",
                snapshot.SharingStatus
            ]);
    }

    private string BuildPathReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Receipts paths");
        builder.AppendLine($"local={_paths.LocalRoot}");
        builder.AppendLine($"library={_paths.LibraryRoot}");
        builder.AppendLine($"images={_paths.ImagesRoot}");
        builder.AppendLine($"videos={_paths.VideosRoot}");
        builder.AppendLine($"documents={_paths.DocumentsRoot}");
        builder.AppendLine($"projects={_paths.ProjectsRoot}");
        builder.AppendLine($"thumbnails={_paths.ThumbnailRoot}");
        builder.AppendLine($"temp={_paths.TempRoot}");
        builder.AppendLine($"logs={_paths.LogsRoot}");
        builder.AppendLine($"ai-output={_paths.AiOutputRoot}");
        builder.AppendLine($"settings={_paths.SettingsPath}");
        builder.AppendLine($"upload-queue={_paths.UploadQueuePath}");
        builder.AppendLine($"workspace-index={_paths.IndexPath}");
        builder.AppendLine($"metadata-db={_paths.MetadataDatabasePath}");
        builder.AppendLine("secrets-root=present; contents intentionally excluded");
        return builder.ToString();
    }

    private object BuildRedactedSettings()
    {
        return new
        {
            _settings.LibraryRoot,
            _settings.AutoCopyImageAfterCapture,
            _settings.PostCaptureAction,
            _settings.CaptureActionsAutoDismissSeconds,
            _settings.EnableCaptureHoverAutoSelect,
            _settings.IncludeCursor,
            _settings.PrivateCaptureMode,
            _settings.RunAtStartup,
            _settings.ConfirmBeforeUpload,
            _settings.AiEnabled,
            _settings.ConfirmBeforeAiRequest,
            ManagedPolicy = new
            {
                _settings.ManagedPolicy.DisableAi,
                _settings.ManagedPolicy.DisableUploads,
                _settings.ManagedPolicy.DisableCustomScripts,
                _settings.ManagedPolicy.DisableCustomWebhooks,
                _settings.ManagedPolicy.DisableLocalPlugins,
                _settings.ManagedPolicy.DisableBrowserExtension,
                _settings.ManagedPolicy.DisableAndroidCapture,
                _settings.ManagedPolicy.DisableVirtualPrinterImport,
                _settings.ManagedPolicy.RequirePrivateCaptureMode,
                _settings.ManagedPolicy.DisableDiagnosticsLogCapture,
                _settings.ManagedPolicy.RetentionDays,
                AllowedShareDestinations = _settings.ManagedPolicy.AllowedShareDestinations.Select(RedactPotentialSecrets).ToList(),
                AllowedPluginIds = _settings.ManagedPolicy.AllowedPluginIds.Select(RedactPotentialSecrets).ToList(),
                AllowedPluginActionIds = _settings.ManagedPolicy.AllowedPluginActionIds.Select(RedactPotentialSecrets).ToList()
            },
            Recording = new
            {
                _settings.Recording.PreferProductionCaptureEngine,
                _settings.Recording.PreferHevcEncoding,
                QualityProfile = RedactPotentialSecrets(_settings.Recording.QualityProfile),
                _settings.Recording.FramesPerSecond,
                _settings.Recording.TargetWidth,
                _settings.Recording.TargetHeight,
                _settings.Recording.BitrateKbps,
                _settings.Recording.UseVariableBitrate,
                _settings.Recording.IncludeMicrophone,
                MicrophoneDeviceId = RedactPotentialSecrets(_settings.Recording.MicrophoneDeviceId),
                _settings.Recording.MicrophoneGain,
                _settings.Recording.MicrophoneMuted,
                _settings.Recording.IncludeSystemAudio,
                SystemAudioDeviceId = RedactPotentialSecrets(_settings.Recording.SystemAudioDeviceId),
                _settings.Recording.SystemAudioGain,
                _settings.Recording.SystemAudioMuted,
                _settings.Recording.NoiseGateThresholdDb,
                _settings.Recording.EnableWebcamOverlay,
                WebcamDeviceId = RedactPotentialSecrets(_settings.Recording.WebcamDeviceId),
                WebcamOverlayPosition = RedactPotentialSecrets(_settings.Recording.WebcamOverlayPosition),
                WebcamOverlayShape = RedactPotentialSecrets(_settings.Recording.WebcamOverlayShape),
                _settings.Recording.MirrorWebcam,
                _settings.Recording.ShowCountdown,
                _settings.Recording.ShowRecordingBorder,
                _settings.Recording.ShowRecordingTimer,
                _settings.Recording.ShowKeystrokeOverlay,
                RecordingTimerPosition = RedactPotentialSecrets(_settings.Recording.RecordingTimerPosition),
                KeystrokeOverlayPosition = RedactPotentialSecrets(_settings.Recording.KeystrokeOverlayPosition),
                _settings.Recording.RecordingOverlayBadgeFontSize,
                RecordingOverlayStyle = RedactPotentialSecrets(_settings.Recording.RecordingOverlayStyle)
            },
            GeminiModelManifestPath = RedactPotentialSecrets(_settings.GeminiModelManifestPath),
            GeminiApiEndpoint = RedactPotentialSecrets(_settings.GeminiApiEndpoint),
            _settings.GeminiDefaultModelId,
            _settings.DefaultShareDestination,
            _settings.FileNameTemplate,
            _settings.LocalExportFolder,
            SlackWebhookUrl = RedactedIfConfigured(_settings.SlackWebhookUrl),
            SlackMessageTemplate = RedactPotentialSecrets(_settings.SlackMessageTemplate),
            DiscordWebhookUrl = RedactedIfConfigured(_settings.DiscordWebhookUrl),
            DiscordMessageTemplate = RedactPotentialSecrets(_settings.DiscordMessageTemplate),
            TeamsWebhookUrl = RedactedIfConfigured(_settings.TeamsWebhookUrl),
            TeamsMessageTemplate = RedactPotentialSecrets(_settings.TeamsMessageTemplate),
            S3Endpoint = RedactPotentialSecrets(_settings.S3Endpoint),
            S3Region = RedactPotentialSecrets(_settings.S3Region),
            S3Bucket = RedactPotentialSecrets(_settings.S3Bucket),
            S3KeyPrefix = RedactPotentialSecrets(_settings.S3KeyPrefix),
            S3PublicBaseUrl = RedactPotentialSecrets(_settings.S3PublicBaseUrl),
            ImgurApiEndpoint = RedactPotentialSecrets(_settings.ImgurApiEndpoint),
            SftpExecutablePath = RedactPotentialSecrets(_settings.SftpExecutablePath),
            SftpHost = RedactPotentialSecrets(_settings.SftpHost),
            _settings.SftpPort,
            SftpUsername = RedactPotentialSecrets(_settings.SftpUsername),
            SftpRemoteDirectory = RedactPotentialSecrets(_settings.SftpRemoteDirectory),
            SftpPrivateKeyPath = RedactPotentialSecrets(_settings.SftpPrivateKeyPath),
            SftpPublicBaseUrl = RedactPotentialSecrets(_settings.SftpPublicBaseUrl),
            WebDavBaseUrl = RedactPotentialSecrets(_settings.WebDavBaseUrl),
            WebDavRemoteDirectory = RedactPotentialSecrets(_settings.WebDavRemoteDirectory),
            WebDavUsername = RedactPotentialSecrets(_settings.WebDavUsername),
            WebDavPublicBaseUrl = RedactPotentialSecrets(_settings.WebDavPublicBaseUrl),
            FtpHost = RedactPotentialSecrets(_settings.FtpHost),
            _settings.FtpPort,
            _settings.FtpUseFtps,
            FtpUsername = RedactPotentialSecrets(_settings.FtpUsername),
            FtpRemoteDirectory = RedactPotentialSecrets(_settings.FtpRemoteDirectory),
            FtpPublicBaseUrl = RedactPotentialSecrets(_settings.FtpPublicBaseUrl),
            CloudinaryApiBaseUrl = RedactPotentialSecrets(_settings.CloudinaryApiBaseUrl),
            CloudinaryCloudName = RedactPotentialSecrets(_settings.CloudinaryCloudName),
            CloudinaryResourceType = RedactPotentialSecrets(_settings.CloudinaryResourceType),
            CloudinaryUploadPreset = RedactPotentialSecrets(_settings.CloudinaryUploadPreset),
            CloudinaryFolder = RedactPotentialSecrets(_settings.CloudinaryFolder),
            DropboxContentApiBaseUrl = RedactPotentialSecrets(_settings.DropboxContentApiBaseUrl),
            DropboxApiBaseUrl = RedactPotentialSecrets(_settings.DropboxApiBaseUrl),
            DropboxRemoteFolder = RedactPotentialSecrets(_settings.DropboxRemoteFolder),
            GoogleDriveUploadApiBaseUrl = RedactPotentialSecrets(_settings.GoogleDriveUploadApiBaseUrl),
            GoogleDriveApiBaseUrl = RedactPotentialSecrets(_settings.GoogleDriveApiBaseUrl),
            GoogleDriveFolderId = RedactPotentialSecrets(_settings.GoogleDriveFolderId),
            _settings.GoogleDriveCreateAnyoneReaderLink,
            GooglePhotosUploadApiBaseUrl = RedactPotentialSecrets(_settings.GooglePhotosUploadApiBaseUrl),
            GooglePhotosApiBaseUrl = RedactPotentialSecrets(_settings.GooglePhotosApiBaseUrl),
            GooglePhotosAlbumId = RedactPotentialSecrets(_settings.GooglePhotosAlbumId),
            GooglePhotosDescriptionTemplate = RedactPotentialSecrets(_settings.GooglePhotosDescriptionTemplate),
            OneDriveGraphApiBaseUrl = RedactPotentialSecrets(_settings.OneDriveGraphApiBaseUrl),
            OneDriveRemoteFolder = RedactPotentialSecrets(_settings.OneDriveRemoteFolder),
            _settings.OneDriveCreateAnonymousViewLink,
            LinearGraphqlEndpoint = RedactPotentialSecrets(_settings.LinearGraphqlEndpoint),
            LinearTeamId = RedactPotentialSecrets(_settings.LinearTeamId),
            LinearIssueId = RedactPotentialSecrets(_settings.LinearIssueId),
            LinearIssueTitleTemplate = RedactPotentialSecrets(_settings.LinearIssueTitleTemplate),
            _settings.LinearCreateAttachment,
            _settings.LinearUseOAuthBearerToken,
            GitHubApiBaseUrl = RedactPotentialSecrets(_settings.GitHubApiBaseUrl),
            GitHubRepository = RedactPotentialSecrets(_settings.GitHubRepository),
            GitHubIssueTitleTemplate = RedactPotentialSecrets(_settings.GitHubIssueTitleTemplate),
            GitHubLabels = RedactPotentialSecrets(_settings.GitHubLabels),
            GitHubAssignees = RedactPotentialSecrets(_settings.GitHubAssignees),
            JiraBaseUrl = RedactPotentialSecrets(_settings.JiraBaseUrl),
            JiraProjectKey = RedactPotentialSecrets(_settings.JiraProjectKey),
            JiraIssueType = RedactPotentialSecrets(_settings.JiraIssueType),
            JiraSummaryTemplate = RedactPotentialSecrets(_settings.JiraSummaryTemplate),
            JiraLabels = RedactPotentialSecrets(_settings.JiraLabels),
            JiraAccountEmail = RedactPotentialSecrets(_settings.JiraAccountEmail),
            AzureDevOpsBaseUrl = RedactPotentialSecrets(_settings.AzureDevOpsBaseUrl),
            AzureDevOpsOrganization = RedactPotentialSecrets(_settings.AzureDevOpsOrganization),
            AzureDevOpsProject = RedactPotentialSecrets(_settings.AzureDevOpsProject),
            AzureDevOpsWorkItemType = RedactPotentialSecrets(_settings.AzureDevOpsWorkItemType),
            AzureDevOpsTitleTemplate = RedactPotentialSecrets(_settings.AzureDevOpsTitleTemplate),
            AzureDevOpsTags = RedactPotentialSecrets(_settings.AzureDevOpsTags),
            AzureDevOpsAssignedTo = RedactPotentialSecrets(_settings.AzureDevOpsAssignedTo),
            _settings.OcrLanguageTag,
            CustomScriptCommand = RedactedIfConfigured(_settings.CustomScriptCommand),
            CustomWebhookUrl = RedactedIfConfigured(_settings.CustomWebhookUrl),
            _settings.EnableWatchFolders,
            _settings.WatchFolderIncludeSubdirectories,
            _settings.WatchFolderAutoImport,
            _settings.WatchFolderCopyImageAfterImport,
            _settings.WatchFolderStripMetadataAfterImport,
            _settings.WatchFolderShareAfterImport,
            UploadDenylistProcesses = _settings.UploadDenylistProcesses.Select(RedactPotentialSecrets).ToList(),
            AiDenylistProcesses = _settings.AiDenylistProcesses.Select(RedactPotentialSecrets).ToList(),
            WatchFolders = _settings.WatchFolders.Select(RedactPotentialSecrets).ToList(),
            AutomationRules = _settings.AutomationRules.Select(rule => new
            {
                rule.Id,
                Name = RedactPotentialSecrets(rule.Name),
                rule.IsEnabled,
                rule.Trigger,
                SourceAppContains = RedactPotentialSecrets(rule.SourceAppContains),
                WindowTitleContains = RedactPotentialSecrets(rule.WindowTitleContains),
                rule.FileExtension,
                rule.MaxFileSizeBytes,
                rule.ImageEffectMode,
                ImageEffectRegion = RedactPotentialSecrets(rule.ImageEffectRegion),
                rule.Actions
            }).ToList()
        };
    }

    private object BuildWorkspaceSummary()
    {
        var items = _workspaceStore.Load();
        return new
        {
            generatedAt = DateTimeOffset.Now,
            totalCaptures = items.Count,
            totalBytes = items.Sum(item => item.Bytes),
            privateIndexedCaptures = items.Count(item => item.IsPrivate),
            kindCounts = items
                .GroupBy(item => item.Kind)
                .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key.ToString(), group => group.Count()),
            sourceAppCounts = items
                .Where(item => !string.IsNullOrWhiteSpace(item.SourceApp))
                .GroupBy(item => item.SourceApp!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToDictionary(group => RedactPotentialSecrets(group.Key), group => group.Count()),
            latestItems = items
                .Take(MaxWorkspaceItems)
                .Select(item => new
                {
                    item.Id,
                    item.Kind,
                    item.CreatedAt,
                    fileName = RedactPotentialSecrets(item.FileName),
                    extension = Path.GetExtension(item.FilePath),
                    item.Width,
                    item.Height,
                    item.Bytes,
                    item.IsPrivate,
                    SourceApp = RedactPotentialSecrets(item.SourceApp ?? string.Empty),
                    hasOcrText = !string.IsNullOrWhiteSpace(item.OcrText),
                    ocrTextLength = item.OcrText?.Length ?? 0,
                    ocrLanguageTag = item.OcrLanguageTag ?? string.Empty,
                    ocrRecognizedAt = item.OcrRecognizedAt,
                    ocrWordCount = item.OcrWords.Count,
                    hasNotes = !string.IsNullOrWhiteSpace(item.Notes),
                    notesLength = item.Notes?.Length ?? 0
                })
                .ToList()
        };
    }

    private static string BuildAssemblyReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Loaded assemblies");
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Select(assembly => assembly.GetName())
                     .OrderBy(name => name.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"{assembly.Name} {assembly.Version}");
        }

        return builder.ToString();
    }

    private static string BuildPrivacyNotes()
    {
        return string.Join(
            Environment.NewLine,
            [
                "This diagnostic bundle is designed for support and local troubleshooting.",
                "It intentionally excludes capture files, thumbnails, full OCR text, and DPAPI secret files.",
                "Settings fields that can contain upload credentials or private infrastructure are redacted.",
                "Log files are included only when present, capped to 256 KB per file, and passed through Receipts sensitive-text redaction."
            ]);
    }

    private void AddRedactedLogs(ZipArchive archive, ICollection<string> entries, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.LogsRoot))
        {
            return;
        }

        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".log", ".txt", ".json", ".ndjson"
        };

        var logFiles = Directory
            .EnumerateFiles(_paths.LogsRoot)
            .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxLogFiles)
            .ToList();

        foreach (var file in logFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesToRead = (int)Math.Min(file.Length, MaxLogFileBytes);
            var buffer = new byte[bytesToRead];
            using (var stream = file.OpenRead())
            {
                _ = stream.Read(buffer, 0, bytesToRead);
            }

            var text = Encoding.UTF8.GetString(buffer);
            var redacted = RedactPotentialSecrets(text);
            if (file.Length > MaxLogFileBytes)
            {
                redacted += $"{Environment.NewLine}{Environment.NewLine}[TRUNCATED: original log was {file.Length:N0} bytes]";
            }

            AddTextEntry(archive, $"logs/{file.Name}", redacted, entries);
        }
    }

    private static void AddJsonEntry(ZipArchive archive, string entryName, object value, ICollection<string> entries)
    {
        AddTextEntry(archive, entryName, JsonSerializer.Serialize(value, JsonOptions), entries);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string text, ICollection<string> entries)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
        entries.Add(entryName);
    }

    private static string RedactedIfConfigured(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : "[redacted:configured]";
    }

    private static string RedactPotentialSecrets(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : SensitiveTextDetector.Redact(value);
    }
}

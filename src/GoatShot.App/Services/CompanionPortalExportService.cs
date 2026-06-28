using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class CompanionPortalExportService
{
    public const string ReportJsonFileName = "companion-portal-report.json";
    public const string ReportHtmlFileName = "index.html";
    public const string MediaReviewJsonFileName = "companion-portal-media-review.json";
    public const string MediaReviewHtmlFileName = "media-review.html";
    public const string MediaDirectoryName = "media";

    private const long DefaultMaxMediaBytes = 250L * 1024L * 1024L;

    private static readonly HashSet<string> SupportedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".mp4",
        ".webm",
        ".mov",
        ".mkv",
        ".avi"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly Func<DiagnosticSnapshot> _diagnosticsSnapshot;
    private readonly ShareService _sharing;

    public CompanionPortalExportService(
        AppPaths paths,
        AppSettings settings,
        DiagnosticsService diagnostics,
        ShareService sharing)
        : this(paths, settings, diagnostics.GetSnapshot, sharing)
    {
    }

    public CompanionPortalExportService(
        AppPaths paths,
        AppSettings settings,
        DiagnosticSnapshot diagnosticsSnapshot,
        ShareService sharing)
        : this(paths, settings, () => diagnosticsSnapshot, sharing)
    {
    }

    private CompanionPortalExportService(
        AppPaths paths,
        AppSettings settings,
        Func<DiagnosticSnapshot> diagnosticsSnapshot,
        ShareService sharing)
    {
        _paths = paths;
        _settings = settings;
        _diagnosticsSnapshot = diagnosticsSnapshot;
        _sharing = sharing;
    }

    public async Task<CompanionPortalExportResult> ExportAsync(
        CompanionPortalExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var outputRoot = ResolveOutputRoot(request.OutputPath);
        var mediaValidationIssues = ValidateMediaReviewRequest(request);
        if (mediaValidationIssues.Count > 0)
        {
            return new CompanionPortalExportResult
            {
                Succeeded = false,
                Message = "Companion portal media review export was not created: " + string.Join(" ", mediaValidationIssues.Select(RedactPortalText)),
                OutputRoot = outputRoot,
                Report = new CompanionPortalExportReport
                {
                    GeneratedAt = DateTimeOffset.Now,
                    Mode = BuildMode(request),
                    Boundary = BuildBoundary(request, localMediaReviewEnabled: false),
                    Issues = mediaValidationIssues.Select(RedactPortalText).ToList()
                }
            };
        }

        Directory.CreateDirectory(outputRoot);

        var mediaReview = await BuildMediaReviewAsync(request, outputRoot, cancellationToken);
        var report = BuildReport(request, outputRoot, mediaReview);
        var reportJsonPath = Path.Combine(outputRoot, ReportJsonFileName);
        var reportHtmlPath = Path.Combine(outputRoot, ReportHtmlFileName);

        await File.WriteAllTextAsync(
            reportJsonPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            reportHtmlPath,
            BuildHtml(report),
            cancellationToken);

        return new CompanionPortalExportResult
        {
            Succeeded = true,
            Message = request.HostedPreview
                ? report.MediaReview.Enabled
                    ? "Companion portal loopback-only read-only preview export created with operator-selected local media review pages. No remote portal contact, sync, upload, secret access, policy mutation, or write route was enabled."
                    : "Companion portal loopback-only read-only preview export created. No remote portal contact, sync, upload, media attachment, secret access, policy mutation, or write route was enabled."
                : report.MediaReview.Enabled
                    ? "Companion portal read-only local export created with operator-selected local media review pages. No hosting, sync, upload, secret access, policy mutation, or remote portal contact was performed."
                    : "Companion portal read-only local export created. No hosting, sync, upload, media attachment, secret access, policy mutation, or remote portal contact was performed.",
            OutputRoot = outputRoot,
            ReportJsonPath = reportJsonPath,
            ReportHtmlPath = reportHtmlPath,
            Report = report
        };
    }

    private CompanionPortalExportReport BuildReport(
        CompanionPortalExportRequest request,
        string outputRoot,
        CompanionPortalMediaReviewSummary mediaReview)
    {
        var generatedAt = DateTimeOffset.Now;
        var warnings = new List<string>();
        var issues = new List<string>();
        var generatedFiles = new List<string>
        {
            ReportJsonFileName,
            ReportHtmlFileName
        };
        generatedFiles.AddRange(mediaReview.GeneratedFiles);
        var report = new CompanionPortalExportReport
        {
            GeneratedAt = generatedAt,
            Mode = BuildMode(request),
            Boundary = BuildBoundary(request, mediaReview.Enabled),
            Policy = BuildPolicySummary(),
            Diagnostics = BuildDiagnosticsSummary(),
            ShareHistory = BuildShareHistorySummary(request.ShareHistoryLimit),
            ManualValidation = BuildManualValidationSummary(request.ManualValidationPath, request.ProofRootPath, warnings),
            ReleaseProof = BuildReleaseProofSummary(request.ProofRootPath, warnings),
            MediaReview = mediaReview,
            GeneratedFiles = generatedFiles,
            Warnings = warnings.Select(RedactPortalText).ToList(),
            Issues = issues.Select(RedactPortalText).ToList()
        };

        report.Summary = BuildReportSummary(report, outputRoot);
        return report;
    }

    private static string BuildMode(CompanionPortalExportRequest request)
    {
        if (!request.HostedPreview)
        {
            return "local-read-only-companion-portal-v0";
        }

        return request.RemoteClientsAllowed
            ? "self-hosted-read-only-companion-portal-preview-v0"
            : "local-loopback-companion-portal-preview-v0";
    }

    private CompanionPortalBoundarySummary BuildBoundary(CompanionPortalExportRequest request, bool localMediaReviewEnabled)
    {
        var hostedPreview = request.HostedPreview;
        var remoteClientsAllowed = hostedPreview && request.RemoteClientsAllowed;
        var authRequired = hostedPreview && request.AuthRequired;
        var accessControlMode = authRequired ? "shared-token" : "none";
        var includedSummaryCategories = new List<string>
        {
            "effective managed-policy summary",
            "redacted diagnostics text snippets",
            "aggregate share/upload history counts",
            "manual-validation lane status counts",
            "release-proof manifest metadata",
            "privacy and threat-boundary flags"
        };
        if (localMediaReviewEnabled)
        {
            includedSummaryCategories.Add("operator-selected local media review copies, hashes, and relative links");
        }

        var excludedCategories = new List<string>
        {
            "capture files except operator-selected media-review inputs",
            "recordings except operator-selected media-review inputs",
            "thumbnails",
            "OCR text",
            "transcripts",
            "prompt history",
            "raw logs",
            "diagnostic bundles",
            "provider credentials",
            "local file paths",
            "URLs from share history"
        };

        return new CompanionPortalBoundarySummary
        {
            Summary = hostedPreview
                ? remoteClientsAllowed
                    ? localMediaReviewEnabled
                        ? "This is an explicitly enabled self-hosted, read-only preview with shared-token access control and selected local media review pages. It is not a cloud portal, account system, sync client, upload flow, or team admin control plane."
                        : "This is an explicitly enabled self-hosted, read-only preview with shared-token access control. It is not a cloud portal, account system, sync client, media host, or team admin control plane."
                    : localMediaReviewEnabled
                        ? "This is a loopback-only, read-only local preview with explicitly selected local media review pages. It is not a cloud portal, sync client, account login, upload flow, hosted media service, or team admin control plane."
                        : "This is a loopback-only, read-only local preview for optional companion portal review. It is not a cloud portal, sync client, account login, media host, or team admin control plane."
                : localMediaReviewEnabled
                    ? "This is an offline, read-only export with explicitly selected local media review pages. It is not a hosted portal, sync client, account login, upload flow, hosted media service, or team admin control plane."
                    : "This is an offline, read-only export for optional companion portal review. It is not a hosted portal, sync client, account login, media host, or team admin control plane.",
            WouldHostPortal = hostedPreview,
            WouldContactPortal = false,
            WouldSync = false,
            WouldUpload = false,
            WouldAttachMedia = localMediaReviewEnabled,
            WouldReadSecrets = false,
            WouldMutatePolicy = false,
            WouldInstallOrRunPlugins = false,
            LoopbackOnly = hostedPreview && !remoteClientsAllowed,
            RemoteClientsAllowed = remoteClientsAllowed,
            MutatingRoutesEnabled = false,
            AccountLoginEnabled = false,
            AuthRequired = authRequired,
            AccessControlMode = accessControlMode,
            MediaReviewPagesEnabled = localMediaReviewEnabled,
            ConsentRequiredForFutureSync = true,
            LocalOnlyByDefault =
            [
                "raw captures",
                "recordings",
                "OCR text",
                "transcripts",
                "AI prompts and responses",
                "workspace index",
                "thumbnails",
                "workflow logs",
                "upload queue payloads",
                "browser-extension payloads",
                "Android captures",
                "print-import outputs",
                "plugin manifests",
                "diagnostic bundles",
                "local paths",
                "machine-specific settings"
            ],
            NeverSynced =
            [
                "DPAPI secret files",
                "decrypted credentials",
                "provider tokens",
                "webhook URLs",
                "custom script bodies",
                "browser cookies",
                "browser headers",
                "form values",
                "browser local/session storage",
                "raw DOM dumps",
                "clipboard contents unless explicitly saved or shared"
            ],
            IncludedSummaryCategories = includedSummaryCategories,
            ExcludedCategories = excludedCategories
        };
    }

    private List<string> ValidateMediaReviewRequest(CompanionPortalExportRequest request)
    {
        var mediaPaths = NormalizeMediaPaths(request.MediaPaths);
        var issues = new List<string>();
        if (mediaPaths.Count == 0)
        {
            return issues;
        }

        if (!request.AcceptMediaCopy)
        {
            issues.Add("Media review requires --accept-media-copy because selected files are copied into the local portal export.");
        }

        foreach (var path in mediaPaths)
        {
            if (!File.Exists(path))
            {
                issues.Add($"Media review input was not found: {path}");
                continue;
            }

            var extension = Path.GetExtension(path);
            if (!SupportedMediaExtensions.Contains(extension))
            {
                issues.Add($"Media review input has an unsupported extension: {path}");
                continue;
            }

            var length = new FileInfo(path).Length;
            if (length > Math.Max(1, request.MaxMediaBytes))
            {
                issues.Add($"Media review input exceeds the local copy limit: {path}");
            }
        }

        return issues;
    }

    private async Task<CompanionPortalMediaReviewSummary> BuildMediaReviewAsync(
        CompanionPortalExportRequest request,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var mediaPaths = NormalizeMediaPaths(request.MediaPaths);
        if (mediaPaths.Count == 0)
        {
            return new CompanionPortalMediaReviewSummary
            {
                Enabled = false,
                AcceptedMediaCopy = false,
                PrivacyNote = "Media review pages are disabled. No capture files, recordings, thumbnails, or selected media copies are included."
            };
        }

        var mediaRoot = Path.Combine(outputRoot, MediaDirectoryName);
        Directory.CreateDirectory(mediaRoot);
        var items = new List<CompanionPortalMediaReviewItem>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < mediaPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = mediaPaths[index];
            var source = new FileInfo(sourcePath);
            var fileName = BuildMediaReviewFileName(index + 1, source.Name, usedNames);
            var destinationPath = Path.Combine(mediaRoot, fileName);
            File.Copy(source.FullName, destinationPath, overwrite: true);
            var copied = new FileInfo(destinationPath);
            var relativePath = NormalizeRelativePath(Path.Combine(MediaDirectoryName, fileName));
            var contentType = MediaContentType(source.Extension);
            var bytes = await File.ReadAllBytesAsync(destinationPath, cancellationToken);
            items.Add(new CompanionPortalMediaReviewItem
            {
                Id = $"media-{index + 1:000}",
                DisplayName = RedactPortalText(source.Name),
                ReviewRelativePath = relativePath,
                ContentType = contentType,
                MediaKind = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video" : "image",
                Extension = source.Extension.ToLowerInvariant(),
                Bytes = copied.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                LastWriteTime = source.LastWriteTime
            });
        }

        var summary = new CompanionPortalMediaReviewSummary
        {
            Enabled = true,
            AcceptedMediaCopy = true,
            ItemCount = items.Count,
            TotalBytes = items.Sum(item => item.Bytes),
            ManifestRelativePath = MediaReviewJsonFileName,
            HtmlRelativePath = MediaReviewHtmlFileName,
            MediaDirectoryRelativePath = MediaDirectoryName,
            Items = items,
            GeneratedFiles = new List<string>
            {
                MediaReviewJsonFileName,
                MediaReviewHtmlFileName
            }.Concat(items.Select(item => item.ReviewRelativePath)).ToList(),
            PrivacyNote = "Only explicitly selected local media inputs were copied. The manifest stores relative review paths, display names, byte counts, hashes, and content types; source file paths, OCR text, transcripts, prompts, share URLs, secrets, sync tokens, and upload payloads are omitted.",
            BoundaryNotes =
            [
                "local static files only",
                "no portal account",
                "no remote clients",
                "no upload",
                "no sync",
                "no write routes",
                "no policy mutation"
            ]
        };

        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, MediaReviewJsonFileName),
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputRoot, MediaReviewHtmlFileName),
            BuildMediaReviewHtml(summary),
            cancellationToken);
        return summary;
    }

    private static List<string> NormalizeMediaPaths(IEnumerable<string> mediaPaths)
    {
        return mediaPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildMediaReviewFileName(int index, string sourceFileName, ISet<string> usedNames)
    {
        var extension = Path.GetExtension(sourceFileName).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
        baseName = Regex.Replace(baseName, @"[^A-Za-z0-9._-]+", "-").Trim('-', '.', '_');
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "selected-media";
        }

        if (baseName.Length > 64)
        {
            baseName = baseName[..64].Trim('-', '.', '_');
        }

        var candidate = $"{index:000}-{baseName}{extension}";
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = $"{index:000}-{baseName}-{suffix++}{extension}";
        }

        return candidate;
    }

    private CompanionPortalPolicySummary BuildPolicySummary()
    {
        var policy = ManagedPolicyService.LoadEffective(_settings);
        return new CompanionPortalPolicySummary
        {
            Source = RedactPortalText(policy.Source),
            Summary = RedactPortalText(policy.Summary),
            DisableAi = policy.DisableAi,
            DisableUploads = policy.DisableUploads,
            DisableCustomScripts = policy.DisableCustomScripts,
            DisableCustomWebhooks = policy.DisableCustomWebhooks,
            DisableLocalPlugins = policy.DisableLocalPlugins,
            DisableBrowserExtension = policy.DisableBrowserExtension,
            DisableAndroidCapture = policy.DisableAndroidCapture,
            DisableVirtualPrinterImport = policy.DisableVirtualPrinterImport,
            RequirePrivateCaptureMode = policy.RequirePrivateCaptureMode,
            DisableDiagnosticsLogCapture = policy.DisableDiagnosticsLogCapture,
            RetentionDays = policy.RetentionDays,
            AllowedShareDestinations = policy.AllowedShareDestinations
                .Select(destination => destination.ToString())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AllowedPluginIdCount = policy.AllowedPluginIds.Count,
            AllowedPluginActionIdCount = policy.AllowedPluginActionIds.Count
        };
    }

    private CompanionPortalDiagnosticsSummary BuildDiagnosticsSummary()
    {
        var snapshot = _diagnosticsSnapshot();
        var sections = new List<CompanionPortalDiagnosticsSection>
        {
            BuildDiagnosticsSection("capture", snapshot.CaptureEngine),
            BuildDiagnosticsSection("recording", snapshot.RecordingEngine),
            BuildDiagnosticsSection("recording-readiness", snapshot.RecordingReadiness),
            BuildDiagnosticsSection("encoder", snapshot.EncoderStatus),
            BuildDiagnosticsSection("ocr", snapshot.OcrStatus),
            BuildDiagnosticsSection("ai", snapshot.AiStatus),
            BuildDiagnosticsSection("policy", snapshot.PolicyStatus),
            BuildDiagnosticsSection("startup", snapshot.StartupStatus),
            BuildDiagnosticsSection("metadata-index", snapshot.MetadataIndexStatus),
            BuildDiagnosticsSection("upload-queue", snapshot.UploadQueueStatus),
            BuildDiagnosticsSection("print-import", snapshot.PrintImportStatus),
            BuildDiagnosticsSection("plugins", snapshot.PluginStatus),
            BuildDiagnosticsSection("browser-bridge", snapshot.BrowserBridgeStatus),
            BuildDiagnosticsSection("sharing", snapshot.SharingStatus)
        };

        return new CompanionPortalDiagnosticsSummary
        {
            OsDescription = RedactPortalText(snapshot.OsDescription),
            RuntimeDescription = RedactPortalText(snapshot.RuntimeDescription),
            SectionCount = sections.Count,
            SensitiveFindingCount = sections.Sum(section => section.SensitiveFindingCount),
            Sections = sections
        };
    }

    private CompanionPortalDiagnosticsSection BuildDiagnosticsSection(string name, string text)
    {
        var findings = SensitiveTextDetector.Find(text);
        return new CompanionPortalDiagnosticsSection
        {
            Name = name,
            SensitiveFindingCount = findings.Count,
            Summary = LimitText(RedactPortalText(text), 900)
        };
    }

    private CompanionPortalShareHistorySummary BuildShareHistorySummary(int limit)
    {
        var clampedLimit = Math.Clamp(limit, 1, 500);
        var entries = _sharing.SearchHistory(limit: clampedLimit);
        var rawHistorySensitiveFindings = CountRawShareHistorySensitiveFindings();
        var byDestination = entries
            .GroupBy(entry => entry.Destination)
            .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CompanionPortalShareDestinationSummary
            {
                Destination = group.Key.ToString(),
                Total = group.Count(),
                Succeeded = group.Count(entry => entry.Succeeded),
                Failed = group.Count(entry => !entry.Succeeded),
                External = ShareService.IsExternalDestination(group.Key)
            })
            .ToList();

        return new CompanionPortalShareHistorySummary
        {
            EntryLimit = clampedLimit,
            TotalEntries = entries.Count,
            ExternalEntries = entries.Count(entry => entry.ExternalDestination),
            LocalEntries = entries.Count(entry => !entry.ExternalDestination),
            SucceededEntries = entries.Count(entry => entry.Succeeded),
            FailedEntries = entries.Count(entry => !entry.Succeeded),
            LatestEntryAt = entries.Count == 0 ? null : entries.Max(entry => entry.CreatedAt),
            RawHistorySensitiveFindingCount = rawHistorySensitiveFindings.Count,
            RawHistorySensitiveFindingKinds = rawHistorySensitiveFindings
                .Select(finding => finding.Kind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ByDestination = byDestination,
            PrivacyNote = "Only aggregate counts are exported. Capture IDs, filenames, file paths, share URLs, provider messages, and retry payloads are omitted."
        };
    }

    private IReadOnlyList<SensitiveFinding> CountRawShareHistorySensitiveFindings()
    {
        if (!File.Exists(_paths.ShareHistoryPath))
        {
            return Array.Empty<SensitiveFinding>();
        }

        try
        {
            return SensitiveTextDetector.Find(File.ReadAllText(_paths.ShareHistoryPath));
        }
        catch
        {
            return Array.Empty<SensitiveFinding>();
        }
    }

    private CompanionPortalManualValidationSummary BuildManualValidationSummary(
        string? manualValidationPath,
        string? proofRootPath,
        List<string> warnings)
    {
        var summaryPath = ResolveManualValidationSummaryPath(manualValidationPath, proofRootPath);
        if (string.IsNullOrWhiteSpace(summaryPath))
        {
            warnings.Add("No manual-validation summary was supplied or found; manual proof remains outside this export.");
            return new CompanionPortalManualValidationSummary
            {
                Included = false,
                PrivacyNote = "Manual-validation evidence is not embedded. Supply --manual-validation to include status counts from an existing summary JSON."
            };
        }

        try
        {
            var json = File.ReadAllText(summaryPath);
            var summary = JsonSerializer.Deserialize<ManualValidationSummaryResult>(json, JsonOptions);
            if (summary is null)
            {
                warnings.Add($"Manual-validation summary could not be read: {summaryPath}");
                return new CompanionPortalManualValidationSummary { Included = false };
            }

            var statusCounts = summary.Lanes
                .GroupBy(lane => lane.Status)
                .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.OrdinalIgnoreCase);
            return new CompanionPortalManualValidationSummary
            {
                Included = true,
                Succeeded = summary.Succeeded,
                Message = RedactPortalText(summary.Message),
                GeneratedAt = summary.GeneratedAt,
                LaneCount = summary.Lanes.Count,
                StatusCounts = statusCounts,
                RedactionStatus = RedactPortalText(summary.Redaction.Status),
                SensitiveFindingCount = summary.Redaction.Findings.Sum(finding => finding.Count),
                DiagnosticsBundleExists = summary.Diagnostics.DiagnosticsBundleExists,
                IssueCount = summary.Issues.Count,
                WarningCount = summary.Warnings.Count,
                Issues = summary.Issues.Take(20).Select(RedactPortalText).ToList(),
                Warnings = summary.Warnings.Take(20).Select(RedactPortalText).ToList(),
                Lanes = summary.Lanes
                    .Select(lane => new CompanionPortalManualValidationLaneSummary
                    {
                        Id = RedactPortalText(lane.Id),
                        Title = RedactPortalText(lane.Title),
                        OAuthParked = lane.OAuthParked,
                        Exists = lane.Exists,
                        Status = lane.Status.ToString(),
                        EvidenceCount = lane.EvidenceCount,
                        IssueCount = lane.Issues.Count,
                        Issues = lane.Issues.Take(5).Select(RedactPortalText).ToList()
                    })
                    .ToList(),
                PrivacyNote = "Only lane statuses, counts, and redacted issue text are exported. Evidence files, screenshots, recordings, diagnostics bundles, and local evidence paths are omitted."
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Manual-validation summary failed to load: {ex.GetType().Name}: {ex.Message}");
            return new CompanionPortalManualValidationSummary { Included = false };
        }
    }

    private CompanionPortalReleaseProofSummary BuildReleaseProofSummary(string? proofRootPath, List<string> warnings)
    {
        var root = ResolveProofRoot(proofRootPath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            warnings.Add("Release proof root was not found; release proof metadata is not included.");
            return new CompanionPortalReleaseProofSummary
            {
                Included = false,
                PrivacyNote = "Release proof metadata is absent because no proof root was found."
            };
        }

        var manifests = Directory
            .EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(10)
            .ToList();
        var zips = Directory
            .EnumerateFiles(root, "GoatShot-release-proof-*.zip", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(10)
            .ToList();
        var latestManifest = manifests.FirstOrDefault();
        var latestZip = zips.FirstOrDefault();
        var commands = latestManifest is null
            ? new List<CompanionPortalReleaseProofCommandSummary>()
            : ReadReleaseProofCommands(latestManifest.FullName, warnings);

        return new CompanionPortalReleaseProofSummary
        {
            Included = manifests.Count > 0 || zips.Count > 0,
            ManifestCount = manifests.Count,
            ZipCount = zips.Count,
            LatestManifest = latestManifest is null ? null : ToArtifactFileSummary(latestManifest, root),
            LatestZip = latestZip is null ? null : ToArtifactFileSummary(latestZip, root),
            CommandCount = commands.Count,
            PassedCommands = commands.Count(command => command.Status.Equals("passed", StringComparison.OrdinalIgnoreCase)),
            FailedCommands = commands.Count(command => command.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)),
            Commands = commands,
            PrivacyNote = "Only proof manifest metadata is exported. Command arguments, logs, packaged files, diagnostics bundles, and absolute paths are omitted."
        };
    }

    private List<CompanionPortalReleaseProofCommandSummary> ReadReleaseProofCommands(string manifestPath, List<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("commands", out var commandsElement) ||
                commandsElement.ValueKind != JsonValueKind.Array)
            {
                return new List<CompanionPortalReleaseProofCommandSummary>();
            }

            var commands = new List<CompanionPortalReleaseProofCommandSummary>();
            foreach (var commandElement in commandsElement.EnumerateArray())
            {
                commands.Add(new CompanionPortalReleaseProofCommandSummary
                {
                    Name = RedactPortalText(ReadString(commandElement, "name")),
                    Status = RedactPortalText(ReadString(commandElement, "status")),
                    ExitCode = ReadNullableInt(commandElement, "exitCode")
                });
            }

            return commands;
        }
        catch (Exception ex)
        {
            warnings.Add($"Release proof manifest failed to load: {ex.GetType().Name}: {ex.Message}");
            return new List<CompanionPortalReleaseProofCommandSummary>();
        }
    }

    private CompanionPortalArtifactFileSummary ToArtifactFileSummary(FileInfo file, string root)
    {
        return new CompanionPortalArtifactFileSummary
        {
            RelativePath = NormalizeRelativePath(Path.GetRelativePath(root, file.FullName)),
            Bytes = file.Length,
            LastWriteTime = file.LastWriteTime
        };
    }

    private string? ResolveManualValidationSummaryPath(string? manualValidationPath, string? proofRootPath)
    {
        if (!string.IsNullOrWhiteSpace(manualValidationPath))
        {
            var supplied = Path.GetFullPath(Environment.ExpandEnvironmentVariables(manualValidationPath));
            if (File.Exists(supplied))
            {
                return supplied;
            }

            var summaryPath = Path.Combine(supplied, ManualValidationSummaryService.SummaryJsonFileName);
            return File.Exists(summaryPath) ? summaryPath : null;
        }

        var roots = new List<string>();
        var proofRoot = ResolveProofRoot(proofRootPath);
        if (!string.IsNullOrWhiteSpace(proofRoot))
        {
            roots.Add(Path.Combine(proofRoot, "manual-validation"));
        }

        roots.Add(Path.Combine(Environment.CurrentDirectory, "artifacts", "manual-validation"));
        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, ManualValidationSummaryService.SummaryJsonFileName, SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static string? ResolveProofRoot(string? proofRootPath)
    {
        if (!string.IsNullOrWhiteSpace(proofRootPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(proofRootPath));
        }

        var defaultRoot = Path.Combine(Environment.CurrentDirectory, "artifacts");
        return Directory.Exists(defaultRoot) ? defaultRoot : null;
    }

    private string ResolveOutputRoot(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(_paths.LocalRoot, "companion-portal-export", stamp);
    }

    private CompanionPortalReportSummary BuildReportSummary(CompanionPortalExportReport report, string outputRoot)
    {
        return new CompanionPortalReportSummary
        {
            Title = report.Boundary.WouldHostPortal
                ? report.Boundary.RemoteClientsAllowed
                    ? "GoatShot companion portal self-hosted preview"
                    : "GoatShot companion portal loopback preview"
                : "GoatShot companion portal read-only local export",
            OutputRootLabel = RedactPortalText(outputRoot),
            BoundarySummary = report.Boundary.Summary,
            ProofSummary = report.ReleaseProof.Included
                ? $"{report.ReleaseProof.PassedCommands}/{report.ReleaseProof.CommandCount} release-proof command(s) passed in the latest manifest."
                : "No release-proof metadata included.",
            ManualValidationSummary = report.ManualValidation.Included
                ? $"{report.ManualValidation.LaneCount} manual-validation lane(s) summarized; redaction status: {report.ManualValidation.RedactionStatus}."
                : "No manual-validation summary included.",
            ShareHistorySummary = $"{report.ShareHistory.TotalEntries} share/upload history entrie(s) summarized as aggregate counts.",
            MediaReviewSummary = report.MediaReview.Enabled
                ? $"{report.MediaReview.ItemCount} operator-selected local media item(s) copied for static review; no source paths, sync payloads, upload payloads, or credentials are included."
                : "No media review files included.",
            PrivacySummary = report.MediaReview.Enabled
                ? "Only explicitly selected media-review copies are included. Thumbnails, OCR text, transcripts, prompts, raw logs, provider URLs, source local paths, secrets, credentials, sync payloads, and upload payloads are omitted."
                : "No capture files, recordings, thumbnails, OCR text, transcripts, prompts, raw logs, provider URLs, local file paths, secrets, or credentials are included."
        };
    }

    private static string BuildHtml(CompanionPortalExportReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>GoatShot Companion Portal Export</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;line-height:1.45;color:#14171a;background:#f8fafc}main{max-width:1040px;margin:0 auto}section{background:#fff;border:1px solid #d7dde5;border-radius:8px;padding:18px;margin:16px 0}h1{font-size:28px;margin:0 0 8px}h2{font-size:18px;margin:0 0 12px}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #e6ebf0;text-align:left;padding:8px}code{background:#eef2f7;padding:2px 5px;border-radius:4px}.flag{display:inline-block;margin:3px 8px 3px 0;padding:3px 7px;border-radius:999px;background:#e9f7f2;color:#145b45;font-size:12px}.warn{background:#fff6df;color:#6f4b00}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body><main>");
        builder.AppendLine($"<h1>{Html(report.Summary.Title)}</h1>");
        builder.AppendLine($"<p>{Html(report.Boundary.Summary)}</p>");
        builder.AppendLine("<section><h2>Mutation Boundary</h2>");
        AppendFlag(builder, "host", report.Boundary.WouldHostPortal);
        AppendFlag(builder, "contact portal", report.Boundary.WouldContactPortal);
        AppendFlag(builder, "sync", report.Boundary.WouldSync);
        AppendFlag(builder, "upload", report.Boundary.WouldUpload);
        AppendFlag(builder, "attach media", report.Boundary.WouldAttachMedia);
        AppendFlag(builder, "read secrets", report.Boundary.WouldReadSecrets);
        AppendFlag(builder, "mutate policy", report.Boundary.WouldMutatePolicy);
        builder.AppendLine($"<p>{Html(report.Summary.PrivacySummary)}</p></section>");

        builder.AppendLine("<section><h2>Policy</h2>");
        builder.AppendLine($"<p>{Html(report.Policy.Summary)}</p>");
        builder.AppendLine($"<p>Source: <code>{Html(report.Policy.Source)}</code></p></section>");

        builder.AppendLine("<section><h2>Share History</h2>");
        builder.AppendLine($"<p>{Html(report.ShareHistory.PrivacyNote)}</p>");
        builder.AppendLine("<table><thead><tr><th>Destination</th><th>Total</th><th>Succeeded</th><th>Failed</th><th>External</th></tr></thead><tbody>");
        foreach (var destination in report.ShareHistory.ByDestination)
        {
            builder.AppendLine($"<tr><td>{Html(destination.Destination)}</td><td>{destination.Total}</td><td>{destination.Succeeded}</td><td>{destination.Failed}</td><td>{destination.External}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");

        builder.AppendLine("<section><h2>Manual Validation</h2>");
        builder.AppendLine($"<p>{Html(report.Summary.ManualValidationSummary)}</p>");
        builder.AppendLine("<table><thead><tr><th>Lane</th><th>Status</th><th>Evidence Count</th></tr></thead><tbody>");
        foreach (var lane in report.ManualValidation.Lanes)
        {
            builder.AppendLine($"<tr><td>{Html(lane.Title)}</td><td>{Html(lane.Status)}</td><td>{lane.EvidenceCount}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");

        builder.AppendLine("<section><h2>Release Proof</h2>");
        builder.AppendLine($"<p>{Html(report.Summary.ProofSummary)}</p>");
        builder.AppendLine("<table><thead><tr><th>Command</th><th>Status</th><th>Exit</th></tr></thead><tbody>");
        foreach (var command in report.ReleaseProof.Commands)
        {
            builder.AppendLine($"<tr><td>{Html(command.Name)}</td><td>{Html(command.Status)}</td><td>{command.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");

        builder.AppendLine("<section><h2>Media Review</h2>");
        builder.AppendLine($"<p>{Html(report.MediaReview.PrivacyNote)}</p>");
        if (report.MediaReview.Enabled)
        {
            builder.AppendLine($"<p><a href=\"{Html(report.MediaReview.HtmlRelativePath)}\">Open media review page</a> ({report.MediaReview.ItemCount} item(s), {report.MediaReview.TotalBytes} bytes)</p>");
            builder.AppendLine("<table><thead><tr><th>Media</th><th>Kind</th><th>Bytes</th><th>SHA-256</th></tr></thead><tbody>");
            foreach (var item in report.MediaReview.Items)
            {
                builder.AppendLine($"<tr><td><a href=\"{Html(item.ReviewRelativePath)}\">{Html(item.DisplayName)}</a></td><td>{Html(item.MediaKind)}</td><td>{item.Bytes}</td><td><code>{Html(item.Sha256)}</code></td></tr>");
            }

            builder.AppendLine("</tbody></table>");
        }
        else
        {
            builder.AppendLine("<p>Media review pages are not enabled for this export.</p>");
        }

        builder.AppendLine("</section>");

        builder.AppendLine("<section><h2>Diagnostics</h2>");
        builder.AppendLine($"<p>OS: {Html(report.Diagnostics.OsDescription)}<br>Runtime: {Html(report.Diagnostics.RuntimeDescription)}</p>");
        foreach (var section in report.Diagnostics.Sections)
        {
            builder.AppendLine($"<h3>{Html(section.Name)}</h3><p>{Html(section.Summary)}</p>");
        }

        builder.AppendLine("</section>");
        if (report.Warnings.Count > 0)
        {
            builder.AppendLine("<section><h2>Warnings</h2>");
            foreach (var warning in report.Warnings)
            {
                builder.AppendLine($"<p class=\"warn\">{Html(warning)}</p>");
            }

            builder.AppendLine("</section>");
        }

        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }

    private static string BuildMediaReviewHtml(CompanionPortalMediaReviewSummary mediaReview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>GoatShot Companion Portal Media Review</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;line-height:1.45;color:#14171a;background:#f8fafc}main{max-width:1040px;margin:0 auto}.item{background:#fff;border:1px solid #d7dde5;border-radius:8px;padding:18px;margin:16px 0}img,video{display:block;max-width:100%;max-height:640px;background:#eef2f7;border:1px solid #d7dde5}code{background:#eef2f7;padding:2px 5px;border-radius:4px}.meta{color:#46515f;font-size:13px}.boundary{background:#e9f7f2;color:#145b45;display:inline-block;margin:3px 8px 3px 0;padding:3px 7px;border-radius:999px;font-size:12px}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body><main>");
        builder.AppendLine("<h1>GoatShot companion portal media review</h1>");
        builder.AppendLine($"<p>{Html(mediaReview.PrivacyNote)}</p>");
        foreach (var note in mediaReview.BoundaryNotes)
        {
            builder.AppendLine($"<span class=\"boundary\">{Html(note)}</span>");
        }

        foreach (var item in mediaReview.Items)
        {
            builder.AppendLine("<article class=\"item\">");
            builder.AppendLine($"<h2>{Html(item.DisplayName)}</h2>");
            if (item.MediaKind.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"<video controls preload=\"metadata\" src=\"{Html(item.ReviewRelativePath)}\"></video>");
            }
            else
            {
                builder.AppendLine($"<img src=\"{Html(item.ReviewRelativePath)}\" alt=\"{Html(item.DisplayName)}\">");
            }

            builder.AppendLine($"<p class=\"meta\">kind={Html(item.MediaKind)} content-type={Html(item.ContentType)} bytes={item.Bytes} sha256=<code>{Html(item.Sha256)}</code></p>");
            builder.AppendLine("</article>");
        }

        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }

    private static void AppendFlag(StringBuilder builder, string label, bool value)
    {
        builder.AppendLine($"<span class=\"flag\">would {Html(label)}: {value.ToString().ToLowerInvariant()}</span>");
    }

    private string RedactPortalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = value;
        redacted = ReplaceKnownPath(redacted, _paths.LocalRoot, "[LOCAL_ROOT]");
        redacted = ReplaceKnownPath(redacted, _paths.LibraryRoot, "[LIBRARY_ROOT]");
        redacted = ReplaceKnownPath(redacted, Environment.CurrentDirectory, "[REPO_ROOT]");
        redacted = SensitiveTextDetector.Redact(redacted);
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b[A-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*",
            "[LOCAL_PATH]");
        redacted = Regex.Replace(redacted, @"\\\\[^\s""<>|]+", "[LOCAL_PATH]");
        return redacted;
    }

    private static string ReplaceKnownPath(string value, string? path, string replacement)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return value;
        }

        var normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return value;
        }

        return Regex.Replace(value, Regex.Escape(normalized), replacement, RegexOptions.IgnoreCase);
    }

    private static string LimitText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static string MediaContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            _ => "application/octet-stream"
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}

public sealed class CompanionPortalExportRequest
{
    public string? OutputPath { get; set; }
    public string? ManualValidationPath { get; set; }
    public string? ProofRootPath { get; set; }
    public int ShareHistoryLimit { get; set; } = 200;
    public bool HostedPreview { get; set; }
    public bool RemoteClientsAllowed { get; set; }
    public bool AuthRequired { get; set; }
    public List<string> MediaPaths { get; set; } = new();
    public bool AcceptMediaCopy { get; set; }
    public long MaxMediaBytes { get; set; } = 250L * 1024L * 1024L;
}

public sealed class CompanionPortalExportResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public string ReportJsonPath { get; set; } = string.Empty;
    public string ReportHtmlPath { get; set; } = string.Empty;
    public CompanionPortalExportReport Report { get; set; } = new();
}

public sealed class CompanionPortalExportReport
{
    public int ManifestVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAt { get; set; }
    public string Product { get; set; } = "GoatShot";
    public string Mode { get; set; } = string.Empty;
    public CompanionPortalReportSummary Summary { get; set; } = new();
    public CompanionPortalBoundarySummary Boundary { get; set; } = new();
    public CompanionPortalPolicySummary Policy { get; set; } = new();
    public CompanionPortalDiagnosticsSummary Diagnostics { get; set; } = new();
    public CompanionPortalShareHistorySummary ShareHistory { get; set; } = new();
    public CompanionPortalManualValidationSummary ManualValidation { get; set; } = new();
    public CompanionPortalReleaseProofSummary ReleaseProof { get; set; } = new();
    public CompanionPortalMediaReviewSummary MediaReview { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public sealed class CompanionPortalReportSummary
{
    public string Title { get; set; } = string.Empty;
    public string OutputRootLabel { get; set; } = string.Empty;
    public string BoundarySummary { get; set; } = string.Empty;
    public string ProofSummary { get; set; } = string.Empty;
    public string ManualValidationSummary { get; set; } = string.Empty;
    public string ShareHistorySummary { get; set; } = string.Empty;
    public string MediaReviewSummary { get; set; } = string.Empty;
    public string PrivacySummary { get; set; } = string.Empty;
}

public sealed class CompanionPortalBoundarySummary
{
    public string Summary { get; set; } = string.Empty;
    public bool WouldHostPortal { get; set; }
    public bool WouldContactPortal { get; set; }
    public bool WouldSync { get; set; }
    public bool WouldUpload { get; set; }
    public bool WouldAttachMedia { get; set; }
    public bool WouldReadSecrets { get; set; }
    public bool WouldMutatePolicy { get; set; }
    public bool WouldInstallOrRunPlugins { get; set; }
    public bool LoopbackOnly { get; set; }
    public bool RemoteClientsAllowed { get; set; }
    public bool MutatingRoutesEnabled { get; set; }
    public bool AccountLoginEnabled { get; set; }
    public bool AuthRequired { get; set; }
    public string AccessControlMode { get; set; } = string.Empty;
    public bool MediaReviewPagesEnabled { get; set; }
    public bool ConsentRequiredForFutureSync { get; set; }
    public List<string> LocalOnlyByDefault { get; set; } = new();
    public List<string> NeverSynced { get; set; } = new();
    public List<string> IncludedSummaryCategories { get; set; } = new();
    public List<string> ExcludedCategories { get; set; } = new();
}

public sealed class CompanionPortalPolicySummary
{
    public string Source { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool DisableAi { get; set; }
    public bool DisableUploads { get; set; }
    public bool DisableCustomScripts { get; set; }
    public bool DisableCustomWebhooks { get; set; }
    public bool DisableLocalPlugins { get; set; }
    public bool DisableBrowserExtension { get; set; }
    public bool DisableAndroidCapture { get; set; }
    public bool DisableVirtualPrinterImport { get; set; }
    public bool RequirePrivateCaptureMode { get; set; }
    public bool DisableDiagnosticsLogCapture { get; set; }
    public int RetentionDays { get; set; }
    public List<string> AllowedShareDestinations { get; set; } = new();
    public int AllowedPluginIdCount { get; set; }
    public int AllowedPluginActionIdCount { get; set; }
}

public sealed class CompanionPortalDiagnosticsSummary
{
    public string OsDescription { get; set; } = string.Empty;
    public string RuntimeDescription { get; set; } = string.Empty;
    public int SectionCount { get; set; }
    public int SensitiveFindingCount { get; set; }
    public List<CompanionPortalDiagnosticsSection> Sections { get; set; } = new();
}

public sealed class CompanionPortalDiagnosticsSection
{
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int SensitiveFindingCount { get; set; }
}

public sealed class CompanionPortalShareHistorySummary
{
    public int EntryLimit { get; set; }
    public int TotalEntries { get; set; }
    public int ExternalEntries { get; set; }
    public int LocalEntries { get; set; }
    public int SucceededEntries { get; set; }
    public int FailedEntries { get; set; }
    public DateTimeOffset? LatestEntryAt { get; set; }
    public int RawHistorySensitiveFindingCount { get; set; }
    public List<string> RawHistorySensitiveFindingKinds { get; set; } = new();
    public List<CompanionPortalShareDestinationSummary> ByDestination { get; set; } = new();
    public string PrivacyNote { get; set; } = string.Empty;
}

public sealed class CompanionPortalShareDestinationSummary
{
    public string Destination { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool External { get; set; }
}

public sealed class CompanionPortalManualValidationSummary
{
    public bool Included { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public int LaneCount { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string RedactionStatus { get; set; } = string.Empty;
    public int SensitiveFindingCount { get; set; }
    public bool DiagnosticsBundleExists { get; set; }
    public int IssueCount { get; set; }
    public int WarningCount { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<CompanionPortalManualValidationLaneSummary> Lanes { get; set; } = new();
    public string PrivacyNote { get; set; } = string.Empty;
}

public sealed class CompanionPortalManualValidationLaneSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool OAuthParked { get; set; }
    public bool Exists { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public int IssueCount { get; set; }
    public List<string> Issues { get; set; } = new();
}

public sealed class CompanionPortalReleaseProofSummary
{
    public bool Included { get; set; }
    public int ManifestCount { get; set; }
    public int ZipCount { get; set; }
    public CompanionPortalArtifactFileSummary? LatestManifest { get; set; }
    public CompanionPortalArtifactFileSummary? LatestZip { get; set; }
    public int CommandCount { get; set; }
    public int PassedCommands { get; set; }
    public int FailedCommands { get; set; }
    public List<CompanionPortalReleaseProofCommandSummary> Commands { get; set; } = new();
    public string PrivacyNote { get; set; } = string.Empty;
}

public sealed class CompanionPortalArtifactFileSummary
{
    public string RelativePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public DateTime LastWriteTime { get; set; }
}

public sealed class CompanionPortalReleaseProofCommandSummary
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
}

public sealed class CompanionPortalMediaReviewSummary
{
    public bool Enabled { get; set; }
    public bool AcceptedMediaCopy { get; set; }
    public int ItemCount { get; set; }
    public long TotalBytes { get; set; }
    public string ManifestRelativePath { get; set; } = string.Empty;
    public string HtmlRelativePath { get; set; } = string.Empty;
    public string MediaDirectoryRelativePath { get; set; } = string.Empty;
    public List<CompanionPortalMediaReviewItem> Items { get; set; } = new();
    public List<string> GeneratedFiles { get; set; } = new();
    public string PrivacyNote { get; set; } = string.Empty;
    public List<string> BoundaryNotes { get; set; } = new();
}

public sealed class CompanionPortalMediaReviewItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ReviewRelativePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string MediaKind { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime LastWriteTime { get; set; }
}

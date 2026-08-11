using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CompanionPortalExportServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public async Task ExportAsync_WritesReadOnlyStaticReportWithoutPortalMutationFlags()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var item = CreateCaptureItem(paths, "local-export.png", 32);
            var localShare = await sharing.ShareAsync(item, ShareDestination.LocalFolder, CancellationToken.None);
            var proofRoot = await WriteReleaseProofAsync(paths);
            var manualRoot = await WriteManualValidationSummaryAsync(paths, succeeded: false);
            var service = CreateService(paths, settings, sharing);

            var result = await service.ExportAsync(new CompanionPortalExportRequest
            {
                OutputPath = Path.Combine(paths.LocalRoot, "portal-export"),
                ManualValidationPath = manualRoot,
                ProofRootPath = proofRoot,
                ShareHistoryLimit = 50
            });

            Assert.IsTrue(localShare.Succeeded, localShare.Message);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(File.Exists(result.ReportJsonPath));
            Assert.IsTrue(File.Exists(result.ReportHtmlPath));
            Assert.IsFalse(result.Report.Boundary.WouldHostPortal);
            Assert.IsFalse(result.Report.Boundary.WouldContactPortal);
            Assert.IsFalse(result.Report.Boundary.WouldSync);
            Assert.IsFalse(result.Report.Boundary.WouldUpload);
            Assert.IsFalse(result.Report.Boundary.WouldAttachMedia);
            Assert.IsFalse(result.Report.Boundary.WouldReadSecrets);
            Assert.IsFalse(result.Report.Boundary.WouldMutatePolicy);
            Assert.AreEqual(1, result.Report.ShareHistory.TotalEntries);
            Assert.AreEqual(1, result.Report.ShareHistory.LocalEntries);
            Assert.IsTrue(result.Report.ManualValidation.Included);
            Assert.IsTrue(result.Report.ManualValidation.StatusCounts.ContainsKey(nameof(ManualValidationLaneStatus.Passed)));
            Assert.IsTrue(result.Report.ReleaseProof.Included);
            Assert.AreEqual(2, result.Report.ReleaseProof.ZipCount);
            StringAssert.Contains(result.Report.ReleaseProof.LatestZip?.RelativePath ?? string.Empty, "Receipts-release-proof-");
            Assert.AreEqual(2, result.Report.ReleaseProof.CommandCount);
            Assert.AreEqual(2, result.Report.ReleaseProof.PassedCommands);
            StringAssert.Contains(File.ReadAllText(result.ReportHtmlPath), "would sync: false");
        });
    }

    [TestMethod]
    public async Task ExportAsync_RedactsSensitiveTextAndOmitsRawHistoryAndLocalPaths()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var secret = "Bearer abcdefghijklmnopqrstuvwxyz1234567890";
            await File.WriteAllTextAsync(
                paths.ShareHistoryPath,
                JsonSerializer.Serialize(new[]
                {
                    new ShareHistoryEntry
                    {
                        FilePath = Path.Combine(paths.ImagesRoot, "secret-capture.png"),
                        Bytes = 12,
                        Destination = ShareDestination.CustomWebhook,
                        ExternalDestination = true,
                        Succeeded = false,
                        Message = $"Provider failed with {secret}",
                        Url = "https://example.test/upload?token=super-secret-token"
                    }
                }, JsonOptions));
            var proofRoot = await WriteReleaseProofAsync(paths);
            var manualRoot = await WriteManualValidationSummaryAsync(
                paths,
                succeeded: false,
                issue: "Operator note included api_key=abcdefghijklmnop and path " + paths.LibraryRoot);
            var service = CreateService(
                paths,
                settings,
                sharing,
                diagnostics: new DiagnosticSnapshot
                {
                    OsDescription = "Windows",
                    RuntimeDescription = ".NET test runtime",
                    CaptureEngine = "Capture OK at " + paths.LocalRoot,
                    RecordingEngine = "Recording disabled for token=" + "abcdefghijklmnop",
                    RecordingReadiness = "Ready",
                    EncoderStatus = "Encoder path " + paths.LibraryRoot,
                    OcrStatus = "OCR local-only",
                    AiStatus = "AI disabled",
                    PolicyStatus = "Policy source " + paths.LocalRoot,
                    StartupStatus = "Startup OK",
                    MetadataIndexStatus = "Index OK",
                    UploadQueueStatus = "Queue empty",
                    PrintImportStatus = "Drop folder " + Path.Combine(paths.LibraryRoot, "PrintDrop"),
                    PluginStatus = "Plugins local-only",
                    BrowserBridgeStatus = "Bridge local-only",
                    SharingStatus = "Sharing history redacted"
                });

            var result = await service.ExportAsync(new CompanionPortalExportRequest
            {
                OutputPath = Path.Combine(paths.LocalRoot, "portal-export"),
                ManualValidationPath = manualRoot,
                ProofRootPath = proofRoot
            });

            var reportJson = await File.ReadAllTextAsync(result.ReportJsonPath);
            var reportHtml = await File.ReadAllTextAsync(result.ReportHtmlPath);
            Assert.IsTrue(result.Report.ShareHistory.RawHistorySensitiveFindingCount > 0);
            Assert.IsTrue(result.Report.ManualValidation.SensitiveFindingCount > 0);
            Assert.IsFalse(reportJson.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(reportHtml.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(reportJson.Contains("super-secret-token", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(reportJson.Contains(paths.LocalRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(reportJson.Contains(paths.LibraryRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(reportJson.Contains("secret-capture.png", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(reportJson, "[REDACTED:api-key-or-password-field]");
            StringAssert.Contains(reportJson, "[LOCAL_ROOT]");
            StringAssert.Contains(reportJson, "[LIBRARY_ROOT]");
        });
    }

    [TestMethod]
    public async Task ExportAsync_WritesOptInMediaReviewPagesWithoutRemoteFlags()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var service = CreateService(paths, settings, sharing);
            var mediaPath = CreateMediaFile(paths, "selected-review.png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
            var outputRoot = Path.Combine(paths.LocalRoot, "portal-media-review");

            var result = await service.ExportAsync(new CompanionPortalExportRequest
            {
                OutputPath = outputRoot,
                MediaPaths = { mediaPath },
                AcceptMediaCopy = true
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.Report.MediaReview.Enabled);
            Assert.IsTrue(result.Report.MediaReview.AcceptedMediaCopy);
            Assert.AreEqual(1, result.Report.MediaReview.ItemCount);
            Assert.IsTrue(result.Report.Boundary.MediaReviewPagesEnabled);
            Assert.IsTrue(result.Report.Boundary.WouldAttachMedia);
            Assert.IsFalse(result.Report.Boundary.WouldContactPortal);
            Assert.IsFalse(result.Report.Boundary.WouldSync);
            Assert.IsFalse(result.Report.Boundary.WouldUpload);
            Assert.IsFalse(result.Report.Boundary.WouldReadSecrets);
            Assert.IsFalse(result.Report.Boundary.WouldMutatePolicy);
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, CompanionPortalExportService.MediaReviewJsonFileName)));
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, CompanionPortalExportService.MediaReviewHtmlFileName)));
            var item = result.Report.MediaReview.Items.Single();
            Assert.AreEqual("image/png", item.ContentType);
            Assert.IsTrue(item.ReviewRelativePath.StartsWith("media/", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, item.ReviewRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.IsTrue(result.Report.GeneratedFiles.Contains(item.ReviewRelativePath));

            var reportJson = await File.ReadAllTextAsync(result.ReportJsonPath);
            var mediaJson = await File.ReadAllTextAsync(Path.Combine(outputRoot, CompanionPortalExportService.MediaReviewJsonFileName));
            var mediaHtml = await File.ReadAllTextAsync(Path.Combine(outputRoot, CompanionPortalExportService.MediaReviewHtmlFileName));
            var mainHtml = await File.ReadAllTextAsync(result.ReportHtmlPath);
            Assert.IsFalse(reportJson.Contains(mediaPath, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(mediaJson.Contains(mediaPath, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(mediaHtml.Contains(mediaPath, StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(mainHtml, CompanionPortalExportService.MediaReviewHtmlFileName);
            StringAssert.Contains(mediaJson, "no upload");
            StringAssert.Contains(mediaHtml, "no upload");
        });
    }

    [TestMethod]
    public async Task ExportAsync_RejectsMediaReviewWithoutExplicitCopyAcceptance()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var service = CreateService(paths, settings, sharing);
            var mediaPath = CreateMediaFile(paths, "selected-review.png", [1, 2, 3, 4]);
            var outputRoot = Path.Combine(paths.LocalRoot, "portal-media-review");

            var result = await service.ExportAsync(new CompanionPortalExportRequest
            {
                OutputPath = outputRoot,
                MediaPaths = { mediaPath }
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "accept-media-copy");
            Assert.IsFalse(Directory.Exists(outputRoot));
            Assert.IsFalse(result.Report.Boundary.MediaReviewPagesEnabled);
            Assert.IsFalse(result.Report.Boundary.WouldAttachMedia);
        });
    }

    [TestMethod]
    public async Task HostAsync_ServesLoopbackReadOnlyPreview()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var proofRoot = await WriteReleaseProofAsync(paths);
            var manualRoot = await WriteManualValidationSummaryAsync(paths, succeeded: false);
            var hostService = new CompanionPortalHostService(CreateService(paths, settings, sharing));

            await using var session = await hostService.StartAsync(new CompanionPortalHostRequest
            {
                OutputPath = Path.Combine(paths.LocalRoot, "portal-preview"),
                ManualValidationPath = manualRoot,
                ProofRootPath = proofRoot,
                Port = 0
            });

            Assert.IsTrue(session.Result.Succeeded, session.Result.Message);
            Assert.IsTrue(session.Result.Url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(session.Result.LoopbackOnly);
            Assert.IsFalse(session.Result.RemoteClientsAllowed);
            Assert.IsFalse(session.Result.MutatingRoutesEnabled);
            Assert.IsFalse(session.Result.WouldContactPortal);
            Assert.IsFalse(session.Result.WouldSync);
            Assert.IsFalse(session.Result.WouldUpload);
            Assert.IsFalse(session.Result.WouldAttachMedia);
            Assert.IsFalse(session.Result.WouldReadSecrets);
            Assert.IsTrue(File.Exists(session.Result.ReportHtmlPath));
            Assert.IsTrue(File.Exists(session.Result.ReportJsonPath));
            Assert.IsNotNull(session.Export);
            Assert.AreEqual("local-loopback-companion-portal-preview-v0", session.Export!.Report.Mode);
            Assert.IsTrue(session.Export.Report.Boundary.WouldHostPortal);
            Assert.IsTrue(session.Export.Report.Boundary.LoopbackOnly);
            Assert.IsFalse(session.Export.Report.Boundary.RemoteClientsAllowed);
            Assert.IsFalse(session.Export.Report.Boundary.MutatingRoutesEnabled);

            using var http = new HttpClient();
            var index = await http.GetStringAsync(session.Result.Url);
            var report = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), CompanionPortalExportService.ReportJsonFileName));
            var health = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), "health.json"));

            StringAssert.Contains(index, "Receipts companion portal loopback preview");
            StringAssert.Contains(index, "would sync: false");
            StringAssert.Contains(report, "\"mode\": \"local-loopback-companion-portal-preview-v0\"");
            StringAssert.Contains(report, "\"wouldHostPortal\": true");
            StringAssert.Contains(report, "\"wouldContactPortal\": false");
            StringAssert.Contains(report, "\"mutatingRoutesEnabled\": false");
            StringAssert.Contains(health, "\"status\": \"ready\"");
            StringAssert.Contains(health, "\"loopbackOnly\": true");

            using var post = new HttpRequestMessage(HttpMethod.Post, session.Result.Url);
            var postResponse = await http.SendAsync(post);
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);

            var traversalResponse = await http.GetAsync(new Uri(new Uri(session.Result.Url), "%2e%2e/secret.txt"));
            Assert.AreEqual(HttpStatusCode.NotFound, traversalResponse.StatusCode);
        });
    }

    [TestMethod]
    public async Task HostAsync_ServesOnlyManifestListedMediaReviewFiles()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var mediaBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 5, 6, 7, 8 };
            var mediaPath = CreateMediaFile(paths, "safe-review.png", mediaBytes);
            var hostService = new CompanionPortalHostService(CreateService(paths, settings, sharing));

            await using var session = await hostService.StartAsync(new CompanionPortalHostRequest
            {
                OutputPath = Path.Combine(paths.LocalRoot, "portal-preview"),
                Port = 0,
                MediaPaths = { mediaPath },
                AcceptMediaCopy = true
            });

            Assert.IsTrue(session.Result.Succeeded, session.Result.Message);
            Assert.IsTrue(session.Result.WouldAttachMedia);
            Assert.IsTrue(session.Result.MediaReviewPagesEnabled);
            Assert.AreEqual(1, session.Result.MediaReviewItemCount);
            Assert.IsNotNull(session.Export);
            var item = session.Export!.Report.MediaReview.Items.Single();

            using var http = new HttpClient();
            var mediaReview = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), CompanionPortalExportService.MediaReviewHtmlFileName));
            var manifest = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), CompanionPortalExportService.MediaReviewJsonFileName));
            var health = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), "health.json"));
            var media = await http.GetByteArrayAsync(new Uri(new Uri(session.Result.Url), item.ReviewRelativePath));
            CollectionAssert.AreEqual(mediaBytes, media);
            StringAssert.Contains(mediaReview, "Receipts companion portal media review");
            StringAssert.Contains(manifest, "\"itemCount\": 1");
            StringAssert.Contains(health, "\"mediaReviewPagesEnabled\": true");

            var blocked = await http.GetAsync(new Uri(new Uri(session.Result.Url), "media/not-listed.png"));
            Assert.AreEqual(HttpStatusCode.NotFound, blocked.StatusCode);
        });
    }

    [TestMethod]
    public async Task HostAsync_RejectsRemotePreviewEvenWithSharedTokenUntilTlsExists()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var hostService = new CompanionPortalHostService(CreateService(paths, settings, sharing));
            var token = "test-token-123456";

            await using var session = await hostService.StartAsync(new CompanionPortalHostRequest
            {
                Host = "0.0.0.0",
                OutputPath = Path.Combine(paths.LocalRoot, "portal-self-hosted"),
                Port = 0,
                AllowRemoteClients = true,
                AcceptRemoteClients = true,
                AccessToken = token
            });

            Assert.IsFalse(session.Result.Succeeded);
            Assert.IsTrue(session.Result.LoopbackOnly);
            Assert.IsFalse(session.Result.RemoteClientsAllowed);
            Assert.IsFalse(session.Result.MutatingRoutesEnabled);
            Assert.IsTrue(session.Result.AuthRequired);
            Assert.AreEqual("shared-token", session.Result.AccessControlMode);
            StringAssert.Contains(session.Result.Message, "loopback-only");
            StringAssert.Contains(session.Result.Message, "HTTPS");
        });
    }

    [TestMethod]
    public async Task HostAsync_RejectsSelfHostedPreviewWithoutSharedToken()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var hostService = new CompanionPortalHostService(CreateService(paths, settings, sharing));
            var outputRoot = Path.Combine(paths.LocalRoot, "portal-self-hosted");

            await using var session = await hostService.StartAsync(new CompanionPortalHostRequest
            {
                Host = "0.0.0.0",
                OutputPath = outputRoot,
                Port = 0,
                AllowRemoteClients = true,
                AcceptRemoteClients = true
            });

            Assert.IsFalse(session.Result.Succeeded);
            Assert.IsFalse(session.Result.RemoteClientsAllowed);
            Assert.IsFalse(session.Result.MutatingRoutesEnabled);
            StringAssert.Contains(session.Result.Message, "loopback-only");
            Assert.IsFalse(Directory.Exists(outputRoot));
        });
    }

    [TestMethod]
    public async Task HostAsync_RejectsNonLoopbackBind()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = CreateSettings(paths);
            var sharing = new ShareService(paths, settings, new SecretStore(paths));
            var hostService = new CompanionPortalHostService(CreateService(paths, settings, sharing));
            var outputRoot = Path.Combine(paths.LocalRoot, "portal-preview");

            await using var session = await hostService.StartAsync(new CompanionPortalHostRequest
            {
                Host = "0.0.0.0",
                OutputPath = outputRoot,
                Port = 0
            });

            Assert.IsFalse(session.Result.Succeeded);
            Assert.IsTrue(session.Result.LoopbackOnly);
            Assert.IsFalse(session.Result.RemoteClientsAllowed);
            Assert.IsFalse(session.Result.MutatingRoutesEnabled);
            StringAssert.Contains(session.Result.Message, "loopback");
            Assert.IsFalse(Directory.Exists(outputRoot));
        });
    }

    private static CompanionPortalExportService CreateService(
        AppPaths paths,
        AppSettings settings,
        ShareService sharing,
        DiagnosticSnapshot? diagnostics = null)
    {
        diagnostics ??= new DiagnosticSnapshot
        {
            OsDescription = "Windows test",
            RuntimeDescription = ".NET test",
            CaptureEngine = "Capture engine local-only.",
            RecordingEngine = "Recording engine local-only.",
            RecordingReadiness = "Recording readiness local-only.",
            EncoderStatus = "Encoder status local-only.",
            OcrStatus = "OCR status local-only.",
            AiStatus = "AI status local-only.",
            PolicyStatus = "Policy status local-only.",
            StartupStatus = "Startup status local-only.",
            MetadataIndexStatus = "Metadata index status local-only.",
            UploadQueueStatus = "Upload queue status local-only.",
            PrintImportStatus = "Print import status local-only.",
            PluginStatus = "Plugin status local-only.",
            BrowserBridgeStatus = "Browser bridge status local-only.",
            SharingStatus = "Sharing status local-only."
        };
        return new CompanionPortalExportService(paths, settings, diagnostics, sharing);
    }

    private static AppSettings CreateSettings(AppPaths paths)
    {
        return new AppSettings
        {
            LocalExportFolder = Path.Combine(paths.DocumentsRoot, "exports"),
            ManagedPolicy = new ManagedPolicySettings
            {
                DisableUploads = true,
                DisableCustomWebhooks = true,
                AllowedShareDestinations = { nameof(ShareDestination.LocalFolder) }
            }
        };
    }

    private static string CreateMediaFile(AppPaths paths, string fileName, byte[] bytes)
    {
        var filePath = Path.Combine(paths.ImagesRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private static CaptureItem CreateCaptureItem(AppPaths paths, string fileName, int bytes)
    {
        var filePath = Path.Combine(paths.ImagesRoot, fileName);
        File.WriteAllBytes(filePath, Enumerable.Range(0, bytes).Select(index => (byte)(index % 255)).ToArray());
        return new CaptureItem
        {
            Kind = CaptureKind.Imported,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = bytes,
            Width = 10,
            Height = 10
        };
    }

    private static async Task<string> WriteManualValidationSummaryAsync(
        AppPaths paths,
        bool succeeded,
        string issue = "Live consent screen proof parked outside automation.")
    {
        var root = Path.Combine(paths.LocalRoot, "manual-validation");
        Directory.CreateDirectory(root);
        var summary = new ManualValidationSummaryResult
        {
            Succeeded = succeeded,
            Message = succeeded ? "Manual validation complete." : "Manual validation incomplete.",
            GeneratedAt = DateTimeOffset.Now,
            Lanes =
            {
                new ManualValidationLaneSummary
                {
                    Id = "browser-extension-live-fixture",
                    Title = "Browser Extension Live Fixture",
                    Exists = true,
                    Status = ManualValidationLaneStatus.Passed,
                    EvidenceCount = 2
                },
                new ManualValidationLaneSummary
                {
                    Id = "live-provider-account-proof",
                    Title = "Live Provider Account Proof",
                    OAuthParked = true,
                    Exists = true,
                    Status = ManualValidationLaneStatus.NotRun,
                    Issues = { issue }
                }
            },
            Redaction = new ManualValidationRedactionSummary
            {
                Status = "clean",
                Findings =
                {
                    new ManualValidationRedactionFinding
                    {
                        RelativePath = "live-provider-account-proof.md",
                        Count = SensitiveTextDetector.Find(issue).Count,
                        Summary = SensitiveTextDetector.Summarize(SensitiveTextDetector.Find(issue))
                    }
                }
            },
            Issues = { issue }
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, ManualValidationSummaryService.SummaryJsonFileName),
            JsonSerializer.Serialize(summary, JsonOptions));
        return root;
    }

    private static async Task<string> WriteReleaseProofAsync(AppPaths paths)
    {
        var root = Path.Combine(paths.LocalRoot, "release-proof");
        Directory.CreateDirectory(root);
        var manifest = new
        {
            manifestVersion = 1,
            generatedAt = DateTimeOffset.Now,
            commands = new[]
            {
                new { name = "Release build", status = "passed", exitCode = 0, arguments = new[] { "build", paths.LocalRoot } },
                new { name = "Release tests", status = "passed", exitCode = 0, arguments = new[] { "test", paths.LibraryRoot } }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        await File.WriteAllBytesAsync(Path.Combine(root, "Receipts-release-proof-test.zip"), [5, 6, 7, 8]);
        await File.WriteAllBytesAsync(Path.Combine(root, "GoatShot-release-proof-test.zip"), [1, 2, 3, 4]);
        return root;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            var paths = AppPaths.Create(new AppSettings());
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

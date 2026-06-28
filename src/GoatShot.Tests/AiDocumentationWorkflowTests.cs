using System.IO.Compression;
using System.Net;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AiDocumentationWorkflowTests
{
    [TestMethod]
    public async Task AiHistory_SupportsPromptReuseAndReviewStatus()
    {
        await WithTempPathsAsync(async paths =>
        {
            var history = new AiActionHistoryService(paths);
            var item = CreateItem(paths, "prompt.png", CaptureKind.Imported);

            await history.RecordAsync(
                AiActionKind.ImageEdit,
                item,
                "gemini-test",
                "Blur email test@example.com before sharing",
                succeeded: true,
                "completed",
                outputPath: item.FilePath);

            var prompts = history.LoadPromptHistory(AiActionKind.ImageEdit, 5);
            Assert.AreEqual(1, prompts.Count);
            StringAssert.Contains(prompts[0].Prompt, "[REDACTED:email-address]");
            Assert.AreEqual(1, prompts[0].UseCount);

            var entry = history.Load(1).Single();
            Assert.AreEqual(AiActionReviewStatus.Pending, entry.ReviewStatus);

            var updated = await history.UpdateReviewStatusAsync(entry.Id[..8], AiActionReviewStatus.Accepted, "looks good");
            Assert.IsNotNull(updated);
            Assert.AreEqual(AiActionReviewStatus.Accepted, updated.ReviewStatus);
            Assert.IsNotNull(updated.ReviewedAt);
        });
    }

    [TestMethod]
    public async Task Transcription_ImportsSrtIntoTranscriptSegments()
    {
        await WithTempPathsAsync(async paths =>
        {
            var srt = Path.Combine(paths.DocumentsRoot, "captions.srt");
            await File.WriteAllTextAsync(
                srt,
                """
                1
                00:00:00,000 --> 00:00:02,000
                Open the settings panel.

                2
                00:00:02,500 --> 00:00:05,000
                Confirm the upload queue is empty.
                """);

            var service = new TranscriptionService(paths);
            var result = await service.ImportSrtAsync(srt);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(2, result.Segments.Count);
            StringAssert.Contains(result.Text, "[0:00] Open the settings panel.");
            Assert.IsTrue(File.Exists(result.TranscriptPath));
        });
    }

    [TestMethod]
    public void Transcription_BuildsOpenAiWhisperArguments()
    {
        var args = TranscriptionService.BuildOpenAiWhisperArguments(
            "audio.wav",
            "out",
            "small",
            "en");

        CollectionAssert.AreEqual(
            new[] { "audio.wav", "--output_format", "srt", "--output_dir", "out", "--model", "small", "--language", "en" },
            args.ToArray());
    }

    [TestMethod]
    public void Transcription_BuildsWhisperCppArguments()
    {
        var args = TranscriptionService.BuildWhisperCppArguments(
            "audio.wav",
            "out/transcript",
            "model.bin",
            "en");

        CollectionAssert.AreEqual(
            new[] { "-m", Path.GetFullPath("model.bin"), "-f", "audio.wav", "-osrt", "-of", "out/transcript", "-l", "en" },
            args.ToArray());
    }

    [TestMethod]
    public void Transcription_ResolveLocalSpeechEngine_ReturnsNullWhenUnavailable()
    {
        var originalExe = Environment.GetEnvironmentVariable("GOATSHOT_WHISPER_EXE");
        var originalPath = Environment.GetEnvironmentVariable("GOATSHOT_WHISPER_PATH");
        var originalPathValue = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_WHISPER_EXE", null);
            Environment.SetEnvironmentVariable("GOATSHOT_WHISPER_PATH", null);
            Environment.SetEnvironmentVariable("PATH", string.Empty);

            var engine = TranscriptionService.ResolveLocalSpeechEngine(new TranscriptionRequest("demo.mp4"));

            Assert.IsNull(engine);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_WHISPER_EXE", originalExe);
            Environment.SetEnvironmentVariable("GOATSHOT_WHISPER_PATH", originalPath);
            Environment.SetEnvironmentVariable("PATH", originalPathValue);
        }
    }

    [TestMethod]
    public void Transcription_ParsesProviderJsonIntoTimestampedSegments()
    {
        var parsed = TranscriptionService.ParseProviderTranscription(
            """
            {"transcript":"Open settings. Upload fails.","segments":[{"start":"0:00","end":"0:02","text":"Open settings."},{"start":"0:02.5","end":"0:05","text":"Upload fails."}]}
            """);

        Assert.AreEqual(2, parsed.Segments.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5), parsed.Segments[1].Start);
        Assert.AreEqual("Upload fails.", parsed.Segments[1].Text);
        StringAssert.Contains(parsed.Text, "Open settings");
    }

    [TestMethod]
    public async Task Gemini_TranscribesAudioWithInlineWavPayloadAndSpeechModel()
    {
        await WithTempPathsAsync(async paths =>
        {
            var wav = Path.Combine(paths.DocumentsRoot, "speech.wav");
            await File.WriteAllBytesAsync(wav, new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4 });
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new { text = "{\"transcript\":\"Hello from audio.\",\"segments\":[{\"start\":\"0:00\",\"end\":\"0:01\",\"text\":\"Hello from audio.\"}]}" }
                            }
                        }
                    }
                }
            });
            var handler = new StubHttpMessageHandler(responseJson);
            var settings = new AppSettings
            {
                GeminiApiEndpoint = "https://gemini.test/v1beta",
                GeminiDefaultModelId = "gemini-image",
                GeminiSpeechToTextModelId = "gemini-speech"
            };
            var secretStore = new SecretStore(paths);
            secretStore.SaveGeminiApiKey("fake-key");
            var gemini = new GeminiImageProvider(settings, paths, secretStore, handler);

            var result = await gemini.TranscribeAudioAsync(wav, "Transcribe this audio.", string.Empty, CancellationToken.None);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("gemini-speech", result.ModelId);
            StringAssert.Contains(result.Text!, "Hello from audio");
            StringAssert.Contains(handler.LastRequestUri?.ToString() ?? string.Empty, "gemini-speech");
            StringAssert.Contains(handler.LastBody ?? string.Empty, "audio/wav");
            StringAssert.Contains(handler.LastBody ?? string.Empty, Convert.ToBase64String(await File.ReadAllBytesAsync(wav)));
        });
    }

    [TestMethod]
    public async Task VideoIntelligence_ExportsTranscriptBasedDocxDraft()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateItem(paths, "checkout-demo.mp4", CaptureKind.RecordingMp4);
            var srt = Path.Combine(paths.DocumentsRoot, "checkout-demo.srt");
            await File.WriteAllTextAsync(
                srt,
                """
                1
                00:00:00,000 --> 00:00:03,000
                User opens the checkout page.

                2
                00:00:03,000 --> 00:00:06,000
                An error appears after payment.
                """);

            var service = new VideoIntelligenceService(paths, new TranscriptionService(paths));
            var output = Path.Combine(paths.DocumentsRoot, "summary.docx");
            var result = await service.GenerateAsync(video, srtPath: srt, outputPath: output);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(output, result.OutputPath);
            Assert.IsTrue(File.Exists(output));
            using var archive = ZipFile.OpenRead(output);
            Assert.IsNotNull(archive.GetEntry("word/document.xml"));
            StringAssert.Contains(result.Summary, "checkout page");
        });
    }

    [TestMethod]
    public async Task VideoIntelligence_UsesGeminiTextDraftWhenRequested()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateItem(paths, "checkout-demo.mp4", CaptureKind.RecordingMp4);
            var srt = Path.Combine(paths.DocumentsRoot, "checkout-demo.srt");
            await File.WriteAllTextAsync(
                srt,
                """
                1
                00:00:00,000 --> 00:00:03,000
                Customer alice@example.com opens the checkout page.

                2
                00:00:03,000 --> 00:00:06,000
                Payment fails after the customer taps submit.
                """);

            var providerText = """
                {"title":"AI Checkout Failure","summary":"The recording shows a checkout flow where payment fails after submit. The customer identifier remains redacted.","chapters":["0:00 - Checkout opens","0:03 - Payment failure appears"]}
                """;
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new { text = providerText }
                            }
                        }
                    }
                }
            });
            var handler = new StubHttpMessageHandler(responseJson);
            var settings = new AppSettings
            {
                GeminiApiEndpoint = "https://gemini.test/v1beta",
                GeminiDefaultModelId = "gemini-test"
            };
            var secretStore = new SecretStore(paths);
            secretStore.SaveGeminiApiKey("fake-key");
            var gemini = new GeminiImageProvider(settings, paths, secretStore, handler);
            var service = new VideoIntelligenceService(paths, new TranscriptionService(paths), gemini);
            var output = Path.Combine(paths.DocumentsRoot, "summary.md");

            var result = await service.GenerateAsync(
                video,
                srtPath: srt,
                outputPath: output,
                useAiProvider: true,
                modelId: "gemini-test",
                providerPrompt: "Focus on checkout failure.");

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.UsedAiProvider);
            Assert.AreEqual("Gemini", result.ProviderName);
            Assert.AreEqual("gemini-test", result.ModelId);
            Assert.AreEqual("AI Checkout Failure", result.Title);
            StringAssert.Contains(result.Summary, "checkout flow");
            CollectionAssert.Contains(result.Chapters, "0:03 - Payment failure appears");
            Assert.IsTrue(File.Exists(output));
            var markdown = await File.ReadAllTextAsync(output);
            StringAssert.Contains(markdown, "Draft source: Gemini (gemini-test)");
            StringAssert.Contains(markdown, "redacted transcript text");
            StringAssert.Contains(handler.LastBody ?? string.Empty, "[REDACTED:email-address]");
            Assert.IsFalse((handler.LastBody ?? string.Empty).Contains("alice@example.com", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task BugReport_ExportsPdfAndDocxFormats()
    {
        await WithTempServicesAsync(async services =>
        {
            var item = CreateItem(services.Paths, "bug.png", CaptureKind.Imported);
            var pdf = Path.Combine(services.Paths.DocumentsRoot, "bug.pdf");
            var docx = Path.Combine(services.Paths.DocumentsRoot, "bug.docx");

            var pdfResult = await services.BugReports.ExportAsync(item, pdf, "pdf");
            var docxResult = await services.BugReports.ExportAsync(item, docx, "docx");

            Assert.IsTrue(pdfResult.Succeeded, pdfResult.Message);
            Assert.IsTrue(docxResult.Succeeded, docxResult.Message);
            CollectionAssert.AreEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 }, File.ReadAllBytes(pdf).Take(4).ToArray());
            using var archive = ZipFile.OpenRead(docx);
            Assert.IsNotNull(archive.GetEntry("word/document.xml"));
        });
    }

    [TestMethod]
    public async Task DocumentationPacket_CreatesManifestWithRedactedTranscriptAndAiReviewState()
    {
        await WithTempServicesAsync(async services =>
        {
            var item = CreateItem(services.Paths, "checkout-demo.mp4", CaptureKind.RecordingMp4);
            var transcript = Path.Combine(services.Paths.DocumentsRoot, "checkout-transcript.txt");
            var srt = Path.Combine(services.Paths.DocumentsRoot, "checkout.srt");
            var summary = Path.Combine(services.Paths.DocumentsRoot, "checkout-summary.md");
            var keyframe = Path.Combine(services.Paths.ImagesRoot, "checkout-keyframe.png");
            await File.WriteAllTextAsync(transcript, "[0:00] Customer alice@example.com opens checkout.");
            await File.WriteAllTextAsync(
                srt,
                """
                1
                00:00:00,000 --> 00:00:02,000
                Customer alice@example.com opens checkout.
                """);
            await File.WriteAllTextAsync(summary, "# Checkout failure\n\nPayment fails after submit.");
            await File.WriteAllBytesAsync(keyframe, new byte[] { 1, 2, 3, 4 });

            var entry = await services.AiHistory.RecordAsync(
                AiActionKind.VideoSummary,
                item,
                "gemini-test",
                "Summarize checkout failure for alice@example.com",
                succeeded: true,
                "Video summary completed.",
                outputPath: summary,
                textOutput: "Payment fails after submit.");
            await services.AiHistory.UpdateReviewStatusAsync(entry.Id, AiActionReviewStatus.Accepted, "approved");

            var packetDir = Path.Combine(services.Paths.DocumentsRoot, "packet");
            var result = await services.DocumentationPackets.CreateAsync(new DocumentationPacketRequest
            {
                Item = item,
                OutputDirectory = packetDir,
                TranscriptPath = transcript,
                SrtPath = srt,
                VideoSummaryPath = summary,
                GenerateBugReport = true,
                KeyframePaths = [keyframe],
                ContextNotes = "Observed customer alice@example.com hitting submit."
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(File.Exists(result.IndexPath));
            Assert.IsTrue(File.Exists(result.BugReportPath));

            var manifest = await File.ReadAllTextAsync(result.ManifestPath);
            StringAssert.Contains(manifest, "\"schemaVersion\": 1");
            StringAssert.Contains(manifest, "\"role\": \"transcript\"");
            StringAssert.Contains(manifest, "\"reviewStatus\": \"Accepted\"");
            StringAssert.Contains(manifest, "[REDACTED:email-address]");
            Assert.IsFalse(manifest.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase));

            var bugReport = await File.ReadAllTextAsync(result.BugReportPath!);
            StringAssert.Contains(bugReport, "## Recording Intelligence");
            StringAssert.Contains(bugReport, "Redacted Transcript Preview");
            StringAssert.Contains(bugReport, "[REDACTED:email-address]");
            Assert.IsFalse(bugReport.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public async Task BugReport_IncludesRecordingEnrichmentWhenProvided()
    {
        await WithTempServicesAsync(async services =>
        {
            var item = CreateItem(services.Paths, "recording.mp4", CaptureKind.RecordingMp4);
            var transcript = Path.Combine(services.Paths.DocumentsRoot, "recording-transcript.txt");
            var summary = Path.Combine(services.Paths.DocumentsRoot, "recording-summary.md");
            await File.WriteAllTextAsync(transcript, "[0:04] User bob@example.com sees upload failure.");
            await File.WriteAllTextAsync(summary, "# Upload failure");
            var aiEntry = await services.AiHistory.RecordAsync(
                AiActionKind.BugReportDraft,
                item,
                "local",
                "Draft bug report",
                succeeded: true,
                "Draft created.");
            await services.AiHistory.UpdateReviewStatusAsync(aiEntry.Id, AiActionReviewStatus.Iterated, "needs detail");

            var output = Path.Combine(services.Paths.DocumentsRoot, "enriched-bug.md");
            var result = await services.BugReports.ExportAsync(
                item,
                output,
                "markdown",
                new BugReportEnrichment
                {
                    TranscriptPath = transcript,
                    VideoSummaryPath = summary,
                    ContextNotes = "Reporter bob@example.com reproduced this twice.",
                    AiHistory = services.AiHistory.Load(10).ToList()
                });

            Assert.IsTrue(result.Succeeded, result.Message);
            var markdown = await File.ReadAllTextAsync(output);
            StringAssert.Contains(markdown, "## Recording Intelligence");
            StringAssert.Contains(markdown, "## AI Review State");
            StringAssert.Contains(markdown, "Iterated");
            StringAssert.Contains(markdown, "[REDACTED:email-address]");
            Assert.IsFalse(markdown.Contains("bob@example.com", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static CaptureItem CreateItem(AppPaths paths, string fileName, CaptureKind kind)
    {
        var root = kind == CaptureKind.RecordingMp4 ? paths.VideosRoot : paths.ImagesRoot;
        var filePath = Path.Combine(root, fileName);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(filePath, Enumerable.Range(0, 128).Select(index => (byte)(index % 255)).ToArray());

        return new CaptureItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            CreatedAt = DateTimeOffset.Now,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Width = kind == CaptureKind.RecordingMp4 ? 0 : 16,
            Height = kind == CaptureKind.RecordingMp4 ? 0 : 16,
            Bytes = new FileInfo(filePath).Length,
            Notes = "Test item"
        };
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
            DeleteDirectoryWithRetry(root);
        }
    }

    private static async Task WithTempServicesAsync(Func<AppServices, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            using var services = AppServices.Create();
            await action(services);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);
            DeleteDirectoryWithRetry(root);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public string? LastBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}

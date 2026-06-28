using System.Net;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class VideoToolServiceTests
{
    [TestMethod]
    public async Task ChangeVolumeAsync_RejectsNegativeGainBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.ChangeVolumeAsync(CreateMissingVideoItem(paths), -1d);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Volume gain must be zero or greater.");
        });
    }

    [TestMethod]
    public void ConvertArguments_UsesPaletteGifAtRequestedFps()
    {
        var arguments = VideoToolService.ConvertArguments(
            "input.mp4",
            "gif",
            "output.gif",
            new AnimationExportOptions { FrameRate = 60, GifTimingMode = "smooth" });

        CollectionAssert.Contains(arguments.ToArray(), "-filter_complex");
        Assert.IsTrue(arguments.Any(argument => argument.Contains("fps=60", StringComparison.Ordinal)));
        Assert.IsTrue(arguments.Any(argument => argument.Contains("palettegen", StringComparison.Ordinal)));
        Assert.IsTrue(arguments.Any(argument => argument.Contains("paletteuse", StringComparison.Ordinal)));
        CollectionAssert.Contains(arguments.ToArray(), "-an");
    }

    [TestMethod]
    public void BuildConvertCompanionArguments_UsesRequestedHighFps()
    {
        var arguments = VideoToolService.BuildConvertCompanionArguments(
            "input.mp4",
            "webm",
            "output.webm",
            frameRate: 120);

        CollectionAssert.Contains(arguments.ToArray(), "fps=120");
        CollectionAssert.Contains(arguments.ToArray(), "libvpx-vp9");
    }

    [TestMethod]
    public async Task ResizeAsync_RejectsInvalidDimensionsBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.ResizeAsync(
                CreateMissingVideoItem(paths),
                width: 0,
                height: 720,
                qualityProfile: "balanced",
                crf: null,
                bitrate: null,
                fps: null);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Resize width and height must be greater than zero");
        });
    }

    [TestMethod]
    public async Task CutMiddleAsync_RejectsNonPositiveDurationBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.CutMiddleAsync(
                CreateMissingVideoItem(paths),
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Cut duration must be greater than zero.");
        });
    }

    [TestMethod]
    public async Task MergeAsync_RequiresAtLeastTwoClips()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.MergeAsync([CreateMissingVideoItem(paths)]);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Video merge requires at least two input clips.");
        });
    }

    [TestMethod]
    public void BuildAtempoFilter_ChainsValuesIntoSupportedRange()
    {
        Assert.AreEqual("atempo=2,atempo=2,atempo=2", VideoToolService.BuildAtempoFilter(8d));
        Assert.AreEqual("atempo=0.5,atempo=0.5,atempo=0.5,atempo=0.8", VideoToolService.BuildAtempoFilter(0.1d));
        Assert.AreEqual("atempo=1.25", VideoToolService.BuildAtempoFilter(1.25d));
    }

    [TestMethod]
    public void BuildChangeSpeedArguments_MapsTempoAdjustedAudioWhenPresent()
    {
        var arguments = VideoToolService.BuildChangeSpeedArguments(
            "input.mp4",
            "output.mp4",
            "0.5*PTS",
            2d,
            includeAudio: true);

        CollectionAssert.Contains(arguments.ToArray(), "-filter_complex");
        CollectionAssert.Contains(arguments.ToArray(), "[aout]");
        CollectionAssert.Contains(arguments.ToArray(), "-c:a");
        Assert.IsFalse(arguments.Contains("-an"));
    }

    [TestMethod]
    public async Task DenoiseAudioAsync_RejectsInvalidReductionBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.DenoiseAudioAsync(CreateMissingVideoItem(paths), noiseReductionDb: 0);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Audio noise reduction must be greater than 0 dB");
        });
    }

    [TestMethod]
    public void BuildDenoiseAudioArguments_UsesAfftdnAndPreservesVideo()
    {
        var arguments = VideoToolService.BuildDenoiseAudioArguments(
            "input.mp4",
            "output.mp4",
            noiseReductionDb: 12.5d);

        CollectionAssert.Contains(arguments.ToArray(), "input.mp4");
        CollectionAssert.Contains(arguments.ToArray(), "output.mp4");
        CollectionAssert.Contains(arguments.ToArray(), "0:v:0");
        CollectionAssert.Contains(arguments.ToArray(), "0:a:0");
        CollectionAssert.Contains(arguments.ToArray(), "copy");
        CollectionAssert.Contains(arguments.ToArray(), "afftdn=nr=12.5");
        CollectionAssert.Contains(arguments.ToArray(), "aac");
        Assert.IsFalse(arguments.Contains("-an"));
    }

    [TestMethod]
    public void BuildMiddleCutFilter_IncludesAudioConcatWhenRequested()
    {
        var filter = VideoToolService.BuildMiddleCutFilter(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            includeAudio: true);

        StringAssert.Contains(filter, "[0:a:0]atrim=start=0:end=1");
        StringAssert.Contains(filter, "[0:a:0]atrim=start=3");
        StringAssert.Contains(filter, "concat=n=2:v=1:a=1[outv][outa]");
    }

    [TestMethod]
    public void NormalizeCutRanges_MergesOverlappingRangesAndClampsToDuration()
    {
        var ranges = VideoToolService.NormalizeCutRanges(
            [
                new VideoEditCut { StartSeconds = -1, EndSeconds = 1.25 },
                new VideoEditCut { StartSeconds = 1.2, EndSeconds = 3 },
                new VideoEditCut { StartSeconds = 7, EndSeconds = 12 },
                new VideoEditCut { StartSeconds = 4, EndSeconds = 4 }
            ],
            durationSeconds: 10);

        Assert.AreEqual(2, ranges.Count);
        Assert.AreEqual(0d, ranges[0].StartSeconds);
        Assert.AreEqual(3d, ranges[0].EndSeconds);
        Assert.AreEqual(7d, ranges[1].StartSeconds);
        Assert.AreEqual(10d, ranges[1].EndSeconds);
    }

    [TestMethod]
    public void BuildAdvancedCutFilter_UsesKeepSegmentsForMultipleReviewedCuts()
    {
        var ranges = VideoToolService.NormalizeCutRanges(
            [
                new VideoEditCut { StartSeconds = 1, EndSeconds = 2 },
                new VideoEditCut { StartSeconds = 3, EndSeconds = 4 }
            ],
            durationSeconds: 5);
        var keep = VideoToolService.BuildKeepSegments(ranges, durationSeconds: 5);
        var filter = VideoToolService.BuildAdvancedCutFilter(keep, includeAudio: true);

        Assert.AreEqual(3, keep.Count);
        StringAssert.Contains(filter, "[0:v:0]trim=start=0:end=1,setpts=PTS-STARTPTS[v0]");
        StringAssert.Contains(filter, "[0:a:0]atrim=start=2:end=3,asetpts=PTS-STARTPTS[a1]");
        StringAssert.Contains(filter, "[v0][a0][v1][a1][v2][a2]concat=n=3:v=1:a=1[outv][outa]");
    }

    [TestMethod]
    public async Task ApplyAdvancedCutPlanAsync_RejectsEmptyCutPlanBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "demo.mp4", [1, 2, 3]);
            var service = CreateService(paths);
            var result = await service.ApplyAdvancedCutPlanAsync(
                video,
                new AdvancedVideoEditPlan
                {
                    Succeeded = true,
                    PlanType = "filler-word-removal"
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "no reviewed cut ranges");
        });
    }

    [TestMethod]
    public void BuildCompositeLayoutArguments_MapsSelectedAudioAndShortestOutput()
    {
        var arguments = VideoToolService.BuildCompositeLayoutArguments(
            "screen.mp4",
            "camera.mp4",
            "composite.mp4",
            "[0:v][1:v]hstack=inputs=2[outv]",
            "camera");

        CollectionAssert.Contains(arguments.ToArray(), "-filter_complex");
        CollectionAssert.Contains(arguments.ToArray(), "[outv]");
        CollectionAssert.Contains(arguments.ToArray(), "1:a:0");
        CollectionAssert.Contains(arguments.ToArray(), "-shortest");
        Assert.IsFalse(arguments.Contains("-an"));
    }

    [TestMethod]
    public async Task ApplyCompositeLayoutPlanAsync_RejectsMissingRecipeBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.ApplyCompositeLayoutPlanAsync(
                new AdvancedVideoEditPlan
                {
                    Succeeded = true,
                    PlanType = "composite-layout"
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "no reviewed layout recipe");
        });
    }

    [TestMethod]
    public async Task ApplyCompositeLayoutPlanAsync_RejectsMissingPlanInputsBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.ApplyCompositeLayoutPlanAsync(
                new AdvancedVideoEditPlan
                {
                    Succeeded = true,
                    PlanType = "composite-layout",
                    CompositeRecipes =
                    [
                        new CompositeVideoLayoutRecipe
                        {
                            Preset = "picture-in-picture",
                            FfmpegFilterComplex = "[0:v][1:v]overlay=0:0[outv]"
                        }
                    ]
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "requires --screen");
        });
    }

    [TestMethod]
    public void BuildWebcamBackgroundFilter_BlursKeyedBackground()
    {
        var filter = VideoToolService.BuildWebcamBackgroundFilter(new WebcamBackgroundProcessingRecipe
        {
            Mode = "blur",
            KeyColor = "0x00ff00",
            Similarity = 0.18d,
            Blend = 0.08d,
            BlurStrength = 12
        });

        StringAssert.Contains(filter, "boxblur=12:1");
        StringAssert.Contains(filter, "chromakey=0x00ff00:0.18:0.08");
        StringAssert.Contains(filter, "overlay=0:0:format=auto[outv]");
    }

    [TestMethod]
    public void BuildWebcamBackgroundFilter_ReplacesKeyedBackgroundWithSolidColor()
    {
        var filter = VideoToolService.BuildWebcamBackgroundFilter(
            new WebcamBackgroundProcessingRecipe
            {
                Mode = "replace",
                KeyColor = "0x00ff00",
                Similarity = 0.2d,
                Blend = 0.05d,
                BackgroundColor = "0x101820"
            },
            width: 320,
            height: 240);

        StringAssert.Contains(filter, "color=c=0x101820:s=320x240[bg]");
        StringAssert.Contains(filter, "chromakey=0x00ff00:0.2:0.05");
    }

    [TestMethod]
    public void BuildWebcamBackgroundFilter_CompositesExternalMaskBackground()
    {
        var recipe = new WebcamBackgroundProcessingRecipe
        {
            ProcessingKind = "external-mask",
            Mode = "blur",
            MaskPath = "mask.mp4",
            InvertMask = true,
            BlurStrength = 18
        };

        var filter = VideoToolService.BuildWebcamBackgroundFilter(recipe, width: 640, height: 360);

        StringAssert.Contains(filter, "[0:v]split[fgsrc][bgsrc]");
        StringAssert.Contains(filter, "[bgsrc]boxblur=18:1[bg]");
        StringAssert.Contains(filter, "[1:v]format=gray,scale=640:360,negate[mask]");
        StringAssert.Contains(filter, "[fgsrc]format=rgba[fg]");
        StringAssert.Contains(filter, "[fg][mask]alphamerge[subject]");
        StringAssert.Contains(filter, "[bg][subject]overlay=0:0:format=auto:shortest=1:eof_action=endall[outv]");
    }

    [TestMethod]
    public void BuildWebcamBackgroundArguments_AddsExternalMaskInput()
    {
        var arguments = VideoToolService.BuildWebcamBackgroundArguments(
            "webcam.mp4",
            "mask.mp4",
            "clean.mp4",
            "[0:v][1:v]alphamerge[outv]",
            includeAudio: true);

        CollectionAssert.Contains(arguments.ToArray(), "webcam.mp4");
        CollectionAssert.Contains(arguments.ToArray(), "mask.mp4");
        CollectionAssert.Contains(arguments.ToArray(), "0:a:0");
        Assert.AreEqual(2, arguments.Count(argument => argument.Equals("-i", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void BuildForegroundMaskFilter_ExtractsKeyedSubjectAlpha()
    {
        var filter = VideoToolService.BuildForegroundMaskFilter(new WebcamMaskGenerationOptions
        {
            Method = "keyed",
            KeyColor = "#00ff00",
            Similarity = 0.2d,
            Blend = 0.05d,
            InvertMask = true
        });

        StringAssert.Contains(filter, "chromakey=0x00ff00:0.2:0.05");
        StringAssert.Contains(filter, "format=rgba,alphaextract,format=gray");
        Assert.IsTrue(filter.EndsWith(",negate", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildForegroundMaskFilter_BuildsClampedLumaThresholdMask()
    {
        var filter = VideoToolService.BuildForegroundMaskFilter(new WebcamMaskGenerationOptions
        {
            Method = "luma",
            LumaThreshold = 400
        });

        Assert.AreEqual("format=gray,lut=y='if(gte(val\\,255)\\,255\\,0)'", filter);
    }

    [TestMethod]
    public void BuildForegroundMaskArguments_OmitsAudioAndWritesVideoMask()
    {
        var arguments = VideoToolService.BuildForegroundMaskArguments(
            "webcam.mp4",
            "mask.mp4",
            "chromakey=0x00ff00:0.18:0.08,format=rgba,alphaextract,format=gray");

        CollectionAssert.Contains(arguments.ToArray(), "-vf");
        CollectionAssert.Contains(arguments.ToArray(), "-an");
        CollectionAssert.Contains(arguments.ToArray(), "libx264");
        CollectionAssert.Contains(arguments.ToArray(), "yuv420p");
        Assert.IsFalse(arguments.Contains("-map"));
    }

    [TestMethod]
    public void BuildPersonSegmentationRunnerArguments_ReplacesQuotedPlaceholders()
    {
        var arguments = VideoToolService.BuildPersonSegmentationRunnerArguments(
            "--input \"{input}\" --output \"{output}\" --model \"{model}\" --label \"person matte\"",
            "C:\\safe content\\webcam.mp4",
            "C:\\safe content\\person mask.mp4",
            "C:\\models\\person.onnx");

        CollectionAssert.AreEqual(
            new[]
            {
                "--input",
                "C:\\safe content\\webcam.mp4",
                "--output",
                "C:\\safe content\\person mask.mp4",
                "--model",
                "C:\\models\\person.onnx",
                "--label",
                "person matte"
            },
            arguments.ToArray());
    }

    [TestMethod]
    public async Task GeneratePersonSegmentationMaskAsync_RequiresExplicitRunnerAcceptance()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "webcam.mp4", [1, 2, 3]);
            var service = CreateService(paths);

            var result = await service.GeneratePersonSegmentationMaskAsync(
                video,
                new PersonSegmentationMaskGenerationOptions
                {
                    RunnerPath = "missing-runner.exe"
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "--accept-external-runner");
        });
    }

    [TestMethod]
    public async Task GeneratePersonSegmentationMaskAsync_RunsConfiguredExternalRunnerAndIndexesWorkspace()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = WindowsPowerShellPath();
            if (runner is null)
            {
                Assert.Inconclusive("Windows PowerShell was not found on this test host.");
                return;
            }

            var video = CreateExistingFileItem(paths, "webcam.mp4", [1, 2, 3, 4]);
            var output = Path.Combine(paths.VideosRoot, "person-mask.mp4");
            var runnerScript = Path.Combine(paths.DocumentsRoot, "fake-person-segmenter.ps1");
            await File.WriteAllTextAsync(
                runnerScript,
                """
                param(
                    [string] $InputPath,
                    [string] $OutputPath
                )

                if (-not (Test-Path -LiteralPath $InputPath)) {
                    exit 3
                }

                [System.IO.File]::WriteAllBytes($OutputPath, [byte[]](11, 22, 33, 44))
                """);
            var service = CreateService(paths);

            var result = await service.GeneratePersonSegmentationMaskAsync(
                video,
                new PersonSegmentationMaskGenerationOptions
                {
                    RunnerPath = runner,
                    ArgumentsTemplate = $"-NoProfile -ExecutionPolicy Bypass -File \"{runnerScript}\" -InputPath \"{{input}}\" -OutputPath \"{{output}}\"",
                    AcceptExternalRunner = true,
                    TimeoutSeconds = 30
                },
                output,
                addToWorkspace: true);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(output, result.OutputPath);
            Assert.IsTrue(File.Exists(output));
            Assert.AreEqual(4, new FileInfo(output).Length);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.PersonSegmentationMaskVideo, result.Item.Kind);
        });
    }

    [TestMethod]
    public async Task GenerateHostedPersonSegmentationMaskAsync_RequiresExplicitAcceptanceBeforeUpload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "webcam.mp4", [1, 2, 3, 4]);
            var handler = new FakeHostedSegmentationHandler([9, 8, 7, 6]);
            var service = CreateService(paths, new HttpClient(handler));

            var result = await service.GenerateHostedPersonSegmentationMaskAsync(
                video,
                new HostedPersonSegmentationMaskGenerationOptions
                {
                    Endpoint = "https://segmenter.example.test/mask"
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "--accept-hosted-service");
            Assert.AreEqual(0, handler.RequestCount);
        });
    }

    [TestMethod]
    public async Task GenerateHostedPersonSegmentationMaskAsync_UploadsToFakeHttpAndIndexesWorkspace()
    {
        await WithTempPathsAsync(async paths =>
        {
            var oldToken = Environment.GetEnvironmentVariable("GOATSHOT_TEST_SEGMENTER_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("GOATSHOT_TEST_SEGMENTER_TOKEN", "secret-token-from-env");
                var sourceBytes = new byte[] { 1, 2, 3, 4, 5 };
                var maskBytes = new byte[] { 9, 8, 7, 6 };
                var video = CreateExistingFileItem(paths, "webcam.mp4", sourceBytes);
                var output = Path.Combine(paths.VideosRoot, "hosted-person-mask.mp4");
                var handler = new FakeHostedSegmentationHandler(maskBytes);
                var service = CreateService(paths, new HttpClient(handler));

                var result = await service.GenerateHostedPersonSegmentationMaskAsync(
                    video,
                    new HostedPersonSegmentationMaskGenerationOptions
                    {
                        Endpoint = "https://segmenter.example.test/mask",
                        ApiKeyEnvironmentVariable = "GOATSHOT_TEST_SEGMENTER_TOKEN",
                        ModelId = "person-v1",
                        AcceptHostedService = true,
                        TimeoutSeconds = 30
                    },
                    output,
                    addToWorkspace: true);

                Assert.IsTrue(result.Succeeded, result.Message);
                Assert.AreEqual(output, result.OutputPath);
                CollectionAssert.AreEqual(maskBytes, await File.ReadAllBytesAsync(output));
                Assert.AreEqual(1, handler.RequestCount);
                Assert.AreEqual(HttpMethod.Post, handler.RequestMethod);
                Assert.AreEqual("https://segmenter.example.test/mask", handler.RequestUri);
                Assert.AreEqual("Bearer", handler.AuthorizationScheme);
                Assert.AreEqual("secret-token-from-env", handler.AuthorizationParameter);
                Assert.IsTrue(handler.Body.Contains("sourceVideo", StringComparison.Ordinal));
                Assert.IsTrue(handler.Body.Contains("responseKind", StringComparison.Ordinal));
                Assert.IsTrue(handler.Body.Contains("mask-video", StringComparison.Ordinal));
                Assert.IsTrue(handler.Body.Contains("modelId", StringComparison.Ordinal));
                Assert.IsTrue(handler.Body.Contains("person-v1", StringComparison.Ordinal));
                Assert.IsNotNull(result.Item);
                Assert.AreEqual(CaptureKind.PersonSegmentationMaskVideo, result.Item.Kind);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GOATSHOT_TEST_SEGMENTER_TOKEN", oldToken);
            }
        });
    }

    [TestMethod]
    public async Task GenerateForegroundMaskAsync_RejectsMissingSourceBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = CreateService(paths);
            var result = await service.GenerateForegroundMaskAsync(
                CreateMissingVideoItem(paths),
                new WebcamMaskGenerationOptions());

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Video file not found:");
        });
    }

    [TestMethod]
    public async Task ApplyWebcamBackgroundPlanAsync_RejectsMissingRecipeBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "webcam.mp4", [1, 2, 3]);
            var service = CreateService(paths);
            var result = await service.ApplyWebcamBackgroundPlanAsync(
                video,
                new AdvancedVideoEditPlan
                {
                    Succeeded = true,
                    PlanType = "webcam-background"
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "no reviewed background recipe");
        });
    }

    [TestMethod]
    public async Task ApplyWebcamBackgroundPlanAsync_RejectsMissingExternalMaskBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "webcam.mp4", [1, 2, 3]);
            var service = CreateService(paths);
            var missingMask = Path.Combine(paths.VideosRoot, "missing-mask.mp4");
            var result = await service.ApplyWebcamBackgroundPlanAsync(
                video,
                new AdvancedVideoEditPlan
                {
                    Succeeded = true,
                    PlanType = "webcam-background",
                    BackgroundRecipe = new WebcamBackgroundProcessingRecipe
                    {
                        ProcessingKind = "external-mask",
                        Mode = "blur",
                        MaskPath = missingMask
                    }
                });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "External mask file not found");
        });
    }

    [TestMethod]
    public async Task BurnSubtitlesAsync_RequiresSubtitleFileBeforeFfmpeg()
    {
        await WithTempPathsAsync(async paths =>
        {
            var video = CreateExistingFileItem(paths, "demo.mp4", [1, 2, 3]);
            var service = CreateService(paths);
            var result = await service.BurnSubtitlesAsync(video, Path.Combine(paths.DocumentsRoot, "missing.srt"));

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Subtitle file not found:");
        });
    }

    [TestMethod]
    public async Task CopySubtitleAsync_CopiesSrtAndCanIndexWorkspace()
    {
        await WithTempPathsAsync(async paths =>
        {
            var source = Path.Combine(paths.DocumentsRoot, "captions.srt");
            await File.WriteAllTextAsync(source, "1\r\n00:00:00,000 --> 00:00:01,000\r\nHello\r\n");
            var output = Path.Combine(paths.DocumentsRoot, "captions-copy.srt");
            var service = CreateService(paths);

            var result = await service.CopySubtitleAsync(source, output, addToWorkspace: true);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(output, result.OutputPath);
            Assert.IsTrue(File.Exists(output));
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.SubtitleFile, result.Item.Kind);
            Assert.AreEqual(1, new WorkspaceStore(paths, new AppSettings()).Load().Count);
        });
    }

    private static VideoToolService CreateService(AppPaths paths)
    {
        return CreateService(paths, httpClient: null);
    }

    private static VideoToolService CreateService(AppPaths paths, HttpClient? httpClient)
    {
        var settings = new AppSettings();
        var store = new WorkspaceStore(paths, settings);
        return new VideoToolService(paths, store, httpClient);
    }

    private static CaptureItem CreateMissingVideoItem(AppPaths paths)
    {
        return new CaptureItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CaptureKind.RecordingMp4,
            FilePath = Path.Combine(paths.VideosRoot, "missing.mp4"),
            ThumbnailPath = string.Empty
        };
    }

    private static CaptureItem CreateExistingFileItem(AppPaths paths, string fileName, byte[] bytes)
    {
        var filePath = Path.Combine(paths.VideosRoot, fileName);
        File.WriteAllBytes(filePath, bytes);
        return new CaptureItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = CaptureKind.RecordingMp4,
            FilePath = filePath,
            ThumbnailPath = filePath,
            Bytes = bytes.Length
        };
    }

    private static string? WindowsPowerShellPath()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemPowerShell = string.IsNullOrWhiteSpace(windir)
            ? null
            : Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!string.IsNullOrWhiteSpace(systemPowerShell) && File.Exists(systemPowerShell))
        {
            return systemPowerShell;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), "powershell.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private sealed class FakeHostedSegmentationHandler(byte[] responseBytes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? RequestMethod { get; private set; }
        public string RequestUri { get; private set; } = string.Empty;
        public string AuthorizationScheme { get; private set; } = string.Empty;
        public string AuthorizationParameter { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            AuthorizationScheme = request.Headers.Authorization?.Scheme ?? string.Empty;
            AuthorizationParameter = request.Headers.Authorization?.Parameter ?? string.Empty;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBytes)
            };
        }
    }
}

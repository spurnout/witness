using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AdvancedVideoEditPlannerServiceTests
{
    [TestMethod]
    public void ParseSilenceDetectOutput_BuildsPreviewCutsWithPadding()
    {
        const string output = """
            [silencedetect @ 000001] silence_start: 1.2
            [silencedetect @ 000001] silence_end: 3.9 | silence_duration: 2.7
            [silencedetect @ 000001] silence_start: 8
            [silencedetect @ 000001] silence_end: 9 | silence_duration: 1
            """;

        var spans = AdvancedVideoEditPlannerService.ParseSilenceDetectOutput(output);
        var cuts = AdvancedVideoEditPlannerService.BuildSilenceCuts(spans, TimeSpan.FromSeconds(0.2));

        Assert.AreEqual(2, spans.Count);
        Assert.AreEqual(2, cuts.Count);
        Assert.AreEqual(1.4d, cuts[0].StartSeconds);
        Assert.AreEqual(3.7d, cuts[0].EndSeconds);
        Assert.AreEqual(2.3d, cuts[0].DurationSeconds);
        Assert.AreEqual("Detected silence", cuts[0].Reason);
    }

    [TestMethod]
    public void BuildTranscriptTermCuts_MatchesWholeWordsAndAddsPadding()
    {
        var segments = new List<TranscriptSegment>
        {
            new()
            {
                Index = 1,
                Start = TimeSpan.FromSeconds(1),
                End = TimeSpan.FromSeconds(3),
                Text = "Um, open settings."
            },
            new()
            {
                Index = 2,
                Start = TimeSpan.FromSeconds(4),
                End = TimeSpan.FromSeconds(6),
                Text = "The thumbnail is ready."
            },
            new()
            {
                Index = 3,
                Start = TimeSpan.FromSeconds(8),
                End = TimeSpan.FromSeconds(9),
                Text = "Upload failed."
            }
        };

        var cuts = AdvancedVideoEditPlannerService.BuildTranscriptTermCuts(
            segments,
            ["um", "failed"],
            TimeSpan.FromSeconds(0.25));

        Assert.AreEqual(2, cuts.Count);
        Assert.AreEqual(0.75d, cuts[0].StartSeconds);
        Assert.AreEqual(3.25d, cuts[0].EndSeconds);
        StringAssert.Contains(cuts[0].Reason, "um");
        StringAssert.Contains(cuts[1].Reason, "failed");
        Assert.IsFalse(cuts.Any(cut => cut.SourceText.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task PlanFillerWordRemovalAsync_ParsesSrtAndKeepsPreviewBoundary()
    {
        await WithTempPathsAsync(async paths =>
        {
            var srt = Path.Combine(paths.DocumentsRoot, "captions.srt");
            await File.WriteAllTextAsync(
                srt,
                """
                1
                00:00:01,000 --> 00:00:02,000
                Um, open settings.

                2
                00:00:03,000 --> 00:00:04,000
                The upload is ready.

                3
                00:00:05,000 --> 00:00:06,000
                You know, retry the upload.
                """);

            var planner = new AdvancedVideoEditPlannerService(paths);
            var plan = await planner.PlanFillerWordRemovalAsync(srt);

            Assert.IsTrue(plan.Succeeded, plan.Message);
            Assert.AreEqual("filler-word-removal", plan.PlanType);
            Assert.AreEqual("preview-only", plan.Mode);
            Assert.AreEqual(2, plan.Cuts.Count);
            Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("Preview only", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task PlanTranscriptTermsAsync_ParsesTimestampedTranscriptText()
    {
        await WithTempPathsAsync(async paths =>
        {
            var transcript = Path.Combine(paths.DocumentsRoot, "transcript.txt");
            await File.WriteAllTextAsync(
                transcript,
                """
                [0:01] Open the upload queue.
                [0:04] Retry failed upload.
                [0:08] Confirm success.
                """);

            var planner = new AdvancedVideoEditPlannerService(paths);
            var plan = await planner.PlanTranscriptTermsAsync(transcript, ["failed"]);

            Assert.IsTrue(plan.Succeeded, plan.Message);
            Assert.AreEqual("text-based-edit", plan.PlanType);
            Assert.AreEqual(1, plan.Cuts.Count);
            Assert.AreEqual(3.75d, plan.Cuts[0].StartSeconds);
            Assert.AreEqual(8.25d, plan.Cuts[0].EndSeconds);
            StringAssert.Contains(plan.Cuts[0].SourceText, "Retry failed upload.");
        });
    }

    [TestMethod]
    public void BuildCompositeLayoutPlan_ReturnsRecipeAndBackgroundCapabilityBoundary()
    {
        var paths = AppPaths.Create(new AppSettings());
        var planner = new AdvancedVideoEditPlannerService(paths);

        var plan = planner.BuildCompositeLayoutPlan(new CompositeVideoLayoutRequest
        {
            Preset = "side-by-side",
            ScreenPath = "screen.mp4",
            CameraPath = "camera.mp4",
            Width = 1280,
            Height = 720
        });

        Assert.IsTrue(plan.Succeeded, plan.Message);
        Assert.AreEqual("composite-layout", plan.PlanType);
        Assert.AreEqual(1, plan.CompositeRecipes.Count);
        Assert.AreEqual("side-by-side", plan.CompositeRecipes[0].Preset);
        StringAssert.Contains(plan.CompositeRecipes[0].FfmpegFilterComplex, "hstack=inputs=2");
        Assert.IsNotNull(plan.CapabilityProbe);
        Assert.AreEqual("webcam-background-processing", plan.CapabilityProbe!.Capability);
        Assert.IsTrue(plan.CapabilityProbe.PreviewRequired);
    }

    [TestMethod]
    public void BuildWebcamBackgroundPlan_ReturnsKeyedRecipeAndPreviewBoundary()
    {
        var paths = AppPaths.Create(new AppSettings());
        var planner = new AdvancedVideoEditPlannerService(paths);

        var plan = planner.BuildWebcamBackgroundPlan(new WebcamBackgroundProcessingRequest
        {
            SourcePath = "webcam.mp4",
            Mode = "replace",
            KeyColor = "#00ff00",
            Similarity = 0.2d,
            Blend = 0.05d,
            BackgroundColor = "101820"
        });

        Assert.IsTrue(plan.Succeeded, plan.Message);
        Assert.AreEqual("webcam-background", plan.PlanType);
        Assert.IsNotNull(plan.BackgroundRecipe);
        Assert.AreEqual("replace", plan.BackgroundRecipe!.Mode);
        Assert.AreEqual("0x00ff00", plan.BackgroundRecipe.KeyColor);
        Assert.AreEqual("0x101820", plan.BackgroundRecipe.BackgroundColor);
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("not automatic human segmentation", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BuildWebcamBackgroundPlan_ReturnsExternalMaskRecipeAndHonestBoundary()
    {
        var paths = AppPaths.Create(new AppSettings());
        var planner = new AdvancedVideoEditPlannerService(paths);

        var plan = planner.BuildWebcamBackgroundPlan(new WebcamBackgroundProcessingRequest
        {
            SourcePath = "webcam.mp4",
            Mode = "blur",
            MaskPath = "person-mask.mp4",
            InvertMask = true,
            BlurStrength = 24
        });

        Assert.IsTrue(plan.Succeeded, plan.Message);
        Assert.AreEqual("webcam-background", plan.PlanType);
        Assert.IsNotNull(plan.BackgroundRecipe);
        Assert.AreEqual("external-mask", plan.BackgroundRecipe!.ProcessingKind);
        Assert.IsTrue(plan.BackgroundRecipe.MaskPath!.EndsWith("person-mask.mp4", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(plan.BackgroundRecipe.InvertMask);
        Assert.AreEqual(24, plan.BackgroundRecipe.BlurStrength);
        StringAssert.Contains(plan.BackgroundRecipe.Notes, "does not itself generate a mask");
        Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("deterministic mask generator", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void NormalizeFfmpegColor_NormalizesCommonColorForms()
    {
        Assert.AreEqual("0x00ff00", AdvancedVideoEditPlannerService.NormalizeFfmpegColor("green", "0x111111"));
        Assert.AreEqual("0xabcdef", AdvancedVideoEditPlannerService.NormalizeFfmpegColor("#abcdef", "0x111111"));
        Assert.AreEqual("0x123456", AdvancedVideoEditPlannerService.NormalizeFfmpegColor("123456", "0x111111"));
    }

    [TestMethod]
    public async Task WritePlanAsync_WritesJsonPlanFile()
    {
        await WithTempPathsAsync(async paths =>
        {
            var planner = new AdvancedVideoEditPlannerService(paths);
            var plan = planner.BuildCompositeLayoutPlan(new CompositeVideoLayoutRequest());
            var output = Path.Combine(paths.DocumentsRoot, "plan.json");

            var path = await planner.WritePlanAsync(plan, output);

            Assert.AreEqual(output, path);
            Assert.IsTrue(File.Exists(path));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.AreEqual("composite-layout", document.RootElement.GetProperty("planType").GetString());
            Assert.AreEqual("preview-only", document.RootElement.GetProperty("mode").GetString());
        });
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

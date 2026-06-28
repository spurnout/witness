using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class GifTimingPlannerTests
{
    [TestMethod]
    public void BuildDelayPattern_ApproximatesSixtyFpsWithTenAndTwentyMillisecondDelays()
    {
        var delays = GifTimingPlanner.BuildDelayPattern(60);

        CollectionAssert.AreEqual(new[] { 20, 20, 10 }, delays.ToArray());
    }

    [TestMethod]
    public void Plan_CapsGifTimingAndRecommendsCompanionAboveOneHundredFps()
    {
        var plan = GifTimingPlanner.Plan(
            new RecordingSettings { FramesPerSecond = 120 },
            new AnimationExportOptions { CompanionFormat = "webm" });

        Assert.AreEqual(120, plan.CaptureFrameRate);
        Assert.AreEqual(100, plan.EffectiveGifFrameRate);
        Assert.IsTrue(plan.CompanionRecommended);
        Assert.AreEqual("webm", plan.CompanionFormat);
        StringAssert.Contains(plan.Message, "GIF timing capped");
    }

    [TestMethod]
    public void Plan_ClampsFrameRateAndFrameBudget()
    {
        var plan = GifTimingPlanner.Plan(
            new RecordingSettings { FramesPerSecond = 500 },
            new AnimationExportOptions { MaxFrames = 99_000 });

        Assert.AreEqual(120, plan.CaptureFrameRate);
        Assert.AreEqual(GifTimingPlanner.AbsoluteMaxFrames, plan.MaxFrames);
    }

    [TestMethod]
    public void Plan_MaxSpeedUsesTenMillisecondDelay()
    {
        var plan = GifTimingPlanner.Plan(
            new RecordingSettings { FramesPerSecond = 60 },
            new AnimationExportOptions { GifTimingMode = "max-speed" });

        CollectionAssert.AreEqual(new[] { 10 }, plan.FrameDelaysMs.ToArray());
    }
}

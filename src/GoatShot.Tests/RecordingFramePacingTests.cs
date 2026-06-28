using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingFramePacingTests
{
    [TestMethod]
    public void ExpectedMp4FrameCount_UsesDurationAndFps()
    {
        Assert.AreEqual(80, RecordingService.ExpectedMp4FrameCount(TimeSpan.FromSeconds(8), 10));
        Assert.AreEqual(375, RecordingService.ExpectedMp4FrameCount(TimeSpan.FromSeconds(12.5), 30));
    }

    [TestMethod]
    public void ExpectedMp4FrameCount_ClampsToRecordingFrameLimit()
    {
        Assert.AreEqual(1_800, RecordingService.ExpectedMp4FrameCount(TimeSpan.FromMinutes(20), 60));
    }

    [TestMethod]
    public void DesiredPacedMp4FrameCount_FillsMissedConstantFpsSlots()
    {
        var desired = RecordingService.DesiredPacedMp4FrameCount(
            TimeSpan.FromSeconds(4.5),
            TimeSpan.FromSeconds(8),
            framesPerSecond: 10,
            currentFrameCount: 32);

        Assert.AreEqual(45, desired);
    }

    [TestMethod]
    public void DesiredPacedMp4FrameCount_NeverMovesBackwardOrPastExpectedDuration()
    {
        var keepsCurrent = RecordingService.DesiredPacedMp4FrameCount(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(8),
            framesPerSecond: 10,
            currentFrameCount: 20);
        var clampsToExpected = RecordingService.DesiredPacedMp4FrameCount(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(8),
            framesPerSecond: 10,
            currentFrameCount: 72);

        Assert.AreEqual(20, keepsCurrent);
        Assert.AreEqual(80, clampsToExpected);
    }
}

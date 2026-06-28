using GoatShot.App.Models;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingCaptureTargetTests
{
    [TestMethod]
    public void Region_NormalizesBoundsAndMapsFrameCaptureKind()
    {
        var target = RecordingCaptureTarget.Region(new CaptureBounds
        {
            X = 10,
            Y = 20,
            Width = 0,
            Height = -5
        });

        Assert.AreEqual(RecordingCaptureTargetKind.Region, target.Kind);
        Assert.AreEqual(CaptureKind.Region, target.FrameCaptureKind);
        Assert.IsTrue(target.UsesExplicitBounds);
        Assert.AreEqual(1, target.Bounds?.Width);
        Assert.AreEqual(1, target.Bounds?.Height);
        StringAssert.Contains(target.DisplayName, "region");
    }

    [TestMethod]
    public void ActiveMonitor_DoesNotUseExplicitBounds()
    {
        var target = RecordingCaptureTarget.ActiveMonitor();

        Assert.IsTrue(target.IsActiveMonitor);
        Assert.IsFalse(target.UsesExplicitBounds);
        Assert.AreEqual(CaptureKind.ActiveMonitor, target.FrameCaptureKind);
        Assert.AreEqual("active monitor", target.DisplayName);
    }

    [TestMethod]
    public void AllMonitors_MapsToAllMonitorCaptureKind()
    {
        var target = RecordingCaptureTarget.AllMonitors();

        Assert.IsFalse(target.IsActiveMonitor);
        Assert.IsFalse(target.UsesExplicitBounds);
        Assert.AreEqual(RecordingCaptureTargetKind.AllMonitors, target.Kind);
        Assert.AreEqual(CaptureKind.AllMonitors, target.FrameCaptureKind);
        Assert.AreEqual("all monitors", target.DisplayName);
    }
}

using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class WindowsGraphicsCaptureFrameSourceTests
{
    [TestMethod]
    public void SupportsTarget_CoversActiveWindowAndBoundedTargets()
    {
        Assert.IsTrue(WindowsGraphicsCaptureFrameSource.SupportsTarget(RecordingCaptureTarget.ActiveMonitor()));
        Assert.IsTrue(WindowsGraphicsCaptureFrameSource.SupportsTarget(RecordingCaptureTarget.ActiveWindow()));
        Assert.IsTrue(WindowsGraphicsCaptureFrameSource.SupportsTarget(RecordingCaptureTarget.Region(new CaptureBounds
        {
            X = 10,
            Y = 20,
            Width = 640,
            Height = 360
        })));
        Assert.IsTrue(WindowsGraphicsCaptureFrameSource.SupportsTarget(RecordingCaptureTarget.FixedRegion(new CaptureBounds
        {
            X = 30,
            Y = 40,
            Width = 1280,
            Height = 720
        })));
    }

    [TestMethod]
    public void SupportsTarget_RejectsUnboundedRegionTargets()
    {
        Assert.IsFalse(WindowsGraphicsCaptureFrameSource.SupportsTarget(new RecordingCaptureTarget
        {
            Kind = RecordingCaptureTargetKind.Region
        }));
        Assert.IsFalse(WindowsGraphicsCaptureFrameSource.SupportsTarget(new RecordingCaptureTarget
        {
            Kind = RecordingCaptureTargetKind.FixedRegion
        }));
    }

    [TestMethod]
    public void SupportsTarget_RejectsAllMonitorsForStreamingFrameSource()
    {
        Assert.IsFalse(WindowsGraphicsCaptureFrameSource.SupportsTarget(RecordingCaptureTarget.AllMonitors()));
    }
}

using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ScreenshotSourceStampTests
{
    [TestMethod]
    public void ShouldStampFromTarget_TrueForClickedWindowLikeTargetsWithAHandle()
    {
        Assert.IsTrue(ScreenshotService.ShouldStampFromTarget(Target(CaptureOverlayTargetKind.Window, handle: 42)));
        Assert.IsTrue(ScreenshotService.ShouldStampFromTarget(Target(CaptureOverlayTargetKind.ContentArea, handle: 42)));
        Assert.IsTrue(ScreenshotService.ShouldStampFromTarget(Target(CaptureOverlayTargetKind.ControlArea, handle: 42)));
    }

    [TestMethod]
    public void ShouldStampFromTarget_FalseForMonitorNullOrHandlelessTargets()
    {
        // Drag selections carry no target, monitors have no owning process, and synthetic
        // targets carry handle zero — all of those keep the pre-overlay foreground context.
        Assert.IsFalse(ScreenshotService.ShouldStampFromTarget(null));
        Assert.IsFalse(ScreenshotService.ShouldStampFromTarget(Target(CaptureOverlayTargetKind.Monitor, handle: 42)));
        Assert.IsFalse(ScreenshotService.ShouldStampFromTarget(Target(CaptureOverlayTargetKind.Window, handle: 0)));
    }

    private static CaptureOverlayTarget Target(CaptureOverlayTargetKind kind, long handle)
    {
        return new CaptureOverlayTarget(
            "target:test",
            "Test target",
            kind,
            new CaptureBounds { X = 0, Y = 0, Width = 100, Height = 100 },
            NativeHandle: handle);
    }
}

using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureOverlayGeometryTests
{
    [TestMethod]
    public void ResolveSelection_SnapsToNearWindowTargetAndAppliesContextPadding()
    {
        var target = new CaptureOverlayTarget(
            "window:test",
            "Window: Test app",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 100, Y = 100, Width = 300, Height = 200 });

        var selection = CaptureOverlayGeometry.ResolveSelection(
            94,
            103,
            405,
            301,
            new CaptureOverlayGeometryOptions(
                VirtualBounds(),
                [target],
                SnapThreshold: 12,
                ContextPadding: 10));

        Assert.AreSame(target, selection.Target);
        AssertBounds(100, 100, 300, 200, selection.SnappedBounds);
        AssertBounds(90, 90, 320, 220, selection.FinalBounds);
        StringAssert.Contains(selection.StatusText, "Window: Test app");
        StringAssert.Contains(selection.StatusText, "10px context");
    }

    [TestMethod]
    public void ResolveSelection_SnapsFreeRegionEdgesToVirtualScreen()
    {
        var selection = CaptureOverlayGeometry.ResolveSelection(
            7,
            8,
            251,
            189,
            new CaptureOverlayGeometryOptions(
                VirtualBounds(),
                Array.Empty<CaptureOverlayTarget>(),
                SnapThreshold: 12,
                ContextPadding: 0));

        Assert.IsNull(selection.Target);
        AssertBounds(0, 0, 251, 189, selection.FinalBounds);
    }

    [TestMethod]
    public void ApplyPadding_ClampsToContainingBounds()
    {
        var padded = CaptureOverlayGeometry.ApplyPadding(
            new CaptureBounds { X = 5, Y = 8, Width = 40, Height = 50 },
            16,
            new CaptureBounds { X = 0, Y = 0, Width = 100, Height = 100 });

        AssertBounds(0, 0, 61, 74, padded);
    }

    [TestMethod]
    public void FindNearestChooserTarget_IgnoresNonChooserControlTargets()
    {
        var monitor = new CaptureOverlayTarget(
            "monitor:1",
            "Primary monitor",
            CaptureOverlayTargetKind.Monitor,
            new CaptureBounds { X = 0, Y = 0, Width = 1000, Height = 800 });
        var hiddenControl = new CaptureOverlayTarget(
            "control:1",
            "Control: Hidden chooser target",
            CaptureOverlayTargetKind.ControlArea,
            new CaptureBounds { X = 20, Y = 20, Width = 200, Height = 120 },
            ShowInChooser: false);
        var window = new CaptureOverlayTarget(
            "window:1",
            "Window: Nearby",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 300, Y = 200, Width = 250, Height = 200 });

        var target = CaptureOverlayGeometry.FindNearestChooserTarget(
            30,
            30,
            [hiddenControl, window, monitor]);

        Assert.AreSame(monitor, target);
    }

    [TestMethod]
    public void ResolveLensCrop_ClampsNearSourceEdges()
    {
        var crop = CaptureOverlayGeometry.ResolveLensCrop(
            VirtualBounds(),
            cursorScreenX: 2,
            cursorScreenY: 3,
            sourcePixelWidth: 1000,
            sourcePixelHeight: 800,
            radius: 9);

        Assert.AreEqual(0, crop.X);
        Assert.AreEqual(0, crop.Y);
        Assert.AreEqual(19, crop.Width);
        Assert.AreEqual(19, crop.Height);
        Assert.AreEqual(2, crop.CursorScreenX);
        Assert.AreEqual(3, crop.CursorScreenY);
    }

    private static CaptureBounds VirtualBounds()
    {
        return new CaptureBounds
        {
            X = 0,
            Y = 0,
            Width = 1000,
            Height = 800
        };
    }

    private static void AssertBounds(int x, int y, int width, int height, CaptureBounds bounds)
    {
        Assert.AreEqual(x, bounds.X);
        Assert.AreEqual(y, bounds.Y);
        Assert.AreEqual(width, bounds.Width);
        Assert.AreEqual(height, bounds.Height);
    }
}

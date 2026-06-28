using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class WindowsGraphicsCaptureEngineTests
{
    [TestMethod]
    public void EngineMetadata_AdvertisesProductionActiveMonitorScope()
    {
        var engine = new WindowsGraphicsCaptureEngine();

        Assert.IsTrue(engine.IsProductionEngine);
        Assert.AreEqual("Windows.Graphics.Capture", engine.EngineName);
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.ActiveMonitor));
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.ActiveWindow));
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.Fullscreen));
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.AllMonitors));
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.Region));
        Assert.IsTrue(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.FixedRegion));
        Assert.IsFalse(WindowsGraphicsCaptureEngine.SupportsKind(CaptureKind.ScrollingWindow));
    }

    [TestMethod]
    public void Diagnostics_ReportsSupportedAndUnsupportedStillCaptureKinds()
    {
        var record = CaptureEngineDiagnostics.Build(
            new WindowsGraphicsCaptureEngine(),
            new ProviderHealth(true, "healthy"));

        CollectionAssert.AreEquivalent(
            new[]
            {
                "region",
                "fullscreen",
                "all-monitors",
                "active-window",
                "active-monitor",
                "fixed-region"
            },
            record.SupportedCaptureKinds.ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "scrolling-window" },
            record.UnsupportedCaptureKinds.ToArray());
        StringAssert.Contains(record.InteractiveRegionStatus, "Interactive region selection");
        StringAssert.Contains(record.FallbackStatus, "default GDI screenshot path");
    }
}

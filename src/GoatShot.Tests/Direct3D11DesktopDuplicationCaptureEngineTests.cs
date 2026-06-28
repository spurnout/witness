using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class Direct3D11DesktopDuplicationCaptureEngineTests
{
    [TestMethod]
    public void EngineMetadata_AdvertisesProductionActiveMonitorScope()
    {
        var engine = new Direct3D11DesktopDuplicationCaptureEngine();

        Assert.IsTrue(engine.IsProductionEngine);
        Assert.AreEqual("Direct3D11 desktop duplication", engine.EngineName);
        Assert.IsTrue(Direct3D11DesktopDuplicationCaptureEngine.SupportsKind(CaptureKind.ActiveMonitor));
        Assert.IsFalse(Direct3D11DesktopDuplicationCaptureEngine.SupportsKind(CaptureKind.Region));
        Assert.IsFalse(Direct3D11DesktopDuplicationCaptureEngine.SupportsKind(CaptureKind.ActiveWindow));
    }

    [TestMethod]
    public void Diagnostics_ReportsDxgiAsActiveMonitorOnly()
    {
        var record = CaptureEngineDiagnostics.Build(
            new Direct3D11DesktopDuplicationCaptureEngine(),
            new ProviderHealth(false, "no presented frame"));

        CollectionAssert.AreEquivalent(
            new[] { "active-monitor" },
            record.SupportedCaptureKinds.ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "region",
                "fullscreen",
                "all-monitors",
                "active-window",
                "scrolling-window",
                "fixed-region"
            },
            record.UnsupportedCaptureKinds.ToArray());
        StringAssert.Contains(record.InteractiveRegionStatus, "does not support region capture");
        StringAssert.Contains(record.FallbackStatus, "diagnostic-only");
    }
}

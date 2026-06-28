using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionStitchPlannerServiceTests
{
    [TestMethod]
    public void Plan_CreatesBoundedVerticalTilePlanForTallPage()
    {
        var manifest = BrowserExtensionStitchPlannerService.Plan(new BrowserExtensionStitchPlanRequest
        {
            FullPageWidth = 1280,
            FullPageHeight = 2400,
            ViewportWidth = 1280,
            ViewportHeight = 720,
            DevicePixelRatio = 1.25,
            OverlapPixels = 80,
            StickyHeaderMitigationPixels = 40
        });

        Assert.AreEqual("planned", manifest.Status);
        Assert.AreEqual(manifest.Tiles.Count, manifest.TileCount);
        Assert.IsTrue(manifest.TileCount > 1);
        Assert.IsFalse(manifest.HorizontalScrollIncluded);
        Assert.AreEqual(0, manifest.Tiles[0].ScrollY);
        Assert.AreEqual(2400 - 720, manifest.Tiles[^1].ScrollY);
        Assert.AreEqual(1.25, manifest.Tiles[0].DevicePixelRatio);
        Assert.IsTrue(manifest.Tiles.All(tile => tile.Width > 0 && tile.Height > 0));
    }

    [TestMethod]
    public void Plan_IncludesHorizontalTilesForWidePage()
    {
        var manifest = BrowserExtensionStitchPlannerService.Plan(new BrowserExtensionStitchPlanRequest
        {
            FullPageWidth = 2400,
            FullPageHeight = 1200,
            ViewportWidth = 1000,
            ViewportHeight = 700,
            IncludeHorizontalScroll = true,
            OverlapPixels = 100
        });

        Assert.AreEqual("planned", manifest.Status);
        Assert.IsTrue(manifest.HorizontalScrollIncluded);
        Assert.IsTrue(manifest.Tiles.Any(tile => tile.ScrollX > 0));
        Assert.IsTrue(manifest.Tiles.Any(tile => tile.ScrollY > 0));
        Assert.AreEqual(2400 - 1000, manifest.Tiles.Max(tile => tile.ScrollX));
    }

    [TestMethod]
    public void Plan_CapsLargePagesWithPartialStatus()
    {
        var manifest = BrowserExtensionStitchPlannerService.Plan(new BrowserExtensionStitchPlanRequest
        {
            FullPageWidth = 5000,
            FullPageHeight = 10000,
            ViewportWidth = 800,
            ViewportHeight = 600,
            IncludeHorizontalScroll = true,
            MaxTileCount = 5,
            OverlapPixels = 50
        });

        Assert.AreEqual("planned-partial", manifest.Status);
        Assert.AreEqual(5, manifest.TileCount);
        Assert.AreEqual(5, manifest.Tiles.Count);
        Assert.IsTrue(manifest.Warnings.Any(warning => warning.Contains("max tile count", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Plan_BlocksInvalidGeometry()
    {
        var manifest = BrowserExtensionStitchPlannerService.Plan(new BrowserExtensionStitchPlanRequest
        {
            FullPageWidth = 0,
            FullPageHeight = 1200,
            ViewportWidth = 800,
            ViewportHeight = 600
        });

        Assert.AreEqual("blocked", manifest.Status);
        Assert.AreEqual(0, manifest.TileCount);
        Assert.IsTrue(manifest.Warnings.Count > 0);
    }
}

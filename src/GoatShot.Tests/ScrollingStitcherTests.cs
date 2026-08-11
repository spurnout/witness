using System.Drawing;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ScrollingStitcherTests
{
    [TestMethod]
    public void Profiles_ApplyBrowserTableAndDocumentDefaults()
    {
        var browser = ScrollingCaptureProfiles.For("chromium").Normalize();
        var table = ScrollingCaptureProfiles.For("large-table").Normalize();
        var document = ScrollingCaptureProfiles.For("pdf").Normalize();

        Assert.AreEqual("browser", browser.Profile);
        Assert.AreEqual(ScrollingCaptureAxis.Vertical, browser.Axis);
        Assert.IsTrue(browser.AutoDetectStickyRegion);
        Assert.IsTrue(browser.MaxFrames > new ScrollingCaptureOptions().MaxFrames);

        Assert.AreEqual("table", table.Profile);
        Assert.AreEqual(ScrollingCaptureAxis.Horizontal, table.Axis);
        Assert.IsTrue(table.MaximumOverlapPixels > browser.MaximumOverlapPixels);

        Assert.AreEqual("document", document.Profile);
        Assert.AreEqual(ScrollingCaptureAxis.Vertical, document.Axis);
        Assert.IsTrue(document.MaximumAutoStickyPixels < browser.MaximumAutoStickyPixels);
    }

    [TestMethod]
    public void Stitch_Vertical_OmitsRepeatedStickyHeader()
    {
        using var first = CreateVerticalFrame(startRow: 0, stickyHeight: 10, contentHeight: 80, stickyColor: Color.DarkRed);
        using var second = CreateVerticalFrame(startRow: 56, stickyHeight: 10, contentHeight: 80, stickyColor: Color.DarkRed);

        using var stitched = ScrollingStitcher.Stitch(
            [first, second],
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Vertical,
                StickyPixels = 10,
                MinimumOverlapPixels = 8,
                MaximumOverlapPixels = 48
            });

        Assert.AreEqual(40, stitched.Width);
        Assert.AreEqual(146, stitched.Height);
        Assert.AreNotEqual(Color.DarkRed.ToArgb(), stitched.GetPixel(4, 90).ToArgb());
        Assert.AreEqual(RowColor(80).ToArgb(), stitched.GetPixel(4, 90).ToArgb());
    }

    [TestMethod]
    public void Stitch_Horizontal_OmitsRepeatedStickyLeftEdge()
    {
        using var first = CreateHorizontalFrame(startColumn: 0, stickyWidth: 8, contentWidth: 80, stickyColor: Color.DarkBlue);
        using var second = CreateHorizontalFrame(startColumn: 56, stickyWidth: 8, contentWidth: 80, stickyColor: Color.DarkBlue);

        using var stitched = ScrollingStitcher.Stitch(
            [first, second],
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Horizontal,
                StickyPixels = 8,
                MinimumOverlapPixels = 8,
                MaximumOverlapPixels = 48
            });

        Assert.AreEqual(144, stitched.Width);
        Assert.AreEqual(36, stitched.Height);
        Assert.AreNotEqual(Color.DarkBlue.ToArgb(), stitched.GetPixel(88, 4).ToArgb());
        Assert.AreEqual(ColumnColor(80).ToArgb(), stitched.GetPixel(88, 4).ToArgb());
    }

    [TestMethod]
    public void Stitch_Vertical_AutoDetectsRepeatedStickyHeader()
    {
        using var first = CreateVerticalFrame(startRow: 0, stickyHeight: 10, contentHeight: 80, stickyColor: Color.DarkRed);
        using var second = CreateVerticalFrame(startRow: 56, stickyHeight: 10, contentHeight: 80, stickyColor: Color.DarkRed);

        var detected = ScrollingStitcher.EffectiveStickyPixels(
            first,
            second,
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Vertical,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 40
            }.Normalize());

        using var stitched = ScrollingStitcher.Stitch(
            [first, second],
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Vertical,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 40,
                MinimumOverlapPixels = 8,
                MaximumOverlapPixels = 48
            });

        Assert.AreEqual(10, detected);
        Assert.AreEqual(146, stitched.Height);
        Assert.AreEqual(RowColor(80).ToArgb(), stitched.GetPixel(4, 90).ToArgb());
    }

    [TestMethod]
    public void Stitch_Horizontal_AutoDetectsRepeatedStickyLeftEdge()
    {
        using var first = CreateHorizontalFrame(startColumn: 0, stickyWidth: 8, contentWidth: 80, stickyColor: Color.DarkBlue);
        using var second = CreateHorizontalFrame(startColumn: 56, stickyWidth: 8, contentWidth: 80, stickyColor: Color.DarkBlue);

        var detected = ScrollingStitcher.EffectiveStickyPixels(
            first,
            second,
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Horizontal,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 40
            }.Normalize());

        using var stitched = ScrollingStitcher.Stitch(
            [first, second],
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Horizontal,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 40,
                MinimumOverlapPixels = 8,
                MaximumOverlapPixels = 48
            });

        Assert.AreEqual(8, detected);
        Assert.AreEqual(144, stitched.Width);
        Assert.AreEqual(ColumnColor(80).ToArgb(), stitched.GetPixel(88, 4).ToArgb());
    }

    [TestMethod]
    public void LooksLikeSameFrame_IgnoresStickyRegionWhenConfigured()
    {
        using var previous = CreateVerticalFrame(startRow: 0, stickyHeight: 12, contentHeight: 50, stickyColor: Color.DarkRed);
        using var current = CreateVerticalFrame(startRow: 0, stickyHeight: 12, contentHeight: 50, stickyColor: Color.DarkBlue);

        var same = ScrollingStitcher.LooksLikeSameFrame(
            previous,
            current,
            new ScrollingCaptureOptions
            {
                Axis = ScrollingCaptureAxis.Vertical,
                StickyPixels = 12
            });

        Assert.IsTrue(same);
    }

    [TestMethod]
    public async Task StressFixtures_GenerateRepeatableScrollTargetsAndStitches()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "GoatShotTests",
            $"scrolling-fixtures-{Guid.NewGuid():N}");

        try
        {
            var report = await ScrollingCaptureStressService.GenerateAsync(outputDirectory);

            Assert.AreEqual(5, report.Scenarios.Count);
            StringAssert.Contains(report.PrivacyNote, "no live desktop pixels");
            StringAssert.Contains(report.RetryGuidance, "receipts image stitch");

            foreach (var scenario in report.Scenarios)
            {
                Assert.IsTrue(File.Exists(scenario.StitchedPath), scenario.StitchedPath);
                Assert.AreEqual(scenario.FrameCount, scenario.FramePaths.Count);
                Assert.IsTrue(scenario.FramePaths.All(File.Exists), scenario.Name);
                Assert.AreEqual(scenario.ExpectedStickyPixels, scenario.DetectedStickyPixels, scenario.Name);
                Assert.AreEqual(scenario.ExpectedOverlapPixels, scenario.BestOverlapPixels, scenario.Name);
                Assert.AreEqual(scenario.ExpectedStitchedWidth, scenario.StitchedWidth, scenario.Name);
                Assert.AreEqual(scenario.ExpectedStitchedHeight, scenario.StitchedHeight, scenario.Name);
            }
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task StressFixtures_CoverStickyHeaderAndHorizontalTableScenarios()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "GoatShotTests",
            $"scrolling-table-{Guid.NewGuid():N}");

        try
        {
            var report = await ScrollingCaptureStressService.GenerateAsync(outputDirectory);

            var stickyHeader = report.Scenarios.Single(scenario => scenario.Name == "browser-sticky-header");
            Assert.AreEqual(ScrollingCaptureAxis.Vertical, stickyHeader.Axis);
            Assert.AreEqual(48, stickyHeader.DetectedStickyPixels);
            Assert.AreEqual(64, stickyHeader.BestOverlapPixels);

            var table = report.Scenarios.Single(scenario => scenario.Name == "large-table-sticky-column");
            Assert.AreEqual(ScrollingCaptureAxis.Horizontal, table.Axis);
            Assert.AreEqual(56, table.DetectedStickyPixels);
            Assert.AreEqual(96, table.BestOverlapPixels);
            Assert.IsTrue(table.StitchedWidth > table.StitchedHeight);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static Bitmap CreateVerticalFrame(int startRow, int stickyHeight, int contentHeight, Color stickyColor)
    {
        var bitmap = new Bitmap(40, stickyHeight + contentHeight);
        using var graphics = Graphics.FromImage(bitmap);
        using var sticky = new SolidBrush(stickyColor);
        graphics.FillRectangle(sticky, 0, 0, bitmap.Width, stickyHeight);
        for (var y = 0; y < contentHeight; y++)
        {
            using var brush = new SolidBrush(RowColor(startRow + y));
            graphics.FillRectangle(brush, 0, stickyHeight + y, bitmap.Width, 1);
        }

        return bitmap;
    }

    private static Bitmap CreateHorizontalFrame(int startColumn, int stickyWidth, int contentWidth, Color stickyColor)
    {
        var bitmap = new Bitmap(stickyWidth + contentWidth, 36);
        using var graphics = Graphics.FromImage(bitmap);
        using var sticky = new SolidBrush(stickyColor);
        graphics.FillRectangle(sticky, 0, 0, stickyWidth, bitmap.Height);
        for (var x = 0; x < contentWidth; x++)
        {
            using var brush = new SolidBrush(ColumnColor(startColumn + x));
            graphics.FillRectangle(brush, stickyWidth + x, 0, 1, bitmap.Height);
        }

        return bitmap;
    }

    private static Color RowColor(int row)
    {
        return Color.FromArgb(255, row % 251, (row * 3) % 251, (row * 7) % 251);
    }

    private static Color ColumnColor(int column)
    {
        return Color.FromArgb(255, (column * 5) % 251, (column * 11) % 251, (column * 17) % 251);
    }
}

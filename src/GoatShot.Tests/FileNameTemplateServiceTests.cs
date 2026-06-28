using System.Drawing;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class FileNameTemplateServiceTests
{
    [TestMethod]
    public void Render_ReplacesKnownTokensAndSanitizesInvalidFileCharacters()
    {
        using var bitmap = new Bitmap(640, 360);
        using var captured = new CapturedBitmap(
            bitmap,
            CaptureKind.ActiveWindow,
            new CaptureBounds { X = 10, Y = 20, Width = 640, Height = 360 },
            new CaptureSource
            {
                ProcessName = "chrome",
                WindowTitle = "Support: Ticket/42",
                MonitorName = "DISPLAY1"
            });

        var rendered = FileNameTemplateService.Render(
            "{app}-{capture_type}-{width}x{height}-{counter}-{window_title}",
            captured,
            7);

        StringAssert.StartsWith(rendered, "chrome-activewindow-640x360-0007-Support-Ticket-42");
        Assert.IsFalse(rendered.Any(Path.GetInvalidFileNameChars().Contains));
    }

    [TestMethod]
    public void Render_PreservesUnknownTokensForFutureCompatibility()
    {
        using var bitmap = new Bitmap(1, 1);
        using var captured = new CapturedBitmap(
            bitmap,
            CaptureKind.Region,
            new CaptureBounds { Width = 1, Height = 1 });

        var rendered = FileNameTemplateService.Render("{future_token}-{counter}", captured, 3);

        Assert.AreEqual("{future_token}-0003", rendered);
    }
}

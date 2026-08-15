using System.Drawing;
using System.Globalization;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
// One test swaps CultureInfo.CurrentCulture; keep it off any future parallel lane.
[DoNotParallelize]
public sealed class FileNameTemplateServiceTests
{
    private static CapturedBitmap CreateCapture(string? windowTitle = null, string? processName = null)
    {
        var bitmap = new Bitmap(4, 4);
        return new CapturedBitmap(
            bitmap,
            CaptureKind.ActiveWindow,
            new CaptureBounds { Width = 4, Height = 4 },
            new CaptureSource
            {
                ProcessName = processName ?? "app",
                WindowTitle = windowTitle ?? "title",
                MonitorName = "DISPLAY1"
            });
    }

    [TestMethod]
    public void Render_UsesGregorianDatesRegardlessOfTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // th-TH defaults to the Buddhist calendar, where 2026 renders as 2569.
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            using var captured = CreateCapture();

            var rendered = FileNameTemplateService.Render("{date}", captured, 1);

            StringAssert.StartsWith(rendered, DateTimeOffset.Now.ToString("yyyy", CultureInfo.InvariantCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Render_CapsLengthSoLongWindowTitlesCannotOverflowThePathLimit()
    {
        using var captured = CreateCapture(windowTitle: new string('a', 500));

        var rendered = FileNameTemplateService.Render("{window_title}", captured, 1);

        Assert.IsTrue(
            rendered.Length <= FileNameTemplateService.MaxRenderedLength,
            $"Rendered name was {rendered.Length} characters.");
        Assert.IsTrue(rendered.Length > 0);
    }

    [TestMethod]
    [DataRow("CON")]
    [DataRow("nul")]
    [DataRow("Com1")]
    [DataRow("LPT9")]
    [DataRow("aux")]
    public void Render_EscapesWindowsReservedDeviceNames(string reserved)
    {
        using var captured = CreateCapture(windowTitle: reserved);

        var rendered = FileNameTemplateService.Render("{window_title}", captured, 1);

        Assert.AreNotEqual(
            reserved,
            rendered,
            StringComparer.OrdinalIgnoreCase.Equals(reserved, rendered)
                ? "A reserved device name would silently discard the capture."
                : string.Empty);
        Assert.IsFalse(
            FileNameTemplateService.IsReservedDeviceName(rendered),
            $"'{rendered}' still resolves to a Windows device.");
    }

    [TestMethod]
    public void Render_StillProducesANameWhenEveryTokenSanitizesAway()
    {
        using var captured = CreateCapture(windowTitle: "///");

        var rendered = FileNameTemplateService.Render("{window_title}", captured, 1);

        Assert.IsFalse(string.IsNullOrWhiteSpace(rendered));
        Assert.IsFalse(rendered.Any(Path.GetInvalidFileNameChars().Contains));
    }

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

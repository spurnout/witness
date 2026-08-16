using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureOverlayColorReaderTests
{
    [TestMethod]
    public void TryReadHex_ReadsBgraPixelsInRgbHexOrder()
    {
        // Two Bgra32 pixels: bytes are B,G,R,A — the hex string must come out R,G,B.
        var source = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0x30, 0x20, 0x10, 0xFF, 0xFF, 0x80, 0x00, 0xFF }, 8);

        Assert.AreEqual("#102030", CaptureOverlayColorReader.TryReadHex(source, 0, 0));
        Assert.AreEqual("#0080FF", CaptureOverlayColorReader.TryReadHex(source, 1, 0));
    }

    [TestMethod]
    public void TryReadHex_ConvertsOtherFormatsBeforeReading()
    {
        // The frozen screen bitmap is not guaranteed to be Bgra32. Bgr32 converts losslessly.
        var source = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgr32, null,
            new byte[] { 0x30, 0x20, 0x10, 0x00 }, 4);

        Assert.AreEqual("#102030", CaptureOverlayColorReader.TryReadHex(source, 0, 0));
    }

    [TestMethod]
    public void TryReadHex_ReturnsNullOutsideTheBitmap()
    {
        var source = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0x00, 0x00, 0x00, 0xFF }, 4);

        Assert.IsNull(CaptureOverlayColorReader.TryReadHex(source, 1, 0));
        Assert.IsNull(CaptureOverlayColorReader.TryReadHex(source, 0, 1));
        Assert.IsNull(CaptureOverlayColorReader.TryReadHex(source, -1, 0));
        Assert.IsNull(CaptureOverlayColorReader.TryReadHex(source, 0, -1));
    }
}

using System.Buffers.Binary;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CapturePipelineTests
{
    [TestMethod]
    public void CreateDib_WritesABottomUpOpaqueBitmapInfoHeader()
    {
        // Two rows of two BGRA pixels, top-down, with transparent alpha that must not survive.
        byte[] pixels =
        [
            1, 2, 3, 0, 4, 5, 6, 0,
            7, 8, 9, 0, 10, 11, 12, 0
        ];

        var dib = ClipboardImageData.CreateDib(pixels, width: 2, height: 2, stride: 8);

        Assert.AreEqual(40 + 16, dib.Length);
        Assert.AreEqual(40, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4)));
        Assert.AreEqual(2, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)), "Positive height means bottom-up rows.");
        Assert.AreEqual(32, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16)), "BI_RGB");
        Assert.AreEqual(16, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(20)));
        CollectionAssert.AreEqual(
            new byte[] { 7, 8, 9, 255, 10, 11, 12, 255, 1, 2, 3, 255, 4, 5, 6, 255 },
            dib.AsSpan(40).ToArray(),
            "The last source row comes first and every pixel is opaque.");
    }

    [TestMethod]
    public void CreateDib_IgnoresStridePaddingAndRejectsShortBuffers()
    {
        byte[] padded = [1, 2, 3, 4, 99, 99, 5, 6, 7, 8];

        var dib = ClipboardImageData.CreateDib(padded, width: 1, height: 2, stride: 6);

        CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 255, 1, 2, 3, 255 }, dib.AsSpan(40).ToArray());
        Assert.Throws<ArgumentException>(() => ClipboardImageData.CreateDib(new byte[7], width: 1, height: 2, stride: 4));
    }

    [TestMethod]
    public void ResolveFrozenCrop_TranslatesScreenBoundsIntoTheFrozenFrame()
    {
        // A secondary monitor to the left gives the virtual screen a negative origin.
        var virtualBounds = new CaptureBounds { X = -1920, Y = 0, Width = 3840, Height = 1080 };

        var crop = ScreenshotService.ResolveFrozenCrop(
            virtualBounds,
            new CaptureBounds { X = -100, Y = 20, Width = 300, Height = 200 });

        Assert.AreEqual(new System.Drawing.Rectangle(1820, 20, 300, 200), crop);
    }

    [TestMethod]
    public void ResolveFrozenCrop_RefusesSelectionsOutsideTheFrozenFrame()
    {
        var virtualBounds = new CaptureBounds { X = 0, Y = 0, Width = 1920, Height = 1080 };

        Assert.IsNull(ScreenshotService.ResolveFrozenCrop(virtualBounds, new CaptureBounds { X = 1800, Y = 0, Width = 200, Height = 100 }));
        Assert.IsNull(ScreenshotService.ResolveFrozenCrop(virtualBounds, new CaptureBounds { X = -1, Y = 0, Width = 10, Height = 10 }));
        Assert.IsNull(ScreenshotService.ResolveFrozenCrop(virtualBounds, new CaptureBounds { X = 0, Y = 0, Width = 0, Height = 10 }));
        Assert.IsNotNull(ScreenshotService.ResolveFrozenCrop(virtualBounds, new CaptureBounds { X = 0, Y = 0, Width = 1920, Height = 1080 }));
    }

    [TestMethod]
    public void Clone_DoesNotShareMutableStateWithTheOriginal()
    {
        var original = new CaptureItem
        {
            Bounds = new CaptureBounds { X = 1, Y = 2, Width = 3, Height = 4 },
            OcrWords = [new OcrRecognizedWord { Text = "kept" }]
        };

        var copy = original.Clone();
        copy.Bounds!.X = 99;
        copy.OcrWords.Add(new OcrRecognizedWord { Text = "extra" });
        copy.Notes = "changed";

        Assert.AreEqual(1, original.Bounds!.X);
        Assert.AreEqual(1, original.OcrWords.Count);
        Assert.IsNull(original.Notes);
        Assert.AreEqual(original.Id, copy.Id);
    }
}

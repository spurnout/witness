using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GoatShot.App.Services;

/// <summary>
/// Builds a packed CF_DIB payload straight from captured pixels. Handing the clipboard one
/// ready-made DIB is a single memory copy, where WPF's Clipboard.SetImage re-encodes the image
/// into several formats on the UI thread. Windows synthesizes CF_BITMAP and CF_DIBV5 on demand.
/// </summary>
public static class ClipboardImageData
{
    private const int BitmapInfoHeaderSize = 40;

    /// <summary>Copies a 32bpp capture into an opaque, bottom-up 32bpp BI_RGB DIB.</summary>
    public static byte[] CreateDib(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(data.Width * 4);
            var dib = CreateHeader(data.Width, data.Height);
            for (var row = 0; row < data.Height; row++)
            {
                var targetOffset = BitmapInfoHeaderSize + (data.Height - 1 - row) * rowBytes;
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), dib, targetOffset, rowBytes);
            }

            ForceOpaque(dib.AsSpan(BitmapInfoHeaderSize));
            return dib;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// Packs top-down BGRA rows into a bottom-up DIB. The reserved byte is forced to 0xFF because
    /// several paste targets read it as alpha, and a capture must never paste as transparent.
    /// </summary>
    public static byte[] CreateDib(ReadOnlySpan<byte> topDownBgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        if (Math.Abs(stride) < rowBytes || topDownBgra.Length < checked(Math.Abs(stride) * (height - 1) + rowBytes))
        {
            throw new ArgumentException("The pixel buffer is smaller than the stated dimensions.", nameof(topDownBgra));
        }

        var dib = CreateHeader(width, height);
        var pixels = dib.AsSpan(BitmapInfoHeaderSize);
        var absoluteStride = Math.Abs(stride);
        for (var row = 0; row < height; row++)
        {
            // A negative stride means the source is already stored bottom-up.
            var sourceRow = stride > 0 ? row : height - 1 - row;
            var source = topDownBgra.Slice(sourceRow * absoluteStride, rowBytes);
            var target = pixels.Slice((height - 1 - row) * rowBytes, rowBytes);
            source.CopyTo(target);
        }

        ForceOpaque(pixels);
        return dib;
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var imageBytes = checked(width * 4 * height);
        var dib = new byte[checked(BitmapInfoHeaderSize + imageBytes)];
        var header = dib.AsSpan(0, BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[0..], BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height); // positive: bottom-up rows
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1); // planes
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32); // bits per pixel
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 0); // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], imageBytes);
        return dib;
    }

    private static void ForceOpaque(Span<byte> pixels)
    {
        var words = MemoryMarshal.Cast<byte, uint>(pixels);
        for (var index = 0; index < words.Length; index++)
        {
            words[index] |= 0xFF000000u;
        }
    }
}

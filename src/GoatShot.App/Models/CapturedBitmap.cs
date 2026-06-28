using System.Drawing;

namespace GoatShot.App.Models;

public sealed class CapturedBitmap : IDisposable
{
    public CapturedBitmap(Bitmap bitmap, CaptureKind kind, CaptureBounds bounds, CaptureSource? source = null)
    {
        Bitmap = bitmap;
        Kind = kind;
        Bounds = bounds;
        Source = source;
    }

    public Bitmap Bitmap { get; }
    public CaptureKind Kind { get; }
    public CaptureBounds Bounds { get; }
    public CaptureSource? Source { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

using System.Drawing;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class ColorPickerService
{
    public PickedColor PickUnderCursor()
    {
        var point = Forms.Cursor.Position;
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(point.X, point.Y, 0, 0, new System.Drawing.Size(1, 1));
        var color = bitmap.GetPixel(0, 0);

        return new PickedColor(
            point.X,
            point.Y,
            color.R,
            color.G,
            color.B,
            $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }
}

public sealed record PickedColor(int X, int Y, byte R, byte G, byte B, string Hex)
{
    public string Display => $"{Hex}  rgb({R}, {G}, {B}) at {X}, {Y}";
}

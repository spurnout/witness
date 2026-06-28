namespace GoatShot.App.Models;

public sealed class CaptureBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public string Display => $"{Width} x {Height} at {X}, {Y}";
}

namespace GoatShot.App.Models;

public sealed class AnimationExportOptions
{
    public int? FrameRate { get; set; }
    public string GifTimingMode { get; set; } = "Smooth";
    public string Quality { get; set; } = "High";
    public string CompanionFormat { get; set; } = string.Empty;
    public int? MaxFrames { get; set; }
}

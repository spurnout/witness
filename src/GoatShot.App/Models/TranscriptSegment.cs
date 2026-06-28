namespace GoatShot.App.Models;

public sealed class TranscriptSegment
{
    public int Index { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = string.Empty;
}

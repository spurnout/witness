namespace GoatShot.App.Models;

public sealed class ScrollingCaptureOptions
{
    public string Profile { get; set; } = string.Empty;
    public int MaxFrames { get; set; } = 6;
    public int WheelClicksPerFrame { get; set; } = 5;
    public int SettleDelayMs { get; set; } = 240;
    public ScrollingCaptureAxis Axis { get; set; } = ScrollingCaptureAxis.Vertical;
    public int StickyPixels { get; set; }
    public bool AutoDetectStickyRegion { get; set; }
    public int MaximumAutoStickyPixels { get; set; } = 180;
    public int MinimumOverlapPixels { get; set; } = 32;
    public int MaximumOverlapPixels { get; set; } = 320;

    public ScrollingCaptureOptions Normalize()
    {
        return new ScrollingCaptureOptions
        {
            Profile = string.IsNullOrWhiteSpace(Profile) ? string.Empty : Profile.Trim(),
            MaxFrames = Math.Clamp(MaxFrames, 1, 24),
            WheelClicksPerFrame = Math.Clamp(WheelClicksPerFrame, 1, 24),
            SettleDelayMs = Math.Clamp(SettleDelayMs, 80, 2_000),
            Axis = Axis,
            StickyPixels = Math.Clamp(StickyPixels, 0, 1_000),
            AutoDetectStickyRegion = AutoDetectStickyRegion,
            MaximumAutoStickyPixels = Math.Clamp(MaximumAutoStickyPixels, 0, 1_000),
            MinimumOverlapPixels = Math.Clamp(MinimumOverlapPixels, 1, 1_000),
            MaximumOverlapPixels = Math.Clamp(MaximumOverlapPixels, 8, 2_000)
        };
    }
}

public static class ScrollingCaptureProfiles
{
    public static ScrollingCaptureOptions For(string? profile)
    {
        var normalized = NormalizeProfile(profile);
        return normalized switch
        {
            "browser" or "chromium" or "web" or "page" => new ScrollingCaptureOptions
            {
                Profile = "browser",
                Axis = ScrollingCaptureAxis.Vertical,
                MaxFrames = 12,
                WheelClicksPerFrame = 6,
                SettleDelayMs = 320,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 180,
                MinimumOverlapPixels = 32,
                MaximumOverlapPixels = 520
            },
            "table" or "largetable" or "grid" or "datagrid" or "datasheet" or "spreadsheet" => new ScrollingCaptureOptions
            {
                Profile = "table",
                Axis = ScrollingCaptureAxis.Horizontal,
                MaxFrames = 14,
                WheelClicksPerFrame = 3,
                SettleDelayMs = 360,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 220,
                MinimumOverlapPixels = 24,
                MaximumOverlapPixels = 720
            },
            "document" or "doc" or "pdf" => new ScrollingCaptureOptions
            {
                Profile = "document",
                Axis = ScrollingCaptureAxis.Vertical,
                MaxFrames = 10,
                WheelClicksPerFrame = 5,
                SettleDelayMs = 280,
                AutoDetectStickyRegion = true,
                MaximumAutoStickyPixels = 140,
                MinimumOverlapPixels = 32,
                MaximumOverlapPixels = 480
            },
            _ => new ScrollingCaptureOptions
            {
                Profile = string.Empty
            }
        };
    }

    public static string NormalizeProfile(string? profile)
    {
        return new string((profile ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}

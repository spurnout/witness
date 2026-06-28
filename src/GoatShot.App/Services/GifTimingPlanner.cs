using System.Globalization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record GifTimingPlan(
    int RequestedFrameRate,
    int CaptureFrameRate,
    int EffectiveGifFrameRate,
    IReadOnlyList<int> FrameDelaysMs,
    int MaxFrames,
    bool CompanionRecommended,
    string CompanionFormat,
    string TimingMode,
    string Message)
{
    public int DelayForFrame(int frameIndex)
    {
        if (FrameDelaysMs.Count == 0)
        {
            return 100;
        }

        return FrameDelaysMs[Math.Abs(frameIndex) % FrameDelaysMs.Count];
    }
}

public static class GifTimingPlanner
{
    public const int MinimumFrameRate = 1;
    public const int MaximumSourceFrameRate = 120;
    public const int MaximumGifFrameRate = 100;
    public const int DefaultMaxFrames = 1_800;
    public const int AbsoluteMaxFrames = 6_000;

    public static GifTimingPlan Plan(RecordingSettings? settings, AnimationExportOptions? options = null)
    {
        settings ??= new RecordingSettings();
        options ??= new AnimationExportOptions();

        var requested = Math.Clamp(
            options.FrameRate ?? settings.FramesPerSecond,
            MinimumFrameRate,
            MaximumSourceFrameRate);
        var mode = NormalizeTimingMode(options.GifTimingMode);
        var maxFrames = Math.Clamp(
            options.MaxFrames ?? DefaultMaxFrames,
            1,
            AbsoluteMaxFrames);
        var companionFormat = NormalizeCompanionFormat(options.CompanionFormat);
        var companionRecommended = requested > MaximumGifFrameRate;
        var effectiveGifFrameRate = Math.Min(requested, MaximumGifFrameRate);
        var delays = BuildDelayPattern(effectiveGifFrameRate, mode);
        var message = BuildMessage(requested, effectiveGifFrameRate, mode, companionRecommended, companionFormat);

        return new GifTimingPlan(
            requested,
            requested,
            effectiveGifFrameRate,
            delays,
            maxFrames,
            companionRecommended,
            companionFormat,
            mode,
            message);
    }

    public static IReadOnlyList<int> BuildDelayPattern(int frameRate, string? timingMode = null)
    {
        frameRate = Math.Clamp(frameRate, MinimumFrameRate, MaximumGifFrameRate);
        var mode = NormalizeTimingMode(timingMode);
        if (mode.Equals("MaxSpeed", StringComparison.OrdinalIgnoreCase) || frameRate >= MaximumGifFrameRate)
        {
            return [10];
        }

        if (frameRate == 60)
        {
            return [20, 20, 10];
        }

        var patternLength = frameRate;
        var delays = new List<int>(patternLength);
        var accumulatedHundredths = 0;
        for (var index = 1; index <= patternLength; index++)
        {
            var targetHundredths = (int)Math.Round(index * 100d / frameRate, MidpointRounding.AwayFromZero);
            var delayHundredths = Math.Clamp(targetHundredths - accumulatedHundredths, 1, 100);
            accumulatedHundredths += delayHundredths;
            delays.Add(delayHundredths * 10);
        }

        return delays;
    }

    public static string NormalizeTimingMode(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "max" or "maxspeed" or "fast" or "fastest" => "MaxSpeed",
            "compat" or "compatible" or "compatibility" => "Compatible",
            _ => "Smooth"
        };
    }

    public static string NormalizeCompanionFormat(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "mp4" => "mp4",
            "webm" => "webm",
            _ => string.Empty
        };
    }

    private static string BuildMessage(
        int requested,
        int effectiveGif,
        string mode,
        bool companionRecommended,
        string companionFormat)
    {
        var baseMessage = requested == effectiveGif
            ? $"GIF timing planned for {effectiveGif.ToString(CultureInfo.InvariantCulture)} fps using {mode} timing."
            : $"GIF timing capped at {effectiveGif.ToString(CultureInfo.InvariantCulture)} fps because GIF delays use 10 ms increments; source capture remains {requested.ToString(CultureInfo.InvariantCulture)} fps.";

        if (!companionRecommended)
        {
            return baseMessage;
        }

        var companion = string.IsNullOrWhiteSpace(companionFormat) ? "MP4/WebM" : companionFormat.ToUpperInvariant();
        return $"{baseMessage} Keep a {companion} companion for true high-frame-rate playback.";
    }
}

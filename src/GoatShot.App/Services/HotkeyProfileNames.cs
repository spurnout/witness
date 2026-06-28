using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class HotkeyProfileNames
{
    public static string ForAction(HotkeyAction action)
    {
        return action.ToString();
    }

    public static string Normalize(string? profile)
    {
        return string.IsNullOrWhiteSpace(profile)
            ? string.Empty
            : profile.Trim();
    }
}

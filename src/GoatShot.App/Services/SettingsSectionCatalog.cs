namespace GoatShot.App.Services;

public sealed record SettingsSection(string Key, string Label, string Description);

public static class SettingsSectionCatalog
{
    public static IReadOnlyList<SettingsSection> All { get; } =
    [
        new("General", "General", "Capture defaults, filenames, OCR, startup, and privacy mode."),
        new("Recording", "Recording", "MP4 profiles, audio, camera, countdown, and overlays."),
        new("Sharing", "Sharing Providers", "Local export, queue behavior, providers, webhooks, and saved credentials."),
        new("Automation", "Automation", "Watch folders, rule defaults, and workflow profile import/export."),
        new("Plugins", "Plugins", "Local plugin trust posture, staged updates, and passive registry checks."),
        new("Ai", "AI", "Gemini endpoint, key status, model defaults, and AI denylist.")
    ];

    public static SettingsSection? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return All.FirstOrDefault(section => section.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}

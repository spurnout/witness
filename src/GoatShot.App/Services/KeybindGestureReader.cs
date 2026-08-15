using System.Windows.Input;

namespace GoatShot.App.Services;

/// <summary>
/// Translates a live key press into a gesture string the keybind catalog can store and register.
/// Deliberately narrow: it only emits chords that <see cref="HotkeyGesture"/> can parse back, so the
/// settings UI can never record something the hotkey service would later reject.
/// </summary>
public static class KeybindGestureReader
{
    /// <summary>
    /// Returns the canonical gesture for a chord, or null when the chord cannot be a global shortcut.
    /// A bare letter or digit is refused because registering it would swallow that key system-wide.
    /// </summary>
    public static string? TryBuild(Key key, ModifierKeys modifiers)
    {
        // RegisterHotKey supports MOD_WIN, but the gesture grammar has no spelling for it. Refuse the
        // chord rather than silently dropping the modifier and binding a different shortcut.
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            return null;
        }

        if (Describe(key) is not { } keyName)
        {
            return null;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (parts.Count == 0 && !CanStandAlone(key))
        {
            return null;
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>Human-readable name for a key the catalog supports, or null when it supports none.</summary>
    public static string? Describe(Key key) => key switch
    {
        Key.Snapshot => "PrintScreen",
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
        _ => null
    };

    /// <summary>PrintScreen and function keys are not typing keys, so they are safe unmodified.</summary>
    private static bool CanStandAlone(Key key) => key == Key.Snapshot || key is >= Key.F1 and <= Key.F24;
}

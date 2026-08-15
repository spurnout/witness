namespace GoatShot.App.Services;

/// <summary>
/// Single source of truth for translating between the text form of a global hotkey
/// ("Ctrl+Shift+R") and the modifier/virtual-key pair that RegisterHotKey expects.
/// Both <see cref="HotkeyService"/> and <see cref="KeybindCatalog"/> route through here so a
/// gesture that parses is always a gesture that can be rendered back identically.
/// </summary>
internal static class HotkeyGesture
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint VkSnapshot = 0x2C;

    private const uint VkDigitZero = 0x30;
    private const uint VkDigitNine = 0x39;
    private const uint VkLetterA = 0x41;
    private const uint VkLetterZ = 0x5A;
    private const uint VkF1 = 0x70;
    private const int MaxFunctionKey = 24;

    public static bool TryParse(string? gesture, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = Split(gesture);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModControl;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
            }
            else if (key == 0 && TryParseVirtualKey(part, out key))
            {
            }
            else
            {
                return false;
            }
        }

        return key != 0;
    }

    /// <summary>Renders a chord in canonical order: Ctrl, then Alt, then Shift, then the key.</summary>
    public static string Format(uint modifiers, uint key, string separator = "+")
    {
        var parts = new List<string>(4);
        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        var keyName = DescribeKey(key);
        if (keyName is not null)
        {
            parts.Add(keyName);
        }

        return string.Join(separator, parts);
    }

    public static string[] Split(string? gesture) =>
        (gesture ?? string.Empty).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryParseVirtualKey(string value, out uint key)
    {
        key = 0;
        if (value.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Print Screen", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Snapshot", StringComparison.OrdinalIgnoreCase))
        {
            key = VkSnapshot;
            return true;
        }

        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
        {
            key = char.ToUpperInvariant(value[0]);
            return true;
        }

        if (value.Length is 2 or 3 && value[0] is 'F' or 'f' &&
            int.TryParse(value[1..], out var functionKey) && functionKey is >= 1 and <= MaxFunctionKey)
        {
            key = VkF1 + (uint)functionKey - 1;
            return true;
        }

        return false;
    }

    private static string? DescribeKey(uint key)
    {
        if (key == VkSnapshot)
        {
            return "PrintScreen";
        }

        if (key is >= VkDigitZero and <= VkDigitNine || key is >= VkLetterA and <= VkLetterZ)
        {
            return ((char)key).ToString();
        }

        if (key is >= VkF1 and <= VkF1 + MaxFunctionKey - 1)
        {
            return $"F{key - VkF1 + 1}";
        }

        return null;
    }
}

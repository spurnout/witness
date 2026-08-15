using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record KeybindDefinition(
    HotkeyAction Action,
    string Group,
    string Label,
    string Description,
    string DefaultGesture);

/// <summary>A catalog definition merged with whatever the user stored for it.</summary>
public sealed record ResolvedKeybind(
    HotkeyAction Action,
    string Group,
    string Label,
    string Description,
    string DefaultGesture,
    string Gesture,
    bool IsValid)
{
    /// <summary>False when the user deliberately cleared the gesture.</summary>
    public bool IsBound => Gesture.Length > 0;

    public bool IsDefault => IsBound && string.Equals(Gesture, DefaultGesture, StringComparison.OrdinalIgnoreCase);

    public string DisplayGesture => IsBound ? KeybindCatalog.ToDisplay(Gesture) : "Not set";

    public string DisplayDefaultGesture => KeybindCatalog.ToDisplay(DefaultGesture);
}

public sealed record KeybindConflict(string DisplayGesture, IReadOnlyList<HotkeyAction> Actions);

public sealed record KeybindRegistration(
    int Id,
    HotkeyAction Action,
    uint Modifiers,
    uint Key,
    string Label,
    string DisplayGesture);

/// <summary>
/// Describes every rebindable global hotkey, merges user overrides over the shipped defaults,
/// and reports chord collisions. Registration ids are pinned per action so unbinding one hotkey
/// never renumbers the others.
/// </summary>
public static class KeybindCatalog
{
    public const string CaptureGroup = "Capture";
    public const string RecordingGroup = "Recording and replay";
    public const string ToolsGroup = "Measure and inspect";

    public static IReadOnlyList<KeybindDefinition> Definitions { get; } =
    [
        new(HotkeyAction.AllInOneCapture, CaptureGroup, "Instant region capture",
            "Starts a region selection on a modifier-free key. Runs the same capture as Region capture.",
            "PrintScreen"),
        new(HotkeyAction.RegionCapture, CaptureGroup, "Region capture",
            "Drag a rectangle and capture it immediately.", "Ctrl+PrintScreen"),
        new(HotkeyAction.WindowCapture, CaptureGroup, "Window capture",
            "Captures the window under the cursor.", "Alt+PrintScreen"),
        new(HotkeyAction.LastRegionCapture, CaptureGroup, "Repeat last region",
            "Re-captures the previous region without reselecting it.", "Shift+PrintScreen"),
        new(HotkeyAction.ToggleRecording, RecordingGroup, "Start or stop recording",
            "Toggles screen recording with the active recording profile.", "Ctrl+Shift+R"),
        new(HotkeyAction.ToggleReplay, RecordingGroup, "Arm or pause replay buffer",
            "Starts or pauses the rolling replay buffer.", "Ctrl+Alt+Shift+R"),
        new(HotkeyAction.SaveReplay, RecordingGroup, "Save replay buffer",
            "Writes the configured preceding interval to a file.", "Ctrl+Shift+PrintScreen"),
        new(HotkeyAction.OcrRegion, ToolsGroup, "OCR a region",
            "Selects a region and copies the recognized text.", "Ctrl+Shift+O"),
        new(HotkeyAction.ColorPicker, ToolsGroup, "Color picker",
            "Samples the pixel colour under the cursor.", "Ctrl+Shift+C"),
        new(HotkeyAction.PixelRuler, ToolsGroup, "Pixel ruler",
            "Measures distances on screen in pixels.", "Ctrl+Shift+U")
    ];

    /// <summary>
    /// Pinned RegisterHotKey ids. Values are part of the runtime contract with the message loop,
    /// so add new actions with new numbers rather than reordering these.
    /// </summary>
    private static readonly IReadOnlyDictionary<HotkeyAction, int> RegistrationIds =
        new Dictionary<HotkeyAction, int>
        {
            [HotkeyAction.AllInOneCapture] = 100,
            [HotkeyAction.RegionCapture] = 101,
            [HotkeyAction.WindowCapture] = 102,
            [HotkeyAction.LastRegionCapture] = 103,
            [HotkeyAction.ToggleRecording] = 104,
            [HotkeyAction.OcrRegion] = 105,
            [HotkeyAction.ColorPicker] = 106,
            [HotkeyAction.PixelRuler] = 107,
            [HotkeyAction.SaveReplay] = 108,
            [HotkeyAction.ToggleReplay] = 109
        };

    public static IReadOnlyList<string> Groups { get; } =
        Definitions.Select(definition => definition.Group).Distinct(StringComparer.Ordinal).ToArray();

    public static string DefaultGesture(HotkeyAction action) =>
        Definitions.FirstOrDefault(definition => definition.Action == action)?.DefaultGesture ?? string.Empty;

    public static bool IsValidGesture(string? gesture) => HotkeyGesture.TryParse(gesture, out _, out _);

    /// <summary>Spaced form used everywhere a gesture is shown or announced ("Ctrl + Shift + R").</summary>
    public static string ToDisplay(string? gesture) => string.Join(" + ", HotkeyGesture.Split(gesture));

    /// <summary>
    /// Rewrites a gesture into canonical storage form ("Ctrl+Shift+R"). Unparsable input is
    /// returned trimmed rather than discarded so the settings UI can show the user what it rejected.
    /// </summary>
    public static string NormalizeGesture(string? gesture)
    {
        var trimmed = (gesture ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return HotkeyGesture.TryParse(trimmed, out var modifiers, out var key)
            ? HotkeyGesture.Format(modifiers, key)
            : trimmed;
    }

    public static IReadOnlyList<ResolvedKeybind> Resolve(IEnumerable<KeybindAssignment>? assignments)
    {
        var overrides = new Dictionary<HotkeyAction, string>();
        foreach (var assignment in assignments ?? [])
        {
            if (assignment is null || overrides.ContainsKey(assignment.Action))
            {
                continue;
            }

            overrides[assignment.Action] = NormalizeGesture(assignment.Gesture);
        }

        return Definitions
            .Select(definition =>
            {
                var gesture = overrides.TryGetValue(definition.Action, out var stored)
                    ? stored
                    : NormalizeGesture(definition.DefaultGesture);
                return new ResolvedKeybind(
                    definition.Action,
                    definition.Group,
                    definition.Label,
                    definition.Description,
                    NormalizeGesture(definition.DefaultGesture),
                    gesture,
                    gesture.Length == 0 || IsValidGesture(gesture));
            })
            .ToArray();
    }

    public static IReadOnlyList<KeybindAssignment> ToAssignments(IEnumerable<ResolvedKeybind> keybinds) =>
        keybinds
            .Select(keybind => new KeybindAssignment
            {
                Action = keybind.Action,
                Gesture = keybind.Gesture
            })
            .ToArray();

    public static IReadOnlyList<KeybindConflict> FindConflicts(IEnumerable<ResolvedKeybind> keybinds) =>
        keybinds
            .Where(keybind => keybind.IsBound && keybind.IsValid)
            .Select(keybind => (Keybind: keybind, Chord: ParseChord(keybind.Gesture)))
            .Where(entry => entry.Chord is not null)
            .GroupBy(entry => entry.Chord!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => new KeybindConflict(
                HotkeyGesture.Format(group.Key.Modifiers, group.Key.Key, " + "),
                group.Select(entry => entry.Keybind.Action).ToArray()))
            .ToArray();

    public static IReadOnlySet<HotkeyAction> ConflictingActions(IEnumerable<ResolvedKeybind> keybinds) =>
        FindConflicts(keybinds)
            .SelectMany(conflict => conflict.Actions)
            .ToHashSet();

    /// <summary>
    /// Produces the RegisterHotKey work list. Unbound and unparsable entries are dropped, and a
    /// chord claimed by two actions is registered once for the first action in catalog order so the
    /// OS-level registration matches what the settings UI flags as the winner.
    /// </summary>
    public static IReadOnlyList<KeybindRegistration> BuildRegistrationPlan(IEnumerable<ResolvedKeybind> keybinds)
    {
        var claimed = new HashSet<(uint Modifiers, uint Key)>();
        var plan = new List<KeybindRegistration>();
        foreach (var keybind in keybinds)
        {
            if (!keybind.IsBound || !keybind.IsValid ||
                ParseChord(keybind.Gesture) is not { } chord ||
                !RegistrationIds.TryGetValue(keybind.Action, out var id) ||
                !claimed.Add(chord))
            {
                continue;
            }

            plan.Add(new KeybindRegistration(
                id,
                keybind.Action,
                chord.Modifiers,
                chord.Key,
                keybind.Label,
                keybind.DisplayGesture));
        }

        return plan;
    }

    /// <summary>
    /// Mirrors the resolved replay gestures back onto <see cref="ReplayBufferSettings"/>. Those two
    /// properties predate this catalog and remain in the persisted file; keeping them in step stops a
    /// downgrade or an external reader from seeing a stale chord. Returns true when anything changed.
    /// </summary>
    public static bool SyncReplayHotkeyMirror(AppSettings settings)
    {
        if (settings.Replay is null)
        {
            return false;
        }

        var resolved = Resolve(settings.Keybinds);
        var changed = false;

        var toggle = GestureFor(resolved, HotkeyAction.ToggleReplay);
        if (toggle.Length > 0 && !string.Equals(settings.Replay.ToggleHotkey, toggle, StringComparison.Ordinal))
        {
            settings.Replay.ToggleHotkey = toggle;
            changed = true;
        }

        var save = GestureFor(resolved, HotkeyAction.SaveReplay);
        if (save.Length > 0 && !string.Equals(settings.Replay.SaveHotkey, save, StringComparison.Ordinal))
        {
            settings.Replay.SaveHotkey = save;
            changed = true;
        }

        return changed;
    }

    private static string GestureFor(IReadOnlyList<ResolvedKeybind> resolved, HotkeyAction action) =>
        resolved.FirstOrDefault(keybind => keybind.Action == action)?.Gesture ?? string.Empty;

    private static (uint Modifiers, uint Key)? ParseChord(string gesture) =>
        HotkeyGesture.TryParse(gesture, out var modifiers, out var key)
            ? (modifiers, key)
            : null;
}

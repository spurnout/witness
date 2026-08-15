using System.Text.Json.Serialization;

namespace GoatShot.App.Models;

/// <summary>
/// A user override for one global hotkey. Actions absent from the stored list fall back to the
/// catalog default; an entry with an empty <see cref="Gesture"/> is an explicit "unbound".
/// </summary>
public sealed class KeybindAssignment
{
    /// <summary>
    /// Persisted by name rather than by ordinal so settings.json stays readable and a future
    /// reordering of <see cref="HotkeyAction"/> cannot silently repoint someone's shortcuts.
    /// The converter still accepts numeric values, so older files keep loading.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<HotkeyAction>))]
    public HotkeyAction Action { get; set; }

    public string Gesture { get; set; } = string.Empty;
}

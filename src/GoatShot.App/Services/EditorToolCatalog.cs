using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record EditorToolShortcut(
    AnnotationMode Mode,
    string Label,
    string Shortcut,
    bool IsPrivacyTool);

public static class EditorToolCatalog
{
    private static readonly IReadOnlyList<EditorToolShortcut> ToolShortcuts =
    [
        new(AnnotationMode.Rectangle, "Rectangle", "R", false),
        new(AnnotationMode.Ellipse, "Ellipse", "E", false),
        new(AnnotationMode.Line, "Line", "L", false),
        new(AnnotationMode.Arrow, "Arrow", "A", false),
        new(AnnotationMode.Freehand, "Freehand", "F", false),
        new(AnnotationMode.Text, "Text", "T", false),
        new(AnnotationMode.Callout, "Callout", "C", false),
        new(AnnotationMode.Step, "Step", "N", false),
        new(AnnotationMode.Highlight, "Highlight", "H", false),
        new(AnnotationMode.Spotlight, "Spotlight", "S", false),
        new(AnnotationMode.Crop, "Crop", "X", false),
        new(AnnotationMode.Redact, "Redact", "D", true),
        new(AnnotationMode.Blur, "Blur", "B", true),
        new(AnnotationMode.Pixelate, "Pixelate", "P", true)
    ];

    public static IReadOnlyList<EditorToolShortcut> Shortcuts => ToolShortcuts;

    public static bool IsPrivacyTool(AnnotationMode mode)
    {
        return ToolShortcuts.Any(tool => tool.Mode == mode && tool.IsPrivacyTool);
    }

    public static string LabelFor(AnnotationMode mode)
    {
        return ToolShortcuts.FirstOrDefault(tool => tool.Mode == mode)?.Label ?? mode.ToString();
    }

    public static bool TryGetModeForShortcut(string? shortcut, out AnnotationMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var match = ToolShortcuts.FirstOrDefault(tool =>
            tool.Shortcut.Equals(shortcut.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        mode = match.Mode;
        return true;
    }
}

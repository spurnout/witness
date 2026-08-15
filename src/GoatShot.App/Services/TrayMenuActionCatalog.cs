using GoatShot.App.Models;

namespace GoatShot.App.Services;

public enum TrayMenuActionKind
{
    CaptureRegion,
    CaptureWindow,
    CaptureScrollingWindow,
    CaptureHorizontalScrollingWindow,
    CaptureFullscreen,
    CaptureAllMonitors,
    CaptureActiveMonitor,
    CaptureFixedRegion1280x720,
    ToggleRecording,
    ToggleRecordingPause,
    RecordShortMp4,
    ToggleReplay,
    SaveReplay,
    ToggleStepRecorder,
    ImportClipboard,
    PickColor,
    OpenPixelRuler,
    OpenWorkspace,
    OpenSettings,
    Exit
}

public sealed record TrayMenuActionDefinition(
    string Label,
    TrayMenuActionKind? ActionKind,
    string Group,
    bool IsSeparator)
{
    public static TrayMenuActionDefinition Action(string label, TrayMenuActionKind actionKind, string group)
    {
        return new TrayMenuActionDefinition(label, actionKind, group, IsSeparator: false);
    }

    public static TrayMenuActionDefinition Separator()
    {
        return new TrayMenuActionDefinition(string.Empty, null, string.Empty, IsSeparator: true);
    }
}

public static class TrayMenuActionCatalog
{
    public static IReadOnlyList<TrayMenuActionDefinition> All { get; } =
    [
        TrayMenuActionDefinition.Action("Capture region", TrayMenuActionKind.CaptureRegion, "Capture"),
        TrayMenuActionDefinition.Action("Capture window", TrayMenuActionKind.CaptureWindow, "Capture"),
        TrayMenuActionDefinition.Action("Capture scrolling window", TrayMenuActionKind.CaptureScrollingWindow, "Capture"),
        TrayMenuActionDefinition.Action("Capture horizontal scrolling window", TrayMenuActionKind.CaptureHorizontalScrollingWindow, "Capture"),
        TrayMenuActionDefinition.Action("Capture fullscreen", TrayMenuActionKind.CaptureFullscreen, "Capture"),
        TrayMenuActionDefinition.Action("Capture all monitors", TrayMenuActionKind.CaptureAllMonitors, "Capture"),
        TrayMenuActionDefinition.Action("Capture active monitor", TrayMenuActionKind.CaptureActiveMonitor, "Capture"),
        TrayMenuActionDefinition.Action("Capture 1280 x 720 at cursor", TrayMenuActionKind.CaptureFixedRegion1280x720, "Capture"),
        TrayMenuActionDefinition.Separator(),
        TrayMenuActionDefinition.Action("Start recording", TrayMenuActionKind.ToggleRecording, "Record"),
        TrayMenuActionDefinition.Action("Pause / resume recording", TrayMenuActionKind.ToggleRecordingPause, "Record"),
        TrayMenuActionDefinition.Action("Record 5s MP4", TrayMenuActionKind.RecordShortMp4, "Record"),
        TrayMenuActionDefinition.Action("Arm Replay", TrayMenuActionKind.ToggleReplay, "Record"),
        TrayMenuActionDefinition.Action("Save replay", TrayMenuActionKind.SaveReplay, "Record"),
        TrayMenuActionDefinition.Action("Start / stop step recorder", TrayMenuActionKind.ToggleStepRecorder, "Record"),
        TrayMenuActionDefinition.Separator(),
        TrayMenuActionDefinition.Action("Import clipboard", TrayMenuActionKind.ImportClipboard, "Tools"),
        TrayMenuActionDefinition.Action("Color picker", TrayMenuActionKind.PickColor, "Tools"),
        TrayMenuActionDefinition.Action("Pixel ruler", TrayMenuActionKind.OpenPixelRuler, "Tools"),
        TrayMenuActionDefinition.Separator(),
        TrayMenuActionDefinition.Action("Open workspace", TrayMenuActionKind.OpenWorkspace, "Workspace"),
        TrayMenuActionDefinition.Action("Settings", TrayMenuActionKind.OpenSettings, "Workspace"),
        TrayMenuActionDefinition.Separator(),
        TrayMenuActionDefinition.Action("Exit", TrayMenuActionKind.Exit, "System")
    ];

    public static IEnumerable<TrayMenuActionDefinition> Actions => All.Where(item => !item.IsSeparator);

    /// <summary>
    /// Tray entries that are also driven by a global hotkey. The tray menu is exactly where someone
    /// looks when they have not memorised the shortcut, so these are labelled with the live gesture.
    /// </summary>
    private static readonly IReadOnlyDictionary<TrayMenuActionKind, HotkeyAction> HotkeyByAction =
        new Dictionary<TrayMenuActionKind, HotkeyAction>
        {
            [TrayMenuActionKind.CaptureRegion] = HotkeyAction.RegionCapture,
            [TrayMenuActionKind.CaptureWindow] = HotkeyAction.WindowCapture,
            [TrayMenuActionKind.ToggleRecording] = HotkeyAction.ToggleRecording,
            [TrayMenuActionKind.ToggleReplay] = HotkeyAction.ToggleReplay,
            [TrayMenuActionKind.SaveReplay] = HotkeyAction.SaveReplay,
            [TrayMenuActionKind.PickColor] = HotkeyAction.ColorPicker,
            [TrayMenuActionKind.OpenPixelRuler] = HotkeyAction.PixelRuler
        };

    public static HotkeyAction? HotkeyFor(TrayMenuActionKind actionKind) =>
        HotkeyByAction.TryGetValue(actionKind, out var action) ? action : null;
}

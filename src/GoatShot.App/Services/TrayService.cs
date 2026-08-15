using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Dictionary<TrayMenuActionKind, Forms.ToolStripItem> _actionItems = new();
    private readonly HotkeyService? _hotkeys;

    public TrayService(MainWindow window, HotkeyService? hotkeys = null)
    {
        var menu = new Forms.ContextMenuStrip();
        foreach (var definition in TrayMenuActionCatalog.All)
        {
            if (definition.IsSeparator)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                continue;
            }

            var actionKind = definition.ActionKind ?? throw new InvalidOperationException("Tray action is missing an action kind.");
            var item = menu.Items.Add(definition.Label, null, (_, _) => Dispatch(window, actionKind));
            _actionItems[actionKind] = item;
        }

        _hotkeys = hotkeys;
        if (_hotkeys is not null)
        {
            RefreshShortcutLabels();
            _hotkeys.RegistrationsChanged += Hotkeys_RegistrationsChanged;
        }

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = BrandIdentity.ProductName,
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => window.Dispatcher.Invoke(window.ShowWorkspaceCommand);
    }

    private void Hotkeys_RegistrationsChanged(object? sender, EventArgs e) => RefreshShortcutLabels();

    /// <summary>
    /// Writes each action's current gesture into the menu's shortcut column. ShortcutKeyDisplayString
    /// only labels the item, so the tray never competes with the real global registration.
    /// </summary>
    private void RefreshShortcutLabels()
    {
        if (_hotkeys is null)
        {
            return;
        }

        foreach (var (actionKind, item) in _actionItems)
        {
            if (item is not Forms.ToolStripMenuItem menuItem ||
                TrayMenuActionCatalog.HotkeyFor(actionKind) is not { } hotkey)
            {
                continue;
            }

            var gesture = _hotkeys.DisplayGesture(hotkey);
            menuItem.ShortcutKeyDisplayString = gesture.Length == 0 ? null : gesture;
            menuItem.ShowShortcutKeys = gesture.Length > 0;
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private static void Dispatch(MainWindow window, TrayMenuActionKind actionKind)
    {
        window.Dispatcher.Invoke(() =>
        {
            switch (actionKind)
            {
                case TrayMenuActionKind.CaptureRegion:
                    window.CaptureRegionCommand();
                    break;
                case TrayMenuActionKind.CaptureWindow:
                    window.CaptureWindowCommand();
                    break;
                case TrayMenuActionKind.CaptureScrollingWindow:
                    window.CaptureScrollingWindowCommand();
                    break;
                case TrayMenuActionKind.CaptureHorizontalScrollingWindow:
                    window.CaptureHorizontalScrollingWindowCommand();
                    break;
                case TrayMenuActionKind.CaptureFullscreen:
                    window.CaptureFullscreenCommand();
                    break;
                case TrayMenuActionKind.CaptureAllMonitors:
                    window.CaptureAllMonitorsCommand();
                    break;
                case TrayMenuActionKind.CaptureActiveMonitor:
                    window.CaptureMonitorCommand();
                    break;
                case TrayMenuActionKind.CaptureFixedRegion1280x720:
                    window.CaptureFixedRegionCommand(1280, 720);
                    break;
                case TrayMenuActionKind.ToggleRecording:
                    window.ToggleRecordingCommand();
                    break;
                case TrayMenuActionKind.ToggleRecordingPause:
                    window.ToggleRecordingPauseCommand();
                    break;
                case TrayMenuActionKind.RecordShortMp4:
                    window.RecordShortMp4Command();
                    break;
                case TrayMenuActionKind.ToggleReplay:
                    window.ToggleReplayCommand();
                    break;
                case TrayMenuActionKind.SaveReplay:
                    window.SaveReplayCommand();
                    break;
                case TrayMenuActionKind.ToggleStepRecorder:
                    window.ToggleStepRecorderCommand();
                    break;
                case TrayMenuActionKind.ImportClipboard:
                    window.ImportClipboardCommand();
                    break;
                case TrayMenuActionKind.PickColor:
                    window.PickColorCommand();
                    break;
                case TrayMenuActionKind.OpenPixelRuler:
                    window.OpenPixelRulerCommand();
                    break;
                case TrayMenuActionKind.OpenWorkspace:
                    window.ShowWorkspaceCommand();
                    break;
                case TrayMenuActionKind.OpenSettings:
                    window.OpenSettingsCommand();
                    break;
                case TrayMenuActionKind.Exit:
                    window.ExitCommand();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Unknown tray action.");
            }
        });
    }

    public void UpdateReplayStatus(GoatShot.App.Models.ReplayBufferStatus status)
    {
        if (_actionItems.TryGetValue(TrayMenuActionKind.ToggleReplay, out var toggle))
        {
            var isSystemSuspended = status.SystemSuspended &&
                status.State is GoatShot.App.Models.ReplayBufferState.Armed or
                    GoatShot.App.Models.ReplayBufferState.Saving;
            toggle.Text = isSystemSuspended
                ? "Replay suspended by Windows"
                : status.State switch
            {
                GoatShot.App.Models.ReplayBufferState.Armed => "Pause Replay",
                GoatShot.App.Models.ReplayBufferState.Paused => "Resume Replay",
                GoatShot.App.Models.ReplayBufferState.Saving => "Replay saving…",
                GoatShot.App.Models.ReplayBufferState.Error => "Retry Replay",
                _ => "Arm Replay"
            };
            toggle.Enabled = status.State != GoatShot.App.Models.ReplayBufferState.Saving;
        }

        if (_actionItems.TryGetValue(TrayMenuActionKind.SaveReplay, out var save))
        {
            save.Enabled = status.State is GoatShot.App.Models.ReplayBufferState.Armed or
                GoatShot.App.Models.ReplayBufferState.Paused;
        }

        var replayLabel = status.SystemSuspended &&
            status.State is GoatShot.App.Models.ReplayBufferState.Armed or
                GoatShot.App.Models.ReplayBufferState.Saving
            ? "Suspended"
            : status.State.ToString();
        _notifyIcon.Text = status.State == GoatShot.App.Models.ReplayBufferState.Off
            ? BrandIdentity.ProductName
            : $"{BrandIdentity.ProductName} — Replay {replayLabel}";
    }

    public void Dispose()
    {
        if (_hotkeys is not null)
        {
            _hotkeys.RegistrationsChanged -= Hotkeys_RegistrationsChanged;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }
}

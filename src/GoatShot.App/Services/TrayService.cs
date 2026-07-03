using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Icon _trayIcon;

    public TrayService(MainWindow window)
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
            menu.Items.Add(definition.Label, null, (_, _) => Dispatch(window, actionKind));
        }

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "GoatShot",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => window.Dispatcher.Invoke(window.ShowWorkspaceCommand);
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }
}

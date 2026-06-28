using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSnapshot = 0x2C;
    private const uint VkR = 0x52;
    private const uint VkO = 0x4F;
    private const uint VkC = 0x43;
    private const uint VkU = 0x55;

    private readonly Dictionary<int, HotkeyAction> _actions = new();
    private readonly List<string> _registrationReport = new();
    private HwndSource? _source;
    private IntPtr _handle;

    public event EventHandler<HotkeyAction>? ActionTriggered;

    public IReadOnlyList<string> RegistrationReport => _registrationReport;

    public void Attach(Window window)
    {
        if (_source is not null)
        {
            return;
        }

        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        Register(100, HotkeyAction.AllInOneCapture, 0, VkSnapshot, "PrintScreen");
        Register(101, HotkeyAction.RegionCapture, ModControl, VkSnapshot, "Ctrl + PrintScreen");
        Register(102, HotkeyAction.WindowCapture, ModAlt, VkSnapshot, "Alt + PrintScreen");
        Register(103, HotkeyAction.LastRegionCapture, ModShift, VkSnapshot, "Shift + PrintScreen");
        Register(104, HotkeyAction.ToggleRecording, ModControl | ModShift, VkR, "Ctrl + Shift + R");
        Register(105, HotkeyAction.OcrRegion, ModControl | ModShift, VkO, "Ctrl + Shift + O");
        Register(106, HotkeyAction.ColorPicker, ModControl | ModShift, VkC, "Ctrl + Shift + C");
        Register(107, HotkeyAction.PixelRuler, ModControl | ModShift, VkU, "Ctrl + Shift + U");
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys.ToArray())
        {
            UnregisterHotKey(_handle, id);
        }

        _actions.Clear();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private void Register(int id, HotkeyAction action, uint modifiers, uint key, string label)
    {
        if (RegisterHotKey(_handle, id, modifiers, key))
        {
            _actions[id] = action;
            _registrationReport.Add($"{label}: active");
        }
        else
        {
            _registrationReport.Add($"{label}: unavailable");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            ActionTriggered?.Invoke(this, action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

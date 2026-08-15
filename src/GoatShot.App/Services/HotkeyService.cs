using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;

    private readonly Dictionary<int, HotkeyAction> _actions = new();
    private readonly List<string> _registrationReport = new();
    private IReadOnlyList<ResolvedKeybind> _keybinds;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _disposed;
    private bool _suspended;

    public event EventHandler<HotkeyAction>? ActionTriggered;

    /// <summary>Raised after <see cref="Reload"/> re-registers, so surfaces can refresh their hints.</summary>
    public event EventHandler? RegistrationsChanged;

    public HotkeyService(IEnumerable<KeybindAssignment>? assignments = null)
    {
        _keybinds = KeybindCatalog.Resolve(assignments);
    }

    public IReadOnlyList<string> RegistrationReport => _registrationReport;

    /// <summary>The gestures this service was constructed with, in catalog order.</summary>
    public IReadOnlyList<ResolvedKeybind> Keybinds => _keybinds;

    public bool IsRegistered(HotkeyAction action) => _actions.ContainsValue(action);

    /// <summary>Gesture currently driving an action, or an empty string when it is unbound.</summary>
    public string DisplayGesture(HotkeyAction action) =>
        _keybinds.FirstOrDefault(keybind => keybind.Action == action) is { IsBound: true } keybind
            ? keybind.DisplayGesture
            : string.Empty;

    public void Attach(Window window)
    {
        if (_source is not null || _disposed)
        {
            return;
        }

        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        RegisterAll();
    }

    /// <summary>
    /// Swaps in a new set of assignments and re-registers immediately, so a rebind saved from the
    /// settings window takes effect without restarting Receipts.
    /// </summary>
    public void Reload(IEnumerable<KeybindAssignment>? assignments)
    {
        if (_disposed)
        {
            return;
        }

        _keybinds = KeybindCatalog.Resolve(assignments);
        if (_source is null)
        {
            return;
        }

        UnregisterAll();
        _suspended = false;
        RegisterAll();
        RegistrationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterAll()
    {
        _registrationReport.Clear();

        foreach (var keybind in _keybinds.Where(keybind => !keybind.IsBound))
        {
            _registrationReport.Add($"{keybind.Label}: not set");
        }

        foreach (var keybind in _keybinds.Where(keybind => keybind.IsBound && !keybind.IsValid))
        {
            _registrationReport.Add($"{keybind.Label}: invalid hotkey '{keybind.Gesture}'");
        }

        foreach (var registration in KeybindCatalog.BuildRegistrationPlan(_keybinds))
        {
            Register(registration);
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToArray())
        {
            UnregisterHotKey(_handle, id);
        }

        _actions.Clear();
    }

    /// <summary>
    /// Releases the OS-level registrations without forgetting them, so the settings window can read a
    /// chord like Ctrl+Shift+R from the keyboard instead of having it fire the action being rebound.
    /// </summary>
    public void Suspend()
    {
        if (_disposed || _suspended || _handle == IntPtr.Zero)
        {
            return;
        }

        _suspended = true;
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_handle, id);
        }
    }

    public void Resume()
    {
        if (_disposed || !_suspended || _handle == IntPtr.Zero)
        {
            return;
        }

        _suspended = false;
        foreach (var registration in KeybindCatalog.BuildRegistrationPlan(_keybinds))
        {
            if (_actions.ContainsKey(registration.Id))
            {
                RegisterHotKey(_handle, registration.Id, registration.Modifiers, registration.Key);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private void Register(KeybindRegistration registration)
    {
        var label = $"{registration.Label} ({registration.DisplayGesture})";
        if (RegisterHotKey(_handle, registration.Id, registration.Modifiers, registration.Key))
        {
            _actions[registration.Id] = registration.Action;
            _registrationReport.Add($"{label}: active");
        }
        else
        {
            _registrationReport.Add($"{label}: unavailable");
        }
    }

    internal static bool TryParseGesture(string? gesture, out uint modifiers, out uint key) =>
        HotkeyGesture.TryParse(gesture, out modifiers, out key);

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

using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class CaptureOverlayTargetCatalog
{
    private const int MaxWindowTargets = 30;
    private const int MaxControlTargets = 40;

    public static CaptureBounds GetVirtualScreenBounds()
    {
        return new CaptureBounds
        {
            X = Forms.SystemInformation.VirtualScreen.Left,
            Y = Forms.SystemInformation.VirtualScreen.Top,
            Width = Forms.SystemInformation.VirtualScreen.Width,
            Height = Forms.SystemInformation.VirtualScreen.Height
        };
    }

    public static IReadOnlyList<CaptureOverlayTarget> BuildLiveTargets()
    {
        var targets = new List<CaptureOverlayTarget>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            targets.Add(new CaptureOverlayTarget(
                $"monitor:{screen.DeviceName}",
                $"{(screen.Primary ? "Primary monitor" : "Monitor")} {screen.DeviceName} ({screen.Bounds.Width} x {screen.Bounds.Height})",
                CaptureOverlayTargetKind.Monitor,
                new CaptureBounds
                {
                    X = screen.Bounds.X,
                    Y = screen.Bounds.Y,
                    Width = screen.Bounds.Width,
                    Height = screen.Bounds.Height
                }));
        }

        var windowCount = 0;
        var controlCount = 0;
        EnumWindows((handle, _) =>
        {
            if (windowCount >= MaxWindowTargets)
            {
                return true;
            }

            if (!TryBuildWindowTarget(handle, windowCount, out var windowTarget))
            {
                return true;
            }

            targets.Add(windowTarget);
            windowCount++;

            var content = BuildContentAreaTarget(windowTarget);
            if (content is not null)
            {
                targets.Add(content);
            }

            if (controlCount < MaxControlTargets)
            {
                EnumChildWindows(handle, (child, _) =>
                {
                    if (controlCount >= MaxControlTargets)
                    {
                        return false;
                    }

                    if (TryBuildControlTarget(child, windowTarget, controlCount, out var controlTarget))
                    {
                        targets.Add(controlTarget);
                        controlCount++;
                    }

                    return true;
                }, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);

        return targets;
    }

    /// <summary>
    /// Child targets for a single window, resolved on demand. The eager catalog caps controls
    /// globally, which starves every window late in z-order; hover only ever needs the window under
    /// the cursor, so it asks for that one window's children instead of paying for all of them.
    /// </summary>
    public static IReadOnlyList<CaptureOverlayTarget> BuildChildTargets(
        CaptureOverlayTarget windowTarget,
        int maxControls = MaxControlTargets)
    {
        ArgumentNullException.ThrowIfNull(windowTarget);
        var targets = new List<CaptureOverlayTarget>();
        var content = BuildContentAreaTarget(windowTarget);
        if (content is not null)
        {
            targets.Add(content);
        }

        var handle = new IntPtr(windowTarget.NativeHandle);
        if (handle == IntPtr.Zero || maxControls <= 0)
        {
            return targets;
        }

        var count = 0;
        EnumChildWindows(handle, (child, _) =>
        {
            if (count >= maxControls)
            {
                return false;
            }

            if (TryBuildControlTarget(child, windowTarget, count, out var controlTarget))
            {
                targets.Add(controlTarget);
                count++;
            }

            return true;
        }, IntPtr.Zero);

        return targets;
    }

    private static bool TryBuildWindowTarget(IntPtr handle, int zOrder, out CaptureOverlayTarget target)
    {
        target = null!;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle))
        {
            return false;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!TryGetBounds(handle, out var bounds) || bounds.Width < 120 || bounds.Height < 90)
        {
            return false;
        }

        target = new CaptureOverlayTarget(
            $"window:{handle.ToInt64():X}",
            $"Window: {TrimForDisplay(title, 72)} ({bounds.Width} x {bounds.Height})",
            CaptureOverlayTargetKind.Window,
            bounds,
            ShowInChooser: true,
            ZOrder: zOrder,
            NativeHandle: handle.ToInt64());
        return true;
    }

    private static CaptureOverlayTarget? BuildContentAreaTarget(CaptureOverlayTarget windowTarget)
    {
        var window = windowTarget.Bounds;
        if (window.Width < 180 || window.Height < 160)
        {
            return null;
        }

        var topInset = Math.Clamp(window.Height / 12, 42, 96);
        var sideInset = Math.Clamp(window.Width / 80, 4, 12);
        var bottomInset = Math.Clamp(window.Height / 80, 4, 12);
        return new CaptureOverlayTarget(
            $"{windowTarget.Id}:content",
            $"Content area: {StripTargetPrefix(windowTarget.DisplayName)}",
            CaptureOverlayTargetKind.ContentArea,
            new CaptureBounds
            {
                X = window.X + sideInset,
                Y = window.Y + topInset,
                Width = Math.Max(1, window.Width - (sideInset * 2)),
                Height = Math.Max(1, window.Height - topInset - bottomInset)
            },
            ShowInChooser: false,
            ZOrder: windowTarget.ZOrder,
            ParentId: windowTarget.Id,
            NativeHandle: windowTarget.NativeHandle);
    }

    private static bool TryBuildControlTarget(
        IntPtr handle,
        CaptureOverlayTarget parent,
        int index,
        out CaptureOverlayTarget target)
    {
        target = null!;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle))
        {
            return false;
        }

        if (!TryGetBounds(handle, out var bounds) ||
            bounds.Width < 120 ||
            bounds.Height < 70 ||
            !Contains(parent.Bounds, bounds))
        {
            return false;
        }

        var className = GetClassName(handle);
        var title = GetWindowTitle(handle);
        var label = string.IsNullOrWhiteSpace(title) ? className : title;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Control {index + 1}";
        }

        target = new CaptureOverlayTarget(
            $"control:{handle.ToInt64():X}",
            $"Control: {TrimForDisplay(label, 56)} ({bounds.Width} x {bounds.Height})",
            CaptureOverlayTargetKind.ControlArea,
            bounds,
            ShowInChooser: false,
            ZOrder: parent.ZOrder,
            ParentId: parent.Id,
            NativeHandle: handle.ToInt64());
        return true;
    }

    private static bool TryGetBounds(IntPtr handle, out CaptureBounds bounds)
    {
        bounds = null!;
        if (!GetWindowRect(handle, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bounds = new CaptureBounds
        {
            X = rect.Left,
            Y = rect.Top,
            Width = width,
            Height = height
        };
        return true;
    }

    private static bool Contains(CaptureBounds outer, CaptureBounds inner)
    {
        return inner.X >= outer.X &&
            inner.Y >= outer.Y &&
            inner.X + inner.Width <= outer.X + outer.Width &&
            inner.Y + inner.Height <= outer.Y + outer.Height;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static string GetClassName(IntPtr handle)
    {
        var builder = new StringBuilder(128);
        return GetClassName(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static string TrimForDisplay(string value, int maxLength)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= maxLength ? value : value[..Math.Max(1, maxLength - 1)] + "...";
    }

    private static string StripTargetPrefix(string value)
    {
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        return separator >= 0 && separator + 1 < value.Length
            ? value[(separator + 1)..].Trim()
            : value;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parentHandle, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

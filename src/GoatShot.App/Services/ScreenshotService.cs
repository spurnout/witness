using System.Drawing;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;
using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public sealed class ScreenshotService
{
    private readonly AppSettings _settings;
    private CaptureBounds? _lastRegion;

    public ScreenshotService(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<CapturedBitmap?> CaptureRegionAsync(Window? owner = null)
    {
        var sourceContext = GetForegroundSourceContext();
        var bounds = SelectRegionBounds(owner);
        if (bounds is null)
        {
            return Task.FromResult<CapturedBitmap?>(null);
        }

        _lastRegion = bounds;
        return Task.FromResult<CapturedBitmap?>(CaptureRectangle(bounds, CaptureKind.Region, sourceContext));
    }

    public Task<CaptureBounds?> SelectRegionBoundsAsync(Window? owner = null)
    {
        var bounds = SelectRegionBounds(owner);
        if (bounds is not null)
        {
            _lastRegion = bounds;
        }

        return Task.FromResult(bounds);
    }

    private CaptureBounds? SelectRegionBounds(Window? owner = null)
    {
        using var background = CaptureVirtualScreenBitmap(includeCursor: false);
        var source = ImageInterop.ToBitmapSource(background);
        var overlay = new RegionCaptureWindow(
            source,
            _settings.CaptureContextPadding,
            enableHoverAutoSelect: _settings.EnableCaptureHoverAutoSelect);
        if (owner?.IsVisible == true)
        {
            overlay.Owner = owner;
        }

        var result = overlay.ShowDialog();
        if (result != true || overlay.SelectedBounds is null)
        {
            return null;
        }

        return overlay.SelectedBounds;
    }

    public Task<CapturedBitmap> CaptureFullScreenAsync()
    {
        var bounds = GetVirtualScreenBounds();
        return Task.FromResult(CaptureRectangle(bounds, CaptureKind.Fullscreen, GetForegroundSourceContext()));
    }

    public Task<CapturedBitmap> CaptureAllMonitorsAsync()
    {
        var bounds = GetVirtualScreenBounds();
        return Task.FromResult(CaptureRectangle(bounds, CaptureKind.AllMonitors, GetForegroundSourceContext()));
    }

    public Task<CapturedBitmap> CaptureActiveMonitorAsync()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var bounds = new CaptureBounds
        {
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };

        var source = GetForegroundSourceContext();
        source.MonitorName = screen.DeviceName;
        return Task.FromResult(CaptureRectangle(bounds, CaptureKind.ActiveMonitor, source));
    }

    public Task<CapturedBitmap> CaptureRecordingTargetAsync(RecordingCaptureTarget? target = null)
    {
        target = (target ?? RecordingCaptureTarget.ActiveMonitor()).Normalize();
        return target.Kind switch
        {
            RecordingCaptureTargetKind.AllMonitors => CaptureAllMonitorsAsync(),
            RecordingCaptureTargetKind.ActiveWindow => CaptureActiveWindowAsync(),
            RecordingCaptureTargetKind.Region or RecordingCaptureTargetKind.FixedRegion when target.Bounds is not null =>
                Task.FromResult(CaptureRectangle(target.Bounds, target.FrameCaptureKind, GetForegroundSourceContext())),
            _ => CaptureActiveMonitorAsync()
        };
    }

    public Task<CapturedBitmap> CaptureActiveWindowAsync()
    {
        if (TryGetActiveWindowBounds(out var bounds, out var handle))
        {
            return Task.FromResult(CaptureRectangle(bounds, CaptureKind.ActiveWindow, GetSourceContext(handle)));
        }

        return CaptureActiveMonitorAsync();
    }

    public async Task<CapturedBitmap> CaptureScrollingActiveWindowAsync(
        int maxFrames = 6,
        int wheelClicksPerFrame = 5,
        int settleDelayMs = 240)
    {
        return await CaptureScrollingActiveWindowAsync(new ScrollingCaptureOptions
        {
            MaxFrames = maxFrames,
            WheelClicksPerFrame = wheelClicksPerFrame,
            SettleDelayMs = settleDelayMs
        });
    }

    public async Task<CapturedBitmap> CaptureScrollingActiveWindowAsync(ScrollingCaptureOptions options)
    {
        options = options.Normalize();

        if (!TryGetActiveWindowBounds(out var bounds, out var handle))
        {
            return await CaptureActiveWindowAsync();
        }

        var source = GetSourceContext(handle);
        var frames = new List<Bitmap>();
        try
        {
            frames.Add(CaptureRectangleBitmap(bounds));
            for (var index = 1; index < options.MaxFrames; index++)
            {
                SetForegroundWindow(handle);
                SendMouseWheel(-MouseWheelDelta * options.WheelClicksPerFrame, options.Axis);
                await Task.Delay(options.SettleDelayMs);

                var next = CaptureRectangleBitmap(bounds);
                if (ScrollingStitcher.LooksLikeSameFrame(frames[^1], next, options))
                {
                    next.Dispose();
                    break;
                }

                frames.Add(next);
            }

            var stitched = ScrollingStitcher.Stitch(frames, options);
            var stitchedBounds = new CaptureBounds
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = stitched.Width,
                Height = stitched.Height
            };

            return new CapturedBitmap(stitched, CaptureKind.ScrollingWindow, stitchedBounds, source);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public Task<CapturedBitmap> CaptureFixedRegionAtCursorAsync(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Fixed capture dimensions must be positive.");
        }

        var bounds = ResolveFixedRegionAtCursorBounds(width, height);

        var source = GetForegroundSourceContext();
        source.MonitorName = Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName;
        return Task.FromResult(CaptureRectangle(bounds, CaptureKind.FixedRegion, source));
    }

    public CaptureBounds ResolveFixedRegionAtCursorBounds(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Fixed capture dimensions must be positive.");
        }

        var virtualScreen = GetVirtualScreenBounds();
        var captureWidth = Math.Min(width, virtualScreen.Width);
        var captureHeight = Math.Min(height, virtualScreen.Height);
        var cursor = Forms.Cursor.Position;
        var x = Math.Clamp(
            cursor.X - (captureWidth / 2),
            virtualScreen.X,
            virtualScreen.X + virtualScreen.Width - captureWidth);
        var y = Math.Clamp(
            cursor.Y - (captureHeight / 2),
            virtualScreen.Y,
            virtualScreen.Y + virtualScreen.Height - captureHeight);

        return new CaptureBounds
        {
            X = x,
            Y = y,
            Width = captureWidth,
            Height = captureHeight
        };
    }

    public Task<CapturedBitmap?> CaptureLastRegionAsync(Window owner)
    {
        if (_lastRegion is null)
        {
            return CaptureRegionAsync(owner);
        }

        return Task.FromResult<CapturedBitmap?>(CaptureRectangle(_lastRegion, CaptureKind.LastRegion, GetForegroundSourceContext()));
    }

    private CapturedBitmap CaptureRectangle(CaptureBounds bounds, CaptureKind kind, CaptureSource source)
    {
        return new CapturedBitmap(CaptureRectangleBitmap(bounds), kind, bounds, source);
    }

    private Bitmap CaptureRectangleBitmap(CaptureBounds bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height));

        if (_settings.IncludeCursor)
        {
            DrawCursorIfInside(graphics, bounds);
        }

        return bitmap;
    }

    private Bitmap CaptureVirtualScreenBitmap(bool includeCursor)
    {
        var bounds = GetVirtualScreenBounds();
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height));

        if (includeCursor)
        {
            DrawCursorIfInside(graphics, bounds);
        }

        return bitmap;
    }

    private static CaptureBounds GetVirtualScreenBounds()
    {
        return new CaptureBounds
        {
            X = Forms.SystemInformation.VirtualScreen.Left,
            Y = Forms.SystemInformation.VirtualScreen.Top,
            Width = Forms.SystemInformation.VirtualScreen.Width,
            Height = Forms.SystemInformation.VirtualScreen.Height
        };
    }

    private static bool TryGetActiveWindowBounds(out CaptureBounds bounds, out IntPtr handle)
    {
        bounds = new CaptureBounds();
        handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
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

    private static void SendMouseWheel(int delta, ScrollingCaptureAxis axis)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                MouseData = unchecked((uint)delta),
                Flags = axis == ScrollingCaptureAxis.Horizontal ? MouseEventHWheel : MouseEventWheel
            }
        };

        SendInput(1, [input], Marshal.SizeOf<NativeInput>());
    }

    private static void DrawCursorIfInside(Graphics graphics, CaptureBounds bounds)
    {
        var cursorInfo = new CursorInfo
        {
            CbSize = Marshal.SizeOf<CursorInfo>()
        };

        if (!GetCursorInfo(out cursorInfo) || cursorInfo.Flags != CursorShowing)
        {
            return;
        }

        var x = cursorInfo.PtScreenPos.X - bounds.X;
        var y = cursorInfo.PtScreenPos.Y - bounds.Y;
        if (x < 0 || y < 0 || x > bounds.Width || y > bounds.Height)
        {
            return;
        }

        var hdc = graphics.GetHdc();
        try
        {
            DrawIconEx(hdc, x, y, cursorInfo.HCursor, 0, 0, 0, IntPtr.Zero, DiNormal);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private static CaptureSource GetForegroundSourceContext()
    {
        var handle = GetForegroundWindow();
        return handle == IntPtr.Zero ? new CaptureSource() : GetSourceContext(handle);
    }

    private static CaptureSource GetSourceContext(IntPtr handle)
    {
        var source = new CaptureSource();
        if (handle == IntPtr.Zero)
        {
            return source;
        }

        var title = new StringBuilder(512);
        if (GetWindowText(handle, title, title.Capacity) > 0)
        {
            source.WindowTitle = title.ToString();
        }

        GetWindowThreadProcessId(handle, out var processId);
        if (processId != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                source.ProcessName = process.ProcessName;
            }
            catch
            {
                source.ProcessName = "unknown";
            }
        }

        var screen = Forms.Screen.FromHandle(handle);
        source.MonitorName = screen.DeviceName;
        return source;
    }

    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;
    private const int MouseWheelDelta = 120;
    private const uint InputMouse = 0;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventHWheel = 0x01000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CursorInfo pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr hdc,
        int xLeft,
        int yTop,
        IntPtr hIcon,
        int cxWidth,
        int cyHeight,
        int istepIfAniCur,
        IntPtr hbrFlickerFreeDraw,
        int diFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, NativeInput[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HCursor;
        public NativePoint PtScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GoatShot.App.Models;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class WindowsGraphicsCaptureEngine : ICaptureEngine
{
    private static readonly FeatureLevel[] PreferredFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    public string EngineName => "Windows.Graphics.Capture";
    public bool IsProductionEngine => true;

    public static bool SupportsKind(CaptureKind kind)
    {
        return kind is CaptureKind.ActiveMonitor
            or CaptureKind.ActiveWindow
            or CaptureKind.Fullscreen
            or CaptureKind.AllMonitors
            or CaptureKind.Region
            or CaptureKind.FixedRegion;
    }

    public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GraphicsCaptureSession.IsSupported())
            {
                return Task.FromResult(new ProviderHealth(false, $"{EngineName} is not supported on this Windows session."));
            }

            using var captured = CaptureMonitor(null, includeCursor: false, cancellationToken);
            return Task.FromResult(new ProviderHealth(
                true,
                $"{EngineName} captured a {captured.Bounds.Width}x{captured.Bounds.Height} active-monitor frame from {captured.Source?.MonitorName ?? "the active monitor"}; active-window, fullscreen/all-monitor, and bounded region capture are also supported."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProviderHealth(false, $"{EngineName} validation failed: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    public Task<CapturedBitmap?> CaptureAsync(CaptureEngineRequest request, CancellationToken cancellationToken)
    {
        if (!SupportsKind(request.Kind))
        {
            return Task.FromResult<CapturedBitmap?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CapturedBitmap?>(request.Kind switch
        {
            CaptureKind.ActiveWindow => CaptureActiveWindow(request.IncludeCursor, cancellationToken),
            CaptureKind.Fullscreen or CaptureKind.AllMonitors => CaptureScreenBounds(GetVirtualScreenBounds(), request.Kind, request.IncludeCursor, cancellationToken),
            CaptureKind.Region or CaptureKind.FixedRegion => CaptureScreenBounds(
                request.Bounds ?? throw new ArgumentException($"Windows.Graphics.Capture {request.Kind} capture requires explicit bounds.", nameof(request)),
                request.Kind,
                request.IncludeCursor,
                cancellationToken),
            _ => CaptureMonitor(request.MonitorName, request.IncludeCursor, cancellationToken)
        });
    }

    private static CapturedBitmap CaptureMonitor(string? monitorName, bool includeCursor, CancellationToken cancellationToken)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException("Windows.Graphics.Capture monitor capture requires a Windows desktop session.");
        }

        var screen = ResolveScreen(monitorName);
        var bounds = new CaptureBounds
        {
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };

        var captureItem = CreateCaptureItemForScreen(screen);
        using var bitmap = CaptureItemBitmap(captureItem, cancellationToken);
        if (includeCursor)
        {
            DrawCursorIfInside(bitmap, bounds);
        }

        var source = new CaptureSource
        {
            MonitorName = screen.DeviceName
        };
        return new CapturedBitmap((Bitmap)bitmap.Clone(), CaptureKind.ActiveMonitor, bounds, source);
    }

    private static CapturedBitmap CaptureActiveWindow(bool includeCursor, CancellationToken cancellationToken)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException("Windows.Graphics.Capture active-window capture requires a Windows desktop session.");
        }

        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("Windows.Graphics.Capture could not resolve the active foreground window.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Windows.Graphics.Capture active-window bounds were empty.");
        }

        var captureItem = CreateCaptureItemForWindow(handle);
        using var bitmap = CaptureItemBitmap(captureItem, cancellationToken);
        var bounds = new CaptureBounds
        {
            X = rect.Left,
            Y = rect.Top,
            Width = bitmap.Width,
            Height = bitmap.Height
        };

        if (includeCursor)
        {
            DrawCursorIfInside(bitmap, bounds);
        }

        return new CapturedBitmap((Bitmap)bitmap.Clone(), CaptureKind.ActiveWindow, bounds, GetSourceContext(handle));
    }

    private static CapturedBitmap CaptureScreenBounds(
        CaptureBounds requestedBounds,
        CaptureKind kind,
        bool includeCursor,
        CancellationToken cancellationToken)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException($"Windows.Graphics.Capture {kind} capture requires a Windows desktop session.");
        }

        var bounds = NormalizeBounds(requestedBounds);
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        var usedScreens = new List<string>();
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var screenBounds = BoundsFromScreen(screen);
                var intersection = Intersect(bounds, screenBounds);
                if (intersection is null)
                {
                    continue;
                }

                using var captured = CaptureMonitor(screen.DeviceName, includeCursor: false, cancellationToken);
                var source = new Rectangle(
                    intersection.Value.X - captured.Bounds.X,
                    intersection.Value.Y - captured.Bounds.Y,
                    intersection.Value.Width,
                    intersection.Value.Height);
                var destination = new Rectangle(
                    intersection.Value.X - bounds.X,
                    intersection.Value.Y - bounds.Y,
                    intersection.Value.Width,
                    intersection.Value.Height);
                graphics.DrawImage(captured.Bitmap, destination, source, GraphicsUnit.Pixel);
                usedScreens.Add(screen.DeviceName);
            }
        }

        if (usedScreens.Count == 0)
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"Windows.Graphics.Capture {kind} capture bounds did not intersect any active monitor: {bounds.Display}");
        }

        if (includeCursor)
        {
            DrawCursorIfInside(bitmap, bounds);
        }

        return new CapturedBitmap(
            bitmap,
            kind,
            bounds,
            new CaptureSource { MonitorName = string.Join(", ", usedScreens.Distinct(StringComparer.OrdinalIgnoreCase)) });
    }

    private static Bitmap CaptureItemBitmap(GraphicsCaptureItem captureItem, CancellationToken cancellationToken)
    {
        using var d3d = CreateD3D11Device();
        using var dxgiDevice = d3d.Device.QueryInterface<IDXGIDevice>();
        var wrapResult = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var winrtDevicePtr);
        if (wrapResult < 0)
        {
            Marshal.ThrowExceptionForHR(wrapResult);
        }

        try
        {
            var winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                captureItem.Size);
            using var session = framePool.CreateCaptureSession(captureItem);
            using var frameReady = new AutoResetEvent(false);

            Direct3D11CaptureFrame? arrivedFrame = null;
            framePool.FrameArrived += (_, _) =>
            {
                arrivedFrame?.Dispose();
                arrivedFrame = framePool.TryGetNextFrame();
                frameReady.Set();
            };

            session.StartCapture();
            WaitForFrame(frameReady, cancellationToken);

            using var frame = arrivedFrame ?? throw new InvalidOperationException("Windows.Graphics.Capture did not return a frame.");
            arrivedFrame = null;
            using var sourceTexture = TextureFromSurface(frame.Surface);
            return ReadTextureToBitmap(d3d.Device, d3d.Context, sourceTexture);
        }
        finally
        {
            if (winrtDevicePtr != IntPtr.Zero)
            {
                Marshal.Release(winrtDevicePtr);
            }
        }
    }

    private static CaptureSource GetSourceContext(IntPtr handle)
    {
        var source = new CaptureSource
        {
            WindowTitle = GetWindowTitle(handle)
        };

        try
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId != 0)
            {
                using var process = Process.GetProcessById((int)processId);
                source.ProcessName = process.ProcessName;
            }
        }
        catch
        {
            source.ProcessName = null;
        }

        return source;
    }

    private static string? GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new StringBuilder(length + 1);
        return GetWindowText(handle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static void WaitForFrame(WaitHandle frameReady, CancellationToken cancellationToken)
    {
        var signaled = WaitHandle.WaitAny([frameReady, cancellationToken.WaitHandle], TimeSpan.FromSeconds(3));
        if (signaled == WaitHandle.WaitTimeout)
        {
            throw new TimeoutException("Windows.Graphics.Capture did not deliver a frame within 3 seconds.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static Forms.Screen ResolveScreen(string? monitorName)
    {
        if (!string.IsNullOrWhiteSpace(monitorName))
        {
            var match = Forms.Screen.AllScreens.FirstOrDefault(screen =>
                screen.DeviceName.Equals(monitorName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            throw new InvalidOperationException($"No Windows screen matched monitor {monitorName}.");
        }

        return Forms.Screen.FromPoint(Forms.Cursor.Position);
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

    private static CaptureBounds BoundsFromScreen(Forms.Screen screen)
    {
        return new CaptureBounds
        {
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };
    }

    private static CaptureBounds NormalizeBounds(CaptureBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must have positive width and height.");
        }

        return new CaptureBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private static Rectangle? Intersect(CaptureBounds first, CaptureBounds second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        return right > left && bottom > top
            ? new Rectangle(left, top, right - left, bottom - top)
            : null;
    }

    private static GraphicsCaptureItem CreateCaptureItemForScreen(Forms.Screen screen)
    {
        var monitor = MonitorFromPoint(
            new NativePoint(screen.Bounds.X + Math.Max(1, screen.Bounds.Width / 2), screen.Bounds.Y + Math.Max(1, screen.Bounds.Height / 2)),
            MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Windows could not resolve monitor handle for {screen.DeviceName}.");
        }

        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var iid = GraphicsCaptureItemInterfaceId;
        var hr = interop.CreateForMonitor(monitor, ref iid, out var itemPtr);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            if (itemPtr != IntPtr.Zero)
            {
                Marshal.Release(itemPtr);
            }
        }
    }

    private static GraphicsCaptureItem CreateCaptureItemForWindow(IntPtr handle)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var iid = GraphicsCaptureItemInterfaceId;
        var hr = interop.CreateForWindow(handle, ref iid, out var itemPtr);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            if (itemPtr != IntPtr.Zero)
            {
                Marshal.Release(itemPtr);
            }
        }
    }

    private static D3D11DeviceContext CreateD3D11Device()
    {
        var result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            PreferredFeatureLevels,
            out var device,
            out var featureLevel,
            out var context);
        if (result.Failure || device is null || context is null)
        {
            device?.Dispose();
            context?.Dispose();
            throw new InvalidOperationException($"Direct3D11 device creation for Windows.Graphics.Capture failed: {result}.");
        }

        return new D3D11DeviceContext(device, context, featureLevel);
    }

    private static ID3D11Texture2D TextureFromSurface(IDirect3DSurface surface)
    {
        var surfacePtr = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(surfacePtr);
            var iid = typeof(ID3D11Texture2D).GUID;
            access.GetInterface(ref iid, out var texturePtr);
            return new ID3D11Texture2D(texturePtr);
        }
        finally
        {
            WinRT.MarshalInterface<IDirect3DSurface>.DisposeAbi(surfacePtr);
        }
    }

    private static Bitmap ReadTextureToBitmap(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D sourceTexture)
    {
        var description = sourceTexture.Description;
        var stagingDescription = description;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;

        using var staging = device.CreateTexture2D(stagingDescription);
        context.CopyResource(staging, sourceTexture);
        context.Flush();

        var mapResult = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped);
        if (mapResult.Failure)
        {
            throw new InvalidOperationException($"Windows.Graphics.Capture staging texture map failed: {mapResult}.");
        }

        try
        {
            return CopyMappedBgraToBitmap(mapped, (int)description.Width, (int)description.Height);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    private static Bitmap CopyMappedBgraToBitmap(MappedSubresource mapped, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(width * 4);
            var buffer = new byte[rowBytes];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(mapped.DataPointer, checked((int)(y * mapped.RowPitch))), buffer, 0, rowBytes);
                for (var x = 3; x < rowBytes; x += 4)
                {
                    buffer[x] = byte.MaxValue;
                }

                Marshal.Copy(buffer, 0, IntPtr.Add(bits.Scan0, y * bits.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        return bitmap;
    }

    private static void DrawCursorIfInside(Bitmap bitmap, CaptureBounds bounds)
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

        using var graphics = Graphics.FromImage(bitmap);
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

    private sealed record D3D11DeviceContext(
        ID3D11Device Device,
        ID3D11DeviceContext Context,
        FeatureLevel FeatureLevel) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();
            Device.Dispose();
        }
    }

    private static readonly Guid GraphicsCaptureItemInterfaceId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, [In] ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, [In] ref Guid iid, out IntPtr result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface([In] ref Guid iid, out IntPtr p);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HCursor;
        public NativePoint PtScreenPos;
    }
}

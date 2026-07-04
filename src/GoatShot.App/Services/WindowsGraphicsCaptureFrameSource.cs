using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class WindowsGraphicsCaptureFrameSource : IDisposable
{
    private static readonly FeatureLevel[] PreferredFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly CaptureBounds _itemBounds;
    private readonly CaptureBounds _bounds;
    private readonly CaptureKind _kind;
    private readonly CaptureSource _source;
    private readonly string _sourceName;
    private readonly D3D11DeviceContext _d3d;
    private readonly IntPtr _winrtDevicePtr;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly object _frameGate = new();
    private Direct3D11CaptureFrame? _pendingFrame;
    private Bitmap? _lastBitmap;
    private bool _disposed;

    private WindowsGraphicsCaptureFrameSource(
        CaptureBounds itemBounds,
        CaptureBounds bounds,
        CaptureKind kind,
        CaptureSource source,
        string sourceName,
        D3D11DeviceContext d3d,
        IntPtr winrtDevicePtr,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session)
    {
        _itemBounds = itemBounds;
        _bounds = bounds;
        _kind = kind;
        _source = source;
        _sourceName = sourceName;
        _d3d = d3d;
        _winrtDevicePtr = winrtDevicePtr;
        _framePool = framePool;
        _session = session;
    }

    public CaptureBounds Bounds => _bounds;
    public string MonitorName => _sourceName;
    public string SourceName => _sourceName;

    public static bool SupportsTarget(RecordingCaptureTarget? target)
    {
        target = (target ?? RecordingCaptureTarget.ActiveMonitor()).Normalize();
        return target.Kind switch
        {
            RecordingCaptureTargetKind.ActiveMonitor or RecordingCaptureTargetKind.ActiveWindow => true,
            RecordingCaptureTargetKind.Region or RecordingCaptureTargetKind.FixedRegion => target.Bounds is not null,
            _ => false
        };
    }

    public static WindowsGraphicsCaptureFrameSource Start(RecordingCaptureTarget target, CancellationToken cancellationToken)
    {
        target = target.Normalize();
        return target.Kind switch
        {
            RecordingCaptureTargetKind.ActiveWindow => StartActiveWindow(cancellationToken),
            RecordingCaptureTargetKind.Region or RecordingCaptureTargetKind.FixedRegion when target.Bounds is not null =>
                StartSingleMonitorBounds(target, cancellationToken),
            RecordingCaptureTargetKind.Region or RecordingCaptureTargetKind.FixedRegion =>
                throw new ArgumentException($"{target.Kind} recording requires explicit bounds for Windows.Graphics.Capture frame capture.", nameof(target)),
            _ => StartActiveMonitor(null, cancellationToken)
        };
    }

    public static WindowsGraphicsCaptureFrameSource StartActiveMonitor(string? monitorName, CancellationToken cancellationToken)
    {
        EnsureSupportedDesktopSession("monitor frame capture");
        cancellationToken.ThrowIfCancellationRequested();
        var screen = ResolveScreen(monitorName);
        var bounds = new CaptureBounds
        {
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };
        var captureItem = CreateCaptureItemForScreen(screen);
        return StartCore(
            captureItem,
            bounds,
            bounds,
            CaptureKind.ActiveMonitor,
            new CaptureSource { MonitorName = screen.DeviceName },
            screen.DeviceName,
            cancellationToken);
    }

    private static WindowsGraphicsCaptureFrameSource StartActiveWindow(CancellationToken cancellationToken)
    {
        EnsureSupportedDesktopSession("active-window frame capture");
        cancellationToken.ThrowIfCancellationRequested();
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("Windows.Graphics.Capture could not resolve the active foreground window for recording.");
        }

        var captureItem = CreateCaptureItemForWindow(handle);
        var bounds = new CaptureBounds
        {
            X = rect.Left,
            Y = rect.Top,
            Width = Math.Max(1, captureItem.Size.Width),
            Height = Math.Max(1, captureItem.Size.Height)
        };
        var source = GetSourceContext(handle);
        var sourceName = string.IsNullOrWhiteSpace(source.WindowTitle)
            ? "active window"
            : $"active window \"{Truncate(source.WindowTitle, 80)}\"";
        return StartCore(
            captureItem,
            bounds,
            bounds,
            CaptureKind.ActiveWindow,
            source,
            sourceName,
            cancellationToken);
    }

    private static WindowsGraphicsCaptureFrameSource StartSingleMonitorBounds(
        RecordingCaptureTarget target,
        CancellationToken cancellationToken)
    {
        EnsureSupportedDesktopSession($"{target.FrameCaptureKind} frame capture");
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = NormalizeBounds(target.Bounds ?? throw new ArgumentException("Bounds are required.", nameof(target)));
        var screen = Forms.Screen.AllScreens.FirstOrDefault(candidate => Contains(BoundsFromScreen(candidate), bounds));
        if (screen is null)
        {
            throw new NotSupportedException($"Windows.Graphics.Capture recording for {target.DisplayName} requires bounds that fit within one monitor; falling back to GDI frames for {bounds.Display}.");
        }

        var itemBounds = BoundsFromScreen(screen);
        var captureItem = CreateCaptureItemForScreen(screen);
        return StartCore(
            captureItem,
            itemBounds,
            bounds,
            target.FrameCaptureKind,
            new CaptureSource { MonitorName = screen.DeviceName },
            $"{target.DisplayName} via {screen.DeviceName}",
            cancellationToken);
    }

    private static WindowsGraphicsCaptureFrameSource StartCore(
        GraphicsCaptureItem captureItem,
        CaptureBounds itemBounds,
        CaptureBounds bounds,
        CaptureKind kind,
        CaptureSource source,
        string sourceName,
        CancellationToken cancellationToken)
    {
        var d3d = CreateD3D11Device();
        IntPtr winrtDevicePtr = IntPtr.Zero;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;

        try
        {
            using var dxgiDevice = d3d.Device.QueryInterface<IDXGIDevice>();
            var wrapResult = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out winrtDevicePtr);
            if (wrapResult < 0)
            {
                Marshal.ThrowExceptionForHR(wrapResult);
            }

            var winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                captureItem.Size);
            session = framePool.CreateCaptureSession(captureItem);
            session.IsCursorCaptureEnabled = false;

            var frameSource = new WindowsGraphicsCaptureFrameSource(
                itemBounds,
                bounds,
                kind,
                source,
                sourceName,
                d3d,
                winrtDevicePtr,
                framePool,
                session);
            d3d = null!;
            winrtDevicePtr = IntPtr.Zero;
            framePool = null;
            session = null;
            frameSource._framePool.FrameArrived += frameSource.OnFrameArrived;
            cancellationToken.ThrowIfCancellationRequested();
            frameSource._session.StartCapture();
            return frameSource;
        }
        finally
        {
            session?.Dispose();
            framePool?.Dispose();
            if (winrtDevicePtr != IntPtr.Zero)
            {
                Marshal.Release(winrtDevicePtr);
            }

            d3d?.Dispose();
        }
    }

    public CapturedBitmap CaptureFrame(bool includeCursor, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var frame = TakeNextFrame(timeout, cancellationToken);
        if (frame is not null)
        {
            using (frame)
            using (var sourceTexture = TextureFromSurface(frame.Surface))
            using (var bitmap = ReadTextureToBitmap(_d3d.Device, _d3d.Context, sourceTexture))
            {
                StoreLastBitmap(bitmap);
            }
        }

        Bitmap fullFrame;
        lock (_frameGate)
        {
            if (_lastBitmap is null)
            {
                throw new TimeoutException("Windows.Graphics.Capture did not deliver an initial recording frame.");
            }

            fullFrame = (Bitmap)_lastBitmap.Clone();
        }

        Bitmap outputFrame;
        using (fullFrame)
        {
            outputFrame = CropFrameToBounds(fullFrame);
        }

        if (includeCursor)
        {
            DrawCursorIfInside(outputFrame, _bounds);
        }

        return new CapturedBitmap(
            outputFrame,
            _kind,
            CopyBounds(_bounds),
            CopySource(_source));
    }

    private Bitmap CropFrameToBounds(Bitmap fullFrame)
    {
        var sourceX = _bounds.X - _itemBounds.X;
        var sourceY = _bounds.Y - _itemBounds.Y;
        if (sourceX == 0 &&
            sourceY == 0 &&
            _bounds.Width == fullFrame.Width &&
            _bounds.Height == fullFrame.Height)
        {
            return (Bitmap)fullFrame.Clone();
        }

        var source = new Rectangle(
            Math.Clamp(sourceX, 0, Math.Max(0, fullFrame.Width - 1)),
            Math.Clamp(sourceY, 0, Math.Max(0, fullFrame.Height - 1)),
            Math.Min(_bounds.Width, Math.Max(1, fullFrame.Width - Math.Clamp(sourceX, 0, Math.Max(0, fullFrame.Width - 1)))),
            Math.Min(_bounds.Height, Math.Max(1, fullFrame.Height - Math.Clamp(sourceY, 0, Math.Max(0, fullFrame.Height - 1)))));
        var output = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        // Output stays pinned at the recording-start size (the MP4 encoder rejects size
        // changes mid-recording). If the live frame shrank below those bounds, letterbox
        // with opaque black; transparent fill turns into GIF artifacts and implicit black
        // in MP4 anyway.
        graphics.Clear(Color.Black);
        graphics.DrawImage(
            fullFrame,
            new Rectangle(0, 0, source.Width, source.Height),
            source,
            GraphicsUnit.Pixel);
        return output;
    }

    private Direct3D11CaptureFrame? TakeNextFrame(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_frameGate)
        {
            if (_pendingFrame is not null)
            {
                var existing = _pendingFrame;
                _pendingFrame = null;
                return existing;
            }
        }

        var wait = WaitHandle.WaitAny([_frameReady, cancellationToken.WaitHandle], timeout);
        cancellationToken.ThrowIfCancellationRequested();
        if (wait == WaitHandle.WaitTimeout)
        {
            return null;
        }

        lock (_frameGate)
        {
            var frame = _pendingFrame;
            _pendingFrame = null;
            return frame;
        }
    }

    private void StoreLastBitmap(Bitmap bitmap)
    {
        lock (_frameGate)
        {
            _lastBitmap?.Dispose();
            _lastBitmap = (Bitmap)bitmap.Clone();
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var next = sender.TryGetNextFrame();
        lock (_frameGate)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = next;
        }

        _frameReady.Set();
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

    private static void EnsureSupportedDesktopSession(string operation)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException($"Windows.Graphics.Capture {operation} requires a Windows desktop session.");
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this Windows session.");
        }
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

    private static bool Contains(CaptureBounds outer, CaptureBounds inner)
    {
        return inner.X >= outer.X &&
            inner.Y >= outer.Y &&
            inner.X + inner.Width <= outer.X + outer.Width &&
            inner.Y + inner.Height <= outer.Y + outer.Height;
    }

    private static CaptureBounds CopyBounds(CaptureBounds bounds)
    {
        return new CaptureBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private static CaptureSource CopySource(CaptureSource source)
    {
        return new CaptureSource
        {
            MonitorName = source.MonitorName,
            WindowTitle = source.WindowTitle,
            ProcessName = source.ProcessName
        };
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
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
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

        var buffer = new System.Text.StringBuilder(length + 1);
        return GetWindowText(handle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static string Truncate(string text, int maxLength)
    {
        text = text.Trim();
        return text.Length <= maxLength
            ? text
            : text[..Math.Max(0, maxLength - 3)] + "...";
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _framePool.Dispose();
        lock (_frameGate)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = null;
            _lastBitmap?.Dispose();
            _lastBitmap = null;
        }

        _frameReady.Dispose();
        if (_winrtDevicePtr != IntPtr.Zero)
        {
            Marshal.Release(_winrtDevicePtr);
        }

        _d3d.Dispose();
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
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

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

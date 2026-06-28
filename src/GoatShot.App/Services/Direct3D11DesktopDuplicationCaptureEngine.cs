using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class Direct3D11DesktopDuplicationCaptureEngine : ICaptureEngine
{
    private static readonly FeatureLevel[] PreferredFeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private static readonly FeatureLevel[] FallbackFeatureLevels =
    [
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    public string EngineName => "Direct3D11 desktop duplication";
    public bool IsProductionEngine => true;

    public static bool SupportsKind(CaptureKind kind)
    {
        return kind is CaptureKind.ActiveMonitor;
    }

    public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var target = ResolveTargetOutput(null);
            using var captured = CaptureMonitor(target.OutputDescription.DeviceName, includeCursor: false);
            return Task.FromResult(new ProviderHealth(
                true,
                $"{EngineName} captured a presented {captured.Bounds.Width}x{captured.Bounds.Height} frame from {target.OutputDescription.DeviceName}."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ProviderHealth(false, $"{EngineName} validation failed: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    public Task<CapturedBitmap?> CaptureAsync(CaptureEngineRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind is not CaptureKind.ActiveMonitor)
        {
            return Task.FromResult<CapturedBitmap?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CapturedBitmap?>(CaptureMonitor(request.MonitorName, request.IncludeCursor));
    }

    public CapturedBitmap CaptureActiveMonitor(bool includeCursor = true)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        return CaptureMonitor(screen.DeviceName, includeCursor);
    }

    private static CapturedBitmap CaptureMonitor(string? monitorName, bool includeCursor)
    {
        using var target = ResolveTargetOutput(monitorName);
        using var d3d = CreateD3D11Device(target.Adapter);
        using var duplication = CreateDuplication(target.Output, d3d.Device);
        var bounds = BoundsFromOutput(target.OutputDescription);

        var lastStatus = "no frame attempt completed";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            IDXGIResource? frameResource = null;
            var frameAcquired = false;
            try
            {
                var acquire = duplication.AcquireNextFrame(500, out var frameInfo, out frameResource);
                lastStatus = FormatFrameStatus(acquire, frameInfo);
                if (acquire.Failure || frameResource is null)
                {
                    Thread.Sleep(75);
                    continue;
                }

                frameAcquired = true;
                if (frameInfo.LastPresentTime == 0 && frameInfo.AccumulatedFrames == 0)
                {
                    Thread.Sleep(75);
                    continue;
                }

                using var sourceTexture = frameResource.QueryInterface<ID3D11Texture2D>();
                using var bitmap = ReadTextureToBitmap(d3d.Device, d3d.Context, sourceTexture);
                if (includeCursor)
                {
                    DrawCursorIfInside(bitmap, bounds);
                }

                var source = new CaptureSource
                {
                    MonitorName = target.OutputDescription.DeviceName
                };
                return new CapturedBitmap((Bitmap)bitmap.Clone(), CaptureKind.ActiveMonitor, bounds, source);
            }
            finally
            {
                frameResource?.Dispose();
                if (frameAcquired)
                {
                    duplication.ReleaseFrame();
                }
            }
        }

        throw new InvalidOperationException($"Direct3D11 desktop duplication did not produce a presented desktop frame. Last frame status: {lastStatus}");
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
            throw new InvalidOperationException($"Direct3D11 staging texture map failed: {mapResult}.");
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

    private static string FormatFrameStatus(Result acquire, OutduplFrameInfo frameInfo)
    {
        return $"result={acquire}, present={frameInfo.LastPresentTime}, accumulated={frameInfo.AccumulatedFrames}, mouse={frameInfo.LastMouseUpdateTime}, protected={frameInfo.ProtectedContentMaskedOut}";
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

    private static IDXGIOutputDuplication CreateDuplication(IDXGIOutput output, ID3D11Device device)
    {
        try
        {
            using var output5 = output.QueryInterface<IDXGIOutput5>();
            return output5.DuplicateOutput1(device, [Format.B8G8R8A8_UNorm]);
        }
        catch (SharpGenException)
        {
            using var output1 = output.QueryInterface<IDXGIOutput1>();
            return output1.DuplicateOutput(device);
        }
    }

    private static D3D11DeviceContext CreateD3D11Device(IDXGIAdapter adapter)
    {
        var result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            PreferredFeatureLevels,
            out var device,
            out var featureLevel,
            out var context);

        if (result.Failure)
        {
            result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                FallbackFeatureLevels,
                out device,
                out featureLevel,
                out context);
        }

        if (result.Failure || device is null || context is null)
        {
            device?.Dispose();
            context?.Dispose();
            throw new InvalidOperationException($"Direct3D11 device creation failed: {result}.");
        }

        return new D3D11DeviceContext(device, context, featureLevel);
    }

    private static DxgiOutputTarget ResolveTargetOutput(string? monitorName)
    {
        monitorName = string.IsNullOrWhiteSpace(monitorName)
            ? Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName
            : monitorName.Trim();

        var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        try
        {
            for (uint adapterIndex = 0; adapterIndex < 32; adapterIndex++)
            {
                var adapterResult = factory.EnumAdapters1(adapterIndex, out var adapter);
                if (adapterResult.Failure || adapter is null)
                {
                    break;
                }

                for (uint outputIndex = 0; outputIndex < 32; outputIndex++)
                {
                    var outputResult = adapter.EnumOutputs(outputIndex, out var output);
                    if (outputResult.Failure || output is null)
                    {
                        break;
                    }

                    var description = output.Description;
                    if (!description.AttachedToDesktop ||
                        !description.DeviceName.Equals(monitorName, StringComparison.OrdinalIgnoreCase))
                    {
                        output.Dispose();
                        continue;
                    }

                    factory.Dispose();
                    return new DxgiOutputTarget(adapter, output, description);
                }

                adapter.Dispose();
            }
        }
        catch
        {
            factory.Dispose();
            throw;
        }

        factory.Dispose();
        throw new InvalidOperationException($"No DXGI output matched monitor {monitorName}.");
    }

    private static CaptureBounds BoundsFromOutput(OutputDescription description)
    {
        var rect = description.DesktopCoordinates;
        return new CaptureBounds
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Right - rect.Left,
            Height = rect.Bottom - rect.Top
        };
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

    private sealed record DxgiOutputTarget(
        IDXGIAdapter1 Adapter,
        IDXGIOutput Output,
        OutputDescription OutputDescription) : IDisposable
    {
        public void Dispose()
        {
            Output.Dispose();
            Adapter.Dispose();
        }
    }

    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;

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
}

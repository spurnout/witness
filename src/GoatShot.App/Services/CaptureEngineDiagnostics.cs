using GoatShot.App.Models;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed record CaptureEngineDiagnosticSnapshot(
    string EngineName,
    bool IsProductionEngine,
    bool IsHealthy,
    string Message,
    string DefaultCapturePath,
    string Scope,
    bool DesktopSessionAvailable,
    string DesktopSessionStatus,
    IReadOnlyList<string> SupportedCaptureKinds,
    IReadOnlyList<string> UnsupportedCaptureKinds,
    string InteractiveRegionStatus,
    string FallbackStatus);

public static class CaptureEngineDiagnostics
{
    private static readonly CaptureKind[] PrimaryStillCaptureKinds =
    [
        CaptureKind.Region,
        CaptureKind.Fullscreen,
        CaptureKind.AllMonitors,
        CaptureKind.ActiveWindow,
        CaptureKind.ScrollingWindow,
        CaptureKind.ActiveMonitor,
        CaptureKind.FixedRegion
    ];

    public static CaptureEngineDiagnosticSnapshot Build(ICaptureEngine engine, ProviderHealth health)
    {
        var supported = PrimaryStillCaptureKinds
            .Where(kind => SupportsKind(engine, kind))
            .Select(FormatKind)
            .ToArray();
        var unsupported = PrimaryStillCaptureKinds
            .Where(kind => !SupportsKind(engine, kind))
            .Select(FormatKind)
            .ToArray();
        var desktopSessionAvailable = Environment.UserInteractive && Forms.SystemInformation.UserInteractive;

        return new CaptureEngineDiagnosticSnapshot(
            engine.EngineName,
            engine.IsProductionEngine,
            health.IsHealthy,
            health.Message,
            "Win32/GDI remains the default screenshot path unless a capture command opts into --engine production. DXGI desktop-duplication can be checked with diagnostics capture-engine --engine dxgi.",
            BuildScope(engine, supported),
            desktopSessionAvailable,
            desktopSessionAvailable
                ? "Windows desktop session is interactive; production capture commands can attempt live screen/window selection."
                : "No interactive Windows desktop session is available; production capture and interactive region selection are unsupported in this process.",
            supported,
            unsupported,
            BuildInteractiveRegionStatus(engine, desktopSessionAvailable),
            BuildFallbackStatus(engine));
    }

    public static bool SupportsKind(ICaptureEngine engine, CaptureKind kind)
    {
        return engine switch
        {
            WindowsGraphicsCaptureEngine => WindowsGraphicsCaptureEngine.SupportsKind(kind),
            Direct3D11DesktopDuplicationCaptureEngine => Direct3D11DesktopDuplicationCaptureEngine.SupportsKind(kind),
            _ => false
        };
    }

    private static string BuildScope(ICaptureEngine engine, IReadOnlyCollection<string> supported)
    {
        if (supported.Count == 0)
        {
            return $"{engine.EngineName} does not advertise any primary still-capture command scope.";
        }

        return string.Join(", ", supported) + " capture";
    }

    private static string BuildInteractiveRegionStatus(ICaptureEngine engine, bool desktopSessionAvailable)
    {
        if (!SupportsKind(engine, CaptureKind.Region))
        {
            return $"{engine.EngineName} does not support region capture; use the default GDI overlay path or Windows.Graphics.Capture.";
        }

        return desktopSessionAvailable
            ? "Interactive region selection is available through the desktop overlay before production region capture."
            : "Interactive region selection requires a Windows desktop session and is not available headlessly.";
    }

    private static string BuildFallbackStatus(ICaptureEngine engine)
    {
        return engine switch
        {
            WindowsGraphicsCaptureEngine => "If Windows.Graphics.Capture setup or frame delivery fails, use the default GDI screenshot path or the explicit DXGI active-monitor diagnostic where applicable.",
            Direct3D11DesktopDuplicationCaptureEngine => "DXGI desktop duplication is diagnostic-only for active-monitor capture; unsupported kinds should use default GDI or Windows.Graphics.Capture.",
            _ => "Use the default GDI screenshot path when this engine does not support a capture kind."
        };
    }

    private static string FormatKind(CaptureKind kind)
    {
        return kind switch
        {
            CaptureKind.AllMonitors => "all-monitors",
            CaptureKind.ActiveWindow => "active-window",
            CaptureKind.ScrollingWindow => "scrolling-window",
            CaptureKind.ActiveMonitor => "active-monitor",
            CaptureKind.FixedRegion => "fixed-region",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}

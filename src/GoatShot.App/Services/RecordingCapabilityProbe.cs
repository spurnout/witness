using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

[JsonConverter(typeof(JsonStringEnumConverter<ProductionVideoEncoderChoice>))]
public enum ProductionVideoEncoderChoice
{
    HardwareHevc,
    SoftwareHevc,
    HardwareH264,
    SoftwareH264,
    Unavailable
}

public sealed record ProductionVideoEncoderSelection(
    ProductionVideoEncoderChoice Choice,
    bool IsAvailable,
    bool IsHardwareAccelerated,
    string Codec,
    string Provider,
    string Summary)
{
    public string ChoiceLabel => Choice switch
    {
        ProductionVideoEncoderChoice.HardwareHevc => "media-foundation-hevc-hardware",
        ProductionVideoEncoderChoice.SoftwareHevc => "media-foundation-hevc-software",
        ProductionVideoEncoderChoice.HardwareH264 => "media-foundation-h264-hardware",
        ProductionVideoEncoderChoice.SoftwareH264 => "media-foundation-h264-software",
        _ => "unavailable"
    };
}

public sealed record EncoderProbeResult(bool Succeeded, int Count, string Error)
{
    public bool Available => Succeeded && Count > 0;

    public string FormatCount()
    {
        return Succeeded ? Count.ToString() : $"failed({Error})";
    }
}

public sealed record FfmpegEncoderProbe(
    string? Path,
    IReadOnlyList<string> HardwareEncoders,
    IReadOnlyList<string> SoftwareEncoders,
    string Status)
{
    public bool Available => !string.IsNullOrWhiteSpace(Path);
    public bool HasSoftwareH264 => SoftwareEncoders.Any(encoder => encoder.Equals("libx264", StringComparison.OrdinalIgnoreCase));
    public bool HasHardwareH264 => HardwareEncoders.Any(encoder => encoder.StartsWith("h264_", StringComparison.OrdinalIgnoreCase));
}

[JsonConverter(typeof(JsonStringEnumConverter<FfmpegVideoEncoderChoice>))]
public enum FfmpegVideoEncoderChoice
{
    MediaFoundationH264,
    NvidiaH264,
    IntelQuickSyncH264,
    AmdH264,
    Libx264,
    Unavailable
}

public sealed record FfmpegVideoEncoderSelection(
    FfmpegVideoEncoderChoice Choice,
    bool IsAvailable,
    bool IsHardwareAccelerated,
    string EncoderName,
    string Provider,
    int TargetBitrateKbps,
    string? RetryEncoderName,
    string Summary);

public sealed record Direct3D11DeviceProbe(bool Succeeded, int FeatureLevel, string Status)
{
    public string FeatureLevelLabel => FeatureLevel switch
    {
        D3DFeatureLevel11_1 => "11.1",
        D3DFeatureLevel11_0 => "11.0",
        D3DFeatureLevel10_1 => "10.1",
        D3DFeatureLevel10_0 => "10.0",
        _ => "unknown"
    };

    private const int D3DFeatureLevel11_1 = 0xB100;
    private const int D3DFeatureLevel11_0 = 0xB000;
    private const int D3DFeatureLevel10_1 = 0xA100;
    private const int D3DFeatureLevel10_0 = 0xA000;
}

public sealed record RecordingCapabilitySnapshot(
    bool WindowsGraphicsCaptureSupported,
    string WindowsGraphicsCaptureStatus,
    Direct3D11DeviceProbe Direct3D11Device,
    EncoderProbeResult H264HardwareEncoder,
    EncoderProbeResult H264InstalledEncoder,
    EncoderProbeResult HevcHardwareEncoder,
    EncoderProbeResult HevcInstalledEncoder,
    FfmpegEncoderProbe Ffmpeg)
{
    public bool MediaFoundationH264Available => H264HardwareEncoder.Available || H264InstalledEncoder.Available;
    public bool MediaFoundationHevcAvailable => HevcHardwareEncoder.Available || HevcInstalledEncoder.Available;

    public ProductionVideoEncoderSelection PreferredProductionVideoEncoder =>
        ProductionVideoEncoderSelector.Select(this);

    public ProductionVideoEncoderSelection SelectProductionVideoEncoder(RecordingSettings? settings) =>
        ProductionVideoEncoderSelector.Select(this, settings);

    public string EncoderStatus =>
        "Media Foundation encoder probe: " +
        $"H.264 hardware={H264HardwareEncoder.FormatCount()}, " +
        $"HEVC hardware={HevcHardwareEncoder.FormatCount()}, " +
        $"H.264 installed={H264InstalledEncoder.FormatCount()}, " +
        $"HEVC installed={HevcInstalledEncoder.FormatCount()}. " +
        $"{Ffmpeg.Status}";

    public string ProductionPrerequisiteSummary =>
        $"WGC={(WindowsGraphicsCaptureSupported ? "available" : "unavailable")}, " +
        $"D3D11={(Direct3D11Device.Succeeded ? $"available feature-level {Direct3D11Device.FeatureLevelLabel}" : "unavailable")}, " +
        $"Media Foundation H.264={(MediaFoundationH264Available ? $"available ({PreferredProductionVideoEncoder.ChoiceLabel})" : "unavailable")}, " +
        $"Media Foundation HEVC={(MediaFoundationHevcAvailable ? "available" : "unavailable")}, " +
        $"FFmpeg fallback={(Ffmpeg.Available ? "available" : "unavailable")}.";
}

public static class ProductionVideoEncoderSelector
{
    public static ProductionVideoEncoderSelection Select(RecordingCapabilitySnapshot capabilities) =>
        Select(capabilities, settings: null);

    public static ProductionVideoEncoderSelection Select(RecordingCapabilitySnapshot capabilities, RecordingSettings? settings)
    {
        if (settings?.PreferHevcEncoding == true)
        {
            if (capabilities.HevcHardwareEncoder.Available)
            {
                return new ProductionVideoEncoderSelection(
                    ProductionVideoEncoderChoice.HardwareHevc,
                    IsAvailable: true,
                    IsHardwareAccelerated: true,
                    Codec: "HEVC",
                    Provider: "Media Foundation",
                    Summary: $"Media Foundation HEVC hardware encoder selected ({capabilities.HevcHardwareEncoder.Count} hardware transform(s) available).");
            }

            if (capabilities.HevcInstalledEncoder.Available)
            {
                return new ProductionVideoEncoderSelection(
                    ProductionVideoEncoderChoice.SoftwareHevc,
                    IsAvailable: true,
                    IsHardwareAccelerated: false,
                    Codec: "HEVC",
                    Provider: "Media Foundation",
                    Summary: $"Media Foundation HEVC software encoder selected ({capabilities.HevcInstalledEncoder.Count} installed transform(s) available, no hardware transform reported).");
            }

            return new ProductionVideoEncoderSelection(
                ProductionVideoEncoderChoice.Unavailable,
                IsAvailable: false,
                IsHardwareAccelerated: false,
                Codec: "HEVC",
                Provider: "Media Foundation",
                Summary: "HEVC was requested, but no Media Foundation HEVC encoder transform is available. Disable HEVC preference to use the H.264 production path.");
        }

        if (capabilities.H264HardwareEncoder.Available)
        {
            return new ProductionVideoEncoderSelection(
                ProductionVideoEncoderChoice.HardwareH264,
                IsAvailable: true,
                IsHardwareAccelerated: true,
                Codec: "H.264",
                Provider: "Media Foundation",
                Summary: $"Media Foundation H.264 hardware encoder selected ({capabilities.H264HardwareEncoder.Count} hardware transform(s) available).");
        }

        if (capabilities.H264InstalledEncoder.Available)
        {
            return new ProductionVideoEncoderSelection(
                ProductionVideoEncoderChoice.SoftwareH264,
                IsAvailable: true,
                IsHardwareAccelerated: false,
                Codec: "H.264",
                Provider: "Media Foundation",
                Summary: $"Media Foundation H.264 software encoder selected ({capabilities.H264InstalledEncoder.Count} installed transform(s) available, no hardware transform reported).");
        }

        return new ProductionVideoEncoderSelection(
            ProductionVideoEncoderChoice.Unavailable,
            IsAvailable: false,
            IsHardwareAccelerated: false,
            Codec: "H.264",
            Provider: "Media Foundation",
            Summary: "No Media Foundation H.264 production encoder transform is available.");
    }
}

public static class FfmpegVideoEncoderSelector
{
    public static FfmpegVideoEncoderSelection Select(FfmpegEncoderProbe probe, NormalizedRecordingSettings settings)
    {
        settings ??= RecordingSettingsNormalizer.Normalize(new RecordingSettings());
        if (!probe.Available)
        {
            return Unavailable("FFmpeg is not available.");
        }

        var retry = probe.HasSoftwareH264 ? "libx264" : null;
        foreach (var candidate in HardwareCandidates)
        {
            if (probe.HardwareEncoders.Any(encoder => encoder.Equals(candidate.EncoderName, StringComparison.OrdinalIgnoreCase)))
            {
                var bitrate = settings.BitrateKbps > 0
                    ? settings.BitrateKbps
                    : EstimateHardwareBitrateKbps(settings);
                return new FfmpegVideoEncoderSelection(
                    candidate.Choice,
                    IsAvailable: true,
                    IsHardwareAccelerated: true,
                    candidate.EncoderName,
                    candidate.Provider,
                    bitrate,
                    retry?.Equals(candidate.EncoderName, StringComparison.OrdinalIgnoreCase) == true ? null : retry,
                    $"FFmpeg fallback video encoder selected: {candidate.EncoderName} ({candidate.Provider}) at {bitrate} kbps" +
                    (retry is null ? "." : $"; retry encoder: {retry}."));
            }
        }

        if (probe.HasSoftwareH264)
        {
            return new FfmpegVideoEncoderSelection(
                FfmpegVideoEncoderChoice.Libx264,
                IsAvailable: true,
                IsHardwareAccelerated: false,
                "libx264",
                "FFmpeg libx264 software encoder",
                settings.BitrateKbps,
                RetryEncoderName: null,
                "FFmpeg fallback video encoder selected: libx264 software encoder.");
        }

        return Unavailable("No supported FFmpeg H.264 encoder was reported.");
    }

    internal static int EstimateHardwareBitrateKbps(NormalizedRecordingSettings settings)
    {
        var baseBitrate = settings.QualityProfile switch
        {
            "Small" => 2_500,
            "High quality" => 12_000,
            "Archive" => 24_000,
            _ => 6_000
        };

        var width = settings.TargetWidth > 0 ? settings.TargetWidth : 1920;
        var height = settings.TargetHeight > 0 ? settings.TargetHeight : 1080;
        var pixels = Math.Max(1, width * height);
        var scale = pixels / (1920d * 1080d);
        var fpsScale = Math.Clamp(settings.FramesPerSecond / 30d, 0.5d, 2d);
        return Math.Clamp((int)Math.Round(baseBitrate * scale * fpsScale), 800, 120_000);
    }

    private static FfmpegVideoEncoderSelection Unavailable(string summary)
    {
        return new FfmpegVideoEncoderSelection(
            FfmpegVideoEncoderChoice.Unavailable,
            IsAvailable: false,
            IsHardwareAccelerated: false,
            string.Empty,
            string.Empty,
            0,
            RetryEncoderName: null,
            summary);
    }

    private static readonly IReadOnlyList<FfmpegHardwareEncoderCandidate> HardwareCandidates =
    [
        new(FfmpegVideoEncoderChoice.MediaFoundationH264, "h264_mf", "FFmpeg Media Foundation H.264 encoder"),
        new(FfmpegVideoEncoderChoice.NvidiaH264, "h264_nvenc", "NVIDIA NVENC H.264 encoder"),
        new(FfmpegVideoEncoderChoice.IntelQuickSyncH264, "h264_qsv", "Intel Quick Sync H.264 encoder"),
        new(FfmpegVideoEncoderChoice.AmdH264, "h264_amf", "AMD AMF H.264 encoder")
    ];

    private sealed record FfmpegHardwareEncoderCandidate(
        FfmpegVideoEncoderChoice Choice,
        string EncoderName,
        string Provider);
}

public static class RecordingCapabilityProbe
{
    private const int MfVersion = 0x00020070;
    private const int D3D11SdkVersion = 7;
    private const int D3DDriverTypeHardware = 1;
    private const int D3D11CreateDeviceBgraSupport = 0x00000020;
    private const int EInvalidArg = unchecked((int)0x80070057);
    private const int D3DFeatureLevel11_1 = 0xB100;
    private const int D3DFeatureLevel11_0 = 0xB000;
    private const int D3DFeatureLevel10_1 = 0xA100;
    private const int D3DFeatureLevel10_0 = 0xA000;
    private const int MftEnumFlagSyncMft = 0x00000001;
    private const int MftEnumFlagAsyncMft = 0x00000002;
    private const int MftEnumFlagHardware = 0x00000004;
    private const int MftEnumFlagLocalMft = 0x00000010;
    private const int MftEnumFlagSortAndFilter = 0x00000040;
    private const int MftEnumFlagAll = MftEnumFlagSyncMft | MftEnumFlagAsyncMft | MftEnumFlagHardware | MftEnumFlagLocalMft | MftEnumFlagSortAndFilter;

    private static readonly Guid MftCategoryVideoEncoder = new("f79eac7d-e545-4387-bdee-d647d7bde42a");
    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatHevc = new("43564548-0000-0010-8000-00aa00389b71");

    private static readonly string[] CommonHardwareFfmpegEncoders =
    [
        "h264_nvenc",
        "hevc_nvenc",
        "h264_qsv",
        "hevc_qsv",
        "h264_amf",
        "hevc_amf",
        "h264_mf",
        "hevc_mf"
    ];

    private static readonly object ProbeGate = new();
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(5);
    private static RecordingCapabilitySnapshot? _cachedSnapshot;
    private static DateTimeOffset _cachedSnapshotAt;

    public static RecordingCapabilitySnapshot Probe(bool refresh = false)
    {
        // Probing is expensive (four MFT enumerations, a D3D11 device creation, and an
        // `ffmpeg -encoders` child process) and runs on recording start and recording-panel
        // edits, so serve a cached snapshot unless it is stale or a refresh is requested.
        lock (ProbeGate)
        {
            if (!refresh &&
                _cachedSnapshot is not null &&
                DateTimeOffset.UtcNow - _cachedSnapshotAt < ProbeCacheTtl)
            {
                return _cachedSnapshot;
            }
        }

        var snapshot = new RecordingCapabilitySnapshot(
            ProbeWindowsGraphicsCapture(out var wgcStatus),
            wgcStatus,
            ProbeDirect3D11Device(),
            ProbeMediaFoundationEncoder(MfVideoFormatH264, hardwareOnly: true),
            ProbeMediaFoundationEncoder(MfVideoFormatH264, hardwareOnly: false),
            ProbeMediaFoundationEncoder(MfVideoFormatHevc, hardwareOnly: true),
            ProbeMediaFoundationEncoder(MfVideoFormatHevc, hardwareOnly: false),
            ProbeFfmpeg());
        lock (ProbeGate)
        {
            _cachedSnapshot = snapshot;
            _cachedSnapshotAt = DateTimeOffset.UtcNow;
        }

        return snapshot;
    }

    public static string FormatHResult(int hr)
    {
        return $"0x{unchecked((uint)hr):X8}";
    }

    private static bool ProbeWindowsGraphicsCapture(out string status)
    {
        try
        {
            var supported = global::Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
            status = supported
                ? "Windows.Graphics.Capture API probe: supported on this machine."
                : "Windows.Graphics.Capture API probe: not supported on this machine.";
            return supported;
        }
        catch (Exception ex)
        {
            status = $"Windows.Graphics.Capture API probe failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static Direct3D11DeviceProbe ProbeDirect3D11Device()
    {
        var featureLevels = new[]
        {
            D3DFeatureLevel11_1,
            D3DFeatureLevel11_0,
            D3DFeatureLevel10_1,
            D3DFeatureLevel10_0
        };
        var result = TryCreateD3D11Device(featureLevels);
        if (result.HResult == EInvalidArg)
        {
            result = TryCreateD3D11Device(featureLevels.Skip(1).ToArray());
        }

        if (result.HResult < 0)
        {
            return new Direct3D11DeviceProbe(
                false,
                0,
                $"Direct3D11 device probe failed: HRESULT {FormatHResult(result.HResult)}.");
        }

        return new Direct3D11DeviceProbe(
            true,
            result.FeatureLevel,
            $"Direct3D11 device probe: hardware device created with BGRA support at feature level {FormatFeatureLevel(result.FeatureLevel)}.");
    }

    private static (int HResult, int FeatureLevel) TryCreateD3D11Device(int[] featureLevels)
    {
        IntPtr device = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        try
        {
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3DDriverTypeHardware,
                IntPtr.Zero,
                D3D11CreateDeviceBgraSupport,
                featureLevels,
                featureLevels.Length,
                D3D11SdkVersion,
                out device,
                out var featureLevel,
                out context);
            return (hr, featureLevel);
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                _ = Marshal.Release(context);
            }

            if (device != IntPtr.Zero)
            {
                _ = Marshal.Release(device);
            }
        }
    }

    private static string FormatFeatureLevel(int featureLevel)
    {
        return featureLevel switch
        {
            D3DFeatureLevel11_1 => "11.1",
            D3DFeatureLevel11_0 => "11.0",
            D3DFeatureLevel10_1 => "10.1",
            D3DFeatureLevel10_0 => "10.0",
            _ => $"0x{featureLevel:X}"
        };
    }

    private static EncoderProbeResult ProbeMediaFoundationEncoder(Guid videoSubtype, bool hardwareOnly)
    {
        var startup = MFStartup(MfVersion, 0);
        if (startup < 0)
        {
            return new EncoderProbeResult(false, 0, $"MFStartup {FormatHResult(startup)}");
        }

        try
        {
            return EnumerateEncoderTransforms(videoSubtype, hardwareOnly);
        }
        catch (Exception ex)
        {
            return new EncoderProbeResult(false, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _ = MFShutdown();
        }
    }

    private static EncoderProbeResult EnumerateEncoderTransforms(Guid videoSubtype, bool hardwareOnly)
    {
        var outputType = new MftRegisterTypeInfo
        {
            MajorType = MfMediaTypeVideo,
            Subtype = videoSubtype
        };
        var category = MftCategoryVideoEncoder;
        var flags = hardwareOnly ? MftEnumFlagHardware | MftEnumFlagSortAndFilter : MftEnumFlagAll;

        var hr = MFTEnumEx(ref category, flags, IntPtr.Zero, ref outputType, out var activates, out var count);
        if (hr < 0)
        {
            return new EncoderProbeResult(false, 0, FormatHResult(hr));
        }

        ReleaseActivateArray(activates, count);
        return new EncoderProbeResult(true, count, string.Empty);
    }

    private static FfmpegEncoderProbe ProbeFfmpeg()
    {
        var configuredResolution = BrandEnvironment.Resolve("FFMPEG_PATH");
        var configured = configuredResolution.Value;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.ExpandEnvironmentVariables(configured);
            if (!File.Exists(configured))
            {
                return new FfmpegEncoderProbe(null, [], [], $"FFmpeg fallback encoder configured but missing: {configured}");
            }
        }

        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            return new FfmpegEncoderProbe(null, [], [], "FFmpeg fallback encoder probe: ffmpeg.exe was not found on PATH and RECEIPTS_FFMPEG_PATH is not set (GOATSHOT_FFMPEG_PATH remains a compatibility alias).");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-hide_banner",
                    "-encoders"
                }
            });

            if (process is null)
            {
                return new FfmpegEncoderProbe(null, [], [], $"FFmpeg fallback encoder probe: could not start {ffmpeg}.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return new FfmpegEncoderProbe(ffmpeg, [], [], $"FFmpeg fallback encoder probe timed out: {ffmpeg}.");
            }

            var encoders = stdout.GetAwaiter().GetResult() + Environment.NewLine + stderr.GetAwaiter().GetResult();
            var hardware = CommonHardwareFfmpegEncoders
                .Where(encoder => encoders.Contains(encoder, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var software = new[]
            {
                "libx264",
                "libx265"
            }.Where(encoder => encoders.Contains(encoder, StringComparison.OrdinalIgnoreCase)).ToArray();

            var hardwareText = hardware.Length == 0
                ? "no common hardware H.264/HEVC encoders reported"
                : $"hardware encoders reported: {string.Join(", ", hardware)}";
            var softwareText = software.Length == 0
                ? "software x264/x265 encoders not reported"
                : $"software encoders reported: {string.Join(", ", software)}";

            return new FfmpegEncoderProbe(
                ffmpeg,
                hardware,
                software,
                $"FFmpeg fallback encoder probe: {ffmpeg}; {hardwareText}; {softwareText}.");
        }
        catch (Exception ex)
        {
            return new FfmpegEncoderProbe(null, [], [], $"FFmpeg fallback encoder probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReleaseActivateArray(IntPtr activates, int count)
    {
        if (activates == IntPtr.Zero)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var activate = Marshal.ReadIntPtr(activates, index * IntPtr.Size);
            if (activate != IntPtr.Zero)
            {
                _ = Marshal.Release(activate);
            }
        }

        Marshal.FreeCoTaskMem(activates);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftRegisterTypeInfo
    {
        public Guid MajorType;
        public Guid Subtype;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(
        ref Guid guidCategory,
        int flags,
        IntPtr inputType,
        ref MftRegisterTypeInfo outputType,
        out IntPtr activates,
        out int count);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        int flags,
        int[] featureLevels,
        int featureLevelsCount,
        int sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);
}

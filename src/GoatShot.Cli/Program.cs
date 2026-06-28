using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Forms = System.Windows.Forms;

namespace GoatShot.Cli;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (Console.IsInputRedirected && IsBrowserNativeHostLaunch(args))
        {
            try
            {
                using var services = AppServices.Create();
                return await RunBrowserNativeHostAsync(services);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"goatshot: {ex.Message}");
                return 1;
            }
        }

        if (args.Length == 0 || Has(args, "--help") || Has(args, "-h"))
        {
            if (args.Length == 0 && Console.IsInputRedirected)
            {
                try
                {
                    using var services = AppServices.Create();
                    return await RunBrowserNativeHostAsync(services);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"goatshot: {ex.Message}");
                    return 1;
                }
            }

            PrintHelp();
            return 0;
        }

        try
        {
            using var services = AppServices.Create();
            return args[0].ToLowerInvariant() switch
            {
                "capture" => await CaptureAsync(services, args.Skip(1).ToArray()),
                "record" => await RecordAsync(services, args.Skip(1).ToArray()),
                "video" => await VideoAsync(services, args.Skip(1).ToArray()),
                "image" => await ImageAsync(services, args.Skip(1).ToArray()),
                "import" => await ImportAsync(services, args.Skip(1).ToArray()),
                "share" or "upload" => await ShareAsync(services, args.Skip(1).ToArray()),
                "queue" or "upload-queue" => await UploadQueueAsync(services, args.Skip(1).ToArray()),
                "providers" or "provider-diagnostics" => ProviderDiagnostics(services, args.Skip(1).ToArray()),
                "oauth" => await OAuthAsync(services, args.Skip(1).ToArray()),
                "share-history" or "upload-history" => ShareHistory(services, args.Skip(1).ToArray()),
                "metadata" => await MetadataAsync(services, args.Skip(1).ToArray()),
                "inspect" or "file-info" => await InspectAsync(services, args.Skip(1).ToArray()),
                "hash" or "checksum" => await HashAsync(services, args.Skip(1).ToArray()),
                "clipboard" => await ClipboardAsync(services, args.Skip(1).ToArray()),
                "color" => Color(services, args.Skip(1).ToArray()),
                "paths" => Paths(services),
                "qr" or "barcode" => await BarcodeAsync(services, args.Skip(1).ToArray()),
                "search" => Search(services, args.Skip(1).ToArray()),
                "workflows" or "workflow" => await WorkflowsAsync(services, args.Skip(1).ToArray()),
                "plugins" or "plugin" or "local-plugins" => await PluginsAsync(services, args.Skip(1).ToArray()),
                "companion-portal" or "portal" or "companion" => await CompanionPortalAsync(services, args.Skip(1).ToArray()),
                "admin-policy" or "managed-policy" or "policy" => AdminPolicy(services, args.Skip(1).ToArray()),
                "browser-extension" or "browser-capture" or "extension" => await BrowserExtensionAsync(services, args.Skip(1).ToArray()),
                "print-import" or "virtual-printer" or "printer-import" => await PrintImportAsync(services, args.Skip(1).ToArray()),
                "diagnostics" or "diagnostic" => await DiagnosticsAsync(services, args.Skip(1).ToArray()),
                "manual-validation" or "manual-validation-harness" or "validation" => await ManualValidationAsync(services, args.Skip(1).ToArray()),
                "bug-report" or "bugreport" => await BugReportAsync(services, args.Skip(1).ToArray()),
                "doc-packet" or "documentation-packet" or "docs-packet" => await DocumentationPacketAsync(services, args.Skip(1).ToArray()),
                "transcribe" or "transcript" => await TranscribeAsync(services, args.Skip(1).ToArray()),
                "video-intel" or "video-summary" or "video-intelligence" => await VideoIntelligenceAsync(services, args.Skip(1).ToArray()),
                "redact-ocr" or "ocr-redact" => await RedactOcrAsync(services, args.Skip(1).ToArray()),
                "scan-text" => await ScanTextAsync(args.Skip(1).ToArray()),
                "ocr" => await OcrAsync(services, args.Skip(1).ToArray()),
                "ai-models" or "ai-capabilities" => await AiModelsAsync(services, args.Skip(1).ToArray()),
                "ai-edit" => await AiEditAsync(services, args.Skip(1).ToArray()),
                "ai-analyze" or "ai-explain" => await AiAnalyzeAsync(services, args.Skip(1).ToArray()),
                "ai-history" => await AiHistoryAsync(services, args.Skip(1).ToArray()),
                _ => Unknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"goatshot: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CaptureAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("capture requires a mode: fullscreen, all-monitors, monitor, window, fixed, region, scrolling, android, android-preview, or android-video.");
            return 2;
        }

        if (args[0].Equals("android-preview", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("android-live-preview", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("adb-preview", StringComparison.OrdinalIgnoreCase))
        {
            return await CaptureAndroidPreviewAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("android-video", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("android-recording", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("adb-screenrecord", StringComparison.OrdinalIgnoreCase))
        {
            return await CaptureAndroidVideoAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("android", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("adb", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("android-device", StringComparison.OrdinalIgnoreCase))
        {
            return await CaptureAndroidAsync(services, args.Skip(1).ToArray());
        }

        await ApplyOptionalCaptureStartDelayAsync(args);

        using var captured = args[0].ToLowerInvariant() switch
        {
            "fullscreen" => await CaptureFullScreenAsync(services, args),
            "all-monitors" or "allmonitors" => await CaptureAllMonitorsAsync(services, args),
            "monitor" or "active-monitor" => await CaptureActiveMonitorAsync(services, args),
            "window" or "active-window" => await CaptureActiveWindowAsync(services, args),
            "scroll" or "scrolling" or "scrolling-window" => await CaptureScrollingAsync(services, args),
            "fixed" or "preset" => await CaptureFixedAsync(services, args),
            "region" => await CaptureRegionAsync(services, args),
            _ => throw new ArgumentException($"Unknown capture mode: {args[0]}")
        };

        if (captured is null)
        {
            Console.Error.WriteLine("capture canceled");
            return 1;
        }

        var output = Value(args, "--output") ?? Value(args, "-o");
        var hotkeyProfile = HotkeyProfileNames.Normalize(
            Value(args, "--profile") ??
            Value(args, "--hotkey-profile") ??
            Value(args, "--workflow-profile"));
        CaptureItem? item = null;
        string path;

        if (string.IsNullOrWhiteSpace(output))
        {
            item = await services.WorkspaceStore.SaveCaptureAsync(
                captured,
                privateCapture: false,
                hotkeyProfile);
            path = item.FilePath;
        }
        else
        {
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            captured.Bitmap.Save(path, ImageFormat.Png);

            if (Has(args, "--workspace"))
            {
                item = await services.WorkspaceStore.AddImageFileAsync(
                    path,
                    captured.Kind,
                    string.IsNullOrWhiteSpace(hotkeyProfile)
                        ? "Imported from goatshot CLI capture command."
                        : $"Imported from goatshot CLI capture command. Hotkey/workflow profile: {hotkeyProfile}.",
                    captured.Source,
                    hotkeyProfile);
            }
        }

        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetImage(ImageInterop.ToBitmapSource(captured.Bitmap));
        }

        Console.WriteLine(path);
        if (item is not null)
        {
            Console.Error.WriteLine($"workspace-id={item.Id}");
            if (!string.IsNullOrWhiteSpace(item.HotkeyProfile))
            {
                Console.Error.WriteLine($"hotkey-profile={item.HotkeyProfile}");
            }
        }

        return 0;
    }

    private static async Task<int> CaptureAndroidAsync(AppServices services, string[] args)
    {
        if (!ManagedPolicyService.IsAndroidCaptureAllowed(services.Settings, out var androidPolicyReason))
        {
            return PrintPolicyBlocked(services, "capture android", androidPolicyReason, Has(args, "--json"));
        }

        if (args.Length > 0 &&
            (args[0].Equals("video", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("recording", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("screenrecord", StringComparison.OrdinalIgnoreCase)))
        {
            return await CaptureAndroidVideoAsync(services, args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("preview", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("live-preview", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("stream-plan", StringComparison.OrdinalIgnoreCase)))
        {
            return await CaptureAndroidPreviewAsync(services, args.Skip(1).ToArray());
        }

        var service = new AndroidAdbCaptureService(services.Paths, services.WorkspaceStore);
        var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
        {
            AdbPath = Value(args, "--adb") ?? Value(args, "--adb-path"),
            DeviceSerial = Value(args, "--device") ?? Value(args, "--serial"),
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            AddToWorkspace = Has(args, "--workspace")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else if (result.Succeeded)
        {
            Console.WriteLine(result.OutputPath);
            Console.Error.WriteLine(result.Message);
            if (result.Item is not null)
            {
                Console.Error.WriteLine($"workspace-id={result.Item.Id}");
            }
        }
        else
        {
            Console.Error.WriteLine(result.Message);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> CaptureAndroidPreviewAsync(AppServices services, string[] args)
    {
        if (!ManagedPolicyService.IsAndroidCaptureAllowed(services.Settings, out var androidPolicyReason))
        {
            return PrintPolicyBlocked(services, "capture android-preview", androidPolicyReason, Has(args, "--json"));
        }

        var service = new AndroidAdbCaptureService(services.Paths, services.WorkspaceStore);
        var execute = Has(args, "--execute");
        var adbPath = Value(args, "--adb") ?? Value(args, "--adb-path");
        var deviceSerial = Value(args, "--device") ?? Value(args, "--serial");
        var strategy = ParseAndroidLivePreviewStrategy(args);
        var durationSeconds = ParseInt(args, "--duration", AndroidAdbCaptureService.DefaultLivePreviewDurationSeconds);
        var frameIntervalMs = ParseInt(
            args,
            "--frame-interval-ms",
            ParseInt(args, "--interval-ms", AndroidAdbCaptureService.DefaultLivePreviewFrameIntervalMs));
        var maxBytes = ParseLong(args, "--max-bytes", AndroidAdbCaptureService.DefaultLivePreviewMaxBytes);
        var outputPath = Value(args, "--output") ?? Value(args, "-o");
        var outputDirectory = Value(args, "--output-dir") ?? outputPath;
        var contactSheetPath = Value(args, "--contact-sheet-output") ?? Value(args, "--contact-sheet-path");
        var generateContactSheet = Has(args, "--contact-sheet") || !string.IsNullOrWhiteSpace(contactSheetPath);
        var contactSheetMaxFrames = ParseInt(
            args,
            "--contact-sheet-max-frames",
            AndroidAdbCaptureService.DefaultLivePreviewContactSheetMaxFrames);

        if (execute)
        {
            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = adbPath,
                DeviceSerial = deviceSerial,
                Strategy = strategy,
                DurationSeconds = durationSeconds,
                FrameIntervalMs = frameIntervalMs,
                MaxBytes = maxBytes,
                OutputDirectory = outputDirectory,
                GenerateContactSheet = generateContactSheet,
                ContactSheetPath = contactSheetPath,
                ContactSheetMaxFrames = contactSheetMaxFrames,
                SafeContentConfirmed =
                    Has(args, "--safe-content-confirmed") ||
                    Has(args, "--confirm-safe-content") ||
                    Has(args, "--confirm-android-safe-content")
            });

            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine("GoatShot Android live-preview execution");
                Console.WriteLine($"status={result.Status}");
                Console.WriteLine($"succeeded={result.Succeeded}");
                Console.WriteLine($"executed={result.Executed}");
                Console.WriteLine($"strategy={result.Strategy}");
                Console.WriteLine($"requested-strategy={result.RequestedStrategy}");
                Console.WriteLine($"duration-seconds={result.DurationSeconds}");
                Console.WriteLine($"frame-interval-ms={result.FrameIntervalMs}");
                Console.WriteLine($"max-bytes={result.MaxBytes}");
                Console.WriteLine($"timeout-seconds={result.TimeoutSeconds}");
                Console.WriteLine($"bytes-captured={result.BytesCaptured}");
                Console.WriteLine($"frame-count={result.FramePaths.Count}");
                Console.WriteLine($"safe-content-confirmed={result.SafeContentConfirmed}");
                Console.WriteLine($"cleanup-performed={result.CleanupPerformed}");
                if (result.Summary is not null)
                {
                    Console.WriteLine($"mode={result.Summary.Mode}");
                    Console.WriteLine($"timeout-status={result.Summary.TimeoutStatus}");
                    Console.WriteLine($"byte-cap-status={result.Summary.ByteCapStatus}");
                    Console.WriteLine($"cleanup-status={result.Summary.CleanupStatus}");
                    Console.WriteLine($"remux-status={result.Summary.RemuxStatus}");
                }

                if (!string.IsNullOrWhiteSpace(result.OutputDirectory))
                {
                    Console.WriteLine($"output-directory={result.OutputDirectory}");
                }

                if (!string.IsNullOrWhiteSpace(result.ManifestPath))
                {
                    Console.WriteLine($"manifest={result.ManifestPath}");
                }

                if (!string.IsNullOrWhiteSpace(result.StreamPath))
                {
                    Console.WriteLine($"stream={result.StreamPath}");
                }

                if (!string.IsNullOrWhiteSpace(result.RemuxedVideoPath))
                {
                    Console.WriteLine($"remuxed-video={result.RemuxedVideoPath}");
                }

                if (result.Strategy == AndroidAdbLivePreviewStrategy.H264Stream)
                {
                    Console.WriteLine($"remux-attempted={result.RemuxAttempted}");
                    Console.WriteLine($"remux-succeeded={result.RemuxSucceeded}");
                    if (!string.IsNullOrWhiteSpace(result.RemuxMessage))
                    {
                        Console.WriteLine($"remux-message={result.RemuxMessage}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.ContactSheetPath))
                {
                    Console.WriteLine($"contact-sheet={result.ContactSheetPath}");
                    Console.WriteLine($"contact-sheet-frame-count={result.ContactSheetFrameCount}");
                    Console.WriteLine($"contact-sheet-max-frames={result.ContactSheetMaxFrames}");
                }

                foreach (var framePath in result.FramePaths)
                {
                    Console.WriteLine($"frame={framePath}");
                }

                if (result.Device is not null)
                {
                    Console.WriteLine($"device={result.Device.DisplayLabel}");
                }

                Console.WriteLine(result.Message);
                foreach (var note in result.PrivacyNotes)
                {
                    Console.WriteLine($"privacy-note={note}");
                }

                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"warning={warning}");
                }

                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"issue={issue}");
                }
            }

            return result.Succeeded
                ? 0
                : result.Status == AndroidAdbStatus.InvalidRequest ? 2 : 1;
        }

        var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
        {
            AdbPath = adbPath,
            DeviceSerial = deviceSerial,
            Strategy = strategy,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            OutputPath = outputPath
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine("GoatShot Android live-preview dry-run plan");
            Console.WriteLine($"status={plan.Status}");
            Console.WriteLine($"ready={plan.Succeeded}");
            Console.WriteLine($"strategy={plan.Strategy}");
            Console.WriteLine($"requested-strategy={plan.RequestedStrategy}");
            Console.WriteLine($"duration-seconds={plan.DurationSeconds}");
            Console.WriteLine($"frame-interval-ms={plan.FrameIntervalMs}");
            Console.WriteLine($"max-bytes={plan.MaxBytes}");
            Console.WriteLine($"timeout-seconds={plan.TimeoutSeconds}");
            if (plan.EstimatedMaxFrames is not null)
            {
                Console.WriteLine($"estimated-max-frames={plan.EstimatedMaxFrames}");
            }

            if (!string.IsNullOrWhiteSpace(plan.OutputHint))
            {
                Console.WriteLine($"output-hint={plan.OutputHint}");
            }

            if (plan.Device is not null)
            {
                Console.WriteLine($"device={plan.Device.DisplayLabel}");
            }

            Console.WriteLine(plan.Message);
            foreach (var command in plan.PlannedCommands)
            {
                Console.WriteLine($"planned-command={command.Name} {command.Tool} {string.Join(' ', command.Arguments)}");
                Console.WriteLine($"planned-command-captures-device-content={command.CapturesDeviceContent}");
                Console.WriteLine(command.Description);
            }

            foreach (var note in plan.PrivacyNotes)
            {
                Console.WriteLine($"privacy-note={note}");
            }

            foreach (var warning in plan.Warnings)
            {
                Console.WriteLine($"warning={warning}");
            }

            foreach (var issue in plan.Issues)
            {
                Console.WriteLine($"issue={issue}");
            }

            if (!string.IsNullOrWhiteSpace(plan.CleanupPlan))
            {
                Console.WriteLine($"cleanup={plan.CleanupPlan}");
            }

            if (!string.IsNullOrWhiteSpace(plan.DisconnectBehavior))
            {
                Console.WriteLine($"disconnect={plan.DisconnectBehavior}");
            }
        }

        return plan.Succeeded
            ? 0
            : plan.Status == AndroidAdbStatus.InvalidRequest ? 2 : 1;
    }

    private static async Task<int> CaptureAndroidVideoAsync(AppServices services, string[] args)
    {
        if (!ManagedPolicyService.IsAndroidCaptureAllowed(services.Settings, out var androidPolicyReason))
        {
            return PrintPolicyBlocked(services, "capture android-video", androidPolicyReason, Has(args, "--json"));
        }

        var service = new AndroidAdbCaptureService(services.Paths, services.WorkspaceStore);
        var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
        {
            AdbPath = Value(args, "--adb") ?? Value(args, "--adb-path"),
            DeviceSerial = Value(args, "--device") ?? Value(args, "--serial"),
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            AddToWorkspace = Has(args, "--workspace"),
            DurationSeconds = ParseInt(args, "--duration", AndroidAdbCaptureService.DefaultScreenrecordDurationSeconds),
            RemoteDirectory = Value(args, "--remote-dir") ?? Value(args, "--remote-directory")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else if (result.Succeeded)
        {
            Console.WriteLine(result.OutputPath);
            Console.Error.WriteLine(result.Message);
            if (result.Item is not null)
            {
                Console.Error.WriteLine($"workspace-id={result.Item.Id}");
            }
        }
        else
        {
            Console.Error.WriteLine(result.Message);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<CapturedBitmap> CaptureActiveMonitorAsync(AppServices services, string[] args)
    {
        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsDefaultCaptureEngine(engine))
        {
            return await services.Screenshots.CaptureActiveMonitorAsync();
        }

        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            return await CaptureWithWindowsGraphicsCaptureAsync(
                services,
                CaptureKind.ActiveMonitor,
                monitorName: Value(args, "--monitor") ?? Value(args, "--monitor-name"));
        }

        if (IsDxgiCaptureEngine(engine))
        {
            if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
            {
                throw new NotSupportedException("DXGI desktop-duplication capture requires a Windows desktop session. Headless DXGI capture is not supported.");
            }

            var captureEngine = new Direct3D11DesktopDuplicationCaptureEngine();
            var captured = await captureEngine.CaptureAsync(
                new CaptureEngineRequest(
                    CaptureKind.ActiveMonitor,
                    IncludeCursor: services.Settings.IncludeCursor,
                    MonitorName: Value(args, "--monitor") ?? Value(args, "--monitor-name")),
                CancellationToken.None);
            return captured ?? throw new InvalidOperationException("DXGI desktop-duplication capture did not return an active-monitor bitmap.");
        }

        throw new ArgumentException($"Unknown capture engine: {engine}. Use default, gdi, fallback, production, wgc, windows-graphics-capture, dxgi, desktop-duplication, direct3d, or d3d11.");
    }

    private static async Task<CapturedBitmap> CaptureActiveWindowAsync(AppServices services, string[] args)
    {
        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsDefaultCaptureEngine(engine))
        {
            return await services.Screenshots.CaptureActiveWindowAsync();
        }

        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            return await CaptureWithWindowsGraphicsCaptureAsync(services, CaptureKind.ActiveWindow);
        }

        throw new ArgumentException($"Unknown capture engine: {engine}. Use default, gdi, fallback, production, wgc, or windows-graphics-capture for window capture.");
    }

    private static async Task<CapturedBitmap> CaptureFullScreenAsync(AppServices services, string[] args)
    {
        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsDefaultCaptureEngine(engine))
        {
            return await services.Screenshots.CaptureFullScreenAsync();
        }

        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            return await CaptureWithWindowsGraphicsCaptureAsync(services, CaptureKind.Fullscreen);
        }

        throw new ArgumentException($"Unknown or unsupported capture engine for fullscreen capture: {engine}. Use default, gdi, fallback, production, wgc, or windows-graphics-capture.");
    }

    private static async Task<CapturedBitmap> CaptureAllMonitorsAsync(AppServices services, string[] args)
    {
        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsDefaultCaptureEngine(engine))
        {
            return await services.Screenshots.CaptureAllMonitorsAsync();
        }

        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            return await CaptureWithWindowsGraphicsCaptureAsync(services, CaptureKind.AllMonitors);
        }

        throw new ArgumentException($"Unknown or unsupported capture engine for all-monitors capture: {engine}. Use default, gdi, fallback, production, wgc, or windows-graphics-capture.");
    }

    private static async Task<CapturedBitmap> CaptureFixedAsync(AppServices services, string[] args)
    {
        var (width, height) = ParseCaptureSize(args);
        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            return await CaptureWithWindowsGraphicsCaptureAsync(
                services,
                CaptureKind.FixedRegion,
                services.Screenshots.ResolveFixedRegionAtCursorBounds(width, height));
        }

        if (!IsDefaultCaptureEngine(engine))
        {
            throw new ArgumentException($"Unknown or unsupported capture engine for fixed capture: {engine}. Use default, gdi, fallback, production, wgc, or windows-graphics-capture.");
        }

        return await services.Screenshots.CaptureFixedRegionAtCursorAsync(width, height);
    }

    private static async Task<CapturedBitmap?> CaptureRegionAsync(AppServices services, string[] args)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException("Interactive region capture requires a Windows desktop session. Headless capture region is not supported.");
        }

        var engine = Value(args, "--engine") ?? Value(args, "--capture-engine");
        if (IsDefaultCaptureEngine(engine))
        {
            return await services.Screenshots.CaptureRegionAsync();
        }

        if (IsWindowsGraphicsCaptureEngine(engine))
        {
            var bounds = await services.Screenshots.SelectRegionBoundsAsync();
            return bounds is null
                ? null
                : await CaptureWithWindowsGraphicsCaptureAsync(services, CaptureKind.Region, bounds);
        }

        throw new ArgumentException($"Unknown or unsupported capture engine for region capture: {engine}. Use default, gdi, fallback, production, wgc, or windows-graphics-capture.");
    }

    private static async Task<CapturedBitmap> CaptureWithWindowsGraphicsCaptureAsync(
        AppServices services,
        CaptureKind kind,
        CaptureBounds? bounds = null,
        string? monitorName = null)
    {
        if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
        {
            throw new NotSupportedException($"Production {kind} capture requires a Windows desktop session. Headless production capture is not supported.");
        }

        var captureEngine = new WindowsGraphicsCaptureEngine();
        var captured = await captureEngine.CaptureAsync(
            new CaptureEngineRequest(
                kind,
                bounds,
                IncludeCursor: services.Settings.IncludeCursor,
                MonitorName: monitorName),
            CancellationToken.None);
        return captured ?? throw new InvalidOperationException($"Windows.Graphics.Capture production capture did not return a {kind} bitmap.");
    }

    private static bool IsDefaultCaptureEngine(string? engine)
    {
        return string.IsNullOrWhiteSpace(engine) ||
            engine.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("gdi", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("fallback", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsGraphicsCaptureEngine(string? engine)
    {
        return engine is not null && (
            engine.Equals("production", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("wgc", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("windows-graphics-capture", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDxgiCaptureEngine(string? engine)
    {
        return engine is not null && (
            engine.Equals("dxgi", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("desktop-duplication", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("direct3d", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("d3d11", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<CapturedBitmap> CaptureScrollingAsync(AppServices services, string[] args)
    {
        var delayText = Value(args, "--delay");
        var delaySeconds = double.TryParse(delayText, CultureInfo.InvariantCulture, out var parsedDelay) ? parsedDelay : 2d;
        if (delaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 0d, 10d)));
        }

        var frameText = Value(args, "--max-frames");
        var frames = int.TryParse(frameText, out var parsedFrames) ? parsedFrames : 6;
        var wheelText = Value(args, "--wheel-clicks");
        var wheelClicks = int.TryParse(wheelText, out var parsedWheelClicks) ? parsedWheelClicks : 5;
        var options = ParseScrollingOptions(args);
        options.MaxFrames = frames;
        options.WheelClicksPerFrame = wheelClicks;
        return await services.Screenshots.CaptureScrollingActiveWindowAsync(options);
    }

    private static async Task ApplyOptionalCaptureStartDelayAsync(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("scrolling", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("scrolling-window", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var delayText = Value(args, "--delay");
        if (!double.TryParse(delayText, CultureInfo.InvariantCulture, out var delaySeconds) || delaySeconds <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 0d, 10d)));
    }

    private static async Task<int> RecordAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("record requires a mode: monitor, audio, devices, or preflight.");
            return 2;
        }

        if (args[0].Equals("devices", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("device-list", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintRecordingDevicesAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("profiles", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("profile-list", StringComparison.OrdinalIgnoreCase))
        {
            return PrintRecordingProfiles(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("save-profile", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("profile-save", StringComparison.OrdinalIgnoreCase))
        {
            return SaveRecordingProfile(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("preflight", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("readiness", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintRecordingReadinessAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("audio", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("audio-probe", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("wav", StringComparison.OrdinalIgnoreCase))
        {
            return await RecordAudioAsync(services, args.Skip(1).ToArray());
        }

        if (!IsRecordingScreenTargetCommand(args[0]))
        {
            Console.Error.WriteLine("record requires monitor, all-monitors, window, region, fixed, devices, preflight, or audio.");
            return 2;
        }

        var target = await BuildRecordingCaptureTargetAsync(services, args);
        if (target is null)
        {
            return 2;
        }

        var durationText = Value(args, "--duration");
        var durationSeconds = double.TryParse(durationText, CultureInfo.InvariantCulture, out var parsed) ? parsed : 3d;
        var output = Value(args, "--output") ?? Value(args, "-o");
        var format = (Value(args, "--format") ?? Path.GetExtension(output ?? string.Empty).TrimStart('.')).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(format))
        {
            format = "gif";
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.Combine(services.Paths.VideosRoot, $"goatshot-recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{format}");
        }

        var recordingSettings = ResolveRecordingSettingsForCommand(services, args);
        if (recordingSettings is null)
        {
            return 2;
        }

        RecordingResult result;
        var addToWorkspace = Has(args, "--workspace");
        if (format.Equals("mp4", StringComparison.OrdinalIgnoreCase))
        {
            result = await services.Recording.RecordMp4Async(
                TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 0.25d, 180d)),
                output,
                recordingSettings,
                target,
                addToWorkspace);
        }
        else if (format.Equals("gif", StringComparison.OrdinalIgnoreCase))
        {
            result = await services.Recording.RecordGifAsync(
                TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 0.25d, 60d)),
                output,
                target,
                recordingSettings,
                addToWorkspace);
        }
        else
        {
            result = new RecordingResult
            {
                Succeeded = false,
                Message = $"Unknown recording format: {format}. Use gif or mp4."
            };
        }

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine(result.OutputPath);
        Console.Error.WriteLine(result.Message);
        if (format.Equals("mp4", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(result.OutputPath) &&
            (Has(args, "--probe-media") || Has(args, "--ffprobe") || Has(args, "--metadata-proof")))
        {
            var probe = await RecordingMediaProbeService.ProbeAsync(result.OutputPath);
            Console.Error.WriteLine(probe.Message);
            Console.Error.WriteLine(probe.SyncSummary);
        }

        if (result.Item is not null)
        {
            Console.Error.WriteLine($"workspace-id={result.Item.Id}");
        }

        return 0;
    }

    private static int PrintRecordingProfiles(AppServices services, IReadOnlyList<string> args)
    {
        var profiles = services.Settings.RecordingProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(profiles, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        Console.WriteLine("GoatShot recording profiles");
        if (profiles.Count == 0)
        {
            Console.WriteLine("(none)");
            return 0;
        }

        foreach (var profile in profiles)
        {
            var normalized = RecordingSettingsNormalizer.Normalize(profile.Settings);
            var description = string.IsNullOrWhiteSpace(profile.Description)
                ? string.Empty
                : $" - {profile.Description.Trim()}";
            Console.WriteLine($"{profile.Name}: {RecordingSettingsNormalizer.FormatSummary(normalized)}{description}");
        }

        return 0;
    }

    private static int SaveRecordingProfile(AppServices services, IReadOnlyList<string> args)
    {
        var name = Value(args, "--name") ??
            PositionalValues(
                args,
                "--name",
                "--description",
                "--recording-profile",
                "--record-profile",
                "--quality",
                "--profile",
                "--size",
                "--width",
                "--height",
                "--bitrate",
                "--fps",
                "--mic-device",
                "--microphone-device",
                "--system-audio-device",
                "--loopback-device",
                "--audio-gain",
                "--mic-gain",
                "--microphone-gain",
                "--system-audio-gain",
                "--loopback-gain",
                "--noise-gate",
                "--noise-gate-db",
                "--webcam-device",
                "--camera-device",
                "--timer-position",
                "--recording-timer-position",
                "--key-position",
                "--keys-position",
                "--keystroke-position",
                "--keystroke-overlay-position",
                "--overlay-badge-size",
                "--badge-size",
                "--overlay-font-size",
                "--overlay-style",
                "--badge-style").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("record save-profile requires a profile name.");
            return 2;
        }

        var settings = ResolveRecordingSettingsForCommand(services, args, allowMissingProfile: false);
        if (settings is null)
        {
            return 2;
        }

        var normalizedName = name.Trim();
        services.Settings.RecordingProfiles.RemoveAll(profile =>
            profile.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
        services.Settings.RecordingProfiles.Add(new RecordingWorkflowProfile
        {
            Name = normalizedName,
            Description = Value(args, "--description") ?? string.Empty,
            Settings = settings
        });
        services.Settings.RecordingProfiles = services.Settings.RecordingProfiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        services.SettingsStore.Save(services.Settings);

        Console.WriteLine($"Saved recording profile: {normalizedName}");
        Console.Error.WriteLine(RecordingSettingsNormalizer.FormatSummary(RecordingSettingsNormalizer.Normalize(settings)));
        return 0;
    }

    private static bool IsRecordingScreenTargetCommand(string command)
    {
        return command.Equals("monitor", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("active-monitor", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("all-monitors", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("allmonitors", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("window", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("active-window", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("region", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("fixed-region", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RecordingCaptureTarget?> BuildRecordingCaptureTargetAsync(AppServices services, IReadOnlyList<string> args)
    {
        var command = args[0];
        if (command.Equals("monitor", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("active-monitor", StringComparison.OrdinalIgnoreCase))
        {
            return RecordingCaptureTarget.ActiveMonitor();
        }

        if (command.Equals("all-monitors", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("allmonitors", StringComparison.OrdinalIgnoreCase))
        {
            return RecordingCaptureTarget.AllMonitors();
        }

        if (command.Equals("window", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("active-window", StringComparison.OrdinalIgnoreCase))
        {
            return RecordingCaptureTarget.ActiveWindow();
        }

        if (TryParseCaptureBounds(args, out var bounds))
        {
            return command.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("fixed-region", StringComparison.OrdinalIgnoreCase)
                ? RecordingCaptureTarget.FixedRegion(bounds)
                : RecordingCaptureTarget.Region(bounds);
        }

        var (width, height) = ParseOptionalVideoSize(args);
        if ((command.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("fixed-region", StringComparison.OrdinalIgnoreCase)) &&
            width is > 0 &&
            height is > 0)
        {
            return RecordingCaptureTarget.FixedRegion(BuildCursorCenteredBounds(width.Value, height.Value));
        }

        if (command.Equals("region", StringComparison.OrdinalIgnoreCase))
        {
            if (!Environment.UserInteractive || !Forms.SystemInformation.UserInteractive)
            {
                Console.Error.WriteLine("record region needs --bounds x,y,width,height, --x/--y/--width/--height, or an interactive desktop region selector.");
                return null;
            }

            using var selected = await services.Screenshots.CaptureRegionAsync();
            if (selected is null)
            {
                Console.Error.WriteLine("Region recording was canceled.");
                return null;
            }

            return RecordingCaptureTarget.Region(selected.Bounds);
        }

        Console.Error.WriteLine("record fixed requires --size WIDTHxHEIGHT or explicit --bounds x,y,width,height.");
        return null;
    }

    private static bool TryParseCaptureBounds(IReadOnlyList<string> args, out CaptureBounds bounds)
    {
        bounds = new CaptureBounds();
        var raw = Value(args, "--bounds") ?? Value(args, "--rect") ?? Value(args, "--region-bounds");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw
                .Replace(';', ',')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 4 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawX) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawY) &&
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawWidth) &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawHeight))
            {
                bounds = ClampBoundsToVirtualScreen(new CaptureBounds
                {
                    X = rawX,
                    Y = rawY,
                    Width = rawWidth,
                    Height = rawHeight
                });
                return bounds.Width > 0 && bounds.Height > 0;
            }
        }

        var x = ParseOptionalInt(args, "--x") ?? ParseOptionalInt(args, "--left");
        var y = ParseOptionalInt(args, "--y") ?? ParseOptionalInt(args, "--top");
        var (sizeWidth, sizeHeight) = ParseOptionalVideoSize(args);
        var width = ParseOptionalInt(args, "--width") ?? sizeWidth;
        var height = ParseOptionalInt(args, "--height") ?? sizeHeight;
        if (x is null || y is null || width is null || height is null)
        {
            return false;
        }

        bounds = ClampBoundsToVirtualScreen(new CaptureBounds
        {
            X = x.Value,
            Y = y.Value,
            Width = width.Value,
            Height = height.Value
        });
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static CaptureBounds BuildCursorCenteredBounds(int width, int height)
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var captureWidth = Math.Min(Math.Max(1, width), Math.Max(1, virtualScreen.Width));
        var captureHeight = Math.Min(Math.Max(1, height), Math.Max(1, virtualScreen.Height));
        var cursor = Forms.Cursor.Position;
        return ClampBoundsToVirtualScreen(new CaptureBounds
        {
            X = cursor.X - (captureWidth / 2),
            Y = cursor.Y - (captureHeight / 2),
            Width = captureWidth,
            Height = captureHeight
        });
    }

    private static CaptureBounds ClampBoundsToVirtualScreen(CaptureBounds bounds)
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var width = Math.Min(Math.Max(1, bounds.Width), Math.Max(1, virtualScreen.Width));
        var height = Math.Min(Math.Max(1, bounds.Height), Math.Max(1, virtualScreen.Height));
        var maxX = virtualScreen.Left + Math.Max(0, virtualScreen.Width - width);
        var maxY = virtualScreen.Top + Math.Max(0, virtualScreen.Height - height);
        return new CaptureBounds
        {
            X = Math.Clamp(bounds.X, virtualScreen.Left, maxX),
            Y = Math.Clamp(bounds.Y, virtualScreen.Top, maxY),
            Width = width,
            Height = height
        };
    }

    private static async Task<int> RecordAudioAsync(AppServices services, string[] args)
    {
        var source = ParseAudioCaptureSource(
            Value(args, "--source") ??
            Value(args, "--audio-source") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ??
            "microphone");
        var durationSeconds = ParseDouble(args, "--duration", 2d);
        var duration = TimeSpan.FromSeconds(Math.Clamp(durationSeconds, 0.25d, 30d));
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.Combine(services.Paths.VideosRoot, $"goatshot-audio-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.wav");
        }

        var deviceId = Value(args, "--device") ??
            (source == AudioCaptureSource.SystemAudio
                ? Value(args, "--system-audio-device") ?? Value(args, "--loopback-device")
                : Value(args, "--mic-device") ?? Value(args, "--microphone-device")) ??
            string.Empty;
        deviceId = string.IsNullOrWhiteSpace(deviceId)
            ? string.Empty
            : NormalizeDeviceIdOverride(deviceId);

        var result = await services.AudioCapture.CaptureWavAsync(
            new AudioCaptureRequest(
                source,
                duration,
                output,
                deviceId,
                BuildAudioProcessingSettings(
                    args,
                    source == AudioCaptureSource.SystemAudio
                        ? services.Settings.Recording.SystemAudioGain
                        : services.Settings.Recording.MicrophoneGain,
                    services.Settings.Recording.NoiseGateThresholdDb,
                    source == AudioCaptureSource.SystemAudio
                        ? services.Settings.Recording.SystemAudioMuted
                        : services.Settings.Recording.MicrophoneMuted,
                    source)),
            CancellationToken.None);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine(result.OutputPath);
        Console.Error.WriteLine(result.Message);
        return 0;
    }

    private static async Task<int> ImportAsync(AppServices services, string[] args)
    {
        var path = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("import requires a file path.");
            return 2;
        }

        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }

        var item = await services.WorkspaceStore.AddImageFileAsync(path, CaptureKind.Imported, "Imported from goatshot CLI.");
        Console.WriteLine(item.Id);
        return 0;
    }

    private static async Task<int> VideoAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("video requires: frame, trim, mute, volume, denoise, speed, crop, resize, cut, merge, intro-outro, subtitles, srt, convert, generate-mask, person-mask, hosted-person-mask, person-model, mask-quality, plan-silence, plan-transcript, plan-filler, apply-plan, plan-composite, apply-composite, plan-background, apply-background, or background-probe.");
            return 2;
        }

        return args[0].ToLowerInvariant() switch
        {
            "frame" or "export-frame" => await ExportVideoFrameAsync(services, args.Skip(1).ToArray()),
            "trim" => await TrimVideoAsync(services, args.Skip(1).ToArray()),
            "mute" => await MuteVideoAsync(services, args.Skip(1).ToArray()),
            "volume" or "vol" => await VolumeVideoAsync(services, args.Skip(1).ToArray()),
            "denoise" or "audio-denoise" or "noise-reduction" => await DenoiseVideoAudioAsync(services, args.Skip(1).ToArray()),
            "speed" or "change-speed" => await SpeedVideoAsync(services, args.Skip(1).ToArray()),
            "crop" => await CropVideoAsync(services, args.Skip(1).ToArray()),
            "resize" or "export" => await ResizeVideoAsync(services, args.Skip(1).ToArray()),
            "cut" or "remove-middle" => await CutVideoAsync(services, args.Skip(1).ToArray()),
            "merge" or "concat" => await MergeVideoAsync(services, args.Skip(1).ToArray()),
            "intro-outro" or "bookend" => await IntroOutroVideoAsync(services, args.Skip(1).ToArray()),
            "subtitles" or "subtitle" or "burn-subtitles" => await SubtitlesVideoAsync(services, args.Skip(1).ToArray()),
            "srt" or "copy-srt" => await CopySrtAsync(services, args.Skip(1).ToArray()),
            "convert" or "format" => await ConvertVideoAsync(services, args.Skip(1).ToArray()),
            "plan-silence" or "silence-plan" or "remove-silence-plan" => await PlanSilenceVideoAsync(services, args.Skip(1).ToArray()),
            "plan-transcript" or "transcript-plan" or "text-edit-plan" => await PlanTranscriptVideoAsync(services, args.Skip(1).ToArray()),
            "plan-filler" or "filler-plan" or "filler-word-plan" => await PlanFillerVideoAsync(services, args.Skip(1).ToArray()),
            "apply-plan" or "apply-cuts" or "export-plan" => await ApplyAdvancedVideoPlanAsync(services, args.Skip(1).ToArray()),
            "plan-composite" or "composite-plan" or "layout-plan" => await PlanCompositeVideoAsync(services, args.Skip(1).ToArray()),
            "apply-composite" or "export-composite" or "composite-export" => await ApplyCompositeVideoAsync(services, args.Skip(1).ToArray()),
            "generate-mask" or "foreground-mask" or "mask" => await GenerateForegroundMaskVideoAsync(services, args.Skip(1).ToArray()),
            "person-mask" or "person-segmentation-mask" or "segment-person" or "ai-mask" => await GeneratePersonSegmentationMaskVideoAsync(services, args.Skip(1).ToArray()),
            "hosted-person-mask" or "hosted-person-segmentation-mask" or "person-mask-hosted" or "hosted-segmentation-mask" => await GenerateHostedPersonSegmentationMaskVideoAsync(services, args.Skip(1).ToArray()),
            "person-model" or "person-segmentation-model" or "segmentation-model" or "model-package" => await PersonSegmentationModelAsync(services, args.Skip(1).ToArray()),
            "mask-quality" or "evaluate-mask" or "mask-evaluate" or "person-mask-quality" or "segmentation-quality" => await EvaluateVideoMaskQualityAsync(args.Skip(1).ToArray()),
            "plan-background" or "background-plan" or "webcam-background-plan" => await PlanWebcamBackgroundVideoAsync(services, args.Skip(1).ToArray()),
            "apply-background" or "export-background" or "webcam-background" => await ApplyWebcamBackgroundVideoAsync(services, args.Skip(1).ToArray()),
            "background-probe" or "webcam-background-probe" => await ProbeAdvancedVideoBackgroundAsync(services, args.Skip(1).ToArray()),
            _ => VideoUsage(args[0])
        };
    }

    private static async Task<int> ExportVideoFrameAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.ExportFrameAsync(
            item,
            ParseSeconds(args, "--at", 1d),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> TrimVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.TrimAsync(
            item,
            ParseSeconds(args, "--start", 0d),
            ParseSeconds(args, "--duration", 5d),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> MuteVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.MuteAsync(
            item,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> SpeedVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.ChangeSpeedAsync(
            item,
            ParseDouble(args, "--factor", 2d),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> VolumeVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var gain = ParseDouble(args, "--gain", ParseDouble(args, "--factor", 1d));
        var result = await services.VideoTools.ChangeVolumeAsync(
            item,
            gain,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> DenoiseVideoAudioAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var reductionDb = ParseDouble(
            args,
            "--noise-reduction-db",
            ParseDouble(args, "--nr", 12d));
        var result = await services.VideoTools.DenoiseAudioAsync(
            item,
            reductionDb,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> CropVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var width = ParseRequiredPositiveInt(args, "--width");
        var height = ParseRequiredPositiveInt(args, "--height");
        if (width is null || height is null)
        {
            return 2;
        }

        var result = await services.VideoTools.CropAsync(
            item,
            ParseInt(args, "--x", 0),
            ParseInt(args, "--y", 0),
            width.Value,
            height.Value,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> ConvertVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.ConvertAsync(
            item,
            Value(args, "--format"),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> ResizeVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var (width, height) = ParseOptionalVideoSize(args);
        var result = await services.VideoTools.ResizeAsync(
            item,
            width,
            height,
            Value(args, "--quality") ?? Value(args, "--profile"),
            ParseOptionalInt(args, "--crf"),
            Value(args, "--bitrate"),
            ParseOptionalInt(args, "--fps"),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> CutVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.CutMiddleAsync(
            item,
            ParseSeconds(args, "--start", 0d),
            ParseSeconds(args, "--duration", 1d),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> MergeVideoAsync(AppServices services, string[] args)
    {
        var targets = PositionalValues(args, "--output", "-o").Select(target => ResolveVideoTarget(services, [target])).ToList();
        if (targets.Any(item => item is null))
        {
            return 2;
        }

        var result = await services.VideoTools.MergeAsync(
            targets.OfType<CaptureItem>().ToList(),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> IntroOutroVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var (width, height) = ParseOptionalVideoSize(args);
        width ??= item.Width > 0 ? item.Width : 1280;
        height ??= item.Height > 0 ? item.Height : 720;

        var result = await services.VideoTools.AddIntroOutroAsync(
            item,
            Value(args, "--intro"),
            Value(args, "--outro"),
            width.Value,
            height.Value,
            ParseSeconds(args, "--still-duration", 2d),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> SubtitlesVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var subtitlePath = Value(args, "--srt") ?? Value(args, "--subtitles");
        if (string.IsNullOrWhiteSpace(subtitlePath))
        {
            Console.Error.WriteLine("video subtitles requires --srt captions.srt.");
            return 2;
        }

        var result = await services.VideoTools.BurnSubtitlesAsync(
            item,
            subtitlePath,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> CopySrtAsync(AppServices services, string[] args)
    {
        var subtitlePath = PositionalValues(args, "--output", "-o").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subtitlePath))
        {
            Console.Error.WriteLine("video srt requires an .srt file path.");
            return 2;
        }

        var result = await services.VideoTools.CopySubtitleAsync(
            subtitlePath,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result);
    }

    private static async Task<int> PlanSilenceVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var plan = await planner.PlanSilenceRemovalAsync(
            item,
            ParseDouble(args, "--noise-db", -35d),
            ParseSeconds(args, "--min-silence", 0.7d),
            ParseSeconds(args, "--padding", 0.15d));
        return await PrintAdvancedVideoPlanAsync(planner, plan, args);
    }

    private static async Task<int> PlanTranscriptVideoAsync(AppServices services, string[] args)
    {
        var srtPath = ResolveSrtArgument(args);
        if (string.IsNullOrWhiteSpace(srtPath))
        {
            Console.Error.WriteLine("video plan-transcript requires --srt captions.srt or --transcript transcript.txt plus --terms term[,term].");
            return 2;
        }

        var terms = ParseCliTerms(args);
        if (terms.Count == 0)
        {
            Console.Error.WriteLine("video plan-transcript requires at least one --term or --terms value.");
            return 2;
        }

        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var plan = await planner.PlanTranscriptTermsAsync(
            srtPath,
            terms,
            padding: ParseSeconds(args, "--padding", 0.25d));
        return await PrintAdvancedVideoPlanAsync(planner, plan, args);
    }

    private static async Task<int> PlanFillerVideoAsync(AppServices services, string[] args)
    {
        var srtPath = ResolveSrtArgument(args);
        if (string.IsNullOrWhiteSpace(srtPath))
        {
            Console.Error.WriteLine("video plan-filler requires --srt captions.srt or --transcript transcript.txt.");
            return 2;
        }

        var terms = ParseCliTerms(args);
        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var plan = await planner.PlanFillerWordRemovalAsync(
            srtPath,
            terms.Count > 0 ? terms : null,
            ParseSeconds(args, "--padding", 0.25d));
        return await PrintAdvancedVideoPlanAsync(planner, plan, args);
    }

    private static async Task<int> ApplyAdvancedVideoPlanAsync(AppServices services, string[] args)
    {
        var planPath = Value(args, "--plan") ?? Value(args, "--input-plan");
        if (string.IsNullOrWhiteSpace(planPath))
        {
            Console.Error.WriteLine("video apply-plan requires --plan <advanced-video-plan.json>.");
            return 2;
        }

        planPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(planPath));
        if (!File.Exists(planPath))
        {
            Console.Error.WriteLine($"Advanced video plan not found: {planPath}");
            return 2;
        }

        if (!Has(args, "--accept-plan") && !Has(args, "--reviewed"))
        {
            Console.Error.WriteLine("video apply-plan requires --accept-plan after reviewing the cut ranges.");
            return 2;
        }

        var target = Value(args, "--video") ??
            Value(args, "--source-video") ??
            Value(args, "--source") ??
            PositionalValues(
                args,
                "--plan",
                "--input-plan",
                "--video",
                "--source-video",
                "--source",
                "--output",
                "-o")
                .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("video apply-plan requires a video file path, workspace id, or --video <video>.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Video not found: {target}");
            return 2;
        }

        AdvancedVideoEditPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<AdvancedVideoEditPlan>(
                await File.ReadAllTextAsync(planPath),
                CliJsonOptions());
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Advanced video plan JSON is invalid: {ex.Message}");
            return 2;
        }

        if (plan is null)
        {
            Console.Error.WriteLine("Advanced video plan JSON did not contain an object.");
            return 2;
        }

        var result = await services.VideoTools.ApplyAdvancedCutPlanAsync(
            item,
            plan,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> PlanCompositeVideoAsync(AppServices services, string[] args)
    {
        var (width, height) = ParseOptionalVideoSize(args);
        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var plan = planner.BuildCompositeLayoutPlan(new CompositeVideoLayoutRequest
        {
            Preset = Value(args, "--preset") ?? Value(args, "--layout"),
            ScreenPath = Value(args, "--screen") ?? Value(args, "--screen-video"),
            CameraPath = Value(args, "--camera") ?? Value(args, "--webcam") ?? Value(args, "--camera-video"),
            Width = width ?? 1920,
            Height = height ?? 1080
        });
        return await PrintAdvancedVideoPlanAsync(planner, plan, args);
    }

    private static async Task<int> ApplyCompositeVideoAsync(AppServices services, string[] args)
    {
        var planPath = Value(args, "--plan") ?? Value(args, "--input-plan");
        if (string.IsNullOrWhiteSpace(planPath))
        {
            Console.Error.WriteLine("video apply-composite requires --plan <composite-plan.json>.");
            return 2;
        }

        planPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(planPath));
        if (!File.Exists(planPath))
        {
            Console.Error.WriteLine($"Composite layout plan not found: {planPath}");
            return 2;
        }

        if (!Has(args, "--accept-plan") && !Has(args, "--reviewed"))
        {
            Console.Error.WriteLine("video apply-composite requires --accept-plan after reviewing the composite layout recipe.");
            return 2;
        }

        AdvancedVideoEditPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<AdvancedVideoEditPlan>(
                await File.ReadAllTextAsync(planPath),
                CliJsonOptions());
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Composite layout plan JSON is invalid: {ex.Message}");
            return 2;
        }

        if (plan is null)
        {
            Console.Error.WriteLine("Composite layout plan JSON did not contain an object.");
            return 2;
        }

        var result = await services.VideoTools.ApplyCompositeLayoutPlanAsync(
            plan,
            Value(args, "--screen") ?? Value(args, "--screen-video"),
            Value(args, "--camera") ?? Value(args, "--webcam") ?? Value(args, "--camera-video"),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"),
            audioSource: Value(args, "--audio") ?? Value(args, "--audio-source"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> GenerateForegroundMaskVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.GenerateForegroundMaskAsync(
            item,
            new WebcamMaskGenerationOptions
            {
                Method = Value(args, "--method") ?? Value(args, "--mask-method"),
                KeyColor = Value(args, "--key-color") ?? Value(args, "--color") ?? Value(args, "--chroma-key"),
                Similarity = ParseOptionalDouble(args, "--similarity"),
                Blend = ParseOptionalDouble(args, "--blend"),
                LumaThreshold = ParseOptionalInt(args, "--luma-threshold") ?? ParseOptionalInt(args, "--threshold"),
                InvertMask = Has(args, "--invert-mask")
            },
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> GeneratePersonSegmentationMaskVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.GeneratePersonSegmentationMaskAsync(
            item,
            new PersonSegmentationMaskGenerationOptions
            {
                RunnerPath = Value(args, "--runner") ??
                    Value(args, "--person-segmenter") ??
                    Value(args, "--person-segmentation-runner"),
                ArgumentsTemplate = Value(args, "--runner-args") ??
                    Value(args, "--arguments") ??
                    Value(args, "--arg-template"),
                ModelPath = Value(args, "--model") ?? Value(args, "--model-path"),
                TimeoutSeconds = ParseOptionalInt(args, "--timeout-seconds") ?? ParseOptionalInt(args, "--timeout"),
                AcceptExternalRunner = Has(args, "--accept-external-runner")
            },
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> GenerateHostedPersonSegmentationMaskVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var result = await services.VideoTools.GenerateHostedPersonSegmentationMaskAsync(
            item,
            new HostedPersonSegmentationMaskGenerationOptions
            {
                Endpoint = Value(args, "--endpoint") ??
                    Value(args, "--url") ??
                    Value(args, "--service-url") ??
                    Value(args, "--hosted-segmentation-url"),
                ApiKeyEnvironmentVariable = Value(args, "--api-key-env") ??
                    Value(args, "--token-env") ??
                    Value(args, "--auth-token-env"),
                AuthorizationScheme = Value(args, "--auth-scheme") ?? "Bearer",
                ModelId = Value(args, "--model") ?? Value(args, "--model-id"),
                TimeoutSeconds = ParseOptionalInt(args, "--timeout-seconds") ?? ParseOptionalInt(args, "--timeout"),
                AcceptHostedService = Has(args, "--accept-hosted-service") ||
                    Has(args, "--accept-remote-service") ||
                    Has(args, "--accept-source-upload")
            },
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> EvaluateVideoMaskQualityAsync(string[] args)
    {
        var positionals = PositionalValues(
            args,
            "--generated",
            "--generated-mask",
            "--mask",
            "--candidate",
            "--reference",
            "--reference-mask",
            "--truth",
            "--ground-truth",
            "--source-video",
            "--video",
            "--output",
            "-o",
            "--threshold",
            "--foreground-threshold",
            "--min-iou",
            "--minimum-iou",
            "--min-precision",
            "--minimum-precision",
            "--min-recall",
            "--minimum-recall",
            "--max-frames",
            "--timeout-seconds",
            "--timeout",
            "--ffmpeg",
            "--operator",
            "--note",
            "--evaluated-at");
        var generatedMask = Value(args, "--generated") ??
            Value(args, "--generated-mask") ??
            Value(args, "--mask") ??
            Value(args, "--candidate") ??
            positionals.FirstOrDefault();
        var referenceMask = Value(args, "--reference") ??
            Value(args, "--reference-mask") ??
            Value(args, "--truth") ??
            Value(args, "--ground-truth") ??
            positionals.Skip(string.IsNullOrWhiteSpace(generatedMask) ? 0 : 1).FirstOrDefault();

        DateTimeOffset? evaluatedAt = null;
        var evaluatedAtText = Value(args, "--evaluated-at");
        if (!string.IsNullOrWhiteSpace(evaluatedAtText) &&
            DateTimeOffset.TryParse(evaluatedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedEvaluatedAt))
        {
            evaluatedAt = parsedEvaluatedAt;
        }

        var result = await new VideoMaskQualityEvaluationService().EvaluateAsync(new VideoMaskQualityEvaluationRequest
        {
            GeneratedMaskPath = generatedMask,
            ReferenceMaskPath = referenceMask,
            SourceVideoPath = Value(args, "--source-video") ?? Value(args, "--video"),
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator"),
            Note = Value(args, "--note"),
            EvaluatedAt = evaluatedAt,
            ForegroundThreshold = ParseOptionalInt(args, "--foreground-threshold") ?? ParseOptionalInt(args, "--threshold"),
            MinimumIntersectionOverUnion = ParseOptionalDouble(args, "--minimum-iou") ?? ParseOptionalDouble(args, "--min-iou"),
            MinimumPrecision = ParseOptionalDouble(args, "--minimum-precision") ?? ParseOptionalDouble(args, "--min-precision"),
            MinimumRecall = ParseOptionalDouble(args, "--minimum-recall") ?? ParseOptionalDouble(args, "--min-recall"),
            MaxFrames = ParseOptionalInt(args, "--max-frames"),
            TimeoutSeconds = ParseOptionalInt(args, "--timeout-seconds") ?? ParseOptionalInt(args, "--timeout"),
            FfmpegPath = Value(args, "--ffmpeg")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"quality-passed={result.QualityPassed}");
            Console.WriteLine($"evaluation-mode={result.EvaluationMode}");
            Console.WriteLine($"generated-mask={result.GeneratedMaskPath}");
            Console.WriteLine($"reference-mask={result.ReferenceMaskPath}");
            Console.WriteLine($"dimensions={result.Width}x{result.Height}");
            Console.WriteLine($"generated-frames={result.GeneratedFrameCount}");
            Console.WriteLine($"reference-frames={result.ReferenceFrameCount}");
            Console.WriteLine($"evaluated-frames={result.EvaluatedFrameCount}");
            Console.WriteLine($"threshold={result.ForegroundThreshold}");
            Console.WriteLine($"max-frames={(result.MaxFrames.HasValue ? result.MaxFrames.Value.ToString(CultureInfo.InvariantCulture) : "all")}");
            Console.WriteLine($"iou={result.IntersectionOverUnion:0.####}");
            Console.WriteLine($"dice={result.DiceCoefficient:0.####}");
            Console.WriteLine($"precision={result.Precision:0.####}");
            Console.WriteLine($"recall={result.Recall:0.####}");
            Console.WriteLine($"accuracy={result.Accuracy:0.####}");
            Console.WriteLine($"would-download-model={result.WouldDownloadModel}");
            Console.WriteLine($"would-run-segmentation-model={result.WouldRunSegmentationModel}");
            Console.WriteLine($"would-contact-hosted-service={result.WouldContactHostedService}");
            Console.WriteLine($"would-mutate-source-media={result.WouldMutateSourceMedia}");
            Console.WriteLine($"would-certify-whole-model={result.WouldCertifyWholeModel}");

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> PersonSegmentationModelAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("video person-model requires: validate --manifest <manifest.json> or stage --manifest <manifest.json> [--accept-download].");
            return 2;
        }

        var command = args[0];
        var manifest = Value(args, "--manifest") ??
            Value(args, "--model-manifest") ??
            Value(args, "--input") ??
            PositionalValues(
                args.Skip(1).ToArray(),
                "--manifest",
                "--model-manifest",
                "--input")
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(manifest))
        {
            Console.Error.WriteLine("video person-model requires --manifest <manifest.json>.");
            return 2;
        }

        PersonSegmentationModelPackageResult result;
        if (command.Equals("validate", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("check", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("plan", StringComparison.OrdinalIgnoreCase))
        {
            result = await services.PersonSegmentationModels.ValidateManifestAsync(manifest);
        }
        else if (command.Equals("stage", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("download", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("acquire", StringComparison.OrdinalIgnoreCase))
        {
            result = await services.PersonSegmentationModels.StageModelAsync(new PersonSegmentationModelPackageRequest
            {
                ManifestLocation = manifest,
                AcceptDownload = Has(args, "--accept-download") ||
                    Has(args, "--accept-remote-download") ||
                    Has(args, "--accept-model-download")
            });
        }
        else
        {
            Console.Error.WriteLine($"Unknown video person-model command: {command}");
            Console.Error.WriteLine("video person-model requires: validate --manifest <manifest.json> or stage --manifest <manifest.json> [--accept-download].");
            return 2;
        }

        PrintPersonSegmentationModelPackageResult(result, Has(args, "--json"));
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> PlanWebcamBackgroundVideoAsync(AppServices services, string[] args)
    {
        var item = ResolveVideoTarget(services, args);
        if (item is null)
        {
            return 2;
        }

        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var plan = planner.BuildWebcamBackgroundPlan(new WebcamBackgroundProcessingRequest
        {
            SourcePath = item.FilePath,
            Mode = Value(args, "--mode") ?? Value(args, "--background-mode"),
            MaskPath = Value(args, "--mask") ?? Value(args, "--matte") ?? Value(args, "--person-mask") ?? Value(args, "--alpha-matte"),
            InvertMask = Has(args, "--invert-mask"),
            KeyColor = Value(args, "--key-color") ?? Value(args, "--color") ?? Value(args, "--chroma-key"),
            Similarity = ParseOptionalDouble(args, "--similarity"),
            Blend = ParseOptionalDouble(args, "--blend"),
            BlurStrength = ParseOptionalInt(args, "--blur"),
            BackgroundColor = Value(args, "--background-color") ?? Value(args, "--replace-color")
        });
        return await PrintAdvancedVideoPlanAsync(planner, plan, args);
    }

    private static async Task<int> ApplyWebcamBackgroundVideoAsync(AppServices services, string[] args)
    {
        var planPath = Value(args, "--plan") ?? Value(args, "--input-plan");
        if (string.IsNullOrWhiteSpace(planPath))
        {
            Console.Error.WriteLine("video apply-background requires --plan <background-plan.json>.");
            return 2;
        }

        planPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(planPath));
        if (!File.Exists(planPath))
        {
            Console.Error.WriteLine($"Webcam background plan not found: {planPath}");
            return 2;
        }

        if (!Has(args, "--accept-plan") && !Has(args, "--reviewed"))
        {
            Console.Error.WriteLine("video apply-background requires --accept-plan after reviewing the background recipe.");
            return 2;
        }

        AdvancedVideoEditPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<AdvancedVideoEditPlan>(
                await File.ReadAllTextAsync(planPath),
                CliJsonOptions());
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Webcam background plan JSON is invalid: {ex.Message}");
            return 2;
        }

        if (plan is null)
        {
            Console.Error.WriteLine("Webcam background plan JSON did not contain an object.");
            return 2;
        }

        var target = Value(args, "--video") ??
            Value(args, "--source-video") ??
            Value(args, "--source") ??
            PositionalValues(
                args,
                "--plan",
                "--input-plan",
                "--video",
                "--source-video",
                "--source",
                "--output",
                "-o")
                .FirstOrDefault() ??
            plan.BackgroundRecipe?.SourcePath ??
            plan.SourcePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("video apply-background requires a video file path, workspace id, or sourcePath in the plan.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Video not found: {target}");
            return 2;
        }

        var result = await services.VideoTools.ApplyWebcamBackgroundPlanAsync(
            item,
            plan,
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));
        return PrintVideoToolResult(result, Has(args, "--json"));
    }

    private static async Task<int> ProbeAdvancedVideoBackgroundAsync(AppServices services, string[] args)
    {
        var planner = new AdvancedVideoEditPlannerService(services.Paths);
        var probe = planner.ProbeWebcamBackgroundProcessing();
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(probe, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        }
        else
        {
            Console.WriteLine($"capability={probe.Capability}");
            Console.WriteLine($"available={probe.Available}");
            Console.WriteLine($"preview-required={probe.PreviewRequired}");
            Console.WriteLine(probe.Message);
        }

        await Task.CompletedTask;
        return 0;
    }

    private static CaptureItem? ResolveVideoTarget(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("video command requires a workspace id, file name, or video file path.");
            return null;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Video not found: {target}");
        }

        return item;
    }

    private static int PrintVideoToolResult(VideoToolResult result, bool json = false)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        if (!json)
        {
            Console.WriteLine(result.OutputPath);
        }

        Console.Error.WriteLine(result.Message);
        if (result.Item is not null)
        {
            Console.Error.WriteLine($"workspace-id={result.Item.Id}");
        }

        return 0;
    }

    private static async Task<int> PrintAdvancedVideoPlanAsync(
        AdvancedVideoEditPlannerService planner,
        AdvancedVideoEditPlan plan,
        string[] args)
    {
        if (!plan.Succeeded)
        {
            Console.Error.WriteLine(plan.Message);
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
            }

            return 1;
        }

        var output = Value(args, "--output") ?? Value(args, "-o");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputPath = await planner.WritePlanAsync(plan, output);
            Console.Error.WriteLine($"plan-output={outputPath}");
        }

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        Console.WriteLine(plan.Message);
        Console.WriteLine($"plan-type={plan.PlanType}");
        Console.WriteLine($"mode={plan.Mode}");
        Console.WriteLine($"cuts={plan.Cuts.Count}");
        foreach (var cut in plan.Cuts.Take(12))
        {
            Console.WriteLine(
                $"  {cut.Index}. {cut.StartSeconds:0.###}-{cut.EndSeconds:0.###}s ({cut.DurationSeconds:0.###}s): {cut.Reason}");
        }

        if (plan.CompositeRecipes.Count > 0)
        {
            foreach (var recipe in plan.CompositeRecipes)
            {
                Console.WriteLine($"recipe={recipe.Name} {recipe.Width}x{recipe.Height}");
                Console.WriteLine(recipe.ExportCommandPreview);
            }
        }

        if (plan.BackgroundRecipe is not null)
        {
            Console.WriteLine($"background-recipe={plan.BackgroundRecipe.Name}");
            Console.WriteLine($"background-kind={plan.BackgroundRecipe.ProcessingKind}");
            Console.WriteLine($"background-mode={plan.BackgroundRecipe.Mode}");
            if (!string.IsNullOrWhiteSpace(plan.BackgroundRecipe.MaskPath))
            {
                Console.WriteLine($"mask={plan.BackgroundRecipe.MaskPath}");
                Console.WriteLine($"invert-mask={plan.BackgroundRecipe.InvertMask}");
            }
        }

        if (plan.CapabilityProbe is not null)
        {
            Console.WriteLine($"capability={plan.CapabilityProbe.Capability}");
            Console.WriteLine($"available={plan.CapabilityProbe.Available}");
            Console.WriteLine($"preview-required={plan.CapabilityProbe.PreviewRequired}");
        }

        foreach (var warning in plan.Warnings)
        {
            Console.Error.WriteLine($"warning={warning}");
        }

        return 0;
    }

    private static string? ResolveSrtArgument(string[] args)
    {
        return Value(args, "--srt") ??
            Value(args, "--subtitles") ??
            Value(args, "--transcript") ??
            PositionalValues(args, "--output", "-o", "--terms", "--term", "--word", "--words", "--padding")
                .FirstOrDefault();
    }

    private static IReadOnlyList<string> ParseCliTerms(string[] args)
    {
        var terms = new List<string>();
        terms.AddRange(Values(args, "--term"));
        terms.AddRange(Values(args, "--terms"));
        terms.AddRange(Values(args, "--word"));
        terms.AddRange(Values(args, "--words"));
        return AdvancedVideoEditPlannerService.NormalizeTerms(terms);
    }

    private static int VideoUsage(string command)
    {
        Console.Error.WriteLine($"Unknown video command: {command}");
        Console.Error.WriteLine("video requires: frame <video> [--at seconds], trim <video> [--start seconds] [--duration seconds], mute <video>, volume <video> [--gain n], denoise <video> [--noise-reduction-db 12], speed <video> [--factor n], crop <video> --width n --height n [--x n] [--y n], resize <video> [--size WxH] [--quality small|balanced|high] [--bitrate 4M] [--fps n], cut <video> [--start seconds] [--duration seconds], merge <video...>, intro-outro <video> [--intro image] [--outro image], subtitles <video> --srt captions.srt, srt captions.srt, convert <video> [--format mp4|gif|webm], generate-mask <video> [--method keyed|alpha|luma] [--key-color 0x00ff00] [--invert-mask], person-mask <video> --runner segmenter.exe --runner-args \"--input {input} --output {output}\" --accept-external-runner, hosted-person-mask <video> --endpoint https://... --accept-hosted-service [--api-key-env NAME], person-model validate|stage --manifest person-model.json [--accept-download], mask-quality --generated-mask mask.png|mask.mp4 --reference-mask reviewed.png|reviewed.mp4 [--min-iou 0.9] [--max-frames n], plan-silence <video>, plan-transcript --srt captions.srt|--transcript transcript.txt --terms term[,term], plan-filler --srt captions.srt|--transcript transcript.txt, apply-plan <video> --plan plan.json --accept-plan, plan-composite [--preset pip|side-by-side|stacked], apply-composite --plan composite-plan.json --accept-plan [--screen screen.mp4] [--camera webcam.mp4], plan-background <webcam.mp4> [--mode blur|remove|replace] [--mask person-mask.mp4] [--invert-mask], apply-background <webcam.mp4> --plan background-plan.json --accept-plan, or background-probe.");
        return 2;
    }

    private static async Task<int> ImageAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("image requires: combine, stitch, or split.");
            return 2;
        }

        return args[0].ToLowerInvariant() switch
        {
            "combine" => await CombineImagesAsync(services, args.Skip(1).ToArray()),
            "stitch" or "scroll-stitch" => await StitchImagesAsync(services, args.Skip(1).ToArray()),
            "split" => await SplitImageAsync(services, args.Skip(1).ToArray()),
            _ => ImageUsage(args[0])
        };
    }

    private static async Task<int> CombineImagesAsync(AppServices services, string[] args)
    {
        var inputs = PositionalValues(args, "--output", "-o", "--orientation", "--layout");
        var result = await services.ImageLayouts.CombineAsync(
            inputs,
            Value(args, "--orientation") ?? Value(args, "--layout") ?? "vertical",
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));

        return PrintImageLayoutResult(result, Has(args, "--json"));
    }

    private static async Task<int> StitchImagesAsync(AppServices services, string[] args)
    {
        var inputs = PositionalValues(
            args,
            "--output",
            "-o",
            "--axis",
            "--orientation",
            "--scroll-profile",
            "--scroll-mode",
            "--stitch-profile",
            "--max-frames",
            "--wheel-clicks",
            "--settle-ms",
            "--settle-delay-ms",
            "--sticky-pixels",
            "--sticky-header",
            "--sticky-left",
            "--auto-sticky-max",
            "--min-overlap",
            "--max-overlap");
        var result = await services.ImageLayouts.StitchAsync(
            inputs,
            ParseScrollingOptions(args),
            Value(args, "--output") ?? Value(args, "-o"),
            addToWorkspace: Has(args, "--workspace"));

        return PrintImageLayoutResult(result, Has(args, "--json"));
    }

    private static async Task<int> SplitImageAsync(AppServices services, string[] args)
    {
        var inputs = PositionalValues(args, "--output-dir", "-d", "--rows", "--columns", "--cols");
        var input = inputs.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("image split requires an image path.");
            return 2;
        }

        var result = await services.ImageLayouts.SplitGridAsync(
            input,
            ParseInt(args, "--rows", 2),
            ParseInt(args, "--columns", ParseInt(args, "--cols", 1)),
            Value(args, "--output-dir") ?? Value(args, "-d"),
            addToWorkspace: Has(args, "--workspace"));

        return PrintImageLayoutResult(result, Has(args, "--json"));
    }

    private static int PrintImageLayoutResult(ImageLayoutResult result, bool json)
    {
        if (json)
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return result.Succeeded ? 0 : 1;
        }

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine(result.Message);
        foreach (var output in result.OutputPaths)
        {
            Console.WriteLine(output);
        }

        foreach (var item in result.Items)
        {
            Console.Error.WriteLine($"workspace-id={item.Id}");
        }

        return 0;
    }

    private static int ImageUsage(string command)
    {
        Console.Error.WriteLine($"Unknown image command: {command}");
        Console.Error.WriteLine("image requires: combine <image...> [--orientation vertical|horizontal] [--output combined.png] [--workspace], stitch <image...> [--scroll-profile browser|table|document] [--axis vertical|horizontal] [--auto-sticky|--no-auto-sticky] [--sticky-pixels n] [--output stitched.png] [--workspace], or split <image> [--rows n] [--columns n] [--output-dir dir] [--workspace].");
        return 2;
    }

    private static async Task<int> ShareAsync(AppServices services, string[] args)
    {
        var file = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(file))
        {
            Console.Error.WriteLine("share/upload requires a file path.");
            return 2;
        }

        file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(file));
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return 2;
        }

        ApplyShareOverrides(services, args);

        var destination = ShareService.ParseDestination(Value(args, "--destination") ?? Value(args, "--provider") ?? services.Settings.DefaultShareDestination);
        var item = new CaptureItem
        {
            Kind = CaptureKind.Imported,
            CreatedAt = DateTimeOffset.Now,
            FilePath = file,
            ThumbnailPath = file,
            Bytes = new FileInfo(file).Length,
            Notes = "Transient CLI share item."
        };

        using (var image = System.Drawing.Image.FromFile(file))
        {
            item.Width = image.Width;
            item.Height = image.Height;
        }

        if (Has(args, "--queue"))
        {
            if (!services.Settings.UploadQueue.EnableQueue)
            {
                Console.Error.WriteLine("Upload queue is disabled in settings.");
                return 1;
            }

            services.SaveSettings();
            var queued = await services.UploadQueue.EnqueueAsync(item, destination, CancellationToken.None);
            Console.WriteLine($"queued id={queued.Id} destination={queued.Destination} file={queued.FileName}");
            Console.WriteLine(queued.LastMessage);
            return 0;
        }

        var result = await services.Sharing.ShareAsync(item, destination);
        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.Url))
        {
            Console.WriteLine(result.Url);
        }

        if (result.Succeeded)
        {
            await services.Automation.ProcessUploadCompletedAsync(item);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> UploadQueueAsync(AppServices services, string[] args)
    {
        if (args.Length > 0 &&
            (args[0].Equals("diagnostics", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("diag", StringComparison.OrdinalIgnoreCase)))
        {
            var diagnostics = services.UploadQueue.GetDiagnostics();
            if (Has(args, "--json"))
            {
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                };
                Console.WriteLine(JsonSerializer.Serialize(diagnostics, jsonOptions));
                return 0;
            }

            PrintQueueDiagnostics(diagnostics);
            return 0;
        }

        if (args.Length == 0 ||
            args[0].Equals("list", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var items = await services.UploadQueue.ListAsync(CancellationToken.None);
            var limit = ParseInt(args, "--limit", 20);
            var visible = items.Take(Math.Clamp(limit, 1, 500)).ToList();

            if (Has(args, "--json"))
            {
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                };
                jsonOptions.Converters.Add(new JsonStringEnumConverter());
                Console.WriteLine(JsonSerializer.Serialize(visible, jsonOptions));
                return 0;
            }

            if (visible.Count == 0)
            {
                Console.WriteLine(services.UploadQueue.GetStatusSummary());
                return 1;
            }

            foreach (var item in visible)
            {
                Console.WriteLine($"{item.ShortId} {item.StatusLabel} {item.Destination} {item.FileName}");
                Console.WriteLine(item.AttemptLabel);
                Console.WriteLine(item.NextAttemptLabel);
                Console.WriteLine(item.LastMessage);
                Console.WriteLine();
            }

            return 0;
        }

        if (args[0].Equals("process", StringComparison.OrdinalIgnoreCase))
        {
            var maxItems = ParseInt(args, "--limit", services.Settings.UploadQueue.MaxConcurrentUploads);
            var result = await services.UploadQueue.ProcessDueAsync(
                services.Sharing,
                Math.Max(1, maxItems),
                CancellationToken.None);
            await NotifyUploadQueueCompletionsAsync(services, result);

            Console.WriteLine(result.Message);
            foreach (var item in result.Items)
            {
                Console.WriteLine($"{item.ShortId} {item.StatusLabel} {item.Destination} {item.FileName}");
                Console.WriteLine(item.DetailLabel);
            }

            return result.Failed > 0 ? 1 : 0;
        }

        if (args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            var id = args.Skip(1).FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(id))
            {
                Console.Error.WriteLine("queue cancel requires an item id.");
                return 2;
            }

            var item = await services.UploadQueue.CancelAsync(id, CancellationToken.None);
            if (item is null)
            {
                Console.Error.WriteLine($"Queue item not found: {id}");
                return 1;
            }

            Console.WriteLine($"{item.Id} {item.Status}: {item.LastMessage}");
            return item.Status.Equals("Canceled", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        if (args[0].Equals("retry", StringComparison.OrdinalIgnoreCase))
        {
            var id = args.Skip(1).FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(id))
            {
                Console.Error.WriteLine("queue retry requires an item id.");
                return 2;
            }

            var item = await services.UploadQueue.RetryAsync(id, CancellationToken.None);
            if (item is null)
            {
                Console.Error.WriteLine($"Queue item not found: {id}");
                return 1;
            }

            Console.WriteLine($"{item.Id} {item.Status}: {item.LastMessage}");
            return 0;
        }

        Console.Error.WriteLine("queue requires: list [--json] [--limit N], diagnostics [--json], process [--limit N], cancel <id>, or retry <id>.");
        return 2;
    }

    private static void PrintQueueDiagnostics(UploadQueueDiagnostics diagnostics)
    {
        Console.WriteLine("GoatShot upload queue diagnostics");
        Console.WriteLine(diagnostics.Summary);
        Console.WriteLine($"queue-path={diagnostics.QueuePath}");
        Console.WriteLine($"background-processing={(diagnostics.BackgroundProcessingEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"retry-failed={(diagnostics.RetryFailedUploads ? "enabled" : "disabled")}");
        Console.WriteLine($"retry-backoff-seconds={diagnostics.RetryBackoffSeconds}");
        Console.WriteLine($"max-attempts={diagnostics.MaxAttempts}");
        Console.WriteLine($"max-concurrent-uploads={diagnostics.MaxConcurrentUploads}");
        Console.WriteLine($"due-now={diagnostics.DueNow}");
        Console.WriteLine($"next-due={(diagnostics.NextDueAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) ?? "none")}");
        Console.WriteLine(diagnostics.RedactionNotice);
        if (diagnostics.StatusCounts.Count > 0)
        {
            Console.WriteLine("status-counts:");
            foreach (var pair in diagnostics.StatusCounts.OrderBy(pair => pair.Key))
            {
                Console.WriteLine($"  {pair.Key}={pair.Value}");
            }
        }
    }

    private static async Task NotifyUploadQueueCompletionsAsync(AppServices services, UploadQueueProcessResult result)
    {
        if (result.Succeeded == 0)
        {
            return;
        }

        var byId = services.WorkspaceStore.Load()
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var queued in result.Items.Where(item => item.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(queued.CaptureItemId) &&
                byId.TryGetValue(queued.CaptureItemId, out var capture))
            {
                await services.Automation.ProcessUploadCompletedAsync(capture);
            }
        }
    }

    private static async Task<int> OAuthAsync(AppServices services, string[] args)
    {
        if (args.Length == 0 ||
            args[0].Equals("status", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("providers", StringComparison.OrdinalIgnoreCase))
        {
            var providerFilter = Value(args, "--provider");
            var records = services.Settings.OAuth.Providers
                .Where(provider => string.IsNullOrWhiteSpace(providerFilter) ||
                    provider.ProviderName.Equals(providerFilter, StringComparison.OrdinalIgnoreCase))
                .Select(provider => CreateOAuthStatus(services, provider))
                .ToList();

            if (Has(args, "--json"))
            {
                var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                };
                Console.WriteLine(JsonSerializer.Serialize(records, jsonOptions));
                return 0;
            }

            if (records.Count == 0)
            {
                Console.WriteLine("No OAuth providers are configured.");
                return 1;
            }

            foreach (var record in records)
            {
                Console.WriteLine($"{record.ProviderName}: client-id={(record.ClientIdConfigured ? "configured" : "missing")}, pkce={(record.UsePkce ? "enabled" : "disabled")}, oauth-token={(record.OAuthTokenStored ? "stored" : "missing")}, refresh={(record.RefreshTokenStored ? "stored" : "missing")}, provider-token={(record.ProviderAccessTokenStored ? "stored" : "missing")}");
                Console.WriteLine($"auth={record.AuthorizationEndpointConfigured}, token={record.TokenEndpointConfigured}, scopes={(string.IsNullOrWhiteSpace(record.Scopes) ? "(none)" : record.Scopes)}");
            }

            return 0;
        }

        if (args[0].Equals("live-proof-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("proof-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("consent-proof-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("account-proof-plan", StringComparison.OrdinalIgnoreCase))
        {
            return await OAuthLiveProofPlanAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("record-live-evidence", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("record-evidence", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("record-proof", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("live-evidence", StringComparison.OrdinalIgnoreCase))
        {
            return await OAuthLiveEvidenceRecordAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("configure", StringComparison.OrdinalIgnoreCase))
        {
            var provider = FindOAuthProvider(services, args.Skip(1).ToArray());
            if (provider is null)
            {
                Console.Error.WriteLine($"OAuth provider not found. Known providers: {FormatOAuthProviders(services)}");
                return 2;
            }

            var clientId = Value(args, "--client-id");
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                provider.ClientId = clientId.Trim();
            }

            var authorizationEndpoint = Value(args, "--authorization-endpoint") ?? Value(args, "--auth-endpoint");
            if (!string.IsNullOrWhiteSpace(authorizationEndpoint))
            {
                provider.AuthorizationEndpoint = authorizationEndpoint.Trim();
            }

            var tokenEndpoint = Value(args, "--token-endpoint");
            if (!string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                provider.TokenEndpoint = tokenEndpoint.Trim();
            }

            var scopes = Value(args, "--scopes") ?? Value(args, "--scope");
            if (scopes is not null)
            {
                provider.Scopes = scopes.Trim();
            }

            if (Has(args, "--pkce"))
            {
                provider.UsePkce = true;
            }

            if (Has(args, "--no-pkce"))
            {
                provider.UsePkce = false;
            }

            var callbackPort = Value(args, "--callback-port");
            if (int.TryParse(callbackPort, out var parsedCallbackPort) &&
                parsedCallbackPort is > 0 and <= 65_535)
            {
                services.Settings.OAuth.LocalCallbackPort = parsedCallbackPort;
            }

            services.SaveSettings();
            Console.WriteLine($"Configured OAuth provider: {provider.ProviderName}");
            return 0;
        }

        if (args[0].Equals("auth-url", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("authorize", StringComparison.OrdinalIgnoreCase))
        {
            var provider = FindOAuthProvider(services, args.Skip(1).ToArray());
            if (provider is null)
            {
                Console.Error.WriteLine($"OAuth provider not found. Known providers: {FormatOAuthProviders(services)}");
                return 2;
            }

            var state = Value(args, "--state") ?? Guid.NewGuid().ToString("N");
            var callbackUri = OAuthCallbackUri(services, args);
            var pkce = (provider.UsePkce && !Has(args, "--no-pkce")) || Has(args, "--pkce")
                ? OAuthPkcePair.Create()
                : null;

            Console.WriteLine(services.OAuthFlow.BuildAuthorizationUri(provider, state, callbackUri, pkce));
            Console.Error.WriteLine($"state={state}");
            Console.Error.WriteLine($"callback={callbackUri}");
            if (pkce is not null)
            {
                Console.Error.WriteLine($"code-verifier={pkce.CodeVerifier}");
            }

            return 0;
        }

        if (args[0].Equals("exchange", StringComparison.OrdinalIgnoreCase))
        {
            var provider = FindOAuthProvider(services, args.Skip(1).ToArray());
            if (provider is null)
            {
                Console.Error.WriteLine($"OAuth provider not found. Known providers: {FormatOAuthProviders(services)}");
                return 2;
            }

            var code = Value(args, "--code");
            if (string.IsNullOrWhiteSpace(code))
            {
                Console.Error.WriteLine("oauth exchange requires --code CODE.");
                return 2;
            }

            var result = await services.OAuthFlow.ExchangeCodeAsync(
                provider,
                code,
                OAuthCallbackUri(services, args),
                Value(args, "--code-verifier"),
                CancellationToken.None);

            if (!result.Succeeded)
            {
                Console.Error.WriteLine(result.Message);
                return 1;
            }

            services.SecretStore.SaveOAuthToken(provider.ProviderName, result);
            provider.RefreshTokenStored = !string.IsNullOrWhiteSpace(result.RefreshToken) ||
                services.SecretStore.ReadOAuthToken(provider.ProviderName)?.RefreshToken.Length > 0;
            services.SaveSettings();
            Console.WriteLine(result.Message);
            Console.WriteLine($"saved={provider.ProviderName}");
            Console.WriteLine($"refresh-token={(provider.RefreshTokenStored ? "stored" : "missing")}");
            return 0;
        }

        if (args[0].Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            var provider = FindOAuthProvider(services, args.Skip(1).ToArray());
            if (provider is null)
            {
                Console.Error.WriteLine($"OAuth provider not found. Known providers: {FormatOAuthProviders(services)}");
                return 2;
            }

            var token = services.SecretStore.ReadOAuthToken(provider.ProviderName);
            if (token is null || string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                Console.Error.WriteLine($"No refresh token is stored for {provider.ProviderName}.");
                return 1;
            }

            var result = await services.OAuthFlow.RefreshAsync(
                provider,
                token.RefreshToken,
                CancellationToken.None);

            if (!result.Succeeded)
            {
                Console.Error.WriteLine(result.Message);
                return 1;
            }

            services.SecretStore.SaveOAuthToken(provider.ProviderName, result);
            provider.RefreshTokenStored = true;
            services.SaveSettings();
            Console.WriteLine(result.Message);
            Console.WriteLine($"saved={provider.ProviderName}");
            return 0;
        }

        if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("logout", StringComparison.OrdinalIgnoreCase))
        {
            var provider = FindOAuthProvider(services, args.Skip(1).ToArray());
            if (provider is null)
            {
                Console.Error.WriteLine($"OAuth provider not found. Known providers: {FormatOAuthProviders(services)}");
                return 2;
            }

            services.SecretStore.ClearOAuthToken(provider.ProviderName);
            provider.RefreshTokenStored = false;
            services.SaveSettings();
            Console.WriteLine($"Cleared OAuth token for {provider.ProviderName}.");
            return 0;
        }

        Console.Error.WriteLine("oauth requires: status [--json], live-proof-plan [--provider <name>|all] [--output folder] [--json], record-live-evidence --provider <name> --status passed|failed|blocked|pending, configure <provider> --client-id ID, auth-url <provider>, exchange <provider> --code CODE, refresh <provider>, or clear <provider>.");
        return 2;
    }

    private static async Task<int> OAuthLiveProofPlanAsync(AppServices services, string[] args)
    {
        var providerName = Value(args, "--provider") ??
            Value(args, "--destination") ??
            PositionalValues(
                args,
                "--provider",
                "--destination",
                "--output",
                "-o",
                "--callback",
                "--redirect-uri",
                "--callback-port")
            .FirstOrDefault();
        var result = await new OAuthLiveProofPlanService().CreateAsync(new OAuthLiveProofPlanRequest
        {
            Providers = services.Settings.OAuth.Providers,
            ProviderName = providerName,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            CallbackUri = Value(args, "--callback") ?? Value(args, "--redirect-uri"),
            CallbackPort = ParseInt(args, "--callback-port", services.Settings.OAuth.LocalCallbackPort),
            PolicyAllowed = true
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"callback={result.CallbackUri}");
            Console.WriteLine($"would-open-browser={result.WouldOpenBrowser}");
            Console.WriteLine($"would-contact-provider={result.WouldContactProvider}");
            Console.WriteLine($"would-exchange-code={result.WouldExchangeCode}");
            Console.WriteLine($"would-store-token={result.WouldStoreToken}");
            Console.WriteLine($"would-refresh-token={result.WouldRefreshToken}");
            Console.WriteLine($"would-upload-file={result.WouldUploadFile}");
            Console.WriteLine($"would-delete-remote-file={result.WouldDeleteRemoteFile}");

            foreach (var provider in result.Providers)
            {
                Console.WriteLine($"{provider.ProviderName}: {provider.Status}");
                Console.WriteLine($"  client-id={provider.ClientIdConfigured}");
                Console.WriteLine($"  auth-endpoint={provider.AuthorizationEndpointConfigured}");
                Console.WriteLine($"  token-endpoint={provider.TokenEndpointConfigured}");
                Console.WriteLine($"  pkce={provider.UsesPkce}");
                Console.WriteLine($"  refresh-marker={provider.RefreshTokenMarkerAlreadyStored}");
                foreach (var issue in provider.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in provider.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> OAuthLiveEvidenceRecordAsync(AppServices services, string[] args)
    {
        var providerName = Value(args, "--provider") ??
            Value(args, "--destination") ??
            PositionalValues(
                args,
                "--provider",
                "--destination",
                "--status",
                "--output",
                "-o",
                "--operator",
                "--note",
                "--observed-at",
                "--evidence",
                "--evidence-path",
                "--consent-evidence",
                "--exchange-evidence",
                "--refresh-evidence",
                "--upload-evidence",
                "--cleanup-evidence",
                "--account-evidence")
            .FirstOrDefault();
        var status = Value(args, "--status") ??
            PositionalValues(
                args,
                "--provider",
                "--destination",
                "--output",
                "-o",
                "--operator",
                "--note",
                "--observed-at",
                "--evidence",
                "--evidence-path",
                "--consent-evidence",
                "--exchange-evidence",
                "--refresh-evidence",
                "--upload-evidence",
                "--cleanup-evidence",
                "--account-evidence")
            .Skip(string.IsNullOrWhiteSpace(providerName) ? 0 : 1)
            .FirstOrDefault();
        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedObservedAt))
        {
            observedAt = parsedObservedAt;
        }

        var result = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
        {
            Providers = services.Settings.OAuth.Providers,
            ProviderName = providerName,
            Status = status,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator"),
            Note = Value(args, "--note"),
            ObservedAt = observedAt,
            Evidence = OAuthEvidenceInputs(args)
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"provider={result.ProviderName}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"proof-complete={result.ProofComplete}");
            Console.WriteLine($"would-open-browser={result.WouldOpenBrowser}");
            Console.WriteLine($"would-contact-provider={result.WouldContactProvider}");
            Console.WriteLine($"would-exchange-code={result.WouldExchangeCode}");
            Console.WriteLine($"would-store-token={result.WouldStoreToken}");
            Console.WriteLine($"would-refresh-token={result.WouldRefreshToken}");
            Console.WriteLine($"would-upload-file={result.WouldUploadFile}");
            Console.WriteLine($"would-delete-remote-file={result.WouldDeleteRemoteFile}");
            if (result.MissingRequiredCategories.Count > 0)
            {
                Console.WriteLine($"missing-required={string.Join(",", result.MissingRequiredCategories)}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static List<OAuthLiveEvidenceInput> OAuthEvidenceInputs(IReadOnlyList<string> args)
    {
        var evidence = new List<OAuthLiveEvidenceInput>();
        AddEvidence(evidence, "other", Values(args, "--evidence"));
        AddEvidence(evidence, "other", Values(args, "--evidence-path"));
        AddEvidence(evidence, "consent", Values(args, "--consent-evidence"));
        AddEvidence(evidence, "exchange", Values(args, "--exchange-evidence"));
        AddEvidence(evidence, "refresh", Values(args, "--refresh-evidence"));
        AddEvidence(evidence, "upload", Values(args, "--upload-evidence"));
        AddEvidence(evidence, "cleanup", Values(args, "--cleanup-evidence"));
        AddEvidence(evidence, "account", Values(args, "--account-evidence"));
        return evidence;
    }

    private static void AddEvidence(List<OAuthLiveEvidenceInput> evidence, string category, IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            evidence.Add(new OAuthLiveEvidenceInput
            {
                Category = category,
                Value = value
            });
        }
    }

    private static int ProviderDiagnostics(AppServices services, string[] args)
    {
        var records = services.ProviderDiagnostics.GetDiagnostics().AsEnumerable();
        var providerFilter = Value(args, "--provider") ?? Value(args, "--destination");
        var positional = PositionalValues(args, "--provider", "--destination", "--status");
        if (string.IsNullOrWhiteSpace(providerFilter) && positional.Count > 0)
        {
            providerFilter = string.Join(' ', positional);
        }

        if (!string.IsNullOrWhiteSpace(providerFilter))
        {
            records = records.Where(record =>
                record.ProviderName.Contains(providerFilter, StringComparison.OrdinalIgnoreCase) ||
                record.DestinationName.Contains(providerFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (Has(args, "--ready"))
        {
            records = records.Where(record => record.ReadyForLocalAttempt);
        }

        if (Has(args, "--not-ready") || Has(args, "--needs-configuration"))
        {
            records = records.Where(record => !record.ReadyForLocalAttempt);
        }

        if (Has(args, "--implemented"))
        {
            records = records.Where(record => record.CatalogImplemented);
        }

        if (Has(args, "--roadmap"))
        {
            records = records.Where(record => !record.CatalogImplemented);
        }

        var list = records.ToList();
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(list, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        if (list.Count == 0)
        {
            Console.WriteLine("No provider diagnostics matched.");
            return 1;
        }

        Console.WriteLine("GoatShot provider diagnostics");
        foreach (var record in list)
        {
            Console.WriteLine($"{record.ProviderName}: {record.Status} ({record.AuthType})");
            Console.WriteLine($"  {record.ReadinessSummary}");
            Console.WriteLine($"  catalog={(record.CatalogImplemented ? "implemented" : "roadmap")}, public-links={(record.SupportsPublicLinks ? "yes" : "no")}, private-links={(record.SupportsPrivateLinks ? "yes" : "no")}");
            PrintProviderDiagnosticList("configured", record.ConfiguredSettings);
            PrintProviderDiagnosticList("saved", record.SavedSecrets);
            PrintProviderDiagnosticList("missing settings", record.MissingSettings);
            PrintProviderDiagnosticList("missing secrets", record.MissingSecrets);
            PrintProviderDiagnosticList("notes", record.Notes);
        }

        return 0;
    }

    private static void PrintProviderDiagnosticList(string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        Console.WriteLine($"  {label}: {string.Join(", ", values)}");
    }

    private static int ShareHistory(AppServices services, string[] args)
    {
        if (args.Length > 0 &&
            (args[0].Equals("copy-url", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("copy-link", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("copy-markdown", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("open", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("retry", StringComparison.OrdinalIgnoreCase)))
        {
            return ShareHistoryAction(services, args);
        }

        var destinationText = Value(args, "--destination") ?? Value(args, "--provider");
        ShareDestination? destination = null;
        if (!string.IsNullOrWhiteSpace(destinationText))
        {
            if (!TryParseHistoryDestination(destinationText, out var parsedDestination))
            {
                Console.Error.WriteLine($"Unknown share-history destination: {destinationText}");
                return 2;
            }

            destination = parsedDestination;
        }

        var statusText = Value(args, "--status");
        var succeeded = ParseHistoryStatus(statusText);
        if (!succeeded.Valid)
        {
            Console.Error.WriteLine("share-history --status must be succeeded, failed, ok, or error.");
            return 2;
        }

        if (Has(args, "--succeeded") || Has(args, "--success") || Has(args, "--ok"))
        {
            succeeded = (true, true);
        }

        if (Has(args, "--failed") || Has(args, "--failure") || Has(args, "--error"))
        {
            succeeded = (true, false);
        }

        bool? external = null;
        if (Has(args, "--external"))
        {
            external = true;
        }

        if (Has(args, "--local"))
        {
            external = false;
        }

        var query = Value(args, "--query") ?? Value(args, "-q");
        var entries = services.Sharing.SearchHistory(
            query,
            destination,
            succeeded.Valid ? succeeded.Value : null,
            external,
            ParseInt(args, "--limit", 20));
        if (Has(args, "--json"))
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(entries, jsonOptions));
            return 0;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No share history yet.");
            return 1;
        }

        foreach (var entry in entries)
        {
            var model = new ShareHistoryActionModel(entry);
            Console.WriteLine($"{model.ShortId} {model.CreatedText} {model.StatusText} {model.DestinationText} {model.FileName}");
            Console.WriteLine(entry.Message);
            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                Console.WriteLine(entry.Url);
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int ShareHistoryAction(AppServices services, string[] args)
    {
        var action = args[0].Trim().ToLowerInvariant();
        var id = args.Skip(1).FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(id))
        {
            Console.Error.WriteLine($"share-history {args[0]} requires a history id.");
            return 2;
        }

        var entry = ShareHistoryActions.FindEntry(services.Sharing.SearchHistory(limit: 500), id);
        if (entry is null)
        {
            Console.Error.WriteLine($"Share history entry not found: {id}");
            return 1;
        }

        var model = new ShareHistoryActionModel(entry);
        switch (action)
        {
            case "copy-url":
            case "copy-link":
                if (!model.CanCopyUrl)
                {
                    Console.Error.WriteLine($"Share history entry {model.ShortId} has no URL to copy.");
                    return 1;
                }

                ShareHistoryActions.CopyUrl(entry);
                Console.WriteLine($"Copied URL for share history entry {model.ShortId}.");
                return 0;

            case "copy-markdown":
                if (!model.CanCopyMarkdown)
                {
                    Console.Error.WriteLine($"Share history entry {model.ShortId} has no URL for Markdown.");
                    return 1;
                }

                ShareHistoryActions.CopyMarkdown(entry);
                Console.WriteLine($"Copied Markdown for share history entry {model.ShortId}.");
                return 0;

            case "open":
                if (!model.CanOpenUrl)
                {
                    Console.Error.WriteLine($"Share history entry {model.ShortId} has no http(s) URL to open.");
                    return 1;
                }

                ShareHistoryActions.OpenUrl(entry);
                Console.WriteLine($"Opened URL for share history entry {model.ShortId}.");
                return 0;

            case "retry":
                if (!model.CanRetry)
                {
                    Console.Error.WriteLine($"Share history entry {model.ShortId} cannot be retried: {model.RetryText}.");
                    return 1;
                }

                var queued = services.UploadQueue.EnqueueAsync(
                    ShareHistoryActions.ToRetryCaptureItem(entry),
                    entry.Destination,
                    CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"Queued retry {queued.ShortId} for share history entry {model.ShortId}: {queued.FileName} -> {queued.Destination}.");
                return 0;

            default:
                Console.Error.WriteLine("share-history action requires copy-url, copy-markdown, open, or retry.");
                return 2;
        }
    }

    private static bool TryParseHistoryDestination(string value, out ShareDestination destination)
    {
        destination = ShareService.ParseDestination(value);
        if (destination != ShareDestination.ClipboardImage)
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "clipboard" or "clipboard image" or "image clipboard";
    }

    private static (bool Valid, bool? Value) ParseHistoryStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return (true, null);
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "succeeded" or "success" or "successful" or "ok" or "passed" => (true, true),
            "failed" or "fail" or "failure" or "error" or "errored" => (true, false),
            "all" or "*" => (true, null),
            _ => (false, null)
        };
    }

    private static void ApplyShareOverrides(AppServices services, string[] args)
    {
        var settings = services.Settings;
        var exportFolder = Value(args, "--export-folder");
        if (!string.IsNullOrWhiteSpace(exportFolder))
        {
            settings.LocalExportFolder = exportFolder;
        }

        var script = Value(args, "--script");
        if (!string.IsNullOrWhiteSpace(script))
        {
            settings.CustomScriptCommand = script;
        }

        var webhook = Value(args, "--webhook-url");
        if (!string.IsNullOrWhiteSpace(webhook))
        {
            settings.CustomWebhookUrl = webhook;
        }

        var slackWebhook = Value(args, "--slack-webhook-url") ?? Value(args, "--slack-url");
        if (!string.IsNullOrWhiteSpace(slackWebhook))
        {
            settings.SlackWebhookUrl = slackWebhook;
        }

        var slackMessage = Value(args, "--slack-message") ?? Value(args, "--slack-message-template");
        if (!string.IsNullOrWhiteSpace(slackMessage))
        {
            settings.SlackMessageTemplate = slackMessage;
        }

        var discordWebhook = Value(args, "--discord-webhook-url") ?? Value(args, "--discord-url");
        if (!string.IsNullOrWhiteSpace(discordWebhook))
        {
            settings.DiscordWebhookUrl = discordWebhook;
        }

        var discordMessage = Value(args, "--discord-message") ?? Value(args, "--discord-message-template");
        if (!string.IsNullOrWhiteSpace(discordMessage))
        {
            settings.DiscordMessageTemplate = discordMessage;
        }

        var teamsWebhook = Value(args, "--teams-webhook-url") ?? Value(args, "--teams-url");
        if (!string.IsNullOrWhiteSpace(teamsWebhook))
        {
            settings.TeamsWebhookUrl = teamsWebhook;
        }

        var teamsMessage = Value(args, "--teams-message") ?? Value(args, "--teams-message-template");
        if (!string.IsNullOrWhiteSpace(teamsMessage))
        {
            settings.TeamsMessageTemplate = teamsMessage;
        }

        var s3Endpoint = Value(args, "--s3-endpoint");
        if (!string.IsNullOrWhiteSpace(s3Endpoint))
        {
            settings.S3Endpoint = s3Endpoint;
        }

        var s3Bucket = Value(args, "--s3-bucket");
        if (!string.IsNullOrWhiteSpace(s3Bucket))
        {
            settings.S3Bucket = s3Bucket;
        }

        var s3Region = Value(args, "--s3-region");
        if (!string.IsNullOrWhiteSpace(s3Region))
        {
            settings.S3Region = s3Region;
        }

        var s3Prefix = Value(args, "--s3-prefix");
        if (s3Prefix is not null)
        {
            settings.S3KeyPrefix = s3Prefix;
        }

        var s3PublicBaseUrl = Value(args, "--s3-public-base-url");
        if (!string.IsNullOrWhiteSpace(s3PublicBaseUrl))
        {
            settings.S3PublicBaseUrl = s3PublicBaseUrl;
        }

        var s3AccessKey = Value(args, "--s3-access-key");
        var s3SecretKey = Value(args, "--s3-secret-key");
        if (!string.IsNullOrWhiteSpace(s3AccessKey) && !string.IsNullOrWhiteSpace(s3SecretKey))
        {
            services.SecretStore.SaveS3Credentials(s3AccessKey, s3SecretKey);
        }

        var imgurEndpoint = Value(args, "--imgur-endpoint");
        if (!string.IsNullOrWhiteSpace(imgurEndpoint))
        {
            settings.ImgurApiEndpoint = imgurEndpoint;
        }

        var imgurClientId = Value(args, "--imgur-client-id");
        if (!string.IsNullOrWhiteSpace(imgurClientId))
        {
            services.SecretStore.SaveImgurClientId(imgurClientId);
        }

        var sftpExecutable = Value(args, "--sftp-executable");
        if (!string.IsNullOrWhiteSpace(sftpExecutable))
        {
            settings.SftpExecutablePath = sftpExecutable;
        }

        var sftpHost = Value(args, "--sftp-host");
        if (!string.IsNullOrWhiteSpace(sftpHost))
        {
            settings.SftpHost = sftpHost;
        }

        var sftpPort = Value(args, "--sftp-port");
        if (int.TryParse(sftpPort, out var parsedSftpPort) && parsedSftpPort > 0)
        {
            settings.SftpPort = parsedSftpPort;
        }

        var sftpUsername = Value(args, "--sftp-user") ?? Value(args, "--sftp-username");
        if (!string.IsNullOrWhiteSpace(sftpUsername))
        {
            settings.SftpUsername = sftpUsername;
        }

        var sftpRemoteDirectory = Value(args, "--sftp-remote-dir") ?? Value(args, "--sftp-directory");
        if (!string.IsNullOrWhiteSpace(sftpRemoteDirectory))
        {
            settings.SftpRemoteDirectory = sftpRemoteDirectory;
        }

        var sftpPrivateKey = Value(args, "--sftp-key") ?? Value(args, "--sftp-private-key");
        if (!string.IsNullOrWhiteSpace(sftpPrivateKey))
        {
            settings.SftpPrivateKeyPath = sftpPrivateKey;
        }

        var sftpPublicBaseUrl = Value(args, "--sftp-public-base-url");
        if (!string.IsNullOrWhiteSpace(sftpPublicBaseUrl))
        {
            settings.SftpPublicBaseUrl = sftpPublicBaseUrl;
        }

        var webDavBaseUrl = Value(args, "--webdav-base-url") ?? Value(args, "--webdav-url");
        if (!string.IsNullOrWhiteSpace(webDavBaseUrl))
        {
            settings.WebDavBaseUrl = webDavBaseUrl;
        }

        var webDavRemoteDirectory = Value(args, "--webdav-remote-dir") ?? Value(args, "--webdav-directory");
        if (!string.IsNullOrWhiteSpace(webDavRemoteDirectory))
        {
            settings.WebDavRemoteDirectory = webDavRemoteDirectory;
        }

        var webDavUsername = Value(args, "--webdav-user") ?? Value(args, "--webdav-username");
        if (!string.IsNullOrWhiteSpace(webDavUsername))
        {
            settings.WebDavUsername = webDavUsername;
        }

        var webDavPassword = Value(args, "--webdav-password");
        if (!string.IsNullOrWhiteSpace(webDavPassword))
        {
            services.SecretStore.SaveWebDavPassword(webDavPassword);
        }

        var webDavPublicBaseUrl = Value(args, "--webdav-public-base-url");
        if (!string.IsNullOrWhiteSpace(webDavPublicBaseUrl))
        {
            settings.WebDavPublicBaseUrl = webDavPublicBaseUrl;
        }

        var ftpHost = Value(args, "--ftp-host") ?? Value(args, "--ftps-host");
        if (!string.IsNullOrWhiteSpace(ftpHost))
        {
            settings.FtpHost = ftpHost;
        }

        var ftpPort = Value(args, "--ftp-port") ?? Value(args, "--ftps-port");
        if (int.TryParse(ftpPort, out var parsedFtpPort) && parsedFtpPort > 0)
        {
            settings.FtpPort = parsedFtpPort;
        }

        if (Has(args, "--ftps") || Has(args, "--ftp-use-ftps"))
        {
            settings.FtpUseFtps = true;
        }

        if (Has(args, "--ftp-plain"))
        {
            settings.FtpUseFtps = false;
        }

        var ftpUsername = Value(args, "--ftp-user") ?? Value(args, "--ftp-username") ?? Value(args, "--ftps-user");
        if (!string.IsNullOrWhiteSpace(ftpUsername))
        {
            settings.FtpUsername = ftpUsername;
        }

        var ftpPassword = Value(args, "--ftp-password") ?? Value(args, "--ftps-password");
        if (!string.IsNullOrWhiteSpace(ftpPassword))
        {
            services.SecretStore.SaveFtpPassword(ftpPassword);
        }

        var ftpRemoteDirectory = Value(args, "--ftp-remote-dir") ?? Value(args, "--ftp-directory") ?? Value(args, "--ftps-remote-dir");
        if (!string.IsNullOrWhiteSpace(ftpRemoteDirectory))
        {
            settings.FtpRemoteDirectory = ftpRemoteDirectory;
        }

        var ftpPublicBaseUrl = Value(args, "--ftp-public-base-url") ?? Value(args, "--ftps-public-base-url");
        if (!string.IsNullOrWhiteSpace(ftpPublicBaseUrl))
        {
            settings.FtpPublicBaseUrl = ftpPublicBaseUrl;
        }

        var cloudinaryApiBaseUrl = Value(args, "--cloudinary-api-base-url");
        if (!string.IsNullOrWhiteSpace(cloudinaryApiBaseUrl))
        {
            settings.CloudinaryApiBaseUrl = cloudinaryApiBaseUrl;
        }

        var cloudinaryCloudName = Value(args, "--cloudinary-cloud-name") ?? Value(args, "--cloudinary-cloud");
        if (!string.IsNullOrWhiteSpace(cloudinaryCloudName))
        {
            settings.CloudinaryCloudName = cloudinaryCloudName;
        }

        var cloudinaryResourceType = Value(args, "--cloudinary-resource-type");
        if (!string.IsNullOrWhiteSpace(cloudinaryResourceType))
        {
            settings.CloudinaryResourceType = cloudinaryResourceType;
        }

        var cloudinaryUploadPreset = Value(args, "--cloudinary-upload-preset") ?? Value(args, "--cloudinary-preset");
        if (!string.IsNullOrWhiteSpace(cloudinaryUploadPreset))
        {
            settings.CloudinaryUploadPreset = cloudinaryUploadPreset;
        }

        var cloudinaryFolder = Value(args, "--cloudinary-folder");
        if (!string.IsNullOrWhiteSpace(cloudinaryFolder))
        {
            settings.CloudinaryFolder = cloudinaryFolder;
        }

        var cloudinaryApiKey = Value(args, "--cloudinary-api-key");
        var cloudinaryApiSecret = Value(args, "--cloudinary-api-secret");
        if (!string.IsNullOrWhiteSpace(cloudinaryApiKey) && !string.IsNullOrWhiteSpace(cloudinaryApiSecret))
        {
            services.SecretStore.SaveCloudinaryCredentials(cloudinaryApiKey, cloudinaryApiSecret);
        }

        var dropboxContentApiBaseUrl = Value(args, "--dropbox-content-api-base-url");
        if (!string.IsNullOrWhiteSpace(dropboxContentApiBaseUrl))
        {
            settings.DropboxContentApiBaseUrl = dropboxContentApiBaseUrl;
        }

        var dropboxApiBaseUrl = Value(args, "--dropbox-api-base-url");
        if (!string.IsNullOrWhiteSpace(dropboxApiBaseUrl))
        {
            settings.DropboxApiBaseUrl = dropboxApiBaseUrl;
        }

        var dropboxFolder = Value(args, "--dropbox-folder") ?? Value(args, "--dropbox-remote-folder");
        if (!string.IsNullOrWhiteSpace(dropboxFolder))
        {
            settings.DropboxRemoteFolder = dropboxFolder;
        }

        var dropboxAccessToken = Value(args, "--dropbox-access-token");
        if (!string.IsNullOrWhiteSpace(dropboxAccessToken))
        {
            services.SecretStore.SaveDropboxAccessToken(dropboxAccessToken);
        }

        var googleDriveUploadApiBaseUrl = Value(args, "--google-drive-upload-api-base-url");
        if (!string.IsNullOrWhiteSpace(googleDriveUploadApiBaseUrl))
        {
            settings.GoogleDriveUploadApiBaseUrl = googleDriveUploadApiBaseUrl;
        }

        var googleDriveApiBaseUrl = Value(args, "--google-drive-api-base-url");
        if (!string.IsNullOrWhiteSpace(googleDriveApiBaseUrl))
        {
            settings.GoogleDriveApiBaseUrl = googleDriveApiBaseUrl;
        }

        var googleDriveFolderId = Value(args, "--google-drive-folder-id") ?? Value(args, "--gdrive-folder-id");
        if (!string.IsNullOrWhiteSpace(googleDriveFolderId))
        {
            settings.GoogleDriveFolderId = googleDriveFolderId;
        }

        if (Has(args, "--google-drive-anyone-reader") || Has(args, "--google-drive-create-anyone-reader"))
        {
            settings.GoogleDriveCreateAnyoneReaderLink = true;
        }

        var googleDriveAccessToken = Value(args, "--google-drive-access-token") ?? Value(args, "--gdrive-access-token");
        if (!string.IsNullOrWhiteSpace(googleDriveAccessToken))
        {
            services.SecretStore.SaveGoogleDriveAccessToken(googleDriveAccessToken);
        }

        var googlePhotosUploadApiBaseUrl = Value(args, "--google-photos-upload-api-base-url");
        if (!string.IsNullOrWhiteSpace(googlePhotosUploadApiBaseUrl))
        {
            settings.GooglePhotosUploadApiBaseUrl = googlePhotosUploadApiBaseUrl;
        }

        var googlePhotosApiBaseUrl = Value(args, "--google-photos-api-base-url");
        if (!string.IsNullOrWhiteSpace(googlePhotosApiBaseUrl))
        {
            settings.GooglePhotosApiBaseUrl = googlePhotosApiBaseUrl;
        }

        var googlePhotosAlbumId = Value(args, "--google-photos-album-id") ?? Value(args, "--gphotos-album-id");
        if (!string.IsNullOrWhiteSpace(googlePhotosAlbumId))
        {
            settings.GooglePhotosAlbumId = googlePhotosAlbumId;
        }

        var googlePhotosDescription = Value(args, "--google-photos-description-template") ?? Value(args, "--gphotos-description-template");
        if (!string.IsNullOrWhiteSpace(googlePhotosDescription))
        {
            settings.GooglePhotosDescriptionTemplate = googlePhotosDescription;
        }

        var googlePhotosAccessToken = Value(args, "--google-photos-access-token") ?? Value(args, "--gphotos-access-token");
        if (!string.IsNullOrWhiteSpace(googlePhotosAccessToken))
        {
            services.SecretStore.SaveGooglePhotosAccessToken(googlePhotosAccessToken);
        }

        var oneDriveGraphApiBaseUrl = Value(args, "--onedrive-graph-api-base-url");
        if (!string.IsNullOrWhiteSpace(oneDriveGraphApiBaseUrl))
        {
            settings.OneDriveGraphApiBaseUrl = oneDriveGraphApiBaseUrl;
        }

        var oneDriveRemoteFolder = Value(args, "--onedrive-folder") ?? Value(args, "--onedrive-remote-folder");
        if (!string.IsNullOrWhiteSpace(oneDriveRemoteFolder))
        {
            settings.OneDriveRemoteFolder = oneDriveRemoteFolder;
        }

        if (Has(args, "--onedrive-anonymous-link") || Has(args, "--onedrive-create-anonymous-link"))
        {
            settings.OneDriveCreateAnonymousViewLink = true;
        }

        var oneDriveAccessToken = Value(args, "--onedrive-access-token");
        if (!string.IsNullOrWhiteSpace(oneDriveAccessToken))
        {
            services.SecretStore.SaveOneDriveAccessToken(oneDriveAccessToken);
        }

        var youTubeUploadApiBaseUrl = Value(args, "--youtube-upload-api-base-url");
        if (!string.IsNullOrWhiteSpace(youTubeUploadApiBaseUrl))
        {
            settings.YouTubeUploadApiBaseUrl = youTubeUploadApiBaseUrl;
        }

        var youTubeTitle = Value(args, "--youtube-title-template") ?? Value(args, "--youtube-title");
        if (!string.IsNullOrWhiteSpace(youTubeTitle))
        {
            settings.YouTubeTitleTemplate = youTubeTitle;
        }

        var youTubeDescription = Value(args, "--youtube-description-template") ?? Value(args, "--youtube-description");
        if (!string.IsNullOrWhiteSpace(youTubeDescription))
        {
            settings.YouTubeDescriptionTemplate = youTubeDescription;
        }

        var youTubePrivacy = Value(args, "--youtube-privacy") ?? Value(args, "--youtube-privacy-status");
        if (!string.IsNullOrWhiteSpace(youTubePrivacy))
        {
            settings.YouTubePrivacyStatus = youTubePrivacy;
        }

        var youTubeCategoryId = Value(args, "--youtube-category-id");
        if (!string.IsNullOrWhiteSpace(youTubeCategoryId))
        {
            settings.YouTubeCategoryId = youTubeCategoryId;
        }

        var youTubeAccessToken = Value(args, "--youtube-access-token");
        if (!string.IsNullOrWhiteSpace(youTubeAccessToken))
        {
            services.SecretStore.SaveYouTubeAccessToken(youTubeAccessToken);
        }

        var oneNoteGraphApiBaseUrl = Value(args, "--onenote-graph-api-base-url");
        if (!string.IsNullOrWhiteSpace(oneNoteGraphApiBaseUrl))
        {
            settings.OneNoteGraphApiBaseUrl = oneNoteGraphApiBaseUrl;
        }

        var oneNoteSectionId = Value(args, "--onenote-section-id");
        if (!string.IsNullOrWhiteSpace(oneNoteSectionId))
        {
            settings.OneNoteSectionId = oneNoteSectionId;
        }

        var oneNotePageTitle = Value(args, "--onenote-page-title-template") ?? Value(args, "--onenote-title-template");
        if (!string.IsNullOrWhiteSpace(oneNotePageTitle))
        {
            settings.OneNotePageTitleTemplate = oneNotePageTitle;
        }

        var oneNoteAccessToken = Value(args, "--onenote-access-token");
        if (!string.IsNullOrWhiteSpace(oneNoteAccessToken))
        {
            services.SecretStore.SaveOneNoteAccessToken(oneNoteAccessToken);
        }

        var linearEndpoint = Value(args, "--linear-endpoint") ?? Value(args, "--linear-graphql-endpoint");
        if (!string.IsNullOrWhiteSpace(linearEndpoint))
        {
            settings.LinearGraphqlEndpoint = linearEndpoint;
        }

        var linearTeamId = Value(args, "--linear-team-id");
        if (!string.IsNullOrWhiteSpace(linearTeamId))
        {
            settings.LinearTeamId = linearTeamId;
        }

        var linearIssueId = Value(args, "--linear-issue-id");
        if (!string.IsNullOrWhiteSpace(linearIssueId))
        {
            settings.LinearIssueId = linearIssueId;
        }

        var linearTitle = Value(args, "--linear-title") ?? Value(args, "--linear-title-template");
        if (!string.IsNullOrWhiteSpace(linearTitle))
        {
            settings.LinearIssueTitleTemplate = linearTitle;
        }

        if (Has(args, "--linear-create-attachment"))
        {
            settings.LinearCreateAttachment = true;
        }

        if (Has(args, "--linear-no-attachment"))
        {
            settings.LinearCreateAttachment = false;
        }

        var linearApiKey = Value(args, "--linear-api-key");
        if (!string.IsNullOrWhiteSpace(linearApiKey))
        {
            settings.LinearUseOAuthBearerToken = false;
            services.SecretStore.SaveLinearCredential(linearApiKey);
        }

        var linearOAuthToken = Value(args, "--linear-oauth-token") ?? Value(args, "--linear-access-token");
        if (!string.IsNullOrWhiteSpace(linearOAuthToken))
        {
            settings.LinearUseOAuthBearerToken = true;
            services.SecretStore.SaveLinearCredential(linearOAuthToken);
        }

        var githubApiBaseUrl = Value(args, "--github-api-base-url") ?? Value(args, "--github-api-url");
        if (!string.IsNullOrWhiteSpace(githubApiBaseUrl))
        {
            settings.GitHubApiBaseUrl = githubApiBaseUrl;
        }

        var githubRepository = Value(args, "--github-repo") ?? Value(args, "--github-repository");
        if (!string.IsNullOrWhiteSpace(githubRepository))
        {
            settings.GitHubRepository = githubRepository;
        }

        var githubTitle = Value(args, "--github-title") ?? Value(args, "--github-title-template");
        if (!string.IsNullOrWhiteSpace(githubTitle))
        {
            settings.GitHubIssueTitleTemplate = githubTitle;
        }

        var githubLabels = Value(args, "--github-labels");
        if (githubLabels is not null)
        {
            settings.GitHubLabels = githubLabels;
        }

        var githubAssignees = Value(args, "--github-assignees");
        if (githubAssignees is not null)
        {
            settings.GitHubAssignees = githubAssignees;
        }

        var githubToken = Value(args, "--github-token") ?? Value(args, "--github-pat");
        if (!string.IsNullOrWhiteSpace(githubToken))
        {
            services.SecretStore.SaveGitHubToken(githubToken);
        }

        var jiraBaseUrl = Value(args, "--jira-base-url") ?? Value(args, "--jira-url");
        if (!string.IsNullOrWhiteSpace(jiraBaseUrl))
        {
            settings.JiraBaseUrl = jiraBaseUrl;
        }

        var jiraProjectKey = Value(args, "--jira-project-key") ?? Value(args, "--jira-project");
        if (!string.IsNullOrWhiteSpace(jiraProjectKey))
        {
            settings.JiraProjectKey = jiraProjectKey;
        }

        var jiraIssueType = Value(args, "--jira-issue-type");
        if (!string.IsNullOrWhiteSpace(jiraIssueType))
        {
            settings.JiraIssueType = jiraIssueType;
        }

        var jiraSummary = Value(args, "--jira-summary") ?? Value(args, "--jira-summary-template");
        if (!string.IsNullOrWhiteSpace(jiraSummary))
        {
            settings.JiraSummaryTemplate = jiraSummary;
        }

        var jiraLabels = Value(args, "--jira-labels");
        if (jiraLabels is not null)
        {
            settings.JiraLabels = jiraLabels;
        }

        var jiraEmail = Value(args, "--jira-email") ?? Value(args, "--jira-account-email");
        if (!string.IsNullOrWhiteSpace(jiraEmail))
        {
            settings.JiraAccountEmail = jiraEmail;
        }

        var jiraApiToken = Value(args, "--jira-api-token") ?? Value(args, "--jira-token");
        if (!string.IsNullOrWhiteSpace(jiraApiToken))
        {
            services.SecretStore.SaveJiraApiToken(jiraApiToken);
        }

        var azureDevOpsBaseUrl = Value(args, "--azure-devops-base-url") ?? Value(args, "--ado-base-url");
        if (!string.IsNullOrWhiteSpace(azureDevOpsBaseUrl))
        {
            settings.AzureDevOpsBaseUrl = azureDevOpsBaseUrl;
        }

        var azureDevOpsOrganization = Value(args, "--azure-devops-org")
            ?? Value(args, "--azure-devops-organization")
            ?? Value(args, "--ado-org");
        if (!string.IsNullOrWhiteSpace(azureDevOpsOrganization))
        {
            settings.AzureDevOpsOrganization = azureDevOpsOrganization;
        }

        var azureDevOpsProject = Value(args, "--azure-devops-project") ?? Value(args, "--ado-project");
        if (!string.IsNullOrWhiteSpace(azureDevOpsProject))
        {
            settings.AzureDevOpsProject = azureDevOpsProject;
        }

        var azureDevOpsWorkItemType = Value(args, "--azure-devops-type")
            ?? Value(args, "--azure-devops-work-item-type")
            ?? Value(args, "--ado-type");
        if (!string.IsNullOrWhiteSpace(azureDevOpsWorkItemType))
        {
            settings.AzureDevOpsWorkItemType = azureDevOpsWorkItemType;
        }

        var azureDevOpsTitle = Value(args, "--azure-devops-title")
            ?? Value(args, "--azure-devops-title-template")
            ?? Value(args, "--ado-title");
        if (!string.IsNullOrWhiteSpace(azureDevOpsTitle))
        {
            settings.AzureDevOpsTitleTemplate = azureDevOpsTitle;
        }

        var azureDevOpsTags = Value(args, "--azure-devops-tags") ?? Value(args, "--ado-tags");
        if (azureDevOpsTags is not null)
        {
            settings.AzureDevOpsTags = azureDevOpsTags;
        }

        var azureDevOpsAssignedTo = Value(args, "--azure-devops-assigned-to") ?? Value(args, "--ado-assigned-to");
        if (azureDevOpsAssignedTo is not null)
        {
            settings.AzureDevOpsAssignedTo = azureDevOpsAssignedTo;
        }

        var azureDevOpsPat = Value(args, "--azure-devops-pat")
            ?? Value(args, "--azure-devops-token")
            ?? Value(args, "--ado-pat");
        if (!string.IsNullOrWhiteSpace(azureDevOpsPat))
        {
            services.SecretStore.SaveAzureDevOpsPat(azureDevOpsPat);
        }
    }

    private static async Task<int> MetadataAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("metadata requires: strip <input> --output <output>, or inspect <input> [--json] [--copy]");
            return 2;
        }

        if (args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            return await InspectAsync(services, args.Skip(1).ToArray());
        }

        if (!args[0].Equals("strip", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("metadata requires: strip <input> --output <output>, or inspect <input> [--json] [--copy]");
            return 2;
        }

        var input = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("metadata strip requires input and --output.");
            return 2;
        }

        input = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
        output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
        await MetadataService.StripImageMetadataToAsync(input, output);
        Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> InspectAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("inspect requires a file path.");
            return 2;
        }

        var result = await services.FileInspector.InspectAsync(target);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        var report = FileInspectorService.FormatReport(result);
        Console.WriteLine(report);
        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(report);
            Console.Error.WriteLine("copied=true");
        }

        return 0;
    }

    private static async Task<int> HashAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("hash requires a file path.");
            return 2;
        }

        var result = await services.FileInspector.InspectAsync(target);
        var algorithm = (Value(args, "--algorithm") ?? Value(args, "--algo") ?? "sha256").Trim().ToLowerInvariant();
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result.Hashes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        string output;
        if (algorithm == "all")
        {
            output = string.Join(
                Environment.NewLine,
                new[] { "sha256", "sha1", "md5" }
                    .Where(result.Hashes.ContainsKey)
                    .Select(name => $"{name}={result.Hashes[name]}"));
        }
        else if (result.Hashes.TryGetValue(algorithm, out var hash))
        {
            output = hash;
        }
        else
        {
            Console.Error.WriteLine("Unsupported hash algorithm. Use sha256, sha1, md5, or all.");
            return 2;
        }

        Console.WriteLine(output);
        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(output);
            Console.Error.WriteLine("copied=true");
        }

        return 0;
    }

    private static async Task<int> ClipboardAsync(AppServices services, string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("clipboard requires: import [--json]");
            return 2;
        }

        var result = await services.ClipboardImports.ImportAsync();
        if (Has(args, "--json"))
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
            return result.Succeeded ? 0 : 1;
        }

        Console.WriteLine(result.Message);
        foreach (var item in result.Items)
        {
            Console.WriteLine(item.FilePath);
            Console.Error.WriteLine($"workspace-id={item.Id}");
        }

        return result.Succeeded ? 0 : 1;
    }

    private static int Search(AppServices services, string[] args)
    {
        var query = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("search requires a query.");
            return 2;
        }

        var limitText = Value(args, "--limit");
        var limit = int.TryParse(limitText, out var parsedLimit) ? parsedLimit : 20;
        var hits = services.WorkspaceIndex.Search(query, limit);

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(hits, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return hits.Count == 0 ? 1 : 0;
        }

        Console.WriteLine(hits.Count == 0
            ? "No matching captures found."
            : $"Found {hits.Count} matching capture(s).");

        foreach (var hit in hits)
        {
            Console.WriteLine($"{hit.CreatedAt} {hit.Kind} {hit.FileName}");
            Console.WriteLine(hit.FilePath);
            if (!string.IsNullOrWhiteSpace(hit.Snippet))
            {
                Console.WriteLine(hit.Snippet);
            }

            Console.WriteLine();
        }

        return hits.Count == 0 ? 1 : 0;
    }

    private static async Task<int> WorkflowsAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("workflows requires: export, import, validate, list-rules, templates, add-template, status, dry-run, dry-run-script, dry-run-webhook, or run.");
            return 2;
        }

        return args[0].ToLowerInvariant() switch
        {
            "export" => await ExportWorkflowAsync(services, args.Skip(1).ToArray()),
            "import" => await ImportWorkflowAsync(services, args.Skip(1).ToArray()),
            "validate" or "check" => await ValidateWorkflowAsync(services, args.Skip(1).ToArray()),
            "list-rules" or "rules" or "list" => ListWorkflowRules(services, args.Skip(1).ToArray()),
            "templates" or "list-templates" => ListWorkflowTemplates(args.Skip(1).ToArray()),
            "add-template" or "template-add" or "apply-template" => AddWorkflowTemplate(services, args.Skip(1).ToArray()),
            "dry-run" or "dryrun" => await RunWorkflowRulesAsync(services, args.Skip(1).ToArray(), forceDryRun: true),
            "dry-run-script" or "dryrun-script" => DryRunWorkflowAction(services, args.Skip(1).ToArray(), webhook: false),
            "dry-run-webhook" or "dryrun-webhook" => DryRunWorkflowAction(services, args.Skip(1).ToArray(), webhook: true),
            "run" => await RunWorkflowRulesAsync(services, args.Skip(1).ToArray(), forceDryRun: false),
            "status" => WorkflowStatus(services, args.Skip(1).ToArray()),
            _ => WorkflowUsage(args[0])
        };
    }

    private static async Task<int> ExportWorkflowAsync(AppServices services, string[] args)
    {
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("workflows export requires --output.");
            return 2;
        }

        var result = await services.WorkflowProfiles.ExportAsync(
            output,
            Value(args, "--name"),
            includeSensitiveValues: Has(args, "--include-sensitive"));
        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            Console.WriteLine(result.Path);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ImportWorkflowAsync(AppServices services, string[] args)
    {
        var input = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("workflows import requires a profile file path.");
            return 2;
        }

        var result = await services.WorkflowProfiles.ImportAsync(
            input,
            new WorkflowProfileImportOptions
            {
                IncludeSensitiveValues = Has(args, "--include-sensitive"),
                ReplaceAutomationRules = !Has(args, "--merge")
            });
        Console.WriteLine(result.Message);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ValidateWorkflowAsync(AppServices services, string[] args)
    {
        var input = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("workflows validate requires a profile file path.");
            return 2;
        }

        var result = await services.WorkflowProfiles.ValidateAsync(input);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return result.Succeeded ? 0 : 1;
        }

        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            Console.WriteLine(result.Path);
        }

        foreach (var error in result.Errors)
        {
            Console.WriteLine($"ERROR: {error}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        return result.Succeeded ? 0 : 1;
    }

    private static int ListWorkflowRules(AppServices services, string[] args)
    {
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(services.Settings.AutomationRules, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return 0;
        }

        if (services.Settings.AutomationRules.Count == 0)
        {
            Console.WriteLine("No automation rules configured.");
            return 0;
        }

        foreach (var rule in services.Settings.AutomationRules)
        {
            var state = rule.IsEnabled ? "enabled" : "disabled";
            Console.WriteLine($"{rule.Id}  {rule.Name}  [{state}]");
            Console.WriteLine($"  trigger: {rule.Trigger}");
            Console.WriteLine($"  actions: {(rule.Actions.Count == 0 ? "none" : string.Join(", ", rule.Actions))}");
            var conditions = DescribeRuleConditions(rule).ToList();
            Console.WriteLine($"  conditions: {(conditions.Count == 0 ? "none" : string.Join("; ", conditions))}");
        }

        return 0;
    }

    private static int WorkflowStatus(AppServices services, string[] args)
    {
        var rules = services.Settings.AutomationRules;
        var payload = new
        {
            TotalRules = rules.Count,
            EnabledRules = rules.Count(rule => rule.IsEnabled),
            DisabledRules = rules.Count(rule => !rule.IsEnabled),
            ByTrigger = rules
                .GroupBy(rule => rule.Trigger)
                .OrderBy(group => group.Key.ToString())
                .Select(group => new
                {
                    Trigger = group.Key,
                    Total = group.Count(),
                    Enabled = group.Count(rule => rule.IsEnabled)
                })
                .ToList(),
            WatchFoldersEnabled = services.Settings.EnableWatchFolders,
            WatchFolderCount = services.Settings.WatchFolders.Count
        };

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return 0;
        }

        Console.WriteLine($"Automation rules: {payload.EnabledRules} enabled / {payload.TotalRules} total");
        foreach (var trigger in payload.ByTrigger)
        {
            Console.WriteLine($"{trigger.Trigger}: {trigger.Enabled} enabled / {trigger.Total} total");
        }

        Console.WriteLine($"Watch folders: {(payload.WatchFoldersEnabled ? "enabled" : "disabled")} ({payload.WatchFolderCount} configured)");
        return 0;
    }

    private static int ListWorkflowTemplates(string[] args)
    {
        var templates = WorkflowRuleTemplateCatalog.ListTemplates();
        var selected = Value(args, "--id") ?? Value(args, "--template") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var template = WorkflowRuleTemplateCatalog.Find(selected);
            if (template is null)
            {
                Console.Error.WriteLine($"Unknown workflow template: {selected}");
                return 2;
            }

            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                }));
                return 0;
            }

            PrintWorkflowTemplate(template);
            return 0;
        }

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(templates, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return 0;
        }

        Console.WriteLine("Workflow templates");
        foreach (var template in templates)
        {
            Console.WriteLine($"{template.Id}  {template.Name}  [{template.Category}]");
            Console.WriteLine($"  trigger: {template.Trigger}");
            Console.WriteLine($"  actions: {template.ActionSummary}");
            Console.WriteLine($"  {template.Description}");
        }

        return 0;
    }

    private static int AddWorkflowTemplate(AppServices services, string[] args)
    {
        var templateId = Value(args, "--id") ?? Value(args, "--template") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(templateId))
        {
            Console.Error.WriteLine("workflows add-template requires a template id. Use workflows templates to list ids.");
            return 2;
        }

        var enabled = Has(args, "--enable") || Has(args, "--enabled");
        var replace = Has(args, "--replace");
        var succeeded = WorkflowRuleTemplateCatalog.TryAddTemplateRule(
            services.Settings,
            templateId,
            enabled,
            replace,
            out var rule,
            out var message);
        if (!succeeded || rule is null)
        {
            Console.Error.WriteLine(message);
            return 2;
        }

        services.SaveSettings();

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(rule, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
        }
        else
        {
            Console.WriteLine(message);
            Console.WriteLine($"State: {(rule.IsEnabled ? "enabled" : "disabled for review")}");
            Console.WriteLine($"Trigger: {rule.Trigger}");
            Console.WriteLine($"Actions: {string.Join(", ", rule.Actions)}");
        }

        return 0;
    }

    private static void PrintWorkflowTemplate(WorkflowRuleTemplate template)
    {
        Console.WriteLine($"{template.Id}  {template.Name}  [{template.Category}]");
        Console.WriteLine($"Trigger: {template.Trigger}");
        Console.WriteLine($"Actions: {template.ActionSummary}");
        Console.WriteLine(template.Description);
        foreach (var note in template.Notes)
        {
            Console.WriteLine($"- {note}");
        }
    }

    private static async Task<int> RunWorkflowRulesAsync(AppServices services, string[] args, bool forceDryRun)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("workflows run/dry-run requires a workspace id, file name, or file path.");
            return 2;
        }

        if (!TryParseAutomationTrigger(Value(args, "--trigger") ?? Value(args, "-t"), out var trigger))
        {
            Console.Error.WriteLine("workflows run/dry-run requires --trigger capture-created|capture-edited|watch|recording|ocr|upload|ai.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Capture not found: {target}");
            return 2;
        }

        var dryRun = forceDryRun || Has(args, "--dry-run");
        var result = await services.Automation.RunRulesAsync(trigger, item, dryRun);

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
        }
        else
        {
            PrintAutomationRunResult(result, includeSkipped: dryRun || Has(args, "--include-skipped") || Has(args, "--verbose"));
        }

        return result.Succeeded ? 0 : 1;
    }

    private static int DryRunWorkflowAction(AppServices services, string[] args, bool webhook)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine(webhook
                ? "workflows dry-run-webhook requires a workspace id, file name, or file path."
                : "workflows dry-run-script requires a workspace id, file name, or file path.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Capture not found: {target}");
            return 2;
        }

        var plan = webhook
            ? services.WorkflowDryRuns.PlanCustomWebhook(
                item,
                Value(args, "--webhook-url") ?? Value(args, "--url"))
            : services.WorkflowDryRuns.PlanCustomScript(
                item,
                Value(args, "--script") ?? Value(args, "--command"));

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
        }
        else
        {
            PrintWorkflowActionDryRunPlan(plan, includeMetadata: Has(args, "--verbose") || Has(args, "--include-metadata"));
        }

        return plan.WouldExecute ? 0 : 1;
    }

    private static int WorkflowUsage(string command)
    {
        Console.Error.WriteLine($"Unknown workflows command: {command}");
        Console.Error.WriteLine("workflows requires: export --output <profile.json>, import <profile.json>, validate <profile.json>, list-rules, templates, add-template <id>, status, dry-run <target> --trigger <trigger>, dry-run-script <target>, dry-run-webhook <target>, or run <target> --trigger <trigger>.");
        return 2;
    }

    private static IEnumerable<string> DescribeRuleConditions(AutomationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.SourceAppContains))
        {
            yield return $"source app contains '{rule.SourceAppContains}'";
        }

        if (!string.IsNullOrWhiteSpace(rule.WindowTitleContains))
        {
            yield return $"window title contains '{rule.WindowTitleContains}'";
        }

        if (!string.IsNullOrWhiteSpace(rule.CaptureKind))
        {
            yield return $"capture kind is '{rule.CaptureKind}'";
        }

        if (!string.IsNullOrWhiteSpace(rule.MonitorContains))
        {
            yield return $"monitor contains '{rule.MonitorContains}'";
        }

        if (!string.IsNullOrWhiteSpace(rule.HotkeyProfile))
        {
            yield return $"hotkey profile is '{rule.HotkeyProfile}'";
        }

        if (!string.IsNullOrWhiteSpace(rule.FileExtension))
        {
            yield return $"file extension is '{rule.FileExtension}'";
        }

        if (rule.MinFileSizeBytes is { } minBytes)
        {
            yield return $"bytes >= {minBytes}";
        }

        if (rule.MaxFileSizeBytes is { } maxBytes)
        {
            yield return $"bytes <= {maxBytes}";
        }

        if (!string.IsNullOrWhiteSpace(rule.OcrContains))
        {
            yield return $"OCR contains '{rule.OcrContains}'";
        }

        if (rule.RequiresSensitiveData is { } requiresSensitiveData)
        {
            yield return requiresSensitiveData ? "requires sensitive OCR/text finding" : "excludes sensitive OCR/text finding";
        }
    }

    private static bool TryParseAutomationTrigger(string? value, out AutomationTrigger trigger)
    {
        trigger = AutomationTrigger.CaptureCreated;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        switch (normalized)
        {
            case "capture":
            case "capture-created":
            case "capturecreated":
                trigger = AutomationTrigger.CaptureCreated;
                return true;
            case "capture-edited":
            case "captureedited":
            case "edit":
            case "edited":
                trigger = AutomationTrigger.CaptureEdited;
                return true;
            case "watch":
            case "watch-folder":
            case "file-added":
            case "file-added-to-watch-folder":
                trigger = AutomationTrigger.FileAddedToWatchFolder;
                return true;
            case "recording":
            case "recording-completed":
            case "record":
                trigger = AutomationTrigger.RecordingCompleted;
                return true;
            case "ocr":
            case "ocr-completed":
                trigger = AutomationTrigger.OcrCompleted;
                return true;
            case "upload":
            case "upload-completed":
            case "share":
                trigger = AutomationTrigger.UploadCompleted;
                return true;
            case "ai":
            case "ai-action":
            case "ai-action-completed":
                trigger = AutomationTrigger.AiActionCompleted;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out trigger);
        }
    }

    private static void PrintAutomationRunResult(AutomationRunResult result, bool includeSkipped)
    {
        Console.WriteLine(result.Summary);
        foreach (var rule in result.Rules)
        {
            Console.WriteLine($"Matched: {rule.Evaluation.RuleName} ({rule.Evaluation.RuleId})");
            foreach (var action in rule.Actions)
            {
                var status = action.Executed ? "executed" : "planned";
                Console.WriteLine($"  {status}: {action.Action} - {action.Message}");
                if (!string.IsNullOrWhiteSpace(action.OutputPath))
                {
                    Console.WriteLine($"    output: {action.OutputPath}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(result.MarkdownLogPath))
        {
            Console.WriteLine($"Log: {result.MarkdownLogPath}");
        }

        if (!string.IsNullOrWhiteSpace(result.LogPath))
        {
            Console.WriteLine($"Log JSON: {result.LogPath}");
        }

        if (!includeSkipped)
        {
            return;
        }

        foreach (var evaluation in result.Evaluations.Where(evaluation => !evaluation.Matches))
        {
            Console.WriteLine($"Skipped: {evaluation.RuleName} ({evaluation.RuleId})");
            foreach (var reason in evaluation.Reasons)
            {
                Console.WriteLine($"  {reason}");
            }
        }
    }

    private static void PrintWorkflowActionDryRunPlan(WorkflowActionDryRunPlan plan, bool includeMetadata)
    {
        Console.WriteLine(plan.Message);
        Console.WriteLine($"Action: {plan.Action}");
        Console.WriteLine($"Configured: {plan.Configured}");
        Console.WriteLine($"Would execute: {plan.WouldExecute}");
        if (!string.IsNullOrWhiteSpace(plan.Target))
        {
            Console.WriteLine($"Target: {plan.Target}");
        }

        if (!string.IsNullOrWhiteSpace(plan.PayloadSummary))
        {
            Console.WriteLine($"Payload: {plan.PayloadSummary}");
        }

        if (!string.IsNullOrWhiteSpace(plan.ResolvedCommand))
        {
            Console.WriteLine("Resolved command:");
            Console.WriteLine(plan.ResolvedCommand);
        }

        if (!includeMetadata)
        {
            return;
        }

        Console.WriteLine("Metadata preview:");
        foreach (var pair in plan.MetadataPreview.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }

    private static async Task<int> PluginsAsync(AppServices services, string[] args)
    {
        if (args.Length > 0 &&
            args[0].Equals("registry", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 ||
                !args[1].Equals("validate", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("plugins registry requires: validate <registry.json-or-url> [--json].");
                return 2;
            }

            var registry = Value(args, "--registry") ??
                args.Skip(2).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins registry validate requires a registry path or URL.");
                return 2;
            }

            var result = await services.RemotePlugins.ValidateRegistryAsync(registry);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("install-plan", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("plan-install", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("remote-plan", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var registry = Value(args, "--registry");
            if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins install-plan requires a plugin id and --registry <registry.json-or-url>.");
                return 2;
            }

            var result = await services.RemotePlugins.PlanInstallAsync(pluginId, registry, stagePackage: false);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("stage", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("stage-package", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("install", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var registry = Value(args, "--registry");
            if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins stage requires a plugin id and --registry <registry.json-or-url>.");
                return 2;
            }

            var result = await services.RemotePlugins.PlanInstallAsync(pluginId, registry, stagePackage: true);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("apply-updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("stage-updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("update-apply", StringComparison.OrdinalIgnoreCase)))
        {
            var registry = Value(args, "--registry") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins apply-updates requires --registry <registry.json-or-url>.");
                return 2;
            }

            var result = await services.RemotePlugins.ApplyUpdatesAsync(
                registry,
                installStaged: Has(args, "--install-staged") || Has(args, "--install"),
                replaceExisting: Has(args, "--replace"));
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("update-check", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("check-updates", StringComparison.OrdinalIgnoreCase)))
        {
            var registry = Value(args, "--registry") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins updates requires --registry <registry.json-or-url>.");
                return 2;
            }

            var result = await services.RemotePlugins.SummarizeUpdatesAsync(registry);
            PrintPluginUpdateSummaryResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("schedule-updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("schedule-update", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("scheduler-plan", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("task-scheduler-plan", StringComparison.OrdinalIgnoreCase)))
        {
            var registry = Value(args, "--registry") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins schedule-updates requires --registry <registry.json-or-url>.");
                return 2;
            }

            var service = new PluginUpdateScheduleService(services.Paths);
            var result = service.CreatePlan(new PluginUpdateSchedulePlanRequest
            {
                RegistryLocation = registry,
                Mode = Value(args, "--mode") ?? Value(args, "--action") ?? PluginBackgroundUpdateService.CheckOnlyMode,
                IntervalHours = ParseDouble(args, "--interval-hours", ParseDouble(args, "--hours", 24d)),
                OutputPath = Value(args, "--output") ?? Value(args, "-o"),
                StatePath = Value(args, "--state") ?? Value(args, "--state-path"),
                CliPath = Value(args, "--cli") ?? Value(args, "--cli-path"),
                TaskName = Value(args, "--task-name") ?? Value(args, "--name"),
                AcceptSensitiveRegistryLocation = Has(args, "--accept-sensitive-registry-location") ||
                    Has(args, "--accept-sensitive-registry-url")
            });
            PrintPluginUpdateSchedulePlanResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("update-task", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("scheduler-task", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("task-scheduler", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("update-scheduler", StringComparison.OrdinalIgnoreCase)))
        {
            var positionals = args
                .Skip(1)
                .Where(value => !value.StartsWith("--", StringComparison.Ordinal))
                .ToList();
            var command = positionals.FirstOrDefault() ?? PluginUpdateScheduleService.StatusCommand;
            var manifest = Value(args, "--manifest") ??
                Value(args, "--schedule") ??
                Value(args, "--plan") ??
                positionals.Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(manifest))
            {
                Console.Error.WriteLine("plugins update-task requires <status|register|unregister> --manifest <plugin-update-schedule.json>.");
                return 2;
            }

            var service = new PluginUpdateScheduleService(services.Paths);
            var result = service.RunTaskCommand(new PluginUpdateTaskSchedulerCommandRequest
            {
                Command = command,
                ManifestPath = manifest,
                TaskName = Value(args, "--task-name") ?? Value(args, "--name"),
                AcceptTaskRegistration = Has(args, "--accept-task-registration") ||
                    Has(args, "--accept-scheduler-registration") ||
                    Has(args, "--accept-register"),
                AcceptTaskRemoval = Has(args, "--accept-task-removal") ||
                    Has(args, "--accept-scheduler-removal") ||
                    Has(args, "--accept-unregister"),
                DryRun = Has(args, "--dry-run") || Has(args, "--what-if"),
                TimeoutSeconds = ParseOptionalInt(args, "--timeout-seconds") ??
                    ParseOptionalInt(args, "--timeout") ??
                    30
            });
            PrintPluginUpdateTaskSchedulerCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("background-updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("background-update", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("scheduled-updates", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("update-background", StringComparison.OrdinalIgnoreCase)))
        {
            var registry = Value(args, "--registry") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins background-updates requires --registry <registry.json-or-url>.");
                return 2;
            }

            var service = new PluginBackgroundUpdateService(services.Paths, services.RemotePlugins);
            var result = await service.RunAsync(new PluginBackgroundUpdateRunRequest
            {
                RegistryLocation = registry,
                Mode = Value(args, "--mode") ?? Value(args, "--action") ?? PluginBackgroundUpdateService.CheckOnlyMode,
                IntervalHours = ParseDouble(args, "--interval-hours", ParseDouble(args, "--hours", 24d)),
                Force = Has(args, "--force"),
                StatePath = Value(args, "--state") ?? Value(args, "--state-path") ?? Value(args, "--output")
            });
            PrintPluginBackgroundUpdateResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("marketplace-plan", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("marketplace-diagnostics", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("marketplace", StringComparison.OrdinalIgnoreCase)))
        {
            var firstPositionalIndex = 1;
            if (args[0].Equals("marketplace", StringComparison.OrdinalIgnoreCase) &&
                args.Length > 1 &&
                (args[1].Equals("plan", StringComparison.OrdinalIgnoreCase) ||
                    args[1].Equals("diagnostics", StringComparison.OrdinalIgnoreCase)))
            {
                firstPositionalIndex = 2;
            }

            var registry = Value(args, "--registry") ??
                args.Skip(firstPositionalIndex).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(registry))
            {
                Console.Error.WriteLine("plugins marketplace-plan requires --registry <registry.json-or-url>.");
                return 2;
            }

            var result = await services.RemotePlugins.PlanMarketplaceAsync(registry);
            PrintPluginMarketplacePlanResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("remove-staged", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("clear-staged", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                Console.Error.WriteLine("plugins remove-staged requires a plugin id.");
                return 2;
            }

            var result = services.RemotePlugins.RemoveStagedPackage(pluginId);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("install-staged", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("promote-staged", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("activate-staged", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                Console.Error.WriteLine("plugins install-staged requires a plugin id.");
                return 2;
            }

            var install = services.RemotePlugins.InstallStagedPackage(
                pluginId,
                Value(args, "--version"),
                replaceExisting: Has(args, "--replace"));
            object? metadata = null;
            if (install.Succeeded)
            {
                metadata = DisablePlugin(services, install.PluginId, removeTrust: true);
            }

            var result = new
            {
                succeeded = install.Succeeded,
                installed = install.Installed,
                pluginId = install.PluginId,
                version = install.Version,
                install,
                metadata,
                wouldTrust = false,
                wouldEnable = false,
                wouldExecute = false,
                message = install.Succeeded
                    ? install.Message + " Existing trust, enablement, and allowlist metadata was cleared."
                    : install.Message
            };
            PrintPluginCommandResult(result, Has(args, "--json"));
            return install.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            (args[0].Equals("activate", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("trust", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                Console.Error.WriteLine("plugins activate requires a plugin id.");
                return 2;
            }

            var request = new LocalPluginActivationRequest
            {
                PluginId = pluginId,
                AcceptRisk = Has(args, "--accept-risk") || Has(args, "--accept-plugin-risk"),
                Trust = Has(args, "--trust"),
                Enable = Has(args, "--enable"),
                EnableLocalPlugins = Has(args, "--enable-local-plugins") ||
                    Has(args, "--global-enable") ||
                    Has(args, "--enable-global"),
                AllowAllActions = Has(args, "--allow-all-actions") || Has(args, "--allow-plugin-actions")
            };
            request.ActionIds.AddRange(Values(args, "--allow-action"));
            request.ActionIds.AddRange(Values(args, "--allow-plugin-action"));

            var result = services.LocalPlugins.Activate(request);
            if (result.Succeeded)
            {
                services.SettingsStore.Save(services.Settings);
            }

            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args.Length > 0 &&
            args[0].Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                Console.Error.WriteLine("plugins disable requires a plugin id.");
                return 2;
            }

            var result = DisablePlugin(services, pluginId, removeTrust: false);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return 0;
        }

        if (args.Length > 0 &&
            (args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("remove", StringComparison.OrdinalIgnoreCase)))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                Console.Error.WriteLine("plugins uninstall requires a plugin id.");
                return 2;
            }

            var metadata = DisablePlugin(services, pluginId, removeTrust: true);
            var staged = services.RemotePlugins.RemoveStagedPackage(pluginId);
            var result = new
            {
                succeeded = true,
                pluginId,
                metadata,
                staged,
                message = "Plugin trust, enablement, action allowlists, and staged packages were removed. Active plugin files were preserved and no plugin code was run."
            };
            PrintPluginCommandResult(result, Has(args, "--json"));
            return 0;
        }

        if (args.Length == 0 ||
            args[0].Equals("list", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("status", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            var plugins = services.LocalPlugins.Discover();
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    pluginsRoot = services.Paths.PluginsRoot,
                    globalEnabled = services.Settings.EnableLocalPlugins,
                    plugins
                }, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(services.LocalPlugins.GetDiagnosticsSummary());
                foreach (var plugin in plugins)
                {
                    Console.WriteLine();
                    Console.WriteLine($"{plugin.PluginId} {plugin.Version} - {plugin.Name}");
                    Console.WriteLine($"  valid={plugin.IsValid} trusted={plugin.IsTrusted} enabled={plugin.IsEnabled}");
                    Console.WriteLine($"  manifest={plugin.ManifestPath}");
                    foreach (var issue in plugin.Issues)
                    {
                        Console.WriteLine($"  issue={issue}");
                    }

                    foreach (var action in plugin.Actions)
                    {
                        Console.WriteLine($"  action={action.QualifiedActionId} scope={action.Scope} allowed={action.IsAllowed}");
                    }
                }
            }

            return 0;
        }

        if (args[0].Equals("dry-run", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("plan", StringComparison.OrdinalIgnoreCase))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var actionId = args.Skip(2).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(actionId))
            {
                Console.Error.WriteLine("plugins dry-run requires a plugin id and action id.");
                return 2;
            }

            var result = services.LocalPlugins.DryRunAction(pluginId, actionId);
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(result.Message);
            }

            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("execute", StringComparison.OrdinalIgnoreCase))
        {
            var pluginId = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var actionId = args.Skip(2).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(actionId))
            {
                Console.Error.WriteLine("plugins run requires a plugin id and action id.");
                return 2;
            }

            var result = await services.LocalPlugins.ExecuteActionAsync(pluginId, actionId);
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(result.Message);
                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    Console.WriteLine(result.StandardOutput);
                }

                if (!string.IsNullOrWhiteSpace(result.StandardError))
                {
                    Console.Error.WriteLine(result.StandardError);
                }
            }

            return result.Succeeded ? 0 : 1;
        }

        Console.Error.WriteLine("plugins requires: list [--json], registry validate <registry> [--json], install-plan <plugin-id> --registry <registry> [--json], stage <plugin-id> --registry <registry> [--json], apply-updates --registry <registry> [--install-staged] [--replace] [--json], updates --registry <registry> [--json], background-updates --registry <registry> [--mode check-only|stage-only] [--force] [--json], schedule-updates --registry <registry> [--mode check-only|stage-only] [--interval-hours 24] [--output folder] [--json], update-task <status|register|unregister> --manifest plugin-update-schedule.json [--dry-run] [--accept-task-registration|--accept-task-removal] [--json], marketplace-plan --registry <registry> [--json], remove-staged <plugin-id> [--json], install-staged <plugin-id> [--version VERSION] [--replace] [--json], activate <plugin-id> --trust --enable --enable-local-plugins (--allow-action ACTION|--allow-all-actions) --accept-risk [--json], disable <plugin-id> [--json], uninstall <plugin-id> [--json], dry-run <plugin-id> <action-id> [--json], or run <plugin-id> <action-id> [--json].");
        return 2;
    }

    private static async Task<int> CompanionPortalAsync(AppServices services, string[] args)
    {
        if (args.Length == 0 ||
            (!args[0].Equals("export", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("report", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("local-export", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("media-review", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("review-media", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("serve", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("host", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("preview", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("companion-portal requires: export --output <folder> [--manual-validation folder-or-summary.json] [--proof-root artifacts] [--share-history-limit 200] [--media file --accept-media-copy] [--json], media-review --output <folder> --media <file> --accept-media-copy [--json], or serve --output <folder> [--host 127.0.0.1|0.0.0.0] [--port 0] [--self-hosted --accept-remote-clients --auth-token-env NAME] [--media file --accept-media-copy] [--check] [--json].");
            return 2;
        }

        var exportService = new CompanionPortalExportService(
            services.Paths,
            services.Settings,
            services.Diagnostics,
            services.Sharing);
        if (args[0].Equals("serve", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("host", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            var hostService = new CompanionPortalHostService(exportService);
            var hostRequest = new CompanionPortalHostRequest
            {
                OutputPath = Value(args, "--output") ?? Value(args, "-o"),
                ManualValidationPath = Value(args, "--manual-validation") ??
                    Value(args, "--manual-validation-root") ??
                    Value(args, "--validation"),
                ProofRootPath = Value(args, "--proof-root") ??
                    Value(args, "--artifacts-root") ??
                    Value(args, "--artifacts"),
                ShareHistoryLimit = ParseInt(args, "--share-history-limit", ParseInt(args, "--limit", 200)),
                Host = Value(args, "--host") ?? "127.0.0.1",
                Port = ParseInt(args, "--port", 0),
                MediaPaths = CompanionPortalMediaInputs(args, includePositionals: false),
                AcceptMediaCopy = Has(args, "--accept-media-copy"),
                MaxMediaBytes = ParseLong(args, "--max-media-bytes", 250L * 1024L * 1024L),
                AllowRemoteClients = Has(args, "--self-hosted") || Has(args, "--allow-remote-clients") || Has(args, "--remote-clients"),
                AcceptRemoteClients = Has(args, "--accept-remote-clients"),
                AccessToken = CompanionPortalAccessToken(args)
            };
            await using var session = await hostService.StartAsync(hostRequest);
            if (!session.Result.Succeeded)
            {
                PrintCompanionPortalHostResult(session.Result, Has(args, "--json"));
                return 1;
            }

            if (Has(args, "--check") || Has(args, "--smoke"))
            {
                using var http = new HttpClient();
                AddCompanionPortalAuth(http, hostRequest.AccessToken);
                var index = await http.GetStringAsync(session.Result.Url);
                var health = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), "health.json"));
                var report = await http.GetStringAsync(new Uri(new Uri(session.Result.Url), CompanionPortalExportService.ReportJsonFileName));
                var checkSucceeded = index.Contains("GoatShot", StringComparison.OrdinalIgnoreCase) &&
                    health.Contains("\"status\": \"ready\"", StringComparison.OrdinalIgnoreCase) &&
                    report.Contains("\"mode\":", StringComparison.OrdinalIgnoreCase);
                if (Has(args, "--json"))
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        session.Result,
                        checkSucceeded,
                        fetchedIndexBytes = index.Length,
                        fetchedHealthBytes = health.Length,
                        fetchedReportBytes = report.Length
                    }, CliJsonOptions()));
                }
                else
                {
                    PrintCompanionPortalHostResult(session.Result, json: false);
                    Console.WriteLine($"check-succeeded={checkSucceeded}");
                    Console.WriteLine($"fetched-index-bytes={index.Length}");
                    Console.WriteLine($"fetched-health-bytes={health.Length}");
                    Console.WriteLine($"fetched-report-bytes={report.Length}");
                }

                return checkSucceeded ? 0 : 1;
            }

            PrintCompanionPortalHostResult(session.Result, Has(args, "--json"));
            if (Has(args, "--json"))
            {
                return 0;
            }

            Console.WriteLine("Press Ctrl+C to stop the loopback preview.");
            using var waitForStop = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                waitForStop.Cancel();
            };
            Console.CancelKeyPress += handler;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, waitForStop.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }

            return 0;
        }

        if (args[0].Equals("media-review", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("review-media", StringComparison.OrdinalIgnoreCase))
        {
            var mediaPaths = CompanionPortalMediaInputs(args, includePositionals: true);
            if (mediaPaths.Count == 0)
            {
                Console.Error.WriteLine("companion-portal media-review requires at least one --media <file> input.");
                return 2;
            }

            var mediaRequest = new CompanionPortalExportRequest
            {
                OutputPath = Value(args, "--output") ?? Value(args, "-o"),
                ManualValidationPath = Value(args, "--manual-validation") ??
                    Value(args, "--manual-validation-root") ??
                    Value(args, "--validation"),
                ProofRootPath = Value(args, "--proof-root") ??
                    Value(args, "--artifacts-root") ??
                    Value(args, "--artifacts"),
                ShareHistoryLimit = ParseInt(args, "--share-history-limit", ParseInt(args, "--limit", 200)),
                MediaPaths = mediaPaths,
                AcceptMediaCopy = Has(args, "--accept-media-copy"),
                MaxMediaBytes = ParseLong(args, "--max-media-bytes", 250L * 1024L * 1024L)
            };
            var mediaResult = await exportService.ExportAsync(mediaRequest);
            PrintCompanionPortalExportResult(mediaResult, Has(args, "--json"));
            return mediaResult.Succeeded ? 0 : 1;
        }

        var request = new CompanionPortalExportRequest
        {
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            ManualValidationPath = Value(args, "--manual-validation") ??
                Value(args, "--manual-validation-root") ??
                Value(args, "--validation"),
            ProofRootPath = Value(args, "--proof-root") ??
                Value(args, "--artifacts-root") ??
                Value(args, "--artifacts"),
            ShareHistoryLimit = ParseInt(args, "--share-history-limit", ParseInt(args, "--limit", 200)),
            MediaPaths = CompanionPortalMediaInputs(args, includePositionals: false),
            AcceptMediaCopy = Has(args, "--accept-media-copy"),
            MaxMediaBytes = ParseLong(args, "--max-media-bytes", 250L * 1024L * 1024L)
        };
        var result = await exportService.ExportAsync(request);
        PrintCompanionPortalExportResult(result, Has(args, "--json"));

        return result.Succeeded ? 0 : 1;
    }

    private static List<string> CompanionPortalMediaInputs(IReadOnlyList<string> args, bool includePositionals)
    {
        var mediaPaths = Values(args, "--media")
            .Concat(Values(args, "--file"))
            .Concat(Values(args, "--input"))
            .ToList();
        if (includePositionals)
        {
            var trailing = args.Skip(1).ToArray();
            mediaPaths.AddRange(PositionalValues(
                trailing,
                "--output",
                "-o",
                "--manual-validation",
                "--manual-validation-root",
                "--validation",
                "--proof-root",
                "--artifacts-root",
                "--artifacts",
                "--share-history-limit",
                "--limit",
                "--media",
                "--file",
                "--input",
                "--max-media-bytes",
                "--host",
                "--port"));
        }

        return mediaPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? CompanionPortalAccessToken(IReadOnlyList<string> args)
    {
        var direct = Value(args, "--auth-token") ?? Value(args, "--access-token");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        var envName = Value(args, "--auth-token-env") ?? Value(args, "--access-token-env");
        if (string.IsNullOrWhiteSpace(envName))
        {
            return null;
        }

        return Environment.GetEnvironmentVariable(envName.Trim())?.Trim();
    }

    private static void AddCompanionPortalAuth(HttpClient http, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {accessToken.Trim()}");
        }
    }

    private static void PrintCompanionPortalExportResult(CompanionPortalExportResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine("GoatShot companion portal read-only local export");
        Console.WriteLine($"succeeded={result.Succeeded}");
        Console.WriteLine($"output={result.OutputRoot}");
        Console.WriteLine($"report-json={result.ReportJsonPath}");
        Console.WriteLine($"report-html={result.ReportHtmlPath}");
        Console.WriteLine($"mode={result.Report.Mode}");
        Console.WriteLine($"would-host={result.Report.Boundary.WouldHostPortal}");
        Console.WriteLine($"would-contact-portal={result.Report.Boundary.WouldContactPortal}");
        Console.WriteLine($"would-sync={result.Report.Boundary.WouldSync}");
        Console.WriteLine($"would-upload={result.Report.Boundary.WouldUpload}");
        Console.WriteLine($"would-attach-media={result.Report.Boundary.WouldAttachMedia}");
        Console.WriteLine($"would-read-secrets={result.Report.Boundary.WouldReadSecrets}");
        Console.WriteLine($"media-review-enabled={result.Report.MediaReview.Enabled}");
        Console.WriteLine($"media-review-items={result.Report.MediaReview.ItemCount}");
        if (result.Report.MediaReview.Enabled)
        {
            Console.WriteLine($"media-review-html={Path.Combine(result.OutputRoot, CompanionPortalExportService.MediaReviewHtmlFileName)}");
            Console.WriteLine($"media-review-json={Path.Combine(result.OutputRoot, CompanionPortalExportService.MediaReviewJsonFileName)}");
        }

        Console.WriteLine($"share-history-total={result.Report.ShareHistory.TotalEntries}");
        Console.WriteLine($"manual-validation-included={result.Report.ManualValidation.Included}");
        Console.WriteLine($"release-proof-included={result.Report.ReleaseProof.Included}");
        Console.WriteLine(result.Message);
        foreach (var issue in result.Report.Issues)
        {
            Console.Error.WriteLine($"issue={issue}");
        }

        foreach (var warning in result.Report.Warnings)
        {
            Console.Error.WriteLine($"warning={warning}");
        }
    }

    private static void PrintCompanionPortalHostResult(CompanionPortalHostResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine("GoatShot companion portal loopback preview");
        Console.WriteLine($"succeeded={result.Succeeded}");
        Console.WriteLine($"url={result.Url}");
        Console.WriteLine($"host={result.Host}");
        Console.WriteLine($"port={result.Port}");
        Console.WriteLine($"output={result.OutputRoot}");
        Console.WriteLine($"report-json={result.ReportJsonPath}");
        Console.WriteLine($"report-html={result.ReportHtmlPath}");
        Console.WriteLine($"mode={result.Mode}");
        Console.WriteLine($"loopback-only={result.LoopbackOnly}");
        Console.WriteLine($"remote-clients-allowed={result.RemoteClientsAllowed}");
        Console.WriteLine($"mutating-routes-enabled={result.MutatingRoutesEnabled}");
        Console.WriteLine($"auth-required={result.AuthRequired}");
        Console.WriteLine($"access-control-mode={result.AccessControlMode}");
        Console.WriteLine($"would-contact-portal={result.WouldContactPortal}");
        Console.WriteLine($"would-sync={result.WouldSync}");
        Console.WriteLine($"would-upload={result.WouldUpload}");
        Console.WriteLine($"would-attach-media={result.WouldAttachMedia}");
        Console.WriteLine($"would-read-secrets={result.WouldReadSecrets}");
        Console.WriteLine($"would-mutate-policy={result.WouldMutatePolicy}");
        Console.WriteLine($"media-review-enabled={result.MediaReviewPagesEnabled}");
        Console.WriteLine($"media-review-items={result.MediaReviewItemCount}");
        Console.WriteLine(result.Message);
    }

    private static int AdminPolicy(AppServices services, string[] args)
    {
        var service = new AdminPolicyBundleService();
        if (args.Length == 0 ||
            args[0].Equals("explain", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var path = Value(args, "--input") ??
                Value(args, "--policy") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            var result = service.Explain(services.Settings, path);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("validate", StringComparison.OrdinalIgnoreCase))
        {
            var path = Value(args, "--input") ??
                Value(args, "--policy") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("admin-policy validate requires a bundle path.");
                return 2;
            }

            var result = service.ValidateFile(path);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("export", StringComparison.OrdinalIgnoreCase))
        {
            var output = Value(args, "--output") ?? Value(args, "-o");
            if (string.IsNullOrWhiteSpace(output))
            {
                Console.Error.WriteLine("admin-policy export requires --output <bundle.json>.");
                return 2;
            }

            var result = service.Export(services.Settings, output);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            var path = Value(args, "--input") ??
                Value(args, "--policy") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("admin-policy import requires a bundle path.");
                return 2;
            }

            var result = service.Import(services.Settings, services.SettingsStore, path, replace: Has(args, "--replace"));
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("diff", StringComparison.OrdinalIgnoreCase))
        {
            var path = Value(args, "--input") ??
                Value(args, "--policy") ??
                args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("admin-policy diff requires a bundle path.");
                return 2;
            }

            var result = service.Diff(services.Settings, path);
            PrintPluginCommandResult(result, Has(args, "--json"));
            return result.Succeeded ? 0 : 1;
        }

        Console.Error.WriteLine("admin-policy requires: explain [bundle.json] [--json], validate <bundle.json> [--json], export --output bundle.json [--json], import <bundle.json> [--replace] [--json], or diff <bundle.json> [--json].");
        return 2;
    }

    private static object DisablePlugin(AppServices services, string pluginId, bool removeTrust)
    {
        var beforeTrusted = services.Settings.TrustedPluginIds.Count;
        var beforeEnabled = services.Settings.EnabledPluginIds.Count;
        var beforeAllowed = services.Settings.AllowedPluginActionIds.Count;
        services.Settings.EnabledPluginIds.RemoveAll(value =>
            value.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        services.Settings.AllowedPluginActionIds.RemoveAll(value =>
            value.Equals($"{pluginId}:*", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith($"{pluginId}:", StringComparison.OrdinalIgnoreCase));
        if (removeTrust)
        {
            services.Settings.TrustedPluginIds.RemoveAll(value =>
                value.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        }

        services.SettingsStore.Save(services.Settings);
        return new
        {
            succeeded = true,
            pluginId,
            removedTrust = removeTrust && beforeTrusted != services.Settings.TrustedPluginIds.Count,
            removedEnabled = beforeEnabled != services.Settings.EnabledPluginIds.Count,
            removedAllowlistEntries = beforeAllowed - services.Settings.AllowedPluginActionIds.Count,
            wouldExecute = false,
            message = removeTrust
                ? "Plugin trust, enablement, and allowlisted actions were removed. Active plugin files were preserved."
                : "Plugin enablement and allowlisted actions were removed. Trust metadata and active plugin files were preserved."
        };
    }

    private static void PrintPluginCommandResult(object result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        var message = result.GetType().GetProperty("Message")?.GetValue(result)?.ToString() ??
            result.GetType().GetProperty("message")?.GetValue(result)?.ToString();
        Console.WriteLine(string.IsNullOrWhiteSpace(message)
            ? JsonSerializer.Serialize(result, CliJsonOptions())
            : message);
    }

    private static void PrintPersonSegmentationModelPackageResult(
        PersonSegmentationModelPackageResult result,
        bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"manifest={result.ManifestLocation}");
        Console.WriteLine($"model={result.ModelId}");
        Console.WriteLine($"version={result.Version}");
        Console.WriteLine($"name={result.Name}");
        Console.WriteLine($"model-uri={result.ModelUri}");
        Console.WriteLine($"staging-root={result.StagingRoot}");
        Console.WriteLine($"staged={result.Staged}");
        Console.WriteLine($"did-download-model={result.DidDownloadModel}");
        Console.WriteLine($"would-download-model={result.WouldDownloadModel}");
        Console.WriteLine($"size-bytes={result.SizeBytes}");
        Console.WriteLine($"sha256={result.ModelSha256}");
        Console.WriteLine($"declared-sha256={result.DeclaredSha256}");
        Console.WriteLine($"staged-model={result.StagedModelPath}");
        Console.WriteLine($"stage-manifest={result.StagedManifestPath}");
        Console.WriteLine($"would-run-model={result.WouldRunModel}");
        Console.WriteLine($"would-trust-model={result.WouldTrustModel}");
        Console.WriteLine($"would-enable-model={result.WouldEnableModel}");
        Console.WriteLine($"would-register-runner={result.WouldRegisterRunner}");
        Console.WriteLine($"would-contact-hosted-segmentation-service={result.WouldContactHostedSegmentationService}");
        Console.WriteLine($"would-certify-model={result.WouldCertifyModel}");

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        foreach (var file in result.GeneratedFiles)
        {
            Console.WriteLine($"artifact={file}");
        }

        if (result.NextActions.Count > 0)
        {
            Console.WriteLine("next-actions:");
            foreach (var action in result.NextActions)
            {
                Console.WriteLine($"  {action}");
            }
        }
    }

    private static void PrintPluginUpdateSummaryResult(RemotePluginUpdateSummaryResult result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"registry={result.RegistryLocation}");
        Console.WriteLine($"plugins-root={result.PluginsRoot}");
        Console.WriteLine($"staging-root={result.StagingRoot}");
        Console.WriteLine($"counts available={result.AvailableCount} staged={result.StagedCount} installed={result.InstalledCount} blocked={result.BlockedCount} incompatible={result.IncompatibleCount}");
        Console.WriteLine("mutation-boundary: install=false trust=false enable=false allowlist=false execute=false");

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        foreach (var status in new[] { "available", "staged", "installed", "blocked", "incompatible", "not-installed" })
        {
            var group = result.Plugins
                .Where(plugin => plugin.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                .OrderBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine();
            Console.WriteLine($"{status}: {group.Count}");
            foreach (var plugin in group)
            {
                Console.WriteLine($"  {plugin.PluginId} installed={(string.IsNullOrWhiteSpace(plugin.InstalledVersion) ? "none" : plugin.InstalledVersion)} registry={plugin.RegistryVersion} staged={(string.IsNullOrWhiteSpace(plugin.StagedVersion) ? "none" : plugin.StagedVersion)}");
                Console.WriteLine($"    {plugin.Message}");
                if (!string.IsNullOrWhiteSpace(plugin.NextAction))
                {
                    Console.WriteLine($"    next={plugin.NextAction}");
                }

                foreach (var reason in plugin.CompatibilityIssues)
                {
                    Console.WriteLine($"    incompatible={reason}");
                }

                foreach (var reason in plugin.PolicyBlockReasons)
                {
                    Console.WriteLine($"    blocked={reason}");
                }

                foreach (var reason in plugin.OperatorGateReasons)
                {
                    Console.WriteLine($"    gate={reason}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("next-actions:");
        foreach (var action in result.NextActions)
        {
            Console.WriteLine($"  {action}");
        }
    }

    private static void PrintPluginBackgroundUpdateResult(PluginBackgroundUpdateRunResult result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"registry={result.RegistryLocation}");
        Console.WriteLine($"mode={result.Mode}");
        Console.WriteLine($"state={result.StatePath}");
        Console.WriteLine($"due={result.Due} forced={result.Forced} skipped={result.Skipped}");
        Console.WriteLine($"next-run={(result.NextRunUtc.HasValue ? result.NextRunUtc.Value.ToString("O") : "none")}");
        Console.WriteLine($"counts available={result.AvailableCount} staged={result.StagedCount} installed={result.InstalledCount} blocked={result.BlockedCount} incompatible={result.IncompatibleCount} failed={result.FailedCount}");
        Console.WriteLine("mutation-boundary: install=false trust=false enable=false allowlist=false execute=false");

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        Console.WriteLine("next-actions:");
        foreach (var action in result.NextActions)
        {
            Console.WriteLine($"  {action}");
        }
    }

    private static void PrintPluginUpdateSchedulePlanResult(PluginUpdateSchedulePlanResult result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"registry={result.RegistryLocationRedacted}");
        Console.WriteLine($"mode={result.Mode}");
        Console.WriteLine($"interval-hours={result.IntervalHours}");
        Console.WriteLine($"task-name={result.TaskName}");
        Console.WriteLine($"output={result.OutputPath}");
        Console.WriteLine($"run-script={result.RunScriptPath}");
        Console.WriteLine($"register-script={result.RegisterScriptPath}");
        Console.WriteLine($"unregister-script={result.UnregisterScriptPath}");
        Console.WriteLine($"manifest={result.ManifestPath}");
        Console.WriteLine("mutation-boundary: install=false trust=false enable=false allowlist=false execute=false register-task=false");

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        Console.WriteLine("next-actions:");
        foreach (var action in result.NextActions)
        {
            Console.WriteLine($"  {action}");
        }
    }

    private static void PrintPluginUpdateTaskSchedulerCommandResult(PluginUpdateTaskSchedulerCommandResult result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"command={result.Command}");
        Console.WriteLine($"manifest={result.ManifestPath}");
        Console.WriteLine($"task-name={result.TaskName}");
        Console.WriteLine($"registered={result.RegisteredTask}");
        Console.WriteLine($"dry-run={result.DryRun}");
        Console.WriteLine($"exit-code={result.ExitCode}");
        Console.WriteLine("mutation-boundary: install=false trust=false enable=false allowlist=false execute=false");

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine("stdout:");
            Console.WriteLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.WriteLine("stderr:");
            Console.WriteLine(result.StandardError.TrimEnd());
        }

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        Console.WriteLine("next-actions:");
        foreach (var action in result.NextActions)
        {
            Console.WriteLine($"  {action}");
        }
    }

    private static void PrintPluginMarketplacePlanResult(RemotePluginMarketplacePlanResult result, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            return;
        }

        Console.WriteLine(result.Message);
        Console.WriteLine($"registry={result.RegistryLocation}");
        Console.WriteLine($"plugins-root={result.PluginsRoot}");
        Console.WriteLine($"staging-root={result.StagingRoot}");
        Console.WriteLine($"counts listed={result.PluginCount} active={result.ActivePluginCount} staged={result.StagedPackageCount} available={result.AvailableCount} blocked={result.BlockedCount} incompatible={result.IncompatibleCount}");
        Console.WriteLine("mutation-boundary: stage=false install=false trust=false enable=false allowlist=false execute=false auto-update=false publish=false account-contact=false");

        foreach (var issue in result.Issues)
        {
            Console.WriteLine($"ERROR: {issue}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"WARN: {warning}");
        }

        Console.WriteLine();
        Console.WriteLine("authority-boundaries:");
        foreach (var boundary in result.AuthorityBoundaries)
        {
            Console.WriteLine($"  {boundary}");
        }

        Console.WriteLine();
        Console.WriteLine("operator-gates:");
        foreach (var gate in result.OperatorGates)
        {
            Console.WriteLine($"  {gate}");
        }

        Console.WriteLine();
        Console.WriteLine("non-goals:");
        foreach (var nonGoal in result.NonGoals)
        {
            Console.WriteLine($"  {nonGoal}");
        }

        foreach (var status in new[] { "available", "staged", "installed", "blocked", "incompatible", "not-installed" })
        {
            var group = result.Plugins
                .Where(plugin => plugin.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                .OrderBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Console.WriteLine();
            Console.WriteLine($"{status}: {group.Count}");
            foreach (var plugin in group)
            {
                Console.WriteLine($"  {plugin.PluginId} installed={(string.IsNullOrWhiteSpace(plugin.InstalledVersion) ? "none" : plugin.InstalledVersion)} registry={plugin.RegistryVersion} staged={(string.IsNullOrWhiteSpace(plugin.StagedVersion) ? "none" : plugin.StagedVersion)}");
                Console.WriteLine($"    {plugin.Message}");
                if (!string.IsNullOrWhiteSpace(plugin.NextAction))
                {
                    Console.WriteLine($"    next={plugin.NextAction}");
                }

                foreach (var reason in plugin.CompatibilityIssues)
                {
                    Console.WriteLine($"    incompatible={reason}");
                }

                foreach (var reason in plugin.PolicyBlockReasons)
                {
                    Console.WriteLine($"    blocked={reason}");
                }

                foreach (var reason in plugin.OperatorGateReasons)
                {
                    Console.WriteLine($"    gate={reason}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("next-actions:");
        foreach (var action in result.NextActions)
        {
            Console.WriteLine($"  {action}");
        }
    }

    private static async Task<int> BrowserExtensionAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("browser-extension requires: validate <payload.json> [--redacted-output redacted.json] [--json], receive <payload.json> [--screenshot image.png] [--stitch-package package-dir] [--redacted-output redacted.json] [--json], live-fixture, proof validate, package, store-readiness, store-package, publication-plan, record-publication-evidence, enterprise-policy-plan, install-plan, install-assist, install-guide, native-host <status|manifest|install|uninstall|run>, or status [--json].");
            return 2;
        }

        if (args[0].Equals("native-host", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("native-messaging", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("host", StringComparison.OrdinalIgnoreCase))
        {
            var hostAction = args.Skip(1).FirstOrDefault() ?? string.Empty;
            if (hostAction.Equals("run", StringComparison.OrdinalIgnoreCase) ||
                hostAction.Equals("stdio", StringComparison.OrdinalIgnoreCase) ||
                hostAction.Equals("install", StringComparison.OrdinalIgnoreCase))
            {
                if (!ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var browserPolicyReason))
                {
                    return PrintPolicyBlocked(services, $"browser-extension native-host {hostAction}", browserPolicyReason, Has(args, "--json"));
                }
            }

            return await BrowserNativeHostAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("package", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("pack", StringComparison.OrdinalIgnoreCase))
        {
            var requestedSource = Value(args, "--source") ??
                Value(args, "--source-dir") ??
                Value(args, "--extension-dir");
            var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (requestedSource is null && !Directory.Exists(source))
            {
                source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
            }

            var output = Value(args, "--output") ??
                Value(args, "-o") ??
                Path.Combine("artifacts", "browser-extension", "goatshot-browser-extension.zip");
            var result = await new BrowserExtensionPackageService().PackageAsync(source, output);
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(result.Message);
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"ERROR: {issue}");
                }

                if (result.Succeeded)
                {
                    Console.WriteLine($"output={result.OutputPath}");
                    Console.WriteLine($"files={string.Join(",", result.IncludedFiles)}");
                }
            }

            return result.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("install-guide", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("setup-guide", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("guide", StringComparison.OrdinalIgnoreCase))
        {
            var requestedSource = Value(args, "--source") ??
                Value(args, "--source-dir") ??
                Value(args, "--extension-dir");
            var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (requestedSource is null && !Directory.Exists(source))
            {
                source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
            }

            var result = new BrowserExtensionInstallGuideService().CreateGuide(new BrowserExtensionInstallGuideRequest
            {
                Browser = Value(args, "--browser"),
                ExtensionSourceDirectory = source,
                ExtensionId = Value(args, "--extension-id"),
                ChromeExtensionId = Value(args, "--chrome-extension-id"),
                EdgeExtensionId = Value(args, "--edge-extension-id"),
                FirefoxExtensionId = Value(args, "--firefox-extension-id"),
                NativeHostStatus = services.BrowserNativeHosts.GetStatus()
            });
            var output = Value(args, "--output") ?? Value(args, "-o");
            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, result.Markdown);
            }

            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result,
                    output
                }, CliJsonOptions()));
            }
            else
            {
                Console.Write(result.Markdown);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine($"guide-output={output}");
                }
            }

            return result.Entries.Count == 0 ? 1 : 0;
        }

        if (args[0].Equals("install-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("auto-install-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("automatic-install-plan", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionInstallPlanAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("install-assist", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("auto-install-assist", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("temporary-install", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("load-unpacked", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionInstallAssistAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("store-readiness", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("store-checklist", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("listing-checklist", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionStoreReadinessAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("store-package", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("submission-package", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("store-bundle", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionStorePackageAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("publication-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("publish-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("store-publication-plan", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionPublicationPlanAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("record-publication-evidence", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("publication-evidence", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("store-evidence", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("record-store-evidence", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionPublicationEvidenceRecordAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("enterprise-policy-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("enterprise-install-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("force-install-plan", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("managed-install-plan", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionEnterprisePolicyPlanAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("live-fixture", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("fixture-proof", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("proof-helper", StringComparison.OrdinalIgnoreCase))
        {
            var verifying = !string.IsNullOrWhiteSpace(Value(args, "--payload")) ||
                !string.IsNullOrWhiteSpace(Value(args, "--payload-json")) ||
                !string.IsNullOrWhiteSpace(Value(args, "--stitch-package")) ||
                !string.IsNullOrWhiteSpace(Value(args, "--stitch-package-path"));
            if (verifying &&
                !ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var browserPolicyReason))
            {
                return PrintPolicyBlocked(services, "browser-extension live-fixture", browserPolicyReason, Has(args, "--json"));
            }

            return await BrowserExtensionLiveFixtureAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("proof", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("proof-folder", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionProofAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("validate-proof", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("proof-validate", StringComparison.OrdinalIgnoreCase))
        {
            return await BrowserExtensionProofAsync(
                services,
                new[] { "validate" }.Concat(args.Skip(1)).ToArray());
        }

        if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("diagnostics", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("diag", StringComparison.OrdinalIgnoreCase))
        {
            var requestedSource = Value(args, "--source") ??
                Value(args, "--source-dir") ??
                Value(args, "--extension-dir");
            var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (requestedSource is null && !Directory.Exists(source))
            {
                source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
            }

            var status = services.BrowserExtensionBridge.GetStatusSummary();
            var nativeHostStatus = services.BrowserNativeHosts.GetStatus();
            var diagnostics = new BrowserExtensionOperatorDiagnosticsService(services.Paths).Build(new BrowserExtensionOperatorDiagnosticsRequest
            {
                ExtensionSourceDirectory = source,
                NativeHostStatus = nativeHostStatus
            });
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    bridgeRoot = services.Paths.BrowserBridgeRoot,
                    status,
                    nativeHostStatus,
                    diagnostics
                }, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(status);
                Console.WriteLine(services.BrowserNativeHosts.GetStatusSummary());
                Console.WriteLine($"extension-source={diagnostics.ExtensionSourceDirectory}");
                Console.WriteLine($"safe-fixture={(diagnostics.SafeFixtureExists ? diagnostics.SafeFixturePath : "missing")}");
                Console.WriteLine("diagnostic-codes:");
                foreach (var entry in diagnostics.Entries)
                {
                    var browser = string.IsNullOrWhiteSpace(entry.Browser) ? string.Empty : $" browser={entry.Browser}";
                    Console.WriteLine($"  {entry.Code} status={entry.Status}{browser}");
                    Console.WriteLine($"    {entry.Message}");
                    if (!string.IsNullOrWhiteSpace(entry.RecoveryAction))
                    {
                        Console.WriteLine($"    action={entry.RecoveryAction}");
                    }
                }

                if (diagnostics.Issues.Count > 0)
                {
                    Console.WriteLine("issues:");
                    foreach (var issue in diagnostics.Issues)
                    {
                        Console.WriteLine($"  {issue}");
                    }
                }

                if (diagnostics.Warnings.Count > 0)
                {
                    Console.WriteLine("warnings:");
                    foreach (var warning in diagnostics.Warnings)
                    {
                        Console.WriteLine($"  {warning}");
                    }
                }

                Console.WriteLine("next-actions:");
                foreach (var action in diagnostics.NextActions)
                {
                    Console.WriteLine($"  {action}");
                }
            }

            return 0;
        }

        if (args[0].Equals("receive", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("handoff", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            if (!ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var browserPolicyReason))
            {
                return PrintPolicyBlocked(services, "browser-extension receive", browserPolicyReason, Has(args, "--json"));
            }

            var input = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Error.WriteLine("browser-extension receive requires a payload JSON file.");
                return 2;
            }

            var result = await services.BrowserExtensionBridge.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = input,
                ScreenshotPath = Value(args, "--screenshot") ?? Value(args, "--image"),
                StitchPackagePath = Value(args, "--stitch-package") ?? Value(args, "--stitch-package-path"),
                RedactedOutputPath = Value(args, "--redacted-output") ?? Value(args, "--output") ?? Value(args, "-o")
            });

            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(result.Message);
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"ERROR: {issue}");
                }

                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"WARN: {warning}");
                }

                if (!string.IsNullOrWhiteSpace(result.RedactedPayloadPath))
                {
                    Console.WriteLine($"redacted-payload={result.RedactedPayloadPath}");
                }

                if (!string.IsNullOrWhiteSpace(result.StitchPackagePath))
                {
                    Console.WriteLine($"stitch-package={result.StitchPackagePath}");
                }

                if (result.Item is not null)
                {
                    Console.WriteLine($"workspace-file={result.Item.FilePath}");
                }
            }

            return result.Succeeded ? 0 : 1;
        }

        if (!args[0].Equals("validate", StringComparison.OrdinalIgnoreCase))
        {
        Console.Error.WriteLine("browser-extension requires: validate, receive, live-fixture, proof validate, package, store-readiness, store-package, publication-plan, record-publication-evidence, enterprise-policy-plan, install-plan, install-assist, install-guide, native-host, or status.");
            return 2;
        }

        var payloadInput = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(payloadInput))
        {
            Console.Error.WriteLine("browser-extension validate requires a payload JSON file.");
            return 2;
        }

        BrowserExtensionCapturePayload? payload;
        try
        {
            var json = await File.ReadAllTextAsync(payloadInput);
            payload = JsonSerializer.Deserialize<BrowserExtensionCapturePayload>(json, CliJsonOptions());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read browser extension payload: {ex.Message}");
            return 1;
        }

        if (payload is null)
        {
            Console.Error.WriteLine("Browser extension payload was empty or invalid JSON.");
            return 1;
        }

        var validation = BrowserExtensionPayloadContractService.Validate(payload);
        var redacted = BrowserExtensionPayloadContractService.RedactForStorage(payload);
        var redactedOutput = Value(args, "--redacted-output") ?? Value(args, "--output") ?? Value(args, "-o");
        if (!string.IsNullOrWhiteSpace(redactedOutput))
        {
            var outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(redactedOutput));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(redacted, CliJsonOptions()));
            redactedOutput = outputPath;
        }

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                validation,
                redactedOutput
            }, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(validation.IsValid
                ? "Browser extension payload is valid."
                : "Browser extension payload is invalid.");
            foreach (var issue in validation.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in validation.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            if (!string.IsNullOrWhiteSpace(redactedOutput))
            {
                Console.WriteLine($"redacted-output={redactedOutput}");
            }
        }

        return validation.IsValid ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionStoreReadinessAsync(string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var request = new BrowserExtensionStoreReadinessRequest
        {
            ExtensionSourceDirectory = source,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            Target = Value(args, "--target") ?? Value(args, "--browser"),
            SupportUrl = Value(args, "--support-url"),
            PrivacyUrl = Value(args, "--privacy-url")
        };

        var result = await new BrowserExtensionStoreReadinessService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }

            foreach (var target in result.Targets)
            {
                Console.WriteLine($"{target.Target}: {target.Status}");
                foreach (var issue in target.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in target.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionInstallPlanAsync(AppServices services, string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var policyAllowed = ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var policyReason);
        var request = new BrowserExtensionInstallPlanRequest
        {
            Browser = Value(args, "--browser") ?? Value(args, "--target"),
            ExtensionSourceDirectory = source,
            StorePackageRoot = Value(args, "--store-package-root") ?? Value(args, "--store-package") ?? Value(args, "--package-root"),
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            ExtensionId = Value(args, "--extension-id"),
            ChromeExtensionId = Value(args, "--chrome-extension-id"),
            EdgeExtensionId = Value(args, "--edge-extension-id"),
            FirefoxExtensionId = Value(args, "--firefox-extension-id"),
            NativeHostStatus = services.BrowserNativeHosts.GetStatus(),
            PolicyAllowed = policyAllowed,
            PolicyReason = policyReason
        };

        var result = await new BrowserExtensionInstallPlanService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            if (!result.PolicyAllowed)
            {
                Console.WriteLine($"policy-block={result.PolicyReason}");
            }

            foreach (var entry in result.Entries)
            {
                Console.WriteLine($"{entry.Browser}: {entry.Status}");
                Console.WriteLine($"  automatic-install={entry.AutomaticInstallStatus}");
                Console.WriteLine($"  desktop-detects-installed-extension={entry.DesktopCanDetectInstalledExtension}");
                Console.WriteLine($"  native-host-installed={entry.NativeHostInstalled}");
                Console.WriteLine($"  store-package={(string.IsNullOrWhiteSpace(entry.StorePackagePath) ? "missing" : entry.StorePackagePath)}");
                foreach (var issue in entry.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in entry.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionEnterprisePolicyPlanAsync(AppServices services, string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var policyAllowed = ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var policyReason);
        var request = new BrowserExtensionEnterprisePolicyPlanRequest
        {
            Browser = Value(args, "--browser") ?? Value(args, "--target"),
            ExtensionSourceDirectory = source,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            ExtensionId = Value(args, "--extension-id"),
            ChromeExtensionId = Value(args, "--chrome-extension-id"),
            EdgeExtensionId = Value(args, "--edge-extension-id"),
            FirefoxExtensionId = Value(args, "--firefox-extension-id"),
            UpdateUrl = Value(args, "--update-url"),
            ChromeUpdateUrl = Value(args, "--chrome-update-url"),
            EdgeUpdateUrl = Value(args, "--edge-update-url"),
            InstallUrl = Value(args, "--install-url"),
            FirefoxInstallUrl = Value(args, "--firefox-install-url"),
            FirefoxXpiUrl = Value(args, "--firefox-xpi-url") ?? Value(args, "--xpi-url"),
            PolicyAllowed = policyAllowed,
            PolicyReason = policyReason
        };

        var result = await new BrowserExtensionEnterprisePolicyPlanService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            Console.WriteLine($"policy-allowed={result.PolicyAllowed}");
            if (!result.PolicyAllowed)
            {
                Console.WriteLine($"policy-block={result.PolicyReason}");
            }

            Console.WriteLine($"would-apply-policy={result.WouldApplyPolicy}");
            Console.WriteLine($"would-write-registry={result.WouldWriteRegistry}");
            Console.WriteLine($"would-write-enterprise-policy-file={result.WouldWriteEnterprisePolicyFile}");
            Console.WriteLine($"would-install-extension={result.WouldInstallExtension}");
            Console.WriteLine($"would-register-native-host={result.WouldRegisterNativeHost}");
            Console.WriteLine($"would-contact-store-account={result.WouldContactStoreAccount}");

            foreach (var entry in result.Entries)
            {
                Console.WriteLine($"{entry.Browser}: {entry.Status}");
                Console.WriteLine($"  template={entry.PolicyTemplatePath}");
                Console.WriteLine($"  extension-id={(string.IsNullOrWhiteSpace(entry.ExtensionId) ? "missing" : entry.ExtensionId)}");
                Console.WriteLine($"  would-apply-policy={entry.WouldApplyPolicy}");
                Console.WriteLine($"  would-install-extension={entry.WouldInstallExtension}");
                foreach (var issue in entry.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in entry.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionInstallAssistAsync(AppServices services, string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var portText = Value(args, "--remote-debugging-port") ?? Value(args, "--debug-port");
        int? remoteDebuggingPort = null;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            remoteDebuggingPort = int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
                ? parsedPort
                : -1;
        }

        var policyAllowed = ManagedPolicyService.IsBrowserExtensionAllowed(services.Settings, out var policyReason);
        var request = new BrowserExtensionInstallAssistRequest
        {
            Browser = Value(args, "--browser") ?? Value(args, "--target"),
            ExtensionSourceDirectory = source,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            StartUrl = Value(args, "--url") ?? Value(args, "--start-url"),
            RemoteDebuggingPort = remoteDebuggingPort,
            StartBrowser = Has(args, "--start") || Has(args, "--launch"),
            BrowserExecutablePath = Value(args, "--browser-exe") ??
                Value(args, "--browser-path") ??
                Value(args, "--exe"),
            PolicyAllowed = policyAllowed,
            PolicyReason = policyReason
        };

        var result = await new BrowserExtensionInstallAssistService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            Console.WriteLine($"browser={result.Browser}");
            Console.WriteLine($"mode={result.Mode}");
            Console.WriteLine($"supports-automated-launch={result.SupportsAutomatedLaunch}");
            Console.WriteLine($"started={result.Started}");
            if (result.StartedProcessId is not null)
            {
                Console.WriteLine($"started-process-id={result.StartedProcessId}");
            }

            if (!result.PolicyAllowed)
            {
                Console.WriteLine($"policy-block={result.PolicyReason}");
            }

            if (!string.IsNullOrWhiteSpace(result.ProfileDirectory))
            {
                Console.WriteLine($"browser-profile={result.ProfileDirectory}");
            }

            if (!string.IsNullOrWhiteSpace(result.LaunchScriptPath))
            {
                Console.WriteLine($"launch-script={result.LaunchScriptPath}");
            }

            Console.WriteLine($"launch-plan={result.LaunchPlanPath}");
            Console.WriteLine($"assist-guide={result.AssistGuidePath}");
            Console.WriteLine($"assist-json={result.AssistJsonPath}");
            Console.WriteLine($"mutates-existing-browser-profile={result.MutationFlags.MutatesExistingBrowserProfile}");
            Console.WriteLine($"installs-extension-permanently={result.MutationFlags.InstallsExtensionPermanently}");
            Console.WriteLine($"contacts-browser-store-account={result.MutationFlags.ContactsBrowserStoreAccount}");
            Console.WriteLine($"writes-registry-or-policy={result.MutationFlags.WritesRegistryOrEnterprisePolicy}");

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionStorePackageAsync(string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var request = new BrowserExtensionStorePackageRequest
        {
            ExtensionSourceDirectory = source,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            Target = Value(args, "--target") ?? Value(args, "--browser"),
            SupportUrl = Value(args, "--support-url"),
            PrivacyUrl = Value(args, "--privacy-url"),
            ReleaseNotes = Value(args, "--release-notes"),
            ReleaseNotesPath = Value(args, "--release-notes-file")
        };

        var result = await new BrowserExtensionStorePackageService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            foreach (var target in result.Targets)
            {
                Console.WriteLine($"{target.Target}: {target.Status}");
                Console.WriteLine($"  extension-package={target.ExtensionPackagePath}");
                Console.WriteLine($"  extension-sha256={target.ExtensionPackageSha256}");
                Console.WriteLine($"  submission-bundle={target.SubmissionBundlePath}");
                Console.WriteLine($"  submission-sha256={target.SubmissionBundleSha256}");
                Console.WriteLine($"  manifest={target.SubmissionManifestPath}");
                foreach (var issue in target.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in target.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionPublicationPlanAsync(string[] args)
    {
        var requestedSource = Value(args, "--source") ??
            Value(args, "--source-dir") ??
            Value(args, "--extension-dir");
        var source = requestedSource ?? Path.Combine(AppContext.BaseDirectory, "browser-extension");
        if (requestedSource is null && !Directory.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "browser-extension");
        }

        var request = new BrowserExtensionPublicationPlanRequest
        {
            ExtensionSourceDirectory = source,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            Target = Value(args, "--target") ?? Value(args, "--browser"),
            StorePackageRoot = Value(args, "--store-package-root") ?? Value(args, "--store-package") ?? Value(args, "--package-root"),
            SupportUrl = Value(args, "--support-url"),
            PrivacyUrl = Value(args, "--privacy-url")
        };

        var result = await new BrowserExtensionPublicationPlanService().CreateAsync(request);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"source={result.SourceDirectory}");
            Console.WriteLine($"store-package-root={result.StorePackageRoot}");
            Console.WriteLine($"readiness={result.ReadinessOutputPath}");
            Console.WriteLine($"would-publish={result.WouldPublish}");
            Console.WriteLine($"would-contact-store-account={result.WouldContactStoreAccount}");
            Console.WriteLine($"would-upload-package={result.WouldUploadPackage}");
            Console.WriteLine($"would-install-extension={result.WouldInstallExtension}");

            foreach (var target in result.Targets)
            {
                Console.WriteLine($"{target.Target}: {target.Status}");
                Console.WriteLine($"  readiness={target.ReadinessStatus}");
                Console.WriteLine($"  extension-package={(string.IsNullOrWhiteSpace(target.ExtensionPackagePath) ? "missing" : target.ExtensionPackagePath)}");
                Console.WriteLine($"  submission-bundle={(string.IsNullOrWhiteSpace(target.StoreSubmissionBundlePath) ? "missing" : target.StoreSubmissionBundlePath)}");
                Console.WriteLine($"  would-publish={target.WouldPublish}");
                Console.WriteLine($"  would-upload-package={target.WouldUploadPackage}");
                Console.WriteLine($"  requires-developer-account={target.RequiresDeveloperAccount}");
                foreach (var issue in target.Issues)
                {
                    Console.WriteLine($"  ERROR: {issue}");
                }

                foreach (var warning in target.Warnings)
                {
                    Console.WriteLine($"  WARN: {warning}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionPublicationEvidenceRecordAsync(string[] args)
    {
        var target = Value(args, "--target") ??
            Value(args, "--browser") ??
            PositionalValues(
                args,
                "--target",
                "--browser",
                "--status",
                "--output",
                "-o",
                "--operator",
                "--operator-name",
                "--note",
                "--observed-at",
                "--evidence",
                "--evidence-path",
                "--package-evidence",
                "--package-proof",
                "--account-evidence",
                "--developer-account-evidence",
                "--store-account-evidence",
                "--submission-evidence",
                "--upload-evidence",
                "--validator-evidence",
                "--review-evidence",
                "--review-status-evidence",
                "--signing-evidence",
                "--availability-evidence",
                "--listing-evidence",
                "--metadata-evidence",
                "--install-evidence",
                "--installation-evidence",
                "--live-browser-evidence",
                "--browser-proof-evidence",
                "--host-status-evidence")
            .FirstOrDefault();
        var status = Value(args, "--status") ??
            PositionalValues(
                args,
                "--target",
                "--browser",
                "--output",
                "-o",
                "--operator",
                "--operator-name",
                "--note",
                "--observed-at",
                "--evidence",
                "--evidence-path",
                "--package-evidence",
                "--package-proof",
                "--account-evidence",
                "--developer-account-evidence",
                "--store-account-evidence",
                "--submission-evidence",
                "--upload-evidence",
                "--validator-evidence",
                "--review-evidence",
                "--review-status-evidence",
                "--signing-evidence",
                "--availability-evidence",
                "--listing-evidence",
                "--metadata-evidence",
                "--install-evidence",
                "--installation-evidence",
                "--live-browser-evidence",
                "--browser-proof-evidence",
                "--host-status-evidence")
            .Skip(string.IsNullOrWhiteSpace(target) ? 0 : 1)
            .FirstOrDefault();
        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedObservedAt))
        {
            observedAt = parsedObservedAt;
        }

        var result = await new BrowserExtensionPublicationEvidenceRecordService().RecordAsync(new BrowserExtensionPublicationEvidenceRecordRequest
        {
            Target = target,
            Status = status,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator") ?? Value(args, "--operator-name"),
            Note = Value(args, "--note"),
            ObservedAt = observedAt,
            Evidence = BrowserExtensionPublicationEvidenceInputs(args)
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.OutputPath}");
            Console.WriteLine($"target={result.Target}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"proof-complete={result.ProofComplete}");
            Console.WriteLine($"would-contact-store-account={result.WouldContactStoreAccount}");
            Console.WriteLine($"would-upload-package={result.WouldUploadPackage}");
            Console.WriteLine($"would-submit-review={result.WouldSubmitReview}");
            Console.WriteLine($"would-sign-or-publish={result.WouldSignOrPublish}");
            Console.WriteLine($"would-install-extension={result.WouldInstallExtension}");
            Console.WriteLine($"would-mutate-browser-profile={result.WouldMutateBrowserProfile}");
            Console.WriteLine($"would-register-native-host={result.WouldRegisterNativeHost}");
            Console.WriteLine($"would-apply-enterprise-policy={result.WouldApplyEnterprisePolicy}");
            if (result.MissingRequiredCategories.Count > 0)
            {
                Console.WriteLine($"missing-required={string.Join(",", result.MissingRequiredCategories)}");
            }

            if (result.MissingRecommendedCategories.Count > 0)
            {
                Console.WriteLine($"missing-recommended={string.Join(",", result.MissingRecommendedCategories)}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static List<BrowserExtensionPublicationEvidenceInput> BrowserExtensionPublicationEvidenceInputs(IReadOnlyList<string> args)
    {
        var evidence = new List<BrowserExtensionPublicationEvidenceInput>();
        foreach (var value in Values(args, "--evidence").Concat(Values(args, "--evidence-path")))
        {
            var parsed = SplitEvidenceCategory(value);
            AddBrowserExtensionPublicationEvidence(evidence, parsed.Category, parsed.Value);
        }

        AddBrowserExtensionPublicationEvidence(evidence, "package", Values(args, "--package-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "package", Values(args, "--package-proof"));
        AddBrowserExtensionPublicationEvidence(evidence, "account", Values(args, "--account-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "account", Values(args, "--developer-account-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "account", Values(args, "--store-account-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "submission", Values(args, "--submission-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "submission", Values(args, "--upload-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "submission", Values(args, "--validator-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "review", Values(args, "--review-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "review", Values(args, "--review-status-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "signing", Values(args, "--signing-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "signing", Values(args, "--availability-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "listing", Values(args, "--listing-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "listing", Values(args, "--metadata-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "install", Values(args, "--install-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "install", Values(args, "--installation-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "live-browser", Values(args, "--live-browser-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "live-browser", Values(args, "--browser-proof-evidence"));
        AddBrowserExtensionPublicationEvidence(evidence, "live-browser", Values(args, "--host-status-evidence"));
        return evidence;
    }

    private static (string Category, string Value) SplitEvidenceCategory(string value)
    {
        var separatorIndex = value.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return ("other", value);
        }

        return (value[..separatorIndex], value[(separatorIndex + 1)..]);
    }

    private static void AddBrowserExtensionPublicationEvidence(
        List<BrowserExtensionPublicationEvidenceInput> evidence,
        string category,
        IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddBrowserExtensionPublicationEvidence(evidence, category, value);
        }
    }

    private static void AddBrowserExtensionPublicationEvidence(
        List<BrowserExtensionPublicationEvidenceInput> evidence,
        string category,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        evidence.Add(new BrowserExtensionPublicationEvidenceInput
        {
            Category = category,
            Value = value
        });
    }

    private static async Task<int> BrowserExtensionLiveFixtureAsync(AppServices services, string[] args)
    {
        DateOnly? date = null;
        var dateText = Value(args, "--date");
        if (!string.IsNullOrWhiteSpace(dateText))
        {
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                Console.Error.WriteLine("browser-extension live-fixture --date must use yyyy-MM-dd.");
                return 2;
            }

            date = parsedDate;
        }

        var request = new BrowserExtensionLiveFixtureProofRequest
        {
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            Date = date,
            ExtensionSourceDirectory = Value(args, "--source") ??
                Value(args, "--source-dir") ??
                Value(args, "--extension-dir"),
            Browser = Value(args, "--browser"),
            ExtensionId = Value(args, "--extension-id") ??
                Value(args, "--chrome-extension-id") ??
                Value(args, "--edge-extension-id") ??
                Value(args, "--firefox-extension-id") ??
                Value(args, "--gecko-id"),
            HostExecutablePath = Value(args, "--host-exe") ??
                Value(args, "--host") ??
                Value(args, "--exe"),
            FixtureUrl = Value(args, "--fixture-url") ??
                Value(args, "--url"),
            PayloadPath = Value(args, "--payload") ??
                Value(args, "--payload-json"),
            StitchPackagePath = Value(args, "--stitch-package") ??
                Value(args, "--stitch-package-path"),
            Force = Has(args, "--force")
        };

        var result = await new BrowserExtensionLiveFixtureProofService(
            services.Paths,
            services.BrowserExtensionBridge)
            .CreateAsync(request, services.BrowserNativeHosts.GetStatus());

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"notes={result.NotesPath}");
            Console.WriteLine($"commands={result.CommandsPath}");
            Console.WriteLine($"serve-script={result.ServerScriptPath}");
            Console.WriteLine($"browser-launch-script={result.BrowserLaunchScriptPath}");
            Console.WriteLine($"browser-launch-plan={result.BrowserLaunchPlanPath}");
            Console.WriteLine($"diagnostics={result.DiagnosticsPath}");
            Console.WriteLine($"proof-manifest={result.ProofManifestPath}");
            Console.WriteLine($"proof-validation={result.ProofValidationPath}");
            Console.WriteLine($"safe-fixture={result.SafeFixturePath}");
            Console.WriteLine($"safe-fixture-url={result.SafeFixtureUrl}");
            Console.WriteLine($"browser-profile={result.BrowserLaunch.ProfileDirectory}");
            Console.WriteLine($"native-host-command={result.NativeHostInstallCommand}");
            if (result.Verification is not null)
            {
                Console.WriteLine($"verification={(result.Verification.Succeeded ? "passed" : "failed")}");
                Console.WriteLine($"import-result={result.Verification.ImportResultPath}");
                Console.WriteLine($"redacted-payload={result.Verification.RedactedPayloadPath}");
                Console.WriteLine($"workspace-file={result.Verification.WorkspaceFilePath}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserExtensionProofAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("browser-extension proof requires: validate --folder <proof-folder> [--source browser-extension] [--browser chrome|edge|firefox] [--browser-version version] [--extension-id id] [--extension-package zip] [--payload payload.json] [--redacted-payload redacted.json] [--stitch-package package-dir] [--import-result import-result.json] [--fixture-url url] [--json].");
            return 2;
        }

        var action = args[0].ToLowerInvariant();
        if (action is not ("validate" or "check" or "summarize" or "summary"))
        {
            Console.Error.WriteLine("browser-extension proof requires: validate.");
            return 2;
        }

        var proofArgs = args.Skip(1).ToArray();
        var folder = Value(proofArgs, "--folder") ??
            Value(proofArgs, "--proof-folder") ??
            Value(proofArgs, "--root") ??
            Value(proofArgs, "--output") ??
            Value(proofArgs, "-o") ??
            proofArgs.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(folder))
        {
            Console.Error.WriteLine("browser-extension proof validate requires --folder <proof-folder>.");
            return 2;
        }

        var result = await new BrowserExtensionProofManifestService().ValidateAsync(
            new BrowserExtensionProofValidationRequest
            {
                ProofRootPath = folder,
                ExtensionSourceDirectory = Value(proofArgs, "--source") ??
                    Value(proofArgs, "--source-dir") ??
                    Value(proofArgs, "--extension-dir"),
                ExtensionPackagePath = Value(proofArgs, "--extension-package") ??
                    Value(proofArgs, "--package") ??
                    Value(proofArgs, "--zip"),
                Browser = Value(proofArgs, "--browser"),
                BrowserVersion = Value(proofArgs, "--browser-version") ??
                    Value(proofArgs, "--version"),
                ExtensionId = Value(proofArgs, "--extension-id") ??
                    Value(proofArgs, "--chrome-extension-id") ??
                    Value(proofArgs, "--edge-extension-id") ??
                    Value(proofArgs, "--firefox-extension-id") ??
                    Value(proofArgs, "--gecko-id"),
                FixtureUrl = Value(proofArgs, "--fixture-url") ??
                    Value(proofArgs, "--url"),
                PayloadPath = Value(proofArgs, "--payload") ??
                    Value(proofArgs, "--payload-json"),
                RedactedPayloadPath = Value(proofArgs, "--redacted-payload") ??
                    Value(proofArgs, "--redacted-output"),
                StitchPackagePath = Value(proofArgs, "--stitch-package") ??
                    Value(proofArgs, "--stitch-package-path"),
                ImportResultPath = Value(proofArgs, "--import-result") ??
                    Value(proofArgs, "--import-result-json")
            },
            services.BrowserNativeHosts.GetStatus());

        if (Has(proofArgs, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.ProofRootPath}");
            Console.WriteLine($"manifest={result.ManifestPath}");
            Console.WriteLine($"validation={result.ValidationPath}");
            if (result.MissingEvidence.Count > 0)
            {
                Console.WriteLine("missing-evidence:");
                foreach (var missing in result.MissingEvidence)
                {
                    Console.WriteLine($"  {missing}");
                }
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BrowserNativeHostAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("browser-extension native-host requires: status, manifest, install, uninstall, or run.");
            return 2;
        }

        var action = args[0].ToLowerInvariant();
        if (action is "run" or "stdio")
        {
            return await RunBrowserNativeHostAsync(services);
        }

        if (action is "status" or "diagnostics")
        {
            var status = services.BrowserNativeHosts.GetStatus();
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(status, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine(services.BrowserNativeHosts.GetStatusSummary());
                foreach (var registration in status.Registrations)
                {
                    Console.WriteLine($"{registration.Browser}: installed={registration.Installed} manifest={registration.ManifestPath}");
                    if (!string.IsNullOrWhiteSpace(registration.RegistrySubKey))
                    {
                        Console.WriteLine($"  registry={registration.RegistrySubKey}");
                    }

                    Console.WriteLine($"  {registration.Message}");
                }
            }

            return 0;
        }

        if (action is "manifest" or "write-manifest" or "contract")
        {
            var request = BuildNativeHostInstallRequest(args);
            var outputDirectory = Value(args, "--output-dir") ?? Value(args, "--output") ?? Value(args, "-o");
            var results = await services.BrowserNativeHosts.WriteManifestsAsync(request, outputDirectory);
            return PrintNativeHostResults(results, args);
        }

        if (action is "install" or "register")
        {
            var request = BuildNativeHostInstallRequest(args);
            var results = await services.BrowserNativeHosts.InstallAsync(request);
            return PrintNativeHostResults(results, args);
        }

        if (action is "uninstall" or "remove" or "unregister")
        {
            var results = services.BrowserNativeHosts.Uninstall(ParseNativeHostBrowsers(args));
            return PrintNativeHostResults(results, args);
        }

        Console.Error.WriteLine("browser-extension native-host requires: status, manifest, install, uninstall, or run.");
        return 2;
    }

    private static async Task<int> RunBrowserNativeHostAsync(AppServices services)
    {
        var runner = new BrowserNativeMessagingHostRunner(services.BrowserExtensionBridge);
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await runner.RunOnceAsync(input, output);
    }

    private static BrowserNativeHostInstallRequest BuildNativeHostInstallRequest(string[] args)
    {
        return new BrowserNativeHostInstallRequest
        {
            Browsers = ParseNativeHostBrowsers(args),
            HostExecutablePath = Value(args, "--host-exe") ??
                Value(args, "--host") ??
                Value(args, "--exe") ??
                DefaultNativeHostExecutablePath(),
            ChromeExtensionId = Value(args, "--chrome-extension-id") ??
                Value(args, "--extension-id"),
            EdgeExtensionId = Value(args, "--edge-extension-id") ??
                Value(args, "--extension-id"),
            FirefoxExtensionId = Value(args, "--firefox-extension-id") ??
                Value(args, "--gecko-id")
        };
    }

    private static IReadOnlyList<BrowserNativeHostBrowser> ParseNativeHostBrowsers(string[] args)
    {
        var value = Value(args, "--browser") ?? Value(args, "--browsers") ?? "all";
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserNativeHostRegistrationService.NormalizeBrowsers(null);
        }

        var browsers = new List<BrowserNativeHostBrowser>();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = raw.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (normalized.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("chromium", StringComparison.OrdinalIgnoreCase))
            {
                browsers.Add(BrowserNativeHostBrowser.Chrome);
            }
            else if (normalized.Equals("edge", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("msedge", StringComparison.OrdinalIgnoreCase))
            {
                browsers.Add(BrowserNativeHostBrowser.Edge);
            }
            else if (normalized.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("mozilla", StringComparison.OrdinalIgnoreCase))
            {
                browsers.Add(BrowserNativeHostBrowser.Firefox);
            }
            else
            {
                throw new ArgumentException($"Unknown browser for native host registration: {raw}. Use chrome, edge, firefox, or all.");
            }
        }

        return BrowserNativeHostRegistrationService.NormalizeBrowsers(browsers);
    }

    private static int PrintNativeHostResults(
        IReadOnlyList<BrowserNativeHostInstallResult> results,
        string[] args)
    {
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(results, CliJsonOptions()));
        }
        else
        {
            foreach (var result in results)
            {
                Console.WriteLine(result.Message);
                if (!string.IsNullOrWhiteSpace(result.ManifestPath))
                {
                    Console.WriteLine($"manifest={result.ManifestPath}");
                }

                if (!string.IsNullOrWhiteSpace(result.RegistrySubKey))
                {
                    Console.WriteLine($"registry={result.RegistrySubKey}");
                }
            }
        }

        return results.All(result => result.Succeeded) ? 0 : 1;
    }

    private static string DefaultNativeHostExecutablePath()
    {
        return Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            throw new InvalidOperationException("Could not resolve GoatShot CLI executable path for native messaging host registration.");
    }

    private static bool IsBrowserNativeHostLaunch(IReadOnlyList<string> args)
    {
        return args.Any(arg =>
            arg.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> PrintImportAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("print-import requires: setup [--folder folder] [--enable] [--setup-note setup.md] [--json], contract [--folder folder] [--ensure-folder] [--json], diagnostics [--folder folder] [--ensure-folder] [--json], or import <file> [--source-app app] [--title title] [--json].");
            return 2;
        }

        var service = new VirtualPrinterImportService(services.Paths, services.Settings, services.WorkspaceStore);
        if (args[0].Equals("setup", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("configure", StringComparison.OrdinalIgnoreCase))
        {
            if (!ManagedPolicyService.IsVirtualPrinterImportAllowed(services.Settings, out var setupPolicyReason))
            {
                return PrintPolicyBlocked(services, "print-import setup", setupPolicyReason, Has(args, "--json"));
            }

            var folder = Value(args, "--folder") ?? Value(args, "--drop-folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                services.Settings.VirtualPrinterImportFolder = folder;
            }

            var enable = Has(args, "--enable") ||
                Has(args, "--enable-watch") ||
                Has(args, "--enable-watched-folder") ||
                Has(args, "--enable-watch-folder");
            if (enable)
            {
                services.Settings.EnableVirtualPrinterImport = true;
            }

            var includeSubdirectoriesChanged = false;
            if (Has(args, "--include-subdirectories") || Has(args, "--recursive"))
            {
                services.Settings.VirtualPrinterImportIncludeSubdirectories = true;
                includeSubdirectoriesChanged = true;
            }
            else if (Has(args, "--no-include-subdirectories") || Has(args, "--no-recursive"))
            {
                services.Settings.VirtualPrinterImportIncludeSubdirectories = false;
                includeSubdirectoriesChanged = true;
            }

            var saveSettings = Has(args, "--save-settings") ||
                Has(args, "--persist") ||
                enable ||
                includeSubdirectoriesChanged;
            if (saveSettings)
            {
                services.SettingsStore.Save(services.Settings);
            }

            var setupResult = service.Setup(new VirtualPrinterSetupRequest
            {
                SetupNotePath = Value(args, "--setup-note") ??
                    Value(args, "--note") ??
                    Value(args, "--output") ??
                    Value(args, "-o")
            });
            setupResult.SettingsSaved = saveSettings;
            if (saveSettings && setupResult.Succeeded)
            {
                setupResult.Message += " Settings saved.";
            }

            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(setupResult, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine("GoatShot print-import setup");
                Console.WriteLine($"succeeded={setupResult.Succeeded}");
                Console.WriteLine($"settings-saved={setupResult.SettingsSaved}");
                Console.WriteLine($"enabled={setupResult.Enabled}");
                Console.WriteLine($"watched-folder-state={setupResult.WatchedFolderState}");
                Console.WriteLine($"drop-folder={setupResult.DropFolder}");
                Console.WriteLine($"setup-note={setupResult.SetupNotePath}");
                Console.WriteLine($"setup-note-written={setupResult.SetupNoteWritten}");
                Console.WriteLine($"include-subdirectories={setupResult.IncludeSubdirectories}");
                Console.WriteLine($"supported-extensions={string.Join(",", setupResult.SupportedExtensions)}");
                if (setupResult.Diagnostics is not null)
                {
                    Console.WriteLine($"policy-status={setupResult.Diagnostics.PolicyStatus}");
                    Console.WriteLine($"drop-folder-status={setupResult.Diagnostics.DropFolderStatus}");
                    Console.WriteLine($"installed-printer-probe={setupResult.Diagnostics.InstalledPrinterProbeStatus}");
                    Console.WriteLine($"microsoft-print-to-pdf={setupResult.Diagnostics.MicrosoftPrintToPdfDetected}");
                    Console.WriteLine($"current-process-elevated={setupResult.Diagnostics.CurrentProcessElevated}");
                    Console.WriteLine($"driver-install-implemented={setupResult.Diagnostics.DriverInstallImplemented}");
                    Console.WriteLine($"driver-install-requires-admin={setupResult.Diagnostics.DriverInstallRequiresAdmin}");
                    Console.WriteLine($"driver-signing-required={setupResult.Diagnostics.DriverSigningRequired}");
                }

                Console.WriteLine(setupResult.Message);
                foreach (var warning in setupResult.Warnings)
                {
                    Console.WriteLine($"warning={warning}");
                }

                foreach (var issue in setupResult.Issues)
                {
                    Console.WriteLine($"issue={issue}");
                }

                foreach (var step in setupResult.NextSteps)
                {
                    Console.WriteLine($"next-step={step}");
                }
            }

            return setupResult.Succeeded ? 0 : 1;
        }

        if (args[0].Equals("contract", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            if ((Has(args, "--enable") || Has(args, "--ensure-folder")) &&
                !ManagedPolicyService.IsVirtualPrinterImportAllowed(services.Settings, out var printPolicyReason))
            {
                return PrintPolicyBlocked(services, "print-import contract", printPolicyReason, Has(args, "--json"));
            }

            var folder = Value(args, "--folder") ?? Value(args, "--drop-folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                services.Settings.VirtualPrinterImportFolder = folder;
            }

            if (Has(args, "--enable"))
            {
                services.Settings.EnableVirtualPrinterImport = true;
            }

            var contract = service.GetContract(ensureFolder: Has(args, "--ensure-folder"));
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(contract, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine("GoatShot virtual printer/file-drop import contract");
                Console.WriteLine($"enabled={contract.Enabled}");
                Console.WriteLine($"drop-folder={contract.DropFolder}");
                Console.WriteLine($"include-subdirectories={contract.IncludeSubdirectories}");
                Console.WriteLine($"supported-extensions={string.Join(",", contract.SupportedExtensions)}");
                Console.WriteLine(contract.DriverInstallStatus);
                Console.WriteLine(contract.PrivacyNote);
            }

            return 0;
        }

        if (args[0].Equals("diagnostics", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("readiness", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("driver-feasibility", StringComparison.OrdinalIgnoreCase))
        {
            if ((Has(args, "--enable") || Has(args, "--ensure-folder")) &&
                !ManagedPolicyService.IsVirtualPrinterImportAllowed(services.Settings, out var diagnosticsPolicyReason))
            {
                return PrintPolicyBlocked(services, "print-import diagnostics", diagnosticsPolicyReason, Has(args, "--json"));
            }

            var folder = Value(args, "--folder") ?? Value(args, "--drop-folder");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                services.Settings.VirtualPrinterImportFolder = folder;
            }

            if (Has(args, "--enable"))
            {
                services.Settings.EnableVirtualPrinterImport = true;
            }

            var diagnostics = service.GetFeasibilityDiagnostics(ensureFolder: Has(args, "--ensure-folder"));
            if (Has(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(diagnostics, CliJsonOptions()));
            }
            else
            {
                Console.WriteLine("GoatShot virtual printer/file-drop diagnostics");
                Console.WriteLine($"enabled={diagnostics.Enabled}");
                Console.WriteLine($"watched-folder-state={diagnostics.WatchedFolderState}");
                Console.WriteLine($"policy-status={diagnostics.PolicyStatus}");
                Console.WriteLine($"drop-folder={diagnostics.DropFolder}");
                Console.WriteLine($"drop-folder-status={diagnostics.DropFolderStatus}");
                Console.WriteLine($"drop-folder-exists={diagnostics.DropFolderExists}");
                Console.WriteLine($"drop-folder-writable={diagnostics.DropFolderWritable}");
                Console.WriteLine($"include-subdirectories={diagnostics.IncludeSubdirectories}");
                Console.WriteLine($"supported-extensions={string.Join(",", diagnostics.SupportedExtensions)}");
                Console.WriteLine($"installed-printer-probe={diagnostics.InstalledPrinterProbeStatus}");
                Console.WriteLine($"microsoft-print-to-pdf={diagnostics.MicrosoftPrintToPdfDetected}");
                Console.WriteLine($"microsoft-xps-writer={diagnostics.MicrosoftXpsWriterDetected}");
                Console.WriteLine($"current-process-elevated={diagnostics.CurrentProcessElevated}");
                Console.WriteLine($"driver-install-implemented={diagnostics.DriverInstallImplemented}");
                Console.WriteLine($"driver-install-requires-admin={diagnostics.DriverInstallRequiresAdmin}");
                Console.WriteLine($"driver-signing-required={diagnostics.DriverSigningRequired}");
                Console.WriteLine(diagnostics.Summary);
                if (diagnostics.Warnings.Count > 0)
                {
                    Console.WriteLine($"warnings={string.Join(" | ", diagnostics.Warnings)}");
                }

                if (diagnostics.Issues.Count > 0)
                {
                    Console.WriteLine($"issues={string.Join(" | ", diagnostics.Issues)}");
                }
            }

            return 0;
        }

        if (!args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("print-import requires: setup, contract, diagnostics, or import.");
            return 2;
        }

        if (!ManagedPolicyService.IsVirtualPrinterImportAllowed(services.Settings, out var importPolicyReason))
        {
            return PrintPolicyBlocked(services, "print-import import", importPolicyReason, Has(args, "--json"));
        }

        var input = args.Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("print-import import requires a PDF/image file path.");
            return 2;
        }

        var result = await service.ImportAsync(new VirtualPrinterImportRequest
        {
            Path = input,
            SourceApplication = Value(args, "--source-app") ?? Value(args, "--app"),
            DocumentTitle = Value(args, "--title") ?? Value(args, "--document-title")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            if (result.Item is not null)
            {
                Console.WriteLine(result.Item.FilePath);
                Console.Error.WriteLine($"workspace-id={result.Item.Id}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static int PrintPolicyBlocked(AppServices services, string action, string reason, bool asJson)
    {
        WriteManagedPolicyAudit(services, action, reason);
        var result = new
        {
            succeeded = false,
            blockedByPolicy = true,
            action,
            message = reason
        };
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.Error.WriteLine(reason);
        }

        return 1;
    }

    private static void WriteManagedPolicyAudit(AppServices services, string action, string reason)
    {
        try
        {
            Directory.CreateDirectory(services.Paths.LogsRoot);
            var entry = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                action = SensitiveTextDetector.Redact(action),
                blockedByPolicy = true,
                reason = SensitiveTextDetector.Redact(reason)
            };
            File.AppendAllText(
                Path.Combine(services.Paths.LogsRoot, "managed-policy-audit.jsonl"),
                JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine);
        }
        catch
        {
            // Policy blocks should still be enforced if the audit log cannot be written.
        }
    }

    private static async Task<int> DiagnosticsAsync(AppServices services, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("print", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = services.Diagnostics.GetSnapshot();
            Console.WriteLine("GoatShot diagnostics");
            Console.WriteLine($"OS: {snapshot.OsDescription}");
            Console.WriteLine($"Runtime: {snapshot.RuntimeDescription}");
            Console.WriteLine();
            Console.WriteLine(snapshot.CaptureEngine);
            Console.WriteLine();
            Console.WriteLine(snapshot.RecordingEngine);
            Console.WriteLine();
            Console.WriteLine(snapshot.RecordingReadiness);
            Console.WriteLine();
            Console.WriteLine(snapshot.EncoderStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.OcrStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.AiStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.PolicyStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.StartupStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.MetadataIndexStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.UploadQueueStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.PrintImportStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.PluginStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.BrowserBridgeStatus);
            Console.WriteLine();
            Console.WriteLine(snapshot.SharingStatus);
            return 0;
        }

        if (args[0].Equals("devices", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("recording-devices", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintRecordingDevicesAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("recording", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("recording-readiness", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("recording-preflight", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintRecordingReadinessAsync(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("recording-media", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("media", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("ffprobe", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintRecordingMediaProbeAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("capture-engine", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("capture", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("production-capture", StringComparison.OrdinalIgnoreCase))
        {
            return await PrintCaptureEngineDiagnosticsAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("scrolling-fixtures", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("scroll-fixtures", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("scrolling-stress", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateScrollingFixturesAsync(args.Skip(1).ToArray());
        }

        if (args[0].Equals("providers", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("provider-diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderDiagnostics(services, args.Skip(1).ToArray());
        }

        if (args[0].Equals("android", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("adb", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("android-device", StringComparison.OrdinalIgnoreCase))
        {
            return await AndroidDiagnosticsAsync(services, args.Skip(1).ToArray());
        }

        if (!args[0].Equals("bundle", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("diagnostics requires: print, devices, recording, recording-media, capture-engine, scrolling-fixtures, providers, android, or bundle [--output bundle.zip] [--copy].");
            return 2;
        }

        var result = await services.DiagnosticBundles.CreateAsync(Value(args, "--output") ?? Value(args, "-o"));
        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            Console.WriteLine(result.Path);
        }

        foreach (var entry in result.Entries)
        {
            Console.Error.WriteLine($"bundle-entry={entry}");
        }

        if (result.Succeeded && Has(args, "--copy") && !string.IsNullOrWhiteSpace(result.Path))
        {
            ClipboardInterop.SetFileDropList([result.Path]);
            Console.Error.WriteLine("copied=true");
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> AndroidDiagnosticsAsync(AppServices services, string[] args)
    {
        var service = new AndroidAdbCaptureService(services.Paths, services.WorkspaceStore);
        var diagnostics = await service.GetDiagnosticsAsync(Value(args, "--adb") ?? Value(args, "--adb-path"));
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(diagnostics, CliJsonOptions()));
            return 0;
        }

        Console.WriteLine("GoatShot Android ADB diagnostics");
        Console.WriteLine($"status={diagnostics.Status}");
        Console.WriteLine($"ready={diagnostics.Ready}");
        Console.WriteLine($"adb-path={(string.IsNullOrWhiteSpace(diagnostics.AdbPath) ? "not-found" : diagnostics.AdbPath)}");
        Console.WriteLine(diagnostics.Message);
        if (diagnostics.Devices.Count == 0)
        {
            Console.WriteLine("devices=0");
            return 0;
        }

        foreach (var device in diagnostics.Devices)
        {
            Console.WriteLine($"device={device.Serial} state={device.State} model={device.Model} product={device.Product} transport={device.TransportId}");
        }

        return 0;
    }

    private static async Task<int> GenerateScrollingFixturesAsync(string[] args)
    {
        var outputDirectory =
            Value(args, "--output-dir") ??
            Value(args, "--output") ??
            Value(args, "-o") ??
            Path.Combine("artifacts", "scrolling-fixtures");

        var report = await ScrollingCaptureStressService.GenerateAsync(outputDirectory);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
        }
        else
        {
            Console.WriteLine($"Generated {report.Scenarios.Count} scrolling fixture scenario(s).");
            Console.WriteLine(report.OutputDirectory);
            Console.WriteLine(report.PrivacyNote);
            Console.WriteLine(report.RetryGuidance);
            foreach (var scenario in report.Scenarios)
            {
                Console.WriteLine(
                    $"{scenario.Name}: axis={scenario.Axis}, frames={scenario.FrameCount}, sticky={scenario.DetectedStickyPixels}px, overlap={scenario.BestOverlapPixels}px, stitched={scenario.StitchedWidth}x{scenario.StitchedHeight}");
                Console.WriteLine($"  {scenario.StitchedPath}");
            }
        }

        return 0;
    }

    private static async Task<int> ManualValidationAsync(AppServices services, string[] args)
    {
        if (args.Length > 0 &&
            (args[0].Equals("summarize", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("summary", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("validate", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("check", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationSummaryAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("proof-plan", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("runbook", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("plan", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationProofPlanAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("findings", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("finding-list", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("findings-list", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("defects", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("gaps", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationFindingsAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("baseline", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("complete-baseline", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("baseline-setup", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationBaselineAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("desktop-proof", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("desktop-accessibility", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("accessibility-proof", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationDesktopProofAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("hardware-proof", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("hardware-readiness", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("device-proof", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("device-readiness", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationHardwareProofAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("operator-pack", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("required-proof-pack", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("desktop-operator-pack", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("human-proof-pack", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationOperatorPackAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("clean-machine-kit", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("clean-machine-proof-kit", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("clean-machine-proof", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("installer-proof-kit", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("installer-kit", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationCleanMachineProofKitAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("record-clean-machine-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("clean-machine-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("record-clean-machine", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("clean-machine-record", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("installer-evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationCleanMachineEvidenceRecordAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("record-desktop-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("desktop-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("record-required-desktop-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("required-desktop-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("accessibility-evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationDesktopEvidenceRecordAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("record-hardware-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("hardware-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("record-device-evidence", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("device-evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationHardwareEvidenceRecordAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (args[0].Equals("record-lane", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("lane", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("update-lane", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("complete-lane", StringComparison.OrdinalIgnoreCase)))
        {
            return await ManualValidationRecordLaneAsync(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            !args[0].StartsWith("-", StringComparison.Ordinal) &&
            !args[0].Equals("create", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("init", StringComparison.OrdinalIgnoreCase) &&
            !args[0].Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("manual-validation requires: create [--date yyyy-MM-dd] [--output folder] [--include-diagnostics-bundle] [--force] [--json], baseline --folder <evidence-folder> [--run-commands] [--json], desktop-proof --folder <evidence-folder> [--run-commands] [--json], hardware-proof --folder <evidence-folder> [--run-commands] [--json], operator-pack --folder <evidence-folder> [--output folder] [--json], clean-machine-kit --folder <evidence-folder> [--portable-zip zip] [--installer exe] [--copy-package] [--json], record-clean-machine-evidence --folder <evidence-folder> --status passed|failed|blocked|pending [--machine-evidence path ...] [--json], record-desktop-evidence --folder <evidence-folder> --lane keyboard|screen-reader|text-scaling|high-contrast|live-region-drag --status passed|failed|blocked|pending [--notes-evidence path ...] [--json], record-hardware-evidence --folder <evidence-folder> --lane multi-monitor-capture|multi-monitor-recording|long-recording|android-safe-device-proof --status passed|failed|blocked|pending [--notes-evidence path ...] [--json], record-lane --folder <evidence-folder> --lane <lane> --status passed|failed|blocked|pending|not-applicable [--note text] [--evidence path] [--json], summarize --folder <evidence-folder> [--json], proof-plan --folder <evidence-folder> [--output folder] [--json], or findings --folder <evidence-folder> [--output folder] [--json].");
            return 2;
        }

        if (args.Length > 0 &&
            (args[0].Equals("create", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("init", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("new", StringComparison.OrdinalIgnoreCase)))
        {
            args = args.Skip(1).ToArray();
        }

        DateOnly? date = null;
        var dateText = Value(args, "--date");
        DateOnly parsedDate = default;
        if (!string.IsNullOrWhiteSpace(dateText) &&
            !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            Console.Error.WriteLine("manual-validation --date must use yyyy-MM-dd.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(dateText))
        {
            date = parsedDate;
        }

        var request = new ManualValidationHarnessRequest(
            OutputPath: Value(args, "--output") ?? Value(args, "-o"),
            Date: date,
            IncludeDiagnosticsBundle: Has(args, "--include-diagnostics-bundle") ||
                                      Has(args, "--diagnostics-bundle") ||
                                      Has(args, "--bundle-diagnostics"),
            Force: Has(args, "--force"));

        var service = new ManualValidationHarnessService();
        var result = await service.CreateAsync(
            request,
            request.IncludeDiagnosticsBundle
                ? services.DiagnosticBundles.CreateAsync
                : null);

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"summary={result.SummaryPath}");
            Console.WriteLine($"readme={result.ReadmePath}");
            Console.WriteLine($"commands={result.CommandsPath}");
            Console.WriteLine($"diagnostics-readme={result.DiagnosticsReadmePath}");
            Console.WriteLine($"diagnostics-bundle={result.DiagnosticsBundlePath ?? "not-created"}");
            Console.WriteLine($"templates={result.Templates.Count}");
            foreach (var warning in result.Warnings)
            {
                Console.Error.WriteLine($"warning={warning}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationCleanMachineProofKitAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--output",
            "--repo-root",
            "--portable-zip",
            "--package",
            "--installer",
            "--installer-path",
            "--cli-path");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation clean-machine-kit requires --folder <evidence-folder>.");
            return 2;
        }

        var result = await new ManualValidationCleanMachineProofKitService().CreateAsync(new ManualValidationCleanMachineProofKitRequest
        {
            RootPath = root,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            RepoRoot = Value(args, "--repo-root"),
            PortableZipPath = Value(args, "--portable-zip") ?? Value(args, "--package"),
            InstallerPath = Value(args, "--installer") ?? Value(args, "--installer-path"),
            CliPath = Value(args, "--cli-path"),
            CopyPackage = Has(args, "--copy-package") ||
                          Has(args, "--copy-packages") ||
                          Has(args, "--include-package") ||
                          Has(args, "--include-packages")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"output={result.OutputRoot}");
            Console.WriteLine($"runbook={result.RunbookPath}");
            Console.WriteLine($"manifest={result.ManifestPath}");
            Console.WriteLine($"script={result.ScriptPath}");
            Console.WriteLine($"evidence-checklist={result.EvidenceChecklistPath}");
            Console.WriteLine($"portable-found={result.PortablePackage.Exists}");
            Console.WriteLine($"portable-sha256={(string.IsNullOrWhiteSpace(result.PortablePackage.Sha256) ? "pending" : result.PortablePackage.Sha256)}");
            Console.WriteLine($"installer-found={result.InstallerPackage.Exists}");
            Console.WriteLine($"self-contained-package-copy={result.SelfContainedPackageCopy}");
            Console.WriteLine($"ready-for-clean-machine-run={result.ReadyForCleanMachineRun}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  {warning}");
                }
            }

            if (result.Issues.Count > 0)
            {
                Console.WriteLine("issues:");
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"  {issue}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationCleanMachineEvidenceRecordAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--output",
            "-o",
            "--status",
            "--result",
            "--operator",
            "--operator-name",
            "--note",
            "--observed-at",
            "--evidence",
            "--evidence-path",
            "--machine-evidence",
            "--machine-info-evidence",
            "--environment-evidence",
            "--package-evidence",
            "--portable-evidence",
            "--portable-zip-evidence",
            "--manifest-evidence",
            "--hash-evidence",
            "--portable-hash-evidence",
            "--checksum-evidence",
            "--diagnostics-evidence",
            "--diagnostics-print-evidence",
            "--paths-evidence",
            "--isolated-paths-evidence",
            "--first-launch-evidence",
            "--main-window-evidence",
            "--render-evidence",
            "--settings-evidence",
            "--settings-save-evidence",
            "--capture-export-evidence",
            "--capture-loop-evidence",
            "--import-export-evidence",
            "--installer-evidence",
            "--installer-skipped-evidence",
            "--installer-log-evidence",
            "--install-uninstall-evidence",
            "--privacy-evidence",
            "--redaction-evidence",
            "--review-evidence",
            "--script-result-evidence",
            "--helper-result-evidence");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        var status = Value(args, "--status") ??
            Value(args, "--result") ??
            positional.Skip(string.IsNullOrWhiteSpace(root) ? 0 : 1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(status))
        {
            Console.Error.WriteLine("manual-validation record-clean-machine-evidence requires --folder <evidence-folder> --status passed|failed|blocked|pending.");
            return 2;
        }

        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        DateTimeOffset parsedObservedAt = default;
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            !DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedObservedAt))
        {
            Console.Error.WriteLine("manual-validation record-clean-machine-evidence --observed-at must be a valid date/time.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(observedAtText))
        {
            observedAt = parsedObservedAt;
        }

        var result = await new ManualValidationCleanMachineEvidenceRecordService().RecordAsync(new ManualValidationCleanMachineEvidenceRecordRequest
        {
            RootPath = root,
            Status = status,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator") ?? Value(args, "--operator-name"),
            Note = Value(args, "--note"),
            ObservedAt = observedAt,
            Evidence = CleanMachineEvidenceInputs(args)
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"output={result.OutputPath}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"proof-complete={result.ProofComplete}");
            Console.WriteLine($"evidence={result.Evidence.Count}");
            Console.WriteLine($"would-launch-app={result.WouldLaunchApp}");
            Console.WriteLine($"would-run-installer={result.WouldRunInstaller}");
            Console.WriteLine($"would-install-or-uninstall={result.WouldInstallOrUninstall}");
            Console.WriteLine($"would-mutate-user-profile={result.WouldMutateUserProfile}");
            Console.WriteLine($"would-capture-screen={result.WouldCaptureScreen}");
            Console.WriteLine($"would-update-manual-lane={result.WouldUpdateManualLane}");
            Console.WriteLine($"would-certify-clean-machine={result.WouldCertifyCleanMachine}");
            if (result.MissingRequiredCategories.Count > 0)
            {
                Console.WriteLine($"missing-required={string.Join(",", result.MissingRequiredCategories)}");
            }

            if (result.MissingRecommendedCategories.Count > 0)
            {
                Console.WriteLine($"missing-recommended={string.Join(",", result.MissingRecommendedCategories)}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationDesktopEvidenceRecordAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--output",
            "-o",
            "--lane",
            "--id",
            "--status",
            "--result",
            "--operator",
            "--operator-name",
            "--note",
            "--observed-at",
            "--evidence",
            "--evidence-path",
            "--notes-evidence",
            "--note-evidence",
            "--observations-evidence",
            "--surface-coverage-evidence",
            "--surface-evidence",
            "--focus-order-evidence",
            "--tab-order-evidence",
            "--result-evidence",
            "--outcome-evidence",
            "--privacy-evidence",
            "--redaction-evidence",
            "--focus-visual-evidence",
            "--failure-media-evidence",
            "--screen-reader-evidence",
            "--narrator-evidence",
            "--nvda-evidence",
            "--status-output-evidence",
            "--control-names-evidence",
            "--scale-125-evidence",
            "--text-scale-125-evidence",
            "--scale-150-evidence",
            "--text-scale-150-evidence",
            "--scale-200-evidence",
            "--text-scale-200-evidence",
            "--layout-evidence",
            "--layout-review-evidence",
            "--restore-evidence",
            "--theme-evidence",
            "--main-evidence",
            "--settings-evidence",
            "--focus-selected-evidence",
            "--dialog-evidence",
            "--safe-content-evidence",
            "--start-evidence",
            "--snap-evidence",
            "--complete-evidence",
            "--cancel-evidence",
            "--shift-bypass-evidence",
            "--pixel-lens-evidence");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        var lane = Value(args, "--lane") ??
            Value(args, "--id") ??
            positional.Skip(string.IsNullOrWhiteSpace(root) ? 0 : 1).FirstOrDefault();
        var status = Value(args, "--status") ??
            Value(args, "--result") ??
            positional.Skip((string.IsNullOrWhiteSpace(root) ? 0 : 1) + (string.IsNullOrWhiteSpace(lane) ? 0 : 1)).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(lane) ||
            string.IsNullOrWhiteSpace(status))
        {
            Console.Error.WriteLine("manual-validation record-desktop-evidence requires --folder <evidence-folder> --lane keyboard|screen-reader|text-scaling|high-contrast|live-region-drag --status passed|failed|blocked|pending.");
            return 2;
        }

        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        DateTimeOffset parsedObservedAt = default;
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            !DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedObservedAt))
        {
            Console.Error.WriteLine("manual-validation record-desktop-evidence --observed-at must be a valid date/time.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(observedAtText))
        {
            observedAt = parsedObservedAt;
        }

        var result = await new ManualValidationDesktopEvidenceRecordService().RecordAsync(new ManualValidationDesktopEvidenceRecordRequest
        {
            RootPath = root,
            Lane = lane,
            Status = status,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator") ?? Value(args, "--operator-name"),
            Note = Value(args, "--note"),
            ObservedAt = observedAt,
            Evidence = DesktopEvidenceInputs(args)
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"output={result.OutputPath}");
            Console.WriteLine($"lane={result.LaneId}");
            Console.WriteLine($"title={result.LaneTitle}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"proof-complete={result.ProofComplete}");
            Console.WriteLine($"evidence={result.Evidence.Count}");
            Console.WriteLine($"would-launch-app={result.WouldLaunchApp}");
            Console.WriteLine($"would-change-windows-settings={result.WouldChangeWindowsSettings}");
            Console.WriteLine($"would-capture-screen={result.WouldCaptureScreen}");
            Console.WriteLine($"would-record-screen={result.WouldRecordScreen}");
            Console.WriteLine($"would-update-manual-lane={result.WouldUpdateManualLane}");
            Console.WriteLine($"would-certify-accessibility={result.WouldCertifyAccessibility}");
            Console.WriteLine($"would-mutate-user-profile={result.WouldMutateUserProfile}");
            if (result.MissingRequiredCategories.Count > 0)
            {
                Console.WriteLine($"missing-required={string.Join(",", result.MissingRequiredCategories)}");
            }

            if (result.MissingRecommendedCategories.Count > 0)
            {
                Console.WriteLine($"missing-recommended={string.Join(",", result.MissingRecommendedCategories)}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static List<ManualValidationDesktopEvidenceInput> DesktopEvidenceInputs(IReadOnlyList<string> args)
    {
        var evidence = new List<ManualValidationDesktopEvidenceInput>();
        foreach (var value in Values(args, "--evidence").Concat(Values(args, "--evidence-path")))
        {
            var parsed = SplitEvidenceCategory(value);
            AddDesktopEvidence(evidence, parsed.Category, parsed.Value);
        }

        AddDesktopEvidence(evidence, "notes", Values(args, "--notes-evidence"));
        AddDesktopEvidence(evidence, "notes", Values(args, "--note-evidence"));
        AddDesktopEvidence(evidence, "notes", Values(args, "--observations-evidence"));
        AddDesktopEvidence(evidence, "surface-coverage", Values(args, "--surface-coverage-evidence"));
        AddDesktopEvidence(evidence, "surface-coverage", Values(args, "--surface-evidence"));
        AddDesktopEvidence(evidence, "focus-order", Values(args, "--focus-order-evidence"));
        AddDesktopEvidence(evidence, "focus-order", Values(args, "--tab-order-evidence"));
        AddDesktopEvidence(evidence, "result", Values(args, "--result-evidence"));
        AddDesktopEvidence(evidence, "result", Values(args, "--outcome-evidence"));
        AddDesktopEvidence(evidence, "privacy", Values(args, "--privacy-evidence"));
        AddDesktopEvidence(evidence, "privacy", Values(args, "--redaction-evidence"));
        AddDesktopEvidence(evidence, "focus-visual", Values(args, "--focus-visual-evidence"));
        AddDesktopEvidence(evidence, "failure-media", Values(args, "--failure-media-evidence"));
        AddDesktopEvidence(evidence, "screen-reader", Values(args, "--screen-reader-evidence"));
        AddDesktopEvidence(evidence, "narrator", Values(args, "--narrator-evidence"));
        AddDesktopEvidence(evidence, "nvda", Values(args, "--nvda-evidence"));
        AddDesktopEvidence(evidence, "status-output", Values(args, "--status-output-evidence"));
        AddDesktopEvidence(evidence, "control-names", Values(args, "--control-names-evidence"));
        AddDesktopEvidence(evidence, "scale-125", Values(args, "--scale-125-evidence"));
        AddDesktopEvidence(evidence, "scale-125", Values(args, "--text-scale-125-evidence"));
        AddDesktopEvidence(evidence, "scale-150", Values(args, "--scale-150-evidence"));
        AddDesktopEvidence(evidence, "scale-150", Values(args, "--text-scale-150-evidence"));
        AddDesktopEvidence(evidence, "scale-200", Values(args, "--scale-200-evidence"));
        AddDesktopEvidence(evidence, "scale-200", Values(args, "--text-scale-200-evidence"));
        AddDesktopEvidence(evidence, "layout-review", Values(args, "--layout-evidence"));
        AddDesktopEvidence(evidence, "layout-review", Values(args, "--layout-review-evidence"));
        AddDesktopEvidence(evidence, "restore", Values(args, "--restore-evidence"));
        AddDesktopEvidence(evidence, "theme", Values(args, "--theme-evidence"));
        AddDesktopEvidence(evidence, "main", Values(args, "--main-evidence"));
        AddDesktopEvidence(evidence, "settings", Values(args, "--settings-evidence"));
        AddDesktopEvidence(evidence, "focus-selected", Values(args, "--focus-selected-evidence"));
        AddDesktopEvidence(evidence, "dialog", Values(args, "--dialog-evidence"));
        AddDesktopEvidence(evidence, "safe-content", Values(args, "--safe-content-evidence"));
        AddDesktopEvidence(evidence, "start", Values(args, "--start-evidence"));
        AddDesktopEvidence(evidence, "snap", Values(args, "--snap-evidence"));
        AddDesktopEvidence(evidence, "complete", Values(args, "--complete-evidence"));
        AddDesktopEvidence(evidence, "cancel", Values(args, "--cancel-evidence"));
        AddDesktopEvidence(evidence, "shift-bypass", Values(args, "--shift-bypass-evidence"));
        AddDesktopEvidence(evidence, "pixel-lens", Values(args, "--pixel-lens-evidence"));
        return evidence;
    }

    private static void AddDesktopEvidence(
        List<ManualValidationDesktopEvidenceInput> evidence,
        string category,
        IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddDesktopEvidence(evidence, category, value);
        }
    }

    private static void AddDesktopEvidence(
        List<ManualValidationDesktopEvidenceInput> evidence,
        string category,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        evidence.Add(new ManualValidationDesktopEvidenceInput
        {
            Category = category,
            Value = value
        });
    }

    private static async Task<int> ManualValidationHardwareEvidenceRecordAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--output",
            "-o",
            "--lane",
            "--id",
            "--status",
            "--result",
            "--operator",
            "--operator-name",
            "--note",
            "--observed-at",
            "--evidence",
            "--evidence-path",
            "--notes-evidence",
            "--note-evidence",
            "--observations-evidence",
            "--safe-content-evidence",
            "--content-evidence",
            "--topology-evidence",
            "--display-evidence",
            "--capture-output-evidence",
            "--capture-evidence",
            "--dimensions-evidence",
            "--privacy-evidence",
            "--redaction-evidence",
            "--wgc-diagnostics-evidence",
            "--capture-diagnostics-evidence",
            "--failure-media-evidence",
            "--recording-output-evidence",
            "--recording-evidence",
            "--playback-evidence",
            "--audio-sync-evidence",
            "--encoder-diagnostics-evidence",
            "--duration-evidence",
            "--sync-evidence",
            "--recovery-evidence",
            "--devices-evidence",
            "--ffprobe-evidence",
            "--device-evidence",
            "--android-device-evidence",
            "--screenshot-or-video-evidence",
            "--screenshot-evidence",
            "--video-evidence",
            "--import-result-evidence",
            "--cleanup-evidence",
            "--preview-evidence",
            "--adb-diagnostics-evidence",
            "--android-diagnostics-evidence");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        var lane = Value(args, "--lane") ??
            Value(args, "--id") ??
            positional.Skip(string.IsNullOrWhiteSpace(root) ? 0 : 1).FirstOrDefault();
        var status = Value(args, "--status") ??
            Value(args, "--result") ??
            positional.Skip((string.IsNullOrWhiteSpace(root) ? 0 : 1) + (string.IsNullOrWhiteSpace(lane) ? 0 : 1)).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(lane) ||
            string.IsNullOrWhiteSpace(status))
        {
            Console.Error.WriteLine("manual-validation record-hardware-evidence requires --folder <evidence-folder> --lane multi-monitor-capture|multi-monitor-recording|long-recording|android-safe-device-proof --status passed|failed|blocked|pending.");
            return 2;
        }

        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        DateTimeOffset parsedObservedAt = default;
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            !DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedObservedAt))
        {
            Console.Error.WriteLine("manual-validation record-hardware-evidence --observed-at must be a valid date/time.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(observedAtText))
        {
            observedAt = parsedObservedAt;
        }

        var result = await new ManualValidationHardwareEvidenceRecordService().RecordAsync(new ManualValidationHardwareEvidenceRecordRequest
        {
            RootPath = root,
            Lane = lane,
            Status = status,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            OperatorName = Value(args, "--operator") ?? Value(args, "--operator-name"),
            Note = Value(args, "--note"),
            ObservedAt = observedAt,
            Evidence = HardwareEvidenceInputs(args)
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"output={result.OutputPath}");
            Console.WriteLine($"lane={result.LaneId}");
            Console.WriteLine($"title={result.LaneTitle}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"proof-complete={result.ProofComplete}");
            Console.WriteLine($"evidence={result.Evidence.Count}");
            Console.WriteLine($"would-capture-desktop={result.WouldCaptureDesktop}");
            Console.WriteLine($"would-record-desktop={result.WouldRecordDesktop}");
            Console.WriteLine($"would-contact-android-device={result.WouldContactAndroidDevice}");
            Console.WriteLine($"would-import-phone-media={result.WouldImportPhoneMedia}");
            Console.WriteLine($"would-change-device-settings={result.WouldChangeDeviceSettings}");
            Console.WriteLine($"would-update-manual-lane={result.WouldUpdateManualLane}");
            Console.WriteLine($"would-certify-hardware={result.WouldCertifyHardware}");
            Console.WriteLine($"would-mutate-user-profile={result.WouldMutateUserProfile}");
            if (result.MissingRequiredCategories.Count > 0)
            {
                Console.WriteLine($"missing-required={string.Join(",", result.MissingRequiredCategories)}");
            }

            if (result.MissingRecommendedCategories.Count > 0)
            {
                Console.WriteLine($"missing-recommended={string.Join(",", result.MissingRecommendedCategories)}");
            }

            foreach (var issue in result.Issues)
            {
                Console.WriteLine($"ERROR: {issue}");
            }

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"WARN: {warning}");
            }

            foreach (var file in result.GeneratedFiles)
            {
                Console.WriteLine($"artifact={file}");
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static List<ManualValidationHardwareEvidenceInput> HardwareEvidenceInputs(IReadOnlyList<string> args)
    {
        var evidence = new List<ManualValidationHardwareEvidenceInput>();
        foreach (var value in Values(args, "--evidence").Concat(Values(args, "--evidence-path")))
        {
            var parsed = SplitEvidenceCategory(value);
            AddHardwareEvidence(evidence, parsed.Category, parsed.Value);
        }

        AddHardwareEvidence(evidence, "notes", Values(args, "--notes-evidence"));
        AddHardwareEvidence(evidence, "notes", Values(args, "--note-evidence"));
        AddHardwareEvidence(evidence, "notes", Values(args, "--observations-evidence"));
        AddHardwareEvidence(evidence, "safe-content", Values(args, "--safe-content-evidence"));
        AddHardwareEvidence(evidence, "safe-content", Values(args, "--content-evidence"));
        AddHardwareEvidence(evidence, "topology", Values(args, "--topology-evidence"));
        AddHardwareEvidence(evidence, "topology", Values(args, "--display-evidence"));
        AddHardwareEvidence(evidence, "capture-output", Values(args, "--capture-output-evidence"));
        AddHardwareEvidence(evidence, "capture-output", Values(args, "--capture-evidence"));
        AddHardwareEvidence(evidence, "dimensions", Values(args, "--dimensions-evidence"));
        AddHardwareEvidence(evidence, "privacy", Values(args, "--privacy-evidence"));
        AddHardwareEvidence(evidence, "privacy", Values(args, "--redaction-evidence"));
        AddHardwareEvidence(evidence, "wgc-diagnostics", Values(args, "--wgc-diagnostics-evidence"));
        AddHardwareEvidence(evidence, "wgc-diagnostics", Values(args, "--capture-diagnostics-evidence"));
        AddHardwareEvidence(evidence, "failure-media", Values(args, "--failure-media-evidence"));
        AddHardwareEvidence(evidence, "recording-output", Values(args, "--recording-output-evidence"));
        AddHardwareEvidence(evidence, "recording-output", Values(args, "--recording-evidence"));
        AddHardwareEvidence(evidence, "playback", Values(args, "--playback-evidence"));
        AddHardwareEvidence(evidence, "audio-sync", Values(args, "--audio-sync-evidence"));
        AddHardwareEvidence(evidence, "encoder-diagnostics", Values(args, "--encoder-diagnostics-evidence"));
        AddHardwareEvidence(evidence, "duration", Values(args, "--duration-evidence"));
        AddHardwareEvidence(evidence, "sync", Values(args, "--sync-evidence"));
        AddHardwareEvidence(evidence, "recovery", Values(args, "--recovery-evidence"));
        AddHardwareEvidence(evidence, "devices", Values(args, "--devices-evidence"));
        AddHardwareEvidence(evidence, "ffprobe", Values(args, "--ffprobe-evidence"));
        AddHardwareEvidence(evidence, "device", Values(args, "--device-evidence"));
        AddHardwareEvidence(evidence, "device", Values(args, "--android-device-evidence"));
        AddHardwareEvidence(evidence, "screenshot-or-video", Values(args, "--screenshot-or-video-evidence"));
        AddHardwareEvidence(evidence, "screenshot-or-video", Values(args, "--screenshot-evidence"));
        AddHardwareEvidence(evidence, "screenshot-or-video", Values(args, "--video-evidence"));
        AddHardwareEvidence(evidence, "import-result", Values(args, "--import-result-evidence"));
        AddHardwareEvidence(evidence, "cleanup", Values(args, "--cleanup-evidence"));
        AddHardwareEvidence(evidence, "preview", Values(args, "--preview-evidence"));
        AddHardwareEvidence(evidence, "adb-diagnostics", Values(args, "--adb-diagnostics-evidence"));
        AddHardwareEvidence(evidence, "adb-diagnostics", Values(args, "--android-diagnostics-evidence"));
        return evidence;
    }

    private static void AddHardwareEvidence(
        List<ManualValidationHardwareEvidenceInput> evidence,
        string category,
        IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddHardwareEvidence(evidence, category, value);
        }
    }

    private static void AddHardwareEvidence(
        List<ManualValidationHardwareEvidenceInput> evidence,
        string category,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        evidence.Add(new ManualValidationHardwareEvidenceInput
        {
            Category = category,
            Value = value
        });
    }

    private static List<ManualValidationCleanMachineEvidenceInput> CleanMachineEvidenceInputs(IReadOnlyList<string> args)
    {
        var evidence = new List<ManualValidationCleanMachineEvidenceInput>();
        foreach (var value in Values(args, "--evidence").Concat(Values(args, "--evidence-path")))
        {
            var parsed = SplitEvidenceCategory(value);
            AddCleanMachineEvidence(evidence, parsed.Category, parsed.Value);
        }

        AddCleanMachineEvidence(evidence, "machine", Values(args, "--machine-evidence"));
        AddCleanMachineEvidence(evidence, "machine", Values(args, "--machine-info-evidence"));
        AddCleanMachineEvidence(evidence, "machine", Values(args, "--environment-evidence"));
        AddCleanMachineEvidence(evidence, "package", Values(args, "--package-evidence"));
        AddCleanMachineEvidence(evidence, "package", Values(args, "--portable-evidence"));
        AddCleanMachineEvidence(evidence, "package", Values(args, "--portable-zip-evidence"));
        AddCleanMachineEvidence(evidence, "package", Values(args, "--manifest-evidence"));
        AddCleanMachineEvidence(evidence, "hash", Values(args, "--hash-evidence"));
        AddCleanMachineEvidence(evidence, "hash", Values(args, "--portable-hash-evidence"));
        AddCleanMachineEvidence(evidence, "hash", Values(args, "--checksum-evidence"));
        AddCleanMachineEvidence(evidence, "diagnostics", Values(args, "--diagnostics-evidence"));
        AddCleanMachineEvidence(evidence, "diagnostics", Values(args, "--diagnostics-print-evidence"));
        AddCleanMachineEvidence(evidence, "paths", Values(args, "--paths-evidence"));
        AddCleanMachineEvidence(evidence, "paths", Values(args, "--isolated-paths-evidence"));
        AddCleanMachineEvidence(evidence, "first-launch", Values(args, "--first-launch-evidence"));
        AddCleanMachineEvidence(evidence, "first-launch", Values(args, "--main-window-evidence"));
        AddCleanMachineEvidence(evidence, "first-launch", Values(args, "--render-evidence"));
        AddCleanMachineEvidence(evidence, "settings", Values(args, "--settings-evidence"));
        AddCleanMachineEvidence(evidence, "settings", Values(args, "--settings-save-evidence"));
        AddCleanMachineEvidence(evidence, "capture-export", Values(args, "--capture-export-evidence"));
        AddCleanMachineEvidence(evidence, "capture-export", Values(args, "--capture-loop-evidence"));
        AddCleanMachineEvidence(evidence, "capture-export", Values(args, "--import-export-evidence"));
        AddCleanMachineEvidence(evidence, "installer", Values(args, "--installer-evidence"));
        AddCleanMachineEvidence(evidence, "installer", Values(args, "--installer-skipped-evidence"));
        AddCleanMachineEvidence(evidence, "installer", Values(args, "--installer-log-evidence"));
        AddCleanMachineEvidence(evidence, "installer", Values(args, "--install-uninstall-evidence"));
        AddCleanMachineEvidence(evidence, "privacy", Values(args, "--privacy-evidence"));
        AddCleanMachineEvidence(evidence, "privacy", Values(args, "--redaction-evidence"));
        AddCleanMachineEvidence(evidence, "privacy", Values(args, "--review-evidence"));
        AddCleanMachineEvidence(evidence, "script-result", Values(args, "--script-result-evidence"));
        AddCleanMachineEvidence(evidence, "script-result", Values(args, "--helper-result-evidence"));
        return evidence;
    }

    private static void AddCleanMachineEvidence(
        List<ManualValidationCleanMachineEvidenceInput> evidence,
        string category,
        IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddCleanMachineEvidence(evidence, category, value);
        }
    }

    private static void AddCleanMachineEvidence(
        List<ManualValidationCleanMachineEvidenceInput> evidence,
        string category,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        evidence.Add(new ManualValidationCleanMachineEvidenceInput
        {
            Category = category,
            Value = value
        });
    }

    private static async Task<int> ManualValidationRecordLaneAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--lane",
            "--status",
            "--note",
            "--operator",
            "--observed-at",
            "--evidence",
            "--evidence-path");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        var lane = Value(args, "--lane") ??
            Value(args, "--id") ??
            positional.ElementAtOrDefault(1);
        var status = Value(args, "--status") ??
            Value(args, "--result") ??
            positional.ElementAtOrDefault(2);
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(lane) ||
            string.IsNullOrWhiteSpace(status))
        {
            Console.Error.WriteLine("manual-validation record-lane requires --folder <evidence-folder> --lane <lane> --status passed|failed|blocked|pending|not-applicable.");
            return 2;
        }

        DateTimeOffset? observedAt = null;
        var observedAtText = Value(args, "--observed-at");
        DateTimeOffset parsedObservedAt = default;
        if (!string.IsNullOrWhiteSpace(observedAtText) &&
            !DateTimeOffset.TryParse(observedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsedObservedAt))
        {
            Console.Error.WriteLine("manual-validation record-lane --observed-at must be a valid date/time.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(observedAtText))
        {
            observedAt = parsedObservedAt;
        }

        var evidence = Values(args, "--evidence")
            .Concat(Values(args, "--evidence-path"))
            .ToList();
        var result = await new ManualValidationLaneUpdateService().UpdateAsync(new ManualValidationLaneUpdateRequest
        {
            RootPath = root,
            Lane = lane,
            Status = status,
            Note = Value(args, "--note"),
            OperatorName = Value(args, "--operator"),
            ObservedAt = observedAt,
            EvidencePaths = evidence
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"lane={result.LaneId}");
            Console.WriteLine($"title={result.LaneTitle}");
            Console.WriteLine($"file={result.LanePath}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"evidence={result.Evidence.Count}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  {warning}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationOperatorPackAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--output",
            "--cli-path",
            "--app-path");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.ElementAtOrDefault(0);
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation operator-pack requires --folder <evidence-folder>.");
            return 2;
        }

        var result = await new ManualValidationOperatorPackService().CreateAsync(new ManualValidationOperatorPackRequest
        {
            RootPath = root,
            OutputPath = Value(args, "--output") ?? Value(args, "-o"),
            CliPath = Value(args, "--cli-path"),
            AppPath = Value(args, "--app-path")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"output={result.OutputRoot}");
            Console.WriteLine($"checklist={result.ChecklistPath}");
            Console.WriteLine($"commands={result.CommandReferencePath}");
            Console.WriteLine($"manifest={result.ManifestPath}");
            Console.WriteLine($"required-open={result.RequiredOpenCount}");
            Console.WriteLine($"operator-lanes={result.Lanes.Count}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  {warning}");
                }
            }

            if (result.Issues.Count > 0)
            {
                Console.WriteLine("issues:");
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"  {issue}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationDesktopProofAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--repo-root",
            "--app-path",
            "--timeout-seconds",
            "--timeout");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation desktop-proof requires --folder <evidence-folder>.");
            return 2;
        }

        var timeoutSeconds = ParseInt(args, "--timeout-seconds", ParseInt(args, "--timeout", 120));
        var result = await new ManualValidationDesktopProofService().CollectAsync(new ManualValidationDesktopProofRequest
        {
            RootPath = root,
            RunCommands = Has(args, "--run-commands") ||
                          Has(args, "--collect") ||
                          Has(args, "--execute"),
            RepoRoot = Value(args, "--repo-root"),
            AppPath = Value(args, "--app-path"),
            TimeoutSeconds = timeoutSeconds
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"proof={result.ProofDirectory}");
            Console.WriteLine($"command-results={result.CommandResultsPath}");
            Console.WriteLine($"environment={result.EnvironmentReportPath}");
            Console.WriteLine($"summary={result.SummaryMarkdownPath}");
            Console.WriteLine($"summary-json={result.SummaryJsonPath}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"updated-lanes={result.UpdatedLaneFiles.Count}");
            Console.WriteLine($"evidence-files={result.EvidenceFiles.Count}");
            if (result.FailedCommands.Count > 0)
            {
                Console.WriteLine("failed-commands:");
                foreach (var command in result.FailedCommands)
                {
                    Console.WriteLine($"  {command}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationHardwareProofAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--repo-root",
            "--cli-path",
            "--timeout-seconds",
            "--timeout");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation hardware-proof requires --folder <evidence-folder>.");
            return 2;
        }

        var timeoutSeconds = ParseInt(args, "--timeout-seconds", ParseInt(args, "--timeout", 120));
        var result = await new ManualValidationHardwareProofService().CollectAsync(new ManualValidationHardwareProofRequest
        {
            RootPath = root,
            RunCommands = Has(args, "--run-commands") ||
                          Has(args, "--collect") ||
                          Has(args, "--execute"),
            RepoRoot = Value(args, "--repo-root"),
            CliPath = Value(args, "--cli-path"),
            TimeoutSeconds = timeoutSeconds
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"proof={result.ProofDirectory}");
            Console.WriteLine($"command-results={result.CommandResultsPath}");
            Console.WriteLine($"environment={result.EnvironmentReportPath}");
            Console.WriteLine($"summary={result.SummaryMarkdownPath}");
            Console.WriteLine($"summary-json={result.SummaryJsonPath}");
            Console.WriteLine($"status={result.Status}");
            Console.WriteLine($"updated-lanes={result.UpdatedLaneFiles.Count}");
            Console.WriteLine($"evidence-files={result.EvidenceFiles.Count}");
            if (result.FailedCommands.Count > 0)
            {
                Console.WriteLine("nonzero-diagnostics:");
                foreach (var command in result.FailedCommands)
                {
                    Console.WriteLine($"  {command}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationBaselineAsync(string[] args)
    {
        var positional = PositionalValues(
            args,
            "--folder",
            "--root",
            "--input",
            "--repo-root",
            "--cli-path",
            "--timeout-seconds",
            "--timeout");
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            positional.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation baseline requires --folder <evidence-folder>.");
            return 2;
        }

        var timeoutSeconds = ParseInt(args, "--timeout-seconds", ParseInt(args, "--timeout", 300));
        var result = await new ManualValidationBaselineService().CompleteAsync(new ManualValidationBaselineRequest
        {
            RootPath = root,
            RunCommands = Has(args, "--run-commands") ||
                          Has(args, "--collect") ||
                          Has(args, "--execute"),
            RepoRoot = Value(args, "--repo-root"),
            CliPath = Value(args, "--cli-path"),
            TimeoutSeconds = timeoutSeconds
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"baseline={result.BaselinePath}");
            Console.WriteLine($"diagnostics={result.DiagnosticsDirectory}");
            Console.WriteLine($"command-results={result.CommandResultsPath}");
            Console.WriteLine($"status={result.Status}");
            if (result.FailedCommands.Count > 0)
            {
                Console.WriteLine("failed-commands:");
                foreach (var command in result.FailedCommands)
                {
                    Console.WriteLine($"  {command}");
                }
            }

            if (result.MissingRequiredEvidence.Count > 0)
            {
                Console.WriteLine("missing-evidence:");
                foreach (var item in result.MissingRequiredEvidence)
                {
                    Console.WriteLine($"  {item}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationProofPlanAsync(string[] args)
    {
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation proof-plan requires --folder <evidence-folder>.");
            return 2;
        }

        var result = await new ManualValidationProofPlanService().CreateAsync(new ManualValidationProofPlanRequest
        {
            RootPath = root,
            OutputPath = Value(args, "--output") ?? Value(args, "-o")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"plan-markdown={result.PlanMarkdownPath}");
            Console.WriteLine($"plan-json={result.PlanJsonPath}");
            Console.WriteLine($"summary-complete={result.SummarySucceeded}");
            Console.WriteLine($"required-open={result.RequiredOpenCount}");
            Console.WriteLine($"hardware-gated-open={result.HardwareGatedOpenCount}");
            Console.WriteLine($"optional-compatibility-open={result.OptionalCompatibilityOpenCount}");
            Console.WriteLine($"parked={result.ParkedCount}");
            Console.WriteLine($"redaction={result.RedactionStatus}");
            if (result.Issues.Count > 0)
            {
                Console.WriteLine("summary-issues:");
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"  {issue}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("summary-warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  {warning}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationFindingsAsync(string[] args)
    {
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation findings requires --folder <evidence-folder>.");
            return 2;
        }

        var result = await new ManualValidationFindingsService().CreateAsync(new ManualValidationFindingsRequest
        {
            RootPath = root,
            OutputPath = Value(args, "--output") ?? Value(args, "-o")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"findings-markdown={result.FindingsMarkdownPath}");
            Console.WriteLine($"findings-json={result.FindingsJsonPath}");
            Console.WriteLine($"summary-complete={result.SummarySucceeded}");
            Console.WriteLine($"required-blocking={result.RequiredBlockingCount}");
            Console.WriteLine($"hardware-gated={result.HardwareGatedOpenCount}");
            Console.WriteLine($"optional-compatibility={result.OptionalCompatibilityOpenCount}");
            Console.WriteLine($"parked={result.ParkedCount}");
            Console.WriteLine($"redaction-findings={result.RedactionFindingCount}");
            Console.WriteLine($"redaction={result.RedactionStatus}");
            if (result.Findings.Count > 0)
            {
                Console.WriteLine("findings:");
                foreach (var finding in result.Findings)
                {
                    Console.WriteLine($"  {finding.Severity} {finding.Category}: {finding.Title} ({finding.Status})");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ManualValidationSummaryAsync(string[] args)
    {
        var root = Value(args, "--folder") ??
            Value(args, "--root") ??
            Value(args, "--input") ??
            Value(args, "--output") ??
            Value(args, "-o") ??
            args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("manual-validation summarize requires --folder <evidence-folder>.");
            return 2;
        }

        var result = await new ManualValidationSummaryService().SummarizeAsync(new ManualValidationSummaryRequest
        {
            RootPath = root
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonOptions()));
        }
        else
        {
            Console.WriteLine(result.Message);
            Console.WriteLine($"root={result.RootPath}");
            Console.WriteLine($"summary-json={result.SummaryJsonPath}");
            Console.WriteLine($"summary-markdown={result.SummaryMarkdownPath}");
            Console.WriteLine($"diagnostics-bundle={(result.Diagnostics.DiagnosticsBundleExists ? result.Diagnostics.DiagnosticsBundlePath : "missing")}");
            Console.WriteLine($"redaction={result.Redaction.Status}");
            Console.WriteLine("lanes:");
            foreach (var lane in result.Lanes)
            {
                Console.WriteLine($"  {lane.Id}={lane.Status} file={lane.RelativePath}");
            }

            if (result.Issues.Count > 0)
            {
                Console.WriteLine("issues:");
                foreach (var issue in result.Issues)
                {
                    Console.WriteLine($"  {issue}");
                }
            }

            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  {warning}");
                }
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> BarcodeAsync(AppServices services, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("qr requires: decode <image> or generate <text> --output <png>.");
            return 2;
        }

        return args[0].ToLowerInvariant() switch
        {
            "decode" => await DecodeBarcodeAsync(services, args.Skip(1).ToArray()),
            "generate" => await GenerateQrAsync(services, args.Skip(1).ToArray()),
            _ => BarcodeUsage(args[0])
        };
    }

    private static async Task<int> BugReportAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("bug-report requires a workspace id, file name, or file path.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Capture not found: {target}");
            return 2;
        }

        var result = await services.BugReports.ExportAsync(
            item,
            Value(args, "--output") ?? Value(args, "-o"),
            Value(args, "--format"),
            BuildBugReportEnrichment(services, item, args));

        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.Path))
        {
            Console.WriteLine(result.Path);
        }

        if (result.Succeeded && Has(args, "--copy") && !string.IsNullOrWhiteSpace(result.Path))
        {
            ClipboardInterop.SetFileDropList([result.Path]);
            Console.Error.WriteLine("copied=true");
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> DocumentationPacketAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("doc-packet requires a workspace id, file name, or file path.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Capture or recording not found: {target}");
            return 2;
        }

        var result = await services.DocumentationPackets.CreateAsync(new DocumentationPacketRequest
        {
            Item = item,
            OutputDirectory = Value(args, "--output") ?? Value(args, "-o"),
            TranscriptPath = Value(args, "--transcript"),
            SrtPath = Value(args, "--srt"),
            VideoSummaryPath = Value(args, "--video-summary") ?? Value(args, "--summary"),
            BugReportPath = Value(args, "--bug-report"),
            BugReportFormat = Value(args, "--bug-report-format") ?? Value(args, "--format") ?? "markdown",
            GenerateBugReport = Has(args, "--generate-bug-report") || Has(args, "--bug-report-generate"),
            KeyframePaths = Values(args, "--keyframe").Concat(Values(args, "--keyframes")).ToList(),
            ContextNotes = Value(args, "--context") ?? Value(args, "--notes")
        });

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        }
        else
        {
            Console.WriteLine(result.Message);
            if (!string.IsNullOrWhiteSpace(result.PacketDirectory))
            {
                Console.WriteLine(result.PacketDirectory);
            }

            if (!string.IsNullOrWhiteSpace(result.ManifestPath))
            {
                Console.WriteLine(result.ManifestPath);
            }

            if (!string.IsNullOrWhiteSpace(result.IndexPath))
            {
                Console.WriteLine(result.IndexPath);
            }

            if (!string.IsNullOrWhiteSpace(result.BugReportPath))
            {
                Console.WriteLine(result.BugReportPath);
            }
        }

        return result.Succeeded ? 0 : 1;
    }

    private static BugReportEnrichment? BuildBugReportEnrichment(
        AppServices services,
        CaptureItem item,
        IReadOnlyList<string> args)
    {
        var transcriptPath = Value(args, "--transcript");
        var srtPath = Value(args, "--srt");
        var videoSummaryPath = Value(args, "--video-summary") ?? Value(args, "--summary");
        var keyframes = Values(args, "--keyframe").Concat(Values(args, "--keyframes")).ToList();
        var context = Value(args, "--context") ?? Value(args, "--notes");
        var history = services.AiHistory.Load(500)
            .Where(entry =>
                entry.CaptureItemId.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                entry.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) ||
                entry.FileName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToList();

        if (string.IsNullOrWhiteSpace(transcriptPath) &&
            string.IsNullOrWhiteSpace(srtPath) &&
            string.IsNullOrWhiteSpace(videoSummaryPath) &&
            keyframes.Count == 0 &&
            string.IsNullOrWhiteSpace(context) &&
            history.Count == 0)
        {
            return null;
        }

        return new BugReportEnrichment
        {
            TranscriptPath = ResolveOptionalCliPath(transcriptPath),
            SrtPath = ResolveOptionalCliPath(srtPath),
            VideoSummaryPath = ResolveOptionalCliPath(videoSummaryPath),
            KeyframePaths = keyframes.Select(ResolveOptionalCliPath).Where(path => path is not null).Cast<string>().ToList(),
            ContextNotes = context,
            AiHistory = history
        };
    }

    private static async Task<int> TranscribeAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("transcribe requires a video file path.");
            return 2;
        }

        var useAiProvider = Has(args, "--ai") ||
            string.Equals(Value(args, "--provider"), "gemini", StringComparison.OrdinalIgnoreCase);
        if (useAiProvider)
        {
            services.Settings.AiEnabled = true;
        }

        var result = await services.Transcription.TranscribeAsync(
            new TranscriptionRequest(
                target,
                Value(args, "--srt"),
                Value(args, "--output") ?? Value(args, "-o"),
                Value(args, "--whisper-exe") ?? Value(args, "--whisper-path"),
                Value(args, "--whisper-model") ?? Value(args, "--model"),
                Value(args, "--language") ?? Value(args, "--lang"),
                useAiProvider,
                Value(args, "--ai-model"),
                Value(args, "--prompt") ?? Value(args, "--ai-prompt")));

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(result.TranscriptPath))
            {
                Console.WriteLine(result.TranscriptPath);
            }

            Console.Error.WriteLine(result.Message);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> VideoIntelligenceAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("video-intel requires a workspace id, file name, or video file path.");
            return 2;
        }

        var item = FindWorkspaceItem(services, target) ?? BuildTransientCaptureItem(target);
        if (item is null)
        {
            Console.Error.WriteLine($"Video not found: {target}");
            return 2;
        }

        var useAiProvider = Has(args, "--ai") ||
            string.Equals(Value(args, "--provider"), "gemini", StringComparison.OrdinalIgnoreCase);
        if (useAiProvider)
        {
            services.Settings.AiEnabled = true;
        }

        var aiModel = Value(args, "--ai-model");
        var aiPrompt = Value(args, "--prompt") ?? Value(args, "--ai-prompt");
        var result = await services.VideoIntelligence.GenerateAsync(
            item,
            Value(args, "--transcript"),
            Value(args, "--srt"),
            Value(args, "--output") ?? Value(args, "-o"),
            Value(args, "--format"),
            Value(args, "--whisper-exe") ?? Value(args, "--whisper-path"),
            Value(args, "--whisper-model") ?? Value(args, "--model"),
            Value(args, "--language") ?? Value(args, "--lang"),
            useAiProvider,
            aiModel,
            aiPrompt);

        if (result.Succeeded)
        {
            await services.AiHistory.RecordAsync(
                AiActionKind.VideoSummary,
                item,
                result.ModelId,
                useAiProvider
                    ? "Generate AI-assisted title, summary, and chapters from redacted transcript/SRT."
                    : "Generate title, summary, and chapters from provided local transcript/SRT.",
                succeeded: true,
                result.Message,
                result.OutputPath,
                $"{result.Title}\n{result.Summary}");
            await services.Automation.ProcessAiActionCompletedAsync(item);
        }

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(result.OutputPath))
            {
                Console.WriteLine(result.OutputPath);
            }

            Console.Error.WriteLine(result.Message);
        }

        return result.Succeeded ? 0 : 1;
    }

    private static CaptureItem? FindWorkspaceItem(AppServices services, string target)
    {
        var expanded = Environment.ExpandEnvironmentVariables(target);
        var fullPath = File.Exists(expanded) ? Path.GetFullPath(expanded) : null;
        return services.WorkspaceStore.Load().FirstOrDefault(item =>
            item.Id.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            item.FileName.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            item.FilePath.Equals(expanded, StringComparison.OrdinalIgnoreCase) ||
            (fullPath is not null && item.FilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)));
    }

    private static CaptureItem? BuildTransientCaptureItem(string target)
    {
        var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
        if (!File.Exists(expanded))
        {
            return null;
        }

        var file = new FileInfo(expanded);
        var extension = Path.GetExtension(expanded);
        var item = new CaptureItem
        {
            Kind = extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? CaptureKind.RecordingMp4
                : CaptureKind.Imported,
            CreatedAt = file.CreationTime == DateTime.MinValue
                ? DateTimeOffset.Now
                : new DateTimeOffset(file.CreationTime),
            FilePath = expanded,
            ThumbnailPath = expanded,
            Bytes = file.Length,
            Notes = "Transient CLI item."
        };

        try
        {
            using var image = System.Drawing.Image.FromFile(expanded);
            item.Width = image.Width;
            item.Height = image.Height;
        }
        catch
        {
            item.Kind = extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? CaptureKind.RecordingMp4
                : CaptureKind.RecordingGif;
        }

        return item;
    }

    private static async Task<int> DecodeBarcodeAsync(AppServices services, string[] args)
    {
        var input = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("qr decode requires an image file path.");
            return 2;
        }

        var result = await services.Barcodes.DecodeFileAsync(input);
        var output = Value(args, "--output") ?? Value(args, "-o");

        if (Has(args, "--json"))
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, json);
                Console.WriteLine(output);
            }
            else
            {
                Console.WriteLine(json);
            }
        }
        else
        {
            Console.WriteLine(result.Message);
            foreach (var item in result.Items)
            {
                Console.WriteLine($"{item.Format}: {item.Text}");
            }
        }

        if (!result.Succeeded)
        {
            return 1;
        }

        var first = result.Items[0];
        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(first.Text);
            Console.Error.WriteLine("copied=true");
        }

        if (Has(args, "--open"))
        {
            if (!first.IsHttpUrl)
            {
                Console.Error.WriteLine("First decoded value is not an HTTP(S) URL.");
                return 2;
            }

            Process.Start(new ProcessStartInfo(first.Text) { UseShellExecute = true });
        }

        return 0;
    }

    private static async Task<int> PrintCaptureEngineDiagnosticsAsync(string[] args)
    {
        ICaptureEngine engine = (Value(args, "--engine") ?? Value(args, "--capture-engine"))?.ToLowerInvariant() switch
        {
            "dxgi" or "desktop-duplication" or "direct3d" or "d3d11" => new Direct3D11DesktopDuplicationCaptureEngine(),
            _ => new WindowsGraphicsCaptureEngine()
        };
        var health = await engine.ValidateAsync(CancellationToken.None);
        var record = CaptureEngineDiagnostics.Build(engine, health);

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return health.IsHealthy ? 0 : 1;
        }

        Console.WriteLine("GoatShot capture engine diagnostics");
        Console.WriteLine($"Production engine: {record.EngineName}");
        Console.WriteLine($"Scope: {record.Scope}");
        Console.WriteLine($"Desktop session: {record.DesktopSessionStatus}");
        Console.WriteLine($"Supported kinds: {string.Join(", ", record.SupportedCaptureKinds)}");
        Console.WriteLine($"Unsupported kinds: {string.Join(", ", record.UnsupportedCaptureKinds)}");
        Console.WriteLine($"Healthy: {(record.IsHealthy ? "yes" : "no")}");
        Console.WriteLine(record.Message);
        Console.WriteLine(record.InteractiveRegionStatus);
        Console.WriteLine(record.FallbackStatus);
        Console.WriteLine(record.DefaultCapturePath);
        return health.IsHealthy ? 0 : 1;
    }

    private static async Task<int> GenerateQrAsync(AppServices services, string[] args)
    {
        var text = Value(args, "--text") ?? args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("qr generate requires text and --output.");
            return 2;
        }

        var path = await services.Barcodes.GenerateQrCodeAsync(text, output);
        Console.WriteLine(path);
        return 0;
    }

    private static int BarcodeUsage(string command)
    {
        Console.Error.WriteLine($"Unknown qr command: {command}");
        Console.Error.WriteLine("qr requires: decode <image> [--json] [--copy] [--open] or generate <text> --output <png>.");
        return 2;
    }

    private static async Task<int> ScanTextAsync(string[] args)
    {
        var input = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("scan-text requires a text file path.");
            return 2;
        }

        input = Path.GetFullPath(Environment.ExpandEnvironmentVariables(input));
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"File not found: {input}");
            return 2;
        }

        var text = await File.ReadAllTextAsync(input);
        var scan = SensitiveTextDetector.Scan(text);
        var output = Value(args, "--output") ?? Value(args, "-o");

        if (Has(args, "--json"))
        {
            var json = JsonSerializer.Serialize(scan, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, json);
                Console.WriteLine(output);
            }
            else
            {
                Console.WriteLine(json);
            }

            return scan.Findings.Count == 0 ? 0 : 1;
        }

        Console.WriteLine(scan.Summary);
        foreach (var finding in scan.Findings)
        {
            Console.WriteLine($"{finding.Kind}: {finding.Preview} @ {finding.StartIndex}");
        }

        if (Has(args, "--redact"))
        {
            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, scan.RedactedText);
                Console.Error.WriteLine($"redacted-output={output}");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(scan.RedactedText);
            }
        }

        return scan.Findings.Count == 0 ? 0 : 1;
    }

    private static async Task<int> OcrAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("ocr requires a workspace id, file name, or image file path.");
            return 2;
        }

        var isWorkspaceItem = true;
        var item = FindWorkspaceItem(services, target);
        if (item is null)
        {
            isWorkspaceItem = false;
            item = BuildTransientCaptureItem(target);
        }

        if (item is null)
        {
            Console.Error.WriteLine($"Image not found: {target}");
            return 2;
        }

        var result = await services.Ocr.RecognizeFileAsync(
            item.FilePath,
            Value(args, "--language") ?? Value(args, "--lang"));

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        item.OcrText = result.Text;
        item.OcrLanguageTag = result.LanguageTag;
        item.OcrRecognizedAt = DateTimeOffset.Now;
        item.OcrWords = result.Words.ToList();

        var scan = SensitiveTextDetector.Scan(result.Text);
        item.Notes = MergeSensitiveScanNote(item.Notes, $"Sensitive scan: {scan.Summary}");

        if (isWorkspaceItem)
        {
            await services.WorkspaceStore.UpdateItemAsync(item);
        }
        else if (Has(args, "--workspace"))
        {
            var saved = await services.WorkspaceStore.AddImageFileAsync(
                item.FilePath,
                item.Kind,
                "Imported from goatshot CLI OCR command.");
            saved.OcrText = item.OcrText;
            saved.OcrLanguageTag = item.OcrLanguageTag;
            saved.OcrRecognizedAt = item.OcrRecognizedAt;
            saved.OcrWords = item.OcrWords.ToList();
            saved.Notes = item.Notes;
            await services.WorkspaceStore.UpdateItemAsync(saved);
            item = saved;
            Console.Error.WriteLine($"workspace-id={item.Id}");
        }

        await services.Automation.ProcessOcrCompletedAsync(item);

        var shouldRedact = Has(args, "--redact");
        var textOutput = shouldRedact ? scan.RedactedText : result.Text;
        var output = Value(args, "--output") ?? Value(args, "-o");

        if (Has(args, "--json"))
        {
            var payload = new
            {
                result.Succeeded,
                Text = textOutput,
                Redacted = shouldRedact,
                result.LanguageTag,
                result.Message,
                result.LineCount,
                result.Words,
                SensitiveSummary = scan.Summary,
                WorkspaceId = isWorkspaceItem || Has(args, "--workspace") ? item.Id : null
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, json);
                Console.WriteLine(output);
            }
            else
            {
                Console.WriteLine(json);
            }
        }
        else if (!string.IsNullOrWhiteSpace(output))
        {
            output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, textOutput);
            Console.WriteLine(output);
        }
        else
        {
            Console.WriteLine(textOutput);
        }

        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(textOutput);
            Console.Error.WriteLine("copied=true");
        }

        Console.Error.WriteLine(result.Message);
        if (scan.Findings.Count > 0)
        {
            Console.Error.WriteLine(scan.Summary);
        }

        return 0;
    }

    private static async Task<int> RedactOcrAsync(AppServices services, string[] args)
    {
        var target = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("redact-ocr requires a workspace id, file name, or image file path.");
            return 2;
        }

        var isWorkspaceItem = true;
        var item = FindWorkspaceItem(services, target);
        if (item is null)
        {
            isWorkspaceItem = false;
            item = BuildTransientCaptureItem(target);
        }

        if (item is null)
        {
            Console.Error.WriteLine($"Image not found: {target}");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(item.OcrText) || item.OcrWords.Count == 0 || item.OcrWords.Any(word => word.Length == 0) || Has(args, "--refresh-ocr"))
        {
            var ocr = await services.Ocr.RecognizeFileAsync(
                item.FilePath,
                Value(args, "--language") ?? Value(args, "--lang"));

            if (!ocr.Succeeded)
            {
                Console.Error.WriteLine(ocr.Message);
                return 1;
            }

            item.OcrText = ocr.Text;
            item.OcrLanguageTag = ocr.LanguageTag;
            item.OcrRecognizedAt = DateTimeOffset.Now;
            item.OcrWords = ocr.Words.ToList();
            var scan = SensitiveTextDetector.Scan(ocr.Text);
            item.Notes = MergeSensitiveScanNote(item.Notes, $"Sensitive scan: {scan.Summary}");
            if (isWorkspaceItem)
            {
                await services.WorkspaceStore.UpdateItemAsync(item);
            }

            await services.Automation.ProcessOcrCompletedAsync(item);
            Console.Error.WriteLine(ocr.Message);
        }

        var output = Value(args, "--output") ?? Value(args, "-o");
        var mode = ParseRedactionMode(Value(args, "--mode"));
        var result = await services.VisualRedactions.RedactSensitiveOcrAsync(
            item,
            output,
            mode,
            addToWorkspace: isWorkspaceItem || Has(args, "--workspace"));

        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        Console.WriteLine(result.OutputPath);
        Console.Error.WriteLine(result.Message);
        if (result.Item is not null)
        {
            Console.Error.WriteLine($"workspace-id={result.Item.Id}");
            await services.Automation.ProcessCaptureEditedAsync(result.Item);
        }

        if (Has(args, "--copy") && !string.IsNullOrWhiteSpace(result.OutputPath))
        {
            ClipboardInterop.SetFileDropList([result.OutputPath]);
            Console.Error.WriteLine("copied=true");
        }

        return 0;
    }

    private static async Task<int> AiModelsAsync(AppServices services, string[] args)
    {
        var models = await services.Gemini.ListModelsAsync(CancellationToken.None);
        var capabilities = models.Select(model => new AiModelCapability
        {
            ProviderName = services.Gemini.ProviderName,
            ModelId = model.Id,
            DisplayName = model.DisplayName,
            SupportsImageEdit = model.SupportsImageEdit,
            SupportsImageAnalysis = model.SupportsImageAnalysis,
            SupportsImageOutput = model.SupportsImageEdit,
            SupportsVideoInput = false,
            SupportsTextOutput = true,
            Source = model.Source,
            Notes = model.SupportsImageEdit
                ? "Usable for GoatShot image edit/analyze flows when Gemini credentials are configured."
                : "Listed by Gemini but image-edit capability was not advertised."
        }).ToList();

        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(capabilities, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        foreach (var model in capabilities)
        {
            Console.WriteLine($"{model.ModelId} - {model.DisplayName}");
            Console.WriteLine($"  image-edit={model.SupportsImageEdit}; image-analysis={model.SupportsImageAnalysis}; video-input={model.SupportsVideoInput}; source={model.Source}");
            Console.WriteLine($"  {model.Notes}");
        }

        return 0;
    }

    private static async Task<int> AiEditAsync(AppServices services, string[] args)
    {
        var file = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        var prompt = Value(args, "--prompt");
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine("ai-edit requires <file> and --prompt.");
            return 2;
        }

        file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(file));
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return 2;
        }

        services.Settings.AiEnabled = true;
        var model = Value(args, "--model") ?? services.Settings.GeminiDefaultModelId;
        var historyItem = BuildTransientCaptureItem(file) ?? new CaptureItem
        {
            Kind = CaptureKind.Imported,
            CreatedAt = DateTimeOffset.Now,
            FilePath = file,
            ThumbnailPath = file,
            Bytes = new FileInfo(file).Length,
            Notes = "Transient CLI AI edit item."
        };
        var result = await services.Gemini.EditImageAsync(
            new AiImageEditRequest(file, prompt, model, RequiresUserConfirmation: false),
            CancellationToken.None);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.OutputImagePath))
        {
            await services.AiHistory.RecordAsync(
                AiActionKind.ImageEdit,
                historyItem,
                model,
                prompt,
                succeeded: false,
                result.Message);
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        await services.AiHistory.RecordAsync(
            AiActionKind.ImageEdit,
            historyItem,
            model,
            prompt,
            succeeded: true,
            result.Message,
            result.OutputImagePath);

        var output = Value(args, "--output") ?? Value(args, "-o");
        if (!string.IsNullOrWhiteSpace(output))
        {
            output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(result.OutputImagePath, output, overwrite: true);
            Console.WriteLine(output);
        }
        else
        {
            Console.WriteLine(result.OutputImagePath);
        }

        var automationItem = historyItem;
        if (Has(args, "--workspace"))
        {
            var workspacePath = string.IsNullOrWhiteSpace(output) ? result.OutputImagePath : output;
            var item = await services.WorkspaceStore.AddImageFileAsync(
                workspacePath,
                CaptureKind.EditedImage,
                $"Gemini CLI edit from prompt: {prompt}");
            automationItem = item;
            Console.Error.WriteLine($"workspace-id={item.Id}");
        }

        await services.Automation.ProcessAiActionCompletedAsync(automationItem);
        return 0;
    }

    private static async Task<int> AiAnalyzeAsync(AppServices services, string[] args)
    {
        var file = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(file))
        {
            Console.Error.WriteLine("ai-analyze requires an image file path.");
            return 2;
        }

        file = Path.GetFullPath(Environment.ExpandEnvironmentVariables(file));
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return 2;
        }

        if (!IsImageFile(file))
        {
            Console.Error.WriteLine("ai-analyze currently supports image files only.");
            return 2;
        }

        services.Settings.AiEnabled = true;
        var prompt = Value(args, "--prompt") ?? DefaultAiAnalysisPrompt();
        var model = Value(args, "--model") ?? services.Settings.GeminiDefaultModelId;
        var historyItem = BuildTransientCaptureItem(file) ?? new CaptureItem
        {
            Kind = CaptureKind.Imported,
            CreatedAt = DateTimeOffset.Now,
            FilePath = file,
            ThumbnailPath = file,
            Bytes = new FileInfo(file).Length,
            Notes = "Transient CLI AI analysis item."
        };
        var result = await services.Gemini.AnalyzeImageAsync(file, prompt, model, CancellationToken.None);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
        {
            await services.AiHistory.RecordAsync(
                AiActionKind.ImageAnalysis,
                historyItem,
                model,
                prompt,
                succeeded: false,
                result.Message);
            Console.Error.WriteLine(result.Message);
            return 1;
        }

        var text = SensitiveTextDetector.Scan(result.Text).RedactedText.Trim();
        await services.AiHistory.RecordAsync(
            AiActionKind.ImageAnalysis,
            historyItem,
            model,
            prompt,
            succeeded: true,
            result.Message,
            textOutput: text);
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (!string.IsNullOrWhiteSpace(output))
        {
            output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, text);
            Console.WriteLine(output);
        }
        else
        {
            Console.WriteLine(text);
        }

        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(text);
            Console.Error.WriteLine("copied=true");
        }

        var automationItem = historyItem;
        if (Has(args, "--workspace"))
        {
            var item = FindWorkspaceItem(services, file) ??
                await services.WorkspaceStore.AddImageFileAsync(file, CaptureKind.Imported, "Imported from goatshot CLI AI analysis command.");
            item.Notes = MergePrefixedNote(
                item.Notes,
                "AI analysis:",
                $"AI analysis: {DateTimeOffset.Now.LocalDateTime:g}{Environment.NewLine}{text}");
            await services.WorkspaceStore.UpdateItemAsync(item);
            automationItem = item;
            Console.Error.WriteLine($"workspace-id={item.Id}");
        }

        await services.Automation.ProcessAiActionCompletedAsync(automationItem);
        return 0;
    }

    private static async Task<int> AiHistoryAsync(AppServices services, string[] args)
    {
        if (args.Length > 0 && args[0].Equals("prompts", StringComparison.OrdinalIgnoreCase))
        {
            return AiPromptHistory(services, args.Skip(1).ToArray());
        }

        if (args.Length > 0 && args[0].Equals("accept", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateAiHistoryReview(services, args.Skip(1).ToArray(), AiActionReviewStatus.Accepted);
        }

        if (args.Length > 0 && args[0].Equals("reject", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateAiHistoryReview(services, args.Skip(1).ToArray(), AiActionReviewStatus.Rejected);
        }

        if (args.Length > 0 && args[0].Equals("iterate", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateAiHistoryReview(services, args.Skip(1).ToArray(), AiActionReviewStatus.Iterated);
        }

        if (args.Length > 0 && args[0].Equals("retry", StringComparison.OrdinalIgnoreCase))
        {
            return await RetryAiHistoryAsync(services, args.Skip(1).ToArray());
        }

        var entries = services.AiHistory.Load(ParseInt(args, "--limit", 20));
        if (Has(args, "--json"))
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(entries, jsonOptions));
            return 0;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No AI action history yet.");
            return 1;
        }

        foreach (var entry in entries)
        {
            Console.WriteLine($"{entry.CreatedAt.LocalDateTime:g} {(entry.Succeeded ? "OK" : "Failed")} {entry.Action} {entry.FileName}");
            Console.WriteLine($"model={entry.ModelId}");
            Console.WriteLine($"review={entry.ReviewStatus}");
            Console.WriteLine(entry.Message);
            Console.WriteLine($"id={entry.Id}");
            if (!string.IsNullOrWhiteSpace(entry.OutputPath))
            {
                Console.WriteLine(entry.OutputPath);
            }

            if (!string.IsNullOrWhiteSpace(entry.TextPreview))
            {
                Console.WriteLine(entry.TextPreview);
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static async Task<int> RetryAiHistoryAsync(AppServices services, string[] args)
    {
        var id = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(id))
        {
            Console.Error.WriteLine("ai-history retry requires a history id.");
            return 2;
        }

        var entry = services.AiHistory.Find(id);
        if (entry is null)
        {
            Console.Error.WriteLine($"AI history entry not found: {id}");
            return 1;
        }

        var item = BuildHistoryCaptureItem(entry);
        if (item is null)
        {
            Console.Error.WriteLine($"AI history source file not found: {entry.FilePath}");
            return 1;
        }

        var model = Value(args, "--model") ?? Value(args, "--ai-model") ?? entry.ModelId;
        var prompt = Value(args, "--prompt") ?? entry.Prompt;
        var profile = Value(args, "--profile");
        if (!string.IsNullOrWhiteSpace(profile))
        {
            prompt = $"{prompt}{Environment.NewLine}{Environment.NewLine}Retry profile: {profile}";
        }

        await services.AiHistory.UpdateReviewStatusAsync(
            entry.Id,
            AiActionReviewStatus.Iterated,
            $"Retry requested with model '{model}'{(string.IsNullOrWhiteSpace(profile) ? string.Empty : $" and profile '{profile}'")}.");

        var retryEntry = entry.Action switch
        {
            AiActionKind.ImageAnalysis => await RetryImageAnalysisAsync(services, entry, item, model, prompt),
            AiActionKind.ImageEdit => await RetryImageEditAsync(services, entry, item, model, prompt),
            AiActionKind.VideoSummary => await RetryVideoSummaryAsync(services, entry, item, args, model, prompt),
            AiActionKind.BugReportDraft => await RetryBugReportDraftAsync(services, entry, item, args, model, prompt),
            _ => await services.AiHistory.RecordAsync(
                entry.Action,
                item,
                model,
                prompt,
                succeeded: false,
                $"Retry is not supported for AI action {entry.Action}.",
                parentEntryId: entry.Id)
        };

        if (Has(args, "--json"))
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(retryEntry, options));
        }
        else
        {
            Console.WriteLine($"{retryEntry.Id} {(retryEntry.Succeeded ? "succeeded" : "failed")} parent={entry.Id}");
            Console.WriteLine(retryEntry.Message);
            if (!string.IsNullOrWhiteSpace(retryEntry.OutputPath))
            {
                Console.WriteLine(retryEntry.OutputPath);
            }
        }

        return retryEntry.Succeeded ? 0 : 1;
    }

    private static async Task<AiActionHistoryEntry> RetryImageAnalysisAsync(
        AppServices services,
        AiActionHistoryEntry original,
        CaptureItem item,
        string model,
        string prompt)
    {
        services.Settings.AiEnabled = true;
        var result = await services.Gemini.AnalyzeImageAsync(item.FilePath, prompt, model, CancellationToken.None);
        return await services.AiHistory.RecordAsync(
            AiActionKind.ImageAnalysis,
            item,
            model,
            prompt,
            result.Succeeded && !string.IsNullOrWhiteSpace(result.Text),
            result.Message,
            textOutput: result.Text,
            parentEntryId: original.Id);
    }

    private static async Task<AiActionHistoryEntry> RetryImageEditAsync(
        AppServices services,
        AiActionHistoryEntry original,
        CaptureItem item,
        string model,
        string prompt)
    {
        services.Settings.AiEnabled = true;
        var result = await services.Gemini.EditImageAsync(
            new AiImageEditRequest(item.FilePath, prompt, model, RequiresUserConfirmation: false),
            CancellationToken.None);
        return await services.AiHistory.RecordAsync(
            AiActionKind.ImageEdit,
            item,
            model,
            prompt,
            result.Succeeded && !string.IsNullOrWhiteSpace(result.OutputImagePath),
            result.Message,
            outputPath: result.OutputImagePath,
            parentEntryId: original.Id);
    }

    private static async Task<AiActionHistoryEntry> RetryVideoSummaryAsync(
        AppServices services,
        AiActionHistoryEntry original,
        CaptureItem item,
        string[] args,
        string model,
        string prompt)
    {
        var useAiProvider = Has(args, "--ai") ||
            string.Equals(Value(args, "--provider"), "gemini", StringComparison.OrdinalIgnoreCase);
        if (useAiProvider)
        {
            services.Settings.AiEnabled = true;
        }

        var result = await services.VideoIntelligence.GenerateAsync(
            item,
            Value(args, "--transcript"),
            Value(args, "--srt"),
            Value(args, "--output") ?? Value(args, "-o"),
            Value(args, "--format"),
            Value(args, "--whisper-exe") ?? Value(args, "--whisper-path"),
            Value(args, "--whisper-model"),
            Value(args, "--language") ?? Value(args, "--lang"),
            useAiProvider,
            model,
            prompt);

        return await services.AiHistory.RecordAsync(
            AiActionKind.VideoSummary,
            item,
            result.ModelId,
            prompt,
            result.Succeeded,
            result.Message,
            outputPath: result.OutputPath,
            textOutput: $"{result.Title}{Environment.NewLine}{result.Summary}",
            parentEntryId: original.Id);
    }

    private static async Task<AiActionHistoryEntry> RetryBugReportDraftAsync(
        AppServices services,
        AiActionHistoryEntry original,
        CaptureItem item,
        string[] args,
        string model,
        string prompt)
    {
        var output = Value(args, "--output") ?? Value(args, "-o");
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.Combine(
                services.Paths.DocumentsRoot,
                $"bug-report-retry-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
        }

        var result = await services.BugReports.ExportAsync(
            item,
            output,
            Value(args, "--format") ?? "markdown",
            BuildBugReportEnrichment(services, item, args));
        return await services.AiHistory.RecordAsync(
            AiActionKind.BugReportDraft,
            item,
            model,
            prompt,
            result.Succeeded,
            result.Message,
            outputPath: result.Path,
            parentEntryId: original.Id);
    }

    private static CaptureItem? BuildHistoryCaptureItem(AiActionHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath))
        {
            return null;
        }

        var item = BuildTransientCaptureItem(entry.FilePath) ?? new CaptureItem
        {
            Id = entry.CaptureItemId,
            Kind = Path.GetExtension(entry.FilePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? CaptureKind.RecordingMp4
                : CaptureKind.Imported,
            CreatedAt = entry.CreatedAt,
            FilePath = entry.FilePath,
            ThumbnailPath = entry.FilePath,
            Bytes = new FileInfo(entry.FilePath).Length,
            Notes = "Recovered from AI history retry."
        };
        if (!string.IsNullOrWhiteSpace(entry.CaptureItemId))
        {
            item.Id = entry.CaptureItemId;
        }

        return item;
    }

    private static int AiPromptHistory(AppServices services, string[] args)
    {
        AiActionKind? action = null;
        var actionText = Value(args, "--action");
        if (!string.IsNullOrWhiteSpace(actionText))
        {
            if (!Enum.TryParse<AiActionKind>(actionText, ignoreCase: true, out var parsedAction))
            {
                Console.Error.WriteLine($"Unknown AI action: {actionText}");
                return 2;
            }

            action = parsedAction;
        }

        var prompts = services.AiHistory.LoadPromptHistory(action, ParseInt(args, "--limit", 20));
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(prompts, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            return prompts.Count == 0 ? 1 : 0;
        }

        if (prompts.Count == 0)
        {
            Console.WriteLine("No AI prompts in local history yet.");
            return 1;
        }

        foreach (var prompt in prompts)
        {
            Console.WriteLine($"{prompt.LastUsedAt.LocalDateTime:g} {prompt.Action} uses={prompt.UseCount} model={prompt.ModelId}");
            Console.WriteLine(prompt.Prompt);
            Console.WriteLine();
        }

        return 0;
    }

    private static int UpdateAiHistoryReview(
        AppServices services,
        string[] args,
        AiActionReviewStatus status)
    {
        var id = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(id))
        {
            Console.Error.WriteLine($"ai-history {status.ToString().ToLowerInvariant()} requires a history id.");
            return 2;
        }

        var entry = services.AiHistory.UpdateReviewStatusAsync(
            id,
            status,
            Value(args, "--reason") ?? Value(args, "--note")).GetAwaiter().GetResult();
        if (entry is null)
        {
            Console.Error.WriteLine($"AI history entry not found: {id}");
            return 1;
        }

        Console.WriteLine($"{entry.Id} {entry.ReviewStatus}");
        return 0;
    }

    private static int Color(AppServices services, string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("pick", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("color requires: pick [--copy]");
            return 2;
        }

        var color = services.ColorPicker.PickUnderCursor();
        if (Has(args, "--copy"))
        {
            ClipboardInterop.SetText(color.Hex);
        }

        Console.WriteLine(color.Display);
        return 0;
    }

    private static int Paths(AppServices services)
    {
        Console.WriteLine($"local={services.Paths.LocalRoot}");
        Console.WriteLine($"library={services.Paths.LibraryRoot}");
        Console.WriteLine($"images={services.Paths.ImagesRoot}");
        Console.WriteLine($"videos={services.Paths.VideosRoot}");
        Console.WriteLine($"settings={services.Paths.SettingsPath}");
        Console.WriteLine($"model-staging={services.Paths.ModelStagingRoot}");
        Console.WriteLine($"person-segmentation-model-staging={services.Paths.PersonSegmentationModelStagingRoot}");
        Console.WriteLine($"upload-queue={services.Paths.UploadQueuePath}");
        Console.WriteLine($"index={services.Paths.IndexPath}");
        Console.WriteLine($"metadata-db={services.Paths.MetadataDatabasePath}");
        Console.WriteLine($"share-history={services.Paths.ShareHistoryPath}");
        Console.WriteLine($"ai-action-history={services.Paths.AiActionHistoryPath}");
        return 0;
    }

    private static OAuthStatusRecord CreateOAuthStatus(AppServices services, OAuthProviderSettings provider)
    {
        var token = services.SecretStore.ReadOAuthToken(provider.ProviderName);
        return new OAuthStatusRecord(
            provider.ProviderName,
            !string.IsNullOrWhiteSpace(provider.ClientId),
            !string.IsNullOrWhiteSpace(provider.AuthorizationEndpoint),
            !string.IsNullOrWhiteSpace(provider.TokenEndpoint),
            provider.Scopes,
            provider.UsePkce,
            token is not null,
            !string.IsNullOrWhiteSpace(token?.RefreshToken) || provider.RefreshTokenStored,
            token?.HasUsableAccessToken(TimeSpan.FromMinutes(5)) ?? false,
            HasProviderAccessToken(services.SecretStore, provider.ProviderName));
    }

    private static OAuthProviderSettings? FindOAuthProvider(AppServices services, string[] args)
    {
        var providerName = Value(args, "--provider") ??
            PositionalValues(
                args,
                "--provider",
                "--client-id",
                "--authorization-endpoint",
                "--auth-endpoint",
                "--token-endpoint",
                "--scopes",
                "--scope",
                "--callback",
                "--redirect-uri",
                "--callback-port",
                "--state",
                "--code")
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providerName))
        {
            return null;
        }

        return services.Settings.OAuth.Providers.FirstOrDefault(
            provider => provider.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));
    }

    private static Uri OAuthCallbackUri(AppServices services, IReadOnlyList<string> args)
    {
        var callback = Value(args, "--callback") ?? Value(args, "--redirect-uri");
        if (!string.IsNullOrWhiteSpace(callback))
        {
            return new Uri(callback, UriKind.Absolute);
        }

        var port = ParseInt(args, "--callback-port", services.Settings.OAuth.LocalCallbackPort);
        port = Math.Clamp(port, 1, 65_535);
        return new Uri($"http://127.0.0.1:{port}/oauth/callback");
    }

    private static bool HasProviderAccessToken(SecretStore secretStore, string providerName)
    {
        return providerName.Trim().ToLowerInvariant() switch
        {
            "dropbox" => secretStore.HasDropboxAccessToken,
            "google drive" or "googledrive" or "gdrive" => secretStore.HasGoogleDriveAccessToken,
            "google photos" or "googlephotos" or "gphotos" or "photos" => secretStore.HasGooglePhotosAccessToken,
            "onedrive" or "one drive" or "microsoft onedrive" => secretStore.HasOneDriveAccessToken,
            "youtube" or "you tube" => secretStore.HasYouTubeAccessToken,
            "onenote" or "one note" or "microsoft onenote" => secretStore.HasOneNoteAccessToken,
            _ => secretStore.HasOAuthToken(providerName)
        };
    }

    private static string FormatOAuthProviders(AppServices services)
    {
        return services.Settings.OAuth.Providers.Count == 0
            ? "(none)"
            : string.Join(", ", services.Settings.OAuth.Providers.Select(provider => provider.ProviderName));
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 2;
    }

    private static bool Has(IEnumerable<string> args, string name)
    {
        return args.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static List<string> Values(IReadOnlyList<string> args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                values.Add(args[++i]);
            }
        }

        return values;
    }

    private static string? ResolveOptionalCliPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static List<string> PositionalValues(IReadOnlyList<string> args, params string[] optionsWithValues)
    {
        var valueOptions = optionsWithValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal) || arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (valueOptions.Contains(arg) && i < args.Count - 1)
                {
                    i++;
                }

                continue;
            }

            values.Add(arg);
        }

        return values;
    }

    private static int ParseInt(IReadOnlyList<string> args, string name, int fallback)
    {
        return int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static long ParseLong(IReadOnlyList<string> args, string name, long fallback)
    {
        return long.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static int? ParseOptionalInt(IReadOnlyList<string> args, string name)
    {
        return int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? ParseRequiredPositiveInt(IReadOnlyList<string> args, string name)
    {
        if (int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        Console.Error.WriteLine($"video crop requires {name} greater than zero.");
        return null;
    }

    private static TimeSpan ParseSeconds(IReadOnlyList<string> args, string name, double fallback)
    {
        var text = Value(args, name);
        var seconds = double.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        return TimeSpan.FromSeconds(Math.Max(0d, seconds));
    }

    private static (int? Width, int? Height) ParseOptionalVideoSize(IReadOnlyList<string> args)
    {
        var size = Value(args, "--size");
        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return (width, height);
            }
        }

        return (ParseOptionalInt(args, "--width"), ParseOptionalInt(args, "--height"));
    }

    private static ScrollingCaptureOptions ParseScrollingOptions(IReadOnlyList<string> args)
    {
        var options = ScrollingCaptureProfiles.For(
            Value(args, "--scroll-profile") ??
            Value(args, "--scroll-mode") ??
            Value(args, "--stitch-profile"));
        var explicitAxis = Value(args, "--axis") ?? Value(args, "--orientation");
        if (!string.IsNullOrWhiteSpace(explicitAxis) || Has(args, "--horizontal") || Has(args, "--vertical"))
        {
            options.Axis = ParseScrollingAxis(explicitAxis);
        }

        if (Has(args, "--horizontal"))
        {
            options.Axis = ScrollingCaptureAxis.Horizontal;
        }

        if (Has(args, "--vertical"))
        {
            options.Axis = ScrollingCaptureAxis.Vertical;
        }

        if (Value(args, "--max-frames") is not null)
        {
            options.MaxFrames = ParseInt(args, "--max-frames", options.MaxFrames);
        }

        if (Value(args, "--wheel-clicks") is not null)
        {
            options.WheelClicksPerFrame = ParseInt(args, "--wheel-clicks", options.WheelClicksPerFrame);
        }

        var settle = Value(args, "--settle-ms") ?? Value(args, "--settle-delay-ms");
        if (settle is not null)
        {
            options.SettleDelayMs = ParseInt(args, "--settle-ms", ParseInt(args, "--settle-delay-ms", options.SettleDelayMs));
        }

        var sticky = Math.Max(ParseInt(args, "--sticky-pixels", options.StickyPixels), ParseInt(args, "--sticky-header", options.StickyPixels));
        sticky = Math.Max(sticky, ParseInt(args, "--sticky-left", options.StickyPixels));
        options.StickyPixels = sticky;

        if (Value(args, "--auto-sticky-max") is not null)
        {
            options.MaximumAutoStickyPixels = ParseInt(args, "--auto-sticky-max", options.MaximumAutoStickyPixels);
        }

        if (Has(args, "--auto-sticky") || Has(args, "--detect-sticky"))
        {
            options.AutoDetectStickyRegion = true;
        }

        if (Has(args, "--no-auto-sticky"))
        {
            options.AutoDetectStickyRegion = false;
        }

        if (Value(args, "--min-overlap") is not null)
        {
            options.MinimumOverlapPixels = ParseInt(args, "--min-overlap", options.MinimumOverlapPixels);
        }

        if (Value(args, "--max-overlap") is not null)
        {
            options.MaximumOverlapPixels = ParseInt(args, "--max-overlap", options.MaximumOverlapPixels);
        }

        return options.Normalize();
    }

    private static ScrollingCaptureAxis ParseScrollingAxis(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScrollingCaptureAxis.Vertical;
        }

        return value.Trim().Equals("horizontal", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)
            ? ScrollingCaptureAxis.Horizontal
            : ScrollingCaptureAxis.Vertical;
    }

    private static RecordingSettings? ResolveRecordingSettingsForCommand(
        AppServices services,
        IReadOnlyList<string> args,
        bool allowMissingProfile = true)
    {
        var profileName = Value(args, "--recording-profile") ?? Value(args, "--record-profile");
        var source = services.Settings.Recording;
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            var profile = FindRecordingProfile(services.Settings, profileName);
            if (profile is null)
            {
                Console.Error.WriteLine($"Recording profile not found: {profileName}. Known profiles: {FormatRecordingProfileNames(services.Settings)}");
                return null;
            }

            source = profile.Settings;
        }
        else if (!allowMissingProfile)
        {
            source = services.Settings.Recording;
        }

        return BuildRecordingSettings(
            source,
            args,
            ParseInt(args, "--fps", source.FramesPerSecond));
    }

    private static RecordingWorkflowProfile? FindRecordingProfile(AppSettings settings, string name)
    {
        var normalized = NormalizeProfileLookup(name);
        return settings.RecordingProfiles.FirstOrDefault(profile =>
                profile.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) ??
            settings.RecordingProfiles.FirstOrDefault(profile =>
                NormalizeProfileLookup(profile.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatRecordingProfileNames(AppSettings settings)
    {
        var names = settings.RecordingProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? "(none)" : string.Join(", ", names);
    }

    private static string NormalizeProfileLookup(string value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static RecordingSettings BuildRecordingSettings(RecordingSettings source, IReadOnlyList<string> args, int fps)
    {
        var settings = new RecordingSettings
        {
            PreferProductionCaptureEngine = source.PreferProductionCaptureEngine,
            PreferHevcEncoding = source.PreferHevcEncoding,
            QualityProfile = source.QualityProfile,
            FramesPerSecond = fps,
            TargetWidth = source.TargetWidth,
            TargetHeight = source.TargetHeight,
            BitrateKbps = source.BitrateKbps,
            UseVariableBitrate = source.UseVariableBitrate,
            IncludeMicrophone = source.IncludeMicrophone,
            IncludeSystemAudio = source.IncludeSystemAudio,
            MicrophoneDeviceId = source.MicrophoneDeviceId,
            SystemAudioDeviceId = source.SystemAudioDeviceId,
            MicrophoneGain = source.MicrophoneGain,
            SystemAudioGain = source.SystemAudioGain,
            NoiseGateThresholdDb = source.NoiseGateThresholdDb,
            MicrophoneMuted = source.MicrophoneMuted,
            SystemAudioMuted = source.SystemAudioMuted,
            EnableWebcamOverlay = source.EnableWebcamOverlay,
            WebcamDeviceId = source.WebcamDeviceId,
            WebcamOverlayPosition = source.WebcamOverlayPosition,
            WebcamOverlayShape = source.WebcamOverlayShape,
            MirrorWebcam = source.MirrorWebcam,
            ShowCountdown = source.ShowCountdown,
            ShowRecordingBorder = source.ShowRecordingBorder,
            ShowRecordingTimer = source.ShowRecordingTimer,
            ShowKeystrokeOverlay = source.ShowKeystrokeOverlay,
            RecordingTimerPosition = source.RecordingTimerPosition,
            KeystrokeOverlayPosition = source.KeystrokeOverlayPosition,
            RecordingOverlayBadgeFontSize = source.RecordingOverlayBadgeFontSize,
            RecordingOverlayStyle = source.RecordingOverlayStyle
        };

        var quality = Value(args, "--quality") ?? Value(args, "--profile");
        if (!string.IsNullOrWhiteSpace(quality))
        {
            settings.QualityProfile = quality;
        }

        var (width, height) = ParseOptionalVideoSize(args);
        if (width.HasValue)
        {
            settings.TargetWidth = width.Value;
        }

        if (height.HasValue)
        {
            settings.TargetHeight = height.Value;
        }

        var bitrate = Value(args, "--bitrate");
        if (!string.IsNullOrWhiteSpace(bitrate))
        {
            settings.BitrateKbps = ParseBitrateKbps(bitrate);
            settings.UseVariableBitrate = false;
        }

        if (Has(args, "--vbr") || Has(args, "--variable-bitrate"))
        {
            settings.UseVariableBitrate = true;
        }

        if (Has(args, "--cbr") || Has(args, "--constant-bitrate"))
        {
            settings.UseVariableBitrate = false;
        }

        if (Has(args, "--hevc") || Has(args, "--h265") || Has(args, "--prefer-hevc"))
        {
            settings.PreferHevcEncoding = true;
        }

        if (Has(args, "--h264") || Has(args, "--no-hevc") || Has(args, "--no-h265"))
        {
            settings.PreferHevcEncoding = false;
        }

        if (Has(args, "--mic") || Has(args, "--microphone"))
        {
            settings.IncludeMicrophone = true;
        }

        var microphoneDevice = Value(args, "--mic-device") ?? Value(args, "--microphone-device");
        if (microphoneDevice is not null)
        {
            settings.MicrophoneDeviceId = NormalizeDeviceIdOverride(microphoneDevice);
        }

        var audioGain = ParseOptionalDouble(args, "--audio-gain");
        if (audioGain.HasValue)
        {
            settings.MicrophoneGain = audioGain.Value;
            settings.SystemAudioGain = audioGain.Value;
        }

        settings.MicrophoneGain = ParseOptionalDouble(args, "--mic-gain") ??
            ParseOptionalDouble(args, "--microphone-gain") ??
            settings.MicrophoneGain;

        settings.NoiseGateThresholdDb = ParseOptionalDouble(args, "--noise-gate-db") ??
            ParseOptionalDouble(args, "--noise-gate") ??
            settings.NoiseGateThresholdDb;

        if (Has(args, "--mute-mic") || Has(args, "--mute-microphone"))
        {
            settings.MicrophoneMuted = true;
        }

        if (Has(args, "--unmute-mic") || Has(args, "--unmute-microphone"))
        {
            settings.MicrophoneMuted = false;
        }

        if (Has(args, "--no-mic") || Has(args, "--no-microphone"))
        {
            settings.IncludeMicrophone = false;
        }

        if (Has(args, "--system-audio") || Has(args, "--loopback"))
        {
            settings.IncludeSystemAudio = true;
        }

        var systemAudioDevice = Value(args, "--system-audio-device") ?? Value(args, "--loopback-device");
        if (systemAudioDevice is not null)
        {
            settings.SystemAudioDeviceId = NormalizeDeviceIdOverride(systemAudioDevice);
        }

        settings.SystemAudioGain = ParseOptionalDouble(args, "--system-audio-gain") ??
            ParseOptionalDouble(args, "--loopback-gain") ??
            settings.SystemAudioGain;

        if (Has(args, "--mute-system-audio") || Has(args, "--mute-loopback"))
        {
            settings.SystemAudioMuted = true;
        }

        if (Has(args, "--unmute-system-audio") || Has(args, "--unmute-loopback"))
        {
            settings.SystemAudioMuted = false;
        }

        if (Has(args, "--no-system-audio") || Has(args, "--no-loopback"))
        {
            settings.IncludeSystemAudio = false;
        }

        if (Has(args, "--webcam") || Has(args, "--camera"))
        {
            settings.EnableWebcamOverlay = true;
        }

        var webcamDevice = Value(args, "--webcam-device") ?? Value(args, "--camera-device");
        if (webcamDevice is not null)
        {
            settings.WebcamDeviceId = NormalizeDeviceIdOverride(webcamDevice);
        }

        if (Has(args, "--no-webcam") || Has(args, "--no-camera"))
        {
            settings.EnableWebcamOverlay = false;
        }

        if (Has(args, "--no-countdown"))
        {
            settings.ShowCountdown = false;
        }

        if (Has(args, "--countdown"))
        {
            settings.ShowCountdown = true;
        }

        if (Has(args, "--border") || Has(args, "--recording-border"))
        {
            settings.ShowRecordingBorder = true;
        }

        if (Has(args, "--no-border") || Has(args, "--no-recording-border"))
        {
            settings.ShowRecordingBorder = false;
        }

        if (Has(args, "--timer"))
        {
            settings.ShowRecordingTimer = true;
        }

        if (Has(args, "--no-timer"))
        {
            settings.ShowRecordingTimer = false;
        }

        if (Has(args, "--keys") || Has(args, "--keystrokes") || Has(args, "--keystroke-overlay"))
        {
            settings.ShowKeystrokeOverlay = true;
        }

        if (Has(args, "--no-keys") || Has(args, "--no-keystrokes") || Has(args, "--no-keystroke-overlay"))
        {
            settings.ShowKeystrokeOverlay = false;
        }

        var timerPosition = Value(args, "--timer-position") ?? Value(args, "--recording-timer-position");
        if (!string.IsNullOrWhiteSpace(timerPosition))
        {
            settings.RecordingTimerPosition = RecordingSettingsNormalizer.NormalizeOverlayPosition(timerPosition, "TopLeft");
        }

        var keystrokePosition = Value(args, "--key-position") ??
            Value(args, "--keys-position") ??
            Value(args, "--keystroke-position") ??
            Value(args, "--keystroke-overlay-position");
        if (!string.IsNullOrWhiteSpace(keystrokePosition))
        {
            settings.KeystrokeOverlayPosition = RecordingSettingsNormalizer.NormalizeOverlayPosition(keystrokePosition, "BottomLeft");
        }

        var badgeSize = ParseOptionalInt(args, "--overlay-badge-size") ??
            ParseOptionalInt(args, "--badge-size") ??
            ParseOptionalInt(args, "--overlay-font-size");
        if (badgeSize.HasValue)
        {
            settings.RecordingOverlayBadgeFontSize = Math.Clamp(badgeSize.Value, 10, 32);
        }

        var overlayStyle = Value(args, "--overlay-style") ?? Value(args, "--badge-style");
        if (!string.IsNullOrWhiteSpace(overlayStyle))
        {
            settings.RecordingOverlayStyle = RecordingSettingsNormalizer.NormalizeOverlayStyle(overlayStyle);
        }

        return settings;
    }

    private static AudioCaptureProcessingSettings BuildAudioProcessingSettings(
        IReadOnlyList<string> args,
        double defaultGain,
        double defaultNoiseGateThresholdDb,
        bool defaultMuted,
        AudioCaptureSource source)
    {
        var gain = ParseOptionalDouble(args, "--gain") ??
            ParseOptionalDouble(args, "--audio-gain") ??
            defaultGain;
        if (source == AudioCaptureSource.Microphone)
        {
            gain = ParseOptionalDouble(args, "--mic-gain") ??
                ParseOptionalDouble(args, "--microphone-gain") ??
                gain;
        }
        else
        {
            gain = ParseOptionalDouble(args, "--system-audio-gain") ??
                ParseOptionalDouble(args, "--loopback-gain") ??
                gain;
        }

        var noiseGate = ParseOptionalDouble(args, "--noise-gate-db") ??
            ParseOptionalDouble(args, "--noise-gate") ??
            defaultNoiseGateThresholdDb;
        var muted = defaultMuted;
        if (Has(args, "--mute"))
        {
            muted = true;
        }

        if (Has(args, "--unmute"))
        {
            muted = false;
        }

        if (source == AudioCaptureSource.Microphone)
        {
            if (Has(args, "--mute-mic") || Has(args, "--mute-microphone"))
            {
                muted = true;
            }

            if (Has(args, "--unmute-mic") || Has(args, "--unmute-microphone"))
            {
                muted = false;
            }
        }
        else
        {
            if (Has(args, "--mute-system-audio") || Has(args, "--mute-loopback"))
            {
                muted = true;
            }

            if (Has(args, "--unmute-system-audio") || Has(args, "--unmute-loopback"))
            {
                muted = false;
            }
        }

        return AudioSampleProcessor.Normalize(new AudioCaptureProcessingSettings(gain, noiseGate, muted));
    }

    private static async Task<int> PrintRecordingDevicesAsync(AppServices services, string[] args)
    {
        var snapshot = await services.Diagnostics.GetRecordingDeviceSnapshotAsync();
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }

        Console.WriteLine("GoatShot recording devices");
        Console.WriteLine(snapshot.SelectionSummary);
        if (!string.IsNullOrWhiteSpace(snapshot.AudioCaptureSummary))
        {
            Console.WriteLine(snapshot.AudioCaptureSummary);
        }

        Console.WriteLine();
        PrintConfidenceReport("Device confidence", snapshot.Confidence);

        Console.WriteLine();
        PrintAudioDeviceGroup("Microphones", snapshot.Microphones);
        Console.WriteLine();
        PrintAudioDeviceGroup("System audio loopback devices", snapshot.SystemAudioDevices);
        Console.WriteLine();
        PrintCameraDeviceGroup("Webcams", snapshot.Cameras);

        if (snapshot.Issues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Issues");
            foreach (var issue in snapshot.Issues)
            {
                Console.WriteLine($"- {issue}");
            }
        }

        return 0;
    }

    private static async Task<int> PrintRecordingReadinessAsync(AppServices services, string[] args)
    {
        var settings = ResolveRecordingSettingsForCommand(services, args);
        if (settings is null)
        {
            return 2;
        }

        var readiness = await services.Diagnostics.GetRecordingReadinessSnapshotAsync(settings);
        var plan = readiness.EnginePlan;
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(readiness, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return plan.CanRecord ? 0 : 1;
        }

        Console.WriteLine("GoatShot recording preflight");
        Console.WriteLine($"Selected engine: {plan.ChoiceLabel}");
        Console.WriteLine($"Can record MP4 now: {(plan.CanRecord ? "yes" : "no")}");
        Console.WriteLine($"Production requested: {(plan.ProductionRequested ? "yes" : "no")}");
        Console.WriteLine($"Production prerequisites met: {(plan.ProductionPrerequisitesMet ? "yes" : "no")}");
        Console.WriteLine($"Production frame capture wired: {(plan.ProductionFrameCaptureImplemented ? "yes" : "no")}");
        Console.WriteLine($"Production engine wired: {(plan.ProductionEngineImplemented ? "yes" : "no")}");
        Console.WriteLine($"Preferred production codec: {plan.ProductionEncoder.Codec} ({plan.ProductionEncoder.ChoiceLabel})");
        Console.WriteLine($"Production encoder selection: {plan.ProductionEncoder.Summary}");
        Console.WriteLine($"FFmpeg fallback encoder selection: {plan.FallbackVideoEncoder.Summary}");
        Console.WriteLine($"FFmpeg fallback available: {(plan.FallbackAvailable ? "yes" : "no")}");
        Console.WriteLine(plan.Summary);

        Console.WriteLine();
        PrintConfidenceReport("Recording confidence", readiness.Confidence);

        PrintPlanGroup("Production blockers", plan.ProductionBlockers);
        PrintPlanGroup("Fallback blockers", plan.FallbackBlockers);
        PrintPlanGroup("Notes", plan.Warnings);

        return plan.CanRecord ? 0 : 1;
    }

    private static async Task<int> PrintRecordingMediaProbeAsync(string[] args)
    {
        var input = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("diagnostics recording-media requires an MP4 file path.");
            return 2;
        }

        var result = await RecordingMediaProbeService.ProbeAsync(input);
        if (Has(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return result.Succeeded || result.Skipped ? 0 : 1;
        }

        Console.WriteLine("GoatShot recording media metadata");
        Console.WriteLine($"File: {result.FilePath}");
        Console.WriteLine($"ffprobe: {(result.FfprobeAvailable ? result.FfprobePath : "unavailable")}");
        Console.WriteLine(result.Message);
        Console.WriteLine(result.SyncSummary);
        return result.Succeeded || result.Skipped ? 0 : 1;
    }

    private static void PrintPlanGroup(string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(title);
        foreach (var value in values)
        {
            Console.WriteLine($"- {value}");
        }
    }

    private static void PrintConfidenceReport(string title, RecordingConfidenceReport report)
    {
        Console.WriteLine(title);
        Console.WriteLine($"Overall: {report.Overall}");
        Console.WriteLine(report.Summary);
        foreach (var signal in report.Signals)
        {
            Console.WriteLine($"- {signal.ToStatusText()}");
        }
    }

    private static void PrintAudioDeviceGroup(string title, IReadOnlyList<AudioCaptureDevice> devices)
    {
        Console.WriteLine(title);
        if (devices.Count == 0)
        {
            Console.WriteLine("  (none found)");
            return;
        }

        foreach (var device in devices)
        {
            var defaultText = device.IsDefault ? " default" : string.Empty;
            var loopbackText = device.SupportsLoopback ? " loopback" : string.Empty;
            Console.WriteLine($"  - {device.DisplayName}{defaultText}{loopbackText}");
            Console.WriteLine($"    id: {device.Id}");
            if (device.PeakLevel.HasValue)
            {
                Console.WriteLine($"    peak: {device.PeakLevel.Value:P0}");
            }

            if (!string.IsNullOrWhiteSpace(device.MeterStatus))
            {
                Console.WriteLine($"    meter: {device.MeterStatus}");
            }
        }
    }

    private static void PrintCameraDeviceGroup(string title, IReadOnlyList<CameraOverlayDevice> devices)
    {
        Console.WriteLine(title);
        if (devices.Count == 0)
        {
            Console.WriteLine("  (none found)");
            return;
        }

        foreach (var device in devices)
        {
            var defaultText = device.IsDefault ? " default" : string.Empty;
            Console.WriteLine($"  - {device.DisplayName}{defaultText}");
            Console.WriteLine($"    id: {device.Id}");
        }
    }

    private static string NormalizeDeviceIdOverride(string value)
    {
        return value.Trim().Equals("default", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("system-default", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value.Trim();
    }

    private static AudioCaptureSource ParseAudioCaptureSource(string value)
    {
        value = value.Trim();
        if (value.Equals("microphone", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("mic", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            return AudioCaptureSource.Microphone;
        }

        if (value.Equals("system", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("system-audio", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("loopback", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("speaker", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("speakers", StringComparison.OrdinalIgnoreCase))
        {
            return AudioCaptureSource.SystemAudio;
        }

        throw new ArgumentException($"Unknown audio source: {value}. Use microphone or system-audio.");
    }

    private static int ParseBitrateKbps(string value)
    {
        value = value.Trim();
        var multiplier = 1d;
        if (value.EndsWith("mbps", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000d;
            value = value[..^4];
        }
        else if (value.EndsWith('m') || value.EndsWith('M'))
        {
            multiplier = 1_000d;
            value = value[..^1];
        }
        else if (value.EndsWith("kbps", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }
        else if (value.EndsWith('k') || value.EndsWith('K'))
        {
            value = value[..^1];
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? (int)Math.Round(parsed * multiplier)
            : 0;
    }

    private static double ParseDouble(IReadOnlyList<string> args, string name, double fallback)
    {
        return double.TryParse(Value(args, name), CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static double? ParseOptionalDouble(IReadOnlyList<string> args, string name)
    {
        return double.TryParse(Value(args, name), CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string MergeSensitiveScanNote(string? notes, string scanNote)
    {
        return MergePrefixedNote(notes, "Sensitive scan:", scanNote);
    }

    private static string MergePrefixedNote(string? notes, string prefix, string note)
    {
        var lines = (notes ?? string.Empty)
            .ReplaceLineEndings(Environment.NewLine)
            .Split(Environment.NewLine, StringSplitOptions.None)
            .Where(line => !line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(note);
        return string.Join(Environment.NewLine, lines);
    }

    private static string DefaultAiAnalysisPrompt()
    {
        return "Explain this screenshot for a bug report or support note. Summarize the visible UI, likely user action, important text, and any visible risk or sensitive data. Do not invent hidden context.";
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static VisualRedactionMode ParseRedactionMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return VisualRedactionMode.Solid;
        }

        return Enum.TryParse<VisualRedactionMode>(value, ignoreCase: true, out var mode)
            ? mode
            : throw new ArgumentException($"Unknown redaction mode: {value}. Use solid, pixelate, or blur.");
    }

    private static (int Width, int Height) ParseCaptureSize(string[] args)
    {
        var size = Value(args, "--size")
            ?? args.Skip(1).FirstOrDefault(value =>
                !value.StartsWith("--", StringComparison.Ordinal) &&
                value.Contains('x', StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', 'X');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var width) &&
                int.TryParse(parts[1], out var height) &&
                width > 0 &&
                height > 0)
            {
                return (width, height);
            }
        }

        var widthText = Value(args, "--width");
        var heightText = Value(args, "--height");
        if (int.TryParse(widthText, out var parsedWidth) &&
            int.TryParse(heightText, out var parsedHeight) &&
            parsedWidth > 0 &&
            parsedHeight > 0)
        {
            return (parsedWidth, parsedHeight);
        }

        throw new ArgumentException("Fixed capture requires --size WIDTHxHEIGHT, or --width and --height.");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            GoatShot CLI

            Usage:
              goatshot capture fullscreen --output shot.png [--delay 2] [--engine default|production|wgc] [--copy] [--workspace] [--profile ClientSupport]
              goatshot capture all-monitors --output shot.png [--delay 2] [--engine default|production|wgc]
              goatshot capture monitor --output shot.png [--delay 2] [--engine default|production|wgc|dxgi]
              goatshot capture window --output shot.png [--delay 2] [--engine default|production|wgc]
              goatshot capture region --output shot.png [--delay 2] [--engine default|production|wgc]
              goatshot capture scrolling --output stitched.png [--delay 2] [--scroll-profile browser|table|document] [--axis vertical|horizontal] [--auto-sticky|--no-auto-sticky] [--sticky-pixels 80]
              goatshot capture fixed --size 1280x720 --output shot.png [--delay 2] [--engine default|production|wgc]
              goatshot capture android --output phone.png [--device SERIAL] [--adb C:\Android\platform-tools\adb.exe] [--workspace] [--json]
              goatshot capture android-preview [--strategy screencap-polling|h264-stream] [--duration 5] [--frame-interval-ms 500] [--max-bytes 52428800] [--device SERIAL] [--adb C:\Android\platform-tools\adb.exe] [--json]
              goatshot capture android-preview --execute --safe-content-confirmed --device SERIAL [--strategy screencap-polling|h264-stream] [--duration 5] [--frame-interval-ms 500] [--max-bytes 52428800] [--output-dir preview-frames] [--contact-sheet] [--contact-sheet-output preview-contact-sheet.png] [--contact-sheet-max-frames 16] [--adb C:\Android\platform-tools\adb.exe] [--json]
              goatshot capture android-video --duration 10 --output phone.mp4 [--device SERIAL] [--adb C:\Android\platform-tools\adb.exe] [--workspace] [--json]
              goatshot record monitor --duration 3 --output demo.gif [--workspace]
              goatshot record all-monitors --duration 3 --format gif --output all-monitors.gif [--workspace]
              goatshot record window --duration 3 --format gif --output window.gif [--workspace]
              goatshot record region --bounds x,y,width,height --duration 3 --format gif --output region.gif [--workspace]
              goatshot record fixed --size 1280x720 --duration 3 --format gif --output fixed.gif [--workspace]
              goatshot record devices [--json]
              goatshot record profiles [--json]
              goatshot record save-profile NAME [--quality small|balanced|high|archive] [--fps 10] [--size 1280x720] [--hevc|--h264] [--mic-gain 1.25] [--noise-gate-db -55] [--timer] [--timer-position top-right] [--keys-position bottom-left] [--overlay-badge-size 18] [--overlay-style neon|high-contrast|subtle] [--border]
              goatshot record preflight [--hevc|--h264] [--json]
              goatshot record audio [microphone|system-audio] --duration 2 --output audio.wav [--device ID] [--gain 1.5] [--noise-gate-db -55] [--mute]
              goatshot record monitor --duration 3 --format mp4 --recording-profile Balanced --output demo.mp4 [--fps 10] [--quality balanced] [--size 1280x720] [--hevc|--h264] [--probe-media] [--border] [--timer] [--timer-position top-right] [--keys] [--keys-position bottom-left] [--overlay-badge-size 18] [--overlay-style neon|high-contrast|subtle] [--mic] [--mic-device ID] [--mic-gain 1.25] [--mute-mic] [--system-audio] [--system-audio-device ID] [--system-audio-gain 0.8] [--mute-system-audio] [--noise-gate-db -55] [--webcam] [--webcam-device ID] [--workspace]
              goatshot record all-monitors --duration 3 --format mp4 --fps 10 --quality small --output all-monitors.mp4 [--workspace]
              goatshot record region --bounds x,y,width,height --duration 3 --format mp4 --fps 10 --output region.mp4 [--workspace]
              goatshot video frame demo.mp4 --at 1 --output frame.png [--workspace]
              goatshot video trim demo.mp4 --start 0 --duration 5 --output clip.mp4 [--workspace]
              goatshot video mute demo.mp4 --output muted.mp4 [--workspace]
              goatshot video volume demo.mp4 --gain 1.5 --output louder.mp4 [--workspace]
              goatshot video denoise demo.mp4 [--noise-reduction-db 12] --output denoised.mp4 [--workspace] [--json]
              goatshot video speed demo.mp4 --factor 2 --output fast.mp4 [--workspace]
              goatshot video crop demo.mp4 --x 0 --y 0 --width 1280 --height 720 --output cropped.mp4 [--workspace]
              goatshot video resize demo.mp4 --size 1280x720 --quality balanced --output resized.mp4 [--workspace]
              goatshot video cut demo.mp4 --start 2 --duration 3 --output cut.mp4 [--workspace]
              goatshot video merge intro.mp4 demo.mp4 outro.mp4 --output merged.mp4 [--workspace]
              goatshot video intro-outro demo.mp4 --intro intro.png --outro outro.png --size 1280x720 --output bookended.mp4 [--workspace]
              goatshot video subtitles demo.mp4 --srt captions.srt --output subtitled.mp4 [--workspace]
              goatshot video srt captions.srt --output captions-copy.srt [--workspace]
              goatshot video convert demo.mp4 --format gif --output demo.gif [--workspace]
              goatshot video plan-silence demo.mp4 [--noise-db -35] [--min-silence 0.7] [--padding 0.15] [--output silence-plan.json] [--json]
              goatshot video plan-transcript --srt captions.srt|--transcript transcript.txt --terms "loading,failed" [--padding 0.25] [--output transcript-plan.json] [--json]
              goatshot video plan-filler --srt captions.srt|--transcript transcript.txt [--terms "um,uh,like"] [--output filler-plan.json] [--json]
              goatshot video apply-plan demo.mp4 --plan filler-plan.json --accept-plan --output cleaned.mp4 [--workspace] [--json]
              goatshot video plan-composite [--preset pip|side-by-side|stacked] [--screen screen.mp4] [--camera webcam.mp4] [--size 1920x1080] [--output composite-plan.json] [--json]
              goatshot video apply-composite --plan composite-plan.json --accept-plan [--screen screen.mp4] [--camera webcam.mp4] [--audio screen|camera|none] --output composite.mp4 [--workspace] [--json]
              goatshot video generate-mask webcam.mp4 [--method keyed|alpha|luma] [--key-color 0x00ff00] [--similarity 0.18] [--blend 0.08] [--luma-threshold 128] [--invert-mask] [--output foreground-mask.mp4] [--workspace] [--json]
              goatshot video person-mask webcam.mp4 --runner segmenter.exe --runner-args ""--input {input} --output {output}"" [--model model.onnx] [--timeout-seconds 600] --accept-external-runner [--output person-mask.mp4] [--workspace] [--json]
              goatshot video hosted-person-mask webcam.mp4 --endpoint https://segmenter.example.test/mask --accept-hosted-service [--api-key-env GOATSHOT_SEGMENTER_TOKEN] [--model person-v1] --output person-mask.mp4 [--workspace] [--json]
              goatshot video person-model validate --manifest person-segmentation-model.json [--json]
              goatshot video person-model stage --manifest person-segmentation-model.json [--accept-download] [--json]
              goatshot video mask-quality --generated-mask person-mask-frame.png --reference-mask reviewed-mask-frame.png [--threshold 128] [--min-iou 0.9 --min-precision 0.9 --min-recall 0.9] [--output artifacts\video-mask-quality] [--json]
              goatshot video mask-quality --generated-mask person-mask.mp4 --reference-mask reviewed-mask.mp4 [--max-frames 300] [--timeout-seconds 600] [--output artifacts\video-mask-quality] [--json]
              goatshot video plan-background webcam.mp4 [--mode blur|remove|replace] [--key-color 0x00ff00] [--similarity 0.18] [--blend 0.08] [--blur 12] [--background-color 0x101820] [--mask person-mask.mp4] [--invert-mask] [--output background-plan.json] [--json]
              goatshot video apply-background webcam.mp4 --plan background-plan.json --accept-plan --output webcam-clean.mp4 [--workspace] [--json]
              goatshot video background-probe [--json]
              goatshot image combine a.png b.png --orientation vertical --output combined.png [--workspace]
              goatshot image stitch frame1.png frame2.png [--scroll-profile browser|table|document] [--axis vertical|horizontal] [--auto-sticky|--no-auto-sticky] [--sticky-pixels 80] --output stitched.png [--workspace]
              goatshot image split combined.png --rows 2 --columns 1 --output-dir .\tiles [--workspace]
              goatshot import shot.png
              goatshot share shot.png --destination "Clipboard image"
              goatshot share shot.png --destination "Local folder" --export-folder C:\Temp
              goatshot upload shot.png --provider "Custom webhook" --webhook-url https://example.test/upload
              goatshot upload shot.png --provider Slack --slack-webhook-url https://hooks.slack.com/services/... --slack-message "Capture ready: {file}"
              goatshot upload shot.png --provider Discord --discord-webhook-url https://discord.com/api/webhooks/...
              goatshot upload shot.png --provider "Microsoft Teams" --teams-webhook-url https://example.webhook.office.com/...
              goatshot upload shot.png --provider "S3-compatible" --s3-endpoint https://s3.example.test --s3-bucket captures --s3-region us-east-1 --s3-access-key KEY --s3-secret-key SECRET
              goatshot upload shot.png --provider Imgur --imgur-client-id CLIENT_ID
              goatshot upload shot.png --provider SFTP --sftp-host files.example.test --sftp-user deploy --sftp-remote-dir /var/www/captures --sftp-key C:\Keys\deploy_ed25519
              goatshot upload shot.png --provider WebDAV --webdav-base-url https://files.example.test/dav --webdav-user USER --webdav-password PASS --webdav-remote-dir captures
              goatshot upload shot.png --provider FTP --ftp-host files.example.test --ftp-user USER --ftp-password PASS --ftp-remote-dir captures [--ftps]
              goatshot upload shot.png --provider Cloudinary --cloudinary-cloud-name my-cloud --cloudinary-upload-preset preset
              goatshot upload shot.png --provider Dropbox --dropbox-folder /GoatShot --dropbox-access-token TOKEN
              goatshot upload shot.png --provider "Google Drive" --google-drive-folder-id FOLDER_ID --google-drive-access-token TOKEN
              goatshot upload shot.png --provider "Google Photos" --google-photos-album-id ALBUM_ID --google-photos-access-token TOKEN
              goatshot upload shot.png --provider OneDrive --onedrive-folder /GoatShot --onedrive-access-token TOKEN
              goatshot upload recording.mp4 --provider YouTube --youtube-privacy private --youtube-access-token TOKEN
              goatshot upload shot.png --provider OneNote --onenote-section-id SECTION_ID --onenote-access-token TOKEN
              goatshot upload shot.png --provider Linear --linear-team-id TEAM_ID --linear-api-key KEY
              goatshot upload shot.png --provider "GitHub Issues" --github-repo owner/repo --github-token TOKEN --github-labels bug,goatshot
              goatshot upload shot.png --provider Jira --jira-base-url https://example.atlassian.net --jira-project-key BUG --jira-email user@example.test --jira-api-token TOKEN
              goatshot upload shot.png --provider "Azure DevOps" --azure-devops-org ORG --azure-devops-project PROJECT --azure-devops-pat PAT
              goatshot providers [--json] [--provider OneDrive] [--ready|--not-ready] [--implemented|--roadmap]
              goatshot upload shot.png --provider "Google Drive" --queue
              goatshot queue list [--json] [--limit 20]
              goatshot queue diagnostics [--json]
              goatshot queue process [--limit 2]
              goatshot queue cancel <id>
              goatshot queue retry <id>
              goatshot oauth status [--json]
              goatshot oauth live-proof-plan [--provider "Google Drive"|all] [--output artifacts\oauth-live-proof-plan] [--json]
              goatshot oauth record-live-evidence --provider "Google Drive" --status passed --consent-evidence consent.md --exchange-evidence exchange.txt --refresh-evidence refresh.txt --upload-evidence upload.json --cleanup-evidence cleanup.md --account-evidence account.json [--output artifacts\oauth-live-evidence] [--json]
              goatshot oauth configure "Google Drive" --client-id CLIENT_ID [--callback-port 53628]
              goatshot oauth auth-url "Google Drive" [--state STATE] [--callback http://127.0.0.1:53628/oauth/callback] [--no-pkce]
              goatshot oauth exchange "Google Drive" --code CODE [--code-verifier VERIFIER]
              goatshot oauth refresh "Google Drive"
              goatshot oauth clear "Google Drive"
              goatshot share-history [--json] [--limit 20] [--query text] [--destination OneDrive] [--status succeeded|failed] [--external|--local]
              goatshot share-history copy-url <history-id>
              goatshot share-history copy-markdown <history-id>
              goatshot share-history open <history-id>
              goatshot share-history retry <history-id>
              goatshot metadata strip shot.png --output stripped.png
              goatshot metadata inspect shot.png [--json] [--copy]
              goatshot inspect shot.png [--json] [--copy]
              goatshot hash shot.png [--algorithm sha256|sha1|md5|all] [--json] [--copy]
              goatshot clipboard import [--json]
              goatshot qr decode shot.png [--json] [--copy] [--open]
              goatshot qr generate "https://example.test" --output qr.png
              goatshot search "error text" [--json] [--limit 20]
              goatshot workflows export --output profile.goatshot-workflow.json
              goatshot workflows import profile.goatshot-workflow.json [--merge]
              goatshot workflows validate profile.goatshot-workflow.json [--json]
              goatshot workflows list-rules [--json]
              goatshot workflows templates [template-id] [--json]
              goatshot workflows add-template capture-doc [--enable] [--replace] [--json]
              goatshot workflows status [--json]
              goatshot workflows dry-run <workspace-id-or-file> --trigger capture-created [--json]
              goatshot workflows dry-run-script <workspace-id-or-file> [--script "Write-Output {file}"] [--json]
              goatshot workflows dry-run-webhook <workspace-id-or-file> [--webhook-url https://example.test/upload] [--json]
              goatshot workflows run <workspace-id-or-file> --trigger capture-created [--dry-run] [--json]
              goatshot plugins list [--json]
              goatshot plugins registry validate samples\local-plugins\registry.json [--json]
              goatshot plugins install-plan sample.redaction-note --registry samples\local-plugins\registry.json [--json]
              goatshot plugins stage sample.redaction-note --registry samples\local-plugins\registry.json [--json]
              goatshot plugins apply-updates --registry samples\local-plugins\registry.json [--install-staged] [--replace] [--json]
              goatshot plugins updates --registry samples\local-plugins\registry.json [--json]
              goatshot plugins background-updates --registry samples\local-plugins\registry.json [--mode check-only|stage-only] [--interval-hours 24] [--force] [--json]
              goatshot plugins schedule-updates --registry samples\local-plugins\registry.json [--mode check-only|stage-only] [--interval-hours 24] [--output artifacts\plugin-update-schedule] [--json]
              goatshot plugins update-task status --manifest artifacts\plugin-update-schedule\plugin-update-schedule.json [--json]
              goatshot plugins update-task register --manifest artifacts\plugin-update-schedule\plugin-update-schedule.json --accept-task-registration [--dry-run] [--json]
              goatshot plugins update-task unregister --manifest artifacts\plugin-update-schedule\plugin-update-schedule.json --accept-task-removal [--dry-run] [--json]
              goatshot plugins marketplace-plan --registry samples\local-plugins\registry.json [--json]
              goatshot plugins remove-staged sample.redaction-note [--json]
              goatshot plugins install-staged sample.redaction-note [--version 0.2.0] [--replace] [--json]
              goatshot plugins activate sample.redaction-note --trust --enable --enable-local-plugins --allow-action write-note --accept-risk [--json]
              goatshot plugins disable sample.redaction-note [--json]
              goatshot plugins uninstall sample.redaction-note [--json]
              goatshot plugins dry-run <plugin-id> <action-id> [--json]
              goatshot plugins run <plugin-id> <action-id> [--json]
              goatshot companion-portal export --output artifacts\companion-portal-readonly [--manual-validation artifacts\manual-validation\yyyy-mm-dd] [--proof-root artifacts] [--share-history-limit 200] [--json]
              goatshot admin-policy explain [bundle.json] [--json]
              goatshot admin-policy validate bundle.json [--json]
              goatshot admin-policy export --output bundle.json [--json]
              goatshot admin-policy import bundle.json [--replace] [--json]
              goatshot admin-policy diff bundle.json [--json]
              goatshot browser-extension validate browser-extension\samples\full-page-capture-payload.json [--redacted-output redacted-payload.json] [--json]
              goatshot browser-extension receive browser-extension\samples\full-page-capture-payload.json [--screenshot page.png] [--stitch-package package-dir] [--redacted-output redacted-payload.json] [--json]
              goatshot browser-extension live-fixture --browser chrome --source browser-extension [--extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] [--payload payload.json --stitch-package package-dir] [--output artifacts\manual-validation\yyyy-mm-dd\browser-extension-live-fixture] [--json]
              goatshot browser-extension proof validate --folder artifacts\manual-validation\yyyy-mm-dd\browser-extension-live-fixture --source browser-extension --browser chrome --browser-version "Chrome 126" [--extension-id id --payload payload.json --redacted-payload redacted-payload.json --stitch-package package-dir --import-result import-result.json] [--json]
              goatshot browser-extension status [--json]
              goatshot browser-extension package --source browser-extension --output artifacts\browser-extension\goatshot-browser-extension.zip [--json]
              goatshot browser-extension store-readiness --source browser-extension --target all [--support-url https://example.invalid/support --privacy-url https://example.invalid/privacy] [--output artifacts\browser-extension-store-readiness] [--json]
              goatshot browser-extension store-package --source browser-extension --target chrome|edge|firefox|all --support-url https://example.invalid/support --privacy-url https://example.invalid/privacy --release-notes "Reviewed submission package." [--output artifacts\browser-extension-store-package] [--json]
              goatshot browser-extension publication-plan --source browser-extension --target chrome|edge|firefox|all [--store-package-root artifacts\browser-extension-store-package] [--support-url https://example.invalid/support --privacy-url https://example.invalid/privacy] [--output artifacts\browser-extension-publication-plan] [--json]
              goatshot browser-extension record-publication-evidence --target chrome --status passed --package-evidence package.md --account-evidence account.md --submission-evidence submission.md --review-evidence review.md --signing-evidence signing.md --listing-evidence listing.md --install-evidence install.md [--live-browser-evidence browser-proof.md] [--output artifacts\browser-extension-publication-evidence] [--json]
              goatshot browser-extension enterprise-policy-plan --browser chrome|edge|firefox|all --source browser-extension [--chrome-extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --edge-extension-id bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb --firefox-extension-id goatshot@example.invalid --firefox-install-url https://example.invalid/goatshot.xpi] [--output artifacts\browser-extension-enterprise-policy-plan] [--json]
              goatshot browser-extension install-plan --browser chrome|edge|firefox|all --source browser-extension [--store-package-root artifacts\browser-extension-store-package] [--extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] [--output artifacts\browser-extension-install-plan] [--json]
              goatshot browser-extension install-assist --browser chrome|edge|firefox --source browser-extension [--url about:blank] [--remote-debugging-port 58617] [--start] [--output artifacts\browser-extension-install-assist] [--json]
              goatshot browser-extension install-guide --browser chrome --extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa [--output artifacts\browser-extension\install-guide.md] [--json]
              goatshot browser-extension native-host status [--json]
              goatshot browser-extension native-host manifest --browser chrome --chrome-extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa [--output-dir artifacts\native-host] [--json]
              goatshot browser-extension native-host install --browser chrome --chrome-extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa [--host-exe GoatShot.Cli.exe] [--json]
              goatshot browser-extension native-host uninstall --browser chrome [--json]
              goatshot companion-portal export --output artifacts\companion-portal-readonly [--manual-validation artifacts\manual-validation\yyyy-mm-dd] [--proof-root artifacts] [--share-history-limit 200] [--json]
              goatshot companion-portal media-review --output artifacts\companion-portal-media-review --media shot.png --accept-media-copy [--json]
              goatshot companion-portal serve --output artifacts\companion-portal-preview [--host 127.0.0.1] [--port 0] [--media shot.png --accept-media-copy] [--check] [--json]
              goatshot companion-portal serve --output artifacts\companion-portal-self-hosted --host 0.0.0.0 --self-hosted --accept-remote-clients --auth-token-env GOATSHOT_PORTAL_TOKEN [--check] [--json]
              goatshot print-import setup [--folder .\PrintDrop] [--enable] [--include-subdirectories] [--setup-note setup.md] [--json]
              goatshot print-import contract [--folder .\PrintDrop] [--ensure-folder] [--json]
              goatshot print-import diagnostics [--folder .\PrintDrop] [--ensure-folder] [--json]
              goatshot print-import import printed.pdf [--source-app "Microsoft Print to PDF"] [--title "Invoice"] [--json]
              goatshot diagnostics bundle --output goatshot-diagnostics.zip [--copy]
              goatshot diagnostics print
              goatshot diagnostics devices [--json]
              goatshot diagnostics recording [--hevc|--h264] [--json]
              goatshot diagnostics recording-media demo.mp4 [--json]
              goatshot diagnostics providers [--json] [--not-ready]
              goatshot diagnostics capture-engine [--engine wgc|dxgi] [--json]
              goatshot diagnostics scrolling-fixtures --output-dir artifacts\scrolling-fixtures [--json]
              goatshot diagnostics android [--adb C:\Android\platform-tools\adb.exe] [--json]
              goatshot manual-validation create [--date yyyy-MM-dd] [--output artifacts\manual-validation\yyyy-mm-dd] [--include-diagnostics-bundle] [--force] [--json]
              goatshot manual-validation baseline --folder artifacts\manual-validation\yyyy-mm-dd [--run-commands] [--repo-root .] [--cli-path path\GoatShot.Cli.exe] [--timeout-seconds 300] [--json]
              goatshot manual-validation desktop-proof --folder artifacts\manual-validation\yyyy-mm-dd [--run-commands] [--repo-root .] [--app-path path\GoatShot.exe] [--timeout-seconds 120] [--json]
              goatshot manual-validation hardware-proof --folder artifacts\manual-validation\yyyy-mm-dd [--run-commands] [--repo-root .] [--cli-path path\GoatShot.Cli.exe] [--timeout-seconds 120] [--json]
              goatshot manual-validation operator-pack --folder artifacts\manual-validation\yyyy-mm-dd [--output artifacts\manual-validation\yyyy-mm-dd\required-desktop-operator-pack] [--cli-path path\GoatShot.Cli.exe] [--app-path path\GoatShot.exe] [--json]
              goatshot manual-validation clean-machine-kit --folder artifacts\manual-validation\yyyy-mm-dd [--output artifacts\manual-validation\yyyy-mm-dd\clean-machine-proof-kit] [--portable-zip artifacts\dist\GoatShot-0.1.0-win-x64-portable.zip] [--installer artifacts\dist\GoatShot-Setup-0.1.0-win-x64.exe] [--copy-package] [--json]
              goatshot manual-validation record-clean-machine-evidence --folder artifacts\manual-validation\yyyy-mm-dd --status passed --machine-evidence clean-machine-evidence\machine-info.txt --package-evidence clean-machine-proof-kit\clean-machine-proof-manifest.json --hash-evidence clean-machine-evidence\portable-hash.txt --diagnostics-evidence clean-machine-evidence\diagnostics-print.txt --paths-evidence clean-machine-evidence\paths.txt --first-launch-evidence clean-machine-evidence\main-window-render.png --settings-evidence clean-machine-evidence\settings-save-proof.png --capture-export-evidence clean-machine-evidence\capture-import-edit-export-proof.png --installer-evidence clean-machine-evidence\installer-build-or-skipped.md --privacy-evidence clean-machine-evidence\privacy-review.md [--script-result-evidence clean-machine-evidence\clean-machine-script-result.json] [--json]
              goatshot manual-validation record-desktop-evidence --folder artifacts\manual-validation\yyyy-mm-dd --lane keyboard --status passed --notes-evidence required-desktop-operator-pack\keyboard-traversal\notes.md --surface-coverage-evidence required-desktop-operator-pack\keyboard-traversal\coverage.md --focus-order-evidence required-desktop-operator-pack\keyboard-traversal\focus-order.md --result-evidence required-desktop-operator-pack\keyboard-traversal\result.md --privacy-evidence required-desktop-operator-pack\keyboard-traversal\privacy-review.md [--json]
              goatshot manual-validation record-hardware-evidence --folder artifacts\manual-validation\yyyy-mm-dd --lane multi-monitor-capture --status passed --notes-evidence hardware-evidence\multi-monitor-capture\notes.md --safe-content-evidence hardware-evidence\multi-monitor-capture\safe-content.md --topology-evidence hardware-proof\environment.md --capture-output-evidence hardware-evidence\multi-monitor-capture\capture-output.md --dimensions-evidence hardware-evidence\multi-monitor-capture\dimensions.md --privacy-evidence hardware-evidence\multi-monitor-capture\privacy-review.md [--json]
              goatshot manual-validation record-lane --folder artifacts\manual-validation\yyyy-mm-dd --lane keyboard --status passed|failed|blocked|pending|not-applicable --note "Observed result" [--evidence desktop-proof\screenshots\main-window.png] [--operator name] [--json]
              goatshot manual-validation summarize --folder artifacts\manual-validation\yyyy-mm-dd [--json]
              goatshot manual-validation proof-plan --folder artifacts\manual-validation\yyyy-mm-dd [--output artifacts\manual-validation\yyyy-mm-dd] [--json]
              goatshot manual-validation findings --folder artifacts\manual-validation\yyyy-mm-dd [--output artifacts\manual-validation\yyyy-mm-dd] [--json]
              goatshot bug-report <workspace-id-or-file> --output report.md [--format html|pdf|docx] [--transcript transcript.txt] [--srt captions.srt] [--video-summary summary.md] [--keyframe frame.png] [--context "..."] [--copy]
              goatshot doc-packet <workspace-id-or-file> --output .\packet [--transcript transcript.txt] [--srt captions.srt] [--video-summary summary.md] [--bug-report report.md|--generate-bug-report] [--keyframe frame.png] [--context "..."] [--json]
              goatshot transcribe demo.mp4 [--srt captions.srt] [--whisper-exe whisper.exe] [--whisper-model base-or-model.bin] [--language en] [--ai --provider gemini --ai-model gemini-model --prompt "timestamps"] --output transcript.txt [--json]
              goatshot video-intel demo.mp4 [--transcript transcript.txt] [--srt captions.srt] [--whisper-exe whisper.exe] [--whisper-model base-or-model.bin] [--ai --ai-model gemini-model --prompt "focus"] --output summary.md [--format markdown|html|pdf|docx] [--json]
              goatshot ocr <workspace-id-or-file> [--language en-US] [--output text.txt] [--redact] [--json] [--copy] [--workspace]
              goatshot redact-ocr <workspace-id-or-file> [--mode solid|pixelate|blur] [--language en-US] [--output redacted.png] [--refresh-ocr] [--workspace] [--copy]
              goatshot scan-text ocr.txt [--redact] [--output redacted.txt] [--json]
              goatshot color pick [--copy]
              goatshot paths

            Gemini command:
              goatshot ai-models [--json]
              goatshot ai-edit <file> --prompt "..." [--model gemini-3.1-flash-image] [--output edited.png] [--workspace]
              goatshot ai-analyze <file> [--prompt "..."] [--model gemini-3.1-flash-image] [--output analysis.txt] [--workspace]
              goatshot ai-history [--json] [--limit 20]
              goatshot ai-history prompts [--action ImageEdit] [--json]
              goatshot ai-history accept <id>
              goatshot ai-history reject <id> [--reason "..."]
              goatshot ai-history iterate <id> [--reason "..."]
              goatshot ai-history retry <id> [--model MODEL] [--profile PROFILE] [--prompt "..."] [--transcript transcript.txt] [--srt captions.srt] [--output output.md] [--json]
            """);
    }

    private static JsonSerializerOptions CliJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static AndroidAdbLivePreviewStrategy ParseAndroidLivePreviewStrategy(IReadOnlyList<string> args)
    {
        if (Has(args, "--h264-stream") || Has(args, "--screenrecord-stream"))
        {
            return AndroidAdbLivePreviewStrategy.H264Stream;
        }

        if (Has(args, "--screencap-polling") || Has(args, "--poll-screencap"))
        {
            return AndroidAdbLivePreviewStrategy.ScreencapPolling;
        }

        var raw = Value(args, "--strategy") ?? Value(args, "--preview-strategy");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AndroidAdbLivePreviewStrategy.Auto;
        }

        var normalized = raw.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "auto" => AndroidAdbLivePreviewStrategy.Auto,
            "screencap" or "screencap-polling" or "polling" or "png-polling" => AndroidAdbLivePreviewStrategy.ScreencapPolling,
            "h264" or "h264-stream" or "screenrecord" or "screenrecord-h264" or "screenrecord-stream" => AndroidAdbLivePreviewStrategy.H264Stream,
            _ => (AndroidAdbLivePreviewStrategy)(-1)
        };
    }

    private sealed record OAuthStatusRecord(
        string ProviderName,
        bool ClientIdConfigured,
        bool AuthorizationEndpointConfigured,
        bool TokenEndpointConfigured,
        string Scopes,
        bool UsePkce,
        bool OAuthTokenStored,
        bool RefreshTokenStored,
        bool AccessTokenUsable,
        bool ProviderAccessTokenStored);

}

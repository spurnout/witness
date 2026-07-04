using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AndroidAdbCaptureService
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public const int DefaultScreenrecordDurationSeconds = 10;
    public const int MinimumScreenrecordDurationSeconds = 1;
    public const int MaximumScreenrecordDurationSeconds = 180;
    public const int DefaultLivePreviewDurationSeconds = 5;
    public const int MinimumLivePreviewDurationSeconds = 1;
    public const int MaximumLivePreviewDurationSeconds = 60;
    public const int DefaultLivePreviewFrameIntervalMs = 500;
    public const int MinimumLivePreviewFrameIntervalMs = 250;
    public const int MaximumLivePreviewFrameIntervalMs = 5000;
    public const long DefaultLivePreviewMaxBytes = 50L * 1024L * 1024L;
    public const long MinimumLivePreviewMaxBytes = 1024L * 1024L;
    public const long MaximumLivePreviewMaxBytes = 256L * 1024L * 1024L;
    public const int MaximumLivePreviewExecutionDurationSeconds = 10;
    public const long MaximumLivePreviewExecutionMaxBytes = DefaultLivePreviewMaxBytes;
    public const int DefaultLivePreviewContactSheetMaxFrames = 16;
    public const int MinimumLivePreviewContactSheetMaxFrames = 1;
    public const int MaximumLivePreviewContactSheetMaxFrames = 64;
    public const int DefaultDiagnosticsDevicesTimeoutSeconds = 15;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;
    private readonly IAdbProcessRunner _runner;
    private readonly int _diagnosticsDevicesTimeoutSeconds;

    public AndroidAdbCaptureService(
        AppPaths paths,
        WorkspaceStore workspaceStore,
        IAdbProcessRunner? runner = null,
        int diagnosticsDevicesTimeoutSeconds = DefaultDiagnosticsDevicesTimeoutSeconds)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
        _runner = runner ?? new AdbProcessRunner();
        _diagnosticsDevicesTimeoutSeconds = Math.Max(1, diagnosticsDevicesTimeoutSeconds);
    }

    public async Task<AndroidAdbDiagnostics> GetDiagnosticsAsync(
        string? adbPath = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveAdbPath(adbPath);
        if (resolved is null)
        {
            return new AndroidAdbDiagnostics
            {
                Status = AndroidAdbStatus.MissingAdb,
                Message = "adb.exe was not found. Install Android platform-tools or set GOATSHOT_ADB_PATH.",
                AdbPath = string.Empty
            };
        }

        var devices = await QueryDevicesAsync(resolved, cancellationToken);
        return BuildDiagnostics(resolved, devices);
    }

    public async Task<AndroidAdbCaptureResult> CaptureScreenshotAsync(
        AndroidAdbCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await GetDiagnosticsAsync(request.AdbPath, cancellationToken);
        if (diagnostics.Status is AndroidAdbStatus.MissingAdb or AndroidAdbStatus.AdbFailed)
        {
            return Failed(diagnostics, diagnostics.Message);
        }

        var selected = SelectDevice(diagnostics, request.DeviceSerial);
        if (!selected.Succeeded || selected.Device is null)
        {
            return Failed(diagnostics, selected.Message);
        }

        var output = ResolveOutputPath(request.OutputPath, selected.Device.Serial);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(selected.Device.Serial))
        {
            args.AddRange(["-s", selected.Device.Serial]);
        }

        args.AddRange(["exec-out", "screencap", "-p"]);
        var capture = await _runner.RunBinaryAsync(diagnostics.AdbPath, args, cancellationToken);
        if (capture.ExitCode != 0)
        {
            return Failed(diagnostics, $"ADB screencap failed with exit code {capture.ExitCode}. {ShortOutput(capture.StandardError, capture.StandardOutputText)}");
        }

        if (!LooksLikePng(capture.StandardOutput))
        {
            return Failed(diagnostics, "ADB screencap did not return a PNG payload.");
        }

        await File.WriteAllBytesAsync(output, capture.StandardOutput, cancellationToken);

        CaptureItem? item = null;
        if (request.AddToWorkspace || string.IsNullOrWhiteSpace(request.OutputPath))
        {
            item = await _workspaceStore.AddImageFileAsync(
                output,
                CaptureKind.AndroidDevice,
                $"Android ADB screencap imported from {selected.Device.DisplayLabel}.",
                new CaptureSource
                {
                    ProcessName = "adb",
                    WindowTitle = selected.Device.DisplayLabel,
                    MonitorName = "Android device"
                });
        }

        return new AndroidAdbCaptureResult
        {
            Succeeded = true,
            Status = AndroidAdbStatus.Ready,
            Message = item is null
                ? $"Android screenshot captured: {output}"
                : $"Android screenshot captured and indexed: {item.FileName}",
            OutputPath = output,
            Item = item,
            Diagnostics = diagnostics,
            Device = selected.Device
        };
    }

    public async Task<AndroidAdbCaptureResult> CaptureScreenrecordAsync(
        AndroidAdbScreenrecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var durationSeconds = request.DurationSeconds ?? DefaultScreenrecordDurationSeconds;
        if (durationSeconds is < MinimumScreenrecordDurationSeconds or > MaximumScreenrecordDurationSeconds)
        {
            return new AndroidAdbCaptureResult
            {
                Succeeded = false,
                Status = AndroidAdbStatus.InvalidRequest,
                Message = $"Android screenrecord duration must be between {MinimumScreenrecordDurationSeconds} and {MaximumScreenrecordDurationSeconds} seconds.",
                DurationSeconds = durationSeconds
            };
        }

        var diagnostics = await GetDiagnosticsAsync(request.AdbPath, cancellationToken);
        if (diagnostics.Status is AndroidAdbStatus.MissingAdb or AndroidAdbStatus.AdbFailed)
        {
            var failed = Failed(diagnostics, diagnostics.Message);
            failed.DurationSeconds = durationSeconds;
            return failed;
        }

        var selected = SelectDevice(diagnostics, request.DeviceSerial);
        if (!selected.Succeeded || selected.Device is null)
        {
            var failed = Failed(diagnostics, selected.Message);
            failed.DurationSeconds = durationSeconds;
            return failed;
        }

        var output = ResolveVideoOutputPath(request.OutputPath, selected.Device.Serial);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var remoteDirectory = string.IsNullOrWhiteSpace(request.RemoteDirectory)
            ? "/sdcard/Movies/GoatShot"
            : request.RemoteDirectory.Trim().TrimEnd('/');
        var remotePath = $"{remoteDirectory}/goatshot-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.mp4";

        AdbProcessResult mkdir;
        try
        {
            // Bound the handshake like the screenrecord step below; a sleeping device or
            // stalled adb daemon must not hang the capture indefinitely.
            using var mkdirTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            mkdirTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            mkdir = await _runner.RunTextAsync(
                diagnostics.AdbPath,
                DeviceArgs(selected.Device, ["shell", "mkdir", "-p", remoteDirectory]),
                mkdirTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedVideo(diagnostics, durationSeconds, remotePath, "ADB timed out preparing the Android screenrecord folder.");
        }

        if (mkdir.ExitCode != 0)
        {
            return FailedVideo(diagnostics, durationSeconds, remotePath, $"ADB could not prepare the Android screenrecord folder. {ShortOutput(mkdir.StandardError, mkdir.StandardOutputText)}");
        }

        var screenrecordArgs = DeviceArgs(
            selected.Device,
            [
                "shell",
                "screenrecord",
                "--time-limit",
                durationSeconds.ToString(CultureInfo.InvariantCulture),
                remotePath
            ]);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(durationSeconds + 30));
            var record = await _runner.RunTextAsync(diagnostics.AdbPath, screenrecordArgs, timeout.Token);
            if (record.ExitCode != 0)
            {
                await TryRemoveRemoteFileAsync(diagnostics.AdbPath, selected.Device, remotePath);
                return FailedVideo(diagnostics, durationSeconds, remotePath, $"ADB screenrecord failed with exit code {record.ExitCode}. {ShortOutput(record.StandardError, record.StandardOutputText)}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TryRemoveRemoteFileAsync(diagnostics.AdbPath, selected.Device, remotePath);
            return FailedVideo(diagnostics, durationSeconds, remotePath, "ADB screenrecord timed out before the bounded recording finished.");
        }

        AdbProcessResult pull;
        try
        {
            // Even a slow adb-over-Wi-Fi transfer finishes well within recording
            // duration plus this buffer; a stalled USB link must not hang forever.
            using var pullTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pullTimeout.CancelAfter(TimeSpan.FromSeconds(durationSeconds + 120));
            pull = await _runner.RunTextAsync(
                diagnostics.AdbPath,
                DeviceArgs(selected.Device, ["pull", remotePath, output]),
                pullTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TryRemoveRemoteFileAsync(diagnostics.AdbPath, selected.Device, remotePath);
            return FailedVideo(diagnostics, durationSeconds, remotePath, "ADB pull timed out transferring the Android screenrecord payload.");
        }

        await TryRemoveRemoteFileAsync(diagnostics.AdbPath, selected.Device, remotePath);
        if (pull.ExitCode != 0)
        {
            return FailedVideo(diagnostics, durationSeconds, remotePath, $"ADB pull failed with exit code {pull.ExitCode}. {ShortOutput(pull.StandardError, pull.StandardOutputText)}");
        }

        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            return FailedVideo(diagnostics, durationSeconds, remotePath, "ADB screenrecord did not create a local MP4 payload.");
        }

        CaptureItem? item = null;
        if (request.AddToWorkspace || string.IsNullOrWhiteSpace(request.OutputPath))
        {
            item = await _workspaceStore.AddImageFileAsync(
                output,
                CaptureKind.AndroidRecording,
                $"Android ADB screenrecord imported from {selected.Device.DisplayLabel}. Duration limit: {durationSeconds} seconds.",
                new CaptureSource
                {
                    ProcessName = "adb",
                    WindowTitle = selected.Device.DisplayLabel,
                    MonitorName = "Android device"
                });
        }

        return new AndroidAdbCaptureResult
        {
            Succeeded = true,
            Status = AndroidAdbStatus.Ready,
            Message = item is null
                ? $"Android screenrecord captured: {output}"
                : $"Android screenrecord captured and indexed: {item.FileName}",
            OutputPath = output,
            Item = item,
            Diagnostics = diagnostics,
            Device = selected.Device,
            DurationSeconds = durationSeconds,
            RemotePath = remotePath
        };
    }

    public async Task<AndroidAdbLivePreviewPlan> BuildLivePreviewPlanAsync(
        AndroidAdbLivePreviewPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var durationSeconds = request.DurationSeconds ?? DefaultLivePreviewDurationSeconds;
        var frameIntervalMs = request.FrameIntervalMs ?? DefaultLivePreviewFrameIntervalMs;
        var maxBytes = request.MaxBytes ?? DefaultLivePreviewMaxBytes;
        var strategy = request.Strategy is AndroidAdbLivePreviewStrategy.Auto
            ? AndroidAdbLivePreviewStrategy.ScreencapPolling
            : request.Strategy;

        var validationIssues = ValidateLivePreviewBounds(durationSeconds, frameIntervalMs, maxBytes, strategy);
        if (validationIssues.Count > 0)
        {
            return new AndroidAdbLivePreviewPlan
            {
                Succeeded = false,
                Status = AndroidAdbStatus.InvalidRequest,
                Strategy = strategy,
                RequestedStrategy = request.Strategy,
                DryRun = true,
                DurationSeconds = durationSeconds,
                FrameIntervalMs = frameIntervalMs,
                MaxBytes = maxBytes,
                Message = "Android live-preview plan request is invalid.",
                Issues = validationIssues,
                PrivacyNotes = DefaultLivePreviewPrivacyNotes()
            };
        }

        var diagnostics = await GetDiagnosticsAsync(request.AdbPath, cancellationToken);
        if (diagnostics.Status is AndroidAdbStatus.MissingAdb or AndroidAdbStatus.AdbFailed)
        {
            return FailedLivePreviewPlan(
                diagnostics,
                strategy,
                request.Strategy,
                durationSeconds,
                frameIntervalMs,
                maxBytes,
                diagnostics.Message);
        }

        var selected = SelectDevice(diagnostics, request.DeviceSerial);
        if (!selected.Succeeded || selected.Device is null)
        {
            return FailedLivePreviewPlan(
                diagnostics,
                strategy,
                request.Strategy,
                durationSeconds,
                frameIntervalMs,
                maxBytes,
                selected.Message);
        }

        return strategy switch
        {
            AndroidAdbLivePreviewStrategy.H264Stream => BuildH264PreviewPlan(
                diagnostics,
                selected.Device,
                request,
                durationSeconds,
                frameIntervalMs,
                maxBytes),
            _ => BuildScreencapPollingPreviewPlan(
                diagnostics,
                selected.Device,
                request,
                durationSeconds,
                frameIntervalMs,
                maxBytes)
        };
    }

    public async Task<AndroidAdbLivePreviewExecutionResult> ExecuteLivePreviewAsync(
        AndroidAdbLivePreviewExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var durationSeconds = request.DurationSeconds ?? DefaultLivePreviewDurationSeconds;
        var frameIntervalMs = request.FrameIntervalMs ?? DefaultLivePreviewFrameIntervalMs;
        var maxBytes = request.MaxBytes ?? DefaultLivePreviewMaxBytes;
        var strategy = request.Strategy is AndroidAdbLivePreviewStrategy.Auto
            ? AndroidAdbLivePreviewStrategy.ScreencapPolling
            : request.Strategy;

        var validationIssues = ValidateLivePreviewExecutionRequest(
            request,
            durationSeconds,
            frameIntervalMs,
            maxBytes,
            strategy);
        if (validationIssues.Count > 0)
        {
            return InvalidLivePreviewExecution(
                request.Strategy,
                strategy,
                durationSeconds,
                frameIntervalMs,
                maxBytes,
                request.SafeContentConfirmed,
                validationIssues);
        }

        var plan = await BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
        {
            AdbPath = request.AdbPath,
            DeviceSerial = request.DeviceSerial,
            Strategy = strategy,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            OutputPath = request.OutputDirectory
        }, cancellationToken);

        if (!plan.Succeeded || plan.Device is null || plan.Diagnostics is null)
        {
            return FailedLivePreviewExecution(plan, request.SafeContentConfirmed);
        }

        if (plan.Strategy == AndroidAdbLivePreviewStrategy.H264Stream)
        {
            return await ExecuteH264StreamPreviewAsync(
                request,
                plan,
                durationSeconds,
                frameIntervalMs,
                maxBytes,
                cancellationToken);
        }

        var outputDirectory = ResolvePreviewOutputDirectory(request.OutputDirectory, plan.Device.Serial);
        var outputDirectoryExisted = Directory.Exists(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var result = new AndroidAdbLivePreviewExecutionResult
        {
            Succeeded = false,
            Executed = true,
            Status = AndroidAdbStatus.Ready,
            Strategy = plan.Strategy,
            RequestedStrategy = request.Strategy,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = plan.TimeoutSeconds,
            OutputDirectory = outputDirectory,
            Device = plan.Device,
            Diagnostics = plan.Diagnostics,
            Plan = plan,
            SafeContentConfirmed = true,
            PrivacyNotes = plan.PrivacyNotes.ToList(),
            Warnings = plan.Warnings
                .Where(warning => !warning.Contains("dry-run", StringComparison.OrdinalIgnoreCase))
                .Append("Executed bounded Android preview polling with explicit safe-content confirmation.")
                .ToList()
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(plan.TimeoutSeconds));

            var command = plan.PlannedCommands.Single(command =>
                command.Name.Equals("adb-screencap-frame", StringComparison.OrdinalIgnoreCase));
            var targetFrames = Math.Max(1, plan.EstimatedMaxFrames ?? (int)Math.Ceiling(durationSeconds * 1000d / frameIntervalMs));

            for (var frameIndex = 0; frameIndex < targetFrames; frameIndex++)
            {
                timeout.Token.ThrowIfCancellationRequested();

                var capture = await _runner.RunBinaryAsync(plan.Diagnostics.AdbPath, command.Arguments, timeout.Token);
                if (capture.ExitCode != 0)
                {
                    return CleanupFailedLivePreviewExecution(
                        result,
                        outputDirectoryExisted,
                        $"ADB screencap preview frame failed with exit code {capture.ExitCode}. {ShortOutput(capture.StandardError, capture.StandardOutputText)}");
                }

                if (!LooksLikePng(capture.StandardOutput))
                {
                    return CleanupFailedLivePreviewExecution(
                        result,
                        outputDirectoryExisted,
                        "ADB screencap preview frame did not return a PNG payload.");
                }

                if (result.BytesCaptured + capture.StandardOutput.LongLength > maxBytes)
                {
                    return CleanupFailedLivePreviewExecution(
                        result,
                        outputDirectoryExisted,
                        $"Android preview byte cap exceeded before frame {frameIndex + 1}. Captured bytes would exceed {maxBytes}.");
                }

                var framePath = Path.Combine(outputDirectory, $"frame-{frameIndex + 1:0000}.png");
                await File.WriteAllBytesAsync(framePath, capture.StandardOutput, timeout.Token);
                result.FramePaths.Add(framePath);
                result.BytesCaptured += capture.StandardOutput.LongLength;

                if (frameIndex < targetFrames - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(frameIntervalMs), timeout.Token);
                }
            }

            if (request.GenerateContactSheet)
            {
                try
                {
                    var contactSheet = WriteLivePreviewContactSheet(
                        result,
                        request.ContactSheetPath,
                        request.ContactSheetMaxFrames);
                    result.ContactSheetPath = contactSheet?.Path;
                    result.ContactSheetFrameCount = contactSheet?.FrameCount ?? 0;
                    result.ContactSheetMaxFrames = contactSheet?.MaxFrames ?? 0;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Android preview contact-sheet generation failed: {ex.Message}");
                }
            }

            result.Succeeded = true;
            result.Message = $"Android preview executed with {result.FramePaths.Count} frame(s) from {plan.Device.DisplayLabel}.";
            result.ManifestPath = ResolveLivePreviewManifestPath(result.OutputDirectory);
            result.Summary = BuildLivePreviewExecutionSummary(result);
            await WriteLivePreviewExecutionManifestAsync(result, timeout.Token);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CleanupFailedLivePreviewExecution(
                result,
                outputDirectoryExisted,
                $"Android preview execution timed out after {plan.TimeoutSeconds} seconds.");
        }
    }

    private async Task<AndroidAdbLivePreviewExecutionResult> ExecuteH264StreamPreviewAsync(
        AndroidAdbLivePreviewExecutionRequest request,
        AndroidAdbLivePreviewPlan plan,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var outputDirectory = ResolvePreviewOutputDirectory(request.OutputDirectory, plan.Device!.Serial);
        var outputDirectoryExisted = Directory.Exists(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var result = new AndroidAdbLivePreviewExecutionResult
        {
            Succeeded = false,
            Executed = true,
            Status = AndroidAdbStatus.Ready,
            Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
            RequestedStrategy = request.Strategy,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = plan.TimeoutSeconds,
            OutputDirectory = outputDirectory,
            Device = plan.Device,
            Diagnostics = plan.Diagnostics,
            Plan = plan,
            SafeContentConfirmed = true,
            PrivacyNotes = plan.PrivacyNotes.ToList(),
            Warnings = plan.Warnings
                .Where(warning => !warning.Contains("dry-run", StringComparison.OrdinalIgnoreCase))
                .Append("Executed bounded Android H.264 stdout stream capture with explicit safe-content confirmation.")
                .ToList()
        };

        if (request.GenerateContactSheet || !string.IsNullOrWhiteSpace(request.ContactSheetPath))
        {
            result.Warnings.Add("Contact-sheet generation is not available for raw H.264 stream capture; remux or sample frames in a later review step.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(plan.TimeoutSeconds));

            var command = plan.PlannedCommands.Single(command =>
                command.Name.Equals("adb-h264-stream", StringComparison.OrdinalIgnoreCase));
            var capture = await _runner.RunBinaryLimitedAsync(
                plan.Diagnostics!.AdbPath,
                command.Arguments,
                maxBytes,
                timeout.Token);
            if (capture.ExitCode != 0)
            {
                return CleanupFailedLivePreviewExecution(
                    result,
                    outputDirectoryExisted,
                    $"ADB H.264 stream failed with exit code {capture.ExitCode}. {ShortOutput(capture.StandardError, capture.StandardOutputText)}");
            }

            if (capture.StandardOutput.Length == 0)
            {
                return CleanupFailedLivePreviewExecution(
                    result,
                    outputDirectoryExisted,
                    "ADB H.264 stream did not return any bytes.");
            }

            if (capture.StandardOutput.LongLength > maxBytes)
            {
                return CleanupFailedLivePreviewExecution(
                    result,
                    outputDirectoryExisted,
                    $"Android H.264 preview byte cap exceeded. Captured bytes exceeded {maxBytes}.");
            }

            if (!LooksLikeH264AnnexB(capture.StandardOutput))
            {
                return CleanupFailedLivePreviewExecution(
                    result,
                    outputDirectoryExisted,
                    "ADB H.264 stream did not look like Annex B H.264 data.");
            }

            var streamPath = Path.Combine(outputDirectory, "android-preview-stream.h264");
            await File.WriteAllBytesAsync(streamPath, capture.StandardOutput, timeout.Token);
            result.StreamPath = streamPath;
            result.BytesCaptured = capture.StandardOutput.LongLength;

            await TryRemuxH264StreamAsync(result, timeout.Token);

            result.Succeeded = true;
            result.Message = $"Android H.264 stream captured {result.BytesCaptured} byte(s) from {plan.Device.DisplayLabel}.";
            result.ManifestPath = ResolveLivePreviewManifestPath(result.OutputDirectory);
            result.Summary = BuildLivePreviewExecutionSummary(result);
            await WriteLivePreviewExecutionManifestAsync(result, timeout.Token);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CleanupFailedLivePreviewExecution(
                result,
                outputDirectoryExisted,
                $"Android H.264 stream execution timed out after {plan.TimeoutSeconds} seconds.");
        }
    }

    public static IReadOnlyList<AndroidAdbDevice> ParseDevices(string output)
    {
        var devices = new List<AndroidAdbDevice>();
        foreach (var rawLine in output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("* daemon", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(2))
            {
                var split = part.Split(':', 2);
                if (split.Length == 2 && !string.IsNullOrWhiteSpace(split[0]))
                {
                    details[split[0]] = split[1];
                }
            }

            devices.Add(new AndroidAdbDevice
            {
                Serial = parts[0],
                State = parts[1],
                Product = details.GetValueOrDefault("product") ?? string.Empty,
                Model = details.GetValueOrDefault("model") ?? string.Empty,
                Device = details.GetValueOrDefault("device") ?? string.Empty,
                TransportId = details.GetValueOrDefault("transport_id") ?? string.Empty
            });
        }

        return devices;
    }

    public static AndroidAdbDiagnostics BuildDiagnostics(
        string adbPath,
        AdbProcessResult devicesResult)
    {
        if (devicesResult.ExitCode != 0)
        {
            return new AndroidAdbDiagnostics
            {
                Status = AndroidAdbStatus.AdbFailed,
                AdbPath = adbPath,
                Message = $"adb devices failed with exit code {devicesResult.ExitCode}. {ShortOutput(devicesResult.StandardError, devicesResult.StandardOutputText)}",
                Devices = []
            };
        }

        return BuildDiagnostics(adbPath, ParseDevices(devicesResult.StandardOutputText));
    }

    public static AndroidAdbDiagnostics BuildDiagnostics(
        string adbPath,
        IReadOnlyList<AndroidAdbDevice> devices)
    {
        var ready = devices.Where(device => device.IsReady).ToList();
        var unauthorized = devices.Where(device => device.IsUnauthorized).ToList();
        var offline = devices.Where(device => device.IsOffline).ToList();
        var status = ready.Count switch
        {
            > 1 => AndroidAdbStatus.MultipleDevices,
            1 => AndroidAdbStatus.Ready,
            _ when unauthorized.Count > 0 => AndroidAdbStatus.UnauthorizedDevice,
            _ when offline.Count > 0 => AndroidAdbStatus.OfflineDevice,
            _ when devices.Count == 0 => AndroidAdbStatus.NoDevice,
            _ => AndroidAdbStatus.NoReadyDevice
        };

        var message = status switch
        {
            AndroidAdbStatus.Ready => $"One Android device is ready: {ready[0].DisplayLabel}.",
            AndroidAdbStatus.MultipleDevices => $"Multiple Android devices are ready ({ready.Count}); pass --device <serial>.",
            AndroidAdbStatus.UnauthorizedDevice => "Android device is connected but unauthorized. Unlock it and accept the USB debugging prompt.",
            AndroidAdbStatus.OfflineDevice => "Android device is connected but offline. Reconnect it or restart ADB.",
            AndroidAdbStatus.NoDevice => "No Android devices were reported by adb.",
            _ => "ADB reported devices, but none are ready for screencap."
        };

        return new AndroidAdbDiagnostics
        {
            Status = status,
            AdbPath = adbPath,
            Message = message,
            Devices = devices.ToList()
        };
    }

    private async Task<AdbProcessResult> QueryDevicesAsync(string adbPath, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_diagnosticsDevicesTimeoutSeconds));
        try
        {
            return await _runner.RunTextAsync(adbPath, ["devices", "-l"], timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AdbProcessResult(
                124,
                string.Empty,
                $"adb devices -l timed out after {_diagnosticsDevicesTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                []);
        }
    }

    private static (bool Succeeded, AndroidAdbDevice? Device, string Message) SelectDevice(
        AndroidAdbDiagnostics diagnostics,
        string? requestedSerial)
    {
        if (!string.IsNullOrWhiteSpace(requestedSerial))
        {
            var requested = diagnostics.Devices.FirstOrDefault(device =>
                device.Serial.Equals(requestedSerial, StringComparison.OrdinalIgnoreCase));
            if (requested is null)
            {
                return (false, null, $"Android device was not found: {requestedSerial}");
            }

            return requested.IsReady
                ? (true, requested, string.Empty)
                : (false, null, $"Android device is not ready: {requested.DisplayLabel} ({requested.State}).");
        }

        var ready = diagnostics.Devices.Where(device => device.IsReady).ToList();
        return ready.Count switch
        {
            1 => (true, ready[0], string.Empty),
            > 1 => (false, null, "Multiple Android devices are ready; pass --device <serial>."),
            _ => (false, null, diagnostics.Message)
        };
    }

    private string ResolveOutputPath(string? outputPath, string serial)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        return Path.Combine(
            _paths.ImagesRoot,
            $"android-{SafeSerial(serial)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png");
    }

    private string ResolveVideoOutputPath(string? outputPath, string serial)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        return Path.Combine(
            _paths.VideosRoot,
            $"android-{SafeSerial(serial)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.mp4");
    }

    private static AndroidAdbCaptureResult Failed(AndroidAdbDiagnostics diagnostics, string message)
    {
        return new AndroidAdbCaptureResult
        {
            Succeeded = false,
            Status = diagnostics.Status,
            Message = message,
            Diagnostics = diagnostics
        };
    }

    private static AndroidAdbCaptureResult FailedVideo(
        AndroidAdbDiagnostics diagnostics,
        int durationSeconds,
        string remotePath,
        string message)
    {
        var result = Failed(diagnostics, message);
        result.DurationSeconds = durationSeconds;
        result.RemotePath = remotePath;
        return result;
    }

    private async Task TryRemoveRemoteFileAsync(
        string adbPath,
        AndroidAdbDevice device,
        string remotePath)
    {
        try
        {
            await _runner.RunTextAsync(
                adbPath,
                DeviceArgs(device, ["shell", "rm", "-f", remotePath]),
                CancellationToken.None);
        }
        catch
        {
            // Best-effort cleanup only; preserve the original capture/pull result.
        }
    }

    private static IReadOnlyList<string> DeviceArgs(AndroidAdbDevice device, IReadOnlyList<string> command)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(device.Serial))
        {
            args.AddRange(["-s", device.Serial]);
        }

        args.AddRange(command);
        return args;
    }

    private static List<string> ValidateLivePreviewBounds(
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes,
        AndroidAdbLivePreviewStrategy strategy)
    {
        var issues = new List<string>();
        if (durationSeconds is < MinimumLivePreviewDurationSeconds or > MaximumLivePreviewDurationSeconds)
        {
            issues.Add($"duration must be between {MinimumLivePreviewDurationSeconds} and {MaximumLivePreviewDurationSeconds} seconds.");
        }

        if (frameIntervalMs is < MinimumLivePreviewFrameIntervalMs or > MaximumLivePreviewFrameIntervalMs)
        {
            issues.Add($"frame interval must be between {MinimumLivePreviewFrameIntervalMs} and {MaximumLivePreviewFrameIntervalMs} milliseconds.");
        }

        if (maxBytes is < MinimumLivePreviewMaxBytes or > MaximumLivePreviewMaxBytes)
        {
            issues.Add($"max bytes must be between {MinimumLivePreviewMaxBytes} and {MaximumLivePreviewMaxBytes}.");
        }

        if (!Enum.IsDefined(strategy))
        {
            issues.Add("strategy must be auto, screencap-polling, or h264-stream.");
        }

        return issues;
    }

    private static List<string> ValidateLivePreviewExecutionRequest(
        AndroidAdbLivePreviewExecutionRequest request,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes,
        AndroidAdbLivePreviewStrategy strategy)
    {
        var issues = new List<string>();
        if (!request.SafeContentConfirmed)
        {
            issues.Add("android-preview execution requires --safe-content-confirmed after staging safe device content.");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceSerial))
        {
            issues.Add("android-preview execution requires --device <serial> so the target device is explicit.");
        }

        if (!Enum.IsDefined(strategy) ||
            strategy is AndroidAdbLivePreviewStrategy.Auto)
        {
            issues.Add("strategy must be screencap-polling or h264-stream for execution.");
        }

        if (durationSeconds is < MinimumLivePreviewDurationSeconds or > MaximumLivePreviewExecutionDurationSeconds)
        {
            issues.Add($"execution duration must be between {MinimumLivePreviewDurationSeconds} and {MaximumLivePreviewExecutionDurationSeconds} seconds.");
        }

        if (frameIntervalMs is < MinimumLivePreviewFrameIntervalMs or > MaximumLivePreviewFrameIntervalMs)
        {
            issues.Add($"frame interval must be between {MinimumLivePreviewFrameIntervalMs} and {MaximumLivePreviewFrameIntervalMs} milliseconds.");
        }

        if (maxBytes is < MinimumLivePreviewMaxBytes or > MaximumLivePreviewExecutionMaxBytes)
        {
            issues.Add($"execution max bytes must be between {MinimumLivePreviewMaxBytes} and {MaximumLivePreviewExecutionMaxBytes}.");
        }

        return issues;
    }

    private static AndroidAdbLivePreviewExecutionResult InvalidLivePreviewExecution(
        AndroidAdbLivePreviewStrategy requestedStrategy,
        AndroidAdbLivePreviewStrategy strategy,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes,
        bool safeContentConfirmed,
        List<string> issues)
    {
        var plan = new AndroidAdbLivePreviewPlan
        {
            Succeeded = false,
            Status = AndroidAdbStatus.InvalidRequest,
            RequestedStrategy = requestedStrategy,
            Strategy = strategy,
            DryRun = true,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = durationSeconds + 10,
            Message = "Android live-preview execution request is invalid.",
            Issues = issues.ToList(),
            PrivacyNotes = DefaultLivePreviewPrivacyNotes()
        };

        var result = new AndroidAdbLivePreviewExecutionResult
        {
            Succeeded = false,
            Executed = false,
            Status = AndroidAdbStatus.InvalidRequest,
            Message = plan.Message,
            RequestedStrategy = requestedStrategy,
            Strategy = strategy,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = plan.TimeoutSeconds,
            SafeContentConfirmed = safeContentConfirmed,
            Plan = plan,
            Issues = issues.ToList(),
            PrivacyNotes = plan.PrivacyNotes.ToList()
        };
        result.Summary = BuildLivePreviewExecutionSummary(result);
        return result;
    }

    private static AndroidAdbLivePreviewExecutionResult FailedLivePreviewExecution(
        AndroidAdbLivePreviewPlan plan,
        bool safeContentConfirmed = false)
    {
        var result = new AndroidAdbLivePreviewExecutionResult
        {
            Succeeded = false,
            Executed = false,
            Status = plan.Status,
            Message = plan.Message,
            RequestedStrategy = plan.RequestedStrategy,
            Strategy = plan.Strategy,
            DurationSeconds = plan.DurationSeconds,
            FrameIntervalMs = plan.FrameIntervalMs,
            MaxBytes = plan.MaxBytes,
            TimeoutSeconds = plan.TimeoutSeconds,
            SafeContentConfirmed = safeContentConfirmed,
            Plan = plan,
            Device = plan.Device,
            Diagnostics = plan.Diagnostics,
            Issues = plan.Issues.Count > 0 ? plan.Issues.ToList() : [plan.Message],
            PrivacyNotes = plan.PrivacyNotes.ToList(),
            Warnings = plan.Warnings.ToList()
        };
        result.Summary = BuildLivePreviewExecutionSummary(result);
        return result;
    }

    private AndroidAdbLivePreviewExecutionResult CleanupFailedLivePreviewExecution(
        AndroidAdbLivePreviewExecutionResult result,
        bool outputDirectoryExisted,
        string message)
    {
        result.Succeeded = false;
        result.Message = message;
        result.Issues.Add(message);
        result.CleanupPerformed = CleanupLivePreviewExecutionOutput(
            result.OutputDirectory,
            result.ManifestPath,
            result.FramePaths,
            result.StreamPath,
            result.RemuxedVideoPath,
            outputDirectoryExisted);
        result.FramePaths.Clear();
        result.StreamPath = null;
        result.RemuxedVideoPath = null;
        result.BytesCaptured = 0;
        result.Summary = BuildLivePreviewExecutionSummary(result);
        return result;
    }

    private static bool CleanupLivePreviewExecutionOutput(
        string? outputDirectory,
        string? manifestPath,
        IReadOnlyList<string> framePaths,
        string? streamPath,
        string? remuxedVideoPath,
        bool outputDirectoryExisted)
    {
        var attempted = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                attempted = true;
                File.Delete(manifestPath);
            }

            if (outputDirectoryExisted)
            {
                foreach (var framePath in framePaths)
                {
                    if (File.Exists(framePath))
                    {
                        attempted = true;
                        File.Delete(framePath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(streamPath) && File.Exists(streamPath))
                {
                    attempted = true;
                    File.Delete(streamPath);
                }

                if (!string.IsNullOrWhiteSpace(remuxedVideoPath) && File.Exists(remuxedVideoPath))
                {
                    attempted = true;
                    File.Delete(remuxedVideoPath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
            {
                attempted = true;
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
        catch
        {
            return attempted;
        }

        return attempted;
    }

    private static string ResolveLivePreviewManifestPath(string? outputDirectory)
    {
        return Path.Combine(outputDirectory ?? string.Empty, "goatshot-android-preview-manifest.json");
    }

    private async Task<string> WriteLivePreviewExecutionManifestAsync(
        AndroidAdbLivePreviewExecutionResult result,
        CancellationToken cancellationToken)
    {
        var outputDirectory = result.OutputDirectory ?? _paths.TempRoot;
        var manifestPath = string.IsNullOrWhiteSpace(result.ManifestPath)
            ? Path.Combine(outputDirectory, "goatshot-android-preview-manifest.json")
            : result.ManifestPath;
        var manifest = new AndroidAdbLivePreviewExecutionManifest
        {
            SchemaVersion = 1,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Strategy = result.Strategy.ToString(),
            DeviceSerial = result.Device?.Serial ?? string.Empty,
            DeviceLabel = result.Device?.DisplayLabel ?? string.Empty,
            DurationSeconds = result.DurationSeconds,
            FrameIntervalMs = result.FrameIntervalMs,
            MaxBytes = result.MaxBytes,
            TimeoutSeconds = result.TimeoutSeconds,
            FrameCount = result.FramePaths.Count,
            BytesCaptured = result.BytesCaptured,
            StreamFileName = string.IsNullOrWhiteSpace(result.StreamPath)
                ? string.Empty
                : Path.GetFileName(result.StreamPath),
            StreamBytes = result.StreamPath is not null && File.Exists(result.StreamPath)
                ? new FileInfo(result.StreamPath).Length
                : 0,
            RemuxedVideoFileName = string.IsNullOrWhiteSpace(result.RemuxedVideoPath)
                ? string.Empty
                : Path.GetFileName(result.RemuxedVideoPath),
            RemuxAttempted = result.RemuxAttempted,
            RemuxSucceeded = result.RemuxSucceeded,
            RemuxMessage = result.RemuxMessage,
            ContactSheetFileName = string.IsNullOrWhiteSpace(result.ContactSheetPath)
                ? string.Empty
                : Path.GetFileName(result.ContactSheetPath),
            ContactSheetFrameCount = result.ContactSheetFrameCount,
            ContactSheetMaxFrames = result.ContactSheetMaxFrames,
            Summary = result.Summary,
            PrivacyNotes = result.PrivacyNotes.ToList(),
            Frames = result.FramePaths
                .Select((path, index) => new AndroidAdbLivePreviewFrameManifest
                {
                    Index = index + 1,
                    FileName = Path.GetFileName(path),
                    Bytes = new FileInfo(path).Length
                })
                .ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            cancellationToken);
        return manifestPath;
    }

    private static AndroidAdbLivePreviewContactSheet? WriteLivePreviewContactSheet(
        AndroidAdbLivePreviewExecutionResult result,
        string? requestedPath,
        int? requestedMaxFrames)
    {
        if (result.FramePaths.Count == 0 || string.IsNullOrWhiteSpace(result.OutputDirectory))
        {
            return null;
        }

        var maxFrames = Math.Clamp(
            requestedMaxFrames ?? DefaultLivePreviewContactSheetMaxFrames,
            MinimumLivePreviewContactSheetMaxFrames,
            MaximumLivePreviewContactSheetMaxFrames);
        var frames = result.FramePaths
            .Where(File.Exists)
            .Take(maxFrames)
            .ToList();
        if (frames.Count == 0)
        {
            return null;
        }

        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(result.OutputDirectory, "android-preview-contact-sheet.png")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const int cellWidth = 240;
        const int cellHeight = 180;
        const int labelHeight = 24;
        const int gap = 8;
        var columns = Math.Min(4, frames.Count);
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);
        var width = columns * cellWidth + (columns + 1) * gap;
        var height = rows * (cellHeight + labelHeight) + (rows + 1) * gap;

        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.FromArgb(18, 24, 30));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var borderPen = new Pen(Color.FromArgb(88, 104, 118), 1);
        using var textBrush = new SolidBrush(Color.FromArgb(226, 235, 241));
        using var labelBrush = new SolidBrush(Color.FromArgb(42, 52, 62));
        using var font = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Point);

        for (var index = 0; index < frames.Count; index++)
        {
            using var image = Image.FromFile(frames[index]);
            var column = index % columns;
            var row = index / columns;
            var x = gap + column * (cellWidth + gap);
            var y = gap + row * (cellHeight + labelHeight + gap);
            var imageBounds = FitImage(image.Width, image.Height, new Rectangle(x, y, cellWidth, cellHeight));
            graphics.DrawImage(image, imageBounds);
            graphics.DrawRectangle(borderPen, x, y, cellWidth, cellHeight);
            graphics.FillRectangle(labelBrush, x, y + cellHeight, cellWidth, labelHeight);
            graphics.DrawString($"#{index + 1:00} {Path.GetFileName(frames[index])}", font, textBrush, x + 8, y + cellHeight + 4);
        }

        sheet.Save(path, ImageFormat.Png);
        return new AndroidAdbLivePreviewContactSheet(path, frames.Count, maxFrames);
    }

    private static Rectangle FitImage(int sourceWidth, int sourceHeight, Rectangle target)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return target;
        }

        var scale = Math.Min(target.Width / (double)sourceWidth, target.Height / (double)sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new Rectangle(
            target.X + (target.Width - width) / 2,
            target.Y + (target.Height - height) / 2,
            width,
            height);
    }

    private static AndroidAdbLivePreviewExecutionSummary BuildLivePreviewExecutionSummary(
        AndroidAdbLivePreviewExecutionResult result)
    {
        var issueText = string.Join(" ", result.Issues.Append(result.Message));
        var timedOut = issueText.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            issueText.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        var byteCapExceeded = issueText.Contains("byte cap", StringComparison.OrdinalIgnoreCase);
        var remuxStatus = result.RemuxAttempted
            ? result.RemuxSucceeded ? "remuxed" : "failed"
            : !string.IsNullOrWhiteSpace(result.StreamPath) ? "skipped" : "not-started";
        return new AndroidAdbLivePreviewExecutionSummary
        {
            Mode = result.Strategy == AndroidAdbLivePreviewStrategy.H264Stream
                ? "bounded-h264-stream"
                : "bounded-screencap-polling",
            Status = result.Status.ToString(),
            Succeeded = result.Succeeded,
            Executed = result.Executed,
            Strategy = result.Strategy.ToString(),
            RequestedStrategy = result.RequestedStrategy.ToString(),
            DeviceSerial = result.Device?.Serial ?? string.Empty,
            DeviceLabel = result.Device?.DisplayLabel ?? string.Empty,
            DurationSeconds = result.DurationSeconds,
            FrameIntervalMs = result.FrameIntervalMs,
            TimeoutSeconds = result.TimeoutSeconds,
            FrameCount = result.FramePaths.Count,
            BytesCaptured = result.BytesCaptured,
            MaxBytes = result.MaxBytes,
            TimeoutStatus = timedOut
                ? "timed-out"
                : !result.Executed
                    ? "not-started"
                    : result.Succeeded
                        ? "completed-within-timeout"
                        : "stopped-before-timeout",
            ByteCapStatus = byteCapExceeded
                ? "byte-cap-exceeded"
                : !result.Executed
                    ? "not-started"
                    : result.BytesCaptured <= result.MaxBytes
                        ? "within-byte-cap"
                        : "over-byte-cap",
            CleanupStatus = result.CleanupPerformed
                ? "cleanup-performed"
                : result.Succeeded
                    ? "not-needed"
                    : !result.Executed
                        ? "not-started"
                        : "not-performed",
            SafeContentConfirmed = result.SafeContentConfirmed,
            OutputDirectory = result.OutputDirectory ?? string.Empty,
            ManifestPath = result.ManifestPath ?? string.Empty,
            StreamPath = result.StreamPath ?? string.Empty,
            StreamBytes = result.StreamPath is not null && File.Exists(result.StreamPath)
                ? new FileInfo(result.StreamPath).Length
                : 0,
            RemuxedVideoPath = result.RemuxedVideoPath ?? string.Empty,
            RemuxAttempted = result.RemuxAttempted,
            RemuxSucceeded = result.RemuxSucceeded,
            RemuxStatus = remuxStatus,
            ContactSheetPath = result.ContactSheetPath ?? string.Empty,
            ContactSheetFrameCount = result.ContactSheetFrameCount,
            ContactSheetMaxFrames = result.ContactSheetMaxFrames
        };
    }

    private static async Task TryRemuxH264StreamAsync(
        AndroidAdbLivePreviewExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.StreamPath) ||
            string.IsNullOrWhiteSpace(result.OutputDirectory) ||
            !File.Exists(result.StreamPath))
        {
            result.RemuxMessage = "No raw H.264 stream was available to remux.";
            return;
        }

        var ffmpeg = ResolveFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            result.RemuxMessage = "FFmpeg was not found; raw H.264 stream was kept without MP4 remux.";
            return;
        }

        result.RemuxAttempted = true;
        var remuxPath = Path.Combine(result.OutputDirectory, "android-preview-stream.mp4");
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-y",
            "-f",
            "h264",
            "-i",
            result.StreamPath,
            "-c",
            "copy",
            remuxPath
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                result.RemuxMessage = "FFmpeg remux process did not start.";
                result.Warnings.Add(result.RemuxMessage);
                return;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0 && File.Exists(remuxPath))
            {
                result.RemuxSucceeded = true;
                result.RemuxedVideoPath = remuxPath;
                result.RemuxMessage = "Raw H.264 stream was remuxed to MP4 with FFmpeg.";
                return;
            }

            result.RemuxMessage = $"FFmpeg remux failed with exit code {process.ExitCode}. {ShortOutput(stderr, string.Empty)}";
            result.Warnings.Add(result.RemuxMessage);
            if (File.Exists(remuxPath))
            {
                File.Delete(remuxPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.RemuxMessage = $"FFmpeg remux failed: {ex.Message}";
            result.Warnings.Add(result.RemuxMessage);
            if (File.Exists(remuxPath))
            {
                File.Delete(remuxPath);
            }
        }
    }

    private static string? ResolveFfmpegPath()
    {
        var environmentPath = Environment.GetEnvironmentVariable("GOATSHOT_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(environmentPath);
            if (File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(directory.Trim(), "ffmpeg");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private string ResolvePreviewOutputDirectory(string? outputDirectory, string serial)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        }

        return Path.Combine(
            _paths.TempRoot,
            "android-preview",
            $"{SafeSerial(serial)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
    }

    private static AndroidAdbLivePreviewPlan FailedLivePreviewPlan(
        AndroidAdbDiagnostics diagnostics,
        AndroidAdbLivePreviewStrategy strategy,
        AndroidAdbLivePreviewStrategy requestedStrategy,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes,
        string message)
    {
        return new AndroidAdbLivePreviewPlan
        {
            Succeeded = false,
            Status = diagnostics.Status,
            Strategy = strategy,
            RequestedStrategy = requestedStrategy,
            DryRun = true,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            Message = message,
            Diagnostics = diagnostics,
            Issues = [message],
            PrivacyNotes = DefaultLivePreviewPrivacyNotes()
        };
    }

    private static AndroidAdbLivePreviewPlan BuildScreencapPollingPreviewPlan(
        AndroidAdbDiagnostics diagnostics,
        AndroidAdbDevice device,
        AndroidAdbLivePreviewPlanRequest request,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes)
    {
        var estimatedFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * 1000d / frameIntervalMs));
        return new AndroidAdbLivePreviewPlan
        {
            Succeeded = true,
            Status = AndroidAdbStatus.Ready,
            Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
            RequestedStrategy = request.Strategy,
            DryRun = true,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = durationSeconds + 10,
            EstimatedMaxFrames = estimatedFrames,
            Message = $"Android live-preview dry-run plan is ready for {device.DisplayLabel} using repeated screencap polling.",
            Diagnostics = diagnostics,
            Device = device,
            PlannedCommands =
            [
                new AndroidAdbPlannedCommand
                {
                    Name = "adb-screencap-frame",
                    Tool = diagnostics.AdbPath,
                    Arguments = DeviceArgs(device, ["exec-out", "screencap", "-p"]).ToList(),
                    Description = "Repeat at the planned frame interval until duration, byte cap, timeout, cancellation, or device disconnect.",
                    CapturesDeviceContent = true
                }
            ],
            PrivacyNotes = DefaultLivePreviewPrivacyNotes(),
            Warnings =
            [
                "This is a dry-run plan; GoatShot does not start live Android preview capture in this tranche.",
                "Repeated screencap polling is safer to prototype than raw H.264 streaming, but it is lower FPS and can expose notifications or private app content."
            ],
            CleanupPlan = "No remote file is created. Stop polling immediately on timeout, cancellation, ADB non-zero exit, empty/invalid PNG payload, or device disconnect.",
            DisconnectBehavior = "If ADB reports a disconnected/offline device or a frame command exits non-zero, stop the preview loop and keep the last safe diagnostic message.",
            OutputHint = string.IsNullOrWhiteSpace(request.OutputPath)
                ? "Preview frames would stay transient unless a later explicit output/import mode is added."
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath))
        };
    }

    private static AndroidAdbLivePreviewPlan BuildH264PreviewPlan(
        AndroidAdbDiagnostics diagnostics,
        AndroidAdbDevice device,
        AndroidAdbLivePreviewPlanRequest request,
        int durationSeconds,
        int frameIntervalMs,
        long maxBytes)
    {
        var outputHint = string.IsNullOrWhiteSpace(request.OutputPath)
            ? "android-preview.mp4"
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        return new AndroidAdbLivePreviewPlan
        {
            Succeeded = true,
            Status = AndroidAdbStatus.Ready,
            Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
            RequestedStrategy = request.Strategy,
            DryRun = true,
            DurationSeconds = durationSeconds,
            FrameIntervalMs = frameIntervalMs,
            MaxBytes = maxBytes,
            TimeoutSeconds = durationSeconds + 15,
            EstimatedMaxFrames = null,
            Message = $"Android live-preview dry-run plan is ready for {device.DisplayLabel} using experimental H.264 stdout streaming.",
            Diagnostics = diagnostics,
            Device = device,
            PlannedCommands =
            [
                new AndroidAdbPlannedCommand
                {
                    Name = "adb-h264-stream",
                    Tool = diagnostics.AdbPath,
                    Arguments = DeviceArgs(
                        device,
                        [
                            "shell",
                            "screenrecord",
                            "--output-format=h264",
                            "--time-limit",
                            durationSeconds.ToString(CultureInfo.InvariantCulture),
                            "-"
                        ]).ToList(),
                    Description = "Read bounded H.264 bytes from stdout until duration, byte cap, timeout, cancellation, or device disconnect.",
                    CapturesDeviceContent = true
                },
                new AndroidAdbPlannedCommand
                {
                    Name = "ffmpeg-remux-preview",
                    Tool = "ffmpeg",
                    Arguments =
                    [
                        "-f",
                        "h264",
                        "-i",
                        "pipe:0",
                        "-c",
                        "copy",
                        outputHint
                    ],
                    Description = "Optional remux step if a later implementation writes the streamed H.264 bytes to MP4.",
                    CapturesDeviceContent = false
                }
            ],
            PrivacyNotes = DefaultLivePreviewPrivacyNotes(),
            Warnings =
            [
                "This is a dry-run plan; GoatShot does not start live Android H.264 streaming in this tranche.",
                "Android screenrecord stdout streaming is device/OS dependent and may fail even when bounded MP4 pull/import works.",
                "The H.264 stream needs parsing/remux validation before it can be treated as production preview."
            ],
            CleanupPlan = "No remote temp file is expected when streaming to stdout. Kill the ADB process on timeout/cancellation and discard partial bytes that do not pass H.264/remux validation.",
            DisconnectBehavior = "If stdout stops early, ADB exits non-zero, bytes exceed the cap, or FFmpeg/remux validation fails, stop and report the partial-stream failure without importing media.",
            OutputHint = outputHint
        };
    }

    private static List<string> DefaultLivePreviewPrivacyNotes()
    {
        return
        [
            "Use only staged safe device content before any live Android capture or preview.",
            "Android screens can expose notifications, account names, chats, photos, passwords, and personal app data.",
            "Do not run live preview against a personal or production device screen unless sensitive content is hidden first."
        ];
    }

    private static bool LooksLikePng(byte[] bytes)
    {
        return bytes.Length >= PngSignature.Length &&
            PngSignature.SequenceEqual(bytes.Take(PngSignature.Length));
    }

    private static bool LooksLikeH264AnnexB(byte[] bytes)
    {
        var scanLength = Math.Min(bytes.Length, 4096);
        for (var index = 0; index <= scanLength - 3; index++)
        {
            if (bytes[index] == 0x00 &&
                bytes[index + 1] == 0x00 &&
                bytes[index + 2] == 0x01)
            {
                return true;
            }

            if (index <= scanLength - 4 &&
                bytes[index] == 0x00 &&
                bytes[index + 1] == 0x00 &&
                bytes[index + 2] == 0x00 &&
                bytes[index + 3] == 0x01)
            {
                return true;
            }
        }

        return false;
    }

    private static string SafeSerial(string serial)
    {
        var safeSerial = new string(serial.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safeSerial) ? "device" : safeSerial;
    }

    private sealed record AndroidAdbLivePreviewContactSheet(
        string Path,
        int FrameCount,
        int MaxFrames);

    private static string? ResolveAdbPath(string? adbPath)
    {
        if (!string.IsNullOrWhiteSpace(adbPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(adbPath);
            return File.Exists(configured)
                ? Path.GetFullPath(configured)
                : null;
        }

        var environmentPath = Environment.GetEnvironmentVariable("GOATSHOT_ADB_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var configured = Environment.ExpandEnvironmentVariables(environmentPath);
            if (File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }

            return null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "adb.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(directory.Trim(), "adb");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string ShortOutput(string stderr, string stdout)
    {
        var output = string.Join(
            " ",
            new[] { stderr, stdout }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.ReplaceLineEndings(" ").Trim()));
        return string.IsNullOrWhiteSpace(output)
            ? "No ADB output was returned."
            : output.Length <= 500
                ? output
                : output[..500] + "...";
    }
}

public sealed class AndroidAdbCaptureRequest
{
    public string? AdbPath { get; set; }
    public string? DeviceSerial { get; set; }
    public string? OutputPath { get; set; }
    public bool AddToWorkspace { get; set; }
}

public sealed class AndroidAdbScreenrecordRequest
{
    public string? AdbPath { get; set; }
    public string? DeviceSerial { get; set; }
    public string? OutputPath { get; set; }
    public bool AddToWorkspace { get; set; }
    public int? DurationSeconds { get; set; }
    public string? RemoteDirectory { get; set; }
}

public sealed class AndroidAdbLivePreviewPlanRequest
{
    public string? AdbPath { get; set; }
    public string? DeviceSerial { get; set; }
    public AndroidAdbLivePreviewStrategy Strategy { get; set; } = AndroidAdbLivePreviewStrategy.Auto;
    public int? DurationSeconds { get; set; }
    public int? FrameIntervalMs { get; set; }
    public long? MaxBytes { get; set; }
    public string? OutputPath { get; set; }
}

public sealed class AndroidAdbLivePreviewExecutionRequest
{
    public string? AdbPath { get; set; }
    public string? DeviceSerial { get; set; }
    public AndroidAdbLivePreviewStrategy Strategy { get; set; } = AndroidAdbLivePreviewStrategy.Auto;
    public int? DurationSeconds { get; set; }
    public int? FrameIntervalMs { get; set; }
    public long? MaxBytes { get; set; }
    public string? OutputDirectory { get; set; }
    public bool SafeContentConfirmed { get; set; }
    public bool GenerateContactSheet { get; set; }
    public string? ContactSheetPath { get; set; }
    public int? ContactSheetMaxFrames { get; set; }
}

public sealed class AndroidAdbCaptureResult
{
    public bool Succeeded { get; set; }
    public AndroidAdbStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public CaptureItem? Item { get; set; }
    public AndroidAdbDevice? Device { get; set; }
    public AndroidAdbDiagnostics? Diagnostics { get; set; }
    public int? DurationSeconds { get; set; }
    public string? RemotePath { get; set; }
}

public sealed class AndroidAdbLivePreviewPlan
{
    public bool Succeeded { get; set; }
    public AndroidAdbStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public AndroidAdbLivePreviewStrategy RequestedStrategy { get; set; }
    public AndroidAdbLivePreviewStrategy Strategy { get; set; }
    public bool DryRun { get; set; } = true;
    public int DurationSeconds { get; set; }
    public int FrameIntervalMs { get; set; }
    public long MaxBytes { get; set; }
    public int TimeoutSeconds { get; set; }
    public int? EstimatedMaxFrames { get; set; }
    public string OutputHint { get; set; } = string.Empty;
    public AndroidAdbDevice? Device { get; set; }
    public AndroidAdbDiagnostics? Diagnostics { get; set; }
    public List<AndroidAdbPlannedCommand> PlannedCommands { get; set; } = new();
    public List<string> PrivacyNotes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public string CleanupPlan { get; set; } = string.Empty;
    public string DisconnectBehavior { get; set; } = string.Empty;
}

public sealed class AndroidAdbLivePreviewExecutionResult
{
    public bool Succeeded { get; set; }
    public bool Executed { get; set; }
    public AndroidAdbStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public AndroidAdbLivePreviewStrategy RequestedStrategy { get; set; }
    public AndroidAdbLivePreviewStrategy Strategy { get; set; }
    public int DurationSeconds { get; set; }
    public int FrameIntervalMs { get; set; }
    public long MaxBytes { get; set; }
    public int TimeoutSeconds { get; set; }
    public long BytesCaptured { get; set; }
    public bool SafeContentConfirmed { get; set; }
    public bool CleanupPerformed { get; set; }
    public string? OutputDirectory { get; set; }
    public string? ManifestPath { get; set; }
    public string? StreamPath { get; set; }
    public string? RemuxedVideoPath { get; set; }
    public bool RemuxAttempted { get; set; }
    public bool RemuxSucceeded { get; set; }
    public string RemuxMessage { get; set; } = string.Empty;
    public string? ContactSheetPath { get; set; }
    public int ContactSheetFrameCount { get; set; }
    public int ContactSheetMaxFrames { get; set; }
    public AndroidAdbLivePreviewExecutionSummary? Summary { get; set; }
    public AndroidAdbDevice? Device { get; set; }
    public AndroidAdbDiagnostics? Diagnostics { get; set; }
    public AndroidAdbLivePreviewPlan? Plan { get; set; }
    public List<string> FramePaths { get; set; } = new();
    public List<string> PrivacyNotes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public sealed class AndroidAdbLivePreviewExecutionManifest
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string DeviceSerial { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int FrameIntervalMs { get; set; }
    public long MaxBytes { get; set; }
    public int TimeoutSeconds { get; set; }
    public int FrameCount { get; set; }
    public long BytesCaptured { get; set; }
    public string StreamFileName { get; set; } = string.Empty;
    public long StreamBytes { get; set; }
    public string RemuxedVideoFileName { get; set; } = string.Empty;
    public bool RemuxAttempted { get; set; }
    public bool RemuxSucceeded { get; set; }
    public string RemuxMessage { get; set; } = string.Empty;
    public string ContactSheetFileName { get; set; } = string.Empty;
    public int ContactSheetFrameCount { get; set; }
    public int ContactSheetMaxFrames { get; set; }
    public AndroidAdbLivePreviewExecutionSummary? Summary { get; set; }
    public List<AndroidAdbLivePreviewFrameManifest> Frames { get; set; } = new();
    public List<string> PrivacyNotes { get; set; } = new();
}

public sealed class AndroidAdbLivePreviewExecutionSummary
{
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public bool Executed { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string RequestedStrategy { get; set; } = string.Empty;
    public string DeviceSerial { get; set; } = string.Empty;
    public string DeviceLabel { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int FrameIntervalMs { get; set; }
    public int TimeoutSeconds { get; set; }
    public int FrameCount { get; set; }
    public long BytesCaptured { get; set; }
    public long MaxBytes { get; set; }
    public string TimeoutStatus { get; set; } = string.Empty;
    public string ByteCapStatus { get; set; } = string.Empty;
    public string CleanupStatus { get; set; } = string.Empty;
    public bool SafeContentConfirmed { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string StreamPath { get; set; } = string.Empty;
    public long StreamBytes { get; set; }
    public string RemuxedVideoPath { get; set; } = string.Empty;
    public bool RemuxAttempted { get; set; }
    public bool RemuxSucceeded { get; set; }
    public string RemuxStatus { get; set; } = string.Empty;
    public string ContactSheetPath { get; set; } = string.Empty;
    public int ContactSheetFrameCount { get; set; }
    public int ContactSheetMaxFrames { get; set; }
}

public sealed class AndroidAdbLivePreviewFrameManifest
{
    public int Index { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Bytes { get; set; }
}

public sealed class AndroidAdbPlannedCommand
{
    public string Name { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public bool CapturesDeviceContent { get; set; }
}

public sealed class AndroidAdbDiagnostics
{
    public AndroidAdbStatus Status { get; set; }
    public string AdbPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<AndroidAdbDevice> Devices { get; set; } = new();
    public bool Ready => Status == AndroidAdbStatus.Ready;
}

public sealed class AndroidAdbDevice
{
    public string Serial { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string TransportId { get; set; } = string.Empty;
    public bool IsReady => State.Equals("device", StringComparison.OrdinalIgnoreCase);
    public bool IsUnauthorized => State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase);
    public bool IsOffline => State.Equals("offline", StringComparison.OrdinalIgnoreCase);
    public string DisplayLabel
    {
        get
        {
            var details = new[]
            {
                string.IsNullOrWhiteSpace(Model) ? null : Model.Replace('_', ' '),
                string.IsNullOrWhiteSpace(Product) ? null : Product,
                string.IsNullOrWhiteSpace(Device) ? null : Device
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            var detailText = string.Join(", ", details);
            return string.IsNullOrWhiteSpace(detailText)
                ? Serial
                : $"{Serial} ({detailText})";
        }
    }
}

public enum AndroidAdbStatus
{
    InvalidRequest,
    MissingAdb,
    AdbFailed,
    NoDevice,
    UnauthorizedDevice,
    OfflineDevice,
    NoReadyDevice,
    MultipleDevices,
    Ready
}

public enum AndroidAdbLivePreviewStrategy
{
    Auto,
    ScreencapPolling,
    H264Stream
}

public interface IAdbProcessRunner
{
    Task<AdbProcessResult> RunTextAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task<AdbProcessResult> RunBinaryAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task<AdbProcessResult> RunBinaryLimitedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        long maxBytes,
        CancellationToken cancellationToken);
}

public sealed record AdbProcessResult(
    int ExitCode,
    string StandardOutputText,
    string StandardError,
    byte[] StandardOutput);

public sealed class AdbProcessRunner : IAdbProcessRunner
{
    public async Task<AdbProcessResult> RunTextAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await RunAsync(fileName, arguments, binaryOutput: false, cancellationToken);
    }

    public async Task<AdbProcessResult> RunBinaryAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await RunAsync(fileName, arguments, binaryOutput: true, cancellationToken);
    }

    public async Task<AdbProcessResult> RunBinaryLimitedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        return await RunAsync(fileName, arguments, binaryOutput: true, cancellationToken, maxBytes);
    }

    private static async Task<AdbProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        bool binaryOutput,
        CancellationToken cancellationToken,
        long? maxBytes = null)
    {
        var adbServerPort = FindAvailableTcpPort().ToString(CultureInfo.InvariantCulture);
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["ADB_SERVER_PORT"] = adbServerPort;
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return new AdbProcessResult(1, string.Empty, "ADB process did not start.", []);
        }

        try
        {
            if (binaryOutput)
            {
                await using var output = new MemoryStream();
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                if (maxBytes is null)
                {
                    var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
                    await Task.WhenAll(stdoutTask, process.WaitForExitAsync(cancellationToken));
                }
                else
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }

                        if (output.Length + read > maxBytes.Value)
                        {
                            try
                            {
                                process.Kill(entireProcessTree: true);
                            }
                            catch
                            {
                                // Preserve the byte-cap failure result even if kill races process exit.
                            }

                            var capStderr = await stderrTask;
                            return new AdbProcessResult(
                                1,
                                string.Empty,
                                $"ADB stdout exceeded byte cap of {maxBytes.Value} bytes. {capStderr}",
                                output.ToArray());
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    await process.WaitForExitAsync(cancellationToken);
                }

                var stderr = await stderrTask;
                return new AdbProcessResult(process.ExitCode, string.Empty, stderr, output.ToArray());
            }
            else
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await Task.WhenAll(stdoutTask, process.WaitForExitAsync(cancellationToken));
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                return new AdbProcessResult(process.ExitCode, stdout, stderr, System.Text.Encoding.UTF8.GetBytes(stdout));
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            StopIsolatedAdbServer(fileName, adbServerPort);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation already determines the result; cleanup is best-effort.
        }
    }

    private static int FindAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void StopIsolatedAdbServer(string adbPath, string adbServerPort)
    {
        try
        {
            var start = new ProcessStartInfo(adbPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            start.ArgumentList.Add("kill-server");
            start.Environment["ADB_SERVER_PORT"] = adbServerPort;

            using var process = Process.Start(start);
            if (process is not null && !process.WaitForExit(5000))
            {
                TryKill(process);
            }
        }
        catch
        {
            // The main ADB command result has already been captured; server cleanup is best-effort.
        }
    }
}

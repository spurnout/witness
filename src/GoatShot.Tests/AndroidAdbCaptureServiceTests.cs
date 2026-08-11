using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AndroidAdbCaptureServiceTests
{
    [TestMethod]
    public void ParseDevices_ParsesReadyUnauthorizedAndOfflineDevices()
    {
        const string output = """
            List of devices attached
            R58M1234	device product:akita model:Pixel_8 device:akita transport_id:1
            emulator-5554	unauthorized transport_id:2
            ZY223	offline product:foo model:Moto_G device:foo transport_id:3
            """;

        var devices = AndroidAdbCaptureService.ParseDevices(output);

        Assert.AreEqual(3, devices.Count);
        Assert.AreEqual("R58M1234", devices[0].Serial);
        Assert.IsTrue(devices[0].IsReady);
        Assert.AreEqual("Pixel_8", devices[0].Model);
        Assert.AreEqual("1", devices[0].TransportId);
        StringAssert.Contains(devices[0].DisplayLabel, "Pixel 8");
        Assert.IsTrue(devices[1].IsUnauthorized);
        Assert.IsTrue(devices[2].IsOffline);
    }

    [TestMethod]
    public void BuildDiagnostics_ReportsExpectedDeviceStates()
    {
        var noDevice = AndroidAdbCaptureService.BuildDiagnostics("adb.exe", Array.Empty<AndroidAdbDevice>());
        Assert.AreEqual(AndroidAdbStatus.NoDevice, noDevice.Status);
        Assert.IsFalse(noDevice.Ready);

        var unauthorized = AndroidAdbCaptureService.BuildDiagnostics("adb.exe", new[]
        {
            new AndroidAdbDevice { Serial = "phone", State = "unauthorized" }
        });
        Assert.AreEqual(AndroidAdbStatus.UnauthorizedDevice, unauthorized.Status);
        StringAssert.Contains(unauthorized.Message, "unauthorized");

        var multiple = AndroidAdbCaptureService.BuildDiagnostics("adb.exe", new[]
        {
            new AndroidAdbDevice { Serial = "one", State = "device" },
            new AndroidAdbDevice { Serial = "two", State = "device" }
        });
        Assert.AreEqual(AndroidAdbStatus.MultipleDevices, multiple.Status);
        StringAssert.Contains(multiple.Message, "--device");
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_ReportsMissingAdbBeforeRunningProcess()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var diagnostics = await service.GetDiagnosticsAsync(Path.Combine(paths.TempRoot, "missing-adb.exe"));

            Assert.AreEqual(AndroidAdbStatus.MissingAdb, diagnostics.Status);
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task GetDiagnosticsAsync_TimesOutHungDevicesProbe()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                TextHandlerAsync = async (_, _, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    return new AdbProcessResult(0, ReadyDeviceOutput("phone-1"), string.Empty, []);
                }
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner,
                diagnosticsDevicesTimeoutSeconds: 1);
            var stopwatch = Stopwatch.StartNew();

            var diagnostics = await service.GetDiagnosticsAsync(CreateFakeAdb(paths));

            stopwatch.Stop();
            Assert.AreEqual(AndroidAdbStatus.AdbFailed, diagnostics.Status);
            StringAssert.Contains(diagnostics.Message, "timed out");
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"ADB diagnostics returned too slowly: {stopwatch.Elapsed}");
        });
    }

    [TestMethod]
    public async Task CaptureScreenshotAsync_CapturesPngAndIndexesWorkspace()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            var store = new WorkspaceStore(paths, settings);
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = CreatePngBytes()
            };
            var service = new AndroidAdbCaptureService(paths, store, runner);
            var adbPath = CreateFakeAdb(paths);

            var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
            {
                AdbPath = adbPath,
                AddToWorkspace = true
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            Assert.IsNotNull(result.OutputPath);
            Assert.IsTrue(File.Exists(result.OutputPath));
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.AndroidDevice, result.Item.Kind);
            Assert.AreEqual("adb", result.Item.SourceApp);
            Assert.AreEqual("Android device", result.Item.SourceMonitorName);
            Assert.AreEqual(1, store.Load().Count);
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "exec-out", "screencap", "-p" },
                runner.BinaryCalls.Single().ToArray());
        });
    }

    [TestMethod]
    public async Task CaptureScreenshotAsync_RequiresDeviceSerialWhenMultipleReady()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = """
                    List of devices attached
                    one	device model:One
                    two	device model:Two
                    """
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
            {
                AdbPath = CreateFakeAdb(paths)
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.MultipleDevices, result.Status);
            StringAssert.Contains(result.Message, "--device");
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task CaptureScreenshotAsync_ReportsUnauthorizedDevice()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = """
                    List of devices attached
                    phone-1	unauthorized model:Pixel_8
                    """
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
            {
                AdbPath = CreateFakeAdb(paths)
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.UnauthorizedDevice, result.Status);
            StringAssert.Contains(result.Message, "unauthorized");
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task CaptureScreenshotAsync_RejectsNonPngPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputPath = Path.Combine(paths.TempRoot, "phone.png");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = Encoding.UTF8.GetBytes("not a png")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
            {
                AdbPath = CreateFakeAdb(paths),
                OutputPath = outputPath
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            StringAssert.Contains(result.Message, "PNG");
            Assert.IsFalse(File.Exists(outputPath));
        });
    }

    [TestMethod]
    public async Task CaptureScreenrecordAsync_RecordsPullsIndexesAndCleansRemote()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputPath = Path.Combine(paths.TempRoot, "phone.mp4");
            var settings = new AppSettings();
            var store = new WorkspaceStore(paths, settings);
            var runner = new FakeAdbRunner();
            runner.TextHandler = (_, arguments) =>
            {
                if (arguments.SequenceEqual(new[] { "devices", "-l" }))
                {
                    return TextResult(0, ReadyDeviceOutput("phone-1"));
                }

                if (arguments.Contains("pull", StringComparer.OrdinalIgnoreCase))
                {
                    File.WriteAllBytes(arguments[^1], CreateMp4Bytes());
                    return TextResult(0, "1 file pulled");
                }

                return TextResult();
            };

            var service = new AndroidAdbCaptureService(paths, store, runner);

            var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
            {
                AdbPath = CreateFakeAdb(paths),
                OutputPath = outputPath,
                AddToWorkspace = true,
                DurationSeconds = 3
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            Assert.AreEqual(3, result.DurationSeconds);
            Assert.IsTrue(File.Exists(outputPath));
            Assert.IsNotNull(result.RemotePath);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.AndroidRecording, result.Item.Kind);
            Assert.AreEqual("adb", result.Item.SourceApp);
            Assert.AreEqual("Android device", result.Item.SourceMonitorName);
            Assert.AreEqual(1, store.Load().Count);

            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "shell", "mkdir", "-p", "/sdcard/Movies/Receipts" },
                runner.TextCalls[1].ToArray());

            StringAssert.Contains(result.RemotePath, "/receipts-");

            var screenrecord = runner.TextCalls.Single(call => call.Contains("screenrecord", StringComparer.OrdinalIgnoreCase));
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "shell", "screenrecord", "--time-limit", "3", result.RemotePath },
                screenrecord.ToArray());

            var pull = runner.TextCalls.Single(call => call.Contains("pull", StringComparer.OrdinalIgnoreCase));
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "pull", result.RemotePath, outputPath },
                pull.ToArray());

            var cleanup = runner.TextCalls.Single(call => call.Contains("rm", StringComparer.OrdinalIgnoreCase));
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "shell", "rm", "-f", result.RemotePath },
                cleanup.ToArray());
        });
    }

    [TestMethod]
    public async Task CaptureScreenrecordAsync_RejectsOutOfRangeDurationBeforeAdb()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DurationSeconds = AndroidAdbCaptureService.MaximumScreenrecordDurationSeconds + 1
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.InvalidRequest, result.Status);
            StringAssert.Contains(result.Message, "duration");
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task CaptureScreenrecordAsync_RequiresDeviceSerialWhenMultipleReady()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = """
                    List of devices attached
                    one	device model:One
                    two	device model:Two
                    """
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DurationSeconds = 2
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.MultipleDevices, result.Status);
            StringAssert.Contains(result.Message, "--device");
            Assert.AreEqual(1, runner.TextCalls.Count);
            Assert.IsFalse(runner.TextCalls.Any(call => call.Contains("screenrecord", StringComparer.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task CaptureScreenrecordAsync_FailsWhenPullDoesNotCreatePayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputPath = Path.Combine(paths.TempRoot, "missing.mp4");
            var runner = new FakeAdbRunner();
            runner.TextHandler = (_, arguments) =>
                arguments.SequenceEqual(new[] { "devices", "-l" })
                    ? TextResult(0, ReadyDeviceOutput("phone-1"))
                    : TextResult();
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
            {
                AdbPath = CreateFakeAdb(paths),
                OutputPath = outputPath,
                DurationSeconds = 2
            });

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            StringAssert.Contains(result.Message, "MP4");
            Assert.IsFalse(File.Exists(outputPath));
            Assert.IsTrue(runner.TextCalls.Any(call => call.Contains("rm", StringComparer.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task BuildLivePreviewPlanAsync_PlansScreencapPollingWithoutStartingCapture()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
            {
                AdbPath = CreateFakeAdb(paths),
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                DurationSeconds = 3,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024
            });

            Assert.IsTrue(plan.Succeeded, plan.Message);
            Assert.AreEqual(AndroidAdbStatus.Ready, plan.Status);
            Assert.IsTrue(plan.DryRun);
            Assert.AreEqual(AndroidAdbLivePreviewStrategy.ScreencapPolling, plan.Strategy);
            Assert.AreEqual(6, plan.EstimatedMaxFrames);
            Assert.AreEqual(13, plan.TimeoutSeconds);
            Assert.AreEqual(1, plan.PlannedCommands.Count);
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "exec-out", "screencap", "-p" },
                plan.PlannedCommands[0].Arguments.ToArray());
            Assert.IsTrue(plan.PlannedCommands[0].CapturesDeviceContent);
            StringAssert.Contains(string.Join(" ", plan.PrivacyNotes), "safe device content");
            StringAssert.Contains(plan.CleanupPlan, "No remote file");
            CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.TextCalls.Single().ToArray());
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task BuildLivePreviewPlanAsync_PlansH264StreamWithBoundsAndCleanup()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputPath = Path.Combine(paths.TempRoot, "preview.mp4");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
            {
                AdbPath = CreateFakeAdb(paths),
                Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
                DurationSeconds = 5,
                FrameIntervalMs = 1000,
                MaxBytes = 10 * 1024 * 1024,
                OutputPath = outputPath
            });

            Assert.IsTrue(plan.Succeeded, plan.Message);
            Assert.AreEqual(AndroidAdbLivePreviewStrategy.H264Stream, plan.Strategy);
            Assert.AreEqual(20, plan.TimeoutSeconds);
            Assert.AreEqual(2, plan.PlannedCommands.Count);
            var adbCommand = plan.PlannedCommands.Single(command => command.Name == "adb-h264-stream");
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "shell", "screenrecord", "--output-format=h264", "--time-limit", "5", "-" },
                adbCommand.Arguments.ToArray());
            var ffmpegCommand = plan.PlannedCommands.Single(command => command.Name == "ffmpeg-remux-preview");
            CollectionAssert.AreEqual(
                new[] { "-f", "h264", "-i", "pipe:0", "-c", "copy", outputPath },
                ffmpegCommand.Arguments.ToArray());
            Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("dry-run", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(plan.Warnings.Any(warning => warning.Contains("device/OS dependent", StringComparison.OrdinalIgnoreCase)));
            StringAssert.Contains(plan.CleanupPlan, "Kill the ADB process");
            StringAssert.Contains(plan.DisconnectBehavior, "stdout");
            CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.TextCalls.Single().ToArray());
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task BuildLivePreviewPlanAsync_RejectsInvalidBoundsBeforeAdb()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
            {
                AdbPath = CreateFakeAdb(paths),
                Strategy = (AndroidAdbLivePreviewStrategy)(-1),
                DurationSeconds = AndroidAdbCaptureService.MaximumLivePreviewDurationSeconds + 1,
                FrameIntervalMs = AndroidAdbCaptureService.MinimumLivePreviewFrameIntervalMs - 1,
                MaxBytes = AndroidAdbCaptureService.MinimumLivePreviewMaxBytes - 1
            });

            Assert.IsFalse(plan.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.InvalidRequest, plan.Status);
            Assert.AreEqual(4, plan.Issues.Count);
            StringAssert.Contains(string.Join(" ", plan.Issues), "duration");
            StringAssert.Contains(string.Join(" ", plan.Issues), "frame interval");
            StringAssert.Contains(string.Join(" ", plan.Issues), "max bytes");
            StringAssert.Contains(string.Join(" ", plan.Issues), "strategy");
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task BuildLivePreviewPlanAsync_RequiresDeviceSerialWhenMultipleReady()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = """
                    List of devices attached
                    one	device model:One
                    two	device model:Two
                    """
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
            {
                AdbPath = CreateFakeAdb(paths),
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling
            });

            Assert.IsFalse(plan.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.MultipleDevices, plan.Status);
            StringAssert.Contains(plan.Message, "--device");
            Assert.AreEqual(0, plan.PlannedCommands.Count);
            CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.TextCalls.Single().ToArray());
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task BuildLivePreviewPlanAsync_ReportsMissingAdbWithoutRunningProcess()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var plan = await service.BuildLivePreviewPlanAsync(new AndroidAdbLivePreviewPlanRequest
            {
                AdbPath = Path.Combine(paths.TempRoot, "missing-adb.exe"),
                Strategy = AndroidAdbLivePreviewStrategy.H264Stream
            });

            Assert.IsFalse(plan.Succeeded);
            Assert.AreEqual(AndroidAdbStatus.MissingAdb, plan.Status);
            StringAssert.Contains(plan.Message, "adb.exe was not found");
            Assert.AreEqual(0, plan.PlannedCommands.Count);
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_RefusesWithoutSafeContentConfirmationBeforeAdb()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Executed);
            Assert.AreEqual(AndroidAdbStatus.InvalidRequest, result.Status);
            StringAssert.Contains(string.Join(" ", result.Issues), "--safe-content-confirmed");
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("bounded-screencap-polling", result.Summary!.Mode);
            Assert.AreEqual("not-started", result.Summary.TimeoutStatus);
            Assert.AreEqual("not-started", result.Summary.ByteCapStatus);
            Assert.AreEqual("not-started", result.Summary.CleanupStatus);
            Assert.IsFalse(result.Summary.SafeContentConfirmed);
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_RequiresExplicitDeviceBeforeAdb()
    {
        await WithTempPathsAsync(async paths =>
        {
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Executed);
            Assert.AreEqual(AndroidAdbStatus.InvalidRequest, result.Status);
            StringAssert.Contains(string.Join(" ", result.Issues), "--device");
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("not-started", result.Summary!.TimeoutStatus);
            Assert.AreEqual("not-started", result.Summary.ByteCapStatus);
            Assert.AreEqual("not-started", result.Summary.CleanupStatus);
            Assert.IsTrue(result.Summary.SafeContentConfirmed);
            Assert.AreEqual(0, runner.TextCalls.Count);
            Assert.AreEqual(0, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CapturesH264StreamAndManifest()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-h264");
            var h264Bytes = CreateH264Bytes();
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = h264Bytes
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024,
                OutputDirectory = outputDirectory,
                GenerateContactSheet = true
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.Executed);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            Assert.AreEqual(AndroidAdbLivePreviewStrategy.H264Stream, result.Strategy);
            Assert.AreEqual(h264Bytes.Length, result.BytesCaptured);
            Assert.IsNotNull(result.StreamPath);
            Assert.IsTrue(File.Exists(result.StreamPath));
            Assert.AreEqual(0, result.FramePaths.Count);
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(result.RemuxMessage.Length > 0);
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("bounded-h264-stream", result.Summary!.Mode);
            Assert.AreEqual("completed-within-timeout", result.Summary.TimeoutStatus);
            Assert.AreEqual("within-byte-cap", result.Summary.ByteCapStatus);
            Assert.AreEqual("not-needed", result.Summary.CleanupStatus);
            Assert.AreEqual(result.StreamPath, result.Summary.StreamPath);
            Assert.AreEqual(h264Bytes.Length, result.Summary.StreamBytes);
            Assert.IsTrue(
                result.Summary.RemuxStatus is "skipped" or "failed" or "remuxed",
                result.Summary.RemuxStatus);
            StringAssert.Contains(string.Join(" ", result.Warnings), "Contact-sheet");
            var manifest = await File.ReadAllTextAsync(result.ManifestPath!);
            Assert.AreEqual("receipts-android-preview-manifest.json", Path.GetFileName(result.ManifestPath));
            StringAssert.Contains(manifest, "\"strategy\": \"H264Stream\"");
            StringAssert.Contains(manifest, "\"streamFileName\": \"android-preview-stream.h264\"");
            StringAssert.Contains(manifest, $"\"streamBytes\": {h264Bytes.Length}");
            CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.TextCalls.Single().ToArray());
            Assert.AreEqual(1, runner.BinaryCalls.Count);
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "shell", "screenrecord", "--output-format=h264", "--time-limit", "1", "-" },
                runner.BinaryCalls.Single().ToArray());
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CleansH264OutputOnByteCap()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-h264-byte-cap");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = CreateH264Bytes(AndroidAdbCaptureService.MinimumLivePreviewMaxBytes + 1)
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = AndroidAdbCaptureService.MinimumLivePreviewMaxBytes,
                OutputDirectory = outputDirectory
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Executed);
            Assert.IsTrue(result.CleanupPerformed);
            StringAssert.Contains(result.Message, "byte cap");
            Assert.IsNull(result.StreamPath);
            Assert.IsFalse(Directory.Exists(outputDirectory));
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("bounded-h264-stream", result.Summary!.Mode);
            Assert.AreEqual("byte-cap-exceeded", result.Summary.ByteCapStatus);
            Assert.AreEqual("cleanup-performed", result.Summary.CleanupStatus);
            Assert.AreEqual(1, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CleansH264OutputOnInvalidPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-h264-invalid");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = Encoding.UTF8.GetBytes("not h264")
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.H264Stream,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = AndroidAdbCaptureService.MinimumLivePreviewMaxBytes,
                OutputDirectory = outputDirectory
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Executed);
            Assert.IsTrue(result.CleanupPerformed);
            StringAssert.Contains(result.Message, "Annex B H.264");
            Assert.IsNull(result.StreamPath);
            Assert.IsFalse(Directory.Exists(outputDirectory));
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("bounded-h264-stream", result.Summary!.Mode);
            Assert.AreEqual("within-byte-cap", result.Summary.ByteCapStatus);
            Assert.AreEqual("cleanup-performed", result.Summary.CleanupStatus);
            Assert.AreEqual(1, runner.TextCalls.Count);
            Assert.AreEqual(1, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CapturesFramesAndManifest()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-success");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            runner.BinaryHandler = (_, _) => new AdbProcessResult(0, string.Empty, string.Empty, CreatePngBytes());
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024,
                OutputDirectory = outputDirectory,
                GenerateContactSheet = true,
                ContactSheetMaxFrames = 1
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.Executed);
            Assert.AreEqual(AndroidAdbStatus.Ready, result.Status);
            Assert.AreEqual(AndroidAdbLivePreviewStrategy.ScreencapPolling, result.Strategy);
            Assert.AreEqual(2, result.FramePaths.Count);
            Assert.IsTrue(result.BytesCaptured > 0);
            Assert.IsTrue(Directory.Exists(outputDirectory));
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(File.Exists(result.ContactSheetPath));
            Assert.AreEqual(1, result.ContactSheetFrameCount);
            Assert.AreEqual(1, result.ContactSheetMaxFrames);
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("bounded-screencap-polling", result.Summary!.Mode);
            Assert.AreEqual("completed-within-timeout", result.Summary.TimeoutStatus);
            Assert.AreEqual("within-byte-cap", result.Summary.ByteCapStatus);
            Assert.AreEqual("not-needed", result.Summary.CleanupStatus);
            Assert.AreEqual(2, result.Summary.FrameCount);
            Assert.IsTrue(result.Summary.SafeContentConfirmed);
            Assert.AreEqual(result.ManifestPath, result.Summary.ManifestPath);
            Assert.AreEqual(result.ContactSheetPath, result.Summary.ContactSheetPath);
            Assert.AreEqual(1, result.Summary.ContactSheetFrameCount);
            Assert.IsTrue(result.FramePaths.All(File.Exists));
            var manifest = await File.ReadAllTextAsync(result.ManifestPath!);
            Assert.AreEqual("receipts-android-preview-manifest.json", Path.GetFileName(result.ManifestPath));
            StringAssert.Contains(manifest, "\"schemaVersion\": 1");
            StringAssert.Contains(manifest, "phone-1");
            StringAssert.Contains(manifest, "\"contactSheetFileName\": \"android-preview-contact-sheet.png\"");
            StringAssert.Contains(manifest, "\"contactSheetFrameCount\": 1");
            StringAssert.Contains(manifest, "\"summary\"");
            CollectionAssert.AreEqual(new[] { "devices", "-l" }, runner.TextCalls.Single().ToArray());
            Assert.AreEqual(2, runner.BinaryCalls.Count);
            CollectionAssert.AreEqual(
                new[] { "-s", "phone-1", "exec-out", "screencap", "-p" },
                runner.BinaryCalls[0].ToArray());
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CleansOutputWhenDeviceDisconnects()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-disconnect");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryExitCode = 1,
                BinaryError = "device offline"
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024,
                OutputDirectory = outputDirectory
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Executed);
            Assert.IsTrue(result.CleanupPerformed);
            StringAssert.Contains(result.Message, "exit code 1");
            StringAssert.Contains(result.Message, "device offline");
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("stopped-before-timeout", result.Summary!.TimeoutStatus);
            Assert.AreEqual("within-byte-cap", result.Summary.ByteCapStatus);
            Assert.AreEqual("cleanup-performed", result.Summary.CleanupStatus);
            Assert.IsFalse(Directory.Exists(outputDirectory));
            Assert.AreEqual(1, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CleansOutputOnTimeout()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-timeout");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1")
            };
            runner.BinaryHandlerAsync = (_, _, _) =>
                Task.FromException<AdbProcessResult>(new OperationCanceledException());
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = 5 * 1024 * 1024,
                OutputDirectory = outputDirectory
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Executed);
            Assert.IsTrue(result.CleanupPerformed);
            StringAssert.Contains(result.Message, "timed out");
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("timed-out", result.Summary!.TimeoutStatus);
            Assert.AreEqual("within-byte-cap", result.Summary.ByteCapStatus);
            Assert.AreEqual("cleanup-performed", result.Summary.CleanupStatus);
            Assert.IsFalse(Directory.Exists(outputDirectory));
            Assert.AreEqual(1, runner.BinaryCalls.Count);
        });
    }

    [TestMethod]
    public async Task ExecuteLivePreviewAsync_CleansOutputOnByteCap()
    {
        await WithTempPathsAsync(async paths =>
        {
            var outputDirectory = Path.Combine(paths.TempRoot, "android-preview-byte-cap");
            var runner = new FakeAdbRunner
            {
                DevicesOutput = ReadyDeviceOutput("phone-1"),
                BinaryOutput = CreatePngLikeBytes(AndroidAdbCaptureService.MinimumLivePreviewMaxBytes + 1)
            };
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                runner);

            var result = await service.ExecuteLivePreviewAsync(new AndroidAdbLivePreviewExecutionRequest
            {
                AdbPath = CreateFakeAdb(paths),
                DeviceSerial = "phone-1",
                Strategy = AndroidAdbLivePreviewStrategy.ScreencapPolling,
                SafeContentConfirmed = true,
                DurationSeconds = 1,
                FrameIntervalMs = 500,
                MaxBytes = AndroidAdbCaptureService.MinimumLivePreviewMaxBytes,
                OutputDirectory = outputDirectory
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Executed);
            Assert.IsTrue(result.CleanupPerformed);
            StringAssert.Contains(result.Message, "byte cap");
            Assert.IsNotNull(result.Summary);
            Assert.AreEqual("stopped-before-timeout", result.Summary!.TimeoutStatus);
            Assert.AreEqual("byte-cap-exceeded", result.Summary.ByteCapStatus);
            Assert.AreEqual("cleanup-performed", result.Summary.CleanupStatus);
            Assert.IsFalse(Directory.Exists(outputDirectory));
            Assert.AreEqual(1, runner.BinaryCalls.Count);
        });
    }

    private static string ReadyDeviceOutput(string serial)
    {
        return $"""
            List of devices attached
            {serial}	device product:akita model:Pixel_8 device:akita transport_id:1
            """;
    }

    private static byte[] CreatePngBytes()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.Teal);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[] CreateMp4Bytes()
    {
        return new byte[]
        {
            0x00, 0x00, 0x00, 0x18,
            0x66, 0x74, 0x79, 0x70,
            0x6D, 0x70, 0x34, 0x32,
            0x00, 0x00, 0x00, 0x00,
            0x6D, 0x70, 0x34, 0x32,
            0x69, 0x73, 0x6F, 0x6D
        };
    }

    private static byte[] CreatePngLikeBytes(long length)
    {
        var bytes = new byte[checked((int)length)];
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(signature, bytes, signature.Length);
        return bytes;
    }

    private static byte[] CreateH264Bytes(long length = 32)
    {
        var bytes = new byte[checked((int)Math.Max(length, 32))];
        var payload = new byte[]
        {
            0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1F,
            0xE5, 0x88, 0x68, 0x54, 0x05, 0x01, 0xED, 0x00,
            0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80,
            0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x21, 0xA0
        };
        Array.Copy(payload, bytes, payload.Length);
        return bytes;
    }

    private static AdbProcessResult TextResult(
        int exitCode = 0,
        string stdout = "",
        string stderr = "")
    {
        return new AdbProcessResult(exitCode, stdout, stderr, Encoding.UTF8.GetBytes(stdout));
    }

    private static string CreateFakeAdb(AppPaths paths)
    {
        Directory.CreateDirectory(paths.TempRoot);
        var path = Path.Combine(paths.TempRoot, "adb.exe");
        File.WriteAllText(path, "fake adb");
        return path;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            var settings = new AppSettings();
            var paths = AppPaths.Create(settings);
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeAdbRunner : IAdbProcessRunner
    {
        public string DevicesOutput { get; init; } = string.Empty;
        public string DevicesError { get; init; } = string.Empty;
        public int DevicesExitCode { get; init; }
        public byte[] BinaryOutput { get; init; } = [];
        public string BinaryError { get; init; } = string.Empty;
        public int BinaryExitCode { get; init; }
        public Func<string, IReadOnlyList<string>, AdbProcessResult>? TextHandler { get; set; }
        public Func<string, IReadOnlyList<string>, CancellationToken, Task<AdbProcessResult>>? TextHandlerAsync { get; set; }
        public Func<string, IReadOnlyList<string>, AdbProcessResult>? BinaryHandler { get; set; }
        public Func<string, IReadOnlyList<string>, CancellationToken, Task<AdbProcessResult>>? BinaryHandlerAsync { get; set; }
        public List<IReadOnlyList<string>> TextCalls { get; } = new();
        public List<IReadOnlyList<string>> BinaryCalls { get; } = new();

        public Task<AdbProcessResult> RunTextAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            TextCalls.Add(arguments.ToArray());
            if (TextHandlerAsync is not null)
            {
                return TextHandlerAsync(fileName, arguments, cancellationToken);
            }

            if (TextHandler is not null)
            {
                return Task.FromResult(TextHandler(fileName, arguments));
            }

            return Task.FromResult(new AdbProcessResult(
                DevicesExitCode,
                DevicesOutput,
                DevicesError,
                Encoding.UTF8.GetBytes(DevicesOutput)));
        }

        public Task<AdbProcessResult> RunBinaryAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            BinaryCalls.Add(arguments.ToArray());
            if (BinaryHandlerAsync is not null)
            {
                return BinaryHandlerAsync(fileName, arguments, cancellationToken);
            }

            if (BinaryHandler is not null)
            {
                return Task.FromResult(BinaryHandler(fileName, arguments));
            }

            return Task.FromResult(new AdbProcessResult(
                BinaryExitCode,
                string.Empty,
                BinaryError,
                BinaryOutput));
        }

        public Task<AdbProcessResult> RunBinaryLimitedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            BinaryCalls.Add(arguments.ToArray());
            if (BinaryHandlerAsync is not null)
            {
                return BinaryHandlerAsync(fileName, arguments, cancellationToken);
            }

            var result = BinaryHandler is not null
                ? BinaryHandler(fileName, arguments)
                : new AdbProcessResult(
                    BinaryExitCode,
                    string.Empty,
                    BinaryError,
                    BinaryOutput);

            if (result.StandardOutput.LongLength > maxBytes)
            {
                return Task.FromResult(new AdbProcessResult(
                    1,
                    string.Empty,
                    $"ADB stdout exceeded byte cap of {maxBytes} bytes.",
                    result.StandardOutput.Take(checked((int)maxBytes)).ToArray()));
            }

            return Task.FromResult(result);
        }
    }
}

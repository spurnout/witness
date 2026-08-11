using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AndroidDeviceTransportTests
{
    [TestMethod]
    public async Task Diagnostics_UsesInProcessTransportWhenNoExternalOverrideIsConfigured()
    {
        await WithPathsAsync(async paths =>
        {
            var transport = new FakeAndroidTransport();
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                transport: transport);

            var diagnostics = await service.GetDiagnosticsAsync();

            Assert.AreEqual(AndroidAdbStatus.Ready, diagnostics.Status);
            Assert.AreEqual("in-process-winusb", diagnostics.AdbPath);
            Assert.AreEqual("usb-test-device", diagnostics.Devices.Single().Serial);
        });
    }

    [TestMethod]
    public async Task Screenshot_UsesBoundedTransportCommandAndWritesPng()
    {
        await WithPathsAsync(async paths =>
        {
            var transport = new FakeAndroidTransport { Output = MinimalPng };
            var output = Path.Combine(paths.TempRoot, "android.png");
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                transport: transport);

            var result = await service.CaptureScreenshotAsync(new AndroidAdbCaptureRequest
            {
                DeviceSerial = "usb-test-device",
                OutputPath = output
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(File.Exists(output));
            Assert.AreEqual("screencap -p", transport.Commands.Single());
            Assert.IsTrue(transport.MaximumBytes.Single() <= AndroidAdbCaptureService.MaximumLivePreviewMaxBytes);
        });
    }

    [TestMethod]
    public async Task Screenrecord_UsesBoundedDirectCommandWithoutAdbExecutable()
    {
        await WithPathsAsync(async paths =>
        {
            var transport = new FakeAndroidTransport { Output = [0, 0, 0, 24, .. "ftypisom"u8.ToArray()] };
            var output = Path.Combine(paths.TempRoot, "android.mp4");
            var service = new AndroidAdbCaptureService(
                paths,
                new WorkspaceStore(paths, new AppSettings()),
                transport: transport);

            var result = await service.CaptureScreenrecordAsync(new AndroidAdbScreenrecordRequest
            {
                DeviceSerial = "usb-test-device",
                DurationSeconds = 3,
                OutputPath = output
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            StringAssert.Contains(transport.Commands.Single(), "screenrecord --time-limit 3");
            StringAssert.Contains(transport.Commands.Single(), "rm -f");
            Assert.IsTrue(transport.Timeouts.Single() <= TimeSpan.FromSeconds(123));
        });
    }

    private static readonly byte[] MinimalPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private static async Task WithPathsAsync(Func<AppPaths, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        var priorLocal = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var priorLibrary = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var priorAdb = Environment.GetEnvironmentVariable("GOATSHOT_ADB_PATH");
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            Environment.SetEnvironmentVariable("GOATSHOT_ADB_PATH", null);
            await action(AppPaths.Create(new AppSettings()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", priorLocal);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", priorLibrary);
            Environment.SetEnvironmentVariable("GOATSHOT_ADB_PATH", priorAdb);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeAndroidTransport : IAndroidDeviceTransport
    {
        public byte[] Output { get; init; } = MinimalPng;
        public List<string> Commands { get; } = new();
        public List<long> MaximumBytes { get; } = new();
        public List<TimeSpan> Timeouts { get; } = new();

        public Task<IReadOnlyList<AndroidTransportDevice>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AndroidTransportDevice>>([
                new("usb-test-device", "Pixel Test", true, "pixel", "Pixel Test", "test-device")
            ]);

        public Task<AndroidTransportCommandResult> ExecuteAsync(
            string deviceId,
            string command,
            long maxBytes,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            MaximumBytes.Add(maxBytes);
            Timeouts.Add(timeout);
            return Task.FromResult(new AndroidTransportCommandResult(true, Output, "ok"));
        }
    }
}

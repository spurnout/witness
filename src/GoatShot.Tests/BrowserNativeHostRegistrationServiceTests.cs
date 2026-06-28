using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserNativeHostRegistrationServiceTests
{
    private const string ChromeExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string EdgeExtensionId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [TestMethod]
    public async Task WriteManifestsAsync_CreatesChromiumAndFirefoxManifests()
    {
        await WithTempPathsAsync(async paths =>
        {
            var hostExe = CreateFakeHost(paths);
            var firefoxRoot = Path.Combine(paths.TempRoot, "firefox-hosts");
            var service = new BrowserNativeHostRegistrationService(paths, new FakeRegistry(), firefoxRoot);

            var results = await service.WriteManifestsAsync(new BrowserNativeHostInstallRequest
            {
                Browsers = new[] { BrowserNativeHostBrowser.Chrome, BrowserNativeHostBrowser.Firefox },
                HostExecutablePath = hostExe,
                ChromeExtensionId = ChromeExtensionId,
                FirefoxExtensionId = "goatshot@example.test"
            });

            Assert.AreEqual(2, results.Count);
            var chromeJson = await File.ReadAllTextAsync(results.Single(result => result.Browser == BrowserNativeHostBrowser.Chrome).ManifestPath);
            StringAssert.Contains(chromeJson, "\"name\": \"com.goatshot.bridge\"");
            StringAssert.Contains(chromeJson, $"\"path\": \"{hostExe.Replace("\\", "\\\\")}\"");
            StringAssert.Contains(chromeJson, $"chrome-extension://{ChromeExtensionId}/");
            StringAssert.Contains(chromeJson, "\"allowed_origins\"");
            Assert.IsFalse(chromeJson.Contains("allowed_extensions", StringComparison.Ordinal));

            var firefoxJson = await File.ReadAllTextAsync(results.Single(result => result.Browser == BrowserNativeHostBrowser.Firefox).ManifestPath);
            StringAssert.Contains(firefoxJson, "\"allowed_extensions\"");
            StringAssert.Contains(firefoxJson, "goatshot@example.test");
            Assert.IsFalse(firefoxJson.Contains("allowed_origins", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public async Task InstallAsync_RegistersChromeEdgeAndFirefoxUserScope()
    {
        await WithTempPathsAsync(async paths =>
        {
            var registry = new FakeRegistry();
            var firefoxRoot = Path.Combine(paths.TempRoot, "firefox-hosts");
            var service = new BrowserNativeHostRegistrationService(paths, registry, firefoxRoot);

            var results = await service.InstallAsync(new BrowserNativeHostInstallRequest
            {
                HostExecutablePath = CreateFakeHost(paths),
                ChromeExtensionId = ChromeExtensionId,
                EdgeExtensionId = EdgeExtensionId,
                FirefoxExtensionId = "goatshot@example.test"
            });

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(2, registry.Values.Count);
            Assert.IsTrue(registry.Values.Keys.Any(key => key.Contains(@"Google\Chrome", StringComparison.Ordinal)));
            Assert.IsTrue(registry.Values.Keys.Any(key => key.Contains(@"Microsoft\Edge", StringComparison.Ordinal)));
            Assert.IsTrue(File.Exists(Path.Combine(firefoxRoot, "com.goatshot.bridge.json")));

            var status = service.GetStatus();
            Assert.AreEqual(3, status.Registrations.Count(registration => registration.Installed));
        });
    }

    [TestMethod]
    public async Task Uninstall_RemovesRegisteredManifests()
    {
        await WithTempPathsAsync(async paths =>
        {
            var registry = new FakeRegistry();
            var firefoxRoot = Path.Combine(paths.TempRoot, "firefox-hosts");
            var service = new BrowserNativeHostRegistrationService(paths, registry, firefoxRoot);
            await service.InstallAsync(new BrowserNativeHostInstallRequest
            {
                HostExecutablePath = CreateFakeHost(paths),
                ChromeExtensionId = ChromeExtensionId,
                EdgeExtensionId = EdgeExtensionId,
                FirefoxExtensionId = "goatshot@example.test"
            });

            var results = service.Uninstall();

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(0, registry.Values.Count);
            Assert.IsFalse(File.Exists(Path.Combine(firefoxRoot, "com.goatshot.bridge.json")));
        });
    }

    [TestMethod]
    public async Task WriteManifestsAsync_RejectsMissingChromiumExtensionId()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = new BrowserNativeHostRegistrationService(paths, new FakeRegistry(), Path.Combine(paths.TempRoot, "firefox-hosts"));

            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await service.WriteManifestsAsync(new BrowserNativeHostInstallRequest
                {
                    Browsers = new[] { BrowserNativeHostBrowser.Chrome },
                    HostExecutablePath = CreateFakeHost(paths)
                }));
        });
    }

    [TestMethod]
    public async Task NativeMessagingRunner_ValidatesAndStoresRedactedPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var bridge = CreateBridge(paths);
            var runner = new BrowserNativeMessagingHostRunner(bridge);
            await using var input = new MemoryStream();
            await BrowserNativeMessagingHostRunner.WriteMessageAsync(input, JsonSerializer.Deserialize<object>(ValidPayloadJson())!);
            input.Position = 0;
            await using var output = new MemoryStream();

            var exitCode = await runner.RunOnceAsync(input, output);
            output.Position = 0;
            var responseJson = ReadNativeMessage(output);
            var response = JsonSerializer.Deserialize<BrowserNativeHostMessageResult>(responseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.IsNotNull(response);
            Assert.AreEqual(0, exitCode, responseJson);
            Assert.IsTrue(response.Succeeded, response.Message);
            Assert.IsTrue(File.Exists(response.RedactedPayloadPath));
            var redacted = await File.ReadAllTextAsync(response.RedactedPayloadPath);
            Assert.IsFalse(redacted.Contains("fake-token-1234567890", StringComparison.Ordinal));
            StringAssert.Contains(redacted, "[REDACTED");
        });
    }

    [TestMethod]
    public async Task NativeMessagingRunner_PingReportsReachableStatus()
    {
        await WithTempPathsAsync(async paths =>
        {
            var bridge = CreateBridge(paths);
            var runner = new BrowserNativeMessagingHostRunner(bridge);
            await using var input = new MemoryStream();
            await BrowserNativeMessagingHostRunner.WriteMessageAsync(input, new
            {
                type = "GOATSHOT_PING"
            });
            input.Position = 0;
            await using var output = new MemoryStream();

            var exitCode = await runner.RunOnceAsync(input, output);
            output.Position = 0;
            var responseJson = ReadNativeMessage(output);
            var response = JsonSerializer.Deserialize<BrowserNativeHostMessageResult>(responseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.IsNotNull(response);
            Assert.AreEqual(0, exitCode, responseJson);
            Assert.IsTrue(response.Succeeded, response.Message);
            StringAssert.Contains(response.Message, "reachable");
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.NativeHostVersion));
        });
    }

    [TestMethod]
    public async Task NativeMessagingRunner_ImportsStitchPackage()
    {
        await WithTempPathsAsync(async paths =>
        {
            var bridge = CreateBridge(paths);
            var packageRoot = Path.Combine(paths.TempRoot, "stitch-package");
            WriteStitchPackage(packageRoot);
            var runner = new BrowserNativeMessagingHostRunner(bridge);
            await using var input = new MemoryStream();
            await BrowserNativeMessagingHostRunner.WriteMessageAsync(input, new
            {
                payload = JsonSerializer.Deserialize<object>(ValidPayloadJson()),
                stitchPackagePath = packageRoot
            });
            input.Position = 0;
            await using var output = new MemoryStream();

            var exitCode = await runner.RunOnceAsync(input, output);
            output.Position = 0;
            var responseJson = ReadNativeMessage(output);
            var response = JsonSerializer.Deserialize<BrowserNativeHostMessageResult>(responseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.IsNotNull(response);
            Assert.AreEqual(0, exitCode, responseJson);
            Assert.IsTrue(response.Succeeded, response.Message);
            Assert.IsTrue(File.Exists(response.RedactedPayloadPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.WorkspaceFilePath));
            Assert.IsTrue(File.Exists(response.WorkspaceFilePath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(response.StitchPackagePath));
        });
    }

    private static BrowserExtensionNativeBridgeService CreateBridge(AppPaths paths)
    {
        var workspace = new WorkspaceStore(paths, new AppSettings());
        workspace.AttachMetadataIndex(new WorkspaceMetadataIndex(paths));
        return new BrowserExtensionNativeBridgeService(paths, workspace);
    }

    private static string ReadNativeMessage(Stream stream)
    {
        Span<byte> header = stackalloc byte[4];
        Assert.AreEqual(4, stream.Read(header));
        var length = BitConverter.ToInt32(header);
        var payload = new byte[length];
        Assert.AreEqual(length, stream.Read(payload));
        return Encoding.UTF8.GetString(payload);
    }

    private static string CreateFakeHost(AppPaths paths)
    {
        Directory.CreateDirectory(paths.TempRoot);
        var host = Path.Combine(paths.TempRoot, "GoatShot.Cli.exe");
        File.WriteAllText(host, "fake host");
        return host;
    }

    private static void WriteStitchPackage(string packageRoot)
    {
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "tiles"));
        WriteSamplePng(Path.Combine(packageRoot, "stitched.png"));
        WriteSamplePng(Path.Combine(packageRoot, "tiles", "tile-0000.png"));
        File.WriteAllText(Path.Combine(packageRoot, "goatshot-stitch-package.json"), """
            {
              "schemaVersion": "goatshot.browser-stitch-package.v1",
              "correlationId": "native-host-fixture",
              "source": "extension-storage-export",
              "stitchedImagePath": "stitched.png",
              "tiles": [
                { "index": 0, "path": "tiles/tile-0000.png", "captureState": "captured" }
              ],
              "warnings": []
            }
            """);
    }

    private static void WriteSamplePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(80, 50, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(48, 230, 195));
            graphics.FillRectangle(brush, 10, 10, 60, 30);
        }

        bitmap.Save(path, ImageFormat.Png);
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
            var paths = AppPaths.Create(new AppSettings());
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

    private static string ValidPayloadJson()
    {
        return """
            {
              "schemaVersion": "goatshot.browser-capture.v1",
              "intent": {
                "captureMode": "full-page",
                "fullPageCaptureRequested": true,
                "includeDomMetadata": true,
                "includeTelemetry": true,
                "correlationId": "native-host-fixture"
              },
              "page": {
                "url": "https://app.example.test/orders?token=fake-token-1234567890&view=summary",
                "title": "Orders for alex@example.test",
                "referrer": "https://app.example.test/login?code=fake-code-1234567890",
                "contentType": "text/html",
                "language": "en-US",
                "capturedAt": "2026-06-15T04:30:00Z"
              },
              "viewport": {
                "width": 1440,
                "height": 900,
                "devicePixelRatio": 1.5,
                "scrollX": 0,
                "scrollY": 240
              },
              "fullPage": {
                "width": 1440,
                "height": 4200,
                "scrollWidth": 1440,
                "scrollHeight": 4200
              },
              "consent": {
                "screenshotConsented": true,
                "telemetryConsented": true,
                "consentText": "User consented to page screenshot metadata plus console/network summaries for GoatShot.",
                "consentedAt": "2026-06-15T04:29:58Z"
              },
              "consoleEvents": [
                {
                  "level": "error",
                  "message": "Checkout failed for alex@example.test with token=fake-secret-token",
                  "sourceUrl": "https://app.example.test/static/app.js?sig=fake-signature-123456",
                  "line": 42,
                  "column": 7
                }
              ],
              "networkEvents": []
            }
            """;
    }

    private sealed class FakeRegistry : IBrowserNativeHostRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetDefaultValue(string subKey) =>
            Values.GetValueOrDefault(subKey);

        public void SetDefaultValue(string subKey, string value)
        {
            Values[subKey] = value;
        }

        public void DeleteSubKeyTree(string subKey)
        {
            Values.Remove(subKey);
        }
    }
}

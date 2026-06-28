using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionLiveFixtureProofServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WritesProofFolderCommandsAndDiagnostics()
    {
        await WithTempPathsAsync(async paths =>
        {
            var source = CreateExtensionFixture(paths);
            var output = Path.Combine(paths.TempRoot, "proof");
            var hostExe = Path.Combine(paths.TempRoot, "GoatShot.Cli.exe");
            await File.WriteAllTextAsync(hostExe, "placeholder");
            var service = CreateService(paths);

            var result = await service.CreateAsync(
                new BrowserExtensionLiveFixtureProofRequest
                {
                    OutputPath = output,
                    ExtensionSourceDirectory = source,
                    Browser = "edge",
                    ExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    HostExecutablePath = hostExe
                },
                new BrowserNativeHostStatus
                {
                    HostName = BrowserNativeHostRegistrationService.HostName
                });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(File.Exists(result.NotesPath));
            Assert.IsTrue(File.Exists(result.CommandsPath));
            Assert.IsTrue(File.Exists(result.ServerScriptPath));
            Assert.IsTrue(File.Exists(result.BrowserLaunchScriptPath));
            Assert.IsTrue(File.Exists(result.BrowserLaunchPlanPath));
            Assert.IsTrue(File.Exists(result.DiagnosticsPath));
            Assert.AreEqual("edge", result.Browser);
            Assert.IsTrue(result.BrowserLaunch.SupportsAutomatedLaunch);
            StringAssert.Contains(result.BrowserLaunch.ProfileDirectory, "edge-live-fixture-profile");
            CollectionAssert.Contains(result.BrowserLaunch.BrowserArguments, $"--load-extension={source}");
            CollectionAssert.Contains(result.BrowserLaunch.BrowserArguments, $"--disable-extensions-except={source}");
            StringAssert.Contains(result.NativeHostInstallCommand, "--browser edge");
            StringAssert.Contains(result.NativeHostInstallCommand, "--edge-extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            var commands = await File.ReadAllTextAsync(result.CommandsPath);
            StringAssert.Contains(commands, "browser-extension diagnostics");
            StringAssert.Contains(commands, source);
            StringAssert.Contains(commands, "live-fixture");
            StringAssert.Contains(commands, "launch-browser-fixture.ps1");
            StringAssert.Contains(commands, "browser-launch-run.json");
            var launchScript = await File.ReadAllTextAsync(result.BrowserLaunchScriptPath);
            StringAssert.Contains(launchScript, "edge-live-fixture-profile");
            StringAssert.Contains(launchScript, "--user-data-dir=");
            StringAssert.Contains(launchScript, "--disable-extensions-except=");
            StringAssert.Contains(launchScript, "--load-extension=");
            StringAssert.Contains(launchScript, "browser-store publication");
            var launchPlan = await File.ReadAllTextAsync(result.BrowserLaunchPlanPath);
            StringAssert.Contains(launchPlan, "edge-live-fixture-profile");
            StringAssert.Contains(launchPlan, "--remote-debugging-port=");
            var notes = await File.ReadAllTextAsync(result.NotesPath);
            StringAssert.Contains(notes, "Browser Extension Live Fixture Proof");
            StringAssert.Contains(notes, "extension-source-ready");
            StringAssert.Contains(notes, "isolated Chrome/Edge profile");
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("not run", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task CreateAsync_WithPayloadAndStitchPackageImportsAndWritesRedactedResult()
    {
        await WithTempPathsAsync(async paths =>
        {
            var source = CreateExtensionFixture(paths);
            var output = Path.Combine(paths.TempRoot, "proof");
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            var packageRoot = Path.Combine(paths.TempRoot, "stitch-package");
            await File.WriteAllTextAsync(payloadPath, ValidPayloadJson());
            WriteStitchPackage(packageRoot);
            var service = CreateService(paths);

            var result = await service.CreateAsync(
                new BrowserExtensionLiveFixtureProofRequest
                {
                    OutputPath = output,
                    ExtensionSourceDirectory = source,
                    PayloadPath = payloadPath,
                    StitchPackagePath = packageRoot,
                    Force = true
                },
                new BrowserNativeHostStatus
                {
                    HostName = BrowserNativeHostRegistrationService.HostName
                });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsNotNull(result.Verification);
            Assert.IsTrue(result.Verification.Succeeded, result.Verification.Message);
            Assert.IsTrue(File.Exists(result.Verification.ImportResultPath));
            Assert.IsTrue(File.Exists(result.Verification.RedactedPayloadPath));
            Assert.IsTrue(File.Exists(result.Verification.WorkspaceFilePath));
            var redacted = await File.ReadAllTextAsync(result.Verification.RedactedPayloadPath);
            Assert.IsFalse(redacted.Contains("fake-token-1234567890", StringComparison.Ordinal));
            Assert.IsFalse(redacted.Contains("alex@example.test", StringComparison.Ordinal));
            StringAssert.Contains(redacted, "[REDACTED");
            var importResult = await File.ReadAllTextAsync(result.Verification.ImportResultPath);
            StringAssert.Contains(importResult, "Browser extension capture imported");
            StringAssert.Contains(importResult, "workspaceFilePath");
        });
    }

    private static BrowserExtensionLiveFixtureProofService CreateService(AppPaths paths)
    {
        var settings = new AppSettings();
        var workspace = new WorkspaceStore(paths, settings);
        workspace.AttachMetadataIndex(new WorkspaceMetadataIndex(paths));
        var bridge = new BrowserExtensionNativeBridgeService(paths, workspace);
        return new BrowserExtensionLiveFixtureProofService(paths, bridge);
    }

    private static string CreateExtensionFixture(AppPaths paths)
    {
        var root = Path.Combine(paths.TempRoot, "browser-extension");
        Directory.CreateDirectory(Path.Combine(root, "samples"));
        File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(root, "service-worker.js"), "// service worker");
        File.WriteAllText(Path.Combine(root, "popup.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(root, "options.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(root, "samples", "safe-fixture.html"), "<!doctype html><title>Safe Fixture</title>");
        return root;
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
                "correlationId": "sample-contract-fixture"
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
              "networkEvents": [
                {
                  "method": "POST",
                  "url": "https://api.example.test/orders?access_token=fake-access-token-123456",
                  "statusCode": 500,
                  "resourceType": "fetch",
                  "initiator": "https://app.example.test/static/app.js?key=fake-key-123456",
                  "errorText": "Bearer fakebearertoken12345678901234567890"
                }
              ]
            }
            """;
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
              "correlationId": "sample-contract-fixture",
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
            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }
}

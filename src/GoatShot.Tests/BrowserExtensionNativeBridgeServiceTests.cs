using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionNativeBridgeServiceTests
{
    [TestMethod]
    public async Task AcceptAsync_StoresRedactedPayloadWithoutScreenshot()
    {
        await WithTempPathsAsync(async paths =>
        {
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            await File.WriteAllTextAsync(payloadPath, ValidPayloadJson());
            var service = CreateService(paths);

            var result = await service.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = payloadPath
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNull(result.Item);
            Assert.IsTrue(File.Exists(result.RedactedPayloadPath));
            var redacted = await File.ReadAllTextAsync(result.RedactedPayloadPath);
            Assert.IsFalse(redacted.Contains("fake-token-1234567890", StringComparison.Ordinal));
            Assert.IsFalse(redacted.Contains("alex@example.test", StringComparison.Ordinal));
            StringAssert.Contains(redacted, "[REDACTED");
            StringAssert.Contains(result.Message, "redacted metadata stored");
        });
    }

    [TestMethod]
    public async Task AcceptAsync_ImportsScreenshotAsBrowserPageCapture()
    {
        await WithTempPathsAsync(async paths =>
        {
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            var screenshotPath = Path.Combine(paths.TempRoot, "page.png");
            await File.WriteAllTextAsync(payloadPath, ValidPayloadJson());
            WriteSamplePng(screenshotPath);
            var service = CreateService(paths);

            var result = await service.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = payloadPath,
                ScreenshotPath = screenshotPath
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.BrowserPage, result.Item.Kind);
            Assert.AreEqual("browser-extension", result.Item.SourceApp);
            Assert.AreEqual("Orders for [REDACTED:email-address]", result.Item.SourceWindowTitle);
            Assert.IsTrue(result.Item.FilePath.StartsWith(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(result.Item.FilePath));
            Assert.IsTrue(File.Exists(result.Item.ThumbnailPath));
            Assert.IsFalse(result.Item.Notes!.Contains("fake-token-1234567890", StringComparison.Ordinal));
            StringAssert.Contains(result.Item.Notes!, "Native messaging host registration");
            StringAssert.Contains(result.Item.Notes!, "local extension ZIP packaging");
            StringAssert.Contains(result.Item.Notes!, "bounded stitch-package import");
            StringAssert.Contains(result.Item.Notes!, "browser-store publication");
        });
    }

    [TestMethod]
    public async Task AcceptAsync_ImportsStitchPackageAsBrowserPageCapture()
    {
        await WithTempPathsAsync(async paths =>
        {
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            var packageRoot = Path.Combine(paths.TempRoot, "stitch-package");
            await File.WriteAllTextAsync(payloadPath, ValidPayloadJson());
            WriteStitchPackage(packageRoot);
            var service = CreateService(paths);

            var result = await service.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = payloadPath,
                StitchPackagePath = packageRoot
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.BrowserPage, result.Item.Kind);
            Assert.AreEqual("browser-extension", result.Item.SourceApp);
            Assert.IsTrue(result.Item.FilePath.StartsWith(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(result.Item.FilePath));
            StringAssert.Contains(result.Item.Notes!, "Stitch package:");
            StringAssert.Contains(result.Item.Notes!, "Stitched image bytes:");
            StringAssert.Contains(result.Item.Notes!, "local extension ZIP packaging");
            StringAssert.Contains(result.Item.Notes!, "bounded stitch-package import");
        });
    }

    [TestMethod]
    public async Task AcceptAsync_RejectsInvalidPayloadBeforeImport()
    {
        await WithTempPathsAsync(async paths =>
        {
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            await File.WriteAllTextAsync(
                payloadPath,
                ValidPayloadJson().Replace("\"screenshotConsented\": true", "\"screenshotConsented\": false"));
            var service = CreateService(paths);

            var result = await service.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = payloadPath
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "rejected");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Screenshot consent is required", StringComparison.Ordinal)));
            Assert.IsFalse(File.Exists(Path.Combine(paths.BrowserBridgeRoot, "sample-contract-fixture.redacted.json")));
        });
    }

    [TestMethod]
    public async Task AcceptAsync_RejectsUnsupportedScreenshotExtensionButKeepsRedactedPayload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var payloadPath = Path.Combine(paths.TempRoot, "browser-payload.json");
            var screenshotPath = Path.Combine(paths.TempRoot, "page.txt");
            await File.WriteAllTextAsync(payloadPath, ValidPayloadJson());
            await File.WriteAllTextAsync(screenshotPath, "not an image");
            var service = CreateService(paths);

            var result = await service.AcceptAsync(new BrowserExtensionBridgeRequest
            {
                PayloadPath = payloadPath,
                ScreenshotPath = screenshotPath
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Unsupported browser extension screenshot extension");
            Assert.IsTrue(File.Exists(result.RedactedPayloadPath));
        });
    }

    [TestMethod]
    public async Task DiagnosticsSnapshot_IncludesBrowserBridgeStatus()
    {
        await WithTempServicesAsync(services =>
        {
            var snapshot = services.Diagnostics.GetSnapshot();

            StringAssert.Contains(snapshot.BrowserBridgeStatus, "Browser extension native bridge receiver is available");
            StringAssert.Contains(snapshot.BrowserBridgeStatus, services.Paths.BrowserBridgeRoot);
            StringAssert.Contains(snapshot.BrowserBridgeStatus, "Native messaging host registration");
            StringAssert.Contains(snapshot.BrowserBridgeStatus, "local extension ZIP packaging");
            StringAssert.Contains(snapshot.BrowserBridgeStatus, "bounded stitch-package import");
            StringAssert.Contains(snapshot.BrowserBridgeStatus, "browser-store publication");
            return Task.CompletedTask;
        });
    }

    private static BrowserExtensionNativeBridgeService CreateService(AppPaths paths)
    {
        var settings = new AppSettings();
        var workspace = new WorkspaceStore(paths, settings);
        workspace.AttachMetadataIndex(new WorkspaceMetadataIndex(paths));
        return new BrowserExtensionNativeBridgeService(paths, workspace);
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

            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static async Task WithTempServicesAsync(Func<AppServices, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            using var services = AppServices.Create();
            await action(services);
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

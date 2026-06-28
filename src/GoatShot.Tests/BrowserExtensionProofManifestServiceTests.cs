using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionProofManifestServiceTests
{
    [TestMethod]
    public async Task ValidateAsync_WritesCompleteManifestAndValidationReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var source = CreateExtensionSource(root);
            var packagePath = Path.Combine(root, "goatshot-browser-extension.zip");
            await File.WriteAllTextAsync(packagePath, "package bytes");
            var payloadPath = Path.Combine(root, "payload.json");
            var redactedPath = Path.Combine(root, "redacted-payload.json");
            await File.WriteAllTextAsync(payloadPath, SafePayloadJson());
            await File.WriteAllTextAsync(redactedPath, SafePayloadJson());
            var stitchPackage = CreateStitchPackage(root);
            var importResult = Path.Combine(root, "import-result.json");
            await File.WriteAllTextAsync(importResult, """{"succeeded":true,"message":"Browser extension capture imported"}""");
            WriteRequiredScreenshots(root);
            WriteNestedNonProofImages(root, stitchPackage);
            File.WriteAllText(Path.Combine(root, "safe-fixture-page-edge.png"), "fixture page screenshot");
            WriteStaleUnclassifiedManifest(root);

            var result = await new BrowserExtensionProofManifestService().ValidateAsync(
                new BrowserExtensionProofValidationRequest
                {
                    ProofRootPath = root,
                    ExtensionSourceDirectory = source,
                    ExtensionPackagePath = packagePath,
                    Browser = "chrome",
                    BrowserVersion = "Chrome 126.0.0.0",
                    ExtensionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    FixtureUrl = "http://127.0.0.1:58615/safe-fixture.html",
                    PayloadPath = payloadPath,
                    RedactedPayloadPath = redactedPath,
                    StitchPackagePath = stitchPackage,
                    ImportResultPath = importResult
                },
                NativeStatus(installed: true));

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(File.Exists(result.ValidationPath));
            Assert.AreEqual(0, result.MissingEvidence.Count);
            Assert.AreEqual("goatshot.browser-proof.v1", result.Manifest.SchemaVersion);
            Assert.AreEqual("chrome", result.Manifest.Browser.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Manifest.Extension.SourceSha256));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Manifest.Extension.PackageSha256));
            var requiredRoles = new[]
            {
                "extension-details",
                "popup-consent-defaults",
                "options-consent-defaults",
                "host-status",
                "selected-element-mode",
                "package-export-toggle",
                "last-handoff-result"
            };
            Assert.AreEqual(
                7,
                result.Manifest.Screenshots.Count(screenshot =>
                    screenshot.Exists &&
                    requiredRoles.Contains(screenshot.Role)));
            Assert.IsFalse(result.Manifest.Screenshots.Any(screenshot => screenshot.Role == "unclassified"));

            var validation = await File.ReadAllTextAsync(result.ValidationPath);
            StringAssert.Contains(validation, "Status: `complete`");
            StringAssert.Contains(validation, "browser-store publication");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task ValidateAsync_ReportsMissingEvidenceAndUnredactedPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var source = CreateExtensionSource(root);
            var payloadPath = Path.Combine(root, "payload.json");
            await File.WriteAllTextAsync(payloadPath, """
                {
                  "page": {
                    "url": "https://app.example.test/orders?token=fake-token-1234567890",
                    "title": "Orders for alex@example.test"
                  },
                  "networkEvents": [
                    { "errorText": "Bearer fakebearertoken12345678901234567890" }
                  ]
                }
                """);

            var result = await new BrowserExtensionProofManifestService().ValidateAsync(
                new BrowserExtensionProofValidationRequest
                {
                    ProofRootPath = root,
                    ExtensionSourceDirectory = source,
                    Browser = "chrome",
                    PayloadPath = payloadPath,
                    FixtureUrl = "http://127.0.0.1:58615/safe-fixture.html"
                },
                NativeStatus(installed: false));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.MissingEvidence.Contains("browser version"));
            Assert.IsTrue(result.MissingEvidence.Contains("extension id"));
            Assert.IsTrue(result.MissingEvidence.Contains("extension package"));
            Assert.IsTrue(result.MissingEvidence.Contains("screenshot:host-status"));
            Assert.IsTrue(result.MissingEvidence.Contains("import result"));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("unredacted", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("No browser native-host registration", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(File.Exists(result.ValidationPath));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static string CreateExtensionSource(string root)
    {
        var source = Path.Combine(root, "browser-extension");
        Directory.CreateDirectory(Path.Combine(source, "samples"));
        foreach (var file in new[]
                 {
                     "manifest.json",
                     "content-script.js",
                     "service-worker.js",
                     "popup.html",
                     "popup.js",
                     "options.html",
                     "options.js",
                     "extension-ui.css"
                 })
        {
            File.WriteAllText(Path.Combine(source, file), file == "manifest.json" ? "{}" : string.Empty);
        }

        File.WriteAllText(Path.Combine(source, "samples", "safe-fixture.html"), "<!doctype html>");
        return source;
    }

    private static string CreateStitchPackage(string root)
    {
        var stitchPackage = Path.Combine(root, "GoatShot", "sample-correlation");
        Directory.CreateDirectory(stitchPackage);
        File.WriteAllText(Path.Combine(stitchPackage, "goatshot-stitch-package.json"), "{}");
        return stitchPackage;
    }

    private static void WriteNestedNonProofImages(string root, string stitchPackage)
    {
        File.WriteAllText(Path.Combine(stitchPackage, "stitched.png"), "stitched output is not a proof screenshot");

        var tiles = Path.Combine(stitchPackage, "tiles");
        Directory.CreateDirectory(tiles);
        File.WriteAllText(Path.Combine(tiles, "tile-0000.png"), "tile output is not a proof screenshot");

        var profileIcons = Path.Combine(root, "edge-live-fixture-profile", "Default", "Web Applications", "Manifest Resources", "sample", "Icons");
        Directory.CreateDirectory(profileIcons);
        File.WriteAllText(Path.Combine(profileIcons, "128.png"), "browser profile icon is not a proof screenshot");
    }

    private static void WriteStaleUnclassifiedManifest(string root)
    {
        var stalePath = Path.Combine(root, "edge-live-fixture-profile", "Default", "Web Applications", "Manifest Resources", "sample", "Icons", "128.png")
            .Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(root, "browser-proof-manifest.json"), $$"""
            {
              "schemaVersion": "goatshot.browser-proof.v1",
              "screenshots": [
                {
                  "role": "unclassified",
                  "path": "{{stalePath}}",
                  "exists": true
                }
              ]
            }
            """);
    }

    private static void WriteRequiredScreenshots(string root)
    {
        foreach (var fileName in new[]
                 {
                     "01-extension-details.png",
                     "02-popup-consent-defaults.png",
                     "03-options-consent-defaults.png",
                     "04-host-status.png",
                     "05-selected-element-mode.png",
                     "06-package-export-toggle.png",
                     "07-last-handoff-result.png"
                 })
        {
            File.WriteAllText(Path.Combine(root, fileName), "not a real image; file presence is enough for proof-manifest tests");
        }
    }

    private static BrowserNativeHostStatus NativeStatus(bool installed)
    {
        return new BrowserNativeHostStatus
        {
            HostName = BrowserNativeHostRegistrationService.HostName,
            ManifestRoot = @"C:\GoatShot\native-host",
            Registrations =
            {
                new BrowserNativeHostRegistrationState
                {
                    Browser = BrowserNativeHostBrowser.Chrome,
                    Installed = installed,
                    ManifestPath = installed ? @"C:\GoatShot\native-host\chrome\com.goatshot.bridge.json" : string.Empty,
                    Message = installed
                        ? "Chrome native messaging host is registered."
                        : "Chrome native messaging host is not registered in HKCU."
                }
            }
        };
    }

    private static string SafePayloadJson()
    {
        return """
            {
              "schemaVersion": "goatshot.browser-capture.v1",
              "page": {
                "url": "https://example.test/safe-fixture",
                "title": "Safe Fixture"
              },
              "consent": {
                "screenshotConsented": true,
                "telemetryConsented": false
              }
            }
            """;
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

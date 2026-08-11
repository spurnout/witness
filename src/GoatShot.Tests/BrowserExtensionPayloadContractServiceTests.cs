using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionPayloadContractServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [TestMethod]
    public void RedactUrl_DropsSensitiveAndNonSensitiveFragments()
    {
        var redacted = BrowserExtensionPayloadContractService.RedactUrl(
            "https://app.example.test/callback?view=open#access_token=super-secret");

        Assert.AreEqual("https://app.example.test/callback?view=open", redacted);
    }

    [TestMethod]
    public void Validate_AcceptsConsentedFullPagePayload()
    {
        var payload = CreateValidPayload();

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void Validate_AcceptsLegacyGoatShotSchema()
    {
        var payload = CreateValidPayload();
        payload.SchemaVersion = BrowserExtensionPayloadContractService.LegacySchemaVersion;

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
    }

    [TestMethod]
    public void Validate_RequiresScreenshotConsentSupportedUrlAndDimensions()
    {
        var payload = CreateValidPayload();
        payload.SchemaVersion = "future-version";
        payload.Page.Url = "file:///C:/private/report.html";
        payload.Viewport.Width = 0;
        payload.FullPage.Height = 0;
        payload.Consent.ScreenshotConsented = false;

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues, "Screenshot consent is required before accepting a browser capture payload.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Unsupported schemaVersion", StringComparison.Ordinal)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("HTTP or HTTPS", StringComparison.Ordinal)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Viewport", StringComparison.Ordinal)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Full-page", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_RequiresTelemetryConsentWhenTelemetryIsPresent()
    {
        var payload = CreateValidPayload();
        payload.Intent.IncludeTelemetry = true;
        payload.Consent.TelemetryConsented = false;
        payload.ConsoleEvents.Add(new BrowserExtensionConsoleEvent
        {
            Level = "error",
            Message = "Failed request"
        });

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues, "Telemetry consent is required when console or network metadata is present.");
    }

    [TestMethod]
    public void RedactForStorage_RemovesSensitiveUrlConsoleAndNetworkValues()
    {
        var payload = CreateValidPayload();
        payload.Page.Url = "https://app.example.test/dashboard?token=super-secret-token-12345&filter=open";
        payload.Page.Title = "Customer alex@example.test";
        payload.ConsoleEvents.Add(new BrowserExtensionConsoleEvent
        {
            Level = "error",
            Message = "Bearer abcdefghijklmnopqrstuvwxyz1234567890",
            SourceUrl = "https://app.example.test/app.js?sig=signature-value-12345",
            Line = 12,
            Column = 3
        });
        payload.NetworkEvents.Add(new BrowserExtensionNetworkEvent
        {
            Method = "GET",
            Url = "https://api.example.test/search?api_key=api-key-value-12345&q=orders",
            StatusCode = 401,
            ResourceType = "fetch",
            Initiator = "https://app.example.test/app.js?key=script-key-12345",
            ErrorText = "token=failed-token-12345"
        });

        var redacted = BrowserExtensionPayloadContractService.RedactForStorage(payload);
        var json = JsonSerializer.Serialize(redacted, JsonOptions);

        Assert.IsFalse(json.Contains("super-secret-token", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsFalse(json.Contains("alex@example.test", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsFalse(json.Contains("abcdefghijklmnopqrstuvwxyz", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsFalse(json.Contains("api-key-value", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsTrue(json.Contains("REDACTED", StringComparison.OrdinalIgnoreCase), json);
        Assert.AreEqual(12, redacted.ConsoleEvents[0].Line);
        Assert.AreEqual(401, redacted.NetworkEvents[0].StatusCode);
    }

    [TestMethod]
    public void Validate_AcceptsPlannedStitchManifest()
    {
        var payload = CreateValidPayload();
        payload.Stitch = BrowserExtensionStitchPlannerService.Plan(new BrowserExtensionStitchPlanRequest
        {
            FullPageWidth = payload.FullPage.Width,
            FullPageHeight = payload.FullPage.Height,
            ViewportWidth = payload.Viewport.Width,
            ViewportHeight = payload.Viewport.Height,
            DevicePixelRatio = payload.Viewport.DevicePixelRatio
        });

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.AreEqual(payload.Stitch.Tiles.Count, payload.Stitch.TileCount);
        Assert.IsTrue(payload.Stitch.Tiles.Count > 1);
    }

    [TestMethod]
    public void Validate_RejectsMalformedStitchManifest()
    {
        var payload = CreateValidPayload();
        payload.Stitch.Requested = true;
        payload.Stitch.Status = "planned";
        payload.Stitch.TileCount = 2;
        payload.Stitch.MaxTileCount = 1;
        payload.Stitch.Tiles.Add(new BrowserExtensionStitchTile
        {
            Index = 0,
            X = -1,
            Y = 0,
            Width = 0,
            Height = 100,
            ScrollX = -1,
            ScrollY = 0,
            DevicePixelRatio = 0
        });

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("tileCount", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("width and height", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("non-negative", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Issues.Any(issue => issue.Contains("devicePixelRatio", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Validate_AcceptsAndRedactsSelectedElementAndPackageExportMetadata()
    {
        var payload = CreateValidPayload();
        payload.Intent.CaptureMode = "selected-element";
        payload.SelectedElement = new BrowserExtensionSelectedElementMetadata
        {
            Found = true,
            Strategy = "viewport-center",
            TagName = "button",
            Role = "button",
            InputType = "",
            HasAccessibleName = true,
            AbsoluteRect = new BrowserExtensionRect
            {
                X = 24,
                Y = 120,
                Width = 320,
                Height = 48
            },
            ViewportRect = new BrowserExtensionRect
            {
                X = 24,
                Y = 120,
                Width = 320,
                Height = 48
            },
            Warnings = new()
        };
        payload.StitchPackage = new BrowserExtensionStitchPackageExport
        {
            Requested = true,
            Status = "downloaded",
            Source = "extension-downloads-export",
            DownloadRoot = "GoatShot/customer-alex@example.test",
            ManifestPath = "GoatShot/customer-alex@example.test/goatshot-stitch-package.json",
            FileCount = 3,
            DownloadIds = new() { 101, 102, 103 },
            Message = "Downloaded files for alex@example.test",
            Warnings = new() { "token=secret-token-12345" }
        };

        var result = BrowserExtensionPayloadContractService.Validate(payload);
        var redacted = BrowserExtensionPayloadContractService.RedactForStorage(payload);
        var json = JsonSerializer.Serialize(redacted, JsonOptions);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.AreEqual("button", redacted.SelectedElement.TagName);
        Assert.AreEqual(320, redacted.SelectedElement.AbsoluteRect.Width);
        Assert.IsFalse(json.Contains("alex@example.test", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsFalse(json.Contains("secret-token", StringComparison.OrdinalIgnoreCase), json);
        Assert.IsTrue(json.Contains("REDACTED", StringComparison.OrdinalIgnoreCase), json);
    }

    [TestMethod]
    public void Validate_WarnsWhenSelectedElementModeHasNoGeometry()
    {
        var payload = CreateValidPayload();
        payload.Intent.CaptureMode = "selected-element";
        payload.SelectedElement = new BrowserExtensionSelectedElementMetadata
        {
            Found = false
        };

        var result = BrowserExtensionPayloadContractService.Validate(payload);

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Selected-element", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void SamplePayload_ValidatesAndRedactsBeforePersistence()
    {
        var root = FindWorkspaceRoot();
        var samplePath = Path.Combine(root, "browser-extension", "samples", "full-page-capture-payload.json");
        var json = File.ReadAllText(samplePath);
        var payload = JsonSerializer.Deserialize<BrowserExtensionCapturePayload>(json, JsonOptions);

        Assert.IsNotNull(payload);
        var result = BrowserExtensionPayloadContractService.Validate(payload);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.IsTrue(payload.Stitch.Requested);
        Assert.AreEqual(5, payload.Stitch.TileCount);
        Assert.AreEqual(payload.Stitch.TileCount, payload.Stitch.Tiles.Count);

        var redacted = BrowserExtensionPayloadContractService.RedactForStorage(payload);
        var redactedJson = JsonSerializer.Serialize(redacted, JsonOptions);

        Assert.IsFalse(redactedJson.Contains("fake-token", StringComparison.OrdinalIgnoreCase), redactedJson);
        Assert.IsFalse(redactedJson.Contains("fake-code", StringComparison.OrdinalIgnoreCase), redactedJson);
        Assert.IsFalse(redactedJson.Contains("fake-access-token", StringComparison.OrdinalIgnoreCase), redactedJson);
        Assert.IsFalse(redactedJson.Contains("alex@example.test", StringComparison.OrdinalIgnoreCase), redactedJson);
        Assert.IsTrue(redactedJson.Contains("REDACTED", StringComparison.OrdinalIgnoreCase), redactedJson);
        Assert.AreEqual(5, redacted.Stitch.TileCount);
        Assert.AreEqual("planned", redacted.Stitch.Status);
    }

    private static BrowserExtensionCapturePayload CreateValidPayload()
    {
        return new BrowserExtensionCapturePayload
        {
            SchemaVersion = BrowserExtensionPayloadContractService.CurrentSchemaVersion,
            Intent = new BrowserExtensionCaptureIntent
            {
                CaptureMode = "full-page",
                FullPageCaptureRequested = true,
                IncludeDomMetadata = true,
                IncludeTelemetry = false,
                CorrelationId = "test"
            },
            Page = new BrowserExtensionPageMetadata
            {
                Url = "https://app.example.test/dashboard",
                Title = "Dashboard",
                ContentType = "text/html",
                Language = "en-US",
                CapturedAt = DateTimeOffset.Parse("2026-06-15T04:00:00Z")
            },
            Viewport = new BrowserExtensionViewportMetadata
            {
                Width = 1280,
                Height = 720,
                DevicePixelRatio = 1d
            },
            FullPage = new BrowserExtensionFullPageMetadata
            {
                Width = 1280,
                Height = 2400,
                ScrollWidth = 1280,
                ScrollHeight = 2400
            },
            Consent = new BrowserExtensionConsent
            {
                ScreenshotConsented = true,
                TelemetryConsented = false,
                ConsentText = "User consented to page screenshot metadata.",
                ConsentedAt = DateTimeOffset.Parse("2026-06-15T04:00:00Z")
            }
        };
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GoatShot.slnx from the current test directory.");
    }
}

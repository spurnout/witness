using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionNativeBridgeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<string> ScreenshotExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;

    public BrowserExtensionNativeBridgeService(AppPaths paths, WorkspaceStore workspaceStore)
    {
        _paths = paths;
        _workspaceStore = workspaceStore;
    }

    public string GetStatusSummary()
    {
        return $"Browser extension native bridge receiver is available through CLI handoff and native-host stdio run mode, with local bridge folder {_paths.BrowserBridgeRoot}. Native messaging host registration, manifest generation, local extension ZIP packaging, browser-download stitch-package export, and bounded stitch-package import are available. Live browser fixture proof is validated separately through proof folders; browser-store publication and automatic extension installation remain separate proof lanes.";
    }

    public async Task<BrowserExtensionBridgeResult> AcceptAsync(
        BrowserExtensionBridgeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadPath))
        {
            return BrowserExtensionBridgeResult.Failed("Payload path is required.");
        }

        var payloadPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.PayloadPath));
        if (!File.Exists(payloadPath))
        {
            return BrowserExtensionBridgeResult.Failed($"Browser extension payload was not found: {payloadPath}");
        }

        BrowserExtensionCapturePayload? payload;
        try
        {
            var json = await File.ReadAllTextAsync(payloadPath, cancellationToken);
            payload = JsonSerializer.Deserialize<BrowserExtensionCapturePayload>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return BrowserExtensionBridgeResult.Failed($"Browser extension payload JSON is invalid: {SensitiveTextDetector.Redact(ex.Message)}");
        }

        return await AcceptPayloadAsync(
            payload,
            request.ScreenshotPath,
            request.RedactedOutputPath,
            request.StitchPackagePath,
            cancellationToken: cancellationToken);
    }

    public async Task<BrowserExtensionBridgeResult> AcceptPayloadAsync(
        BrowserExtensionCapturePayload? payload,
        string? screenshotPath = null,
        string? redactedOutputPath = null,
        string? stitchPackagePath = null,
        bool confineImportsToBridgeRoot = false,
        CancellationToken cancellationToken = default)
    {
        var validation = BrowserExtensionPayloadContractService.Validate(payload);
        if (!validation.IsValid || payload is null)
        {
            return new BrowserExtensionBridgeResult
            {
                Succeeded = false,
                Message = "Browser extension payload was rejected.",
                Issues = validation.Issues,
                Warnings = validation.Warnings
            };
        }

        var redacted = BrowserExtensionPayloadContractService.RedactForStorage(payload);
        var redactedPath = await WriteRedactedPayloadAsync(redacted, redactedOutputPath, cancellationToken);
        CaptureItem? item = null;
        var warnings = validation.Warnings.ToList();

        if (!string.IsNullOrWhiteSpace(stitchPackagePath))
        {
            // Same trust boundary as the screenshot path: over native messaging the
            // package location is attacker/extension-controlled, so confine it to the
            // app-owned bridge folder. The stitch service only confines paths *within*
            // the package, not the package root itself.
            if (confineImportsToBridgeRoot &&
                !IsInsideDirectory(
                    _paths.BrowserBridgeRoot,
                    Path.GetFullPath(Environment.ExpandEnvironmentVariables(stitchPackagePath))))
            {
                return new BrowserExtensionBridgeResult
                {
                    Succeeded = false,
                    Message = "Browser stitch package must be located inside the Receipts browser bridge folder for native handoff.",
                    RedactedPayloadPath = redactedPath,
                    Warnings = warnings
                };
            }

            var packageValidation = new BrowserExtensionStitchPackageService().Validate(
                stitchPackagePath,
                redacted.Intent.CorrelationId);
            warnings.AddRange(packageValidation.Warnings);
            if (!packageValidation.IsValid)
            {
                return new BrowserExtensionBridgeResult
                {
                    Succeeded = false,
                    Message = packageValidation.Message,
                    RedactedPayloadPath = redactedPath,
                    Issues = packageValidation.Issues,
                    Warnings = warnings,
                    StitchPackagePath = packageValidation.ManifestPath
                };
            }

            try
            {
                item = await ImportStitchPackageAsync(packageValidation, redacted, redactedPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or InvalidOperationException)
            {
                return new BrowserExtensionBridgeResult
                {
                    Succeeded = false,
                    Message = SensitiveTextDetector.Redact(ex.Message),
                    RedactedPayloadPath = redactedPath,
                    Warnings = warnings,
                    StitchPackagePath = packageValidation.ManifestPath
                };
            }
        }
        else if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            try
            {
                item = await ImportScreenshotAsync(screenshotPath, redacted, redactedPath, confineImportsToBridgeRoot, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or InvalidOperationException)
            {
                return new BrowserExtensionBridgeResult
                {
                    Succeeded = false,
                    Message = SensitiveTextDetector.Redact(ex.Message),
                    RedactedPayloadPath = redactedPath,
                    Warnings = warnings
                };
            }
        }

        return new BrowserExtensionBridgeResult
        {
            Succeeded = true,
            Message = item is null
                ? $"Browser extension payload accepted and redacted metadata stored: {redactedPath}"
                : $"Browser extension capture imported: {item.FileName}",
            RedactedPayloadPath = redactedPath,
            Item = item,
            Warnings = warnings,
            StitchPackagePath = string.IsNullOrWhiteSpace(stitchPackagePath)
                ? string.Empty
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(stitchPackagePath))
        };
    }

    private async Task<string> WriteRedactedPayloadAsync(
        BrowserExtensionCapturePayload payload,
        string? requestedPath,
        CancellationToken cancellationToken)
    {
        var outputPath = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(
                _paths.BrowserBridgeRoot,
                $"{SafeFileName(payload.Intent.CorrelationId, "browser-capture")}.redacted.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        return outputPath;
    }

    private async Task<CaptureItem> ImportScreenshotAsync(
        string screenshotPath,
        BrowserExtensionCapturePayload payload,
        string redactedPayloadPath,
        bool confineToBridgeRoot,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(screenshotPath));

        // When the handoff is driven over native messaging (attacker/extension-controlled),
        // the screenshot must live inside the app-owned bridge folder. Otherwise a
        // compromised extension could import any readable image on disk into the workspace.
        // The operator-driven CLI path leaves confineToBridgeRoot false and stays flexible.
        if (confineToBridgeRoot && !IsInsideDirectory(_paths.BrowserBridgeRoot, fullPath))
        {
            throw new InvalidOperationException(
                "Browser extension screenshot must be located inside the Receipts browser bridge folder for native handoff.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Browser extension screenshot was not found.", fullPath);
        }

        if (!ScreenshotExtensions.Contains(Path.GetExtension(fullPath)))
        {
            throw new InvalidOperationException($"Unsupported browser extension screenshot extension: {Path.GetExtension(fullPath)}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _workspaceStore.ImportFileCopyAsync(
            fullPath,
            BuildNotes(payload, redactedPayloadPath),
            CaptureKind.BrowserPage,
            new CaptureSource
            {
                ProcessName = "browser-extension",
                WindowTitle = payload.Page.Title,
                MonitorName = $"{payload.Viewport.Width}x{payload.Viewport.Height} viewport, {payload.FullPage.Width}x{payload.FullPage.Height} page",
                SourceUrl = payload.Page.Url
            });
    }

    private async Task<CaptureItem> ImportStitchPackageAsync(
        BrowserExtensionStitchPackageValidationResult package,
        BrowserExtensionCapturePayload payload,
        string redactedPayloadPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.StitchedImagePath))
        {
            throw new InvalidOperationException("Browser stitch package did not resolve a stitched image path.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _workspaceStore.ImportFileCopyAsync(
            package.StitchedImagePath,
            BuildNotes(payload, redactedPayloadPath, package),
            CaptureKind.BrowserPage,
            new CaptureSource
            {
                ProcessName = "browser-extension",
                WindowTitle = payload.Page.Title,
                MonitorName = $"{payload.Viewport.Width}x{payload.Viewport.Height} viewport, {payload.FullPage.Width}x{payload.FullPage.Height} page",
                SourceUrl = payload.Page.Url
            });
    }

    private static string BuildNotes(BrowserExtensionCapturePayload payload, string redactedPayloadPath)
    {
        return "Imported from a consented browser extension native handoff." +
            Environment.NewLine +
            $"Page: {payload.Page.Title}" +
            Environment.NewLine +
            $"Url: {payload.Page.Url}" +
            Environment.NewLine +
            $"Viewport: {payload.Viewport.Width}x{payload.Viewport.Height} @ {payload.Viewport.DevicePixelRatio:0.##} DPR" +
            Environment.NewLine +
            $"Full page: {payload.FullPage.Width}x{payload.FullPage.Height}" +
            Environment.NewLine +
            $"Stitch: {BuildStitchSummary(payload.Stitch)}" +
            Environment.NewLine +
            $"Console events: {payload.ConsoleEvents.Count}; network events: {payload.NetworkEvents.Count}" +
            Environment.NewLine +
            $"Redacted payload: {redactedPayloadPath}" +
            Environment.NewLine +
            "Native messaging host registration, local extension ZIP packaging, browser-download stitch-package export, and bounded stitch-package import are available. Live browser fixture proof is validated separately through proof folders; browser-store publication and automatic extension installation remain separate later proof lanes.";
    }

    private static string BuildNotes(
        BrowserExtensionCapturePayload payload,
        string redactedPayloadPath,
        BrowserExtensionStitchPackageValidationResult package)
    {
        return BuildNotes(payload, redactedPayloadPath) +
            Environment.NewLine +
            $"Stitch package: {package.ManifestPath}" +
            Environment.NewLine +
            $"Stitched image bytes: {package.StitchedImageBytes}; tile bytes: {package.TotalTileBytes}";
    }

    private static string BuildStitchSummary(BrowserExtensionStitchManifest stitch)
    {
        if (!stitch.Requested)
        {
            return "not requested";
        }

        var status = string.IsNullOrWhiteSpace(stitch.Status) ? "unknown" : stitch.Status;
        var direction = stitch.HorizontalScrollIncluded ? "vertical+horizontal" : "vertical";
        return $"{status}, {stitch.TileCount} tile(s), {direction}, overlap {stitch.OverlapPixels}px, sticky mitigation {stitch.StickyHeaderMitigationPixels}px";
    }

    private static bool IsInsideDirectory(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFileName(string? value, string fallback)
    {
        value = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }
}

public sealed class BrowserExtensionBridgeRequest
{
    public string PayloadPath { get; set; } = string.Empty;
    public string? ScreenshotPath { get; set; }
    public string? StitchPackagePath { get; set; }
    public string? RedactedOutputPath { get; set; }
}

public sealed class BrowserExtensionBridgeResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RedactedPayloadPath { get; set; } = string.Empty;
    public string StitchPackagePath { get; set; } = string.Empty;
    public CaptureItem? Item { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static BrowserExtensionBridgeResult Failed(string message)
    {
        return new BrowserExtensionBridgeResult
        {
            Succeeded = false,
            Message = message
        };
    }
}

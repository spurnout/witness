using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class BrowserExtensionPayloadContractService
{
    public const string CurrentSchemaVersion = "goatshot.browser-capture.v1";

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token",
        "api_key",
        "apikey",
        "auth",
        "code",
        "key",
        "password",
        "pwd",
        "secret",
        "sig",
        "signature",
        "token"
    };

    public static BrowserExtensionContractValidationResult Validate(BrowserExtensionCapturePayload? payload)
    {
        var result = new BrowserExtensionContractValidationResult();
        if (payload is null)
        {
            result.Issues.Add("Payload is required.");
            return result;
        }

        if (!payload.SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal))
        {
            result.Issues.Add($"Unsupported schemaVersion. Expected {CurrentSchemaVersion}.");
        }

        if (!payload.Consent.ScreenshotConsented)
        {
            result.Issues.Add("Screenshot consent is required before accepting a browser capture payload.");
        }

        if (PayloadContainsTelemetry(payload) && !payload.Consent.TelemetryConsented)
        {
            result.Issues.Add("Telemetry consent is required when console or network metadata is present.");
        }

        if (!IsSupportedPageUrl(payload.Page.Url))
        {
            result.Issues.Add("Page URL must be an absolute HTTP or HTTPS URL.");
        }

        if (payload.Viewport.Width <= 0 || payload.Viewport.Height <= 0)
        {
            result.Issues.Add("Viewport width and height must be positive.");
        }

        if (payload.FullPage.Width <= 0 || payload.FullPage.Height <= 0)
        {
            result.Issues.Add("Full-page width and height must be positive.");
        }

        if (payload.FullPage.Height > 0 &&
            payload.Viewport.Height > 0 &&
            payload.FullPage.Height < payload.Viewport.Height)
        {
            result.Warnings.Add("Full-page height is smaller than viewport height; the extension may have captured incomplete geometry.");
        }

        ValidateSelectedElement(payload, result);
        ValidateStitchManifest(payload, result);
        ValidateStitchPackageExport(payload, result);

        if (payload.ConsoleEvents.Count > 100)
        {
            result.Warnings.Add("Console event list is larger than the recommended 100 event cap.");
        }

        if (payload.NetworkEvents.Count > 200)
        {
            result.Warnings.Add("Network event list is larger than the recommended 200 event cap.");
        }

        return result;
    }

    public static BrowserExtensionCapturePayload RedactForStorage(BrowserExtensionCapturePayload payload)
    {
        return new BrowserExtensionCapturePayload
        {
            SchemaVersion = payload.SchemaVersion,
            Intent = new BrowserExtensionCaptureIntent
            {
                CaptureMode = SensitiveTextDetector.Redact(payload.Intent.CaptureMode),
                FullPageCaptureRequested = payload.Intent.FullPageCaptureRequested,
                IncludeDomMetadata = payload.Intent.IncludeDomMetadata,
                IncludeTelemetry = payload.Intent.IncludeTelemetry,
                CorrelationId = SensitiveTextDetector.Redact(payload.Intent.CorrelationId)
            },
            Page = new BrowserExtensionPageMetadata
            {
                Url = RedactUrl(payload.Page.Url),
                Title = SensitiveTextDetector.Redact(payload.Page.Title),
                Referrer = RedactUrl(payload.Page.Referrer),
                ContentType = SensitiveTextDetector.Redact(payload.Page.ContentType),
                Language = SensitiveTextDetector.Redact(payload.Page.Language),
                CapturedAt = payload.Page.CapturedAt
            },
            Viewport = new BrowserExtensionViewportMetadata
            {
                Width = payload.Viewport.Width,
                Height = payload.Viewport.Height,
                DevicePixelRatio = payload.Viewport.DevicePixelRatio,
                ScrollX = payload.Viewport.ScrollX,
                ScrollY = payload.Viewport.ScrollY
            },
            FullPage = new BrowserExtensionFullPageMetadata
            {
                Width = payload.FullPage.Width,
                Height = payload.FullPage.Height,
                ScrollWidth = payload.FullPage.ScrollWidth,
                ScrollHeight = payload.FullPage.ScrollHeight
            },
            SelectedElement = RedactSelectedElement(payload.SelectedElement),
            Stitch = RedactStitchManifest(payload.Stitch),
            StitchPackage = RedactStitchPackage(payload.StitchPackage),
            Consent = new BrowserExtensionConsent
            {
                ScreenshotConsented = payload.Consent.ScreenshotConsented,
                TelemetryConsented = payload.Consent.TelemetryConsented,
                ConsentText = SensitiveTextDetector.Redact(payload.Consent.ConsentText),
                ConsentedAt = payload.Consent.ConsentedAt
            },
            ConsoleEvents = payload.ConsoleEvents.Select(RedactConsoleEvent).ToList(),
            NetworkEvents = payload.NetworkEvents.Select(RedactNetworkEvent).ToList()
        };
    }

    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return SensitiveTextDetector.Redact(url);
        }

        var redactedUrl = SensitiveTextDetector.Redact(url);
        if (!redactedUrl.Equals(url, StringComparison.Ordinal))
        {
            return redactedUrl;
        }

        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return url;
        }

        var query = uri.Query.TrimStart('?');
        var redactedQuery = string.Join(
            "&",
            query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(RedactQueryPart));

        var baseUrl = uri.GetLeftPart(UriPartial.Path);
        var fragment = string.IsNullOrWhiteSpace(uri.Fragment) ? string.Empty : uri.Fragment;
        return string.IsNullOrWhiteSpace(redactedQuery)
            ? $"{baseUrl}{fragment}"
            : $"{baseUrl}?{redactedQuery}{fragment}";
    }

    private static void ValidateStitchManifest(
        BrowserExtensionCapturePayload payload,
        BrowserExtensionContractValidationResult result)
    {
        var stitch = payload.Stitch;
        if (!stitch.Requested && stitch.Tiles.Count == 0 && stitch.TileCount == 0)
        {
            return;
        }

        if (stitch.Requested && string.IsNullOrWhiteSpace(stitch.Status))
        {
            result.Issues.Add("Stitch status is required when full-page stitching is requested.");
        }

        if (stitch.MaxTileCount <= 0)
        {
            result.Issues.Add("Stitch max tile count must be positive.");
        }

        if (stitch.TileCount != stitch.Tiles.Count)
        {
            result.Issues.Add("Stitch tileCount must match the number of tile entries.");
        }

        if (stitch.MaxTileCount > 0 && stitch.Tiles.Count > stitch.MaxTileCount)
        {
            result.Issues.Add("Stitch tile entries exceed the declared max tile count.");
        }

        if (stitch.Requested &&
            stitch.Tiles.Count == 0 &&
            !stitch.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add("Stitching was requested but no tile entries were provided.");
        }

        for (var i = 0; i < stitch.Tiles.Count; i++)
        {
            var tile = stitch.Tiles[i];
            if (tile.Index != i)
            {
                result.Warnings.Add("Stitch tile indexes are not sequential.");
                break;
            }

            if (tile.Width <= 0 || tile.Height <= 0)
            {
                result.Issues.Add("Stitch tile width and height must be positive.");
            }

            if (tile.X < 0 || tile.Y < 0 || tile.ScrollX < 0 || tile.ScrollY < 0)
            {
                result.Issues.Add("Stitch tile coordinates and scroll positions must be non-negative.");
            }

            if (tile.DevicePixelRatio <= 0)
            {
                result.Issues.Add("Stitch tile devicePixelRatio must be positive.");
            }

            if (payload.FullPage.Width > 0 &&
                tile.X + tile.Width > payload.FullPage.Width + payload.Viewport.Width)
            {
                result.Warnings.Add("A stitch tile extends beyond the reported full-page width.");
            }

            if (payload.FullPage.Height > 0 &&
                tile.Y + tile.Height > payload.FullPage.Height + payload.Viewport.Height)
            {
                result.Warnings.Add("A stitch tile extends beyond the reported full-page height.");
            }
        }
    }

    private static void ValidateSelectedElement(
        BrowserExtensionCapturePayload payload,
        BrowserExtensionContractValidationResult result)
    {
        var selected = payload.SelectedElement;
        if (payload.Intent.CaptureMode.Equals("selected-element", StringComparison.OrdinalIgnoreCase) &&
            !selected.Found)
        {
            result.Warnings.Add("Selected-element capture was requested but no visible element geometry was reported.");
            return;
        }

        if (!selected.Found)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.TagName))
        {
            result.Warnings.Add("Selected-element metadata did not include a tagName.");
        }

        ValidateRect(selected.AbsoluteRect, "Selected-element absoluteRect", result);
        ValidateRect(selected.ViewportRect, "Selected-element viewportRect", result);

        if (payload.FullPage.Width > 0 &&
            selected.AbsoluteRect.X + selected.AbsoluteRect.Width > payload.FullPage.Width + payload.Viewport.Width)
        {
            result.Warnings.Add("Selected-element absoluteRect extends beyond the reported full-page width.");
        }

        if (payload.FullPage.Height > 0 &&
            selected.AbsoluteRect.Y + selected.AbsoluteRect.Height > payload.FullPage.Height + payload.Viewport.Height)
        {
            result.Warnings.Add("Selected-element absoluteRect extends beyond the reported full-page height.");
        }
    }

    private static void ValidateStitchPackageExport(
        BrowserExtensionCapturePayload payload,
        BrowserExtensionContractValidationResult result)
    {
        var package = payload.StitchPackage;
        if (!package.Requested &&
            string.IsNullOrWhiteSpace(package.Status) &&
            string.IsNullOrWhiteSpace(package.DownloadRoot) &&
            package.FileCount == 0)
        {
            return;
        }

        if (package.Requested && string.IsNullOrWhiteSpace(package.Status))
        {
            result.Warnings.Add("Stitch package export was requested but no export status was reported.");
        }

        if (package.FileCount < 0)
        {
            result.Issues.Add("Stitch package fileCount must not be negative.");
        }

        if (package.Requested &&
            package.Status.Equals("downloaded", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(package.ManifestPath))
        {
            result.Warnings.Add("Stitch package export reported downloaded status without a manifest path hint.");
        }
    }

    private static void ValidateRect(
        BrowserExtensionRect rect,
        string label,
        BrowserExtensionContractValidationResult result)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            result.Issues.Add($"{label} width and height must be positive.");
        }

        if (rect.X < 0 || rect.Y < 0)
        {
            result.Issues.Add($"{label} coordinates must be non-negative.");
        }
    }

    private static BrowserExtensionStitchManifest RedactStitchManifest(BrowserExtensionStitchManifest stitch)
    {
        return new BrowserExtensionStitchManifest
        {
            Requested = stitch.Requested,
            Mode = SensitiveTextDetector.Redact(stitch.Mode),
            Status = SensitiveTextDetector.Redact(stitch.Status),
            CaptureApi = SensitiveTextDetector.Redact(stitch.CaptureApi),
            TileCount = stitch.TileCount,
            MaxTileCount = stitch.MaxTileCount,
            OverlapPixels = stitch.OverlapPixels,
            StickyHeaderMitigationPixels = stitch.StickyHeaderMitigationPixels,
            HorizontalScrollIncluded = stitch.HorizontalScrollIncluded,
            Tiles = stitch.Tiles.Select(tile => new BrowserExtensionStitchTile
            {
                Index = tile.Index,
                X = tile.X,
                Y = tile.Y,
                Width = tile.Width,
                Height = tile.Height,
                ScrollX = tile.ScrollX,
                ScrollY = tile.ScrollY,
                DevicePixelRatio = tile.DevicePixelRatio,
                CaptureState = SensitiveTextDetector.Redact(tile.CaptureState),
                ArtifactName = SensitiveTextDetector.Redact(tile.ArtifactName),
                Error = SensitiveTextDetector.Redact(tile.Error)
            }).ToList(),
            Warnings = stitch.Warnings.Select(SensitiveTextDetector.Redact).ToList()
        };
    }

    private static BrowserExtensionSelectedElementMetadata RedactSelectedElement(BrowserExtensionSelectedElementMetadata selected)
    {
        return new BrowserExtensionSelectedElementMetadata
        {
            Found = selected.Found,
            Strategy = SensitiveTextDetector.Redact(selected.Strategy),
            TagName = SensitiveTextDetector.Redact(selected.TagName),
            Role = SensitiveTextDetector.Redact(selected.Role),
            InputType = SensitiveTextDetector.Redact(selected.InputType),
            HasAccessibleName = selected.HasAccessibleName,
            AbsoluteRect = CopyRect(selected.AbsoluteRect),
            ViewportRect = CopyRect(selected.ViewportRect),
            Warnings = selected.Warnings.Select(SensitiveTextDetector.Redact).ToList()
        };
    }

    private static BrowserExtensionStitchPackageExport RedactStitchPackage(BrowserExtensionStitchPackageExport stitchPackage)
    {
        return new BrowserExtensionStitchPackageExport
        {
            Requested = stitchPackage.Requested,
            Status = SensitiveTextDetector.Redact(stitchPackage.Status),
            Source = SensitiveTextDetector.Redact(stitchPackage.Source),
            DownloadRoot = SensitiveTextDetector.Redact(stitchPackage.DownloadRoot),
            ManifestPath = SensitiveTextDetector.Redact(stitchPackage.ManifestPath),
            FileCount = stitchPackage.FileCount,
            DownloadIds = stitchPackage.DownloadIds.ToList(),
            Message = SensitiveTextDetector.Redact(stitchPackage.Message),
            Warnings = stitchPackage.Warnings.Select(SensitiveTextDetector.Redact).ToList()
        };
    }

    private static BrowserExtensionRect CopyRect(BrowserExtensionRect rect)
    {
        return new BrowserExtensionRect
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height
        };
    }

    private static BrowserExtensionConsoleEvent RedactConsoleEvent(BrowserExtensionConsoleEvent entry)
    {
        return new BrowserExtensionConsoleEvent
        {
            Level = SensitiveTextDetector.Redact(entry.Level),
            Message = SensitiveTextDetector.Redact(entry.Message),
            SourceUrl = RedactUrl(entry.SourceUrl),
            Line = entry.Line,
            Column = entry.Column
        };
    }

    private static BrowserExtensionNetworkEvent RedactNetworkEvent(BrowserExtensionNetworkEvent entry)
    {
        return new BrowserExtensionNetworkEvent
        {
            Method = SensitiveTextDetector.Redact(entry.Method),
            Url = RedactUrl(entry.Url),
            StatusCode = entry.StatusCode,
            ResourceType = SensitiveTextDetector.Redact(entry.ResourceType),
            Initiator = RedactUrl(entry.Initiator),
            ErrorText = SensitiveTextDetector.Redact(entry.ErrorText)
        };
    }

    private static string RedactQueryPart(string part)
    {
        var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return SensitiveQueryKeys.Contains(Uri.UnescapeDataString(part))
                ? $"{part}=[REDACTED:query-token]"
                : SensitiveTextDetector.Redact(part);
        }

        var rawKey = part[..equalsIndex];
        var rawValue = part[(equalsIndex + 1)..];
        var key = Uri.UnescapeDataString(rawKey);
        if (SensitiveQueryKeys.Contains(key))
        {
            return $"{rawKey}=[REDACTED:query-token]";
        }

        var value = Uri.UnescapeDataString(rawValue);
        var redactedValue = SensitiveTextDetector.Redact(value);
        return redactedValue.Equals(value, StringComparison.Ordinal)
            ? part
            : $"{rawKey}={Uri.EscapeDataString(redactedValue)}";
    }

    private static bool PayloadContainsTelemetry(BrowserExtensionCapturePayload payload)
    {
        return payload.Intent.IncludeTelemetry ||
            payload.ConsoleEvents.Count > 0 ||
            payload.NetworkEvents.Count > 0;
    }

    private static bool IsSupportedPageUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}

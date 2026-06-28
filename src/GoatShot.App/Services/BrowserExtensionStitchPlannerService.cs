using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class BrowserExtensionStitchPlannerService
{
    public const int DefaultMaxTileCount = 80;
    public const int DefaultOverlapPixels = 64;

    public static BrowserExtensionStitchManifest Plan(BrowserExtensionStitchPlanRequest request)
    {
        var fullWidth = Math.Max(0, request.FullPageWidth);
        var fullHeight = Math.Max(0, request.FullPageHeight);
        var viewportWidth = Math.Max(0, request.ViewportWidth);
        var viewportHeight = Math.Max(0, request.ViewportHeight);
        var maxTiles = Math.Clamp(request.MaxTileCount <= 0 ? DefaultMaxTileCount : request.MaxTileCount, 1, 500);
        var overlap = Math.Clamp(request.OverlapPixels < 0 ? DefaultOverlapPixels : request.OverlapPixels, 0, Math.Max(0, Math.Min(viewportWidth, viewportHeight) - 1));
        var sticky = Math.Clamp(request.StickyHeaderMitigationPixels, 0, Math.Max(0, viewportHeight - 1));
        var manifest = new BrowserExtensionStitchManifest
        {
            Requested = request.Requested,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "full-page" : request.Mode.Trim(),
            Status = "planned",
            CaptureApi = string.IsNullOrWhiteSpace(request.CaptureApi) ? "tabs.captureVisibleTab" : request.CaptureApi.Trim(),
            MaxTileCount = maxTiles,
            OverlapPixels = overlap,
            StickyHeaderMitigationPixels = sticky
        };

        if (!request.Requested)
        {
            manifest.Status = "not-requested";
            return manifest;
        }

        if (fullWidth <= 0 || fullHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            manifest.Status = "blocked";
            manifest.Warnings.Add("Full-page and viewport dimensions must be positive before tile planning.");
            return manifest;
        }

        var includeHorizontal = request.IncludeHorizontalScroll || fullWidth > viewportWidth;
        var xPositions = includeHorizontal
            ? BuildPositions(fullWidth, viewportWidth, overlap)
            : new List<int> { 0 };
        var yStepOverlap = Math.Clamp(overlap + sticky, 0, Math.Max(0, viewportHeight - 1));
        var yPositions = BuildPositions(fullHeight, viewportHeight, yStepOverlap);

        var index = 0;
        foreach (var y in yPositions)
        {
            foreach (var x in xPositions)
            {
                if (manifest.Tiles.Count >= maxTiles)
                {
                    manifest.Status = "planned-partial";
                    manifest.Warnings.Add($"Tile plan exceeded the max tile count of {maxTiles}; only the first {maxTiles} tiles are included.");
                    manifest.TileCount = manifest.Tiles.Count;
                    manifest.HorizontalScrollIncluded = xPositions.Count > 1;
                    return manifest;
                }

                manifest.Tiles.Add(new BrowserExtensionStitchTile
                {
                    Index = index++,
                    X = x,
                    Y = y,
                    Width = Math.Min(viewportWidth, fullWidth - x),
                    Height = Math.Min(viewportHeight, fullHeight - y),
                    ScrollX = x,
                    ScrollY = y,
                    DevicePixelRatio = request.DevicePixelRatio <= 0 ? 1d : request.DevicePixelRatio,
                    CaptureState = "planned"
                });
            }
        }

        manifest.TileCount = manifest.Tiles.Count;
        manifest.HorizontalScrollIncluded = xPositions.Count > 1;
        if (manifest.TileCount == 0)
        {
            manifest.Status = "blocked";
            manifest.Warnings.Add("No tiles were planned for the requested page geometry.");
        }

        return manifest;
    }

    private static List<int> BuildPositions(int total, int viewport, int overlap)
    {
        if (total <= viewport)
        {
            return new List<int> { 0 };
        }

        var step = Math.Max(1, viewport - overlap);
        var lastStart = Math.Max(0, total - viewport);
        var positions = new List<int> { 0 };
        while (positions[^1] < lastStart)
        {
            var next = positions[^1] + step;
            if (next >= lastStart)
            {
                next = lastStart;
            }

            if (next == positions[^1])
            {
                break;
            }

            positions.Add(next);
        }

        return positions;
    }
}

public sealed class BrowserExtensionStitchPlanRequest
{
    public bool Requested { get; set; } = true;
    public string Mode { get; set; } = "full-page";
    public string CaptureApi { get; set; } = "tabs.captureVisibleTab";
    public int FullPageWidth { get; set; }
    public int FullPageHeight { get; set; }
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }
    public double DevicePixelRatio { get; set; } = 1d;
    public bool IncludeHorizontalScroll { get; set; }
    public int MaxTileCount { get; set; } = BrowserExtensionStitchPlannerService.DefaultMaxTileCount;
    public int OverlapPixels { get; set; } = BrowserExtensionStitchPlannerService.DefaultOverlapPixels;
    public int StickyHeaderMitigationPixels { get; set; }
}

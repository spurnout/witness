(() => {
  const schemaVersion = "goatshot.browser-capture.v1";
  const stitchPackageSchemaVersion = "goatshot.browser-stitch-package.v1";
  const requestType = "GOATSHOT_COLLECT_PAGE_CAPTURE";
  const responseType = "GOATSHOT_PAGE_CAPTURE_PAYLOAD";
  const nativeResultType = "GOATSHOT_NATIVE_RECEIVE_RESULT";
  const defaultMaxTiles = 80;
  const defaultOverlapPixels = 64;
  const defaultMaxStitchedPixels = 32000000;

  function numberOrZero(value) {
    return Number.isFinite(value) ? Math.round(value) : 0;
  }

  function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
  }

  function sleep(milliseconds) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
  }

  function sanitizePathSegment(value) {
    const sanitized = String(value || "capture")
      .replace(/[^A-Za-z0-9._-]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 80);
    return sanitized || "capture";
  }

  function collectNetworkEvents() {
    return performance
      .getEntriesByType("resource")
      .slice(-100)
      .map((entry) => ({
        method: "",
        url: entry.name || "",
        statusCode: 0,
        resourceType: entry.initiatorType || "",
        initiator: "",
        errorText: ""
      }));
  }

  function buildPositions(total, viewport, overlap) {
    if (total <= viewport) {
      return [0];
    }

    const positions = [0];
    const lastStart = Math.max(0, total - viewport);
    const step = Math.max(1, viewport - overlap);
    while (positions[positions.length - 1] < lastStart) {
      let next = positions[positions.length - 1] + step;
      if (next >= lastStart) {
        next = lastStart;
      }

      if (next === positions[positions.length - 1]) {
        break;
      }

      positions.push(next);
    }

    return positions;
  }

  function isUsefulElement(element) {
    if (!element || element === document.body || element === document.documentElement) {
      return false;
    }

    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function rectToMetadata(rect, scrollX, scrollY) {
    return {
      x: numberOrZero(rect.left + scrollX),
      y: numberOrZero(rect.top + scrollY),
      width: Math.max(1, numberOrZero(rect.width)),
      height: Math.max(1, numberOrZero(rect.height))
    };
  }

  function rectToViewportMetadata(rect) {
    return {
      x: numberOrZero(rect.left),
      y: numberOrZero(rect.top),
      width: Math.max(1, numberOrZero(rect.width)),
      height: Math.max(1, numberOrZero(rect.height))
    };
  }

  function buildSelectedElementMetadata() {
    let element = document.activeElement;
    let strategy = "active-element";
    if (!isUsefulElement(element)) {
      element = document.elementFromPoint(
        Math.max(1, Math.floor(window.innerWidth / 2)),
        Math.max(1, Math.floor(window.innerHeight / 2))
      );
      strategy = "viewport-center";
    }

    if (!isUsefulElement(element)) {
      return {
        found: false,
        strategy: "none",
        tagName: "",
        role: "",
        inputType: "",
        hasAccessibleName: false,
        absoluteRect: { x: 0, y: 0, width: 0, height: 0 },
        viewportRect: { x: 0, y: 0, width: 0, height: 0 },
        warnings: ["No selected or centered element with visible geometry was found."]
      };
    }

    const rect = element.getBoundingClientRect();
    const tagName = (element.tagName || "").toLowerCase();
    return {
      found: true,
      strategy,
      tagName,
      role: element.getAttribute("role") || "",
      inputType: tagName === "input" ? (element.getAttribute("type") || "") : "",
      hasAccessibleName: Boolean(
        element.getAttribute("aria-label") ||
        element.getAttribute("aria-labelledby") ||
        element.getAttribute("title")
      ),
      absoluteRect: rectToMetadata(rect, window.scrollX, window.scrollY),
      viewportRect: rectToViewportMetadata(rect),
      warnings: []
    };
  }

  function buildCaptureArea(fullPage, selectedElement, options) {
    if (options.captureMode === "selected-element" && selectedElement?.found) {
      const selected = selectedElement.absoluteRect;
      const x = clamp(selected.x, 0, Math.max(0, fullPage.width - 1));
      const y = clamp(selected.y, 0, Math.max(0, fullPage.height - 1));
      return {
        x,
        y,
        width: clamp(selected.width, 1, Math.max(1, fullPage.width - x)),
        height: clamp(selected.height, 1, Math.max(1, fullPage.height - y))
      };
    }

    return {
      x: 0,
      y: 0,
      width: fullPage.width,
      height: fullPage.height
    };
  }

  function buildStitchManifest(fullPage, viewport, selectedElement, options) {
    const requested = options.fullPageCaptureRequested !== false;
    const maxTiles = clamp(numberOrZero(options.maxTiles || defaultMaxTiles), 1, 500);
    const overlap = clamp(
      numberOrZero(options.tileOverlapPixels ?? defaultOverlapPixels),
      0,
      Math.max(0, Math.min(viewport.width, viewport.height) - 1)
    );
    const sticky = clamp(
      numberOrZero(options.stickyHeaderMitigationPixels || 0),
      0,
      Math.max(0, viewport.height - 1)
    );
    const captureArea = buildCaptureArea(fullPage, selectedElement, options);
    const manifest = {
      requested,
      mode: options.captureMode || "full-page",
      status: requested ? "planned" : "not-requested",
      captureApi: "tabs.captureVisibleTab",
      tileCount: 0,
      maxTileCount: maxTiles,
      overlapPixels: overlap,
      stickyHeaderMitigationPixels: sticky,
      horizontalScrollIncluded: false,
      tiles: [],
      warnings: []
    };

    if (!requested) {
      return manifest;
    }

    if (options.captureMode === "selected-element" && !selectedElement?.found) {
      manifest.status = "blocked";
      manifest.warnings.push("Selected-element capture was requested, but no visible element geometry was found.");
      return manifest;
    }

    if (fullPage.width <= 0 || fullPage.height <= 0 || viewport.width <= 0 || viewport.height <= 0) {
      manifest.status = "blocked";
      manifest.warnings.push("Full-page and viewport dimensions must be positive before tile planning.");
      return manifest;
    }

    const includeHorizontal =
      options.includeHorizontalScroll === true ||
      captureArea.width > viewport.width ||
      fullPage.width > viewport.width;
    const xPositions = includeHorizontal
      ? buildPositions(captureArea.width, viewport.width, overlap)
      : [0];
    const yPositions = buildPositions(
      captureArea.height,
      viewport.height,
      clamp(overlap + sticky, 0, Math.max(0, viewport.height - 1))
    );
    let index = 0;

    for (const relativeY of yPositions) {
      for (const relativeX of xPositions) {
        if (manifest.tiles.length >= maxTiles) {
          manifest.status = "planned-partial";
          manifest.warnings.push(`Tile plan exceeded the max tile count of ${maxTiles}; only the first ${maxTiles} tiles are included.`);
          manifest.tileCount = manifest.tiles.length;
          manifest.horizontalScrollIncluded = xPositions.length > 1;
          return manifest;
        }

        const x = captureArea.x + relativeX;
        const y = captureArea.y + relativeY;
        manifest.tiles.push({
          index,
          x,
          y,
          width: Math.min(viewport.width, captureArea.width - relativeX),
          height: Math.min(viewport.height, captureArea.height - relativeY),
          scrollX: clamp(x, 0, Math.max(0, fullPage.width - viewport.width)),
          scrollY: clamp(y, 0, Math.max(0, fullPage.height - viewport.height)),
          devicePixelRatio: viewport.devicePixelRatio || 1,
          captureState: "planned",
          artifactName: "",
          error: ""
        });
        index += 1;
      }
    }

    manifest.tileCount = manifest.tiles.length;
    manifest.horizontalScrollIncluded = xPositions.length > 1;
    return manifest;
  }

  function requestVisibleTabCapture(includeDataUrl) {
    if (typeof chrome === "undefined" || !chrome.runtime?.sendMessage) {
      return Promise.resolve({ succeeded: false, message: "Chrome runtime messaging is unavailable." });
    }

    return new Promise((resolve) => {
      chrome.runtime.sendMessage(
        {
          type: "GOATSHOT_CAPTURE_VISIBLE_TAB",
          format: "png",
          includeDataUrl: includeDataUrl === true
        },
        (response) => {
          if (chrome.runtime.lastError) {
            resolve({ succeeded: false, message: chrome.runtime.lastError.message });
            return;
          }

          resolve(response || { succeeded: false, message: "No capture response was returned." });
        }
      );
    });
  }

  function downloadStitchFile(filename, dataUrl) {
    if (typeof chrome === "undefined" || !chrome.runtime?.sendMessage) {
      return Promise.resolve({ succeeded: false, message: "Chrome runtime messaging is unavailable." });
    }

    return new Promise((resolve) => {
      chrome.runtime.sendMessage(
        {
          type: "GOATSHOT_DOWNLOAD_STITCH_FILE",
          filename,
          dataUrl
        },
        (response) => {
          if (chrome.runtime.lastError) {
            resolve({ succeeded: false, message: chrome.runtime.lastError.message });
            return;
          }

          resolve(response || { succeeded: false, message: "No download response was returned." });
        }
      );
    });
  }

  function dataUrlByteLength(dataUrl) {
    const comma = dataUrl.indexOf(",");
    if (comma < 0) {
      return 0;
    }

    const base64 = dataUrl.slice(comma + 1);
    const padding = base64.endsWith("==") ? 2 : base64.endsWith("=") ? 1 : 0;
    return Math.max(0, Math.floor((base64.length * 3) / 4) - padding);
  }

  function dataUrlToBytes(dataUrl) {
    const comma = dataUrl.indexOf(",");
    if (comma < 0) {
      return new Uint8Array();
    }

    const binary = atob(dataUrl.slice(comma + 1));
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) {
      bytes[i] = binary.charCodeAt(i);
    }

    return bytes;
  }

  async function sha256DataUrl(dataUrl) {
    if (!crypto?.subtle) {
      return "";
    }

    try {
      const hash = await crypto.subtle.digest("SHA-256", dataUrlToBytes(dataUrl));
      return Array.from(new Uint8Array(hash))
        .map((byte) => byte.toString(16).padStart(2, "0"))
        .join("");
    } catch {
      return "";
    }
  }

  function textDataUrl(text) {
    const bytes = new TextEncoder().encode(text);
    let binary = "";
    const chunkSize = 0x8000;
    for (let i = 0; i < bytes.length; i += chunkSize) {
      const chunk = bytes.subarray(i, i + chunkSize);
      binary += String.fromCharCode(...chunk);
    }

    return `data:application/json;base64,${btoa(binary)}`;
  }

  function loadImage(dataUrl) {
    return new Promise((resolve, reject) => {
      const image = new Image();
      image.onload = () => resolve(image);
      image.onerror = () => reject(new Error("Captured tile image could not be decoded."));
      image.src = dataUrl;
    });
  }

  function computeTileBounds(capturedTiles) {
    const minX = Math.min(...capturedTiles.map((entry) => entry.tile.x));
    const minY = Math.min(...capturedTiles.map((entry) => entry.tile.y));
    const maxX = Math.max(...capturedTiles.map((entry) => entry.tile.x + entry.tile.width));
    const maxY = Math.max(...capturedTiles.map((entry) => entry.tile.y + entry.tile.height));
    return {
      x: minX,
      y: minY,
      width: Math.max(1, maxX - minX),
      height: Math.max(1, maxY - minY)
    };
  }

  async function buildStitchedImageDataUrl(capturedTiles, options) {
    const warnings = [];
    if (capturedTiles.length === 0) {
      return {
        dataUrl: "",
        warnings: ["No captured tile images were available for stitching."]
      };
    }

    const dpr = Math.max(1, ...capturedTiles.map((entry) => entry.tile.devicePixelRatio || 1));
    const bounds = computeTileBounds(capturedTiles);
    const width = Math.max(1, Math.ceil(bounds.width * dpr));
    const height = Math.max(1, Math.ceil(bounds.height * dpr));
    const maxPixels = clamp(
      numberOrZero(options.maxStitchedPixels || defaultMaxStitchedPixels),
      1000000,
      150000000
    );

    if (width * height > maxPixels) {
      warnings.push(`Stitched canvas would exceed the ${maxPixels} pixel safety cap; exported the first tile as a bounded preview image.`);
      return {
        dataUrl: capturedTiles[0].dataUrl,
        warnings
      };
    }

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) {
      return {
        dataUrl: capturedTiles[0].dataUrl,
        warnings: ["Canvas stitching was unavailable; exported the first tile as a bounded preview image."]
      };
    }

    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, width, height);
    for (const entry of capturedTiles) {
      const image = await loadImage(entry.dataUrl);
      const destinationX = Math.round((entry.tile.x - bounds.x) * dpr);
      const destinationY = Math.round((entry.tile.y - bounds.y) * dpr);
      const destinationWidth = Math.max(1, Math.round(entry.tile.width * dpr));
      const destinationHeight = Math.max(1, Math.round(entry.tile.height * dpr));
      context.drawImage(
        image,
        0,
        0,
        Math.min(image.width, destinationWidth),
        Math.min(image.height, destinationHeight),
        destinationX,
        destinationY,
        destinationWidth,
        destinationHeight
      );
    }

    return {
      dataUrl: canvas.toDataURL("image/png"),
      warnings
    };
  }

  async function exportStitchPackage(correlationId, stitch, capturedTiles, options) {
    const result = {
      requested: options.exportStitchPackage === true,
      status: options.exportStitchPackage === true ? "blocked" : "not-requested",
      source: "extension-downloads-export",
      downloadRoot: "",
      manifestPath: "",
      fileCount: 0,
      downloadIds: [],
      message: "",
      warnings: []
    };

    if (options.exportStitchPackage !== true) {
      return result;
    }

    if (capturedTiles.length === 0) {
      result.message = "No tile images were captured, so no stitch package was exported.";
      result.warnings.push(result.message);
      return result;
    }

    const safeCorrelation = sanitizePathSegment(correlationId || `capture-${Date.now()}`);
    const root = `GoatShot/${safeCorrelation}`;
    result.downloadRoot = root;
    result.manifestPath = `${root}/goatshot-stitch-package.json`;

    const stitched = await buildStitchedImageDataUrl(capturedTiles, options);
    if (!stitched.dataUrl) {
      result.message = "Stitched image generation failed.";
      result.warnings.push(...stitched.warnings);
      return result;
    }

    result.warnings.push(...stitched.warnings);
    const stitchedDownload = await downloadStitchFile(`${root}/stitched.png`, stitched.dataUrl);
    if (!stitchedDownload.succeeded) {
      result.message = stitchedDownload.message || "Stitched image download failed.";
      result.warnings.push(result.message);
      return result;
    }

    result.downloadIds.push(stitchedDownload.downloadId);
    result.fileCount += 1;
    const packageTiles = [];
    for (const entry of capturedTiles) {
      const name = `tile-${String(entry.tile.index).padStart(4, "0")}.png`;
      const path = `tiles/${name}`;
      const download = await downloadStitchFile(`${root}/${path}`, entry.dataUrl);
      if (!download.succeeded) {
        result.status = "partial";
        result.warnings.push(download.message || `Tile ${entry.tile.index} download failed.`);
        continue;
      }

      result.downloadIds.push(download.downloadId);
      result.fileCount += 1;
      packageTiles.push({
        index: entry.tile.index,
        path,
        bytes: dataUrlByteLength(entry.dataUrl),
        sha256: await sha256DataUrl(entry.dataUrl),
        captureState: entry.tile.captureState
      });
    }

    const manifest = {
      schemaVersion: stitchPackageSchemaVersion,
      correlationId: correlationId || "",
      createdAt: new Date().toISOString(),
      source: "extension-downloads-export",
      stitchedImagePath: "stitched.png",
      tiles: packageTiles,
      warnings: [...stitch.warnings, ...result.warnings]
    };
    const manifestDownload = await downloadStitchFile(
      result.manifestPath,
      textDataUrl(JSON.stringify(manifest, null, 2))
    );
    if (!manifestDownload.succeeded) {
      result.status = "partial";
      result.message = manifestDownload.message || "Stitch package manifest download failed.";
      result.warnings.push(result.message);
      return result;
    }

    result.downloadIds.push(manifestDownload.downloadId);
    result.fileCount += 1;
    result.status = result.status === "partial" ? "partial" : "downloaded";
    result.message = `Downloaded stitch package files under Downloads/${root}.`;
    return result;
  }

  async function capturePlannedTiles(manifest, options) {
    const stitchPackage = {
      requested: options.exportStitchPackage === true,
      status: options.exportStitchPackage === true ? "blocked" : "not-requested",
      source: "extension-downloads-export",
      downloadRoot: "",
      manifestPath: "",
      fileCount: 0,
      downloadIds: [],
      message: "",
      warnings: []
    };

    if (!manifest.requested || manifest.tiles.length === 0) {
      return { capturedTiles: [], stitchPackage };
    }

    const includeDataUrl = options.exportStitchPackage === true;
    const originalX = window.scrollX;
    const originalY = window.scrollY;
    const settleDelayMs = clamp(numberOrZero(options.settleDelayMs || 120), 0, 2000);
    const capturedTiles = [];
    let captured = 0;
    try {
      for (const tile of manifest.tiles) {
        window.scrollTo(tile.scrollX, tile.scrollY);
        await sleep(settleDelayMs);
        const response = await requestVisibleTabCapture(includeDataUrl);
        if (response?.succeeded) {
          tile.captureState = includeDataUrl ? "captured" : "captured-metadata-only";
          tile.artifactName = `tile-${String(tile.index).padStart(4, "0")}.png`;
          captured += 1;
          if (includeDataUrl && response.dataUrl) {
            capturedTiles.push({
              tile: { ...tile },
              dataUrl: response.dataUrl
            });
          }
        } else {
          tile.captureState = "failed";
          tile.error = response?.message || "Visible-tab capture failed.";
          manifest.status = "capture-partial";
        }
      }
    } finally {
      window.scrollTo(originalX, originalY);
    }

    if (captured === manifest.tiles.length) {
      manifest.status = includeDataUrl ? "captured" : "captured-metadata-only";
    } else if (captured > 0 && manifest.status !== "capture-partial") {
      manifest.status = "capture-partial";
    }

    if (!includeDataUrl) {
      manifest.warnings.push("Tile bitmap bytes are not embedded in the native payload; enable stitch package export to write local package files through browser downloads.");
      return { capturedTiles, stitchPackage };
    }

    return {
      capturedTiles,
      stitchPackage: await exportStitchPackage(options.correlationId || "", manifest, capturedTiles, options)
    };
  }

  function collectGeometry() {
    const documentElement = document.documentElement;
    const body = document.body;
    const viewport = {
      width: numberOrZero(window.innerWidth),
      height: numberOrZero(window.innerHeight),
      devicePixelRatio: window.devicePixelRatio || 1,
      scrollX: numberOrZero(window.scrollX),
      scrollY: numberOrZero(window.scrollY)
    };
    const fullPage = {
      width: Math.max(
        numberOrZero(documentElement.scrollWidth),
        numberOrZero(body ? body.scrollWidth : 0),
        viewport.width
      ),
      height: Math.max(
        numberOrZero(documentElement.scrollHeight),
        numberOrZero(body ? body.scrollHeight : 0),
        viewport.height
      ),
      scrollWidth: numberOrZero(documentElement.scrollWidth),
      scrollHeight: numberOrZero(documentElement.scrollHeight)
    };

    return { viewport, fullPage };
  }

  async function buildPayload(options) {
    const telemetryConsented = options.telemetryConsented === true;
    const { viewport, fullPage } = collectGeometry();
    const selectedElement = buildSelectedElementMetadata();
    const stitch = buildStitchManifest(fullPage, viewport, selectedElement, options);
    let stitchPackage = {
      requested: options.exportStitchPackage === true,
      status: options.exportStitchPackage === true ? "blocked" : "not-requested",
      source: "extension-downloads-export",
      downloadRoot: "",
      manifestPath: "",
      fileCount: 0,
      downloadIds: [],
      message: options.exportStitchPackage === true
        ? "Tile capture did not run."
        : "",
      warnings: []
    };

    if ((options.captureTiles === true || options.exportStitchPackage === true) && options.screenshotConsented === true) {
      const captureResult = await capturePlannedTiles(stitch, options);
      stitchPackage = captureResult.stitchPackage;
    } else if (options.exportStitchPackage === true) {
      stitchPackage.message = "Screenshot consent is required before exporting a stitch package.";
      stitchPackage.warnings.push(stitchPackage.message);
    }

    return {
      schemaVersion,
      intent: {
        captureMode: options.captureMode || "full-page",
        fullPageCaptureRequested: options.fullPageCaptureRequested !== false,
        includeDomMetadata: true,
        includeTelemetry: telemetryConsented,
        correlationId: options.correlationId || ""
      },
      page: {
        url: location.href,
        title: document.title || "",
        referrer: document.referrer || "",
        contentType: document.contentType || "",
        language: document.documentElement.lang || navigator.language || "",
        capturedAt: new Date().toISOString()
      },
      viewport,
      fullPage,
      selectedElement,
      stitch,
      stitchPackage,
      consent: {
        screenshotConsented: options.screenshotConsented === true,
        telemetryConsented,
        consentText: telemetryConsented
          ? "User consented to page screenshot metadata plus console/network summaries for GoatShot."
          : "User consented to page screenshot metadata for GoatShot. Telemetry was not requested.",
        consentedAt: new Date().toISOString()
      },
      consoleEvents: [],
      networkEvents: telemetryConsented ? collectNetworkEvents() : []
    };
  }

  window.addEventListener("message", async (event) => {
    if (event.source !== window || event.data?.type !== requestType) {
      return;
    }

    const options = event.data.options || {};
    const payload = await buildPayload(options);
    window.postMessage(
      {
        type: responseType,
        payload
      },
      "*"
    );

    if (options.nativeHost === true && typeof chrome !== "undefined" && chrome.runtime?.sendMessage) {
      chrome.runtime.sendMessage(
        {
          type: "GOATSHOT_NATIVE_RECEIVE",
          payload
        },
        (response) => {
          window.postMessage(
            {
              type: nativeResultType,
              correlationId: payload.intent.correlationId,
              response
            },
            "*"
          );
        }
      );
    }
  });

  if (typeof chrome !== "undefined" && chrome.runtime?.onMessage) {
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
      if (!message || message.type !== "GOATSHOT_POPUP_CAPTURE") {
        return false;
      }

      (async () => {
        const options = message.options || {};
        const payload = await buildPayload(options);
        let nativeResult = null;
        if (options.nativeHost === true && chrome.runtime?.sendMessage) {
          nativeResult = await new Promise((resolve) => {
            chrome.runtime.sendMessage(
              {
                type: "GOATSHOT_NATIVE_RECEIVE",
                payload
              },
              (response) => {
                if (chrome.runtime.lastError) {
                  resolve({
                    succeeded: false,
                    diagnosticCode: "native-host-unreachable",
                    message: chrome.runtime.lastError.message
                  });
                  return;
                }

                resolve(response || null);
              }
            );
          });
        }

        sendResponse({
          succeeded: true,
          payload,
          nativeResult
        });
      })().catch((error) => {
        sendResponse({
          succeeded: false,
          message: error.message || "GoatShot capture failed."
        });
      });

      return true;
    });
  }
})();

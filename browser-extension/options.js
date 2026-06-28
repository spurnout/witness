const storageKey = "goatshot.popupSettings";
const ids = ["nativeHost", "telemetry", "horizontal", "exportPackage", "maxTiles", "overlap", "sticky"];
const elements = Object.fromEntries(ids.map(id => [id, document.getElementById(id)]));
const result = document.getElementById("result");

function setStatus(message) {
  result.textContent = message;
}

function readSettings() {
  return {
    captureMode: "full-page",
    nativeHost: elements.nativeHost.checked,
    telemetryConsented: elements.telemetry.checked,
    includeHorizontalScroll: elements.horizontal.checked,
    captureTiles: elements.exportPackage.checked,
    exportStitchPackage: elements.exportPackage.checked,
    maxTiles: Number.parseInt(elements.maxTiles.value, 10) || 80,
    tileOverlapPixels: Number.parseInt(elements.overlap.value, 10) || 64,
    stickyHeaderMitigationPixels: Number.parseInt(elements.sticky.value, 10) || 0
  };
}

function applySettings(settings) {
  if (!settings) {
    return;
  }

  elements.nativeHost.checked = settings.nativeHost !== false;
  elements.telemetry.checked = settings.telemetryConsented === true;
  elements.horizontal.checked = settings.includeHorizontalScroll === true;
  elements.exportPackage.checked = settings.exportStitchPackage === true;
  elements.maxTiles.value = settings.maxTiles || 80;
  elements.overlap.value = settings.tileOverlapPixels ?? 64;
  elements.sticky.value = settings.stickyHeaderMitigationPixels || 0;
}

async function load() {
  const stored = await chrome.storage.local.get([storageKey, "goatshot.lastResult"]);
  applySettings(stored[storageKey]);
  const last = stored["goatshot.lastResult"];
  if (last) {
    setStatus(`Last capture: ${last.title || "untitled"} (${last.tileCount || 0} tile entries)`);
  }
}

document.getElementById("save").addEventListener("click", async () => {
  await chrome.storage.local.set({ [storageKey]: readSettings() });
  setStatus("Saved.");
});

document.getElementById("status").addEventListener("click", async () => {
  const response = await chrome.runtime.sendMessage({ type: "GOATSHOT_NATIVE_STATUS" });
  const code = response?.diagnosticCode ? `${response.diagnosticCode}: ` : "";
  setStatus(`${code}${response?.message || "Native host status unavailable."}`);
});

load().catch(error => setStatus(error.message));

const fields = {
  captureMode: document.getElementById("captureMode"),
  telemetry: document.getElementById("telemetry"),
  horizontal: document.getElementById("horizontal"),
  captureTiles: document.getElementById("captureTiles"),
  exportPackage: document.getElementById("exportPackage"),
  nativeHost: document.getElementById("nativeHost"),
  maxTiles: document.getElementById("maxTiles"),
  overlap: document.getElementById("overlap"),
  sticky: document.getElementById("sticky"),
  result: document.getElementById("result")
};

const storageKey = "goatshot.popupSettings";

function setStatus(message) {
  fields.result.textContent = message;
}

function readSettings() {
  return {
    captureMode: fields.captureMode.value,
    telemetryConsented: fields.telemetry.checked,
    includeHorizontalScroll: fields.horizontal.checked,
    captureTiles: fields.captureTiles.checked || fields.exportPackage.checked,
    exportStitchPackage: fields.exportPackage.checked,
    nativeHost: fields.nativeHost.checked,
    maxTiles: Number.parseInt(fields.maxTiles.value, 10) || 80,
    tileOverlapPixels: Number.parseInt(fields.overlap.value, 10) || 64,
    stickyHeaderMitigationPixels: Number.parseInt(fields.sticky.value, 10) || 0
  };
}

function applySettings(settings) {
  if (!settings) {
    return;
  }

  fields.captureMode.value = settings.captureMode || "full-page";
  fields.telemetry.checked = settings.telemetryConsented === true;
  fields.horizontal.checked = settings.includeHorizontalScroll === true;
  fields.captureTiles.checked = settings.captureTiles === true;
  fields.exportPackage.checked = settings.exportStitchPackage === true;
  fields.nativeHost.checked = settings.nativeHost !== false;
  fields.maxTiles.value = settings.maxTiles || 80;
  fields.overlap.value = settings.tileOverlapPixels ?? 64;
  fields.sticky.value = settings.stickyHeaderMitigationPixels || 0;
}

async function saveSettings(settings) {
  await chrome.storage.local.set({ [storageKey]: settings });
}

async function loadSettings() {
  const stored = await chrome.storage.local.get(storageKey);
  applySettings(stored[storageKey]);
}

async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) {
    throw new Error("No active browser tab was available.");
  }

  return tab;
}

async function collect(sendToNativeHost) {
  const tab = await activeTab();
  const settings = readSettings();
  settings.nativeHost = sendToNativeHost;
  settings.screenshotConsented = true;
  settings.fullPageCaptureRequested = settings.captureMode !== "visible-viewport";
  settings.correlationId = `popup-${Date.now()}`;
  await saveSettings(settings);

  const response = await chrome.tabs.sendMessage(tab.id, {
    type: "GOATSHOT_POPUP_CAPTURE",
    options: settings
  });

  if (!response?.succeeded) {
    throw new Error(response?.message || "Capture request failed.");
  }

  const tileCount = response.payload?.stitch?.tileCount ?? 0;
  const stitchPackage = response.payload?.stitchPackage || null;
  const packageMessage = stitchPackage?.requested
    ? `\nPackage: ${stitchPackage.message || stitchPackage.status || "requested"}`
    : "";
  const nativeMessage = response.nativeResult
    ? `Native: ${response.nativeResult.diagnosticCode || ""} ${response.nativeResult.message || (response.nativeResult.succeeded ? "sent" : "failed")}`.trimEnd()
    : "";
  await chrome.storage.local.set({
    "goatshot.lastResult": {
      capturedAt: new Date().toISOString(),
      title: response.payload?.page?.title || "",
      tileCount,
      stitchPackage,
      nativeResult: response.nativeResult || null
    }
  });
  setStatus(`Captured ${tileCount} tile plan entries.${packageMessage}${nativeMessage ? `\n${nativeMessage}` : ""}`);
}

async function hostStatus() {
  const response = await chrome.runtime.sendMessage({
    type: "GOATSHOT_NATIVE_STATUS"
  });
  const code = response?.diagnosticCode ? `${response.diagnosticCode}: ` : "";
  setStatus(`${code}${response?.message || "Native host status unavailable."}`);
}

document.getElementById("collect").addEventListener("click", async () => {
  try {
    await collect(false);
  } catch (error) {
    setStatus(error.message);
  }
});

document.getElementById("send").addEventListener("click", async () => {
  try {
    await collect(true);
  } catch (error) {
    setStatus(error.message);
  }
});

document.getElementById("status").addEventListener("click", async () => {
  try {
    await hostStatus();
  } catch (error) {
    setStatus(error.message);
  }
});

document.getElementById("options").addEventListener("click", () => {
  chrome.runtime.openOptionsPage();
});

loadSettings().catch(error => setStatus(error.message));

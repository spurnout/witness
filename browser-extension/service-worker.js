const RECEIPTS_NATIVE_HOST = "com.receipts.bridge";
const LEGACY_NATIVE_HOST = "com.goatshot.bridge";

function classifyNativeError(message) {
  const text = String(message || "").toLowerCase();
  if (text.includes("not found") || text.includes("specified native messaging host")) {
    return "native-host-missing";
  }

  if (text.includes("access") || text.includes("permission")) {
    return "native-host-permission-denied";
  }

  return "native-host-unreachable";
}

function classifyNativeResponse(response) {
  if (!response) {
    return "native-host-no-response";
  }

  if (response.succeeded === false || response.isValid === false) {
    return "payload-rejected";
  }

  return "ready";
}

function sendNativeMessage(payload, sendResponse) {
  chrome.runtime.sendNativeMessage(RECEIPTS_NATIVE_HOST, payload, response => {
    const primaryError = chrome.runtime.lastError?.message || "";
    if (!primaryError) {
      sendResponse({
        ...(response || {}),
        diagnosticCode: classifyNativeResponse(response),
        nativeHost: RECEIPTS_NATIVE_HOST,
        usedLegacyHost: false,
        message: response?.message || "Native host responded."
      });
      return;
    }

    if (classifyNativeError(primaryError) !== "native-host-missing") {
      sendResponse({
        succeeded: false,
        diagnosticCode: classifyNativeError(primaryError),
        nativeHost: RECEIPTS_NATIVE_HOST,
        usedLegacyHost: false,
        message: primaryError
      });
      return;
    }

    // Installed GoatShot builds register only the old host name. Falling back
    // here keeps the updated extension usable throughout the Receipts upgrade.
    chrome.runtime.sendNativeMessage(LEGACY_NATIVE_HOST, payload, legacyResponse => {
      const legacyError = chrome.runtime.lastError?.message || "";
      if (legacyError) {
        sendResponse({
          succeeded: false,
          diagnosticCode: classifyNativeError(legacyError),
          nativeHost: LEGACY_NATIVE_HOST,
          usedLegacyHost: true,
          message: legacyError
        });
        return;
      }

      sendResponse({
        ...(legacyResponse || {}),
        diagnosticCode: classifyNativeResponse(legacyResponse),
        nativeHost: LEGACY_NATIVE_HOST,
        usedLegacyHost: true,
        message: legacyResponse?.message || "Native host responded through the legacy compatibility alias."
      });
    });
  });
}

function downloadStitchFile(message, sendResponse) {
  if (!chrome.downloads?.download) {
    sendResponse({
      succeeded: false,
      diagnosticCode: "downloads-permission-missing",
      message: "The downloads API is unavailable; check the extension downloads permission."
    });
    return;
  }

  if (!message.filename || !message.dataUrl) {
    sendResponse({
      succeeded: false,
      diagnosticCode: "invalid-download-request",
      message: "A stitch package download requires filename and dataUrl."
    });
    return;
  }

  chrome.downloads.download(
    {
      url: message.dataUrl,
      filename: message.filename,
      conflictAction: "overwrite",
      saveAs: false
    },
    downloadId => {
      if (chrome.runtime.lastError) {
        sendResponse({
          succeeded: false,
          diagnosticCode: "download-failed",
          message: chrome.runtime.lastError.message
        });
        return;
      }

      sendResponse({
        succeeded: true,
        diagnosticCode: "download-started",
        downloadId,
        filename: message.filename,
        message: `Download started: ${message.filename}`
      });
    }
  );
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message) {
    return false;
  }

  if (message.type === "GOATSHOT_DOWNLOAD_STITCH_FILE") {
    downloadStitchFile(message, sendResponse);
    return true;
  }

  if (message.type === "GOATSHOT_CAPTURE_VISIBLE_TAB") {
    const windowId = sender?.tab?.windowId;
    const senderTabId = sender?.tab?.id;
    const captureOptions = {
      format: message.format === "jpeg" ? "jpeg" : "png"
    };
    if (Number.isFinite(message.quality)) {
      captureOptions.quality = message.quality;
    }

    // captureVisibleTab shoots whatever tab is foreground in the window; if the user
    // switched tabs mid scroll-capture we would silently capture an unrelated
    // (possibly sensitive) tab, so abort instead.
    chrome.tabs.query({ active: true, windowId }, tabs => {
      if (chrome.runtime.lastError) {
        sendResponse({
          succeeded: false,
          diagnosticCode: "visible-tab-capture-failed",
          message: chrome.runtime.lastError.message
        });
        return;
      }

      const activeTab = tabs && tabs[0];
      if (Number.isFinite(senderTabId) && activeTab && activeTab.id !== senderTabId) {
        sendResponse({
          succeeded: false,
          diagnosticCode: "capture-tab-not-active",
          message: "The capture tab is no longer the active tab; aborting so an unrelated tab is not captured."
        });
        return;
      }

      chrome.tabs.captureVisibleTab(
        windowId,
        captureOptions,
        dataUrl => {
          if (chrome.runtime.lastError) {
            sendResponse({
              succeeded: false,
              diagnosticCode: "visible-tab-capture-failed",
              message: chrome.runtime.lastError.message
            });
            return;
          }

          sendResponse({
            succeeded: true,
            diagnosticCode: "visible-tab-captured",
            bytes: dataUrl ? dataUrl.length : 0,
            dataUrl: message.includeDataUrl === true ? dataUrl : undefined
          });
        }
      );
    });

    return true;
  }

  if (message.type === "GOATSHOT_NATIVE_STATUS") {
    sendNativeMessage({ type: "GOATSHOT_PING" }, sendResponse);

    return true;
  }

  if (message.type !== "GOATSHOT_NATIVE_RECEIVE") {
    return false;
  }

  sendNativeMessage({ payload: message.payload }, sendResponse);

  return true;
});

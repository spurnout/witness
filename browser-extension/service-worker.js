const GOATSHOT_NATIVE_HOST = "com.goatshot.bridge";

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
    const captureOptions = {
      format: message.format === "jpeg" ? "jpeg" : "png"
    };
    if (Number.isFinite(message.quality)) {
      captureOptions.quality = message.quality;
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

    return true;
  }

  if (message.type === "GOATSHOT_NATIVE_STATUS") {
    chrome.runtime.sendNativeMessage(
      GOATSHOT_NATIVE_HOST,
      {
        type: "GOATSHOT_PING"
      },
      response => {
        if (chrome.runtime.lastError) {
          sendResponse({
            succeeded: false,
            diagnosticCode: classifyNativeError(chrome.runtime.lastError.message),
            message: chrome.runtime.lastError.message
          });
          return;
        }

        sendResponse({
          ...(response || {}),
          diagnosticCode: classifyNativeResponse(response),
          message: response?.message || "Native host responded."
        });
      }
    );

    return true;
  }

  if (message.type !== "GOATSHOT_NATIVE_RECEIVE") {
    return false;
  }

  chrome.runtime.sendNativeMessage(
    GOATSHOT_NATIVE_HOST,
    {
      payload: message.payload
    },
    response => {
      if (chrome.runtime.lastError) {
        sendResponse({
          succeeded: false,
          diagnosticCode: classifyNativeError(chrome.runtime.lastError.message),
          message: chrome.runtime.lastError.message
        });
        return;
      }

      sendResponse({
        ...(response || {}),
        diagnosticCode: classifyNativeResponse(response),
        message: response?.message || "Payload sent to GoatShot native host."
      });
    }
  );

  return true;
});

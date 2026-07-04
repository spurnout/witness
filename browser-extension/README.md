# GoatShot Browser Extension Prototype

This folder is an optional companion-module prototype for browser page capture. It does not replace the native WPF desktop app. The native app can receive validated payloads through the CLI handoff path, generate/register a user-scope native messaging host for Chrome, Edge, and Firefox, import bounded local stitch packages, package this prototype as a local ZIP for unpacked/manual browser loading, generate temporary local install-assist launchers, generate store-readiness checklist/copy artifacts, generate local store-submission package folders for manual review, generate read-only publication plans, and report operator diagnostic codes for extension source readiness, safe fixture readiness, native-host registration state, payload rejection, and stitch-package import readiness. Edge live safe-fixture Host Status/export/import proof is saved under `artifacts/tranche-browser-live-fixture-proof-closure/`; browser-store account submission/review/signing/availability, permanent or store-managed automatic installation, enterprise force-install, and Chrome/Firefox live fixture proof remain later/manual proof lanes.

## Current Scope

- Define the browser-to-GoatShot payload contract.
- Collect page geometry, viewport, title, URL, language, and optional telemetry summaries after explicit consent.
- Build a bounded full-page stitch manifest with viewport tile coordinates, overlap, sticky-header mitigation, horizontal-scroll coverage, and partial/blocked capture states.
- Optionally walk planned tile scroll positions and request visible-tab tile captures from the service worker, while keeping bitmap bytes out of the native payload.
- Export a local stitch package through browser downloads when the operator enables stitch package export. The package folder contains `goatshot-stitch-package.json`, `stitched.png`, and `tiles/*.png`.
- Include selected-element geometry for the active element or viewport-center fallback without collecting DOM text, form values, ids, classes, cookies, headers, or storage.
- Prototype a content-script handoff shape for the native bridge receiver.
- Receive validated payloads through `goatshot browser-extension receive`, store redacted handoff JSON, and optionally import a consented screenshot image into the workspace.
- Receive a bounded local stitch package through `goatshot browser-extension receive --stitch-package`, validate the package manifest, and import the package's stitched output as a `BrowserPage`.
- Generate Chrome/Edge/Firefox native messaging host manifests and user-scope registrations through `goatshot browser-extension native-host`.
- Provide a popup/options UI for consented capture settings, host status, and last handoff feedback.
- Generate browser-specific setup notes through `goatshot browser-extension install-guide`.
- Report local operator diagnostics through `goatshot browser-extension status` / `diagnostics`, including stable diagnostic codes for missing extension source, missing/incomplete package files, safe fixture availability, native-host missing/manifest-missing/registered-but-browser-proof-needed states, rejected payload diagnostics, stitch-package import diagnostics, and the browser-download package boundary.
- Forward a consented payload to `com.goatshot.bridge` only from a popup-initiated capture (a user gesture). Page `postMessage` requests are untrusted and never trigger native handoff, tile capture, or downloads.
- Validate and package the prototype with `goatshot browser-extension package`. The package ZIP intentionally includes the manifest, content script, service worker, popup/options UI, and shared extension CSS.
- Generate store-readiness checklist artifacts with `goatshot browser-extension store-readiness`, including local/Chrome/Edge/Firefox target status, permission rationale, privacy/data-use copy, screenshot checklist, and native-host proof boundaries.
- Generate local store-submission package folders with `goatshot browser-extension store-package`, including target-specific extension ZIPs, submission handoff ZIPs, SHA-256 manifests, readiness copy, missing manual-evidence lists, and publication non-goals.
- Generate read-only publication plans with `goatshot browser-extension publication-plan`, including package/readiness references, browser-store authority boundaries, manual gates, required evidence, official store documentation references, and false mutation flags without contacting store accounts or uploading packages.
- Generate read-only install plans with `goatshot browser-extension install-plan`, including browser-specific manual steps, native-host commands, generated package references, required evidence, and authority boundaries without changing browser profiles, registry keys, native-host folders, or store state.
- Generate temporary install-assist artifacts with `goatshot browser-extension install-assist`, including isolated Chrome/Edge `--load-extension` launch scripts, browser launch plan JSON, manual Firefox temporary-load guidance, and false mutation flags for browser-store contact, permanent install, existing profile mutation, registry/policy writes, and native-host registration.
- Rely on the `activeTab` permission for consented `tabs.captureVisibleTab` tile capture, which is granted for the active tab only after the operator invokes the popup; no broad `<all_urls>` host permission is requested. Content scripts still match only `http://*/*` and `https://*/*`, and the prototype still excludes cookies, headers, form values, local/session storage, DOM text dumps, ids, classes, and raw selector paths.
- Keep console/network telemetry opt-in.

## Not Yet Implemented

- Browser-store account submission/review/signing/availability.
- Permanent/store-managed automatic extension installation, enterprise force-install, and browser-store availability. The install-plan command documents manual proof requirements, and install-assist only supports temporary local loading.
- Automatic bug-report telemetry upload.

## Privacy Rules

- Screenshot capture requires visible user consent.
- Console/network telemetry requires separate consent.
- Do not collect cookies, request headers, response headers, form values, local storage, session storage, or DOM text contents in this prototype.
- Redact URLs, query tokens, console messages, and network metadata through GoatShot's contract service before persistence or sharing.

## Prototype Handoff

The content script listens for a page message and replies with geometry/plan metadata
only. A page cannot grant consent or drive privileged flows: `screenshotConsented`,
`telemetryConsented`, `captureTiles`, `exportStitchPackage`, and `nativeHost` supplied
by a page are ignored and forced off. Actual tile capture, stitch-package downloads, and
native handoff run only from the extension popup, where the user clicked a button.

```js
window.postMessage({
  type: "GOATSHOT_COLLECT_PAGE_CAPTURE",
  options: {
    captureMode: "full-page",
    fullPageCaptureRequested: true,
    includeHorizontalScroll: true,
    tileOverlapPixels: 64,
    stickyHeaderMitigationPixels: 0,
    maxTiles: 80,
    correlationId: "operator-generated-id"
  }
}, "*");
```

It replies with:

```js
window.postMessage({
  type: "GOATSHOT_PAGE_CAPTURE_PAYLOAD",
  payload
}, "*");
```

GoatShot validates `schemaVersion`, consent flags, dimensions, URL support, and redaction before accepting the payload. Use:

```powershell
goatshot browser-extension validate .\samples\full-page-capture-payload.json --redacted-output redacted.json
goatshot browser-extension receive .\samples\full-page-capture-payload.json --screenshot page.png --redacted-output redacted.json --json
goatshot browser-extension receive .\samples\full-page-capture-payload.json --stitch-package .\stitch-package --redacted-output redacted.json --json
goatshot browser-extension status --json
goatshot browser-extension diagnostics --source .\browser-extension
goatshot browser-extension install-guide --browser chrome --extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output install-guide.md --json
goatshot browser-extension native-host manifest --browser chrome --chrome-extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output-dir .\native-host --json
goatshot browser-extension package --output .\goatshot-browser-extension.zip --json
goatshot browser-extension store-readiness --source .\browser-extension --target all --output .\artifacts\browser-extension-store-readiness --json
goatshot browser-extension store-package --source .\browser-extension --target chrome --support-url https://example.invalid/support --privacy-url https://example.invalid/privacy --release-notes "Reviewed submission package." --output .\artifacts\browser-extension-store-package --json
goatshot browser-extension publication-plan --source .\browser-extension --target all --store-package-root .\artifacts\browser-extension-store-package --support-url https://example.invalid/support --privacy-url https://example.invalid/privacy --output .\artifacts\browser-extension-publication-plan --json
goatshot browser-extension install-plan --browser chrome --source .\browser-extension --store-package-root .\artifacts\browser-extension-store-package --extension-id aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output .\artifacts\browser-extension-install-plan --json
goatshot browser-extension install-assist --browser edge --source .\browser-extension --output .\artifacts\browser-extension-install-assist --json
```

Use `native-host install` only after you know the browser extension id. Registration is user-scope: Chromium/Edge use HKCU native messaging host keys, and Firefox uses the current user's `Mozilla\NativeMessagingHosts` folder.

The portable GoatShot package includes this `browser-extension/` folder so the package, store-readiness, store-package, publication-plan, install-plan, and install-assist commands can run from an installed portable build. Store-package output is only a local handoff bundle for manual submission prep. Publication-plan output is only a local checklist/authority-boundary artifact; browser stores still require their own review, signing, listing metadata, account access, and manual submission flow. Install-plan output is only a local operator plan. Install-assist output can load Chrome/Edge from an isolated temporary profile and writes manual Firefox temporary-load guidance; Chrome, Edge, Firefox, enterprise policy, and browser stores remain the authorities for whether an extension is actually installed permanently or made available through a store.

The popup stores local defaults in extension storage. The popup and options Host Status buttons ping `com.goatshot.bridge` and include the returned `diagnosticCode` in operator-visible status text when the native host reports one; this proves native-host reachability only when run inside the browser and does not prove browser-store account submission/review/signing, store availability, permanent installation, enterprise force-install, or store-managed automatic installation.

`goatshot browser-extension status` prints a desktop-side diagnostic summary. These codes are intentionally split by proof boundary: `extension-source-ready` and `safe-fixture-ready` are local filesystem readiness, `native-host-missing` and `native-host-manifest-missing` are desktop registration problems, `native-host-registered-browser-proof-needed` still requires the browser popup Host Status check, and `payload-rejected-diagnostics-available` / `stitch-package-import-diagnostics-available` mean the native CLI has actionable failure reporting for fixture evidence. Desktop diagnostics cannot prove that Chrome, Edge, or Firefox has loaded the unpacked extension.

`captureTiles: true` (popup-initiated only) asks the prototype to scroll through the planned tile positions and call `chrome.tabs.captureVisibleTab` for each tile. The payload records capture state and tile metadata only; bitmap bytes are intentionally not embedded in native messaging payloads.

`exportStitchPackage: true` (popup-initiated only) captures tile image bytes after screenshot consent and writes a local package under the browser downloads folder, using `GoatShot/<correlationId>/` as the relative folder hint. The browser does not expose a stable absolute downloads path to the native app, so import remains explicit:

```powershell
goatshot browser-extension receive .\samples\full-page-capture-payload.json --stitch-package "$env:USERPROFILE\Downloads\GoatShot\operator-generated-id" --json
```

The included safe fixture at `browser-extension/samples/safe-fixture.html` is designed for local proof of tall pages, horizontal scroll, sticky headers, and selected-element capture without private browser content.

A stitch package manifest looks like:

```json
{
  "schemaVersion": "goatshot.browser-stitch-package.v1",
  "correlationId": "operator-generated-id",
  "source": "extension-storage-export",
  "stitchedImagePath": "stitched.png",
  "tiles": [
    { "index": 0, "path": "tiles/tile-0000.png", "captureState": "captured" }
  ],
  "warnings": []
}
```

All paths must stay inside the package directory. The native bridge validates image type, non-empty files, size limits, and correlation id before importing the stitched image into the workspace.

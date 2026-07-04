# GoatShot Browser Extension Bridge Contract

Schema version: `goatshot.browser-capture.v1`

## Required Top-Level Fields

- `schemaVersion`: must equal `goatshot.browser-capture.v1`.
- `intent`: capture mode, full-page intent, DOM metadata intent, telemetry intent, and correlation id.
- `page`: URL, title, referrer, content type, language, and capture timestamp.
- `viewport`: visible viewport dimensions, device pixel ratio, and current scroll offsets.
- `fullPage`: scroll/page dimensions used for full-page capture planning.
- `selectedElement`: optional selected-element geometry when the operator chooses selected-element capture.
- `stitch`: optional bounded tile/stitch manifest for full-page capture planning.
- `stitchPackage`: optional browser-download package status and folder hint.
- `consent`: screenshot and telemetry consent flags plus the consent copy shown to the user.
- `consoleEvents`: optional bounded console summaries.
- `networkEvents`: optional bounded network summaries.

## Consent Boundary

- `consent.screenshotConsented` must be true for any capture handoff.
- `consent.telemetryConsented` must be true when `intent.includeTelemetry` is true or console/network events are present.
- Bug-report telemetry is opt-in and must be visible before collection.
- Consent originates from a user gesture in the extension popup. A page `postMessage` is untrusted and cannot grant consent or trigger tile capture, downloads, or native handoff; page-initiated requests return geometry/plan metadata only.

## Data Boundary

Allowed:

- Page URL, title, referrer, content type, language.
- Viewport and full-page geometry.
- Selected-element tag name, role/input type, accessible-name presence, and geometry. The extension must not include element text, form values, ids, classes, or raw selector paths.
- Console level/message/source location when telemetry consent is true.
- Network URL/status/resource type/initiation summary when telemetry consent is true.

Disallowed in this prototype:

- Cookies.
- Request or response headers.
- Form values.
- DOM text dumps.
- Local storage or session storage.
- Automatic upload.

## Native Handoff Requirement

Before GoatShot accepts a payload, it must run `BrowserExtensionPayloadContractService.Validate` and `RedactForStorage`. Invalid payloads are rejected with validation issues; redacted payloads may be used for local bug reports, capture planning, or workspace import.

Current native receiver:

- `goatshot browser-extension receive <payload.json>` stores a redacted payload under the local browser bridge area or a requested `--redacted-output` path.
- `--screenshot <image>` imports a consented browser screenshot into the workspace as `BrowserPage`.
- `--stitch-package <dir-or-manifest>` validates a bounded stitch package and imports its stitched output image into the workspace as `BrowserPage`.
- `goatshot browser-extension status` reports the local receiver and native-host registration status.
- `goatshot browser-extension package` validates and creates a minimal local extension ZIP for unpacked/manual loading.
- Native messaging host registration, local ZIP packaging, browser-download stitch-package export, and read-only publication planning are implemented; browser-store account submission/review/signing and automatic extension installation remain later proof lanes.

## Stitch Manifest

The optional `stitch` object lets the extension describe how a full-page capture should be tiled without pushing large image bytes through native messaging.

Required shape when `stitch.requested` is true:

- `mode`: capture mode such as `full-page`, `visible-viewport`, or `selected-element`.
- `status`: `planned`, `planned-partial`, `captured`, `captured-metadata-only`, `capture-partial`, `blocked`, or `not-requested`.
- `captureApi`: browser API used by the prototype, currently `tabs.captureVisibleTab`.
- `tileCount` and `maxTileCount`: declared tile count and safety cap.
- `overlapPixels`: overlap between adjacent tiles.
- `stickyHeaderMitigationPixels`: extra overlap reserved for sticky headers.
- `horizontalScrollIncluded`: true when the page is wider than the viewport or horizontal scroll was requested.
- `tiles`: ordered tile list with `index`, content-space `x/y/width/height`, scroll positions, DPR, and capture state.
- `warnings`: bounded user-facing warnings for partial plans or blocked capture.

Bitmap bytes are not embedded in `stitch`. If the prototype captures visible-tab tiles, it records `captureState` and synthetic artifact names only. Actual image bytes use a separate bounded stitch package.

## Stitch Package

The extension can export a local stitch package through browser downloads under the relative folder hint `GoatShot/<correlationId>/`. The native bridge accepts that local package through CLI `--stitch-package` or native-host JSON `stitchPackagePath`. A package path may be either a directory containing `goatshot-stitch-package.json` / `stitch-package.json`, or a direct manifest file.

Package manifest schema version: `goatshot.browser-stitch-package.v1`.

Required fields:

- `schemaVersion`: must match the current package schema.
- `correlationId`: should match `intent.correlationId` from the browser payload when present.
- `stitchedImagePath`: relative path to the final stitched image.

Optional fields:

- `source`: source label such as `extension-storage-export`.
- `createdAt`: package timestamp.
- `tiles`: optional relative tile image paths and capture states for diagnostics.
- `warnings`: bounded user-facing warnings.
- Payload `stitchPackage`: `requested`, `status`, `source`, `downloadRoot`, `manifestPath`, `fileCount`, `downloadIds`, `message`, and `warnings`.

Safety rules:

- All package paths must stay inside the package directory.
- The stitched image must be a supported image file and non-empty.
- The stitched image is capped at 250 MB.
- Individual tile images are capped at 50 MB and total tile bytes are capped at 500 MB.
- The package is local-only; it does not upload browser content.
- Browser downloads do not provide a portable absolute path to the native host, so the operator or CLI caller still chooses the exact local package folder for import.

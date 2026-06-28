# GoatShot OAuth-Parked Next Buildout Plan

Date: 2026-06-15

Purpose: continue implementing the remaining GoatShot roadmap while leaving live OAuth consent/account validation parked. This is the short operational plan to use for the next build tranches.

No git workflow is required right now.

## Scope Boundary

- [ ] Keep Google Drive, Dropbox, OneDrive, and similar live OAuth consent screens parked.
- [ ] Keep refresh-token expiry/recovery and live provider-account upload proof parked.
- [ ] Do not rework OAuth unless a non-OAuth task exposes a small compatibility bug.
- [ ] Keep the product native WPF/.NET. Optional browser-extension work can live as a separate companion module, but it must not turn the desktop app into a web app.
- [ ] Continue proving work with local tests, fake providers/processes, safe artifacts, diagnostics, screenshots, and portable packaging.

## Current Baseline To Preserve

- [x] Native WPF desktop app, CLI, diagnostics, tests, Product Design/WPF audit artifacts, and portable packaging exist.
- [x] Capture, scrolling capture, recording, editor/privacy tools, AI/document workflows, upload queue/history, provider adapters, workflow automation, manual validation harness, and advanced video edit planning are locally proven in prior tranche artifacts.
- [x] OAuth/live-account proof is the main parked account lane, but it is not the only remaining roadmap work.
- [x] Android ADB screenshot capture/import is finished, tested, documented, and proven under `artifacts/tranche-android-adb-capture/`.
- [x] Virtual printer/file-drop PDF/image import is finished, tested, documented, and proven under `artifacts/tranche-virtual-printer-import/`; OS printer driver installation remains later.
- [x] Plugin SDK/local extension manifest discovery, dry-run trust gates, and guarded local execution are finished, tested, documented, and proven under `artifacts/tranche-plugin-sdk/` and `artifacts/tranche-plugin-execution/`; remote marketplace behavior remains later.
- [x] Browser extension native CLI receiver is finished, tested, documented, and proven under `artifacts/tranche-browser-native-bridge/`; native messaging host registration and local ZIP packaging are now proven separately, while browser-store publication remains later.
- [x] Android bounded `screenrecord` pull/import is finished, tested, documented, and proven under `artifacts/tranche-android-video-decision/`; Android live-preview dry-run planning is proven under `artifacts/tranche-android-live-preview/`; guarded screencap preview execution is proven under `artifacts/tranche-android-preview-execution/`; live device video/preview proof and production Android live streaming remain later.
- [x] Companion portal/team-admin boundaries are documented under `artifacts/tranche-companion-portal-planning/`; portal implementation and team/admin mode remain later.
- [x] Browser native messaging host manifest generation, stdio host run mode, user-scope Chrome/Edge/Firefox registration support, local ZIP packaging, desktop-side operator diagnostics, and browser-download stitch-package export are finished, tested, documented, and proven; browser-store publication and automatic extension installation remain later; Edge live fixture proof is complete, and Chrome/Firefox live fixture proof remains later/manual if needed.
- [x] Guarded local plugin execution is finished, tested, documented, and proven under `artifacts/tranche-plugin-execution/`; remote plugin registry/package acquisition is finished under `artifacts/tranche-plugin-remote-install-scaffold/`; guarded staged activation is finished under `artifacts/tranche-plugin-active-install/`; automatic background updates and marketplace behavior remain later.

## Completed Tranche 1 - Android ADB Capture

Goal: add optional Android screenshot import through `adb exec-out screencap -p` without blocking the desktop product.

- [x] Stabilize the current Android ADB service/CLI scaffolding and make sure Release build passes.
- [x] Add or confirm CLI help for `capture android` and `diagnostics android`.
- [x] Add ADB discovery through explicit path, `GOATSHOT_ADB_PATH`, and PATH lookup.
- [x] Parse `adb devices -l` output into ready, unauthorized, offline, no-device, missing-ADB, failed-ADB, and multiple-device states.
- [x] Implement screenshot capture to a user-specified output path or GoatShot workspace/library.
- [x] Require `--device` when multiple ready devices are present.
- [x] Reject non-PNG screencap payloads with a clear error.
- [x] Keep Android video/streaming out of scope until screenshot capture is stable.

Proof:

- [x] Parser/service tests using fake ADB output.
- [x] Fake ADB process tests for successful PNG capture, unauthorized, no-device, multiple-device, missing-ADB, and non-PNG payload cases.
- [x] CLI `diagnostics android` smoke output on this machine. A ready device was detected, so automatic screencap was skipped for privacy.
- [x] CLI `capture android --json` failure artifact using an explicit missing ADB path.
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `--help`
- [x] CLI `diagnostics print`
- [x] `.\scripts\package-release.ps1 -SkipInstaller`
- [x] Notes at `artifacts/tranche-android-adb-capture/notes.md`.

## Completed Tranche 2 - Browser Extension Contract And Prototype

Goal: start perfect browser/page capture as an optional module with explicit consent boundaries.

- [x] Define extension-to-desktop contract for page geometry, DOM metadata, viewport/full-page capture intent, console/network telemetry summary, and bug-report context.
- [x] Define redaction rules for URLs, query tokens, form values, cookies, headers, console messages, and network metadata.
- [x] Add fixture validation for sample page payloads before adding browser UI.
- [x] Add minimal extension manifest/content-script prototype that can collect geometry/metadata.
- [x] Define the local/native bridge handoff design without claiming full end-to-end parity yet.
- [x] Keep telemetry opt-in and visibly consented.

Proof:

- [x] Contract/model tests or fixture validation.
- [x] Sample DOM/page payloads under `artifacts/tranche-browser-extension/`.
- [x] Privacy redaction tests.
- [x] Notes at `artifacts/tranche-browser-extension/notes.md`.

## Completed Tranche 3 - Virtual Printer Import Path

Goal: support print-to-file style capture before committing to installer/admin driver work.

- [x] Define a local watched-folder/file-drop contract for PDF/image outputs.
- [x] Add watched-folder import rules for PDF/image outputs.
- [x] Preserve source-app metadata where available.
- [x] Add diagnostics that report configured print-import folders and supported extensions.
- [x] Document that true virtual-printer driver install remains installer/admin-scoped and unproven until a later clean-machine lane.

Proof:

- [x] Watched-folder import tests.
- [x] Safe sample PDF/image imports.
- [x] Diagnostics smoke output.
- [x] Notes at `artifacts/tranche-virtual-printer-import/notes.md`.

## Completed Tranche 4 - Plugin SDK And Local Extension Points

Goal: let power users extend GoatShot locally without weakening policy, trust, redaction, or diagnostics.

- [x] Define a minimal local plugin manifest for actions, share destinations, workflow actions, and diagnostics.
- [x] Add plugin discovery from an app-owned folder.
- [x] Keep discovered plugins disabled/untrusted by default.
- [x] Add allowlist/trust-state checks before any plugin action can run.
- [x] Add diagnostics for plugin id, version, source path, trust state, and allowed/blocked actions.
- [x] Add sample plugin fixtures with no network side effects.
- [x] Keep hosted marketplace/remote plugin install out of scope.

Proof:

- [x] Manifest parser tests.
- [x] Policy/allowlist tests.
- [x] Sample plugin dry-run proof.
- [x] Diagnostics redaction checks.
- [x] Notes at `artifacts/tranche-plugin-sdk/notes.md`.

## Completed Tranche 6 - Browser Extension Native Bridge Follow-Through

Goal: turn the existing browser extension contract/prototype into a local end-to-end handoff.

- [x] Define the native messaging or local bridge installer boundary.
- [x] Add a bounded local handoff receiver that validates `goatshot.browser-capture.v1` payloads before import.
- [x] Import consented full-page bitmap/page payloads into the workspace when the bridge is available.
- [x] Keep telemetry opt-in and bounded; never collect cookies, headers, form values, local/session storage, or raw DOM text dumps.
- [x] Add clear disabled/missing-bridge diagnostics and docs.

Proof:

- [x] Receiver tests with accepted/rejected payload fixtures.
- [x] Redaction tests.
- [x] Local bridge smoke with safe fixtures.
- [x] Notes at `artifacts/tranche-browser-native-bridge/notes.md`.

## Completed Tranche 7 - Android Video Expansion Decision

Goal: decide whether Android stays screenshot-only or expands into safe local video import.

- [x] Compare ADB screencap polling, `adb shell screenrecord` pull/import, and live streaming options.
- [x] Implement bounded `adb shell screenrecord --time-limit` import through CLI `capture android-video` / `capture android screenrecord`.
- [x] Require bounded duration, explicit device selection when multiple devices are ready, remote cleanup best effort, and local MP4 payload validation.
- [x] Keep live device proof opt-in/privacy-gated and keep Android live streaming later-scope.

Proof:

- [x] Fake ADB command choreography tests.
- [x] Invalid-duration and missing-ADB CLI artifacts.
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `--help`
- [x] CLI `diagnostics print`
- [x] `.\scripts\package-release.ps1 -SkipInstaller`
- [x] Notes at `artifacts/tranche-android-video-decision/notes.md`.

## Completed Tranche 5 - Companion Portal And Team/Admin Boundary Planning

Goal: separate later shared/team/hosted work from the local-first desktop MVP.

- [x] Write an architecture note for optional hosted/self-hosted companion portal boundaries.
- [x] Define what can sync, what must stay local, and what requires explicit consent.
- [x] Define how portal/team policy relates to existing managed-policy keys.
- [x] Define what the portal cannot bypass: desktop policy, redaction, local proof, user consent, or provider account boundaries.
- [x] Keep implementation out of scope until the boundary note is approved.

Proof:

- [x] Architecture note.
- [x] Threat/privacy checklist.
- [x] Notes at `artifacts/tranche-companion-portal-planning/notes.md`.

## Completed Tranche 8 - Browser Native Host Registration

Goal: turn the browser native bridge into a user-scope native messaging host registration path without browser-store claims.

- [x] Add stdio native-host run mode to `GoatShot.Cli.exe`.
- [x] Add Chrome, Edge, and Firefox native messaging manifest generation.
- [x] Add user-scope Chrome/Edge HKCU registration and Firefox profile-folder manifest installation commands.
- [x] Add status and uninstall commands.
- [x] Update the prototype extension with `nativeMessaging` permission and a service-worker handoff.
- [x] Keep browser-store publication and automatic extension installation later-scope; Edge live fixture proof is complete, and Chrome/Firefox live fixture proof remains later/manual if needed.

Proof:

- [x] Fake registry install/uninstall tests.
- [x] Native-message validation/redaction tests.
- [x] CLI status/manifest artifacts.
- [x] Notes at `artifacts/tranche-browser-native-host-registration/notes.md`.

## Completed Tranche 9 - Guarded Local Plugin Execution

Goal: execute local plugin actions only after the existing trust, enable, and action allowlist gates pass.

- [x] Add `execution` metadata support to plugin actions.
- [x] Add `plugins run <plugin-id> <action-id>` CLI command.
- [x] Enforce global plugin enablement, plugin trust, plugin enablement, and action allowlist before process start.
- [x] Add bounded timeout, redacted stdout/stderr, exit-code reporting, timeout reporting, and plugin-directory working-directory guard.
- [x] Keep remote plugin acquisition in the remote scaffold, staged activation, and explicit update-apply tranches; unattended updates and hosted marketplace behavior remain later-scope.

Proof:

- [x] Blocked/run/timeout tests.
- [x] Sample plugin CLI run proof.
- [x] Notes at `artifacts/tranche-plugin-execution/notes.md`.

## Parallel Manual Proof Backlog

These can be run whenever safe local hardware/account conditions are available. They should not block the non-OAuth implementation tranches above.

- [ ] Keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA checks for key WPF flows.
- [ ] Windows text scaling and high-contrast checks.
- [ ] Live region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording with safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Clean-machine portable ZIP and optional installer proof.
- [ ] Live provider proof with disposable real accounts when OAuth is unparked.

## Definition Of Done For Every Implementation Tranche

- [ ] Focused tests cover changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when prompts, transcripts, OCR text, URLs, tokens, logs, settings, or telemetry payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.
- [ ] Refresh `artifacts/active-non-oauth-buildout-todos.md`, `artifacts/non-oauth-continuation-plan.md`, `artifacts/current-implementation-todos-oauth-parked.md`, and `artifacts/v1-readiness-summary.md` when a tranche completes.

## Recommended Order

1. Pick a dedicated later lane only if desired: browser-store publication/in-browser live proof, OS printer driver install, portal implementation, team/admin remote mode, or production Android live streaming beyond the dry-run planner.
2. Otherwise run manual proof lanes when safe hardware/accounts are available.

Next move: select a dedicated later lane or manual proof lane.

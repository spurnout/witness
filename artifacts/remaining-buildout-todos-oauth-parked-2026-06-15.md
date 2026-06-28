# GoatShot Remaining Buildout TODO Plan - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is still left on the GoatShot roadmap without blocking on live OAuth consent screens, refresh-token recovery, or real cloud-account proof. This plan starts from the current native WPF/.NET desktop baseline and keeps the remaining work in small, locally provable tranches.

No Git workflow is required for this project right now.

## Scope Rules

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Keep Google Drive, Dropbox, OneDrive, and similar live OAuth consent/account proof parked.
- [ ] Do not describe fake providers, local tokens, synthetic files, package-only checks, or local fixtures as live provider/account readiness.
- [ ] Use local proof first: MSTest coverage, fake HTTP/process providers, safe fixtures, JS syntax checks, CLI smoke, diagnostics redaction checks, WPF render screenshots when desktop UI changes, and portable package output.
- [ ] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.
- [ ] Refresh `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active TODO ledgers when a tranche changes readiness status.

## Current Baseline

- [x] Native WPF app, CLI, diagnostics, MSTest project, Product Design/WPF audit artifacts, manual validation harness, and portable package path exist.
- [x] Core capture, scrolling capture, recording, editor/privacy tools, workflow automation, AI/document workflows, upload queue/history, provider adapters, release/admin posture, Android screenshot/video import, virtual-printer file-drop import, browser native bridge/host registration, local plugin SDK/execution, remote plugin staging, and local admin policy bundles are locally proven.
- [x] Browser extension ZIP packaging, operator popup/options UI, native-host status, desktop-side diagnostic codes, bounded stitch-manifest planning, and native local stitch-package import are locally proven.
- [x] Browser automatic in-extension stitch-package export is locally implemented and proven under `artifacts/tranche-browser-auto-stitch-package/`.
- [x] Selected-element browser capture geometry is implemented; live-safe browser fixture proof remains manual.
- [x] Android live preview dry-run planning beyond bounded `screenrecord` import is implemented and locally proven; production live streaming remains later-scope.
- [x] Virtual printer driver feasibility beyond file-drop import is locally implemented and proven under `artifacts/tranche-virtual-printer-driver-feasibility/`.
- [ ] Companion portal implementation remains a decision after local team/admin boundaries.
- [ ] Manual hardware/accessibility/installer proof remains separate from locally buildable work.

## Completed Tranche 1 - Browser Automatic Stitch Package Export

Goal: finish the browser full-page capture lane by generating a bounded package from the extension itself, then importing/validating it through the native handoff path.

- [x] Add selected-element capture geometry and UX contract.
- [x] Add browser-side tile bitmap capture with explicit user consent and no cookies, headers, storage, form values, or raw DOM text.
- [x] Generate `goatshot.browser-stitch-package.v1` packages from captured tiles, stitch metadata, and optional stitched bitmap output.
- [x] Export the package through browser downloads with clear filenames and manifest references.
- [x] Add popup/options controls and status copy for package export, partial capture, rejected payloads, and package import failure.
- [x] Add diagnostics that distinguish native host missing, host manifest missing, host registered but still requiring browser Host Status proof, payload rejected, stitch-package import readiness, and package download/import boundary.
- [x] Add a safe local browser fixture for tall, wide, sticky-header, selected-element, and partial-capture cases.
- [x] Keep browser-store publication and automatic extension installation out of scope.

Proof:

- [x] JS syntax checks for extension scripts.
- [x] Extension manifest/permission tests.
- [x] Native package validation/import tests.
- [x] Safe fixture and package artifacts under `artifacts/tranche-browser-auto-stitch-package/`.
- [ ] Browser screenshot/fixture proof remains manual unless a safe browser target is staged.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-browser-auto-stitch-package/notes.md`.

## Completed Tranche 2 - Virtual Printer Driver Feasibility

Goal: move beyond file-drop import by documenting and diagnosing what would be required for a real Windows printer-driver path without claiming a driver is shipped.

- [x] Document Microsoft Print to PDF handoff, watched-folder routing, port monitor, v4 driver, PostScript/PDF pipeline, installer/admin requirements, and signing constraints.
- [x] Add CLI/admin diagnostics for watched folder health, supported extensions, printer-driver unavailability, installer privilege state, and package hook readiness.
- [x] Add non-invasive package hook guidance only where it does not require a signed driver or admin install.
- [x] Keep true driver installation and clean-machine printer proof manual/admin-scoped.

Proof:

- [x] Print-import diagnostics tests.
- [x] Safe PDF/image import regression tests.
- [x] Architecture note under `artifacts/tranche-virtual-printer-driver-feasibility/`.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-virtual-printer-driver-feasibility/notes.md`.

## Completed Tranche 3 - Android Live Preview Spike

Goal: explore live Android preview after bounded screenshot and `screenrecord` import, while keeping real-device capture privacy-gated.

- [x] Compare repeated screencap preview, `screenrecord --output-format=h264 -`, FFmpeg remux, and scrcpy-style external-tool boundaries.
- [x] Implement only the safest locally provable path first: fake ADB stream/remux planning or repeated screencap preview planning.
- [x] Add explicit safe-content consent reminders before any live-device capture.
- [x] Add duration, byte, timeout, disconnect, and cleanup bounds.
- [x] Keep live-device proof manual unless safe device content is staged.

Proof:

- [x] Fake ADB/service tests for planned stream/polling commands, timeout/disconnect/invalid-payload stop guidance, invalid bounds, missing ADB, multiple devices, and cleanup.
- [x] CLI dry-run/plan output under `artifacts/tranche-android-live-preview/`.
- [x] Real-device proof remains manual/privacy-gated.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-android-live-preview/notes.md`.

## Tranche 4 - Companion Portal Implementation Decision

Goal: decide whether to build portal code after the local admin/team policy model is real.

- [ ] Review `artifacts/tranche-companion-portal-planning/` and confirm the approved boundary.
- [ ] Decide whether portal v0 is docs-only, self-hosted local LAN, or hosted service.
- [ ] If approved, create a separate module with explicit auth, policy merge, data sync, audit, diagnostics, deployment, and privacy boundaries.
- [ ] Do not let portal code bypass desktop policy, local consent, provider account boundaries, redaction rules, or OAuth parking.

Proof:

- [ ] Architecture approval note.
- [ ] Threat/privacy checklist update.
- [ ] Separate tests, diagnostics, deployment notes, and proof artifacts before any implementation claim.

## Tranche 5 - Manual Proof Pass

Goal: use the existing manual validation harness to capture proof that deterministic tests cannot honestly provide.

- [ ] Keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA screen-reader verification for key WPF flows.
- [ ] Windows text scaling and high-contrast checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording with staged safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setup.
- [ ] Live Android screenshot/video proof with staged safe device content.
- [ ] Clean-machine portable ZIP and optional installer proof.
- [ ] Record evidence in `artifacts/manual-validation/<yyyy-mm-dd>/` and avoid private screen/device/provider data.

## Parked OAuth/Live Account Lane

Do not pick these up until a dedicated account tranche is explicitly scheduled:

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Per-Tranche Definition Of Done

- [ ] Focused tests cover changed services, models, CLI behavior, extension scripts, and UI models.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when URLs, tokens, prompts, transcripts, OCR text, logs, settings, package manifests, or telemetry payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.

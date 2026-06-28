# GoatShot Next Forward Buildout TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is still left on the GoatShot roadmap while keeping live OAuth consent, refresh-token validation, and real cloud-account proof parked. This is the forward queue from the current local V1 candidate.

No Git workflow is required for this project right now.

## Scope Rules

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Keep Google Drive, Dropbox, OneDrive, and similar OAuth consent/account proof parked unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not claim fake-provider, local-token, or synthetic proof as live provider/account readiness.
- [ ] Prefer locally provable implementation: service tests, fake HTTP/process providers, safe synthetic media, WPF render screenshots, CLI smoke output, diagnostics redaction checks, and portable package output.
- [ ] Refresh Product Design/WPF audit notes only when a desktop UI flow materially changes.
- [ ] End each tranche with `artifacts/tranche-<name>/notes.md`.

## Current Baseline

- [x] Native WPF app, CLI, diagnostics, MSTest coverage, Product Design/WPF screenshot-backed audit artifacts, manual validation harness, and portable package path exist.
- [x] Core capture, scrolling capture, recording, editor/privacy tools, workflow automation, AI/document workflows, upload queue/history, share providers, release/admin posture, Android ADB screenshot import, Android bounded screenrecord import, Android live-preview dry-run planning, virtual-printer file-drop import, browser extension native bridge/host registration, local plugin SDK, guarded local plugin execution, and remote plugin registry/package staging scaffold are locally proven.
- [x] Companion portal/team-admin boundaries are documented, but hosted portal implementation and team/admin product mode are not built.
- [x] Browser extension ZIP packaging is locally proven under `artifacts/tranche-browser-extension-packaging/`.

## Completed Tranche 0 - Close Browser Extension Packaging

Goal: finish the interrupted local browser-extension packaging tranche before starting new feature work.

Implementation TODOs:

- [x] Verify `artifacts/tranche-browser-extension-packaging/goatshot-browser-extension.zip` contains only the intended extension files.
- [x] Add `artifacts/tranche-browser-extension-packaging/notes.md`.
- [x] Update README/spec/browser-extension docs to distinguish local ZIP packaging from browser-store publication.
- [x] Update readiness/todo ledgers so browser-store publication and automatic extension installation remain later-scope.
- [x] Decide whether portable packaging should include the `browser-extension/` source folder or document that packaging is source-checkout only.
  - [x] Portable packaging now includes the `browser-extension/` source folder.

Proof TODOs:

- [x] `dotnet test .\GoatShot.slnx -c Release --filter BrowserExtensionPackageServiceTests`
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `browser-extension package --source browser-extension --output artifacts\tranche-browser-extension-packaging\goatshot-browser-extension.zip --json`
- [x] CLI `--help` and `diagnostics print`
- [x] `.\scripts\package-release.ps1 -SkipInstaller`

## Completed Tranche 1 - Browser Full-Page Stitch Package Handoff

Goal: move beyond native-host payload receipt by producing consented browser-side full-page bitmap capture packages.

Implementation TODOs:

- [x] Add extension capture options for full-page, visible viewport style mode strings, and telemetry-off mode.
- [x] Add selected-element capture geometry and UX contract.
- [x] Add tiled viewport capture/stitch planning that handles page height limits, scroll offsets, device pixel ratio, sticky headers, fixed elements, and horizontal scroll.
- [x] Add fixture-based stitch validation with sample payload proof so correctness can be tested without collecting private browsing content.
- [x] Extend the native bridge payload to include stitch manifest metadata: tile count, viewport size, page size, scroll positions, DPR, failure states, and redacted URL/title.
- [x] Add graceful partial-capture and manual-fallback states when browser APIs, permissions, or page behavior prevent full stitching.
- [x] Keep cookies, headers, form values, storage, and raw DOM text out of the payload.
- [x] Define and implement bounded local stitch-package handoff for actual tile images or stitched bitmap output.
- [x] Add native import for a stitched browser bitmap once the file-handoff boundary is settled.
- [x] Stage a safe local browser fixture for later live proof.
- [x] Add automatic in-browser generation/export of the stitch package through browser downloads.

Proof TODOs:

- [x] Extension manifest/contract tests for required permissions and consent flags.
- [x] Stitch planner tests for tall pages, horizontal pages, sticky headers, DPR changes, and failure states.
- [x] Native receiver/contract tests for redacted stitch manifests and imported note summaries.
- [x] Safe fixture payloads under `artifacts/tranche-browser-full-page-stitching/`.
- [x] CLI/native-host package import proof with synthetic local stitch package.
- [x] Browser-download package export is locally proven with JS syntax checks, extension package validation, and synthetic package import proof under `artifacts/tranche-browser-auto-stitch-package/`.
- [ ] Live browser extension load/screenshot proof remains manual unless a safe browser target is staged.

## Partially Completed Tranche 2 - Browser Extension Operator UX And Install Diagnostics

Goal: make the extension practical for local operators without claiming store publication.

Implementation TODOs:

- [x] Add extension popup/options UI for consent, capture mode, telemetry toggle, native-host status, and last handoff result.
- [x] Add a local install guide generator or CLI command that emits browser-specific unpacked-extension/native-host steps.
- [x] Add native-host health diagnostics for popup reachability through a `GOATSHOT_PING` path.
- [x] Add local browser diagnostics that distinguish extension source/package readiness, host missing, host manifest missing, host registered but still needing browser Host Status proof, payload rejection diagnostics, stitch-package import diagnostics, and the browser-download package boundary. Live browser screenshots still remain manual proof.
- [x] Add package metadata/version checks so native-host reachability reports a version and install guide records the extension id/status boundary.
- [x] Keep automatic browser extension installation and store publishing out of scope.

Proof TODOs:

- [x] Static extension UI HTML/CSS artifacts.
- [x] CLI install-guide/status artifacts for Chrome, Edge, and Firefox.
- [x] Tests for compatibility/version diagnostics.
- [x] Updated `browser-extension/README.md` and tranche notes.
- [x] Local diagnostics proof under `artifacts/tranche-browser-live-diagnostics/`.
- [ ] Live browser screenshot/fixture proof when a safe browser target is staged.

## Completed Tranche 3 - Android Live Preview/Streaming Spike

Goal: explore Android live capture beyond bounded `screenrecord` import while preserving privacy gates.

Implementation TODOs:

- [x] Add an architecture note comparing `screenrecord --output-format=h264 -`, repeated screencap polling, FFmpeg remux, and external tools such as scrcpy.
- [x] Implement only the safest locally provable path first: fake-ADB H.264 stream parsing/remux planning or repeated screencap preview planning.
- [x] Add explicit consent and safe-content reminders before any live-device capture.
- [x] Add duration, byte, and cleanup bounds for streaming experiments.
- [x] Keep live device proof manual unless the user stages safe device content.

Proof TODOs:

- [x] Fake ADB/service tests for planned stream/polling commands, timeout/disconnect/invalid-payload stop guidance, invalid bounds, missing ADB, multiple devices, and cleanup.
- [x] CLI dry-run or plan output under `artifacts/tranche-android-live-preview/`.
- [x] Real-device proof remains manual/privacy-gated.

## Completed Tranche 4 - Virtual Printer Driver Feasibility Lane

Goal: decide and prove the next step beyond file-drop import without pretending a driver is already shipped.

Implementation TODOs:

- [x] Document Windows printer-driver options: Microsoft Print to PDF handoff, port monitor, v4 driver, PostScript pipeline, installer/admin requirements, and signing constraints.
- [x] Add CLI/admin diagnostics for print-import readiness, watched folder health, supported extensions, and installer/driver unavailability.
- [x] Add package-hook guidance only for non-invasive folder/docs setup that does not require a signed driver.
- [x] Keep true driver installation and clean-machine printer proof manual/admin-scoped.

Proof TODOs:

- [x] Print-import diagnostics tests.
- [x] Safe sample PDF/image import regression tests.
- [x] Architecture note and proof boundary under `artifacts/tranche-virtual-printer-driver-feasibility/`.

## Completed Tranche 5 - Remote Plugin Install And Update Scaffold

Goal: add a governed plugin acquisition path without weakening the existing local trust model.

Implementation TODOs:

- [x] Define a plugin registry manifest format with id, version, description, capabilities, permissions, SHA-256, source URL/path, signature placeholder, and compatibility range.
- [x] Add dry-run install/upgrade planning that downloads or reads packages into a staging folder but leaves plugins disabled/untrusted by default.
- [x] Add checksum verification, path traversal protection, max size limits, redacted diagnostics, and clear trust prompts.
- [x] Add uninstall/disable/update-check commands that never auto-run plugin code.
- [x] Keep hosted marketplace accounts, payments, ratings, and remote execution out of scope.

Proof TODOs:

- [x] Fake HTTP registry/package tests.
- [x] Zip/path traversal/security tests.
- [x] CLI smoke artifacts under `artifacts/tranche-plugin-remote-install-scaffold/`.
- [x] Updated sample registry and plugin docs.

## Completed Tranche 6 - Local Team/Admin Mode

Goal: implement a local/admin-friendly mode before any hosted portal work.

Implementation TODOs:

- [x] Define team/admin policy bundles for allowed providers, disabled AI/uploads, external script/webhook controls, redaction defaults, retention defaults, plugin controls, and diagnostics bundle rules.
- [x] Add import/export/validate commands for admin policy bundles, omitting secrets by default.
- [x] Add desktop diagnostics/settings surfaces that show effective admin mode and policy source.
- [x] Add audit entries when policy changes block actions.
- [x] Keep hosted account sync, remote enforcement, and multi-user portal state out of scope.

Proof TODOs:

- [x] Policy precedence tests and blocked-action tests.
- [x] CLI validate/import/export artifacts under `artifacts/tranche-local-team-admin-mode/`.
- [x] WPF screenshot/render proof not needed because no Settings UI layout changed.

## Tranche 7 - Companion Portal Implementation Decision

Goal: choose whether to build hosted/self-hosted portal code after local team/admin mode is real.

Implementation TODOs:

- [ ] Review `artifacts/tranche-companion-portal-planning/` and confirm the approved boundary.
- [ ] Decide whether portal v0 is docs-only, self-hosted local LAN, or hosted service.
- [ ] If approved, create a separate module with explicit auth, policy merge, data sync, audit, and privacy boundaries.
- [ ] Do not let portal code bypass desktop policy, local consent, provider account boundaries, or redaction rules.

Proof TODOs:

- [ ] Architecture approval note.
- [ ] Threat/privacy checklist update.
- [ ] No implementation claim until the portal has its own tests, diagnostics, deployment notes, and proof artifacts.

## Parallel Manual Proof Backlog

Manual proof remains valuable, but should not block locally buildable tranches.

- [ ] Keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA screen-reader verification for key WPF flows.
- [ ] Windows text scaling and high-contrast checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording with staged safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Live Android screenshot/video proof with staged safe device content.
- [ ] Clean-machine portable ZIP and optional installer proof.
- [ ] Live provider proof with disposable real accounts when OAuth is unparked.

## Parked OAuth/Live Account Backlog

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Recommended Next Order

1. Live browser fixture proof when safe browser content is staged.
2. Manual validation when safe desktop/device/hardware conditions are available.
3. Tranche 7 only after the hosted portal boundary is approved.

## Definition Of Done

- [ ] Focused tests cover changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, or plugin packages are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.
- [ ] `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active TODO ledgers are updated when status changes.

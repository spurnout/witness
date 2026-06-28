# GoatShot Non-OAuth Continuation Plan

Date: 2026-06-15

Purpose: continue building GoatShot from the current native WPF/.NET V1 candidate while keeping live OAuth consent/account proof parked. This plan turns the remaining roadmap and half-built work into ordered implementation TODOs with local proof requirements.

Active execution ledger: `artifacts/active-non-oauth-buildout-todos.md`.

Current next-buildout plan: `artifacts/oauth-parked-next-buildout-plan.md`.

Broader readiness summary: `artifacts/v1-readiness-summary.md`.

Status note, 2026-06-15: Tranche A is complete and locally proven under `artifacts/tranche-recording-field-proof/`. Tranche B is complete and locally proven under `artifacts/tranche-manual-validation-harness/`. Tranche C is complete and locally proven under `artifacts/tranche-advanced-video-editor/`. Tranche D is complete and locally proven under `artifacts/tranche-android-adb-capture/`. Tranche E is complete and locally proven under `artifacts/tranche-browser-extension/`. Tranche F is complete and locally proven under `artifacts/tranche-virtual-printer-import/`. Tranche G is complete and locally proven under `artifacts/tranche-plugin-sdk/`. Tranche H is complete and locally documented under `artifacts/tranche-companion-portal-planning/`. Tranche I is complete and locally proven under `artifacts/tranche-browser-native-bridge/`. Tranche J is complete and locally proven under `artifacts/tranche-android-video-decision/`. Tranche K is complete and locally proven under `artifacts/tranche-browser-native-host-registration/`. Tranche L is complete and locally proven under `artifacts/tranche-plugin-execution/`. Remaining work is now in dedicated later lanes and manual/OAuth proof lanes.

## Scope Decision

- [ ] Keep Google Drive, Dropbox, OneDrive, and other live OAuth consent screens parked.
- [ ] Keep refresh-token expiry/recovery proof and live provider account uploads parked.
- [ ] Do not rework OAuth unless a non-OAuth task exposes a small compatibility bug.
- [ ] Keep provider diagnostics honest: local token, fake-provider, and synthetic upload proof are not live-account proof.
- [ ] Continue building native WPF/.NET surfaces, CLI parity, diagnostics, tests, and packaging proof.

## Definition Of Done For Every Tranche

- [ ] Focused service/model/CLI tests cover the changed behavior.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when prompts, OCR, transcripts, URLs, tokens, logs, or settings payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records what changed, proof paths, skipped proof, and remaining risk.
- [ ] `artifacts/active-non-oauth-buildout-todos.md` and `artifacts/current-implementation-todos-oauth-parked.md` are refreshed after completion.

## Tranche A - Recording Field Proof Polish

Goal: finish locally buildable recording confidence work before live long-run hardware validation.

Implementation TODOs:

- [x] Add microphone/system-audio timestamp, duration, byte-count, and elapsed-time logging for sync proof without storing private audio content.
- [x] Surface audio sync health in `diagnostics recording`, `record preflight`, and recording confidence notes.
- [x] Add optional `ffprobe` metadata extraction for recorded MP4 files when `ffprobe` is available.
- [x] Report a clear skipped state when `ffprobe` is unavailable.
- [x] Add a safe synthetic or short local media proof artifact for metadata parsing.
- [x] Add HEVC opt-in settings and CLI flags only when Media Foundation reports HEVC support.
- [x] Keep H.264 as the default and make HEVC fallback/error messaging explicit.
- [x] Render a WPF screenshot of updated recording confidence/device states.
- [x] Keep live all-monitor and long-recording proof in the manual lane.

Primary code surfaces:

- [ ] `src/GoatShot.App/Services/RecordingService.cs`
- [ ] `src/GoatShot.App/Services/RecordingConfidenceService.cs`
- [ ] `src/GoatShot.App/Services/RecordingEnginePlanner.cs`
- [ ] `src/GoatShot.App/Services/RecordingPanelPresenter.cs`
- [ ] `src/GoatShot.App/Services/DiagnosticsService.cs`
- [ ] `src/GoatShot.App/Models/RecordingSettings.cs`
- [ ] `src/GoatShot.Cli/Program.cs`

Proof TODOs:

- [x] Recording planner/confidence/presenter tests.
- [x] Media metadata parser tests, including no-`ffprobe` skipped state.
- [x] CLI diagnostics/preflight smoke output.
- [x] WPF screenshot under `artifacts/product-design-audit/2026-06-15/recording-field-proof/`.
- [x] Tranche note at `artifacts/tranche-recording-field-proof/notes.md`.

## Tranche B - Manual Validation Harness

Goal: make human/device/manual proof repeatable instead of leaving it as loose prose.

Implementation TODOs:

- [x] Add a script or CLI command that creates a dated manual-validation evidence folder.
- [x] Generate blank Markdown templates for keyboard traversal, screen reader, text scaling, high contrast, region drag, multi-monitor capture, multi-monitor recording, long recording, clean-machine install, and live provider account proof.
- [x] Add redaction reminders and safe-content rules to every template.
- [x] Add current diagnostics bundle references and command reminders.
- [x] Add a sample generated folder under `artifacts/tranche-manual-validation-harness/`.

Proof TODOs:

- [x] Script/CLI tests where practical.
- [x] Generated sample evidence folder.
- [x] Diagnostics redaction smoke.
- [x] Tranche note at `artifacts/tranche-manual-validation-harness/notes.md`.

## Tranche C - Advanced Local Video Editing

Goal: extend local FFmpeg-backed video tools while keeping edits previewable, reversible, and explicit.

Implementation TODOs:

- [x] Add silence-removal analysis that produces a previewable cut list before export.
- [x] Add text-based edit planning from transcript/SRT timestamps.
- [x] Add filler-word removal planning from transcript terms without auto-deleting content.
- [x] Add composite screen/webcam layout export recipes.
- [x] Add reviewed keyed webcam-background blur/removal/replacement behind capability probing, preview planning, and explicit export acceptance.
- [x] Add thumbnails/previews for generated edit plans where feasible.

Proof TODOs:

- [x] Video command argument validation tests.
- [x] Fixture exports or dry-run edit plans.
- [x] Transcript/cut-list tests.
- [x] Tranche note at `artifacts/tranche-advanced-video-editor/notes.md`.

## Tranche D - Android ADB Capture

Goal: add optional Android screenshot capture without blocking the desktop product.

Implementation TODOs:

- [x] Add ADB discovery and diagnostics.
- [x] Implement `adb exec-out screencap -p` import into the GoatShot workspace.
- [x] Handle missing ADB, no device, unauthorized device, offline device, multiple devices, and failed capture states.
- [x] Add CLI capture/diagnostics commands.
- [x] Keep Android recording/video streaming out of scope until screenshot capture is stable.

Proof TODOs:

- [x] Fake ADB process tests.
- [x] Parser/service tests.
- [x] CLI diagnostics artifact on this machine. A ready device was detected, so automatic screencap was skipped for privacy; explicit missing-ADB diagnostics/capture failure artifacts were saved instead.
- [x] Tranche note at `artifacts/tranche-android-adb-capture/notes.md`.

## Tranche E - Browser Extension Contract And Prototype

Goal: start perfect DOM/page capture as an optional module without changing the desktop baseline.

Implementation TODOs:

- [x] Define the extension-to-desktop handoff contract for page geometry, DOM metadata, full-page capture intent, and optional bug-report telemetry.
- [x] Add a minimal extension manifest and content-script prototype.
- [x] Keep telemetry opt-in and explicitly consented.
- [x] Add fixture validation for page payloads and redaction rules.
- [x] Do not claim extension parity until an end-to-end browser proof exists.

Proof TODOs:

- [x] Contract/fixture tests.
- [x] Sample DOM/page payloads.
- [x] Privacy redaction checks.
- [x] Tranche note at `artifacts/tranche-browser-extension/notes.md`.

## Tranche F - Virtual Printer Import Path

Goal: design the import handoff before committing to driver-level installer work.

Implementation TODOs:

- [x] Define a local file-drop/import contract for print-to-image/PDF handoff.
- [x] Add watched-folder import rules for PDF/image outputs.
- [x] Preserve source-app metadata where available.
- [x] Document that true virtual-printer driver installation is installer/admin-scoped and not locally proven yet.

Proof TODOs:

- [x] Watched-folder import tests.
- [x] Safe sample PDF/image imports.
- [x] Diagnostics note.
- [x] Tranche note at `artifacts/tranche-virtual-printer-import/notes.md`.

## Tranche G - Plugin SDK And Local Extension Points

Goal: let power users extend GoatShot locally without weakening policy, redaction, or diagnostics.

Implementation TODOs:

- [x] Define a minimal plugin manifest for local actions, share destinations, workflow actions, and diagnostics.
- [x] Add local plugin discovery from an app-owned folder.
- [x] Keep discovered plugins disabled by default until trusted/enabled.
- [x] Add diagnostics for plugin id, version, trust state, and allowed/blocked actions.
- [x] Add sample local plugin fixtures with no network side effects.
- [x] Keep hosted marketplace or remote plugin installation out of scope.

Proof TODOs:

- [x] Manifest parser tests.
- [x] Policy/allowlist tests.
- [x] Sample plugin dry-run proof.
- [x] Diagnostics redaction checks.
- [x] Tranche note at `artifacts/tranche-plugin-sdk/notes.md`.

## Completed Tranche H - Companion Portal And Team/Admin Planning

Goal: keep shared/team/hosted workflows separate from the local-first desktop MVP until boundaries are approved.

Implementation TODOs:

- [x] Write an architecture note for optional hosted/self-hosted companion portal boundaries.
- [x] Define what syncs, what stays local, and what requires consent.
- [x] Define what cannot bypass desktop policy.
- [x] Define team/admin requirements separately from current managed-policy keys.
- [x] Do not implement a hosted service until the boundary note is approved.

Proof TODOs:

- [x] Architecture note.
- [x] Threat/privacy checklist.
- [x] Tranche note at `artifacts/tranche-companion-portal-planning/notes.md`.

## Tranche I - Browser Extension Native Bridge Follow-Through

Goal: turn the existing browser extension contract/prototype into a local end-to-end handoff.

Implementation TODOs:

- [x] Define the native messaging or local bridge installer boundary.
- [x] Add a bounded local handoff receiver that validates `goatshot.browser-capture.v1` payloads before import.
- [x] Import consented full-page bitmap/page payloads into the workspace when the bridge is available.
- [x] Keep telemetry opt-in and bounded; never collect cookies, headers, form values, local/session storage, or raw DOM text dumps.
- [x] Add clear disabled/missing-bridge diagnostics and docs.

Proof TODOs:

- [x] Receiver tests with accepted/rejected payload fixtures.
- [x] Redaction tests.
- [x] Local bridge smoke with safe fixtures.
- [x] Tranche note at `artifacts/tranche-browser-native-bridge/notes.md`.

## Completed Tranche J - Android Video Expansion Decision

Goal: decide whether Android remains screenshot-only or expands into safe video import.

Implementation TODOs:

- [x] Write an architecture note comparing ADB screencap polling, `adb shell screenrecord` pull/import, and live streaming options.
- [x] Add bounded `adb shell screenrecord --time-limit` import through CLI `capture android-video` / `capture android screenrecord`.
- [x] Keep live device video proof opt-in and privacy-gated.
- [x] Do not start Android live streaming until screenshot/video import is stable.

Proof TODOs:

- [x] Fake ADB command tests.
- [x] Safe-device manual proof boundary documented; real device proof only with staged safe content.
- [x] Tranche note at `artifacts/tranche-android-video-decision/notes.md`.

## Completed Tranche K - Browser Native Host Registration

Goal: turn the browser native bridge into a user-scope native messaging host registration path without browser-store claims.

Implementation TODOs:

- [x] Add stdio native-host run mode to `GoatShot.Cli.exe`.
- [x] Add Chrome, Edge, and Firefox native messaging manifest generation.
- [x] Add user-scope Chrome/Edge HKCU registration and Firefox profile-folder manifest installation commands.
- [x] Add status and uninstall commands.
- [x] Update the prototype extension with `nativeMessaging` permission and a service-worker handoff.
- [x] Keep browser-store publication and automatic extension installation later-scope; Edge live fixture proof is complete, and Chrome/Firefox live fixture proof remains later/manual if needed.

Proof TODOs:

- [x] Fake registry install/uninstall tests.
- [x] Native-message validation/redaction tests.
- [x] CLI status/manifest artifacts.
- [x] Tranche note at `artifacts/tranche-browser-native-host-registration/notes.md`.

## Completed Tranche L - Guarded Local Plugin Execution

Goal: execute local plugin actions only after the existing trust, enable, and action allowlist gates pass.

Implementation TODOs:

- [x] Add `execution` metadata support to plugin actions.
- [x] Add `plugins run <plugin-id> <action-id>` CLI command.
- [x] Enforce global plugin enablement, plugin trust, plugin enablement, and action allowlist before process start.
- [x] Add bounded timeout, redacted stdout/stderr, exit-code reporting, timeout reporting, and plugin-directory working-directory guard.
- [x] Keep remote plugin install governed locally, with unattended updates and hosted marketplace behavior later-scope.

Proof TODOs:

- [x] Blocked/run/timeout tests.
- [x] Sample plugin CLI run proof.
- [x] Tranche note at `artifacts/tranche-plugin-execution/notes.md`.

## Recommended Execution Order

1. Pick a dedicated later lane only if desired: browser-store publication/Chrome-Firefox live browser fixture proof beyond the completed Edge proof, OS printer driver install, Android live streaming, portal implementation, or team/admin mode.
2. Otherwise run manual proof lanes when safe hardware/accounts are available.

## Parked Backlog

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Live refresh-token persistence, expiry, recovery, and reauthorization proof.
- [ ] Provider-specific OAuth scope/copy/account diagnostics polish.
- [ ] Live cloud upload proof and live-account remote delete behavior.
- [ ] Clean-machine installer proof unless a dedicated packaging/install validation lane is scheduled.
- [ ] Full accessibility compliance claims; only claim observed keyboard, focus, contrast, scaling, or screen-reader evidence after manual proof exists.

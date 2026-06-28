# GoatShot OAuth-Parked Forward Buildout TODOs

Date: 2026-06-15

Purpose: continue implementing the remaining GoatShot roadmap while leaving live OAuth consent/account proof where it is for now. This is the current execution plan to use instead of blocking on Google Drive, Dropbox, OneDrive, or other live provider consent screens.

No Git workflow is required for this project right now.

## Scope Boundary

- [ ] Keep the app native WPF/.NET; do not introduce a web stack for the desktop product.
- [ ] Leave OAuth authorization-code/client/refresh plumbing as-is unless a non-OAuth task exposes a small compatibility bug.
- [ ] Keep live Google Drive, Dropbox, OneDrive, and future OAuth consent-screen proof parked.
- [ ] Keep live provider-account upload proof, refresh-token expiry/recovery proof, and remote-delete proof parked.
- [ ] Do not claim full accessibility compliance, clean-machine installer readiness, long-run hardware stability, or live cloud readiness without fresh proof.
- [ ] Continue proving each tranche with local tests, fake providers/processes, safe artifacts, diagnostics, screenshots where UI changes, and portable packaging.

## Current Baseline

- [x] Native WPF desktop app, tray path, CLI, diagnostics, MSTest project, local artifacts, Product Design/WPF audit notes, and portable ZIP packaging exist.
- [x] Capture, scrolling capture, recording, editor/privacy tools, AI/document workflows, upload queue/history, provider adapters, workflow automation, manual validation harness, Android ADB screenshot import, browser extension contract/prototype/native CLI receiver, and advanced video edit planning are locally proven in prior tranche artifacts.
- [x] OAuth/live-account consent is parked by decision, not treated as completed.
- [x] Remaining local buildout is concentrated in dedicated later lanes: live Android proof/streaming beyond bounded `screenrecord` import and dry-run preview planning, browser-store publication/automatic install/live browser fixture proof beyond the native-host receiver and local diagnostics, automatic plugin install/trust/enable/allowlist/execute updates and hosted marketplace behavior beyond governed local staging/install-staged/update-apply/background check-stage support, OS virtual-printer driver proof beyond the file-drop path, portal implementation after boundary approval, and manual proof lanes.

## Completed Phase 0 - Stabilize The Current Virtual-Printer Worktree

Goal: start from the code that is already partially present, make it compile, and turn it into a proven tranche before adding a new module.

- [x] Verify the current `VirtualPrinterImportService`, `print-import` CLI, diagnostics, settings migration, watch-folder, workflow-profile, and workspace import changes compile together.
- [x] Add or repair tests for virtual-printer import, PDF routing, image routing, unsupported extensions, watched-folder inclusion, settings migration v11, and workflow-profile export/import fields.
- [x] Create safe sample PDF/image fixtures under `artifacts/tranche-virtual-printer-import/`.
- [x] Run the `print-import contract` and `print-import import` CLI smoke commands against isolated local/library roots.
- [x] Update README/spec/readiness artifacts only after proof exists.
- [x] Finish with `artifacts/tranche-virtual-printer-import/notes.md`.

Proof:

- [x] `dotnet test .\GoatShot.slnx -c Release --filter VirtualPrinterImport`
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `--help`, `diagnostics print`, `print-import contract`, and `print-import import` output saved under the tranche artifact folder.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`

## Completed Phase 1 - Virtual Printer Import Path

Goal: support print-to-file style capture through a safe local file-drop/import path before driver-level installer work.

- [x] Define the local file-drop/import contract for print-to-PDF/image handoff.
- [x] Add a default app-owned drop folder and allow user-configured folder override.
- [x] Add watched-folder import rules for PDF and image outputs while preserving existing image/video watch behavior.
- [x] Preserve source-app/document-title metadata where available, and redact sensitive paths/titles in diagnostics or notes.
- [x] Route PDFs/documents into the documents library and images into the image library.
- [x] Add diagnostics that show enabled/disabled state, folder, subdirectory behavior, supported extensions, and driver-install boundary.
- [x] Document that true virtual printer driver installation is installer/admin-scoped and remains unproven until a clean-machine lane.

Proof:

- [x] Service and automation tests.
- [x] Safe sample PDF import.
- [x] Safe sample PNG/image import.
- [x] Diagnostics output.
- [x] Tranche note with explicit boundary: file-drop import is proven; OS printer driver install is not.

## Completed Phase 2 - Plugin SDK And Local Extension Points

Goal: let power users extend GoatShot locally without weakening policy, trust, redaction, or diagnostics.

- [x] Define a minimal plugin manifest for local actions, share destinations, workflow actions, and diagnostics contributions.
- [x] Add plugin discovery from an app-owned local folder.
- [x] Keep discovered plugins disabled/untrusted by default.
- [x] Add trust-state and allowlist checks before any plugin action can run.
- [x] Add dry-run execution for plugin actions before real side effects are allowed.
- [x] Add diagnostics for plugin id, version, source path, trust state, allowed actions, blocked actions, and parse errors.
- [x] Add sample plugin fixtures with no network side effects.
- [x] Keep hosted marketplace behavior and automatic install/trust/enable/allowlist/execute updates out of scope for this early local-plugin tranche; later governed remote staging and background check/stage-only support is tracked in the remote plugin tranches.

Proof:

- [x] Manifest parser tests.
- [x] Policy/allowlist/trust-state tests.
- [x] Sample plugin dry-run proof.
- [x] Diagnostics redaction checks.
- [x] `artifacts/tranche-plugin-sdk/notes.md`.

## Completed Phase 3 - Browser Extension Native Bridge Follow-Through

Goal: turn the existing contract/prototype into a local native receiver handoff without changing the native desktop baseline.

- [x] Define the native messaging or local bridge installer boundary.
- [x] Add a bounded local handoff receiver that validates `goatshot.browser-capture.v1` payloads before import.
- [x] Import consented full-page bitmap/page payloads into the workspace when the bridge is available.
- [x] Keep telemetry opt-in and bounded; never collect cookies, headers, form values, local/session storage, or raw DOM text dumps.
- [x] Add clear disabled/missing-bridge state in diagnostics and docs.
- [x] Keep browser-store publication/submission/review and cross-browser publication as a later release task.

Proof:

- [x] Contract/receiver tests with accepted and rejected payload fixtures.
- [x] Redaction tests for URLs, query tokens, console messages, and network summaries.
- [x] Local bridge smoke with safe fixture payloads.
- [x] `artifacts/tranche-browser-native-bridge/notes.md`.

## Completed Phase 4 - Android Expansion Decision

Goal: decide whether Android should remain screenshot-only or expand into recording/streaming.

- [x] Write a short architecture note comparing ADB screencap polling, screenrecord pull/import, and live streaming options.
- [x] Add bounded `adb shell screenrecord --time-limit` import through CLI `capture android-video` / `capture android screenrecord`.
- [x] Keep live device video proof opt-in and privacy-gated.
- [x] Do not start Android live streaming until screenshot/video import is stable.

Proof:

- [x] Fake ADB command tests for screenrecord start/pull/cleanup/failure states.
- [x] Safe-device manual proof boundary documented; real proof only with staged safe phone content.
- [x] `artifacts/tranche-android-video-decision/notes.md`.

## Completed Phase 5 - Companion Portal And Team/Admin Boundaries

Goal: separate hosted/shared/team work from the local-first desktop MVP before building services.

- [x] Write an architecture note for optional hosted/self-hosted companion portal boundaries.
- [x] Define what can sync, what must stay local, and what requires explicit consent.
- [x] Define how portal/team policy relates to existing managed-policy keys.
- [x] Define what the portal cannot bypass: desktop policy, redaction, local proof, user consent, path boundaries, or provider-account boundaries.
- [x] Define team/admin mode separately from individual managed Windows policy keys.
- [x] Do not implement a hosted service until this boundary is approved.

Proof:

- [x] Architecture note.
- [x] Threat/privacy checklist.
- [x] `artifacts/tranche-companion-portal-planning/notes.md`.

## Completed Phase 6 - Browser Native Host Registration

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
- [x] `artifacts/tranche-browser-native-host-registration/notes.md`.

## Completed Phase 7 - Guarded Local Plugin Execution

Goal: execute local plugin actions only after the existing trust, enable, and action allowlist gates pass.

- [x] Add `execution` metadata support to plugin actions.
- [x] Add `plugins run <plugin-id> <action-id>` CLI command.
- [x] Enforce global plugin enablement, plugin trust, plugin enablement, and action allowlist before process start.
- [x] Add bounded timeout, redacted stdout/stderr, exit-code reporting, timeout reporting, and plugin-directory working-directory guard.
- [x] Keep remote plugin acquisition in the remote scaffold, staged activation, explicit update-apply, and governed background check/stage-only tranches; automatic install/trust/enable/allowlist/execute updates and hosted marketplace behavior remain later-scope.

Proof:

- [x] Blocked/run/timeout tests.
- [x] Sample plugin CLI run proof.
- [x] `artifacts/tranche-plugin-execution/notes.md`.

## Parallel Manual Proof Backlog

These can run whenever safe hardware/account conditions are available. They should not block the non-OAuth implementation phases above.

- [ ] Live keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA checks for key WPF flows.
- [ ] Windows text scaling and high-contrast checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording with safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Clean-machine portable ZIP proof.
- [ ] Optional installer proof with admin/install prerequisites.
- [ ] Live provider proof with disposable real accounts when OAuth is unparked.

## OAuth Parked Backlog

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence, expiry, recovery, and reauthorization proof against live accounts.
- [ ] Provider-specific OAuth scope/copy/account diagnostics polish.
- [ ] Live cloud upload proof.
- [ ] Live-account remote-delete behavior where provider APIs support it.

## Definition Of Done For Every Implementation Phase

- [ ] Focused tests cover changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when prompts, transcripts, OCR text, URLs, tokens, logs, settings, file paths, or telemetry payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.
- [ ] Refresh `artifacts/active-non-oauth-buildout-todos.md`, `artifacts/non-oauth-continuation-plan.md`, `artifacts/current-implementation-todos-oauth-parked.md`, and `artifacts/v1-readiness-summary.md` after each completed tranche.

## Recommended Execution Order

1. Pick a dedicated later lane only if desired: browser-store publication/in-browser live proof, OS printer driver install, portal implementation, team/admin remote mode, or production Android live streaming beyond the dry-run planner.
2. Otherwise run manual proof lanes when safe hardware/accounts are available.

Next move: select a dedicated later lane or manual proof lane.

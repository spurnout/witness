# GoatShot OAuth-Parked Remaining Implementation TODOs

Date: 2026-06-14

Purpose: keep GoatShot moving toward a complete native Screenpresso/Snagit/ShareX-style MVP and V1 without blocking on live OAuth consent screens. OAuth authorization-code plumbing can stay where it is until a later provider-consent proof tranche.

This is the current execution plan. `artifacts/remaining-buildout-todos.md` remains the broad historical backlog, and `artifacts/next-buildout-todos-oauth-parked.md` remains the detailed prior queue. This file is the shorter "what do we build next" list.

## Working Rules

- [ ] Keep Google Drive, OneDrive, Dropbox, and provider-consent-screen polish parked.
- [ ] Do not claim live cloud-account readiness, live consent proof, or refresh-token reliability until a later explicit proof tranche runs.
- [ ] Keep provider diagnostics honest: local configuration readiness is not live account proof.
- [ ] Prefer local proof: fake providers, deterministic service tests, WPF render screenshots, safe synthetic capture artifacts, CLI smoke, diagnostics, and portable package output.
- [ ] Do not require a git workflow for this project right now.

## Already Built Enough To Treat As Baseline

- [x] Native WPF/.NET app, CLI, tests, diagnostics, and portable package path.
- [x] Product Design screenshot-backed WPF audit artifacts for main window, settings, editor, tray, capture overlay, recording controls, and upload windows.
- [x] GDI fallback screenshot capture plus opt-in Windows.Graphics.Capture still-capture path.
- [x] MP4/GIF recording with WGC/D3D frame capture where supported, GDI/FFmpeg fallback paths, native Media Foundation H.264, audio controls, webcam overlay, and smoke artifacts.
- [x] Scrolling capture and manual stitch foundations, including vertical/horizontal profiles and sticky leading-edge mitigation.
- [x] Editor privacy tool grouping, OCR/redaction, AI prompt box, prompt reuse, and screenshot proof.
- [x] Local video tools, transcription paths, AI video summary/title/chapter drafting, and document export foundations.
- [x] Sharing facade with local/export, custom script/webhook, WebDAV, Slack, Discord, Teams, FTP/FTPS, S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, and Linear executable adapters plus remaining cloud token/OAuth paths.
- [x] Upload queue basics, desktop process/cancel/retry controls, CLI queue list/process/cancel/retry, searchable redacted share history, and fake-provider retry/cancel tests.
- [x] Before-upload confirmation and after-upload result windows with copy/open/Markdown/QR/retry actions.
- [x] Workflow rules, profiles, import/export, Settings rule manager, CLI dry-run/run, and automation proof for common local actions.

## Next Tranche 1: Upload Queue And History Operator Polish

Goal: make queued sharing and upload history feel like a real operator surface rather than a diagnostic afterthought.

- [x] Inspect existing queue/history commands before editing; current CLI already has `queue list/process/cancel/retry` and filtered `share-history`.
- [x] Add a queue diagnostics command or mode that reports counts by status, next due retry, backoff settings, max attempts, queue path, and redaction posture.
- [x] Add clearer queue status presentation for pending, processing, waiting retry, failed, canceled, and completed items.
- [x] Add retry/backoff detail text without exposing URLs, bearer tokens, webhook paths, usernames, or secret material.
- [x] Add a proper desktop upload-history surface to replace the current summary-only flow, with search/filter plus copy URL, copy Markdown, open URL, retry failed item, and cancel pending item where applicable.
- [x] Add history action model tests so UI/CLI behavior can be proven without live providers.
- [x] Add CLI smoke output for queue diagnostics and history actions.
- [x] Update README and `artifacts/tranche-upload-queue-history/notes.md`.
- [x] Proof: focused queue/history tests, Release build/test, CLI help, CLI diagnostics, queue diagnostics output, and package with `-SkipInstaller`.

## Next Tranche 2: Provider Extraction Carry-On

Goal: continue moving provider execution behind concrete adapters, while keeping OAuth consent polish parked.

- [x] Extract S3-compatible signed PUT into an executable provider adapter with fake HTTP success/failure tests.
- [x] Extract Imgur Client-ID upload into an executable provider adapter with fake HTTP success/failure tests.
- [x] Extract Cloudinary upload into an executable provider adapter with fake HTTP success/failure tests.
- [x] Extract SFTP into a testable adapter around an injectable process runner or command executor.
- [x] Extract Linear token/key flow into an adapter.
- [ ] Keep Dropbox, Google Drive, and OneDrive live OAuth consent polish parked; only add local/fake API tests around existing bearer-token and upload-session behavior.
- [x] Keep `ShareService` as the stable facade until all callers are safely routed through adapters.
- [x] Preserve DPAPI-backed secrets, redacted diagnostics, and redacted share history as invariants.
- [x] Update `artifacts/tranche-provider-adapters-next/notes.md`.
- [x] Proof: focused adapter tests, fake HTTP/process logs, diagnostics redaction assertions, full Release test run, CLI smoke, and package.

## Next Tranche 3: Capture Overlay Polish

Goal: improve the daily screenshot interaction without changing the core capture engine.

- [x] Add edge snapping for region selection.
- [x] Add window/control-area auto-detection for common target bounds.
- [x] Add a pixel zoom lens during region selection.
- [x] Add configurable capture-context padding around selected regions.
- [x] Add a window/monitor chooser for users who do not want active-window or active-monitor shortcuts.
- [x] Add geometry tests for snapping, padding, monitor bounds, and chooser target selection.
- [x] Add safe synthetic overlay screenshots and Product Design notes under `artifacts/tranche-capture-overlay-polish/`.
- [x] Proof: focused capture/geometry tests, render artifact, Release build/test, CLI smoke, and package.

## Next Tranche 4: Scrolling Capture Stress Lane

Goal: make scrolling capture trustworthy against repeatable local targets.

- [x] Build local synthetic scroll targets for plain page, document-like page, large table, sticky header, and horizontal scrolling scenarios.
- [x] Add deterministic stitcher tests for sticky-header mitigation.
- [x] Add deterministic stitcher tests for horizontal table stitching.
- [x] Add preview/retry messages when simulated scrolling cannot control the target.
- [x] Preserve perfect DOM/page capture for the later browser-extension module.
- [x] Update `artifacts/tranche-scrolling-capture-stress/notes.md`.
- [x] Proof: stitcher tests, generated scroll/stitch artifacts, Release build/test, CLI smoke, and package.

## Next Tranche 5: Recording Confidence And Device States

Goal: harden the already-built recording stack around real-world Windows failure modes.

- [ ] Add multi-monitor and cross-monitor region proof helpers that avoid retaining private desktop captures by default.
- [ ] Add microphone, system-audio, and camera permission-denied states in the WPF recording UI.
- [ ] Add device-disconnect states and recovery guidance for audio/camera capture.
- [ ] Add deeper timestamp logging for mic/system-audio sync if current duration-delta checks are insufficient.
- [ ] Add recording profile presets for small share, 1080p60, and 4K60 where hardware supports them.
- [ ] Add HEVC encode opt-in only after diagnostics report Media Foundation support and the failure states are clear.
- [ ] Update `artifacts/tranche-recording-confidence/notes.md`.
- [ ] Proof: focused recording tests, safe fixed-region smoke, device diagnostics, optional ffprobe metadata, Release build/test, CLI smoke, and package.

## Completed Tranche 6: Editor And Privacy Tool Completion

Goal: close the remaining Screenpresso/Snagit-style editor gaps and strengthen privacy review.

- [x] Add freehand drawing.
- [x] Add spotlight area.
- [x] Add print/export handoff from selected captures.
- [x] Add clearer review UI for detected sensitive OCR regions before flattened export.
- [x] Add keyboard tool-selection and focus-order proof for toolbar, canvas, export, AI prompt, and privacy tools.
- [x] Keep real-time recording blur as later scope unless the preview/reversibility UX is solid.
- [x] Update `artifacts/tranche-editor-privacy-tools/notes.md`.
- [x] Proof: editor service tests, WPF screenshots/accessibility notes, Release build/test, CLI smoke, and package.

## Next Tranche 7: Workflow Task Surface And CLI Parity

Goal: make automation inspectable and easy to operate after captures, recordings, OCR, uploads, and AI events.

- [ ] Add an after-capture quick task window with open, edit, copy, share, AI, document, and delete-local-with-confirmation actions.
- [ ] Add rule execution logs that explain skipped conditions and blocked actions.
- [ ] Add script/webhook dry-run from desktop and CLI.
- [ ] Add workflow import/export validation command.
- [ ] Add provider diagnostics filters if the existing diagnostics command is not enough for operators.
- [ ] Add tests proving Settings round-trips advanced fields without dropping hidden future fields.
- [ ] Update `artifacts/tranche-workflow-task-surface/notes.md`.
- [ ] Proof: workflow tests, CLI dry-run smoke, WPF screenshot proof, Release build/test, CLI diagnostics, and package.

## Next Tranche 8: AI, Video Intelligence, And Documentation Review Loop

Goal: make existing AI/video/doc plumbing useful as an explicit review loop while keeping AI optional and privacy-explicit.

- [ ] Add desktop accept/reject/iterate controls where AI action history already stores review status.
- [ ] Add prompt-history picker for video/document workflows.
- [ ] Add retry-with-different-model/profile recovery for failed AI actions.
- [ ] Generate richer bug reports from recordings using transcript, keyframes, environment, and redacted context.
- [ ] Keep long-recording transcription local/Whisper-first until a media-upload provider path is intentionally added.
- [ ] Update `artifacts/tranche-ai-document-workflow/notes.md`.
- [ ] Proof: fake-provider/local-fixture AI tests, document exports, redaction checks, Release build/test, CLI smoke, and package.

## Next Tranche 9: Release Proof And Managed Posture

Goal: make the project handoff-ready without relying on live cloud accounts.

- [ ] Build a release proof bundle with build/test/package logs, diagnostics redaction proof, and selected screenshots.
- [ ] Add compiled installer proof when Inno Setup is available; keep portable zip as the default proof path.
- [ ] Add optional policy keys for disabling AI, disabling uploads, and restricting providers.
- [ ] Add diagnostics that show policy source and effective state.
- [ ] Document managed Windows deployment behavior.
- [ ] Update `artifacts/tranche-release-proof-admin/notes.md`.
- [ ] Proof: release bundle, package output, policy diagnostics tests, Release build/test, CLI smoke, and package.

## Later Modules

- [ ] Browser extension for perfect DOM/page capture and optional consented bug-report telemetry.
- [ ] Android device capture through ADB/screencap.
- [ ] Virtual printer capture.
- [x] Reviewed advanced video cut-plan export for text-based, silence, and filler-word plans (`video apply-plan --accept-plan`).
- [x] Reviewed composite camera/screen layout export (`video apply-composite --accept-plan`).
- [x] Reviewed keyed webcam-background blur/removal/replacement export (`video apply-background --accept-plan`).
- [ ] Advanced video editor remainder: general AI/person-segmentation webcam background processing beyond keyed chromakey-style processing.
- [ ] Plugin SDK and optional hosted/self-hosted companion portal.
- [ ] Team/admin mode as a separate post-V1 module.

## Standard Definition Of Done

- [ ] Focused tests for changed services, CLI behavior, and UI models.
- [ ] WPF screenshot/Product Design artifact for UI changes.
- [ ] Tranche note under `artifacts/tranche-<name>/notes.md`.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Honest remaining-risk note, especially for hardware/manual proof and anything OAuth-adjacent.

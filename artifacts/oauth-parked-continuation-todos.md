# GoatShot OAuth-Parked Continuation TODOs

Date: 2026-06-14

Purpose: keep GoatShot moving toward a complete native WPF/.NET Screenpresso/Snagit/ShareX replacement while leaving OAuth consent-screen polish where it is. Do not require a git workflow. Treat local build, tests, CLI smoke, package output, screenshots, diagnostics, and tranche notes as proof.

Use this file as the next-session implementation queue. `artifacts/oauth-parked-remaining-implementation-todos.md` remains the detailed working ledger; this file is the cleaner "what do we do next" checklist.

## Parked Scope

- [ ] Do not expand Google Drive, OneDrive, Dropbox, or future provider live consent-screen polish.
- [ ] Do not claim live cloud-account readiness, refresh-token reliability, or live provider proof without a later explicit consent/account tranche.
- [ ] Keep OAuth authorization-code plumbing as-is unless a non-OAuth task needs a small compatibility fix.
- [ ] Prefer fake HTTP providers, local fixtures, WPF screenshot renders, safe synthetic capture artifacts, CLI smoke, diagnostics redaction checks, and portable package output.

## Current Baseline

- [x] Native WPF/.NET desktop app, CLI, tests, diagnostics, and portable package path.
- [x] Product Design screenshot-backed WPF audit artifacts for main window, Settings, editor, tray, capture overlay, recording controls, and upload windows.
- [x] Screenshot capture, scrolling capture foundations, local OCR/redaction, editor basics, recording, video tools, transcription, AI drafting, workflow rules, upload queue/history, diagnostics, and package output exist.
- [x] Executable `IShareProvider` adapters exist for local folder, custom script, custom webhook, WebDAV, Discord, Slack, Microsoft Teams, FTP/FTPS, S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, and Linear.
- [x] Upload queue/history has desktop and CLI operator actions for diagnostics, search/filter, copy/open link, retry, cancel, and redacted history proof.

## Tranche 0: Close Current Adapter Proof

Goal: finish the proof and documentation around the S3/Imgur/Cloudinary adapter extraction before starting a larger area.

- [x] Add or refresh `artifacts/tranche-provider-adapters-next/notes.md` with scope, files, tests, and remaining provider risk.
- [x] Update README current-truth/sharing wording so S3-compatible, Imgur, SFTP, and Cloudinary are described as executable adapters.
- [x] Run focused adapter and provider diagnostics tests.
- [x] Run full Release build/test, CLI `--help`, CLI `diagnostics print`, and `scripts/package-release.ps1 -SkipInstaller`.
- [x] Capture provider diagnostics smoke for at least one ready/unready adapter path without secrets.
- [x] Record remaining risk: no live provider-account proof, no OAuth consent proof, and no live SFTP server proof.

## Tranche 1: Finish Non-OAuth Provider Adapter Extraction

Goal: reduce the remaining `ShareService` monolith while staying in token/key/local/fake-provider territory.

- [x] Extract SFTP into a testable adapter around an injectable process runner or command executor.
- [x] Add fake process-runner tests for SFTP success, command failure, host-key/untrusted failure text, cancellation, and URL mapping.
- [x] Extract GitHub Issues into an executable adapter with fake HTTP success/failure tests and redacted diagnostics.
- [x] Extract Jira into an executable adapter with fake HTTP success/failure tests and ADF payload validation.
- [x] Extract Azure DevOps into an executable adapter with fake HTTP success/failure tests and JSON Patch validation.
- [x] Extract Linear into an executable adapter with fake GraphQL/upload success/failure tests.
- [ ] Keep Dropbox, Google Drive, and OneDrive live OAuth polish parked; only add local/fake API tests around existing token/upload-session behavior if useful.
- [ ] Preserve DPAPI-backed secrets, redacted diagnostics, redacted share history, and stable CLI arguments as invariants.
- [x] Proof: focused provider tests, fake HTTP/process logs, diagnostics redaction assertions, full Release build/test, CLI smoke, and package.

## Tranche 2: Capture Overlay Polish

Goal: improve the daily screenshot interaction without changing the core capture engine.

- [x] Add edge snapping for region selection.
- [x] Add window/control-area auto-detection for common target bounds.
- [x] Add pixel zoom lens during region selection.
- [x] Add configurable capture-context padding around selected regions.
- [x] Add window/monitor chooser for users who do not want active-window or active-monitor shortcuts.
- [x] Add geometry tests for snapping, padding, monitor bounds, and chooser target selection.
- [x] Add safe synthetic overlay screenshots plus Product Design notes under `artifacts/tranche-capture-overlay-polish/`.
- [x] Proof: focused capture/geometry tests, render artifact, Release build/test, CLI smoke, and package.

## Tranche 3: Scrolling Capture Stress Lane

Goal: make scrolling capture trustworthy against repeatable local targets.

- [x] Build local synthetic scroll targets for plain page, document-like page, large table, sticky header, and horizontal scrolling scenarios.
- [x] Add deterministic stitcher tests for sticky-header mitigation.
- [x] Add deterministic stitcher tests for horizontal table stitching.
- [x] Add preview/retry messages when simulated scrolling cannot control the target.
- [x] Preserve perfect DOM/page capture for the later browser-extension module.
- [x] Update `artifacts/tranche-scrolling-capture-stress/notes.md`.
- [x] Proof: stitcher tests, generated scroll/stitch artifacts, Release build/test, CLI smoke, and package.

## Tranche 4: Recording Confidence And Device States

Goal: harden the already-built recording stack around real-world Windows failure modes.

- [ ] Add multi-monitor and cross-monitor region proof helpers that avoid retaining private desktop captures by default.
- [ ] Add microphone, system-audio, and camera permission-denied states in the WPF recording UI.
- [ ] Add device-disconnect states and recovery guidance for audio/camera capture.
- [ ] Add deeper timestamp logging for mic/system-audio sync if current duration-delta checks are insufficient.
- [ ] Add recording profile presets for small share, 1080p60, and 4K60 where hardware supports them.
- [ ] Add HEVC encode opt-in only after diagnostics report Media Foundation support and failure states are clear.
- [ ] Update `artifacts/tranche-recording-confidence/notes.md`.
- [ ] Proof: focused recording tests, safe fixed-region smoke, device diagnostics, optional ffprobe metadata, Release build/test, CLI smoke, and package.

## Completed Tranche 5: Editor And Privacy Tool Completion

Goal: close remaining Screenpresso/Snagit-style editor gaps and strengthen privacy review.

- [x] Add freehand drawing.
- [x] Add spotlight area.
- [x] Add print/export handoff from selected captures.
- [x] Add clearer review UI for detected sensitive OCR regions before flattened export.
- [x] Add keyboard tool-selection and focus-order proof for toolbar, canvas, export, AI prompt, and privacy tools.
- [x] Keep real-time recording blur as later scope unless preview/reversibility UX is solid.
- [x] Update `artifacts/tranche-editor-privacy-tools/notes.md`.
- [x] Proof: editor service tests, WPF screenshots/accessibility notes, Release build/test, CLI smoke, and package.

## Tranche 6: Workflow Task Surface And CLI Parity

Goal: make automation inspectable and easy to operate after captures, recordings, OCR, uploads, and AI events.

- [ ] Add after-capture quick task window with open, edit, copy, share, AI, document, and delete-local-with-confirmation actions.
- [ ] Add rule execution logs that explain skipped conditions and blocked actions.
- [ ] Add script/webhook dry-run from desktop and CLI.
- [ ] Add workflow import/export validation command.
- [ ] Add provider diagnostics filters if existing diagnostics output is not enough for operators.
- [ ] Add tests proving Settings round-trips advanced fields without dropping hidden future fields.
- [ ] Update `artifacts/tranche-workflow-task-surface/notes.md`.
- [ ] Proof: workflow tests, CLI dry-run smoke, WPF screenshot proof, Release build/test, CLI diagnostics, and package.

## Tranche 7: AI, Video Intelligence, And Documentation Review Loop

Goal: make existing AI/video/doc plumbing useful as an explicit review loop while keeping AI optional and privacy-explicit.

- [ ] Add desktop accept/reject/iterate controls where AI action history already stores review status.
- [ ] Add prompt-history picker for video/document workflows.
- [ ] Add retry-with-different-model/profile recovery for failed AI actions.
- [ ] Generate richer bug reports from recordings using transcript, keyframes, environment, and redacted context.
- [ ] Keep long-recording transcription local/Whisper-first until a media-upload provider path is intentionally added.
- [ ] Update `artifacts/tranche-ai-document-workflow/notes.md`.
- [ ] Proof: fake-provider/local-fixture AI tests, document exports, redaction checks, Release build/test, CLI smoke, and package.

## Tranche 8: Release Proof And Managed Posture

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

# GoatShot Next Buildout TODOs - OAuth Parked

Date: 2026-06-14

Purpose: keep implementation moving through the remaining GoatShot MVP/V1 work without blocking on live OAuth consent screens. OAuth authorization-code plumbing can stay in its current state until a later consent-proof tranche.

Proof style: each tranche should end with local build, tests, CLI smoke, package output, focused artifacts, and honest notes. No git workflow is required.

Current execution pointer: use `artifacts/remaining-buildout-todo-plan-oauth-parked.md` as the concise next-tranche TODO plan. This file is retained as historical queue context.

Status note, 2026-06-15: workflow task surface, AI/document review, upload-session reliability, recording confidence reconciliation, release proof bundling, managed policy posture, share provider adapter cleanup, and V1 evidence/readiness sweep are now locally proven in later tranche artifacts. The next non-OAuth work is recording field-proof polish.

## Current Working Rule

- [ ] Do not expand Google Drive, OneDrive, Dropbox, or future provider consent-screen polish in the next tranches.
- [ ] Do not claim live OAuth consent proof, refresh-token reliability, or live cloud-account readiness.
- [ ] Keep provider diagnostics explicit: local configuration readiness is not the same thing as live account proof.
- [ ] Prefer locally provable work: fake providers, WPF render screenshots, deterministic service tests, safe synthetic captures, and package diagnostics.

## Tranche 0: Close The Upload Task Window Work

Goal: finish the already-started before/after upload operator UI and make it a proven, documented baseline.

- [x] Verify the before-upload confirmation and after-upload result windows render cleanly at desktop size.
- [x] Confirm external destinations show privacy/risk notes, destination metadata, file metadata, and cancel/continue actions.
- [x] Confirm result windows expose copy link, open link, copy Markdown, QR preview, retry, and disabled delete-remote when unsupported.
- [x] Keep automatic uploads opt-in through workflow rules/settings; do not make upload the default after capture.
- [x] Add/update `artifacts/tranche-upload-task-windows/notes.md`.
- [x] Update `artifacts/remaining-buildout-todos.md` to mark only the completed pieces done.
- [x] Update README current truth if this tranche is fully proven.
- [x] Proof: focused upload-window model/startup tests, screenshot output under `artifacts/product-design-audit/2026-06-14/`, `dotnet build`, `dotnet test`, CLI help/diagnostics, and `scripts/package-release.ps1 -SkipInstaller`.

## Tranche 1: Finish Non-OAuth Provider Adapter Extraction

Goal: keep moving provider execution out of the monolithic share service without touching parked OAuth polish.

- [x] Extract WebDAV into a concrete executable `IShareProvider` adapter.
- [x] Extract FTP/FTPS into a concrete executable `IShareProvider` adapter.
- [x] Extract Discord webhook file upload into a concrete executable `IShareProvider` adapter.
- [x] Extract Slack and Microsoft Teams notification destinations where the current behavior is notification-only.
- [x] Add fake HTTP tests for success, failure diagnostics, cancellation/no-send behavior, and provider readiness diagnostics per extracted HTTP adapter.
- [ ] Keep `ShareService` as the stable facade until every provider branch has moved.
- [ ] Preserve DPAPI-backed secrets and redacted upload history invariants.
- [x] Proof: focused provider adapter tests, provider diagnostics tests, fake-provider logs, and `artifacts/tranche-non-oauth-provider-adapters/notes.md`.

## Tranche 2: Upload Queue And History Operator Polish

Goal: make queued sharing feel reliable and inspectable for local/fake-provider workflows.

- [x] Add searchable upload-history actions for copy URL, copy Markdown, open URL, retry failed item, and cancel pending item.
- [x] Add clearer queued-item status text for pending, retrying, canceled, failed, and completed.
- [x] Add retry/backoff display details without exposing secrets or raw bearer tokens.
- [ ] Add fake resumable/large-file tests for existing upload-session style code paths where feasible.
- [x] Add queue/history CLI operations for list, retry, cancel, and diagnostics.
- [x] Proof: queue service tests, UI model tests, CLI smoke output, diagnostics redaction proof, and `artifacts/tranche-upload-queue-history/notes.md`.

## Tranche 3: Capture Overlay Polish

Goal: improve the daily screenshot capture experience with locally testable overlay behavior.

- [ ] Add edge snapping for region selection.
- [ ] Add window-area auto-detection for common window/control bounds.
- [ ] Add a pixel zoom lens while selecting a region.
- [ ] Add capture-context padding around selected areas.
- [ ] Add a window/monitor chooser for users who do not want active-window/active-monitor shortcuts.
- [ ] Add safe synthetic overlay render proof and geometry tests.
- [ ] Proof: capture geometry tests, overlay screenshot artifact, WPF/Product Design notes, and `artifacts/tranche-capture-overlay-polish/notes.md`.

## Tranche 4: Scrolling Capture Stress Lane

Goal: make scrolling capture trustworthy against repeatable local targets.

- [ ] Build local synthetic scroll targets for plain document, browser-like page, large table, sticky header, and horizontal scrolling.
- [ ] Add deterministic stitcher tests for sticky-header mitigation.
- [ ] Add deterministic stitcher tests for horizontal table stitching.
- [ ] Improve preview/retry messaging when simulated scrolling cannot control the target.
- [ ] Keep perfect DOM/page capture in the later browser-extension track.
- [ ] Proof: stitcher tests, generated scroll artifacts, and `artifacts/tranche-scrolling-capture-stress/notes.md`.

## Tranche 5: Recording Confidence And Device States

Goal: harden the already-built recording stack around real-world failure modes without requiring cloud work.

- [ ] Add multi-monitor and cross-monitor region proof helpers that avoid retaining private desktop captures by default.
- [ ] Add microphone, system-audio, and camera permission-denied states in the WPF recording UI.
- [ ] Add device-disconnect states and recovery guidance for audio/camera capture.
- [ ] Add deeper timestamp logging for mic/system-audio sync if current duration-delta checks are insufficient.
- [ ] Add HEVC diagnostics and opt-in encode path only when Media Foundation reports support.
- [ ] Add recording profile presets for small share, 1080p60, and 4K60 when hardware supports them.
- [ ] Proof: focused recording tests, safe fixed-region recording smoke, device diagnostics, and `artifacts/tranche-recording-confidence/notes.md`.

## Completed Tranche 6: Editor And Privacy Tool Completion

Goal: finish the Screenpresso/Snagit-style editor gaps and make privacy review safer.

- [x] Add freehand drawing.
- [x] Add spotlight area.
- [x] Add print/export handoff from selected captures.
- [x] Add clearer review UI for detected sensitive OCR regions before flattened export.
- [x] Add keyboard tool-selection and focus-order proof for toolbar, canvas, export, AI prompt, and privacy tools.
- [x] Keep real-time recording blur as a later task unless the preview/reversibility UX is solid.
- [x] Proof: editor service tests, WPF screenshots/accessibility notes, and `artifacts/tranche-editor-privacy-tools/notes.md`.

## Tranche 7: Workflow Task Surface And CLI Parity

Goal: turn automation from "configured rules" into inspectable operator workflows.

- [ ] Add after-capture quick task window with open, edit, copy, share, AI, document, and delete-local-with-confirmation actions.
- [ ] Add rule execution logs that explain skipped conditions and blocked actions.
- [ ] Add script/webhook dry-run from desktop and CLI.
- [ ] Add workflow import/export validation command.
- [ ] Add tests that Settings round-trips advanced fields without dropping hidden future fields.
- [ ] Proof: workflow tests, CLI dry-run smoke, UI screenshot proof, and `artifacts/tranche-workflow-task-surface/notes.md`.

## Tranche 8: AI, Video Intelligence, And Documentation

Goal: make the existing AI/video/doc plumbing useful as a review loop, while keeping AI optional and privacy-explicit.

- [ ] Add accept/reject/iterate controls where AI action history already stores review status.
- [ ] Add prompt-history picker for video/document workflows.
- [ ] Add retry-with-different-model/profile recovery for failed AI actions.
- [ ] Generate richer bug reports from recordings using transcript, keyframes, environment, and redacted context.
- [ ] Keep long-recording transcription local/Whisper-first until a media-upload provider path is intentionally added.
- [ ] Proof: fake-provider/local-fixture AI tests, document exports, redaction checks, and `artifacts/tranche-ai-document-workflow/notes.md`.

## Completed Tranche 9: Packaging, Release Proof, And Managed Posture

Goal: make the project handoff-ready without relying on live cloud accounts.

- [x] Build a release proof bundle with build/test/package logs, diagnostics redaction proof, and selected screenshots.
- [x] Keep portable zip as the default proof path; compiled installer and clean-machine proof remain manual/tooling-dependent.
- [x] Add optional policy keys for disabling AI, disabling uploads, restricting providers, custom scripts, and custom webhooks.
- [x] Add diagnostics that show policy source and effective state.
- [x] Document managed Windows deployment behavior.
- [x] Proof: release bundle, package output, policy diagnostics tests, settings render, full Release build/test/CLI/package lane, and `artifacts/tranche-release-proof-admin/notes.md`.

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

## Standard Proof Checklist

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Focused tests for changed services/UI models.
- [ ] WPF screenshot/Product Design artifact for UI changes.
- [ ] Tranche note under `artifacts/tranche-<name>/notes.md`.

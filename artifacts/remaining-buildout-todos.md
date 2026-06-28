# GoatShot Remaining Buildout TODOs

Date: 2026-06-14

Scope: continue building GoatShot from the current native WPF/.NET app. Keep OAuth where it is for now. Do not require a git workflow. Treat local build, tests, CLI smoke, package output, screenshots, and notes as proof.

Next execution backlog: use `artifacts/next-buildout-todos-oauth-parked.md` for the current OAuth-parked implementation order. This file remains the broader historical backlog and proof ledger.

## Current Ground Truth

- The Screenpresso-replacement core is broad now: screenshot capture, WPF workspace, tray actions, editor, scrolling stitcher, local OCR/redaction, video tools, MP4/GIF recording, audio/camera plumbing, provider sharing, upload history/queue, workflow profiles/rules, Gemini image/text/video workflows, diagnostics, and portable packaging all exist.
- OAuth authorization-code setup exists for configured cloud providers, but live provider consent proof and provider-specific OAuth polish remain parked.
- Advanced automation rule fields are now exposed in Settings and documented with screenshot/test/package proof.
- Product Design screenshot-backed WPF evidence exists, but live Tab traversal, screen-reader narration, text scaling, and hardware/manual proof are still separate evidence lanes.

## Immediate Implementation Queue - OAuth Parked

Use this queue as the working order for the next buildout sessions. The point is to keep moving through product surface area that can be completed locally without waiting on real cloud consent screens.

- [x] Tranche A: Provider adapter extraction.
  - [x] Inspect the current `ShareService` execution branches and identify the safest first provider to extract.
  - [x] Define the concrete provider-adapter contract around destination id, execution, returned links, upload history, and redacted errors.
  - [x] Extract local folder export as the first executable `IShareProvider` adapter.
  - [x] Extract custom webhook as the second executable `IShareProvider` adapter.
  - [x] Extract custom script as the third executable `IShareProvider` adapter.
  - [x] Extract WebDAV as the fourth executable `IShareProvider` adapter.
  - [x] Extract Discord webhook as the fifth executable `IShareProvider` adapter.
  - [x] Extract Slack and Microsoft Teams webhook notifications as executable `IShareProvider` adapters.
  - [x] Extract FTP/FTPS as an executable `IShareProvider` adapter with a testable FTP upload-client seam.
  - [x] Extract S3-compatible signed PUT as an executable `IShareProvider` adapter.
  - [x] Extract Imgur Client-ID upload as an executable `IShareProvider` adapter.
  - [x] Extract Cloudinary upload as an executable `IShareProvider` adapter.
  - [x] Extract SFTP as an executable `IShareProvider` adapter with a testable process-runner seam.
  - [x] Extract GitHub Issues as an executable `IShareProvider` adapter.
  - [x] Extract Jira as an executable `IShareProvider` adapter.
  - [x] Extract Azure DevOps as an executable `IShareProvider` adapter.
  - [x] Extract Linear as an executable `IShareProvider` adapter.
  - [x] Continue moving remaining larger non-OAuth network providers one provider at a time in later adapter passes.
  - [x] Keep `ShareService` as the stable facade while moving one provider branch at a time behind adapters.
  - [x] Add focused tests that prove legacy behavior, adapter selection, redacted history, and diagnostics stay stable.
  - [x] Provider adapter proof: focused sharing tests, Release build/test, CLI help/diagnostics, package, `artifacts/tranche-provider-adapters/notes.md`, and `artifacts/tranche-provider-adapters-next/notes.md`.

- [ ] Tranche B: Upload queue reliability and local fake-provider proof.
  - [x] Add fake HTTP provider tests for retry, backoff, cancel, retry-after-failure, and successful completion history.
  - [x] Add local proof for queued upload cancellation that does not require real provider credentials.
  - [x] Add queue diagnostics and clearer status/backoff presentation for local/fake-provider workflows.
  - [x] Add desktop searchable share-history actions plus queue cancel/retry side-panel actions.
  - [x] Add CLI smoke for queue diagnostics and retrying a failed share-history entry into the queue.
  - [ ] Add or tighten resumable/large-file behavior tests for existing non-consent paths where feasible.
  - [ ] Preserve DPAPI-backed secrets and redacted diagnostics/history as invariants.
  - [ ] Proof: queue/provider tests, fake-provider logs, diagnostics redaction check, and `artifacts/tranche-upload-queue-reliability/notes.md`.

- [ ] Tranche C: Before-upload and after-upload operator windows.
  - [x] Add a before-upload confirmation window that shows destination, file metadata, privacy-sensitive notes, and cancel/continue actions.
  - [x] Add an after-upload result window with copy link, open link, copy Markdown, show QR, retry, and disabled delete-remote when the provider does not safely support it yet.
  - [ ] Wire workflow automation hooks to these windows without making automatic upload the default.
  - [x] Add Product Design/WPF screenshot evidence for the new windows.
  - [x] Proof: UI render tests or screenshot renderer output, focused workflow/share tests, and `artifacts/tranche-upload-task-windows/notes.md`.

- [ ] Tranche D: Capture overlay polish.
  - [x] Add edge snapping and window-area auto-detection for region selection.
  - [x] Add a pixel zoom lens and capture-context padding option.
  - [x] Add a window/monitor chooser flow for users who do not want active-window/active-monitor shortcuts.
  - [x] Add focused overlay geometry tests and safe synthetic screenshot proof.
  - [x] Proof: focused capture tests, overlay screenshot artifact, and `artifacts/tranche-capture-overlay-polish/notes.md`.

- [ ] Tranche E: Scrolling capture stress lane.
  - [x] Add local synthetic scroll targets for plain page, document, large table, sticky header, and horizontal scrolling scenarios.
  - [x] Add deterministic stitcher tests for sticky-header mitigation and horizontal table stitching.
  - [x] Improve preview/retry messaging when simulated scrolling cannot control the target.
  - [x] Keep perfect DOM/page capture in the later browser-extension module.
  - [x] Proof: stitcher tests, generated scroll artifacts, and `artifacts/tranche-scrolling-capture-stress/notes.md`.

- [ ] Tranche F: Recording confidence without new OAuth work.
  - [ ] Add multi-monitor and cross-monitor region proof helpers that avoid retaining private desktop captures by default.
  - [ ] Add device-disconnect and permission-denied UI states for microphone, system audio, and camera.
  - [ ] Add deeper timestamp logging for microphone/system audio if the existing duration-delta checks are not enough.
  - [ ] Add HEVC diagnostics and encode-path opt-in only when Media Foundation reports support.
  - [ ] Add recording profile presets for small share, 1080p60, and 4K60 where hardware supports them.
  - [ ] Proof: focused recording tests, safe recording smoke, diagnostics output, and `artifacts/tranche-recording-confidence/notes.md`.

- [x] Tranche G: Editor and privacy completion.
  - [x] Add spotlight and freehand drawing tools.
  - [x] Add print/export handoff from selected captures.
  - [x] Improve detected-sensitive-region review before flattened export.
  - [x] Add keyboard tool-selection/focus-order proof for toolbar, canvas, export, AI prompt, and privacy tools.
  - [x] Proof: editor tests, screenshot/accessibility evidence, and `artifacts/tranche-editor-privacy-tools/notes.md`.

- [ ] Tranche H: Workflow automation task surface.
  - [ ] Add after-capture quick task window with open/edit/copy/share/AI/document actions.
  - [ ] Add rule execution logs that explain skipped conditions and blocked actions.
  - [ ] Add rule import/export validation, provider diagnostics filters, and script/webhook dry-run.
  - [x] Add queue/history CLI diagnostics and retry/list/action polish for the upload-history surface.
  - [ ] Add tests that Settings round-trips advanced fields without dropping hidden future fields.
  - [ ] Proof: workflow tests, CLI smoke, UI screenshot proof, and `artifacts/tranche-workflow-task-surface/notes.md`.

- [ ] Tranche I: AI, video intelligence, and documentation workflow.
  - [ ] Add accept/reject/iterate controls where history already tracks review state.
  - [ ] Add prompt-history picker for video/document workflows.
  - [ ] Add retry-with-different-model/profile recovery for failed AI actions.
  - [ ] Generate richer bug reports from recordings using transcript, keyframes, environment, and redacted context.
  - [ ] Keep long recording transcription local/Whisper-first until a media-upload provider path is explicitly implemented.
  - [ ] Proof: AI/document tests with fake providers or local fixtures, export artifacts, and `artifacts/tranche-ai-document-workflow/notes.md`.

- [ ] Tranche J: Packaging, release proof, and managed posture.
  - [ ] Build a release proof bundle with build/test/package logs, diagnostics redaction proof, and selected screenshots.
  - [ ] Add compiled installer proof when Inno Setup is available; keep portable zip as the default proof path.
  - [ ] Add optional policy keys for disabling AI, disabling uploads, and restricting providers.
  - [ ] Add diagnostics that show policy source and effective state.
  - [ ] Proof: package/installer logs where available, policy diagnostics tests, and `artifacts/tranche-release-proof-admin/notes.md`.

OAuth stays parked across all immediate tranches:

- [ ] Do not expand Google/Dropbox/Microsoft consent-screen polish in these tranches.
- [ ] Do not claim live cloud-consent proof unless a later task explicitly runs it.
- [ ] Keep provider diagnostics honest: local configuration readiness is not live account proof.

## Next Three Tranches

- [x] Finish the current advanced automation Settings tranche.
  - Verified rendered Settings screenshots for advanced capture/file-size/image-effect controls.
  - Updated README, automation tranche notes, and Product Design audit notes so current docs match Settings coverage.
  - Ran full proof: Release build, Release tests, CLI `--help`, CLI `diagnostics print`, package with `-SkipInstaller`.

- [ ] Harden recording for real-world use.
  - [x] Add a repeatable recording smoke harness that records fixed/monitor/window targets for configurable duration and validates output metadata when `ffprobe.exe` is available.
  - [x] Add opt-in audio stream and audio/video duration-delta checks to the recording smoke harness where `ffprobe.exe` is available.
  - [x] Add all-monitor recording through the screenshot-frame path with explicit WGC streaming scope.
  - Add multi-monitor and cross-monitor region proof, including WGC single-monitor fallback behavior.
  - Refresh recording diagnostics and README truth after the proof run.

- [ ] Build the non-OAuth sharing/provider tranche.
  - [x] Extract concrete `IShareProvider` adapters for local folder, custom script, custom webhook, WebDAV, Slack, Discord, Microsoft Teams, FTP/FTPS, S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, and Linear.
  - [x] Extract Linear from the current `ShareService` path so provider catalog entries can execute through adapters instead of the monolith.
  - Add provider diagnostics and tests around retry/backoff/cancel/history behavior with fake local providers.
  - Add the before-upload/after-upload operator UI and QR-for-uploaded-link behavior.
  - Leave cloud OAuth consent and provider-specific OAuth polish parked unless a task absolutely needs it.

## Phase 0: Truth And Proof Cleanup

- [x] Close the automation advanced Settings tranche.
  - [x] Confirm `17-settings-automation-advanced-fields.png` is current and shows the advanced rule controls.
  - [x] Add second focused render proof for image-effect mode/region controls.
  - [x] Add `artifacts/tranche-automation-advanced-settings/notes.md`.
  - [x] Update stale README wording about advanced automation fields.
  - [x] Update the Product Design audit with the new Settings automation evidence.
  - [x] Run focused automation/settings tests.
  - [x] Run full build/test/CLI/package proof.

- [ ] Maintain one current status section.
  - [ ] Keep README "Current Truth" aligned with diagnostics output.
  - [ ] Keep artifact notes scoped by tranche.
  - [ ] Avoid claiming manual/live proof until it was actually run.

## Phase 1: Capture Completion

- [ ] Expand capture polish.
  - [x] Add or improve edge snapping and window-area auto-detection in region selection.
  - [x] Add a pixel zoom lens during region selection.
  - [x] Add capture-context padding around selected areas.
  - [x] Add a window/monitor chooser flow instead of relying only on active window/monitor shortcuts.

- [ ] Stress scrolling capture.
  - [x] Add fixtures or a local synthetic scroll target for browser, document, table, sticky-header, and horizontal scrolling cases.
  - [x] Add deterministic stitcher tests for sticky header mitigation and horizontal table stitching.
  - [x] Improve preview/retry UX when simulated scrolling cannot control the target.
  - [x] Keep perfect DOM/page capture in the later browser-extension lane.

- [ ] Fill CLI capture parity gaps.
  - [ ] Keep interactive `capture region` on the desktop overlay path.
  - [ ] Preserve clear headless unsupported errors.
  - [ ] Add missing window/monitor menu or target-list commands if useful for scripts.

## Phase 2: Recording Hardening

- [x] Add all-monitor recording or explicit unsupported truth.
  - [x] Constrain all-monitor recording to the screenshot-frame capture path across the virtual desktop while WGC streaming remains active monitor/window/single-monitor bounds.
  - [x] Add CLI and desktop controls for all-monitor MP4/GIF recording.
  - [x] Add diagnostics explaining the selected path.

- [ ] Prove long recordings.
  - [x] Add a configurable smoke command/script for recording proof runs.
  - [x] Validate duration, frame count when reported, audio stream presence, and codec metadata when `ffprobe.exe` is available.
  - [ ] Run 5, 10, and 30 minute local stability lanes.
  - [ ] Record stability notes under artifacts.

- [ ] Improve audio/camera confidence.
  - [x] Add opt-in smoke-harness audio stream presence and audio/video duration-delta validation.
  - [ ] Run live muted/unmuted microphone and system-audio smoke lanes after preparing a safe audio environment.
  - [ ] Add deeper timestamp logging for microphone/system audio captures if duration-delta checks are not enough.
  - [ ] Add device-disconnect and permission-denied error states in desktop UI.
  - [ ] Add a camera preview privacy-safe proof for real state transitions.

- [ ] Continue video format work.
  - [ ] Add HEVC path diagnostics and opt-in encode path if Media Foundation support is available.
  - [ ] Keep FFmpeg fallback optional and documented.
  - [ ] Add output profile presets for 1080p60, 4K60 when hardware supports it, and small-share recordings.

## Phase 3: Editor And Privacy

- [ ] Complete remaining Screenpresso/ShareX-style editor tools.
  - [x] Spotlight area.
  - [x] Freehand drawing.
  - [ ] Rounded corners, border effects, drop shadow, torn edges, and reflection effect where they are worth the complexity.
  - [x] Print/export handoff from selected captures.

- [ ] Improve privacy tooling.
  - [ ] Add real-time capture/recording blur pipeline only after it can be made visibly adjustable and reversible before export.
  - [ ] Add a Windows AI Text Recognition adapter for confidence/NPU-capable devices when available.
  - [ ] Keep Windows.Media.Ocr fallback as the dependable local path.
  - [x] Add clearer review UI for detected sensitive regions before flattened export.

- [x] Finish editor accessibility proof.
  - [x] Add keyboard tool selection checks.
  - [x] Add focus order proof for toolbar, canvas, export actions, AI prompt, and privacy tools.

## Phase 4: Workflow Automation

- [ ] Finish desktop rule-manager parity.
  - [ ] Ensure Settings exposes capture kind, monitor, file-size bounds, image-effect mode/region, and common actions.
  - [ ] Keep import/export as the path for uncommon or future fields.
  - [ ] Add tests that Settings round-trips advanced fields without dropping hidden future fields.

- [ ] Add interactive task windows.
  - [ ] After-capture quick task window.
  - [ ] Before-upload confirmation window.
  - [ ] After-upload result window with copy/open/QR/delete-remote where supported.

- [ ] Expand power-user actions carefully.
  - [ ] Optional auto-capture/repeated interval capture.
  - [ ] Named profiles per hotkey and provider where current settings are still global.
  - [ ] Gated local delete after upload, with explicit confirmation and dry-run proof.
  - [ ] Rule execution logs that explain skipped conditions and blocked actions.

- [ ] Extend CLI parity.
  - [ ] Rule import/export validation.
  - [ ] Queue/history operations.
  - [ ] Provider diagnostics filters.
  - [ ] Script/webhook dry-run.

## Phase 5: Sharing And Providers Without OAuth Expansion

- [x] Extract provider adapters.
  - [x] Define the executable adapter map plus concrete local-folder, custom-script, and custom-webhook `IShareProvider` adapters.
  - [x] Extract concrete `IShareProvider` adapters for WebDAV, Slack, Discord, Microsoft Teams, FTP/FTPS, S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, and Linear.
  - [x] Continue extracting Linear as the remaining non-OAuth work-tracking adapter.
  - [ ] Keep credentials in DPAPI-backed `SecretStore`.
  - [ ] Keep upload history redacted.
  - [x] Keep the current `ShareService` behavior green while extracting the first provider.

- [ ] Improve upload queue reliability.
  - [x] Add fake HTTP providers for retry/backoff/cancel tests.
  - [x] Add fake WebDAV/FTP/SFTP proof where feasible.
  - [ ] Add resumable upload retry tests for existing large-file providers.
  - [ ] Add remote-delete support only where the provider has a safe API path and a local audit record.

- [ ] Add non-OAuth UI polish.
  - [ ] Provider setup panes with readiness diagnostics.
  - [ ] Searchable upload history actions.
  - [x] QR generation for returned upload URLs.
  - [ ] Short-link, expiration, and password fields only for providers that support them.

- [ ] Keep OAuth parked.
  - [ ] Do not expand consent-screen polishing in this lane.
  - [ ] Do not claim live Google/Dropbox/Microsoft consent proof.
  - [ ] Let provider diagnostics keep reporting local configuration readiness only.

## Phase 6: AI, Video Intelligence, And Documentation

- [ ] Improve long-recording transcription.
  - [ ] Add a Gemini Files API or equivalent media-upload path later for longer provider transcription.
  - [ ] Keep local Whisper/SRT as the long-recording path until then.
  - [ ] Add clearer privacy notes when audio or transcript text leaves the machine.

- [ ] Make AI review loops easier.
  - [ ] Add accept/reject/iterate controls where history currently stores review state but UI flow is thin.
  - [ ] Add prompt-history picker for video/document workflows.
  - [ ] Add failure recovery actions that retry with a different model/profile.

- [ ] Build richer recording-derived documents.
  - [ ] Generate bug reports from recordings with transcript, keyframes, environment, and redacted context.
  - [ ] Generate tutorial docs from recordings plus step-recorder screenshots.
  - [ ] Export Markdown, HTML, PDF, DOCX, and SRT with consistent artifact naming.

## Phase 7: Packaging, Installer, And Admin Controls

- [ ] Finish installer proof.
  - [ ] Build a compiled installer when Inno Setup is available.
  - [ ] Add install/uninstall smoke notes.
  - [ ] Keep portable zip output as the default proof path.

- [ ] Add admin/deployment posture.
  - [ ] Optional policy keys for disabling AI, disabling uploads, or restricting providers.
  - [ ] Diagnostics that show policy source and effective state.
  - [ ] Documentation for managed Windows deployments.

- [ ] Improve release diagnostics.
  - [ ] Add a release proof bundle that includes build/test/package logs, diagnostics redaction proof, and key screenshots.
  - [ ] Keep capture files and secrets out of bundles.

## Later-Scope Modules

- [ ] Browser extension.
  - [ ] Perfect DOM/page capture.
  - [ ] Browser tab recording or browser telemetry only with explicit consent.
  - [ ] Optional bug-report console/network context.

- [ ] Android device capture.
  - [ ] ADB device discovery.
  - [ ] `screencap` import.
  - [ ] Optional screenrecord import.

- [ ] Virtual printer capture.
  - [ ] Printer driver/import investigation.
  - [ ] Document-to-capture workflow.

- [x] Reviewed advanced video cut-plan export for text-based, silence, and filler-word plans.
- [x] Reviewed composite camera/screen layout export.
- [x] Reviewed keyed webcam-background blur/removal/replacement export.
- [ ] Advanced video editor remainder: general AI/person-segmentation webcam background processing beyond keyed chromakey-style processing.
  - [x] Text-based editing.
  - [x] Silence and filler-word removal.
  - [x] Keyed webcam background blur/removal/replacement.
  - [x] Composite camera/screen layout editor.

- [ ] Extension and team platform.
  - [ ] Plugin SDK.
  - [ ] Optional hosted/self-hosted companion portal.
  - [ ] Team/admin mode as a separate post-V1 module.

## Manual Proof Backlog

- [ ] OAuth consent screens and refresh behavior for Google Drive, Dropbox, OneDrive, and future OAuth providers. Parked for now.
- [ ] Live keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, and recording controls.
- [ ] Narrated screen-reader verification for key WPF flows.
- [ ] Text scaling and high-contrast Windows mode checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Live upload proof against real provider accounts when credentials and consent are available.
- [ ] Installer proof on a clean Windows machine.

## Standard Proof Lane For Each Tranche

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Focused tests for the changed service/UI model.
- [ ] Product Design screenshot or WPF visual-tree proof for UI changes.
- [ ] Artifact note under `artifacts/tranche-<name>/notes.md`.

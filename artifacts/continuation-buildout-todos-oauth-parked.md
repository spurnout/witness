# GoatShot Continuation Buildout TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue building GoatShot from the current native WPF/.NET baseline without waiting on live OAuth consent screens. OAuth setup, live provider account proof, refresh-token recovery, and provider consent copy remain parked. The next work should maximize locally provable product value.

No Git workflow is required for this project right now.

## Scope Rules

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Do not rework OAuth authorization-code or consent flows in this pass.
- [ ] Keep Google Drive, Dropbox, OneDrive, and future live account validation marked as parked manual proof.
- [ ] Treat fake-provider HTTP tests as reliability proof, not live cloud readiness proof.
- [ ] Keep DPAPI-backed secrets, diagnostics redaction, and privacy confirmations as invariants.
- [ ] Finish each tranche with tests, CLI smoke, package proof, and `artifacts/tranche-<name>/notes.md`.

## Completed Tranche 4 - Upload Session Reliability Without Live OAuth

Goal: prove the large-upload and queued-upload behavior using local fake providers and deterministic tests.

Implementation TODOs:

- [x] Inventory current Google Drive, Dropbox, OneDrive, and upload queue paths for resumable/session behavior.
- [x] Add fake HTTP provider tests for upload-session creation, chunk upload, final link handling, and response parsing.
- [x] Add transient failure plus retry/resume proof for upload-session style uploads.
- [x] Add cancel-before-process and cancel-during-retry assertions for queued large uploads.
- [x] Tighten queue history fields for attempts, next retry time, canceled state, and last redacted failure.
- [x] Extend redaction tests for authorization headers, bearer tokens, upload URLs, session URLs, and secret query parameters.
- [x] Keep remote delete disabled unless there is a provider-safe delete API path, audit trail, and local fake-provider proof.

Proof TODOs:

- [x] Focused upload queue/provider tests.
- [x] CLI smoke artifacts for queue list, process, retry, cancel, and history.
- [x] Diagnostics output showing fake-provider proof clearly distinguished from live account proof.
- [x] Release build/test, CLI help, diagnostics print, and package release.
- [x] `artifacts/tranche-upload-queue-reliability/notes.md`.

## Partially Completed Tranche 5 - Recording Field Proof And Profile Presets

Goal: improve confidence and operator feedback for real Windows recording conditions while avoiding private desktop artifact retention.

Implementation TODOs:

- [x] Add safe fixed-region and synthetic-region proof helpers for multi-monitor and cross-monitor scenarios.
- [x] Add explicit recording panel states for microphone permission denied, system-audio unavailable, camera permission denied, and device disconnected.
- [x] Add recovery guidance for Windows privacy permissions, endpoint refresh, and device reconnect.
- [ ] Add deeper audio timestamp and duration logging for microphone/system-audio sync proof.
- [x] Add recording presets: small share, 1080p60, and 4K60.
- [ ] Add HEVC opt-in encode path only when Media Foundation reports support. Current state is diagnostics/probe-only.
- [ ] Keep FFmpeg as fallback and preserve clear failure messaging when hardware paths are unavailable.

Proof TODOs:

- [x] Recording confidence, planner, profile, and presenter tests.
- [x] Safe fixed/all-monitor plan-only recording artifact with no private desktop retention by default.
- [x] Device diagnostics artifact.
- [ ] Optional `ffprobe` metadata artifact when available.
- [ ] WPF screenshot of updated recording confidence states.
- [x] Release build/test, CLI help, diagnostics print, and package release.
- [x] `artifacts/tranche-recording-confidence/notes.md`.

## Completed Tranche 6 - Release Proof And Managed/Admin Posture

Goal: make GoatShot handoff-ready for local/managed Windows use without live cloud account dependencies.

Implementation TODOs:

- [x] Add a release proof bundle script or CLI command that gathers build/test/package logs, diagnostics redaction proof, selected screenshots, and tranche notes.
- [x] Add optional policy keys for disabling AI, disabling uploads, restricting providers, and disabling external scripts/webhooks.
- [x] Add diagnostics for policy source, effective policy state, and overridden settings.
- [x] Add tests for policy defaults, policy precedence, blocked actions, and release-proof redaction.
- [x] Document managed Windows deployment behavior in README and a release handoff note.
- [x] Keep portable zip as the default proof path and record the compiled-installer/clean-machine proof boundary honestly.

Proof TODOs:

- [x] Policy diagnostics tests.
- [x] Release proof bundle under `artifacts/tranche-release-proof-admin/`.
- [x] Portable package output.
- [ ] Optional installer output when tooling exists.
- [x] Release build/test, CLI help, diagnostics print, and package release.
- [x] `artifacts/tranche-release-proof-admin/notes.md`.

## Completed Tranche 7 - Share Provider Adapter Cleanup

Goal: finish non-OAuth provider plumbing polish without touching live consent screens.

Implementation TODOs:

- [x] Inventory remaining executable share branches still living only in `ShareService`.
- [x] Extract remaining non-OAuth branches into concrete `IShareProvider` adapters only where that reduces duplication or improves diagnostics.
- [x] Keep `ShareService` as the stable facade for routing, history, queueing, confirmations, and compatibility.
- [x] Preserve DPAPI-backed secrets, redacted share history, provider diagnostics, before-upload confirmation, and after-upload result behavior.
- [x] Leave OAuth-backed live account providers in their current token/diagnostic posture.

Proof TODOs:

- [x] Focused provider adapter tests.
- [x] Fake HTTP/process/fake-surface proof for locally executable adapters.
- [x] Provider diagnostics smoke showing implemented, configured, missing, policy-blocked, and parked-live-account states.
- [x] Release build/test, CLI help, diagnostics print, and package release.
- [x] `artifacts/tranche-provider-adapter-cleanup/notes.md`.

## Completed Tranche 8 - V1 Evidence And Readiness Sweep

Goal: make the local V1 handoff honest, readable, and easy to resume.

Implementation TODOs:

- [x] Refresh README/spec current-truth sections against implemented code and artifacts.
- [x] Refresh Product Design/WPF screenshot-backed audit notes only for flows changed since the last audit.
- [x] Create a manual validation checklist artifact for keyboard traversal, screen reader pass, high contrast/text scaling, multi-monitor hardware proof, long recording stability, clean-machine installer proof, and live provider account proof.
- [x] Create `artifacts/v1-readiness-summary.md` separating implemented, locally proven, manually unverified, OAuth parked, and later-scope work.

Proof TODOs:

- [x] README/spec consistency scan.
- [x] WPF render screenshots for changed surfaces.
- [x] Full Release build/test, CLI help, CLI diagnostics, package lane, and release proof bundle refresh.
- [x] `artifacts/v1-readiness-summary.md`.

## Manual Proof Backlog

Manual proof should stay separate from locally provable implementation work.

- [ ] Live keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review window, upload queue/history, and share/provider setup.
- [ ] Narrated screen-reader verification for key WPF flows.
- [ ] Windows text scaling and high-contrast mode checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording proof with safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Live upload proof against real provider accounts when credentials and consent are available.
- [ ] Installer proof on a clean Windows machine.

Parked OAuth/manual account TODOs:

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence and expiry recovery for live cloud accounts.
- [ ] Provider-specific consent copy, scopes, and account diagnostics.

## Later Modules After V1

Do not start these until the local V1 proof tranches above are complete.

- [x] Browser extension contract/prototype plus native CLI receiver for consented page handoff.
- [x] Android device screenshot capture through ADB/screencap and bounded MP4 import through `adb shell screenrecord --time-limit`.
- [x] Virtual printer/file-drop import for PDF/image outputs.
- [x] Advanced video editor planning/export with text-based editing, silence removal, filler-word removal, keyed webcam-background processing, and composite screen/camera recipes.
- [x] Local Plugin SDK manifest discovery and dry-run trust gates.
- [x] Browser native messaging host manifest generation and user-scope registration support.
- [ ] Browser-store publication, automatic extension installation, and in-browser full-page stitching.
- [ ] Live Android device proof and Android live streaming beyond bounded import.
- [ ] OS virtual-printer driver installation.
- [x] Guarded local plugin execution after trust/enable/allowlist gates.
- [ ] Unattended background plugin updates and marketplace behavior beyond governed local staging/install-staged/update-apply support.
- [x] Companion portal/team-admin boundaries documented under `artifacts/tranche-companion-portal-planning/`.
- [ ] Hosted/self-hosted companion portal implementation after boundary approval.
- [ ] Team/admin mode as a separate post-V1 module after boundary approval.

## Recommended Execution Order

1. Recording proof follow-up only where it can be locally improved; live multi-monitor/long-recording proof stays manual.
2. Manual accessibility, hardware, provider, and installer proof when the required devices/accounts/time are available.
3. Parked OAuth consent and live account proof as a dedicated future tranche.

## Standard Done Checklist

- [ ] Focused tests for changed services, CLI behavior, and UI models.
- [ ] WPF screenshot or render artifact for desktop UI changes.
- [ ] Redaction/privacy tests when prompts, transcripts, OCR text, URLs, tokens, settings, or provider payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Tranche note with changed files, proof paths, what was not validated, and remaining risk.

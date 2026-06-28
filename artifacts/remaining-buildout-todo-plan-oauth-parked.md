# GoatShot Remaining Buildout TODO Plan - OAuth Parked

Date: 2026-06-15

Purpose: continue building GoatShot without blocking on live OAuth consent screens. OAuth authorization-code setup, provider-specific consent polish, live refresh-token proof, and live cloud-account validation stay parked until an explicit OAuth tranche is scheduled. The remaining work should prioritize locally provable product value.

No git workflow is required for this project right now.

Active execution queue: `artifacts/active-non-oauth-buildout-todos.md`.

## Ground Rules

- [ ] Keep the app native WPF/.NET. Do not introduce a web stack.
- [ ] Do not rework OAuth unless a non-OAuth tranche exposes a small compatibility bug.
- [ ] Keep Google Drive, OneDrive, Dropbox, and other live account consent screens in the parked backlog.
- [ ] Keep cloud-provider diagnostics honest: fake-provider and local-token proof are not live-account proof.
- [ ] Prefer deterministic tests, fake providers, safe synthetic media, WPF screenshots, CLI smoke output, diagnostics bundles, and portable packaging as proof.
- [ ] Finish each tranche with `artifacts/tranche-<name>/notes.md`.
- [ ] Update `artifacts/current-implementation-todos-oauth-parked.md` when a tranche is completed.

## Standard Done Checklist For Every Tranche

- [ ] Focused service/model/CLI tests for changed behavior.
- [ ] WPF screenshot or render artifact for desktop UI changes.
- [ ] Redaction/privacy assertions when payloads include prompts, transcript text, OCR text, URLs, tokens, or settings.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Tranche note with what changed, proof paths, and remaining risk.

## DONE 1 - AI, Video Intelligence, And Documentation Review Loop

Goal: turn the existing AI/transcript/video/document pieces into an explicit local review workflow.

Primary code surface:

- `src/GoatShot.App/Services/AiActionHistoryService.cs`
- `src/GoatShot.App/Services/TranscriptionService.cs`
- `src/GoatShot.App/Services/VideoIntelligenceService.cs`
- `src/GoatShot.App/Services/BugReportService.cs`
- `src/GoatShot.App/MainWindow.xaml`
- `src/GoatShot.App/MainWindow.xaml.cs`
- `src/GoatShot.Cli/Program.cs`
- `src/GoatShot.Tests/AiDocumentationWorkflowTests.cs`

Implementation TODOs:

- [x] Replace the desktop AI history message-box view with an AI history/review window.
- [x] Add desktop accept/reject controls for pending AI history entries.
- [x] Add an iterate/reuse prompt action that copies the previous prompt and marks the source entry for iteration.
- [x] Add a prompt-history picker for image edit, screenshot analysis, bug report draft, and video intelligence workflows.
- [x] Add retry recovery for failed AI actions with an alternate model/profile, while recording the retry as a linked AI history entry.
- [x] Add a local documentation packet service that writes a manifest linking source media, transcript text, SRT, video summary, bug report exports, and AI review state.
- [x] Generate richer recording bug reports when transcript/SRT/keyframe/environment metadata is available.
- [x] Keep long-recording transcription local/Whisper-first; only use Gemini STT when the user explicitly requests provider transcription for short audio.
- [x] Add CLI command(s) for documentation packet generation and AI retry/review status inspection if the desktop workflow creates equivalent capabilities.

Proof TODOs:

- [x] Local fixture AI/document workflow tests for review-state, prompt reuse, document export, manifest, and redaction behavior.
- [x] Documentation packet manifest tests.
- [x] Bug report enrichment tests with transcript, keyframes, and redacted environment context.
- [x] CLI smoke artifacts under `artifacts/tranche-ai-document-workflow/`.
- [x] Desktop screenshot of the AI history/review window.
- [x] Tranche note at `artifacts/tranche-ai-document-workflow/notes.md`.

## DONE 2 - Upload Session Reliability Without Live OAuth

Goal: prove larger upload behavior locally without touching live consent screens.

Primary code surface:

- `src/GoatShot.App/Services/UploadQueueService.cs`
- `src/GoatShot.App/Services/UploadQueueWorkerService.cs`
- `src/GoatShot.App/Services/UploadQueueContracts.cs`
- OneDrive/Dropbox/Google Drive adapter code paths that already exist
- `src/GoatShot.Tests/UploadQueueServiceTests.cs`

Implementation TODOs:

- [x] Inspect Google Drive, Dropbox, and OneDrive upload-session branches for fake-provider test seams.
- [x] Add local fake HTTP provider tests for resumable upload initiation, chunk upload, transient failure, resume, cancel, and final link handling.
- [x] Tighten retry/backoff/cancel status recording in upload queue history where needed.
- [x] Ensure upload-session diagnostics distinguish local fake proof from live provider proof.
- [x] Ensure diagnostics/history redaction covers upload URLs, authorization headers, session URLs, and query tokens.
- [x] Leave remote delete disabled unless a provider has a safe API path, an audit record, and fake-provider proof.

Proof TODOs:

- [x] Upload-session focused tests.
- [x] Diagnostics redaction tests for session URLs and auth values.
- [x] CLI queue smoke: list, process, retry, cancel, history.
- [x] Tranche note update at `artifacts/tranche-upload-queue-reliability/notes.md`.

## TODO 3 - Recording Field Proof And Profile Presets

Goal: improve confidence and operator feedback for real recording conditions without requiring private desktop captures in artifacts.

Primary code surface:

- `src/GoatShot.App/Services/RecordingService.cs`
- `src/GoatShot.App/Services/RecordingConfidenceService.cs`
- `src/GoatShot.App/Services/RecordingEnginePlanner.cs`
- `src/GoatShot.App/Services/RecordingPanelPresenter.cs`
- `src/GoatShot.App/Models/RecordingSettings.cs`
- `src/GoatShot.Tests/RecordingConfidenceServiceTests.cs`
- `src/GoatShot.Tests/RecordingPanelPresenterTests.cs`

Implementation TODOs:

- [x] Add safe multi-monitor/cross-monitor proof helpers that can use synthetic/fixed regions and avoid retaining private desktop captures by default.
- [x] Add explicit microphone permission-denied, system-audio unavailable, camera permission-denied, and device-disconnected states in the recording panel model.
- [x] Add recovery guidance for device reconnect, Windows privacy permissions, and endpoint refresh.
- [ ] Add deeper audio timestamp/duration logging for microphone/system-audio sync proof.
- [x] Add recording profile presets: small share, 1080p60, and 4K60.
- [ ] Add HEVC opt-in encode path only when Media Foundation reports support and failure messaging is clear. Current state is diagnostics/probe-only.

Proof TODOs:

- [x] Focused recording confidence/presenter/planner tests.
- [x] Safe fixed/all-monitor plan-only recording proof artifact with no private desktop capture retention by default.
- [x] Device diagnostics artifact.
- [ ] Optional `ffprobe` metadata artifact when available.
- [ ] WPF screenshot of updated recording confidence states.
- [x] Tranche note update at `artifacts/tranche-recording-confidence/notes.md`.

## DONE 4 - Release Proof And Managed/Admin Posture

Goal: make the project handoff-ready without depending on live cloud accounts.

Primary code surface:

- `src/GoatShot.App/Services/DiagnosticsService.cs`
- Provider diagnostics and settings services
- `src/GoatShot.Cli/Program.cs`
- `scripts/package-release.ps1`
- `packaging/`
- `README.md`

Implementation TODOs:

- [x] Build a release proof bundle command or script wrapper that gathers build/test/package logs, diagnostics redaction proof, selected screenshots, and tranche notes.
- [x] Add optional policy keys for disabling AI, disabling uploads, restricting providers, and disabling external webhooks/scripts.
- [x] Add diagnostics that report policy source and effective policy state.
- [x] Add tests for policy defaults, policy override precedence, blocked actions, and release-proof redaction.
- [x] Document managed Windows deployment behavior in README and a release handoff note.
- [x] Keep portable zip as the default release artifact and state the installer/clean-machine proof boundary.

Proof TODOs:

- [x] Policy diagnostics tests.
- [x] Release proof bundle artifact under `artifacts/tranche-release-proof-admin/`.
- [x] Portable package output.
- [ ] Optional installer output when tooling exists.
- [x] Tranche note at `artifacts/tranche-release-proof-admin/notes.md`.

## DONE 5 - Share Provider Adapter Cleanup

Goal: finish non-OAuth provider plumbing polish without touching live consent screens.

Primary code surface:

- `src/GoatShot.App/Services/ShareService.cs`
- `src/GoatShot.App/Services/ShareProviders/`
- `src/GoatShot.App/Services/ProviderDiagnosticsService.cs`
- `src/GoatShot.Tests/`

Implementation TODOs:

- [x] Inventory remaining executable share branches still living only in `ShareService`.
- [x] Extract only the remaining non-OAuth branches where an adapter reduces duplication or improves diagnostics.
- [x] Keep `ShareService` as the stable facade for routing, history, queueing, confirmations, and compatibility.
- [x] Preserve DPAPI-backed secrets, redacted share history, provider diagnostics, before-upload confirmation, and after-upload result behavior.
- [x] Leave OAuth-backed live account providers in their current token/diagnostic posture.

Proof TODOs:

- [x] Focused provider adapter tests.
- [x] Fake HTTP/process/fake-surface proof for locally executable adapters.
- [x] Provider diagnostics smoke showing implemented, configured, missing, policy-blocked, and parked-live-account states.
- [x] Standard Release build/test, CLI help, CLI diagnostics, and package lane.
- [x] Tranche note under `artifacts/tranche-provider-adapter-cleanup/notes.md`.

## DONE 6 - V1 Evidence Sweep And Manual Proof Backlog Setup

Goal: separate what requires human/device/provider access from what can be built locally.

Status note: local evidence artifacts are complete. The manual validation TODOs below remain intentionally unchecked until a human/device/provider proof lane runs.

Local evidence TODOs:

- [x] Refresh README/spec current-truth sections against implemented code and artifacts.
- [x] Refresh Product Design/WPF screenshot-backed audit notes only for flows changed since the last audit.
- [x] Create a manual validation checklist artifact for keyboard traversal, screen reader pass, high contrast/text scaling, multi-monitor hardware proof, long recording stability, clean-machine installer proof, and live provider account proof.
- [x] Create `artifacts/v1-readiness-summary.md` separating implemented, locally proven, manually unverified, OAuth parked, and later-scope work.

Manual validation TODOs:

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

## TODO 7 - Later Modules After V1 Buildout

Goal: keep post-V1 expansion visible without letting it interrupt the core desktop app.

Later module TODOs:

- [ ] Browser extension for DOM/page capture and optional consented bug-report telemetry.
- [ ] Android device capture through ADB/screencap.
- [ ] Virtual printer capture.
- [x] Reviewed advanced video cut-plan export for text-based, silence, and filler-word plans (`video apply-plan --accept-plan`).
- [x] Reviewed composite camera/screen layout export (`video apply-composite --accept-plan`).
- [x] Reviewed keyed webcam-background blur/removal/replacement export (`video apply-background --accept-plan`).
- [ ] Advanced video editor remainder: general AI/person-segmentation webcam background processing beyond keyed chromakey-style processing.
- [ ] Plugin SDK.
- [ ] Optional hosted/self-hosted companion portal.
- [ ] Team/admin mode as a separate post-V1 module.

## Recommended Next Implementation Order

1. TODO 3 follow-up only where it can be locally improved; keep live multi-monitor/long-recording proof in the manual lane.
2. TODO 7 later modules after the V1 local handoff is honest.

The next code tranche should start with TODO 3 recording field-proof polish because V1 evidence/readiness packaging is now locally proven and OAuth consent screens remain parked.

# GoatShot Continued V1 Buildout TODOs - OAuth Parked

Date: 2026-06-15

Purpose: keep building the remaining GoatShot V1 surface while leaving live OAuth consent screens, refresh-token recovery against real accounts, and cloud-provider account proof parked. This plan starts from the current native WPF/.NET app and the existing tranche artifacts; it does not require a Git workflow.

## Current Execution Rules

- [ ] Keep GoatShot native WPF/.NET; do not introduce a web stack.
- [ ] Leave OAuth plumbing where it is unless a non-OAuth task exposes a narrow compatibility bug.
- [ ] Do not claim live Google Drive, OneDrive, Dropbox, or other cloud-account readiness without a later live-consent tranche.
- [ ] Use local proof first: deterministic tests, fake providers, safe synthetic/fixed media, WPF render screenshots, diagnostics bundles, CLI smoke output, and portable packaging.
- [ ] End every tranche with `artifacts/tranche-<name>/notes.md` and update this plan only for the tranche actually completed.

## Completed Tranche A - Reconcile Recording Proof Already Built

Goal: close the partially completed recording-confidence tranche cleanly before moving on.

Implementation TODOs:

- [x] Update `artifacts/tranche-recording-confidence/notes.md` so it reflects the latest implemented recording profile catalog, 60 fps normalization, explicit device blocked/recovery states, and multi-action confidence presenter.
- [x] Update the older OAuth-parked TODO ledgers to mark the completed recording items as done and keep remaining items visible.
- [x] Keep HEVC marked as diagnostics-only unless an opt-in encode path is actually implemented and tested.

Proof TODOs:

- [x] Confirm `record-profiles.json` includes `Small Share`, `1080p60`, and `4K60`.
- [x] Run `scripts/smoke-recording.ps1` in `-PlanOnly` mode for fixed/all-monitor targets so no private desktop footage is retained by default.
- [x] Save plan-only recording proof under `artifacts/tranche-recording-confidence/safe-recording-proof/`.
- [x] Refresh package proof with `.\scripts\package-release.ps1 -SkipInstaller` if the current tranche note does not point at a fresh package log.

## Tranche B - Finish Recording Field Proof Gaps

Goal: make real-world recording failures and device sync easier to diagnose without requiring private artifacts.

Implementation TODOs:

- [ ] Add deeper microphone/system-audio timestamp and duration-delta logging where the existing smoke script or recorder metadata is too coarse.
- [ ] Add clear WPF rendering evidence for recording confidence states, including blocked device state, warning state, and ready state.
- [ ] Add HEVC opt-in encode support only if Media Foundation probing reports support and the fallback/error path is clear. Otherwise, document HEVC as detected but not selected.
- [ ] Preserve FFmpeg as a secondary fallback and keep production/fallback engine selection visible in diagnostics.

Proof TODOs:

- [ ] Focused recording diagnostics/planner/presenter tests.
- [ ] Device diagnostics JSON artifact.
- [ ] Optional `ffprobe` metadata artifact when available locally.
- [ ] WPF screenshot or accessibility note for updated recording confidence states.
- [ ] Full Release build/test, CLI help, CLI diagnostics, and package lane.

## Completed Tranche C - Release Proof Bundle

Goal: produce a single local handoff bundle proving the build without live cloud accounts.

Implementation TODOs:

- [x] Add `scripts/create-release-proof-bundle.ps1` or a CLI equivalent that gathers build/test/package logs, diagnostics output, selected screenshots, tranche notes, README/spec snapshots, and package metadata.
- [x] Exclude capture files, thumbnails, OCR text dumps, AI payloads, DPAPI secret files, raw tokens, upload session URLs, and private desktop recordings.
- [x] Include a machine-readable manifest with tool versions, command exit codes, artifact paths, and known unverified lanes.
- [x] Add tests for manifest generation, redaction, and artifact inclusion/exclusion.

Proof TODOs:

- [x] Release proof bundle under `artifacts/tranche-release-proof-admin/`.
- [x] Portable package output under `artifacts/dist/`.
- [x] CLI diagnostics bundle proof.
- [x] Full Release build/test, CLI help, CLI diagnostics, and package lane.

## Completed Tranche D - Managed/Admin Policy Posture

Goal: make GoatShot usable in managed Windows environments without live cloud-provider proof.

Implementation TODOs:

- [x] Add optional policy/settings keys for disabling AI features, disabling uploads, restricting provider destinations, disabling custom scripts, and disabling custom webhooks.
- [x] Define deny-wins policy precedence between app settings and an optional external managed policy file; imported workflow profiles remain constrained by the effective policy.
- [x] Surface effective policy state in Settings, provider diagnostics, workflow dry-run output, diagnostic bundles, and `diagnostics print`.
- [x] Block restricted actions with clear operator-facing messages and persisted diagnostic notes.
- [x] Document managed Windows deployment behavior in README and `artifacts/tranche-release-proof-admin/notes.md`.

Proof TODOs:

- [x] Policy default and override precedence tests.
- [x] Provider/workflow restriction tests.
- [x] Diagnostics/policy status tests plus release-proof redaction tests.
- [x] WPF screenshot or render artifact for Settings/effective policy state.
- [x] Full Release build/test, CLI help, CLI diagnostics, and package lane.

## Completed Tranche E - Share Provider Adapter Cleanup

Goal: finish non-OAuth provider plumbing polish without touching live consent screens.

Implementation TODOs:

- [x] Inventory any remaining executable share branches still living only in `ShareService`.
- [x] Move remaining non-OAuth execution branches into concrete `IShareProvider` adapters only where that reduces duplication or improves diagnostics.
- [x] Keep `ShareService` as the stable facade for routing, history, queueing, and compatibility.
- [x] Preserve DPAPI-backed secrets, redacted share history, provider diagnostics, before-upload confirmation, and after-upload result behavior.
- [x] Leave OAuth-backed live account providers in their current token/diagnostic posture.

Proof TODOs:

- [x] Focused provider adapter tests.
- [x] Fake HTTP/process/fake-surface proof for adapters that can execute locally.
- [x] Provider diagnostics smoke showing implemented, configured, missing, and parked-live-account states.
- [x] Full Release build/test, CLI help, CLI diagnostics, and package lane.

## Completed Tranche F - V1 Evidence Sweep

Goal: make the local V1 handoff honest, readable, and easy to resume.

Implementation TODOs:

- [x] Refresh README current-truth sections against implemented code and artifacts.
- [x] Refresh Product Design/WPF screenshot-backed audit notes only for flows changed since the last audit.
- [x] Create a manual validation checklist artifact for keyboard traversal, screen reader pass, high contrast/text scaling, multi-monitor hardware proof, long recording stability, clean-machine installer proof, and live provider account proof.
- [x] Create a concise `artifacts/v1-readiness-summary.md` that separates implemented, locally proven, manually unverified, OAuth parked, and later-scope work.

Proof TODOs:

- [x] README/spec consistency scan.
- [x] WPF render screenshots for changed surfaces.
- [x] Full Release build/test, CLI help, CLI diagnostics, package lane, and release proof bundle.

## Parked Manual/OAuth Lane

Do not start this lane until explicitly scheduled.

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence and expiry recovery for live cloud accounts.
- [ ] Provider-specific consent copy, scopes, account diagnostics, and user-facing setup polish.
- [ ] Live upload proof against real provider accounts.

## Later Post-V1 Modules

These stay visible but should not interrupt the V1 local proof path.

- [ ] Browser extension for DOM/page capture and consented bug-report telemetry.
- [ ] Android device capture through ADB/screencap.
- [ ] Virtual printer capture.
- [x] Reviewed advanced video cut-plan export for text-based, silence, and filler-word plans (`video apply-plan --accept-plan`).
- [x] Reviewed composite screen/camera layout export (`video apply-composite --accept-plan`).
- [x] Reviewed keyed webcam-background blur/removal/replacement export (`video apply-background --accept-plan`).
- [ ] Advanced video editor remainder: general AI/person-segmentation webcam background processing beyond keyed chromakey-style processing.
- [ ] Plugin SDK.
- [ ] Optional hosted/self-hosted companion portal.
- [ ] Team/admin mode as a separate post-V1 module.

## Recommended Next Move

Next, do Tranche B / Recording Field Proof Polish. Treat live multi-monitor/long-recording/hardware proof items as manual/live-device work unless a deterministic local improvement is obvious. OAuth consent screens stay parked until a dedicated consent/account tranche is explicitly scheduled.

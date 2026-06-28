# GoatShot Continue-Remaining Implementation TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue from the current GoatShot state without reopening live OAuth consent, refresh-token, or live cloud-account proof. The app is already broadly built and locally proven; the remaining work is now a proof-and-polish queue: finish local manual-validation automation, run safe desktop/accessibility evidence, patch concrete findings, collect optional hardware/device/browser evidence only when safe content exists, then refresh the release bundle.

No Git workflow is required right now.

## Current Inputs

- Active remaining plan: `artifacts/current-remaining-non-oauth-todos-2026-06-15.md`.
- Active implementation ledger: `artifacts/active-non-oauth-buildout-todos.md`.
- Readiness summary: `artifacts/v1-readiness-summary.md`.
- Current manual folder: `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- Current manual summary: `artifacts/manual-validation/2026-06-15-current-required-proof/summary.md`.
- Completed local helper file for command-backed baseline proof: `src/GoatShot.App/Services/ManualValidationBaselineService.cs`.

## Non-Goals For This Pass

- [ ] Do not reopen Google Drive, Dropbox, OneDrive, or other live OAuth consent screens.
- [ ] Do not claim live-account upload/delete proof, refresh-token reliability, or OAuth scope/copy completion.
- [ ] Do not introduce a web stack for the desktop product.
- [ ] Do not start hosted portal accounts/sync, hosted plugin marketplace behavior, automatic plugin install/trust/enable/allowlist/execute updates, browser-store publication, true OS printer-driver installation, or production Android streaming unless explicitly rescheduled.
- [ ] Do not treat generated or synthetic proof as human accessibility certification, clean-machine validation, or hardware stability proof.

## Completed Tranche A - Finish Baseline Manual-Validation Automation

Goal: turn the required `Baseline Setup` manual lane from pending into command-backed local evidence, so the remaining open manual lanes are truly human/device proof lanes.

- [x] Finish and compile `ManualValidationBaselineService`.
- [x] Ensure JSON-named outputs such as `recording-readiness.json`, `recording-devices.json`, `capture-engine-wgc.json`, and `providers.json` contain raw JSON stdout, while command metadata stays in `diagnostics/baseline-command-results.json`.
- [x] Wire CLI aliases: `manual-validation baseline`, `manual-validation complete-baseline`, and `manual-validation baseline-setup`.
- [x] Support `--folder`, `--run-commands`, `--repo-root`, `--cli-path`, `--timeout-seconds`, and `--json`.
- [x] Add focused MSTest coverage for successful command-backed baseline completion, failed-command reporting, missing-evidence blocking, and summary integration.
- [x] Run the baseline helper against `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [x] Regenerate `manual-validation summarize` and `manual-validation proof-plan`.
- [x] Write `artifacts/tranche-manual-baseline-proof/notes.md` with proof commands, results, and remaining boundaries.

Proof:

- [x] Focused `ManualValidationBaseline` tests.
- [x] `dotnet build .\GoatShot.slnx -c Release`.
- [x] `dotnet test .\GoatShot.slnx -c Release`.
- [x] CLI `manual-validation baseline --folder artifacts\manual-validation\2026-06-15-current-required-proof --run-commands --json`.
- [x] Summary shows `Baseline Setup` as passed with concrete command details; OAuth remains parked.

## Tranche B - Required Desktop Accessibility And Interaction Proof

Goal: complete the local required human-observation lanes that are still `Pending`, then fix concrete WPF issues found during the pass.

- [x] Finish and test the command-backed `manual-validation desktop-proof` helper.
- [x] Run the helper against `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [x] Save app-owned screenshots, WPF focus/name audits, environment evidence, command logs, and updated lane files under `desktop-proof/`.
- [x] Regenerate `manual-validation summarize` and `manual-validation proof-plan`; current summary is complete/redaction-clean while six required lanes remain `Blocked`.
- [ ] Keyboard traversal: Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, share/provider setup.
- [ ] Screen reader: Narrator and/or NVDA notes for the same core flows.
- [ ] Windows text scaling: verify at common scaling values with notes about clipped text, overlap, or missing scroll behavior.
- [ ] High contrast: verify readable colors, visible focus, and meaningful state indicators.
- [ ] Live region drag: perform a safe-content region selection and record status/focus behavior.
- [ ] Clean-machine portable/GUI lane: either run on a clean Windows profile/VM or keep explicitly pending if no safe clean session exists.
- [ ] Patch only confirmed issues: missing accessible names, broken tab order, invisible focus, clipped text, contrast failures, target-size problems, confusing recovery copy, stale diagnostics.
- [ ] Use Product Design/WPF screenshot-backed audit for material UI fixes; use Figma only if screenshots show a redesign question worth exploring.

Proof:

- [x] Updated command-backed blocked notes in `artifacts/manual-validation/2026-06-15-current-required-proof/*.md`.
- [x] Safe app-owned screenshots under `artifacts/manual-validation/2026-06-15-current-required-proof/desktop-proof/screenshots/`.
- [x] Focused `ManualValidationDesktopProof` tests.
- [x] Regenerated summary/proof-plan.
- [x] `artifacts/tranche-manual-desktop-accessibility-proof/notes.md`.

## Tranche C - Recording Hardware And Stability Proof

Goal: collect the remaining non-OAuth hardware proof only when the desktop content, devices, and duration are safe.

- [ ] Multi-monitor capture proof.
- [ ] Multi-monitor recording proof.
- [ ] Long recording stability proof.
- [ ] Microphone/system-audio sync metadata proof.
- [ ] Webcam overlay granted/denied/recovery states.
- [ ] Fallback behavior when WGC, camera, audio, HEVC, or FFmpeg paths are unavailable.
- [ ] Patch deterministic failures only after observed evidence.

Proof:

- [ ] Recording diagnostics and metadata.
- [ ] Redacted/safe recording artifacts or summaries.
- [ ] Optional `ffprobe` output where available.
- [ ] Updated manual summary and `artifacts/tranche-recording-hardware-proof/notes.md`.

## Tranche D - Safe Android Live-Device Proof

Goal: prove the existing bounded Android screenshot/video/preview paths against a real device only after safe phone content is staged.

- [ ] Prepare a phone screen with no private notifications, accounts, chats, contacts, photos, tokens, or customer data.
- [ ] Run `diagnostics android`.
- [ ] Run screenshot import.
- [ ] Run bounded screenrecord import.
- [ ] Run guarded `capture android-preview --execute` with selected device, safe-content confirmation, frame/byte/duration/timeout caps.
- [ ] Review media before preserving evidence.
- [ ] Keep production Android streaming deferred.

Proof:

- [ ] Safe device diagnostics.
- [ ] Reviewed screenshot/video/preview artifacts.
- [ ] Contact sheet if generated.
- [ ] Updated manual summary and `artifacts/tranche-android-live-device-proof/notes.md`.

## Tranche E - Optional Chrome/Firefox Browser Proof Decision

Goal: decide whether the completed Edge proof and current local package/readiness artifacts are enough for V1, or whether Chrome/Firefox screenshots are needed for compatibility claims.

- [ ] Reconfirm current public/browser-extension claims.
- [ ] If no Chrome/Firefox live claim is needed, leave the lane optional/manual and document the decision.
- [ ] If proof is needed, run the existing live-fixture helper, native-host status proof, package export, import validation, and screenshots for each browser.
- [ ] Keep browser-store account submission, review/signing, availability, permanent/store-managed automatic installation, and actual enterprise deployment proof later-scope.

Proof:

- [ ] Decision note or browser-specific proof folder.
- [ ] `browser-extension proof validate --folder <proof-folder>` output if reopened.
- [ ] Updated manual summary only if the lane is reopened.

## Tranche F - Final Evidence And Handoff Refresh

Goal: refresh local proof after any tranche above changes source, docs, or evidence claims.

- [ ] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/current-remaining-non-oauth-todos-2026-06-15.md`, `artifacts/current-implementation-todos-oauth-parked.md`, and `artifacts/active-non-oauth-buildout-todos.md` from observed evidence only.
- [ ] Run `dotnet build .\GoatShot.slnx -c Release`.
- [ ] Run `dotnet test .\GoatShot.slnx -c Release`.
- [ ] Run CLI `--help`.
- [ ] Run CLI `diagnostics print`.
- [ ] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [ ] Run `scripts\create-release-proof-bundle.ps1` when README/spec/readiness or proof claims changed materially.
- [ ] Write `artifacts/tranche-final-evidence-refresh-after-manual-proof/notes.md`.

## Parked Until Explicitly Scheduled

- [ ] Live OAuth consent screens and live provider account proof.
- [ ] Refresh-token expiry/recovery proof against live accounts.
- [ ] Browser-store account submission, review/signing, publication, permanent/store-managed automatic extension installation, and actual enterprise policy deployment/force-install proof.
- [ ] Hosted/self-hosted companion portal accounts, sync, media hosting, and remote admin.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [x] Governed background plugin update check/stage-only runs.
- [ ] Automatic plugin install/trust/enable/allowlist/execute updates and hosted marketplace behavior.
- [ ] True OS virtual-printer driver installation, signed driver packaging, and clean-machine printer proof.
- [ ] Production Android streaming beyond bounded ADB screenshot/video/preview helpers.

## Recommended Next Command Sequence

```powershell
dotnet test .\src\GoatShot.Tests\GoatShot.Tests.csproj -c Release --filter "FullyQualifiedName~ManualValidationBaseline"
dotnet build .\GoatShot.slnx -c Release
dotnet test .\GoatShot.slnx -c Release
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation baseline --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --run-commands --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation summarize --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation proof-plan --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --output .\artifacts\manual-validation\2026-06-15-current-required-proof --json
```

Expected after Tranche A: baseline setup is no longer pending, while keyboard, screen-reader, text-scaling, high-contrast, live region drag, clean-machine GUI proof, hardware/device proof, optional Chrome/Firefox proof, and OAuth/live-provider proof remain separately classified.

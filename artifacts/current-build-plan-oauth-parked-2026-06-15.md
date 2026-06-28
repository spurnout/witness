# GoatShot Current Build Plan - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing and proving what is still left in GoatShot without reopening live OAuth consent, refresh-token, or cloud-account proof. GoatShot stays a native WPF/.NET desktop app. No Git workflow is required right now.

Use this as the active todo plan. The broader ledgers remain useful background, but this file is the current continuation queue.

## Current Truth

- [x] The app is broadly implemented as a native WPF/.NET desktop app with CLI, MSTest coverage, diagnostics, manual-validation tooling, Product Design/WPF audit artifacts, and portable packaging.
- [x] OAuth/live-provider account work is parked. Do not run or claim Google Drive, Dropbox, OneDrive, refresh-token, live upload, live delete, or provider consent proof in this pass.
- [x] Baseline manual validation is command-backed and passed under `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [x] `manual-validation desktop-proof` exists and has produced app-owned screenshots, WPF focus/name audits, environment evidence, and blocked notes for the required human desktop lanes.
- [x] `manual-validation record-lane` exists for operator-observed lane updates without hand-editing Markdown.
- [x] `manual-validation operator-pack` exists and has generated a required desktop operator checklist, per-lane notes, command reference, and manifest under the current manual-validation folder without marking human lanes passed.
- [x] A safe proof scene exists through `GoatShot.exe --proof-scene`, `--render-proof-scene-output`, and `--audit-wpf-surface proof-scene`.
- [x] Release evidence after the safe proof scene is refreshed through `artifacts/tranche-safe-proof-scene/` and `artifacts/tranche-release-proof-admin/` with 448 tests passed and 0 policy exclusions.
- [x] `manual-validation hardware-proof` is implemented and locally proven under `artifacts/tranche-hardware-readiness-proof/`; it records readiness/blocker evidence for hardware-gated lanes without claiming live hardware proof.
- [x] `GoatShot.exe --record-proof-scene-output <mp4>` is implemented and locally proven under `artifacts/tranche-proof-scene-recording-smoke/`; it records only the app-owned proof-scene window with audio/webcam disabled and does not claim live hardware proof.
- [x] Portable ZIP verification plus packaged app-owned WPF render proof is refreshed under `artifacts/tranche-clean-machine-packaging-proof/`; this does not claim true clean VM or installer proof.
- [x] `manual-validation findings` is implemented and locally proven under `artifacts/tranche-manual-validation-findings/`; it sorts current proof blockers and claim boundaries without claiming proof completion.
- [x] Optional Chrome/Firefox browser-extension live fixture proof is closed as `NotApplicable` for current V1 claims under `artifacts/tranche-browser-optional-lane-closure/`; reopen it before advertising Chrome/Firefox live proof.
- [ ] Required local-V1 human lanes remain open: keyboard traversal, screen reader, text scaling, high contrast, live region drag, and clean-machine portable/GUI proof.
- [ ] Hardware-gated proof remains open: multi-monitor capture, multi-monitor recording, long recording stability, and live Android safe-device proof.

## Ground Rules

- [ ] Keep GoatShot native WPF/.NET; do not introduce a web stack.
- [ ] Keep OAuth where it is unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not turn command output, fake providers, synthetic media, screenshots, or WPF audits into claims of human accessibility compliance, live account proof, clean-machine validation, or hardware stability.
- [ ] Use Product Design/WPF screenshot-backed audit for material desktop UI fixes. Use Figma only if screenshots reveal a redesign problem that benefits from visual exploration.
- [ ] End each implementation tranche with `artifacts/tranche-<name>/notes.md`, updated summaries/proof artifacts, and an honest remaining-risk section.

## Completed Tranche 0 - Finish Hardware Readiness Helper

Goal: finish the in-progress `manual-validation hardware-proof` helper so hardware-gated lanes have command-backed blocker evidence without pretending live hardware proof was performed.

- [x] Compile and fix `src/GoatShot.App/Services/ManualValidationHardwareProofService.cs`.
- [x] Compile and fix `src/GoatShot.Tests/ManualValidationHardwareProofServiceTests.cs`.
- [x] Verify CLI dispatch and help for `manual-validation hardware-proof`, `hardware-readiness`, `device-proof`, and `device-readiness`.
- [x] Ensure `--run-commands` writes:
  - [x] `hardware-proof/recording-preflight.json`
  - [x] `hardware-proof/recording-devices.json`
  - [x] `hardware-proof/diagnostics-recording.json`
  - [x] `hardware-proof/diagnostics-devices.json`
  - [x] `hardware-proof/capture-engine-wgc.json`
  - [x] `hardware-proof/android-diagnostics.json`
  - [x] `hardware-proof/environment.md`
  - [x] `hardware-proof/environment.json`
  - [x] `hardware-proof/hardware-proof-command-results.json`
  - [x] `hardware-proof/logs/*.txt`
- [x] Make failed readiness commands blocker evidence, not helper failure, when at least one command result was collected.
- [x] Mark lanes `07`, `08`, `09`, and `13` as `Blocked` with precise claim boundaries, never `Passed`.
- [x] Regenerate `manual-validation summarize` and `manual-validation proof-plan`.
- [x] Write `artifacts/tranche-hardware-readiness-proof/notes.md`.

Proof:

- [x] Focused `ManualValidationHardwareProofServiceTests`.
- [x] `dotnet build .\GoatShot.slnx -c Release`.
- [x] CLI `manual-validation hardware-proof --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --run-commands --json`.
- [x] Updated manual summary/proof plan with redaction status clean.

## Completed Tranche 0.5 - Required Desktop Operator Pack Helper

Goal: make the six required human desktop lanes easier to run and record without pretending automation completes them.

- [x] Add `manual-validation operator-pack --folder <evidence-folder> [--output <folder>] [--json]`.
- [x] Generate `required-desktop-operator-pack/required-desktop-operator-checklist.md`.
- [x] Generate per-lane notes for keyboard traversal, screen reader, text scaling, high contrast, live region drag, and clean-machine portable/GUI proof.
- [x] Generate `required-desktop-operator-pack/record-lane-command-reference.ps1` with print-only command templates for pass/fail/blocked outcomes.
- [x] Generate `required-desktop-operator-pack/operator-pack-manifest.json`.
- [x] Preserve the boundary that this helper prepares operator evidence and commands only; it does not perform human accessibility, live drag, or clean-machine proof.
- [x] Regenerate `manual-validation summarize` and `manual-validation proof-plan`.
- [x] Write `artifacts/tranche-required-desktop-operator-pack/notes.md`.

Proof:

- [x] Focused `ManualValidationOperatorPackServiceTests` plus lane-update tests.
- [x] `dotnet build .\GoatShot.slnx -c Release`.
- [x] `dotnet test .\GoatShot.slnx -c Release`.
- [x] CLI `--help` and `diagnostics print`.
- [x] CLI `manual-validation operator-pack --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --json`.
- [x] Updated manual summary/proof plan remains redaction-clean with 6 required human lanes open, 4 hardware-gated lanes open, 0 optional compatibility lanes open, and 1 OAuth/live-provider lane parked.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`.
- [x] `.\scripts\create-release-proof-bundle.ps1 -Version 0.1.0`.

## Completed Tranche 0.75 - App-Owned Proof-Scene Recording Smoke

Goal: add a private-safe recording smoke that exercises the production WGC/Media Foundation path without retaining desktop, microphone, system-audio, webcam, provider, or phone content.

- [x] Add `GoatShot.exe --record-proof-scene-output <mp4> [--record-proof-scene-duration <seconds>]`.
- [x] Add environment fallbacks for unattended proof runs: `GOATSHOT_RECORD_PROOF_SCENE`, `GOATSHOT_RECORD_PROOF_SCENE_OUTPUT`, and `GOATSHOT_RECORD_PROOF_SCENE_DURATION`.
- [x] Record explicit proof-scene WPF window bounds instead of the active foreground window.
- [x] Write a `.proof.json` sidecar with safe-content label, requested duration, audio/webcam flags, target, bounds, and recording message.
- [x] Disable microphone, system audio, webcam, countdown, and keystroke overlay for this proof lane.
- [x] Fix MP4 frame pacing so slower WGC/frame-composition delivery does not shorten constant-FPS output duration.
- [x] Preserve the boundary that this proves a bounded app-owned MP4 smoke only; it does not prove live multi-monitor recording, long-run stability, audio sync, webcam permission states, Android device media, clean-machine GUI, or OAuth/live-provider proof.

Proof:

- [x] Focused startup/recording pacing tests passed.
- [x] Proof MP4 and sidecar saved under `artifacts/tranche-proof-scene-recording-smoke/`.
- [x] `diagnostics recording-media` reported H.264, 1180x760, 8s, 80 frames, 0 audio streams.
- [x] `artifacts/tranche-proof-scene-recording-smoke/notes.md`.

## Completed Tranche 0.9 - Clean Packaging Proof Refresh

Goal: refresh portable ZIP and packaged first-launch evidence after the latest source/docs/proof changes without claiming true clean-machine or installer proof.

- [x] Run `scripts\verify-portable-package.ps1` against `artifacts\dist\GoatShot-0.1.0-win-x64-portable.zip`.
- [x] Verify required app, CLI, README, browser-extension, and safe-fixture package entries.
- [x] Verify forbidden runtime/proof entries are absent from the portable ZIP.
- [x] Run packaged `GoatShot.Cli.exe --help` and `diagnostics print` with isolated roots.
- [x] Run packaged `GoatShot.exe --render-main --output <png>` with isolated roots.
- [x] Run packaged `GoatShot.Cli.exe paths` with the same isolated roots.
- [x] Preserve the boundary that this is portable ZIP, packaged CLI, isolated-root path, and app-owned WPF render proof only.

Proof:

- [x] Portable verifier JSON/Markdown under `artifacts/tranche-clean-machine-packaging-proof/`.
- [x] Packaged CLI smoke logs under `artifacts/tranche-clean-machine-packaging-proof/`.
- [x] Packaged WPF render screenshot and process result under `artifacts/tranche-clean-machine-packaging-proof/`.
- [x] `artifacts/tranche-clean-machine-packaging-proof/notes.md`.

## Completed Tranche 0.95 - Manual Validation Findings

Goal: turn current manual evidence into a sorted release-blocker and claim-boundary list before any patch loop.

- [x] Add `manual-validation findings --folder <evidence-folder> [--output <folder>] [--json]`.
- [x] Add aliases `finding-list`, `findings-list`, `defects`, and `gaps`.
- [x] Write `manual-validation-findings.md` and `manual-validation-findings.json`.
- [x] Sort redaction risks, failed/missing required proof, blocked required proof, hardware-gated claim boundaries, optional compatibility gaps, and parked OAuth/live-provider proof by severity.
- [x] Keep the command reporting-only; it does not perform human or hardware proof.

Proof:

- [x] Focused manual-validation findings tests.
- [x] CLI findings output for `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [x] Current findings report shows 6 required blocking findings, 4 hardware-gated claim boundaries, 0 optional compatibility findings, 1 parked OAuth/live-provider lane, and 0 redaction findings.
- [x] `artifacts/tranche-manual-validation-findings/notes.md`.

## Tranche 1 - Required Human Desktop UX And Accessibility Pass

Goal: complete the six local-V1 required human-observation lanes or record accepted blockers with precise follow-up. Patch only confirmed issues.

- [ ] Keyboard traversal: Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA screen-reader pass for the same core flows.
- [ ] Windows text scaling at 125%, 150%, and 200% where usable.
- [ ] Windows high contrast pass for focus, selected, disabled, and error states.
- [ ] Live region-drag proof with safe desktop content only.
- [ ] Clean-profile or clean-VM portable GUI pass. Installer compile/install/uninstall stays separate unless tooling is available.
- [ ] Use `manual-validation record-lane` for every observed pass, fail, or blocked lane.
- [ ] Patch confirmed issues: missing accessible names, broken tab order, invisible focus, clipped text, contrast failures, too-small targets, confusing state copy, or stale recovery guidance.
- [ ] Run Product Design/WPF screenshot-backed review for any material UI change before calling the desktop lane complete.

Proof:

- [ ] Completed lane notes under `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [ ] Safe screenshots or short clips only when they expose no private desktop/account content.
- [ ] Focused tests for changed UI/services.
- [ ] Regenerated summary/proof plan.
- [ ] `artifacts/tranche-required-desktop-proof/notes.md`.

## Tranche 2 - Recording Hardware And Stability Proof

Goal: prove the recording paths that cannot be honestly validated with synthetic media alone.

- [x] Add and run private-safe app-owned proof-scene MP4 smoke; keep it separate from live hardware proof.
- [ ] Multi-monitor capture proof with safe content.
- [ ] Multi-monitor recording proof with safe content.
- [ ] Long recording stability proof with microphone/system-audio/webcam enabled only when safe.
- [ ] Verify audio sync metadata, device disconnect/recovery, webcam permission granted/denied states, and fallback messaging.
- [ ] Patch deterministic failures found during the pass.
- [ ] Keep HEVC, FFmpeg, WGC, camera, and audio fallback claims tied to observed diagnostics and media metadata.

Proof:

- [ ] Recording diagnostics and redacted metadata.
- [ ] Safe recordings or summarized media metadata.
- [ ] Optional `ffprobe` output where available.
- [ ] Updated manual summary/proof plan.
- [ ] `artifacts/tranche-recording-hardware-proof/notes.md`.

## Tranche 3 - Android Safe-Device Live Proof

Goal: collect real-device proof only after safe phone content is staged.

- [ ] Stage an Android screen with no private notifications, accounts, chats, contacts, photos, tokens, or customer data.
- [ ] Run live `diagnostics android`.
- [ ] Run screenshot import.
- [ ] Run bounded `screenrecord` pull/import.
- [ ] Run guarded `capture android-preview --execute` with selected device, safe-content confirmation, and duration/frame/byte/timeout caps.
- [ ] Review media before preserving evidence.
- [ ] Keep production Android streaming deferred.

Proof:

- [ ] Safe-device diagnostics.
- [ ] Reviewed screenshot/video/preview artifacts.
- [ ] Contact sheet if generated.
- [ ] Updated manual summary/proof plan.
- [ ] `artifacts/tranche-android-live-device-proof/notes.md`.

## Completed Tranche 4 - Optional Chrome/Firefox Browser Proof Decision

Goal: decide whether completed Edge proof plus local multi-target package/readiness/install/publication-plan artifacts are enough for V1 claims.

- [x] Reconfirm README/spec/browser-extension claims.
- [x] If no Chrome/Firefox live claim is needed, leave the lane optional/manual and document the decision.
- [ ] If compatibility proof is needed, run the existing live-fixture helper, native-host status proof, package export/import validation, and screenshots per browser.
- [ ] Keep browser-store account submission, review/signing, availability, and automatic installation later-scope.

Proof:

- [x] Decision note under `artifacts/manual-validation/2026-06-15-current-required-proof/browser-extension-live-fixture/chrome-firefox-proof-decision.md`.
- [ ] `browser-extension proof validate --folder <proof-folder>` output if reopened.
- [x] Updated manual summary/proof-plan reports 0 optional compatibility lanes open.

## Tranche 5 - Final Evidence And Handoff Refresh

Goal: make the handoff bundle match the actual source, docs, and proof state after each completed tranche.

- [ ] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/current-remaining-non-oauth-todos-2026-06-15.md`, `artifacts/current-implementation-todos-oauth-parked.md`, and `artifacts/active-non-oauth-buildout-todos.md` from observed evidence only.
- [ ] Run `dotnet build .\GoatShot.slnx -c Release`.
- [ ] Run `dotnet test .\GoatShot.slnx -c Release`.
- [ ] Run CLI `--help`.
- [ ] Run CLI `diagnostics print`.
- [ ] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [ ] Run `scripts\create-release-proof-bundle.ps1` when source, README/spec/readiness, or proof claims changed materially.
- [ ] Write a tranche-specific final refresh note, for example `artifacts/tranche-final-evidence-refresh-after-hardware-proof/notes.md`.

## Parked Or Later-Scope

- [ ] Live OAuth consent screens, refresh-token expiry/recovery, live upload/delete proof, and provider-account diagnostics polish.
- [ ] Browser-store account submission, review/signing, publication, and automatic browser extension installation.
- [ ] Hosted/self-hosted companion portal accounts, sync, media hosting, and remote admin.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted marketplace behavior.
- [ ] True OS virtual-printer driver installation, signed driver packaging, and clean-machine printer proof.
- [ ] Production Android streaming beyond bounded ADB screenshot/video/preview helpers.

## First Next Commands

```powershell
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation record-lane --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --lane keyboard --status blocked --note "Awaiting human keyboard traversal on safe desktop content." --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation summarize --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation proof-plan --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --output .\artifacts\manual-validation\2026-06-15-current-required-proof --json
```

Expected next: required human desktop lanes remain explicit until an operator records real keyboard, screen-reader, scaling, high-contrast, live-drag, and clean-machine GUI observations. Hardware-gated lanes now have readiness evidence but remain unproven until actual safe hardware/device passes are run. Chrome/Firefox live browser proof is not a current V1 blocker unless those live claims are later advertised.

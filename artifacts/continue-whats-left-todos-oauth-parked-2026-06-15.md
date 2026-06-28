# GoatShot Continue-What-Is-Left TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue building GoatShot from the current native WPF/.NET baseline while keeping OAuth/live cloud-account consent proof parked. This is the active forward plan for the remaining non-OAuth work: finish proof tooling closeout, run safe desktop/device proof, patch findings, and only then move into later modules.

No Git workflow is required right now.

## Current Truth

- [x] GoatShot remains a native WPF/.NET desktop app with CLI, diagnostics, MSTest coverage, Product Design/WPF audit artifacts, manual-validation tooling, and portable packaging.
- [x] OAuth/live account proof stays parked for Google Drive, OneDrive, Dropbox, live upload/delete, refresh-token expiry/recovery, and provider consent screens.
- [x] Baseline manual validation is passed in `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [x] Desktop-proof, hardware-proof, proof-plan, and record-lane helpers exist for command-backed setup/readiness evidence.
- [x] Browser Chrome/Firefox live proof is closed as `NotApplicable` for current V1 claims; reopen only before advertising those live compatibility claims.
- [ ] Required local-V1 human desktop lanes remain open: keyboard traversal, screen reader, text scaling, high contrast, live region drag, and clean-machine portable/GUI proof.
- [ ] Hardware-gated proof remains open: live multi-monitor capture, live multi-monitor recording, long recording stability, and live Android safe-device media proof.
- [x] A required desktop operator-pack helper exists in source/artifacts and is documented as the default handoff packet for the six required human desktop lanes.
- [x] A private-safe app-owned proof-scene MP4 smoke exists in source/artifacts and proves bounded WGC/Media Foundation recording without desktop/audio/webcam content; it does not close live hardware proof lanes.
- [x] Portable ZIP verification and packaged app-owned WPF render proof have been refreshed under `artifacts/tranche-clean-machine-packaging-proof/`; true clean Windows VM/human GUI and installer proof remain open.
- [x] Manual-validation findings generation exists in source/artifacts and converts current lane evidence into a sorted release-blocker/claim-boundary list without performing human or hardware proof.

## Ground Rules

- [ ] Do not reopen OAuth unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not claim live provider proof, refresh-token reliability, accessibility compliance, hardware stability, clean-machine installer proof, or live device proof without fresh observed evidence.
- [ ] Use Product Design/WPF screenshot-backed audit for material desktop UI fixes; use Figma only if the screenshot audit exposes a redesign problem that benefits from layout exploration.
- [ ] Every tranche ends with `artifacts/tranche-<name>/notes.md`, focused tests for changed code, regenerated manual summaries when lane evidence changes, and a release proof refresh when source/docs/proof claims changed materially.
- [ ] Keep evidence private-safe: no desktop screenshots, phone content, browser accounts, provider payloads, tokens, OCR text, transcripts, or customer data unless explicitly staged and reviewed.

## Tranche 0 - Close Required Desktop Operator Pack

Goal: turn the existing helper into the official packet for the six required human desktop lanes.

- [x] Finish docs/ledger updates for `manual-validation operator-pack`.
- [x] Ensure CLI help and invalid-usage copy mention `operator-pack` and the full lane status set, including `not-applicable` where valid.
- [x] Preserve the boundary that the pack generates notes and command references only; it does not complete human proof.
- [x] Regenerate manual-validation summary/proof-plan after the pack is generated.
- [x] Write `artifacts/tranche-required-desktop-operator-pack/notes.md`.

Proof:

- [x] Focused `ManualValidationOperatorPackServiceTests` plus lane-update tests.
- [x] `dotnet build .\GoatShot.slnx -c Release`.
- [x] `dotnet test .\GoatShot.slnx -c Release`.
- [x] CLI `--help`, `diagnostics print`, `manual-validation operator-pack --folder <current-folder> --json`.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`.
- [x] Release proof bundle refresh if docs/source/proof claims changed.

## Tranche 1 - Required Human Desktop Proof

Goal: use the operator pack to complete or honestly block the six required local-V1 desktop lanes.

- [ ] Keyboard traversal: Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator and/or NVDA pass for the same core flows.
- [ ] Windows text scaling at 125%, 150%, and 200% where practical.
- [ ] Windows high contrast pass for focus, selected, disabled, warning, and error states.
- [ ] Live region-drag proof using only safe desktop content.
- [ ] Clean-profile or clean-VM portable GUI first-launch pass; keep compiled installer install/uninstall separate unless tooling is available.
- [ ] Record each lane with `manual-validation record-lane` rather than hand-editing Markdown.
- [ ] Patch confirmed issues only: inaccessible names, tab-order traps, invisible focus, clipped text, contrast failures, small targets, stale copy, or unclear recovery states.
- [ ] Run Product Design/WPF screenshot-backed review after any material desktop UI fix.

Proof:

- [ ] Completed lane notes under `artifacts/manual-validation/2026-06-15-current-required-proof/`.
- [ ] Safe screenshots/clips only where they demonstrate a private-free state or defect.
- [ ] Focused UI/service tests for fixes.
- [ ] Regenerated summary/proof-plan.
- [ ] `artifacts/tranche-required-desktop-proof/notes.md`.

## Tranche 2 - Recording Hardware And Stability Proof

Goal: validate the recording claims that cannot be proven with synthetic media.

- [x] Run private-safe app-owned proof-scene MP4 smoke and `ffprobe` metadata validation.
- [x] Patch MP4 pacing so slow WGC/frame-composition delivery does not shorten constant-FPS output duration.
- [ ] Run live multi-monitor capture proof with safe content.
- [ ] Run live multi-monitor recording proof with safe content.
- [ ] Run long recording stability proof with microphone/system-audio/webcam only when safe.
- [ ] Check audio sync metadata, device disconnect/recovery, webcam permission granted/denied states, and fallback messaging.
- [ ] Capture media metadata or `ffprobe` output where available without preserving private content.
- [ ] Patch deterministic recording defects found during the proof pass.

Proof:

- [ ] Recording diagnostics and redacted media metadata.
- [x] App-owned proof-scene MP4, sidecar, and media metadata under `artifacts/tranche-proof-scene-recording-smoke/`.
- [ ] Safe recordings or reviewed metadata summaries.
- [ ] Updated manual summary/proof-plan.
- [ ] Focused recording tests plus standard build/test/CLI/package gate.
- [ ] `artifacts/tranche-recording-hardware-proof/notes.md`.

## Tranche 3 - Android Safe-Device Live Proof

Goal: collect real Android evidence only after safe phone content is staged.

- [ ] Stage an Android screen with no private notifications, accounts, chats, contacts, photos, tokens, or customer data.
- [ ] Run live `diagnostics android`.
- [ ] Run screenshot capture/import.
- [ ] Run bounded `screenrecord` pull/import.
- [ ] Run guarded `capture android-preview --execute` with selected device, safe-content confirmation, and duration/frame/byte/timeout caps.
- [ ] Review all media before preserving it as proof.
- [ ] Keep production Android streaming deferred.

Proof:

- [ ] Safe-device diagnostics.
- [ ] Reviewed screenshot/video/preview artifacts.
- [ ] Contact sheet if generated.
- [ ] Updated manual summary/proof-plan.
- [ ] `artifacts/tranche-android-live-device-proof/notes.md`.

## Tranche 4 - Clean Machine And Packaging Reality

Goal: separate portable package proof from true clean-machine or installer proof.

- [x] Re-run portable package verification after source/docs/proof changes.
- [x] Run packaged CLI help/diagnostics smoke with isolated GoatShot roots.
- [x] Run packaged app-owned WPF main-window render with isolated GoatShot roots.
- [ ] If a clean Windows VM/profile is available, run human WPF first-launch and basic navigation proof.
- [ ] If installer tooling/signing is available, run installer compile/install/uninstall proof as a separate lane.
- [ ] Keep OS driver installation, signed virtual printer work, and enterprise deployment proof later-scope unless explicitly scheduled.

Proof:

- [x] Portable package verifier output.
- [x] Local packaged WPF render screenshot from isolated roots.
- [ ] Clean-machine VM/profile notes/screenshots if available.
- [ ] Installer logs only if installer tooling is available.
- [x] `artifacts/tranche-clean-machine-packaging-proof/notes.md`.

## Tranche 5 - Patch Loop From Proof Findings

Goal: turn observed defects into small implementation tranches.

- [x] Create a findings list from desktop, hardware, Android, and clean-machine proof.
- [x] Sort findings by user impact and release-blocking severity.
- [ ] Patch only confirmed problems, keeping changes scoped to the owning service/view/model.
- [ ] Add or update tests that prove each fix.
- [ ] Re-run Product Design/WPF audit for material UI changes.
- [ ] Re-run the exact lane that exposed the defect.

Proof:

- [x] Focused findings service tests.
- [x] Regenerated summary/proof-plan/findings evidence for the current manual-validation folder.
- [ ] Standard build/test/CLI/package gate.
- [x] Tranche note for findings generation under `artifacts/tranche-manual-validation-findings/`.
- [ ] Tranche notes per fix group after concrete findings are patched.

## Tranche 6 - Final Evidence And Handoff Refresh

Goal: make the handoff match the actual current source and proof state.

- [ ] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/current-build-plan-oauth-parked-2026-06-15.md`, `artifacts/current-remaining-non-oauth-todos-2026-06-15.md`, and this plan from observed evidence only.
- [ ] Run `dotnet build .\GoatShot.slnx -c Release`.
- [ ] Run `dotnet test .\GoatShot.slnx -c Release`.
- [ ] Run CLI `--help`.
- [ ] Run CLI `diagnostics print`.
- [ ] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [ ] Run `.\scripts\create-release-proof-bundle.ps1 -Version 0.1.0`.
- [ ] Confirm the final proof-plan counts and call out anything still manual/parked.

Proof:

- [ ] Fresh logs under a final tranche folder.
- [ ] Fresh release proof bundle manifest and ZIP.
- [ ] Final handoff note with implemented, locally proven, manually verified, OAuth parked, and later-scope sections.

## Parked Or Later-Scope

- [ ] OAuth consent screens, refresh-token live proof, live cloud upload/delete proof, and provider-account diagnostics polish.
- [ ] Browser-store account submission, review/signing, publication, and automatic browser extension installation.
- [ ] Chrome/Firefox live browser fixture proof unless those live compatibility claims are advertised.
- [ ] Hosted/self-hosted companion portal accounts, sync, media hosting, and remote admin.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted marketplace behavior.
- [ ] True OS virtual-printer driver installation, signed driver packaging, and clean-machine printer proof.
- [ ] Production Android streaming beyond bounded ADB screenshot/video/preview helpers.

## Immediate Next Commands

```powershell
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation operator-pack --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation summarize --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --json
.\src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe manual-validation proof-plan --folder .\artifacts\manual-validation\2026-06-15-current-required-proof --output .\artifacts\manual-validation\2026-06-15-current-required-proof --json
dotnet build .\GoatShot.slnx -c Release
dotnet test .\GoatShot.slnx -c Release
```

Expected next: close Tranche 0, then use the generated operator pack to drive the six required human desktop lanes. OAuth remains parked until explicitly scheduled.

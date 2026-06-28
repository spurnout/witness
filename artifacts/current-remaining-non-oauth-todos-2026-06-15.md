# GoatShot Current Remaining Non-OAuth TODOs

Date: 2026-06-15

Purpose: continue implementing what is actually left while keeping OAuth and live cloud-account proof parked. The remaining work is proof-driven: collect safe local/manual evidence, patch concrete findings, and refresh release proof. Do not reopen Google Drive, Dropbox, OneDrive, refresh-token live behavior, or live-provider account proof in this pass.

No Git workflow is required right now.

## Ground Rules

- [ ] Keep GoatShot native WPF/.NET; do not introduce a web stack for the desktop app.
- [ ] Keep OAuth exactly where it is unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not claim live provider proof, refresh-token reliability, full accessibility compliance, clean-machine installer validation, long-run hardware stability, browser-store publication, or hosted/team behavior without fresh evidence.
- [ ] Use Product Design/WPF screenshot-backed review for material desktop UI changes; use Figma only if a screenshot audit finds redesign work that benefits from visual layout exploration.
- [ ] End implementation tranches with `artifacts/tranche-<name>/notes.md`, proof paths, and remaining-risk boundaries.

## Completed Tranche 1 - Close Clean-Profile WPF First-Launch Proof

Goal: finish the in-progress packaged WPF first-launch proof without relying on private desktop screenshots.

- [x] Verify the packaged `GoatShot.exe --render-main --output <png>` run exits successfully from isolated local/library roots.
- [x] Keep the app-owned WPF render screenshot as the proof artifact; do not capture the user desktop.
- [x] Record isolated roots and packaged CLI `paths` output.
- [x] Write `artifacts/tranche-clean-profile-wpf-first-launch/notes.md`.
- [x] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active ledgers to say packaged WPF first-launch render proof is local-only.
- [x] Keep clean VM, human click-through, and compiled installer proof manual unless safe interactive evidence is staged.
- [x] Current portable ZIP verification plus packaged app-owned WPF render proof refreshed under `artifacts/tranche-clean-machine-packaging-proof/`.

Proof:

- [x] Focused `AppStartupOptions` tests if the render command changes.
- [x] Release build and full Release tests.
- [x] CLI help and diagnostics.
- [x] `scripts\package-release.ps1 -SkipInstaller`.
- [x] Packaged render screenshot, process exit, and isolated-path output.
- [x] Current portable verifier output, packaged CLI smoke, packaged WPF render screenshot, and isolated-path output under `artifacts/tranche-clean-machine-packaging-proof/`.

## Tranche 2 - Manual Desktop And Accessibility Proof Pass

Goal: turn the required local-V1 manual lanes from `NotRun` into observed evidence, then fix only confirmed issues.

- [x] Run or refresh `manual-validation create --include-diagnostics-bundle`.
- [x] Fill baseline setup notes with command-backed local evidence.
- [x] Collect command-backed desktop proof through `manual-validation desktop-proof --run-commands`, including app-owned screenshots, WPF focus/name audits, environment evidence, lane notes, and redaction-clean summary output.
- [x] Add a safe proof scene staging surface for private-free keyboard, text-scaling, high-contrast, live region-drag, and recording checks.
- [x] Generate a required desktop operator pack with a consolidated checklist, per-lane notes, a print-only `record-lane` command reference, and manifest under the current manual-validation folder.
- [x] Generate a sorted manual-validation findings list that identifies release-blocking required lanes, hardware-gated claim boundaries, optional compatibility gaps, parked scope, and redaction risks.
- [ ] Fill keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Fill Narrator/NVDA notes for key WPF flows.
- [ ] Check Windows text scaling and high contrast.
- [ ] Run live region-drag proof with safe desktop content.
- [ ] Keep OAuth/live-provider templates parked.
- [ ] Patch concrete findings: missing accessible names, broken focus order, unclear state copy, contrast issues, target-size issues, stale diagnostics, or confusing recovery paths.
- [x] Regenerate `manual-validation summarize` and `manual-validation proof-plan`.

Proof:

- [x] Generated manual-validation folder with diagnostics bundle.
- [x] `manual-validation baseline --run-commands` completed under `artifacts/manual-validation/2026-06-15-current-required-proof/`; `Baseline Setup` now reports `Passed`.
- [x] App-owned render screenshots and WPF audit notes saved under `artifacts/manual-validation/2026-06-15-current-required-proof/desktop-proof/`; human observation remains open.
- [x] Safe proof scene render/audit saved under `artifacts/tranche-safe-proof-scene/`, `artifacts/product-design-audit/2026-06-15/safe-proof-scene/`, and the current desktop-proof packet as `desktop-proof/screenshots/proof-scene.png` plus `desktop-proof/audits/proof-scene-accessibility.md`.
- [x] `manual-validation record-lane` implemented and locally proven under `artifacts/tranche-manual-lane-update-helper/`; use it to record real operator-observed lane outcomes with redacted notes/evidence instead of hand-editing lane Markdown.
- [x] Required desktop operator pack proof under `artifacts/tranche-required-desktop-operator-pack/` and `artifacts/manual-validation/2026-06-15-current-required-proof/required-desktop-operator-pack/`; this is a handoff packet and does not complete human proof by itself.
- [x] Manual validation findings proof under `artifacts/tranche-manual-validation-findings/` and `artifacts/manual-validation/2026-06-15-current-required-proof/manual-validation-findings.md`; this is a triage/reporting aid and does not complete human proof by itself.
- [ ] Safe screenshots or notes for human-observed states.
- [x] WPF audit notes for command-backed static/focus evidence; no material UI redesign was made in this tranche.
- [x] Focused `ManualValidationBaseline` tests.
- [x] Focused `ManualValidationDesktopProof` tests.
- [x] Release build and full Release tests through the baseline helper; full suite passed 436 tests.
- [x] CLI help smoke includes `manual-validation baseline`; current help also includes `manual-validation desktop-proof`.

## Tranche 3 - Recording Hardware Stability Proof

Goal: validate the recording paths that cannot be honestly proven with synthetic media alone.

- [x] Collect command-backed hardware/device readiness evidence through `manual-validation hardware-proof --run-commands`.
- [x] Update multi-monitor capture, multi-monitor recording, long-recording, and Android safe-device lanes as `Blocked` with explicit readiness-vs-live-proof boundaries.
- [x] Add and run private-safe app-owned proof-scene MP4 smoke with `ffprobe` metadata validation.
- [x] Patch MP4 pacing so slow WGC/frame-composition delivery does not shorten constant-FPS output duration.
- [ ] Run safe all-monitor and multi-monitor recording if hardware is available.
- [ ] Run long-recording stability proof with safe content.
- [ ] Verify microphone/system-audio duration and sync metadata.
- [ ] Verify webcam overlay states with permission granted and denied where practical.
- [ ] Verify fallback behavior when WGC, camera, audio, HEVC, or FFmpeg paths are unavailable.
- [ ] Patch deterministic issues found during proof.

Proof:

- [ ] Recording diagnostics.
- [ ] Safe recording artifacts or redacted summaries.
- [ ] Media metadata/ffprobe output where available.
- [x] App-owned proof-scene recording smoke artifacts under `artifacts/tranche-proof-scene-recording-smoke/`.
- [ ] Focused tests and standard proof gate for fixes.
- [x] Hardware readiness packet under `artifacts/manual-validation/2026-06-15-current-required-proof/hardware-proof/`.
- [x] Helper proof under `artifacts/tranche-hardware-readiness-proof/`.

## Tranche 4 - Android Safe-Device Live Proof

Goal: collect real-device proof only when safe phone content is staged.

- [x] Collect Android readiness diagnostics through `manual-validation hardware-proof --run-commands`; current state is readiness/blocker evidence, not live phone media proof.
- [ ] Prepare Android content with no private notifications, accounts, messages, contacts, photos, tokens, or customer data.
- [ ] Run live `diagnostics android`.
- [ ] Run live screenshot capture/import.
- [ ] Run bounded `screenrecord` pull/import.
- [ ] Run guarded `capture android-preview --execute` with selected device, safe-content confirmation, and duration/frame/byte/timeout caps.
- [ ] Review media before keeping it as evidence.
- [ ] Do not reopen production Android streaming for V1.

Proof:

- [ ] Live safe-device diagnostics.
- [ ] Reviewed screenshot/video/preview artifacts.
- [ ] Contact sheet if generated.
- [ ] Manual-validation summary update.

## Completed Tranche 5 - Optional Browser Compatibility Proof

Goal: collect Chrome/Firefox live fixture proof only if V1 compatibility claims need it. Edge proof is already complete.

- [x] Decide whether Chrome and Firefox live screenshots are needed for current claims.
- [ ] If needed, run the existing live-fixture helper, native-host status proof, package export, import, and proof validation.
- [ ] Capture browser-specific screenshots for extension details, consent defaults, Host Status, package export, selected-element mode, and last result.
- [ ] Keep browser-store account submission, review, signing, and automatic installation later-scope.

Proof:

- [x] Decision note saved under `artifacts/manual-validation/2026-06-15-current-required-proof/browser-extension-live-fixture/`.
- [x] Lane recorded as `NotApplicable` for current V1 claims with `manual-validation record-lane`.
- [ ] Redacted payload/import result.
- [ ] `browser-extension proof validate --folder <proof-folder>` output.
- [x] Manual-validation summary/proof-plan update reports 0 optional compatibility lanes open.

## Completed Tranche 6 - Final Release Evidence Refresh

Goal: refresh the handoff bundle after any proof or source changes.

- [x] Run `dotnet build .\GoatShot.slnx -c Release`.
- [x] Run `dotnet test .\GoatShot.slnx -c Release`.
- [x] Run CLI `--help`.
- [x] Run CLI `diagnostics print`.
- [x] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [x] Run `scripts\create-release-proof-bundle.ps1` if README/spec/readiness or proof claims changed materially.
- [x] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active TODO ledgers from actual evidence only.

Proof:

- [x] Fresh build/test/CLI/package logs.
- [x] Fresh release proof bundle if run.
- [x] Post-manual-baseline release proof: `artifacts/tranche-final-evidence-refresh-after-manual-baseline/`, 6 commands passed, 436 tests passed, 0 policy exclusions.
- [x] Final handoff note separating implemented, locally proven, manually verified, OAuth-parked, and later-scope items.
- [x] Post-safe-proof-scene release proof: `artifacts/tranche-safe-proof-scene/` plus refreshed formal bundle under `artifacts/tranche-release-proof-admin/`, 448 tests passed, 0 policy exclusions.

## Parked Or Later-Scope

- [ ] OAuth consent screens, refresh-token live proof, live upload/delete proof, and provider-account polish.
- [ ] Clean Windows VM or installer click-through proof unless a safe machine/session is available.
- [ ] Browser-store account submission/review/signing and automatic browser extension installation.
- [ ] Hosted/self-hosted companion portal accounts, sync, media hosting, and remote admin.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted marketplace behavior.
- [ ] True OS virtual-printer driver installation and signed/admin clean-machine proof.
- [ ] Production Android streaming beyond bounded ADB screenshot/video/preview helpers.

## Definition Of Done For Each Tranche

- [ ] Focused tests for changed services, CLI behavior, UI models, or renderers.
- [ ] WPF screenshot/Product Design evidence for material desktop UI changes.
- [ ] Redaction/privacy checks for prompts, transcripts, OCR text, URLs, tokens, logs, settings, diagnostics, and provider payloads.
- [ ] Release build and full Release tests.
- [ ] CLI help and diagnostics smoke.
- [ ] Portable package proof.
- [ ] Tranche note with changed files, proof artifacts, unverified boundaries, and next recommended step.

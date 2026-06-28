# GoatShot Next Non-OAuth Implementation TODOs

Date: 2026-06-15

Purpose: continue implementing what is left without reopening Google Drive, Dropbox, OneDrive, or other live OAuth/account proof. OAuth stays in its current implemented-but-live-proof-parked state while the remaining local, browser, hardware, packaging, and later-module decisions are handled in small proven tranches.

No Git workflow is required right now.

## Current Baseline

- [x] Native WPF/.NET desktop app, CLI, tests, diagnostics, local proof artifacts, Product Design/WPF screenshot-backed audits, manual validation tooling, and portable packaging exist.
- [x] Core local V1 buildout is broadly implemented: capture, scrolling capture, recording, editor/privacy tools, OCR, video tools, AI/document workflows, automation, upload queue/history, provider adapters, browser extension scaffolding, Android ADB helpers, print-import file-drop, plugins, admin policy, companion export, diagnostics, and release proof.
- [x] Edge browser-extension safe-fixture proof is complete.
- [x] Android production streaming and true OS virtual-printer driver work are explicitly deferred for V1.
- [x] Companion portal is local static read-only export only for V1.
- [x] Plugin marketplace behavior is local/read-only/governed only for V1.
- [x] Browser extension publication-plan source/artifact work is complete and locally proven under `artifacts/tranche-browser-extension-publication-plan/`.

## Working Rules

- [ ] Keep OAuth consent screens, refresh-token live proof, live cloud-account upload/delete proof, and provider-account polish parked.
- [ ] Keep GoatShot native WPF/.NET; do not introduce a web stack for the desktop app.
- [ ] Prefer local proof: focused MSTest coverage, fake HTTP/process providers, safe synthetic media, WPF render screenshots, CLI smoke, diagnostics redaction checks, package output, and notes.
- [ ] Use Product Design/WPF screenshot-backed review for material desktop UI changes. Use Figma only if a local screenshot audit finds redesign work that benefits from visual exploration.
- [ ] Do not claim manual/hardware/browser-store/account proof unless it was actually collected.

## Completed Tranche 0 - Finish Browser Extension Publication Plan

Goal: close the in-progress browser-store publication planning tranche as a read-only planning surface, without contacting stores or mutating browser profiles.

- [x] Re-open `BrowserExtensionPublicationPlanService`, `Program.cs`, and `BrowserExtensionPublicationPlanServiceTests`.
- [x] Verify `browser-extension publication-plan|publish-plan|store-publication-plan` works after a fresh build.
- [x] Confirm generated plans reference existing store-readiness and store-package artifacts for Chrome, Edge, and Firefox.
- [x] Confirm mutation flags stay false: no store account contact, no package upload, no review/signing claim, no extension install, no browser profile mutation.
- [x] Add or update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` only for proven behavior.
- [x] Write `artifacts/tranche-browser-extension-publication-plan/notes.md`.

Proof:

- [x] Focused publication-plan tests.
- [x] `browser-extension store-package --target all` proof.
- [x] `browser-extension publication-plan --target all --json` proof.
- [x] Text publication-plan proof.
- [x] Release build, full Release tests, CLI help, CLI diagnostics, portable package.

## Tranche 1 - Manual Validation And Deterministic Fix Pass

Goal: turn remaining human/device/accessibility unknowns into observed evidence, then patch only concrete issues found.

- [x] Add requirement-aware lane classification so required local-V1 desktop proof, hardware-gated proof, optional compatibility proof, and parked OAuth/live-provider proof are summarized differently.
- [x] Add `manual-validation proof-plan` so the current summary can generate required-lane operator steps, recommended evidence names, and claim boundaries.
- [ ] Run or refresh `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Fill keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Fill Narrator/NVDA notes for key WPF flows.
- [ ] Check Windows text scaling and high contrast.
- [ ] Run live region-drag proof with safe desktop content.
- [ ] Run multi-monitor capture proof if hardware is available.
- [ ] Run multi-monitor recording proof if hardware is available.
- [ ] Run long-recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Keep live provider/OAuth templates parked.
- [ ] Fix deterministic issues found in this pass: missing accessible names, broken focus order, unclear disabled-state copy, contrast issues, target-size issues, or stale diagnostics text.
- [ ] Regenerate manual-validation summary.

Proof:

- [x] Requirement-aware generated templates and summary under `artifacts/tranche-manual-validation-requirement-classification/`.
- [x] Focused manual-validation tests and Release build proof.
- [x] Requirement-aware proof plan under `artifacts/tranche-manual-validation-proof-plan/`.
- [ ] Filled manual-validation templates and summary.
- [ ] Safe screenshots or short clips where useful.
- [ ] Focused tests/screenshots for any code fixes.
- [ ] Standard proof gate after code changes.

## Completed Tranche 2 - Chrome And Firefox Browser Fixture Decision

Goal: decide whether Edge live proof is enough for V1, or collect Chrome/Firefox proof using existing safe-fixture tooling.

- [x] Decide whether Chrome live proof is required for V1 claims.
- [x] Decide whether Firefox live proof is required for V1 claims.
- [ ] If required, run existing live-fixture helper, native-host status, package export, import, and proof validation.
- [ ] Capture browser-specific screenshots: extension details, popup consent defaults, options consent defaults, Host Status, package-export toggle, selected-element mode, and last result.
- [x] Keep browser-store account submission/review/signing, permanent/store-managed automatic installation, and actual enterprise deployment proof out of scope.
- [x] Decision: Chrome/Firefox live fixture proof is optional/manual for V1, not a blocker for the local V1 candidate.

Proof:

- [x] Decision artifact: `artifacts/tranche-browser-cross-browser-proof-decision/chrome-firefox-proof-decision.md`.
- [ ] Browser-specific proof folder if Chrome/Firefox live proof is later reopened.
- [ ] Redacted payload/import result if Chrome/Firefox live proof is later reopened.
- [ ] `browser-extension proof validate --folder <proof-folder>` output if Chrome/Firefox live proof is later reopened.
- [ ] Manual-validation summary update if Chrome/Firefox live proof is later reopened.

## Tranche 3 - Android Safe-Device Live Proof

Goal: collect real-device Android proof only when staged safe phone content exists.

- [ ] Prepare safe Android content with no private notifications, accounts, messages, contacts, photos, tokens, or customer data.
- [ ] Run live `diagnostics android`.
- [ ] Run live screenshot capture/import.
- [ ] Run bounded `screenrecord` pull/import.
- [ ] Run guarded `capture android-preview --execute` with selected device, safe-content confirmation, duration/frame/byte/timeout caps, and optional contact sheet.
- [ ] Review collected media before keeping it as evidence.
- [ ] Do not reopen production Android streaming.

Proof:

- [ ] Live safe-device diagnostics.
- [ ] Imported screenshot/video/preview artifacts reviewed for privacy.
- [ ] Contact sheet if generated.
- [ ] Manual-validation summary update.

## Partially Completed Tranche 4 - Clean Profile And Packaging Proof

Goal: prove the portable handoff from a clean local profile or VM without requiring an installer.

- [x] Run the latest portable ZIP from an equivalent isolated user-data folder for packaged CLI proof.
- [x] Run packaged app-owned WPF first-launch render proof from isolated local/library roots.
- [ ] Run human WPF GUI first launch from a clean profile/VM when safe interactive desktop proof is available.
- [x] Verify CLI availability, diagnostics print, and browser-extension source availability from packaged output.
- [x] Verify no private workspace data, DPAPI secrets, or proof artifacts are bundled unexpectedly.
- [ ] If Inno Setup is available, optionally compile the installer and record the result; otherwise keep installer compilation explicitly skipped.
- [ ] Keep clean-machine OS printer-driver proof and live account proof parked.

Proof:

- [x] Local isolated-root portable package notes: `artifacts/tranche-clean-portable-package-proof/notes.md`.
- [x] Package content audit.
- [x] CLI diagnostics from packaged output.
- [x] Local packaged app-owned WPF first-launch render screenshot: `artifacts/tranche-clean-profile-wpf-first-launch/portable-first-launch-main-window-startprocess.png`.
- [ ] Clean Windows VM or human click-through WPF first-launch screenshots.
- [ ] Updated release proof note if claims change.

## Tranche 5 - Recording Hardware Stability Proof

Goal: finish non-OAuth recording proof that needs real hardware state.

- [ ] Run safe live all-monitor recording if multi-monitor hardware is available.
- [ ] Run long recording stability proof with safe content.
- [ ] Verify microphone/system-audio duration and sync metadata.
- [ ] Verify webcam overlay behavior with permission granted and denied where practical.
- [ ] Verify fallback behavior when WGC, camera, audio, HEVC, or FFmpeg paths are unavailable.
- [ ] Patch deterministic issues found during proof.

Proof:

- [ ] Recording diagnostics.
- [ ] Media metadata/ffprobe output where available.
- [ ] Safe recording artifacts or redacted summaries.
- [ ] Focused tests for fixes.
- [ ] Standard proof gate after code changes.

## Tranche 6 - Later-Module Scheduling Decisions

Goal: prevent later-scope modules from floating as vague roadmap items.

- [x] Browser-store publication: keep as manual post-V1 store-account work; do not schedule account-backed publication in this pass.
- [x] Plugin background checks: V1 supports governed passive summaries plus background check/stage-only runs; automatic install/trust/enable/allowlist/execute updates remain out.
- [x] Hosted/self-hosted portal: keep local static export for V1 unless a narrow hosted architecture is explicitly approved.
- [x] OS virtual-printer driver: keep file-drop/import for V1 unless admin/signing/clean-machine proof is explicitly accepted.
- [x] Android streaming: keep bounded ADB helpers for V1 unless post-V1 Android companion-app work is approved.
- [x] Team/remote admin sync: keep local policy bundles for V1 unless hosted portal/team sync is approved.

Proof:

- [x] Consolidated later-module decision note: `artifacts/tranche-later-module-scheduling-decisions/later-module-decisions.md`.
- [x] README/spec/readiness updates only for decisions made.

## Tranche 7 - Final Release Evidence Refresh

Goal: refresh release evidence after any manual proof, source changes, or readiness-claim changes.

- [ ] Run `dotnet build .\GoatShot.slnx -c Release`.
- [ ] Run `dotnet test .\GoatShot.slnx -c Release`.
- [ ] Run CLI `--help`.
- [ ] Run CLI `diagnostics print`.
- [ ] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [ ] Run release proof bundle script if README/spec/readiness or proof claims changed materially.
- [ ] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active TODO ledgers from actual evidence only.

Proof:

- [ ] Fresh build/test/CLI/package logs.
- [ ] Fresh release proof bundle if run.
- [ ] Final handoff note naming implemented, locally proven, manually verified, parked, and later-scope items.

## Recommended Execution Order

1. Tranche 0 is complete under `artifacts/tranche-browser-extension-publication-plan/`.
2. Run Tranche 1 next if safe manual desktop/accessibility content is available; patch only concrete findings.
3. Tranche 2 decision is complete under `artifacts/tranche-browser-cross-browser-proof-decision/`; Chrome/Firefox live proof remains optional/manual if safe browser content is staged.
4. Tranche 4 has local packaged-CLI verification under `artifacts/tranche-clean-portable-package-proof/`; run clean-profile/VM WPF first-launch proof when safe interactive desktop proof is available.
5. Run Tranche 5 when hardware and safe recording content are available.
6. Run Tranche 6 to turn any remaining post-V1 choices into explicit decisions.
7. End with Tranche 7 release evidence refresh.

## Current Claim Boundary

Acceptable claim after the current local evidence: GoatShot is a broad, locally proven native desktop V1 candidate with explicit manual/OAuth/hardware/browser-store/account gaps.

Do not claim yet: live OAuth consent validation, refresh-token reliability against real accounts, clean-machine installer validation, full accessibility compliance, long-run hardware stability, Chrome/Firefox live browser fixture proof, browser-store account submission/review/signing, permanent/store-managed automatic extension installation, actual enterprise policy deployment/force-install proof, true OS virtual-printer driver installation, automatic plugin install/trust/enable/allowlist/execute updates, hosted marketplace behavior, hosted/self-hosted portal sync, remote admin, live Android device proof, or production Android streaming.

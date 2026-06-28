# GoatShot Continue-Implementation TODO Plan - OAuth Parked

Date: 2026-06-15

Purpose: continue building GoatShot from the current native WPF/.NET state without reopening Google Drive, Dropbox, OneDrive, or other live OAuth/account proof. OAuth stays where it is unless a non-OAuth task exposes a small compatibility bug.

No Git workflow is required right now.

## Current Truth

- [x] Core local V1 product work is broadly implemented and locally proven: capture, scrolling capture, recording, editor/privacy, OCR, video tools, AI/document workflow, workflow automation, upload queue/history, provider adapters, browser extension scaffolding, Android import/preview, virtual-printer file-drop import, plugins, local admin policy, diagnostics, tests, and portable packaging.
- [x] Edge live browser-extension safe-fixture proof is complete under `artifacts/tranche-browser-live-fixture-proof-closure/`.
- [x] Chrome/Firefox live browser proof remains optional/manual if cross-browser live evidence is needed.
- [x] Manual-validation folder `artifacts/manual-validation/2026-06-15-post-edge-proof/` exists, with the Edge browser lane filled in.
- [x] The post-Edge manual-validation summary was repaired under `artifacts/manual-validation/2026-06-15-post-edge-proof/manual-validation-summary.json` and `manual-validation-summary.md`; it now classifies the Edge browser lane as `Passed`, keeps OAuth/live-provider proof parked, and leaves unfilled human/hardware lanes as `NotRun`.
- [x] Read-only companion portal local export V0 is implemented and locally proven under `artifacts/tranche-companion-portal-readonly-v0/`.
- [x] Virtual-printer driver decision is complete under `artifacts/tranche-virtual-printer-driver-decision/`: keep file-drop/import for V1; do not add OS printer-driver code until a post-V1 driver/Print Support App path is explicitly scheduled.
- [x] Production Android streaming decision is complete under `artifacts/tranche-android-production-streaming-decision/`: keep bounded ADB screenshot/video/preview paths for V1; defer production streaming to a post-V1 module.
- [x] Release proof was refreshed after the Android decision under `artifacts/tranche-release-proof-post-android-decision/`, and the formal release proof bundle is refreshed under `artifacts/tranche-release-proof-admin/`.
- [ ] Remaining work is now mostly manual/hardware/device proof, Chrome/Firefox browser fixture proof if needed, and deliberate later-module decisions for browser-store publication and unattended plugin updates.

## Parked Until A Dedicated OAuth/Account Tranche

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Live refresh-token expiry, reauthorization, and recovery proof.
- [ ] Live cloud upload/delete proof against real provider accounts.
- [ ] Provider-specific live scopes, account diagnostics, and consent-screen copy polish.

## Completed Tranche 0 - Repair Current Manual Evidence Summary

Goal: make the post-Edge manual-validation folder internally consistent before more proof is added.

- [x] Re-run `goatshot manual-validation summarize --folder artifacts\manual-validation\2026-06-15-post-edge-proof --json` without redirecting stdout inside the scanned manual-validation folder.
- [x] Confirm `12-browser-extension-live-fixture.md` is classified as passed.
- [x] Confirm all unfilled manual lanes remain `NotRun`, not silently claimed.
- [x] Save command output/exit code separately from generated summary files under `artifacts/tranche-manual-summary-repair/`.
- [x] Update readiness ledgers only from regenerated evidence.

Proof:

- [x] Regenerated `manual-validation-summary.json`.
- [x] Regenerated `manual-validation-summary.md`.
- [x] Command output and exit-code artifact. The command exits `1` by design while required manual lanes remain `NotRun`.
- [x] `artifacts/tranche-manual-summary-repair/notes.md`.

## Tranche 1 - Manual Validation And Small Fix Pass

Goal: turn the remaining human/device/accessibility unknowns into observed evidence, then fix only deterministic issues discovered during the pass.

- [ ] Complete baseline setup notes for the current app/package.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region-drag proof using safe desktop content.
- [ ] Complete multi-monitor capture proof if hardware is available.
- [ ] Complete multi-monitor recording proof if hardware is available.
- [ ] Complete long-recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-profile or clean-machine portable ZIP proof if a suitable profile/VM is available.
- [ ] Keep live provider account proof pending/parked.
- [ ] Fix small deterministic findings from this pass, such as missing accessible names, broken focus order, unclear disabled-state copy, contrast regressions, or stale diagnostics text.
- [ ] Use Product Design/WPF screenshot-backed review for any material desktop UI changes found during this pass; use Figma only if a redesign needs visual exploration.

Proof:

- [ ] Filled manual-validation templates.
- [ ] Safe screenshots where helpful.
- [ ] Focused tests/screenshots for any code fixes.
- [ ] Regenerated manual-validation summary.

## Completed Tranche 2 - Release Proof Refresh

Goal: make handoff artifacts match the latest source, latest proof, and latest manual-validation status.

Status note: the release proof bundle was refreshed after the Android production-streaming decision and proof-script hardening. Current evidence is saved under `artifacts/tranche-release-proof-post-android-decision/`, and the formal release proof output is under `artifacts/tranche-release-proof-admin/`.

- [x] Run `dotnet build .\GoatShot.slnx -c Release`.
- [x] Run `dotnet test .\GoatShot.slnx -c Release`.
- [x] Run `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`.
- [x] Run `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`.
- [x] Run `.\scripts\package-release.ps1 -SkipInstaller`.
- [x] Refresh the release proof bundle.
- [x] Update readiness/TODO ledgers only for evidence actually collected.

Proof:

- [x] Fresh build/test/CLI/package logs: `artifacts/tranche-release-proof-post-android-decision/`.
- [x] Fresh portable ZIP path: `artifacts/dist/GoatShot-0.1.0-win-x64-portable.zip`.
- [x] Fresh release proof bundle: `artifacts/tranche-release-proof-admin/manifest.json` plus latest timestamped `GoatShot-release-proof-0.1.0-*.zip`.
- [x] Notes naming skipped manual/OAuth/hardware proof: `artifacts/tranche-release-proof-post-android-decision/notes.md`.

## Tranche 3 - Optional Chrome/Firefox Browser Fixture Proof

Goal: decide whether Edge proof is enough for V1, or collect cross-browser evidence without reopening store publication.

- [ ] Decide whether Chrome and/or Firefox live fixture proof is required for V1 claims.
- [ ] If required, use the existing safe fixture, install-plan, native-host status, and proof validator.
- [ ] Capture required screenshots: extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff result.
- [ ] Export a safe stitch package and import it through the native receiver.
- [ ] Run `browser-extension proof validate --folder <proof-folder>`.
- [ ] Keep browser-store publication, review/signing, and automatic installation out of scope.

Proof:

- [ ] Browser-specific screenshots.
- [ ] Exported stitch package.
- [ ] Redacted payload/import result JSON.
- [ ] Proof validation JSON/Markdown.

## Tranche 4 - Android Live Safe-Device Proof

Goal: collect real-device Android proof only with staged safe content, before any production streaming decision.

- [ ] Prepare a safe Android screen with no private notifications, accounts, messages, contacts, photos, tokens, or customer data.
- [ ] Run live `diagnostics android`.
- [ ] Run live screenshot capture/import.
- [ ] Run bounded `screenrecord` pull/import.
- [ ] Run guarded preview execution with `--execute`, selected device, safe-content confirmation, frame/byte/timeout caps, and optional contact sheet.
- [ ] Summarize results through manual-validation tooling.
- [ ] Do not start production Android streaming until this proof exists and is reviewed.

Proof:

- [ ] Live safe-device diagnostics.
- [ ] Imported screenshot/video/preview artifacts reviewed for privacy.
- [ ] Manual-validation summary update.

## Completed Tranche 5 - Plugin Marketplace Governance Planner

Goal: advance marketplace readiness without automatic background updates, hosted accounts, ratings, payments, remote execution, or ungoverned installs.

- [x] Write a marketplace authority note covering registry trust, package provenance, update governance, policy blocks, operator approval, audit evidence, and rollback.
- [x] Add a read-only marketplace/index planner reusing existing registry/package validation and update-summary state.
- [x] Add diagnostics that distinguish local plugin folder, staged remote packages, installed staged packages, available updates, blocked updates, and future hosted marketplace accounts.
- [x] Add tests proving marketplace planning does not install, trust, enable, allowlist, execute, auto-update, publish, or contact a marketplace account service.
- [x] Keep hosted marketplace service, accounts, ratings, payments, remote execution, and background updates later-scope.

Proof:

- [x] Governance note: `artifacts/tranche-plugin-marketplace-planner/marketplace-authority-note.md`.
- [x] Focused planner/diagnostics tests.
- [x] CLI smoke artifacts: `artifacts/tranche-plugin-marketplace-planner/marketplace-plan-sample.json` and `.txt`.
- [x] Standard proof gate under `artifacts/tranche-plugin-marketplace-planner/`.

## Completed Tranche 6 - Companion Portal Read-Only V0 Decision

Goal: decide whether a companion portal belongs in V1; if yes, start with local exported evidence viewing only.

- [x] Re-open `artifacts/tranche-companion-portal-planning/` boundary notes.
- [x] Choose local static report export for V0; do not start hosted/self-hosted portal code.
- [x] Implement read-only local export/report viewing: policy summary, diagnostics summary, proof bundle index, share/upload history aggregates, and manual-validation summary.
- [x] Add redaction tests for exported report payloads.
- [x] Do not host capture files, secrets, tokens, provider account data, OAuth state, or remote commands in v0.
- [x] Skip Product Design review because no new desktop or browser-facing UI was created; the output is static local evidence HTML/JSON.

Proof:

- [x] Boundary approval note: `artifacts/tranche-companion-portal-readonly-v0/portal-boundary-approval-note.md`.
- [x] Focused export/report tests: `CompanionPortalExportServiceTests`.
- [x] CLI export artifacts: `artifacts/tranche-companion-portal-readonly-v0/companion-portal-export.json`, `.txt`, and `portal-export/`.
- [x] Standard proof gate under `artifacts/tranche-companion-portal-readonly-v0/`.

## Completed Tranche 7 - Virtual Printer Driver Decision

Goal: decide whether true OS virtual-printer work is worth accepting admin, signing, installer, clean-machine, rollback, and support constraints.

- [x] Write a driver decision note covering Windows driver options, signing/distribution requirements, admin install, clean-machine validation, rollback/uninstall, and support burden.
- [x] Confirm GoatShot should remain file-drop/import only for V1.
- [x] Do not start driver code; require an explicit post-V1 schedule before installer planning or clean-machine driver proof.
- [x] Keep `print-import` file-drop as the production-safe fallback.

Proof:

- [x] Decision note: `artifacts/tranche-virtual-printer-driver-decision/driver-decision.md`.
- [x] Notes: `artifacts/tranche-virtual-printer-driver-decision/notes.md`.
- [x] No code added because admin/signing constraints are not accepted for V1.
- [x] If code is later added, require clean-machine/manual proof before readiness claims.

## Completed Tranche 8 - Production Android Streaming Decision

Goal: keep production Android streaming behind proof and scope control.

- [x] Do not begin production streaming until Tranche 4 live safe-device proof is complete.
- [x] Compare screencap polling, `screenrecord` chunks, H.264 stdout, FFmpeg remux, scrcpy-style external tooling, and Android companion-app/MediaProjection direction against latency, privacy, packaging, and support constraints.
- [x] Choose bounded ADB screenshot/video/preview paths for V1; do not start production streaming code.
- [x] Keep device privacy prompts and safe-content confirmations non-bypassable.

Proof:

- [x] Decision note: `artifacts/tranche-android-production-streaming-decision/android-production-streaming-decision.md`.
- [x] Notes: `artifacts/tranche-android-production-streaming-decision/notes.md`.
- [x] Prototype proof only if explicitly approved in a future post-V1 tranche.
- [x] Manual safe-device evidence remains required.

## Standard Done Gate For Any Code Tranche

- [ ] Focused MSTest coverage for changed services, models, CLI behavior, UI models, policy gates, and redaction paths.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy checks when prompts, transcripts, OCR text, URLs, tokens, logs, settings, browser packages, Android media, plugin packages, diagnostics, or report exports are touched.
- [ ] `dotnet build .\GoatShot.slnx -c Release`.
- [ ] `dotnet test .\GoatShot.slnx -c Release`.
- [ ] CLI `--help`.
- [ ] CLI `diagnostics print`.
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`.
- [ ] `artifacts/tranche-<name>/notes.md` with changed files, proof paths, skipped/manual proof, and remaining risk.

## Recommended Execution Order

1. Tranche 1: complete manual validation and patch only deterministic UI/accessibility/diagnostic issues found.
2. Tranche 2 is complete; refresh release proof again after any future manual-proof or release-evidence pass.
3. Tranche 3 only if cross-browser live proof is needed for V1.
4. Tranche 4 only when safe Android device content is staged.
5. Use the completed Tranche 6 companion export for local evidence review; keep hosted/self-hosted portal accounts and sync later-scope.
6. Tranche 7 and Tranche 8 are completed deferral decisions: no OS printer driver and no production Android streaming for V1.

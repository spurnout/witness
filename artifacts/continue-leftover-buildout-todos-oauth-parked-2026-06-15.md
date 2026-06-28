# GoatShot Leftover Buildout TODO Plan - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is left without waiting on OAuth consent screens, refresh-token proof, or real cloud-provider accounts. This is the current execution plan after the Android preview review and virtual-printer setup helper tranches.

No Git workflow is required right now.

## Decisions

- [x] Keep Google Drive, Dropbox, OneDrive, and future live OAuth/account proof parked.
- [x] Keep the existing OAuth plumbing where it is unless a non-OAuth task exposes a small compatibility bug.
- [x] Keep GoatShot as a native WPF/.NET desktop app with CLI support.
- [x] Treat fake-device, fake-provider, synthetic-media, local-token, dry-run, and diagnostics evidence as local proof only.
- [x] Use Product Design/WPF screenshot-backed review only for material desktop UI changes.
- [x] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Current Truth

- [x] The core local V1 candidate is broadly implemented and locally proven: capture, scrolling capture, recording, editor/privacy, OCR, video tools, AI/document workflow, workflow automation, upload queue/history, provider adapters, local admin policy, plugins, browser extension scaffolding, Android import/planning/preview, virtual-printer file-drop import, and portable packaging.
- [x] OAuth/live-account work is not the only remaining item; it is simply the parked account lane.
- [x] The remaining work is mostly evidence closure, manual/hardware/browser/device proof, and choosing one later module at a time.
- [x] Browser extension local packaging, native host registration, stitch-package handoff, live-fixture helper/verifier, proof manifest validation, store-readiness copy, and Edge live safe-fixture load/export/import proof are locally proven; Chrome/Firefox live proof remains manual if needed.
- [x] Android screenshot/video import and guarded preview execution are locally proven with fake ADB paths; live safe-device proof and production streaming remain manual/later.
- [x] Virtual-printer file-drop import, driver feasibility diagnostics, and setup-note helper are locally proven; true OS printer driver installation remains admin/signing/clean-machine scoped.

## Completed Tranche 0 - Evidence Hygiene Reset

Goal: make the proof ledger honest before building the next module.

- [x] Check `artifacts/tranche-virtual-printer-setup-helper/notes.md` against files actually present in that folder.
- [x] If missing, regenerate and save the referenced proof logs:
  - [x] `dotnet-test-release.txt`
  - [x] `cli-help.txt`
  - [x] `diagnostics-print.txt`
  - [x] `package-release.txt`
- [x] If proof cannot be regenerated, edit the note to state exactly what was and was not saved.
- [x] Run a quick status scan for other tranche notes that reference missing proof artifacts.
- [x] Update readiness/TODO ledgers only from evidence that exists.

Proof:

- [x] Updated tranche note or regenerated proof logs.
- [x] Short artifact note under `artifacts/tranche-proof-hygiene-reset/notes.md`.

## Completed Tranche 1 - Release Proof Refresh

Goal: make the current handoff evidence match the latest source state.

- [x] Re-run the standard proof gate from the current source.
- [x] Refresh the release proof bundle with build/test/package logs, CLI diagnostics, selected screenshot/audit notes, selected tranche notes, and manual validation summaries if available.
- [x] Keep portable ZIP as the default release artifact.
- [x] Keep installer/clean-machine proof separate unless it is actually run.
- [x] Update `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` only for evidence collected in this tranche.

Proof:

- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [x] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [x] `.\scripts\package-release.ps1 -SkipInstaller`
- [x] `.\scripts\create-release-proof-bundle.ps1`
- [x] `artifacts/tranche-release-proof-refresh/notes.md`

## Completed Tranche 2 - Browser Live Fixture Proof Closure

Goal: complete the browser extension proof lane with safe local browser content without claiming browser-store publication.

- [x] Use `browser-extension/samples/safe-fixture.html` unless another safe page is explicitly staged.
- [x] Use the existing live-fixture helper to create a proof folder for Chrome or Edge.
- [x] Register/check the user-scope native host using the existing CLI status/install commands.
- [x] Load the unpacked extension in Edge through the isolated launch helper.
- [x] Capture screenshots for extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff result.
- [x] Run one consented fixture capture with screenshot consent and package export enabled.
- [x] Import the downloaded stitch package through the native browser-extension receiver path.
- [x] Run `goatshot browser-extension proof validate --folder <proof-folder>`.
- [x] Keep browser-store publication, review, signing, and automatic installation out of scope.

Proof:

- [x] Live browser screenshots with only safe fixture content.
- [x] Exported stitch package from a real browser session.
- [x] Native import result JSON from the real browser-exported package.
- [x] Proof validation JSON/Markdown.
- [x] Updated readiness docs only if live proof is actually collected.

## Tranche 3 - Manual Validation Capture Sprint

Goal: gather evidence that deterministic tests cannot honestly replace.

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-profile or clean-machine portable ZIP proof if a suitable environment is available.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Run `goatshot manual-validation summarize --folder <folder>`.
- [ ] Keep live provider/OAuth lanes parked in the summary unless explicitly unparked.

Proof:

- [ ] Filled lane templates under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Manual summary JSON/Markdown.
- [ ] Readiness docs updated only for proof actually collected.

## Completed Tranche 4 - Browser Store Submission Package Builder

Goal: make browser-store submission mechanically ready without actually publishing or claiming review/signing success.

This is the recommended next buildable later module because the browser extension already has the most local scaffolding and does not require OAuth.

- [x] Add a CLI command such as `browser-extension store-package` that creates target-specific submission bundles for Chrome, Edge, and Firefox from the current extension source.
- [x] Validate required store metadata without making publication claims: extension name, version, manifest permissions, native-host disclosure, privacy copy, screenshot checklist, support URL placeholder, and release notes.
- [x] Produce a store package manifest with source hashes, package hash, target browser, generated files, missing manual evidence, and publication non-goals.
- [x] Reuse existing store-readiness copy and proof-manifest validation rather than duplicating policy text.
- [x] Keep actual Chrome Web Store, Edge Add-ons, AMO accounts, signing, review, and publication manual/later.
- [x] Add docs that distinguish local ZIP packaging, store submission package generation, and actual store publication.

Proof:

- [x] Service tests for target validation, missing metadata, package hash, and native-host disclosure.
- [x] CLI smoke artifacts for Chrome, Edge, and Firefox package generation.
- [x] Redaction/privacy checks for generated metadata.
- [x] Standard proof gate.
- [x] `artifacts/tranche-browser-store-package-builder/notes.md`.

## Completed Tranche 5 - Browser Extension Install Planning

Goal: turn the automatic-installation roadmap item into an honest, read-only installation planner without claiming that GoatShot can install browser extensions for the user.

Status: complete and locally proven under `artifacts/tranche-browser-extension-install-planning/`.

- [x] Add focused tests for install-plan generation across Chrome, Edge, and Firefox.
- [x] Add tests for generated store-package artifact detection.
- [x] Add tests for managed-policy blocking and missing source/package blockers.
- [x] Add CLI smoke proof for `browser-extension install-plan --browser all --source browser-extension --store-package-root artifacts\tranche-browser-store-package-builder\store-package-all --json`.
- [x] Add a blocked-policy CLI proof artifact.
- [x] Add or refresh README/spec/browser-extension docs that distinguish install planning from automatic installation.
- [x] Write `artifacts/tranche-browser-extension-install-planning/approval-note.md` naming authority boundaries, privacy/threat risks, proof gate, and non-goals.
- [x] Write `artifacts/tranche-browser-extension-install-planning/notes.md`.
- [x] Update readiness/TODO ledgers only after proof is saved.
- [x] Keep browser-store publication, browser account submission, signing, review, automatic profile mutation, registry/native-host mutation outside explicit commands, and live Host Status proof out of this tranche.

Proof:

- [x] Focused install-plan service tests.
- [x] CLI success JSON/Markdown artifact for all browsers.
- [x] CLI policy-blocked artifact.
- [x] Release build/test, CLI help, diagnostics print, package-release.
- [x] Tranche note with generated file paths and remaining live-browser proof boundary.

## Completed Tranche 6 - Browser Live Fixture Proof Closure

Goal: complete the manual browser proof lane with safe local browser content, now using the package/readiness/install-plan helpers already built.

- [x] Stage `browser-extension/samples/safe-fixture.html`.
- [x] Generate a fresh live-fixture proof folder.
- [x] Generate and review an install plan for the target browser.
- [x] Register/check the user-scope native host only through explicit CLI commands.
- [x] Load the unpacked extension in Edge through the isolated launch helper.
- [x] Capture screenshots for extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff result.
- [x] Run one consented fixture capture with screenshot consent and package export enabled.
- [x] Import the real browser-exported stitch package through `browser-extension live-fixture --payload --stitch-package`, which uses the native receiver.
- [x] Run `browser-extension proof validate --folder <proof-folder>`.
- [x] Keep browser-store publication/review/signing and unattended installation out of scope.

Proof:

- [x] Live browser screenshots with only safe fixture content.
- [x] Exported stitch package from a real browser session.
- [x] Native import result JSON from the real browser-exported package.
- [x] Proof validation JSON/Markdown.

## Tranche 7 - Manual Validation Capture Sprint

Goal: convert the remaining human/device/manual proof into evidence instead of relying on implementation confidence.

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-profile or clean-machine portable ZIP proof if a suitable environment is available.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Run `goatshot manual-validation summarize --folder <folder>`.
- [ ] Keep live provider/OAuth lanes parked in the summary unless explicitly unparked.

Proof:

- [ ] Filled lane templates under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Manual summary JSON/Markdown.
- [ ] Readiness docs updated only for proof actually collected.

## Tranche 8 - Plugin Marketplace Governance Plan

Goal: advance plugin marketplace thinking without introducing automatic background updates, remote execution, accounts, payments, ratings, or ungoverned install behavior.

- [ ] Write a marketplace authority note covering registry trust, package provenance, update governance, policy blocks, operator approval, audit evidence, and rollback.
- [ ] Add a read-only marketplace/index planner if it can reuse existing remote registry validation.
- [ ] Add diagnostics that distinguish local plugin folder, staged remote packages, installed staged packages, available updates, blocked updates, and future hosted marketplace accounts.
- [ ] Add tests proving marketplace planning does not install, trust, enable, allowlist, execute, or auto-update plugins.
- [ ] Keep actual hosted marketplace service, accounts, ratings, payments, remote execution, and background updates later-scope.

Proof:

- [ ] Architecture/governance note.
- [ ] Focused planner/diagnostics tests if code is added.
- [ ] CLI smoke artifact if a planner command is added.
- [ ] Standard proof gate.

## Tranche 9 - Companion Portal Read-Only V0 Decision

Goal: decide whether to implement a minimal read-only companion portal module, and only start with local exported evidence viewing.

- [ ] Re-open the companion portal boundary note and identify the smallest read-only v0.
- [ ] If approved, implement local export/report viewing only: policy summary, diagnostics summary, proof bundle index, upload history summary, and manual validation summary.
- [ ] Keep portal auth, hosted sync, remote policy enforcement, multi-user admin state, cloud storage, and remote commands out of this tranche.
- [ ] Add redaction tests for any exported portal/report payload.
- [ ] Use Product Design review only if a new desktop or browser-facing UI is created.

Proof:

- [ ] Boundary approval note or explicit "not now" note.
- [ ] Focused export/report tests if code is added.
- [ ] Standard proof gate.

## Tranche 10 - Android Live Safe-Device Proof

Goal: collect live Android proof only after safe content is staged, before considering production streaming.

- [ ] Prepare a safe phone screen with no private notifications, accounts, messages, tokens, photos, or contacts.
- [ ] Run live `diagnostics android`.
- [ ] Run live screenshot capture/import.
- [ ] Run bounded screenrecord pull/import.
- [ ] Run guarded preview execution with `--execute`, selected device, safe-content confirmation, frame/byte/timeout caps, and optional contact sheet.
- [ ] Summarize proof through manual-validation tooling.
- [ ] Do not start production Android live streaming until this proof exists and is reviewed.

Proof:

- [ ] Live safe-device JSON/Markdown artifacts.
- [ ] Imported screenshot/video/preview artifacts reviewed for privacy.
- [ ] Manual validation summary update.

## Tranche 11 - Virtual Printer Driver Decision

Goal: decide whether true OS virtual-printer installation is worth accepting admin, signing, installer, and clean-machine constraints.

- [ ] Write a driver decision note covering Windows driver model options, signing/distribution requirements, admin install, clean-machine validation, rollback/uninstall, and support burden.
- [ ] Confirm whether GoatShot should remain file-drop/import only for V1.
- [ ] If approved, start with installer planning and clean-machine proof requirements before driver code.
- [ ] Keep existing print-import file-drop path as the production-safe fallback.

Proof:

- [ ] Decision note.
- [ ] No code unless admin/signing constraints are accepted.
- [ ] If code is later added, require clean-machine/manual proof before readiness claims.

## Tranche 12 - Release Proof Refresh After Each Completed Lane

Goal: keep handoff artifacts aligned with the latest source and evidence.

- [ ] Re-run the standard proof gate after each completed tranche.
- [ ] Refresh `create-release-proof-bundle.ps1` output after any docs/source/readiness evidence changes.
- [ ] Update `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and this plan from saved evidence only.
- [ ] Keep stale bundle references out of readiness claims.

Proof:

- [ ] Fresh release proof bundle.
- [ ] Current portable package path.
- [ ] Notes explaining any skipped manual/hardware/browser/account proof.

## Parked OAuth/Live Account Lane

Do not resume these until a dedicated account tranche is scheduled:

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Standard Definition Of Done

- [ ] Focused tests for changed services, models, CLI behavior, UI models, and policy gates.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy assertions when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, browser packages, Android media, plugin packages, or diagnostics are touched.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` with changed files, proof paths, skipped/manual proof, and remaining risk.

# GoatShot Continue Implementation TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is left in GoatShot without waiting on live OAuth consent screens, refresh-token proof, or real cloud-account uploads. OAuth stays where it is unless a non-OAuth tranche exposes a small compatibility bug.

Use this as the next execution plan after the Settings Plugins passive update surface tranche.

No Git workflow is required right now.

## Short Answer

The only parked account lane is live OAuth/provider proof. It is not the only remaining work. The next useful work is mostly local and buildable: browser proof assistance, manual-validation summarization, Android preview review, virtual-printer setup polish, a portal decision, and a final release-proof refresh.

## Scope Rules

- [x] Keep Google Drive, Dropbox, OneDrive, and future live OAuth consent/account proof parked.
- [x] Keep existing OAuth authorization-code/client/refresh plumbing in place.
- [x] Keep GoatShot as a native WPF/.NET desktop app with CLI support.
- [x] Keep fake-provider, synthetic-media, local-token, fake-device, dry-run, and local diagnostics evidence labeled as local proof only.
- [x] Keep Product Design/WPF screenshot-backed notes for material desktop UI changes.
- [x] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Do Not Pick Up In This Pass

- [ ] Live Google Drive, Dropbox, or OneDrive consent-screen proof.
- [ ] Live refresh-token expiry/recovery proof against real cloud accounts.
- [ ] Real cloud-account upload/delete proof.
- [ ] Browser-store publication, review, signing, or automatic extension installation.
- [ ] Signed/admin OS virtual-printer driver installation.
- [ ] Production Android live streaming beyond bounded preview/import paths.
- [ ] Unattended background plugin updates, hosted marketplace accounts, ratings, payments, or remote plugin execution.
- [ ] Hosted/self-hosted portal implementation unless the portal decision tranche explicitly approves a narrow v0.

## Tranche 1 - Browser Proof Assistant Polish

Goal: make live browser-extension proof repeatable and self-checking without depending on OAuth, browser-store publication, or automatic installation.

Status note, 2026-06-15: complete and locally proven under `artifacts/tranche-browser-proof-assistant/`. `browser-extension live-fixture` now writes `browser-proof-manifest.json` and `browser-proof-validation.md`, and `browser-extension proof validate --folder <proof-folder>` reports missing screenshots/import results/browser metadata/package hashes plus unredacted payload findings. Live browser screenshots and real browser-exported package proof remain manual evidence lanes.

Implementation TODOs:

- [x] Add or extend a browser proof manifest that records browser name/version, extension id, extension source hash, package hash, native-host registration/status, fixture URL, payload path, stitch-package path, import result path, and collected screenshot paths.
- [x] Add CLI validation for a completed browser proof folder.
- [x] Report missing evidence clearly: extension details screenshot, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, browser version, payload, stitch package, import result, and operator notes.
- [x] Add redaction checks for browser payloads and proof manifests.
- [x] Add Chrome and Edge fallback instructions into the generated manual template.
- [x] Keep browser-store submission, signing, and automatic installation out of scope.

Proof TODOs:

- [x] Focused tests for manifest generation, proof-folder validation, missing-evidence output, and redaction checks.
- [x] CLI smoke artifacts for `browser-extension live-fixture` and the new proof validation command.
- [x] Tranche notes under `artifacts/tranche-browser-proof-assistant/notes.md`.

## Tranche 2 - Manual Validation Summary/Validator

Goal: turn manual evidence folders into a useful pass/fail/blocked summary so hardware and accessibility proof can be gathered over time without blocking code work.

Implementation TODOs:

- [ ] Add a `manual-validation validate` or `manual-validation summarize` CLI command for a dated evidence folder.
- [ ] Check that expected lane templates exist: keyboard traversal, Narrator/NVDA, text scaling, high contrast, region drag, multi-monitor capture, multi-monitor recording, long recording, clean-profile portable ZIP, live Android, live browser, and live provider proof.
- [ ] Classify each lane as `pass`, `fail`, `blocked`, `not-run`, or `not-applicable`.
- [ ] Require a short note for `fail` and `blocked` lanes before a summary is considered complete.
- [ ] Include diagnostics bundle presence and redaction status.
- [ ] Do not require OAuth/live-provider lanes to pass while OAuth is parked.

Proof TODOs:

- [ ] Tests for complete, incomplete, blocked, and redaction-warning evidence folders.
- [ ] Sample generated summary under `artifacts/tranche-manual-validation-summary/`.
- [ ] Release build/test/CLI/package gate.
- [ ] Tranche notes under `artifacts/tranche-manual-validation-summary/notes.md`.

## Tranche 3 - Android Preview Review Surface

Goal: make guarded Android preview execution output easy to inspect without claiming production Android streaming.

Implementation TODOs:

- [ ] Add a CLI summary for `capture android-preview --execute` output: device, duration, frame count, total bytes, cap/timeout status, cleanup status, safe-content confirmation, and output folder.
- [ ] Add optional contact-sheet generation from collected PNG frames.
- [ ] Add diagnostics/Settings copy that says Android preview is privacy-gated and production streaming is later-scope.
- [ ] Keep H.264 stdout streaming, FFmpeg remux, scrcpy-style mirroring, and continuous streaming out of scope.

Proof TODOs:

- [ ] Fake ADB tests for summary/contact-sheet paths.
- [ ] CLI artifacts for blocked, dry-run, and fake execution review.
- [ ] WPF screenshot/Product Design note only if Settings UI changes.
- [ ] Tranche notes under `artifacts/tranche-android-preview-review/notes.md`.

## Tranche 4 - Virtual Printer Setup Helper

Goal: improve print-import setup without claiming GoatShot installs a signed OS printer driver.

Implementation TODOs:

- [ ] Add a first-run or CLI helper that creates the watched print-import folder.
- [ ] Write a local setup note that explains Microsoft Print to PDF/file-drop handoff and supported import types.
- [ ] Add diagnostics for folder existence, writeability, watched-folder status, supported extensions, and policy blocks.
- [ ] Add Settings copy only if the current diagnostics surface is not clear enough.
- [ ] Keep signed driver work and admin installer proof later/manual.

Proof TODOs:

- [ ] Tests for folder creation, diagnostics, policy-blocked behavior, and setup-note generation.
- [ ] CLI/diagnostics artifacts under `artifacts/tranche-virtual-printer-setup-helper/`.
- [ ] Tranche notes under `artifacts/tranche-virtual-printer-setup-helper/notes.md`.

## Tranche 5 - Companion Portal Decision

Goal: decide the portal/team path before adding hosted or self-hosted code.

Decision TODOs:

- [ ] Review `artifacts/tranche-companion-portal-planning/companion-portal-boundaries.md`.
- [ ] Review `artifacts/tranche-companion-portal-planning/threat-privacy-checklist.md`.
- [ ] Choose one V1 posture:
  - [ ] no portal for V1
  - [ ] local static evidence/report export only
  - [ ] self-hosted LAN portal v0
  - [ ] hosted portal v0
- [ ] If any v0 is approved, start read-only with policy summaries, audit summaries, diagnostics summaries, and release-proof summaries.
- [ ] Do not sync capture files, secrets, tokens, provider account data, plugin trust decisions, or raw private content.

Proof TODOs:

- [ ] Architecture decision note.
- [ ] Updated threat/privacy checklist if scope changes.
- [ ] Tests and diagnostics only if code is added.
- [ ] Tranche notes under `artifacts/tranche-companion-portal-decision/notes.md`.

## Tranche 6 - Release Proof Refresh

Goal: make the latest source and evidence easy to hand off.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, screenshot/audit notes, and selected tranche notes.
- [ ] Keep portable ZIP as the default release artifact.
- [ ] Keep clean-machine installer proof separate unless a clean profile or VM actually proves it.
- [ ] Update readiness docs only for evidence actually collected.

Proof TODOs:

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Release proof bundle artifact and notes.

## Opportunistic Manual Proof Lanes

These can be done whenever safe content and devices are available, but they should not block the non-OAuth code tranches above.

- [ ] Live browser extension load, Host Status, consent defaults, safe fixture capture, browser-exported stitch package, and native import result.
- [ ] Live keyboard traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA screen-reader pass.
- [ ] Windows text scaling and high-contrast pass.
- [ ] Live region drag path with safe desktop content.
- [ ] Multi-monitor capture and recording with safe desktop content.
- [ ] Long recording stability with safe microphone/system-audio/webcam content.
- [ ] Clean-profile or clean-machine portable ZIP proof.
- [ ] Live Android screenshot/video/preview proof with staged safe phone content.

## Standard Done Gate For Code Tranches

- [ ] Focused tests for changed services, models, CLI behavior, UI models, and policy gates.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy checks when prompts, transcripts, OCR text, URLs, tokens, logs, settings, browser payloads, Android media, plugin packages, or diagnostics are touched.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help`
- [ ] CLI `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` with changed files, proof paths, skipped/manual proof, and remaining risk.

## Recommended Execution Order

1. Browser Proof Assistant Polish.
2. Manual Validation Summary/Validator.
3. Android Preview Review Surface.
4. Virtual Printer Setup Helper.
5. Companion Portal Decision.
6. Release Proof Refresh.

After this plan is complete, the remaining items should be honest manual or later-scope lanes: live OAuth/provider proof, live browser-store publication/signing/automatic install, signed virtual-printer driver installation, production Android streaming, automatic plugin marketplace behavior, and any approved portal implementation beyond a decision note.

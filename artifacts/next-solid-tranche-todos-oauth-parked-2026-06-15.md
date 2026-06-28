# GoatShot Next Solid Tranche TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing the remaining GoatShot work without blocking on live OAuth consent screens, refresh-token validation, or real cloud-account proof. OAuth stays where it is for now. The next work should favor shippable local functionality, deterministic tests, safe artifacts, diagnostics, and honest manual-proof boundaries.

No Git workflow is required right now.

## Current Decision

- [x] Keep Google Drive, Dropbox, OneDrive, and future OAuth/live-account proof parked.
- [x] Keep the existing OAuth authorization-code/client/refresh plumbing in place unless a non-OAuth task exposes a small compatibility bug.
- [x] Keep GoatShot as a native WPF/.NET desktop app with CLI support.
- [x] Treat fake-provider, synthetic-media, dry-run, fake-device, local-token, and local diagnostics evidence as local proof only.
- [x] Use Product Design/WPF screenshot-backed notes only for material desktop UI changes; CLI/service-only tranches do not need a new frontend audit.
- [x] End every implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Current Truth

- [x] The local V1 candidate is broadly implemented across capture, recording, editor/privacy, OCR, AI/document workflow, workflow automation, provider adapters, upload queue/history, diagnostics, policy, plugins, browser-extension scaffolding, Android import/planning, virtual-printer file-drop import, and portable packaging.
- [x] Browser proof assistant work is locally proven under `artifacts/tranche-browser-proof-assistant/`; Edge live browser extension load, Host Status screenshot, real browser package export, and native import proof are saved under `artifacts/tranche-browser-live-fixture-proof-closure/`. Chrome/Firefox live proof remains manual if needed.
- [x] Manual validation summary/validator tooling is complete and locally proven under `artifacts/tranche-manual-validation-summary/`.
- [x] Android screenshot/video import and guarded preview execution are implemented with fake-process proof; live safe-phone proof and production streaming remain manual/later-scope.
- [x] Virtual printer file-drop import and driver-feasibility diagnostics are implemented; signed/admin OS printer driver installation remains later/manual.
- [x] Companion portal boundaries are documented; portal implementation remains later-scope unless a narrow v0 is explicitly selected.

## Completed Tranche 0 - Close Manual Validation Summary

Goal: finish the already-started manual-validation summary/validator tranche so later hardware/accessibility proof has a reliable summary gate.

Implementation TODOs:

- [x] Confirm `goatshot manual-validation summarize --folder <folder> [--json]` help and behavior from the latest build.
- [x] Write `artifacts/tranche-manual-validation-summary/notes.md` with changed files, proof paths, and remaining manual boundaries.
- [x] Refresh `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` so they no longer list the validator as future work.
- [x] Keep live provider/OAuth evidence optional and parked in summary results.

Proof TODOs:

- [x] Focused manual-validation tests.
- [x] Full Release build and test.
- [x] CLI `--help` and `diagnostics print`.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`.
- [x] Sample incomplete/redaction-warning summary artifacts under `artifacts/tranche-manual-validation-summary/`.

## Completed Tranche 1 - Android Preview Review Surface

Goal: make guarded Android preview execution output easy to review without claiming production Android streaming.

Implementation TODOs:

- [x] Add a CLI summary for `capture android-preview --execute` output: device id, mode, duration, frame count, total bytes, timeout/cap status, cleanup status, safe-content confirmation, and output folder.
- [x] Add optional contact-sheet generation from collected PNG frames, using bounded frame count and deterministic ordering.
- [x] Add diagnostics copy that distinguishes screenshot import, bounded `screenrecord` import, preview polling, live safe-device proof, and later production streaming.
- [x] Preserve existing refusal behavior for missing `--safe-content-confirmed`, missing selected device, H.264 stdout execution, timeout, byte cap, and disconnect cleanup.
- [x] Keep FFmpeg remux, scrcpy-style mirroring, H.264 stdout execution, and continuous production streaming out of scope.

Proof TODOs:

- [x] Fake ADB tests for summary and contact-sheet paths.
- [x] CLI artifacts for blocked, dry-run, fake execution, and contact-sheet review.
- [x] Product Design/WPF note only if Settings UI changes.
- [x] Standard proof gate and `artifacts/tranche-android-preview-review/notes.md`.

## Completed Tranche 2 - Virtual Printer Setup Helper

Goal: improve local print-import setup without claiming GoatShot installs a signed Windows printer driver.

Implementation TODOs:

- [x] Add a CLI helper such as `print-import setup` that creates or verifies the watched print-import folder.
- [x] Generate a local setup note explaining Microsoft Print to PDF/file-drop handoff, supported import types, watched-folder behavior, and the admin-scoped driver boundary.
- [x] Extend diagnostics for folder existence, writeability, watched-folder state, supported extensions, policy blocks, installed-printer hints, and current elevation.
- [x] Add Settings copy only if the current diagnostics surface remains unclear after CLI/setup-note work.
- [x] Keep signed driver work, driver packaging, admin install, and clean-machine printer proof in later/manual lanes.

Proof TODOs:

- [x] Tests for folder creation, diagnostics, policy-blocked behavior, and setup-note generation.
- [x] CLI/diagnostics artifacts under `artifacts/tranche-virtual-printer-setup-helper/`.
- [x] Product Design/WPF note only if Settings UI changes.
- [x] Standard proof gate and `artifacts/tranche-virtual-printer-setup-helper/notes.md`.

## Tranche 3 - Release Proof Refresh

Goal: make the handoff artifact match the current source and current evidence.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, selected screenshot/audit notes, and selected tranche notes.
- [ ] Keep portable ZIP as the default release proof.
- [ ] Keep clean-machine installer proof separate unless a clean profile or VM actually proves it.
- [ ] Update readiness docs only for evidence actually collected.

Proof TODOs:

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help`
- [ ] CLI `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Release proof bundle artifact and notes.

## Tranche 4 - Manual Proof Capture

Goal: gather evidence that cannot be honestly produced by deterministic tests.

Manual TODOs:

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content.
- [ ] Complete clean-profile or clean-machine portable ZIP proof.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Complete live browser extension fixture proof only with safe browser content.
- [ ] Run `goatshot manual-validation summarize --folder <folder>` after notes are filled in.

Parked in this lane:

- [ ] Live provider/OAuth proof remains parked unless explicitly unparked.

## Tranche 5 - Later Module Decision

Goal: choose one larger module before adding more architecture surface.

Decision TODOs:

- [ ] Pick exactly one next module, or choose no later module for V1.
- [ ] Write a short approval note with authority boundaries, privacy/threat risks, proof gate, and non-goals.
- [ ] Start any approved module read-only or local-only first.
- [ ] Keep desktop policy, local consent, provider-account boundaries, redaction rules, plugin trust, and OAuth parking non-bypassable.

Candidate modules:

- [ ] Browser-store publication/review/signing and automatic extension installation.
- [ ] Production Android live streaming beyond bounded preview/import paths.
- [ ] Signed/admin virtual-printer driver installation and clean-machine printer proof.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted plugin marketplace behavior.
- [ ] Hosted/self-hosted companion portal and remote/multi-user admin sync.

Recommended order if no preference is given:

1. Browser-store path, because the extension has the most local scaffolding.
2. Plugin marketplace planning, with update/install/trust/enable/allowlist/execution still operator-gated.
3. Companion portal v0, read-only policy/audit/report viewing first.
4. Production Android streaming after safe-device proof exists.
5. True virtual-printer driver after signing/admin installer constraints are accepted.

## Parked OAuth/Live Account Lane

Do not pick these up until a dedicated account tranche is scheduled:

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Standard Proof Gate

- [ ] Focused tests for changed services, models, CLI behavior, UI models, and policy gates.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy assertions when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, browser packages, Android media, plugin packages, or diagnostics are touched.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` with changed files, proof paths, skipped/manual proof, and remaining risk.

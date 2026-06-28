# GoatShot What-Is-Left Implementation TODOs - OAuth Parked

Date: 2026-06-15

Purpose: provide the next execution plan after the current V1/non-OAuth buildout work. Keep OAuth consent, live cloud-account proof, refresh-token validation, and live provider remote-delete behavior parked. Move on to locally buildable work and manually provable product proof so GoatShot can keep progressing without account setup blocking the roadmap.

No Git workflow is required right now.

## Decisions

- [x] Keep Google Drive, Dropbox, OneDrive, and future OAuth/live-account proof parked.
- [x] Keep existing OAuth plumbing in place unless a non-OAuth task exposes a small compatibility bug.
- [x] Keep GoatShot as a native WPF/.NET desktop app with CLI support.
- [x] Use Product Design/WPF screenshot-backed notes only for material desktop UI changes.
- [x] Treat fake-device, synthetic-media, local-token, fake-provider, dry-run, and local diagnostics evidence as local proof only.
- [x] End every implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Current Baseline

- [x] Native WPF desktop app, CLI, MSTest coverage, diagnostics, manual validation harness, manual validation summary tooling, Product Design audit artifacts, and portable package output exist.
- [x] Core V1 capture, scrolling capture, recording, editor/privacy, OCR, video tooling, AI/document workflow, workflow automation, upload queue/history, provider adapters, local admin policy, plugins, browser extension scaffolding, Android import/planning, and virtual-printer file-drop import are broadly implemented and locally proven.
- [x] Remaining gaps are mostly proof/packaging/review surfaces, live hardware/browser/device validation, and explicit later-module choices.

## Completed Tranche 1 - Android Preview Review Surface

Goal: make guarded Android preview execution easy to review without claiming production Android streaming.

- [x] Add a CLI execution summary for `capture android-preview --execute`: device id, mode, duration, frame count, total bytes, timeout status, byte-cap status, cleanup status, safe-content confirmation, output folder, manifest path, and contact-sheet path when present.
- [x] Add optional contact-sheet generation from collected PNG frames, with bounded max frame count and deterministic frame ordering.
- [x] Add summary/contact-sheet fields to the execution manifest/result model.
- [x] Update diagnostics copy to distinguish Android screenshot import, bounded `screenrecord` import, bounded preview polling, live safe-device proof, and later production streaming.
- [x] Preserve existing refusal and cleanup behavior for missing `--safe-content-confirmed`, missing selected device, H.264 stdout execution, timeout, byte cap, and disconnect cleanup.
- [x] Keep FFmpeg remux, scrcpy mirroring, H.264 stdout execution, and continuous production streaming out of scope.
- [x] Proof: fake ADB tests, CLI blocked/dry-run/fake-execute artifacts, focused Android tests, standard proof gate, and `artifacts/tranche-android-preview-review/notes.md`.

## Completed Tranche 2 - Virtual Printer Setup Helper

Goal: improve local print-import setup without claiming GoatShot installs a signed/admin Windows printer driver.

- [x] Add a CLI helper such as `print-import setup` to create or verify the watched print-import folder.
- [x] Generate a local setup note with Microsoft Print to PDF/file-drop instructions, supported import types, watched-folder behavior, and the admin-scoped driver boundary.
- [x] Extend diagnostics for drop-folder existence, writeability, watched-folder state, supported extensions, managed-policy blocks, installed-printer hints, and elevation.
- [x] Add Settings copy only if CLI/diagnostics still leave the setup unclear.
- [x] Keep signed driver work, driver packaging, admin install, and clean-machine printer proof in later/manual lanes.
- [x] Proof: folder-creation tests, diagnostics tests, policy-block tests, setup-note artifacts, standard proof gate, and `artifacts/tranche-virtual-printer-setup-helper/notes.md`.

## Tranche 3 - Browser Live Fixture Proof Closure

Goal: finish the local browser-extension proof lane when safe browser content is staged.

- [ ] Use the existing live-fixture helper to create a proof folder for Chrome or Edge.
- [ ] Load the unpacked extension from local/package output and capture a Host Status screenshot.
- [ ] Export a safe stitch package from the browser fixture.
- [ ] Import the downloaded package through GoatShot's native receiver.
- [ ] Run `browser-extension proof validate --folder <proof-folder>`.
- [ ] Keep browser-store publication, review, signing, and automatic installation out of scope.
- [ ] Proof: live fixture screenshots, exported stitch package, import result JSON, proof validation output, manual notes, and updated readiness docs.

## Tranche 4 - Manual Validation Capture Sprint

Goal: gather human/device/accessibility evidence that tests cannot honestly replace.

- [ ] Run `manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content.
- [ ] Complete clean-profile or clean-machine portable ZIP proof if a suitable environment is available.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Run `manual-validation summarize --folder <folder>` and keep OAuth/live-provider lanes parked.
- [ ] Proof: completed templates, screenshots where appropriate, diagnostics bundle, summary JSON/Markdown, and updated readiness docs.

## Tranche 5 - Release Proof Refresh

Goal: make the handoff evidence match the latest source and latest manual/local proof.

- [ ] Re-run the full standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, selected screenshots/audit notes, selected tranche notes, and manual validation summaries.
- [ ] Keep portable ZIP as the default proof artifact.
- [ ] Keep installer/clean-machine proof separate unless it is actually run.
- [ ] Update `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` with only evidence actually collected.

## Tranche 6 - Pick One Later Module

Goal: avoid spreading the app across multiple large expansions at once.

- [ ] Pick exactly one larger module, or explicitly decide that no later module is needed for V1.
- [ ] Write an approval note covering authority boundaries, privacy/threat risks, proof gate, and non-goals.
- [ ] Start any approved module read-only, local-only, or proof-helper-only before production side effects.
- [ ] Keep desktop policy, local consent, provider-account boundaries, redaction rules, plugin trust gates, and OAuth parking non-bypassable.

Recommended order if no preference is given:

1. Browser-store publication/review/signing and automatic installation planning, because the extension has the most local scaffolding.
2. Plugin marketplace planning, keeping update/install/trust/enable/allowlist/execution operator-gated.
3. Companion portal v0 planning, starting with read-only policy/audit/report viewing.
4. Production Android streaming only after safe-device proof exists.
5. True virtual-printer driver work only after signing/admin installer constraints are accepted.

## Parked OAuth/Live Account Lane

Do not resume these until a dedicated account tranche is scheduled:

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

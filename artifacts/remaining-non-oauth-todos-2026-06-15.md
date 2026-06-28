# GoatShot Remaining Non-OAuth TODO Plan

Date: 2026-06-15

Purpose: provide the current, consolidated TODO plan for continuing GoatShot after the local V1 buildout, while keeping OAuth/live-account proof parked. This replaces the older rolling queues as the shortest operational checklist for the next work.

No Git workflow is required right now.

Current execution-shaped TODO plan: `artifacts/current-non-oauth-buildout-todo-plan-2026-06-15.md`.

## Current Decision

- [x] Keep Google Drive, Dropbox, OneDrive, and future live OAuth consent/account proof parked.
- [x] Keep GoatShot as a native WPF/.NET desktop app.
- [x] Treat local/fake-provider proof, synthetic media, browser package validation, Android dry-runs, and local token setup as local proof only.
- [x] Use Product Design/WPF screenshot-backed audit notes for material desktop UI changes.
- [x] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Recommended Order

1. Use `artifacts/current-non-oauth-buildout-todo-plan-2026-06-15.md` as the current execution checklist.
2. Close or sharply bound browser extension live-fixture proof with safe local browser content; add helper/verifier code if manual browser setup remains too brittle.
3. Run a manual validation/proof pass for accessibility, hardware, packaging, and safe-device lanes.
4. Refresh the release proof bundle and readiness docs from the evidence actually collected.
5. Move to one buildable later module at a time after live browser/manual/release proof is refreshed. Android preview execution, browser-store readiness checklist/copy generation, passive plugin update summaries, the Settings Plugins passive update surface, and explicit plugin update apply are now complete; browser publication/review/automatic installation and unattended plugin updates/hosted marketplaces remain later/manual proof lanes.

## Completed Tranche 1 - Finish Provider Setup UX Cleanup

Goal: make Settings provider setup readable and honest without reopening OAuth work.

Implementation TODOs:

- [x] Finish the provider readiness summary strip in Settings.
- [x] Group providers into ready, needs setup, policy-blocked, implemented OAuth/live-proof-pending, and roadmap states.
- [x] Keep OAuth providers visible as local-token/configurable but live-proof-pending.
- [x] Refresh the summary after provider save/clear actions and Settings saves.
- [x] Add focused tests for provider setup summary counts, details, policy-block priority, OAuth-pending copy, and secret redaction.
- [x] Add or update WPF screenshot-backed audit notes for the changed Settings Sharing/provider flow.
- [x] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` after proof passes.

Proof TODOs:

- [x] Focused provider setup summary tests.
- [x] Settings Sharing screenshot/render artifact.
- [x] Product Design/WPF audit note under `artifacts/product-design-audit/2026-06-15/provider-setup-ux/`.
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `--help`, `diagnostics print`, and provider diagnostics smoke.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`
- [x] `artifacts/tranche-provider-setup-ux/notes.md`.

## Tranche 2 - Browser Extension Live Fixture Proof

Goal: prove the already-built browser extension path in a real browser using only safe local fixture content.

Status note, 2026-06-15: a safe Chrome attempt is documented under `artifacts/manual-validation/2026-06-15/browser-extension-live-fixture/`. Chrome Developer Mode screenshots and desktop diagnostics were captured, but Chrome did not register the unpacked extension through automation and Playwright could not complete the native Load unpacked folder picker. The `browser-extension live-fixture` helper/verifier is now locally proven under `artifacts/tranche-browser-live-fixture-helper/`; live extension id, browser-side native-host Host Status, and real browser package export proof remain open.

Implementation TODOs:

- [ ] Use `browser-extension/samples/safe-fixture.html` as the live target unless another safe page is explicitly staged.
- [ ] Load the unpacked extension in Chrome or Edge.
- [ ] Register the user-scope native host with the existing CLI command.
- [ ] Capture screenshots for extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff result.
- [ ] Run one consented fixture capture with screenshot consent and package export enabled.
- [ ] Import the downloaded stitch package through `goatshot browser-extension receive --stitch-package`.
- [x] Add a helper/verifier command that generates proof notes/commands and verifies a downloaded stitch package through the native receiver when payload/package paths are supplied.
- [ ] Save live browser screenshots, redacted payloads, package output, browser version, extension id, and notes under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/`.
- [ ] Keep browser-store publication and automatic extension installation out of scope for this tranche.

Proof TODOs:

- [ ] Live browser screenshots with only safe fixture content.
- [x] Redacted payload/package/import artifacts for the synthetic helper proof under `artifacts/tranche-browser-live-fixture-helper/proof/`.
- [ ] Native import result artifact from a real live browser-exported package.
- [ ] Updated readiness docs only if live proof is actually collected.

## Tranche 3 - Manual Validation Proof Pass

Goal: gather human/device evidence that deterministic tests cannot honestly produce.

Implementation TODOs:

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-machine portable ZIP proof in a clean profile or VM.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Keep live provider/OAuth proof parked unless explicitly unparked.

Proof TODOs:

- [ ] Lane notes under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Manual summary with pass/fail/blocked per lane.
- [ ] Readiness docs updated only for proof actually collected.

## Tranche 4 - Release Proof Refresh

Goal: make the handoff artifact match the current source and collected evidence.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, screenshots/audit notes, and selected tranche notes.
- [ ] Keep portable ZIP as the default release proof.
- [ ] Do not claim clean-machine installer proof unless Tranche 3 actually completes it.
- [ ] Update the readiness summary and current TODO ledgers to separate implemented, locally proven, manually verified, OAuth parked, and later-scope work.

Proof TODOs:

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help`
- [ ] CLI `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Release proof bundle artifact.

## Tranche 5 - Later Module Decision

Goal: choose one larger module only after the local V1 proof is stable.

Candidate modules:

- [ ] Browser-store publication and automatic extension installation.
- [ ] Production Android live streaming beyond guarded screencap preview execution and bounded `screenrecord` import.
- [ ] Signed/admin virtual-printer driver installation and clean-machine printer proof.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted plugin marketplace behavior beyond the proven passive CLI/Settings summaries and explicit update apply flow.
- [ ] Hosted/self-hosted companion portal and remote/multi-user admin sync.

Decision TODOs:

- [ ] Pick exactly one module before implementation starts.
- [ ] Write an approval note naming the module, threat/privacy boundary, proof gate, and non-goals.
- [ ] Keep desktop policy, local consent, provider account boundaries, redaction rules, plugin trust, and OAuth parking non-bypassable.
- [ ] Require separate tests, diagnostics, deployment notes, and proof artifacts before any implementation claim.

Recommended later-module order if no preference is given:

1. Browser-store/readiness path, because the extension already has the most local scaffolding.
2. Plugin marketplace planning, but only if automatic staging/install/trust/enable/allowlist/execution stays disabled.
3. Companion portal v0, starting read-only with policy templates and audit summaries.
4. Production Android live streaming after safe-device proof exists.
5. True virtual-printer driver after signing/admin installer constraints are accepted.

## Parked OAuth/Live Account Lane

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Standard Proof Gate

- [ ] Focused tests for changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy assertions when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, or plugin packages are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.

# GoatShot Current Non-OAuth Buildout TODO Plan

Date: 2026-06-15

Purpose: continue building GoatShot from the current native WPF/.NET state while keeping live OAuth consent/account work parked. This is the execution-shaped TODO queue to use next; it favors buildable local work and treats manual/browser/device proof as evidence lanes, not reasons to stop all other implementation.

No Git workflow is required right now.

## Scope Rules

- [x] Keep Google Drive, Dropbox, OneDrive, and future live OAuth consent/account proof parked.
- [x] Keep the existing OAuth authorization-code/client/refresh plumbing in place unless a non-OAuth task exposes a small compatibility bug.
- [x] Keep GoatShot a native WPF/.NET desktop app with CLI support; do not introduce a web stack for the desktop product.
- [x] Keep fake-provider, synthetic-media, local-token, dry-run, and local diagnostics evidence labeled as local proof only.
- [x] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.
- [x] Refresh `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` when readiness status changes.

## Current Baseline

- [x] Local V1 candidate is broadly implemented and locally proven across capture, recording, editor/privacy tools, OCR, AI/document workflow, workflow automation, provider adapters, queue/history, diagnostics, admin policy, plugins, browser-extension scaffolding, Android import/planning, virtual-printer file-drop import, and portable packaging.
- [x] Provider setup UX cleanup is complete and locally proven under `artifacts/tranche-provider-setup-ux/`.
- [x] Browser extension desktop-side diagnostics, native-host registration commands, local packaging, popup/options UX, stitch planning, and package import are implemented and locally proven.
- [x] Browser extension live-fixture helper/verifier is locally proven under `artifacts/tranche-browser-live-fixture-helper/`; it generates proof notes/commands/server helper files, verifies downloaded stitch packages through the native bridge receiver, and the portable package includes `GoatShot.Cli.exe` for native-host/helper commands.
- [x] Browser extension store-readiness checklist/copy generation is locally proven under `artifacts/tranche-browser-store-readiness/`; it validates the current source for local/Chrome/Edge/Firefox target planning, generates permission rationale, privacy/data-use copy, screenshot checklist, and JSON status, while leaving publication/review/manual install proof open.
- [ ] Live browser extension fixture proof is still open. A Playwright/Chrome attempt captured evidence under `artifacts/manual-validation/2026-06-15/browser-extension-live-fixture/`, but Chrome unpacked-extension loading could not be completed through automation.
- [ ] Manual accessibility, hardware, clean-machine, live Android device, and long-recording proof are still open.
- [ ] Later modules need explicit selection before implementation: browser-store publication/automatic installation, production Android streaming, signed virtual-printer driver, plugin marketplace/automatic updates, or hosted/self-hosted companion portal.

## Tranche 1 - Browser Live Fixture Closure

Goal: close or sharply bound the browser-extension live proof gap with safe fixture content, without browser-store or automatic-installation claims.

Implementation TODOs:

- [x] Add a small browser-extension live-fixture helper command if the current manual steps remain too brittle. The helper should generate a dated proof folder, print exact Chrome/Edge load-unpacked steps, start or describe the safe local fixture URL, and emit the native-host install command once an extension id is supplied.
- [x] Add a verifier path that checks a downloaded `GoatShot/<correlationId>/` stitch package, validates the package manifest, imports it through the existing native receiver, and writes redacted result JSON into the proof folder.
- [ ] Prefer Chrome first, then Edge if Chrome still refuses automated or operator-driven unpacked loading.
- [ ] Capture loaded extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, last handoff result, and browser version.
- [ ] Keep browser-store publication and automatic extension installation out of scope.

Proof TODOs:

- [ ] Live safe-fixture screenshots under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/`.
- [x] Redacted payload/package/import result artifacts for the synthetic browser-download package under `artifacts/tranche-browser-live-fixture-helper/proof/`.
- [ ] Native-host status artifact with the real extension id redacted if needed.
- [x] Tranche note if new helper/verifier code is added.
- [ ] Readiness docs updated only if live proof is actually collected.

## Tranche 2 - Manual Validation Proof Pass

Goal: gather human/device evidence that deterministic tests cannot honestly produce, while leaving OAuth/live-provider proof parked.

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

- [ ] Lane notes and artifacts under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Summary file with pass/fail/blocked per lane.
- [ ] Readiness docs updated only for proof actually collected.

## Tranche 3 - Release Proof Refresh

Goal: make the handoff artifact match the current source and collected evidence.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, screenshots/audit notes, and selected tranche notes.
- [ ] Keep portable ZIP as the default release proof.
- [ ] Keep compiled installer and clean-machine proof separate unless Inno Setup and a clean profile/VM are actually used.
- [ ] Update readiness docs to separate implemented, locally proven, manually verified, OAuth parked, and later-scope work.

Proof TODOs:

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help`
- [ ] CLI `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Release proof bundle artifact.

## Tranche 4 - Browser Store Readiness, No Publication Yet

Goal: turn the extension into a store-ready package track without publishing or claiming automatic install.

Implementation TODOs:

- [x] Add target-specific extension package validation for local, Chrome Web Store, Edge Add-ons, and Firefox where manifest differences matter.
- [x] Generate permission rationale and privacy copy from the current manifest and bridge contract.
- [x] Add store asset/checklist output: screenshots needed, extension description, data-use statements, native-host dependency note, support URL placeholder, and review limitations.
- [x] Keep native-host install proof separate from store submission proof.
- [x] Add tests for package-target validation and permission-copy generation.

Proof TODOs:

- [x] Focused extension package/store-readiness tests.
- [x] Target package artifacts under `artifacts/tranche-browser-store-readiness/`.
- [x] Redacted checklist/copy outputs.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-browser-store-readiness/notes.md`.

## Tranche 5 - Plugin Update Notifier, Still Operator-Gated

Goal: make remote plugin updates easier to inspect without automatic background install or execution.

Implementation TODOs:

- [x] Add a concise update summary model for active plugin version, registry version, compatibility, staged package state, and policy blocks.
- [x] Add CLI output for update summaries that clearly separates available, staged, installed, blocked, and incompatible states.
- [x] Leave WPF Settings/diagnostics unchanged for this tranche; the CLI summary reuses existing plugin/provider boundaries without adding desktop clutter.
- [x] Keep install, trust, enable, allowlist, and run as separate explicit operator actions.
- [x] Add tests for blocked updates, incompatible updates, staged-but-not-installed updates, and redacted registry/package metadata.

Proof TODOs:

- [x] Focused plugin update summary tests.
- [x] CLI update summary artifacts with fake registry roots.
- [x] WPF/Product Design screenshot note only if desktop UI changes; no desktop UI changed in this tranche.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-plugin-update-notifier/notes.md`.

## Tranche 6 - Android Preview Execution Gate

Goal: decide whether the dry-run Android preview planner should become a tightly bounded opt-in execution path.

Implementation TODOs:

- [x] Add an approval note before coding that chooses `screencap` polling, H.264 stdout, or no execution.
- [x] If approved, add `capture android-preview --execute` only behind explicit safe-content confirmation, short duration caps, byte caps, timeout caps, selected-device requirements, and cleanup.
- [x] Keep production Android live streaming and scrcpy-style continuous mirroring later-scope unless separately approved.
- [x] Add fake ADB process tests for start, frames/chunks, disconnect, timeout, byte cap, cleanup, and refusal without confirmation.

Proof TODOs:

- [x] Approval note under `artifacts/tranche-android-preview-execution/`.
- [x] Fake ADB tests and CLI dry-run/blocked/execute-plan artifacts.
- [x] Live device proof only if safe phone content is staged; no safe phone content was staged for this local fake-ADB tranche.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-android-preview-execution/notes.md`.

## Tranche 7 - Companion Portal Or Team Sync Decision

Goal: choose the portal path before any hosted/self-hosted implementation begins.

Decision TODOs:

- [ ] Choose one: no portal for V1, local static report export, self-hosted LAN portal, or hosted portal.
- [ ] Write an approval note naming authority boundaries, sync categories, consent rules, policy precedence, data retention, and threat/privacy risks.
- [ ] If implementation is approved, start with read-only policy/audit/report viewing before any remote write, sync, or admin enforcement.
- [ ] Keep desktop deny-wins policy and local consent non-bypassable.

Proof TODOs:

- [ ] Architecture approval note.
- [ ] Threat/privacy checklist update.
- [ ] Focused tests and diagnostics if code is added.
- [ ] Separate tranche notes before making implementation claims.

## Parked OAuth/Live Account Lane

Do not pick these up until a dedicated account tranche is explicitly scheduled:

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Standard Proof Gate

- [ ] Focused tests for changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy assertions when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, browser packages, Android media, plugin packages, or diagnostics are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.

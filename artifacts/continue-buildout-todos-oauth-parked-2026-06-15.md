# GoatShot Continue Buildout TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is still left without blocking on Google Drive, Dropbox, OneDrive, or other live OAuth consent/account proof. This plan starts from the current workspace state after the remote-plugin staged activation tranche restored the Release build and added a guarded `plugins install-staged` path.

No Git workflow is required right now.

Current consolidated remaining TODO plan: `artifacts/remaining-non-oauth-todos-2026-06-15.md`.

## Scope Rules

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Keep OAuth authorization-code/client/refresh plumbing where it is unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not claim live consent, live refresh-token recovery, live cloud upload, live remote delete, browser-store publication, clean-machine installer readiness, full accessibility compliance, long-run recording stability, or live Android/browser proof without fresh evidence.
- [ ] Prefer locally provable work: MSTest, fake HTTP/process/ADB fixtures, safe synthetic media, extension JS syntax checks, CLI smoke output, diagnostics redaction checks, WPF render screenshots for changed desktop UI, and portable package output.
- [ ] Use Product Design/WPF audit notes for any material desktop UI changes. Use Figma only if a redesign exploration is genuinely needed.
- [ ] End every implementation tranche with `artifacts/tranche-<name>/notes.md`.
- [ ] Refresh `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` when readiness status changes.

## Immediate State

- [x] Core local V1 candidate is broadly implemented and locally proven across capture, recording, editor/privacy tools, OCR, AI/document workflow, workflow automation, provider adapters, queue/history, browser extension scaffolding, Android import/planning, virtual-printer file-drop import, plugin SDK/execution/staging, local admin policy, diagnostics, and packaging.
- [x] OAuth/live account proof is parked by decision.
- [x] Current build was restored after `RemotePluginPackageService` references to `RemotePluginActiveInstallResult` were completed with DTOs and tests.
- [x] The interrupted plugin work is stabilized enough to move on after proof artifacts are recorded.

## Tranche 0 - Restore Build And Finish Staged Plugin Activation

Goal: complete the interrupted active plugin install path from staged remote packages while preserving the existing trust model.

- [x] Add missing `RemotePluginActiveInstallResult` and `RemotePluginInstallManifest` DTOs.
- [x] Verify staged package install copies a reviewed package into the local plugin root without trusting, enabling, allowlisting, or executing plugin code.
- [x] Add CLI command `plugins install-staged <plugin-id> [--version VERSION] [--replace] [--json]`.
- [x] After a successful install, clear inherited trust/enablement/allowlist metadata for that plugin.
- [x] Require `--replace` before overwriting an existing active plugin folder.
- [x] Support nested package roots with one packaged `plugin.json`.
- [x] Add tests for successful install, missing staged package, existing active plugin without `--replace`, replace behavior, nested package roots, and blocked dry-run after install.
- [x] Generate proof under `artifacts/tranche-plugin-active-install/`.
- [x] Update docs and readiness ledgers so "active staged install" is locally proven, while unattended updates and hosted marketplace behavior remain later-scope.

Proof:

- [ ] `dotnet test .\GoatShot.slnx -c Release --filter RemotePluginPackageServiceTests`
- [ ] CLI stage/install/list/dry-run-blocked artifacts with isolated local roots.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help` and `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-plugin-active-install/notes.md`

## Completed Tranche 1 - Provider Setup UX Cleanup

Goal: improve the dense Settings provider setup surface without reopening OAuth consent work.

- [x] Use the current Product Design/WPF evidence sweep and new Settings screenshots to identify the smallest provider setup improvements.
- [x] Add provider-specific readiness panels or grouped summaries for configured, missing, policy-blocked, fake-proof, and live-proof-pending states.
- [x] Keep OAuth providers visibly parked/live-proof-pending, not hidden or implied complete.
- [x] Add keyboard/focus model tests for the changed Settings/provider surface where practical.
- [x] Add diagnostics links or commands from the UI model for non-OAuth providers and queue/history recovery.
- [x] Save WPF render screenshots and a short audit note under a new product-design audit subfolder if the UI changes materially.

Proof:

- [x] Focused Settings/provider UI model tests.
- [x] Product Design/WPF audit note or render screenshots.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-provider-setup-ux/notes.md`

## Tranche 2 - Browser Live Fixture Proof Closure

Goal: close the local browser extension proof gap with safe fixture evidence, without store publication or automatic installation claims.

- [ ] Use only `browser-extension/samples/safe-fixture.html` unless another safe page is staged.
- [ ] Load the unpacked extension in Chrome or Edge.
- [ ] Register the native host through the existing user-scope command.
- [ ] Capture popup/options consent states, native-host status, selected-element mode, package-export toggle, and handoff result.
- [ ] Run a consented fixture capture and import the downloaded stitch package through the CLI/native receiver.
- [ ] Save screenshots, redacted payloads, imported package output, and notes under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/`.
- [ ] Keep browser-store publication and automatic extension installation later-scope.

Proof:

- [ ] Live fixture screenshots with safe content.
- [ ] Redacted payload/package artifacts.
- [ ] Native import result artifact.
- [ ] Updated readiness docs only for proof actually collected.

## Tranche 3 - Manual Proof Pass

Goal: use the existing harness for proof deterministic tests cannot honestly provide.

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard Tab traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-machine portable ZIP proof in a clean profile or VM.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Keep live provider/OAuth proof parked unless explicitly unparked.

Proof:

- [ ] Lane notes and artifacts under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Summary file with pass/fail/blocked per lane.
- [ ] Readiness docs updated only for proof actually collected.

## Tranche 4 - Later Module Decision Pack

Goal: choose the next real implementation module after local V1 proof is stable.

- [ ] Review `artifacts/tranche-companion-portal-planning/` and decide whether portal v0 is docs-only, self-hosted local/LAN, or hosted service.
- [ ] Decide whether signed/admin virtual-printer driver work is worth scheduling, with installer/signing/clean-machine constraints explicit.
- [ ] Decide whether production Android live streaming should move beyond the dry-run planner, with safe-content consent and byte/duration bounds.
- [ ] Decide whether hosted plugin marketplace and unattended plugin updates belong in V1.x or post-V1.
- [ ] Do not start any of these until the approval note names exactly one module and its proof gate.

Proof if a module is approved:

- [ ] Architecture approval note.
- [ ] Threat/privacy checklist update.
- [ ] Focused tests, diagnostics, deployment notes, and separate proof artifacts before any implementation claim.

## Parked OAuth/Live Account Lane

Do not pick these up until a dedicated account tranche is explicitly scheduled:

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

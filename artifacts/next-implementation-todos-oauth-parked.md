# GoatShot Next Implementation TODOs - OAuth Parked

Date: 2026-06-15

Purpose: provide the current resume plan for finishing the remaining non-OAuth GoatShot work. OAuth consent screens, live refresh-token recovery, and real cloud-account proof stay parked until a dedicated account tranche is scheduled.

No Git workflow is required right now.

## Ground Rules

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Do not rework Google Drive, Dropbox, OneDrive, or similar OAuth flows unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not describe fake providers, local tokens, synthetic files, extension package checks, or dry-run diagnostics as live account, live browser, or live device proof.
- [ ] Prefer locally provable work: MSTest coverage, fake ADB/HTTP/process providers, safe fixtures, JS syntax checks, CLI smoke output, diagnostics redaction checks, WPF render screenshots when UI changes, and portable package output.
- [ ] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.
- [ ] Refresh `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` when readiness status changes.

## Current Baseline

- [x] Core native desktop/CLI product is locally proven: capture, scrolling, recording, editor/privacy tools, OCR, AI/document workflow, workflow automation, upload queue/history, provider adapters, diagnostics, Product Design/WPF audit artifacts, manual validation harness, and portable packaging.
- [x] Android screenshot import, bounded Android `screenrecord` MP4 pull/import, and Android live-preview dry-run planning are implemented and fake-process tested.
- [x] Browser extension contract/prototype, native host registration, local ZIP packaging, popup/options UX, stitch-manifest planning, browser-download stitch-package export, selected-element geometry, and bounded native stitch-package import are implemented and locally proven.
- [x] Virtual printer/file-drop import and driver-feasibility diagnostics are implemented and locally proven; true signed/admin printer-driver installation is not.
- [x] Local plugin SDK, guarded local plugin execution, stage-only remote package acquisition, update checks, and local admin policy bundles are implemented and locally proven.
- [x] Companion portal/team-admin boundaries are documented; hosted/self-hosted portal code is not implemented.

## Completed Tranche A - Android Live Preview Dry-Run Planner

Goal: explore Android live-preview paths without starting uncontrolled device streaming or capturing private phone content.

- [x] Add `artifacts/tranche-android-live-preview/architecture.md` comparing repeated `screencap` polling, `screenrecord --output-format=h264 -`, FFmpeg remux, and scrcpy-style external tooling.
- [x] Add a service-level live-preview plan model for bounded strategies, device selection, consent reminders, duration, byte cap, timeout, disconnect, and cleanup expectations.
- [x] Add a CLI dry-run command such as `capture android-preview --strategy screencap-polling|h264-stream --duration <seconds> --frame-interval-ms <ms> --max-bytes <bytes> --json`.
- [x] Keep the command as a planner/dry-run first. It may probe `adb devices`, but it must not start live capture unless a later explicit capture mode is designed.
- [x] Add fake ADB tests for ready device, missing ADB, multiple devices, invalid bounds, planned screencap polling, planned H.264 stream, timeout/disconnect messaging, and cleanup guidance.
- [x] Generate CLI proof artifacts under `artifacts/tranche-android-live-preview/`.
- [x] Leave real-device preview proof manual/privacy-gated until safe device content is staged.

Proof gate:

- [x] Focused Android ADB tests.
- [x] CLI dry-run JSON/text artifacts.
- [x] `dotnet build .\GoatShot.slnx -c Release`
- [x] `dotnet test .\GoatShot.slnx -c Release`
- [x] CLI `--help` and `diagnostics print`.
- [x] `.\scripts\package-release.ps1 -SkipInstaller`
- [x] `artifacts/tranche-android-live-preview/notes.md`.

## Partially Completed Tranche B - Browser Live Fixture Proof And Diagnostics Closure

Goal: close the browser extension proof gap with safe fixture evidence, without claiming browser-store publication or automatic extension installation.

- [ ] Use `browser-extension/samples/safe-fixture.html` as the only live fixture target unless the user stages another safe page.
- [ ] Manually load the unpacked extension in Chrome or Edge.
- [ ] Register the native host with the existing user-scope command.
- [ ] Capture screenshots of popup/options consent states, native-host status, selected-element mode, package-export toggle, and handoff result.
- [ ] Run a consented fixture capture and import the downloaded stitch package through the CLI receiver.
- [ ] Save screenshots, redacted payloads, imported package output, and notes under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/` or a dedicated tranche folder.
- [x] Add local diagnostics that can be proven without store publication, including clearer source readiness, safe fixture readiness, host-missing, host-manifest-missing, host-registered-but-browser-proof-needed, payload-rejected, stitch-package-import, and browser-download-package-boundary statuses.
- [ ] Keep browser-store publication and automatic installation as later-scope product/release decisions.

Proof gate:

- [ ] Live fixture screenshots with safe content.
- [ ] Redacted payload/package artifacts.
- [ ] Native import result artifact.
- [x] Updated local diagnostics notes under `artifacts/tranche-browser-live-diagnostics/`.
- [x] Build/test/package for the local diagnostic code change.

## Tranche C - Manual Proof Pass

Goal: use the existing manual validation harness for proof that deterministic tests cannot honestly provide.

- [ ] Generate a fresh manual evidence folder with `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if the hardware setup is available.
- [ ] Complete long recording stability proof with safe microphone/system-audio/webcam content if permissions and devices are available.
- [ ] Complete clean-machine portable ZIP proof in a clean profile or VM.
- [ ] Complete live Android screenshot/video proof only with staged safe phone content.
- [ ] Keep live provider/OAuth proof parked unless explicitly unparked.

Proof gate:

- [ ] Lane notes and artifacts under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Summary file stating pass/fail/blocked per lane.
- [ ] Public readiness docs updated only for proof actually collected.

## Tranche D - Companion Portal Implementation Decision

Goal: decide whether to build portal code after the local/admin boundaries are real.

- [ ] Review `artifacts/tranche-companion-portal-planning/companion-portal-boundaries.md` and `threat-privacy-checklist.md`.
- [ ] Choose one path: docs-only for V1, self-hosted local/LAN portal v0, or hosted service v0.
- [ ] If implementation is approved, create it as a separate module with explicit auth, tenant/privacy boundaries, data sync categories, policy merge semantics, audit logs, diagnostics, and deployment notes.
- [ ] Start with policy-template download and read-only audit summaries, not capture-file hosting.
- [ ] Do not allow portal code to bypass desktop policy, local consent, provider account boundaries, redaction rules, plugin trust, or OAuth parking.

Proof gate if code is approved:

- [ ] Architecture approval note.
- [ ] Threat/privacy checklist update.
- [ ] Focused tests for auth/policy/audit/sync boundaries.
- [ ] Diagnostics and deployment notes.
- [ ] Separate proof artifacts before any implementation claim.

## Later-Scope Or Explicitly Parked

- [ ] OAuth/live account proof: Google Drive, Dropbox, OneDrive consent, refresh-token recovery, real uploads, live-account delete behavior.
- [ ] Browser-store publication and automatic extension installation.
- [ ] True Windows virtual-printer driver installation, signing, admin installer integration, and clean-machine printer proof.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted marketplace accounts/payments/ratings beyond governed local staging/install-staged/update-apply support.
- [ ] Hosted/self-hosted companion portal implementation unless Tranche D approves a concrete v0.
- [ ] Production Android live streaming beyond the dry-run planner and manual safe-device proof.

## Recommended Order

1. Run Tranche B when a safe browser session can be used for live fixture proof.
2. Run Tranche C as a manual QA/proof day rather than mixing it into feature work.
3. Decide Tranche D before adding any web/backend portal code.
4. Leave OAuth and admin/signed-driver/store/marketplace work parked until explicitly scheduled.

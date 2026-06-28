# GoatShot Next Build TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue implementing what is left on GoatShot without waiting on live OAuth consent screens, refresh-token validation, or real cloud-account proof. Use this as the current forward TODO plan after the Android preview execution tranche.

No Git workflow is required right now.

## Current Direction

- [x] Keep Google Drive, Dropbox, OneDrive, and future live OAuth consent/account proof parked.
- [x] Keep GoatShot as a native WPF/.NET desktop app with CLI support.
- [x] Keep fake-provider, synthetic-media, local-token, local-browser-package, and fake-device evidence labeled as local proof only.
- [x] Use Product Design/WPF screenshot-backed audit notes for material desktop UI changes.
- [x] End each implementation tranche with `artifacts/tranche-<name>/notes.md`.

## Parked Until Explicitly Scheduled

- [ ] Google Drive, Dropbox, and OneDrive live OAuth consent proof.
- [ ] Live refresh-token expiry/recovery and reauthorization proof.
- [ ] Real cloud-account upload/delete proof.
- [ ] Browser-store publication/review/signing and automatic extension installation.
- [ ] Signed/admin Windows virtual-printer driver installation.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates or hosted marketplace behavior.
- [ ] Hosted/self-hosted companion portal code unless the portal decision tranche approves a v0.
- [ ] Production Android live streaming beyond bounded screencap polling and bounded `screenrecord` import.

## Standard Done Gate For Every Code Tranche

- [ ] Focused MSTest coverage for changed services, models, CLI behavior, UI models, and policy gates.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy checks when prompts, transcripts, OCR text, URLs, tokens, logs, settings, browser payloads, Android media, plugin packages, or diagnostics are touched.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Tranche notes with changed files, proof paths, skipped/manual proof, and remaining risk.

## Tranche 1 - Browser Live Fixture Closure

Goal: close or sharply bound the live browser-extension proof gap using only safe local fixture content, without making browser-store or automatic-install claims.

Status note, 2026-06-15: the helper now generates an isolated Chrome/Edge launch script and browser launch plan JSON in addition to the safe-fixture server, commands, diagnostics, and verifier artifacts. Live browser screenshots, Host Status, and real browser-exported package proof remain manual.

Implementation TODOs:

- [ ] Use `browser-extension/samples/safe-fixture.html` or the existing live-fixture helper safe server as the only browser target unless another safe page is explicitly staged.
- [ ] Prefer Chrome first, then Edge if Chrome extension loading remains brittle.
- [x] Extend `browser-extension live-fixture` to generate a browser launch/proof script using an isolated profile, `--load-extension`, `--disable-extensions-except`, the safe fixture URL, and explicit no-store/no-auto-install notes.
- [ ] Register the native host with the existing user-scope command once the live extension id is known.
- [ ] Capture extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, browser version, and last handoff result.
- [ ] Run one consented fixture capture with screenshot consent and package export enabled.
- [ ] Import the downloaded stitch package through `goatshot browser-extension receive --stitch-package`.
- [ ] Save live screenshots, redacted payloads, import result JSON, package path, extension id note, browser version, and operator notes under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/`.

Exit criteria:

- [ ] Live safe-browser proof is complete, or the tranche records the exact browser/OS automation blocker and leaves browser proof as a manual lane without blocking later non-OAuth implementation.

## Tranche 2 - Manual Validation And Fix Pass

Goal: turn the existing manual-validation harness into useful proof, and patch any small local UI/accessibility/diagnostic issues found during the pass.

Implementation TODOs:

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region-drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long-recording stability proof with safe microphone/system-audio/webcam content if devices and permissions are available.
- [ ] Complete clean-profile or clean-machine portable ZIP proof if available.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Fix small deterministic issues discovered in this pass, such as missing accessible names, bad focus order, unclear disabled reasons, contrast regressions, or diagnostics copy bugs.
- [ ] Keep live provider/OAuth proof parked.

Exit criteria:

- [ ] `artifacts/manual-validation/<yyyy-mm-dd>/summary.md` lists pass/fail/blocked for every lane.
- [ ] Any code fixes discovered by the pass have focused tests/screenshots and tranche notes.

## Tranche 3 - Desktop Plugin Update Surface

Goal: bring the already-proven plugin update summary out of CLI-only status without enabling automatic installs, trust, enablement, allowlists, or execution.

Status note, 2026-06-15: Settings Plugins now exposes the passive remote-plugin update summary, copy-CLI handoff, and staging-folder review action. Screenshot and focus/name audit proof are saved under `artifacts/product-design-audit/2026-06-15/plugin-update-surface/`.

Implementation TODOs:

- [x] Reuse the existing plugin update summary model in a WPF Settings or diagnostics surface.
- [x] Show available, staged, installed, blocked, incompatible, and not-installed states.
- [x] Show active version, registry version, compatibility, staged package state, policy blocks, and operator gates.
- [x] Add actions only for safe operator handoff: copy CLI command, open staging folder, refresh diagnostics, or view registry metadata.
- [x] Do not auto-stage, auto-install, auto-trust, auto-enable, auto-allowlist, or auto-run plugins.
- [x] Add focused UI-model tests and screenshot-backed Product Design/WPF notes.

Exit criteria:

- [x] Desktop users can inspect plugin updates without leaving the trust model.

## Tranche 4 - Browser Extension Proof Assistant Polish

Goal: make future browser proof repeatable after Tranche 1, even when a live browser cannot be fully automated.

Implementation TODOs:

- [ ] Add or extend a proof manifest that records browser name/version, extension source/package hash, native-host status, fixture URL, payload path, stitch-package path, import result, and screenshots collected.
- [ ] Add CLI validation for a completed browser proof folder so missing screenshots, missing import results, or unredacted payloads are called out before readiness docs are updated.
- [ ] Add manual-template copy for Chrome and Edge fallback paths.
- [ ] Keep publication, review, signing, and automatic install out of scope.

Exit criteria:

- [ ] A future operator can rerun browser proof and know exactly what evidence is missing.

## Tranche 5 - Android Preview Review Surface

Goal: make the guarded Android preview execution output easier to inspect without starting production Android streaming.

Implementation TODOs:

- [ ] Add a small manifest viewer or CLI summary for `capture android-preview --execute` output: frame count, duration, byte count, selected device, bounds, cleanup status, and safe-content confirmation.
- [ ] Add optional contact-sheet generation from collected PNG frames for review.
- [ ] Add WPF/Settings diagnostics copy that keeps live Android preview proof privacy-gated.
- [ ] Keep H.264 stdout streaming, FFmpeg remux, scrcpy-style mirroring, and continuous production streaming later-scope.

Exit criteria:

- [ ] Operators can inspect bounded preview proof quickly without replaying private device content.

## Tranche 6 - Virtual Printer Installer Prep, No Driver Claim

Goal: improve the print-import handoff without pretending GoatShot ships a signed OS printer driver.

Implementation TODOs:

- [ ] Add a package-time or first-run helper that creates the watched print-import folder and writes a short local setup note.
- [ ] Add Settings/diagnostics feedback for print-import folder write health and supported extensions if not already visible enough.
- [ ] Add a clean separation in docs between Microsoft Print to PDF/file-drop handoff and true driver installation.
- [ ] Keep signed driver work, admin installation, and clean-machine printer proof in the later/manual lane.

Exit criteria:

- [ ] A user can configure file-drop print import without needing driver-level work.

## Tranche 7 - Companion Portal Decision

Goal: choose the next portal/team path before adding any hosted or self-hosted code.

Decision TODOs:

- [ ] Review `artifacts/tranche-companion-portal-planning/companion-portal-boundaries.md` and `threat-privacy-checklist.md`.
- [ ] Choose one path: no portal for V1, local static report export, self-hosted LAN portal v0, or hosted portal v0.
- [ ] If a v0 is approved, start read-only with policy templates, audit summaries, diagnostics summaries, and release-proof summaries.
- [ ] Do not host capture files, secrets, tokens, or provider account data in v0.
- [ ] Do not let portal code bypass desktop policy, local consent, redaction rules, provider account boundaries, plugin trust, or OAuth parking.

Exit criteria:

- [ ] Either portal remains explicitly parked, or a narrow v0 has an approved architecture note and proof gate.

## Tranche 8 - Release Proof Refresh

Goal: make the handoff artifact match the latest source and collected evidence.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, selected screenshots/audit notes, and current tranche notes.
- [ ] Keep portable ZIP as the default proof artifact.
- [ ] Do not claim clean-machine installer proof unless a clean profile/VM actually proves it.
- [ ] Update `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, `artifacts/active-non-oauth-buildout-todos.md`, and `artifacts/current-implementation-todos-oauth-parked.md` only for evidence actually collected.

Exit criteria:

- [ ] The release proof bundle states implemented, locally proven, manually verified, OAuth parked, and later-scope work without overclaiming.

## Recommended Execution Order

1. Browser Live Fixture Closure.
2. Manual Validation And Fix Pass.
3. Release Proof Refresh if manual proof changes readiness claims.
4. Desktop Plugin Update Surface.
5. Browser Extension Proof Assistant Polish.
6. Android Preview Review Surface.
7. Virtual Printer Installer Prep.
8. Companion Portal Decision.
9. Final Release Proof Refresh after any code tranches.

If a manual/hardware/browser step is blocked by unavailable safe content, record the blocker and continue to the next locally buildable tranche instead of reopening OAuth.

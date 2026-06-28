# GoatShot Remaining Non-OAuth Implementation Plan

Date: 2026-06-15

Purpose: continue implementing what is left after the local V1 buildout while keeping live OAuth consent, refresh-token recovery, and real cloud-account proof parked. This file is the short, execution-ready plan for the remaining TODO lanes.

No Git workflow is required for this project right now.

## Scope Guard

- [ ] Keep GoatShot a native WPF/.NET desktop app.
- [ ] Keep Google Drive, OneDrive, Dropbox, and other OAuth consent/account proof parked.
- [ ] Do not describe fake-provider, local-token, synthetic, or package-only proof as live account readiness.
- [ ] Prefer locally provable implementation: MSTest coverage, fake HTTP/process providers, safe fixtures, CLI smoke, diagnostics redaction checks, WPF render screenshots when UI changes, and portable package output.
- [ ] End each tranche with `artifacts/tranche-<name>/notes.md`.
- [ ] Refresh `README.md`, `spec.md`, `artifacts/v1-readiness-summary.md`, and active TODO ledgers when status changes.

## Recommended Build Order

### Completed Tranche A - Remote Plugin Install And Update Scaffold

Why next: it is locally provable, does not require live browser/hardware/OAuth proof, and builds directly on the completed local plugin SDK and guarded execution model.

- [x] Define `goatshot.plugin-registry.v1` with plugin id, version, name, description, capabilities, permissions, package URI, SHA-256, size, signature placeholder, compatibility range, and release notes.
- [x] Add registry validation for local files and fake HTTP registries.
- [x] Add package staging that downloads or copies archives into an app-owned staging folder without enabling, trusting, or executing plugin code.
- [x] Verify SHA-256, max package size, required manifest, compatibility range, duplicate ids, and safe archive paths.
- [x] Reject zip path traversal, absolute paths, reserved names, missing manifests, mismatched ids, checksum mismatches, oversized packages, and executable auto-run requests.
- [x] Add CLI commands for registry validation, install planning/staging, update checks, staged-package removal, and plugin disable/uninstall metadata handling.
- [x] Keep remote execution, hosted marketplaces, ratings, accounts, payments, and automatic background updates out of scope.
- [x] Proof: fake HTTP registry/package tests, archive security tests, CLI smoke artifacts, sample registry docs, full Release build/test/package, and `artifacts/tranche-plugin-remote-install-scaffold/notes.md`.

### Completed Tranche B - Browser Automatic Stitch Package Export

Why second: core native import and stitch planning are already proven; the remaining work is browser-side package generation plus safe fixture proof.

- [x] Add selected-element capture geometry and UX contract.
- [x] Generate local stitch packages in the extension from captured tiles, manifest metadata, and optional stitched bitmap output.
- [x] Keep cookies, headers, form values, storage, and raw DOM text out of all payloads.
- [x] Add live-safe browser fixture page under artifacts or samples for tall, wide, sticky-header, and partial-capture cases.
- [x] Add extension/native diagnostics that distinguish extension source/package readiness, safe fixture readiness, host missing, host manifest missing, host registered but still requiring browser Host Status proof, payload rejected, stitch-package import readiness, and browser-download package boundary.
- [x] Keep browser-store publication and automatic extension installation out of scope.
- [x] Proof: JS syntax checks, package validation, native receiver tests, safe fixture package import artifacts, full Release build/test/package, and `artifacts/tranche-browser-auto-stitch-package/notes.md` plus `artifacts/tranche-browser-live-diagnostics/notes.md`.
- [ ] Browser screenshot/fixture proof remains manual until safe browser content is staged.

### Completed Tranche C - Local Team/Admin Mode

Why third: managed policy keys exist, but a coherent local admin mode is still later-scope and useful before any hosted portal work.

- [x] Define admin policy bundles for provider allowlists, AI/upload disablement, external script/webhook controls, redaction defaults, retention defaults, plugin controls, diagnostics bundle rules, and browser/Android/import permissions.
- [x] Add CLI commands to validate, import, export, diff, and explain admin policy bundles while omitting secrets by default.
- [x] Add effective-policy diagnostics and audit entries when policy blocks actions.
- [x] Add WPF Settings/diagnostics surfaces only after the CLI/service contract is stable.
- [x] Preserve deny-wins precedence over user settings.
- [x] Keep hosted sync, remote enforcement, multi-user account state, and portal management out of scope.
- [x] Proof: policy precedence tests, blocked-action tests, CLI artifacts, diagnostics redaction checks, WPF screenshot proof if UI changes, full Release build/test/package, and `artifacts/tranche-local-team-admin-mode/notes.md`.

### Completed Tranche D - Virtual Printer Driver Feasibility

Why fourth: file-drop print import is done; the next useful step is honest driver/admin feasibility, not pretending a printer driver is shipped.

- [x] Document Microsoft Print to PDF handoff, watched-folder routing, port monitor, v4 driver, PostScript/PDF pipelines, installer/admin requirements, and signing constraints.
- [x] Add CLI/admin diagnostics for watched folder health, supported extensions, printer-driver unavailability, installer privilege state, and package hooks.
- [x] Add non-invasive package-hook guidance only where it does not require a signed driver or admin install.
- [x] Keep real driver installation and clean-machine printer proof manual/admin-scoped.
- [x] Proof: diagnostics tests, safe PDF/image import regressions, architecture note, Release build/test/package, and `artifacts/tranche-virtual-printer-driver-feasibility/notes.md`.

### Completed Tranche E - Android Live Preview Spike

Why fifth: bounded Android screenshot/video import is done; live preview needs extra privacy and hardware care.

- [x] Compare repeated screencap preview, `screenrecord --output-format=h264 -`, FFmpeg remux, and scrcpy-style external-tool boundaries.
- [x] Implement only a dry-run/fake-process or repeated-screencap planning path first.
- [x] Add explicit safe-content consent reminders, duration limits, byte limits, cleanup bounds, and disconnected-device states.
- [x] Keep live device preview proof manual unless safe device content is staged.
- [x] Proof: fake ADB stream/polling plan tests, timeout/disconnect/invalid-payload stop guidance, CLI dry-run artifacts, Release build/test/package, and `artifacts/tranche-android-live-preview/notes.md`.

### Tranche F - Manual Proof Pass

Why sixth: these lanes need real human/device/OS state and should not block locally buildable tranches.

- [ ] Keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Narrator/NVDA screen-reader pass for key WPF flows.
- [ ] Windows text scaling and high-contrast checks.
- [ ] Live region drag proof.
- [ ] Live multi-monitor capture and recording.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Clean-machine portable ZIP proof and optional installer proof.
- [ ] Record evidence in the manual validation harness folders and keep sensitive screen/device content out of artifacts.

### Tranche G - Companion Portal Decision

Why last: portal work should wait until local admin/team policy is real.

- [ ] Re-review `artifacts/tranche-companion-portal-planning/`.
- [ ] Decide docs-only, self-hosted LAN, or hosted service boundary.
- [ ] If approved, create a separate module with explicit auth, policy merge, sync, audit, and privacy boundaries.
- [ ] Do not allow portal code to bypass desktop consent, local policy, provider account boundaries, or redaction rules.
- [ ] Proof: architecture approval note, threat/privacy checklist update, and separate tests/diagnostics/deployment notes before any implementation claim.

## Parked OAuth/Live Account Lane

Keep parked until explicitly scheduled:

- [ ] Google Drive live OAuth consent screen proof.
- [ ] Dropbox live OAuth consent screen proof.
- [ ] OneDrive live OAuth consent screen proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Per-Tranche Definition Of Done

- [ ] Focused tests cover changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot, render artifact, or Product Design audit note exists for changed desktop UI.
- [ ] Redaction/privacy assertions exist when URLs, tokens, prompts, transcripts, OCR text, logs, settings, plugin packages, or telemetry payloads are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.

# Receipts 0.3.0 Readiness Summary

Date: 2026-08-11

Receipts `0.3.0` is the current local release candidate. It is not yet a tagged or stable GitHub release, and the Windows packages are currently unsigned. This document records implementation and local proof; it is not a public-release announcement.

## Current Product Readiness

The GoatShot product has been renamed to **Receipts** while retaining internal `GoatShot.*` project and namespace names for migration safety. The desktop app, CLI, installer, shortcuts, browser bridge, release metadata, and new-user storage paths use the Receipts identity. Existing installations retain an upgrade and compatibility path.

Replay is integrated into screen recording as an opt-in mode:

- A duration- and byte-bounded rolling buffer stores finalized two-second MP4 segments and can save the configured preceding interval without stopping the live buffer.
- The default profile is 60 seconds, 30 FPS, follow-cursor monitor, cursor enabled, audio/microphone/webcam disabled, and a 512 MB total cap. Replay requires onboarding consent before it can be armed.
- Capture strategies cover a chosen monitor, cursor-following monitor, all-monitor composite, separate synchronized monitor tracks, chosen window, foreground-following window, selected region, and fixed region.
- Replay exposes Off, Armed, Paused, Saving, and Error states plus configurable arm/pause and save hotkeys. The default Save Replay hotkey is `Ctrl+Shift+PrintScreen`.
- A saved replay becomes one `ReplayReceipt` library item. Frame Explorer supports playback, scrubbing, frame stepping, source/track selection, before/after review, frame extraction, unique-frame extraction, contact sheets, track/composite video export, and step-by-step guides.
- Scene indexing and OCR comparison run locally after saving. Findings are labeled `Possible addition`, `Possible edit`, or `Possible deletion`, retain supporting before/after frames, and require human confirmation.

The principal implementation and deterministic acceptance coverage are in [Replay recording](../src/GoatShot.App/Services/ReplayRecordingService.cs), [Frame Explorer](../src/GoatShot.App/Services/ReplayReceiptExplorerService.cs), and [Replay receipt acceptance tests](../src/GoatShot.Tests/ReplayReceiptAcceptanceTests.cs).

## Integrity Claim

Each original replay receipt is a package containing its preserved MP4 segments, canonical `receipts.receipt.v1` manifest, public verification key, thumbnail, and optional local analysis. Receipts hashes segments with SHA-256, chains them in source/timestamp order, and signs the canonical manifest with ECDSA P-256/SHA-256. The device private key is protected for the current Windows user with DPAPI; the public key and fingerprint travel with the receipt.

Verification reports only these outcomes:

- `Intact — known device key`
- `Intact — unknown device key`
- `Modified`
- `Incomplete`
- `Unverifiable`

Receipts can verify integrity since local signing. It does **not** independently attest the device clock, remote-service state, message author, operator identity, or legal/forensic authenticity. OCR findings are review suggestions, not proof of what occurred. Extracted frames, edited images, reports, and exported videos are linked derivatives; Receipts does not overwrite signed originals.

See [receipt integrity](../src/GoatShot.App/Services/ReceiptIntegrityService.cs), [device-key storage](../src/GoatShot.App/Services/ReceiptDeviceKeyService.cs), and [integrity tests](../src/GoatShot.Tests/ReceiptIntegrityServiceTests.cs).

## Automated Proof

- Release solution tests: **775 passed, 0 failed**.
- NuGet vulnerability audit: no vulnerable direct or transitive packages reported.
- Inno Setup `6.7.3` produced `Receipts-0.3.0-win-x64.exe`; all 14 installer script checks passed.
- Isolated installer inventory matched all **528 of 528** published payload files with no missing or extra entries. The executable checksum sidecar matched, PE/product metadata reported Receipts `0.3.0`, and Authenticode correctly reported `NotSigned`.
- Portable package verification passed all required-entry checks and packaged `Receipts.Cli.exe` help/diagnostics smoke commands.
- Single-executable verification passed its layout and embedding checks with no loose native libraries or unexpected distribution entries.
- The final WPF Frame Explorer audit found 34 named controls with no missing accessible names or focus-order rejections.
- Current-machine hardware readiness diagnostics completed six commands with no nonzero result and detected two displays. This is readiness evidence only, not live capture/recording proof.

The verification entry points are [package-release.ps1](../scripts/package-release.ps1), [verify-installer-package.ps1](../scripts/verify-installer-package.ps1), [verify-portable-package.ps1](../scripts/verify-portable-package.ps1), [verify-single-exe-package.ps1](../scripts/verify-single-exe-package.ps1), and [create-release-proof-bundle.ps1](../scripts/create-release-proof-bundle.ps1). Generated proof remains local under `artifacts/` and must be regenerated for each intended release commit before publication.

## Remaining Operator Proof

The following are not complete release claims and still require safe, human-observed execution:

- Clean Windows profile/VM installer install, upgrade, startup, repair, and uninstall behavior.
- Keyboard traversal, Narrator/NVDA, text scaling, high contrast, and live-region/drag behavior across the primary WPF surfaces.
- Live multi-monitor capture and recording for each relevant strategy, including display topology/DPI/source transitions and separate-track synchronization.
- Long-running recording and Replay stability with safe system-audio, microphone, and webcam fixtures, including sleep/lock, encoder failure, disk-full, and recovery behavior.
- Android screenshot/video/preview behavior on an authorized device with staged safe content.
- Optional browser-extension live-fixture observations.
- OAuth and live-provider account proof; those lanes remain parked unless explicitly scheduled with safe test accounts.

Automated tests and current-machine diagnostics reduce risk but do not substitute for these operator-observed lanes.

## Release Decision

Receipts `0.3.0` is ready for continued local installation and manual validation as an **unsigned release candidate**. Do not describe it as a stable release, claim the Git tag exists, or publish the candidate packages until the final source is rebuilt, the intended release checks are rerun, and the remaining required operator gates are consciously accepted or completed.

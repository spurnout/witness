# Receipts

Receipts is a local-first screenshot, screen-recording, replay, annotation, and capture-library app for Windows. It is designed for fast keyboard-driven capture, useful privacy tools, and explicit control over anything that leaves your device. Its evidence features help people preserve and review what appeared on their own screen—for example, possible message edits or deletions—without claiming independent service-side truth.

> [!IMPORTANT]
> Receipts `0.3.0` is the current personal-preview development target. Release executables are unsigned, so Windows SmartScreen may warn. Verify the published SHA-256 checksum before running one.

## Download and install

When `v0.3.0` is published, the Windows x64 release contains:

- `Receipts-0.3.0-win-x64-single-exe.exe` — self-contained standalone executable
- `Receipts-0.3.0-win-x64-portable.zip` — portable desktop and CLI bundle
- `Receipts-0.3.0-win-x64.exe` — per-user installer when built with Inno Setup
- SHA-256 checksum, build metadata, SPDX SBOM, and third-party notices

Verify the executable in PowerShell:

```powershell
Get-FileHash .\Receipts-0.3.0-win-x64.exe -Algorithm SHA256
```

Run the downloaded executable:

- **Inno installer** (`Receipts-0.3.0-win-x64.exe`): follow the per-user setup wizard. The desktop-shortcut and start-at-sign-in tasks are optional and unchecked by default.
- **Standalone executable** (`Receipts-0.3.0-win-x64-single-exe.exe`): choose **Yes** at its first-run prompt to install for the current user and enable tray startup, or **No** to run that downloaded copy without installing it. Running a newer copy offers to update an existing standalone installation.
- **Portable ZIP**: extract it before use, then run `Receipts.exe` or `Receipts.Cli.exe`. Choose **No** if the desktop executable offers its optional self-install prompt and you want to keep running from the extracted folder.

Installation does not require administrator rights. Repair, startup, and uninstall controls are available in Settings. Uninstall preserves captures and settings unless those are removed separately.

### Requirements

- Windows 10 version 2004 or newer, or Windows 11
- x64 processor

The release is self-contained. A separate .NET, FFmpeg, or segmentation installation is not required for normal capture and editing. Local Whisper transcription is optional; Receipts never bundles or downloads Whisper itself.

## What it does

### Capture and recording

- Region, window, monitor, all-monitor, fixed-size, last-region, delayed, and scrolling screenshots
- Print Screen hotkeys that work while Receipts is in the tray
- Auto-copy after capture by default
- GIF and MP4 recording with monitor, window, and region targets
- Optional microphone, system-audio, webcam, cursor, timer, and keystroke overlays
- Android screenshot, bounded recording, and preview support through an in-process Windows USB/ADB transport, with an external `adb.exe` override for troubleshooting

### Replay

Recording offers **Record now** and **Replay** modes. Replay is opt-in; while armed, it continuously retains a duration- and byte-bounded ring of finalized MP4 segments, two seconds each by default. Saving the preceding interval creates one replay receipt in the library without stopping the live buffer; unsaved segments remain ephemeral application data and are neither signed nor library items.

- States are visible in the recording panel, footer, and tray: **Off**, **Armed**, **Paused**, **Saving**, and **Error**. Windows lock, sleep, and display-change transitions temporarily show Replay as suspended while retaining finalized history.
- Default hotkeys are `Ctrl+Alt+Shift+R` for Arm/Pause Replay and `Ctrl+Shift+PrintScreen` for Save Replay. Both are editable and apply after restart.
- Capture strategies are chosen monitor, follow cursor monitor, all monitors as one composite, separate synchronized monitor tracks, chosen window, follow foreground window, selected region, and fixed region.
- The default profile is a 60-second buffer, two-second segments, a 512 MB cap across all tracks, Balanced H.264 at 30 FPS, follow-cursor monitor, cursor enabled, and system audio, microphone, and webcam disabled.
- Scene indexing and OCR comparison are enabled by default but run locally only after a receipt is saved, never continuously or through an external AI provider. Replay itself remains disabled until consent; automatic arming at sign-in is a separate per-profile option.
- Replay shares the recording encoder, resolution, FPS, bitrate, cursor, audio, microphone, webcam, overlay, and privacy settings. Privacy-excluded processes are blacked out or masked, and audio is omitted from segments affected by an exclusion.
- Followed-source, resolution, display-topology, and DPI changes finalize the current segment so the signed timeline preserves the transition. Changing Replay settings while armed restarts the ephemeral buffer and releases its prior unsaved history.

### Library and editing

- Local searchable capture library with thumbnails. Favorites, collections, and trash are not available in `0.3.0`; Delete asks for confirmation and removes the local file.
- Rectangle, ellipse, line, arrow, freehand, text, callout, step-marker, highlight, spotlight, crop, blur, pixelate, and solid-redaction tools
- Keyboard shortcuts, undo/redo, clipboard actions, and flattened edited exports
- Local Windows OCR, QR/barcode decoding, metadata inspection/removal, hashing, image combine/split, and scrolling stitch tools
- OCR-assisted sensitive-data review for emails, credentials, API keys, and token-like text before sharing
- FFmpeg-backed trimming, frame export, mute/volume, denoise, resize, cuts, composites, subtitles, and format conversion
- Bundled ONNX person segmentation with DirectML acceleration and CPU fallback

### Frame Explorer and receipt integrity

A saved Replay opens in Frame Explorer as one receipt rather than flooding the library with segments. You can choose a source or track, play seamlessly across segments, scrub and use hover previews, seek by the configured frame interval, navigate scene markers, and compare before/after frames.

Frame Explorer can save the current frame, extract unique frames from a selected range, export a contact sheet, export selected tracks or a synchronized composite MP4, and create a step-by-step guide. Local analysis compares adjacent frames only within the same stable source and labels OCR differences as **Possible addition**, **Possible edit**, or **Possible deletion**. Each finding shows its supporting frames and requires a person to confirm or dismiss it.

Each original receipt is a directory package containing preserved MP4 segments, canonical `receipt.json`, `public-key.pem`, a thumbnail, and optional unsigned, rebuildable local analysis. Receipts hashes every signed artifact with SHA-256, chains segments in source/time order, and signs the canonical manifest with ECDSA P-256/SHA-256. The device private key is created on the first signed receipt and protected for the current Windows user with DPAPI; the public key and fingerprint travel with each receipt.

Verify in Frame Explorer or with the portable CLI:

```powershell
.\Receipts.Cli.exe receipt verify "C:\path\to\receipt-package"
.\Receipts.Cli.exe receipt key rotate
```

Both commands accept `--key-path path`. Verification returns exactly one of: `Intact — known device key`, `Intact — unknown device key`, `Modified`, `Incomplete`, or `Unverifiable`. Rotation creates a new local fingerprint; old receipts remain independently verifiable from their embedded public keys.

Original segments and manifests are never overwritten by app editing. Extracted frames, reports, guides, and exported videos are linked derivatives. Deleting an original with derivatives requires explicit confirmation; retained derivatives are marked source unavailable.

### Sharing and automation

- Clipboard, local folder, email, S3-compatible storage, Imgur, SFTP, WebDAV, FTP/FTPS, Cloudinary, webhooks, and configured issue-tracker destinations
- Strict host-key verification for in-process SSH.NET SFTP
- Upload confirmation, result, retry, history, and redacted diagnostics surfaces
- Watch folders, workflow rules, profiles, local plugins, browser native messaging, and governed background runtime verbs

Manual external shares honor **Ask before upload or external share**. Explicitly enabling a watch folder's quick-share option or a workflow rule with an external share/webhook action opts that automation into unattended transfer, so it can upload without another per-item prompt. Unattended automation never executes `DeleteLocalFile`.

The optional browser companion is included in the portable package. See the [browser extension README](browser-extension/README.md) for installation, native-host setup, compatibility identifiers, and its capture trust boundary.

The internal `GoatShot.Cli` project path remains unchanged for source compatibility; its distribution executable is `Receipts.Cli.exe`. The shipped desktop executable exposes only the governed runtime verbs needed for installation, diagnostics, browser native messaging, and background jobs.

## Privacy and transcription

Receipts stores captures and application state locally by default. Nothing is uploaded unless you initiate an external share or provider action, or explicitly enable an automation that does so.

- New-install captures: `%USERPROFILE%\Pictures\Receipts`
- Settings, index, thumbnails, and runtime assets: `%LOCALAPPDATA%\Receipts`
- Installed executable: `%LOCALAPPDATA%\Programs\Receipts\Receipts.exe`

For custom or isolated roots, set `RECEIPTS_LIBRARY_ROOT` and `RECEIPTS_LOCAL_ROOT`. Legacy `GOATSHOT_LIBRARY_ROOT` and `GOATSHOT_LOCAL_ROOT` aliases remain accepted when the corresponding Receipts variable is unset; the `RECEIPTS_*` value wins, and diagnostics identify legacy fallback use.

An upgrade copies small durable state without overwriting existing Receipts files. It does not move an existing capture library; the configured legacy library path remains in use until the user explicitly changes it.

Transcription options are deliberately explicit:

- Embedded subtitles and supplied SRT files stay local.
- External OpenAI Whisper or `whisper.cpp` can be configured in Settings or passed with `--whisper-exe`/`--whisper-model`.
- Executable discovery also checks `RECEIPTS_WHISPER_EXE`, `RECEIPTS_WHISPER_PATH`, then `whisper-cli` or `whisper` on `PATH`. Model overrides can use `RECEIPTS_WHISPER_MODEL` or `RECEIPTS_WHISPER_MODEL_PATH`. Corresponding `GOATSHOT_*` names remain fallback aliases.
- Receipts does not bundle or download Whisper.
- Gemini speech-to-text requires a configured key and consent for each action.
- Gemini is never an automatic fallback.

Cloud accounts, credentials, browser installations, hardware drivers, Android devices, and user scripts remain external integrations.

## Release status

`0.3.0` remains a prerelease target so people can evaluate the current build without turning incomplete proof into a stable-readiness claim.

Tamper-evident receipts can prove that locally signed artifacts have not changed since signing. They do not independently attest the device clock, remote-service state, message author, operator identity, or legal/forensic authenticity. OCR-based additions, edits, and deletions are review suggestions, not proven facts.

Locally automated proof covers the full solution build and test suite, portable, standalone, and Inno installer packaging, isolated launch, embedded-asset extraction, deliberate asset corruption and repair, checksum/build metadata, SBOM generation, browser native-host framing, plugin runtime verbs, FFmpeg resolution, SFTP host-key rejection, deterministic ADB protocol behavior, segmentation output, and transcription consent boundaries.

The following lanes still need fresh operator or hardware evidence before a stable release claim:

- Clean-profile Windows Sandbox installation and uninstall
- Sign-out/sign-in tray startup
- Full keyboard, screen-reader, high-contrast, and 200% text-scaling review
- Long-run multi-monitor, audio, webcam, and recording validation
- Live authorized Android device compatibility
- Live OAuth account flows, which remain outside this release

See [the Receipts 0.3.0 readiness summary](artifacts/receipts-0.3.0-readiness-summary.md) for the detailed evidence and claim boundaries.

## Build from source

Prerequisites:

- Windows x64
- .NET 10 SDK
- Windows PowerShell
- Git
- Network access when restoring packages and downloading the hash-locked release assets
- Optional: Inno Setup 6, required only to produce and verify `Receipts-0.3.0-win-x64.exe`. The packaging script discovers `ISCC.exe` or accepts `INNO_SETUP_ISCC`.

Restore, build, and test:

```powershell
dotnet restore GoatShot.slnx
dotnet build GoatShot.slnx -c Release --no-restore
dotnet test GoatShot.slnx -c Release --no-build
dotnet list GoatShot.slnx package --vulnerable --include-transitive
```

Build and verify the self-contained release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.3.0
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-portable-package.ps1 -PackagePath .\artifacts\dist\Receipts-0.3.0-win-x64-portable.zip -RunCliSmoke
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-single-exe-package.ps1 -Version 0.3.0
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-installer-package.ps1 -Version 0.3.0
```

Without Inno Setup, add `-SkipInstaller` to `package-release.ps1` and omit `verify-installer-package.ps1`; the portable ZIP and standalone executable still build. The portable package also carries the optional browser companion. Use `Receipts.Cli.exe browser-extension publication-plan` to create a read-only checklist for manual browser-store submission; it does not contact a store, upload an extension, or claim publication.

The locked FFmpeg and segmentation inputs are declared in [`packaging/embedded-assets.lock.json`](packaging/embedded-assets.lock.json). The packaging script verifies every download by SHA-256 before embedding it.

## Repository map

```text
src/GoatShot.App/       WPF desktop application and production services
src/GoatShot.Cli/       Developer and deterministic proof CLI
src/GoatShot.Tests/     Unit and integration tests
browser-extension/      Browser extension and native-host assets
packaging/              Locked embedded-asset manifest
scripts/                Build, package, SBOM, and verification scripts
artifacts/*.md           Tracked readiness and validation documents
spec.md                  Product specification and historical scope
```

## Project status and licensing

This repository is public for inspection and collaboration, but no project-wide license has been granted yet. Third-party components retain their respective licenses; every release includes third-party notices and an SPDX SBOM.

Bug reports and focused pull requests are welcome through [GitHub Issues](https://github.com/spurnout/witness/issues).

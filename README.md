# GoatShot

GoatShot is a local-first screenshot, screen-recording, annotation, and capture-library app for Windows. It is designed for fast keyboard-driven capture, useful privacy tools, and explicit control over anything that leaves your device.

> [!IMPORTANT]
> GoatShot `0.2.0` is a personal preview release. The executable is unsigned, so Windows SmartScreen may warn. Verify its SHA-256 checksum before running it.

## Download and install

Download the current Windows x64 preview from the [`v0.2.0` release](https://github.com/spurnout/witness/releases/tag/v0.2.0):

- [`GoatShot-0.2.0-win-x64.exe`](https://github.com/spurnout/witness/releases/download/v0.2.0/GoatShot-0.2.0-win-x64.exe)
- [`SHA-256 checksum`](https://github.com/spurnout/witness/releases/download/v0.2.0/GoatShot-0.2.0-win-x64.exe.sha256)
- [`Build metadata`](https://github.com/spurnout/witness/releases/download/v0.2.0/GoatShot-0.2.0-win-x64.build.json)
- [`SPDX SBOM`](https://github.com/spurnout/witness/releases/download/v0.2.0/GoatShot-0.2.0-win-x64.spdx.json)
- [`Third-party notices`](https://github.com/spurnout/witness/releases/download/v0.2.0/GoatShot-0.2.0-THIRD-PARTY-NOTICES.txt)

Verify the executable in PowerShell:

```powershell
Get-FileHash .\GoatShot-0.2.0-win-x64.exe -Algorithm SHA256
```

Run the downloaded executable:

- Choose **Yes** to install GoatShot for the current Windows user at `%LOCALAPPDATA%\Programs\GoatShot\GoatShot.exe` and start it in the tray at sign-in.
- Choose **No** to open the downloaded copy without installing it.
- Run a newer downloaded GoatShot executable to update an installed copy.

Installation does not require administrator rights. Repair, startup, and uninstall controls are available in Settings. Uninstall preserves captures and settings unless those are removed separately.

### Requirements

- Windows 10 version 2004 or newer, or Windows 11
- x64 processor

The release is self-contained. A separate .NET, FFmpeg, segmentation, or Whisper installation is not required for normal capture and editing. Whisper is optional and is never bundled or downloaded by GoatShot.

## What it does

### Capture and recording

- Region, window, monitor, all-monitor, fixed-size, last-region, delayed, and scrolling screenshots
- Print Screen hotkeys that work while GoatShot is in the tray
- Auto-copy after capture by default
- GIF and MP4 recording with monitor, window, and region targets
- Optional microphone, system-audio, webcam, cursor, timer, and keystroke overlays
- Android screenshot, bounded recording, and preview support through an in-process Windows USB/ADB transport, with an external `adb.exe` override for troubleshooting

### Library and editing

- Local searchable capture library with thumbnails, favorites, collections, and trash
- Rectangle, ellipse, line, arrow, freehand, text, callout, step-marker, highlight, spotlight, crop, blur, pixelate, and solid-redaction tools
- Keyboard shortcuts, undo/redo, clipboard actions, and flattened edited exports
- Local Windows OCR, QR/barcode decoding, metadata inspection/removal, hashing, image combine/split, and scrolling stitch tools
- OCR-assisted sensitive-data review for emails, credentials, API keys, and token-like text before sharing
- FFmpeg-backed trimming, frame export, mute/volume, denoise, resize, cuts, composites, subtitles, and format conversion
- Bundled ONNX person segmentation with DirectML acceleration and CPU fallback

### Sharing and automation

- Clipboard, local folder, email, S3-compatible storage, Imgur, SFTP, WebDAV, FTP/FTPS, Cloudinary, webhooks, and configured issue-tracker destinations
- Strict host-key verification for in-process SSH.NET SFTP
- Upload confirmation, result, retry, history, and redacted diagnostics surfaces
- Watch folders, workflow rules, profiles, local plugins, browser native messaging, and governed background runtime verbs

The full `GoatShot.Cli` project remains a developer and proof surface. The shipped executable exposes only the governed runtime verbs needed for installation, diagnostics, browser native messaging, and background jobs.

## Privacy and transcription

GoatShot stores captures and application state locally by default. Nothing is uploaded until you choose a configured external destination or explicitly invoke a provider-backed action.

- Captures: `%USERPROFILE%\Pictures\GoatShot`
- Settings, index, thumbnails, and runtime assets: `%LOCALAPPDATA%\GoatShot`
- Installed executable: `%LOCALAPPDATA%\Programs\GoatShot\GoatShot.exe`

Transcription options are deliberately explicit:

- Embedded subtitles and supplied SRT files stay local.
- External Whisper requires paths to a user-supplied executable and model.
- GoatShot does not bundle or download Whisper.
- Gemini speech-to-text requires a configured key and consent for each action.
- Gemini is never an automatic fallback.

Cloud accounts, credentials, browser installations, hardware drivers, Android devices, and user scripts remain external integrations.

## Release status

`0.2.0` is published as a prerelease so people can install and evaluate the current build without turning incomplete proof into a stable-readiness claim.

Locally automated proof covers the full solution build and test suite, single-executable packaging, isolated launch, embedded-asset extraction, deliberate asset corruption and repair, checksum/build metadata, SBOM generation, browser native-host framing, plugin runtime verbs, FFmpeg resolution, SFTP host-key rejection, deterministic ADB protocol behavior, segmentation output, and transcription consent boundaries.

The following lanes still need fresh operator or hardware evidence before a stable release claim:

- Clean-profile Windows Sandbox installation and uninstall
- Sign-out/sign-in tray startup
- Full keyboard, screen-reader, high-contrast, and 200% text-scaling review
- Long-run multi-monitor, audio, webcam, and recording validation
- Live authorized Android device compatibility
- Live OAuth account flows, which remain outside this release

See [the readiness summary](artifacts/v1-readiness-summary.md) for the detailed evidence and claim boundaries.

## Build from source

Prerequisites:

- Windows x64
- .NET 10 SDK
- Windows PowerShell
- Git
- Network access when restoring packages and downloading the hash-locked release assets

Restore, build, and test:

```powershell
dotnet restore GoatShot.slnx
dotnet build GoatShot.slnx -c Release --no-restore
dotnet test GoatShot.slnx -c Release --no-build
dotnet list GoatShot.slnx package --vulnerable --include-transitive
```

Build and verify the self-contained release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.2.0
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-single-exe-package.ps1 -Version 0.2.0
```

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

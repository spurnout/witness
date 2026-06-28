# GoatShot Manual Validation Checklist

Date: 2026-06-15

Purpose: define the manual proof lanes that cannot be honestly completed from deterministic local tests, fake providers, or app-owned WPF render screenshots. Use this checklist for human/device/provider validation after the local V1 buildout.

Recommended evidence folder: `artifacts/manual-validation/<yyyy-mm-dd>/`.

## Evidence Rules

- [ ] Use safe demo desktop content only. Do not capture private email, chats, credentials, customer data, personal files, or live production systems.
- [ ] Save screenshots, short recordings, notes, and command output in the dated manual-validation folder.
- [ ] Record the GoatShot build/package used, Windows version, monitor layout, audio devices, camera device, and whether FFmpeg/ffprobe are installed.
- [ ] Redact provider account names, email addresses, tokens, callback URLs with codes, upload URLs with signatures, and private filesystem paths before sharing externally.
- [ ] Do not mark a lane passed unless a human actually performed the stated interaction.
- [ ] Do not claim WCAG/accessibility compliance from these checks alone; record observed keyboard, focus, screen-reader, contrast, and scaling behavior.

## Baseline Setup

- [ ] Start from the latest portable package or Release build.
- [ ] Confirm `dotnet build .\GoatShot.slnx -c Release` is green for the tested source state.
- [ ] Confirm `dotnet test .\GoatShot.slnx -c Release` is green for the tested source state.
- [ ] Run `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`.
- [ ] Save a diagnostics bundle or diagnostics output with secrets redacted.
- [ ] Record whether Windows.Graphics.Capture, Direct3D11, Media Foundation H.264/HEVC, WASAPI, camera, FFmpeg, and ffprobe were detected.

## Keyboard Traversal

Scope: Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, capture task window, upload confirmation/result, share history/queue, and share/provider setup.

- [ ] Tab through the Main Window from startup without using the mouse.
- [ ] Confirm visible focus indicator is present on each actionable control.
- [ ] Confirm focus order follows visual order closely enough to complete capture, edit, share, and settings tasks.
- [ ] Confirm disabled controls are skipped or announced as unavailable.
- [ ] Confirm `Esc`, `Enter`, arrow keys, and spacebar work where native WPF behavior implies they should.
- [ ] Confirm capture overlay can be dismissed and does not trap focus unexpectedly.
- [ ] Confirm tray-menu actions can be reached through OS keyboard access where available.
- [ ] Save notes: `keyboard-traversal.md`.

Pass criteria: a keyboard-only user can complete common capture, edit, settings, recording setup, and share-history recovery flows without losing focus or needing mouse-only controls.

## Screen Reader / Narrator Pass

Recommended tools: Windows Narrator and, if available, NVDA.

- [ ] Main Window controls have meaningful names and state announcements.
- [ ] Settings section picker, provider fields, password boxes, Save/Clear buttons, and status text are understandable.
- [ ] Recording controls announce selected profile, source/output state, device states, preview state, and start/stop/pause state.
- [ ] Editor tool groups, privacy tools, active tool status, and export actions are understandable.
- [ ] AI review entries announce action type, review status, model/profile, and accept/reject/iterate actions.
- [ ] Upload confirmation/result windows announce destination, risk notes, URL fields, QR path action, retry/delete availability, and final status.
- [ ] Share history/queue announces selected item status and queue action availability.
- [ ] Save notes: `screen-reader.md`.

Pass criteria: the operator can understand the purpose and state of each key flow with screen-reader output, even if the UI still needs future accessibility polish.

## High Contrast And Text Scaling

- [ ] Test Windows text scaling at 125%.
- [ ] Test Windows text scaling at 150%.
- [ ] Test Windows text scaling at 200% if usable on the display.
- [ ] Test at least one Windows high-contrast theme.
- [ ] Verify no critical buttons, labels, status text, provider fields, or scroll areas overlap or become unreachable.
- [ ] Verify focus outlines and selected states remain visible.
- [ ] Capture screenshots for Main Window, Settings, Editor, Recording controls, AI review, Share History, and Upload Result.
- [ ] Save notes: `contrast-and-scaling.md`.

Pass criteria: core flows remain readable and operable with expected scrolling/reflow, and any visual defects are recorded with screenshots.

## Live Region Selection And Overlay

- [ ] Start an interactive region capture from the desktop app.
- [ ] Drag a region by hand on safe desktop content.
- [ ] Verify edge snapping can be observed and bypassed with Shift.
- [ ] Verify the pixel lens and size badge are visible while selecting.
- [ ] Verify padding/chooser behavior if configured.
- [ ] Verify cancel and complete behavior.
- [ ] Save safe screenshot/recording evidence only if it does not expose private desktop content.
- [ ] Save notes: `live-region-selection.md`.

Pass criteria: region selection feels controllable, recoverable, and visually clear during real mouse interaction.

## Multi-Monitor Capture And Recording

- [ ] Record monitor count, layout, scaling, and primary monitor.
- [ ] Capture active monitor.
- [ ] Capture all monitors.
- [ ] Capture a fixed region fully inside one monitor.
- [ ] Capture a cross-monitor region if layout allows.
- [ ] Record active monitor MP4 with safe content.
- [ ] Record all monitors MP4 with safe content.
- [ ] Verify output dimensions, visible content, and cursor/overlay behavior.
- [ ] Save notes: `multi-monitor.md`.

Pass criteria: capture and recording behavior matches the selected target, and any scaling/cropping issues are recorded.

## Long Recording Stability

- [ ] Record at least 30 minutes using a safe desktop scene.
- [ ] Include microphone if permission is granted.
- [ ] Include system audio loopback if available.
- [ ] Include webcam overlay if a safe camera scene is available.
- [ ] Use the intended quality profile, FPS, bitrate, timer, and overlay options.
- [ ] Verify output plays in a standard media player.
- [ ] Check audio/video duration drift and obvious sync issues.
- [ ] Save ffprobe output if `ffprobe` is installed.
- [ ] Save notes: `long-recording.md`.

Pass criteria: the recording completes, the file is playable, audio/video remain acceptably aligned, and resource or device failures are recorded.

## Clean-Machine Portable/Installer Proof

- [ ] Test the portable ZIP on a clean Windows user profile or VM.
- [ ] Launch the app.
- [ ] Confirm capture storage and app state folders are created in the expected user locations.
- [ ] Confirm Settings can be saved.
- [ ] Confirm a simple capture/import/edit/export loop.
- [ ] If Inno Setup installer tooling is available, compile and install the installer on a clean machine.
- [ ] Run `scripts\verify-installer-package.ps1` and attach the generated JSON/Markdown so compiler, installer artifact, and optional silent-smoke status are explicit.
- [ ] Confirm uninstall behavior does not remove user captures without explicit operator action.
- [ ] Save notes: `clean-machine.md`.

Pass criteria: the packaged app starts and completes a basic workflow on a clean environment, with installer status called out separately from portable ZIP proof.

## Live Provider Account Proof

OAuth providers remain parked unless this lane is explicitly scheduled.

- [ ] Google Drive OAuth consent, token save, upload, link creation, and expiry/retry behavior.
- [ ] Dropbox OAuth consent, token save, upload, temporary link, and expiry/retry behavior.
- [ ] OneDrive OAuth consent, token save, small upload, large upload session, anonymous link, and expiry/retry behavior.
- [ ] Non-OAuth/token providers with disposable test accounts: S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, Linear, WebDAV, FTP/FTPS, Slack, Discord, Microsoft Teams.
- [ ] Confirm upload history redacts secrets and queue retry/cancel behavior is understandable.
- [ ] Save notes: `live-provider-proof.md`.

Pass criteria: live account flows succeed with disposable or test accounts, consent/token failures are recoverable, and no credentials appear in logs or screenshots.

## Browser Extension Live Fixture

- [ ] Use only `browser-extension/samples/safe-fixture.html` unless another safe page is explicitly staged.
- [ ] Load the unpacked extension from the local `browser-extension/` folder.
- [ ] Copy the generated extension id and install the user-scope native host for the target browser.
- [ ] Run `goatshot browser-extension diagnostics --source <browser-extension-folder>` and save text/JSON output.
- [ ] Capture safe screenshots of the loaded extension details page, popup consent defaults, options consent defaults, popup/options Host Status result, selected-element mode, package-export toggle, and last handoff result.
- [ ] Run a consented fixture capture with screenshot consent and package export enabled.
- [ ] Import the downloaded `GoatShot/<correlationId>/` stitch package with `goatshot browser-extension receive --stitch-package`.
- [ ] Save redacted payload/import JSON, browser name/version, extension id, package folder, pass/fail result, and limitations.
- [ ] Save notes: `browser-extension-live-fixture.md`.

Pass criteria: the safe fixture proves browser-side extension loading, native-host reachability, consent UI, package export, and native package import for the tested browser only.

## Android Safe Device Proof

- [ ] Stage safe phone content with no private notifications, chats, email, credentials, photos, or account data visible.
- [ ] Run `goatshot diagnostics android --json` and save output.
- [ ] If multiple ready devices are connected, record the selected serial and use `--device`.
- [ ] Run `goatshot capture android --output <safe-output.png> --device <serial> --json` only after the safe content check.
- [ ] Run bounded `goatshot capture android-video --duration <seconds> --output <safe-output.mp4> --device <serial> --json` only when safe motion/content is staged.
- [ ] Run `goatshot capture android-preview --strategy screencap-polling --duration 5 --device <serial> --json` as a dry-run preview plan and confirm it does not import streamed media.
- [ ] Verify outputs contain only safe content and record ADB path, device authorization state, duration, cleanup behavior, and any disconnect/timeout warnings.
- [ ] Save notes: `android-safe-device-proof.md`.

Pass criteria: Android screenshot/video import works against safe staged content, preview remains dry-run only, and production live streaming is not claimed.

## Final Manual Sign-Off

- [ ] All required lane notes exist.
- [ ] Any failures have a linked issue/TODO artifact or a clear follow-up note.
- [ ] Manual proof summary created at `artifacts/manual-validation/<yyyy-mm-dd>/summary.md`.
- [ ] Public claims updated to match what was actually proven.

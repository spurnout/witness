# Windows Screenshot + Screen Recording App Specification

**Working name:** GoatShot / ScreenForge / TBD
**Owner:** Matt
**Date:** 2026-06-11
**Primary platform:** Windows desktop installer
**Primary goal:** Build a reliable Screenpresso replacement with better GPU recording, Gemini-powered image editing, strong sharing/export workflows, and power-user automation.

**Implementation status note, 2026-06-27 / completion audit, 2026-06-28:** this file remains the product specification and roadmap source, while current implementation/readiness truth is tracked in `README.md`, `artifacts/current-implementation-completion-audit-2026-06-28.md`, `artifacts/v1-readiness-summary.md`, `artifacts/current-unimplemented-scope-2026-06-27.md`, and `artifacts/active-non-oauth-buildout-todos.md`. The local V1 candidate includes the native WPF/CLI app, local capture/recording/editing/export flows, provider/readiness diagnostics, executable Google Photos upload, YouTube upload, and OneNote export adapters, governed local plugin and browser-extension scaffolds, reviewed local-plugin activation metadata writes, plugin update Task Scheduler handoff generation plus explicit scheduler task status/register/unregister lifecycle commands, bounded Android helpers, file-drop print import, local admin-policy bundles, manual-validation proof tooling, clean-machine evidence recording, required desktop evidence recording, hardware evidence recording, browser publication evidence recording, deterministic and external-runner person mask generation, explicit hosted person-segmentation service handoff, stage-only person-segmentation model package acquisition, still-image and video-frame mask quality evaluation, read-only companion portal export/media review, loopback preview, and explicit self-hosted shared-token read-only preview. This spec does not imply completion of live OAuth consent/refresh/upload proof, clean-machine installer proof, full keyboard/screen-reader/text-scaling/high-contrast proof, long-run hardware recording stability, actual browser-store submission/review/signing/availability, permanent/store-managed browser-extension install, actual enterprise policy deployment, true Windows virtual-printer driver installation, automatic plugin install/trust/enable/allowlist/execute updates, hosted marketplace behavior, hosted portal accounts, first-class portal login, team sync, hosted media, remote/multi-user admin sync, live Android device video/preview proof, production Android streaming, bundled AI person-segmentation inference, first-party hosted segmentation account proof, automatic model inference, or broad segmentation model certification.

**Current manual-validation handoff update, 2026-06-27:** a fresh evidence folder is saved under `artifacts/manual-validation/2026-06-27-current-required-proof/` with tranche notes under `artifacts/tranche-current-manual-validation-handoff-2026-06-27/`. Command-backed baseline proof passed Release build and 542 Release tests. Desktop and hardware proof packets were collected, and the current generated findings list six release-blocking required human/clean-machine lanes, four hardware-gated claim boundaries, zero optional compatibility findings, and one parked live-provider lane. This refresh also hardens Android/ADB diagnostics so no-device probes return promptly and clean up isolated ADB server state.

**Current browser optional-lane closure update, 2026-06-27:** Browser Extension Live Fixture in `artifacts/manual-validation/2026-06-27-current-required-proof/` is recorded as `NotApplicable` for the current V1 claim set, with decision evidence at `browser-extension-live-fixture/chrome-firefox-current-v1-decision.md` and tranche proof under `artifacts/tranche-browser-optional-lane-closure-2026-06-27/`. Edge live proof plus local multi-target browser-extension artifacts carry the current browser-extension claim; Chrome/Firefox live fixture proof must be reopened before advertising Chrome/Firefox live behavior.

**OAuth provider-proof-plan update, 2026-06-27/2026-06-28:** `goatshot oauth live-proof-plan` now emits provider-specific Google Drive, Google Photos, OneDrive, Dropbox, YouTube, and OneNote proof guidance for scope review, consent screenshots, account diagnostics, upload command hints, cleanup boundaries, and false mutation flags. Local proof is saved under `artifacts/tranche-oauth-provider-specific-proof-plan/` and the current focused source/test lane. This remains planning evidence only; real consent screens, live refresh-token recovery, live upload/cleanup proof, and redacted account evidence remain manual/parked.

**OAuth evidence-recorder update, 2026-06-27:** `goatshot oauth record-live-evidence` now records reviewed live OAuth evidence references for configured providers without opening a browser, contacting providers, exchanging codes, storing tokens, refreshing tokens, uploading files, or deleting remote files. A passed record requires consent, exchange, refresh, upload, cleanup, and account evidence categories. Local proof is saved under `artifacts/tranche-oauth-live-evidence-recorder/`; actual real-account proof remains manual/parked until an operator runs and reviews it.

**Companion portal self-hosted preview update, 2026-06-27:** `companion-portal serve` remains read-only and loopback-only by default, but can now be explicitly opened as a self-hosted shared-token preview with `--self-hosted --accept-remote-clients --auth-token-env <name>`. This is not hosted portal accounts, account login, team sync, hosted media, or remote admin; those remain later modules.

**Reviewed plugin activation update, 2026-06-27:** `plugins activate <plugin-id> --trust --enable --enable-local-plugins (--allow-action <action-id>|--allow-all-actions) --accept-risk` now provides a first-class reviewed local-plugin activation metadata step. It writes trust, enablement, global local-plugin enablement, and action allowlist settings only after explicit acceptance, honors managed policy, and reports that no plugin process was started. Local proof is saved under `artifacts/tranche-plugin-reviewed-activation/`. Automatic install/trust/enable/allowlist/execute update behavior and hosted marketplace trust remain later-scope.

**Plugin update scheduler handoff update, 2026-06-27:** `plugins schedule-updates --registry <registry> [--mode check-only|stage-only] [--interval-hours 24] [--output <folder>]` now generates a local Task Scheduler handoff for the governed `plugins background-updates` runner. It writes a schedule manifest plus run/register/unregister scripts, rejects sensitive registry URLs unless explicitly accepted, does not register a task by itself, and does not install, trust, enable, allowlist, or execute plugin code. Local proof is saved under `artifacts/tranche-plugin-update-scheduler-handoff/`. Hosted marketplaces and automatic plugin install/trust/enable/allowlist/execute update behavior remain later-scope.

**Plugin update task lifecycle update, 2026-06-27:** `plugins update-task <status|register|unregister> --manifest <plugin-update-schedule.json>` now provides an explicit Task Scheduler lifecycle for generated plugin update schedules. `status` queries Task Scheduler, `register` requires `--accept-task-registration`, `unregister` requires `--accept-task-removal`, `--dry-run` proves register/unregister wiring without mutating Task Scheduler, and reported process arguments/output are redacted. Local proof is saved under `artifacts/tranche-plugin-update-task-lifecycle/`. This still does not install, trust, enable, allowlist, or execute plugin code and does not provide hosted marketplace or automatic trust/execute update behavior.

**Person segmentation runner update, 2026-06-27:** `video person-mask` now provides a governed local external-runner contract for person-segmentation mask generation. It requires a local runner executable, `{input}` and `{output}` placeholders in the runner argument template, explicit `--accept-external-runner`, optional local `--model`, timeout bounds, output verification, and workspace indexing as `PersonSegmentationMaskVideo`. GoatShot still does not bundle, trust, enable, register, run, host, or broadly quality-certify a segmentation model.

**Hosted person segmentation service update, 2026-06-27:** `video hosted-person-mask <video> --endpoint <url> --accept-hosted-service [--api-key-env NAME]` now provides a governed hosted service handoff for mask generation. It requires explicit source-upload acceptance, posts multipart source media to an operator-supplied HTTP(S) endpoint, supports token lookup from an environment variable, writes the binary mask-video response, and can index the result as `PersonSegmentationMaskVideo`. Local proof is saved under `artifacts/tranche-hosted-person-segmentation-service/`. This is not a first-party hosted account, provider certification, bundled model inference, or broad model-quality guarantee.

**Person segmentation model package staging update, 2026-06-27:** `video person-model validate|stage --manifest <manifest.json> [--accept-download]` now validates `goatshot.person-segmentation-model.v1` manifests and stages local or explicitly accepted remote model files after schema, id, URI, size, and SHA-256 checks. Local proof is saved under `artifacts/tranche-person-segmentation-model-package-staging/`. This is stage-only acquisition: it does not run inference, trust, enable, register a runner, contact a hosted segmentation service, or certify a model.

**Mask quality evaluator update, 2026-06-27:** `video mask-quality` now compares generated still masks or generated mask videos against reviewed reference masks/videos and writes Markdown/JSON evidence with IoU, Dice, precision, recall, accuracy, threshold settings, frame counts, per-frame metrics for videos, and explicit false mutation/model-download/service-contact flags. Still-image proof is saved under `artifacts/tranche-video-mask-quality-evaluator/`; mask-video frame proof is saved under `artifacts/tranche-video-mask-quality-video-evaluator/`. This is a supplied-evidence evaluator, not bundled model inference, hosted segmentation service, automatic model inference, or whole-model certification.

**Clean-machine proof-kit update, 2026-06-27:** `manual-validation clean-machine-kit --folder <evidence-folder>` now writes a clean Windows VM/profile proof kit with portable-package discovery, SHA-256 manifest, operator runbook, evidence checklist, and optional `--copy-package` transfer bundle. Local proof is saved under `artifacts/tranche-clean-machine-proof-kit/`. This is still a proof helper only; actual clean-machine GUI proof, compiled installer creation, and installer install/uninstall proof remain manual until an operator runs and records them.

**Clean-machine evidence-recorder update, 2026-06-27:** `manual-validation record-clean-machine-evidence --folder <evidence-folder> --status passed|failed|blocked|pending` now records reviewed clean-machine/installer evidence references without launching GoatShot, running installers, installing or uninstalling software, mutating user profiles, capturing screens, certifying a clean machine, or updating the manual lane. A passed record requires machine/profile, package, hash, diagnostics, paths, first-launch, settings, capture/export, installer, and privacy-review evidence categories; helper-script output is recommended evidence. Local proof is saved under `artifacts/tranche-clean-machine-evidence-recorder/`; the actual clean Windows VM/profile run, human GUI click-through, compiled installer creation, and installer install/uninstall proof remain manual until an operator runs, reviews, and records the lane.

**Required desktop evidence-recorder update, 2026-06-27:** `manual-validation record-desktop-evidence --folder <evidence-folder> --lane keyboard|screen-reader|text-scaling|high-contrast|live-region-drag --status passed|failed|blocked|pending` now records reviewed required-desktop evidence references without launching GoatShot, changing Windows settings, capturing or recording the screen, mutating the user profile, certifying accessibility, or updating manual lane files. A passed record requires lane-specific categories for keyboard traversal, screen-reader output, text scaling, high contrast, or live region drag. Local proof is saved under `artifacts/tranche-required-desktop-evidence-recorder/`; the actual human keyboard traversal, Narrator/NVDA observation, text-scaling, high-contrast, and live drag proof remain manual until an operator runs, reviews, and records the lane.

**Hardware evidence-recorder update, 2026-06-27:** `manual-validation record-hardware-evidence --folder <evidence-folder> --lane multi-monitor-capture|multi-monitor-recording|long-recording|android-safe-device-proof --status passed|failed|blocked|pending` now records reviewed hardware/device evidence references without capturing or recording the desktop, contacting Android devices, importing phone media, changing device settings, certifying hardware, mutating the user profile, or updating manual lane files. A passed record requires lane-specific categories for multi-monitor capture, multi-monitor recording, long-run recording, or Android safe-device proof. Local proof is saved under `artifacts/tranche-hardware-evidence-recorder/`; actual live multi-monitor capture, multi-monitor recording, long-run recording, Android device proof, and safe-content review remain manual until an operator runs, reviews, and records the lane.

**Browser publication evidence-recorder update, 2026-06-27:** `browser-extension record-publication-evidence --target chrome|edge|firefox --status passed|failed|blocked|pending` now records reviewed browser-store publication evidence references without contacting store accounts, uploading packages, submitting reviews, signing or publishing listings, installing extensions, mutating browser profiles, registering native hosts, or applying enterprise policy. A passed record requires package, account, submission, review, signing, listing, and install evidence categories. Local proof is saved under `artifacts/tranche-browser-publication-evidence-recorder/`; actual store submission/review/signing/listing/install/enterprise proof remains manual until an operator runs and records it.

**Manual validation baseline update, 2026-06-15:** `manual-validation baseline --folder <evidence-folder> --run-commands` is implemented and locally proven under `artifacts/tranche-manual-baseline-proof/`. The earlier June 15 manual folder has command-backed Release build/test, CLI diagnostics, diagnostics bundle, recording/device/provider/capture-engine probes, and raw JSON evidence for the baseline lane; it reports `Baseline Setup` as passed. Keyboard traversal, screen-reader, text-scaling, high-contrast, live region drag, clean-machine GUI proof, hardware/device proof, and OAuth/live-provider proof remain separate manual or parked lanes. Chrome/Firefox live fixture proof must be reopened before advertising Chrome/Firefox live proof.

**Manual desktop-proof update, 2026-06-15:** `manual-validation desktop-proof --folder <evidence-folder> --run-commands` is implemented and locally proven under `artifacts/tranche-manual-desktop-accessibility-proof/`. The earlier June 15 manual folder includes app-owned screenshots, WPF focus/name audits, current-machine environment evidence, command logs, and blocked notes for the six required desktop lanes. The safe proof scene is implemented as an app-owned WPF staging surface: `GoatShot.exe --proof-scene` opens it for operators, `--render-proof-scene-output <png>` captures it for evidence, and `--audit-wpf-surface proof-scene` records focus/name evidence. Product Design/WPF proof for the new surface is saved under `artifacts/product-design-audit/2026-06-15/safe-proof-scene/`, and tranche evidence is saved under `artifacts/tranche-safe-proof-scene/`. The refreshed summary was redaction-clean with no issues, but the proof plan still reported six required open lanes because automation does not replace human keyboard traversal, Narrator/NVDA observation, Windows text-scaling/high-contrast checks, live region drag, or clean-machine GUI proof.

**Manual desktop-proof summary update, 2026-06-27:** `manual-validation desktop-proof` now also writes `desktop-proof/desktop-proof-summary.md` and `desktop-proof/desktop-proof-summary.json`; local proof is saved under `artifacts/tranche-manual-desktop-proof-summary/`. The summary consolidates command counts, failed commands, expected/missing evidence, current-machine accessibility environment fields, remaining human lanes, next operator steps, and the explicit claim boundary that the packet is preparation evidence only. This closes a local reporting/handoff gap; it still does not perform keyboard traversal, Narrator/NVDA observation, Windows text-scaling/high-contrast checks, live region drag, clean-machine GUI proof, or accessibility certification.

**Manual operator-pack update, 2026-06-27:** `manual-validation operator-pack --folder <evidence-folder> [--output <folder>]` is implemented and locally proven under `artifacts/tranche-required-desktop-operator-pack/`, with the current packet regenerated under `artifacts/manual-validation/2026-06-27-current-required-proof/required-desktop-operator-pack/`. It reads the current requirement-aware proof plan and writes a required desktop operator packet with a consolidated checklist, per-lane notes files, a PowerShell command reference, and an operator-pack manifest under `required-desktop-operator-pack/`. This packet makes the remaining six required human/clean-machine lanes easier to run and record through `manual-validation record-lane`; it does not perform or certify keyboard traversal, screen-reader behavior, text scaling, high contrast, live region dragging, clean-machine GUI proof, or accessibility compliance.

**Manual hardware-proof update, 2026-06-15:** `manual-validation hardware-proof --folder <evidence-folder> --run-commands` is implemented and locally proven under `artifacts/tranche-hardware-readiness-proof/`. The earlier June 15 manual folder includes `hardware-proof/` readiness evidence for recording preflight/devices, recording diagnostics, device diagnostics, WGC capture-engine diagnostics, Android diagnostics, display topology, FFmpeg/ffprobe detection, command logs, and command-result metadata. It updates multi-monitor capture, multi-monitor recording, long-recording, and Android safe-device lane files as `Blocked` with explicit claim boundaries. This is readiness evidence only: live multi-monitor capture/recording, long-run recording stability, and safe Android device media proof remain unproven until an operator stages safe hardware/device content and records a passed lane.

**Manual hardware-proof summary update, 2026-06-27:** `manual-validation hardware-proof` now also writes `hardware-proof/hardware-proof-summary.md` and `hardware-proof/hardware-proof-summary.json`; local proof is saved under `artifacts/tranche-manual-hardware-proof-summary/`. The summary consolidates command counts, nonzero diagnostics, expected/missing evidence, current display/FFmpeg environment fields, remaining hardware-gated lanes, next operator steps, and the explicit claim boundary that the packet is readiness evidence only. This closes a local reporting/handoff gap; it still does not perform live multi-monitor capture, multi-monitor recording, long-run recording stability, Android safe-device media proof, or safe-content review.

**Proof-scene recording smoke update, 2026-06-15:** `GoatShot.exe --record-proof-scene-output <mp4> [--record-proof-scene-duration <seconds>]` is implemented and locally proven under `artifacts/tranche-proof-scene-recording-smoke/`. It opens the app-owned proof-scene WPF window, records explicit window bounds with microphone/system-audio/webcam disabled, writes a `.proof.json` sidecar, and verifies the retained MP4 through `diagnostics recording-media`. The MP4 recording service now paces slower WGC/frame-composition delivery by duplicating the last good frame into missed constant-FPS slots, preventing shortened output duration. This is a private-safe bounded recording smoke only; live multi-monitor, long-run, audio-sync, webcam-permission, clean-machine, Android, and OAuth proof remain separate lanes.

**Manual validation findings update, 2026-06-27:** `manual-validation findings --folder <evidence-folder> [--output <folder>]` is implemented and locally proven under `artifacts/tranche-manual-validation-findings/`, with current findings regenerated under `artifacts/manual-validation/2026-06-27-current-required-proof/manual-validation-findings.md`. It refreshes the manual-validation summary, writes `manual-validation-findings.md` and `.json`, and sorts release-blocking required proof gaps, hardware-gated claim boundaries, optional compatibility gaps, redaction risks, and parked OAuth/live-provider scope. The current required-proof folder reports six required human/clean-machine proof findings, four hardware-gated boundaries, zero optional compatibility findings, one parked OAuth/live-provider lane, and zero redaction findings.

**Manual lane recording update, 2026-06-15:** `manual-validation record-lane --folder <evidence-folder> --lane <lane> --status passed|failed|blocked|pending|not-applicable --note "<operator note>" [--evidence <path>]` is implemented and locally proven under `artifacts/tranche-manual-lane-update-helper/`. It updates generated lane Markdown with a single checked result, redacted notes, normalized evidence references, and operator-update metadata, while requiring notes for blocked/failed lanes, rejecting not-applicable for required lanes, and preserving private-path redaction. This helper records manual evidence consistently; it does not perform the remaining human accessibility, clean-machine, hardware, Android-device, browser-store, or OAuth/live-account proof.

---

## 1. Product Summary

This product is a **Windows-native screenshot and screen recording application**. It should feel as fast and convenient as Screenpresso, as configurable as ShareX, as polished as Snagit/CleanShot, and smarter than all of them where AI and automation matter.

The app is **not** a Docker container and should not require a web server, database server, or self-hosted stack to run. It installs locally on Windows and works as a normal desktop utility from the tray, global hotkeys, and optional startup entry.

### Core thesis

> Capture anything on the screen, annotate or edit it quickly, record smooth GPU-backed videos, optionally use Gemini to manipulate screenshots, and share/export captures to wherever the user already works.

### Primary user

A technical Windows power user who takes lots of screenshots and short videos for:

- Client support
- Bug reports
- Documentation
- Internal notes
- Tutorials
- Async explanations
- AI-assisted screenshot cleanup/editing
- Sharing captures through multiple destinations

---

## 2. Hard Requirements From Matt

1. **Windows install, not Docker.**
   - Must ship as a Windows desktop app.
   - Docker/self-hosting may exist later only as an optional companion service, not as a requirement.

2. **GPU support is required.**
   - Screenshot capture and video recording should use modern Windows graphics APIs where possible.
   - Video encoding should support hardware acceleration where available.

3. **Gemini editing is required.**
   - The built-in image editor must support prompt-based image manipulation through Gemini.
   - The Gemini integration must be resilient to model changes and deprecations.

4. **Screenpresso-like sharing is required.**
   - User must be able to publish/share captures to multiple places.
   - Sharing should include local, clipboard, cloud, developer/workflow, and custom destinations.

5. **Feature theft is encouraged.**
   - Borrow good ideas aggressively.
   - Do not clone branding, assets, exact UI, or proprietary implementation details.
   - Adapt proven workflows into a better Windows-native product.

---

## 3. Research Sources

The feature inventory below is based on public product pages/docs for:

- [Screenpresso feature tour](https://www.screenpresso.com/features/)
- [Screenpresso 2.2.8 release notes](https://www.screenpresso.com/releases/screenpresso-2-2-8/)
- [ShareX feature page](https://getsharex.com/)
- [Snagit feature page](https://www.techsmith.com/snagit/features/)
- [CleanShot X features](https://cleanshot.com/features)
- [PicPick](https://picpick.app/)
- [Loom AI](https://www.loom.com/ai)
- [ScreenPal AI](https://screenpal.com/ai)
- [Cap](https://cap.so/)
- [OBS Studio](https://obsproject.com/)
- [Microsoft Windows.Graphics.Capture docs](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [Microsoft Windows.Media.Ocr docs](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)
- [Microsoft Windows App SDK Text Recognition docs](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition)
- [Gemini API image generation/editing docs](https://ai.google.dev/gemini-api/docs/image-generation)
- [Gemini API model deprecation docs](https://ai.google.dev/gemini-api/docs/deprecations)
- [Microsoft Snipping Tool support page](https://support.microsoft.com/en-us/windows/use-snipping-tool-to-capture-screenshots-00246869-1843-655f-f220-97299b865f6b)
- [Greenshot](https://getgreenshot.org/)

---

# 4. Feature Theft List

This section is the explicit list of features to steal/adapt.

---

## 4.1 Steal From Screenpresso

Screenpresso is the main baseline because the goal is to replace it.

### Capture workflow

- `PrintScreen`-first capture workflow.
- One capture shortcut that can select full screen, window, region, or screen area based on cursor position.
- Edge snapping / window-area auto-detection while selecting.
- Region capture with pixel-accurate zoom lens.
- Fullscreen capture.
- Active window capture.
- Specific window capture.
- Scrolling window capture with stitching.
- Capture mouse cursor option.
- Delayed capture for context menus and hover states.
- Capture context around the selected area.

### Workspace/library

- Automatically save every capture to a workspace/history panel.
- Thumbnail-based capture library.
- Fast access to recent images, videos, and generated documents.
- Drag captures from the workspace into other apps.
- Toolbar actions for new capture, edit, copy, publish, print, and organize.
- Real thumbnail drag feedback when dragging from workspace.

### Image editor

- Built-in vector image editor.
- Arrows.
- Rectangles.
- Ellipses.
- Text boxes.
- Callouts.
- Speech bubbles.
- Spotlight area.
- Step numbering tool with auto-increment.
- Crop.
- Drop shadow.
- Rounded corners.
- Reflection effect.
- Torn edges.
- Border effects.
- Generative AI image modification from the editor.

### Video recording

- MP4 output.
- Lightweight video files for sharing.
- Record screen.
- Record webcam.
- Record microphone/system audio.
- Export frames/images from video.
- Crop video.
- Mute video.
- Change video speed.
- Convert/change video format.
- Automatically generate subtitles from speech.
- Save subtitles as `.srt`.
- Merge subtitles into MP4.
- Basic noise gate.
- Webcam overlay with modern rendering.
- HEVC codec support.
- 4K/60fps target for pro-quality recordings.
- GPU-backed screen capture engine.
- GPU-backed video capture engine.
- HDR capture improvements.
- Multi-monitor GPU recording inspiration.

### Privacy/redaction

- Automatic detection and blur for sensitive content.
- Detect email addresses.
- Detect credit-card-like values.
- Detect IP addresses.
- Detect API keys.
- Apply blur during screenshot capture and real-time video recording.
- Let user remove or adjust blurred sections later.
- Use built-in Windows OCR rather than bundling brittle third-party OCR when possible.

### Sharing

Screenpresso-like publishing is a major requirement. Steal/adapt these destinations and behaviors:

- Instant public link sharing equivalent.
- Email attachment.
- Drag-anywhere publish behavior.
- Google Drive upload.
- Google Photos media upload.
- YouTube upload for video.
- Microsoft OneDrive upload.
- Microsoft OneNote export.
- Dropbox upload.
- Linear attachment / issue workflow.
- Imgur upload.
- Cloudinary upload.
- SFTP upload.
- Amazon S3 upload.
- Custom script publishing.
- Proxy-friendly networking.
- OAuth token refresh that does not randomly break every afternoon like some cursed enterprise SSO ritual.

### Other

- OCR from screenshots.
- QR code capture/decode.
- Android device capture as a later optional feature.
- User guide/document generator.
- MSI deployment inspiration.
- Admin policy inspiration.
- Disable-AI policy key equivalent.

---

## 4.2 Steal From ShareX

ShareX is the power-user automation and extensibility target.

### Capture modes

- Fullscreen capture.
- Active window capture.
- Active monitor capture.
- Window menu.
- Monitor menu.
- Region capture.
- Lightweight region capture.
- Transparent region capture.
- Last region.
- Custom region.
- Screen recording.
- GIF recording.
- Scrolling capture.
- Auto capture / repeated interval capture.

### After-capture task system

- Show quick task menu.
- Show after-capture window.
- Beautify image.
- Add image effects.
- Open in image editor.
- Copy image to clipboard.
- Pin to screen.
- Print image.
- Save image to file.
- Save image as.
- Save thumbnail image.
- Run custom actions.
- Copy file to clipboard.
- Copy file path to clipboard.
- Show file in Explorer.
- Scan QR code.
- Recognize text with OCR.
- Show before-upload window.
- Upload image to host.
- Delete local file after upload if configured.

### Region annotation tools

- Rectangle region.
- Ellipse region.
- Freehand region.
- Rectangle annotation.
- Ellipse annotation.
- Freehand drawing.
- Freehand arrow.
- Line.
- Arrow.
- Text with outline.
- Text with background.
- Speech balloon.
- Step marker.
- Magnify.
- Image-from-file stamp.
- Image-from-screen stamp.
- Stickers.
- Cursor overlay.
- Smart eraser.
- Blur.
- Pixelate.
- Highlight.
- Crop.
- Cut out.

### Upload and workflow automation

- Upload file.
- Upload folder.
- Upload from clipboard.
- Upload text.
- Upload from URL.
- Drag-and-drop upload.
- Shorten URL.
- Watch folder.
- After-upload window.
- Share URL.
- Copy URL to clipboard.
- Open URL.
- Show QR code for uploaded URL.
- Advanced custom uploader support.
- Customizable workflow system.
- Export/import workflow profiles.

### Productivity tools

- Color picker.
- Screen color picker.
- Ruler.
- Pin screenshot to screen.
- Image editor.
- Image beautifier.
- Image effects.
- Image viewer.
- Image combiner.
- Image splitter.
- Image thumbnailer.
- Video converter.
- Video thumbnailer.
- OCR.
- QR code tool.
- Hash checker.
- Metadata viewer/remover.
- Directory indexer.
- Clipboard viewer.
- Borderless window helper.
- Inspect window.
- Monitor test.

---

## 4.3 Steal From Snagit

Snagit is the professional documentation and polished editing target.

### AI and documentation

- AI step capture: automatically generate visual step-by-step guides from clicks.
- AI smart redact: hide sensitive information with one click.
- AI simplify: convert detailed screenshots into simplified graphics.
- Local post-recording background-noise reduction for recordings; provider/model-based AI noise removal remains optional later work.
- Text recognition that can copy, edit, or delete text within screenshots.
- Step recorder for documentation.

### Screenshot capture

- Scrolling capture for long pages and large data sets.
- Time-delay capture.
- Cursor editing after capture.
- Custom capture presets.
- Custom keyboard shortcuts for presets.
- Menu/object capture.
- Exact capture dimensions.
- Time-lapse capture.
- Multiple area capture in a single image.
- Printer capture via local file-drop/PDF-image import plus driver-feasibility diagnostics; OS virtual-printer driver install remains later.

### Recording

- Draw on screen while recording.
- Add arrows/shapes/step numbers while recording.
- Webcam capture.
- Picture-in-picture recording.
- Webcam overlay shape/size control.
- Swap focus between screen and webcam.
- Create video from screenshots/images with narration.
- Combine clips.
- Record microphone and system audio.

### Annotations

- Arrows.
- Callouts.
- Shapes.
- Step tool.
- Stamps/emojis.
- Spotlight.
- Magnify.
- Add capture info such as OS/date/app info.

---

## 4.4 Steal From CleanShot X

CleanShot is Mac-only, but its UX ideas are worth stealing shamelessly and rebuilding for Windows.

### UX / capture polish

- Modern quick-access overlay.
- Simple editor with high performance.
- Drag-me button for dragging the capture to another app.
- Dark/light mode support.
- Capture area/window/fullscreen/scrolling window.
- Timer/self-timer.
- Window screenshots with adjustable padding.
- Window screenshots with background/shadow options.
- Transparent window screenshots.
- Crosshair.
- Magnifier.
- Freeze screen selection.
- All-in-one mode with one shortcut for all capture modes.
- Lock aspect ratio.
- Save last selection for easy retakes.

### Recording

- Record selected window, fullscreen, or custom area.
- MP4 H.264 output.
- GIF output.
- Control quality/FPS/resolution.
- Record microphone.
- Record computer audio.
- Automatically enable Do Not Disturb while recording.
- Show/hide cursor.
- Recording timer display.
- Hide desktop clutter while recording.
- Capture clicks with style/size/animation options.
- Capture keystrokes with position/size/style options.
- Camera overlay with position/size/shape/fullscreen options.
- Built-in video editor.
- Trim.
- Change quality.
- Convert stereo audio to mono.
- Playback recorded video.
- Change resolution.
- Adjust volume or mute audio.

### Sharing and organization

- Upload screenshots/videos and get a share link.
- Self-destruct/expiring links inspiration.
- Password-protected links inspiration.
- Cloud optional, not required.
- Custom domain/branding inspiration for future.
- Screenshot tagging.
- Floating screenshots always on top.
- Adjust floating screenshot size and opacity.
- Arrow-key positioning.
- Lock mode to interact with apps beneath a pinned screenshot.
- Local/on-device OCR that copies selected text to clipboard.

---

## 4.5 Steal From PicPick

PicPick is useful as an all-in-one visual utility target.

- Screen capture.
- Screen recording.
- Image editor.
- Color picker.
- Color palette.
- Magnifier.
- Pixel ruler.
- Crosshair.
- Protractor.
- Whiteboard.
- Lightweight Windows utility feel.

---

## 4.6 Steal From Greenshot

Greenshot’s value is simplicity and reliability.

- Region/window/fullscreen capture.
- Scrolling web page capture inspiration.
- Easy annotation.
- Highlighting.
- Obfuscation/blur/pixelation.
- Export to file.
- Export to printer.
- Copy to clipboard.
- Attach to email.
- Export to Office-style apps.
- Plugin-style export targets.
- Keep basic screenshot workflows fast and unfussy.

---

## 4.7 Steal From Loom

Loom is the async video and AI bug-reporting target.

- Fast screen/webcam recording.
- Shareable video links.
- AI-generated titles.
- AI-generated summaries.
- AI-generated chapters.
- Filler word removal.
- Silence removal.
- Turn video into a document.
- Turn video into Jira/Linear issue.
- Turn video into share message/email.
- AI bug reports with technical context.
- Capture device/browser/OS info for bug reports.
- Capture console/network context later through browser extension.

---

## 4.8 Steal From ScreenPal

ScreenPal is the AI video workflow target.

- AI video titles.
- AI video summaries.
- AI transcripts.
- AI captions.
- AI chapters.
- AI translation.
- Text-based video editing.
- Automatic filler word removal.
- Automatic silence removal.
- AI-generated quiz/questions as a far-later feature.
- AI speech-to-text.
- Video keyed-background and external-mask background blur/removal/replacement for reviewed webcam clips; local external-runner person mask generation, explicit hosted segmentation handoff, stage-only person model package acquisition, plus still-image and video-frame mask quality evaluation are implemented, while bundled model inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification remain later scope.
- AI image-to-text for screenshots.

---

## 4.9 Steal From Cap

Cap is relevant because it is local-first and Windows-friendly.

- Record locally, share only when the user chooses.
- Own your recordings.
- Connect user-owned Google Drive.
- Connect user-owned S3 bucket.
- 4K/60fps recording target.
- Instant mode for quick sharing.
- Studio mode for local editing.
- Composite recording: camera and screen captured separately, rendered together.
- Multiple recording layouts.
- Custom branding.
- Fast native Windows app inspiration.
- Keyboard shortcuts.

---

## 4.10 Steal From OBS Studio

OBS is too big to clone, but its recording/audio architecture is useful.

- High-performance real-time video/audio capture and mixing.
- Multiple sources concept as a future advanced mode.
- Webcam source.
- Window/display source.
- Audio mixer.
- Per-source audio filters.
- Noise gate.
- Noise suppression through local post-recording FFmpeg denoise export.
- Gain.
- Hotkeys.
- Plugin mindset, but not full OBS complexity.

---

## 4.11 Steal From Windows Snipping Tool

Windows Snipping Tool defines the baseline we must exceed.

- `Win + Shift + S` style screenshot overlay expectation.
- `Win + Shift + R` style video clip expectation.
- Rectangle capture.
- Window capture.
- Fullscreen capture.
- Freeform capture.
- Clipboard-first behavior.
- Native-feeling Windows UX.

---

# 5. Product Scope

## 5.1 MVP Scope

The MVP should replace Matt’s daily Screenpresso usage.

### MVP must include

- Windows installer.
- Tray app.
- Run at startup option.
- Configurable global hotkeys.
- Region capture.
- Window capture.
- Fullscreen capture.
- Active monitor capture.
- All-monitor capture.
- Last-region capture.
- Fixed-size capture presets.
- Delayed capture.
- Cursor include/exclude.
- Magnifier while selecting.
- Crosshair while selecting.
- Freeze-screen selection.
- Basic scrolling capture.
- Auto-save workspace.
- Thumbnail history.
- Built-in editor.
- Annotations.
- Redaction tools.
- Local OCR.
- QR decode.
- GPU-backed video recording.
- MP4/H.264 export.
- Hardware encoder support where available.
- Mic audio.
- System audio.
- Webcam overlay.
- Cursor highlight/click visualization.
- Trim video.
- Export frame from video.
- GIF export.
- Gemini screenshot editing.
- Share/export to local file, clipboard, email, Google Drive, Google Photos, OneDrive, Dropbox, S3-compatible, SFTP, Imgur, Cloudinary, Linear, custom script, and custom webhook.

## 5.2 V1 Scope

The first full release should make the app feel better than Screenpresso.

- More robust scrolling capture.
- Advanced sharing profiles.
- Upload history.
- Short-link support through supported providers.
- OCR-indexed workspace search.
- Sensitive-data auto-detection.
- One-click redact.
- AI screenshot summary/explanation.
- AI image cleanup/editing.
- AI-generated bug reports from recordings.
- Speech-to-text transcript.
- `.srt` subtitle generation.
- AI video summary/title/chapters.
- Documentation generator.
- Step recorder.
- Markdown/PDF/DOCX/HTML export.
- CLI.
- Watch folder.
- Workflow rules.

## 5.3 Later Scope

- Browser extension for better web scrolling capture and bug-report telemetry.
- Jira/GitHub issue creation.
- Screen capture from Android devices.
- OS virtual-printer driver install and clean-machine printer proof beyond the local file-drop import and diagnostics path.
- Composite camera/screen recording.
- Advanced video editor beyond reviewed silence/transcript/filler cut-plan export, reviewed composite layout export, deterministic foreground mask generation, local external-runner person mask generation, explicit hosted segmentation handoff, stage-only person model package acquisition, still-image/video-frame mask quality evaluation, reviewed keyed-background export, and reviewed mask/matte export: bundled AI person-segmentation inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification.
- Hosted plugin marketplace and automatic install/trust/enable/allowlist/execute plugin update behavior beyond the governed local registry/staging/install-staged/reviewed-activation/update-apply/background-check/scheduler-handoff/explicit scheduler task lifecycle/read-only marketplace planning scaffold.
- Optional hosted portal accounts, first-class account login, team sync, hosted/remote media hosting, and remote/multi-user admin beyond the local static export, opt-in local media-review pages, loopback preview, and explicit self-hosted shared-token read-only preview.
- Team/admin mode.

---

# 6. Non-Goals

- Do not build a Docker-only product.
- Do not require a server to capture, edit, record, or share through normal destinations.
- Do not clone the Screenpresso UI pixel-for-pixel.
- Do not create a full OBS replacement.
- Do not create a full Premiere/DaVinci-style video editor.
- Do not make AI mandatory for normal capture/edit/share workflows.
- Do not hard-code one Gemini preview model and then act surprised when Google murders it.
- Do not upload captures anywhere unless the user explicitly chooses that behavior or configures an automation rule.
- Do not store unencrypted cloud credentials in plaintext.

---

# 7. Platform and Installation Requirements

## 7.1 Target OS

- Windows 11 primary target.
- Windows 10 support preferred where APIs are available.
- Use feature detection for capture APIs and hardware encoders.
- Gracefully degrade or hide unsupported features.

## 7.2 Installer

Required install options:

- Standard `.exe` installer.
- MSI installer for machine-wide deployment.
- Optional MSIX package if useful.
- Optional portable ZIP build later.

Installer must support:

- Per-user install.
- Machine-wide install.
- Start menu shortcut.
- Desktop shortcut option.
- Run at startup option.
- Auto-update channel selection.
- Stable/beta update channels.
- Clean uninstall.
- Preserve user data on uninstall unless user opts to remove it.

## 7.3 Process model

- Tray/background process for hotkeys and capture overlay.
- Main workspace window.
- Editor window.
- Recorder controller window/overlay.
- Background upload worker.
- Background OCR/index worker.
- Background AI worker.

---

# 8. Core UX

## 8.1 Tray behavior

Tray menu should include:

- Capture region.
- Capture window.
- Capture fullscreen.
- Capture active monitor.
- Capture scrolling window.
- Start recording.
- Open workspace.
- Open recent capture.
- Upload from clipboard.
- OCR from region.
- Color picker.
- Settings.
- Exit.

## 8.2 Hotkeys

Default hotkeys:

| Action | Default |
|---|---|
| All-in-one capture | `PrintScreen` |
| Region capture | `Ctrl + PrintScreen` |
| Window capture | `Alt + PrintScreen` override optional |
| Last region | `Shift + PrintScreen` |
| Start/stop recording | `Ctrl + Shift + R` |
| OCR region | `Ctrl + Shift + O` |
| Color picker | `Ctrl + Shift + C` |
| Open workspace | `Ctrl + Shift + W` |

Requirements:

- Every hotkey must be user-configurable.
- Detect conflicts with Windows/app hotkeys.
- Allow disabling any hotkey.
- Export/import hotkey profiles.

## 8.3 Capture overlay

The overlay must support:

- Multi-monitor selection.
- Mixed DPI scaling.
- Crosshair.
- Magnifier.
- Pixel dimensions while dragging.
- Coordinate display optional.
- Freeze screen mode.
- Object/window edge detection.
- Snap to window/control/monitor.
- Hold modifier to disable snapping.
- Hold modifier to lock aspect ratio.
- Hold modifier to draw from center.
- Keyboard nudging.
- Confirm/cancel via keyboard.
- Immediate copy/save depending on selected workflow.

---

# 9. Screenshot Capture Requirements

## 9.1 Capture modes

| Feature | Priority | Notes |
|---|---:|---|
| Region capture | P0 | Rectangle selection. |
| Window capture | P0 | Hover or list picker. |
| Active window capture | P0 | Fast hotkey. |
| Fullscreen capture | P0 | Entire virtual desktop or selected monitor. |
| Active monitor capture | P0 | Capture current cursor monitor. |
| All monitors capture | P0 | Single combined image. |
| Last region capture | P0 | Repeat exact previous region. |
| Fixed-size region | P0 | Presets like 1920x1080, 1280x720, custom. |
| Delayed capture | P0 | 3s/5s/10s/custom. |
| Cursor capture | P0 | Include/exclude cursor. |
| Freeform capture | P1 | Snipping Tool / ShareX style. |
| Multi-region capture | P1 | Multiple regions into one image. |
| Time-lapse capture | P2 | Same region at interval. |
| Printer capture | P2 | Local PDF/image file-drop import and driver-feasibility diagnostics are implemented; OS virtual-printer driver install remains later. |

## 9.2 Scrolling capture

MVP:

- Browser scrolling capture for common Chromium-based browsers.
- Basic app scrolling capture using simulated scroll and image stitching.
- Manual stitch fallback.
- Preview final stitched image before save.

V1:

- Better overlap detection.
- Horizontal scrolling capture.
- Capture large tables/data grids.
- Exclude sticky headers/footers if detected.
- Browser extension optional for perfect DOM/page capture.

---

# 10. Image Editor Requirements

## 10.1 Editor model

- Use a non-destructive editing model while editing.
- Store original image plus edit operations in project metadata.
- Export flattened images for sharing.
- Redactions must be truly flattened/destructive on export.
- Warn if exporting project file with hidden/unflattened sensitive content.

## 10.2 Annotation tools

| Tool | Priority |
|---|---:|
| Arrow | P0 |
| Line | P0 |
| Rectangle | P0 |
| Rounded rectangle | P0 |
| Ellipse | P0 |
| Freehand pen | P0 |
| Highlighter | P0 |
| Text box | P0 |
| Callout | P0 |
| Speech bubble | P0 |
| Step number | P0 |
| Spotlight | P0 |
| Magnifier bubble | P0 |
| Blur | P0 |
| Pixelate | P0 |
| Solid redaction block | P0 |
| Cursor overlay | P1 |
| Stamps/emojis | P1 |
| Smart eraser | P1 |
| Image stamp from file | P1 |
| Image stamp from screen | P1 |
| Background removal | P2/AI |
| Simplify screenshot | P2/AI |

## 10.3 Image transforms

- Crop.
- Resize.
- Rotate.
- Flip.
- Canvas resize.
- Padding.
- Border.
- Drop shadow.
- Rounded corners.
- Torn edge.
- Cutout.
- Background fill.
- Transparent background support.
- Export preview with file size estimate.

## 10.4 Style system

- Global style presets.
- Per-tool style presets.
- Recently used colors.
- Brand/theme presets.
- Copy/paste style.
- Save annotation templates.

---

# 11. Workspace / Library Requirements

## 11.1 Local storage

- Auto-save all captures by default.
- Configurable storage folder.
- Configurable retention policy.
- Private capture mode that does not save history.
- SQLite metadata database.
- Separate original files from exported/generated variants.

## 11.2 Workspace UI

- Thumbnail history.
- Filter by type: image, video, document, upload.
- Search by filename.
- Search by app/window title.
- Search by date/time.
- Search by tags.
- Search by OCR text.
- Favorite/pin captures.
- Project/folder grouping.
- Open in editor.
- Open containing folder.
- Copy image.
- Copy file.
- Copy path.
- Copy Markdown image link.
- Copy upload URL.
- Drag to other apps.
- Multi-select operations.
- Batch export/upload/delete.

## 11.3 Metadata to store

- Capture ID.
- File path.
- Original file path.
- Thumbnail path.
- Capture type.
- Capture date/time.
- Monitor info.
- DPI scale.
- Source app process name.
- Source app window title.
- Source URL if browser extension exists later.
- Dimensions.
- File size.
- OCR text.
- OCR bounding boxes.
- Tags.
- Favorite flag.
- Upload records.
- Redaction status.
- AI action history.

---

# 12. Video Recording Requirements

## 12.1 Capture modes

| Feature | Priority |
|---|---:|
| Region recording | P0 |
| Window recording | P0 |
| Monitor recording | P0 |
| Active monitor recording | P0 |
| All-monitor recording | P1 |
| Webcam-only recording | P1 |
| Composite screen + camera recording | P2 |
| Browser tab recording via extension | P2 |

## 12.2 GPU and encoding

Required:

- Use Windows.Graphics.Capture or equivalent modern capture path where possible.
- Use Direct3D frame pipeline.
- Prefer zero-copy GPU path where practical.
- Encode MP4/H.264 using hardware acceleration when available.
- Detect NVIDIA NVENC.
- Detect Intel Quick Sync / QSV / oneVPL path.
- Detect AMD AMF.
- Fall back to software encoding if hardware encoding fails.
- Provide useful diagnostics when GPU recording is unavailable.

Codec support:

| Codec | Priority | Notes |
|---|---:|---|
| H.264 MP4 | P0 | Default compatibility. |
| HEVC/H.265 MP4 | P1 | Smaller files, optional. |
| AV1 | P2 | Future hardware support. |
| WebM | P2 | Dev/web workflows. |
| GIF | P0 | Short clips only. |

Quality profiles:

- Small file.
- Balanced.
- High quality.
- Lossless/near-lossless optional.
- 720p.
- 1080p.
- 1440p.
- 4K.
- 30fps.
- 60fps.
- Custom bitrate.
- Variable bitrate.
- Constant bitrate.

## 12.3 Audio

Required:

- Microphone recording.
- System audio recording through Windows loopback.
- Audio level meters.
- Device picker.
- Mute mic hotkey.
- Mute system audio hotkey.
- Audio sync correction.
- Noise gate.
- Gain control.
- Noise suppression through local post-recording FFmpeg denoise export.
- Optional compressor/limiter later.

## 12.4 Webcam overlay

- Enable/disable webcam.
- Select camera.
- Position overlay.
- Resize overlay.
- Shape: circle, rounded rectangle, square, vertical rounded rectangle.
- Mirror webcam option.
- Background blur/replacement later.
- Fullscreen webcam mode later.
- Swap screen/webcam focus later.

## 12.5 Recording overlays

- Show/hide cursor.
- Cursor highlight.
- Click animations.
- Left/right click distinction.
- Keystroke overlay.
- Recording border.
- Recording timer.
- Countdown.
- Pause/resume indicator.
- Hide desktop icons/clutter.
- Enable Do Not Disturb / Focus Assist while recording if possible.
- Draw arrows/shapes while recording later.

## 12.6 Video editor

MVP:

- Trim start/end.
- Mute audio.
- Adjust volume.
- Change export quality.
- Change resolution.
- Export frame as image.
- Convert short clip to GIF.

V1:

- Cut middle section.
- Crop video.
- Change speed.
- Merge clips.
- Add intro/outro image.
- Burn subtitles into video.
- Export `.srt` subtitles.

V2:

- Text-based editing through reviewed transcript/SRT cut plans.
- Silence removal through reviewed cut plans.
- Filler word removal through reviewed cut plans.
- Deterministic foreground mask generation through `generate-mask`, local external-runner person mask generation through `person-mask`, explicit hosted segmentation handoff through `hosted-person-mask`, stage-only person model package acquisition through `person-model`, still-image/video-frame mask quality evaluation through `mask-quality`, plus webcam keyed-background and mask/matte-backed background blur/removal/replacement through reviewed `apply-background` export plans; bundled model inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification remain later scope.
- Composite camera/screen layout editor through reviewed `apply-composite` export plans.

---

# 13. Gemini Editing Requirements

## 13.1 Required Gemini features

The editor must support Gemini-powered image manipulation.

User flow:

1. User captures or opens screenshot.
2. User clicks **AI Edit**.
3. User enters prompt.
4. App sends current image and prompt to Gemini.
5. App returns edited image as a new layer/version.
6. User can accept, reject, compare, or iterate.

Example prompts:

- “Remove the personal information from this screenshot.”
- “Blur everything except the error message.”
- “Make this screenshot look cleaner for documentation.”
- “Remove the desktop clutter in the background.”
- “Highlight the button the user should click.”
- “Replace the background with a neutral office background.”
- “Make this screenshot suitable for a user guide.”
- “Translate the visible Spanish text to English and preserve the layout.”
- “Generate a simplified diagram from this UI screenshot.”

## 13.2 Gemini model strategy

Requirements:

- Do not hard-code one preview model.
- Maintain a model manifest.
- Support stable Gemini image models first.
- Default to the fastest/cheapest capable image-editing model.
- Allow user to choose higher-quality model.
- Include model health check.
- Include model capability check.
- Include friendly error messages for quota, billing, invalid API key, model shutdown, unsupported feature, and region restrictions.
- Cache provider/model metadata.
- Keep model IDs configurable.

As of this spec date, Google documents Gemini native image generation/editing under the Nano Banana family, including image-editing-capable models. Use Google’s current docs as implementation source of truth when coding, because AI model names are basically mayflies with API keys.

## 13.3 AI provider abstraction

Even though Gemini is required, design this as an AI provider layer:

```text
AI Provider Interface
  - ValidateCredentials()
  - ListModels()
  - GetCapabilities(model)
  - EditImage(image, prompt, options)
  - AnalyzeImage(image, prompt, options)
  - TranscribeAudio(audio, options)
  - SummarizeVideo(transcript, keyframes, options)
```

Initial provider:

- Gemini API.

Later providers:

- OpenAI.
- Azure OpenAI.
- Local Ollama/LM Studio for non-image tasks.
- Local ONNX/DirectML for OCR/redaction assistance.

## 13.4 AI privacy controls

- AI disabled by default until configured.
- User supplies Gemini API key.
- Explain that screenshots sent to Gemini leave the machine.
- Per-request confirmation option.
- “Never send captures from these apps” denylist.
- “Never send captures containing detected secrets” option.
- Local-only mode.
- Admin setting to disable AI entirely.
- Do not log full prompts/images by default.
- Redact API keys from logs.

## 13.5 AI image actions

MVP:

- Prompt-based edit.
- Screenshot cleanup.
- Blur/redact by instruction.
- Highlight by instruction.
- Generate alt text.
- Explain screenshot.

V1:

- Smart redact suggestions.
- Translate screenshot text.
- Simplify screenshot into documentation graphic.
- Remove background/clutter.
- Generate documentation caption.
- Generate bug report from screenshot.

V2:

- Multi-turn conversational editing.
- Region-aware AI editing.
- Style presets.
- Batch AI processing.

---

# 14. OCR, QR, and Redaction Requirements

## 14.1 OCR

Required:

- Local OCR from screenshot.
- OCR selected region.
- Copy OCR text to clipboard.
- Store OCR text in workspace index.
- Store OCR bounding boxes.
- OCR language selection.
- OCR confidence score where available.

Implementation preference:

- Use Windows built-in OCR APIs where available.
- Evaluate Windows App SDK Text Recognition for NPU-capable devices.
- Fallback to Windows.Media.Ocr or another local OCR path.

## 14.2 QR/barcode

- Decode QR from screenshot.
- Decode QR from selected region.
- Show decoded value.
- Copy decoded value.
- Open URL after confirmation.
- Generate QR for upload URL later.

## 14.3 Sensitive data detection

Detect and optionally redact:

- Email addresses.
- Phone numbers.
- IP addresses.
- Credit-card-like values.
- API keys.
- Bearer tokens.
- JWTs.
- AWS keys.
- GitHub tokens.
- Connection strings.
- URLs with tokens.
- Password fields if visually detectable.

## 14.4 Redaction safety

- Blur and pixelate are visual hiding tools.
- Solid redaction is the safest default.
- Exported redactions must be flattened.
- Cropped-out pixels must be removed from exported file.
- Metadata stripping option.
- Warn when copying an unflattened/layered project.

---

# 15. Sharing and Publishing Requirements

Sharing should be Screenpresso-like but less brittle.

## 15.1 Sharing architecture

Use provider adapters:

```text
Share Provider Interface
  - ProviderName
  - AuthType
  - ValidateCredentials()
  - Upload(file, metadata, options)
  - GetShareLink(uploadResult)
  - DeleteRemote(uploadId)
  - RefreshToken()
  - SupportsPublicLinks
  - SupportsPrivateLinks
  - SupportsExpiration
  - SupportsPassword
```

## 15.2 MVP destinations

| Destination | Priority | Notes |
|---|---:|---|
| Local folder | P0 | Always available. |
| Clipboard image | P0 | Paste into apps. |
| Clipboard file | P0 | Paste/upload as file. |
| Clipboard path | P0 | Power-user useful. |
| Markdown image link | P0 | Docs/GitHub. |
| Email attachment | P0 | Default mail client/Outlook. |
| Google Drive | P0 | Required Screenpresso-like cloud sharing. |
| OneDrive | P0 | Required Windows ecosystem. |
| Dropbox | P0 | Common sharing target. |
| YouTube | P1 | Video publishing. |
| OneNote | P1 | Documentation/notebook export. |
| S3-compatible | P0 | AWS S3, Cloudflare R2, MinIO, Backblaze B2 if S3-compatible. |
| SFTP | P0 | Durable nerd-friendly upload. |
| Imgur | P0 | Quick public image sharing. |
| Cloudinary | P0 | Image/video CDN workflows. |
| Linear | P0/P1 | Attach to issue or create link workflow. |
| Custom script | P0 | Replaces Screenpresso custom script idea. |
| Custom webhook | P0 | POST file + JSON metadata. |

## 15.3 V1/V2 destinations

| Destination | Priority | Notes |
|---|---:|---|
| Google Photos | P2 | Optional. |
| Slack | P1 | Share to channel/DM. |
| Discord | P1 | Webhook upload. |
| Microsoft Teams | P1 | Share to chat/channel. |
| GitHub Issues | P1 | Attach image/video to issue workflow. |
| Jira | P1 | Bug report creation. |
| Azure DevOps | P2 | Enterprise/dev workflow. |
| WebDAV | P1 | Nextcloud/etc. |
| FTP/FTPS | P2 | Legacy support. |
| Generic OAuth2 provider | P2 | Maybe overkill. |

## 15.4 Sharing UX

- Quick-share button.
- Favorite destinations.
- Per-hotkey destination profiles.
- Ask-before-upload option.
- Upload progress.
- Upload history.
- Copy link automatically after upload.
- Open link after upload option.
- Generate QR for link.
- Delete remote upload if provider supports it.
- Retry failed upload.
- Proxy settings.
- Token refresh handling.
- Clear credential error messages.

## 15.5 Custom script contract

Custom scripts receive:

- File path.
- Capture type.
- Metadata JSON path.
- OCR text path if available.
- Source app/window info.
- Project/workspace ID.

Script output:

- Exit code.
- Optional JSON result.
- Optional URL to copy to clipboard.
- Optional message to display.

Example command:

```powershell
my-uploader.ps1 -File "{file}" -Metadata "{metadata}" -CaptureId "{id}"
```

---

# 16. Documentation and Bug Report Features

## 16.1 Step recorder

- Capture screenshot on click.
- Auto-number steps.
- Record window/app title per step.
- OCR visible UI text.
- Let user edit each step title/body.
- Export as Markdown.
- Export as PDF.
- Export as DOCX.
- Export as HTML.

## 16.2 Document generator

Templates:

- How-to guide.
- Client support guide.
- Bug report.
- SOP.
- Release note.
- Troubleshooting guide.

## 16.3 AI-generated docs

- Generate guide from step recorder.
- Generate guide from recording transcript and keyframes.
- Generate summary.
- Generate title.
- Generate table of contents.

## 16.4 Bug report mode

MVP/V1:

- Record screen.
- Capture user narration.
- Capture screenshots/keyframes.
- Collect OS version.
- Collect app/window title.
- Collect display/resolution/GPU info.
- Generate Markdown bug report.

Later browser extension:

- Current URL.
- User agent.
- Console errors.
- Network failures.
- Browser version.
- Local storage/session storage preview with privacy controls.

Bug report output fields:

- Title.
- Summary.
- Environment.
- Steps to reproduce.
- Expected result.
- Actual result.
- Attachments.
- Video link.
- Screenshot links.
- Logs/context.

---

# 17. Automation Requirements

## 17.1 Workflow rules

Rules should be trigger-based:

```text
When capture created
  If capture type == image
  If source app == chrome.exe
  If hotkey profile == ClientSupport
Then
  Open editor
  Run OCR
  Detect sensitive data
  Ask before upload
  Upload to Google Drive
  Copy share link
```

Triggers:

- Capture created.
- Capture edited.
- File added to watch folder.
- Recording completed.
- OCR completed.
- Upload completed.
- AI action completed.

Conditions:

- Capture type.
- Source app.
- Window title contains.
- Monitor.
- Hotkey profile.
- File size.
- File extension.
- OCR contains text.
- Sensitive data detected.

Actions:

- Open editor.
- Copy to clipboard.
- Save to folder.
- Run OCR.
- Redact detected sensitive data.
- Apply image effect.
- Upload to provider.
- Run script.
- Call webhook.
- Generate document.
- Delete local file.
- Show notification.

## 17.2 CLI

Provide a CLI for automation:

```powershell
goatshot capture region --output "C:\Temp\shot.png"
goatshot capture window --process chrome --copy
goatshot record region --duration 30 --output "demo.mp4"
goatshot ocr "shot.png"
goatshot upload "shot.png" --provider s3 --profile personal
goatshot ai-edit "shot.png" --prompt "remove personal info"
```

## 17.3 Watch folders

- Watch local folders.
- Auto-import images/videos.
- Auto-process using workflow rules.
- Avoid processing temporary/partial files.

---

# 18. Utility Tools

MVP/P1:

- Color picker.
- Screen color picker.
- Pixel ruler.
- Magnifier.
- Pin screenshot to screen.
- QR decode.
- OCR region.
- Metadata remover.

P2:

- Color palette manager.
- Crosshair.
- Protractor.
- Whiteboard.
- Image combiner.
- Image splitter.
- Hash checker.
- Clipboard viewer.
- Directory indexer.
- Window inspector.
- Monitor test.
- Borderless window helper.

---

# 19. Settings

Settings areas:

- General.
- Startup.
- Hotkeys.
- Capture.
- Editor.
- Recording.
- Audio.
- Webcam.
- Workspace/library.
- File naming.
- Export formats.
- Sharing providers.
- AI/Gemini.
- OCR/redaction.
- Automation rules.
- Privacy/security.
- Proxy/network.
- Updates.
- Advanced/debug.

## 19.1 Filename templates

Support variables:

```text
{date}
{time}
{datetime}
{capture_type}
{app}
{process}
{window_title}
{monitor}
{width}
{height}
{counter}
{project}
```

Example:

```text
{date}-{time}-{app}-{capture_type}-{counter}.png
```

---

# 20. Security and Privacy

## 20.1 Credential storage

- Store secrets using Windows Credential Manager or DPAPI.
- Never store raw API keys in logs.
- Exported settings should omit secrets by default.
- Optional encrypted settings backup.

## 20.2 Local-first defaults

- Captures remain local unless user uploads.
- AI features disabled until configured.
- Cloud providers disabled until configured.
- Private capture mode available.
- Per-app denylist for saving history.
- Per-app denylist for uploading.
- Per-app denylist for AI.

## 20.3 Logging

- Structured logs.
- Verbose mode for debugging.
- Redact tokens/secrets from logs.
- Include provider error codes.
- Include GPU/encoder diagnostics.
- “Copy diagnostic bundle” command.

---

# 21. Technical Architecture

## 21.1 Proposed stack

| Layer | Recommendation |
|---|---|
| UI | .NET 8/9 with WinUI 3 or WPF |
| Capture | Windows.Graphics.Capture, Direct3D11 |
| Encoding | Media Foundation and/or FFmpeg wrapper |
| Image rendering/editor | SkiaSharp, Win2D, or Direct2D |
| Metadata | SQLite |
| Search | SQLite FTS5 for OCR/search |
| OCR | Windows.Media.Ocr + Windows App SDK Text Recognition where available |
| AI | Gemini API adapter first; provider interface for future |
| Sharing | Provider adapter system |
| Installer | WiX/MSI + EXE bootstrapper; optional MSIX |
| Updates | Squirrel.Windows, Velopack, or custom updater |
| Config | JSON settings + encrypted secret store |
| CLI | .NET console entry point or app subcommand |

## 21.2 Modules

```text
GoatShot.App
  - Tray
  - Workspace
  - Editor
  - Settings
  - Recorder UI

GoatShot.Capture
  - Screenshot capture
  - Window/monitor detection
  - Scrolling capture
  - Cursor capture
  - DPI/HDR handling

GoatShot.Recording
  - Frame capture
  - Encoder selection
  - Audio capture
  - Webcam capture
  - Overlay rendering

GoatShot.Editor
  - Canvas
  - Annotation model
  - Redaction model
  - Export renderer

GoatShot.Library
  - File store
  - SQLite metadata
  - OCR index
  - Thumbnails
  - Retention rules

GoatShot.OCR
  - Windows OCR adapter
  - Text boundaries
  - QR decode
  - Sensitive data detector

GoatShot.AI
  - Provider abstraction
  - Gemini provider
  - Prompt templates
  - Safety/privacy gates

GoatShot.Sharing
  - Provider abstraction
  - OAuth/token handling
  - Upload queue
  - Custom script/webhook

GoatShot.Automation
  - Rules engine
  - Watch folders
  - CLI actions

GoatShot.Diagnostics
  - Logs
  - GPU diagnostics
  - Provider diagnostics
  - Support bundle
```

## 21.3 Capture pipeline

```text
Hotkey
  -> Capture overlay
  -> Select target/region
  -> Acquire frame(s)
  -> Normalize HDR/DPI
  -> Save original
  -> Generate thumbnail
  -> Run configured after-capture workflow
  -> Open editor / copy / upload / OCR / notify
```

## 21.4 Recording pipeline

```text
Start recording
  -> Resolve capture target
  -> Start Windows.Graphics.Capture session
  -> Start audio capture
  -> Start webcam capture if enabled
  -> Composite overlays
  -> Encode using hardware encoder if available
  -> Write MP4
  -> Generate thumbnail/keyframes
  -> Optional speech-to-text
  -> Run after-recording workflow
```

## 21.5 AI image edit pipeline

```text
Open editor
  -> User selects AI Edit
  -> Check provider credentials
  -> Check model availability/capability
  -> Check privacy rules
  -> Send image + prompt to Gemini
  -> Receive edited image
  -> Store as AI-generated version/layer
  -> User accepts/rejects/iterates
  -> Export flattened result
```

---

# 22. Data Storage Layout

Default per-user paths:

```text
%LOCALAPPDATA%\GoatShot\
  app.db
  logs\
  cache\
  thumbnails\
  temp\

%USERPROFILE%\Pictures\GoatShot\
  Images\
  Videos\
  Documents\
  Projects\
```

Configurable:

- Capture library root.
- Temp folder.
- Export folder.
- Cloud sync-safe location.

---

# 23. Acceptance Criteria

## 23.1 Screenshot acceptance

- Pressing `PrintScreen` opens capture overlay in under 250ms on a normal machine.
- Region capture produces a PNG with correct pixels on mixed-DPI monitors.
- Window capture captures the intended window without random borders or wrong monitor scaling.
- Last-region capture repeats the exact previous rectangle.
- Delayed capture can capture context menus.
- Captures appear in workspace automatically.
- User can annotate and export within 10 seconds without touching file dialogs.

## 23.2 Recording acceptance

- User can record a 1080p/60fps monitor with mic/system audio to MP4.
- User can record 4K/60fps when hardware supports it.
- App selects hardware encoding automatically when available.
- If hardware encoding fails, app falls back to software and tells the user.
- Webcam overlay stays synced.
- Audio/video drift is not noticeable over a 10-minute recording.
- Recording can be paused/resumed.
- User can trim and export a recording.

## 23.3 Gemini acceptance

- User can configure Gemini API key.
- App validates credentials.
- App lists/uses a current image-editing-capable Gemini model.
- User can send screenshot + prompt and receive edited image.
- Failed requests show actionable errors.
- App does not hard-code deprecated preview model IDs.
- AI can be disabled globally.

## 23.4 Sharing acceptance

- User can upload one image to Google Drive, Google Photos, Dropbox, S3-compatible, SFTP, Imgur, Cloudinary, and custom webhook/script.
- User can copy resulting URL to clipboard.
- Upload progress and failure reasons are visible.
- OAuth tokens refresh without repeated login when provider supports refresh.
- Proxy settings can be configured.

## 23.5 Privacy acceptance

- Private capture mode does not save capture to workspace.
- Redacted exports cannot recover original pixels.
- API keys are not visible in logs.
- AI requests are blocked for denylisted apps.
- Upload requests are blocked for denylisted apps.

---

# 24. Milestones

## Milestone 0 — Prototype

- Tray app.
- Global hotkey.
- Region capture.
- Save PNG.
- Workspace thumbnail.
- Basic editor canvas.
- Basic MP4 recording prototype.
- GPU/encoder diagnostics proof-of-concept.

## Milestone 1 — Screenshot MVP

- Full capture overlay.
- Region/window/fullscreen/monitor/last-region/delay.
- Magnifier/crosshair/freeze selection.
- Editor tools: arrow, rectangle, text, callout, step, blur, pixelate, crop.
- Auto-save workspace.
- Clipboard/file/path export.
- Basic sharing: local, email, custom script/webhook.

## Milestone 2 — Recording MVP

- GPU-backed recording.
- Hardware encoder selection.
- MP4/H.264.
- Mic/system audio.
- Webcam overlay.
- Cursor/click highlight.
- Pause/resume.
- Trim.
- GIF export.

## Milestone 3 — Sharing MVP

- Google Drive.
- OneDrive.
- Dropbox.
- S3-compatible.
- SFTP.
- Imgur.
- Cloudinary.
- Linear.
- Upload queue/history.
- Provider diagnostics.

## Milestone 4 — OCR/Redaction MVP

- Local OCR.
- OCR region hotkey.
- OCR workspace indexing.
- QR decode.
- Sensitive data detection.
- One-click redact.
- Metadata stripping.

## Milestone 5 — Gemini MVP

- Gemini API key setup.
- Model manifest.
- Health/capability check.
- Prompt-based image edit.
- AI action history.
- Accept/reject/iterate workflow.
- Privacy controls.

## Milestone 6 — V1 Polish

- Robust scrolling capture.
- Documentation generator.
- Step recorder.
- AI screenshot summary/explain.
- AI video transcript/title/summary/chapters.
- Subtitle export.
- Workflow rules.
- CLI.
- Watch folders.
- Installer/updater polish.

---

# 25. Open Review Questions

These are not blockers, just things to decide during review.

1. App name: GoatShot, ScreenForge, CaptureForge, something else?
2. Minimum Windows version: Windows 11 only, or Windows 10 support?
3. UI stack preference: WPF stability vs WinUI 3 modern feel?
4. Should the editor use SkiaSharp, Win2D, or Direct2D?
5. Should FFmpeg be bundled, optional, or avoided?
6. Which sharing destinations are absolutely required in the first usable build?
7. Should uploads default to “ask every time” or “auto-upload by workflow profile”?
8. Should Gemini be BYO API key only, or eventually support OAuth/account sign-in?
9. Should the app have a paid/pro feature split eventually, or stay personal/internal?
10. Should the optional web sharing portal exist later, or keep all sharing provider-based?

---

# 26. Recommended Build Priority

My recommended priority order:

1. **Capture overlay + workspace** — the daily muscle memory.
2. **Editor + redaction** — screenshots need to become useful immediately.
3. **GPU recording** — a core requirement and technical risk.
4. **Sharing providers** — Screenpresso replacement requirement.
5. **Gemini image editing** — cool differentiator, but should sit on top of a reliable editor.
6. **OCR/search/redaction automation** — huge daily value.
7. **Docs/bug reports/AI video intelligence** — turns the app from utility into workflow weapon.

---

# 27. Guiding Principles

- Fast beats fancy.
- Local-first beats cloud-dependent.
- GPU-backed beats CPU-melting goblin mode.
- Sharing should be flexible, not trapped inside one vendor.
- AI should be powerful but optional.
- Redaction should be safe, not just visually blurry theater.
- Workflows should be configurable without making the settings screen look like a spaceship crashed into regedit.
- Every feature should answer: “Does this make screenshots or screen videos faster, clearer, safer, or easier to share?”

# GoatShot Current Implementation TODOs - OAuth Parked

Date: 2026-06-15

Purpose: continue the GoatShot buildout from the current native WPF/.NET baseline without blocking on live OAuth consent screens. Google Drive, OneDrive, Dropbox, YouTube, and OneNote proof-plan guidance plus local OAuth live-evidence recording are now implemented, while actual OAuth/provider-consent proof stays parked. The priority is to finish locally provable product work and keep every tranche backed by tests, diagnostics, screenshots or safe artifacts, package output, and honest notes.

No git workflow is required for this project right now.

Execution checklist: `artifacts/current-non-oauth-buildout-todo-plan-2026-06-15.md` is the current execution-shaped TODO plan. `artifacts/active-non-oauth-buildout-todos.md` remains the active implementation ledger, and `artifacts/remaining-buildout-todo-plan-oauth-parked.md` remains the broader historical TODO ledger.

Latest current build plan: `artifacts/current-build-plan-oauth-parked-2026-06-15.md`.

Continuation plan: `artifacts/non-oauth-continuation-plan.md`.

Current next-buildout plan: `artifacts/oauth-parked-next-buildout-plan.md`.

Forward execution TODOs: `artifacts/oauth-parked-forward-buildout-todos.md`.

Next forward implementation TODOs: `artifacts/next-forward-buildout-todos-oauth-parked.md`.

Remaining non-OAuth implementation plan: `artifacts/remaining-non-oauth-implementation-plan.md`.

Current continuation TODO plan: `artifacts/remaining-buildout-todos-oauth-parked-2026-06-15.md`.

Current consolidated remaining TODO plan: `artifacts/remaining-non-oauth-todos-2026-06-15.md`.

Newest continuation TODO plan: `artifacts/continue-implementation-todos-oauth-parked-2026-06-15.md`.

Current solid tranche TODO plan: `artifacts/next-solid-tranche-todos-oauth-parked-2026-06-15.md`.

Current what-is-left implementation TODO plan: `artifacts/what-is-left-implementation-todos-oauth-parked-2026-06-15.md`.

Current leftover buildout TODO plan: `artifacts/continue-leftover-buildout-todos-oauth-parked-2026-06-15.md`.

Current post-Edge continuation TODO plan: `artifacts/continue-implementation-leftover-todos-oauth-parked-2026-06-15.md`.

Current next non-OAuth implementation TODO plan: `artifacts/next-non-oauth-implementation-todos-2026-06-15.md`.

Current remaining non-OAuth TODO plan: `artifacts/current-remaining-non-oauth-todos-2026-06-15.md`.

Post-Edge manual validation summary repair: `artifacts/tranche-manual-summary-repair/`.

## Short Answer

OAuth consent screens are not the only thing left. They are the main parked live-account proof lane, but the ordinary non-OAuth planning/buildout tranches are now locally complete, including Android preview summary/contact-sheet review output, bounded H.264 stdout preview execution with optional FFmpeg remux, a V1 decision to keep production Android streaming out of scope beyond bounded ADB screenshot/video/preview paths, virtual-printer setup-note generation and watched-folder diagnostics, a V1 decision to keep virtual-printer support file-drop/import-only, browser extension operator diagnostics that distinguish source/package readiness, native-host missing/manifest-missing/registered-but-browser-proof-needed states, payload rejection diagnostics, stitch-package import readiness, `browser-extension live-fixture` helper/verifier scaffolding plus isolated Chrome/Edge launch-script generation, `browser-extension proof validate` manifest/missing-evidence/redaction checks, `browser-extension store-readiness` checklist/copy generation, `browser-extension store-package` local submission package generation, read-only `browser-extension install-plan` generation, temporary `browser-extension install-assist` generation, read-only `browser-extension publication-plan` generation, read-only `browser-extension enterprise-policy-plan` template generation, V1 decision that Chrome/Firefox live browser proof is optional/manual beyond completed Edge proof, local portable-package verifier/proof, packaged WPF first-launch render proof from isolated roots, guarded staged plugin activation into the active local plugin folder without trust/enablement/allowlist inheritance, passive plugin update summaries in CLI and Settings, governed plugin background update check/stage-only runs, plugin update Task Scheduler handoff generation, explicit plugin update Task Scheduler status/register/unregister lifecycle commands, operator-invoked plugin update apply/stage/install for already installed local plugins, read-only plugin marketplace planning, read-only local `companion-portal export`, loopback-only read-only `companion-portal serve`, opt-in local `companion-portal media-review`, Settings provider setup UX that groups ready, needs-setup, policy-blocked, implemented OAuth/live-proof-pending, and roadmap states, proof hygiene reset, current release proof refresh, a repaired post-Edge manual validation summary that marks Edge browser fixture proof passed while leaving unrun human/hardware lanes as `NotRun`, requirement-aware manual-validation summaries that separate required local-V1 desktop proof from hardware-gated, optional compatibility, and OAuth-parked claim boundaries, `manual-validation proof-plan` runbooks that turn the current summary into exact required-lane operator steps and evidence names, a fresh current manual desktop/accessibility runbook refresh under `artifacts/tranche-manual-desktop-accessibility-refresh/`, a manual-validation lane update that marks Chrome/Firefox live fixture proof `NotApplicable` for current V1 claims, and explicit later-module decisions that keep store publication, permanent/store-managed browser extension installation and managed deployment proof, automatic plugin install/trust/enable/allowlist/execute updates, hosted portal accounts/auth/sync/hosted-media, OS printer-driver installation, Android production streaming, and remote/team admin sync out of V1. Remaining work is in dedicated later/manual lanes: safe manual desktop/accessibility proof, clean Windows VM/human GUI click-through/installer proof, live Android device screenshot/video/preview proof if safe device content is staged, live Chrome/Firefox browser-side fixture proof only if those live compatibility claims are later advertised, and post-V1 account/hosted/driver/streaming/admin modules when explicitly scheduled.

2026-06-27 companion portal update: explicit self-hosted shared-token read-only preview is now implemented and locally proven under `artifacts/tranche-companion-portal-self-hosted-auth/`. This adds remote-client serving only when `--self-hosted`, `--accept-remote-clients`, and a token from `--auth-token-env` or `--auth-token` are supplied; it is still not hosted portal accounts, first-class account login, team sync, hosted media, remote admin, sync, upload, or write routes.

2026-06-27 person segmentation update: `goatshot video person-mask <video> --runner <exe> --runner-args "<template>" --accept-external-runner` is implemented and locally proven under `artifacts/tranche-person-segmentation-runner/`. This closes the local external-runner contract for model/person mask generation without bundling, trusting, enabling, registering, running, hosting, or broadly quality-certifying a segmentation model.

2026-06-27 person segmentation model package staging update: `goatshot video person-model validate|stage --manifest <manifest.json> [--accept-download]` is implemented and locally proven under `artifacts/tranche-person-segmentation-model-package-staging/`. This closes stage-only local/remote model package acquisition with manifest schema/id/URI/size/SHA-256 checks while keeping inference, trust, enablement, runner registration, hosted-service contact, and model certification out of V1.

2026-06-27 hosted person segmentation update: `goatshot video hosted-person-mask <video> --endpoint <url> --accept-hosted-service [--api-key-env NAME]` is implemented and locally proven under `artifacts/tranche-hosted-person-segmentation-service/`. This closes the governed hosted-service handoff with explicit source-upload acceptance, environment-variable token lookup, multipart upload, binary mask response handling, and workspace indexing while leaving first-party hosted accounts, real provider proof, bundled inference, and broad model certification out of V1.

2026-06-27 clean-machine proof-kit update: `goatshot manual-validation clean-machine-kit --folder <evidence-folder> [--portable-zip <zip>] [--installer <exe>] [--copy-package]` is implemented and locally proven under `artifacts/tranche-clean-machine-proof-kit/`. This closes the local kit-generation gap for clean Windows VM/profile handoff while leaving the actual VM run, human GUI click-through, compiled installer creation, and installer install/uninstall proof manual.

2026-06-27 clean-machine evidence recorder update: `goatshot manual-validation record-clean-machine-evidence --folder <evidence-folder> --status passed|failed|blocked|pending` is implemented and locally proven under `artifacts/tranche-clean-machine-evidence-recorder/`. This closes the local reviewed-evidence recording gap for clean-machine/installer proof while leaving the actual clean Windows VM/profile run, human GUI click-through, compiled installer creation, and installer install/uninstall proof manual.

2026-06-27 required desktop evidence recorder update: `goatshot manual-validation record-desktop-evidence --folder <evidence-folder> --lane keyboard|screen-reader|text-scaling|high-contrast|live-region-drag --status passed|failed|blocked|pending` is implemented and locally proven under `artifacts/tranche-required-desktop-evidence-recorder/`. This closes the local reviewed-evidence recording gap for required desktop proof while leaving actual keyboard traversal, Narrator/NVDA observation, text-scaling, high-contrast, and live region-drag proof manual.

2026-06-27 hardware evidence recorder update: `goatshot manual-validation record-hardware-evidence --folder <evidence-folder> --lane multi-monitor-capture|multi-monitor-recording|long-recording|android-safe-device-proof --status passed|failed|blocked|pending` is implemented and locally proven under `artifacts/tranche-hardware-evidence-recorder/`. This closes the local reviewed-evidence recording gap for hardware/device proof while leaving actual live multi-monitor capture, multi-monitor recording, long-run recording, Android device proof, and safe-content review manual.

Post-hardware-evidence-recorder release evidence is saved under `artifacts/tranche-release-proof-after-hardware-evidence-recorder/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 566 tests, CLI help, diagnostics print, diagnostics bundle, portable package generation, and release proof bundle generation passed; the formal release proof bundle included 165 files with 0 policy exclusions. This is now historical behind the current browser optional-lane closure release refresh.

2026-06-27 current browser optional-lane closure update: `manual-validation record-lane` marked Browser Extension Live Fixture as `NotApplicable` for current V1 claims under `artifacts/manual-validation/2026-06-27-current-required-proof/`, with decision evidence at `browser-extension-live-fixture/chrome-firefox-current-v1-decision.md` and tranche proof under `artifacts/tranche-browser-optional-lane-closure-2026-06-27/`. The current summary/proof-plan/findings report 6 required human/clean-machine lanes open, 4 hardware-gated lanes open, 0 optional compatibility lanes/findings open, and 1 parked OAuth/live-provider lane. Chrome/Firefox live proof is still not claimed and must be reopened before advertising it.

Post-Google-Photos-adapter release evidence is saved under `artifacts/tranche-release-proof-after-google-photos-adapter-2026-06-28/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 576 tests, CLI help, diagnostics print, diagnostics bundle, portable package generation, and release proof bundle generation passed; the formal release proof bundle included 164 files with 0 policy exclusions and the manifest lists Google Drive, Google Photos, Dropbox, OneDrive, YouTube, and OneNote live proof as still unverified. The previous YouTube/OneNote adapter proof remains available under `artifacts/tranche-release-proof-after-youtube-onenote-adapters-2026-06-28/`, the previous completion-audit release evidence remains available under `artifacts/tranche-release-proof-after-completion-audit-2026-06-28/`, and the previous browser optional-lane closure release evidence remains available under `artifacts/tranche-release-proof-after-browser-optional-lane-closure-2026-06-27/`.

2026-06-27 browser publication evidence recorder update: `goatshot browser-extension record-publication-evidence --target chrome|edge|firefox --status passed|failed|blocked|pending` is implemented and locally proven under `artifacts/tranche-browser-publication-evidence-recorder/`. This closes the local evidence-recording gap for browser-store publication/deployment proof while leaving actual store account submission, review, signing/listing availability, permanent or store-managed installation, browser profile mutation, native-host registration, and enterprise policy deployment manual.

2026-06-27 plugin update scheduler handoff update: `goatshot plugins schedule-updates --registry <registry> [--mode check-only|stage-only] [--interval-hours 24] [--output <folder>]` is implemented and locally proven under `artifacts/tranche-plugin-update-scheduler-handoff/`. This closes the local Task Scheduler handoff gap for the governed background update runner while leaving actual task registration/removal to the explicit `plugins update-task` lifecycle command and leaving package install, trust, enablement, action allowlists, plugin execution, hosted marketplaces, and automatic trust/execute updates as explicit/manual or later-scope boundaries.

2026-06-27 plugin update task lifecycle update: `goatshot plugins update-task <status|register|unregister> --manifest <plugin-update-schedule.json>` is implemented and locally proven under `artifacts/tranche-plugin-update-task-lifecycle/`. This closes the explicit Task Scheduler status/register/unregister command gap while preserving acceptance gates, dry-run proof, redacted command reporting, and the boundary that plugin install, trust, enablement, action allowlists, execution, hosted marketplaces, and automatic trust/execute updates remain separate explicit/manual or later-scope behavior.

2026-06-27 video mask quality evaluator update: `goatshot video mask-quality --generated-mask <png> --reference-mask <png>` is implemented and locally proven under `artifacts/tranche-video-mask-quality-evaluator/`. This closes the local still-mask comparison/evidence gap with IoU, Dice, precision, recall, and accuracy reporting while leaving bundled model inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification out of V1.

2026-06-27 video mask quality video evaluator update: `goatshot video mask-quality --generated-mask <video> --reference-mask <video>` is implemented and locally proven under `artifacts/tranche-video-mask-quality-video-evaluator/`. This closes local frame-by-frame mask-video quality evaluation for supplied generated/reference mask videos while leaving bundled model inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification out of V1.

Post-diagnostics-video-truth release evidence is saved under `artifacts/tranche-release-proof-after-diagnostics-video-truth/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 542 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, the formal release proof bundle command reported 166 included files with 0 policy exclusions, the latest ZIP is the timestamped `GoatShot-release-proof-0.1.0-*.zip` listed in `manifest.json`, and the current NuGet vulnerability scan reports no vulnerable `GoatShot.App` packages.

Current final release evidence refresh after the clean-profile WPF and manual desktop/accessibility refresh is saved under `artifacts/tranche-final-release-evidence-refresh/`.

Current manual baseline proof is saved under `artifacts/tranche-manual-baseline-proof/`. `goatshot manual-validation baseline --folder artifacts\manual-validation\2026-06-15-current-required-proof --run-commands` completed successfully, wrote command-backed diagnostics under the manual folder, and moved `Baseline Setup` to `Passed`. Current desktop-proof helper proof is saved under `artifacts/tranche-manual-desktop-accessibility-proof/`. `goatshot manual-validation desktop-proof --folder artifacts\manual-validation\2026-06-15-current-required-proof --run-commands` completed successfully, wrote app-owned screenshots, WPF focus/name audits, environment evidence, command logs, and blocked notes for the six required desktop lanes. Safe proof scene staging is saved under `artifacts/tranche-safe-proof-scene/` and `artifacts/product-design-audit/2026-06-15/safe-proof-scene/`; `GoatShot.exe --proof-scene` opens the private-safe WPF staging surface, `--render-proof-scene-output` renders it, and `--audit-wpf-surface proof-scene` writes focus/name evidence. Current lane-recording helper proof is saved under `artifacts/tranche-manual-lane-update-helper/`; `goatshot manual-validation record-lane --folder <folder> --lane <lane> --status passed|failed|blocked|pending|not-applicable --note "<operator note>" [--evidence <path>]` now updates generated lane files with redacted notes and normalized evidence references without hand-editing Markdown. The regenerated summary exits complete and redaction-clean, but the regenerated proof plan still reports 6 required open lanes because human keyboard traversal, screen-reader, text-scaling, high-contrast, live region drag, and clean-machine portable/GUI proof remain blocked; OAuth/live-provider proof remains parked.

2026-06-27 manual desktop-proof summary update: `manual-validation desktop-proof` now writes `desktop-proof/desktop-proof-summary.md` and `desktop-proof/desktop-proof-summary.json` with command counts, failed commands, expected/missing evidence, current-machine accessibility environment fields, remaining human lanes, next operator steps, and explicit claim boundaries. Local proof is saved under `artifacts/tranche-manual-desktop-proof-summary/`. This closes the desktop-proof packet summary/reporting gap only; it does not complete keyboard traversal, Narrator/NVDA observation, text-scaling, high-contrast, live region-drag, clean-machine GUI proof, or accessibility certification.

Post-manual-desktop-proof-summary release evidence is saved under `artifacts/tranche-release-proof-after-manual-desktop-proof-summary/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 554 tests, CLI help, diagnostics print, diagnostics bundle, portable package generation, and release proof bundle generation passed; the formal release proof bundle included 161 files with 0 policy exclusions.

Post-baseline release evidence is saved under `artifacts/tranche-final-evidence-refresh-after-manual-baseline/`: Release build, full Release tests, CLI help, CLI diagnostics print, CLI diagnostics bundle, and portable package all passed; the full suite passed 436 tests and the release proof bundle reported 0 policy exclusions.

Post-desktop-proof release evidence is saved under `artifacts/tranche-final-evidence-refresh-after-desktop-proof/`: Release build, full Release tests, CLI help, CLI diagnostics print, CLI diagnostics bundle, and portable package all passed; the full suite passed 439 tests and the release proof bundle reported 0 policy exclusions.

Post-lane-update release evidence is saved under `artifacts/tranche-final-evidence-refresh-after-manual-lane-update/`: Release build, full Release tests, CLI help, CLI diagnostics print, CLI diagnostics bundle, and portable package all passed; the full suite passed 444 tests and the release proof bundle reported 0 policy exclusions.

Post-safe-proof-scene release evidence is saved through `artifacts/tranche-safe-proof-scene/` and the refreshed formal bundle under `artifacts/tranche-release-proof-admin/`: Release build, full Release tests, CLI help, CLI diagnostics print, portable package, and release proof bundle generation all passed; the full suite passed 448 tests and the release proof bundle reported 104 included files with 0 policy exclusions.

Current hardware readiness proof is saved under `artifacts/tranche-hardware-readiness-proof/` and `artifacts/manual-validation/2026-06-15-current-required-proof/hardware-proof/`: `goatshot manual-validation hardware-proof --folder artifacts\manual-validation\2026-06-15-current-required-proof --run-commands` completed successfully, wrote recording/device/WGC/Android readiness evidence plus display topology and command logs, and moved the four hardware-gated lanes to `Blocked` with explicit claim boundaries. The regenerated summary/proof plan exits complete and redaction-clean. After the optional browser lane closure, it reports 6 required human lanes open, 4 hardware-gated lanes open, 0 optional compatibility lanes open, and 1 parked OAuth/live-provider lane. Live multi-monitor capture/recording, long-run recording stability, and safe Android device media proof remain unproven.

2026-06-27 manual hardware-proof summary update: `manual-validation hardware-proof` now writes `hardware-proof/hardware-proof-summary.md` and `hardware-proof/hardware-proof-summary.json` with command counts, nonzero diagnostics, expected/missing evidence, current display/FFmpeg environment fields, remaining hardware-gated lanes, next operator steps, and explicit claim boundaries. Local proof is saved under `artifacts/tranche-manual-hardware-proof-summary/`. This closes the hardware-proof packet summary/reporting gap only; it does not complete live multi-monitor capture, multi-monitor recording, long-run recording stability, Android safe-device media proof, or safe-content review.

Post-manual-proof-summaries release evidence is saved under `artifacts/tranche-release-proof-after-manual-proof-summaries/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 554 tests, CLI help, diagnostics print, diagnostics bundle, portable package generation, and release proof bundle generation passed; the formal release proof bundle included 166 files with 0 policy exclusions.

Post-hardware-readiness release evidence is saved under `artifacts/tranche-hardware-readiness-proof/` and refreshed formal bundle output under `artifacts/tranche-release-proof-admin/`: Release build, full Release tests, CLI help, CLI diagnostics print, portable package, and release proof bundle generation all passed; the full suite passed 451 tests and the release proof bundle command reported 105 included files with 0 policy exclusions.

Earlier browser optional-lane closure evidence is saved under `artifacts/tranche-browser-optional-lane-closure/`; current closure evidence is saved under `artifacts/tranche-browser-optional-lane-closure-2026-06-27/`. Browser Extension Live Fixture is recorded as `NotApplicable` for current V1 claims, with decision evidence inside `artifacts/manual-validation/2026-06-27-current-required-proof/`. The regenerated summary/proof plan reports 6 required human/clean-machine lanes open, 4 hardware-gated lanes open, 0 optional compatibility lanes open, and 1 parked OAuth/live-provider lane. Release build, full Release tests, CLI help, CLI diagnostics print, diagnostics bundle, portable package, and release proof bundle generation all passed in `artifacts/tranche-release-proof-after-browser-optional-lane-closure-2026-06-27/`; the full suite passed 566 tests and the release proof bundle included 168 files with 0 policy exclusions.

Required desktop operator-pack evidence is saved under `artifacts/tranche-required-desktop-operator-pack/` and `artifacts/manual-validation/2026-06-15-current-required-proof/required-desktop-operator-pack/`: `goatshot manual-validation operator-pack --folder artifacts\manual-validation\2026-06-15-current-required-proof --json` completed successfully, wrote a consolidated checklist, per-lane notes, a print-only `record-lane` command reference, and `operator-pack-manifest.json` for the six required human desktop lanes. The regenerated summary/proof plan remains redaction-clean and still reports 6 required human lanes open, 4 hardware-gated lanes open, 0 optional compatibility lanes open, and 1 parked OAuth/live-provider lane. This helper prepares the operator handoff; it does not complete keyboard traversal, screen-reader observation, Windows text-scaling/high-contrast checks, live region drag, clean-machine GUI proof, hardware proof, Android media proof, browser-store proof, or OAuth consent proof.

Post-operator-pack release evidence is saved under `artifacts/tranche-required-desktop-operator-pack/` and refreshed formal bundle output under `artifacts/tranche-release-proof-admin/`: focused operator-pack/lane-update tests passed 7 tests, Release build passed with 0 warnings and 0 errors, full Release tests passed 453 tests, CLI help and diagnostics print passed, portable package generation passed, and release proof bundle generation passed with 0 policy exclusions.

Proof-scene recording smoke evidence is saved under `artifacts/tranche-proof-scene-recording-smoke/`: `GoatShot.exe --record-proof-scene-output <mp4> --record-proof-scene-duration 8` records only the app-owned proof-scene WPF window with microphone/system-audio/webcam disabled, writes a `.proof.json` sidecar, and `diagnostics recording-media` reports H.264 1180x760, 8s, 80 frames, and 0 audio streams. The recording service now paces slower WGC/frame-composition delivery so MP4 output duration is not shortened when fewer fresh frames arrive than the requested constant FPS. This is bounded local recording smoke only; live multi-monitor, long-run, audio-sync, webcam-permission, clean-machine, Android, and OAuth proof remain separate lanes.

Post-proof-scene recording smoke release evidence is saved under `artifacts/tranche-proof-scene-recording-smoke/` and refreshed formal bundle output under `artifacts/tranche-release-proof-admin/`: focused startup/recording tests passed 55 tests, Release build passed with 0 warnings and 0 errors, full Release tests passed 460 tests, CLI help and diagnostics print passed, portable package generation passed, and release proof bundle generation passed with 0 policy exclusions.

Clean packaging proof refresh evidence is saved under `artifacts/tranche-clean-machine-packaging-proof/`: current portable ZIP verification passed, packaged CLI help and diagnostics passed with isolated GoatShot roots, packaged `GoatShot.exe --render-main --output <png>` exited `0`, and packaged `GoatShot.Cli.exe paths` exited `0` with the same isolated roots. This refresh proves local portable ZIP/package-first-launch behavior only; true clean Windows VM/profile, human GUI click-through, installer install/uninstall, accessibility, hardware, Android, and OAuth proof remain open or parked.

Manual validation findings evidence is saved under `artifacts/tranche-manual-validation-findings/`: `manual-validation findings` now writes sorted Markdown/JSON findings from the current manual-validation folder. The current required-proof folder reports 6 release-blocking required findings, 4 hardware-gated claim boundaries, 0 optional compatibility findings, 1 parked OAuth/live-provider lane, and 0 redaction findings. This is a triage/reporting aid; it does not perform or replace human/operator proof.

Post-plugin-signature release evidence is saved under `artifacts/tranche-release-proof-after-plugin-signature/`: optional RSA SHA-256 remote plugin package signature verification is locally proven, `SQLitePCLRaw.bundle_e_sqlite3` is pinned to 3.0.3 to clear the prior high-severity transitive vulnerability warning, `dotnet list .\src\GoatShot.App\GoatShot.App.csproj package --include-transitive --vulnerable` reports no vulnerable packages from current NuGet sources, Release build passed with 0 warnings and 0 errors, full Release tests passed 492 tests, CLI help/diagnostics/bundle passed, portable package generation passed, and the release proof bundle reported 0 policy exclusions. Remaining work is still in manual/external/post-V1 lanes.

Video audio denoise evidence is saved under `artifacts/tranche-video-audio-denoise/`: `video denoise` / `audio-denoise` / `noise-reduction` exports a local FFmpeg `afftdn` denoised copy for videos with audio, rejects invalid reduction values before FFmpeg, preserves the video stream, and remains honest that this is local DSP cleanup rather than provider/model AI enhancement. Focused video tests passed, CLI build passed, CLI help includes the command, and a synthetic noisy H.264/AAC clip was denoised and ffprobe-confirmed as H.264 video plus AAC audio.

Post-video-denoise release evidence is saved under `artifacts/tranche-release-proof-after-video-denoise/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 494 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, and the release proof bundle reported 0 policy exclusions. Remaining work is still in manual/external/post-V1 lanes.

Installer proof readiness evidence is saved under `artifacts/tranche-installer-proof-readiness/`: `scripts\verify-installer-package.ps1` validates the Inno Setup script, detects compiler availability, records installer artifact state, and can optionally build or run explicit silent install/uninstall smoke. Current evidence passes static script validation but reports Inno Setup unavailable and no compiled installer artifact, so clean-machine/human GUI installer proof remains manual.

Post-installer-readiness release evidence is saved under `artifacts/tranche-release-proof-after-installer-readiness/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 496 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, and the release proof bundle reported 0 policy exclusions. Remaining work is still in manual/external/post-V1 lanes.

Plugin background update evidence is saved under `artifacts/tranche-plugin-background-updates/`: `plugins background-updates --registry <registry> --mode check-only` writes due-state and available-update counts without staging packages, while `--mode stage-only` stages compatible updates for already installed local plugins without installing, trusting, enabling, allowlisting, or executing plugin code. Focused tests passed 4 tests, neighboring plugin tests passed 30 tests, CLI build passed with 0 warnings and 0 errors, and isolated CLI proof produced `check-only.json`, `stage-only.json`, state files, and `assertions.json`.

Post-OAuth-evidence-recorder release evidence is saved under `artifacts/tranche-release-proof-after-oauth-evidence-recorder/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 511 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, the release proof bundle reported 0 policy exclusions, and the current NuGet vulnerability scan reports no vulnerable `GoatShot.App` packages. Remaining work is still in manual/external/post-V1 lanes.

Post-foreground-mask-generation release evidence is saved under `artifacts/tranche-release-proof-after-foreground-mask-generation/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 515 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, the release proof bundle reported 0 policy exclusions, and the current NuGet vulnerability scan reports no vulnerable `GoatShot.App` packages. Remaining work is still in manual/external/post-V1 lanes.

Post-clean-machine-proof-kit release evidence is saved under `artifacts/tranche-release-proof-after-clean-machine-proof-kit/`: Release build passed with 0 warnings and 0 errors, full Release tests passed 526 tests, CLI help, diagnostics print, diagnostics bundle, and portable package generation passed, the release proof bundle included 152 files with 0 policy exclusions, and the current NuGet vulnerability scan reports no vulnerable `GoatShot.App` packages. Remaining work is still in manual/external/post-V1 lanes.

OAuth live-proof plan evidence is saved under `artifacts/tranche-oauth-live-proof-plan/`, with provider-specific proof-plan polish saved under `artifacts/tranche-oauth-provider-specific-proof-plan/`: `oauth live-proof-plan` writes Markdown/JSON operator instructions for configured OAuth providers, required evidence lists, suggested commands, provider kind, scope review, consent checklist, account diagnostics, provider-specific upload command hints, cleanup boundaries, and false mutation flags for browser launch, provider contact, code exchange, token storage, token refresh, upload, and remote delete. Focused OAuth tests passed 9 tests, CLI build passed with 0 warnings and 0 errors, and isolated CLI proof generated a three-provider plan with fake nonsecret client IDs. Real consent screens, live refresh-token recovery, safe upload/cleanup proof, and redacted account evidence remain parked/manual lanes.

OAuth live-evidence recorder evidence is saved under `artifacts/tranche-oauth-live-evidence-recorder/`: `oauth record-live-evidence` writes Markdown/JSON records for reviewed evidence references and rejects a passed record unless consent, exchange, refresh, upload, cleanup, and account evidence categories are present. Focused OAuth evidence tests passed 13 tests, CLI build passed with 0 warnings and 0 errors, and isolated CLI proof confirmed no browser launch, provider contact, code exchange, token storage, token refresh, upload, or remote delete. This records proof references only; actual real-account consent, refresh, upload, cleanup, and account evidence remain parked/manual lanes.

Browser extension enterprise policy plan evidence is saved under `artifacts/tranche-browser-extension-enterprise-policy-plan/`: `browser-extension enterprise-policy-plan` writes Chrome/Edge `ExtensionInstallForcelist` templates, Firefox `policies.json`, required evidence lists, and explicit false mutation flags for policy application, registry writes, browser profile mutation, extension install, native-host registration, and store/MDM contact. Focused tests passed 3 tests, CLI build passed with 0 warnings and 0 errors, and isolated CLI proof produced `enterprise-policy-plan.md`, JSON, policy templates, and assertions.

Keep moving in the order below. Each tranche should be small enough to implement, prove locally, and hand back with a fresh artifact note.

## Working Rules

- [ ] Keep OAuth authorization-code plumbing as-is unless a non-OAuth task exposes a small compatibility bug.
- [ ] Do not claim live cloud-account readiness, live consent proof, refresh-token reliability, or provider-account proof until an explicit later consent/account tranche runs.
- [ ] Keep provider diagnostics honest: local configuration readiness and fake-provider proof are not the same as live provider proof.
- [ ] Prefer locally provable work: deterministic service tests, fake HTTP/process providers, WPF render screenshots, safe synthetic capture artifacts, CLI smoke, diagnostics redaction checks, and portable package output.
- [ ] End each implementation tranche with an artifact note under `artifacts/tranche-<name>/notes.md`.

## Current Baseline

- [x] Native WPF/.NET desktop app, CLI, MSTest coverage, diagnostics, Product Design screenshot-backed WPF audit artifacts, manual validation harness with browser-extension/Android proof templates, manual lane recording, and portable package path exist.
- [x] Screenshot capture supports GDI fallback plus Windows.Graphics.Capture still-capture paths, polished region overlay snapping/padding/chooser/lens behavior, with diagnostics and CLI surface.
- [x] Scrolling capture foundations exist, including vertical/horizontal profiles, sticky leading-edge mitigation, synthetic stress fixtures, manual image stitching, fallback guidance, and CLI controls.
- [x] Recording supports MP4/GIF paths, WGC/D3D frame capture where supported, GDI/FFmpeg fallbacks, Media Foundation H.264, audio controls, webcam overlay, profile/settings plumbing, confidence reporting, safe smoke harness artifacts, and an app-owned proof-scene MP4 smoke path.
- [x] Editor, OCR, local redaction, QR/barcode utilities, video tools, transcription paths, AI drafting, documentation export foundations, and workspace search exist.
- [x] Workflow rules/profiles/import-export, Settings rule management, starter templates, CLI dry-run/run/template commands, watch folders, upload queue/history, and diagnostics exist.
- [x] Executable provider adapters exist for local folder, custom script, custom webhook, WebDAV, Discord, Slack, Microsoft Teams, FTP/FTPS, S3-compatible, Imgur, SFTP, Cloudinary, GitHub Issues, Jira, Azure DevOps, Linear, YouTube, and OneNote.
- [x] Before-upload confirmation, after-upload result window, QR for returned upload URLs, searchable share history, queue retry/cancel/list/process, and provider diagnostics smoke are implemented.
- [x] Browser extension local ZIP packaging, operator UX, install-guide/status commands, and desktop-side operator diagnostics are implemented and locally proven; the package now includes content script, service worker, popup/options UI, shared UI CSS, and portable release packaging includes the `browser-extension/` source folder.
- [x] Browser extension stitch-manifest planning, automatic browser-download stitch-package export, and bounded local stitch-package import are implemented and locally proven; live browser proof remains open.
- [x] Browser extension store-readiness checklist/copy generation, store-package generation, read-only install-plan generation, temporary install-assist generation, read-only publication-plan generation, read-only publication evidence recording, read-only enterprise policy template planning, proof-manifest validation, and Edge live safe-fixture screenshots/export/import are implemented and locally proven; actual browser-store account submission/review/signing/availability, permanent/store-managed automatic extension installation, actual enterprise policy deployment/force-install proof, and Chrome/Firefox live browser screenshots remain open if needed.
- [x] Manual validation summary/validator tooling is implemented and locally proven; `manual-validation summarize --folder <folder>` writes JSON/Markdown summaries, classifies lanes by requirement, checks diagnostics bundle presence, requires fail/blocked notes, treats OAuth/live-provider proof as parked, separates hardware-gated and optional compatibility warning lanes from local-V1 blocking required lanes, and scans text evidence for redaction issues. `manual-validation proof-plan --folder <folder>` writes a requirement-aware Markdown/JSON runbook for the current required-lane proof gap. `manual-validation record-lane --folder <folder> --lane <lane> --status passed|failed|blocked|pending|not-applicable --note "<operator note>" [--evidence <path>]` records operator-supplied lane results with redacted notes and normalized evidence references; it does not perform the manual proof itself.
- [x] Plugin marketplace planning is implemented and locally proven through `plugins marketplace-plan --registry <registry>`; it reports registry/local/staged/policy state plus authority boundaries without staging, installing, trusting, enabling, allowlisting, executing, auto-updating, publishing, or contacting a hosted marketplace account service.
- [x] Companion portal local export V0 is implemented and locally proven through `companion-portal export`; it writes static JSON/HTML summaries without sync, upload, media attachment, secret access, policy mutation, plugin execution, or portal account contact.
- [x] Companion portal loopback preview is implemented and locally proven through `companion-portal serve`; it serves the sanitized report on loopback only, rejects non-loopback bind requests, exposes no write routes, and keeps account login, sync, upload, and remote admin out of scope.
- [x] Companion portal local media review is implemented and locally proven through `companion-portal media-review` and opt-in `--media ... --accept-media-copy`; it copies only explicitly selected files into static local review pages with relative links and hashes, omits source paths, and does not contact a portal, allow remote clients, sync, upload, read secrets, mutate policy, install/run plugins, or enable write routes.
- [x] Companion portal explicit self-hosted shared-token preview is implemented and locally proven through `companion-portal serve --self-hosted --accept-remote-clients --auth-token-env <name>`; it remains read-only, rejects weak/missing self-hosted tokens, returns `401` without auth, does not serialize tokens into reports, and still does not provide hosted accounts, account login, team sync, hosted media, remote admin, sync, upload, or write routes.
- [x] Virtual-printer driver decision is complete; GoatShot remains `print-import` file-drop/import-only for V1 and true OS printer-driver work is deferred until admin/signing/clean-machine constraints are explicitly accepted.
- [x] Production Android streaming decision is complete; GoatShot remains bounded to ADB screenshot/video/preview helpers for V1, and true live streaming is deferred to a post-V1 Android companion-app or explicitly accepted dependency/streaming tranche.
- [x] Release proof was refreshed after the Android streaming decision; standard gate logs are under `artifacts/tranche-release-proof-post-android-decision/`, and the formal release proof manifest/bundle is under `artifacts/tranche-release-proof-admin/`.

## Completed Tranche 1: Editor And Privacy Tool Completion

Goal: close the remaining Screenpresso/Snagit-style editor gaps and strengthen local privacy review before export.

- [x] Inspect the current editor tool model, canvas rendering, toolbar focus behavior, and export actions.
- [x] Add freehand drawing as a first-class annotation tool with undo/redo compatibility.
- [x] Add spotlight area as a privacy/review-friendly visual emphasis tool.
- [x] Add print/export handoff from selected captures.
- [x] Add clearer review UI for detected sensitive OCR regions before flattened export.
- [x] Add keyboard tool-selection and focus-order proof for toolbar, canvas, export actions, AI prompt, and privacy tools.
- [x] Keep real-time recording blur later-scope unless the preview/reversibility UX is solid.
- [x] Proof: editor service/UI-model tests, WPF screenshot/accessibility notes, Release build/test, CLI smoke, package, and `artifacts/tranche-editor-privacy-tools/notes.md`.

## Completed Tranche 2: Workflow Task Surface And CLI Parity

Goal: make automation inspectable and easy to operate after captures, recordings, OCR, uploads, and AI events.

- [x] Inspect existing workflow rule execution, Settings templates, upload task windows, and CLI workflow commands.
- [x] Add an after-capture quick task window with open, edit, copy, share, AI, document, and delete-local-with-confirmation actions.
  - [x] Define a UI model for available post-capture actions and disabled-state reasons.
  - [x] Render safe app-owned screenshot proof without using private desktop capture content.
  - [x] Hook the window after new captures, and expose it from the workspace selection context.
- [x] Add rule execution logs that explain skipped conditions and blocked actions.
  - [x] Persist JSON plus readable text/Markdown logs under an artifacts/log-style app data path.
  - [x] Include trigger, capture id/path, dry-run state, matched rules, skipped rules, skip reasons, blocked actions, and action outcomes.
  - [x] Surface the log path from CLI workflow `run`/`dry-run` output.
- [x] Add script/webhook dry-run from desktop and CLI.
  - [x] CLI commands should show the resolved command/webhook payload summary without executing a process or sending HTTP.
  - [x] Desktop should expose the same dry-run path from the workflow/task surface.
  - [x] Redact secrets, query tokens, bearer values, and configured credentials.
- [x] Add workflow import/export validation command.
  - [x] Validate schema version, duplicate rule ids/names, invalid triggers/actions, empty action lists, risky delete/upload combinations, and secret omission behavior.
  - [x] Keep import behavior backward-compatible, but reuse validation so failures are understandable.
- [x] Provider diagnostics filters already exist through `providers --provider`, `--ready`, `--not-ready`, `--implemented`, and `--roadmap`; do not rebuild unless operators still lack a needed filter.
- [x] Add tests proving Settings round-trips advanced fields without dropping hidden or future fields.
  - [x] Preserve uncommon automation fields when Settings edits only visible fields.
  - [x] Preserve unknown/future JSON-compatible profile fields where current serializers can reasonably keep them, or document the boundary if not feasible.
- [x] Proof: workflow tests, CLI dry-run smoke, WPF screenshot proof, Release build/test, CLI diagnostics, package, and `artifacts/tranche-workflow-task-surface/notes.md`.

## Completed Tranche 3: AI, Video Intelligence, And Documentation Review Loop

Goal: make the existing AI/video/doc plumbing useful as an explicit review loop while keeping AI optional and privacy-explicit.

- [x] Inspect current `AiActionHistoryService`, `TranscriptionService`, `VideoIntelligenceService`, `BugReportService`, CLI commands, and desktop entry points.
- [x] Add desktop accept/reject/iterate controls where AI action history already stores review status.
- [x] Add prompt-history picker for video/document workflows.
- [x] Add retry-with-different-model/profile recovery for failed AI actions.
- [x] Generate richer bug reports from recordings using transcript, keyframes, environment, and redacted context.
- [x] Add a local documentation packet/manifest that links transcript, SRT, video summary, chapters, bug report export, source media metadata, and AI history/review state.
- [x] Keep long-recording transcription local/Whisper-first until a media-upload provider path is intentionally added.
- [x] Proof: local-fixture AI/document tests, document exports, redaction checks, Release build/test, CLI smoke, package, and `artifacts/tranche-ai-document-workflow/notes.md`.

## Completed Tranche 4: Upload Session Reliability Tests

Goal: finish the remaining local reliability proof around larger upload paths without touching live OAuth consent.

- [x] Inspect existing Google Drive, Dropbox, OneDrive, and queue code paths only for local/fake API proof opportunities.
- [x] Add or tighten fake resumable/large-file tests for existing upload-session style code paths where feasible.
- [x] Verify retry/backoff/cancel behavior for upload-session branches without real provider accounts.
- [x] Preserve DPAPI-backed secrets and redacted diagnostics/history as invariants.
- [x] Keep remote-delete support disabled unless a provider has a safe delete API path and local audit record.
- [x] Proof: upload-session tests, diagnostics redaction assertions, CLI queue smoke, Release build/test, package, and an update to `artifacts/tranche-upload-queue-reliability/notes.md`.

## Partially Completed Tranche 5: Recording Field Proof And Profile Presets

Goal: finish the remaining non-OAuth recording proof that still needs real Windows state or conservative UI modeling.

- [x] Add multi-monitor and cross-monitor region proof helpers that avoid retaining private desktop captures by default.
- [x] Add explicit microphone, system-audio, and camera permission-denied/recovery states in the WPF recording UI where confidence reporting already detects blocked signals.
- [x] Add device-disconnect state messaging and recovery guidance for audio/camera capture.
- [x] Add deeper timestamp logging for microphone/system-audio sync if duration-delta checks are not enough.
- [x] Add recording profile presets for small share, 1080p60, and 4K60 where hardware supports them.
- [x] Add HEVC opt-in encode path only after Media Foundation reports support and failure states are clear.
- [x] Add app-owned proof-scene MP4 smoke and frame pacing so slow frame delivery preserves requested constant-FPS duration.
- [x] Proof: focused recording tests, safe plan-only fixed/all-monitor smoke matrix, device diagnostics, Release build/test, CLI smoke, package, and an update to `artifacts/tranche-recording-confidence/notes.md`.
- [x] Remaining local proof: optional `ffprobe` metadata from safe synthetic media and WPF screenshot of updated confidence states.
- [ ] Remaining manual proof: live/manual multi-monitor recording and long-run recording with safe desktop content.

## Completed Tranche 6: Release Proof And Managed Posture

Goal: make the project handoff-ready without relying on live cloud accounts.

- [x] Build a release proof bundle with build/test/package logs, diagnostics redaction proof, and selected screenshots.
- [x] Keep portable zip as the default proof path; compiled installer and clean-machine installer proof remain manual/tooling-dependent.
- [x] Add optional policy keys for disabling AI, disabling uploads, restricting providers, custom scripts, and custom webhooks.
- [x] Add diagnostics that show policy source and effective state.
- [x] Document managed Windows deployment behavior.
- [x] Proof: release bundle, portable package output, policy diagnostics tests, Release build/test, CLI smoke, package, settings render, and `artifacts/tranche-release-proof-admin/notes.md`.

## Completed Tranche 7: Share Provider Adapter Cleanup

Goal: finish non-OAuth provider plumbing polish without touching live consent screens.

- [x] Inventory remaining executable share branches still living only in `ShareService`.
- [x] Extract remaining non-OAuth branches into concrete `IShareProvider` adapters only where that reduces duplication or improves diagnostics.
- [x] Keep `ShareService` as the stable facade for routing, history, queueing, confirmations, and compatibility.
- [x] Preserve DPAPI-backed secrets, redacted share history, provider diagnostics, before-upload confirmation, and after-upload result behavior.
- [x] Leave OAuth-backed live account providers in their current token/diagnostic posture.
- [x] Proof: focused provider adapter tests, fake HTTP/process/fake-surface proof where executable locally, provider diagnostics smoke, Release build/test, CLI smoke, package, and `artifacts/tranche-provider-adapter-cleanup/notes.md`.

## Completed Tranche 8: V1 Evidence And Readiness Sweep

Goal: make the local V1 handoff honest, readable, and easy to resume.

Active plan: see `artifacts/active-non-oauth-buildout-todos.md`.

- [x] Refresh README/spec current-truth sections against implemented code and artifacts.
- [x] Refresh Product Design/WPF screenshot-backed audit notes only for flows changed since the last audit.
- [x] Create a manual validation checklist artifact for keyboard traversal, screen reader pass, high contrast/text scaling, multi-monitor hardware proof, long recording stability, clean-machine installer proof, and live provider account proof.
- [x] Create `artifacts/v1-readiness-summary.md` separating implemented, locally proven, manually unverified, OAuth parked, and later-scope work.
- [x] Proof: README/spec consistency scan, changed-surface WPF render screenshots, full Release build/test, CLI help, CLI diagnostics, package lane, and release proof bundle refresh.

## Completed Tranche 9: Recording Field Proof Polish

Goal: finish the remaining locally buildable recording confidence work before manual long-run hardware validation.

- [x] Add deeper microphone/system-audio timestamp and duration logging for sync proof.
- [x] Surface audio sync health in diagnostics and recording confidence notes without storing private audio content.
- [x] Add optional `ffprobe` metadata extraction when `ffprobe` is available, with a clear skipped state when it is not.
- [x] Render a WPF screenshot of updated recording confidence/device states.
- [x] Add HEVC opt-in encode path only when Media Foundation reports support and fallback/error messaging is explicit.
- [x] Keep all-monitor/live long-recording proof in the manual lane unless safe user-approved desktop content is available.
- [x] Proof: focused recording confidence/planner/device tests, safe synthetic recording artifacts, diagnostics output, WPF screenshot, Release build/test/CLI/package, and `artifacts/tranche-recording-field-proof/notes.md`.

## Completed Tranche 10: Manual Validation Harness And Evidence Templates

Goal: make human/device/manual proof repeatable instead of a loose checklist.

- [x] Add a script or CLI command that creates a dated manual-validation evidence folder with blank notes templates.
- [x] Include templates for keyboard traversal, Narrator/NVDA checks, text scaling, high contrast, region drag path, multi-monitor capture, multi-monitor recording, long recording, clean-machine install, and live provider proof.
- [x] Add redaction reminders and safe-content rules to every template.
- [x] Add diagnostics bundle references so a manual run can attach current app state without exposing secrets.
- [x] Proof: script/CLI tests where practical, generated sample folder under `artifacts/tranche-manual-validation-harness/`, diagnostics redaction check, Release build/test/CLI/package, and tranche notes.

## Completed Tranche 11: Advanced Local Video Editing

Goal: extend local FFmpeg-backed video tools while keeping edits previewable, reversible, and explicit.

- [x] Add silence-removal analysis that produces a previewable cut list before export.
- [x] Add text-based edit planning from transcript/SRT timestamps.
- [x] Add filler-word removal planning from transcript terms without auto-deleting content.
- [x] Add composite screen/webcam layout export recipes.
- [x] Add reviewed keyed webcam-background blur/removal/replacement planning and export behind explicit preview/acceptance.
- [x] Add deterministic foreground mask generation plus reviewed external mask/matte webcam-background planning/export.
- [x] Add thumbnails/previews for generated edit plans where feasible.
- [x] Proof: video command argument validation tests, fixture exports or dry-run edit plans, transcript/cut-list tests, Release build/test/CLI/package, and `artifacts/tranche-advanced-video-editor/notes.md`.

## Completed Tranche 12: Android ADB Capture

Goal: add optional Android screenshot capture without blocking the desktop product.

- [x] Stabilize and prove the current Android ADB source scaffolding.
- [x] Add ADB discovery and diagnostics.
- [x] Implement `adb exec-out screencap -p` import into the GoatShot workspace when a device is connected.
- [x] Handle missing ADB, no device, unauthorized device, offline device, multiple devices, and failed capture states.
- [x] Add CLI capture/diagnostics commands.
- [x] Keep Android recording/video streaming out of scope until screenshot capture is stable.
- [x] Proof: fake ADB process tests, parser/service tests, CLI diagnostics on this machine, explicit missing-ADB proof, Release build/test/CLI/package, and `artifacts/tranche-android-adb-capture/notes.md`.

## Completed Tranche 13: Browser Extension Contract And Prototype

Goal: start perfect DOM/page capture as a separate optional module without changing the native desktop baseline.

- [x] Define extension-to-desktop contract for full-page capture, DOM metadata, console/network telemetry, and explicit user consent.
- [x] Add a minimal extension manifest and content-script prototype that can capture page geometry/metadata and hand off to GoatShot through a local/native bridge design.
- [x] Keep bug-report telemetry opt-in and visibly consented.
- [x] Do not claim browser extension parity with desktop capture until end-to-end browser proof exists.
- [x] Proof: extension contract tests or fixture validation, sample DOM/page payloads, privacy redaction checks, and `artifacts/tranche-browser-extension/notes.md`.

## Completed Tranche 14: Virtual Printer Import Path

Goal: design and implement the import handoff before committing to driver-level installer work.

- [x] Define a local file-drop/import contract for print-to-image/PDF handoff.
- [x] Add watched-folder import rules for PDF/image outputs.
- [x] Preserve source-app metadata where available.
- [x] Document that true virtual-printer driver installation is installer/admin-scoped and not locally proven yet.
- [x] Proof: watched-folder import tests, safe sample PDF/image imports, diagnostics note, and `artifacts/tranche-virtual-printer-import/notes.md`.

## Completed Tranche 15: Plugin SDK And Local Extension Points

Goal: let power users extend GoatShot locally without weakening policy, redaction, diagnostics, or the local-first trust model.

- [x] Define a minimal local plugin manifest for actions, share destinations, workflow actions, and diagnostics.
- [x] Add local plugin discovery from an app-owned folder.
- [x] Keep discovered plugins disabled/untrusted by default.
- [x] Add allowlist/trust-state checks before plugin actions can run.
- [x] Add diagnostics for plugin id, version, source path, trust state, allowed/blocked actions, and parse errors.
- [x] Add sample plugin fixtures with no network side effects.
- [x] Proof: manifest parser tests, policy/allowlist tests, sample plugin dry-run proof, diagnostics redaction, and `artifacts/tranche-plugin-sdk/notes.md`.

## Completed Tranche 16: Browser Extension Native Bridge Follow-Through

Goal: turn the existing browser extension contract/prototype into a local end-to-end handoff without changing the native desktop baseline.

- [x] Define the native messaging or local bridge installer boundary.
- [x] Add a bounded local handoff receiver that validates `goatshot.browser-capture.v1` payloads before import.
- [x] Import consented full-page bitmap/page payloads into the workspace when the bridge is available.
- [x] Keep telemetry opt-in and bounded; never collect cookies, headers, form values, local/session storage, or raw DOM text dumps.
- [x] Add clear disabled/missing-bridge diagnostics and docs.
- [x] Proof: receiver tests, accepted/rejected payload fixtures, redaction tests, local bridge smoke with safe fixtures, and `artifacts/tranche-browser-native-bridge/notes.md`.

## Completed Tranche 17: Android Video Expansion Decision

Goal: decide whether Android remains screenshot-only or expands into safe video import.

- [x] Write an architecture note comparing ADB screencap polling, `adb shell screenrecord` pull/import, and live streaming options.
- [x] Add bounded `adb shell screenrecord --time-limit` import through CLI `capture android-video` / `capture android screenrecord`.
- [x] Keep live device video proof opt-in and privacy-gated.
- [x] Do not start Android live streaming until screenshot/video import is stable.
- [x] Proof: fake ADB command tests, invalid-duration/missing-ADB CLI artifacts, safe-device manual proof boundary, and `artifacts/tranche-android-video-decision/notes.md`.

## Completed Tranche 17A: Android Live Preview Dry-Run Planner

Goal: explore live Android preview after bounded screenshot and `screenrecord` import without capturing private phone content.

- [x] Add an architecture note comparing repeated `screencap` polling, `screenrecord --output-format=h264 -`, FFmpeg remux, and scrcpy-style external tooling.
- [x] Add a service-level dry-run plan model for strategy, device selection, consent reminders, duration, byte cap, timeout, disconnect, and cleanup behavior.
- [x] Add CLI `capture android-preview` and `capture android preview` planning commands.
- [x] Keep the planning command dry-run only: it may probe `adb devices`, but it does not start screencap polling, H.264 streaming, FFmpeg remux, or media import.
- [x] Proof: fake ADB service tests, CLI ready/missing/invalid plan artifacts, Release build/test/CLI/package, and `artifacts/tranche-android-live-preview/notes.md`.
- [ ] Remaining manual proof: live Android preview behavior with staged safe device content.

## Completed Tranche 17B: Android Preview Execution Gate

Goal: add a tightly bounded, opt-in execution path for Android preview without starting production Android streaming.

- [x] Wrote an approval note choosing bounded `screencap` polling as the only approved execution strategy for now.
- [x] Added `capture android-preview --execute` behind explicit `--safe-content-confirmed`, selected `--device`, short duration caps, frame/byte/timeout caps, and failure cleanup.
- [x] Kept scrcpy-style mirroring and continuous production streaming later-scope; bounded H.264 stdout preview execution is now covered separately under `artifacts/tranche-android-h264-preview-execution/`.
- [x] Added fake ADB service tests for confirmation refusal, selected-device requirement, H.264 execution refusal, frame collection, disconnect cleanup, timeout cleanup, and byte-cap cleanup.
- [x] Proof: focused Android tests, CLI dry-run/blocked/execute artifacts, Release build/test/CLI/package, and `artifacts/tranche-android-preview-execution/notes.md`.
- [ ] Remaining manual proof: live Android preview behavior with staged safe device content.

## Completed Tranche 18: Companion Portal And Team/Admin Boundary Planning

Goal: separate hosted/shared/team work from the local-first desktop MVP before building services.

- [x] Write an architecture note for optional hosted/self-hosted companion portal boundaries.
- [x] Define what syncs, what stays local, what requires consent, and what cannot bypass desktop policy.
- [x] Define how portal/team policy relates to existing managed-policy keys.
- [x] Define team/admin mode separately from individual managed Windows policy keys.
- [x] Do not implement a hosted service until this boundary is approved.
- [x] Proof: architecture note, threat/privacy checklist, and `artifacts/tranche-companion-portal-planning/notes.md`.

## Completed Tranche 19: Browser Native Host Registration

Goal: turn the browser native bridge into a user-scope native messaging host registration path without browser-store claims.

- [x] Add stdio native-host run mode to `GoatShot.Cli.exe`.
- [x] Add Chrome, Edge, and Firefox native messaging manifest generation.
- [x] Add user-scope Chrome/Edge HKCU registration and Firefox profile-folder manifest installation commands.
- [x] Add status and uninstall commands.
- [x] Update the prototype extension with `nativeMessaging` permission and a service-worker handoff.
- [x] Keep browser-store account submission/review/signing/availability, permanent/store-managed automatic extension installation, and actual enterprise policy deployment/force-install proof later-scope.
- [x] Proof: fake registry install/uninstall tests, native-message validation/redaction tests, CLI status/manifest artifacts, and `artifacts/tranche-browser-native-host-registration/notes.md`.

## Completed Tranche 20: Guarded Local Plugin Execution

Goal: execute local plugin actions only after the existing trust, enable, and action allowlist gates pass.

- [x] Add `execution` metadata support to plugin actions.
- [x] Add `plugins run <plugin-id> <action-id>` CLI command.
- [x] Enforce global plugin enablement, plugin trust, plugin enablement, and action allowlist before process start.
- [x] Add bounded timeout, redacted stdout/stderr, exit-code reporting, timeout reporting, and plugin-directory working-directory guard.
- [x] Keep remote plugin acquisition for the later remote scaffold, staged activation, explicit update-apply, and governed background check/stage-only tranches; automatic install/trust/enable/allowlist/execute updates and hosted marketplace behavior remain later-scope.
- [x] Proof: blocked/run/timeout tests, sample plugin CLI run proof, and `artifacts/tranche-plugin-execution/notes.md`.

## Completed Tranche 21: Remote Plugin Install And Update Scaffold

Goal: add a governed plugin acquisition path without weakening the existing local trust model.

- [x] Add `goatshot.plugin-registry.v1` with plugin id, version, name, description, capabilities, permissions, package URI, SHA-256, size, optional RSA package signature metadata/verification, compatibility range, and release notes.
- [x] Add registry validation for local files and fake HTTP registries.
- [x] Add stage-only package acquisition that downloads or copies archives into `PluginStagingRoot` without enabling, trusting, allowlisting, or executing plugin code.
- [x] Verify SHA-256, max package size, required packaged `plugin.json`, compatibility range, duplicate ids, and safe archive paths.
- [x] Reject zip path traversal, absolute archive paths, missing manifests, duplicate manifests, mismatched ids, checksum mismatches, and oversized packages.
- [x] Add CLI commands for `plugins registry validate`, `install-plan`, `stage`, `updates`, `remove-staged`, `disable`, and metadata-only `uninstall`.
- [x] Keep hosted marketplace accounts, payments, ratings, remote execution, active install into the local plugin root, and automatic install/trust/enable/allowlist/execute updates out of scope for this scaffold tranche.
- [x] Proof: fake HTTP registry/package tests, zip/path traversal tests, CLI smoke artifacts, sample registry docs, Release build/test/package, and `artifacts/tranche-plugin-remote-install-scaffold/notes.md`.

## Completed Tranche 21A: Staged Plugin Activation

Goal: copy reviewed staged packages into the active local plugin folder without weakening the trust model.

- [x] Add `RemotePluginActiveInstallResult` and `goatshot.plugin-install.v1` install manifests.
- [x] Add `plugins install-staged <plugin-id> [--version VERSION] [--replace] [--json]`.
- [x] Copy exactly one packaged plugin root from the staging folder into the active local plugin root.
- [x] Require `--replace` before overwriting an existing active plugin folder.
- [x] Clear inherited trust, enablement, and action allowlist metadata after successful install.
- [x] Preserve disabled/untrusted/no-execution defaults so dry-run remains blocked until explicit operator trust/enable/allowlist steps.
- [x] Proof: focused service tests, CLI stage/install/list/dry-run-blocked artifacts, Release build/test/package, and `artifacts/tranche-plugin-active-install/notes.md`.

## Completed Tranche 22: Local Team/Admin Mode

Goal: implement a local/admin-friendly mode before hosted portal work.

- [x] Define `goatshot.admin-policy.v1` policy bundles for allowed destinations, disabled AI/uploads/scripts/webhooks, local plugin controls, browser/Android/print-import controls, private capture mode, diagnostics log capture, and retention marker.
- [x] Add `admin-policy validate`, `export`, `import`, `diff`, and `explain` CLI commands that omit secrets by default.
- [x] Preserve deny-wins policy precedence when importing bundles unless explicit replace is requested.
- [x] Add local plugin id/action managed-policy enforcement.
- [x] Add managed-policy blocks for Android capture, browser-extension handoff/native-host run/install, and virtual-printer import side effects.
- [x] Add redacted JSONL audit entries when CLI side-effect commands are blocked by policy.
- [x] Expand diagnostics/redacted bundle policy fields.
- [x] Keep hosted account sync, remote enforcement, multi-user portal state, and remote admin sync out of scope.
- [x] Proof: policy precedence tests, blocked-action tests, CLI validate/import/export/diff/explain artifacts, policy-block audit artifacts, Release build/test/package, and `artifacts/tranche-local-team-admin-mode/notes.md`.

## Completed Tranche 21: Browser Extension Local Packaging

Goal: package the optional browser extension prototype for local/unpacked use without browser-store claims.

- [x] Add `browser-extension package` CLI support for validating and creating a local extension ZIP.
- [x] Package the loadable extension files: `manifest.json`, `content-script.js`, `service-worker.js`, popup/options UI files, and shared UI CSS.
- [x] Validate Manifest V3, `nativeMessaging` permission, content-script registration, and background service-worker registration before writing the ZIP.
- [x] Include the `browser-extension/` source folder in portable GoatShot releases so packaging can run outside a source checkout.
- [x] Update README/spec/browser-extension docs to distinguish local ZIP packaging and temporary install-assist from browser-store account submission/review/signing/availability, permanent/store-managed automatic installation, and in-browser stitching.
- [x] Keep browser-store account submission/review/signing/availability, permanent/store-managed automatic extension installation, and actual enterprise policy deployment/force-install proof later-scope.
- [x] Proof: focused package tests, CLI package/help/diagnostics smoke, portable ZIP extension-entry proof, Release build/test/package, and `artifacts/tranche-browser-extension-packaging/notes.md`.

## Completed Tranche 22: Browser Full-Page Stitch Package Handoff

Goal: implement full-page browser stitching handoff with safe planning, explicit package export, and native package import.

- [x] Add stitch manifest and tile metadata to the browser extension payload contract.
- [x] Add deterministic tile planning for tall pages, horizontal scroll, overlap, sticky-header mitigation, max-tile caps, partial states, and blocked states.
- [x] Validate and redact stitch manifests in the native bridge contract service.
- [x] Add content-script stitch planning and optional visible-tab tile capture state metadata.
- [x] Add service-worker visible-tab capture support through `chrome.tabs.captureVisibleTab`.
- [x] Require `activeTab` permission during extension package validation.
- [x] Update sample payload, browser-extension docs, README/spec status, and tranche artifacts.
- [x] Define and implement a bounded local stitch-package handoff for actual tile images or a stitched output.
- [x] Add native import for a stitched browser bitmap after the file-handoff boundary is settled.
- [x] Stage a safe local browser fixture for later live proof.
- [x] Add automatic in-browser stitch-package creation/export through browser downloads.
- [x] Proof: focused browser-extension tests, sample payload validation/redaction, extension package smoke, synthetic stitch-package import, native-host stitch-package routing, `artifacts/tranche-browser-full-page-stitching/notes.md`, and `artifacts/tranche-browser-auto-stitch-package/notes.md`.

## Completed Tranche 23: Browser Extension Operator UX And Install Diagnostics

Goal: make the extension operable locally without browser-store or automatic-install claims.

- [x] Add extension popup UI for capture settings, consent toggles, native-host send, native-host status, and last-result feedback.
- [x] Add extension options UI for saved defaults and status.
- [x] Add popup-to-content-script capture messaging.
- [x] Add service-worker native-host status ping and native-host `GOATSHOT_PING` response with version.
- [x] Expand package validation and ZIP output to include popup/options UI and shared CSS.
- [x] Add `browser-extension install-guide` CLI for Chrome, Edge, and Firefox setup notes.
- [x] Keep browser-store account submission/review/signing/availability, permanent/store-managed automatic extension installation, and actual enterprise policy deployment/force-install proof out of scope.
- [x] Add desktop-side status/diagnostics codes that distinguish source/package readiness, safe fixture readiness, native-host missing, native-host manifest missing, native-host registered but requiring browser Host Status proof, payload rejection diagnostics, stitch-package import diagnostics, and browser-download package boundary.
- [x] Run Edge live safe-browser extension load, popup Host Status, consent/default screenshots, package export, and capture/import proof under `artifacts/tranche-browser-live-fixture-proof-closure/`.
- [x] Add automatic in-browser stitch-package creation/export through browser downloads.
- [x] Proof: focused browser tests, JS syntax checks, CLI package/install-guide/status/help smoke, static UI artifacts, local diagnostics artifacts, and `artifacts/tranche-browser-extension-operator-ux/notes.md` plus `artifacts/tranche-browser-live-diagnostics/notes.md`.

## Manual Proof Backlog

- [ ] OAuth consent screens and refresh behavior for Google Drive, Dropbox, OneDrive, and future OAuth providers. Parked for now.
- [ ] Live keyboard Tab traversal across Main Window, Settings, Editor, tray menu, capture overlay, and recording controls.
- [ ] Narrated screen-reader verification for key WPF flows.
- [ ] Text scaling and high-contrast Windows mode checks.
- [ ] Live interactive region selection with a human drag path.
- [ ] Live multi-monitor/cross-monitor capture and recording proof with safe desktop content.
- [ ] Long recording stability with microphone, system audio, webcam, and multi-monitor setups.
- [ ] Live upload proof against real provider accounts when credentials and consent are available.
- [ ] Installer proof on a clean Windows machine.

## Forward Buildout Queue

- [ ] Live browser fixture proof when safe browser content is staged. Use `artifacts/next-forward-buildout-todos-oauth-parked.md`.

## Later Modules

- [x] Read-only enterprise policy template planning beyond the native-host receiver, store-package artifacts, temporary install-assist helper, read-only install planner, and read-only publication planner.
- [ ] Browser-store account submission/review/signing/availability, permanent/store-managed automatic extension installation, and actual enterprise policy deployment/force-install proof.
- [ ] Live Android device screenshot/video/preview proof with staged safe content, and any post-V1 production Android streaming beyond bounded `screenrecord` import plus dry-run/guarded screencap/H.264 preview helpers.
- [ ] Signed/admin OS virtual-printer driver installation and clean-machine printer proof beyond the proven file-drop/diagnostics path.
- [x] Reviewed advanced video cut-plan export for text-based, silence, and filler-word plans (`video apply-plan --accept-plan`).
- [x] Reviewed composite camera/screen layout export for planned picture-in-picture, side-by-side, and stacked recipes (`video apply-composite --accept-plan`).
- [x] Reviewed keyed webcam-background blur/removal/replacement export for green-screen/key-color clips (`video apply-background --accept-plan`).
- [x] Deterministic foreground mask generation for keyed, alpha, and luma mask videos (`video generate-mask`), locally proven under `artifacts/tranche-foreground-mask-generation/`.
- [x] External person-segmentation mask generation runner contract (`video person-mask --runner <exe> --runner-args "<template>" --accept-external-runner`), locally proven under `artifacts/tranche-person-segmentation-runner/`; GoatShot verifies output and indexes `PersonSegmentationMaskVideo` but does not bundle/trust/enable/register/run/host/broadly quality-certify a model.
- [x] Hosted person-segmentation service handoff (`video hosted-person-mask --endpoint <url> --accept-hosted-service [--api-key-env NAME]`), locally proven under `artifacts/tranche-hosted-person-segmentation-service/`; GoatShot uploads only after explicit acceptance and does not provide first-party hosted accounts, provider proof, or model certification.
- [x] Stage-only person-segmentation model package acquisition (`video person-model validate|stage --manifest <manifest.json> [--accept-download]`), locally proven under `artifacts/tranche-person-segmentation-model-package-staging/`.
- [x] Still-mask quality evaluation for generated masks against reviewed reference masks (`video mask-quality --generated-mask <png> --reference-mask <png>`), locally proven under `artifacts/tranche-video-mask-quality-evaluator/`.
- [x] Frame-by-frame mask-video quality evaluation for supplied generated/reference mask videos (`video mask-quality --generated-mask <video> --reference-mask <video>`), locally proven under `artifacts/tranche-video-mask-quality-video-evaluator/`.
- [x] Reviewed external mask/matte webcam-background blur/removal/replacement export for reviewed mask videos (`video plan-background --mask`, then `video apply-background --accept-plan`).
- [ ] Advanced video editor remainder: bundled AI person-segmentation inference, first-party hosted segmentation account proof, automatic model inference, and broad model certification beyond deterministic foreground masks, explicit local external-runner masks, hosted service handoff, stage-only model package acquisition, still-image/video-frame mask quality evaluation, and reviewed mask/matte processing.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [x] Governed background plugin update check/stage-only runs.
- [ ] Automatic plugin install/trust/enable/allowlist/execute updates and marketplace behavior beyond governed local staging/install-staged/update-apply/background check-stage support.
- [ ] Hosted portal accounts, first-class account login, team sync, hosted/remote media hosting, and remote admin after boundary approval; the local self-hosted shared-token read-only preview is implemented but is not an account/login/team-sync/admin system.
- [ ] Team/admin mode as a separate post-V1 module after boundary approval.

## Standard Definition Of Done

- [ ] Focused tests for changed services, CLI behavior, and UI models.
- [ ] WPF screenshot or Product Design artifact for UI changes.
- [ ] Tranche note under `artifacts/tranche-<name>/notes.md`.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Honest remaining-risk note, especially for hardware/manual proof and anything OAuth-adjacent.

# GoatShot Non-OAuth Continuation TODOs

Date: 2026-06-15

Purpose: continue building GoatShot after the passive plugin update notifier tranche while keeping Google Drive, Dropbox, OneDrive, and future live OAuth consent/account proof parked. This is the short next-action plan; it favors locally buildable work, deterministic tests, safe synthetic artifacts, and honest manual-proof boundaries.

No Git workflow is required right now.

## Standing Rules

- [ ] Keep existing OAuth authorization-code, refresh-token, and provider setup plumbing where it is.
- [ ] Do not claim live OAuth consent, live cloud-account upload readiness, refresh-token recovery, or live-account remote delete until a dedicated account-proof tranche is scheduled.
- [ ] Keep GoatShot a native WPF/.NET desktop app with CLI support; do not introduce a web stack for the desktop product.
- [ ] Use fake providers/processes, safe fixtures, synthetic media, WPF render screenshots, diagnostics, and portable package output as proof.
- [ ] Add a tranche note under `artifacts/tranche-<name>/notes.md` for each implementation tranche.
- [ ] Update readiness ledgers only for proof actually collected or code actually shipped.

## Immediate Tranche 1 - Android Preview Execution Gate

Goal: turn the existing Android live-preview dry-run planner into a tightly bounded opt-in execution path without starting production Android streaming.

Implementation TODOs:

- [x] Add an approval note under `artifacts/tranche-android-preview-execution/` choosing bounded `screencap` polling as the only approved execution strategy for now.
- [x] Add `capture android-preview --execute` only when the operator also provides explicit safe-content confirmation.
- [x] Require a selected ready device, short duration caps, frame/byte caps, timeout caps, and local cleanup on failure.
- [x] Keep H.264 stdout streaming, FFmpeg remux, scrcpy-style mirroring, and continuous production streaming later-scope.
- [x] Add fake ADB process tests for start, frame/chunk collection, disconnect, timeout, byte cap, cleanup, unsupported strategy refusal, and refusal without confirmation.

Proof TODOs:

- [x] Focused Android preview execution tests.
- [x] CLI dry-run, blocked-without-confirmation, unsupported-strategy, and execute-plan artifacts.
- [x] Release build/test/CLI/package gate.
- [x] `artifacts/tranche-android-preview-execution/notes.md`.
- [x] Live phone proof only if safe phone content is explicitly staged; none was staged for this local fake-ADB tranche.

## Immediate Tranche 2 - Browser Live Fixture Closure

Goal: close or sharply bound the live browser-extension proof gap with safe local browser content.

Implementation TODOs:

- [ ] Use the existing live-fixture helper/verifier unless a tiny helper fix is needed.
- [ ] Prefer Chrome first, then Edge if Chrome unpacked-extension loading remains blocked.
- [ ] Register the user-scope native host with the real extension id.
- [ ] Capture screenshots for extension details, popup consent defaults, options consent defaults, Host Status, selected-element mode, package-export toggle, and last handoff result.
- [ ] Run one consented safe-fixture capture with screenshot consent and package export enabled.
- [ ] Import the downloaded stitch package through the native receiver.
- [ ] Keep browser-store publication, review, signing, and automatic installation out of scope.

Proof TODOs:

- [ ] Live safe-fixture screenshots under `artifacts/manual-validation/<yyyy-mm-dd>/browser-extension-live-fixture/`.
- [ ] Redacted package/payload/import-result artifacts.
- [ ] Browser version and extension id notes, with redaction where needed.
- [ ] Readiness docs updated only if live proof is collected.

## Immediate Tranche 3 - Manual Validation Proof Pass

Goal: gather human/device evidence that deterministic tests cannot honestly produce.

Implementation TODOs:

- [ ] Run `goatshot manual-validation create --include-diagnostics-bundle`.
- [ ] Complete keyboard traversal notes for Main Window, Settings, Editor, tray menu, capture overlay, recording controls, AI review, upload queue/history, and share/provider setup.
- [ ] Complete Narrator/NVDA notes for key WPF flows.
- [ ] Complete Windows text scaling and high-contrast checks.
- [ ] Complete live region drag proof with safe desktop content.
- [ ] Complete multi-monitor capture/recording proof if hardware is available.
- [ ] Complete long recording stability proof with safe microphone, system-audio, webcam, and desktop content.
- [ ] Complete clean-machine portable ZIP proof in a clean profile or VM.
- [ ] Complete live Android screenshot/video/preview proof only with staged safe phone content.
- [ ] Keep live provider/OAuth proof parked.

Proof TODOs:

- [ ] Lane notes and artifacts under `artifacts/manual-validation/<yyyy-mm-dd>/`.
- [ ] Redacted diagnostics bundle.
- [ ] Summary file with pass/fail/blocked per lane.
- [ ] Readiness docs updated only for completed lanes.

## Immediate Tranche 4 - Release Proof Refresh

Goal: make the handoff artifact match the current source and the latest collected evidence.

Implementation TODOs:

- [ ] Re-run the standard proof gate from the latest source state.
- [ ] Refresh the release proof bundle with build/test/package logs, diagnostics, screenshots/audit notes, and selected tranche notes.
- [ ] Keep portable ZIP as the default release proof.
- [ ] Keep compiled installer and clean-machine claims separate unless Inno Setup and a clean profile/VM are actually used.
- [ ] Update readiness docs to separate implemented, locally proven, manually verified, OAuth parked, and later-scope work.

Proof TODOs:

- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] CLI `--help`
- [ ] CLI `diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] Release proof bundle artifact.

## Decision Tranche 5 - Pick One Later Module

Goal: choose exactly one larger post-local-V1 module before implementation starts.

Candidate modules:

- [ ] Browser-store publication/review/signing and automatic extension installation.
- [ ] Production Android live streaming beyond bounded screenshot/video import and preview execution.
- [ ] Signed/admin virtual-printer driver installation and clean-machine printer proof.
- [x] Operator-invoked plugin update apply/stage/install flow (`plugins apply-updates`) for already installed local plugins.
- [ ] Unattended background plugin updates and hosted plugin marketplace behavior.
- [ ] Hosted/self-hosted companion portal and remote/multi-user admin sync.

Decision TODOs:

- [ ] Write an approval note naming the selected module, authority boundary, threat/privacy risks, proof gate, and non-goals.
- [ ] Preserve desktop deny-wins policy, local consent, redaction, plugin trust, and provider-account boundaries.
- [ ] Start with the smallest read-only or operator-gated implementation surface.
- [ ] Add focused tests, diagnostics, artifact notes, and release proof before making implementation claims.

Recommended order if no preference is given:

1. Browser-store publication/readiness path, because extension scaffolding is already the furthest along.
2. Companion portal v0, starting read-only with policy/audit/report viewing.
3. Production Android live streaming after safe-device proof exists.
4. Signed virtual-printer driver after admin/signing/installer constraints are accepted.
5. Plugin marketplace/automatic updates only after stronger trust/update policy is approved.

## Parked OAuth/Live Account Lane

- [ ] Google Drive live OAuth consent proof.
- [ ] Dropbox live OAuth consent proof.
- [ ] OneDrive live OAuth consent proof.
- [ ] Refresh-token persistence, expiry, reauthorization, and recovery proof against live accounts.
- [ ] Provider-specific scopes, consent copy, account diagnostics, live upload proof, and live-account remote-delete behavior.

## Definition Of Done For Each Implementation Tranche

- [ ] Focused tests for changed services, models, CLI behavior, and UI models.
- [ ] WPF screenshot/render artifact or Product Design/WPF audit note for changed desktop UI.
- [ ] Redaction/privacy assertions when prompts, transcripts, OCR text, URLs, tokens, logs, settings, telemetry payloads, browser packages, Android media, plugin packages, or diagnostics are involved.
- [ ] `dotnet build .\GoatShot.slnx -c Release`
- [ ] `dotnet test .\GoatShot.slnx -c Release`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe --help`
- [ ] `src\GoatShot.Cli\bin\Release\net10.0-windows10.0.19041.0\GoatShot.Cli.exe diagnostics print`
- [ ] `.\scripts\package-release.ps1 -SkipInstaller`
- [ ] `artifacts/tranche-<name>/notes.md` records changed files, proof paths, skipped/manual proof, and remaining risk.

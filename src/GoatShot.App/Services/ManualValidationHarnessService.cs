using System.Globalization;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record ManualValidationHarnessRequest(
    string? OutputPath = null,
    DateOnly? Date = null,
    bool IncludeDiagnosticsBundle = false,
    bool Force = false);

public sealed record ManualValidationTemplateFile(
    string Id,
    string Title,
    string RelativePath,
    bool Created,
    bool Overwritten);

public sealed record ManualValidationHarnessResult(
    bool Succeeded,
    string RootPath,
    string SummaryPath,
    string ReadmePath,
    string CommandsPath,
    string DiagnosticsReadmePath,
    string? DiagnosticsBundlePath,
    IReadOnlyList<ManualValidationTemplateFile> Templates,
    IReadOnlyList<string> Warnings,
    string Message);

public sealed record ManualValidationLaneDefinition(
    string Id,
    string Title,
    string RelativePath,
    bool OAuthParked,
    ManualValidationLaneRequirement Requirement,
    string RequirementRationale);

public enum ManualValidationLaneRequirement
{
    Required,
    HardwareGated,
    OptionalCompatibility,
    Parked
}

public sealed class ManualValidationHarnessService
{
    public const string DefaultRootRelativePath = "artifacts/manual-validation";
    private const string DiagnosticsDirectoryName = "diagnostics";

    private static readonly IReadOnlyList<ManualValidationTemplateDefinition> TemplateDefinitions =
    [
        new(
            "baseline",
            "Baseline Setup",
            "01-baseline-setup.md",
            ManualValidationLaneRequirement.Required,
            "Required before handoff so the manual run has current build, diagnostics, and environment context.",
            "Record the build, package, machine, diagnostics, and capability probes before manual proof starts.",
            [
                "Start from the latest portable package or Release build.",
                "Confirm the tested source state passes Release build and tests.",
                "Run CLI diagnostics print and save output under diagnostics/.",
                "Run diagnostics recording, devices, providers, and capture-engine probes.",
                "Record whether WGC, D3D11, Media Foundation H.264/HEVC, WASAPI, camera, FFmpeg, and ffprobe are detected."
            ]),
        new(
            "keyboard-traversal",
            "Keyboard Traversal",
            "02-keyboard-traversal.md",
            ManualValidationLaneRequirement.Required,
            "Required for the local V1 desktop handoff because core WPF flows must be operable without a mouse.",
            "Verify keyboard-only operation through core WPF surfaces.",
            [
                "Tab through Main Window from startup without the mouse.",
                "Tab through Settings, Editor, AI review, capture task, share history, queue, and upload result windows.",
                "Confirm visible focus is present and focus order follows visual order closely enough to complete common tasks.",
                "Confirm disabled controls are skipped or announced as unavailable.",
                "Confirm Esc, Enter, arrows, and spacebar work where native WPF behavior implies they should.",
                "Confirm capture overlay can be dismissed and does not trap focus unexpectedly."
            ]),
        new(
            "screen-reader",
            "Screen Reader Narrator NVDA",
            "03-screen-reader-narrator-nvda.md",
            ManualValidationLaneRequirement.Required,
            "Required as observed assistive-technology proof for key WPF flows; this is not a WCAG compliance certification.",
            "Capture observed screen-reader behavior with Narrator and, if available, NVDA.",
            [
                "Main Window controls announce meaningful names and states.",
                "Settings section picker, provider fields, password boxes, Save/Clear buttons, and status text are understandable.",
                "Recording controls announce profile, source/output state, device state, preview state, and recording state.",
                "Editor tool groups, privacy tools, active tool status, and export actions are understandable.",
                "AI review, upload confirmation/result, share history, and queue states are understandable."
            ]),
        new(
            "text-scaling",
            "Text Scaling",
            "04-text-scaling.md",
            ManualValidationLaneRequirement.Required,
            "Required to verify the dense WPF settings and workflow surfaces remain readable at common Windows text scaling levels.",
            "Verify core flows remain readable and reachable at Windows text scaling levels.",
            [
                "Test Windows text scaling at 125%.",
                "Test Windows text scaling at 150%.",
                "Test Windows text scaling at 200% if usable on the display.",
                "Verify buttons, labels, provider fields, status text, and scroll regions do not overlap or become unreachable.",
                "Capture safe screenshots for Main Window, Settings, Editor, Recording controls, AI review, Share History, and Upload Result."
            ]),
        new(
            "high-contrast",
            "High Contrast",
            "05-high-contrast.md",
            ManualValidationLaneRequirement.Required,
            "Required to catch obvious contrast and focus visibility issues in Windows high-contrast mode.",
            "Verify focus, selected states, text, and controls remain visible under a Windows high-contrast theme.",
            [
                "Enable at least one Windows high-contrast theme.",
                "Verify critical text and controls remain readable.",
                "Verify focus outlines and selected states remain visible.",
                "Verify scroll areas and modal/dialog states remain operable.",
                "Capture safe screenshots of any visual defects."
            ]),
        new(
            "live-region-drag",
            "Live Region Drag Path",
            "06-live-region-drag.md",
            ManualValidationLaneRequirement.Required,
            "Required because the capture overlay is a primary Screenpresso-replacement workflow and cannot be fully proven by static screenshots.",
            "Validate real mouse-driven region selection on safe desktop content.",
            [
                "Start interactive region capture from the desktop app.",
                "Drag a region by hand on safe demo content.",
                "Verify edge snapping can be observed and bypassed with Shift.",
                "Verify pixel lens, size badge, padding, chooser, cancel, and complete behavior.",
                "Save safe screenshot/recording evidence only when it contains no private desktop content."
            ]),
        new(
            "multi-monitor-capture",
            "Multi Monitor Capture",
            "07-multi-monitor-capture.md",
            ManualValidationLaneRequirement.HardwareGated,
            "Required only when multi-monitor hardware is available; otherwise it remains a documented hardware proof gap.",
            "Validate live capture target behavior across the available display topology.",
            [
                "Record monitor count, layout, scaling, and primary monitor.",
                "Capture active monitor.",
                "Capture all monitors.",
                "Capture a fixed region fully inside one monitor.",
                "Capture a cross-monitor region if layout allows.",
                "Verify output dimensions, visible content, and cursor behavior."
            ]),
        new(
            "multi-monitor-recording",
            "Multi Monitor Recording",
            "08-multi-monitor-recording.md",
            ManualValidationLaneRequirement.HardwareGated,
            "Required only when multi-monitor hardware is available and safe content can be staged.",
            "Validate MP4 recording behavior across single-monitor, all-monitor, and cross-monitor targets with safe content.",
            [
                "Record active monitor MP4 with safe content.",
                "Record all monitors MP4 with safe content.",
                "Record a fixed or region target on one monitor.",
                "Record a cross-monitor region if layout allows.",
                "Verify output dimensions, overlays, cursor/click visualization, encoder choice, and playback."
            ]),
        new(
            "long-recording",
            "Long Recording Stability",
            "09-long-recording.md",
            ManualValidationLaneRequirement.HardwareGated,
            "Required only for long-run hardware stability claims; not running it keeps that claim unproven.",
            "Validate long-run recording stability with real hardware and safe content.",
            [
                "Record at least 30 minutes using a safe desktop scene.",
                "Include microphone if permission is granted and safe narration is available.",
                "Include system audio loopback if available and safe.",
                "Include webcam overlay only with a safe camera scene.",
                "Verify output plays in a standard media player.",
                "Save ffprobe output and audio/video duration drift when ffprobe is installed."
            ]),
        new(
            "clean-machine-install",
            "Clean Machine Portable Installer",
            "10-clean-machine-portable-installer.md",
            ManualValidationLaneRequirement.Required,
            "Required before claiming clean-profile or installer readiness beyond the local portable-package verifier.",
            "Validate the portable ZIP and optional installer on a clean Windows profile or VM.",
            [
                "Test the portable ZIP on a clean Windows user profile or VM.",
                "Launch the app and confirm user folders are created in expected locations.",
                "Confirm Settings can be saved.",
                "Confirm a simple capture/import/edit/export loop.",
                "If Inno Setup tooling is available, compile and install the installer on a clean machine.",
                "Confirm uninstall behavior does not remove user captures without explicit operator action."
            ]),
        new(
            "live-provider-proof",
            "Live Provider Account Proof",
            "11-live-provider-account-proof.md",
            ManualValidationLaneRequirement.Parked,
            "Parked while OAuth/live-provider proof is intentionally out of scope for this buildout pass.",
            "Capture live provider account proof only when a dedicated account/provider lane is scheduled.",
            [
                "Keep Google Drive, Dropbox, and OneDrive OAuth parked unless explicitly scheduled.",
                "Use disposable or test accounts for live providers.",
                "Verify upload, link creation, history redaction, queue retry/cancel, and failure recovery.",
                "Redact provider account names, email addresses, tokens, callback URLs with codes, signed upload URLs, and private paths.",
                "Do not mark live account proof passed from fake providers, local tokens, or synthetic upload tests."
            ]),
        new(
            "browser-extension-live-fixture",
            "Browser Extension Live Fixture",
            "12-browser-extension-live-fixture.md",
            ManualValidationLaneRequirement.OptionalCompatibility,
            "Optional compatibility proof for additional browsers; the current V1 claim relies on completed Edge live proof plus local multi-target artifacts.",
            "Validate the optional browser extension with the safe local fixture, without claiming browser-store publication or automatic installation.",
            [
                "Use only `browser-extension/samples/safe-fixture.html` unless another safe page is explicitly staged.",
                "Prefer the generated isolated Chrome/Edge launch script when available; otherwise load the unpacked extension from the local `browser-extension/` folder in the target browser.",
                "Copy the generated extension id and install the user-scope native host for that browser.",
                "Run `receipts browser-extension diagnostics --source <browser-extension-folder>` and save text and JSON output.",
                "Run `receipts browser-extension live-fixture --browser <browser> --source <browser-extension-folder>` to generate helper commands, proof notes, safe-fixture server script, isolated launch script, and browser launch plan for this lane.",
                "Capture safe screenshots of loaded extension details, popup consent defaults, options consent defaults, Host Status result, selected-element mode, package-export toggle, and last handoff result.",
                "Run a consented fixture capture with screenshot consent and package export enabled.",
                "Import the downloaded `Receipts/<correlationId>/` stitch package with `receipts browser-extension live-fixture --payload <payload.json> --stitch-package <package-folder>` or `receipts browser-extension receive --stitch-package`.",
                "Save redacted payload/import JSON and record browser name/version, extension id, package folder, pass/fail result, and limitations.",
                "Do not claim browser-store publication, automatic installation, or cross-browser parity unless separately proven."
            ]),
        new(
            "android-safe-device-proof",
            "Android Safe Device Proof",
            "13-android-safe-device-proof.md",
            ManualValidationLaneRequirement.HardwareGated,
            "Required only when a safe Android device is staged; production Android streaming remains post-V1.",
            "Validate Android screenshot/video/preview behavior only with staged safe phone content.",
            [
                "Stage safe phone content with no private notifications, chats, email, credentials, photos, or account data visible.",
                "Run `receipts diagnostics android --json` and save output.",
                "If multiple ready devices are connected, record the selected serial and use `--device`.",
                "Run `receipts capture android --output <safe-output.png> --device <serial> --json` only after the safe content check.",
                "Run bounded `receipts capture android-video --duration <seconds> --output <safe-output.mp4> --device <serial> --json` only when safe motion/content is staged.",
                "Run `receipts capture android-preview --strategy screencap-polling --duration 5 --device <serial> --json` as a dry-run preview plan and confirm it does not import streamed media.",
                "Optionally run guarded `receipts capture android-preview --execute --safe-content-confirmed --strategy h264-stream --duration 5 --device <serial> --output-dir <safe-preview-folder> --json` only after staging safe phone content, then verify the raw H.264 stream/remux output contains only safe content.",
                "Verify outputs contain only safe content and record ADB path, device authorization state, duration, cleanup behavior, and any disconnect/timeout warnings.",
                "Do not claim production Android live streaming from screenshot import, bounded screenrecord import, dry-run preview planning, guarded screencap preview execution, or guarded H.264 preview execution."
            ])
    ];

    public async Task<ManualValidationHarnessResult> CreateAsync(
        ManualValidationHarnessRequest request,
        Func<string, CancellationToken, Task<DiagnosticBundleResult>>? createDiagnosticBundle = null,
        CancellationToken cancellationToken = default)
    {
        var date = request.Date ?? DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime);
        var root = ResolveRoot(request.OutputPath, date);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, DiagnosticsDirectoryName));

        var warnings = new List<string>();
        var generatedAt = DateTimeOffset.Now;
        var writeContext = new WriteContext(request.Force, new List<ManualValidationTemplateFile>());

        var readmePath = WriteText(root, "README.md", BuildReadme(date, generatedAt), "readme", "README", writeContext);
        var commandsPath = WriteText(root, "commands.md", BuildCommands(), "commands", "Command Reference", writeContext);
        var diagnosticsReadmePath = WriteText(root, Path.Combine(DiagnosticsDirectoryName, "README.md"), BuildDiagnosticsReadme(includeBundleCreated: false, diagnosticsBundlePath: null), "diagnostics", "Diagnostics README", writeContext);

        foreach (var definition in TemplateDefinitions)
        {
            WriteText(
                root,
                definition.FileName,
                BuildTemplate(date, generatedAt, definition),
                definition.Id,
                definition.Title,
                writeContext);
        }

        string? diagnosticsBundlePath = null;
        var diagnosticsSucceeded = true;
        if (request.IncludeDiagnosticsBundle)
        {
            if (createDiagnosticBundle is null)
            {
                diagnosticsSucceeded = false;
                warnings.Add("Diagnostics bundle was requested, but no diagnostic bundle factory was supplied.");
            }
            else
            {
                diagnosticsBundlePath = Path.Combine(root, DiagnosticsDirectoryName, ManualValidationBaselineService.DiagnosticsBundleFileName);
                var bundle = await createDiagnosticBundle(diagnosticsBundlePath, cancellationToken);
                if (!bundle.Succeeded || string.IsNullOrWhiteSpace(bundle.Path))
                {
                    diagnosticsSucceeded = false;
                    warnings.Add($"Diagnostics bundle creation failed: {bundle.Message}");
                }
                else
                {
                    diagnosticsBundlePath = bundle.Path;
                    diagnosticsReadmePath = WriteText(
                        root,
                        Path.Combine(DiagnosticsDirectoryName, "README.md"),
                        BuildDiagnosticsReadme(includeBundleCreated: true, diagnosticsBundlePath),
                        "diagnostics",
                        "Diagnostics README",
                        writeContext,
                        overwriteExisting: true);
                }
            }
        }

        var summaryPath = WriteText(
            root,
            "summary.md",
            BuildSummary(date, generatedAt, root, diagnosticsBundlePath, warnings),
            "summary",
            "Summary",
            writeContext);

        var resultTemplates = writeContext.Templates
            .Where(file => file.Id is not "readme" and not "commands" and not "diagnostics" and not "summary")
            .ToList();
        var succeeded = diagnosticsSucceeded;
        var message = succeeded
            ? $"Manual validation evidence folder ready: {root}"
            : $"Manual validation evidence folder created with warning(s): {root}";
        return new ManualValidationHarnessResult(
            succeeded,
            root,
            summaryPath,
            readmePath,
            commandsPath,
            diagnosticsReadmePath,
            diagnosticsBundlePath,
            resultTemplates,
            warnings,
            message);
    }

    public static IReadOnlyList<string> TemplateFileNames =>
        TemplateDefinitions.Select(definition => definition.FileName).ToList();

    public static IReadOnlyList<ManualValidationLaneDefinition> ExpectedLanes =>
        TemplateDefinitions.Select(definition => new ManualValidationLaneDefinition(
            definition.Id,
            definition.Title,
            definition.FileName,
            OAuthParked: definition.Requirement is ManualValidationLaneRequirement.Parked,
            definition.Requirement,
            definition.RequirementRationale)).ToList();

    private static string ResolveRoot(string? outputPath, DateOnly date)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        }

        return Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            DefaultRootRelativePath,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    private static string WriteText(
        string root,
        string relativePath,
        string content,
        string id,
        string title,
        WriteContext context,
        bool overwriteExisting = false)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var exists = File.Exists(path);
        if (!exists || context.Force || overwriteExisting)
        {
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        context.Templates.Add(new ManualValidationTemplateFile(
            id,
            title,
            relativePath.Replace('\\', '/'),
            Created: !exists,
            Overwritten: exists && (context.Force || overwriteExisting)));
        return path;
    }

    private static string BuildReadme(DateOnly date, DateTimeOffset generatedAt)
    {
        return string.Join(
            Environment.NewLine,
            [
                "# Receipts Manual Validation Evidence",
                string.Empty,
                $"Date: {date:yyyy-MM-dd}",
                $"Generated at: {generatedAt:O}",
                string.Empty,
                "Use this folder to collect human/device/provider proof that cannot be honestly completed by deterministic local tests.",
                string.Empty,
                "## Safety Rules",
                string.Empty,
                "- Use safe demo desktop content only.",
                "- Do not capture private email, chats, credentials, customer data, personal files, or production systems.",
                "- Redact provider account names, email addresses, tokens, OAuth codes, signed upload URLs, and private filesystem paths before sharing externally.",
                "- Do not mark a lane passed unless a human actually performed the stated interaction.",
                "- Do not claim accessibility compliance from these checks alone; record observed keyboard, focus, screen-reader, contrast, and scaling behavior.",
                string.Empty,
                "## Suggested Flow",
                string.Empty,
                "1. Complete `01-baseline-setup.md` first.",
                "2. Run the diagnostics commands from `commands.md` and save outputs under `diagnostics/`.",
                "3. Work through each lane template and attach only safe screenshots or recordings.",
                "4. Update `summary.md` with final pass/fail/blocked status and public-claim boundaries."
            ]);
    }

    private static string BuildCommands()
    {
        return string.Join(
            Environment.NewLine,
            [
                "# Manual Validation Command Reference",
                string.Empty,
                "Run from the Receipts repository root unless noted.",
                string.Empty,
                "```powershell",
                "dotnet build .\\GoatShot.slnx -c Release *> .\\diagnostics\\build-release.txt",
                "dotnet test .\\GoatShot.slnx -c Release *> .\\diagnostics\\test-release.txt",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics print *> .\\diagnostics\\diagnostics-print.txt",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics bundle --output .\\diagnostics\\receipts-diagnostics.zip",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics recording --json *> .\\diagnostics\\recording-readiness.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe record devices --json *> .\\diagnostics\\recording-devices.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics capture-engine --engine wgc --json *> .\\diagnostics\\capture-engine-wgc.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics providers --json *> .\\diagnostics\\providers.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe browser-extension diagnostics --source .\\browser-extension --json *> .\\diagnostics\\browser-extension-diagnostics.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe browser-extension live-fixture --browser chrome --source .\\browser-extension --output . --json *> .\\diagnostics\\browser-extension-live-fixture.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe diagnostics android --json *> .\\diagnostics\\android-diagnostics.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe capture android-preview --strategy screencap-polling --duration 5 --json *> .\\diagnostics\\android-preview-plan.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe manual-validation hardware-proof --folder . --run-commands --json *> .\\hardware-proof-cli.json",
                ".\\src\\GoatShot.Cli\\bin\\Release\\net10.0-windows10.0.19041.0\\Receipts.Cli.exe manual-validation summarize --folder . --json *> .\\manual-validation-summary-cli.json",
                "```",
                string.Empty,
                "Privacy reminder: diagnostic bundles are designed to exclude capture media, thumbnails, full OCR text, AI payloads, DPAPI secret files, raw tokens, and private desktop recordings. Review before sharing externally anyway."
            ]);
    }

    private static string BuildDiagnosticsReadme(bool includeBundleCreated, string? diagnosticsBundlePath)
    {
        var bundleLine = includeBundleCreated && !string.IsNullOrWhiteSpace(diagnosticsBundlePath)
            ? $"- Diagnostics bundle created: `{Path.GetFileName(diagnosticsBundlePath)}`"
            : "- Diagnostics bundle not created yet. Run the command in `../commands.md` or recreate the harness with `--include-diagnostics-bundle`.";
        return string.Join(
            Environment.NewLine,
            [
                "# Diagnostics Attachments",
                string.Empty,
                bundleLine,
                "- Save command output from build, tests, diagnostics print, recording readiness, devices, capture-engine, and provider diagnostics here.",
                "- Do not place raw capture images, videos, OCR dumps, OAuth callback URLs with codes, tokens, signed upload URLs, or private desktop recordings in this folder unless they are safe and redacted.",
                "- Prefer diagnostic bundles and bounded text output over screenshots of private system state."
            ]);
    }

    private static string BuildTemplate(DateOnly date, DateTimeOffset generatedAt, ManualValidationTemplateDefinition definition)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {definition.Title}");
        builder.AppendLine();
        builder.AppendLine($"Date: {date:yyyy-MM-dd}");
        builder.AppendLine($"Generated at: {generatedAt:O}");
        builder.AppendLine();
        builder.AppendLine($"Purpose: {definition.Purpose}");
        builder.AppendLine();
        builder.AppendLine("## Requirement");
        builder.AppendLine();
        builder.AppendLine($"- Classification: {FormatRequirement(definition.Requirement)}");
        builder.AppendLine($"- Rationale: {definition.RequirementRationale}");
        builder.AppendLine("- Required lanes block the local V1 manual handoff when left pending or not run.");
        builder.AppendLine("- Hardware-gated and optional compatibility lanes must stay visible but do not prove their corresponding claims until passed.");
        builder.AppendLine("- Parked lanes stay out of the current proof gate until explicitly scheduled.");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine("- [ ] Pending");
        builder.AppendLine("- [ ] Passed");
        builder.AppendLine("- [ ] Failed");
        builder.AppendLine("- [ ] Blocked");
        builder.AppendLine();
        builder.AppendLine("## Safety And Redaction");
        builder.AppendLine();
        builder.AppendLine("- [ ] Safe demo content only; no private email, chats, credentials, customer data, production systems, or personal files.");
        builder.AppendLine("- [ ] Provider account names, email addresses, OAuth codes, tokens, signed upload URLs, and private filesystem paths are redacted before sharing externally.");
        builder.AppendLine("- [ ] Evidence files added for this lane are safe to store locally and safe to include in a handoff.");
        builder.AppendLine("- [ ] This lane is described as observed behavior only, not full compliance certification.");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine("- Receipts build/package:");
        builder.AppendLine("- Windows version:");
        builder.AppendLine("- Display/monitor setup:");
        builder.AppendLine("- Input/audio/camera devices:");
        builder.AppendLine("- FFmpeg/ffprobe availability:");
        builder.AppendLine("- Diagnostics bundle or diagnostics output path:");
        builder.AppendLine();
        builder.AppendLine("## Checklist");
        builder.AppendLine();
        foreach (var item in definition.Checklist)
        {
            builder.AppendLine($"- [ ] {item}");
        }

        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        builder.AppendLine("- Screenshots:");
        builder.AppendLine("- Recordings:");
        builder.AppendLine("- Command output:");
        builder.AppendLine("- Related files:");
        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        builder.AppendLine("- Observed behavior:");
        builder.AppendLine("- Defects or follow-ups:");
        builder.AppendLine("- Public claim boundary:");
        return builder.ToString();
    }

    private static string BuildSummary(
        DateOnly date,
        DateTimeOffset generatedAt,
        string root,
        string? diagnosticsBundlePath,
        IReadOnlyList<string> warnings)
    {
        var laneRows = TemplateDefinitions
            .Select(definition => $"| {definition.Title} | {FormatRequirement(definition.Requirement)} | Pending | `{definition.FileName}` |")
            .ToList();
        var warningLines = warnings.Count == 0
            ? "- None."
            : string.Join(Environment.NewLine, warnings.Select(warning => $"- {warning}"));
        var diagnosticsLine = string.IsNullOrWhiteSpace(diagnosticsBundlePath)
            ? "Not created yet."
            : $"`{Path.GetRelativePath(root, diagnosticsBundlePath).Replace('\\', '/')}`";

        return string.Join(
            Environment.NewLine,
            [
                "# Receipts Manual Validation Summary",
                string.Empty,
                $"Date: {date:yyyy-MM-dd}",
                $"Generated at: {generatedAt:O}",
                string.Empty,
                "## Status",
                string.Empty,
                "| Lane | Requirement | Result | Notes File |",
                "|---|---|---|---|",
                .. laneRows,
                string.Empty,
                "## Diagnostics",
                string.Empty,
                $"- Diagnostics bundle: {diagnosticsLine}",
                "- Command references: `commands.md`",
                "- Diagnostics folder: `diagnostics/`",
                string.Empty,
                "## Safety Boundary",
                string.Empty,
                "- Safe demo content only.",
                "- Redact credentials, OAuth codes, signed URLs, private paths, account identifiers, OCR dumps, and private desktop content before sharing externally.",
                "- Manual observations do not equal WCAG/accessibility compliance certification.",
                "- OAuth/live account proof remains parked unless this manual lane is explicitly scheduled.",
                string.Empty,
                "## Warnings",
                string.Empty,
                warningLines,
                string.Empty,
                "## Final Sign-Off",
                string.Empty,
                "- [ ] Every required lane has a result.",
                "- [ ] Failures have follow-up notes.",
                "- [ ] Public claims match the evidence in this folder."
            ]);
    }

    private static string FormatRequirement(ManualValidationLaneRequirement requirement) =>
        requirement switch
        {
            ManualValidationLaneRequirement.Required => "Required",
            ManualValidationLaneRequirement.HardwareGated => "Hardware-gated",
            ManualValidationLaneRequirement.OptionalCompatibility => "Optional compatibility",
            ManualValidationLaneRequirement.Parked => "Parked",
            _ => requirement.ToString()
        };

    private sealed record ManualValidationTemplateDefinition(
        string Id,
        string Title,
        string FileName,
        ManualValidationLaneRequirement Requirement,
        string RequirementRationale,
        string Purpose,
        IReadOnlyList<string> Checklist);

    private sealed record WriteContext(
        bool Force,
        List<ManualValidationTemplateFile> Templates);
}

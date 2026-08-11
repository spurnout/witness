using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPen = System.Drawing.Pen;
using DrawingSolidBrush = System.Drawing.SolidBrush;
using Forms = System.Windows.Forms;

namespace GoatShot.App.Services;

public sealed class ManualValidationDesktopProofService
{
    public const string ProofDirectoryName = "desktop-proof";
    public const string CommandResultsFileName = "desktop-proof-command-results.json";
    public const string EnvironmentReportFileName = "environment.md";
    public const string EnvironmentJsonFileName = "environment.json";
    public const string SummaryMarkdownFileName = "desktop-proof-summary.md";
    public const string SummaryJsonFileName = "desktop-proof-summary.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly IReadOnlyDictionary<string, DesktopProofLanePlan> LanePlans =
        new Dictionary<string, DesktopProofLanePlan>(StringComparer.OrdinalIgnoreCase)
        {
            ["keyboard"] = new(
                "02-keyboard-traversal.md",
                "Keyboard Traversal",
                [
                    "desktop-proof/audits/main-accessibility.md",
                    "desktop-proof/audits/settings-accessibility.md",
                    "desktop-proof/audits/editor-accessibility.md",
                    "desktop-proof/audits/recording-accessibility.md",
                    "desktop-proof/screenshots/main-window.png",
                    "desktop-proof/screenshots/settings-sharing.png",
                    "desktop-proof/screenshots/editor.png",
                    "desktop-proof/screenshots/tray-menu.png",
                    "desktop-proof/screenshots/capture-overlay.png",
                    "desktop-proof/screenshots/share-history.png",
                    "desktop-proof/screenshots/ai-review.png",
                    "desktop-proof/screenshots/upload-result.png",
                    "desktop-proof/screenshots/proof-scene.png",
                    "desktop-proof/audits/proof-scene-accessibility.md"
                ],
                [
                    "Programmatic WPF focus/name audits were collected for Main Window, Settings, Editor, and Recording controls.",
                    "Safe app-owned screenshots were rendered for Main Window, Settings/provider setup, Editor, tray menu, capture overlay, upload result, share history, AI review, and capture task surfaces.",
                    "A safe proof scene screenshot and focus/name audit were collected for operator keyboard traversal staging.",
                    "A human Tab traversal is still required before this lane can be completed."
                ],
                "Human keyboard traversal was not performed by this command helper; keep this lane blocked until an operator completes the Tab/Shift+Tab/Enter/Esc/arrow-key pass on safe content."),
            ["screen-reader"] = new(
                "03-screen-reader-narrator-nvda.md",
                "Screen Reader Narrator NVDA",
                [
                    "desktop-proof/audits/main-accessibility.md",
                    "desktop-proof/audits/settings-accessibility.md",
                    "desktop-proof/audits/settings-sharing-accessibility.md",
                    "desktop-proof/audits/settings-recording-accessibility.md",
                    "desktop-proof/audits/editor-accessibility.md",
                    "desktop-proof/audits/recording-accessibility.md",
                    "desktop-proof/audits/proof-scene-accessibility.md"
                ],
                [
                    "WPF automation-name and live-region audits were collected as screen-reader preparation evidence.",
                    "A safe proof scene audit was collected so Narrator/NVDA can be checked against private-free controls and live status.",
                    "The helper verified app-owned focus/name evidence, not real Narrator or NVDA spoken output.",
                    "A Narrator/NVDA observation pass is still required before this lane can be completed."
                ],
                "Narrator/NVDA was not driven or observed by this helper; keep this lane blocked until assistive-technology output is checked by an operator."),
            ["text-scaling"] = new(
                "04-text-scaling.md",
                "Text Scaling",
                [
                    "desktop-proof/environment.md",
                    "desktop-proof/screenshots/main-window.png",
                    "desktop-proof/screenshots/settings-sharing.png",
                    "desktop-proof/screenshots/editor.png",
                    "desktop-proof/screenshots/upload-result.png",
                    "desktop-proof/screenshots/share-history.png",
                    "desktop-proof/screenshots/proof-scene.png"
                ],
                [
                    "Current-machine display, DPI, and text-scale evidence was recorded.",
                    "Screenshots were collected at the current Windows scale only.",
                    "A safe proof scene screenshot was collected for operator-controlled 125%, 150%, and 200% scale comparison.",
                    "125%, 150%, and 200% Windows text-scaling passes still require operator-controlled OS setting changes."
                ],
                "The helper does not mutate Windows text-scaling settings; keep this lane blocked until an operator checks the required scale values and records any clipping/overlap."),
            ["high-contrast"] = new(
                "05-high-contrast.md",
                "High Contrast",
                [
                    "desktop-proof/environment.md",
                    "desktop-proof/screenshots/main-window.png",
                    "desktop-proof/screenshots/settings-sharing.png",
                    "desktop-proof/screenshots/editor.png",
                    "desktop-proof/screenshots/proof-scene.png"
                ],
                [
                    "Current high-contrast state was recorded.",
                    "Current-theme screenshots were collected for key surfaces.",
                    "A safe proof scene screenshot was collected for operator-controlled high-contrast state comparison.",
                    "A Windows high-contrast theme pass still requires operator-controlled OS theme changes."
                ],
                "The helper does not switch Windows high-contrast themes; keep this lane blocked until focus, selected states, and control affordances are checked under a high-contrast theme."),
            ["live-region-drag"] = new(
                "06-live-region-drag.md",
                "Live Region Drag Path",
                [
                    "desktop-proof/screenshots/capture-overlay.png",
                    "desktop-proof/screenshots/proof-scene.png",
                    "desktop-proof/audits/proof-scene-accessibility.md",
                    "desktop-proof/audits/main-accessibility.md"
                ],
                [
                    "The capture overlay preview was rendered using app-owned synthetic proof output.",
                    "A safe proof scene with app-owned draggable content was rendered for live region-drag staging.",
                    "This confirms overlay UI can be rendered for review, but not live hand drag behavior.",
                    "A human region-drag pass is still required before this lane can be completed."
                ],
                "The helper cannot perform a real mouse drag on staged safe desktop content; keep this lane blocked until drag, snapping, Shift bypass, chooser, cancel, and completion are observed."),
            ["clean-machine"] = new(
                "10-clean-machine-portable-installer.md",
                "Clean Machine Portable Installer",
                [
                    "desktop-proof/environment.md",
                    "../../dist/Receipts-*-win-x64-portable.zip"
                ],
                [
                    "Current-machine environment evidence and portable package path were recorded.",
                    "This is not a clean Windows profile or VM.",
                    "A clean-profile/VM portable launch or installer click-through remains required before this lane can be completed."
                ],
                "This helper ran on the current machine only; keep this lane blocked until a clean Windows user profile or VM validates first launch, settings save, basic capture/import/edit/export, and optional installer behavior.")
        };

    public async Task<ManualValidationDesktopProofResult> CollectAsync(
        ManualValidationDesktopProofRequest request,
        IManualValidationDesktopProofCommandRunner? runner = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationDesktopProofResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationDesktopProofResult.Failed($"Manual validation folder was not found: {root}");
        }

        foreach (var plan in LanePlans.Values)
        {
            var path = Path.Combine(root, plan.FileName);
            if (!File.Exists(path))
            {
                return ManualValidationDesktopProofResult.Failed($"Expected manual validation template was not found: {path}");
            }
        }

        var proofRoot = Path.Combine(root, ProofDirectoryName);
        var screenshotsRoot = Path.Combine(proofRoot, "screenshots");
        var auditsRoot = Path.Combine(proofRoot, "audits");
        var logsRoot = Path.Combine(proofRoot, "logs");
        Directory.CreateDirectory(proofRoot);
        Directory.CreateDirectory(screenshotsRoot);
        Directory.CreateDirectory(auditsRoot);
        Directory.CreateDirectory(logsRoot);

        runner ??= new ProcessManualValidationDesktopProofCommandRunner();
        var commandResults = request.RunCommands
            ? await RunCommandsAsync(root, proofRoot, screenshotsRoot, auditsRoot, logsRoot, request, runner, cancellationToken)
            : await ReadExistingCommandResultsAsync(proofRoot, cancellationToken);

        var environment = BuildEnvironmentReport(root, proofRoot, request);
        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, EnvironmentJsonFileName),
            JsonSerializer.Serialize(environment, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, EnvironmentReportFileName),
            BuildEnvironmentMarkdown(environment),
            cancellationToken);

        var failedCommands = commandResults
            .Where(command => command.ExitCode != 0)
            .ToList();
        var laneStatus = failedCommands.Count == 0
            ? ManualValidationLaneStatus.Blocked
            : ManualValidationLaneStatus.Failed;
        var hasCommandEvidence = commandResults.Count > 0;
        var succeeded = hasCommandEvidence && failedCommands.Count == 0;

        foreach (var plan in LanePlans.Values)
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, plan.FileName),
                BuildLaneMarkdown(root, proofRoot, plan, laneStatus, commandResults, failedCommands, environment),
                cancellationToken);
        }

        var evidenceBeforeSummary = BuildEvidenceList(root, proofRoot);
        var summary = BuildSummary(root, proofRoot, laneStatus, commandResults, failedCommands, environment, evidenceBeforeSummary);
        var summaryJsonPath = Path.Combine(proofRoot, SummaryJsonFileName);
        var summaryMarkdownPath = Path.Combine(proofRoot, SummaryMarkdownFileName);
        await File.WriteAllTextAsync(
            summaryJsonPath,
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            cancellationToken);
        await File.WriteAllTextAsync(
            summaryMarkdownPath,
            BuildSummaryMarkdown(summary),
            cancellationToken);

        var result = new ManualValidationDesktopProofResult
        {
            Succeeded = succeeded,
            Message = succeeded
                ? "Desktop accessibility proof packet was collected; human-only lanes remain blocked."
                : failedCommands.Count > 0
                    ? $"Desktop accessibility proof packet has {failedCommands.Count} failed command(s)."
                    : "Desktop accessibility proof packet is blocked: no command results were recorded.",
            RootPath = root,
            ProofDirectory = proofRoot,
            CommandResultsPath = Path.Combine(proofRoot, CommandResultsFileName),
            EnvironmentReportPath = Path.Combine(proofRoot, EnvironmentReportFileName),
            EnvironmentJsonPath = Path.Combine(proofRoot, EnvironmentJsonFileName),
            SummaryMarkdownPath = summaryMarkdownPath,
            SummaryJsonPath = summaryJsonPath,
            Summary = summary,
            Status = laneStatus,
            Commands = commandResults,
            FailedCommands = failedCommands.Select(command => command.Name).ToList(),
            UpdatedLaneFiles = LanePlans.Values.Select(plan => plan.FileName).ToList(),
            EvidenceFiles = BuildEvidenceList(root, proofRoot)
        };

        return result;
    }

    private static async Task<List<ManualValidationDesktopProofCommandResult>> RunCommandsAsync(
        string root,
        string proofRoot,
        string screenshotsRoot,
        string auditsRoot,
        string logsRoot,
        ManualValidationDesktopProofRequest request,
        IManualValidationDesktopProofCommandRunner runner,
        CancellationToken cancellationToken)
    {
        var editorSource = Path.Combine(proofRoot, "editor-source.png");
        CreateSyntheticEditorImage(editorSource);

        var commands = BuildCommands(root, screenshotsRoot, auditsRoot, logsRoot, request, editorSource).ToList();
        var results = new List<ManualValidationDesktopProofCommandResult>();
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(command.LogPath)!);
            var result = await runner.RunAsync(command, cancellationToken);
            results.Add(result);
            await File.WriteAllTextAsync(command.LogPath, FormatCommandOutput(result), cancellationToken);
        }

        await File.WriteAllTextAsync(
            Path.Combine(proofRoot, CommandResultsFileName),
            JsonSerializer.Serialize(results, JsonOptions) + Environment.NewLine,
            cancellationToken);
        return results;
    }

    private static IEnumerable<ManualValidationDesktopProofCommandSpec> BuildCommands(
        string root,
        string screenshotsRoot,
        string auditsRoot,
        string logsRoot,
        ManualValidationDesktopProofRequest request,
        string editorSourcePath)
    {
        var appPath = ResolveAppPath(request.AppPath);
        var workingDirectory = string.IsNullOrWhiteSpace(request.RepoRoot)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RepoRoot));
        var timeoutSeconds = request.TimeoutSeconds <= 0 ? 120 : request.TimeoutSeconds;

        ManualValidationDesktopProofCommandSpec Cmd(string name, string[] args, string outputPath) =>
            new(name, appPath, args, workingDirectory, outputPath, Path.Combine(logsRoot, SafeFileName(name) + ".txt"), timeoutSeconds);

        yield return Cmd("Render Main Window", ["--render-main-output", Path.Combine(screenshotsRoot, "main-window.png")], Path.Combine(screenshotsRoot, "main-window.png"));
        yield return Cmd("Render Settings Sharing", ["--render-settings-section", "Sharing", "--render-settings-output", Path.Combine(screenshotsRoot, "settings-sharing.png")], Path.Combine(screenshotsRoot, "settings-sharing.png"));
        yield return Cmd("Render Settings Recording", ["--render-settings-section", "Recording", "--render-settings-output", Path.Combine(screenshotsRoot, "settings-recording.png")], Path.Combine(screenshotsRoot, "settings-recording.png"));
        yield return Cmd("Render Settings Replay", ["--render-settings-section", "Replay", "--render-settings-output", Path.Combine(screenshotsRoot, "settings-replay.png")], Path.Combine(screenshotsRoot, "settings-replay.png"));
        yield return Cmd("Render Settings Automation", ["--render-settings-section", "Automation", "--render-settings-output", Path.Combine(screenshotsRoot, "settings-automation.png")], Path.Combine(screenshotsRoot, "settings-automation.png"));
        yield return Cmd("Render Editor", ["--render-editor", "--editor-image", editorSourcePath, "--render-editor-output", Path.Combine(screenshotsRoot, "editor.png")], Path.Combine(screenshotsRoot, "editor.png"));
        yield return Cmd("Render Tray Menu", ["--render-tray-menu-output", Path.Combine(screenshotsRoot, "tray-menu.png")], Path.Combine(screenshotsRoot, "tray-menu.png"));
        yield return Cmd("Render Capture Overlay", ["--render-capture-overlay-output", Path.Combine(screenshotsRoot, "capture-overlay.png")], Path.Combine(screenshotsRoot, "capture-overlay.png"));
        yield return Cmd("Render Upload Result", ["--render-upload-task-surface", "result", "--render-upload-task-output", Path.Combine(screenshotsRoot, "upload-result.png")], Path.Combine(screenshotsRoot, "upload-result.png"));
        yield return Cmd("Render Share History", ["--render-share-history-output", Path.Combine(screenshotsRoot, "share-history.png")], Path.Combine(screenshotsRoot, "share-history.png"));
        yield return Cmd("Render AI Review", ["--render-ai-history-output", Path.Combine(screenshotsRoot, "ai-review.png")], Path.Combine(screenshotsRoot, "ai-review.png"));
        yield return Cmd("Render Capture Task", ["--render-capture-task-output", Path.Combine(screenshotsRoot, "capture-task.png")], Path.Combine(screenshotsRoot, "capture-task.png"));
        yield return Cmd("Render Proof Scene", ["--render-proof-scene-output", Path.Combine(screenshotsRoot, "proof-scene.png")], Path.Combine(screenshotsRoot, "proof-scene.png"));
        yield return Cmd("Render Frame Explorer", ["--render-frame-explorer-output", Path.Combine(screenshotsRoot, "frame-explorer.png")], Path.Combine(screenshotsRoot, "frame-explorer.png"));
        yield return Cmd("Audit WPF Main", ["--audit-wpf-surface", "main", "--audit-wpf-output", Path.Combine(auditsRoot, "main-accessibility.md")], Path.Combine(auditsRoot, "main-accessibility.md"));
        yield return Cmd("Audit WPF Settings", ["--audit-wpf-surface", "settings", "--audit-wpf-output", Path.Combine(auditsRoot, "settings-accessibility.md")], Path.Combine(auditsRoot, "settings-accessibility.md"));
        yield return Cmd("Audit WPF Editor", ["--audit-wpf-surface", "editor", "--audit-wpf-output", Path.Combine(auditsRoot, "editor-accessibility.md")], Path.Combine(auditsRoot, "editor-accessibility.md"));
        yield return Cmd("Audit WPF Recording", ["--audit-wpf-surface", "recording", "--audit-wpf-output", Path.Combine(auditsRoot, "recording-accessibility.md")], Path.Combine(auditsRoot, "recording-accessibility.md"));
        yield return Cmd("Audit WPF Frame Explorer", ["--audit-wpf-surface", "frame-explorer", "--audit-wpf-output", Path.Combine(auditsRoot, "frame-explorer-accessibility.md")], Path.Combine(auditsRoot, "frame-explorer-accessibility.md"));
        yield return Cmd("Audit WPF Proof Scene", ["--audit-wpf-surface", "proof-scene", "--audit-wpf-output", Path.Combine(auditsRoot, "proof-scene-accessibility.md")], Path.Combine(auditsRoot, "proof-scene-accessibility.md"));
        yield return Cmd("Audit Settings Sharing", ["--audit-settings-section", "Sharing", "--audit-settings-output", Path.Combine(auditsRoot, "settings-sharing-accessibility.md")], Path.Combine(auditsRoot, "settings-sharing-accessibility.md"));
        yield return Cmd("Audit Settings Recording", ["--audit-settings-section", "Recording", "--audit-settings-output", Path.Combine(auditsRoot, "settings-recording-accessibility.md")], Path.Combine(auditsRoot, "settings-recording-accessibility.md"));
        yield return Cmd("Audit Settings Automation", ["--audit-settings-section", "Automation", "--audit-settings-output", Path.Combine(auditsRoot, "settings-automation-accessibility.md")], Path.Combine(auditsRoot, "settings-automation-accessibility.md"));
    }

    private static async Task<List<ManualValidationDesktopProofCommandResult>> ReadExistingCommandResultsAsync(
        string proofRoot,
        CancellationToken cancellationToken)
    {
        var resultsPath = Path.Combine(proofRoot, CommandResultsFileName);
        if (!File.Exists(resultsPath))
        {
            return new List<ManualValidationDesktopProofCommandResult>();
        }

        try
        {
            var text = await File.ReadAllTextAsync(resultsPath, cancellationToken);
            return JsonSerializer.Deserialize<List<ManualValidationDesktopProofCommandResult>>(text, JsonOptions) ?? new List<ManualValidationDesktopProofCommandResult>();
        }
        catch (JsonException)
        {
            return new List<ManualValidationDesktopProofCommandResult>();
        }
    }

    private static ManualValidationDesktopEnvironment BuildEnvironmentReport(
        string root,
        string proofRoot,
        ManualValidationDesktopProofRequest request)
    {
        var screens = Forms.Screen.AllScreens
            .Select(screen => new ManualValidationDesktopScreen
            {
                DeviceName = screen.DeviceName,
                Primary = screen.Primary,
                Bounds = $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}",
                WorkingArea = $"{screen.WorkingArea.X},{screen.WorkingArea.Y},{screen.WorkingArea.Width},{screen.WorkingArea.Height}"
            })
            .ToList();

        return new ManualValidationDesktopEnvironment
        {
            GeneratedAt = DateTimeOffset.Now,
            RootPath = root,
            ProofDirectory = proofRoot,
            WindowsVersion = Environment.OSVersion.VersionString,
            HighContrast = System.Windows.SystemParameters.HighContrast,
            MousePresent = Forms.SystemInformation.MousePresent,
            KeyboardDelay = Forms.SystemInformation.KeyboardDelay,
            KeyboardSpeed = Forms.SystemInformation.KeyboardSpeed,
            TextScaleFactor = ReadRegistryValue(@"Control Panel\Accessibility", "TextScaleFactor"),
            LogPixels = ReadRegistryValue(@"Control Panel\Desktop", "LogPixels"),
            AppPath = ResolveAppPath(request.AppPath),
            Screens = screens
        };
    }

    private static string BuildEnvironmentMarkdown(ManualValidationDesktopEnvironment environment)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Desktop Accessibility Proof Environment");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {environment.GeneratedAt:O}");
        builder.AppendLine();
        builder.AppendLine($"- Windows version: {environment.WindowsVersion}");
        builder.AppendLine($"- High contrast currently enabled: {Bool(environment.HighContrast)}");
        builder.AppendLine($"- Text scale registry value: {environment.TextScaleFactor ?? "not-detected"}");
        builder.AppendLine($"- LogPixels registry value: {environment.LogPixels ?? "not-detected"}");
        builder.AppendLine($"- Mouse present: {Bool(environment.MousePresent)}");
        builder.AppendLine($"- Keyboard delay/speed: {environment.KeyboardDelay.ToString(CultureInfo.InvariantCulture)} / {environment.KeyboardSpeed.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Screen count: {environment.Screens.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var screen in environment.Screens)
        {
            builder.AppendLine($"  - {screen.DeviceName}: primary={Bool(screen.Primary)}, bounds={screen.Bounds}, workArea={screen.WorkingArea}");
        }

        builder.AppendLine();
        builder.AppendLine("Evidence limit: this file records the current machine state only. It does not prove Windows text-scaling behavior at 125%, 150%, or 200%, high-contrast theme usability, clean-machine behavior, or human keyboard/screen-reader operation.");
        return builder.ToString();
    }

    private static string BuildLaneMarkdown(
        string root,
        string proofRoot,
        DesktopProofLanePlan plan,
        ManualValidationLaneStatus status,
        IReadOnlyList<ManualValidationDesktopProofCommandResult> commands,
        IReadOnlyList<ManualValidationDesktopProofCommandResult> failedCommands,
        ManualValidationDesktopEnvironment environment)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {plan.Title}");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime):yyyy-MM-dd}");
        builder.AppendLine($"Generated at: {DateTimeOffset.Now:O}");
        builder.AppendLine();
        builder.AppendLine("Purpose: Attach local command evidence for this desktop proof lane while keeping human-only validation boundaries explicit.");
        builder.AppendLine();
        builder.AppendLine("## Requirement");
        builder.AppendLine();
        builder.AppendLine("- Classification: Required");
        builder.AppendLine("- Rationale: Required local desktop proof lane from the manual-validation plan.");
        builder.AppendLine("- Generated command evidence can support this lane, but it does not replace the human observation named by the lane.");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.NotRun)}] Pending");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Passed)}] Passed");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Failed)}] Failed");
        builder.AppendLine($"- [{Check(status is ManualValidationLaneStatus.Blocked)}] Blocked");
        builder.AppendLine();
        builder.AppendLine("## Safety And Redaction");
        builder.AppendLine();
        builder.AppendLine("- [x] Safe demo content only; screenshots rendered by this helper are app-owned or synthetic proof surfaces.");
        builder.AppendLine("- [x] No private desktop screenshots, account pages, OAuth screens, customer content, or live provider payloads were captured by this helper.");
        builder.AppendLine("- [x] This lane is described as observed command/static evidence only, not accessibility compliance certification.");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- Windows version: {environment.WindowsVersion}");
        builder.AppendLine($"- High contrast currently enabled: {Bool(environment.HighContrast)}");
        builder.AppendLine($"- Text scale registry value: {environment.TextScaleFactor ?? "not-detected"}");
        builder.AppendLine($"- Display count: {environment.Screens.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Proof folder: `{Path.GetRelativePath(root, proofRoot).Replace('\\', '/')}/`");
        builder.AppendLine();
        builder.AppendLine("## Checklist");
        builder.AppendLine();
        foreach (var note in plan.ChecklistNotes)
        {
            builder.AppendLine($"- [x] {note}");
        }

        builder.AppendLine("- [ ] Human/operator observation required before changing this lane to Passed.");
        builder.AppendLine();
        builder.AppendLine("## Evidence");
        builder.AppendLine();
        foreach (var evidence in plan.Evidence)
        {
            var path = Path.Combine(root, evidence.Replace('/', Path.DirectorySeparatorChar));
            var exists = File.Exists(path) || Directory.Exists(path) || evidence.Contains("*", StringComparison.Ordinal);
            builder.AppendLine($"- [{Check(exists)}] `{evidence}`");
        }

        builder.AppendLine($"- [{Check(File.Exists(Path.Combine(proofRoot, CommandResultsFileName)))}] `desktop-proof/{CommandResultsFileName}`");
        builder.AppendLine($"- [{Check(File.Exists(Path.Combine(proofRoot, EnvironmentReportFileName)))}] `desktop-proof/{EnvironmentReportFileName}`");
        builder.AppendLine();
        builder.AppendLine("## Command Results");
        builder.AppendLine();
        if (commands.Count == 0)
        {
            builder.AppendLine("- No desktop-proof commands were recorded; rerun with `manual-validation desktop-proof --run-commands`.");
        }
        else
        {
            foreach (var command in commands)
            {
                builder.AppendLine($"- {command.Name}: exit `{command.ExitCode}`; output `{Path.GetRelativePath(root, command.OutputPath).Replace('\\', '/')}`; log `{Path.GetRelativePath(root, command.LogPath).Replace('\\', '/')}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        if (status is ManualValidationLaneStatus.Failed)
        {
            builder.AppendLine("- Observed behavior: One or more desktop-proof commands failed; review the failed command logs before continuing manual validation.");
            builder.AppendLine("- Defects or follow-ups: fix or rerun failed command(s): " + string.Join(", ", failedCommands.Select(command => command.Name)) + ".");
        }
        else
        {
            builder.AppendLine("- Observed behavior: Desktop proof helper collected local command/static evidence for this lane.");
            builder.AppendLine("- Defects or follow-ups: " + plan.BlockedReason);
        }

        builder.AppendLine("- Public claim boundary: Do not claim this lane passed until the named human/manual proof has been performed with safe content and the result is changed from Blocked to Passed by an operator.");
        return builder.ToString();
    }

    private static ManualValidationDesktopProofSummary BuildSummary(
        string root,
        string proofRoot,
        ManualValidationLaneStatus status,
        IReadOnlyList<ManualValidationDesktopProofCommandResult> commands,
        IReadOnlyList<ManualValidationDesktopProofCommandResult> failedCommands,
        ManualValidationDesktopEnvironment environment,
        IReadOnlyList<string> evidenceFiles)
    {
        var expectedEvidence = LanePlans.Values
            .SelectMany(plan => plan.Evidence)
            .Concat([
                $"desktop-proof/{CommandResultsFileName}",
                $"desktop-proof/{EnvironmentReportFileName}",
                $"desktop-proof/{EnvironmentJsonFileName}"
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingEvidence = expectedEvidence
            .Where(path => !EvidenceExists(root, path))
            .ToList();

        return new ManualValidationDesktopProofSummary
        {
            GeneratedAt = DateTimeOffset.Now,
            Status = status,
            ProofDirectory = Path.GetRelativePath(root, proofRoot).Replace('\\', '/') + "/",
            CommandCount = commands.Count,
            FailedCommandCount = failedCommands.Count,
            FailedCommands = failedCommands.Select(command => command.Name).ToList(),
            EvidenceFileCount = evidenceFiles.Count,
            ExpectedEvidence = expectedEvidence,
            MissingEvidence = missingEvidence,
            CommandBackedEvidencePresent = commands.Count > 0 && failedCommands.Count == 0,
            HumanObservationRequired = true,
            BlocksLocalV1Handoff = true,
            RemainingHumanLanes = LanePlans.Values
                .Select(plan => plan.Title)
                .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WindowsVersion = environment.WindowsVersion,
            HighContrast = environment.HighContrast,
            TextScaleFactor = environment.TextScaleFactor ?? "not-detected",
            DisplayCount = environment.Screens.Count,
            ClaimBoundary = "This packet is command-backed preparation evidence only. It does not prove keyboard traversal, Narrator/NVDA output, Windows text scaling, high-contrast usability, live region-drag behavior, or clean-machine launch/install behavior.",
            NextOperatorSteps =
            [
                "Open the safe proof scene or rendered app-owned surfaces.",
                "Perform keyboard traversal and Narrator/NVDA observation with safe content.",
                "Repeat visual checks under required Windows text-scaling and high-contrast states.",
                "Perform live region-drag and clean-machine GUI checks before changing required lanes to Passed."
            ]
        };
    }

    private static string BuildSummaryMarkdown(ManualValidationDesktopProofSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Desktop Proof Packet Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {summary.GeneratedAt:O}");
        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine($"- Status written to required desktop lanes: {summary.Status}");
        builder.AppendLine($"- Command-backed evidence present: {Bool(summary.CommandBackedEvidencePresent)}");
        builder.AppendLine($"- Commands recorded: {summary.CommandCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Failed commands: {summary.FailedCommandCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Evidence files currently in packet: {summary.EvidenceFileCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Human/operator observation required: {Bool(summary.HumanObservationRequired)}");
        builder.AppendLine($"- Blocks local V1 handoff until operator lanes pass: {Bool(summary.BlocksLocalV1Handoff)}");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- Windows version: {summary.WindowsVersion}");
        builder.AppendLine($"- High contrast currently enabled: {Bool(summary.HighContrast)}");
        builder.AppendLine($"- Text scale registry value: {summary.TextScaleFactor}");
        builder.AppendLine($"- Display count: {summary.DisplayCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine("## Remaining Human Lanes");
        builder.AppendLine();
        foreach (var lane in summary.RemainingHumanLanes)
        {
            builder.AppendLine($"- {lane}");
        }

        builder.AppendLine();
        builder.AppendLine("## Missing Evidence");
        builder.AppendLine();
        if (summary.MissingEvidence.Count == 0)
        {
            builder.AppendLine("- None from the expected command/static evidence set.");
        }
        else
        {
            foreach (var evidence in summary.MissingEvidence)
            {
                builder.AppendLine($"- `{evidence}`");
            }
        }

        if (summary.FailedCommands.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failed Commands");
            builder.AppendLine();
            foreach (var command in summary.FailedCommands)
            {
                builder.AppendLine($"- {command}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Next Operator Steps");
        builder.AppendLine();
        foreach (var step in summary.NextOperatorSteps)
        {
            builder.AppendLine($"- {step}");
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Boundary");
        builder.AppendLine();
        builder.AppendLine(summary.ClaimBoundary);
        builder.AppendLine();
        builder.AppendLine("Do not mark the required desktop lanes Passed from this summary alone.");
        return builder.ToString();
    }

    private static bool EvidenceExists(string root, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(root, normalized);
        if (!relativePath.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        var directory = Path.GetDirectoryName(path);
        var pattern = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var exists = Directory.EnumerateFileSystemEntries(directory, pattern).Any();
        if (exists || !pattern.StartsWith("Receipts-", StringComparison.OrdinalIgnoreCase))
        {
            return exists;
        }

        var legacyPattern = "GoatShot-" + pattern["Receipts-".Length..];
        return Directory.EnumerateFileSystemEntries(directory, legacyPattern).Any();
    }

    private static List<string> BuildEvidenceList(string root, string proofRoot)
    {
        if (!Directory.Exists(proofRoot))
        {
            return new List<string>();
        }

        return Directory.EnumerateFiles(proofRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveAppPath(string? requestedAppPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedAppPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedAppPath));
        }

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            var packagedApp = Path.Combine(processDirectory, BrandIdentity.DesktopExecutableName);
            if (File.Exists(packagedApp))
            {
                return packagedApp;
            }

            var legacyPackagedApp = Path.Combine(processDirectory, $"{BrandIdentity.LegacyProductName}.exe");
            if (File.Exists(legacyPackagedApp))
            {
                return legacyPackagedApp;
            }
        }

        var buildDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "GoatShot.App",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0");
        var currentPath = Path.Combine(buildDirectory, BrandIdentity.DesktopExecutableName);
        var legacyPath = Path.Combine(buildDirectory, $"{BrandIdentity.LegacyProductName}.exe");
        return File.Exists(currentPath) || !File.Exists(legacyPath) ? currentPath : legacyPath;
    }

    private static string? ReadRegistryValue(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatCommandOutput(ManualValidationDesktopProofCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {result.FileName} {string.Join(' ', result.Arguments)}");
        builder.AppendLine($"WorkingDirectory: {result.WorkingDirectory}");
        builder.AppendLine($"OutputPath: {result.OutputPath}");
        builder.AppendLine($"ExitCode: {result.ExitCode}");
        builder.AppendLine($"TimedOut: {result.TimedOut}");
        builder.AppendLine();
        builder.AppendLine("STDOUT:");
        builder.AppendLine(result.StandardOutput ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine("STDERR:");
        builder.AppendLine(result.StandardError ?? string.Empty);
        return builder.ToString();
    }

    private static void CreateSyntheticEditorImage(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new DrawingBitmap(960, 540);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.FromArgb(18, 29, 38));
        using var titleFont = new DrawingFont("Segoe UI", 28, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);
        using var bodyFont = new DrawingFont("Segoe UI", 16, DrawingFontStyle.Regular, DrawingGraphicsUnit.Pixel);
        using var accentBrush = new DrawingSolidBrush(DrawingColor.FromArgb(37, 217, 189));
        using var mutedBrush = new DrawingSolidBrush(DrawingColor.FromArgb(180, 196, 208));
        using var panelBrush = new DrawingSolidBrush(DrawingColor.FromArgb(31, 45, 58));
        using var linePen = new DrawingPen(DrawingColor.FromArgb(73, 103, 124), 2);
        graphics.DrawString("Receipts desktop proof", titleFont, DrawingBrushes.White, 54, 48);
        graphics.DrawString("Synthetic source image for editor accessibility screenshots.", bodyFont, mutedBrush, 56, 92);
        graphics.FillRectangle(panelBrush, 56, 148, 390, 285);
        graphics.FillRectangle(panelBrush, 486, 148, 414, 138);
        graphics.FillRectangle(accentBrush, 84, 188, 132, 12);
        for (var index = 0; index < 6; index++)
        {
            graphics.DrawLine(linePen, 84, 236 + index * 30, 404, 236 + index * 30);
            graphics.DrawLine(linePen, 518, 186 + index * 15, 858, 186 + index * 15);
        }

        bitmap.Save(path, DrawingImageFormat.Png);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = name.Select(ch => invalid.Contains(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars).Replace(' ', '-');
    }

    private static string Check(bool value) => value ? "x" : " ";
    private static string Bool(bool value) => value ? "yes" : "no";

    private sealed record DesktopProofLanePlan(
        string FileName,
        string Title,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> ChecklistNotes,
        string BlockedReason);
}

public sealed class ProcessManualValidationDesktopProofCommandRunner : IManualValidationDesktopProofCommandRunner
{
    public async Task<ManualValidationDesktopProofCommandResult> RunAsync(
        ManualValidationDesktopProofCommandSpec command,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return ManualValidationDesktopProofCommandResult.FromSpec(command, -1, string.Empty, "Process did not start.", timedOut: false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var timeout = TimeSpan.FromSeconds(command.TimeoutSeconds <= 0 ? 120 : command.TimeoutSeconds);
        var exited = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between timeout check and kill.
            }

            return ManualValidationDesktopProofCommandResult.FromSpec(command, -1, await SafeReadAsync(stdoutTask), await SafeReadAsync(stderrTask), timedOut: true);
        }

        return ManualValidationDesktopProofCommandResult.FromSpec(
            command,
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut: false);
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public interface IManualValidationDesktopProofCommandRunner
{
    Task<ManualValidationDesktopProofCommandResult> RunAsync(
        ManualValidationDesktopProofCommandSpec command,
        CancellationToken cancellationToken = default);
}

public sealed record ManualValidationDesktopProofRequest
{
    public string RootPath { get; init; } = string.Empty;
    public bool RunCommands { get; init; }
    public string? RepoRoot { get; init; }
    public string? AppPath { get; init; }
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record ManualValidationDesktopProofCommandSpec(
    string Name,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string OutputPath,
    string LogPath,
    int TimeoutSeconds);

public sealed record ManualValidationDesktopProofCommandResult
{
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public static ManualValidationDesktopProofCommandResult FromSpec(
        ManualValidationDesktopProofCommandSpec spec,
        int exitCode,
        string standardOutput,
        string standardError,
        bool timedOut) =>
        new()
        {
            Name = spec.Name,
            FileName = spec.FileName,
            Arguments = spec.Arguments.ToArray(),
            WorkingDirectory = spec.WorkingDirectory,
            OutputPath = spec.OutputPath,
            LogPath = spec.LogPath,
            ExitCode = exitCode,
            TimedOut = timedOut,
            StandardOutput = SensitiveTextDetector.Redact(standardOutput),
            StandardError = SensitiveTextDetector.Redact(standardError)
        };
}

public sealed class ManualValidationDesktopEnvironment
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string ProofDirectory { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public bool HighContrast { get; set; }
    public bool MousePresent { get; set; }
    public int KeyboardDelay { get; set; }
    public int KeyboardSpeed { get; set; }
    public string? TextScaleFactor { get; set; }
    public string? LogPixels { get; set; }
    public string AppPath { get; set; } = string.Empty;
    public List<ManualValidationDesktopScreen> Screens { get; set; } = new();
}

public sealed class ManualValidationDesktopScreen
{
    public string DeviceName { get; set; } = string.Empty;
    public bool Primary { get; set; }
    public string Bounds { get; set; } = string.Empty;
    public string WorkingArea { get; set; } = string.Empty;
}

public sealed class ManualValidationDesktopProofResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string ProofDirectory { get; set; } = string.Empty;
    public string CommandResultsPath { get; set; } = string.Empty;
    public string EnvironmentReportPath { get; set; } = string.Empty;
    public string EnvironmentJsonPath { get; set; } = string.Empty;
    public string SummaryMarkdownPath { get; set; } = string.Empty;
    public string SummaryJsonPath { get; set; } = string.Empty;
    public ManualValidationDesktopProofSummary Summary { get; set; } = new();
    public ManualValidationLaneStatus Status { get; set; }
    public List<ManualValidationDesktopProofCommandResult> Commands { get; set; } = new();
    public List<string> FailedCommands { get; set; } = new();
    public List<string> UpdatedLaneFiles { get; set; } = new();
    public List<string> EvidenceFiles { get; set; } = new();

    public static ManualValidationDesktopProofResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Status = ManualValidationLaneStatus.Failed
        };
}

public sealed class ManualValidationDesktopProofSummary
{
    public DateTimeOffset GeneratedAt { get; set; }
    public ManualValidationLaneStatus Status { get; set; }
    public string ProofDirectory { get; set; } = string.Empty;
    public int CommandCount { get; set; }
    public int FailedCommandCount { get; set; }
    public List<string> FailedCommands { get; set; } = new();
    public int EvidenceFileCount { get; set; }
    public List<string> ExpectedEvidence { get; set; } = new();
    public List<string> MissingEvidence { get; set; } = new();
    public bool CommandBackedEvidencePresent { get; set; }
    public bool HumanObservationRequired { get; set; }
    public bool BlocksLocalV1Handoff { get; set; }
    public List<string> RemainingHumanLanes { get; set; } = new();
    public string WindowsVersion { get; set; } = string.Empty;
    public bool HighContrast { get; set; }
    public string TextScaleFactor { get; set; } = string.Empty;
    public int DisplayCount { get; set; }
    public string ClaimBoundary { get; set; } = string.Empty;
    public List<string> NextOperatorSteps { get; set; } = new();
}

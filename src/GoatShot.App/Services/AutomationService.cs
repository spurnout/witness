using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AutomationService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".avi",
        ".mkv",
        ".webm"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly WorkspaceStore _workspaceStore;
    private readonly ShareService _shareService;
    private readonly MetadataService _metadataService;
    private readonly OcrService? _ocrService;
    private readonly VisualRedactionService? _visualRedactionService;
    private readonly BugReportService? _bugReportService;
    private readonly WorkflowRunLogService? _workflowRunLogs;

    public AutomationService(
        AppSettings settings,
        AppPaths paths,
        WorkspaceStore workspaceStore,
        ShareService shareService,
        MetadataService metadataService,
        OcrService? ocrService = null,
        VisualRedactionService? visualRedactionService = null,
        BugReportService? bugReportService = null,
        WorkflowRunLogService? workflowRunLogs = null)
    {
        _settings = settings;
        _paths = paths;
        _workspaceStore = workspaceStore;
        _shareService = shareService;
        _metadataService = metadataService;
        _ocrService = ocrService;
        _visualRedactionService = visualRedactionService;
        _bugReportService = bugReportService;
        _workflowRunLogs = workflowRunLogs;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<CaptureItem>? CaptureImported;

    public async Task ProcessCaptureCreatedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.CaptureCreated, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessCaptureEditedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.CaptureEdited, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessRecordingCompletedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.RecordingCompleted, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessOcrCompletedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.OcrCompleted, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessUploadCompletedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.UploadCompleted, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessAiActionCompletedAsync(CaptureItem item, CancellationToken cancellationToken = default)
    {
        await RunRulesAsync(AutomationTrigger.AiActionCompleted, item, dryRun: false, cancellationToken);
    }

    public async Task ProcessWatchedFileAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var isVirtualPrinterImport = VirtualPrinterImportService.IsInsideDropFolder(_settings, _paths, path);
        if ((!_settings.EnableWatchFolders || !_settings.WatchFolderAutoImport) &&
            !isVirtualPrinterImport)
        {
            return;
        }

        if (!IsSupportedImport(path))
        {
            return;
        }

        if (!await WaitForStableFileAsync(path, cancellationToken))
        {
            PublishStatus($"Watch folder skipped unstable file: {path}");
            return;
        }

        var item = await _workspaceStore.ImportFileCopyAsync(
            path,
            isVirtualPrinterImport
                ? "Auto-imported from the configured virtual-printer/file-drop folder."
                : "Auto-imported from a configured watch folder.",
            isVirtualPrinterImport ? VirtualPrinterImportService.GetCaptureKind(path) : null,
            isVirtualPrinterImport
                ? new CaptureSource
                {
                    ProcessName = "virtual-printer-file-drop",
                    WindowTitle = Path.GetFileNameWithoutExtension(path),
                    MonitorName = "Print/file-drop import"
                }
                : null);

        CaptureImported?.Invoke(this, item);
        PublishStatus($"Watch folder imported: {item.FileName}");

        if (_settings.WatchFolderCopyImageAfterImport && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            await _shareService.ShareAsync(item, ShareDestination.ClipboardImage, cancellationToken);
        }

        if (_settings.WatchFolderStripMetadataAfterImport && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            var stripped = await _metadataService.StripImageMetadataAsync(item, _workspaceStore);
            CaptureImported?.Invoke(this, stripped);
            PublishStatus($"Watch folder created metadata-stripped copy: {stripped.FileName}");
        }

        if (_settings.WatchFolderShareAfterImport)
        {
            var result = await _shareService.ShareAsync(item, _shareService.ResolveDefaultDestination(), cancellationToken);
            PublishStatus(result.Message);
        }

        await RunRulesAsync(AutomationTrigger.FileAddedToWatchFolder, item, dryRun: false, cancellationToken);
    }

    public IReadOnlyList<AutomationRuleEvaluation> EvaluateRules(AutomationTrigger trigger, CaptureItem item)
    {
        return _settings.AutomationRules
            .Select(rule => EvaluateRule(rule, trigger, item))
            .ToList();
    }

    public async Task<AutomationRunResult> RunRulesAsync(
        AutomationTrigger trigger,
        CaptureItem item,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var result = new AutomationRunResult
        {
            Trigger = trigger,
            DryRun = dryRun
        };

        // Evaluate each rule immediately before executing it, not as an up-front
        // snapshot: earlier rules in the batch can mutate the item (e.g. RunOcr
        // populating OcrText), and later OCR/sensitive-data conditions must observe
        // that fresh state or they silently never match.
        var evaluations = new List<AutomationRuleEvaluation>();
        foreach (var rule in _settings.AutomationRules.ToList())
        {
            var evaluation = EvaluateRule(rule, trigger, item);
            evaluations.Add(evaluation);
            if (!evaluation.Matches)
            {
                continue;
            }

            var ruleResult = new AutomationRuleRunResult
            {
                Evaluation = evaluation
            };

            result.Rules.Add(ruleResult);

            if (dryRun)
            {
                ruleResult.Actions.AddRange(evaluation.Actions.Select(action => new AutomationActionResult
                {
                    Action = action,
                    Executed = false,
                    Succeeded = true,
                    Message = $"Dry run would execute {action}."
                }));
                continue;
            }

            PublishStatus($"Automation rule running: {evaluation.RuleName}");
            foreach (var action in evaluation.Actions)
            {
                var actionResult = await ExecuteActionAsync(action, item, rule, cancellationToken);
                ruleResult.Actions.Add(actionResult);
            }
        }

        result.TotalRules = evaluations.Count;
        result.MatchingRules = evaluations.Count(evaluation => evaluation.Matches);
        result.Evaluations = evaluations;

        if (_workflowRunLogs is not null)
        {
            try
            {
                await _workflowRunLogs.WriteAsync(result, item, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.LogMessage))
                {
                    PublishStatus(result.LogMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.LogMessage = $"Workflow run completed, but log writing failed: {ex.Message}";
                PublishStatus(result.LogMessage);
            }
        }

        return result;
    }

    public static AutomationRuleEvaluation EvaluateRule(AutomationRule rule, AutomationTrigger requestedTrigger, CaptureItem item)
    {
        var evaluation = new AutomationRuleEvaluation
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            RuleTrigger = rule.Trigger,
            RequestedTrigger = requestedTrigger,
            IsEnabled = rule.IsEnabled,
            TriggerMatches = rule.Trigger == requestedTrigger,
            Actions = rule.Actions.ToList()
        };

        if (!rule.IsEnabled)
        {
            evaluation.Reasons.Add("Rule is disabled.");
            return evaluation;
        }

        if (rule.Trigger != requestedTrigger)
        {
            evaluation.Reasons.Add($"Rule trigger is {rule.Trigger}, not {requestedTrigger}.");
            return evaluation;
        }

        AddContainsCheck(evaluation, "source app", item.SourceApp, rule.SourceAppContains);
        AddContainsCheck(evaluation, "window title", item.SourceWindowTitle ?? item.Notes, rule.WindowTitleContains);
        AddContainsCheck(evaluation, "monitor", item.SourceMonitorName, rule.MonitorContains);
        AddContainsCheck(evaluation, "OCR text", item.OcrText, rule.OcrContains);

        if (!string.IsNullOrWhiteSpace(rule.HotkeyProfile))
        {
            var actualProfile = item.HotkeyProfile;
            if (string.IsNullOrWhiteSpace(actualProfile))
            {
                evaluation.Reasons.Add($"Hotkey profile is not set, expected {rule.HotkeyProfile}.");
            }
            else if (!actualProfile.Equals(rule.HotkeyProfile, StringComparison.OrdinalIgnoreCase))
            {
                evaluation.Reasons.Add($"Hotkey profile is {actualProfile}, expected {rule.HotkeyProfile}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.FileExtension))
        {
            var actual = Path.GetExtension(item.FilePath);
            var expected = NormalizeExtension(rule.FileExtension);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                evaluation.Reasons.Add($"File extension is '{actual}', expected '{expected}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.CaptureKind) &&
            !item.Kind.ToString().Equals(rule.CaptureKind, StringComparison.OrdinalIgnoreCase))
        {
            evaluation.Reasons.Add($"Capture kind is {item.Kind}, expected {rule.CaptureKind}.");
        }

        if (rule.RequiresSensitiveData is { } requiresSensitiveData)
        {
            var hasSensitiveData = SensitiveTextDetector.Find(item.OcrText).Count > 0;
            if (requiresSensitiveData && !hasSensitiveData)
            {
                evaluation.Reasons.Add("Sensitive-data condition requires a sensitive OCR/text finding, but none was found.");
            }
            else if (!requiresSensitiveData && hasSensitiveData)
            {
                evaluation.Reasons.Add("Sensitive-data condition excludes captures with sensitive OCR/text findings.");
            }
        }

        if (rule.MinFileSizeBytes is { } minBytes && item.Bytes < minBytes)
        {
            evaluation.Reasons.Add($"File size is {item.Bytes} byte(s), below minimum {minBytes}.");
        }

        if (rule.MaxFileSizeBytes is { } maxBytes && item.Bytes > maxBytes)
        {
            evaluation.Reasons.Add($"File size is {item.Bytes} byte(s), above maximum {maxBytes}.");
        }

        if (rule.Actions.Count == 0)
        {
            evaluation.Reasons.Add("Rule has no actions.");
        }

        evaluation.Matches = evaluation.Reasons.Count == 0;
        if (evaluation.Matches)
        {
            evaluation.Reasons.Add("Matched.");
        }

        return evaluation;
    }

    private async Task<AutomationActionResult> ExecuteActionAsync(
        AutomationActionKind action,
        CaptureItem item,
        AutomationRule? rule,
        CancellationToken cancellationToken)
    {
        try
        {
            return action switch
            {
                AutomationActionKind.OpenEditor => StatusOnly(action, "Open editor is a desktop UI action; automation recorded the request for the app shell."),
                AutomationActionKind.CopyImageToClipboard => await ShareActionAsync(action, item, ShareDestination.ClipboardImage, cancellationToken),
                AutomationActionKind.CopyFileToClipboard => await ShareActionAsync(action, item, ShareDestination.ClipboardFile, cancellationToken),
                AutomationActionKind.CopyPathToClipboard => await ShareActionAsync(action, item, ShareDestination.ClipboardPath, cancellationToken),
                AutomationActionKind.SaveToFolder => await ShareActionAsync(action, item, ShareDestination.LocalFolder, cancellationToken),
                AutomationActionKind.RunOcr => await RunOcrAsync(action, item, cancellationToken),
                AutomationActionKind.RedactDetectedSensitiveData => await RedactSensitiveDataAsync(action, item, cancellationToken),
                AutomationActionKind.ApplyImageEffect => await ApplyImageEffectAsync(action, item, rule, cancellationToken),
                AutomationActionKind.StripMetadataCopy => await StripMetadataAsync(action, item),
                AutomationActionKind.ShareDefaultDestination => await ShareActionAsync(action, item, _shareService.ResolveDefaultDestination(), cancellationToken),
                AutomationActionKind.RunCustomScript => await ShareActionAsync(action, item, ShareDestination.CustomScript, cancellationToken),
                AutomationActionKind.CallCustomWebhook => await ShareActionAsync(action, item, ShareDestination.CustomWebhook, cancellationToken),
                AutomationActionKind.GenerateDocument => await GenerateDocumentAsync(action, item, cancellationToken),
                AutomationActionKind.DeleteLocalFile => StatusOnly(action, "Delete local file is intentionally not executed by unattended automation."),
                AutomationActionKind.ShowNotification => StatusOnly(action, $"Automation notification: {item.FileName} matched a workflow rule."),
                _ => StatusOnly(action, $"Unsupported automation action: {action}", succeeded: false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Automation action '{action}' failed: {ex.Message}";
            PublishStatus(message);
            return new AutomationActionResult
            {
                Action = action,
                Executed = true,
                Succeeded = false,
                Message = message
            };
        }
    }

    private async Task<AutomationActionResult> ShareActionAsync(
        AutomationActionKind action,
        CaptureItem item,
        ShareDestination destination,
        CancellationToken cancellationToken)
    {
        var result = await _shareService.ShareAsync(item, destination, cancellationToken);
        PublishStatus(result.Message);
        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = result.Succeeded,
            Message = result.Message,
            OutputPath = result.Url
        };
    }

    private async Task<AutomationActionResult> StripMetadataAsync(AutomationActionKind action, CaptureItem item)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(item.FilePath)))
        {
            return StatusOnly(action, "Metadata stripping currently supports image captures only.", succeeded: false);
        }

        var stripped = await _metadataService.StripImageMetadataAsync(item, _workspaceStore);
        CaptureImported?.Invoke(this, stripped);
        return StatusOnly(action, $"Automation created metadata-stripped copy: {stripped.FileName}", outputPath: stripped.FilePath);
    }

    private async Task<AutomationActionResult> ApplyImageEffectAsync(
        AutomationActionKind action,
        CaptureItem item,
        AutomationRule? rule,
        CancellationToken cancellationToken)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(item.FilePath)))
        {
            return StatusOnly(action, "Image-effect automation currently supports image captures only.", succeeded: false);
        }

        if (rule?.ImageEffectMode is not { } mode)
        {
            return StatusOnly(action, "Image-effect automation needs ImageEffectMode set to Solid, Blur, or Pixelate.", succeeded: false);
        }

        using var source = new Bitmap(item.FilePath);
        if (!TryResolveImageEffectRegion(rule.ImageEffectRegion, source.Width, source.Height, out var region, out var reason))
        {
            return StatusOnly(action, reason, succeeded: false);
        }

        using var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        ImageRegionEffects.Apply(output, region, mode);
        var outputPath = MakeUniquePath(Path.Combine(
            Path.GetDirectoryName(item.FilePath) ?? Environment.CurrentDirectory,
            $"{Path.GetFileNameWithoutExtension(item.FilePath)}-effect-{mode.ToString().ToLowerInvariant()}-{DateTimeOffset.Now:HHmmss}.png"));
        output.Save(outputPath, ImageFormat.Png);

        cancellationToken.ThrowIfCancellationRequested();
        var saved = await _workspaceStore.AddImageFileAsync(
            outputPath,
            item.Kind,
            $"Automation image effect: {mode}; region {FormatImageEffectRegion(region, source.Width, source.Height)}.");
        saved.SourceApp = item.SourceApp;
        saved.SourceWindowTitle = item.SourceWindowTitle;
        saved.SourceMonitorName = item.SourceMonitorName;
        saved.HotkeyProfile = item.HotkeyProfile;
        await _workspaceStore.UpdateItemAsync(saved);
        CaptureImported?.Invoke(this, saved);

        var message = $"Automation applied {mode} image effect: {saved.FileName}";
        PublishStatus(message);
        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = true,
            Message = message,
            OutputPath = outputPath
        };
    }

    private async Task<AutomationActionResult> RunOcrAsync(
        AutomationActionKind action,
        CaptureItem item,
        CancellationToken cancellationToken)
    {
        if (_ocrService is null)
        {
            return StatusOnly(action, "OCR service is not available to automation.", succeeded: false);
        }

        if (!ImageExtensions.Contains(Path.GetExtension(item.FilePath)))
        {
            return StatusOnly(action, "OCR automation currently supports image captures only.", succeeded: false);
        }

        var result = await _ocrService.RecognizeFileAsync(item.FilePath, cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return StatusOnly(action, result.Message, succeeded: false);
        }

        item.OcrText = result.Text;
        item.OcrLanguageTag = result.LanguageTag;
        item.OcrRecognizedAt = DateTimeOffset.Now;
        item.OcrWords = result.Words.ToList();
        var scan = SensitiveTextDetector.Scan(result.Text);
        item.Notes = MergeNote(item.Notes, $"Sensitive scan: {scan.Summary}");
        await _workspaceStore.UpdateItemAsync(item);
        PublishStatus(result.Message);

        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = true,
            Message = result.Message
        };
    }

    private async Task<AutomationActionResult> RedactSensitiveDataAsync(
        AutomationActionKind action,
        CaptureItem item,
        CancellationToken cancellationToken)
    {
        if (_visualRedactionService is null)
        {
            return StatusOnly(action, "Visual redaction service is not available to automation.", succeeded: false);
        }

        if (string.IsNullOrWhiteSpace(item.OcrText) || item.OcrWords.Count == 0)
        {
            return StatusOnly(action, "Visual redaction needs OCR text and word bounds; run OCR before this action.", succeeded: false);
        }

        var result = await _visualRedactionService.RedactSensitiveOcrAsync(
            item,
            mode: VisualRedactionMode.Solid,
            addToWorkspace: true,
            cancellationToken: cancellationToken);
        if (result.Item is not null)
        {
            CaptureImported?.Invoke(this, result.Item);
        }

        PublishStatus(result.Message);
        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = result.Succeeded,
            Message = result.Message,
            OutputPath = result.OutputPath
        };
    }

    private async Task<AutomationActionResult> GenerateDocumentAsync(
        AutomationActionKind action,
        CaptureItem item,
        CancellationToken cancellationToken)
    {
        if (_bugReportService is null)
        {
            return StatusOnly(action, "Document export service is not available to automation.", succeeded: false);
        }

        var result = await _bugReportService.ExportAsync(item, format: "markdown", cancellationToken: cancellationToken);
        PublishStatus(result.Message);
        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = result.Succeeded,
            Message = result.Message,
            OutputPath = result.Path
        };
    }

    private AutomationActionResult StatusOnly(
        AutomationActionKind action,
        string message,
        bool succeeded = true,
        string? outputPath = null)
    {
        PublishStatus(message);
        return new AutomationActionResult
        {
            Action = action,
            Executed = true,
            Succeeded = succeeded,
            Message = message,
            OutputPath = outputPath
        };
    }

    private static void AddContainsCheck(
        AutomationRuleEvaluation evaluation,
        string label,
        string? actual,
        string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(actual) ||
            !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            evaluation.Reasons.Add($"{label} does not contain '{expected}'.");
        }
    }

    private static string MergeNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return note;
        }

        if (existing.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return existing.TrimEnd() + Environment.NewLine + note;
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
    }

    private static bool TryResolveImageEffectRegion(
        string? value,
        int imageWidth,
        int imageHeight,
        out RectangleF region,
        out string reason)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            region = RectangleF.Empty;
            reason = "Image-effect automation needs positive image dimensions.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("full", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            region = new RectangleF(0, 0, imageWidth, imageHeight);
            reason = string.Empty;
            return true;
        }

        var parts = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            region = RectangleF.Empty;
            reason = "Image-effect region must be 'full' or four percentage values: x,y,width,height.";
            return false;
        }

        var values = new double[4];
        for (var index = 0; index < parts.Length; index++)
        {
            var text = parts[index].TrimEnd('%');
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                region = RectangleF.Empty;
                reason = "Image-effect region percentages must be numeric.";
                return false;
            }

            values[index] = parsed;
        }

        var x = Math.Clamp(values[0], 0d, 100d);
        var y = Math.Clamp(values[1], 0d, 100d);
        var width = Math.Clamp(values[2], 0d, 100d - x);
        var height = Math.Clamp(values[3], 0d, 100d - y);
        if (width <= 0 || height <= 0)
        {
            region = RectangleF.Empty;
            reason = "Image-effect region width and height must be greater than zero.";
            return false;
        }

        region = new RectangleF(
            (float)(imageWidth * x / 100d),
            (float)(imageHeight * y / 100d),
            (float)(imageWidth * width / 100d),
            (float)(imageHeight * height / 100d));
        reason = string.Empty;
        return true;
    }

    private static string FormatImageEffectRegion(RectangleF region, int imageWidth, int imageHeight)
    {
        if (region.Left <= 0 &&
            region.Top <= 0 &&
            region.Width >= imageWidth &&
            region.Height >= imageHeight)
        {
            return "full";
        }

        return $"{region.Left:0.#},{region.Top:0.#},{region.Width:0.#},{region.Height:0.#}px";
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{fileName}-{Guid.NewGuid():N}{extension}");
    }

    private static bool IsSupportedImport(string path)
    {
        var extension = Path.GetExtension(path);
        return ImageExtensions.Contains(extension) ||
            VideoExtensions.Contains(extension) ||
            DocumentExtensions.Contains(extension);
    }

    private static async Task<bool> WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        long? previousLength = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (previousLength == info.Length)
                {
                    return true;
                }

                previousLength = info.Length;
            }
            catch (IOException)
            {
                // The producer may still be writing the file.
            }
            catch (UnauthorizedAccessException)
            {
                // Some apps create placeholders before the final file arrives.
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }
}

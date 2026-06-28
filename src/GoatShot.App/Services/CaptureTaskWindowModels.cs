using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class CaptureTaskWindowModels
{
    public static CaptureTaskViewModel Build(CaptureItem item, AppSettings settings)
    {
        var fileExists = File.Exists(item.FilePath);
        var isImage = IsImageFile(item.FilePath);
        var canAi = settings.AiEnabled && isImage && !IsAiDenied(settings, item);
        var actions = new List<CaptureTaskActionViewModel>
        {
            Action(CaptureTaskActionKind.OpenFile, "Open", "Open the saved capture with the default Windows app.", fileExists, MissingFileReason(item)),
            Action(CaptureTaskActionKind.OpenEditor, "Edit", "Open annotation, redaction, crop, and export tools.", isImage && fileExists, isImage ? MissingFileReason(item) : "Editor supports image captures."),
            Action(CaptureTaskActionKind.Copy, isImage ? "Copy image" : "Copy file", isImage ? "Copy bitmap pixels to the clipboard." : "Copy the file to the clipboard.", fileExists, MissingFileReason(item)),
            Action(CaptureTaskActionKind.Share, "Share", $"Share with the current default destination: {settings.DefaultShareDestination}.", fileExists, MissingFileReason(item)),
            Action(CaptureTaskActionKind.AiExplain, "AI", "Explain the screenshot using the configured Gemini provider.", canAi, AiDisabledReason(settings, item, isImage, fileExists)),
            Action(CaptureTaskActionKind.ExportDocument, "Document", "Export a local Markdown bug-report packet.", fileExists, MissingFileReason(item)),
            Action(CaptureTaskActionKind.DryRunScript, "Dry-run script", "Preview the custom script command without starting a process.", !string.IsNullOrWhiteSpace(settings.CustomScriptCommand), "Custom script command is not configured."),
            Action(CaptureTaskActionKind.DryRunWebhook, "Dry-run webhook", "Preview the custom webhook upload without sending HTTP.", !string.IsNullOrWhiteSpace(settings.CustomWebhookUrl), "Custom webhook URL is not configured."),
            Action(CaptureTaskActionKind.DeleteLocalFile, "Delete local", "Delete the local capture after an explicit confirmation.", fileExists, MissingFileReason(item))
        };

        return new CaptureTaskViewModel(
            "Capture actions",
            item.FileName,
            item.FilePath,
            item.Kind.ToString(),
            FormatBytes(item.Bytes),
            PrivacySummary(item),
            actions);
    }

    private static CaptureTaskActionViewModel Action(
        CaptureTaskActionKind kind,
        string title,
        string description,
        bool enabled,
        string disabledReason)
    {
        return new CaptureTaskActionViewModel(kind, title, description, enabled, enabled ? string.Empty : disabledReason);
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static bool IsAiDenied(AppSettings settings, CaptureItem item)
    {
        return !string.IsNullOrWhiteSpace(item.SourceApp) &&
            settings.AiDenylistProcesses.Any(process =>
                item.SourceApp.Contains(process, StringComparison.OrdinalIgnoreCase));
    }

    private static string AiDisabledReason(AppSettings settings, CaptureItem item, bool isImage, bool fileExists)
    {
        if (!fileExists)
        {
            return MissingFileReason(item);
        }

        if (!isImage)
        {
            return "AI explanation currently supports image captures.";
        }

        if (!settings.AiEnabled)
        {
            return "AI is disabled until configured in Settings.";
        }

        if (IsAiDenied(settings, item))
        {
            return $"AI is blocked by denylist for source app: {item.SourceApp}.";
        }

        return string.Empty;
    }

    private static string PrivacySummary(CaptureItem item)
    {
        var parts = new List<string>();
        if (item.IsPrivate)
        {
            parts.Add("private capture");
        }

        if (!string.IsNullOrWhiteSpace(item.OcrText))
        {
            parts.Add("OCR text stored");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceApp))
        {
            parts.Add($"source app: {item.SourceApp}");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceWindowTitle))
        {
            parts.Add("window title stored");
        }

        return parts.Count == 0 ? "No stored privacy markers." : string.Join("; ", parts);
    }

    private static string MissingFileReason(CaptureItem item)
    {
        return File.Exists(item.FilePath) ? string.Empty : "Local file is missing.";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024 * 1024
            ? $"{bytes / 1024d:0.#} KB"
            : $"{bytes / 1024d / 1024d:0.##} MB";
    }
}

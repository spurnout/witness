using System.Globalization;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WorkflowActionDryRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;

    public WorkflowActionDryRunService(AppSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    public WorkflowActionDryRunPlan PlanCustomScript(CaptureItem item, string? commandOverride = null)
    {
        if (!ManagedPolicyService.IsShareDestinationAllowed(_settings, ShareDestination.CustomScript, out var policyReason))
        {
            return new WorkflowActionDryRunPlan
            {
                Action = AutomationActionKind.RunCustomScript,
                Configured = !string.IsNullOrWhiteSpace(commandOverride) || !string.IsNullOrWhiteSpace(_settings.CustomScriptCommand),
                WouldExecute = false,
                Target = string.Empty,
                PayloadSummary = "Custom script dry run is blocked by managed policy.",
                MetadataPreview = RedactedMetadata(BuildRequest(item).Metadata),
                Message = policyReason
            };
        }

        var command = string.IsNullOrWhiteSpace(commandOverride)
            ? _settings.CustomScriptCommand
            : commandOverride;
        var configured = !string.IsNullOrWhiteSpace(command);
        var metadataPath = Path.Combine(_paths.TempRoot, $"{SafeId(item)}-metadata.json");
        var ocrPath = Path.Combine(_paths.TempRoot, $"{SafeId(item)}-ocr.txt");
        var request = BuildRequest(item);
        var resolved = configured
            ? ExpandScriptCommand(command!, request, metadataPath, ocrPath)
            : string.Empty;
        var fileExists = File.Exists(item.FilePath);

        return new WorkflowActionDryRunPlan
        {
            Action = AutomationActionKind.RunCustomScript,
            Configured = configured,
            WouldExecute = configured && fileExists,
            Target = "powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand <base64 command>",
            ResolvedCommand = WorkflowTextRedactor.Redact(resolved),
            PayloadSummary = configured
                ? $"Would write metadata JSON to {metadataPath}, OCR text to {ocrPath}, then execute the resolved PowerShell command."
                : "Custom script command is not configured.",
            MetadataPreview = RedactedMetadata(request.Metadata),
            Message = configured && fileExists
                ? "Dry run only. No process was started."
                : configured
                    ? $"Dry run blocked: source file does not exist: {item.FilePath}"
                    : "Dry run blocked: no custom script command is configured."
        };
    }

    public WorkflowActionDryRunPlan PlanCustomWebhook(CaptureItem item, string? webhookUrlOverride = null)
    {
        if (!ManagedPolicyService.IsShareDestinationAllowed(_settings, ShareDestination.CustomWebhook, out var policyReason))
        {
            return new WorkflowActionDryRunPlan
            {
                Action = AutomationActionKind.CallCustomWebhook,
                Configured = !string.IsNullOrWhiteSpace(webhookUrlOverride) || !string.IsNullOrWhiteSpace(_settings.CustomWebhookUrl),
                WouldExecute = false,
                Target = string.Empty,
                PayloadSummary = "Custom webhook dry run is blocked by managed policy.",
                MetadataPreview = RedactedMetadata(BuildRequest(item).Metadata),
                Message = policyReason
            };
        }

        var url = string.IsNullOrWhiteSpace(webhookUrlOverride)
            ? _settings.CustomWebhookUrl
            : webhookUrlOverride;
        var configured = !string.IsNullOrWhiteSpace(url);
        var request = BuildRequest(item);
        var metadata = BuildWebhookMetadata(request);
        var fileExists = File.Exists(item.FilePath);

        return new WorkflowActionDryRunPlan
        {
            Action = AutomationActionKind.CallCustomWebhook,
            Configured = configured,
            WouldExecute = configured && fileExists,
            Target = configured ? WorkflowTextRedactor.Redact(url) : string.Empty,
            PayloadSummary = configured
                ? $"Would POST multipart/form-data with file field '{Path.GetFileName(item.FilePath)}' and metadata JSON: {WorkflowTextRedactor.Redact(JsonSerializer.Serialize(metadata, JsonOptions))}"
                : "Custom webhook URL is not configured.",
            MetadataPreview = RedactedMetadata(request.Metadata),
            Message = configured && fileExists
                ? "Dry run only. No HTTP request was sent."
                : configured
                    ? $"Dry run blocked: source file does not exist: {item.FilePath}"
                    : "Dry run blocked: no custom webhook URL is configured."
        };
    }

    private static ShareUploadRequest BuildRequest(CaptureItem item)
    {
        return new ShareUploadRequest(
            item.FilePath,
            item.Kind.ToString(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = item.Id,
                ["file"] = item.FilePath,
                ["fileName"] = item.FileName,
                ["captureType"] = item.Kind.ToString(),
                ["createdAt"] = item.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                ["bytes"] = item.Bytes.ToString(CultureInfo.InvariantCulture),
                ["width"] = item.Width.ToString(CultureInfo.InvariantCulture),
                ["height"] = item.Height.ToString(CultureInfo.InvariantCulture),
                ["isPrivate"] = item.IsPrivate ? "true" : "false",
                ["sourceApp"] = item.SourceApp ?? string.Empty,
                ["sourceWindowTitle"] = item.SourceWindowTitle ?? string.Empty,
                ["bounds"] = item.Bounds?.Display ?? string.Empty,
                ["notes"] = item.Notes ?? string.Empty,
                ["ocrText"] = item.OcrText ?? string.Empty
            });
    }

    private static Dictionary<string, string> BuildWebhookMetadata(ShareUploadRequest request)
    {
        return new Dictionary<string, string>
        {
            ["id"] = MetadataValue(request, "id"),
            ["file"] = MetadataValue(request, "file", request.FilePath),
            ["captureType"] = MetadataValue(request, "captureType", request.CaptureType),
            ["createdAt"] = MetadataValue(request, "createdAt"),
            ["width"] = MetadataValue(request, "width"),
            ["height"] = MetadataValue(request, "height"),
            ["sourceApp"] = MetadataValue(request, "sourceApp"),
            ["bounds"] = MetadataValue(request, "bounds")
        };
    }

    private static string ExpandScriptCommand(
        string template,
        ShareUploadRequest request,
        string metadataPath,
        string ocrPath)
    {
        return template
            .Replace("{file}", EscapePowerShellLiteral(request.FilePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{metadata}", EscapePowerShellLiteral(metadataPath), StringComparison.OrdinalIgnoreCase)
            .Replace("{ocr}", EscapePowerShellLiteral(ocrPath), StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", EscapePowerShellLiteral(MetadataValue(request, "id")), StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", EscapePowerShellLiteral(request.CaptureType), StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) ? value : fallback;
    }

    private static Dictionary<string, string> RedactedMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        return metadata.ToDictionary(
            pair => pair.Key,
            pair => WorkflowTextRedactor.Redact(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string SafeId(CaptureItem item)
    {
        return string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
    }
}

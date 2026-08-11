using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WorkflowRunLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;

    public WorkflowRunLogService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task WriteAsync(
        AutomationRunResult result,
        CaptureItem item,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_paths.LogsRoot, "workflow-runs");
        Directory.CreateDirectory(directory);

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff");
        var safeId = string.IsNullOrWhiteSpace(item.Id) ? "capture" : SanitizeFileName(item.Id);
        var basePath = Path.Combine(directory, $"{stamp}-{result.Trigger}-{safeId}");
        var jsonPath = basePath + ".json";
        var markdownPath = basePath + ".md";

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(BuildLog(result, item), JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            markdownPath,
            BuildMarkdown(result, item),
            cancellationToken);

        result.LogPath = jsonPath;
        result.MarkdownLogPath = markdownPath;
        result.LogMessage = $"Workflow run log written: {markdownPath}";
    }

    private static object BuildLog(AutomationRunResult result, CaptureItem item)
    {
        return new
        {
            createdAt = DateTimeOffset.Now,
            trigger = result.Trigger,
            dryRun = result.DryRun,
            totalRules = result.TotalRules,
            matchingRules = result.MatchingRules,
            succeeded = result.Succeeded,
            capture = new
            {
                id = item.Id,
                fileName = item.FileName,
                filePath = item.FilePath,
                kind = item.Kind,
                bytes = item.Bytes,
                isPrivate = item.IsPrivate,
                sourceApp = WorkflowTextRedactor.Redact(item.SourceApp),
                sourceWindowTitle = WorkflowTextRedactor.Redact(item.SourceWindowTitle),
                monitor = item.SourceMonitorName
            },
            evaluations = result.Evaluations.Select(evaluation => new
            {
                evaluation.RuleId,
                evaluation.RuleName,
                evaluation.RuleTrigger,
                evaluation.RequestedTrigger,
                evaluation.IsEnabled,
                evaluation.TriggerMatches,
                evaluation.Matches,
                reasons = evaluation.Reasons.Select(WorkflowTextRedactor.Redact).ToList(),
                actions = evaluation.Actions
            }).ToList(),
            rules = result.Rules.Select(rule => new
            {
                rule.Evaluation.RuleId,
                rule.Evaluation.RuleName,
                actions = rule.Actions.Select(action => new
                {
                    action.Action,
                    action.Executed,
                    action.Succeeded,
                    message = WorkflowTextRedactor.Redact(action.Message),
                    outputPath = WorkflowTextRedactor.Redact(action.OutputPath)
                }).ToList()
            }).ToList()
        };
    }

    private static string BuildMarkdown(AutomationRunResult result, CaptureItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Workflow Run");
        builder.AppendLine();
        builder.AppendLine($"- Trigger: `{result.Trigger}`");
        builder.AppendLine($"- Dry run: `{result.DryRun}`");
        builder.AppendLine($"- Capture: `{EscapeMarkdown(item.FileName)}`");
        builder.AppendLine($"- Path: `{EscapeMarkdown(item.FilePath)}`");
        builder.AppendLine($"- Matching rules: `{result.MatchingRules}` of `{result.TotalRules}`");
        builder.AppendLine($"- Result: `{(result.Succeeded ? "Succeeded" : "Failed")}`");
        builder.AppendLine();

        builder.AppendLine("## Rule Evaluation");
        foreach (var evaluation in result.Evaluations)
        {
            builder.AppendLine();
            builder.AppendLine($"### {EscapeMarkdown(evaluation.RuleName)}");
            builder.AppendLine($"- Rule id: `{EscapeMarkdown(evaluation.RuleId)}`");
            builder.AppendLine($"- Rule trigger: `{evaluation.RuleTrigger}`");
            builder.AppendLine($"- Matched: `{evaluation.Matches}`");
            builder.AppendLine($"- Actions: `{(evaluation.Actions.Count == 0 ? "none" : string.Join(", ", evaluation.Actions))}`");
            builder.AppendLine("- Reasons:");
            foreach (var reason in evaluation.Reasons)
            {
                builder.AppendLine($"  - {EscapeMarkdown(WorkflowTextRedactor.Redact(reason))}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Action Results");
        if (result.Rules.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No matching rule actions ran.");
        }

        foreach (var rule in result.Rules)
        {
            builder.AppendLine();
            builder.AppendLine($"### {EscapeMarkdown(rule.Evaluation.RuleName)}");
            foreach (var action in rule.Actions)
            {
                builder.AppendLine(
                    $"- `{action.Action}` executed=`{action.Executed}` succeeded=`{action.Succeeded}`: {EscapeMarkdown(WorkflowTextRedactor.Redact(action.Message))}");
                if (!string.IsNullOrWhiteSpace(action.OutputPath))
                {
                    builder.AppendLine($"  Output: `{EscapeMarkdown(WorkflowTextRedactor.Redact(action.OutputPath))}`");
                }
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdown(string? value)
    {
        return (value ?? string.Empty).Replace("`", "'", StringComparison.Ordinal);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }
}

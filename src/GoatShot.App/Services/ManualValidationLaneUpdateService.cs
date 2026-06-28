using System.Globalization;
using System.Text;

namespace GoatShot.App.Services;

public sealed class ManualValidationLaneUpdateService
{
    private static readonly IReadOnlyDictionary<string, string> LaneAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["keyboard"] = "keyboard-traversal",
            ["tab"] = "keyboard-traversal",
            ["tabs"] = "keyboard-traversal",
            ["narrator"] = "screen-reader",
            ["nvda"] = "screen-reader",
            ["screenreader"] = "screen-reader",
            ["reader"] = "screen-reader",
            ["scaling"] = "text-scaling",
            ["textscale"] = "text-scaling",
            ["contrast"] = "high-contrast",
            ["drag"] = "live-region-drag",
            ["region"] = "live-region-drag",
            ["live-drag"] = "live-region-drag",
            ["clean"] = "clean-machine-install",
            ["clean-machine"] = "clean-machine-install",
            ["installer"] = "clean-machine-install",
            ["provider"] = "live-provider-proof",
            ["providers"] = "live-provider-proof",
            ["oauth"] = "live-provider-proof",
            ["browser"] = "browser-extension-live-fixture",
            ["extension"] = "browser-extension-live-fixture",
            ["android"] = "android-safe-device-proof"
        };

    public async Task<ManualValidationLaneUpdateResult> UpdateAsync(
        ManualValidationLaneUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationLaneUpdateResult.Failed("Manual validation folder is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Lane))
        {
            return ManualValidationLaneUpdateResult.Failed("Manual validation lane is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return ManualValidationLaneUpdateResult.Failed("Manual validation lane status is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationLaneUpdateResult.Failed($"Manual validation folder was not found: {root}");
        }

        if (!TryParseStatus(request.Status, out var status, out var statusIssue))
        {
            return ManualValidationLaneUpdateResult.Failed(statusIssue);
        }

        var lane = FindLane(request.Lane);
        if (lane is null)
        {
            return ManualValidationLaneUpdateResult.Failed($"Manual validation lane was not recognized: {SensitiveTextDetector.Redact(request.Lane)}");
        }

        if (status is ManualValidationLaneStatus.NotApplicable &&
            lane.Requirement is ManualValidationLaneRequirement.Required)
        {
            return ManualValidationLaneUpdateResult.Failed("Required manual validation lanes cannot be marked not applicable; use blocked with an operator note when proof cannot be collected.");
        }

        var note = SensitiveTextDetector.Redact((request.Note ?? string.Empty).Trim());
        if (status is ManualValidationLaneStatus.Blocked or ManualValidationLaneStatus.Failed &&
            string.IsNullOrWhiteSpace(note))
        {
            return ManualValidationLaneUpdateResult.Failed("Blocked or failed manual validation lane requires --note.");
        }

        var lanePath = Path.Combine(root, lane.RelativePath);
        if (!File.Exists(lanePath))
        {
            return ManualValidationLaneUpdateResult.Failed($"Manual validation lane file was not found: {lanePath}");
        }

        var warnings = new List<string>();
        var evidence = NormalizeEvidence(root, request.EvidencePaths, warnings);
        var observedAt = request.ObservedAt ?? DateTimeOffset.Now;
        var operatorName = SensitiveTextDetector.Redact((request.OperatorName ?? string.Empty).Trim());

        var text = await File.ReadAllTextAsync(lanePath, cancellationToken);
        text = UpdateResultSection(text, status);
        text = AppendEvidenceToSection(text, observedAt, evidence);
        text = AppendNoteToSection(text, observedAt, status, note, operatorName);
        text = AppendOperatorUpdateBlock(text, observedAt, status, note, operatorName, evidence);

        await File.WriteAllTextAsync(lanePath, text, cancellationToken);

        var message = status is ManualValidationLaneStatus.Passed
            ? $"Manual validation lane recorded as passed: {lane.Title}"
            : $"Manual validation lane recorded as {FormatStatus(status).ToLowerInvariant()}: {lane.Title}";

        return new ManualValidationLaneUpdateResult
        {
            Succeeded = true,
            Message = message,
            RootPath = root,
            LaneId = lane.Id,
            LaneTitle = lane.Title,
            LanePath = lanePath,
            Status = status,
            Note = note,
            OperatorName = operatorName,
            ObservedAt = observedAt,
            Evidence = evidence,
            Warnings = warnings
        };
    }

    private static ManualValidationLaneDefinition? FindLane(string laneText)
    {
        var normalized = NormalizeKey(laneText);
        if (LaneAliases.TryGetValue(normalized, out var aliasedId))
        {
            normalized = NormalizeKey(aliasedId);
        }

        foreach (var lane in ManualValidationHarnessService.ExpectedLanes)
        {
            var candidates = new[]
            {
                lane.Id,
                lane.Title,
                lane.RelativePath,
                Path.GetFileNameWithoutExtension(lane.RelativePath)
            };

            if (candidates.Any(candidate => NormalizeKey(candidate).Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return lane;
            }
        }

        return null;
    }

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool TryParseStatus(string statusText, out ManualValidationLaneStatus status, out string issue)
    {
        var normalized = NormalizeKey(statusText);
        switch (normalized)
        {
            case "pass":
            case "passed":
            case "success":
                status = ManualValidationLaneStatus.Passed;
                issue = string.Empty;
                return true;
            case "fail":
            case "failed":
            case "failure":
                status = ManualValidationLaneStatus.Failed;
                issue = string.Empty;
                return true;
            case "block":
            case "blocked":
                status = ManualValidationLaneStatus.Blocked;
                issue = string.Empty;
                return true;
            case "pending":
            case "notrun":
            case "unrun":
                status = ManualValidationLaneStatus.NotRun;
                issue = string.Empty;
                return true;
            case "na":
            case "n/a":
            case "notapplicable":
            case "notapp":
            case "skipped":
                status = ManualValidationLaneStatus.NotApplicable;
                issue = string.Empty;
                return true;
            default:
                status = ManualValidationLaneStatus.NotRun;
                issue = "Manual validation lane status must be passed, failed, blocked, pending, or not-applicable.";
                return false;
        }
    }

    private static List<ManualValidationLaneUpdateEvidence> NormalizeEvidence(
        string root,
        IReadOnlyList<string> evidencePaths,
        List<string> warnings)
    {
        var evidence = new List<ManualValidationLaneUpdateEvidence>();
        foreach (var rawValue in evidencePaths.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var trimmed = Environment.ExpandEnvironmentVariables(rawValue.Trim());
            var findings = SensitiveTextDetector.Find(trimmed);
            var warning = string.Empty;
            var display = SensitiveTextDetector.Redact(trimmed);
            var exists = false;
            var insideRoot = false;

            try
            {
                var candidatePath = Path.IsPathRooted(trimmed)
                    ? Path.GetFullPath(trimmed)
                    : Path.GetFullPath(Path.Combine(root, trimmed));
                exists = File.Exists(candidatePath) || Directory.Exists(candidatePath);
                insideRoot = IsInsideRoot(root, candidatePath);

                if (insideRoot)
                {
                    display = Path.GetRelativePath(root, candidatePath).Replace('\\', '/');
                }
                else if (Path.IsPathRooted(trimmed))
                {
                    var fileName = Path.GetFileName(trimmed);
                    display = string.IsNullOrWhiteSpace(fileName)
                        ? "[external evidence path omitted]"
                        : $"[external evidence: {SensitiveTextDetector.Redact(fileName)}]";
                    warning = "External evidence path was reduced to a file name only.";
                    warnings.Add(warning);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                display = SensitiveTextDetector.Redact(trimmed);
                warning = "Evidence value could not be normalized as a path.";
                warnings.Add(warning);
            }

            if (findings.Count > 0)
            {
                var sensitiveWarning = $"Evidence value was redacted: {SensitiveTextDetector.Summarize(findings)}";
                warning = string.IsNullOrWhiteSpace(warning)
                    ? sensitiveWarning
                    : $"{warning} {sensitiveWarning}";
                warnings.Add(sensitiveWarning);
            }

            evidence.Add(new ManualValidationLaneUpdateEvidence
            {
                Value = display,
                Exists = exists,
                InsideManualFolder = insideRoot,
                Warning = warning
            });
        }

        return evidence;
    }

    private static bool IsInsideRoot(string root, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string UpdateResultSection(string text, ManualValidationLaneStatus selectedStatus)
    {
        var newline = PreferredNewLine(text);
        var lines = ToLines(text);
        var inResult = false;
        var sawResult = false;
        var sawSelectedStatus = false;
        var resultEndIndex = -1;
        var lastStatusLineIndex = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals("## Result", StringComparison.OrdinalIgnoreCase))
            {
                inResult = true;
                sawResult = true;
                continue;
            }

            if (inResult && trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                resultEndIndex = i;
                break;
            }

            if (!inResult || !TryReadResultStatus(lines[i], out var lineStatus, out var indent))
            {
                continue;
            }

            lastStatusLineIndex = i;
            var isSelected = lineStatus == selectedStatus ||
                             (selectedStatus is ManualValidationLaneStatus.NotRun && lineStatus is ManualValidationLaneStatus.NotRun);
            lines[i] = $"{indent}- [{(isSelected ? "x" : " ")}] {LabelForResultLine(lineStatus)}";
            sawSelectedStatus |= isSelected;
        }

        if (!sawResult)
        {
            lines.AddRange(["", "## Result", "", $"- [x] {LabelForResultLine(selectedStatus)}"]);
            return string.Join(newline, lines).TrimEnd('\r', '\n') + newline;
        }

        if (!sawSelectedStatus)
        {
            var insertIndex = lastStatusLineIndex >= 0
                ? lastStatusLineIndex + 1
                : resultEndIndex >= 0
                    ? resultEndIndex
                    : lines.Count;
            lines.Insert(insertIndex, $"- [x] {LabelForResultLine(selectedStatus)}");
        }

        return string.Join(newline, lines).TrimEnd('\r', '\n') + newline;
    }

    private static bool TryReadResultStatus(
        string line,
        out ManualValidationLaneStatus status,
        out string indent)
    {
        status = ManualValidationLaneStatus.NotRun;
        indent = string.Empty;

        var trimmedStart = line.TrimStart();
        if (!trimmedStart.StartsWith("- [", StringComparison.OrdinalIgnoreCase) ||
            trimmedStart.Length < 5)
        {
            return false;
        }

        var marker = trimmedStart[..5];
        if (!marker.Equals("- [ ]", StringComparison.OrdinalIgnoreCase) &&
            !marker.Equals("- [x]", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var label = trimmedStart[5..].Trim();
        if (label.Contains("Passed", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Pass", StringComparison.OrdinalIgnoreCase))
        {
            status = ManualValidationLaneStatus.Passed;
        }
        else if (label.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                 label.Equals("Fail", StringComparison.OrdinalIgnoreCase))
        {
            status = ManualValidationLaneStatus.Failed;
        }
        else if (label.Contains("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            status = ManualValidationLaneStatus.Blocked;
        }
        else if (label.Contains("Not applicable", StringComparison.OrdinalIgnoreCase) ||
                 label.Contains("N/A", StringComparison.OrdinalIgnoreCase))
        {
            status = ManualValidationLaneStatus.NotApplicable;
        }
        else if (label.Contains("Pending", StringComparison.OrdinalIgnoreCase) ||
                 label.Contains("Not run", StringComparison.OrdinalIgnoreCase))
        {
            status = ManualValidationLaneStatus.NotRun;
        }
        else
        {
            return false;
        }

        indent = line[..(line.Length - trimmedStart.Length)];
        return true;
    }

    private static string LabelForResultLine(ManualValidationLaneStatus status) =>
        status switch
        {
            ManualValidationLaneStatus.Passed => "Passed",
            ManualValidationLaneStatus.Failed => "Failed",
            ManualValidationLaneStatus.Blocked => "Blocked",
            ManualValidationLaneStatus.NotApplicable => "Not applicable",
            _ => "Pending"
        };

    private static string AppendEvidenceToSection(
        string text,
        DateTimeOffset observedAt,
        IReadOnlyList<ManualValidationLaneUpdateEvidence> evidence)
    {
        if (evidence.Count == 0)
        {
            return text;
        }

        var lines = evidence
            .Select(item => $"- Operator evidence {observedAt:O}: `{item.Value}`")
            .ToList();
        return InsertIntoSection(text, "## Evidence", lines);
    }

    private static string AppendNoteToSection(
        string text,
        DateTimeOffset observedAt,
        ManualValidationLaneStatus status,
        string note,
        string operatorName)
    {
        var lines = new List<string>
        {
            $"- Operator update {observedAt:O}: status `{FormatStatus(status)}`."
        };

        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            lines.Add($"- Operator: {operatorName}");
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add($"- Note: {note}");
        }

        return InsertIntoSection(text, "## Notes", lines);
    }

    private static string AppendOperatorUpdateBlock(
        string text,
        DateTimeOffset observedAt,
        ManualValidationLaneStatus status,
        string note,
        string operatorName,
        IReadOnlyList<ManualValidationLaneUpdateEvidence> evidence)
    {
        var newline = PreferredNewLine(text);
        var builder = new StringBuilder(text.TrimEnd('\r', '\n'));
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("## Operator Update");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Observed at: `{observedAt:O}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Status: `{FormatStatus(status)}`");
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            builder.AppendLine($"- Operator: {operatorName}");
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            builder.AppendLine($"- Note: {note}");
        }

        if (evidence.Count > 0)
        {
            builder.AppendLine("- Evidence:");
            foreach (var item in evidence)
            {
                var suffix = item.Exists
                    ? "exists"
                    : "not checked or missing";
                builder.AppendLine($"  - `{item.Value}` ({suffix})");
            }
        }

        builder.AppendLine("- Boundary: this is operator-supplied evidence; it does not replace live human/hardware/OAuth proof that was not actually performed.");
        return builder.ToString().Replace(Environment.NewLine, newline, StringComparison.Ordinal).TrimEnd('\r', '\n') + newline;
    }

    private static string InsertIntoSection(string text, string heading, IReadOnlyList<string> insertLines)
    {
        if (insertLines.Count == 0)
        {
            return text;
        }

        var newline = PreferredNewLine(text);
        var lines = ToLines(text);
        var headingIndex = lines.FindIndex(line => line.Trim().Equals(heading, StringComparison.OrdinalIgnoreCase));
        if (headingIndex < 0)
        {
            lines.Add(string.Empty);
            lines.Add(heading);
            lines.Add(string.Empty);
            lines.AddRange(insertLines);
            return string.Join(newline, lines).TrimEnd('\r', '\n') + newline;
        }

        var insertIndex = lines.Count;
        for (var i = headingIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("## ", StringComparison.Ordinal))
            {
                insertIndex = i;
                break;
            }
        }

        if (insertIndex > headingIndex + 1 && string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
        {
            insertIndex--;
        }

        lines.InsertRange(insertIndex, insertLines);
        lines.Insert(insertIndex + insertLines.Count, string.Empty);
        return string.Join(newline, lines).TrimEnd('\r', '\n') + newline;
    }

    private static List<string> ToLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static string PreferredNewLine(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string FormatStatus(ManualValidationLaneStatus status) =>
        status switch
        {
            ManualValidationLaneStatus.NotRun => "NotRun",
            ManualValidationLaneStatus.NotApplicable => "NotApplicable",
            _ => status.ToString()
        };
}

public sealed class ManualValidationLaneUpdateRequest
{
    public string RootPath { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? OperatorName { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public IReadOnlyList<string> EvidencePaths { get; set; } = Array.Empty<string>();
}

public sealed class ManualValidationLaneUpdateResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string LaneId { get; set; } = string.Empty;
    public string LaneTitle { get; set; } = string.Empty;
    public string LanePath { get; set; } = string.Empty;
    public ManualValidationLaneStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
    public List<ManualValidationLaneUpdateEvidence> Evidence { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ManualValidationLaneUpdateResult Failed(string message)
    {
        return new ManualValidationLaneUpdateResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Warnings = [SensitiveTextDetector.Redact(message)]
        };
    }
}

public sealed class ManualValidationLaneUpdateEvidence
{
    public string Value { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool InsideManualFolder { get; set; }
    public string Warning { get; set; } = string.Empty;
}

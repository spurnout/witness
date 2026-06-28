using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AiActionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public AiActionHistoryService(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<AiActionHistoryEntry> Load(int limit = 50)
    {
        lock (_gate)
        {
            return LoadCore()
                .OrderByDescending(entry => entry.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToList();
        }
    }

    public IReadOnlyList<AiPromptHistoryItem> LoadPromptHistory(AiActionKind? action = null, int limit = 20)
    {
        lock (_gate)
        {
            return LoadCore()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Prompt))
                .Where(entry => action is null || entry.Action == action)
                .GroupBy(entry => new
                {
                    entry.Action,
                    Prompt = entry.Prompt.Trim(),
                    entry.ModelId
                })
                .Select(group => new AiPromptHistoryItem
                {
                    Action = group.Key.Action,
                    Prompt = group.Key.Prompt,
                    ModelId = group.Key.ModelId,
                    UseCount = group.Count(),
                    LastUsedAt = group.Max(entry => entry.CreatedAt)
                })
                .OrderByDescending(item => item.LastUsedAt)
                .ThenByDescending(item => item.UseCount)
                .Take(Math.Clamp(limit, 1, 100))
                .ToList();
        }
    }

    public AiActionHistoryEntry? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            return LoadCore().FirstOrDefault(entry =>
                entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                entry.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<AiActionHistoryEntry?> UpdateReviewStatusAsync(
        string id,
        AiActionReviewStatus status,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            lock (_gate)
            {
                var entries = LoadCore().ToList();
                var entry = entries.FirstOrDefault(existing =>
                    existing.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    existing.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return null;
                }

                entry.ReviewStatus = status;
                entry.ReviewedAt = DateTimeOffset.Now;
                entry.ReviewNote = string.IsNullOrWhiteSpace(note)
                    ? null
                    : RedactAndLimit(note, 1_000);

                SaveCore(entries);
                return entry;
            }
        }, cancellationToken);
    }

    public async Task<AiActionHistoryEntry> RecordAsync(
        AiActionKind action,
        CaptureItem item,
        string modelId,
        string prompt,
        bool succeeded,
        string message,
        string? outputPath = null,
        string? textOutput = null,
        string? parentEntryId = null,
        AiActionReviewStatus? reviewStatus = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AiActionHistoryEntry
        {
            Action = action,
            CaptureItemId = item.Id,
            FileName = item.FileName,
            FilePath = item.FilePath,
            ModelId = modelId,
            Prompt = RedactAndLimit(prompt, 1_000),
            Succeeded = succeeded,
            ReviewStatus = reviewStatus ?? (succeeded ? AiActionReviewStatus.Pending : AiActionReviewStatus.Rejected),
            Message = RedactAndLimit(message, 1_000),
            OutputPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath,
            TextPreview = string.IsNullOrWhiteSpace(textOutput) ? null : RedactAndLimit(textOutput, 1_000),
            ParentEntryId = string.IsNullOrWhiteSpace(parentEntryId) ? null : parentEntryId
        };

        await Task.Run(() =>
        {
            lock (_gate)
            {
                var entries = LoadCore().ToList();

                entries.Insert(0, entry);
                entries = entries
                    .OrderByDescending(existing => existing.CreatedAt)
                    .Take(500)
                    .ToList();

                SaveCore(entries);
            }
        }, cancellationToken);

        return entry;
    }

    private List<AiActionHistoryEntry> LoadCore()
    {
        if (!File.Exists(_paths.AiActionHistoryPath))
        {
            return new List<AiActionHistoryEntry>();
        }

        try
        {
            var json = File.ReadAllText(_paths.AiActionHistoryPath);
            return JsonSerializer.Deserialize<List<AiActionHistoryEntry>>(json, JsonOptions) ?? new List<AiActionHistoryEntry>();
        }
        catch
        {
            return new List<AiActionHistoryEntry>();
        }
    }

    private void SaveCore(IReadOnlyList<AiActionHistoryEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.AiActionHistoryPath)!);
        File.WriteAllText(_paths.AiActionHistoryPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static string RedactAndLimit(string value, int maxLength)
    {
        var redacted = SensitiveTextDetector.Scan(value).RedactedText.ReplaceLineEndings(" ").Trim();
        return redacted.Length <= maxLength ? redacted : redacted[..maxLength] + "...";
    }
}

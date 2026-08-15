namespace GoatShot.App.Services;

/// <summary>One searchable control in the settings window, plus the section it lives in.</summary>
public sealed record SettingsSearchEntry(string Label, string SectionKey, string SectionLabel)
{
    public string Display => $"{Label}  ·  {SectionLabel}";
}

/// <summary>
/// Matching for the settings filter box. Settings is one long scroll of roughly 240 controls, so
/// finding a field previously required already knowing which section owned it.
/// </summary>
public static class SettingsSearchIndex
{
    public static IReadOnlyList<SettingsSearchEntry> Match(
        IEnumerable<SettingsSearchEntry> entries,
        string? query,
        int limit)
    {
        var terms = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0 || limit <= 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label))
            .Where(entry => seen.Add($"{entry.SectionKey}{entry.Label}"))
            .Select(entry => (Entry: entry, Score: Score(entry, terms)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Entry.Label, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Entry)
            .ToArray();
    }

    /// <summary>
    /// Every term must hit, so typing more words narrows rather than widens. A term that starts the
    /// label scores highest, then a word start inside it, then the section name.
    /// </summary>
    private static int Score(SettingsSearchEntry entry, IReadOnlyList<string> terms)
    {
        var total = 0;
        foreach (var term in terms)
        {
            var termScore = 0;
            if (entry.Label.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = 6;
            }
            else if (StartsAWordIn(entry.Label, term))
            {
                termScore = 4;
            }
            else if (entry.Label.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = 3;
            }
            else if (entry.SectionLabel.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = 1;
            }

            if (termScore == 0)
            {
                return 0;
            }

            total += termScore;
        }

        return total;
    }

    private static bool StartsAWordIn(string label, string term)
    {
        var index = label.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (index > 0)
        {
            if (!char.IsLetterOrDigit(label[index - 1]))
            {
                return true;
            }

            index = label.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

namespace GoatShot.App.Services;

public sealed record LegacyLibraryRelocationPlan(string Source, string Target)
{
    public string Prompt =>
        $"Move your capture library from{Environment.NewLine}{Source}{Environment.NewLine}to{Environment.NewLine}{Target}?" +
        $"{Environment.NewLine}{Environment.NewLine}Files are moved, not copied, and nothing is deleted.";
}

public sealed record LegacyLibraryRelocationResult(bool Succeeded, string Message, string LibraryRoot);

/// <summary>
/// Upgraded installs keep their capture folder from before the Receipts rename, so the library still
/// reads "GoatShot" while the rest of the app does not. Moving someone's files silently would be
/// wrong, so this only ever describes the option and acts when the user asks.
/// </summary>
public static class LegacyLibraryRelocationService
{
    /// <summary>Returns a move plan when the library still sits in the legacy folder, else null.</summary>
    public static LegacyLibraryRelocationPlan? Describe(string? libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(libraryRoot.Trim());
        var folder = Path.GetFileName(trimmed);
        if (!string.Equals(folder, BrandIdentity.LegacyLibraryDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return null;
        }

        return new LegacyLibraryRelocationPlan(
            trimmed,
            Path.Combine(parent, BrandIdentity.LibraryDirectoryName));
    }

    public static LegacyLibraryRelocationResult Relocate(LegacyLibraryRelocationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!Directory.Exists(plan.Source))
        {
            return new LegacyLibraryRelocationResult(
                false,
                $"The folder {plan.Source} no longer exists, so there is nothing to move.",
                plan.Source);
        }

        // Refuse rather than merge: clobbering an existing library would be unrecoverable.
        if (Directory.Exists(plan.Target) &&
            Directory.EnumerateFileSystemEntries(plan.Target).Any())
        {
            return new LegacyLibraryRelocationResult(
                false,
                $"{plan.Target} already has files in it. Move or rename it first, then try again.",
                plan.Source);
        }

        try
        {
            if (Directory.Exists(plan.Target))
            {
                // Directory.Move needs a target that does not exist; the empty one is safe to drop.
                Directory.Delete(plan.Target, recursive: false);
            }

            Directory.Move(plan.Source, plan.Target);
            return new LegacyLibraryRelocationResult(
                true,
                $"Your library now lives in {plan.Target}.",
                plan.Target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LegacyLibraryRelocationResult(
                false,
                $"The library could not be moved and was left where it is. {ex.Message}",
                plan.Source);
        }
    }
}

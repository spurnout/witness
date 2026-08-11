namespace GoatShot.App.Services;

/// <summary>
/// Resolves Receipts environment variables while preserving GOATSHOT_* compatibility.
/// The Receipts name always wins when both names are configured.
/// </summary>
public static class BrandEnvironment
{
    public const string LocalRootSuffix = "LOCAL_ROOT";
    public const string LibraryRootSuffix = "LIBRARY_ROOT";

    public static BrandEnvironmentResolution Resolve(
        string suffix,
        Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;

        var currentName = BrandIdentity.EnvironmentVariable(suffix);
        var currentValue = NormalizeValue(readVariable(currentName));
        if (currentValue is not null)
        {
            return new BrandEnvironmentResolution(currentValue, currentName, UsedLegacyFallback: false);
        }

        var legacyName = BrandIdentity.LegacyEnvironmentVariable(suffix);
        var legacyValue = NormalizeValue(readVariable(legacyName));
        return legacyValue is null
            ? BrandEnvironmentResolution.Unconfigured
            : new BrandEnvironmentResolution(legacyValue, legacyName, UsedLegacyFallback: true);
    }

    public static BrandPathResolution ResolveLocalRoot(
        Func<string, string?>? readVariable = null,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        getFolderPath ??= Environment.GetFolderPath;
        return ResolvePath(
            LocalRootSuffix,
            Path.Combine(getFolderPath(Environment.SpecialFolder.LocalApplicationData), BrandIdentity.LocalDataDirectoryName),
            readVariable);
    }

    public static BrandPathResolution ResolveLibraryRoot(
        Func<string, string?>? readVariable = null,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        getFolderPath ??= Environment.GetFolderPath;
        return ResolvePath(
            LibraryRootSuffix,
            Path.Combine(getFolderPath(Environment.SpecialFolder.MyPictures), BrandIdentity.LibraryDirectoryName),
            readVariable);
    }

    public static BrandPathResolution ResolvePath(
        string suffix,
        string defaultPath,
        Func<string, string?>? readVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);
        var configured = Resolve(suffix, readVariable);
        var value = configured.IsConfigured ? configured.Value! : defaultPath;
        return new BrandPathResolution(
            Environment.ExpandEnvironmentVariables(value),
            configured.SourceVariable,
            configured.UsedLegacyFallback);
    }

    public static string ResolveLegacyLocalRoot(
        Func<string, string?>? readVariable = null,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        getFolderPath ??= Environment.GetFolderPath;

        var configured = NormalizeValue(readVariable(BrandIdentity.LegacyEnvironmentVariable(LocalRootSuffix)));
        return Environment.ExpandEnvironmentVariables(
            configured ?? Path.Combine(
                getFolderPath(Environment.SpecialFolder.LocalApplicationData),
                BrandIdentity.LegacyLocalDataDirectoryName));
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record BrandEnvironmentResolution(
    string? Value,
    string? SourceVariable,
    bool UsedLegacyFallback)
{
    public static BrandEnvironmentResolution Unconfigured { get; } = new(null, null, false);

    public bool IsConfigured => Value is not null;
}

public sealed record BrandPathResolution(
    string Value,
    string? SourceVariable,
    bool UsedLegacyFallback)
{
    public bool IsConfigured => SourceVariable is not null;
}

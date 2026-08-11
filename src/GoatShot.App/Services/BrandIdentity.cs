namespace GoatShot.App.Services;

/// <summary>
/// Central product identity for the Receipts rebrand. Internal assembly and namespace names
/// intentionally remain GoatShot during the compatibility window.
/// </summary>
public static class BrandIdentity
{
    public const string ProductName = "Receipts";
    public const string LegacyProductName = "GoatShot";
    public const string RepositoryName = "witness";
    public const string ReleaseVersion = "0.3.0";

    public const string EnvironmentVariablePrefix = "RECEIPTS_";
    public const string LegacyEnvironmentVariablePrefix = "GOATSHOT_";

    public const string LocalDataDirectoryName = ProductName;
    public const string LegacyLocalDataDirectoryName = LegacyProductName;
    public const string LibraryDirectoryName = ProductName;
    public const string LegacyLibraryDirectoryName = LegacyProductName;

    public const string DesktopExecutableName = "Receipts.exe";
    public const string CommandLineExecutableName = "Receipts.Cli.exe";
    public const string NativeMessagingHostName = "com.receipts.bridge";
    public const string LegacyNativeMessagingHostName = "com.goatshot.bridge";

    public const string LocalStateMigrationSchema = "receipts.local-state-migration.v1";
    public const string LocalStateMigrationMarkerFileName = ".goatshot-to-receipts-migration-v1.json";

    public static string EnvironmentVariable(string suffix)
    {
        return EnvironmentVariablePrefix + NormalizeEnvironmentVariableSuffix(suffix);
    }

    public static string LegacyEnvironmentVariable(string suffix)
    {
        return LegacyEnvironmentVariablePrefix + NormalizeEnvironmentVariableSuffix(suffix);
    }

    public static string RenderCliHelpTemplate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template
            .Replace("goatshot ", "receipts ", StringComparison.Ordinal)
            .Replace("/GoatShot", "/Receipts", StringComparison.Ordinal)
            .Replace("bug,goatshot", "bug,receipts", StringComparison.Ordinal)
            .Replace(".goatshot-workflow.json", ".receipts-workflow.json", StringComparison.Ordinal)
            .Replace("goatshot-browser-extension", "receipts-browser-extension", StringComparison.Ordinal)
            .Replace("goatshot@example.invalid", "receipts@example.invalid", StringComparison.Ordinal)
            .Replace("goatshot.xpi", "receipts.xpi", StringComparison.Ordinal)
            .Replace("goatshot-diagnostics.zip", "receipts-diagnostics.zip", StringComparison.Ordinal)
            .Replace("GoatShot.Cli.exe", CommandLineExecutableName, StringComparison.Ordinal)
            .Replace("GoatShot.exe", DesktopExecutableName, StringComparison.Ordinal)
            .Replace(
                "GoatShot-0.1.0-win-x64-portable.zip",
                $"Receipts-{ReleaseVersion}-win-x64-portable.zip",
                StringComparison.Ordinal);
    }

    private static string NormalizeEnvironmentVariableSuffix(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        var normalized = suffix.Trim().ToUpperInvariant();
        if (normalized.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException("An environment-variable suffix cannot contain '='.", nameof(suffix));
        }

        return normalized;
    }
}

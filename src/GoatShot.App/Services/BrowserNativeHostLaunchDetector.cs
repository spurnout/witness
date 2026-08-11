namespace GoatShot.App.Services;

internal static class BrowserNativeHostLaunchDetector
{
    private const string RuntimeVerb = "--browser-native-host";

    public static string[] Resolve(IReadOnlyList<string> args, bool standardInputIsRedirected)
    {
        ArgumentNullException.ThrowIfNull(args);

        // WinExe processes can report an unavailable standard-input handle as redirected.
        // A bare launch must therefore remain interactive. Supported browsers identify a
        // native-messaging launch with the calling extension origin on the command line.
        if (standardInputIsRedirected && args.Any(IsExtensionOrigin))
        {
            return [RuntimeVerb];
        }

        return args.ToArray();
    }

    private static bool IsExtensionOrigin(string? argument)
    {
        return argument?.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) == true ||
               argument?.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) == true;
    }
}

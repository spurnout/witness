namespace GoatShot.App.Services;

internal static class StartupTrace
{
    public static void Write(string message)
    {
        var path = Environment.GetEnvironmentVariable("GOATSHOT_STARTUP_TRACE");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.AppendAllText(fullPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Tracing must never change startup behavior.
        }
    }
}

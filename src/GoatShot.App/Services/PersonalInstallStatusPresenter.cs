namespace GoatShot.App.Services;

public enum InstallStatusTone
{
    Neutral,
    Ok,
    Attention
}

public sealed record InstallStatusRow(string Label, string Value, InstallStatusTone Tone);

/// <summary>
/// Turns the installation and runtime facts into one labelled row each. This used to be a single
/// run-on paragraph holding eight unrelated facts, which made the first block a new user meets in
/// Settings the densest text in the app.
/// </summary>
public static class PersonalInstallStatusPresenter
{
    private const int MaxPathLength = 64;

    public static IReadOnlyList<InstallStatusRow> Build(
        PersonalInstallState install,
        bool updateAvailable,
        bool runtimeReady,
        string runtimeMessage,
        string? ffmpegVersion,
        bool segmentationReady,
        string segmentationMessage,
        string transcriptionProvider,
        string whisperState)
    {
        var isInstalled = !string.IsNullOrWhiteSpace(install.InstalledVersion);

        return
        [
            new InstallStatusRow(
                "This copy",
                $"{install.CurrentVersion} ({install.BuildId})",
                InstallStatusTone.Neutral),
            new InstallStatusRow(
                "Installed",
                isInstalled ? $"{install.InstalledVersion} in {Shorten(install.InstalledPath)}" : "Not installed",
                isInstalled ? InstallStatusTone.Ok : InstallStatusTone.Attention),
            new InstallStatusRow(
                "Running",
                install.RunningInstalledCopy ? "The installed copy" : "A downloaded or development copy",
                install.RunningInstalledCopy ? InstallStatusTone.Ok : InstallStatusTone.Neutral),
            new InstallStatusRow(
                "Update",
                !isInstalled
                    ? "Nothing installed to update"
                    : updateAvailable ? "A newer version is ready to install" : "Up to date",
                // Nothing installed means nothing to update, so this must not read as a problem.
                !isInstalled ? InstallStatusTone.Neutral
                    : updateAvailable ? InstallStatusTone.Attention : InstallStatusTone.Ok),
            new InstallStatusRow(
                "Startup",
                install.StartupRegistered && install.StartupCommandCurrent
                    ? "Starts to the tray when you sign in"
                    : install.StartupRegistered ? "Registered, but the command is stale - run repair" : "Off",
                install.StartupRegistered && install.StartupCommandCurrent
                    ? InstallStatusTone.Ok
                    : install.StartupRegistered ? InstallStatusTone.Attention : InstallStatusTone.Neutral),
            new InstallStatusRow(
                "Bundled runtime",
                runtimeMessage,
                runtimeReady ? InstallStatusTone.Ok : InstallStatusTone.Attention),
            new InstallStatusRow(
                "FFmpeg",
                string.IsNullOrWhiteSpace(ffmpegVersion) ? "Resolved from PATH" : ffmpegVersion,
                string.IsNullOrWhiteSpace(ffmpegVersion) ? InstallStatusTone.Neutral : InstallStatusTone.Ok),
            new InstallStatusRow(
                "Person segmentation",
                segmentationMessage,
                segmentationReady ? InstallStatusTone.Ok : InstallStatusTone.Attention),
            new InstallStatusRow(
                "Transcription",
                $"{transcriptionProvider}; {whisperState}",
                InstallStatusTone.Neutral)
        ];
    }

    /// <summary>Keeps a long install path readable in a grid cell by eliding its middle.</summary>
    internal static string Shorten(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= MaxPathLength)
        {
            return path;
        }

        var name = Path.GetFileName(path);
        var head = path[..Math.Max(0, MaxPathLength - name.Length - 4)];
        return $"{head}...{Path.DirectorySeparatorChar}{name}";
    }
}

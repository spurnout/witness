using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace GoatShot.App.Services;

public sealed class PersonalInstallService
{
    public const string ProductVersion = "0.2.0";
    private const string InstalledRelativePath = @"Programs\GoatShot\GoatShot.exe";
    private readonly StartupRegistrationService _startup;
    private readonly string _localAppData;
    private readonly string _currentExecutable;

    public PersonalInstallService(
        StartupRegistrationService? startup = null,
        string? localAppData = null,
        string? currentExecutable = null)
    {
        _startup = startup ?? new StartupRegistrationService();
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _currentExecutable = Path.GetFullPath(currentExecutable ?? Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable."));
    }

    public string InstalledExecutablePath => Path.Combine(_localAppData, InstalledRelativePath);
    public string InstallDirectory => Path.GetDirectoryName(InstalledExecutablePath)!;
    public string PreviousExecutablePath => InstalledExecutablePath + ".previous";
    public string RuntimeRoot => Path.Combine(_localAppData, "GoatShot", "runtime");
    public bool IsRunningInstalledCopy => PathsEqual(_currentExecutable, InstalledExecutablePath);
    public bool IsDistributionBuild => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "GoatShotDistribution" && attribute.Value == "true") == true;

    public PersonalInstallState GetState()
    {
        var installedVersion = ReadFileVersion(InstalledExecutablePath);
        var currentVersion = ReadFileVersion(_currentExecutable) ?? ProductVersion;
        var startup = _startup.GetState(InstalledExecutablePath);
        return new PersonalInstallState(
            InstalledExecutablePath,
            currentVersion,
            installedVersion ?? string.Empty,
            BuildIdentity.Current,
            startup.IsRegistered,
            startup.IsCurrentCommand,
            File.Exists(PreviousExecutablePath),
            IsRunningInstalledCopy,
            File.Exists(InstalledExecutablePath) && startup.IsRegistered && startup.IsCurrentCommand);
    }

    public PersonalInstallResult InstallOrUpdate()
    {
        try
        {
            Directory.CreateDirectory(InstallDirectory);
            if (IsRunningInstalledCopy)
            {
                var startup = _startup.SetEnabled(true, InstalledExecutablePath);
                return new PersonalInstallResult(startup.Succeeded, InstalledExecutablePath, startup.Message, false);
            }

            var stagingPath = InstalledExecutablePath + ".new";
            File.Copy(_currentExecutable, stagingPath, overwrite: true);
            if (File.Exists(InstalledExecutablePath))
            {
                ReplaceInstalledExecutableWithRetry(stagingPath);
            }
            else
            {
                File.Move(stagingPath, InstalledExecutablePath);
            }

            var registration = _startup.SetEnabled(true, InstalledExecutablePath);
            if (!registration.Succeeded)
            {
                return new PersonalInstallResult(false, InstalledExecutablePath, registration.Message, false);
            }

            return new PersonalInstallResult(true, InstalledExecutablePath, "GoatShot was installed for the current Windows user and will start in the tray at sign-in.", true);
        }
        catch (Exception exception)
        {
            return new PersonalInstallResult(false, InstalledExecutablePath, $"GoatShot could not be installed: {exception.Message}", false);
        }
    }

    public PersonalInstallResult Repair()
    {
        if (!File.Exists(InstalledExecutablePath))
        {
            return InstallOrUpdate();
        }

        var registration = _startup.SetEnabled(true, InstalledExecutablePath);
        return new PersonalInstallResult(registration.Succeeded, InstalledExecutablePath, registration.Message, false);
    }

    public PersonalInstallResult DisableStartup()
    {
        var registration = _startup.SetEnabled(false, InstalledExecutablePath);
        return new PersonalInstallResult(registration.Succeeded, InstalledExecutablePath, registration.Message, false);
    }

    public PersonalInstallResult BeginUninstall()
    {
        try
        {
            _startup.SetEnabled(false, InstalledExecutablePath);
            RemoveOwnedScheduledJobs();
            var helperPath = Path.Combine(Path.GetTempPath(), $"GoatShot-uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(_currentExecutable, helperPath, overwrite: false);
            Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                Arguments = $"--complete-uninstall --wait-pid {Environment.ProcessId} --installed-directory \"{InstallDirectory}\" --runtime-directory \"{RuntimeRoot}\""
            });
            return new PersonalInstallResult(true, InstalledExecutablePath, "GoatShot uninstall will finish after the app exits. Captures and settings will be preserved.", false);
        }
        catch (Exception exception)
        {
            return new PersonalInstallResult(false, InstalledExecutablePath, $"Uninstall could not start: {exception.Message}", false);
        }
    }

    public static async Task<int> CompleteUninstallAsync(IReadOnlyList<string> args)
    {
        var processId = ReadIntArgument(args, "--wait-pid");
        var installedDirectory = ReadArgument(args, "--installed-directory");
        var runtimeDirectory = ReadArgument(args, "--runtime-directory");
        if (processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // The app already exited.
            }
            catch (TimeoutException)
            {
                return 2;
            }
        }

        DeleteDirectoryIfSafe(installedDirectory, "GoatShot");
        DeleteDirectoryIfSafe(runtimeDirectory, "runtime");
        var helper = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(helper))
        {
            MoveFileEx(helper, null, MoveFileDelayUntilReboot);
        }

        return 0;
    }

    public void MarkStartupSuccessful()
    {
        if (IsRunningInstalledCopy && File.Exists(PreviousExecutablePath))
        {
            File.Delete(PreviousExecutablePath);
        }
    }

    public bool IsNewerThanInstalled()
    {
        var installed = ReadFileVersion(InstalledExecutablePath);
        var current = ReadFileVersion(_currentExecutable) ?? ProductVersion;
        return string.IsNullOrWhiteSpace(installed) ||
            (Version.TryParse(current, out var currentVersion) &&
             Version.TryParse(installed, out var installedVersion) &&
             currentVersion > installedVersion);
    }

    private void ReplaceInstalledExecutableWithRetry(string stagingPath)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                File.Replace(stagingPath, InstalledExecutablePath, PreviousExecutablePath, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
                Thread.Sleep(250);
            }
        }

        throw new IOException("The running installed copy did not exit in time for an atomic update.", lastError);
    }

    private static void DeleteDirectoryIfSafe(string path, string expectedLeaf)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!Path.GetFileName(fullPath).Equals(expectedLeaf, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected directory: {fullPath}");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private void RemoveOwnedScheduledJobs()
    {
        var taskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GoatShot Plugin Background Updates"
        };
        var dataRoot = Path.Combine(_localAppData, "GoatShot");
        if (Directory.Exists(dataRoot))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(dataRoot, "plugin-update-schedule.json", SearchOption.AllDirectories))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    if (document.RootElement.TryGetProperty("taskName", out var property) && !string.IsNullOrWhiteSpace(property.GetString()))
                    {
                        taskNames.Add(property.GetString()!);
                    }
                }
                catch
                {
                    // A malformed historical handoff must not block uninstall.
                }
            }
        }

        foreach (var taskName in taskNames)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { "/Delete", "/TN", taskName, "/F" }
                });
                process?.WaitForExit(5000);
            }
            catch
            {
                // Missing tasks and disabled Task Scheduler are safe uninstall outcomes.
            }
        }
    }

    private static string ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }

    private static int ReadIntArgument(IReadOnlyList<string> args, string name) =>
        int.TryParse(ReadArgument(args, name), out var parsed) ? parsed : 0;

    private static string? ReadFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0];
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private const int MoveFileDelayUntilReboot = 0x4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}

public sealed record PersonalInstallState(
    string InstalledPath,
    string CurrentVersion,
    string InstalledVersion,
    string BuildId,
    bool StartupRegistered,
    bool StartupCommandCurrent,
    bool RollbackAvailable,
    bool RunningInstalledCopy,
    bool RepairHealthy);

public sealed record PersonalInstallResult(
    bool Succeeded,
    string InstalledPath,
    string Message,
    bool ShouldLaunchInstalledCopy);

public static class BuildIdentity
{
    public static string Current => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "GoatShotBuildId")?.Value ?? "local";
}

using GoatShot.App.Models;
using Microsoft.Win32;

namespace GoatShot.App.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GoatShot";

    public StartupRegistrationState GetState(string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new StartupRegistrationState
            {
                Message = "Startup registration is only available on Windows."
            };
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var command = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                return new StartupRegistrationState
                {
                    Message = "Startup registration is disabled."
                };
            }

            var expected = BuildStartupCommand(executablePath);
            var isCurrent = command.Equals(expected, StringComparison.OrdinalIgnoreCase);
            return new StartupRegistrationState
            {
                IsRegistered = true,
                IsCurrentCommand = isCurrent,
                Command = command,
                Message = isCurrent
                    ? "Startup registration is enabled for the current app path."
                    : "Startup registration exists but points to a different app path."
            };
        }
        catch (Exception ex)
        {
            return new StartupRegistrationState
            {
                Message = $"Startup registration status could not be read: {ex.Message}"
            };
        }
    }

    public StartupRegistrationResult SetEnabled(bool enabled, string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new StartupRegistrationResult
            {
                Succeeded = false,
                Message = "Startup registration is only available on Windows."
            };
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                return new StartupRegistrationResult
                {
                    Succeeded = false,
                    Message = "Could not open the current-user Windows Run key."
                };
            }

            if (enabled)
            {
                key.SetValue(ValueName, BuildStartupCommand(executablePath), RegistryValueKind.String);
                return new StartupRegistrationResult
                {
                    Succeeded = true,
                    Message = "GoatShot will run when the current Windows user signs in."
                };
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return new StartupRegistrationResult
            {
                Succeeded = true,
                Message = "GoatShot startup registration is disabled."
            };
        }
        catch (Exception ex)
        {
            return new StartupRegistrationResult
            {
                Succeeded = false,
                Message = $"Startup registration could not be updated: {ex.Message}"
            };
        }
    }

    internal static string BuildStartupCommand(string? executablePath = null)
    {
        var executable = executablePath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = Environment.ProcessPath;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("The current app executable path could not be resolved.");
        }

        return $"\"{Path.GetFullPath(executable)}\" --background";
    }
}

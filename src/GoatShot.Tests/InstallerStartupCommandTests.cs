using System.Text.RegularExpressions;
using GoatShot.App.Services;

namespace GoatShot.Tests;

/// <summary>
/// The Inno Setup script and <see cref="StartupRegistrationService"/> both write the same HKCU Run
/// value. They drifted once: the installer omitted --background, so every upgrade silently replaced
/// the app's tray-only startup with one that opens a visible window, and GetState then reported the
/// command as stale. These tests pin the two representations together.
/// </summary>
[TestClass]
public sealed class InstallerStartupCommandTests
{
    private const string RunKeyMarker = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ReadInstallerScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "packaging", "GoatShot.iss");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate packaging/GoatShot.iss from the test output directory.");
    }

    /// <summary>Pulls the ValueData for the Receipts Run entry out of the [Registry] section.</summary>
    private static string ReadInstallerStartupValueData()
    {
        var line = ReadInstallerScript()
            .Split('\n')
            .Select(value => value.Trim())
            .FirstOrDefault(value =>
                !value.StartsWith(';') &&
                value.Contains(RunKeyMarker, StringComparison.Ordinal) &&
                value.Contains("\"Receipts\"", StringComparison.Ordinal));

        Assert.IsNotNull(line, "The installer no longer writes a Receipts Run value.");

        var match = Regex.Match(line, @"ValueData:\s*(?<data>.+?);\s*Flags:");
        Assert.IsTrue(match.Success, $"Could not parse ValueData from: {line}");
        return match.Groups["data"].Value.Trim();
    }

    [TestMethod]
    public void InstallerRunValue_MatchesTheCommandTheAppWrites()
    {
        // Inno doubles embedded quotes, so """{app}\Receipts.exe"" --background" is the literal
        // "<path>" --background once expanded.
        var valueData = ReadInstallerStartupValueData();
        var expanded = UnescapeInnoString(valueData).Replace("{app}", @"C:\Install", StringComparison.Ordinal);
        var appCommand = StartupRegistrationService.BuildStartupCommand(@"C:\Install\Receipts.exe");

        Assert.AreEqual(
            appCommand,
            expanded,
            "packaging/GoatShot.iss and StartupRegistrationService.BuildStartupCommand must agree, " +
            "or an installer upgrade silently rewrites the user's startup entry.");
    }

    [TestMethod]
    public void InstallerRunValue_KeepsTheBackgroundArgumentSoStartupStaysTrayOnly()
    {
        StringAssert.Contains(
            ReadInstallerStartupValueData(),
            "--background",
            "Without --background the app starts with a visible window at sign-in.");
    }

    [TestMethod]
    public void BuildStartupCommand_QuotesThePathAndRequestsBackgroundLaunch()
    {
        Assert.AreEqual(
            "\"C:\\Install\\Receipts.exe\" --background",
            StartupRegistrationService.BuildStartupCommand(@"C:\Install\Receipts.exe"));
    }

    /// <summary>Turns an Inno double-quoted literal into the string Windows actually stores.</summary>
    private static string UnescapeInnoString(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.Replace("\"\"", "\"", StringComparison.Ordinal);
    }
}

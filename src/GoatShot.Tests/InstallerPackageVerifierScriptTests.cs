using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GoatShot.Tests;

[TestClass]
public sealed class InstallerPackageVerifierScriptTests
{
    [TestMethod]
    public void VerifyInstallerPackage_CurrentInnoScriptPassesStaticValidation()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "verify-installer-package.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-installer-verify-test-" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var result = RunPowerShell(
                repoRoot,
                scriptPath,
                outputRoot,
                installerScript: Path.Combine(repoRoot, "packaging", "GoatShot.iss"));

            Assert.AreEqual(0, result.ExitCode, result.Output);

            var jsonPath = Path.Combine(outputRoot, "installer-package-verification.json");
            Assert.IsTrue(File.Exists(jsonPath), "JSON verification artifact was not created.");
            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = json.RootElement;

            Assert.IsTrue(root.GetProperty("succeeded").GetBoolean());
            Assert.AreEqual(0, root.GetProperty("issues").GetArrayLength());
            Assert.AreEqual("Receipts-0.3.0-win-x64.exe", Path.GetFileName(root.GetProperty("installerPath").GetString()));
            Assert.IsTrue(root.GetProperty("scriptChecks").EnumerateArray().All(check => check.GetProperty("passed").GetBoolean()));
            Assert.AreEqual("skipped", root.GetProperty("installSmoke").GetProperty("status").GetString());
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, "installer-package-verification.md")));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    [TestMethod]
    public void VerifyInstallerPackage_InvalidInnoScriptFailsStaticValidation()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "verify-installer-package.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-installer-verify-test-" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(tempRoot, "out");
        var invalidScript = Path.Combine(tempRoot, "Broken.iss");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(
            invalidScript,
            """
            [Setup]
            AppName=NotGoatShot
            DefaultDirName={pf}\NotGoatShot
            """);

        try
        {
            var result = RunPowerShell(repoRoot, scriptPath, outputRoot, invalidScript);

            Assert.AreEqual(1, result.ExitCode, result.Output);

            var jsonPath = Path.Combine(outputRoot, "installer-package-verification.json");
            Assert.IsTrue(File.Exists(jsonPath), "JSON verification artifact was not created.");
            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = json.RootElement;

            Assert.IsFalse(root.GetProperty("succeeded").GetBoolean());
            Assert.IsTrue(root.GetProperty("issues").GetArrayLength() > 0);
            Assert.IsTrue(
                root.GetProperty("issues")
                    .EnumerateArray()
                    .Any(issue => issue.GetString()!.Contains("Installer script check failed", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    [TestMethod]
    public void VerifyInstallerPackage_HashesInstallerUnderPwshModulePath()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "verify-installer-package.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-installer-verify-test-" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(tempRoot, "out");
        var installerPath = Path.Combine(tempRoot, "GoatShot-Setup-0.1.0-win-x64.exe");
        Directory.CreateDirectory(tempRoot);

        var installerBytes = new byte[2048];
        for (var i = 0; i < installerBytes.Length; i++)
        {
            installerBytes[i] = (byte)(i % 251);
        }

        File.WriteAllBytes(installerPath, installerBytes);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(installerBytes));

        try
        {
            var result = RunPowerShell(
                repoRoot,
                scriptPath,
                outputRoot,
                installerScript: Path.Combine(repoRoot, "packaging", "GoatShot.iss"),
                installerPath: installerPath,
                psModulePath: PwshOnlyModulePath());

            Assert.AreEqual(0, result.ExitCode, result.Output);

            var jsonPath = Path.Combine(outputRoot, "installer-package-verification.json");
            Assert.IsTrue(File.Exists(jsonPath), "JSON verification artifact was not created.");
            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var installer = json.RootElement.GetProperty("installer");

            Assert.IsTrue(installer.GetProperty("exists").GetBoolean());
            Assert.AreEqual((long)installerBytes.Length, installer.GetProperty("length").GetInt64());
            Assert.AreEqual(expectedSha256, installer.GetProperty("sha256").GetString());
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    private static string PwshOnlyModulePath()
    {
        // Mimics powershell.exe inheriting a pwsh 7 PSModulePath: the pwsh module
        // directories shadow the Windows PowerShell ones, so 5.1 script functions
        // like Get-FileHash stop resolving while compiled cmdlets keep working.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.Join(
            Path.PathSeparator,
            Path.Combine(documents, "PowerShell", "Modules"),
            Path.Combine(programFiles, "PowerShell", "Modules"),
            Path.Combine(programFiles, "PowerShell", "7", "Modules"));
    }

    private static (int ExitCode, string Output) RunPowerShell(
        string repoRoot,
        string scriptPath,
        string outputRoot,
        string installerScript,
        string? installerPath = null,
        string? psModulePath = null)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        processInfo.ArgumentList.Add("-NoProfile");
        processInfo.ArgumentList.Add("-ExecutionPolicy");
        processInfo.ArgumentList.Add("Bypass");
        processInfo.ArgumentList.Add("-File");
        processInfo.ArgumentList.Add(scriptPath);
        processInfo.ArgumentList.Add("-InstallerScript");
        processInfo.ArgumentList.Add(installerScript);
        processInfo.ArgumentList.Add("-OutputRoot");
        processInfo.ArgumentList.Add(outputRoot);
        processInfo.ArgumentList.Add("-Json");

        if (installerPath is not null)
        {
            processInfo.ArgumentList.Add("-InstallerPath");
            processInfo.ArgumentList.Add(installerPath);
        }

        if (psModulePath is not null)
        {
            processInfo.Environment["PSModulePath"] = psModulePath;
        }

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + Environment.NewLine + stderr);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GoatShot.slnx from the test output directory.");
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

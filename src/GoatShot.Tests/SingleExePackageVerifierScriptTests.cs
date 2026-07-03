using System.Diagnostics;
using System.Text.Json;

namespace GoatShot.Tests;

[TestClass]
public sealed class SingleExePackageVerifierScriptTests
{
    [TestMethod]
    public void VerifySingleExePackage_CleanLayoutPasses()
    {
        var repoRoot = FindRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-single-exe-verify-test-" + Guid.NewGuid().ToString("N"));
        var distDir = Path.Combine(tempRoot, "dist");
        var publishDir = Path.Combine(tempRoot, "publish");
        var outputRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(tempRoot);

        try
        {
            CreateSingleExeLayout(distDir, publishDir, includeLooseNativeLibrary: false);

            var result = RunPowerShell(repoRoot, distDir, publishDir, outputRoot);

            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "single-exe-package-verification.json")));
            var root = json.RootElement;
            Assert.IsTrue(root.GetProperty("succeeded").GetBoolean());
            Assert.AreEqual(0, root.GetProperty("issues").GetArrayLength());
            Assert.AreEqual(0, root.GetProperty("looseNativeLibraries").GetArrayLength());
            Assert.AreEqual(0, root.GetProperty("unexpectedDistEntries").GetArrayLength());
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, "single-exe-package-verification.md")));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    [TestMethod]
    public void VerifySingleExePackage_FailsWhenNativeLibrariesWereNotEmbedded()
    {
        var repoRoot = FindRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-single-exe-verify-test-" + Guid.NewGuid().ToString("N"));
        var distDir = Path.Combine(tempRoot, "dist");
        var publishDir = Path.Combine(tempRoot, "publish");
        var outputRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(tempRoot);

        try
        {
            CreateSingleExeLayout(distDir, publishDir, includeLooseNativeLibrary: true);

            var result = RunPowerShell(repoRoot, distDir, publishDir, outputRoot);

            Assert.AreEqual(1, result.ExitCode, result.Output);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "single-exe-package-verification.json")));
            var root = json.RootElement;
            Assert.IsFalse(root.GetProperty("succeeded").GetBoolean());
            Assert.IsTrue(root.GetProperty("looseNativeLibraries").GetArrayLength() > 0);
            Assert.IsTrue(
                root.GetProperty("issues")
                    .EnumerateArray()
                    .Any(issue => issue.GetString()!.Contains("IncludeNativeLibrariesForSelfExtract", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    private static void CreateSingleExeLayout(string distDir, string publishDir, bool includeLooseNativeLibrary)
    {
        Directory.CreateDirectory(distDir);
        Directory.CreateDirectory(publishDir);

        var exeBytes = new byte[4096];
        File.WriteAllBytes(Path.Combine(distDir, "GoatShot.exe"), exeBytes);
        File.WriteAllBytes(Path.Combine(publishDir, "GoatShot.exe"), exeBytes);
        File.WriteAllText(Path.Combine(distDir, "GoatShot.pdb"), "x");
        File.WriteAllText(Path.Combine(publishDir, "GoatShot.pdb"), "x");

        if (includeLooseNativeLibrary)
        {
            File.WriteAllText(Path.Combine(publishDir, "wpfgfx_cor3.dll"), "x");
        }
    }

    private static (int ExitCode, string Output) RunPowerShell(
        string repoRoot,
        string distDir,
        string publishDir,
        string outputRoot)
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
        processInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "verify-single-exe-package.ps1"));
        processInfo.ArgumentList.Add("-DistDir");
        processInfo.ArgumentList.Add(distDir);
        processInfo.ArgumentList.Add("-PublishDir");
        processInfo.ArgumentList.Add(publishDir);
        processInfo.ArgumentList.Add("-OutputRoot");
        processInfo.ArgumentList.Add(outputRoot);
        processInfo.ArgumentList.Add("-MinimumExeBytes");
        processInfo.ArgumentList.Add("1024");
        processInfo.ArgumentList.Add("-Json");

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

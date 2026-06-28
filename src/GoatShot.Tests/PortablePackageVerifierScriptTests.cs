using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace GoatShot.Tests;

[TestClass]
public sealed class PortablePackageVerifierScriptTests
{
    [TestMethod]
    public void VerifyPortablePackage_CleanPackagePassesWithoutCliSmoke()
    {
        var repoRoot = FindRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-portable-verify-test-" + Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(tempRoot, "clean.zip");
        var outputRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(tempRoot);

        try
        {
            CreatePackage(packagePath, includeForbiddenRuntimeState: false);

            var result = RunPowerShell(repoRoot, packagePath, outputRoot);

            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "portable-package-verification.json")));
            var root = json.RootElement;
            Assert.IsTrue(root.GetProperty("succeeded").GetBoolean());
            Assert.AreEqual(0, root.GetProperty("issues").GetArrayLength());
            Assert.AreEqual(0, root.GetProperty("forbiddenMatches").GetArrayLength());
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, "portable-package-verification.md")));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    [TestMethod]
    public void VerifyPortablePackage_FailsWhenRuntimeStateIsBundled()
    {
        var repoRoot = FindRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "goatshot-portable-verify-test-" + Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(tempRoot, "dirty.zip");
        var outputRoot = Path.Combine(tempRoot, "out");
        Directory.CreateDirectory(tempRoot);

        try
        {
            CreatePackage(packagePath, includeForbiddenRuntimeState: true);

            var result = RunPowerShell(repoRoot, packagePath, outputRoot);

            Assert.AreEqual(1, result.ExitCode, result.Output);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "portable-package-verification.json")));
            var root = json.RootElement;
            Assert.IsFalse(root.GetProperty("succeeded").GetBoolean());
            Assert.IsTrue(root.GetProperty("forbiddenMatches").GetArrayLength() > 0);
            Assert.IsTrue(
                root.GetProperty("issues")
                    .EnumerateArray()
                    .Any(issue => issue.GetString()!.Contains("Forbidden runtime/proof entries", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteIfExists(tempRoot);
        }
    }

    private static void CreatePackage(string packagePath, bool includeForbiddenRuntimeState)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var required in RequiredEntries())
        {
            var entry = archive.CreateEntry(required);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(required.EndsWith("README.md", StringComparison.OrdinalIgnoreCase)
                ? "GoatShot portable package publication-plan documentation."
                : "x");
        }

        if (includeForbiddenRuntimeState)
        {
            var settings = archive.CreateEntry("settings.json");
            using var writer = new StreamWriter(settings.Open());
            writer.Write("{\"secret\":\"should-not-ship\"}");
        }
    }

    private static IReadOnlyList<string> RequiredEntries() => new[]
    {
        "GoatShot.exe",
        "GoatShot.dll",
        "GoatShot.Cli.exe",
        "GoatShot.Cli.dll",
        "README.md",
        "browser-extension/README.md",
        "browser-extension/bridge-contract.md",
        "browser-extension/manifest.json",
        "browser-extension/content-script.js",
        "browser-extension/service-worker.js",
        "browser-extension/popup.html",
        "browser-extension/popup.js",
        "browser-extension/options.html",
        "browser-extension/options.js",
        "browser-extension/samples/safe-fixture.html"
    };

    private static (int ExitCode, string Output) RunPowerShell(
        string repoRoot,
        string packagePath,
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
        processInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "verify-portable-package.ps1"));
        processInfo.ArgumentList.Add("-PackagePath");
        processInfo.ArgumentList.Add(packagePath);
        processInfo.ArgumentList.Add("-OutputRoot");
        processInfo.ArgumentList.Add(outputRoot);
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

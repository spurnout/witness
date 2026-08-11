using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReleaseProofBundleScriptTests
{
    [TestMethod]
    public void CreateReleaseProofBundle_SkipCommandsCreatesRedactedManifestAndZip()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "create-release-proof-bundle.ps1");
        Assert.IsTrue(File.Exists(scriptPath), $"Script missing: {scriptPath}");

        var tempRoot = Path.Combine(Path.GetTempPath(), "receipts-release-proof-test-" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(tempRoot, "proof");
        var secretLog = Path.Combine(tempRoot, "secret-log.txt");
        var mediaPayload = Path.Combine(tempRoot, "private-recording.mp4");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(
            secretLog,
            "Authorization: Bearer abc123 access_token=secret-token https://upload.example.test/session?token=abc&safe=1");
        File.WriteAllBytes(mediaPayload, [1, 2, 3, 4]);

        try
        {
            var result = RunPowerShell(
                repoRoot,
                scriptPath,
                outputRoot,
                secretLog,
                mediaPayload);

            Assert.AreEqual(0, result.ExitCode, result.Output);

            var manifestPath = Path.Combine(outputRoot, "manifest.json");
            Assert.IsTrue(File.Exists(manifestPath), "Manifest was not created.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;

            Assert.AreEqual(1, root.GetProperty("manifestVersion").GetInt32());
            Assert.AreEqual("Receipts", root.GetProperty("application").GetString());
            Assert.AreEqual("0.3.0", root.GetProperty("version").GetString());
            var publishedZipPath = root.GetProperty("zipPath").GetString()!;
            StringAssert.StartsWith(publishedZipPath, "Receipts-release-proof-0.3.0-");
            Assert.IsTrue(publishedZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            var localZipPath = Path.IsPathRooted(publishedZipPath)
                ? publishedZipPath
                : Path.Combine(outputRoot, publishedZipPath);
            Assert.IsTrue(File.Exists(localZipPath));
            Assert.AreEqual(".", root.GetProperty("repoRoot").GetString());
            Assert.IsFalse(File.ReadAllText(manifestPath).Contains(repoRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(File.ReadAllText(manifestPath).Contains(tempRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(
                root.GetProperty("unverifiedLanes")
                    .EnumerateArray()
                    .Any(item => item.GetString()!.Contains("OAuth", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(
                root.GetProperty("commands")
                    .EnumerateArray()
                    .All(command => command.GetProperty("status").GetString() == "skipped"));

            var redactedLogPath = Path.Combine(outputRoot, "bundle-content", "additional-artifacts", "secret-log.txt");
            var redacted = File.ReadAllText(redactedLogPath);
            Assert.IsFalse(redacted.Contains("abc123", StringComparison.Ordinal));
            Assert.IsFalse(redacted.Contains("secret-token", StringComparison.Ordinal));
            StringAssert.Contains(redacted, "Authorization: Bearer [REDACTED]");
            StringAssert.Contains(redacted, "https://upload.example.test/session?token=[REDACTED]&safe=1");

            var excluded = root.GetProperty("excludedByPolicy")
                .EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToList();
            Assert.IsTrue(excluded.Any(item => item.Contains("media payloads are excluded", StringComparison.OrdinalIgnoreCase)));

            using var zip = ZipFile.OpenRead(localZipPath);
            Assert.IsTrue(ZipContainsEntry(zip, "manifest.json"));
            Assert.IsTrue(ZipContainsEntry(zip, "additional-artifacts/secret-log.txt"));
            Assert.IsFalse(ZipContainsEntry(zip, "additional-artifacts/private-recording.mp4"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ReleaseAutomation_UsesCurrentReceiptsArtifactContract()
    {
        var repoRoot = FindRepoRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "personal-release.yml"));
        var proofScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "create-release-proof-bundle.ps1"));

        StringAssert.Contains(workflow, "default: 0.3.0");
        StringAssert.Contains(workflow, "Receipts-${{ steps.version.outputs.value }}-win-x64-portable.zip");
        StringAssert.Contains(workflow, "Receipts-${{ steps.version.outputs.value }}-win-x64-single-exe.exe");
        StringAssert.Contains(workflow, "Receipts-${{ steps.version.outputs.value }}-win-x64.exe");
        StringAssert.Contains(workflow, "Receipts-release-proof-*.zip");
        StringAssert.Contains(workflow, "verify-portable-package.ps1");
        StringAssert.Contains(workflow, "verify-single-exe-package.ps1");
        StringAssert.Contains(workflow, "verify-installer-package.ps1");
        Assert.IsFalse(workflow.Contains("artifacts/dist/GoatShot-", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("artifacts\\dist\\GoatShot-", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("GoatShot-release-proof-", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("default: 0.2.0", StringComparison.Ordinal));

        StringAssert.Contains(proofScript, "[string] $Version = \"0.3.0\"");
        StringAssert.Contains(proofScript, "Receipts.Cli.exe");
        StringAssert.Contains(proofScript, "$env:RECEIPTS_LOCAL_ROOT");
        StringAssert.Contains(proofScript, "$env:RECEIPTS_LIBRARY_ROOT");
        StringAssert.Contains(proofScript, "Receipts-$Version-$Runtime-single-exe.exe");
        StringAssert.Contains(proofScript, "application = \"Receipts\"");
        StringAssert.Contains(proofScript, "Receipts-release-proof-");
        Assert.IsFalse(proofScript.Contains("GoatShot.Cli.exe", StringComparison.Ordinal));
        Assert.IsFalse(proofScript.Contains("$env:GOATSHOT_LOCAL_ROOT", StringComparison.Ordinal));
        Assert.IsFalse(proofScript.Contains("$env:GOATSHOT_LIBRARY_ROOT", StringComparison.Ordinal));
        Assert.IsFalse(proofScript.Contains("GoatShot-release-proof-", StringComparison.Ordinal));
        Assert.IsFalse(proofScript.Contains("\"0.2.0\"", StringComparison.Ordinal));
    }

    private static bool ZipContainsEntry(ZipArchive zip, string normalizedPath)
    {
        return zip.Entries.Any(entry =>
            entry.FullName.Replace('\\', '/').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    private static (int ExitCode, string Output) RunPowerShell(
        string repoRoot,
        string scriptPath,
        string outputRoot,
        string secretLog,
        string mediaPayload)
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
        processInfo.ArgumentList.Add("-OutputRoot");
        processInfo.ArgumentList.Add(outputRoot);
        processInfo.ArgumentList.Add("-SkipCommands");
        processInfo.ArgumentList.Add("-SkipTrancheNotes");
        processInfo.ArgumentList.Add("-AdditionalArtifactPath");
        processInfo.ArgumentList.Add(secretLog + "," + mediaPayload);

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
}

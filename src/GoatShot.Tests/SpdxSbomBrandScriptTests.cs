using System.Diagnostics;
using System.Text.Json;

namespace GoatShot.Tests;

[TestClass]
public sealed class SpdxSbomBrandScriptTests
{
    [TestMethod]
    public void CreateSpdxSbom_DefaultsToReceiptsVersionAndIdentity()
    {
        var repoRoot = FindRepoRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "receipts-sbom-brand-test-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempRoot, "Receipts-0.3.0-win-x64.spdx.json");
        Directory.CreateDirectory(tempRoot);

        try
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
            processInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "create-spdx-sbom.ps1"));
            processInfo.ArgumentList.Add("-OutputPath");
            processInfo.ArgumentList.Add(outputPath);
            processInfo.ArgumentList.Add("-EmbeddedManifestPath");
            processInfo.ArgumentList.Add(Path.Combine(tempRoot, "not-present.json"));

            using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, stdout + Environment.NewLine + stderr);

            using var json = JsonDocument.Parse(File.ReadAllText(outputPath));
            var root = json.RootElement;
            Assert.AreEqual("Receipts-0.3.0-win-x64", root.GetProperty("name").GetString());
            StringAssert.StartsWith(root.GetProperty("documentNamespace").GetString(), "https://github.com/spurnout/witness/spdx/");
            CollectionAssert.AreEqual(
                new[] { "SPDXRef-Receipts" },
                root.GetProperty("documentDescribes").EnumerateArray().Select(item => item.GetString()).ToArray());
            Assert.IsTrue(root.GetProperty("creationInfo").GetProperty("creators").EnumerateArray()
                .Any(item => item.GetString() == "Tool: Receipts create-spdx-sbom.ps1"));
            Assert.IsTrue(root.GetProperty("packages").EnumerateArray()
                .Any(package => package.GetProperty("SPDXID").GetString() == "SPDXRef-Receipts" &&
                    package.GetProperty("name").GetString() == "Receipts" &&
                    package.GetProperty("versionInfo").GetString() == "0.3.0"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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

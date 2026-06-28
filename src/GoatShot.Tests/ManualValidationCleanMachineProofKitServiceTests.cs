using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationCleanMachineProofKitServiceTests
{
    [TestMethod]
    public async Task CreateAsync_GeneratesSelfContainedProofKitWhenCopyPackageRequested()
    {
        var root = CreateTempRoot();
        var repoRoot = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var dist = Path.Combine(repoRoot, "artifacts", "dist");
            Directory.CreateDirectory(dist);
            var portableZip = Path.Combine(dist, "GoatShot-0.1.0-win-x64-portable.zip");
            var installer = Path.Combine(dist, "GoatShot-Setup-0.1.0-win-x64.exe");
            await File.WriteAllBytesAsync(portableZip, Encoding.UTF8.GetBytes("portable package bytes"));
            await File.WriteAllBytesAsync(installer, Encoding.UTF8.GetBytes("installer bytes"));
            var expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(portableZip))).ToLowerInvariant();

            var result = await new ManualValidationCleanMachineProofKitService().CreateAsync(new ManualValidationCleanMachineProofKitRequest
            {
                RootPath = root,
                RepoRoot = repoRoot,
                CopyPackage = true,
                CliPath = @"C:\Tools\GoatShot.Cli.exe"
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.ReadyForCleanMachineRun);
            Assert.IsTrue(result.SelfContainedPackageCopy);
            Assert.AreEqual(expectedHash, result.PortablePackage.Sha256);
            Assert.IsTrue(File.Exists(result.RunbookPath));
            Assert.IsTrue(File.Exists(result.ManifestPath));
            Assert.IsTrue(File.Exists(result.ScriptPath));
            Assert.IsTrue(File.Exists(result.EvidenceChecklistPath));
            Assert.IsTrue(File.Exists(result.PortablePackage.CopiedPath));
            Assert.IsTrue(File.Exists(result.InstallerPackage.CopiedPath));

            var runbook = await File.ReadAllTextAsync(result.RunbookPath);
            StringAssert.Contains(runbook, "This kit does not perform or certify the clean-machine pass.");
            StringAssert.Contains(runbook, "manual-validation record-lane");
            StringAssert.Contains(runbook, "clean-machine-install");

            var script = await File.ReadAllTextAsync(result.ScriptPath);
            StringAssert.Contains(script, "GOATSHOT_LOCAL_ROOT");
            StringAssert.Contains(script, "Expand-Archive");
            StringAssert.Contains(script, "clean-machine-script-result.json");

            var checklist = await File.ReadAllTextAsync(result.EvidenceChecklistPath);
            StringAssert.Contains(checklist, "Clean Machine Evidence Checklist");
            StringAssert.Contains(checklist, expectedHash);

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath));
            Assert.IsTrue(manifest.RootElement.GetProperty("selfContainedPackageCopy").GetBoolean());
            Assert.AreEqual(expectedHash, manifest.RootElement.GetProperty("portablePackage").GetProperty("sha256").GetString());
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(repoRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_DefaultsToReferenceOnlyPackage()
    {
        var root = CreateTempRoot();
        var repoRoot = CreateTempRoot();
        try
        {
            await new ManualValidationHarnessService().CreateAsync(new ManualValidationHarnessRequest(OutputPath: root));
            var dist = Path.Combine(repoRoot, "artifacts", "dist");
            Directory.CreateDirectory(dist);
            var portableZip = Path.Combine(dist, "GoatShot-0.1.0-win-x64-portable.zip");
            await File.WriteAllBytesAsync(portableZip, Encoding.UTF8.GetBytes("portable package bytes"));

            var result = await new ManualValidationCleanMachineProofKitService().CreateAsync(new ManualValidationCleanMachineProofKitRequest
            {
                RootPath = root,
                RepoRoot = repoRoot
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.ReadyForCleanMachineRun);
            Assert.IsFalse(result.SelfContainedPackageCopy);
            Assert.AreEqual(portableZip, result.PortablePackage.SourcePath);
            Assert.AreEqual(string.Empty, result.PortablePackage.CopiedPath);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Installer artifact was not found", StringComparison.OrdinalIgnoreCase)));

            var runbook = await File.ReadAllTextAsync(result.RunbookPath);
            StringAssert.Contains(runbook, "Copy it into the VM or rerun with `--copy-package`.");

            var script = await File.ReadAllTextAsync(result.ScriptPath);
            StringAssert.Contains(script, "(-not [string]::IsNullOrWhiteSpace($defaultInstaller))");
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(repoRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_FailsForMissingManualValidationFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-missing-clean-machine-kit-" + Guid.NewGuid().ToString("N"));

        var result = await new ManualValidationCleanMachineProofKitService().CreateAsync(new ManualValidationCleanMachineProofKitRequest
        {
            RootPath = path
        });

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "was not found");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-clean-machine-proof-kit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ManualValidationHarnessServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WritesDatedEvidenceFolderAndTemplates()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var output = Path.Combine(tempRoot, "manual-validation");
            var service = new ManualValidationHarnessService();

            var result = await service.CreateAsync(new ManualValidationHarnessRequest(
                OutputPath: output,
                Date: new DateOnly(2026, 6, 15)));

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(output, result.RootPath);
            Assert.IsTrue(File.Exists(result.ReadmePath));
            Assert.IsTrue(File.Exists(result.SummaryPath));
            Assert.IsTrue(File.Exists(result.CommandsPath));
            Assert.IsTrue(File.Exists(result.DiagnosticsReadmePath));
            Assert.AreEqual(ManualValidationHarnessService.TemplateFileNames.Count, result.Templates.Count);

            foreach (var templateName in ManualValidationHarnessService.TemplateFileNames)
            {
                var path = Path.Combine(output, templateName);
                Assert.IsTrue(File.Exists(path), $"Template missing: {templateName}");
                var text = await File.ReadAllTextAsync(path);
                Assert.IsTrue(text.Contains("## Requirement", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("- Classification:", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("## Safety And Redaction", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("Safe demo content only", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("Provider account names", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("## Evidence", StringComparison.Ordinal), templateName);
                Assert.IsTrue(text.Contains("## Notes", StringComparison.Ordinal), templateName);
            }

            var commands = await File.ReadAllTextAsync(result.CommandsPath);
            StringAssert.Contains(commands, "diagnostics bundle");
            StringAssert.Contains(commands, "diagnostics providers");
            StringAssert.Contains(commands, "record devices");
            StringAssert.Contains(commands, "browser-extension diagnostics");
            StringAssert.Contains(commands, "diagnostics android");
            StringAssert.Contains(commands, "capture android-preview");

            var summary = await File.ReadAllTextAsync(result.SummaryPath);
            StringAssert.Contains(summary, "OAuth/live account proof remains parked");
            StringAssert.Contains(summary, "Not created yet.");
            StringAssert.Contains(summary, "Requirement");
            StringAssert.Contains(summary, "Browser Extension Live Fixture");
            StringAssert.Contains(summary, "Android Safe Device Proof");

            var browserFixture = await File.ReadAllTextAsync(Path.Combine(output, "12-browser-extension-live-fixture.md"));
            StringAssert.Contains(browserFixture, "Optional compatibility");
            StringAssert.Contains(browserFixture, "browser-extension/samples/safe-fixture.html");
            StringAssert.Contains(browserFixture, "Host Status");
            StringAssert.Contains(browserFixture, "Do not claim browser-store publication");

            var androidFixture = await File.ReadAllTextAsync(Path.Combine(output, "13-android-safe-device-proof.md"));
            StringAssert.Contains(androidFixture, "Hardware-gated");
            StringAssert.Contains(androidFixture, "staged safe phone content");
            StringAssert.Contains(androidFixture, "capture android-preview");
            StringAssert.Contains(androidFixture, "Do not claim production Android live streaming");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_PreservesExistingNotesUnlessForceIsUsed()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var output = Path.Combine(tempRoot, "manual-validation");
            var service = new ManualValidationHarnessService();
            await service.CreateAsync(new ManualValidationHarnessRequest(OutputPath: output));

            var keyboardTemplate = Path.Combine(output, "02-keyboard-traversal.md");
            await File.WriteAllTextAsync(keyboardTemplate, "operator notes stay here");

            var preserved = await service.CreateAsync(new ManualValidationHarnessRequest(OutputPath: output));
            Assert.IsTrue(preserved.Succeeded, preserved.Message);
            Assert.AreEqual("operator notes stay here", await File.ReadAllTextAsync(keyboardTemplate));
            Assert.IsFalse(preserved.Templates.Single(file => file.RelativePath == "02-keyboard-traversal.md").Overwritten);

            var forced = await service.CreateAsync(new ManualValidationHarnessRequest(OutputPath: output, Force: true));
            Assert.IsTrue(forced.Succeeded, forced.Message);
            Assert.IsTrue(
                (await File.ReadAllTextAsync(keyboardTemplate)).Contains("## Safety And Redaction", StringComparison.Ordinal),
                "Force should regenerate the template content.");
            Assert.IsTrue(forced.Templates.Single(file => file.RelativePath == "02-keyboard-traversal.md").Overwritten);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [TestMethod]
    public async Task CreateAsync_CanAttachDiagnosticsBundleThroughDelegate()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var output = Path.Combine(tempRoot, "manual-validation");
            var service = new ManualValidationHarnessService();

            var result = await service.CreateAsync(
                new ManualValidationHarnessRequest(
                    OutputPath: output,
                    IncludeDiagnosticsBundle: true),
                async (path, cancellationToken) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, "fake diagnostic bundle", cancellationToken);
                    return new DiagnosticBundleResult
                    {
                        Succeeded = true,
                        Path = path,
                        Message = "fake bundle created",
                        Entries = ["manifest.json", "settings.redacted.json"]
                    };
                });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.DiagnosticsBundlePath);
            Assert.IsTrue(File.Exists(result.DiagnosticsBundlePath));

            var summary = await File.ReadAllTextAsync(result.SummaryPath);
            StringAssert.Contains(summary, "`diagnostics/receipts-diagnostics.zip`");

            var diagnosticsReadme = await File.ReadAllTextAsync(result.DiagnosticsReadmePath);
            StringAssert.Contains(diagnosticsReadme, "Diagnostics bundle created");
            StringAssert.Contains(diagnosticsReadme, "receipts-diagnostics.zip");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-manual-validation-test-" + Guid.NewGuid().ToString("N"));
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

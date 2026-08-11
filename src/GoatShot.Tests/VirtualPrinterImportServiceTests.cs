using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class VirtualPrinterImportServiceTests
{
    [TestMethod]
    public async Task GetContract_UsesDefaultDropFolderAndSupportedExtensions()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                EnableVirtualPrinterImport = true
            };
            var service = CreateService(paths, settings);

            var contract = service.GetContract(ensureFolder: true);

            Assert.IsTrue(contract.Enabled);
            Assert.AreEqual(Path.Combine(paths.DocumentsRoot, "PrintDrop"), contract.DropFolder);
            Assert.IsTrue(Directory.Exists(contract.DropFolder));
            CollectionAssert.Contains(contract.SupportedExtensions, ".pdf");
            CollectionAssert.Contains(contract.SupportedExtensions, ".png");
            StringAssert.Contains(contract.DriverInstallStatus, "Not installed");
            StringAssert.Contains(contract.PrivacyNote, "private document content");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ImportAsync_ImportsPdfToDocumentsAsPrintedDocumentWithSourceMetadata()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            var service = CreateService(paths, settings);
            var source = Path.Combine(paths.TempRoot, "safe-print.pdf");
            await WriteSamplePdfAsync(source);

            var result = await service.ImportAsync(new VirtualPrinterImportRequest
            {
                Path = source,
                SourceApplication = "Microsoft Print to PDF",
                DocumentTitle = "Safe print fixture"
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.PrintedDocument, result.Item.Kind);
            Assert.AreEqual("Microsoft Print to PDF", result.Item.SourceApp);
            Assert.AreEqual("Safe print fixture", result.Item.SourceWindowTitle);
            Assert.AreEqual("Print/file-drop import", result.Item.SourceMonitorName);
            StringAssert.Contains(result.Item.Notes!, "virtual-printer/file-drop");
            StringAssert.Contains(result.Item.Notes!, "installer/admin-scoped");
            Assert.IsTrue(result.Item.FilePath.StartsWith(paths.DocumentsRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(result.Item.FilePath));
            Assert.AreEqual(".pdf", Path.GetExtension(result.Item.FilePath));
        });
    }

    [TestMethod]
    public async Task ImportAsync_ImportsImageToImagesAsPrintedImage()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            var service = CreateService(paths, settings);
            var source = Path.Combine(paths.TempRoot, "safe-print.png");
            WriteSamplePng(source);

            var result = await service.ImportAsync(new VirtualPrinterImportRequest
            {
                Path = source,
                SourceApplication = "Test image printer",
                DocumentTitle = "Image print fixture"
            });

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsNotNull(result.Item);
            Assert.AreEqual(CaptureKind.PrintedImage, result.Item.Kind);
            Assert.IsTrue(result.Item.FilePath.StartsWith(paths.ImagesRoot, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(12, result.Item.Width);
            Assert.AreEqual(8, result.Item.Height);
            Assert.IsTrue(File.Exists(result.Item.ThumbnailPath));
        });
    }

    [TestMethod]
    public async Task ImportAsync_RejectsUnsupportedExtension()
    {
        await WithTempPathsAsync(async paths =>
        {
            var settings = new AppSettings();
            var service = CreateService(paths, settings);
            var source = Path.Combine(paths.TempRoot, "not-a-print-output.txt");
            await File.WriteAllTextAsync(source, "plain text is not a supported print output");

            var result = await service.ImportAsync(new VirtualPrinterImportRequest
            {
                Path = source
            });

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "Unsupported print import extension");
            Assert.IsNull(result.Item);
        });
    }

    [TestMethod]
    public async Task BuildWatchedFolders_IncludesVirtualPrinterFolderWhenEnabled()
    {
        await WithTempPathsAsync(paths =>
        {
            var watch = Path.Combine(paths.TempRoot, "watch");
            var printDrop = Path.Combine(paths.TempRoot, "print-drop");
            var settings = new AppSettings
            {
                EnableWatchFolders = true,
                EnableVirtualPrinterImport = true,
                VirtualPrinterImportFolder = printDrop,
                WatchFolders = [watch, watch]
            };

            var folders = VirtualPrinterImportService.BuildWatchedFolders(settings, paths);

            Assert.AreEqual(2, folders.Count);
            CollectionAssert.Contains(folders.ToList(), Path.GetFullPath(watch));
            CollectionAssert.Contains(folders.ToList(), Path.GetFullPath(printDrop));
            Assert.IsTrue(VirtualPrinterImportService.IsInsideDropFolder(
                settings,
                paths,
                Path.Combine(printDrop, "nested", "safe-print.pdf")));
            Assert.IsFalse(VirtualPrinterImportService.IsInsideDropFolder(
                settings,
                paths,
                Path.Combine(paths.TempRoot, "other", "safe-print.pdf")));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ProcessWatchedFileAsync_ImportsPdfFromVirtualPrinterFolderWhenRegularWatchFoldersAreDisabled()
    {
        await WithTempServicesAsync(async services =>
        {
            var dropFolder = Path.Combine(services.Paths.TempRoot, "print-drop");
            Directory.CreateDirectory(dropFolder);
            services.Settings.EnableWatchFolders = false;
            services.Settings.WatchFolderAutoImport = false;
            services.Settings.EnableVirtualPrinterImport = true;
            services.Settings.VirtualPrinterImportFolder = dropFolder;

            var source = Path.Combine(dropFolder, "watched-safe-print.pdf");
            await WriteSamplePdfAsync(source);
            var imported = new List<CaptureItem>();
            services.Automation.CaptureImported += (_, item) => imported.Add(item);

            await services.Automation.ProcessWatchedFileAsync(source);

            Assert.AreEqual(1, imported.Count);
            Assert.AreEqual(CaptureKind.PrintedDocument, imported[0].Kind);
            Assert.AreEqual("virtual-printer-file-drop", imported[0].SourceApp);
            Assert.AreEqual("Print/file-drop import", imported[0].SourceMonitorName);
            Assert.IsTrue(imported[0].FilePath.StartsWith(services.Paths.DocumentsRoot, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(imported[0].FilePath));
        });
    }

    [TestMethod]
    public async Task WorkflowProfiles_RoundTripVirtualPrinterWatchFolderFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var exportSettings = new AppSettings
            {
                EnableVirtualPrinterImport = true,
                VirtualPrinterImportFolder = Path.Combine(root, "print-drop"),
                VirtualPrinterImportIncludeSubdirectories = true
            };
            var exportStore = new SettingsStore();
            exportStore.UsePath(Path.Combine(root, "export-settings.json"));
            var profilePath = Path.Combine(root, "workflow-profile.json");

            var exportResult = await new WorkflowProfileService(exportSettings, exportStore)
                .ExportAsync(profilePath, "Print import profile");

            Assert.IsTrue(exportResult.Succeeded);
            var profileJson = await File.ReadAllTextAsync(profilePath);
            StringAssert.Contains(profileJson, "enableVirtualPrinterImport");
            StringAssert.Contains(profileJson, "virtualPrinterImportFolder");

            var importSettings = new AppSettings();
            var importStore = new SettingsStore();
            importStore.UsePath(Path.Combine(root, "import-settings.json"));
            var importResult = await new WorkflowProfileService(importSettings, importStore)
                .ImportAsync(profilePath, new WorkflowProfileImportOptions { IncludeSensitiveValues = true });

            Assert.IsTrue(importResult.Succeeded, importResult.Message);
            Assert.IsTrue(importSettings.EnableVirtualPrinterImport);
            Assert.AreEqual(exportSettings.VirtualPrinterImportFolder, importSettings.VirtualPrinterImportFolder);
            Assert.IsTrue(importSettings.VirtualPrinterImportIncludeSubdirectories);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    [TestMethod]
    public async Task Diagnostics_ReportsVirtualPrinterImportStatus()
    {
        await WithTempServicesAsync(services =>
        {
            services.Settings.EnableVirtualPrinterImport = true;
            services.Settings.VirtualPrinterImportIncludeSubdirectories = true;
            services.Settings.VirtualPrinterImportFolder = Path.Combine(services.Paths.TempRoot, "print-drop");

            var snapshot = services.Diagnostics.GetSnapshot();

            StringAssert.Contains(snapshot.PrintImportStatus, "Virtual printer/file-drop import is enabled");
            StringAssert.Contains(snapshot.PrintImportStatus, services.Settings.VirtualPrinterImportFolder);
            StringAssert.Contains(snapshot.PrintImportStatus, ".pdf");
            StringAssert.Contains(snapshot.PrintImportStatus, "Watched folder state");
            StringAssert.Contains(snapshot.PrintImportStatus, "Policy status");
            StringAssert.Contains(snapshot.PrintImportStatus, "Driver install implemented: False");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task FeasibilityDiagnostics_ReportsDriverBoundaryAndDropFolderHealth()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                EnableVirtualPrinterImport = true,
                VirtualPrinterImportFolder = Path.Combine(paths.TempRoot, "print-drop")
            };
            var service = CreateService(paths, settings);

            var diagnostics = service.GetFeasibilityDiagnostics(ensureFolder: true);

            Assert.IsTrue(diagnostics.Enabled);
            Assert.IsTrue(diagnostics.PolicyAllowed);
            Assert.AreEqual("allowed", diagnostics.PolicyStatus);
            Assert.AreEqual("enabled-and-writable", diagnostics.WatchedFolderState);
            Assert.IsTrue(diagnostics.DropFolderExists);
            Assert.IsTrue(diagnostics.DropFolderWritable, diagnostics.DropFolderStatus);
            Assert.IsFalse(diagnostics.DriverInstallImplemented);
            Assert.IsTrue(diagnostics.DriverInstallRequiresAdmin);
            Assert.IsTrue(diagnostics.DriverSigningRequired);
            CollectionAssert.Contains(diagnostics.SupportedExtensions, ".pdf");
            Assert.IsTrue(diagnostics.DriverOptions.Any(option =>
                option.Name.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) &&
                !option.RequiresSignedDriver));
            Assert.IsTrue(diagnostics.DriverOptions.Any(option =>
                option.RequiresAdmin &&
                option.RequiresSignedDriver));
            Assert.IsTrue(diagnostics.NextSteps.Any(step =>
                step.Contains("Do not claim virtual-printer driver support", StringComparison.OrdinalIgnoreCase)));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Setup_CreatesDropFolderAndWritesSetupNote()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                EnableVirtualPrinterImport = true,
                VirtualPrinterImportIncludeSubdirectories = true,
                VirtualPrinterImportFolder = Path.Combine(paths.TempRoot, "print-drop")
            };
            var service = CreateService(paths, settings);

            var result = service.Setup(new VirtualPrinterSetupRequest());

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(result.SetupNoteWritten);
            Assert.IsTrue(Directory.Exists(settings.VirtualPrinterImportFolder));
            Assert.IsTrue(File.Exists(result.SetupNotePath));
            Assert.AreEqual("enabled-and-writable", result.WatchedFolderState);
            CollectionAssert.Contains(result.SupportedExtensions, ".pdf");
            CollectionAssert.Contains(result.SupportedExtensions, ".png");

            var note = File.ReadAllText(result.SetupNotePath);
            StringAssert.Contains(note, "Microsoft Print To PDF");
            StringAssert.Contains(note, "Supported file types");
            StringAssert.Contains(note, ".pdf");
            StringAssert.Contains(note, "does not install a Windows printer driver");
            StringAssert.Contains(note, "installer/admin-scoped");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Setup_ReportsManagedPolicyBlockWithoutWritingSetupNote()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                EnableVirtualPrinterImport = true,
                VirtualPrinterImportFolder = Path.Combine(paths.TempRoot, "print-drop"),
                ManagedPolicy = new ManagedPolicySettings
                {
                    DisableVirtualPrinterImport = true
                }
            };
            var service = CreateService(paths, settings);

            var result = service.Setup(new VirtualPrinterSetupRequest());

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.SetupNoteWritten);
            Assert.IsFalse(File.Exists(result.SetupNotePath));
            Assert.AreEqual("blocked-by-managed-policy", result.WatchedFolderState);
            Assert.IsNotNull(result.Diagnostics);
            Assert.IsFalse(result.Diagnostics!.PolicyAllowed);
            Assert.AreEqual("blocked-by-managed-policy", result.Diagnostics.PolicyStatus);
            StringAssert.Contains(result.Message, "managed policy");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("managed policy", StringComparison.OrdinalIgnoreCase)));
            return Task.CompletedTask;
        });
    }

    private static VirtualPrinterImportService CreateService(AppPaths paths, AppSettings settings)
    {
        var workspace = new WorkspaceStore(paths, settings);
        workspace.AttachMetadataIndex(new WorkspaceMetadataIndex(paths));
        return new VirtualPrinterImportService(paths, settings, workspace);
    }

    private static async Task WriteSamplePdfAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Count 0 >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static void WriteSamplePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(12, 8, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(48, 230, 195));
            graphics.FillRectangle(brush, 2, 2, 8, 4);
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            var settings = new AppSettings();
            var paths = AppPaths.Create(settings);

            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static async Task WithTempServicesAsync(Func<AppServices, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));

            using var services = AppServices.Create();
            await action(services);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }
}

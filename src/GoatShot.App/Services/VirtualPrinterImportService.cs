using GoatShot.App.Models;
using System.Drawing.Printing;
using System.Security.Principal;
using System.Text;

namespace GoatShot.App.Services;

public sealed class VirtualPrinterImportService
{
    public static readonly string[] SupportedExtensions =
    [
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp",
        ".tif",
        ".tiff"
    ];

    private static readonly HashSet<string> SupportedExtensionSet = new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly WorkspaceStore _workspaceStore;

    public VirtualPrinterImportService(AppPaths paths, AppSettings settings, WorkspaceStore workspaceStore)
    {
        _paths = paths;
        _settings = settings;
        _workspaceStore = workspaceStore;
    }

    public VirtualPrinterImportContract GetContract(bool ensureFolder = false)
    {
        var folder = GetDropFolder(_settings, _paths);
        if (ensureFolder)
        {
            Directory.CreateDirectory(folder);
        }

        return new VirtualPrinterImportContract
        {
            Enabled = _settings.EnableVirtualPrinterImport,
            DropFolder = folder,
            IncludeSubdirectories = _settings.VirtualPrinterImportIncludeSubdirectories,
            SupportedExtensions = SupportedExtensions.ToList()
        };
    }

    public VirtualPrinterFeasibilityDiagnostics GetFeasibilityDiagnostics(bool ensureFolder = false)
    {
        var contract = GetContract(ensureFolder);
        var folderHealth = ProbeDropFolder(contract.DropFolder, ensureFolder);
        var printerProbe = ProbeInstalledPrinters();
        var isElevated = IsCurrentProcessElevated();
        var policyAllowed = ManagedPolicyService.IsVirtualPrinterImportAllowed(_settings, out var policyReason);
        var diagnostics = new VirtualPrinterFeasibilityDiagnostics
        {
            Enabled = contract.Enabled,
            DropFolder = contract.DropFolder,
            IncludeSubdirectories = contract.IncludeSubdirectories,
            SupportedExtensions = contract.SupportedExtensions.ToList(),
            WatchedFolderState = BuildWatchedFolderState(contract.Enabled, folderHealth.Exists, folderHealth.Writable, policyAllowed),
            PolicyAllowed = policyAllowed,
            PolicyStatus = policyAllowed ? "allowed" : "blocked-by-managed-policy",
            PolicyReason = policyReason,
            DropFolderExists = folderHealth.Exists,
            DropFolderWritable = folderHealth.Writable,
            DropFolderStatus = folderHealth.Status,
            InstalledPrinters = printerProbe.Printers,
            InstalledPrinterProbeStatus = printerProbe.Status,
            MicrosoftPrintToPdfDetected = printerProbe.HasMicrosoftPrintToPdf,
            MicrosoftXpsWriterDetected = printerProbe.HasMicrosoftXpsWriter,
            CurrentProcessElevated = isElevated,
            DriverInstallImplemented = false,
            DriverInstallRequiresAdmin = true,
            DriverSigningRequired = true,
            InstallerHookImplemented = false,
            Summary = "Receipts can import print-to-file PDF/image outputs through the local file-drop path. A true Windows virtual-printer driver is not installed or shipped by Receipts in this build."
        };

        diagnostics.DriverOptions.AddRange(
        [
            new VirtualPrinterDriverOption
            {
                Name = "Existing Microsoft Print to PDF or app-native PDF/image export",
                Status = diagnostics.MicrosoftPrintToPdfDetected ? "available-on-this-machine" : "manual-app-export",
                RequiresAdmin = false,
                RequiresSignedDriver = false,
                Notes = "Use the existing printer/export flow and save PDF/image outputs into the Receipts drop folder. This is the current supported path."
            },
            new VirtualPrinterDriverOption
            {
                Name = "Installer-created drop folder and setup shortcut",
                Status = "non-invasive-future-hook",
                RequiresAdmin = false,
                RequiresSignedDriver = false,
                Notes = "A future installer can create the drop folder, document the print-to-file workflow, and add a shortcut without installing a printer driver."
            },
            new VirtualPrinterDriverOption
            {
                Name = "Custom port monitor or v4 print driver",
                Status = "future-admin-signed-driver",
                RequiresAdmin = true,
                RequiresSignedDriver = true,
                Notes = "Requires installer/admin flow, Windows driver signing, architecture-specific packaging, and clean-machine validation."
            },
            new VirtualPrinterDriverOption
            {
                Name = "PostScript/PDF pipeline",
                Status = "future-admin-toolchain",
                RequiresAdmin = true,
                RequiresSignedDriver = true,
                Notes = "Requires a signed printer pipeline or a managed external renderer, plus licensing and distribution review."
            }
        ]);

        diagnostics.PackageHooks.AddRange(
        [
            "Portable package: includes file-drop import only; no printer driver registration.",
            "Installer-safe hook: create/check the drop folder and add setup documentation.",
            "Deferred admin hook: signed driver or port-monitor install after explicit clean-machine proof."
        ]);

        diagnostics.NextSteps.AddRange(
        [
            "Use `print-import contract --ensure-folder` to create the local drop folder.",
            "Print or export from another app to PDF/image and save into the drop folder.",
            "Use `print-import import <file>` or enable the watched file-drop folder to bring the output into Receipts.",
            "Do not claim virtual-printer driver support until a signed/admin installer lane is implemented and clean-machine tested."
        ]);

        if (!folderHealth.Exists)
        {
            diagnostics.Issues.Add("Drop folder does not exist. Run diagnostics or contract with --ensure-folder to create it.");
        }

        if (!policyAllowed)
        {
            diagnostics.Issues.Add(policyReason);
        }

        if (folderHealth.Exists && !folderHealth.Writable)
        {
            diagnostics.Issues.Add("Drop folder exists but is not writable by the current user.");
        }

        if (!diagnostics.MicrosoftPrintToPdfDetected)
        {
            diagnostics.Warnings.Add("Microsoft Print to PDF was not detected, or installed printers could not be enumerated. App-native PDF/image export can still use the file-drop path.");
        }

        if (!isElevated)
        {
            diagnostics.Warnings.Add("The current process is not elevated. That is fine for file-drop import, but real printer-driver installation would require an admin/installer lane.");
        }

        return diagnostics;
    }

    public VirtualPrinterSetupResult Setup(VirtualPrinterSetupRequest request)
    {
        var diagnostics = GetFeasibilityDiagnostics(ensureFolder: true);
        var result = new VirtualPrinterSetupResult
        {
            Succeeded = diagnostics.DropFolderExists && diagnostics.DropFolderWritable && diagnostics.PolicyAllowed,
            DropFolder = diagnostics.DropFolder,
            SetupNotePath = ResolveSetupNotePath(request.SetupNotePath, diagnostics.DropFolder),
            Enabled = diagnostics.Enabled,
            IncludeSubdirectories = diagnostics.IncludeSubdirectories,
            WatchedFolderState = diagnostics.WatchedFolderState,
            SupportedExtensions = diagnostics.SupportedExtensions.ToList(),
            Diagnostics = diagnostics,
            NextSteps = diagnostics.NextSteps.ToList(),
            Warnings = diagnostics.Warnings.ToList(),
            Issues = diagnostics.Issues.ToList()
        };

        if (!diagnostics.PolicyAllowed)
        {
            result.Message = diagnostics.PolicyReason;
            return result;
        }

        if (!diagnostics.DropFolderExists || !diagnostics.DropFolderWritable)
        {
            result.Message = diagnostics.DropFolderStatus;
            return result;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(result.SetupNotePath)!);
            File.WriteAllText(result.SetupNotePath, BuildSetupNote(diagnostics), Encoding.UTF8);
            result.SetupNoteWritten = true;
            result.Message = $"Print-import setup verified {diagnostics.DropFolder} and wrote {result.SetupNotePath}.";
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            var message = $"Setup note could not be written: {SensitiveTextDetector.Redact(ex.Message)}";
            result.Succeeded = false;
            result.Message = message;
            result.Issues.Add(message);
            return result;
        }
    }

    public async Task<VirtualPrinterImportResult> ImportAsync(
        VirtualPrinterImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.Path));
        if (!File.Exists(path))
        {
            return Failed(path, "Print import source file was not found.");
        }

        if (!IsSupportedPrintOutput(path))
        {
            return Failed(path, $"Unsupported print import extension: {Path.GetExtension(path)}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceApp = string.IsNullOrWhiteSpace(request.SourceApplication)
            ? "virtual-printer-file-drop"
            : request.SourceApplication.Trim();
        var documentTitle = string.IsNullOrWhiteSpace(request.DocumentTitle)
            ? Path.GetFileNameWithoutExtension(path)
            : request.DocumentTitle.Trim();
        var item = await _workspaceStore.ImportFileCopyAsync(
            path,
            BuildNotes(path, sourceApp, documentTitle),
            GetCaptureKind(path),
            new CaptureSource
            {
                ProcessName = sourceApp,
                WindowTitle = documentTitle,
                MonitorName = "Print/file-drop import"
            });

        return new VirtualPrinterImportResult
        {
            Succeeded = true,
            InputPath = path,
            Message = $"Print output imported: {item.FileName}",
            Item = item
        };
    }

    public static IReadOnlyList<string> BuildWatchedFolders(AppSettings settings, AppPaths paths)
    {
        var folders = new List<string>();
        if (settings.EnableWatchFolders)
        {
            folders.AddRange(settings.WatchFolders);
        }

        if (settings.EnableVirtualPrinterImport)
        {
            folders.Add(GetDropFolder(settings, paths));
        }

        return folders
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Environment.ExpandEnvironmentVariables)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetDropFolder(AppSettings settings, AppPaths paths)
    {
        return string.IsNullOrWhiteSpace(settings.VirtualPrinterImportFolder)
            ? Path.Combine(paths.DocumentsRoot, "PrintDrop")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.VirtualPrinterImportFolder));
    }

    public static bool IsInsideDropFolder(AppSettings settings, AppPaths paths, string path)
    {
        if (!settings.EnableVirtualPrinterImport)
        {
            return false;
        }

        var folder = EnsureTrailingSeparator(GetDropFolder(settings, paths));
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        return fullPath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedPrintOutput(string path)
    {
        return SupportedExtensionSet.Contains(Path.GetExtension(path));
    }

    public static CaptureKind GetCaptureKind(string path)
    {
        return Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? CaptureKind.PrintedDocument
            : CaptureKind.PrintedImage;
    }

    private static string BuildNotes(string path, string sourceApp, string documentTitle)
    {
        var safePath = SensitiveTextDetector.Redact(path);
        var safeSource = SensitiveTextDetector.Redact(sourceApp);
        var safeTitle = SensitiveTextDetector.Redact(documentTitle);
        return "Imported from a virtual-printer/file-drop handoff." +
            Environment.NewLine +
            $"Source app: {safeSource}" +
            Environment.NewLine +
            $"Document title: {safeTitle}" +
            Environment.NewLine +
            $"Original path: {safePath}" +
            Environment.NewLine +
            "True virtual-printer driver installation is installer/admin-scoped and not proven by this import.";
    }

    private static VirtualPrinterImportResult Failed(string path, string message)
    {
        return new VirtualPrinterImportResult
        {
            Succeeded = false,
            InputPath = path,
            Message = message
        };
    }

    private static string EnsureTrailingSeparator(string path)
    {
        path = Path.GetFullPath(path);
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string BuildWatchedFolderState(
        bool enabled,
        bool folderExists,
        bool folderWritable,
        bool policyAllowed)
    {
        if (!policyAllowed)
        {
            return "blocked-by-managed-policy";
        }

        if (!enabled)
        {
            return "disabled";
        }

        if (!folderExists)
        {
            return "enabled-folder-missing";
        }

        return folderWritable
            ? "enabled-and-writable"
            : "enabled-folder-not-writable";
    }

    private static string ResolveSetupNotePath(string? setupNotePath, string dropFolder)
    {
        return string.IsNullOrWhiteSpace(setupNotePath)
            ? Path.Combine(dropFolder, "receipts-print-import-setup.md")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(setupNotePath));
    }

    private static string BuildSetupNote(VirtualPrinterFeasibilityDiagnostics diagnostics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Print Import Setup");
        builder.AppendLine();
        builder.AppendLine("## Drop Folder");
        builder.AppendLine();
        builder.AppendLine($"Drop folder: `{diagnostics.DropFolder}`");
        builder.AppendLine($"Watched folder state: `{diagnostics.WatchedFolderState}`");
        builder.AppendLine($"Include subdirectories: `{diagnostics.IncludeSubdirectories}`");
        builder.AppendLine($"Supported file types: `{string.Join(", ", diagnostics.SupportedExtensions)}`");
        builder.AppendLine();
        builder.AppendLine("## Microsoft Print To PDF / File Export");
        builder.AppendLine();
        builder.AppendLine("1. Open the source document or image in the app that owns it.");
        builder.AppendLine("2. Choose Print and select Microsoft Print to PDF when it is available, or use the app's PDF/image export command.");
        builder.AppendLine("3. Save the PDF/image into the Receipts drop folder above.");
        builder.AppendLine("4. If watched print import is enabled, Receipts can import supported files from that folder while the desktop app is running.");
        builder.AppendLine("5. You can also import one file explicitly with `receipts print-import import <file>`.");
        builder.AppendLine();
        builder.AppendLine("## Readiness");
        builder.AppendLine();
        builder.AppendLine($"Drop folder status: {diagnostics.DropFolderStatus}");
        builder.AppendLine($"Installed printer probe: {diagnostics.InstalledPrinterProbeStatus}");
        builder.AppendLine($"Microsoft Print to PDF detected: {diagnostics.MicrosoftPrintToPdfDetected}");
        builder.AppendLine($"Microsoft XPS Document Writer detected: {diagnostics.MicrosoftXpsWriterDetected}");
        builder.AppendLine($"Current process elevated: {diagnostics.CurrentProcessElevated}");
        builder.AppendLine($"Policy status: {diagnostics.PolicyStatus}");
        if (!string.IsNullOrWhiteSpace(diagnostics.PolicyReason))
        {
            builder.AppendLine($"Policy reason: {diagnostics.PolicyReason}");
        }

        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine("Receipts currently supports local file-drop import. It does not install a Windows printer driver, register a port monitor, or claim clean-machine printer-driver proof.");
        builder.AppendLine("A true virtual-printer driver remains an installer/admin-scoped, signed-driver lane that needs explicit clean-machine validation.");
        builder.AppendLine();
        builder.AppendLine("## Privacy");
        builder.AppendLine();
        builder.AppendLine("Print outputs can contain private document content. Keep the drop folder local, review files before sharing, and avoid saving sensitive documents into shared or synced folders unless that is intentional.");
        builder.AppendLine();
        builder.AppendLine("## Next Steps");
        foreach (var step in diagnostics.NextSteps)
        {
            builder.AppendLine($"- {step}");
        }

        if (diagnostics.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Warnings");
            foreach (var warning in diagnostics.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        if (diagnostics.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Issues");
            foreach (var issue in diagnostics.Issues)
            {
                builder.AppendLine($"- {issue}");
            }
        }

        return builder.ToString();
    }

    private static VirtualPrinterDropFolderProbe ProbeDropFolder(string folder, bool ensureFolder)
    {
        try
        {
            if (ensureFolder)
            {
                Directory.CreateDirectory(folder);
            }

            if (!Directory.Exists(folder))
            {
                return new VirtualPrinterDropFolderProbe(false, false, "Drop folder does not exist.");
            }

            var probeFile = Path.Combine(folder, $".receipts-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "Receipts print-import write probe");
            File.Delete(probeFile);
            return new VirtualPrinterDropFolderProbe(true, true, "Drop folder exists and is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new VirtualPrinterDropFolderProbe(
                Directory.Exists(folder),
                false,
                $"Drop folder probe failed: {SensitiveTextDetector.Redact(ex.Message)}");
        }
    }

    private static VirtualPrinterInstalledPrinterProbe ProbeInstalledPrinters()
    {
        try
        {
            var rawPrinters = PrinterSettings.InstalledPrinters
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var printers = rawPrinters
                .Select(RedactPrinterName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
            return new VirtualPrinterInstalledPrinterProbe(
                printers,
                "Installed printers enumerated.",
                rawPrinters.Any(name => name.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)),
                rawPrinters.Any(name => name.Contains("Microsoft XPS Document Writer", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new VirtualPrinterInstalledPrinterProbe(
                [],
                $"Installed printer probe failed: {SensitiveTextDetector.Redact(ex.Message)}",
                false,
                false);
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string RedactPrinterName(string name)
    {
        var redacted = SensitiveTextDetector.Redact(name);
        if (!redacted.Equals(name, StringComparison.Ordinal))
        {
            return redacted;
        }

        return name switch
        {
            "Microsoft Print to PDF" => name,
            "Microsoft XPS Document Writer" => name,
            "Fax" => name,
            "OneNote (Desktop)" => name,
            "OneNote for Windows 10" => name,
            _ => "[REDACTED:printer-name]"
        };
    }

    private sealed record VirtualPrinterDropFolderProbe(bool Exists, bool Writable, string Status);

    private sealed record VirtualPrinterInstalledPrinterProbe(
        List<string> Printers,
        string Status,
        bool HasMicrosoftPrintToPdf,
        bool HasMicrosoftXpsWriter);
}

public sealed class VirtualPrinterImportContract
{
    public bool Enabled { get; set; }
    public string DropFolder { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; }
    public List<string> SupportedExtensions { get; set; } = new();
    public string DriverInstallStatus { get; set; } = "Not installed by Receipts. Use this file-drop folder with an existing print-to-PDF/image workflow until a dedicated installer/admin lane exists.";
    public string PrivacyNote { get; set; } = "Print outputs can contain private document content. Keep the drop folder local and review files before sharing.";
}

public sealed class VirtualPrinterFeasibilityDiagnostics
{
    public bool Enabled { get; set; }
    public string DropFolder { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; }
    public List<string> SupportedExtensions { get; set; } = new();
    public string WatchedFolderState { get; set; } = string.Empty;
    public bool PolicyAllowed { get; set; }
    public string PolicyStatus { get; set; } = string.Empty;
    public string PolicyReason { get; set; } = string.Empty;
    public bool DropFolderExists { get; set; }
    public bool DropFolderWritable { get; set; }
    public string DropFolderStatus { get; set; } = string.Empty;
    public List<string> InstalledPrinters { get; set; } = new();
    public string InstalledPrinterProbeStatus { get; set; } = string.Empty;
    public bool MicrosoftPrintToPdfDetected { get; set; }
    public bool MicrosoftXpsWriterDetected { get; set; }
    public bool CurrentProcessElevated { get; set; }
    public bool DriverInstallImplemented { get; set; }
    public bool DriverInstallRequiresAdmin { get; set; }
    public bool DriverSigningRequired { get; set; }
    public bool InstallerHookImplemented { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<VirtualPrinterDriverOption> DriverOptions { get; set; } = new();
    public List<string> PackageHooks { get; set; } = new();
    public List<string> NextSteps { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class VirtualPrinterSetupRequest
{
    public string? SetupNotePath { get; set; }
}

public sealed class VirtualPrinterSetupResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DropFolder { get; set; } = string.Empty;
    public string SetupNotePath { get; set; } = string.Empty;
    public bool SetupNoteWritten { get; set; }
    public bool SettingsSaved { get; set; }
    public bool Enabled { get; set; }
    public bool IncludeSubdirectories { get; set; }
    public string WatchedFolderState { get; set; } = string.Empty;
    public List<string> SupportedExtensions { get; set; } = new();
    public VirtualPrinterFeasibilityDiagnostics? Diagnostics { get; set; }
    public List<string> NextSteps { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class VirtualPrinterDriverOption
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }
    public bool RequiresSignedDriver { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class VirtualPrinterImportRequest
{
    public string Path { get; set; } = string.Empty;
    public string? SourceApplication { get; set; }
    public string? DocumentTitle { get; set; }
}

public sealed class VirtualPrinterImportResult
{
    public bool Succeeded { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public CaptureItem? Item { get; set; }
}

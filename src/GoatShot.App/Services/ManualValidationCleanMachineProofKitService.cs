using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class ManualValidationCleanMachineProofKitService
{
    public const string DefaultDirectoryName = "clean-machine-proof-kit";
    public const string RunbookFileName = "clean-machine-proof-runbook.md";
    public const string ManifestFileName = "clean-machine-proof-manifest.json";
    public const string ScriptFileName = "run-clean-machine-proof.ps1";
    public const string EvidenceChecklistFileName = "evidence-checklist.md";
    public const string PackageDirectoryName = "package";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ManualValidationCleanMachineProofKitResult> CreateAsync(
        ManualValidationCleanMachineProofKitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return ManualValidationCleanMachineProofKitResult.Failed("Manual validation folder is required.");
        }

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.RootPath));
        if (!Directory.Exists(root))
        {
            return ManualValidationCleanMachineProofKitResult.Failed($"Manual validation folder was not found: {root}");
        }

        var outputRoot = string.IsNullOrWhiteSpace(request.OutputPath)
            ? Path.Combine(root, DefaultDirectoryName)
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        Directory.CreateDirectory(outputRoot);

        var packageRoot = Path.Combine(outputRoot, PackageDirectoryName);
        var repoRoot = ResolveRepoRoot(request.RepoRoot, root);
        var portablePath = ResolvePackagePath(
            request.PortableZipPath,
            repoRoot,
            Path.Combine("artifacts", "dist"),
            $"Receipts-{BrandIdentity.ReleaseVersion}-win-x64-portable.zip",
            "Receipts-*-win-x64-portable.zip",
            "GoatShot-*-win-x64-portable.zip");
        var installerPath = ResolvePackagePath(
            request.InstallerPath,
            repoRoot,
            Path.Combine("artifacts", "dist"),
            $"Receipts-{BrandIdentity.ReleaseVersion}-win-x64.exe",
            "Receipts-*-win-x64.exe",
            "GoatShot-Setup-*-win-x64.exe");

        var result = new ManualValidationCleanMachineProofKitResult
        {
            Succeeded = true,
            Message = $"Clean-machine proof kit written: {outputRoot}",
            RootPath = root,
            OutputRoot = outputRoot,
            RepoRoot = repoRoot,
            GeneratedAt = DateTimeOffset.Now,
            RunbookPath = Path.Combine(outputRoot, RunbookFileName),
            ManifestPath = Path.Combine(outputRoot, ManifestFileName),
            ScriptPath = Path.Combine(outputRoot, ScriptFileName),
            EvidenceChecklistPath = Path.Combine(outputRoot, EvidenceChecklistFileName),
            PackageDirectory = packageRoot
        };

        result.PortablePackage = await BuildArtifactAsync("portable-zip", portablePath, cancellationToken);
        result.InstallerPackage = await BuildArtifactAsync("installer", installerPath, cancellationToken);

        if (!result.PortablePackage.Exists)
        {
            result.Warnings.Add("Portable ZIP was not found. The kit was written, but a clean-machine pass cannot start until a portable package is supplied.");
        }

        if (!result.InstallerPackage.Exists)
        {
            result.Warnings.Add("Installer artifact was not found. Installer compile/install/uninstall proof remains skipped unless an installer is supplied later.");
        }

        if (request.CopyPackage)
        {
            Directory.CreateDirectory(packageRoot);
            await CopyArtifactAsync(result.PortablePackage, packageRoot, cancellationToken);
            await CopyArtifactAsync(result.InstallerPackage, packageRoot, cancellationToken);
        }

        result.ReadyForCleanMachineRun = result.PortablePackage.Exists;
        result.SelfContainedPackageCopy = result.PortablePackage.Exists && !string.IsNullOrWhiteSpace(result.PortablePackage.CopiedPath);
        result.CopiedFiles.AddRange(
            new[] { result.PortablePackage, result.InstallerPackage }
                .Where(artifact => !string.IsNullOrWhiteSpace(artifact.CopiedPath))
                .Select(artifact => artifact.CopiedPath));

        await File.WriteAllTextAsync(
            result.RunbookPath,
            BuildRunbook(request, result),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.ScriptPath,
            BuildScript(result),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.EvidenceChecklistPath,
            BuildEvidenceChecklist(request, result),
            cancellationToken);
        await File.WriteAllTextAsync(
            result.ManifestPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            cancellationToken);

        return result;
    }

    private static async Task<ManualValidationCleanMachineProofArtifact> BuildArtifactAsync(
        string kind,
        string? path,
        CancellationToken cancellationToken)
    {
        var artifact = new ManualValidationCleanMachineProofArtifact
        {
            Kind = kind,
            SourcePath = path ?? string.Empty,
            FileName = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path)
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return artifact;
        }

        var info = new FileInfo(path);
        artifact.Exists = true;
        artifact.Length = info.Length;
        artifact.LastWriteTime = info.LastWriteTime;
        artifact.Sha256 = await Sha256FileAsync(path, cancellationToken);
        return artifact;
    }

    private static async Task CopyArtifactAsync(
        ManualValidationCleanMachineProofArtifact artifact,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        if (!artifact.Exists || string.IsNullOrWhiteSpace(artifact.SourcePath))
        {
            return;
        }

        var targetPath = Path.Combine(packageRoot, artifact.FileName);
        await using (var source = File.OpenRead(artifact.SourcePath))
        await using (var destination = File.Create(targetPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        artifact.CopiedPath = targetPath;
        artifact.CopiedRelativePath = Path.Combine(PackageDirectoryName, artifact.FileName)
            .Replace('\\', '/');
    }

    private static string? ResolvePackagePath(
        string? requestedPath,
        string repoRoot,
        string relativeSearchDirectory,
        params string[] searchPatterns)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath));
        }

        var directory = Path.Combine(repoRoot, relativeSearchDirectory);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var searchPattern in searchPatterns)
        {
            var match = Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenByDescending(info => info.Length)
                .Select(info => info.FullName)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static string ResolveRepoRoot(string? requestedRepoRoot, string manualValidationRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRepoRoot))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedRepoRoot));
        }

        foreach (var start in new[] { Environment.CurrentDirectory, manualValidationRoot })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")) ||
                    Directory.Exists(Path.Combine(directory.FullName, "src", "GoatShot.App")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    private static string BuildRunbook(
        ManualValidationCleanMachineProofKitRequest request,
        ManualValidationCleanMachineProofKitResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Clean-Machine Portable And Installer Proof Kit");
        builder.AppendLine();
        builder.AppendLine($"Generated at: {result.GeneratedAt:O}");
        builder.AppendLine($"Manual validation folder: `{result.RootPath}`");
        builder.AppendLine($"Output folder: `{result.OutputRoot}`");
        builder.AppendLine($"Repo root used for artifact discovery: `{result.RepoRoot}`");
        builder.AppendLine();
        builder.AppendLine("## Status");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Portable ZIP found: `{result.PortablePackage.Exists}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Portable ZIP SHA256: `{ValueOrPending(result.PortablePackage.Sha256)}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Installer artifact found: `{result.InstallerPackage.Exists}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Self-contained package copy: `{result.SelfContainedPackageCopy}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Ready for clean-machine run: `{result.ReadyForCleanMachineRun}`");
        builder.AppendLine();
        builder.AppendLine("This kit does not perform or certify the clean-machine pass. It stages the runbook, hashes, script template, and evidence checklist so a human can run the lane on a clean Windows profile or VM and then record the result.");
        builder.AppendLine();
        builder.AppendLine("## Files In This Kit");
        builder.AppendLine();
        builder.AppendLine($"- `{ManifestFileName}` - machine-readable source artifact, hash, and output metadata.");
        builder.AppendLine($"- `{ScriptFileName}` - optional PowerShell helper for a clean Windows VM/profile.");
        builder.AppendLine($"- `{EvidenceChecklistFileName}` - evidence names and review slots for the operator.");
        if (result.SelfContainedPackageCopy)
        {
            builder.AppendLine($"- `{result.PortablePackage.CopiedRelativePath}` - copied portable ZIP for transfer.");
        }
        else if (result.PortablePackage.Exists)
        {
            builder.AppendLine($"- Portable ZIP source: `{result.PortablePackage.SourcePath}`. Copy it into the VM or rerun with `--copy-package`.");
        }
        else
        {
            builder.AppendLine("- Portable ZIP source: not found. Build/package first, then rerun this command or pass `--portable-zip <path>`.");
        }

        if (result.InstallerPackage.Exists)
        {
            builder.AppendLine(string.IsNullOrWhiteSpace(result.InstallerPackage.CopiedRelativePath)
                ? $"- Installer source: `{result.InstallerPackage.SourcePath}`."
                : $"- `{result.InstallerPackage.CopiedRelativePath}` - copied optional installer artifact.");
        }
        else
        {
            builder.AppendLine("- Installer source: not found. Installer compile/install/uninstall proof remains skipped until supplied.");
        }

        builder.AppendLine();
        builder.AppendLine("## Clean Machine Preconditions");
        builder.AppendLine();
        builder.AppendLine("- Use a clean Windows user profile or disposable Windows VM.");
        builder.AppendLine("- Stage only safe demo content. Do not expose private files, chats, email, account screens, credentials, or customer data.");
        builder.AppendLine("- Keep the VM evidence folder separate from real Receipts user data and any legacy GoatShot state.");
        builder.AppendLine("- Prefer a local-only VM/network-disabled run unless provider or browser-store proof is explicitly scheduled.");
        builder.AppendLine("- Do not mark this lane passed from current-developer-machine evidence.");
        builder.AppendLine();
        builder.AppendLine("## Suggested VM Flow");
        builder.AppendLine();
        builder.AppendLine("1. Copy this kit folder, or at least the portable ZIP and this runbook, into the clean Windows profile or VM.");
        builder.AppendLine("2. Run `Get-FileHash -Algorithm SHA256` against the portable ZIP and compare it to the manifest.");
        builder.AppendLine("3. Run `run-clean-machine-proof.ps1` from PowerShell, passing `-PortableZip` if the ZIP is not inside the kit.");
        builder.AppendLine("4. Launch `Receipts.exe` from the extracted portable folder and complete the human GUI click-through with safe content.");
        builder.AppendLine("5. Save screenshots, notes, diagnostics output, and any installer logs under a clean-machine evidence folder.");
        builder.AppendLine("6. Review the evidence for privacy/redaction before copying it back into the manual-validation folder.");
        builder.AppendLine("7. Use `manual-validation record-lane --lane clean-machine-install` only after the human pass is complete.");
        builder.AppendLine();
        builder.AppendLine("## Human GUI Checks");
        builder.AppendLine();
        builder.AppendLine("- First launch creates expected app folders only under the clean profile or isolated roots.");
        builder.AppendLine("- Settings open, save, and reload without requiring source-tree files.");
        builder.AppendLine("- A simple safe import/edit/export loop works from the portable package.");
        builder.AppendLine("- No shipped package contains prior user settings, secrets, workspace indexes, thumbnails, logs, proof artifacts, or capture media.");
        builder.AppendLine("- If an installer is supplied, install and uninstall behavior is observed separately from the portable ZIP run.");
        builder.AppendLine("- Uninstall does not remove user captures or libraries without explicit operator action.");
        builder.AppendLine();
        builder.AppendLine("## Recording The Lane");
        builder.AppendLine();
        builder.AppendLine("After the clean-machine operator pass, copy reviewed safe evidence into this manual-validation folder and run a command like:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine(BuildRecordCommand(request, result.RootPath, Path.Combine(DefaultDirectoryName, EvidenceChecklistFileName)));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Use `--status blocked` with a note instead of `passed` if the VM, installer, package, or GUI evidence is not available.");
        builder.AppendLine();
        builder.AppendLine("## Boundaries");
        builder.AppendLine();
        builder.AppendLine("- This kit is planning and evidence scaffolding, not proof completion.");
        builder.AppendLine("- The generated script can collect command-backed evidence, but it does not replace human GUI observation.");
        builder.AppendLine("- Installer compile/install/uninstall proof remains unproven unless an actual installer artifact is supplied and run on a clean machine.");
        builder.AppendLine("- OAuth/live-provider, browser-store, Android-device, virtual-printer-driver, and hardware recording lanes remain separate.");
        AppendList(builder, "Warnings", result.Warnings);
        return builder.ToString();
    }

    private static string BuildEvidenceChecklist(
        ManualValidationCleanMachineProofKitRequest request,
        ManualValidationCleanMachineProofKitResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Clean Machine Evidence Checklist");
        builder.AppendLine();
        builder.AppendLine("Use this file inside the manual-validation folder. Fill it in after reviewing the clean Windows VM/profile evidence.");
        builder.AppendLine();
        builder.AppendLine("## Artifact Identity");
        builder.AppendLine();
        builder.AppendLine($"- Portable ZIP: `{ValueOrPending(result.PortablePackage.FileName)}`");
        builder.AppendLine($"- Portable SHA256: `{ValueOrPending(result.PortablePackage.Sha256)}`");
        builder.AppendLine($"- Installer: `{ValueOrPending(result.InstallerPackage.FileName)}`");
        builder.AppendLine($"- Installer SHA256: `{ValueOrPending(result.InstallerPackage.Sha256)}`");
        builder.AppendLine();
        builder.AppendLine("## Evidence Files To Bring Back");
        builder.AppendLine();
        builder.AppendLine("- `machine-info.txt`");
        builder.AppendLine("- `portable-hash.txt`");
        builder.AppendLine("- `diagnostics-print.txt`");
        builder.AppendLine("- `paths.txt`");
        builder.AppendLine("- `main-window-render.png` or safe first-launch screenshot");
        builder.AppendLine("- `settings-save-proof.png` or notes");
        builder.AppendLine("- `capture-import-edit-export-proof.png` or notes");
        builder.AppendLine("- `installer-build-or-skipped.md`");
        builder.AppendLine("- `installer-install-uninstall-log.txt` when an installer was tested");
        builder.AppendLine("- `clean-machine-script-result.json` when the helper script was used");
        builder.AppendLine();
        builder.AppendLine("## Operator Result");
        builder.AppendLine();
        builder.AppendLine("- Result: pass / fail / blocked");
        builder.AppendLine("- Operator:");
        builder.AppendLine("- Date/time:");
        builder.AppendLine("- Windows version and clean-profile/VM source:");
        builder.AppendLine("- Portable hash matched manifest: yes / no");
        builder.AppendLine("- First launch result:");
        builder.AppendLine("- Settings save/reload result:");
        builder.AppendLine("- Import/edit/export loop result:");
        builder.AppendLine("- Installer compile/install/uninstall result:");
        builder.AppendLine("- Privacy/redaction review result:");
        builder.AppendLine();
        builder.AppendLine("## Record-Lane Command Template");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine(BuildRecordCommand(request, result.RootPath, Path.Combine(DefaultDirectoryName, EvidenceChecklistFileName)));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Do not run the passed command until the clean-machine operator pass was actually performed and reviewed.");
        return builder.ToString();
    }

    private static string BuildScript(ManualValidationCleanMachineProofKitResult result)
    {
        var copiedPortable = result.PortablePackage.CopiedRelativePath.Replace('/', '\\');
        var copiedInstaller = result.InstallerPackage.CopiedRelativePath.Replace('/', '\\');
        var defaultPortable = !string.IsNullOrWhiteSpace(copiedPortable)
            ? $"Join-Path $PSScriptRoot {PsQuote(copiedPortable)}"
            : PsQuote(result.PortablePackage.SourcePath);
        var defaultInstaller = !string.IsNullOrWhiteSpace(copiedInstaller)
            ? $"Join-Path $PSScriptRoot {PsQuote(copiedInstaller)}"
            : PsQuote(result.InstallerPackage.SourcePath);

        var builder = new StringBuilder();
        builder.AppendLine("param(");
        builder.AppendLine("    [string]$PortableZip = \"\",");
        builder.AppendLine("    [string]$InstallerPath = \"\",");
        builder.AppendLine("    [string]$OutputRoot = (Join-Path $PSScriptRoot \"evidence\"),");
        builder.AppendLine("    [switch]$RunInstallerSmoke");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("$ErrorActionPreference = \"Stop\"");
        builder.AppendLine();
        builder.AppendLine("function Invoke-ProofCommand {");
        builder.AppendLine("    param(");
        builder.AppendLine("        [string]$Name,");
        builder.AppendLine("        [string]$FilePath,");
        builder.AppendLine("        [string[]]$Arguments,");
        builder.AppendLine("        [string]$LogPath");
        builder.AppendLine("    )");
        builder.AppendLine("    & $FilePath @Arguments *> $LogPath");
        builder.AppendLine("    $exitCode = $LASTEXITCODE");
        builder.AppendLine("    if ($null -eq $exitCode) { $exitCode = 0 }");
        builder.AppendLine("    return [pscustomobject]@{ name = $Name; filePath = $FilePath; logPath = $LogPath; exitCode = $exitCode }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null");
        builder.AppendLine("$machineInfoPath = Join-Path $OutputRoot \"machine-info.txt\"");
        builder.AppendLine("Get-ComputerInfo | Out-String | Set-Content -LiteralPath $machineInfoPath -Encoding UTF8");
        builder.AppendLine();
        builder.AppendLine($"$defaultPortable = {defaultPortable}");
        builder.AppendLine("if ([string]::IsNullOrWhiteSpace($PortableZip) -and (-not [string]::IsNullOrWhiteSpace($defaultPortable)) -and (Test-Path -LiteralPath $defaultPortable -PathType Leaf)) {");
        builder.AppendLine("    $PortableZip = $defaultPortable");
        builder.AppendLine("}");
        builder.AppendLine("if ([string]::IsNullOrWhiteSpace($PortableZip)) {");
        builder.AppendLine("    throw \"PortableZip is required. Copy the portable ZIP into the kit package folder or pass -PortableZip <path>.\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("$portableHashPath = Join-Path $OutputRoot \"portable-hash.txt\"");
        builder.AppendLine("$portableHash = (Get-FileHash -LiteralPath $PortableZip -Algorithm SHA256).Hash.ToLowerInvariant()");
        builder.AppendLine("$portableHash | Set-Content -LiteralPath $portableHashPath -Encoding UTF8");
        builder.AppendLine();
        builder.AppendLine("$extractRoot = Join-Path $OutputRoot \"portable-extract\"");
        builder.AppendLine("if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }");
        builder.AppendLine("New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null");
        builder.AppendLine("Expand-Archive -LiteralPath $PortableZip -DestinationPath $extractRoot -Force");
        builder.AppendLine();
        builder.AppendLine("$cli = Get-ChildItem -LiteralPath $extractRoot -Filter \"Receipts.Cli.exe\" -Recurse | Select-Object -First 1");
        builder.AppendLine("if ($null -eq $cli) { $cli = Get-ChildItem -LiteralPath $extractRoot -Filter \"GoatShot.Cli.exe\" -Recurse | Select-Object -First 1 }");
        builder.AppendLine("$app = Get-ChildItem -LiteralPath $extractRoot -Filter \"Receipts.exe\" -Recurse | Select-Object -First 1");
        builder.AppendLine("if ($null -eq $app) { $app = Get-ChildItem -LiteralPath $extractRoot -Filter \"GoatShot.exe\" -Recurse | Select-Object -First 1 }");
        builder.AppendLine("if ($null -eq $cli) { throw \"Receipts.Cli.exe (or the legacy GoatShot.Cli.exe) was not found after extracting the portable ZIP.\" }");
        builder.AppendLine("if ($null -eq $app) { throw \"Receipts.exe (or the legacy GoatShot.exe) was not found after extracting the portable ZIP.\" }");
        builder.AppendLine();
        builder.AppendLine("$env:RECEIPTS_LOCAL_ROOT = Join-Path $OutputRoot \"isolated-local-root\"");
        builder.AppendLine("$env:RECEIPTS_LIBRARY_ROOT = Join-Path $OutputRoot \"isolated-library-root\"");
        builder.AppendLine("$env:GOATSHOT_LOCAL_ROOT = $env:RECEIPTS_LOCAL_ROOT");
        builder.AppendLine("$env:GOATSHOT_LIBRARY_ROOT = $env:RECEIPTS_LIBRARY_ROOT");
        builder.AppendLine("New-Item -ItemType Directory -Force -Path $env:RECEIPTS_LOCAL_ROOT, $env:RECEIPTS_LIBRARY_ROOT | Out-Null");
        builder.AppendLine();
        builder.AppendLine("$commands = @()");
        builder.AppendLine("$commands += Invoke-ProofCommand -Name \"CLI diagnostics print\" -FilePath $cli.FullName -Arguments @(\"diagnostics\", \"print\") -LogPath (Join-Path $OutputRoot \"diagnostics-print.txt\")");
        builder.AppendLine("$commands += Invoke-ProofCommand -Name \"CLI paths\" -FilePath $cli.FullName -Arguments @(\"paths\") -LogPath (Join-Path $OutputRoot \"paths.txt\")");
        builder.AppendLine("$commands += Invoke-ProofCommand -Name \"Render main window\" -FilePath $app.FullName -Arguments @(\"--render-main-output\", (Join-Path $OutputRoot \"main-window-render.png\")) -LogPath (Join-Path $OutputRoot \"render-main.log\")");
        builder.AppendLine();
        builder.AppendLine($"$defaultInstaller = {defaultInstaller}");
        builder.AppendLine("if ([string]::IsNullOrWhiteSpace($InstallerPath) -and (-not [string]::IsNullOrWhiteSpace($defaultInstaller)) -and (Test-Path -LiteralPath $defaultInstaller -PathType Leaf)) {");
        builder.AppendLine("    $InstallerPath = $defaultInstaller");
        builder.AppendLine("}");
        builder.AppendLine("$installerSmoke = [pscustomobject]@{ requested = [bool]$RunInstallerSmoke; status = \"skipped\"; installerPath = $InstallerPath; logPath = \"\" }");
        builder.AppendLine("if ($RunInstallerSmoke) {");
        builder.AppendLine("    if ([string]::IsNullOrWhiteSpace($InstallerPath) -or -not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {");
        builder.AppendLine("        $installerSmoke.status = \"blocked-missing-installer\"");
        builder.AppendLine("    }");
        builder.AppendLine("    else {");
        builder.AppendLine("        $installRoot = Join-Path $OutputRoot \"installer-root\"");
        builder.AppendLine("        $installLog = Join-Path $OutputRoot \"installer-install-uninstall-log.txt\"");
        builder.AppendLine("        New-Item -ItemType Directory -Force -Path $installRoot | Out-Null");
        builder.AppendLine("        & $InstallerPath /VERYSILENT /SUPPRESSMSGBOXES /NORESTART \"/DIR=$installRoot\" \"/LOG=$installLog\"");
        builder.AppendLine("        $installExit = $LASTEXITCODE");
        builder.AppendLine("        if ($null -eq $installExit) { $installExit = 0 }");
        builder.AppendLine("        $uninstaller = Join-Path $installRoot \"unins000.exe\"");
        builder.AppendLine("        if ($installExit -eq 0 -and (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {");
        builder.AppendLine("            & $uninstaller /VERYSILENT /SUPPRESSMSGBOXES /NORESTART");
        builder.AppendLine("            $installerSmoke.status = \"completed-review-log\"");
        builder.AppendLine("        }");
        builder.AppendLine("        else {");
        builder.AppendLine("            $installerSmoke.status = \"failed-review-log\"");
        builder.AppendLine("        }");
        builder.AppendLine("        $installerSmoke.logPath = $installLog");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("$result = [pscustomobject]@{");
        builder.AppendLine("    generatedAt = (Get-Date).ToString(\"O\")");
        builder.AppendLine("    portableZip = $PortableZip");
        builder.AppendLine("    portableSha256 = $portableHash");
        builder.AppendLine("    outputRoot = $OutputRoot");
        builder.AppendLine("    receiptsLocalRoot = $env:RECEIPTS_LOCAL_ROOT");
        builder.AppendLine("    receiptsLibraryRoot = $env:RECEIPTS_LIBRARY_ROOT");
        builder.AppendLine("    legacyEnvironmentAliasesSet = $true");
        builder.AppendLine("    commands = $commands");
        builder.AppendLine("    installerSmoke = $installerSmoke");
        builder.AppendLine("    boundary = \"Command evidence only. Human GUI click-through and privacy review must be recorded separately.\"");
        builder.AppendLine("}");
        builder.AppendLine("$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputRoot \"clean-machine-script-result.json\") -Encoding UTF8");
        builder.AppendLine("Write-Output \"Clean-machine command evidence written to $OutputRoot. Review evidence before recording the manual-validation lane.\"");
        return builder.ToString();
    }

    private static string BuildRecordCommand(
        ManualValidationCleanMachineProofKitRequest request,
        string root,
        string evidencePath)
    {
        var cliPath = ResolveCliPath(request.CliPath);
        return string.Join(
            " ",
            [
                "&",
                PsQuote(cliPath),
                "manual-validation",
                "record-lane",
                "--folder",
                PsQuote(root),
                "--lane",
                PsQuote("clean-machine-install"),
                "--status",
                PsQuote("passed"),
                "--note",
                PsQuote("<replace with clean-machine operator result>"),
                "--evidence",
                PsQuote(evidencePath.Replace('\\', '/')),
                "--json"
            ]);
    }

    private static string ResolveCliPath(string? requestedCliPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedCliPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedCliPath));
        }

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            var packagedCli = Path.Combine(processDirectory, BrandIdentity.CommandLineExecutableName);
            if (File.Exists(packagedCli))
            {
                return packagedCli;
            }

            var legacyPackagedCli = Path.Combine(processDirectory, $"{BrandIdentity.LegacyProductName}.Cli.exe");
            if (File.Exists(legacyPackagedCli))
            {
                return legacyPackagedCli;
            }
        }

        var buildDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "GoatShot.Cli",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0");
        var currentPath = Path.Combine(buildDirectory, BrandIdentity.CommandLineExecutableName);
        var legacyPath = Path.Combine(buildDirectory, $"{BrandIdentity.LegacyProductName}.Cli.exe");
        return File.Exists(currentPath) || !File.Exists(legacyPath) ? currentPath : legacyPath;
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("- None.");
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }

    private static string ValueOrPending(string value) =>
        string.IsNullOrWhiteSpace(value) ? "pending" : value;

    private static string PsQuote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

public sealed class ManualValidationCleanMachineProofKitRequest
{
    public string RootPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? RepoRoot { get; set; }
    public string? PortableZipPath { get; set; }
    public string? InstallerPath { get; set; }
    public string? CliPath { get; set; }
    public bool CopyPackage { get; set; }
}

public sealed class ManualValidationCleanMachineProofKitResult
{
    public bool Succeeded { get; set; }
    public bool ReadyForCleanMachineRun { get; set; }
    public bool SelfContainedPackageCopy { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public string RepoRoot { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string RunbookPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string EvidenceChecklistPath { get; set; } = string.Empty;
    public string PackageDirectory { get; set; } = string.Empty;
    public ManualValidationCleanMachineProofArtifact PortablePackage { get; set; } = new();
    public ManualValidationCleanMachineProofArtifact InstallerPackage { get; set; } = new();
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Issues { get; set; } = new();

    public static ManualValidationCleanMachineProofKitResult Failed(string message) =>
        new()
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message),
            Issues = [SensitiveTextDetector.Redact(message)]
        };
}

public sealed class ManualValidationCleanMachineProofArtifact
{
    public string Kind { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset? LastWriteTime { get; set; }
    public string CopiedPath { get; set; } = string.Empty;
    public string CopiedRelativePath { get; set; } = string.Empty;
}

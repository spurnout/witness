param(
    [string]$PackageScript = "scripts\package-release.ps1",
    [string]$InstallerScript = "packaging\GoatShot.iss",
    [string]$PublishDir = "artifacts\publish\GoatShot-win-x64",
    [string]$DistRoot = "artifacts\dist",
    [string]$Version = "0.1.0",
    [string]$OutputRoot = "artifacts\installer-package-verification",
    [string]$InstallerPath = "",
    [switch]$BuildInstaller,
    [switch]$RunSilentInstallSmoke,
    [switch]$Json
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function Add-Message {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Message
    )

    [void]$List.Add($Message)
}

function Write-TextFile {
    param(
        [string]$PathValue,
        [string]$Content
    )

    $parent = Split-Path -Parent $PathValue
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Set-Content -LiteralPath $PathValue -Value $Content -Encoding UTF8
}

function Find-InnoCompiler {
    $candidates = @()
    if ($env:INNO_SETUP_ISCC) {
        $candidates += $env:INNO_SETUP_ISCC
    }

    $fromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($fromPath) {
        $candidates += $fromPath.Source
    }

    $candidates += @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )

    return $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}

function Test-InnoScript {
    param(
        [string]$PathValue,
        [System.Collections.Generic.List[string]]$Issues
    )

    $checks = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
        Add-Message $Issues "Installer script was not found: $PathValue"
        return $checks
    }

    $content = Get-Content -LiteralPath $PathValue -Raw
    $required = @(
        @{ name = "Setup section"; pattern = '(?im)^\[Setup\]\s*$' },
        @{ name = "AppName"; pattern = '(?im)^AppName\s*=\s*GoatShot\s*$' },
        @{ name = "Version macro"; pattern = '(?im)^AppVersion\s*=\s*\{#AppVersion\}\s*$' },
        @{ name = "User-local install root"; pattern = '(?im)^DefaultDirName\s*=\s*\{localappdata\}\\Programs\\GoatShot\s*$' },
        @{ name = "Lowest privilege default"; pattern = '(?im)^PrivilegesRequired\s*=\s*lowest\s*$' },
        @{ name = "Output filename"; pattern = '(?im)^OutputBaseFilename\s*=\s*GoatShot-Setup-\{#AppVersion\}-win-x64\s*$' },
        @{ name = "Published file tree"; pattern = '(?im)^Source:\s*"\{#PublishDir\}\\\*";\s*DestDir:\s*"\{app\}"' },
        @{ name = "Uninstall display icon"; pattern = '(?im)^UninstallDisplayIcon\s*=\s*\{app\}\\GoatShot\.exe\s*$' },
        @{ name = "Optional startup task"; pattern = '(?im)^Name:\s*"startup";.*Start GoatShot when I sign in' },
        @{ name = "Post-install launch is user-visible"; pattern = '(?im)^Filename:\s*"\{app\}\\GoatShot\.exe";.*postinstall' }
    )

    foreach ($item in $required) {
        $passed = [regex]::IsMatch($content, $item.pattern)
        [void]$checks.Add([pscustomobject]@{
            name = $item.name
            passed = $passed
        })

        if (-not $passed) {
            Add-Message $Issues "Installer script check failed: $($item.name)"
        }
    }

    return $checks
}

function Build-Markdown {
    param([pscustomobject]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# GoatShot Installer Package Verification")
    [void]$lines.Add("")
    [void]$lines.Add("Installer script: ``$($Result.installerScript)``")
    [void]$lines.Add("Package script: ``$($Result.packageScript)``")
    [void]$lines.Add("Expected installer: ``$($Result.installerPath)``")
    [void]$lines.Add("Succeeded: ``$($Result.succeeded)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Inno Setup")
    [void]$lines.Add("")
    [void]$lines.Add("- Compiler available: ``$($Result.inno.compilerAvailable)``")
    [void]$lines.Add("- Compiler path: ``$($Result.inno.compilerPath)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Script Checks")
    [void]$lines.Add("")
    foreach ($check in $Result.scriptChecks) {
        [void]$lines.Add("- ``$($check.name)`` passed=``$($check.passed)``")
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Installer Artifact")
    [void]$lines.Add("")
    [void]$lines.Add("- Exists: ``$($Result.installer.exists)``")
    [void]$lines.Add("- Length: ``$($Result.installer.length)``")
    [void]$lines.Add("- SHA256: ``$($Result.installer.sha256)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Build")
    [void]$lines.Add("")
    [void]$lines.Add("- Requested: ``$($Result.build.requested)``")
    [void]$lines.Add("- Status: ``$($Result.build.status)``")
    [void]$lines.Add("- Exit code: ``$($Result.build.exitCode)``")
    [void]$lines.Add("- Log: ``$($Result.build.log)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Silent Install Smoke")
    [void]$lines.Add("")
    [void]$lines.Add("- Requested: ``$($Result.installSmoke.requested)``")
    [void]$lines.Add("- Status: ``$($Result.installSmoke.status)``")
    [void]$lines.Add("- Install root: ``$($Result.installSmoke.installRoot)``")
    [void]$lines.Add("- Log: ``$($Result.installSmoke.log)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Warnings")
    [void]$lines.Add("")
    if ($Result.warnings.Count -eq 0) {
        [void]$lines.Add("- None.")
    }
    else {
        foreach ($warning in $Result.warnings) {
            [void]$lines.Add("- $warning")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Issues")
    [void]$lines.Add("")
    if ($Result.issues.Count -eq 0) {
        [void]$lines.Add("- None.")
    }
    else {
        foreach ($issue in $Result.issues) {
            [void]$lines.Add("- $issue")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Boundary")
    [void]$lines.Add("")
    [void]$lines.Add("This verifier is command/static evidence. Clean Windows VM/profile proof and human GUI install/uninstall observation remain manual unless a silent install smoke was explicitly requested and reviewed.")

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

$packageScriptFullPath = Resolve-FullPath $PackageScript
$installerScriptFullPath = Resolve-FullPath $InstallerScript
$publishFullPath = Resolve-FullPath $PublishDir
$distFullPath = Resolve-FullPath $DistRoot
$outputFullPath = Resolve-FullPath $OutputRoot
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$issues = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $packageScriptFullPath -PathType Leaf)) {
    Add-Message $issues "Package script was not found: $packageScriptFullPath"
}

$scriptChecks = Test-InnoScript -PathValue $installerScriptFullPath -Issues $issues
$innoCompiler = Find-InnoCompiler
if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
    Add-Message $warnings "Inno Setup compiler was not found. Install Inno Setup 6 or set INNO_SETUP_ISCC to build the installer."
}

$expectedInstaller = if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    Join-Path $distFullPath "GoatShot-Setup-$Version-win-x64.exe"
}
else {
    Resolve-FullPath $InstallerPath
}

$buildStatus = "skipped"
$buildExitCode = $null
$buildLog = Join-Path $outputFullPath "installer-build.log"

if ($BuildInstaller) {
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        $buildStatus = "blocked-missing-inno"
        Add-Message $warnings "BuildInstaller was requested, but no Inno Setup compiler was available."
    }
    elseif ($issues.Count -eq 0) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageScriptFullPath -Configuration Release -Runtime win-x64 -Version $Version *> $buildLog
        $buildExitCode = $LASTEXITCODE
        if ($null -eq $buildExitCode) {
            $buildExitCode = 0
        }

        $buildStatus = if ($buildExitCode -eq 0) { "passed" } else { "failed" }
        if ($buildExitCode -ne 0) {
            Add-Message $issues "Installer build failed with exit code $buildExitCode. See $buildLog"
        }
    }
}

$installerExists = Test-Path -LiteralPath $expectedInstaller -PathType Leaf
$installerLength = if ($installerExists) { (Get-Item -LiteralPath $expectedInstaller).Length } else { 0 }
$installerSha256 = if ($installerExists) { (Get-FileHash -LiteralPath $expectedInstaller -Algorithm SHA256).Hash } else { "" }

if ($installerExists -and $installerLength -le 0) {
    Add-Message $issues "Installer artifact is empty: $expectedInstaller"
}
elseif (-not $installerExists) {
    Add-Message $warnings "Installer artifact was not found: $expectedInstaller"
}

$installSmokeStatus = "skipped"
$installSmokeLog = Join-Path $outputFullPath "silent-install-smoke.log"
$installRoot = Join-Path $outputFullPath "silent-install-root"

if ($RunSilentInstallSmoke) {
    if (-not $installerExists) {
        $installSmokeStatus = "blocked-missing-installer"
        Add-Message $issues "Silent install smoke was requested, but the installer artifact was not found."
    }
    else {
        New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
        & $expectedInstaller /VERYSILENT /SUPPRESSMSGBOXES /NORESTART "/DIR=$installRoot" "/LOG=$installSmokeLog"
        $installExit = $LASTEXITCODE
        if ($null -eq $installExit) {
            $installExit = 0
        }

        if ($installExit -ne 0) {
            $installSmokeStatus = "failed-install"
            Add-Message $issues "Silent install failed with exit code $installExit. See $installSmokeLog"
        }
        else {
            $installedExe = Join-Path $installRoot "GoatShot.exe"
            if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
                $installSmokeStatus = "failed-missing-exe"
                Add-Message $issues "Silent install did not create GoatShot.exe under $installRoot"
            }
            else {
                $uninstaller = Get-ChildItem -LiteralPath $installRoot -Filter "unins*.exe" -File | Select-Object -First 1
                if ($null -eq $uninstaller) {
                    $installSmokeStatus = "failed-missing-uninstaller"
                    Add-Message $issues "Silent install did not create an Inno uninstaller under $installRoot"
                }
                else {
                    & $uninstaller.FullName /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
                    $uninstallExit = $LASTEXITCODE
                    if ($null -eq $uninstallExit) {
                        $uninstallExit = 0
                    }

                    if ($uninstallExit -ne 0) {
                        $installSmokeStatus = "failed-uninstall"
                        Add-Message $issues "Silent uninstall failed with exit code $uninstallExit."
                    }
                    else {
                        $installSmokeStatus = "passed"
                    }
                }
            }
        }
    }
}

$result = [pscustomobject]@{
    generatedAt = (Get-Date).ToString("o")
    succeeded = $issues.Count -eq 0
    packageScript = $packageScriptFullPath
    installerScript = $installerScriptFullPath
    publishDir = $publishFullPath
    distRoot = $distFullPath
    installerPath = $expectedInstaller
    inno = [pscustomobject]@{
        compilerAvailable = -not [string]::IsNullOrWhiteSpace($innoCompiler)
        compilerPath = if ($innoCompiler) { $innoCompiler } else { "" }
    }
    scriptChecks = @($scriptChecks)
    installer = [pscustomobject]@{
        exists = $installerExists
        length = $installerLength
        sha256 = $installerSha256
    }
    build = [pscustomobject]@{
        requested = [bool]$BuildInstaller
        status = $buildStatus
        exitCode = $buildExitCode
        log = if (Test-Path -LiteralPath $buildLog -PathType Leaf) { $buildLog } else { "" }
    }
    installSmoke = [pscustomobject]@{
        requested = [bool]$RunSilentInstallSmoke
        status = $installSmokeStatus
        installRoot = if ($RunSilentInstallSmoke) { $installRoot } else { "" }
        log = if (Test-Path -LiteralPath $installSmokeLog -PathType Leaf) { $installSmokeLog } else { "" }
    }
    warnings = @($warnings)
    issues = @($issues)
}

$jsonPath = Join-Path $outputFullPath "installer-package-verification.json"
$markdownPath = Join-Path $outputFullPath "installer-package-verification.md"
Write-TextFile -PathValue $jsonPath -Content ($result | ConvertTo-Json -Depth 8)
Write-TextFile -PathValue $markdownPath -Content (Build-Markdown -Result $result)

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    Write-Output "Installer verification written to $markdownPath"
}

if ($issues.Count -gt 0) {
    exit 1
}

exit 0

param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [string]$DistDir = "",
    [string]$PublishDir = "",
    [string]$OutputRoot = "artifacts\single-exe-package-verification",
    # A correct self-contained single-file GoatShot.exe measured ~102 MB for
    # release 0.1.0; a build under this floor has almost certainly failed to
    # embed the runtime or native libraries.
    [long]$MinimumExeBytes = 60MB,
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

function Get-FileSha256Hex {
    param([string]$PathValue)

    # Get-FileHash is a script function that goes missing when powershell.exe
    # inherits a pwsh 7 PSModulePath (as under the test harness), so hash via
    # .NET directly.
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($PathValue)
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace("-", "")
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $sha256.Dispose()
    }
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

function Build-Markdown {
    param([pscustomobject]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# GoatShot Single-Exe Package Verification")
    [void]$lines.Add("")
    [void]$lines.Add("Dist dir: ``$($Result.distDir)``")
    [void]$lines.Add("Publish dir: ``$($Result.publishDir)``")
    [void]$lines.Add("Succeeded: ``$($Result.succeeded)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Artifact")
    [void]$lines.Add("")
    [void]$lines.Add("- ``GoatShot.exe`` exists=``$($Result.exe.exists)`` length=``$($Result.exe.length)`` minimum=``$($Result.exe.minimumLength)``")
    [void]$lines.Add("- ``GoatShot.exe`` sha256=``$($Result.exe.sha256)``")
    [void]$lines.Add("- ``GoatShot.pdb`` exists=``$($Result.pdb.exists)`` length=``$($Result.pdb.length)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Unexpected Dist Entries")
    [void]$lines.Add("")
    if ($Result.unexpectedDistEntries.Count -eq 0) {
        [void]$lines.Add("- None.")
    }
    else {
        foreach ($entry in $Result.unexpectedDistEntries) {
            [void]$lines.Add("- ``$entry``")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Loose Native Libraries (publish dir)")
    [void]$lines.Add("")
    if ($Result.looseNativeLibraries.Count -eq 0) {
        [void]$lines.Add("- None.")
    }
    else {
        foreach ($entry in $Result.looseNativeLibraries) {
            [void]$lines.Add("- ``$entry``")
        }
    }
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
    [void]$lines.Add("This verifier is static evidence. Launching GoatShot.exe on a clean machine and observing startup (the historical DllNotFoundException regression) remains a manual check.")

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = "artifacts\dist\GoatShot-$Version-$Runtime-single-exe"
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "artifacts\publish\GoatShot-$Runtime-single-exe"
}

$distFullPath = Resolve-FullPath $DistDir
$publishFullPath = Resolve-FullPath $PublishDir
$outputFullPath = Resolve-FullPath $OutputRoot
New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

$issues = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$unexpectedDistEntries = New-Object System.Collections.Generic.List[string]
$looseNativeLibraries = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $distFullPath -PathType Container)) {
    Add-Message $issues "Single-exe dist directory was not found: $distFullPath"
}

$exePath = Join-Path $distFullPath "GoatShot.exe"
$pdbPath = Join-Path $distFullPath "GoatShot.pdb"

$exeExists = Test-Path -LiteralPath $exePath -PathType Leaf
$exeLength = if ($exeExists) { (Get-Item -LiteralPath $exePath).Length } else { 0 }
$exeSha256 = if ($exeExists) { Get-FileSha256Hex $exePath } else { "" }

if (-not $exeExists) {
    Add-Message $issues "GoatShot.exe was not found: $exePath"
}
elseif ($exeLength -lt $MinimumExeBytes) {
    Add-Message $issues "GoatShot.exe is smaller than expected for a self-contained single-file build ($exeLength bytes < $MinimumExeBytes bytes). The runtime or native libraries are likely not embedded."
}

$pdbExists = Test-Path -LiteralPath $pdbPath -PathType Leaf
$pdbLength = if ($pdbExists) { (Get-Item -LiteralPath $pdbPath).Length } else { 0 }

if (-not $pdbExists) {
    Add-Message $issues "GoatShot.pdb was not found: $pdbPath"
}
elseif ($pdbLength -le 0) {
    Add-Message $issues "GoatShot.pdb is empty: $pdbPath"
}

if (Test-Path -LiteralPath $distFullPath -PathType Container) {
    $allowedDistEntries = @("GoatShot.exe", "GoatShot.pdb")
    foreach ($entry in Get-ChildItem -LiteralPath $distFullPath) {
        if ($allowedDistEntries -notcontains $entry.Name) {
            [void]$unexpectedDistEntries.Add($entry.Name)
        }
    }

    if ($unexpectedDistEntries.Count -gt 0) {
        Add-Message $issues ("Single-exe dist directory contains unexpected entries: " + ($unexpectedDistEntries -join ", "))
    }
}

if (Test-Path -LiteralPath $publishFullPath -PathType Container) {
    foreach ($dll in Get-ChildItem -LiteralPath $publishFullPath -Filter "*.dll" -File) {
        [void]$looseNativeLibraries.Add($dll.Name)
    }

    if ($looseNativeLibraries.Count -gt 0) {
        Add-Message $issues ("Loose libraries remain beside the published single exe, so they were not embedded (IncludeNativeLibrariesForSelfExtract regression; the app would crash at startup): " + ($looseNativeLibraries -join ", "))
    }
}
else {
    Add-Message $warnings "Single-exe publish directory was not found, so the loose-native-library check was skipped: $publishFullPath"
}

$result = [pscustomobject]@{
    generatedAt = (Get-Date).ToString("o")
    succeeded = $issues.Count -eq 0
    distDir = $distFullPath
    publishDir = $publishFullPath
    exe = [pscustomobject]@{
        path = $exePath
        exists = $exeExists
        length = $exeLength
        minimumLength = $MinimumExeBytes
        sha256 = $exeSha256
    }
    pdb = [pscustomobject]@{
        path = $pdbPath
        exists = $pdbExists
        length = $pdbLength
    }
    unexpectedDistEntries = @($unexpectedDistEntries)
    looseNativeLibraries = @($looseNativeLibraries)
    warnings = @($warnings)
    issues = @($issues)
}

$jsonPath = Join-Path $outputFullPath "single-exe-package-verification.json"
$markdownPath = Join-Path $outputFullPath "single-exe-package-verification.md"
Write-TextFile -PathValue $jsonPath -Content ($result | ConvertTo-Json -Depth 8)
Write-TextFile -PathValue $markdownPath -Content (Build-Markdown -Result $result)

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    if ($result.succeeded) {
        Write-Host "Single-exe package verification passed: $jsonPath"
    }
    else {
        Write-Host "Single-exe package verification failed: $jsonPath"
        foreach ($issue in $issues) {
            Write-Host "ISSUE: $issue"
        }
    }
}

if (-not $result.succeeded) {
    exit 1
}

exit 0

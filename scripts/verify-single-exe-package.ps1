param(
    [string]$Version = "0.2.0",
    [string]$Runtime = "win-x64",
    [string]$DistDir = "",
    [string]$PublishDir = "",
    [string]$OutputRoot = "artifacts\single-exe-package-verification",
    # A correct self-contained single-file GoatShot.exe measured ~102 MB for
    # release 0.1.0; a build under this floor has almost certainly failed to
    # embed the runtime or native libraries.
    [long]$MinimumExeBytes = 60MB,
    [switch]$SkipMetadataChecks,
    [switch]$SkipRuntimeSmoke,
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
    [void]$lines.Add("- ``$($Result.exe.name)`` exists=``$($Result.exe.exists)`` length=``$($Result.exe.length)`` minimum=``$($Result.exe.minimumLength)``")
    [void]$lines.Add("- executable sha256=``$($Result.exe.sha256)``")
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
    [void]$lines.Add("This verifier proves the one-executable layout and publish embedding. Clean-profile Windows Sandbox operation remains a separate operator-observed proof lane.")

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

if ([string]::IsNullOrWhiteSpace($DistDir)) {
    $DistDir = "artifacts\dist"
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = "artifacts\publish\GoatShot-$Runtime-single-exe"
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
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

$exeName = "GoatShot-$Version-$Runtime.exe"
$exePath = Join-Path $distFullPath $exeName

$exeExists = Test-Path -LiteralPath $exePath -PathType Leaf
$exeLength = if ($exeExists) { (Get-Item -LiteralPath $exePath).Length } else { 0 }
$exeSha256 = if ($exeExists) { Get-FileSha256Hex $exePath } else { "" }

if (-not $exeExists) {
    Add-Message $issues "$exeName was not found: $exePath"
}
elseif ($exeLength -lt $MinimumExeBytes) {
    Add-Message $issues "GoatShot.exe is smaller than expected for a self-contained single-file build ($exeLength bytes < $MinimumExeBytes bytes). The runtime or native libraries are likely not embedded."
}

$checksumPath = "$exePath.sha256"
$metadataPath = Join-Path $distFullPath "GoatShot-$Version-$Runtime.build.json"
$noticesPath = Join-Path $distFullPath "GoatShot-$Version-THIRD-PARTY-NOTICES.txt"
$sbomPath = Join-Path $distFullPath "GoatShot-$Version-$Runtime.spdx.json"
if (-not $SkipMetadataChecks) {
    foreach ($required in @($checksumPath, $metadataPath, $noticesPath, $sbomPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { Add-Message $issues "Required release companion is missing: $required" }
    }
    if (Test-Path -LiteralPath $checksumPath) {
        $declaredHash = ((Get-Content -Raw -LiteralPath $checksumPath).Trim() -split '\s+')[0]
        if ($declaredHash -ne $exeSha256.ToLowerInvariant()) { Add-Message $issues "Published SHA-256 does not match the executable." }
    }
    if (Test-Path -LiteralPath $metadataPath) {
        $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
        if ($metadata.version -ne $Version) { Add-Message $issues "Build metadata version $($metadata.version) does not match $Version." }
        if ($metadata.executableSha256 -ne $exeSha256.ToLowerInvariant()) { Add-Message $issues "Build metadata SHA-256 does not match the executable." }
        if ([string]::IsNullOrWhiteSpace($metadata.buildId) -or [string]::IsNullOrWhiteSpace($metadata.embeddedAssetManifestSha256)) { Add-Message $issues "Build metadata is missing build or embedded-manifest identity." }
    }
    if (Test-Path -LiteralPath $noticesPath) {
        $notices = Get-Content -Raw -LiteralPath $noticesPath
        if ($notices.Contains('$(')) {
            Add-Message $issues "Third-party notices contain an unexpanded PowerShell expression."
        }

        $assetLockPath = Join-Path $repoRoot "packaging\embedded-assets.lock.json"
        if (Test-Path -LiteralPath $assetLockPath) {
            $assetLock = Get-Content -Raw -LiteralPath $assetLockPath | ConvertFrom-Json
            foreach ($expectedNoticeValue in @(
                $assetLock.ffmpeg.version,
                $assetLock.ffmpeg.license,
                $assetLock.ffmpeg.build,
                $assetLock.personSegmentation.version,
                $assetLock.personSegmentation.license)) {
                if (-not [string]::IsNullOrWhiteSpace($expectedNoticeValue) -and
                    $notices.IndexOf([string]$expectedNoticeValue, [StringComparison]::Ordinal) -lt 0) {
                    Add-Message $issues "Third-party notices do not identify locked asset value: $expectedNoticeValue"
                }
            }
        }
    }
}

$runtimeSmoke = [ordered]@{ attempted = $false; diagnosticsExitCode = $null; repairExitCode = $null; succeeded = $false; runtimeDirectory = ""; message = "Skipped." }
if ($exeExists -and -not $SkipRuntimeSmoke) {
    $runtimeSmoke.attempted = $true
    $isolationRoot = Join-Path $outputFullPath ("isolated-runtime-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $isolationRoot | Out-Null
    function Invoke-GoatShotRuntime([string[]] $Arguments) {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $exePath
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.CreateNoWindow = $true
        $start.EnvironmentVariables["GOATSHOT_LOCAL_ROOT"] = (Join-Path $isolationRoot "local")
        $start.EnvironmentVariables["GOATSHOT_LIBRARY_ROOT"] = (Join-Path $isolationRoot "library")
        $start.EnvironmentVariables["GOATSHOT_STARTUP_TRACE"] = (Join-Path $isolationRoot "startup-trace.log")
        $start.Arguments = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_.Replace('"', '\"') + '"' } else { $_ } }) -join ' '
        $process = [Diagnostics.Process]::Start($start)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            try { $process.Kill() } catch {}
            throw "GoatShot runtime smoke timed out."
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdoutTask.Result; StdErr = $stderrTask.Result }
    }
    try {
        $diagnostics = Invoke-GoatShotRuntime @("--runtime-diagnostics")
        $runtimeSmoke.diagnosticsExitCode = $diagnostics.ExitCode
        if ($diagnostics.ExitCode -ne 0) { throw "Runtime diagnostics failed: $($diagnostics.StdErr)" }
        $diagnosticsJson = $diagnostics.StdOut | ConvertFrom-Json
        $runtimeDirectory = $diagnosticsJson.bundledRuntime.runtimeDirectory
        $runtimeSmoke.runtimeDirectory = $runtimeDirectory
        $ffmpeg = Join-Path $runtimeDirectory "tools\ffmpeg\ffmpeg.exe"
        if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "Bundled FFmpeg was not extracted during isolated launch." }
        [IO.File]::WriteAllBytes($ffmpeg, [byte[]](0x00, 0x01, 0x02))
        $repair = Invoke-GoatShotRuntime @("--repair", "--runtime-only")
        $runtimeSmoke.repairExitCode = $repair.ExitCode
        if ($repair.ExitCode -ne 0) { throw "Runtime repair failed: $($repair.StdErr)" }
        $postRepair = Invoke-GoatShotRuntime @("--runtime-diagnostics")
        $postRepairJson = $postRepair.StdOut | ConvertFrom-Json
        if (-not $postRepairJson.bundledRuntime.succeeded) { throw "Runtime remained unhealthy after repair." }
        $runtimeSmoke.succeeded = $true
        $runtimeSmoke.message = "Isolated launch extracted assets and repaired a deliberately corrupted FFmpeg executable."
    }
    catch {
        $runtimeSmoke.message = $_.Exception.Message
        Add-Message $issues "Runtime smoke failed: $($runtimeSmoke.message)"
    }
}

if (Test-Path -LiteralPath $distFullPath -PathType Container) {
    foreach ($entry in Get-ChildItem -LiteralPath $distFullPath -File) {
        if ($entry.Extension -in @(".exe", ".dll", ".pdb") -and $entry.Name -ne $exeName) {
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
        name = $exeName
        path = $exePath
        exists = $exeExists
        length = $exeLength
        minimumLength = $MinimumExeBytes
        sha256 = $exeSha256
    }
    unexpectedDistEntries = @($unexpectedDistEntries)
    looseNativeLibraries = @($looseNativeLibraries)
    runtimeSmoke = $runtimeSmoke
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

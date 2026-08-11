param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.3.0",
    [string] $OutputRoot = "",
    [switch] $SkipCommands,
    [switch] $SkipTrancheNotes,
    [string[]] $AdditionalArtifactPath = @()
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex([string] $PathValue) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($PathValue)
        try { return ([BitConverter]::ToString($sha256.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $sha256.Dispose() }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\release-proof"
}
elseif ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    $OutputRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputRoot))
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.3.0"
}

$bundleContentRoot = Join-Path $OutputRoot "bundle-content"
$commandLogRoot = Join-Path $bundleContentRoot "command-logs"
$sourceSnapshotRoot = Join-Path $bundleContentRoot "source-snapshots"
$trancheNoteRoot = Join-Path $bundleContentRoot "tranche-notes"
$diagnosticsRoot = Join-Path $bundleContentRoot "diagnostics"
$additionalRoot = Join-Path $bundleContentRoot "additional-artifacts"
$manifestPath = Join-Path $OutputRoot "manifest.json"
$zipPath = Join-Path $OutputRoot ("Receipts-release-proof-{0}-{1}.zip" -f $Version, (Get-Date -Format "yyyyMMdd-HHmmss"))

if (Test-Path $bundleContentRoot) {
    Remove-Item -LiteralPath $bundleContentRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot, $bundleContentRoot, $commandLogRoot, $sourceSnapshotRoot, $trancheNoteRoot, $diagnosticsRoot, $additionalRoot | Out-Null

$commands = New-Object System.Collections.Generic.List[object]
$includedFiles = New-Object System.Collections.Generic.List[string]
$excludedByPolicy = New-Object System.Collections.Generic.List[string]

function Convert-ToRelativePath {
    param([string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $basePath = [System.IO.Path]::GetFullPath($OutputRoot)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }

    if ($fullPath.StartsWith($basePath, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($basePath.Length).Replace('\', '/')
    }

    return $fullPath
}

function Add-IncludedFile {
    param([string] $Path)
    [void]$includedFiles.Add((Convert-ToRelativePath -Path $Path))
}

function Redact-Text {
    param([string] $Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $redacted = $Text
    foreach ($privateRoot in @([string]$repoRoot, [string]$OutputRoot, [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile))) {
        if (-not [string]::IsNullOrWhiteSpace($privateRoot)) {
            $replacement = if ($privateRoot -eq [string]$repoRoot) {
                "[REPO]"
            }
            elseif ($privateRoot -eq [string]$OutputRoot) {
                "[OUTPUT]"
            }
            else {
                "%USERPROFILE%"
            }
            foreach ($privateRootForm in @($privateRoot.Replace('\', '\\'), $privateRoot)) {
                $redacted = [regex]::Replace(
                    $redacted,
                    [regex]::Escape($privateRootForm),
                    $replacement,
                    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            }
        }
    }
    $redacted = [regex]::Replace($redacted, '(?i)(Authorization\s*[:=]\s*Bearer\s+)[^\s,;"]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(Authorization\s*[:=]\s*Basic\s+)[^\s,;"]+', '$1[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)(access_token|refresh_token|id_token|api_key|apikey|password|secret|client_secret|token|authorization_code|oauth_code)=([^&\s]+)', '$1=[REDACTED]')
    $redacted = [regex]::Replace($redacted, '(?i)("?(accessToken|refreshToken|apiKey|password|secret|clientSecret|token)"?\s*:\s*")([^"]+)(")', '$1[REDACTED]$4')
    $redacted = [regex]::Replace($redacted, '(?i)(https?://[^\s"<>?]+)\?([^\s"<>]+)', {
            param($match)
            $url = $match.Groups[1].Value
            $query = $match.Groups[2].Value
            $safeQuery = [regex]::Replace($query, '(?i)(sig|signature|token|access_token|refresh_token|session|code|key|secret|password)=([^&]+)', '$1=[REDACTED]')
            return "${url}?$safeQuery"
        })
    return $redacted
}

function Test-TextArtifact {
    param([string] $Path)

    $extension = [System.IO.Path]::GetExtension($Path)
    return @(".txt", ".md", ".json", ".log", ".csv", ".xml", ".ps1", ".sha256") -contains $extension.ToLowerInvariant()
}

function Test-ExcludedArtifact {
    param([string] $Path)

    $normalized = $Path.ToLowerInvariant()
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if (@(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wav", ".mp3", ".m4a") -contains $extension) {
        return "media payloads are excluded from release proof bundles"
    }

    if ($normalized.Contains("\secrets\") -or $normalized.Contains("/secrets/") -or $normalized.EndsWith(".secret") -or $normalized.EndsWith(".dpapi")) {
        return "secret and DPAPI files are excluded"
    }

    if ($normalized.Contains("\recordings\") -or $normalized.Contains("/recordings/") -or
        $normalized.Contains("\thumbnails\") -or $normalized.Contains("/thumbnails/") -or
        $normalized.Contains("\images\") -or $normalized.Contains("/images/")) {
        return "capture, recording, image, and thumbnail trees are excluded"
    }

    return $null
}

function Copy-TextArtifact {
    param(
        [string] $Source,
        [string] $Destination
    )

    $exclusion = Test-ExcludedArtifact -Path $Source
    if ($exclusion) {
        [void]$excludedByPolicy.Add("$Source - $exclusion")
        return
    }

    if (-not (Test-TextArtifact -Path $Source)) {
        [void]$excludedByPolicy.Add("$Source - non-text artifact not allowlisted")
        return
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    $text = Get-Content -LiteralPath $Source -Raw -ErrorAction Stop
    Set-Content -LiteralPath $Destination -Value (Redact-Text -Text $text) -Encoding UTF8
    Add-IncludedFile -Path $Destination
}

function Convert-ToProcessArgumentText {
    param([string[]] $Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            '""'
            continue
        }

        $value = [string]$argument
        if ($value.Length -gt 0 -and $value -notmatch '[\s"]') {
            $value
            continue
        }

        $escaped = $value -replace '(\\*)"', '$1$1\"'
        $escaped = $escaped -replace '(\\+)$', '$1$1'
        '"' + $escaped + '"'
    }

    $quoted -join ' '
}

function Invoke-ProofCommand {
    param(
        [string] $Name,
        [string] $FileName,
        [string[]] $Arguments,
        [string] $LogFile
    )

    if ($SkipCommands) {
        Set-Content -LiteralPath $LogFile -Value "Skipped by -SkipCommands." -Encoding UTF8
        [void]$commands.Add([pscustomobject]@{
                name = $Name
                fileName = $FileName
                arguments = $Arguments
                exitCode = $null
                status = "skipped"
                log = Convert-ToRelativePath -Path $LogFile
            })
        Add-IncludedFile -Path $LogFile
        return
    }

    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $FileName
    $processInfo.WorkingDirectory = $repoRoot
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    if ($null -ne $processInfo.ArgumentList) {
        foreach ($argument in $Arguments) {
            [void]$processInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $processInfo.Arguments = Convert-ToProcessArgumentText -Arguments $Arguments
    }

    $process = [Diagnostics.Process]::Start($processInfo)
    # Drain stderr asynchronously: sequential ReadToEnd calls deadlock once the child
    # fills the ~4 KB stderr pipe while stdout is still being read (dotnet test spew).
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $process.WaitForExit()

    $combined = @(
        "> $FileName $($Arguments -join ' ')",
        "",
        "exitCode=$($process.ExitCode)",
        "",
        "STDOUT:",
        $stdout,
        "",
        "STDERR:",
        $stderr
    ) -join [Environment]::NewLine

    Set-Content -LiteralPath $LogFile -Value (Redact-Text -Text $combined) -Encoding UTF8
    [void]$commands.Add([pscustomobject]@{
            name = $Name
            fileName = $FileName
            arguments = $Arguments
            exitCode = $process.ExitCode
            status = if ($process.ExitCode -eq 0) { "passed" } else { "failed" }
            log = Convert-ToRelativePath -Path $LogFile
        })
    Add-IncludedFile -Path $LogFile

    if ($process.ExitCode -ne 0) {
        throw "$Name failed with exit code $($process.ExitCode). See $LogFile."
    }
}

Copy-TextArtifact -Source (Join-Path $repoRoot "README.md") -Destination (Join-Path $sourceSnapshotRoot "README.md")
Copy-TextArtifact -Source (Join-Path $repoRoot "spec.md") -Destination (Join-Path $sourceSnapshotRoot "spec.md")

$trancheNoteSources = @()
$artifactRoot = Join-Path $repoRoot "artifacts"
if (-not $SkipTrancheNotes -and (Test-Path $artifactRoot)) {
    $trancheNoteSources = @(Get-ChildItem -LiteralPath $artifactRoot -Directory -Filter "tranche-*" |
            ForEach-Object {
                $note = Join-Path $_.FullName "notes.md"
                if (Test-Path $note) { Get-Item -LiteralPath $note }
            } |
            Sort-Object FullName)
}

foreach ($note in $trancheNoteSources) {
    Copy-TextArtifact -Source $note.FullName -Destination (Join-Path $trancheNoteRoot "$($note.Directory.Name).md")
}

foreach ($additionalEntry in $AdditionalArtifactPath) {
    $additionalItems = @($additionalEntry -split ',' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($additional in $additionalItems) {
    if ([string]::IsNullOrWhiteSpace($additional)) {
        continue
    }

    $resolved = if ([System.IO.Path]::IsPathRooted($additional)) {
        [System.IO.Path]::GetFullPath($additional)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $additional))
    }
    if (-not (Test-Path $resolved)) {
        [void]$excludedByPolicy.Add("$resolved - requested additional artifact was missing")
        continue
    }

    $name = [System.IO.Path]::GetFileName($resolved)
    Copy-TextArtifact -Source $resolved -Destination (Join-Path $additionalRoot $name)
    }
}

$buildLog = Join-Path $commandLogRoot "build-release.txt"
Invoke-ProofCommand -Name "Release build" -FileName "dotnet" -Arguments @("build", ".\GoatShot.slnx", "-c", $Configuration) -LogFile $buildLog

$testLog = Join-Path $commandLogRoot "test-release.txt"
Invoke-ProofCommand -Name "Release tests" -FileName "dotnet" -Arguments @("test", ".\GoatShot.slnx", "-c", $Configuration, "--logger", "console;verbosity=normal") -LogFile $testLog

$cliPath = Join-Path $repoRoot "src\GoatShot.Cli\bin\$Configuration\net10.0-windows10.0.19041.0\Receipts.Cli.exe"
$env:RECEIPTS_LOCAL_ROOT = Join-Path $OutputRoot "cli-local"
$env:RECEIPTS_LIBRARY_ROOT = Join-Path $OutputRoot "cli-library"
New-Item -ItemType Directory -Force -Path $env:RECEIPTS_LOCAL_ROOT, $env:RECEIPTS_LIBRARY_ROOT | Out-Null

Invoke-ProofCommand -Name "CLI help" -FileName $cliPath -Arguments @("--help") -LogFile (Join-Path $commandLogRoot "cli-help.txt")
Invoke-ProofCommand -Name "CLI diagnostics print" -FileName $cliPath -Arguments @("diagnostics", "print") -LogFile (Join-Path $commandLogRoot "diagnostics-print.txt")
Invoke-ProofCommand -Name "CLI diagnostics bundle" -FileName $cliPath -Arguments @("diagnostics", "bundle", "--output", (Join-Path $diagnosticsRoot "receipts-diagnostics.zip")) -LogFile (Join-Path $commandLogRoot "diagnostics-bundle.txt")

Invoke-ProofCommand -Name "Receipts release package" -FileName "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\package-release.ps1", "-Configuration", $Configuration, "-Runtime", $Runtime, "-Version", $Version) -LogFile (Join-Path $commandLogRoot "package-release.txt")
Invoke-ProofCommand -Name "Receipts portable verification" -FileName "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\verify-portable-package.ps1", "-PackagePath", ".\artifacts\dist\Receipts-$Version-$Runtime-portable.zip", "-RunCliSmoke") -LogFile (Join-Path $commandLogRoot "verify-portable-package.txt")
Invoke-ProofCommand -Name "Receipts single-exe verification" -FileName "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\verify-single-exe-package.ps1", "-Version", $Version, "-Runtime", $Runtime) -LogFile (Join-Path $commandLogRoot "verify-single-exe-package.txt")
Invoke-ProofCommand -Name "Receipts installer verification" -FileName "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\verify-installer-package.ps1", "-Version", $Version) -LogFile (Join-Path $commandLogRoot "verify-installer-package.txt")

$distExe = Join-Path $repoRoot "artifacts\dist\Receipts-$Version-$Runtime-single-exe.exe"
if (Test-Path $distExe) {
    $packageInfoPath = Join-Path $bundleContentRoot "package.json"
    $packageFile = Get-Item -LiteralPath $distExe
    ConvertTo-Json -Depth 4 -InputObject ([pscustomobject]@{
            executable = $packageFile.Name
            bytes = $packageFile.Length
            sha256 = Get-Sha256Hex $packageFile.FullName
            lastWriteTimeUtc = $packageFile.LastWriteTimeUtc
        }) | Set-Content -LiteralPath $packageInfoPath -Encoding UTF8
    Add-IncludedFile -Path $packageInfoPath
}

$unverifiedLanes = @(
    "Live Google Drive OAuth consent and account upload proof",
    "Live Google Photos OAuth consent and media upload proof",
    "Live Dropbox OAuth consent and account upload proof",
    "Live OneDrive OAuth consent and account upload proof",
    "Live YouTube OAuth consent and video upload proof",
    "Live OneNote OAuth consent and page export proof",
    "Refresh-token expiry recovery against live cloud accounts",
    "Live keyboard/screen-reader/high-contrast accessibility pass",
    "Live multi-monitor and long recording proof with safe desktop content",
    "Clean-profile Windows Sandbox install, update, repair, and uninstall proof"
)

$bundleManifestPath = Join-Path $bundleContentRoot "manifest.json"
Add-IncludedFile -Path $bundleManifestPath

$manifest = [pscustomobject]@{
    manifestVersion = 1
    generatedAt = Get-Date -Format o
    application = "Receipts"
    version = $Version
    configuration = $Configuration
    runtime = $Runtime
    repoRoot = "."
    outputRoot = "."
    zipPath = [System.IO.Path]::GetFileName($zipPath)
    commands = $commands
    includedFiles = @($includedFiles)
    excludedByPolicy = @($excludedByPolicy)
    privacyBoundary = "Release proof bundle includes allowlisted command status, public source snapshots, verifier output, release companions, and package metadata. It excludes capture media, thumbnails, OCR text dumps, AI payloads, DPAPI secret files, raw tokens, upload session URLs, private desktop recordings, and local tranche caches."
    unverifiedLanes = $unverifiedLanes
}

$manifestJson = Redact-Text -Text (ConvertTo-Json -InputObject $manifest -Depth 12)
Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8
Copy-Item -LiteralPath $manifestPath -Destination $bundleManifestPath -Force

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $bundleContentRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

[pscustomobject]@{
    Manifest = $manifestPath
    Zip = $zipPath
    OutputRoot = $OutputRoot
    CommandCount = $commands.Count
    IncludedFileCount = $includedFiles.Count
    ExcludedByPolicyCount = $excludedByPolicy.Count
}

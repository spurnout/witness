param(
    [string]$PackagePath = "artifacts\dist\Receipts-0.3.0-win-x64-portable.zip",
    [string]$OutputRoot = "artifacts\portable-package-verification",
    [switch]$RunCliSmoke,
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

function Add-Issue {
    param(
        [System.Collections.Generic.List[string]]$Issues,
        [string]$Message
    )

    [void]$Issues.Add($Message)
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

function Invoke-PackagedCli {
    param(
        [string]$CliPath,
        [string[]]$Arguments,
        [string]$LogPath,
        [string]$CleanLocalRoot,
        [string]$CleanLibraryRoot
    )

    $oldReceiptsLocal = $env:RECEIPTS_LOCAL_ROOT
    $oldReceiptsLibrary = $env:RECEIPTS_LIBRARY_ROOT
    $oldGoatShotLocal = $env:GOATSHOT_LOCAL_ROOT
    $oldGoatShotLibrary = $env:GOATSHOT_LIBRARY_ROOT
    try {
        $env:RECEIPTS_LOCAL_ROOT = $CleanLocalRoot
        $env:RECEIPTS_LIBRARY_ROOT = $CleanLibraryRoot
        # Set both names to the same isolated roots so this verifier is safe
        # against both Receipts builds and pre-migration compatibility builds.
        $env:GOATSHOT_LOCAL_ROOT = $CleanLocalRoot
        $env:GOATSHOT_LIBRARY_ROOT = $CleanLibraryRoot
        & $CliPath @Arguments *> $LogPath
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) {
            $exitCode = 0
        }

        return [pscustomobject]@{
            command = [IO.Path]::GetFileName($CliPath) + " " + ($Arguments -join " ")
            exitCode = $exitCode
            log = $LogPath
        }
    }
    finally {
        $env:RECEIPTS_LOCAL_ROOT = $oldReceiptsLocal
        $env:RECEIPTS_LIBRARY_ROOT = $oldReceiptsLibrary
        $env:GOATSHOT_LOCAL_ROOT = $oldGoatShotLocal
        $env:GOATSHOT_LIBRARY_ROOT = $oldGoatShotLibrary
    }
}

function Build-Markdown {
    param([pscustomobject]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Receipts Portable Package Verification")
    [void]$lines.Add("")
    [void]$lines.Add("Package: ``$($Result.packagePath)``")
    [void]$lines.Add("Extracted to: ``$($Result.extractRoot)``")
    [void]$lines.Add("Succeeded: ``$($Result.succeeded)``")
    [void]$lines.Add("")
    [void]$lines.Add("## Required Entries")
    [void]$lines.Add("")
    foreach ($entry in $Result.requiredEntries) {
        [void]$lines.Add("- ``$($entry.path)`` present=``$($entry.present)`` length=``$($entry.length)``")
    }
    [void]$lines.Add("")
    [void]$lines.Add("## Forbidden Matches")
    [void]$lines.Add("")
    if ($Result.forbiddenMatches.Count -eq 0) {
        [void]$lines.Add("- None.")
    }
    else {
        foreach ($match in $Result.forbiddenMatches) {
            [void]$lines.Add("- ``$match``")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("## CLI Smoke")
    [void]$lines.Add("")
    if ($Result.cliSmoke.Count -eq 0) {
        [void]$lines.Add("- Not run.")
    }
    else {
        foreach ($smoke in $Result.cliSmoke) {
            [void]$lines.Add("- ``$($smoke.command)`` exitCode=``$($smoke.exitCode)`` log=``$($smoke.log)``")
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

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

$packageFullPath = Resolve-FullPath $PackagePath
$outputFullPath = Resolve-FullPath $OutputRoot
$issues = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$cliSmoke = New-Object System.Collections.Generic.List[object]

New-Item -ItemType Directory -Force -Path $outputFullPath | Out-Null

if (-not (Test-Path -LiteralPath $packageFullPath -PathType Leaf)) {
    Add-Issue $issues "Portable package was not found: $packageFullPath"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$extractRoot = Join-Path $outputFullPath "extract-$timestamp"
$entries = @()

if ($issues.Count -eq 0) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageFullPath)
    try {
        $entries = @($archive.Entries | ForEach-Object {
            [pscustomobject]@{
                fullName = $_.FullName.Replace("\", "/")
                length = $_.Length
            }
        })
    }
    finally {
        $archive.Dispose()
    }

    Expand-Archive -LiteralPath $packageFullPath -DestinationPath $extractRoot -Force
}

$receiptsRequiredPaths = @(
    "Receipts.exe",
    "Receipts.dll",
    "Receipts.Cli.exe",
    "Receipts.Cli.dll",
    "README.md",
    "browser-extension/README.md",
    "browser-extension/bridge-contract.md",
    "browser-extension/manifest.json",
    "browser-extension/content-script.js",
    "browser-extension/service-worker.js",
    "browser-extension/popup.html",
    "browser-extension/popup.js",
    "browser-extension/options.html",
    "browser-extension/options.js",
    "browser-extension/samples/safe-fixture.html"
)
$legacyRequiredPaths = @(
    "GoatShot.exe",
    "GoatShot.dll",
    "GoatShot.Cli.exe",
    "GoatShot.Cli.dll"
) + $receiptsRequiredPaths[4..($receiptsRequiredPaths.Count - 1)]

$hasCompleteReceiptsLayout = @($receiptsRequiredPaths | Where-Object {
    $required = $_
    -not ($entries | Where-Object { $_.fullName.Equals($required, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)
}).Count -eq 0
$hasCompleteLegacyLayout = @($legacyRequiredPaths | Where-Object {
    $required = $_
    -not ($entries | Where-Object { $_.fullName.Equals($required, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)
}).Count -eq 0
$packageBrand = if ($hasCompleteReceiptsLayout) { "Receipts" } elseif ($hasCompleteLegacyLayout) { "GoatShot compatibility" } else { "Receipts" }
$requiredPaths = if ($hasCompleteLegacyLayout -and -not $hasCompleteReceiptsLayout) { $legacyRequiredPaths } else { $receiptsRequiredPaths }
if ($hasCompleteLegacyLayout -and -not $hasCompleteReceiptsLayout) {
    [void]$warnings.Add("Verifying a legacy GoatShot portable layout through the compatibility reader; new packages must use Receipts filenames.")
}
elseif ($hasCompleteReceiptsLayout) {
    foreach ($legacyBinary in $legacyRequiredPaths[0..3]) {
        if ($entries | Where-Object { $_.fullName.Equals($legacyBinary, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1) {
            Add-Issue $issues "Receipts package contains an obsolete legacy binary entry: $legacyBinary"
        }
    }
}

$requiredResults = New-Object System.Collections.Generic.List[object]
foreach ($required in $requiredPaths) {
    $entry = $entries | Where-Object { $_.fullName.Equals($required, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    $present = $null -ne $entry
    $length = if ($present) { $entry.length } else { 0 }
    [void]$requiredResults.Add([pscustomobject]@{
        path = $required
        present = $present
        length = $length
    })

    if (-not $present) {
        Add-Issue $issues "Required package entry is missing: $required"
    }
    elseif ($length -le 0) {
        Add-Issue $issues "Required package entry is empty: $required"
    }
}

$forbiddenPatterns = @(
    "(^|/)artifacts/",
    "(^|/)manual-validation/",
    "(^|/)product-design-audit/",
    "(^|/)tranche-[^/]+/",
    "(^|/)secrets/",
    "(^|/)thumbnails/",
    "(^|/)logs/",
    "(^|/)ai-output/",
    "(^|/)plugin-staging/",
    "(^|/)browser-bridge/",
    "(^|/)settings\.json$",
    "(^|/)share-history\.json$",
    "(^|/)upload-queue\.json$",
    "(^|/)workspace-index\.json$",
    "(^|/)workspace\.sqlite$",
    "\.dpapi$"
)

$forbiddenMatches = New-Object System.Collections.Generic.List[string]
foreach ($entry in $entries) {
    foreach ($pattern in $forbiddenPatterns) {
        if ($entry.fullName -match $pattern) {
            [void]$forbiddenMatches.Add($entry.fullName)
            break
        }
    }
}

if ($forbiddenMatches.Count -gt 0) {
    Add-Issue $issues ("Forbidden runtime/proof entries were bundled: " + (($forbiddenMatches | Select-Object -First 20) -join ", "))
}

if ($issues.Count -eq 0) {
    $rootReadme = Join-Path $extractRoot "README.md"
    $extensionReadme = Join-Path $extractRoot "browser-extension\README.md"
    if ((Test-Path -LiteralPath $rootReadme -PathType Leaf) -and
        -not ((Get-Content -LiteralPath $rootReadme -Raw) -match "publication-plan")) {
        Add-Issue $issues "Packaged root README does not document browser-extension publication-plan."
    }

    if ((Test-Path -LiteralPath $extensionReadme -PathType Leaf) -and
        -not ((Get-Content -LiteralPath $extensionReadme -Raw) -match "publication-plan")) {
        Add-Issue $issues "Packaged browser-extension README does not document publication-plan."
    }
}

if ($RunCliSmoke -and $issues.Count -eq 0) {
    $cliPath = Join-Path $extractRoot $(if ($packageBrand -eq "GoatShot compatibility") { "GoatShot.Cli.exe" } else { "Receipts.Cli.exe" })
    $cleanLocalRoot = Join-Path $outputFullPath "clean-localappdata"
    $cleanLibraryRoot = Join-Path $outputFullPath "clean-library"
    New-Item -ItemType Directory -Force -Path $cleanLocalRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $cleanLibraryRoot | Out-Null

    $helpLog = Join-Path $outputFullPath "packaged-cli-help.txt"
    $diagnosticsLog = Join-Path $outputFullPath "packaged-cli-diagnostics-print.txt"
    [void]$cliSmoke.Add((Invoke-PackagedCli `
        -CliPath $cliPath `
        -Arguments @("--help") `
        -LogPath $helpLog `
        -CleanLocalRoot $cleanLocalRoot `
        -CleanLibraryRoot $cleanLibraryRoot))
    [void]$cliSmoke.Add((Invoke-PackagedCli `
        -CliPath $cliPath `
        -Arguments @("diagnostics", "print") `
        -LogPath $diagnosticsLog `
        -CleanLocalRoot $cleanLocalRoot `
        -CleanLibraryRoot $cleanLibraryRoot))

    foreach ($smoke in $cliSmoke) {
        if ($smoke.exitCode -ne 0) {
            Add-Issue $issues "Packaged CLI smoke failed: $($smoke.command)"
        }
    }
}

$result = [pscustomobject]@{
    succeeded = $issues.Count -eq 0
    packageBrand = $packageBrand
    packagePath = $packageFullPath
    outputRoot = $outputFullPath
    extractRoot = $extractRoot
    runCliSmoke = [bool]$RunCliSmoke
    requiredEntries = $requiredResults
    forbiddenMatches = $forbiddenMatches
    cliSmoke = $cliSmoke
    warnings = $warnings
    issues = $issues
}

$jsonPath = Join-Path $outputFullPath "portable-package-verification.json"
$markdownPath = Join-Path $outputFullPath "portable-package-verification.md"
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-TextFile $markdownPath (Build-Markdown $result)

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    if ($result.succeeded) {
        Write-Host "Portable package verification passed: $jsonPath"
    }
    else {
        Write-Host "Portable package verification failed: $jsonPath"
        foreach ($issue in $issues) {
            Write-Host "ISSUE: $issue"
        }
    }
}

if (-not $result.succeeded) {
    exit 1
}

exit 0

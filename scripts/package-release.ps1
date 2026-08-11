param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.2.0",
    [string] $BuildId = "",
    [switch] $SkipInstaller,
    [switch] $SkipSingleExe
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string] $Step) {
    # Successful PowerShell child scripts do not set LASTEXITCODE. Only native
    # processes provide a numeric code; script failures already terminate under
    # ErrorActionPreference=Stop.
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Get-Sha256Hex([string] $PathValue) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($PathValue)
        try { return ([BitConverter]::ToString($sha256.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $sha256.Dispose() }
}

function Remove-ValidatedDirectory([string] $PathValue, [string] $ExpectedParent) {
    if (-not (Test-Path -LiteralPath $PathValue)) { return }
    $resolved = [System.IO.Path]::GetFullPath($PathValue)
    $parent = [System.IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear packaging directory outside $parent`: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\GoatShot.App\GoatShot.App.csproj"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$publishDir = Join-Path $publishRoot "GoatShot-$Runtime-single-exe"
$distRoot = Join-Path $repoRoot "artifacts\dist"
$embeddedRoot = Join-Path $repoRoot "artifacts\embedded-assets"
$artifactName = "GoatShot-$Version-$Runtime.exe"
$artifactPath = Join-Path $distRoot $artifactName

if ($Runtime -ne "win-x64") { throw "Personal V1 supports only win-x64." }
if ($SkipSingleExe) { throw "Personal V1 is distributed only as a single executable." }
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = (git -C $repoRoot rev-parse --short=12 HEAD).Trim()
    Assert-LastExitCode "Build identity resolution"
}

New-Item -ItemType Directory -Force -Path $publishRoot, $distRoot | Out-Null
Remove-ValidatedDirectory $publishDir $publishRoot

& (Join-Path $PSScriptRoot "prepare-embedded-assets.ps1") -OutputRoot $embeddedRoot -Version $Version -BuildId $BuildId
Assert-LastExitCode "Embedded asset preparation"

dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    /p:DebugType=embedded `
    /p:Version=$Version `
    /p:SourceRevisionId=$BuildId `
    /p:GoatShotDistribution=true `
    /p:GoatShotBuildId=$BuildId `
    /p:EmbeddedAssetsRoot=$embeddedRoot
Assert-LastExitCode "Personal single-exe publish"

$publishedExe = Join-Path $publishDir "GoatShot.exe"
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Single-exe publish did not create $publishedExe"
}

Get-ChildItem -LiteralPath $distRoot -Filter "*.exe" -File |
    Remove-Item -Force
Get-ChildItem -LiteralPath $distRoot -Filter "GoatShot-*-portable.zip" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force
Copy-Item -LiteralPath $publishedExe -Destination $artifactPath -Force

# A PE executable may carry a ZIP payload after its image. ZipArchive reads the
# final central directory and treats the PE bytes as a self-extracting prefix.
# This keeps the runtime to one file without relying on .NET single-file
# manifest-resource streams for large native assets.
$assetArchive = Join-Path $embeddedRoot "embedded-assets.zip"
if (-not (Test-Path -LiteralPath $assetArchive -PathType Leaf)) {
    throw "Prepared embedded asset archive is missing: $assetArchive"
}
$targetStream = [IO.File]::Open($artifactPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $assetStream = [IO.File]::OpenRead($assetArchive)
    try {
        $assetLength = $assetStream.Length
        $assetStream.CopyTo($targetStream)
        $lengthBytes = [BitConverter]::GetBytes([Int64]$assetLength)
        $targetStream.Write($lengthBytes, 0, $lengthBytes.Length)
        $magicBytes = [Text.Encoding]::ASCII.GetBytes("GOATSHOTASSET1!!")
        $targetStream.Write($magicBytes, 0, $magicBytes.Length)
    }
    finally { $assetStream.Dispose() }
}
finally { $targetStream.Dispose() }

$hash = Get-Sha256Hex $artifactPath
$hashPath = "$artifactPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $artifactName" -Encoding ASCII

$manifestPath = Join-Path $embeddedRoot "embedded-assets.manifest.json"
$manifestHash = Get-Sha256Hex $manifestPath
$metadataPath = Join-Path $distRoot "GoatShot-$Version-$Runtime.build.json"
[pscustomobject]@{
    schemaVersion = "goatshot.personal-build.v1"
    product = "GoatShot"
    version = $Version
    runtime = $Runtime
    buildId = $BuildId
    buildTimestampUtc = (Get-Date).ToUniversalTime().ToString("o")
    executable = $artifactName
    executableSha256 = $hash
    embeddedAssetManifestSha256 = $manifestHash
    signed = $false
    distribution = "unsigned per-user self-installing executable"
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$noticesPath = Join-Path $distRoot "GoatShot-$Version-THIRD-PARTY-NOTICES.txt"
$embeddedNotices = Join-Path $embeddedRoot "staging\bundle\THIRD_PARTY_NOTICES.md"
if (-not (Test-Path -LiteralPath $embeddedNotices)) { throw "Embedded third-party notices are missing." }
$noticeLines = Get-Content -LiteralPath $embeddedNotices
Set-Content -LiteralPath $noticesPath -Value $noticeLines -Encoding UTF8

$sbomPath = Join-Path $distRoot "GoatShot-$Version-$Runtime.spdx.json"
& (Join-Path $PSScriptRoot "create-spdx-sbom.ps1") -Version $Version -Runtime $Runtime -OutputPath $sbomPath -EmbeddedManifestPath $manifestPath | Out-Null
Assert-LastExitCode "SPDX SBOM generation"

[pscustomobject]@{
    Executable = $artifactPath
    Sha256 = $hash
    ChecksumFile = $hashPath
    BuildMetadata = $metadataPath
    Notices = $noticesPath
    Sbom = $sbomPath
    PublishDir = $publishDir
    EmbeddedAssets = $embeddedRoot
    Installer = $null
    PortableZip = $null
}

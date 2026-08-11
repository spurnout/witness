param(
    [string] $OutputRoot,
    [string] $Version = "0.3.0",
    [string] $BuildId,
    [switch] $Force
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex([string] $Path) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($Path)
        try { return ([BitConverter]::ToString($sha256.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $sha256.Dispose() }
}

function Assert-Hash([string] $Path, [string] $Expected) {
    $actual = Get-Sha256Hex $Path
    if ($actual -ne $Expected.ToLowerInvariant()) {
        throw "SHA-256 mismatch for $Path. Expected $Expected, received $actual."
    }
}

function Get-LockedFile([string] $Url, [string] $ExpectedHash, [string] $Destination) {
    if (-not (Test-Path -LiteralPath $Destination) -or $Force) {
        $parent = Split-Path -Parent $Destination
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        $temporary = "$Destination.download"
        Invoke-WebRequest -Uri $Url -OutFile $temporary
        Assert-Hash $temporary $ExpectedHash
        Move-Item -LiteralPath $temporary -Destination $Destination -Force
    }

    Assert-Hash $Destination $ExpectedHash
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\embedded-assets"
}
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = (git -C $repoRoot rev-parse --short=12 HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the build commit." }
}

$lockPath = Join-Path $repoRoot "packaging\embedded-assets.lock.json"
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$cacheRoot = Join-Path $repoRoot "artifacts\asset-cache"
$stagingRoot = Join-Path $OutputRoot "staging"
$bundleRoot = Join-Path $stagingRoot "bundle"
$ffmpegArchive = Join-Path $cacheRoot "ffmpeg-lgpl-shared.zip"
$modelFile = Join-Path $cacheRoot "fcn-resnet50-12-int8.onnx"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $bundleRoot, $cacheRoot | Out-Null

Get-LockedFile $lock.ffmpeg.source $lock.ffmpeg.sha256 $ffmpegArchive
Get-LockedFile $lock.personSegmentation.source $lock.personSegmentation.sha256 $modelFile

$ffmpegExtract = Join-Path $stagingRoot "ffmpeg-source"
Expand-Archive -LiteralPath $ffmpegArchive -DestinationPath $ffmpegExtract -Force
$ffmpegBin = Get-ChildItem -LiteralPath $ffmpegExtract -Recurse -Directory |
    Where-Object { $_.Name -eq "bin" -and (Test-Path (Join-Path $_.FullName "ffmpeg.exe")) } |
    Select-Object -First 1
if ($null -eq $ffmpegBin) { throw "The locked FFmpeg archive did not contain bin\ffmpeg.exe." }

$ffmpegTarget = Join-Path $bundleRoot "tools\ffmpeg"
New-Item -ItemType Directory -Force -Path $ffmpegTarget | Out-Null
Get-ChildItem -LiteralPath $ffmpegBin.FullName -File |
    Where-Object { $_.Extension -eq ".dll" -or $_.Name -in @("ffmpeg.exe", "ffprobe.exe") } |
    Copy-Item -Destination $ffmpegTarget -Force

$modelTarget = Join-Path $bundleRoot "models\person-segmentation.onnx"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $modelTarget) | Out-Null
Copy-Item -LiteralPath $modelFile -Destination $modelTarget -Force

$extensionSource = Join-Path $repoRoot "browser-extension"
if (-not (Test-Path -LiteralPath $extensionSource)) { throw "Browser extension source is missing: $extensionSource" }
Copy-Item -LiteralPath $extensionSource -Destination (Join-Path $bundleRoot "browser-extension") -Recurse -Force

$notices = @"
# Receipts third-party notices

- FFmpeg $($lock.ffmpeg.version), $($lock.ffmpeg.license), built by BtbN from FFmpeg source with the locked $($lock.ffmpeg.build) configuration. Source: $($lock.ffmpeg.source)
- ONNX Model Zoo FCN ResNet50 person-segmentation model $($lock.personSegmentation.version), $($lock.personSegmentation.license). Source: $($lock.personSegmentation.source)
- Microsoft.ML.OnnxRuntime.DirectML 1.24.4, MIT.
- SSH.NET 2025.1.0, MIT.

Receipts does not embed Whisper, Google Platform Tools, browser binaries, cloud credentials, hardware drivers, or user scripts.
"@
$noticesPath = Join-Path $bundleRoot "THIRD_PARTY_NOTICES.md"
[IO.File]::WriteAllText($noticesPath, $notices, [Text.UTF8Encoding]::new($false))

$assets = [System.Collections.Generic.List[object]]::new()
$bundlePrefix = (Resolve-Path $bundleRoot).Path.TrimEnd('\') + '\'
Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    $assetFile = $_
    if (-not $_.FullName.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Asset path escaped bundle root: $($_.FullName)"
    }
    $relative = $_.FullName.Substring($bundlePrefix.Length).Replace('\', '/')
    $id = switch -Regex ($relative) {
        '^tools/ffmpeg/ffmpeg\.exe$' { 'ffmpeg'; break }
        '^tools/ffmpeg/ffprobe\.exe$' { 'ffprobe'; break }
        '^models/person-segmentation\.onnx$' { 'person-segmentation-model'; break }
        '^browser-extension/' { "browser-extension-$($relative.Replace('/', '-').Replace('.', '-').ToLowerInvariant())"; break }
        '^THIRD_PARTY_NOTICES\.md$' { 'third-party-notices'; break }
        default { "ffmpeg-runtime-$($assetFile.Name.ToLowerInvariant())" }
    }
    $source = if ($relative.StartsWith('tools/ffmpeg/')) { $lock.ffmpeg.source } elseif ($relative.StartsWith('models/')) { $lock.personSegmentation.source } else { 'Receipts repository' }
    $license = if ($relative.StartsWith('tools/ffmpeg/')) { $lock.ffmpeg.license } elseif ($relative.StartsWith('models/')) { $lock.personSegmentation.license } else { 'Receipts' }
    $capability = if ($relative.StartsWith('tools/ffmpeg/')) { 'video-tools' } elseif ($relative.StartsWith('models/')) { 'person-segmentation' } elseif ($relative.StartsWith('browser-extension/')) { 'browser-extension' } else { 'notices' }
    $assetVersion = if ($relative.StartsWith('tools/ffmpeg/')) { $lock.ffmpeg.version } elseif ($relative.StartsWith('models/')) { $lock.personSegmentation.version } else { $Version }
    $assets.Add([ordered]@{
        id = $id
        resourceName = "Receipts.EmbeddedAsset.$id"
        relativePath = $relative
        sha256 = Get-Sha256Hex $_.FullName
        size = $_.Length
        kind = $_.Extension.TrimStart('.')
        version = $assetVersion
        license = $license
        source = $source
        buildOptions = if ($relative.StartsWith('tools/ffmpeg/')) { $lock.ffmpeg.build } elseif ($relative.StartsWith('models/')) { 'upstream pretrained ONNX model; no local conversion' } else { 'embedded verbatim from repository source' }
        extractionTarget = $relative
        capability = $capability
    })
}

$manifest = [ordered]@{
    schemaVersion = 'receipts.embedded-assets.v1'
    productVersion = $Version
    buildId = $BuildId
    manifestSha256 = ''
    assets = $assets
}
$manifestPath = Join-Path $OutputRoot "embedded-assets.manifest.json"
$bundlePath = Join-Path $OutputRoot "embedded-assets.zip"
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$manifestJson = $manifest | ConvertTo-Json -Depth 8
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $manifest.manifestSha256 = ([BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifestJson))) -replace '-', '').ToLowerInvariant()
}
finally {
    $sha256.Dispose()
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$itemsPath = Join-Path $OutputRoot "embedded-assets.items.props"
$itemLines = [System.Collections.Generic.List[string]]::new()
$itemLines.Add('<Project>')
$itemLines.Add('  <ItemGroup>')
foreach ($asset in $assets) {
    $includePath = Join-Path $bundleRoot ($asset.relativePath.Replace('/', '\'))
    $escapedPath = [Security.SecurityElement]::Escape($includePath)
    $escapedResource = [Security.SecurityElement]::Escape($asset.resourceName)
    $itemLines.Add("    <EmbeddedResource Include=`"$escapedPath`" LogicalName=`"$escapedResource`" />")
}
$itemLines.Add('  </ItemGroup>')
$itemLines.Add('</Project>')
[IO.File]::WriteAllLines($itemsPath, $itemLines, [Text.UTF8Encoding]::new($false))
if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $bundlePath -CompressionLevel Optimal

[pscustomobject]@{
    OutputRoot = (Resolve-Path $OutputRoot).Path
    Manifest = $manifestPath
    Items = $itemsPath
    Bundle = $bundlePath
    BuildId = $BuildId
    AssetCount = $assets.Count
    BundleBytes = (Get-Item -LiteralPath $bundlePath).Length
}

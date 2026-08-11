param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.3.0",
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

function Remove-ReleaseFile([string] $PathValue, [string] $ExpectedParent) {
    if (-not (Test-Path -LiteralPath $PathValue)) { return }
    $resolved = [System.IO.Path]::GetFullPath($PathValue)
    $parent = [System.IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove release file outside $parent`: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Force
}

function Add-EmbeddedAssetPayload([string] $ExecutablePath, [string] $AssetArchivePath) {
    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "Published executable is missing: $ExecutablePath"
    }
    if (-not (Test-Path -LiteralPath $AssetArchivePath -PathType Leaf)) {
        throw "Prepared embedded asset archive is missing: $AssetArchivePath"
    }

    # The runtime reader intentionally retains the legacy footer magic so a
    # Receipts build remains compatible with already-shipped payload readers.
    $targetStream = [IO.File]::Open($ExecutablePath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $assetStream = [IO.File]::OpenRead($AssetArchivePath)
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
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\GoatShot.App\GoatShot.App.csproj"
$cliProject = Join-Path $repoRoot "src\GoatShot.Cli\GoatShot.Cli.csproj"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$portablePublishDir = Join-Path $publishRoot "Receipts-$Runtime"
$singleExePublishDir = Join-Path $publishRoot "Receipts-$Runtime-single-exe"
$distRoot = Join-Path $repoRoot "artifacts\dist"
$embeddedRoot = Join-Path $repoRoot "artifacts\embedded-assets"
$portableZipPath = Join-Path $distRoot "Receipts-$Version-$Runtime-portable.zip"
$singleExeName = "Receipts-$Version-$Runtime-single-exe.exe"
$singleExePath = Join-Path $distRoot $singleExeName
$installerScript = Join-Path $repoRoot "packaging\GoatShot.iss"

if ($Runtime -ne "win-x64") { throw "Receipts 0.3 supports only win-x64 distribution builds." }
foreach ($requiredProject in @($appProject, $cliProject)) {
    if (-not (Test-Path -LiteralPath $requiredProject -PathType Leaf)) {
        throw "Project was not found: $requiredProject"
    }
}
if ([string]::IsNullOrWhiteSpace($BuildId)) {
    $BuildId = (git -C $repoRoot rev-parse --short=12 HEAD).Trim()
    Assert-LastExitCode "Build identity resolution"
}

New-Item -ItemType Directory -Force -Path $publishRoot, $distRoot | Out-Null
Remove-ValidatedDirectory $portablePublishDir $publishRoot
Remove-ValidatedDirectory $singleExePublishDir $publishRoot
$releaseFiles = @($portableZipPath)
if (-not $SkipSingleExe) {
    $releaseFiles += @(
        $singleExePath,
        "$singleExePath.sha256",
        (Join-Path $distRoot "Receipts-$Version-$Runtime.build.json"),
        (Join-Path $distRoot "Receipts-$Version-THIRD-PARTY-NOTICES.txt"),
        (Join-Path $distRoot "Receipts-$Version-$Runtime.spdx.json"))
}
if (-not $SkipInstaller) {
    $releaseFiles += @(
        (Join-Path $distRoot "Receipts-$Version-win-x64.exe"),
        (Join-Path $distRoot "Receipts-$Version-win-x64.exe.sha256"))
}
foreach ($releaseFile in $releaseFiles) {
    Remove-ReleaseFile $releaseFile $distRoot
}

& (Join-Path $PSScriptRoot "prepare-embedded-assets.ps1") -OutputRoot $embeddedRoot -Version $Version -BuildId $BuildId
Assert-LastExitCode "Embedded asset preparation"
$assetArchive = Join-Path $embeddedRoot "embedded-assets.zip"

dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $portablePublishDir `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version `
    /p:SourceRevisionId=$BuildId `
    /p:ReceiptsDistribution=true `
    /p:ReceiptsBuildId=$BuildId `
    /p:EmbeddedAssetsRoot=$embeddedRoot
Assert-LastExitCode "Receipts portable app publish"

dotnet publish $cliProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $portablePublishDir `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version `
    /p:SourceRevisionId=$BuildId `
    /p:ReceiptsDistribution=true `
    /p:ReceiptsBuildId=$BuildId `
    /p:EmbeddedAssetsRoot=$embeddedRoot
Assert-LastExitCode "Receipts portable CLI publish"

$portableExe = Join-Path $portablePublishDir "Receipts.exe"
$portableCli = Join-Path $portablePublishDir "Receipts.Cli.exe"
foreach ($requiredExecutable in @($portableExe, $portableCli)) {
    if (-not (Test-Path -LiteralPath $requiredExecutable -PathType Leaf)) {
        throw "Portable publish did not create $requiredExecutable"
    }
}
Add-EmbeddedAssetPayload $portableExe $assetArchive
Add-EmbeddedAssetPayload $portableCli $assetArchive

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $portablePublishDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "spec.md") -Destination (Join-Path $portablePublishDir "spec.md") -Force
$browserExtensionSource = Join-Path $repoRoot "browser-extension"
if (Test-Path -LiteralPath $browserExtensionSource -PathType Container) {
    Copy-Item -LiteralPath $browserExtensionSource -Destination (Join-Path $portablePublishDir "browser-extension") -Recurse -Force
}
Compress-Archive -Path (Join-Path $portablePublishDir "*") -DestinationPath $portableZipPath -CompressionLevel Optimal

$singleExeArtifact = $null
$hash = $null
$hashPath = $null
$metadataPath = $null
$noticesPath = $null
$sbomPath = $null
if (-not $SkipSingleExe) {
    dotnet publish $appProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $singleExePublishDir `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=false `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishReadyToRun=true `
        /p:PublishTrimmed=false `
        /p:DebugType=embedded `
        /p:Version=$Version `
        /p:SourceRevisionId=$BuildId `
        /p:ReceiptsDistribution=true `
        /p:ReceiptsBuildId=$BuildId `
        /p:EmbeddedAssetsRoot=$embeddedRoot
    Assert-LastExitCode "Receipts single-exe publish"

    $publishedSingleExe = Join-Path $singleExePublishDir "Receipts.exe"
    if (-not (Test-Path -LiteralPath $publishedSingleExe -PathType Leaf)) {
        throw "Single-exe publish did not create $publishedSingleExe"
    }
    Copy-Item -LiteralPath $publishedSingleExe -Destination $singleExePath
    Add-EmbeddedAssetPayload $singleExePath $assetArchive
    $singleExeArtifact = $singleExePath

    $hash = Get-Sha256Hex $singleExePath
    $hashPath = "$singleExePath.sha256"
    Set-Content -LiteralPath $hashPath -Value "$hash  $singleExeName" -Encoding ASCII

    $manifestPath = Join-Path $embeddedRoot "embedded-assets.manifest.json"
    $manifestHash = Get-Sha256Hex $manifestPath
    $metadataPath = Join-Path $distRoot "Receipts-$Version-$Runtime.build.json"
    [pscustomobject]@{
        schemaVersion = "receipts.personal-build.v1"
        product = "Receipts"
        version = $Version
        runtime = $Runtime
        buildId = $BuildId
        buildTimestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        executable = $singleExeName
        executableSha256 = $hash
        embeddedAssetManifestSha256 = $manifestHash
        signed = $false
        distribution = "unsigned per-user self-installing executable"
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

    $noticesPath = Join-Path $distRoot "Receipts-$Version-THIRD-PARTY-NOTICES.txt"
    $embeddedNotices = Join-Path $embeddedRoot "staging\bundle\THIRD_PARTY_NOTICES.md"
    if (-not (Test-Path -LiteralPath $embeddedNotices -PathType Leaf)) {
        throw "Embedded third-party notices are missing."
    }
    $notices = (Get-Content -Raw -LiteralPath $embeddedNotices).Replace("GoatShot", "Receipts")
    Set-Content -LiteralPath $noticesPath -Value $notices -Encoding UTF8

    $sbomPath = Join-Path $distRoot "Receipts-$Version-$Runtime.spdx.json"
    & (Join-Path $PSScriptRoot "create-spdx-sbom.ps1") -Version $Version -Runtime $Runtime -OutputPath $sbomPath -EmbeddedManifestPath $manifestPath | Out-Null
    Assert-LastExitCode "SPDX SBOM generation"
}

$isccCandidates = @()
if ($env:INNO_SETUP_ISCC) { $isccCandidates += $env:INNO_SETUP_ISCC }
$isccFromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($isccFromPath) { $isccCandidates += $isccFromPath.Source }
$isccCandidates += @(
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
$installerPath = $null
$installerHash = $null
$installerHashPath = $null
if (-not $SkipInstaller -and $iscc) {
    & $iscc `
        "/DAppVersion=$Version" `
        "/DPublishDir=$portablePublishDir" `
        "/DOutputDir=$distRoot" `
        $installerScript
    Assert-LastExitCode "Inno Setup compile"
    $installerPath = Join-Path $distRoot "Receipts-$Version-win-x64.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Inno Setup reported success but the installer was not created: $installerPath"
    }
    $installerHash = Get-Sha256Hex $installerPath
    $installerHashPath = "$installerPath.sha256"
    Set-Content -LiteralPath $installerHashPath -Value "$installerHash  $([IO.Path]::GetFileName($installerPath))" -Encoding ASCII
}
elseif (-not $SkipInstaller) {
    Write-Warning "Inno Setup compiler was not found. The portable and single-exe artifacts were created; install Inno Setup 6 or set INNO_SETUP_ISCC to build the installer."
}

[pscustomobject]@{
    Product = "Receipts"
    Version = $Version
    PublishDir = $portablePublishDir
    PortableZip = $portableZipPath
    PortableDesktopExecutable = $portableExe
    PortableCliExecutable = $portableCli
    SingleExePublishDir = if ($SkipSingleExe) { $null } else { $singleExePublishDir }
    SingleExe = $singleExeArtifact
    Sha256 = $hash
    ChecksumFile = $hashPath
    BuildMetadata = $metadataPath
    Notices = $noticesPath
    Sbom = $sbomPath
    EmbeddedAssets = $embeddedRoot
    Installer = $installerPath
    InstallerSha256 = $installerHash
    InstallerChecksumFile = $installerHashPath
    InnoSetupCompiler = $iscc
}

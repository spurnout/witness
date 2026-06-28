param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.1.0",
    [switch] $SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProject = Join-Path $repoRoot "src\GoatShot.App\GoatShot.App.csproj"
$cliProject = Join-Path $repoRoot "src\GoatShot.Cli\GoatShot.Cli.csproj"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$distRoot = Join-Path $repoRoot "artifacts\dist"
$publishDir = Join-Path $publishRoot "GoatShot-$Runtime"
$zipPath = Join-Path $distRoot "GoatShot-$Version-$Runtime-portable.zip"
$installerScript = Join-Path $repoRoot "packaging\GoatShot.iss"

if (-not (Test-Path $appProject)) {
    throw "App project not found: $appProject"
}

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found: $cliProject"
}

New-Item -ItemType Directory -Force -Path $publishRoot, $distRoot | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version

dotnet publish $cliProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $publishDir `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $publishDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "spec.md") -Destination (Join-Path $publishDir "spec.md") -Force
$browserExtensionSource = Join-Path $repoRoot "browser-extension"
if (Test-Path $browserExtensionSource) {
    Copy-Item -LiteralPath $browserExtensionSource -Destination (Join-Path $publishDir "browser-extension") -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$isccCandidates = @()
if ($env:INNO_SETUP_ISCC) {
    $isccCandidates += $env:INNO_SETUP_ISCC
}

$isccFromPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($isccFromPath) {
    $isccCandidates += $isccFromPath.Source
}

$isccCandidates += @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
$installerPath = $null

if (-not $SkipInstaller -and $iscc) {
    & $iscc `
        "/DAppVersion=$Version" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$distRoot" `
        $installerScript

    $installerPath = Join-Path $distRoot "GoatShot-Setup-$Version-win-x64.exe"
}
elseif (-not $SkipInstaller) {
    Write-Warning "Inno Setup compiler was not found. Portable zip was created; install Inno Setup 6 or set INNO_SETUP_ISCC to build the .exe installer."
}

[pscustomobject]@{
    PublishDir = $publishDir
    PortableZip = $zipPath
    Installer = $installerPath
    InnoSetupCompiler = $iscc
}

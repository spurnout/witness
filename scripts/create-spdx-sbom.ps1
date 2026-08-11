param(
    [string] $Version = "0.2.0",
    [string] $Runtime = "win-x64",
    [string] $OutputPath = "artifacts\dist\GoatShot-0.2.0-win-x64.spdx.json",
    [string] $EmbeddedManifestPath = "artifacts\embedded-assets\embedded-assets.manifest.json"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
function Resolve-RepoPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}
function New-SpdxId([string] $Value) { return "SPDXRef-" + ($Value -replace '[^A-Za-z0-9.-]', '-') }

$assetsPath = Join-Path $repoRoot "src\GoatShot.App\obj\project.assets.json"
if (-not (Test-Path -LiteralPath $assetsPath)) { throw "Restore assets were not found: $assetsPath" }
$assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
$packages = [System.Collections.Generic.List[object]]::new()
$relationships = [System.Collections.Generic.List[object]]::new()
$appId = "SPDXRef-GoatShot"
$packages.Add([ordered]@{ SPDXID = $appId; name = "GoatShot"; versionInfo = $Version; downloadLocation = "NOASSERTION"; filesAnalyzed = $false; licenseConcluded = "NOASSERTION"; licenseDeclared = "NOASSERTION"; supplier = "NOASSERTION" })

foreach ($libraryProperty in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($libraryProperty.Value.type -ne "package") { continue }
    $parts = $libraryProperty.Name.Split('/')
    $name = $parts[0]
    $packageVersion = if ($parts.Length -gt 1) { $parts[1] } else { "unknown" }
    $id = New-SpdxId "NuGet-$name-$packageVersion"
    $packages.Add([ordered]@{ SPDXID = $id; name = $name; versionInfo = $packageVersion; downloadLocation = "https://www.nuget.org/packages/$name/$packageVersion"; filesAnalyzed = $false; licenseConcluded = "NOASSERTION"; licenseDeclared = "NOASSERTION"; supplier = "NOASSERTION" })
    $relationships.Add([ordered]@{ spdxElementId = $appId; relationshipType = "DEPENDS_ON"; relatedSpdxElement = $id })
}

$embeddedPath = Resolve-RepoPath $EmbeddedManifestPath
if (Test-Path -LiteralPath $embeddedPath) {
    $embedded = Get-Content -Raw -LiteralPath $embeddedPath | ConvertFrom-Json
    foreach ($asset in $embedded.assets) {
        $id = New-SpdxId "Embedded-$($asset.id)"
        $packages.Add([ordered]@{ SPDXID = $id; name = $asset.id; versionInfo = $asset.version; downloadLocation = $asset.source; filesAnalyzed = $false; checksums = @([ordered]@{ algorithm = "SHA256"; checksumValue = $asset.sha256 }); licenseConcluded = $asset.license; licenseDeclared = $asset.license; supplier = "NOASSERTION"; comment = "Capability: $($asset.capability). Extraction target: $($asset.extractionTarget). Build options: $($asset.buildOptions)" })
        $relationships.Add([ordered]@{ spdxElementId = $appId; relationshipType = "CONTAINS"; relatedSpdxElement = $id })
    }
}

$namespaceSeed = "$Version-$Runtime-" + [Guid]::NewGuid().ToString("N")
$document = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "GoatShot-$Version-$Runtime"
    documentNamespace = "https://goatshot.local/spdx/$namespaceSeed"
    creationInfo = [ordered]@{ created = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); creators = @("Tool: GoatShot create-spdx-sbom.ps1") }
    documentDescribes = @($appId)
    packages = $packages
    relationships = $relationships
}
$resolvedOutput = Resolve-RepoPath $OutputPath
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
$document | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
[pscustomobject]@{ Output = $resolvedOutput; PackageCount = $packages.Count; RelationshipCount = $relationships.Count }

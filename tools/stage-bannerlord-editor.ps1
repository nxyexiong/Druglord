param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string]$InstalledModulePath
)

$ErrorActionPreference = "Stop"

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString(
                $algorithm.ComputeHash($stream)
            ).Replace("-", "")
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$moduleSource = Join-Path $repositoryRoot "src\Druglord\_Module"
$installedAssetSources = Join-Path $InstalledModulePath "AssetSources"
$installedAssets = Join-Path $InstalledModulePath "Assets"
$installedCache = Join-Path $InstalledModulePath "RuntimeDataCache"
$publishedCache = Join-Path $repositoryRoot "artifacts\published\Druglord\RuntimeDataCache"

New-Item -ItemType Directory -Path `
    $installedAssetSources, `
    $installedCache `
    -Force | Out-Null

$legacySources = @(
    @("AssetSources\Weapons\AKM\Import\druglord_akm.fbx", "druglord_akm.fbx"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_body_d.tga", "druglord_akm_body_d.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_body_n.png", "druglord_akm_body_n.png"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_body_s.tga", "druglord_akm_body_s.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_furniture_d.tga", "druglord_akm_furniture_d.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_furniture_n.tga", "druglord_akm_furniture_n.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_furniture_s.tga", "druglord_akm_furniture_s.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_magazine_d.tga", "druglord_akm_magazine_d.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_magazine_n.tga", "druglord_akm_magazine_n.tga"),
    @("AssetSources\Weapons\AKM\Import\Textures\druglord_akm_magazine_d.tga", "druglord_akm_magazine.tga"),
    @("AssetSources\Weapons\AWP\Import\druglord_awp.fbx", "druglord_awp.fbx"),
    @("AssetSources\Weapons\AWP\Import\Textures\druglord_awp_d.tga", "druglord_awp_d.tga"),
    @("AssetSources\Weapons\AWP\Import\Textures\druglord_awp_n.tga", "druglord_awp_n.tga"),
    @("AssetSources\Weapons\AWP\Import\Textures\druglord_awp_s.tga", "druglord_awp_s.tga")
)

foreach ($mapping in $legacySources) {
    $source = Join-Path $moduleSource $mapping[0]
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required editor source is missing: $source"
    }

    Copy-Item -LiteralPath $source `
        -Destination (Join-Path $installedAssetSources $mapping[1]) `
        -Force
}

$packageFiles = @(
    Get-ChildItem -LiteralPath $installedAssets `
        -Filter "*.tpac" `
        -File `
        -ErrorAction Stop
)
$cacheFiles = @(
    Get-ChildItem -LiteralPath $publishedCache `
        -Filter "*.rdc" `
        -File `
        -ErrorAction SilentlyContinue
)

if ($packageFiles.Count -gt 0 -and $cacheFiles.Count -eq 0) {
    throw (
        "No published runtime caches were found at '$publishedCache'. " +
        "Do not launch Bannerlord Editor with dependent TPACs active."
    )
}

foreach ($cacheFile in $cacheFiles) {
    Copy-Item -LiteralPath $cacheFile.FullName `
        -Destination (Join-Path $installedCache $cacheFile.Name) `
        -Force
}

$sourcePattern = [regex](
    '\$BASE/Modules/Druglord/AssetSources/+' +
    '([A-Za-z0-9_.-]+\.(?:fbx|tga|png))'
)
$sourceNames = New-Object `
    "System.Collections.Generic.HashSet[string]" `
    ([StringComparer]::OrdinalIgnoreCase)

foreach ($packageFile in $packageFiles) {
    $bytes = [IO.File]::ReadAllBytes($packageFile.FullName)
    $text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
    foreach ($match in $sourcePattern.Matches($text)) {
        [void]$sourceNames.Add($match.Groups[1].Value)
    }
}

$missingSources = @(
    foreach ($sourceName in $sourceNames) {
        $sourcePath = Join-Path $installedAssetSources $sourceName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            $sourceName
        }
    }
)

if ($missingSources.Count -gt 0) {
    throw (
        "Active TPAC source files are missing: " +
        ($missingSources -join ", ")
    )
}

foreach ($cacheFile in $cacheFiles) {
    $installedFile = Join-Path $installedCache $cacheFile.Name
    $sourceHash = Get-Sha256 -Path $cacheFile.FullName
    $installedHash = Get-Sha256 -Path $installedFile
    if ($sourceHash -ne $installedHash) {
        throw "Runtime cache copy failed validation: $($cacheFile.Name)"
    }
}

$clientBin = Join-Path $InstalledModulePath "bin\Win64_Shipping_Client"
$editorBin = Join-Path $InstalledModulePath "bin\Win64_Shipping_wEditor"
New-Item -ItemType Directory -Path $editorBin -Force | Out-Null
foreach ($binaryName in "Druglord.dll", "0Harmony.dll") {
    $binaryPath = Join-Path $clientBin $binaryName
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
        throw "Required editor binary is missing: $binaryPath"
    }

    Copy-Item -LiteralPath $binaryPath -Destination $editorBin -Force
}

Write-Output (
    "Editor staging verified: {0} TPAC source paths and {1} runtime caches." `
        -f $sourceNames.Count, $cacheFiles.Count
)

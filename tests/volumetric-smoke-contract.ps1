param(
    [switch]$RequireBundle
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$settings = Join-Path $root "GameData/PersistentSRBSmoke/PluginData/Settings.cfg"
$bundle = Join-Path $root "GameData/PersistentSRBSmoke/PluginData/VolumetricSmoke-WindowsD3D11.bundle"
$project = Join-Path $root "unity/VolumetricSmokeAssets"
$projectVersion = Join-Path $project "ProjectSettings/ProjectVersion.txt"

if (-not (Select-String -Path $settings -Pattern '^VOLUMETRIC_SRB_SMOKE$' -Quiet)) {
    throw "Settings.cfg must use the VOLUMETRIC_SRB_SMOKE root."
}
if (-not (Select-String -Path $settings -Pattern '^\s*schemaVersion\s*=\s*2\s*$' -Quiet)) {
    throw "Settings.cfg must declare schemaVersion = 2."
}

$removed = @(
    "src/PersistentSRBSmoke/SmokeParticlePool.cs",
    "src/PersistentSRBSmoke/NearNozzleSmokeLayer.cs",
    "src/PersistentSRBSmoke/WaterfallVolumetricLayer.cs",
    "src/PersistentSRBSmoke/EveVolumetricMaterial.cs"
)
foreach ($relative in $removed) {
    if (Test-Path (Join-Path $root $relative)) {
        throw "Legacy presentation source is still present: $relative"
    }
}

$requiredAssets = @(
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeRaymarch.shader",
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeTemporal.shader",
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeComposite.shader",
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeDepthCopy.shader",
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeShadow.shader",
    "Assets/VolumetricSmoke/Shaders/VolumetricSmokeTileCull.compute",
    "Assets/VolumetricSmoke/Editor/BuildVolumetricSmokeBundle.cs"
)
foreach ($relative in $requiredAssets) {
    if (-not (Test-Path (Join-Path $project $relative))) {
        throw "Missing volumetric AssetBundle source: $relative"
    }
}

if (-not (Select-String -Path $projectVersion -Pattern '^m_EditorVersion:\s*2019\.4\.18f1\s*$' -Quiet)) {
    throw "Unity AssetBundle project must stay pinned to KSP's Unity 2019.4.18f1."
}

$raymarch = Get-Content (Join-Path $project "Assets/VolumetricSmoke/Shaders/VolumetricSmokeRaymarch.shader") -Raw
foreach ($property in @("_SegmentData", "_TileCounts", "_TileIndices", "_CameraDepthTexture", "_ShapeNoise")) {
    if (-not $raymarch.Contains($property)) {
        throw "Raymarch shader does not expose required property $property"
    }
}

$assetSource = Get-ChildItem (Join-Path $project "Assets") -Recurse -File |
    ForEach-Object { Get-Content $_.FullName -Raw }
if ($assetSource -match 'Waterfall|\bEVE\b') {
    throw "Volumetric AssetBundle source must not have an implicit Waterfall/EVE dependency."
}

if ($RequireBundle -and -not (Test-Path $bundle)) {
    throw "Missing compiled D3D11 AssetBundle: $bundle"
}
Write-Host "Volumetric SRB smoke contract checks passed."

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$settings = Join-Path $root "GameData/PersistentSRBSmoke/PluginData/Settings.cfg"
$renderer = Join-Path $root "src/PersistentSRBSmoke/VolumetricSmokeRenderer.cs"

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

$rendererSource = Get-Content $renderer -Raw
if ($rendererSource -match 'AssetBundle\.Load|LoadBundle\(|ComputeShader|VolumetricSmoke-WindowsD3D11') {
    throw "Stock renderer must not require an AssetBundle, compute shader, or D3D11 bundle."
}
foreach ($required in @('"Particles/Alpha Blended"', 'CreateSmokeTexture', '_renderRecords.Sort')) {
    if (-not $rendererSource.Contains($required)) {
        throw "Stock renderer is missing required no-Unity path: $required"
    }
}
Write-Host "Stock-rendered SRB smoke contract checks passed."

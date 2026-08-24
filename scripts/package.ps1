param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$bundle = "GameData/PersistentSRBSmoke/PluginData/VolumetricSmoke-WindowsD3D11.bundle"
if (-not (Test-Path $bundle)) {
    throw "Missing required D3D11 volumetric AssetBundle: $bundle. Run scripts/build-volumetric-assets.ps1 first."
}
& "$root/tests/volumetric-smoke-contract.ps1" -RequireBundle
if ($LASTEXITCODE -ne 0) {
    throw "Volumetric smoke contract checks failed."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionData = Get-Content "GameData/PersistentSRBSmoke/PersistentSRBSmoke.version" -Raw | ConvertFrom-Json
    $Version = "v$($versionData.VERSION.MAJOR).$($versionData.VERSION.MINOR).$($versionData.VERSION.PATCH)"
}

$stage = Join-Path $root "dist/stage"
$outDir = Join-Path $root "dist"
$zip = Join-Path $outDir "PersistentSRBSmoke-$Version.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path "$stage/GameData/PersistentSRBSmoke/Plugins" -Force | Out-Null

Copy-Item "GameData/PersistentSRBSmoke/PluginData" "$stage/GameData/PersistentSRBSmoke/PluginData" -Recurse -Force
Copy-Item "GameData/PersistentSRBSmoke/PersistentSRBSmoke.version" "$stage/GameData/PersistentSRBSmoke/PersistentSRBSmoke.version" -Force
Copy-Item "src/bin/Release/PersistentSRBSmoke.dll" "$stage/GameData/PersistentSRBSmoke/Plugins/PersistentSRBSmoke.dll" -Force

if (Test-Path "README.md") { Copy-Item "README.md" "$stage/README.md" -Force }
if (Test-Path "README_RU.md") { Copy-Item "README_RU.md" "$stage/README_RU.md" -Force }
if (Test-Path "LICENSE") { Copy-Item "LICENSE" "$stage/LICENSE" -Force }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage/*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Created $zip"

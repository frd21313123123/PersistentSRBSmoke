param(
    [string]$UnityPath = $env:UNITY_PATH,
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "unity/VolumetricSmokeAssets"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "GameData/PersistentSRBSmoke/PluginData"
}

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe",
        "C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity"
    )
    $UnityPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($UnityPath) -or -not (Test-Path $UnityPath)) {
    throw "Unity 2019.4.18f1 was not found. Set UNITY_PATH to its Unity.exe before building the D3D11 AssetBundle."
}
if (-not (Test-Path (Join-Path $project "Assets"))) {
    throw "Unity project is missing: $project"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$env:PERSISTENT_SRB_SMOKE_BUNDLE_OUTPUT = [System.IO.Path]::GetFullPath($OutputDirectory)
& $UnityPath -batchmode -nographics -quit -projectPath $project `
    -executeMethod PersistentSRBSmoke.Assets.Editor.BuildVolumetricSmokeBundle.BuildWindowsD3D11 `
    -logFile -
if ($LASTEXITCODE -ne 0) {
    throw "Unity AssetBundle build failed with exit code $LASTEXITCODE."
}

$bundle = Join-Path $OutputDirectory "VolumetricSmoke-WindowsD3D11.bundle"
if (-not (Test-Path $bundle)) {
    throw "Unity finished without producing $bundle."
}
Write-Host "Built $bundle"

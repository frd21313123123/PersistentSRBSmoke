# Persistent SRB Smoke 1.0

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

Standalone volumetric SRB smoke for KSP 1.12.x. Version 1.0 replaces the former cloudlet/particle, Waterfall and EVE proxy presentation with one D3D11 volume pipeline.

## What it renders

- A fixed pool of up to 4,096 body-relative Hermite trail segments.
- The dense warm nozzle core and cold SRB trail through the same volume path, including the bell-to-trail transition.
- Continuous deposition by distance, with time-based optical mass at low speed so launch-pad smoke does not disappear before liftoff.
- Wind, buoyancy, expansion, dissipation, Universal Time/rails-warp advancement and loaded detached boosters.
- Up to eight logical 32³ pad-pressure tiles, rendered as local volume fields rather than an input of visual particles.
- D3D11 compute tile culling (16×16 pixels, 64 candidates), empty-space skipping, half-resolution raymarching, Beer–Lambert transmittance, two-lobe phase lighting, 3D noise and temporal reconstruction.
- Integrated segment-density terrain shadows using the existing bounded terrain cache.

Old segments coarsen before the fixed pool is exhausted. Merging preserves optical mass and momentum, is limited to a single celestial body and vessel, and never consumes the fresh nozzle/core records.

## Supported platform

This major release supports only:

- KSP 1.12.x
- Windows x64
- Direct3D 11 with compute shader support

If the graphics API is not D3D11, or the required AssetBundle is absent/incompatible, the effect is disabled and the reason is written to `KSP.log`. There is intentionally no particle, Waterfall or EVE fallback.

## Installation

1. Download a release ZIP.
2. Extract it into the KSP root so the bundle is at:
   `<KSP_DIR>/GameData/PersistentSRBSmoke/PluginData/VolumetricSmoke-WindowsD3D11.bundle`.
3. Start KSP with the Windows/D3D11 launcher option.

The mod still suppresses stock smoke for detected SolidFuel engines; stock flames and ordinary engine effects remain untouched.

## Configuration

Edit [`Settings.cfg`](GameData/PersistentSRBSmoke/PluginData/Settings.cfg). Its root is:

```cfg
VOLUMETRIC_SRB_SMOKE
{
    schemaVersion = 2
}
```

`Settings.cfg` from 0.x is not migrated. A v1 runtime ignores any old root or incompatible schema and uses clean v2 defaults; replace the file with the template from this release.

The default `Balanced` profile is designed for 1080p, 2–4 SRBs and a 1,024-segment screen budget (256 near / 512 mid / 256 far), with 24 / 14 / 8 view samples and four near/mid sun samples.

## Build on Windows

Requirements:

1. KSP 1.12.x and `KSP_DIR` pointing to its installation.
2. Visual Studio/.NET Framework build tools with the .NET Framework 4.7 targeting pack.
3. Unity **2019.4.18f1** with Windows Build Support. Set `UNITY_PATH` to its `Unity.exe`, or install it in the default Unity Hub path.

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
set UNITY_PATH=C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe
build.bat
```

To build only the asset bundle:

```powershell
./scripts/build-volumetric-assets.ps1
```

The Unity project lives in [`unity/VolumetricSmokeAssets`](unity/VolumetricSmokeAssets), is pinned to KSP's Unity version, and emits `VolumetricSmoke-WindowsD3D11.bundle` into `GameData/PersistentSRBSmoke/PluginData`.

## CI and checks

GitHub Actions uses Unity 2019.4.18f1 to build the Windows/D3D11 bundle, compiles the plugin against KSP skeleton references, validates the volumetric asset contract, and packages the DLL and bundle together. Configure `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` repository secrets for Unity activation.

Run the source-level contract locally without Unity:

```powershell
./tests/volumetric-smoke-contract.ps1
```

Run it with `-RequireBundle` after Unity has built the bundle. The deterministic segment-rule unit tests run with `dotnet run --project tests/VolumetricSmoke.AlgorithmTests.csproj --configuration Release`. The in-game acceptance matrix is documented in [`tests/README.md`](tests/README.md).

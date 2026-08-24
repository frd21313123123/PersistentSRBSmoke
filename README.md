# Persistent SRB Smoke 1.0.1

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

Persistent segment-based SRB smoke for KSP 1.12.x. Version 1.0.1 renders the current body-relative trail simulation with KSP's stock transparent particle material; it needs no Unity Editor, custom shader, AssetBundle, Waterfall, or EVE.

## What it renders

- A fixed pool of up to 4,096 body-relative Hermite trail segments.
- A dense warm nozzle core and cold SRB trail through the same soft crossed-ribbon path, including the bell-to-trail transition.
- Continuous deposition by distance, with time-based optical mass at low speed so launch-pad smoke does not disappear before liftoff.
- Wind, buoyancy, expansion, dissipation, Universal Time/rails-warp advancement and loaded detached boosters.
- Up to eight logical 32³ pad-pressure tiles, rendered as local soft fields rather than an input of visual particles.
- KSP's built-in `Particles/Alpha Blended` shader, a generated soft smoke texture, camera-facing crossed ribbons, depth testing, and back-to-front segment ordering.
- Integrated segment-density terrain shadows using the existing bounded terrain cache.

Old segments coarsen before the fixed pool is exhausted. Merging preserves optical mass and momentum, is limited to a single celestial body and vessel, and never consumes the fresh nozzle/core records.

## Supported platform

This major release supports only:

- KSP 1.12.x
- Windows x64
- Any KSP-supported graphics API that exposes the stock transparent particle shader

If KSP's built-in transparent particle shader cannot be found, the effect is disabled and the reason is written to `KSP.log`.

## Installation

1. Download a release ZIP.
2. Extract it into the KSP root so the DLL is at:
   `<KSP_DIR>/GameData/PersistentSRBSmoke/Plugins/PersistentSRBSmoke.dll`.
3. Start KSP normally.

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

The default `Balanced` profile is designed for 1080p, 2–4 SRBs and a 1,024-segment screen budget (256 near / 512 mid / 256 far).

## Build on Windows

Requirements:

1. KSP 1.12.x and `KSP_DIR` pointing to its installation.
2. Visual Studio/.NET Framework build tools with the .NET Framework 4.7 targeting pack.

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

`build.bat` compiles the DLL and validates the no-Unity renderer; no Unity installation or license is required.

## CI and checks

GitHub Actions compiles the plugin against KSP skeleton references, validates the stock-renderer contract, and packages the DLL. No Unity license secrets are required.

Run the source-level contract locally without Unity:

```powershell
./tests/volumetric-smoke-contract.ps1
```

The deterministic segment-rule unit tests run with `dotnet run --project tests/VolumetricSmoke.AlgorithmTests.csproj --configuration Release`. The in-game acceptance matrix is documented in [`tests/README.md`](tests/README.md).

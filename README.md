# Persistent SRB Smoke

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

A from-scratch KSP 1.12.x plugin that creates persistent, expanding world-space smoke trails for engines that consume `SolidFuel`.

## Features

- Detects loaded `ModuleEngines` / `ModuleEnginesFX` that use `SolidFuel`.
- Emits from every engine thrust transform and fills trail gaps based on distance travelled.
- Uses world-space Unity Shuriken particles registered with KSP `FloatingOrigin`.
- Keeps detached loaded SRBs working by scanning all loaded vessels, not only the active vessel.
- Scales emission, size, lifetime, opacity and spacing with engine thrust.
- Keeps smoke evolution synchronized with KSP Universal Time during time warp.
- Applies altitude-dependent wind shear and long-lived dynamic drift.
- Uses a coarse near-pad density field for horizontal exhaust outflow and rising pad-cloud billows.
- Suppresses stock/legacy SRB smoke while leaving flame and Waterfall effects alone.
- Generates its smoke texture procedurally at runtime; no borrowed texture assets are required.

## Performance architecture

The current renderer is still particle-based, but the expensive parts are deliberately bounded:

- Wind Perlin noise is sampled into a configurable altitude cache once per dynamic update instead of being evaluated for every particle.
- Old smoke is dynamically updated less often than fresh smoke.
- Smoke farther than `dynamicFarDistance` receives an additional update-rate reduction.
- Dynamic LOD keeps the existing particle velocity between updates, so Unity continues integrating motion every frame.
- The default cloudlet mesh uses three crossed transparent quads instead of six.
- Particle distance sorting is disabled by default to avoid sorting tens of thousands of transparent cloudlets.
- Reusable collections avoid repeated engine-scan allocations.
- Stock-smoke component discovery is cached and deep reflection is no longer repeated every frame.

The balanced defaults are currently `36000` maximum particles and `6 Hz` full dynamic updates. Increase them only after profiling your KSP install.

## Current renderer limitation

Persistent SRB Smoke does **not** yet use true volumetric raymarching. Each cloudlet is still a small crossed-quad mesh using an alpha-blended smoke texture. That means very dense trails can still become GPU-overdraw limited even after CPU-side optimizations.

The planned next renderer replaces old/distant particle cloudlets with chunked density volumes and adds raymarched lighting, Beer-Lambert extinction, phase-function scattering, self-shadowing, depth-aware composition and temporal accumulation. See [`docs/VOLUMETRIC_ROADMAP.md`](docs/VOLUMETRIC_ROADMAP.md).

## Installation

1. Download the latest `PersistentSRBSmoke-v*.zip` from Releases.
2. Extract the archive into the Kerbal Space Program root directory, or copy the `PersistentSRBSmoke` folder into `GameData/`.
3. Confirm the resulting path is `<KSP_DIR>/GameData/PersistentSRBSmoke/`.

## Configuration

Edit:

`GameData/PersistentSRBSmoke/PluginData/Settings.cfg`

Important performance controls:

```cfg
maxParticles = 36000
dynamicMotionHz = 6
cloudletPlanes = 3
sortParticles = false

dynamicMidAge = 0.20
dynamicOldAge = 0.55
dynamicMidStride = 2
dynamicOldStride = 4
dynamicFarDistance = 5000
dynamicFarStrideMultiplier = 2

windCacheLayers = 96
```

For better FPS, reduce `maxParticles`, `lifetime`, `particlesPerMeter` and `dynamicMotionHz` first. If the GPU is the bottleneck, keep `cloudletPlanes = 3` and `sortParticles = false`.

## Build on Windows

Requirements:

1. Kerbal Space Program 1.12.x installed.
2. Visual Studio 2022 with .NET desktop development / .NET Framework build tools.
3. Set the `KSP_DIR` environment variable to your KSP folder.

Example for Steam:

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

The project targets .NET Framework 4.7. A local build references KSP/Unity assemblies from:

`%KSP_DIR%\KSP_x64_Data\Managed`

After a successful build, the DLL is copied to:

`%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\PersistentSRBSmoke.dll`

Copy the repository's `GameData/PersistentSRBSmoke` folder into KSP `GameData` as well so `PluginData/Settings.cfg` is present.

## Automatic builds

GitHub Actions builds the plugin on pushes to `main`, pull requests and manual workflow runs. CI compiles against public KSP 1.11.2 skeleton reference assemblies only; these are compile-time stubs and are not included in release ZIPs.

A GitHub Release is created only for a `v*` tag or when a manual workflow run explicitly enables release creation.

## Compatibility

The plugin does not replace Waterfall effects. Waterfall can continue rendering the engine plume while Persistent SRB Smoke renders the long-lived particulate trail behind solid rocket motors.

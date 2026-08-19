# Persistent SRB Smoke — MVP 0.1

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

A from-scratch KSP 1.12.x plugin that creates persistent, expanding world-space smoke trails for engines that consume `SolidFuel`.

## What already works in this MVP

- Automatically detects loaded `ModuleEngines` / `ModuleEnginesFX` that use `SolidFuel`.
- Emits a continuous trail from every engine thrust transform.
- Uses world-space Unity Shuriken particles so smoke remains behind the rocket.
- Registers the particle system with KSP `FloatingOrigin`, which is important for long trails in flight.
- Fills gaps based on distance travelled, not only particles-per-second.
- Smoke expands and fades over ~150 seconds.
- Atmospheric-density scaling: dense near sea level, reduced near the edge of the atmosphere, none in vacuum.
- Procedural smoke texture generated at runtime; no borrowed textures/assets are required.
- Built-in particle noise for basic turbulent breakup.
- Handles detached, loaded SRBs because all loaded vessels are scanned, not only the active vessel.

## Current limitations

This is the first implementation pass. It does **not** yet include:

- true altitude-dependent wind layers / wind shear;
- depth-aware lighting and self-shadowing;
- ground collision / launch-pad billowing;
- GPU instancing/custom smoke shader;
- in-game settings GUI;
- LOD merging for very old/distant smoke;
- compatibility profiles for special mod fuels.

## Build on Windows

Requirements:

1. Kerbal Space Program 1.12.x installed.
2. Visual Studio 2022 with **.NET desktop development** / .NET Framework build tools.
3. Set the `KSP_DIR` environment variable to your KSP folder.

Example for a Steam install:

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

The project targets .NET Framework 4.7 and references KSP/Unity assemblies from:

`%KSP_DIR%\KSP_x64_Data\Managed`

After a successful build, the DLL is copied automatically to:

`%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\PersistentSRBSmoke.dll`

Copy the repository's `GameData/PersistentSRBSmoke` folder into your KSP `GameData` as well so `PluginData/Settings.cfg` is present.

## Automatic builds

GitHub Actions builds the plugin on every push to `main`, on pull requests, and on manual workflow runs. The resulting installation ZIP is available under the workflow run's **Artifacts** section.

CI compiles against public KSP 1.11.2 skeleton reference assemblies only. These are compile-time stubs and are **not** included in the release ZIP. Local builds with `KSP_DIR` continue to compile against your real KSP 1.12.x assemblies.

Push a tag such as `v0.1.0` to build the mod and automatically create a GitHub Release containing the installation ZIP.

## Tuning

Edit:

`GameData/PersistentSRBSmoke/PluginData/Settings.cfg`

Good first values for a dramatic shuttle-like trail:

```cfg
lifetime = 180
baseEmissionRate = 30
particlesPerMeter = 0.28
startSize = 3.2
sizeGrowth = 10.5
opacity = 0.76
turbulenceStrength = 0.8
```

For better FPS, reduce `maxParticles`, `lifetime`, and `particlesPerMeter` first.

## Compatibility design

The plugin does not replace Waterfall effects. Waterfall can continue rendering the engine plume while this plugin renders persistent smoke behind solid rocket motors.

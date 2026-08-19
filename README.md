# Persistent SRB Smoke

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

A from-scratch KSP 1.12.x plugin that creates persistent, expanding, volumetric-inspired smoke trails for engines that consume `SolidFuel`.

## v0.4 rendering

PersistentSRBSmoke now evaluates smoke lighting dynamically instead of treating every cloudlet as an equally lit translucent sprite.

- **Directional Kerbol lighting**: the renderer resolves the sun direction and elevation above the local horizon every frame.
- **Atmospheric attenuation**: direct sunlight is dimmed using a Beer-Lambert-style optical-depth approximation, with warmer light near the horizon.
- **Sky ambient + ground bounce**: smoke receives diffuse light from the sky dome and a weaker reflected-light term from the surface.
- **Dual-lobe Henyey-Greenstein phase scattering**: a strong forward lobe produces a bright backlit / silver-lining response, while a weaker backward lobe keeps front-lit smoke soft rather than flat.
- **Multiple-scattering approximation**: dense cloudlets recover some internal illumination instead of becoming uniformly black.
- **Beer-Powder response**: density controls extinction and powder-like light recovery through the interior of a cloudlet.
- **Spherical pseudo-normal shading**: the procedural density texture is dynamically relit from its UV coordinates so each slice reads more like a rounded cloud volume.
- **Soft Particles**: the stock KSP particle shader's depth-fade path is enabled (`SOFTPARTICLES_ON`, `_CameraDepthTexture`, `_InvFade`) so smoke blends into terrain, the launch pad and vessel geometry instead of being sharply clipped.
- **Dynamic fallback**: if a custom shader named `PersistentSRBSmoke/VolumetricSmoke` is present, its volumetric parameters are populated directly. Otherwise the built-in KSP shader plus CPU texture relighting is used automatically. No external shader loader is required for the default path.

## Other implemented systems

- Automatically detects loaded `ModuleEngines` / `ModuleEnginesFX` that use `SolidFuel`.
- Emits a continuous trail from every engine thrust transform.
- Uses world-space Unity Shuriken particles and registers the system with KSP `FloatingOrigin`.
- Fills gaps based on distance travelled, keeping the trail continuous at high vehicle speed.
- Engine-dependent smoke profiles: small separation motors emit fewer, smaller, darker, shorter-lived cloudlets than large SRBs.
- Suppresses stock/legacy SRB smoke while leaving Waterfall / flame effects alone.
- Long-lived expansion, fade, turbulence, buoyancy and altitude-dependent wind shear.
- KSP Universal Time synchronization, so smoke ages and moves correctly under time warp.
- Near-pad hold plus a density-driven pad-cloud solver that pushes dense exhaust sideways and lifts the thinning outer lobes.
- Procedural smoke density texture generated at runtime; no third-party smoke texture is redistributed.

## Installation

1. Download the latest `PersistentSRBSmoke-v*.zip` from Releases or a test artifact from GitHub Actions.
2. Extract it into the Kerbal Space Program root directory, or copy `PersistentSRBSmoke` into `GameData/`.
3. The final path should be `<KSP_DIR>/GameData/PersistentSRBSmoke/`.

Waterfall can remain installed. PersistentSRBSmoke renders the persistent SRB cloud while Waterfall can continue rendering the engine flame/plume.

## Volumetric settings

Edit:

`GameData/PersistentSRBSmoke/PluginData/Settings.cfg`

Default v0.4 optical settings:

```cfg
volumetricLightingEnabled = true
volumetricScatteringForward = 0.85
volumetricScatteringBackward = -0.35
volumetricMultipleScattering = 0.55
volumetricSoftDepthFactor = 1.65
volumetricSunIntensity = 1.10
volumetricAmbientIntensity = 0.46
volumetricBeerPowderFactor = 0.72
```

Useful tuning notes:

- Increase `volumetricScatteringForward` for a stronger silver lining when the plume is between the camera and Kerbol.
- Increase `volumetricMultipleScattering` if dense smoke interiors are too dark.
- Increase `volumetricSoftDepthFactor` for a wider, softer terrain/geometry intersection fade.
- Lower `volumetricSunIntensity` if the sun-facing edge becomes too bright.
- Increase `volumetricAmbientIntensity` if shadowed smoke is too dark.
- `volumetricBeerPowderFactor` changes the balance between extinction in dense smoke and powder-like internal light recovery.

## Build on Windows

Requirements:

1. Kerbal Space Program 1.12.x installed.
2. Visual Studio 2022 with **.NET desktop development** / .NET Framework build tools.
3. Set `KSP_DIR` to the KSP directory.

Example:

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

The project targets .NET Framework 4.7 and uses KSP/Unity assemblies from:

`%KSP_DIR%\KSP_x64_Data\Managed`

A successful local build copies the DLL to:

`%KSP_DIR%\GameData\PersistentSRBSmoke\Plugins\PersistentSRBSmoke.dll`

## Automatic verification and builds

GitHub Actions runs on pull requests, pushes to `main`, tags, and manual runs. CI now:

1. validates the structure of `Settings.cfg` and checks that all required volumetric keys exist;
2. restores the public KSP skeleton references;
3. compiles **Debug** with warnings treated as errors;
4. compiles **Release** with warnings treated as errors;
5. packages the Release DLL and `GameData` files into an installation ZIP.

The KSP skeleton assemblies are compile-time stubs only and are not redistributed in the mod archive. Local builds with `KSP_DIR` still compile against the real KSP 1.12.x assemblies.

Tags such as `v0.4.0` automatically publish a GitHub Release.

## Performance

The default rendering path does not raymarch every pixel through a global 3D volume. Instead it keeps the existing cloudlet particle representation and adds physically inspired optical terms plus a throttled dynamic density-texture relight. This keeps v0.4 practical for long SRB trails with tens of thousands of particles.

For better FPS, reduce `maxParticles`, `lifetime`, `particlesPerMeter`, or `dynamicMotionHz` first.

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
- Generates a shape/detail smoke mask with edge erosion and Beer-Lambert-like density at runtime.
- When Waterfall is installed, condenses the particle simulation into a bounded set of analytic
  proxy volumes using the same renderer architecture exposed by VolumetricVaporCones.
- Uses an independent EVE-inspired light volume for sunlight extinction, dense-core self-shadowing,
  ambient multiple scattering, sunset tint and restrained forward/backward phase scattering.
- Automatically uses the cloud-volume particle shader from an installed EVE Volumetric Clouds;
  no EVE binary, shader bundle or texture is copied or redistributed.
- Falls back to the standalone procedural cloudlet renderer when EVE is absent or incompatible.

## Performance architecture

The current renderer is still particle-based, but the expensive parts are deliberately bounded:

- Wind Perlin noise is sampled into a configurable altitude cache once per dynamic update instead of being evaluated for every particle.
- Old smoke is dynamically updated less often than fresh smoke.
- Smoke farther than `dynamicFarDistance` receives an additional update-rate reduction.
- Fully off-screen smoke time-slices wind/flow reevaluation while Unity keeps integrating velocity.
- Dynamic LOD keeps the existing particle velocity between updates, so Unity continues integrating motion every frame.
- The default cloudlet mesh uses three crossed transparent quads instead of six.
- Particle distance sorting is disabled by default to avoid sorting tens of thousands of transparent cloudlets.
- Particle renderers skip shadow, probe and motion-vector passes; mesh GPU instancing is enabled when the active shader supports it.
- Large booster clusters share deposition samples with Beer-Lambert optical-depth compensation instead of producing thinner trails.
- Direct and ambient smoke lighting are cached per spatial cell and refreshed in separate time slices;
  particles sample the cache instead of each marching toward the Sun.
- The optional Waterfall layer condenses up to `48000` simulation particles into at most `96`
  analytic volumes, so its proxy count does not grow with the number of active boosters.
- Reusable collections avoid repeated engine-scan allocations.
- Stock-smoke component discovery is cached and deep reflection is no longer repeated every frame.

The visual defaults restore the v0.6.1 trail (`48000` maximum particles and three cloudlet planes), while expensive dynamic motion runs at `4 Hz` and projected shadows at `8 Hz`.

## Renderer architecture

`VolumetricVaporCones` does not contain a renderer of its own; it configures Waterfall's
`Additive (Volumetric)` shader. Persistent SRB Smoke now detects that already-loaded shader and
proxy model at runtime, groups smoke into body-relative cells, and renders a bounded analytic volume
for every retained cell. The normal Shuriken system remains authoritative for motion, time warp and
projected shadows, and is the automatic fallback when Waterfall is absent.

The default overlay retains a reduced-opacity particle shell because Waterfall's shader is additive
and was designed for vapor/plumes rather than fully opaque grey smoke. Set
`waterfallVolumetricReplaceParticles = true` for the faster pure analytic presentation. No Waterfall
shader, model, texture, DLL or VolumetricVaporCones file is copied into this mod.

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
maxParticles = 48000
dynamicMotionHz = 4
offscreenDynamicMotionHz = 0.5
cloudletPlanes = 3
sortParticles = false

preferEveVolumetricShader = true
volumetricDensity = 1.05
volumetricMinScatter = 0.82
volumetricSoftDepth = 0.008

waterfallVolumetricEnabled = true
waterfallVolumetricReplaceParticles = false
waterfallVolumetricMaxVolumes = 96
waterfallVolumetricCellSize = 72
waterfallVolumetricBrightness = 0.65
waterfallParticleShellOpacity = 0.55

dynamicMidAge = 0.20
dynamicOldAge = 0.55
dynamicMidStride = 3
dynamicOldStride = 8
dynamicFarDistance = 3500
dynamicFarStrideMultiplier = 3

adaptiveParticleCulling = false
fullDensityEmitterBudget = 8
minimumEmitterDensityScale = 0.35
windCacheLayers = 64

lightVolumeEnabled = true
lightVolumeCellSize = 72
lightMarchSteps = 4
lightDirectTimeSlices = 4
lightAmbientTimeSlices = 8
```

For better FPS, reduce `lifetime` or `maxParticles` first. Keep `adaptiveParticleCulling = false`: that legacy age-only path destroyed visible density. Large booster clusters are handled by compensated deposition budgeting instead.

`preferEveVolumetricShader` does not install EVE or load files from the reference archive. It only
uses EVE's shader registry when EVE is already installed. Windows/D3D11 uses the procedural fallback
because EVE routes that shader through a private off-screen compositor which cannot accept Unity
Shuriken. The selected mode and fallback reason are written to `KSP.log`.

The Waterfall bridge is optional and uses assets from the user's installed Waterfall at runtime.
The architecture was investigated through
[VolumetricVaporCones](https://github.com/huj31415/VolumetricVaporCones) (MIT) and
[Waterfall](https://github.com/post-kerbin-mining-corporation/Waterfall) (CC BY-NC-SA 4.0).

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

# Persistent SRB Smoke

[![Build](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml/badge.svg)](https://github.com/frd21313123123/PersistentSRBSmoke/actions/workflows/build.yml)

Persistent SRB smoke for **Kerbal Space Program 1.12.x**.

## v0.4.1 renderer

v0.4.1 fixes the v0.4.0 black-smoke regression by keeping the engine-specific grey/warm albedo as the dominant colour. Lighting now adds local core shadow, ambient fill and sun-facing highlights instead of multiplying the complete plume by a dark global tint.

The mod now has two volumetric rendering paths.

### True raymarched volume

When `PersistentSRBSmoke/VolumetricSmoke` is available from the optional `.shab` bundle, the Shuriken ParticleSystem remains responsible for lifetime, wind, expansion, time warp and pad-cloud physics, while rendering switches to instanced 3D proxy volumes.

The shader in `Shaders/PersistentSRBVolumetricSmoke.shader` implements procedural 3D FBM density, erosion, primary ray marching, Kerbol shadow marching, dual-lobe Henyey-Greenstein scattering, Beer-Lambert extinction, Beer-Powder response, multiple-scattering fill, sky ambient, ground bounce and depth fading.

The shader bundle is built with **KSPBuildTools + Unity 2019.4.18f1** and loaded in KSP by **Shabby**. `.github/workflows/build-shaders.yml` is already configured. GitHub needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` repository secrets to compile the `.shab` artifact. Until that one-time Unity activation is configured, test ZIPs use the native slice-volume fallback below.

### Native 3D slice-volume fallback

Without the custom shader bundle, each smoke cloudlet uses density slices distributed throughout X/Y/Z instead of six flat cards crossing at one point. This path is not ray marching, but it occupies real 3D volume and requires no shader dependency.

Fallback lighting is clamped so the smoke cannot become coal-black simply because direct sunlight is weak:

```cfg
nativeVolumeSlicesPerAxis = 5
nativeVolumeSliceOpacity = 0.20
fallbackMinimumLight = 0.72
fallbackCoreShadow = 0.16
```

## Raymarch defaults

```cfg
raymarchedVolumetricEnabled = true
raymarchMaxCloudlets = 7000
raymarchSteps = 24
raymarchShadowSteps = 4
raymarchDensityMultiplier = 1.15
raymarchExtinction = 2.10
```

For better GPU performance, reduce `raymarchMaxCloudlets` first, then `raymarchSteps`, then `raymarchShadowSteps`.

## Other systems

- automatic SolidFuel engine detection;
- engine-specific smoke scaling for large SRBs vs separation motors;
- stock/legacy smoke suppression while leaving Waterfall/flame visuals alone;
- continuous high-speed trail emission;
- world-space smoke and KSP FloatingOrigin support;
- expansion, turbulence, buoyancy, wind shear and time-warp synchronization;
- density-driven Shuttle-style launch-pad cloud;
- soft-particle depth fading;
- procedural density/noise with no borrowed smoke assets.

## Installation

Copy `GameData/PersistentSRBSmoke` into the KSP `GameData` directory.

The native 3D fallback needs nothing else. For true raymarching, also install **Shabby** and copy `PersistentSRBSmokeVolumetric.shab` into:

```text
GameData/PersistentSRBSmoke/Shaders/
```

## Build

```bat
set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
build.bat
```

GitHub Actions validates `Settings.cfg`, compiles Debug and Release with warnings-as-errors, and packages the installation ZIP.

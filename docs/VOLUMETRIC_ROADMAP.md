# Volumetric renderer roadmap

The current renderer intentionally remains compatible with a normal KSP plugin build: it uses Unity Shuriken mesh particles and runtime-generated textures. The next major renderer should replace old/distant cloudlets with chunked density volumes rather than making the existing transparent-particle system increasingly complex.

## Target architecture

```text
EngineSmokeEmitter
        |
        v
SmokeSimulation
  |- wind / velocity field
  |- density injection
  |- temperature / buoyancy
  `- near-pad pressure flow
        |
        v
SmokeChunkManager
  |- near chunks: high resolution
  |- mid chunks: medium resolution
  `- far chunks: low resolution / merged history
        |
        v
VolumetricSmokeRenderer
  |- density raymarch
  |- Beer-Lambert extinction
  |- phase-function scattering
  |- self shadowing
  |- scene-depth composition
  `- temporal accumulation
```

## Phase 1: shader and asset pipeline

- Add a dedicated Unity project or reproducible AssetBundle build step for KSP 1.12.x's Unity version.
- Bundle a custom smoke raymarch shader instead of relying on `Shader.Find` for stock alpha particle shaders.
- Add a small loader that falls back to the current particle renderer if the bundle cannot be loaded.
- Keep the existing renderer as a compatibility / low-quality mode.

## Phase 2: chunked density volumes

- Split the trail into world-space chunks along the flight path.
- Use higher-resolution chunks near the camera and progressively lower resolution farther away.
- Inject new SRB exhaust into density/temperature fields rather than creating one long-lived render particle per sample.
- Merge old history into lower-resolution chunks so render cost does not grow linearly with launch duration.
- Preserve FloatingOrigin compatibility by shifting chunk transforms/coordinates with KSP.

A practical first prototype can use 32^3 or 48^3 density textures for near chunks and 16^3 for far chunks. The exact values need GPU profiling in KSP rather than being hard-coded as final defaults.

## Phase 3: lighting

Implement physically motivated attenuation:

```text
T = exp(-density * extinction * distance)
```

Then add directional scattering with a Henyey-Greenstein phase function. Start with a modest forward-scattering coefficient and tune from real SRB launch references rather than baking brightness into the base particle colour.

Add a short secondary march toward the sun for approximate self-shadowing. Keep sun-shadow sample counts much lower than view-ray sample counts.

## Phase 4: noise and motion

- Replace decorative 2D cloudlet noise with 3D shape/detail noise.
- Use low-frequency fBm for macro shape and Worley/erosion noise for broken billow edges.
- Add curl/vorticity to the velocity field so launch clouds roll instead of only expanding radially.
- Carry density, velocity and temperature in the near-pad simulation grid.
- Advect old density at a lower rate than fresh density.

## Phase 5: temporal and depth integration

- Jitter raymarch sample positions with blue noise.
- Reproject/accumulate previous frames to reduce the required samples per frame.
- Reject history around fast camera motion and chunk discontinuities.
- Sample the scene depth texture so smoke fades correctly into terrain and launch structures.

## Performance goals

The volumetric path should be designed around bounded work:

- fixed maximum visible chunks;
- camera-distance LOD;
- frustum rejection;
- update-rate LOD for old/far simulation chunks;
- temporal reconstruction instead of very high per-frame ray sample counts;
- optional self-shadowing quality tiers;
- particle fallback for low-end hardware.

The important constraint is that a five-minute trail must not cost five times as much to render as a one-minute trail. Old trail history should be merged/coarsened so cost approaches a configured ceiling.

## Why this is separate from the current optimization PR

A `.shader` source file in this repository is not enough for a released KSP plugin: Unity shaders need to be compiled for the game's Unity/runtime targets and loaded through a compatible asset pipeline. Keeping that work separate allows the current CPU/GPU optimizations to remain buildable through the existing MSBuild-only CI while the AssetBundle pipeline is developed and validated independently.

# Volumetric SRB smoke architecture

This document records the v1 implementation rather than a particle-to-volume proposal.

## Data flow

```text
ModuleEngines (SolidFuel)
  → SrbSmokeInjection
  → VolumetricSmokeSystem
      ├─ fixed 4,096 body-relative TrailSegment records
      ├─ warm nozzle segment + cold trail segments
      ├─ 8 logical 32³ pad volume tiles
      ├─ wind / expansion / dissipation / Universal Time update
      └─ mass-preserving per-vessel merge
  → VolumetricSmokeRenderer (Windows/D3D11)
      ├─ compute tile culling
      ├─ half-resolution raymarch
      ├─ temporal reconstruction
      └─ weighted-blended composite
  → VolumeTrailShadowLayer → terrain-cache projection
```

## Invariants

- All persistent records are relative to `CelestialBody.transform`; `FloatingOrigin` changes do not rewrite the pool.
- Distance controls the number of connected records; low-speed time deposition controls mass. A stationary firing SRB must emit smoke.
- The fresh nozzle segment is never merged or evicted ahead of an old trail record.
- Merging preserves total `OpticalMass` and mass-weighted velocity. Its spatial key includes both body and vessel identity.
- Storage overflow runs coarsening before the oldest non-nozzle trail record can be discarded. The renderer does not use a particle fallback.
- The D3D11 bundle is an application requirement, not an optional visual enhancement. Failure is logged and disables the effect.

## Balanced profile

| Stage | Budget | Samples |
| --- | ---: | ---: |
| Near | 256 segments | 24 view / 4 sun |
| Mid | 512 segments | 14 view / 4 sun |
| Far | 256 segments | 8 view / 0 sun |

Tile culling uses 16×16-pixel screen tiles and 64 entries per tile. Segment storage remains 4,096 records; view selection is independent of coarsening, so a dense visible core is not erased merely because the scene has a long old trail.

## AssetBundle contract

`unity/VolumetricSmokeAssets` is pinned to Unity 2019.4.18f1 and emits:

- `VolumetricSmokeRaymarch`
- `VolumetricSmokeTemporal`
- `VolumetricSmokeComposite`
- `VolumetricSmokeDepthCopy`
- `VolumetricSmokeShadow`
- `VolumetricSmokeTileCull`
- `VolumetricSmokeShapeNoise`

The runtime requires all six named assets in `VolumetricSmoke-WindowsD3D11.bundle`. CI builds it with the same Unity version and `tests/volumetric-smoke-contract.ps1` checks the source/asset contract before packaging.

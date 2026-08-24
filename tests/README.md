# SRB smoke verification

`volumetric-smoke-contract.ps1` is the fast source-level check used in CI. It verifies schema v2, the absence of legacy renderer source, and the no-Unity stock-material renderer contract.

`VolumetricSmoke.AlgorithmTests.csproj` is a platform-independent unit executable for the shared trail rules: continuous stationary/moving insertion, mass/momentum merge rules, five-minute LOD classification, step-independent dissipation, Universal Time warp aging and body-relative Floating Origin coordinates.

```powershell
dotnet run --project tests/VolumetricSmoke.AlgorithmTests.csproj --configuration Release
```

The following checks require KSP 1.12.x on Windows:

| Scenario | Expected result |
| --- | --- |
| One SRB held on pad | Warm bell volume connects to a dense local pad field; no initial empty trail. |
| One slow liftoff | Continuous soft column, with no velocity-dependent gaps. |
| Four clustered SRBs | Cores remain connected; nearby old trails combine without becoming transparent. |
| Detached burning booster | Its own body-relative trail persists and does not merge with another vessel. |
| Evening light | Soft sunlight and a warm horizon tint; no unlit white glow at night. |
| Camera at bell / far away | Depth clips against vehicle/terrain; the crossed soft ribbons stay coherent. |
| Terrain shadow | Soft projected density shadow follows Sun direction and respects cached terrain height. |
| Physics and rails warp | Segment age/dissipation advance with Universal Time; no frozen trail after a camera/origin discontinuity. |

Performance acceptance is measured on a fixed 1080p test configuration with the `Balanced` profile and 2–4 SRBs: median frame time ≤16.7 ms and p99 ≤25 ms. Capture KSP.log alongside the benchmark; startup must report use of the stock KSP smoke shader.

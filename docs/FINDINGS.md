# What the game actually does

Notes from decompiling `Game.dll` of Cities: Skylines II `1.6.0f1 (419.d6c6)`, Unity `2022.3.71f1`.
Everything below was read out of the current build, not carried over from launch-era write-ups —
several things people still repeat about this game stopped being true.

## The build

```
Game version              1.6.0f1 (419.d6c6) [6216.19404]
Unity                     2022.3.71f1 (c9bf13b0b844)
Graphics API              Direct3D 11.0 [level 11.1]
Rendering Threading Mode  LegacyJobified
Scripting                 Mono 6.13.0, .NET Standard 2.1
Modding runtime           Builtin
lib_burst_generated.dll   57 MB
Game.dll                  11.5 MB managed
```

Two of these matter more than they look.

**Still Direct3D 11.** D3D11 carries far higher per-draw-call CPU overhead than D3D12, and the game
submits thousands of them per frame. Cutting draw calls is therefore worth more here than cutting
triangles — and the cheapest draw calls to cut are shadow ones, because they are invisible by
definition.

**57 MB of Burst-compiled native code.** Harmony patches managed methods only. The hot culling,
skinning and batching jobs are past that boundary, so the launch-era modding advice of "patch the
culling system" does not apply. Anything that needs to change those has to change their *inputs*.

## What launch-era analysis got right, and what has changed

The 2023 autopsy measured a frame at 121M input vertices, 36M rasterized triangles, 6,705 draw calls
— **4,828 of them (72%) in the shadow pass alone, costing ~40 ms**. Citizen meshes carried ~56,000
vertices with no LOD variants; 13 characters in one profiled frame affected zero pixels. Culling was
frustum-only.

Checking each of those against the current build:

| Claim | Status in 1.6.0f1 |
|---|---|
| Frustum culling only, no occlusion culling | **Still true.** `FrustumPlanes.cs` exists; nothing in `Game.Rendering` implements occlusion. |
| `directionalShadowResolution` is wired to nothing | **Fixed.** `ShadowsQualitySettings.Apply()` now calls `m_SunLightData.SetShadowResolution()`. |
| No way to reject small shadow casters | **Fixed, and this is the big one.** See below. |
| Texture filtering has no anisotropic option | **Still true.** `filterMode` is `Trilinear`. |

## The lever: shadow-caster culling thresholds

`RenderingSystem.GetShadowCullingData()` reads two values that did not exist at launch:

```csharp
ShadowsQualitySettings qualitySetting = instance.graphics.GetQualitySetting<ShadowsQualitySettings>();
result.y = qualitySetting.shadowCullingThresholdHeight;
result.z = qualitySetting.shadowCullingThresholdVolume;
```

Iceflake built exactly the mechanism needed to attack the 40 ms shadow pass: reject casters below a
height or volume threshold. The catch is how it is exposed.

Neither property carries a `[SettingsUISlider]` attribute. **There is no UI control for them at all.**
The only way a player reaches them is by selecting a whole quality preset:

| Preset | `...ThresholdHeight` | `...ThresholdVolume` | `directionalShadowResolution` | `terrainCastShadows` |
|--------|---------------------|----------------------|-------------------------------|----------------------|
| High   | 1.0 | 1.0 | 4096 | true  |
| Medium | 1.5 | 1.5 | 2048 | true  |
| Low    | 2.0 | 2.0 | 1024 | false |

So the most aggressive value the game will ever set is `2.0`, bundled with a shadow resolution drop
you may not want. Writing `6.0` while keeping resolution at 2048 is not a configuration the game can
produce, and it is the single highest-value thing this patcher does.

## The other lever the UI hides

`ExtraQualitySettings.cascadeShadowSplitCount` — slider range **0 to 4**, but only two presets are
registered (`Low = 2`, `High = 4`). Every cascade re-rasterizes the scene into another shadow map.
Going from 4 to 2 halves that work; 1 is reachable and the UI will never offer it.

The same class carries `shaderQualityTier`, which toggles the `COLOSSAL_QUALITY_TIER_HIGH/MED/LOW`
shader keywords — a reduction at the shader-variant level, not just a parameter change.

Notably, `ExtraQualitySettings` is **absent from a freshly written `Settings.coc`**. The game only
persists blocks it has touched, so the patcher has to create it.

## Ranges, read from the game

These come from the `[SettingsUISlider]` attributes in `Game.Settings`, and are what the patcher
enforces. Anything outside them is rejected before it is written.

| Setting | Min | Max | Medium preset |
|---|---|---|---|
| `LevelOfDetailQualitySettings.levelOfDetail` | 0.1 | 1.0 | 0.5 |
| `LevelOfDetailQualitySettings.maxLightCount` | 512 | 16384 | 4096 |
| `LevelOfDetailQualitySettings.meshMemoryBudget` | 128 | 4096 MB | 1024 |
| `ExtraQualitySettings.cascadeShadowSplitCount` | 0 | 4 | — |
| `VolumetricsQualitySettings.budget` | 0.001 | 1.0 | 0.33 |
| `VolumetricsQualitySettings.resolutionDepthRatio` | 0.001 | 1.0 | 0.666 |
| `SSAOQualitySettings.maxPixelRadius` | 16 | 256 | 40 |
| `SSAOQualitySettings.stepCount` | 2 | 32 | 6 |
| `SSRQualitySettings.maxRaySteps` | 1 | 128 | 32 |
| `WaterQualitySettings.maxTessellationFactor` | 0 | 15 | 6 |
| `TextureQualitySettings.mipbias` | 0 | 3 | 1 |
| `DynamicResolutionScaleSettings.minScale` | 0.5 | 1.0 | 0.5 |

`shadowCullingThresholdHeight` and `shadowCullingThresholdVolume` appear in **no** slider attribute,
so they have no documented bound. The values this tool writes (3–6) are extrapolated from the preset
progression and have not been validated against a hard engine limit. That is a known gap.

## `Settings.coc`

Plain text. Named sections, each followed by a JSON object:

```
Graphics Settings
{ "qualitySettings": [ { ..., "@type": "Game.Settings.ShadowsQualitySettings" }, ... ] }
Interface Settings
{ "locale": "pt-BR" }
```

Entries in `qualitySettings` are discriminated by `@type`. `QualitySetting.Level` includes a `Custom`
member, and `GetLevel()` returns it whenever the live values match no preset — so hand-written values
are a state the game understands, not something it will fight.

## Where a settings patch stops being enough

Settings cannot express "this specific citizen is 200 m away, draw a cheap version". Reaching that
needs a code mod, and the useful surface for one is that `RenderingSystem` exposes plain managed
properties Burst does not protect:

```csharp
public float levelOfDetail    { get; set; }   // 0.5f default
public int   maxLightCount    { get; set; }
public bool  lodCrossFade     { get; set; }
public bool  disableLodModels { get; set; }
public int   shadowBias       { get; set; }
```

Beyond that, the interesting targets in `Game.Rendering` are `PreCullingSystem` (125 KB — frustum
culling and LOD selection), `BatchDataSystem` (116 KB), and the citizen skinning path
(`AnimatedSystem`, `ProceduralSkeletonSystem`, `InitializeBonesSystem`). `CullingInfo` carries a
per-entity `m_MinLod` and `m_PassedCulling`, which is the natural place for per-entity budgeting.

## The shape of the codebase

A full type inventory across `Game.dll` and the `Colossal.*` assemblies (generated with
`ilspycmd -l`, kept out of the repo under `.research/typemap/`):

**11,113 types**, of which `Game.dll` alone holds 8,904. Among them are **906 ECS systems**:

| Namespace | Systems | Why it matters |
|---|---:|---|
| `Game.Simulation` | 315 | Where CPU cost concentrates as a city grows |
| `Game.UI` | 123 | |
| `Game.Serialization` | 74 | |
| `Game.Rendering` | 63 | The GPU-side surface this project targets |
| `Game.Tools` | 55 | |
| `Game.Prefabs` | 36 | Mesh and geometry setup |

Water is spread across roughly twenty systems (`WaterSystem`, `SoilWaterSystem`,
`GroundWaterSystem`, `WaterPipeFlowSystem` and friends) — consistent with Iceflake publicly
naming water simulation as an area they are still optimising.

## System update intervals: a lever, with a trap

Every system can declare how often it runs:

```csharp
public virtual int GetUpdateInterval(SystemUpdatePhase phase) => 1;
public virtual int GetUpdateOffset(SystemUpdatePhase phase)   => -1;
```

254 overrides across 198 files, overwhelmingly in `Game.Simulation`. Both methods are plain
managed virtuals, so they are reachable by Harmony where the jobs they schedule are not.

**The trap**, in `UpdateSystem`:

```csharp
if (!math.ispow2(interval))
    throw new Exception("System update interval not power of 2");
```

An interval that is not a power of two throws. Anything scaling these values must round to a
power of two or it will crash the game rather than slow it down.

This is recorded as a finding, not implemented. Changing how often a simulation system runs
changes the simulation, and the design rule for this project is that visual waste is fair game
while the simulation is not. If it is ever used, it should be restricted to systems that are
demonstrably cosmetic.

## Measurement

The game ships its own benchmark, and it is a better bench than playing by hand: a fixed camera
path over a city bundled with the game, 90 seconds every time — 20s paused, 35s at speed 1, 35s at
speed 3. Two runs therefore differ only by what was changed between them.

It records the CPU-game, CPU-render and GPU cost of *every individual frame* and writes the lot to
`Benchmark.coc` beside `Settings.coc`, in the same section-plus-JSON format. That is what makes real
lows computable after the fact rather than estimated.

Three things make it usable unattended:

- **`--benchmark`** on the command line runs it straight from startup (`GameManager.Configuration`).
  The game also merges anything in `<user data>/runOnce.txt` into its command line and then deletes
  the file, so the flag can be armed without controlling how the process gets spawned.
- **`steam_appid.txt`** containing `949230` must sit next to `Cities2.exe` to launch it directly.
  Without it the Steam platform service fails to initialise and the asset database dies with a null
  reference in `PopulateFromDataSource` before the menu — the failure looks nothing like its cause.
- **The window has to be in the foreground.** Backgrounded, rendering is throttled and textures do
  not stream, so the run completes and writes a plausible-looking result that measures nothing.

`scripts/Run-Benchmark.ps1` handles all three and files each result as `Benchmark.<label>.coc`;
`cs2patch bench` reads them back and prints the comparison.

## What the profiles actually buy

RTX 3050 8GB, Ryzen 5 4600G, 1080p, game build 1.6.0f1. Baseline is the game's own auto-detected
Medium. Frame cost is whichever of CPU and GPU finished last, per frame.

| run | avg fps | 1% low | GPU ms | render ms | GPU-bound |
|---|---|---|---|---|---|
| baseline | 22.7 | 10.5 | 43.8 | 9.1 | 97% |
| free | 23.7 | 10.7 | 41.9 | 8.2 | 96% |
| traffic | 24.1 | 10.8 | 41.0 | 8.1 | 94% |
| potato | 36.8 | 10.3 | 26.6 | 6.2 | 88% |

Read that carefully, because the headline is not the story.

**`free` and `traffic` are worth about 5-6%.** The fourteen changes that separate them buy one
percentage point. Shadow-caster thresholds, cascade count, SSR steps, SSAO steps, volumetrics
budget — the whole cheap tier — is nearly free performance, but it is also nearly no performance.

**`potato` is worth 62% on the average**, and it gets there on the two settings the cheap tier
refuses to touch: `levelOfDetail` at 0.2 and `shaderQualityTier` at Low. Cutting what is *drawn*
works; cutting what is *post-processed* barely registers.

**No profile moves the 1% low at all.** It sits at 10.3-10.8 fps in every configuration including
the untouched baseline. That is the number that decides whether the game feels smooth, and settings
do not currently touch it.

Split by phase, `potato` shows why:

| phase | avg fps | 1% low |
|---|---|---|
| paused | 44.2 | 25.8 |
| speed 1 | 41.3 | 11.0 |
| speed 3 | 27.9 | 10.9 |

With the camera parked and the simulation stopped — rendering alone — it manages 44 fps with a 26
fps floor. Start the simulation and the floor falls to 11 and stays there.

## Where the frames are lost, by the numbers

Frame-time distributions, same two runs:

| | baseline | potato |
|---|---|---|
| GPU average | 43.8 ms | 26.6 ms |
| GPU frames over 33 ms | 1660 of 2001 | 560 of 3165 |
| GPU worst frame | 103 ms | 642 ms |
| CPU-game average | 17.3 ms | 13.6 ms |
| CPU-render average | 9.1 ms | 6.2 ms |

Two conclusions, and one non-conclusion.

**The average is GPU-bound and the tail is too.** `potato`'s 99th-percentile GPU frame is 60 ms.
The worst 1% of frames sit between 60 and 100 ms, and they are GPU frames — not simulation stalls,
despite the 1% low only degrading once the simulation starts. More GPU savings will still move it.

**The CPU is not the wall.** Render-thread time is 6-9 ms and never exceeds 30 ms in either run.
`potato` *reduces* CPU-game time as well, because fewer visible objects means less to prepare.

**The 0.1% low is not a real regression.** It appears to collapse by half under `potato`, but 0.1%
of 3165 frames is four frames, and both runs contain exactly three frames over 100 ms. `potato`'s
single 642 ms frame is almost certainly the shader-variant compile triggered by
`Shader.EnableKeyword("COLOSSAL_QUALITY_TIER_LOW")` — a one-time cost that lands inside the
measurement window. Quote the 1% low, which is 32 frames; treat the 0.1% column as an outlier
detector rather than a result.

## The honest caveat

The table above is measured on one machine, at one resolution, on one build, with one run per
configuration — no repeats, so run-to-run variance is unquantified. The relative ordering is large
enough to be trustworthy; the exact percentages are not. Nothing here has been measured on any
other hardware, and the goal for this project is 60 stable fps, which none of these configurations
reaches.

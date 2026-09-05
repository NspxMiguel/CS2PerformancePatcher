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

RTX 3050 8GB, Ryzen 5 4600G, 1080p, game build 1.6.0f1, no mods loaded. Baseline is the game's own
auto-detected Medium. Frame cost is whichever of CPU and GPU finished last, per frame.

| run | avg fps | 1% low | GPU ms | render ms | GPU-bound |
|---|---|---|---|---|---|
| baseline | 22.7 | 10.5 | 43.8 | 9.1 | 97% |
| free | 23.7 | 10.7 | 41.9 | 8.2 | 96% |
| traffic | 24.1 | 10.8 | 41.0 | 8.1 | 94% |
| sharp | 30.3 (×2 runs) | 10.6 / 9.6 | 32.5 | 7.5 | 90% |
| potato | 36.8 | 10.3 | 26.6 | 6.2 | 88% |
| super-potato | 48.0 (×3 runs) | 16.9 | 19.3 | 4.9 | 72% |
| super-potato + `targetPatchSize` 64 | 51.2 (×2 runs) | 16.4 | 18.3 | 4.4 | 74% |

### How much of that is noise

Three runs of `super-potato`, identical settings, back to back: the average landed within 1.3% of
itself every time; the 1% low ranged from 16.5 to 20.2, a spread of 22%.

So **the average is trustworthy to about ±2%, and the 1% low to about ±10%.** Everything below is
read against that. A change worth five percent on the 1% low cannot be distinguished from doing
nothing, and this document does not pretend otherwise.

### What actually earns its place

**`free` and `traffic` are worth 5-6%, which is inside nothing.** The fourteen changes separating
them buy one percentage point. Shadow-caster thresholds, cascade count, SSR steps, SSAO steps,
volumetrics budget — the whole cheap tier — is nearly free performance and also nearly no
performance.

**`potato` is worth 62%**, and it earns that on the two settings the cheap tier refuses to touch:
`levelOfDetail` at 0.2 and `shaderQualityTier` at Low. Cutting what is *drawn* works. Cutting what
is *post-processed* does not register.

Measured one at a time on top of `potato`, three settings carry nearly everything, and none of them
was in a profile:

| change | avg fps | 1% low |
|---|---|---|
| `levelOfDetail` 0.2 → 0.1 | 42.5 (+87% vs baseline) | 18.7 (+78%) |
| `dlssQuality` → UltraPerformance | 42.1 (+86%) | 13.9 (+33%) |
| `cascadeShadowSplitCount` 1 → 0 | 37.0 (+63%) | 13.1 (+25%) |

They attack different things — geometry, pixels, shadow rasterisation — so they compound rather
than overlap. Together they are `super-potato`.

The DLSS one is the clearest example of what this whole tool is for. `ApplyDLSSAutoSettings` picks
the upscaler quality purely from pixel count: at 1080p, `num >= 2073600 && num <= 3686400` always
lands on `MaximumQuality`, and no menu goes further. The hardware will.

### `levelOfDetail` has no useful middle

Swept on top of `traffic`, so geometry is the only thing changing and the city keeps its stock
textures, shadows and shaders:

| `levelOfDetail` | avg fps | 1% low |
|---|---|---|
| 0.5 (stock) | 24.1 | 10.8 |
| 0.4 | 25.6 | 8.0 |
| 0.3 | 28.1 | 9.7 |
| 0.2 | 27.2 | 6.1 |
| 0.15 | 33.1 | 14.2 |

The average creeps up and the 1% low gets *worse* until the value goes well below 0.2, at which
point both improve sharply. Between 0.2 and 0.4 there is nothing worth having: a few frames of
average, bought with a worse floor. Pulling LOD transitions closer appears to cost more in
streaming churn than it saves in triangles, right up until the transitions are close enough that
most of the city is drawing its cheap mesh.

**The levers are not independent, and the order matters.** The same knob measured on top of
`potato` — where the shader tier, post-processing and shadow resolution have already been cut —
was worth +87% average and +78% on the 1% low. On top of `traffic` it is worth +46% and +35%.
Geometry only pays once the fixed per-frame costs are out of the way.

The practical consequence for anyone tuning by hand: setting `levelOfDetail` to "something in the
middle" as a compromise is the one choice that gets neither the frames nor the picture.

### One more setting the presets never reach

`TerrainQualitySettings.targetPatchSize` decides how large a terrain patch is before it becomes
its own draw. The game's presets run 12 (High) to 24 (Low); the slider goes to 64.

At 64 it measured 51.2 average across two runs on top of `super-potato`, against 48.0 across three
runs without it — about +7%, well outside the ±2% the average moves on its own. On the `sharp` tier
it is worth +3%: the same change matters less when there is more else left to draw.

### The mesh budget heuristic is right, tested from both sides

`meshMemoryBudget` is the one setting this tool raises rather than lowers, sized from VRAM. On 8 GB
it picks 2048. Both directions from there are worse:

| budget | avg fps | 1% low |
|---|---|---|
| 1536 | 52.1 | 11.8 |
| 2048 (chosen) | 52.2 | 16.6 |
| 4096 | 43.7 | 9.7 |

Too small and the game evicts meshes it is about to need again; too large and it competes with the
virtual texture atlas and the upscaler's buffers for the same 8 GB. The 1% low is what notices,
falling by around a third in both directions while the average barely moves at 1536. On a PCIe
3.0 x8 link, every one of those re-uploads is expensive.

### Two things that did not work

**More aggressive is not monotonically better.** Adding `mipbias=3` and a 4096 MB mesh budget on
top of `super-potato` produced 43.7 average against 48.6, and dropped the 1% low to 9.7 — below the
untouched baseline.

**`maxFrameLatency` does nothing here.** Raising it to 3 to let the CPU run further ahead of a
bursty GPU measured 48.7 average and a 15.3 1% low, both inside the noise. The bursts are not a
queueing problem.

**Upscaling alone cannot save a sharp city.** `traffic` plus DLSS UltraPerformance reached 26.1,
while `potato` plus the same DLSS setting reached 42.1. The machine is limited by geometry before
it is limited by pixels, so a profile that keeps every LOD at stock gets little from an upscaler.
Anything that wants both the frames and a sharp city has to cut geometry *selectively* — which
settings cannot express, and which is exactly what the code mod is for.

### The 1% low, and where it stops moving

No settings profile moved the 1% low until `super-potato`. It sat at 10.3-10.8 in `baseline`,
`free`, `traffic` and `potato` alike — the number that decides whether the game feels smooth, flat
across a 62% swing in the average.

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

> This last paragraph held only as far as `potato` went. Once the GPU frame drops under about
> 20 ms the tail changes hands entirely — see *The lows change hands as the GPU gets cheaper*
> below, which was measured later with `cs2patch bench --spikes` and supersedes it.

**The CPU is not the wall.** Render-thread time is 6-9 ms and never exceeds 30 ms in either run.
`potato` *reduces* CPU-game time as well, because fewer visible objects means less to prepare.

**The 0.1% low is not a real regression.** It appears to collapse by half under `potato`, but 0.1%
of 3165 frames is four frames, and both runs contain exactly three frames over 100 ms. `potato`'s
single 642 ms frame is almost certainly the shader-variant compile triggered by
`Shader.EnableKeyword("COLOSSAL_QUALITY_TIER_LOW")` — a one-time cost that lands inside the
measurement window. Quote the 1% low, which is 32 frames; treat the 0.1% column as an outlier
detector rather than a result.

## The game reorders `Settings.coc` sections on its own

A file the patcher had reverted came back byte-different from the copy taken before any of this
started — same 3741 bytes, same values, but with `Gameplay Settings`, `Keybinding Settings` and
`Interface Settings` in a different order.

The revert is not at fault; it restores its backup exactly, and that is hash-verified. The game
rewrites the file itself and does not preserve section order between writes.

This matters for `HasDriftedSincePatch()`, which compares hashes: after the game has written the
file once, it will report drift even when every value is identical to what the patcher wrote. The
warning is currently more alarming than the situation. Comparing parsed sections rather than raw
bytes would fix it.

## What it takes to make a local mod load

Copying `Cs2Saver.dll` into `.cache/Mods/local/Cs2Saver/` does nothing. The game starts and logs

```
======= Active Playset =======
	(none)
======= Enabled Mods =======
	(none)
Mods registered in 8.99ms
```

having never looked at the folder. Two separate things have to be true, and each fails silently.

**A Paradox session.** `ModManager.RegisterMods` discovers mods through
`ExecutableAsset.GetModAssets()`, which queries `AssetDatabase.global` rather than the mod folder.
The mod roots only reach that database through `PdxSdkPlatform`, which needs a session — and the
session arrives as exactly two command-line arguments the launcher passes:

```
Cities2.exe --pdx-launcher-session-token <token> --paradox-account-userid <id>
```

That is the whole thing; 139 characters. There is no IPC handle and no hub session despite the
game masking those names in its own logs. Captured once, the pair can be replayed, which is what
makes an unattended mod benchmark possible at all. Having the launcher merely *running* does not
help — the game is handed the session, it does not go looking for one. Tested, not assumed.

**An active playset containing the mod.** This is the part that is easy to miss, because a session
alone is not enough: with a valid session but no active playset the log still reads `(none)` and
nothing loads, including mods bought from Paradox Mods. The mod must be enabled inside a playset
that is currently active, and the game requires a restart afterwards — it says `Restart required`
in `Modding.log` and then carries on running the old set.

A local mod shows **0 KB** in the mod list. That is normal; there is no download size to report.

Once both hold, the log says what it should:

```
======= Active Playset =======
	dd (11759876)
======= Enabled Mods =======
	 - Cs2Saver v (Cs2Saver)
Loaded Cs2Saver, Version=1.0.0.0 in 32.2696ms
```

A worthwhile side effect of all this: **every settings measurement in this document was taken with
zero mods loaded**, which is a cleaner comparison than was intended.

## The mod's own frame log is quantised by vSync

`FrameLogSystem` writes plausible-looking percentiles that are not as precise as they appear. On a
240 Hz display with vSync on, every recorded frame time lands on a multiple of the refresh
interval — 12.50, 25.00, 33.34, 50.01, 100.02 ms are all multiples of 4.167 — so p50 and p95
collapse onto a handful of values and small differences vanish.

Turn vSync off for any run whose numbers are meant to be compared. The benchmark's own timings do
not have this problem: it records CPU and GPU cost per frame rather than presentation intervals.

## Geometry is sixty percent of a sharp city

`sharp` turns off every screen-space effect the pipeline has — ambient occlusion, reflections,
global illumination, volumetrics, depth of field, motion blur, volumetric clouds — and still costs
32.5 ms of GPU. Two measurements say why, and together they say what to do about it.

Forcing the upscaler from its least aggressive setting to its most aggressive one, which is the
difference between rendering at 1114x627 and at 640x360, was worth **ten percent**. Quartering the
pixel count buys almost nothing, which is what a triangle-bound frame looks like from outside.

Taking `levelOfDetail` to its floor on the same profile was worth this:

| | avg | 1% low | GPU |
|---|---|---|---|
| `sharp` | 30.3 | 10.6 | 32.5 ms |
| `sharp` + `levelOfDetail=0.1` | 47.8 | 20.4 | 20.4 ms |

**Twelve of the thirty-two milliseconds are geometry.** That is the budget this project spends
from here on, and the whole problem is that the slider which releases it scales every LOD
transition at once — buildings included, which is exactly what makes a city look like clay.

## The clutter, not the crowds

Prefabs carry components saying what they are, so the geometry can be taken selectively. Counting
what the game actually has, logged by the mod on load:

```
    25 creature prefabs        99 vehicle prefabs
    20 tree prefabs            52 plant prefabs
14,291 prop prefabs            12 net-object prefabs
```

Fourteen thousand prop prefabs — signs, lamps, bins, fences, benches — against twenty kinds of
tree. The cost was never the crowds, which is why the mod's original premise of culling
pedestrians and vehicles measured as nothing at all.

Cutting everything except buildings:

| | avg | 1% low | GPU | buildings |
|---|---|---|---|---|
| `sharp` | 30.3 | 10.6 | 32.5 ms | untouched |
| `sharp` + mod at Declutter | 40.3 | 18.1 | 24.5 ms | **untouched** |
| `sharp` + `levelOfDetail=0.1` | 47.8 | 20.4 | 20.4 ms | cut |

**Eight of the twelve available milliseconds, without touching a building.** No setting in the
game can express that distinction, and that is the entire justification for shipping a code mod
alongside a settings patcher.

Isolated on the finished `skyline` configuration, where the settings have already given everything
they have, the mod is still worth this:

| | avg | 1% low | GPU | at play speed |
|---|---|---|---|---|
| `skyline`, settings only | 42.7 | 20.3 | 22.5 ms | 47.0 |
| `skyline` + mod at Declutter | 51.5 | 22.6 | 18.6 ms | **59.0** |

## The lows change hands as the GPU gets cheaper

`cs2patch bench --spikes` takes the slowest 1% of frames apart. Run across the ladder it shows the
bottleneck moving:

| | slow-frame GPU | slow-frame CPU-game | CPU was the wall |
|---|---|---|---|
| stock settings | 2.2x typical | 2.1x typical | 3 of 21 |
| super-potato | 2.4x typical | 5.3x typical | 35 of 45 |

At stock the lows are draw cost, and settings reach them. Once the GPU frame is down near 16 ms
they are CPU-game spikes, and no graphics setting touches those.

The per-phase medians say what those spikes are, and it is not a hitch:

| phase | median CPU-game, super-potato |
|---|---|
| paused | 6.5 ms |
| speed 1 | 9.7 ms |
| speed 3 | 19.1 ms |

Paused is zero simulation, and each step of speed adds three to four milliseconds. **It is the
simulation, scaling linearly, exactly as it should.** The apparent "14% of frames are spikes" at
super-potato was an artefact of comparing three phases against one median.

The consequence is a hard ceiling that no amount of graphics work can move: at speed 3 the CPU
spends 19.1 ms per frame before a single pixel is drawn, which caps that phase at about 52 fps on
this CPU. Cutting it would mean cutting the simulation, which this project does not do.

## Sixty is reachable at play speed, and not at 3x

Splitting `skyline` by phase, which is the only honest way to read a benchmark that spends 35 of
its 90 seconds at triple simulation speed:

| phase | avg | 1% low |
|---|---|---|
| paused | 60.4 | 36.1 |
| speed 1 | **59.0** | 33.5 |
| speed 3 | 38.6 | 16.3 |

Normal play speed is at sixty. Fast-forward is capped by the simulation, per the section above.
Quoting a single whole-run average for this benchmark understates ordinary play by about fifteen
percent and always will.

## Levers that measured as nothing

Recorded because a negative result costs the same to obtain as a positive one and is worth as much
the second time somebody wonders:

| lever | on | measured |
|---|---|---|
| upscaler Balanced to MaximumPerformance | `sharp` (triangle-bound) | +3.7% |
| upscaler Balanced to MaximumPerformance | `skyline` (geometry cut) | +4.9% |
| `targetPatchSize` 24 to 64 | `sharp` + mod | +1.7% |
| `lodCrossFade` off | `sharp` + mod, no shadows | +1.3% |
| `meshMemoryBudget` 1024 to 4096 | `skyline` | +0.2% |
| mod culling pedestrians and vehicles | stock settings | +0.9% |

Everything on that list is inside the ±2% error bar on the average. The upscaler rows are the
interesting pair: the same change is worth more once the frame is no longer waiting on triangles,
which is the general shape of this whole exercise — **the order the levers are pulled in changes
what they are worth.**

`meshMemoryBudget` is the one worth a footnote. It did nothing to the average but took the paused
0.1% low from 25.8 to 40.0, which is consistent with fewer streaming stalls. It is not in any
profile, because 0.1% of a run is four frames and this project's own rule is to treat that column
as an outlier detector rather than a result.

## The honest caveat

Everything here is measured on one machine — RTX 3050, Ryzen 5 4600G, 1080p, build 1.6.0f1 — with
one run per configuration unless stated otherwise. Repeats put the average at ±2% and the 1% low at
±10%, so the ordering is trustworthy and the exact percentages are not.

Nothing has been measured on any other hardware. The Celeron target in particular is a design
intent, not a result: `super-potato` exists for it and has never run on one.

The 60 fps goal is met at normal play speed and while paused, and is not met at 3x simulation
speed, where the ceiling is the simulation itself. The 1% low is 33.5 at play speed against a
target of 60, and closing that gap is a simulation problem rather than a rendering one.

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

## The honest caveat

Every frame-time figure quoted here comes from published analysis of the launch build. Nothing in
this repository has measured a frame yet. The profile tiers are reasoned from the game's own source
and from what each pass costs in principle — they are informed hypotheses, not results. Iceflake now
ships a benchmark tool; wiring the patcher up to it for before/after comparison is the next task, and
until that exists, every performance claim here should be read with that in mind.

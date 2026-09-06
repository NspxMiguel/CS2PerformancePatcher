# CS2 Performance Patcher

Point it at your Cities: Skylines II install, pick a profile, click patch. It reaches settings the
in-game menu does not expose, backs up everything it touches, and undoes itself in one command.

```
cs2patch status              # what you have, and whether it is patched
cs2patch list                # profiles, and what each one costs you visually
cs2patch apply traffic       # apply
cs2patch revert              # restore the original, byte for byte
```

## Why this exists

Cities: Skylines II renders a great deal of geometry that never becomes a pixel. A frame analysed at
launch submitted **121 million vertices** and **6,705 draw calls**, of which **4,828 — 72% — were the
shadow pass alone**, costing ~40 ms. Citizen models carry ~56,000 vertices with no LOD variants at all;
in one profiled frame, thirteen of them were fully rendered and affected *zero pixels*.

Iceflake Studios took the game over in January 2026 and has been fixing this properly — better LODs,
cheaper shadows, and, importantly, **shadow-caster culling thresholds** that did not exist at launch.
This tool does not compete with that work. It does the thing a developer cannot ship as a default:
it pushes the game's own knobs past the conservative values the presets use, because you are allowed
to decide that a shadow cast by a mailbox is not worth a draw call.

## How it works

The game stores its graphics configuration in plain text at

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Settings.coc
```

Several of the most expensive settings have **no UI control at all**. They are reachable only by
picking a whole quality preset, and the presets stop well short of what the underlying values allow.
`shadowCullingThresholdVolume`, for example, has no slider; the Low preset sets it to `2.0`, and
nothing in the game will set it higher. This tool writes `6.0`.

That is a supported state, not a hack: the game's own `QualitySetting.Level` enum includes `Custom`,
and `GetLevel()` returns it whenever your values do not match a preset.

**No DLL is installed. No game file is modified. No code is injected.** That means the patch survives
Steam's file verification, does not break when the game updates, and cannot corrupt a save. It also
means it works on any hardware, not just the machine it was built on.

### Ranges are taken from the game, not guessed

Every bound the patcher enforces was read out of the game's own `[SettingsUISlider]` attributes in
`Game.dll`. A value outside the range the game declares is rejected before it reaches disk.

## Profiles

| Profile        | What it costs you | fps | 1% low | at 60 |
|----------------|-------------------|-----|--------|-------|
| *(untouched)*  | The game as it ships, mod off. | 26.1 | 14.0 | 0% |
| `free`         | Nothing you can see. Removes work that produces no visible pixels. | 41.5 | 26.3 | 0% |
| `traffic`      | Effects you do not look at. City and traffic stay sharp. | 42.4 | 26.7 | 0% |
| `sharp`        | Every screen-space effect. Geometry and textures completely untouched. | 50.2 | 28.4 | 7% |
| `potato`       | Visible cuts, including blurry textures. For machines with no upscaler. | 56.7 | 31.2 | 48% |
| `skyline`      | Sharper buildings, at the cost of internal resolution. | 56.7 | 29.7 | 46% |
| `handsome`     | **The only one with the sun's shadow on. Start here.** | **60.7** | **31.0** | **66%** |
| `super-potato` | Everything, including the internal resolution. Jagged, and fast. | 70.4 | 33.8 | 89% |
| `mega-potato`  | Below that. Two thirds of the resolution. | 71.2 | 29.9 | 89% |
| `bone-dry`     | Half the resolution. Nothing below this exists. | 72.1 | 36.7 | 89% |

Figures are the speed-1 phase — normal play — with the mod installed at Declutter, its greenery on
Full, the Showroom look and Matte surfaces, on the machine in
[docs/FINDINGS.md](docs/FINDINGS.md). The whole table is one session of back-to-back runs, because
comparing runs taken hours apart compares the machine's mood as much as the settings.

**`handsome` is the one to use, and it is the only tier here with the sun's shadow on.** Every
other profile buys its frames partly by switching that shadow off, and matched photographs of the
same benchmark frame are what showed why that is a bad trade: a city with no shadow and no ambient
occlusion is not a stylised city, it is a flat one — grass with no texture, buildings with no
volume, trees pasted onto the ground. Turning both back on cost 2.1 frames per second. Finding
those frames again cost one shopfront's window frames and an upscaler step that a 1:1 crop cannot
resolve. That is the whole design of the tier.

It needs the mod for it. Turning the sun's shadow on is a game setting, and bounding how far it
reaches is an HDRP volume parameter the game never exposes — bounded to a block it costs 0.9 fps,
unbounded it costs 6.0. So `cs2patch apply handsome` writes the mod's reach setting too, and tells
you if the mod is not installed.

Coming at it from the other end does not work. `sharp` with the mod running measures 50 and there
is nothing left in it to cut that is not the city itself, which is why this tier is built by
starting from the fastest one and spending its surplus on the image instead.

**One number worth reading twice.** With no profile applied at all — the game exactly as it ships —
installing the mod and setting nothing but the shadow reach to Block takes it from **26.1 fps to
32.2, and its 1% low from 14.0 to 23.4.** That is a 23% average and a 67% improvement in the frames
you actually feel, for one setting, on an unpatched game, and the only thing that changes on screen
is that shadows stop being drawn more than a hundred metres from the camera. It is the largest
single-knob result in this project and it is not a settings change, because no settings file can
reach it.

Each change is tagged `Free`, `Cheap`, or `Visible`, and `cs2patch apply` prints every one with its
before and after value. Nothing happens that you cannot see happening.

The mesh streaming budget is the one setting sized to *your* machine rather than the profile, and it
is the one thing the patcher turns **up**: a budget below your available VRAM makes the game evict
and re-upload meshes it is about to need again. VRAM is read from the game's own `Player.log`, which
reports the true figure — Windows' `Win32_VideoController.AdapterRAM` is a 32-bit field that
saturates at 4096 MB and will tell you an 8 GB card has 4 GB.

## The code mod

Settings can only move global dials. The game's detail slider scales every LOD transition at once,
buildings included — which is exactly what makes a city look like modelling clay. `Cs2Saver` exists
to make that distinction, because prefabs carry components saying what they are and **a setting
cannot say "cut the street furniture but not the buildings."**

```
cs2patch install-mod      # copy it into the game's local mods folder
cs2patch uninstall-mod
```

Then enable it in the game's mod list and open its options page. It ships **off**; nothing
changes until you pick a preset.

**How it works.** Every visibility test in the game reduces to
`CalculateMaxLod(bounds, camera) >= m_MinLod`. The LOD value falls with distance, so raising the
floor culls sooner — and because `CalculateDistanceFactor(lod) = pow(2, (128 - lod) / 6)`,
**every +6 halves render distance**. Every setting in the mod is therefore expressed as a count of
halvings, per category, applied to the prefab so the game hands the value back rather than
overwriting it.

`BuildingData` is excluded from every query. That is the promise, and it is enforced by category
rather than by keeping the numbers small.

Each prefab's untouched value is remembered, so every pass is computed from the original rather
than from the last result. That keeps the work idempotent, keeps the cut *relative* to each
object's own size — a large tree still outlives a shrub — and means turning the mod off actually
puts the city back rather than leaving it cut until you restart.

**What it is worth.** On the finished `skyline` configuration, where the settings have already
given everything they have:

| | avg | 1% low | at normal play speed |
|---|---|---|---|
| settings only | 42.7 | 20.3 | 47.0 |
| with the mod at Declutter | **51.5** | **22.6** | **59.0** |

The category counts explain where that comes from: the city has 14,291 prop prefabs against 20
kinds of tree, 99 vehicles and 25 creatures. The cost was never the crowds — an earlier version of
this mod culled only pedestrians and vehicles and measured as nothing at all.

**It also records frame timings.** Turn on "Record frame timings" and it writes a CSV every ten
seconds: average FPS plus the 1% and 0.1% lows. The lows are the point — an average can improve
while the game feels worse, and only the percentiles show that. Use it to check whether a preset
actually helped on *your* machine rather than trusting anybody's numbers, including this
repository's.

**It also gives the city a look.** Every tier here buys frames by removing something, and what is
left is correct but flat — a photoreal renderer with most of its photorealism switched off. Grading
is the one thing that can be spent on looks without spending frames, so the mod drives HDRP's own
grading stack from a global volume: `Vivid` puts colour back, `Toybox` is the cartoon one,
`Miniature` is model-railway light, and `Cel` steps the luminance response so light falls in flat
bands instead of a smooth gradient, which is the part that actually reads as drawn.

No shaders are involved. `ColorCurves` takes a curve built from keyframes at runtime, and keyframes
with infinite tangents are how an `AnimationCurve` expresses constant interpolation — so a
staircase there is real posterisation. The ink outline of proper cel shading is the one thing out
of reach, because it has to sample depth and normals.

It costs nothing, and that is measured rather than assumed: `skyline` with `Cel` ran 53.7 average
against 53.2 across three runs of the same profile with no look, and the GPU frame stayed at
17.9 ms.

**It also bounds the sun's shadow.** HDRP keeps the shadow distance in a volume component the game
creates but never exposes, so no settings file reaches it — which is exactly the kind of thing this
mod is for. Measured at normal play speed on `handsome`, the same shadows with the same cascade
count and the same resolution cost **6.0 fps at the distance the game picks and 0.9 at a hundred
metres.** Beyond that distance nobody was looking at a shadow anyway.

This is why `handsome` can afford to have shadows at all, and why applying it also writes the mod's
reach setting when the mod is installed. Turning shadows *on* is the game's own setting and only a
profile reaches it; bounding them is only the mod's. Neither half is much use alone.

**One honest caveat.** It is built with plain `dotnet build` rather than the official Mod Post
Processor, so there is no Burst-compiled native companion and its work runs as managed code. That
mattered once: an earlier version swept every pedestrian and vehicle every frame and cost 5 ms of
CPU to save under 1 ms of GPU. The current design does its work only when something changes, over
a few thousand prefabs rather than millions of instances, so the managed cost no longer shows up
in a measurement — but it is still a real difference from what the official toolchain produces.

## Safety

- The original `Settings.coc` is copied to `CS2PerformancePatcher_Backup\` before anything is written.
- Re-applying always starts from that pristine copy, so profiles never compound.
- Writes go through a temp file and an atomic move, so an interrupted write cannot truncate anything.
- `cs2patch status` warns you if the game updated, or if settings drifted, since the patch.
- `cs2patch revert` restores the original byte for byte. This is verified by hash, not assumed.

The patcher refuses to run while the game is open, because Cities: Skylines II rewrites
`Settings.coc` on exit and would overwrite the patch.

## Building

```
dotnet build src/Cs2Patcher.Cli/Cs2Patcher.Cli.csproj -c Release
```

Requires the .NET 8 SDK. Targets `net8.0-windows`.

## Measuring

The game ships a benchmark — a fixed camera path over a bundled city, 90 seconds, with per-frame
CPU and GPU timings written to disk. This tool drives it, so a profile can be judged by numbers
instead of by feel.

```
scripts\Run-Benchmark.ps1 -Label baseline
scripts\Run-Benchmark.ps1 -Label traffic -TuningProfile traffic
cs2patch bench --phases
```

`--set` pushes a single setting past whatever the profile does, which is how a knob gets measured
on its own before anyone decides it belongs in a profile:

```
scripts\Run-Benchmark.ps1 -Label dlss-test -TuningProfile potato -Set GraphicsSettings.dlssQuality=UltraPerformance
```

Overrides go through the same range check, backup and manifest as everything else, and `revert`
still undoes them.

Reported numbers are the average, the 1% low and the 0.1% low, computed from the raw frame times.
The 1% low is the one to read: it is the mean of the slowest 1% of frames, it is what makes a game
feel rough, and it moves independently of the average.

## Status

Working, measured, and the mod runs. Game version `1.6.0f1 (419.d6c6)`, Unity 2022.3.71f1. The
full table is in [docs/FINDINGS.md](docs/FINDINGS.md).

On an RTX 3050 with a Ryzen 5 4600G at 1080p, over the city the game ships for its own benchmark:

| | avg | 1% low | GPU |
|---|---|---|---|
| untouched | 22.7 | 10.5 | 43.8 ms |
| `skyline` + mod | 53.2 | 22.8 | 18.0 ms |
| `super-potato` + mod | **68.2** | **26.3** | 13.6 ms |

Split by what the benchmark is doing, because it spends 35 of its 90 seconds at triple simulation
speed and a single average hides that. The percentage is the share of frames that actually hit 60,
which is the only number that answers "sixty *stable*":

| | paused | normal speed | 3x speed |
|---|---|---|---|
| untouched | 25.5 | 24.9 | 18.8 |
| `skyline` + mod | 63.9 | **60.2** — 61% of frames | 39.9 |
| `super-potato` + mod | 89.5 — 100% | **75.8** — 93% of frames | 48.0 |

Three runs each for `skyline`, two for `super-potato`; they agreed to within 1% on the average.
Neither tier drops a single frame below 30 at normal play speed.

**Sixty at normal play speed with the city still sharp**, and seventy-five if you will accept the
bottom tier's looks. Fast-forward reaches neither, and cannot: at 3x the simulation alone needs
19.1 ms of CPU per frame before anything is drawn.

**120 is not reachable on this machine, on this city.** That has now been checked from both ends.
The CPU alone spends 11.2 ms per frame at play speed against the 8.33 ms a 120 fps frame allows,
so no graphics setting can get there — and separately, the GPU is already at its floor, because
`super-potato` renders internally at 640x360 and the remaining 13.5 ms cannot be halved by
dropping the output resolution as well.

The one thing that would move it is a smaller city. Of that 11.2 ms, 5.8 is per-frame engine work
that scales with how much there is to draw and 2.7 is simulation that scales with how much there
is to simulate; the benchmark city has 105,188 people. That is a reasoned projection rather than a
measurement, because the harness can only drive the benchmark's own city — the mod's frame log is
the way to check it on yours. See [docs/FINDINGS.md](docs/FINDINGS.md).

Not yet done, in rough order of value:

- **The 1% low.** 33.5 at play speed against a target of 60. The slowest frames stopped being draw
  cost once the GPU frame dropped under 20 ms; they are simulation now, so no graphics setting
  reaches them.
- **Repeat runs.** Most figures are a single run per configuration. Repeats put the average at ±2%
  and the 1% low at ±10%, which is enough to trust the ordering and not the exact percentages.
- **Other hardware.** Everything here is one machine. `super-potato` exists for the Celeron target
  and has never run on one.
- A licence.

## Licence

GPL-3.0. See [LICENSE](LICENSE).

The intent behind that choice is worth stating, because it is a common misunderstanding: **no
open-source licence forbids charging money, and this one does not either.** What it does is
require that anyone who distributes this — modified or not — ships the source under the same
terms. So nobody can take this work, close it, and sell it as their own product, which is the
outcome the choice was made to prevent.

Nothing from Cities: Skylines II is in this repository. The patcher transforms the files already
on your machine and ships none of Paradox's. The decompiled code used to work out what the game
does lives under `.research/`, which is not tracked, and never will be.

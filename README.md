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

| Profile   | Changes | What it costs you |
|-----------|---------|-------------------|
| `free`    | 5       | Nothing you can see. Removes work that produces no visible pixels. |
| `traffic` | 19      | Effects you do not look at. The city and traffic stay sharp. **Start here.** |
| `potato`  | 32      | Visible cuts, deliberately. For hardware well under the requirements. |

Each change is tagged `Free`, `Cheap`, or `Visible`, and `cs2patch apply` prints every one with its
before and after value. Nothing happens that you cannot see happening.

The mesh streaming budget is the one setting sized to *your* machine rather than the profile, and it
is the one thing the patcher turns **up**: a budget below your available VRAM makes the game evict
and re-upload meshes it is about to need again. VRAM is read from the game's own `Player.log`, which
reports the true figure — Windows' `Win32_VideoController.AdapterRAM` is a 32-bit field that
saturates at 4096 MB and will tell you an 8 GB card has 4 GB.

## The code mod (experimental)

Settings can only move global dials. "Keep the city sharp but stop drawing pedestrians at
distance" needs per-entity control, which is what `Cs2Saver` does.

```
cs2patch install-mod      # copy it into the game's local mods folder
cs2patch uninstall-mod
```

Then enable it in the game's mod list and open its options page. It ships **off**; nothing
changes until you pick a preset.

**How it works.** Every visibility test in the game reduces to
`CalculateMaxLod(bounds, camera) >= CullingInfo.m_MinLod`, where `m_MinLod` is a per-entity byte.
The LOD value falls with distance, so raising the floor culls sooner — and because
`CalculateDistanceFactor(lod) = pow(2, (128 - lod) / 6)`, **every +6 halves render distance**.
The mod raises that floor on pedestrians and vehicles only. Buildings, roads and terrain are
never touched, so the city looks identical.

The write only ever *raises* the floor, never sets an absolute value or accumulates an offset,
which makes running it every frame idempotent and lets the game rebuild culling data whenever it
likes without the mod fighting it.

**It also records frame timings.** Turn on "Record frame timings" and it writes a CSV every ten
seconds: average FPS plus the 1% and 0.1% lows. The lows are the point — an average can improve
while the game feels worse, and only the percentiles show that. Use it to check whether a preset
actually helped on *your* machine rather than trusting anybody's numbers, including this
repository's.

**Two honest caveats.** It is not runtime tested — it compiles against the real `Game.dll` of
1.6.0f1, which validates every API it touches, but no frame has been rendered with it loaded.
And because it is built with plain `dotnet build` rather than the official Mod Post Processor,
there is no Burst-compiled native companion, so its job runs as managed code. For a loop that
takes a `max` over one byte per entity that is unlikely to matter, but it is a real difference
from what the official toolchain produces.

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

Working and tested end to end on game version `1.6.0f1 (419.d6c6)`, Unity 2022.3.71f1, and now
measured — see [docs/FINDINGS.md](docs/FINDINGS.md) for the full table.

The short version, on an RTX 3050 at 1080p: `free` and `traffic` are worth about 5-6% and move the
1% low not at all. `potato` is worth 62% on the average. The two changes that matter most are not
in any profile yet — forcing DLSS past the quality level the game picks for itself (+86% average,
+33% on the 1% low) and disabling shadow cascades entirely (+25% on the 1% low).

Not yet done, in rough order of value:

- **Fold the measured wins into the profiles.** The tiers were built from reasoning about the code
  and the measurements disagree with that reasoning in places. They should be rebuilt around what
  was measured, not amended around the edges.
- **Repeat runs.** Every figure is a single run per configuration, so run-to-run variance is
  unquantified. The ordering is clear; the exact percentages are not.
- Per-entity culling of citizens and vehicles, which settings cannot reach. `Cs2Saver` implements
  it and still has not rendered a frame.
- Other hardware. Everything measured so far is one machine.

## Licence

Not yet chosen.

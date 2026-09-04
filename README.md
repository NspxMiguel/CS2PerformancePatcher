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

## Status

Working and tested end to end on game version `1.6.0f1 (419.d6c6)`, Unity 2022.3.71f1.

Not yet done, in rough order of value:

- **Measurement.** Every claim about frame time in this README comes from published analysis, not
  from a benchmark run by this tool. The profiles are reasoned from the game's own code, but the
  numbers behind them are not yet measured per-profile. This is the next thing to fix, and until it
  is, treat the profile tiers as informed hypotheses.
- A GUI, so this is not a terminal-only tool.
- Per-entity culling of citizens and oversized props, which settings cannot reach and which needs a
  real code mod.

## Licence

Not yet chosen.

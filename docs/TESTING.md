# Measuring

Every number in this repository comes from the benchmark the game ships with, driven by
`scripts/Run-Benchmark.ps1`. This is how to reproduce them, and how to measure a change of your
own without fooling yourself.

## Why the game's benchmark and not a play session

It flies a fixed camera path over a city bundled with the game — Lornoston, 105,188 people — for
the same 90 seconds every run: 20s paused, 35s at speed 1, 35s at speed 3. Two runs therefore
differ only by what you changed. It records the CPU-game, CPU-render and GPU cost of every
individual frame, which is what makes real 1% lows computable rather than estimated.

A hand-played session cannot do this. The camera is never in the same place twice, so a comparison
between two of them measures the route as much as the settings.

## One run

```
scripts\Run-Benchmark.ps1 -Label baseline
scripts\Run-Benchmark.ps1 -Label traffic -TuningProfile traffic
cs2patch bench --phases
```

Each run takes about three minutes: roughly a minute to load, ninety seconds to measure. The
result is filed as `.research/bench/Benchmark.<label>.coc` and `cs2patch bench` reads them all
back into one table.

To measure a single setting rather than a whole profile:

```
scripts\Run-Benchmark.ps1 -Label lod-min -TuningProfile potato -Set LevelOfDetailQualitySettings.levelOfDetail=0.1
```

Overrides go through the same range check, backup and manifest as everything else, and
`cs2patch revert` still undoes them.

## How much of a difference is real

Three runs of the same profile, changing nothing between them, moved the average by 1.3% and the
1% low by 22%. So:

- **The average is good to about ±2%.**
- **The 1% low is good to about ±10%.**

A change worth 5% on the 1% low is indistinguishable from doing nothing. Repeat the run before
believing it. This is not a formality — one host tweak in this repository measured +3% average and
-11% low, and repeating it showed both were noise.

## Three things that silently invalidate a run

**The window must be in the foreground.** Backgrounded, Windows throttles rendering and the game
does not stream textures. The run completes and writes a result that measures nothing. The script
forces the window forward and warns if it never managed to; treat that warning as a failed run.

**Nothing heavy may run alongside it.** A WMI query and an `nvidia-smi` call during one run
produced 18.1 fps against 24.1 for the same settings — a result that looked like a regression and
was an artefact. Do not build, probe or compile while a run is in flight.

**Turn vSync off if you are reading the mod's CSV.** On a 240 Hz display every frame time lands on
a multiple of 4.167 ms, which flattens its percentiles onto a handful of values. The benchmark's
own timings are unaffected; it measures work, not presentation.

## Measuring the mod

The mod needs two things that are easy to miss, and both fail silently:

1. **A Paradox session.** The game only registers mods when launched with
   `--pdx-launcher-session-token` and `--paradox-account-userid`, which the Paradox launcher
   supplies. Run `scripts\Capture-LauncherArgs.ps1`, press Play once, and the pair is recorded;
   `Run-Benchmark.ps1` replays it automatically from then on.
2. **An active playset containing the mod.** Enable `Cs2Saver` inside a playset and make that
   playset active, then restart the game. It shows as 0 KB in the list, which is normal for a
   local mod. Put it in a playset *by itself* — anything else loaded alongside it lands in the
   measurement too.

Then the A/B is one flag:

```
scripts\Run-Benchmark.ps1 -Label mod-off  -Revert -ModPreset Off
scripts\Run-Benchmark.ps1 -Label mod-aggr -Revert -ModPreset Aggressive
```

Confirm it actually loaded before trusting anything, in
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Modding.log`:

```
======= Enabled Mods =======
	 - Cs2Saver v (Cs2Saver)
Loaded Cs2Saver, Version=1.0.0.0 ...
```

## What to look at in the result

`avg fps` is the number people quote. **`1% low` is the number people feel** — it is the mean of
the slowest 1% of frames, and it moves independently of the average. In this project no settings
profile moved it at all until the most aggressive one.

`gpu ms` against `render ms` says which side is the wall. At baseline this machine is 97%
GPU-bound with a 9 ms render thread, so cutting draw work is the lever and cutting CPU work is
not.

Treat `0.1% low` as an outlier detector rather than a result. On a 3000-frame run it is four
frames, and a single shader compile lands in it.

## Seeing what a profile costs

Frame times cannot tell you the city turned to putty. `scripts\Capture-Shots.ps1` photographs the
run at fixed points; because the camera path is identical, shot N of one profile frames roughly
the same view as shot N of another.

The game must be in a window for this — exclusive fullscreen captures as black:

```
scripts\Run-Benchmark.ps1 -Label visual -TuningProfile potato -Set GraphicsSettings.displayMode=FullscreenWindow
```

Borderless costs about 2% against exclusive fullscreen, so those runs are for looking at, not for
quoting.

## Undoing everything

```
cs2patch revert            # settings, byte for byte
cs2patch uninstall-mod     # the mod
cs2patch tune-pc --revert  # anything changed outside the game
```

Note that the game rewrites `Settings.coc` itself and does not preserve section order, so a
reverted file can differ byte-wise from a copy taken earlier while holding identical values.

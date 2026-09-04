# First run: what to do and what would prove it works

Nothing in the mod has rendered a frame yet. This is the shortest path to finding out whether it
works, and it is written to fail loudly rather than ambiguously — a null result should mean
"broken", not "maybe it did something".

Budget about ten minutes.

## Before you start

```
cs2patch status          # confirms the game is found and reports your hardware
cs2patch install-mod     # copies Cs2Saver.dll into the local mods folder
```

Close the game first if it is open. It rewrites its settings on exit.

## Step 1 — does it even load

Start the game and open **Options → CS2 Saver**.

- **The page is there** → the mod loaded and registered its settings. Good.
- **The page is missing** → it did not load. Stop here and send
  `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Modding.log`
  and `Player.log`. Everything below is pointless until this works.

## Step 2 — baseline

1. In the CS2 Saver options, leave the preset on **Off**.
2. Turn on **Record frame timings**, set the label to `off`.
3. Load your largest city.
4. Park the camera at a normal working zoom over a busy area and **do not move it**. Camera
   motion changes what is drawn, and a comparison between two different views measures nothing.
5. Play for two minutes at normal speed.

## Step 3 — the actual test

Without moving the camera:

1. Open options, set the preset to **Traffic focus**, change the label to `traffic`.
2. Play another two minutes.

Then, still without moving:

3. Set the preset to **Aggressive**, label `aggressive`.
4. Two more minutes.

## Step 4 — send the file

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Cs2Saver\frames-*.csv
```

## What the result will mean

The CSV has one row per ten seconds, tagged with the label:

```
timestamp,label,frames,seconds,avg_fps,p50_ms,p95_ms,p99_ms,low_1pct_fps,low_01pct_fps
```

- **`avg_fps` rises and the 1% low rises with it** → it works, and it works on the frames that
  actually feel bad. This is the outcome we want.
- **`avg_fps` rises but `low_1pct_fps` falls** → net worse to play, whatever the average says.
  Worth knowing, and the reason the lows are recorded at all.
- **Nothing moves at all between labels** → the floors are not reaching the renderer. That is a
  real possible outcome and it points at a specific thing: whether writing
  `ObjectGeometryData.m_MinLod` after prefab initialisation actually survives.

## The thing to watch with your eyes

Look at whether **distant pedestrians vanish while buildings, roads and cars stay exactly as they
were**. That is the whole design claim. If buildings change too, the prefab query is matching more
than creature prefabs and needs narrowing.

On **Aggressive**, citizens should disappear at roughly an eighth of their normal distance, which
should be obvious rather than subtle. If it is subtle, the floor is not being applied.

## Undoing everything

```
cs2patch uninstall-mod
cs2patch revert          # only if you also applied a settings profile
```

Neither leaves anything behind.

# Sandrail — orientation

**Sandrail** (named 2026-11-17; until then the codename was Tumbleweed, which the worklog's older
entries still use). Repository: https://github.com/MatthewCarven/Sandrail. A buggy stunt racer in Godot
4.7.1 + C#: a dune buggy with a high top
speed on tracks of snap-together pieces (ramps, jumps, banked turns, loops) laid over noise terrain you
can drive on. Tracks are generated from a seed and shared as a short code; later an editor opens any
track. In the genre of the late-80s tile-track stunt racers, but our own name, pieces, car and look, not
a remake of any one game. Cheap physics, "not the Taj Mahal".

Matthew's other project, **Overburden** (`..\-=Programming=-\MinerCraft`), is where this one's habits
come from: a plan doc before each milestone, a play at the end of it, a worklog of why, and Core kept
free of Godot. Its generator's `Hash64` and `GradientNoise` are ported here unchanged.

## Read these before changing anything

| Document | What it is |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | **The plan**: his decisions, Claude's suggestions, the car, pieces, generator, ghosts, milestones M0–M6, and the decisions still his. |
| [docs/M1-PLAN.md](docs/M1-PLAN.md) | **M1, the buggy**: six facts, the car's model written down with its starting numbers, six decisions (all taken 2026-10-31), slices M1a–M1e with their checks, and the named levers. Read "The car's model" before touching `Buggy` or `CarSpec`. |
| [docs/M2-PLAN.md](docs/M2-PLAN.md) | **M2, pieces and the track file**: eight facts (corner radii on a grid, connectors, crests, the loop on the grid, checkpoints, gap jumps), the model written down (grid, piece, placement, track, validation), six decisions (his, written 2026-11-23), slices M2a–M2e. Read "The model" before touching pieces or tracks. |
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | **M3, the generator**: eight facts (the noise's reach, how fast the road can climb, how steep the car can, why the route is walked in pieces, why a stunt can only replace its own footprint, the height field, painted ground, terrain in the track file), the model (settings, a seed per stage, terrain, route, stunts, the bed), ten decisions (his, all taken: six on 2026-11-23, four on circuits on 2026-11-24), slices M3a–M3f and M3g, circuits (walked out and walked home, 3 laps of 0.8–1.5 km, half the seeds; built after M3c). **Passed 2026-11-24.** |
| [docs/M4-PLAN.md](docs/M4-PLAN.md) | **M4, ghosts, replays and track codes**: nine facts (record what happened, not inputs; 30 snapshots a second misses a curve by millimetres; a ghost belongs to one track, named by a fingerprint of what's driven; a seed is a track for one generator version), the model (the generator version, codes like `5v1`, the track fingerprint, the snapshot, the ghost file, `CarLooks`, replay cameras from the laid track), seven decisions (his, all taken as recommended 2026-11-24), slices M4a–M4e. |
| [docs/M5-PLAN.md](docs/M5-PLAN.md) | **M5, the editor**: seven facts (validating is free; a track being built is unsound, and `Track.Load` refuses that; a piece builds in 1.5 ms, the ground in about 400 ms; big pieces want laying onto the road's open end; an edited generated track needs a name of its own; one key in, one key back), the model (`TrackEditor` in Core: place, erase, open ends, next, undo; the editor's camera, cursor, palette and problems), seven decisions (his, all taken as recommended 2026-11-24; M6 folded in), slices M5a–M5e, then his tuning. |
| [docs/PROFILE-PLAN.md](docs/PROFILE-PLAN.md) | **The car profile, for his tuning (M1e)**: a car driven over a track by the autodriver, a sweep of cautions, one row a run in a CSV per track (time and slowest speed on each piece), his own runs beside them, a garage of cars, and each car's best clean run as a ghost (P4). Eight facts (a run is the same every time and at any speed; about ×10 headless; the autodriver plans from grip, top speed and downforce only, so the clean limit tells cars apart; his working `buggy.json` was a car kept nowhere else, now in his garage), seven decisions (his, all taken 2026-11-24, the sweep from 50% at his word), slices P1–P5. No breeding, at his word. |
| [WORKLOG.md](WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |

## Non-negotiables

1. **`Sandrail.Core` never references Godot.** It holds what every player must agree on (the track,
   the generator, the file formats), and that absence is what lets tests and the bench run headless.
   **The car's physics is the exception, on purpose**: it lives in Game on Godot's Jolt, because one
   rigid body and four raycasts don't justify a solver of our own (PLAN, Architecture).
2. **`Hash64` is the only source of randomness in generation.** Never `HashCode`, `string.GetHashCode`
   or an unseeded `Random`. .NET randomises those per process, so a track would change between launches
   while looking right within each one. `KnownInputsHashToKnownOutputs` pins it.
3. **Content IDs are namespaced strings, never enums, and the base game's namespace is `base:`, not the
   game's name.** Piece IDs go into every track file, and Overburden carries `minercraft:` as an alias
   forever because a name was baked into its save format. With `base:`, renaming the game is a
   search-and-replace over the code and no saved track ever notices, which is how Tumbleweed became
   Sandrail.
4. **One derived seed per generator stage** (terrain, route, stunts, scenery), so turning one dial
   can't reshape what an earlier stage made.
5. **Dials are calibrated against what the noise actually reaches, measured with the bench**, not
   against its documented range. See the 2026-10-31 worklog entry for the figures.

## Commands

```bash
dotnet test Sandrail.slnx
```

```bash
start.cmd
```

`start.cmd` builds `Sandrail.Game` first and refuses to launch a failed build, because Godot run with
`--path` loads whatever DLL is already built. Anything you pass lands after `--` as a game flag. It uses
the Godot copied into this folder (gitignored) and falls back to Overburden's.

Benches are **Release only**:

```bash
dotnet run --project Sandrail.Bench -c Release -- --noise
```

```bash
dotnet run --project Sandrail.Bench -c Release -- --terrain --seeds=100
```

`--terrain` measures the generated ground over many seeds (heights, slopes, how much the car can't
climb, how often the road could follow it), takes the dials from the command line (`--hills=`,
`--feature=`, `--region=`, `--rarity=` and so on) so they're tried without a rebuild, and with five
seeds or fewer says where each one's steepest ground is.

```bash
dotnet run --project Sandrail.Bench -c Release -- --generate --seeds=1000
```

`--generate` makes a track from each seed and lays it: **any track `Lay` refuses is a generator bug, and
must be none**. It reports time, length, pieces, attempts, backtracks, slopes, banks, how the road met
the ground, and stunts by kind with the ones the speed estimate turned down and why, and checkpoints;
`--first=` starts elsewhere, `--turniness=`, `--stunts=` (density, 0 to 1) and `--checkpoints=` (metres
apart, 0 for none) and `--circuits=` (the share of circuits, 0 to 1) try the dials, sprints and circuits
are reported apart, and five seeds or fewer print each with its code and fingerprint (M4a); it times the
fingerprint, and `--save=<folder>` writes each track's file (how `gen-5v1.json` was made).

Headless check, which catches scene and script errors (`--quit-after` counts **frames**):

```bash
Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Sandrail.Game --headless --quit-after 240
```

The pass prints `Core linked: Hash64.Mix(0) = 0xE220A8397B1DCDAF`. After a fresh clone, import once
first: the same command with `--import` in place of `--quit-after 240`.

**The oracles** drive the car headless and print one line each, ending with the spec's fingerprint
(M1-PLAN, M1a). Run them after any change to the car, and quote them in the worklog:

```bash
pwsh tools/oracles.ps1
```

`-Oracles drop` runs one; `-Spec variant.json` drives a variant file instead of the shipped buggy, which
is how to try numbers without touching what plays. There are seventeen: `straight`, `drop`, `brake` and
`circle` on the bare plain with road grip (their M1a figures are in the 2026-10-31 worklog entry;
`straight` runs off the plain's end at 26 s and counts top speed only before), `handbrake` there too (a
stop on it alone, and a full-lock turn with and without it; the M1d entry), `sweeper` there too (the
most grip at speed, on a 120 m circle with the speed creeping up: where a wing shows; the downforce
entry), `respawn` there too (the one oracle with the auto-respawn on: dropped on its roof, then past
the plain's end, which is 1000 m out, Jolt's world boundary), `climb` there too (the steepest 40 m
slope it climbs from rest on each surface; the M3a entry), `jump`, `loop` and `bumps` on the test ground (figures in the M1c entry), `air` there too
(station 1's jump five times with one input held or tapped in flight; the air-control entry),
`pieces` in the showroom (the loop, both jumps and the slope, each at several speeds; the M2b entry),
`lap`, the autodriver
lapping station 6 (figures in the 2026-11-23 autodriver entry), and `track`, the autodriver driving the
shakedown start to finish (or `--track=` or `--seed=` another): its time, splits, each jump and the
loop, where it left the road, or exactly where it stopped (the M2d entry), and `bank`, from rest at the
foot of a generated track's three tallest banks (`--seed=`, 1 unless told), full throttle at the road
(the M3d entry), and `ghost`, the `track` oracle's drive recorded as a ghost, saved, loaded and played back
against the car at 120 a second: its size and the worst miss on straights, in corners, in the loop and in
the air (the M4b entry: millimetres, and 1.75 cm for a tick on a hard landing). **`lap` is how a tune is compared**: run
it on two specs and read the flying lap, the corner speeds and where it left the road; `track` says
whether a car (or a track) can be finished at all.

**The car profile's viewer**, `SandrailProfiles.exe`, a program of its own (Python and DearPyGui, not part of
the solution): the profiles folder's sheets, a track's files read as one and All tracks at the top, sorted by up
to three columns from the boxes above the table (or a header click), the car's numbers from `cars.csv` joined on
the fingerprint, new rows picked up as the game writes them, each column fitted to its widest cell over every run with
long headers angled above (his pick), a right-click copying a run as one line separated by commas. It only reads the
sheets. `build.cmd` runs its tests and
compiles it into `tools/profile-viewer/dist/` (not committed); `python -m pytest tools/profile-viewer/tests`
tests it alone; `viewer.py <folder> --screenshot=<png>` is how Claude sees it (its README):

```bash
tools\profile-viewer\build.cmd
```

Game flags, after `--` (or passed to `start.cmd`): `--edit=new` or `--edit=<track id or path>` (the editor, M5, no car and no race; the Esc menu's New track and Edit this track use it; `--edit=new:<seed>` for an empty grid on a seed's ground; `--edit=@editor` is the editor's own track, kept across a test drive), `--drive-from=<column>,<row>` with `--track=` (a test drive from the piece on that tile, untimed unless it's a sound track's start gate, the file read without refusing it; `--track=@editor` drives the editor's track; M5c), `--seed=<n>` (a generated track on its own ground,
M3b, with stunts and checkpoints since M3c; since M4a a code too, `--seed=5v1`, and a code from another
version of the generator drives its file if the game has one, or says why not: the timer, P and `--oracle=track` all work on it; the
autodriver finishes all five test sprints and three of the five test circuits at `--caution=0.7`; since
M3g the even seeds among 1-10 are circuits of 3 laps; `--stunts=` and `--checkpoints=` set the stunt
stage's dials, and `--stunts=0 --checkpoints=0` is the route as it was before M3c),
`--terrain=<seed>` (the generated ground alone, M3a), `--ground=test|showroom|bare` (the showroom is every
M2 piece on its own with a run-up, a station each; [ and ] step through them), `--track=base:shakedown`
(a track from `content/base/tracks/` by its ID, or `--track=<path>.json`, a generated one saved from the
Esc menu included, which comes with its own ground, bed and rocks; the car starts at its start
gate, the clock starts on the throttle, R goes back to the last checkpoint (before the first, it starts
again), and holding R for a second or pressing Backspace starts again, clock and all;
`--checkpoint=N` starts where R would put the car after checkpoint N, for a screenshot), `--station=N`,
`--surface=road|dirt|grass|sand` (sand by default on the test ground), `--spec=<path>`,
`--oracle=<name>`, `--panels=tuning,telemetry` (opens either at start),
`--record` (starts a run at start), `--autodrive` (the autodriver from the start; with `--station=6`),
`--caution=` (the autodriver's aggression, the share of grip it plans for, 0.5 to 1, 0.8 by default:
for the game and the `lap` oracle alike; in the game - and + change it, and since 2026-11-25 a change stays put
from one track to the next, over the flag, until the game is closed),
`--ghost=you|auto|none|<car>` (whose ghost shows on a track, M4c; since P4 a car profiled on the track, by its name or
fingerprint, `--ghost=stock`; G changes it in the game) and
`--ghosts=<folder>` (where ghosts and bests are kept, `user://ghosts` unless told: **point checks at a
scratch folder**, so his bests are never touched), `--saved=<folder>` (where tracks are saved and read back, `user://tracks` unless told: **point checks at a scratch folder** too), `--tunes=<folder>` (the garage's cars, `user://tunes` unless told: **a scratch copy for checks** too; the Esc menu's Change car picks one, and the lock beside a car there unlocks Set inactive, which moves its file into `inactive/` inside the folder, still listed and drivable but left out of Profile every car; F2's Save as a new car adds one, and Save on the stock car asks for a name, since the game never writes it), `--profile=this|all` with `--track=` or `--seed=` (P3: the autodriver sweeps the car, or every car in the garage, from 50% aggression by 5% until a run isn't clean, writes a row a run and quits; in play the Esc menu's Profile button opens Profile this car and Profile every car, which do the same, Esc stops it), `--profile=everywhere`, needing neither (every car on every track: Profile every car on each of Load a track's tracks in turn, the unfinished ones left out and a fingerprint profiled once; Profile every car on every track, the third under the Esc menu's Profile, does the same in play and comes back to the track and car it began on, Esc stops it) and `--profiles=<folder>` (where the sheets go, `user://profiles` unless told: **a scratch folder for checks**, since every finished run of his writes a row there too), `--replay=you|auto|<car>` (a profiled car's best by name or fingerprint, P4) with `--replay-at=<seconds>` and
`--replay-camera=trackside|chase|free` (a saved best's replay from the start, M4d, for a look or a
screenshot; in the game Enter watches the last run and the Esc menu offers the bests),
`--time=<scale>` (how fast time goes, `0.25` for a quarter speed; in the game `,` and `.`, or the D-pad's left and
right, step through ×0.1 to ×16, and laps, ghosts and runs stay timed in game time because `GameClock` turns the
tick rate with it; past ×1 the board says how fast it really runs, `time ×16 · running ×15.7`, amber when it's a
tenth short), and `--screenshot=<path.png>`, which saves a frame at rest and one driving, then quits; with it,
`--drive=throttle,steer[,wide,handbrake,brake]` sets how it drives (the fifth number, the brake, reverses it from rest) and `--camera=x,y,z:x,y,z` looks at the ground from a fixed
point instead of following the car. It opens a window for a few seconds, and it is how Claude sees
anything at all. `--padcheck` is a sensor run on a gamepad instead of a drive: it waits for a touch,
records 30 s, and writes `user://padcheck.txt` (worklog 2026-11-22 has his pad's). `--bindpad` asks
for each control on the pad and saves the pad's own layout to `user://pads/<guid>.json`, which then
replaces the default pad bindings for that pad. **His pad (a generic clone) has one**: throttle and
brake on the right stick, since its L2/R2 are only buttons.

**A tuning session** (M1b): Matthew drives with F3 (telemetry) up, presses F4 to record the bit that
feels wrong, and says what felt wrong (`,` slows time down to watch it happen; the run is recorded in game time either way). Runs land in `%APPDATA%\Godot\app_userdata\Sandrail\runs\` as
`<time>-<fingerprint>.csv` with the spec beside them as `.spec.json`. Read the CSV (49 columns, units
in the headers), change numbers in a `-Spec` variant, run the oracles, and only then touch
`buggy.json`. He can also change numbers live with F2, whose Save writes the loaded spec file.

## Layout

- `Sandrail.Core/` — no Godot. `Generation/` holds `Hash64`, `GradientNoise`, the `BumpField`, and
  M3's generator as it's built: `GenStage` (a seed per stage), `Terrain` with its `TerrainSettings`
  (heights a metre apart over 1024 m, relief regions, painted by height; seed 1 pinned to the last bit)
  `Bed` (the road's bed: the ground set under it, 25° dirt banks and cuttings either side, so a car that
  leaves the road can climb back), `Rocks` (scattered off the road from the ground's seed, never on the bed),
  and `TrackGenerator` (`GenSettings`; the route walked in pieces with backtracking, levels chosen all at
  once to keep the road out of the ground, banks by a speed estimate; a `Track` with its `Ground`), and
  `StuntPlanner` (`Stunts.cs`, M3c: jumps and loops swapped in where the route already has their footprint,
  each approved by `Driving/SpeedEstimate` for a stock car pinned in code; checkpoints every 400 m), and
  circuits (M3g: `IsCircuit` by `GenStage.Kind`, `RouteWalker.WalkCircuit` out and home with a closing
  search, levels that come back round, banks on a flying lap), and `TrackCode` (M4a: a seed and the
  generator's version, `5v1`; `TrackGenerator.Version`, held to its output by a test that pins seeds 1-10).
  `Cars/` holds `CarSpec`, `Surfaces`, the `Wings` (parts, by ID) and `WheelModel`, the car's laws as
  plain functions. `Pieces/` is
  the road: `Frame`, the centrelines (line, pitch, banked turn, loop), `Chain`, `Profile`, the `Sweep`
  mesher and the `TestGroundLayout`. `Driving/` is the autodriver: `RacingLine` (samples, curvature,
  bank, speed plan, and `MinimumCurvature`, the racing line inside the wheels' room) and `Autodriver`
  (a front-axle Stanley tracker with yaw damping and slip guards); `LapTimer`, which times laps
  line to line for the game and the lap oracle alike; and `StuckWatch`, the auto-respawn's rule.
  `Tracks/` is M2's grid: `TrackGrid` (32 × 32 tiles of 20 m, levels of 2.5 m, on the plain),
  `PieceDef` (a piece as data: tiles, way in, way out, road steps; refused if its road misses its way
  out by a millimetre), `Placement` and `PlacedPiece` (turned and mirrored onto the grid),
  `PieceCatalogue` (one piece a file from `content/base/pieces/`), the `Showroom`, and `Track` (the
  track file, from `content/base/tracks/`: load, save, `Lay` against the catalogue for its problems and
  its route, start to finish, or on a circuit, format 3 with `laps`, from its start-finish line round to
  it), the `RaceTimer` (gates passed through their arches, in order, with
  splits) and `TrackRacing` (a laid track's gates, and its route as one racing line, stunts down the
  middle at their speed hints), `TrackEditor` (M5a: a track being edited, place, erase, open ends, next, undo, laid after every change), and `TrackFingerprint` (M4a: eight hex digits over what's driven, the
  placements, the pieces' shapes, the bedded heights and the rocks; ghosts are filed under it). `Ghosts/` is M4's stream: `Snapshot` (a pose, quantised as the file keeps
  it), `Ghost` (a header line of JSON with the car's spec, then the snapshots deflated; `At(t)` plays it back,
  holding at cuts; since P4 the header can carry the car's name and a profile run's caution), `GhostRecorder` (every 4th tick from the clock's zero to just past the finish) and
  `ReplayCameras` (M4d: cameras beside a laid track, at the start, each stunt, corners and long straights,
  each raised until it sees its stretch of road over the ground). `Pads/` is `PadProfile`. `Profiles/` is the
  car profile's (PROFILE-PLAN, P1): `RunMeter` (a run tick by tick: time and slowest speed on each route piece, lap
  by lap, top speed, off road, slides, spins past 30°, respawns; clean or not) and `CarSheet` (a run as a CSV row
  under a header from the track's route, the track's name in every row since 2026-11-25, one file per track, every
  car's numbers in `cars.csv`).
- `Sandrail.Core.Tests/` — xunit.
- `Sandrail.Bench/` — headless measurements, one arm per flag.
- `Sandrail.Game/` — the Godot project. `project.godot` sets Jolt, 120 physics ticks a second and
  physics interpolation; `GameClock` keeps a tick 1/120 s of game time at any time scale, ×0.01 to ×16, raises the
  most ticks a frame with it and measures how fast game time really runs (count ticks with its `TicksPerSecond`,
  never `Engine.PhysicsTicksPerSecond`, which the time scale turns up and down). `Main._EnterTree` gives every load
  a fresh physics world, so Jolt's body IDs start from nothing and a run is the same however it was reached (keep
  any new body inside the scene, built in a fixed order, and never hold a world or space across a reload). `Cars/` is the `Buggy`, its `CarLooks` (the grey boxes and wing, a node a ghost
  can wear too), the `GhostCar` (a best run see-through beside you, on the race clock), its `ChaseCamera`,
  `CarReadout`, the `RunRecorder`, `MeterRead` (the one rule for off the road and a slide, for the profile's
  meter), the `Garage` (the stock car, `user://tunes/` and the inactive cars in `tunes/inactive/`; `Main.Garage.cs` is the Esc menu's Change car) and the
  replay's `FreeCamera`; `Main.Profile.cs` is the profiler (sweeps through the race's own code, rows to the sheet,
  rows held while a spreadsheet has it open), and `Main.ProfileEverywhere.cs` chains it over every track (Profile every
  car on every track); `Main.Ghosts.cs` loads and saves each
  track's bests as ghosts, and since P4 each profiled car's best clean run (`profile-<car fingerprint>.ghost`, G steps
  through them), and `Main.Replay.cs` plays a run back (trackside, chase or free cameras);
  `Editor/` the `EditorView` (M5: camera, cursor, track laid onto the road's end, the palette, the mouse, problems; `Main.Editor.cs` opens it); `Ui/` the `Hud`, `TelemetryOverlay`, `TuningPanel`, the Esc menu (`SeedMenu`) and its drawn padlock (`LockButton`); `World/` builds the test ground's meshes and
  colliders; `Oracles/` the headless drives; `content/base/cars/buggy.json` the shipped spec.
- `tools/godot-sln/` — the solution Godot's editor builds from (`project/solution_directory`).
- `tools/profile-viewer/` — the car profile's viewer, in Python on DearPyGui: `sheets.py` reads, joins and sorts
  (it groups columns by the names `CarSheet.cs` writes, so change the two together), `viewer.py` is the window,
  `build.cmd` the exe.
- `Directory.Build.props` — Debug builds are JIT-optimised, Overburden's measured rule.

## Where things stand

**2026-11-23: M1 is built (M1a-M1d), plus wide lock, his pad's layout, a lap of corners and the
autodriver. The play that passes it waits, at his word, until after the track editor (M5): "i'll do
better tuning after the track editor is complete". M2 is under way (`docs/M2-PLAN.md`, its six
decisions taken as recommended): M2a, the grid and the piece catalogue, and M2b, the starter set of
fifteen pieces (corners 30 and 50 m flat and banked, slopes, two launch ramps and a landing, a grid loop,
a chicane, three gates), are built and shown in the showroom; M2c, the track file and the shakedown
(every piece in 1.28 km, `--track=base:shakedown`), and M2d, racing it (the clock, checkpoints in
order with splits, R back to the last checkpoint, Backspace to start again, the autodriver on the route
and the `track` oracle: the shipped car finishes it in 56.70 s at 80%), are built too. M2e, his play on
the shakedown (are the pieces the right size and shape?), is his whenever he likes. **M3, the
generator, is planned (`docs/M3-PLAN.md`, its six decisions taken as recommended) and under way**: M3a,
terrain from a seed (`--terrain=<seed>`, the `climb` oracle, the bench's `--terrain` arm), is built;
M3b, the route (`--seed=<n>`, the S-bend piece, the bench's `--generate`), is built too, and M3d's bed
came next at his word (25° dirt banks and cuttings; the `bank` oracle); M3c, stunts and checkpoints, is
built too (2026-11-24: jumps and loops where the route has their footprint and the speed estimate says
the stock car can make them, a median of 1 a track; checkpoints every 400 m; seed 5 keeps its road and
gains two short jumps). **M3g, circuits**, is built too (2026-11-24 at his ask: about half the seeds,
the even ones among 1-10, are circuits of 3 laps of 0.8–1.5 km, walked out from a start-finish line and
home again; format 3 carries `laps`; the board counts laps and keeps a best lap; seeds 1, 3, 5 and 7,
seed 5 his favourite, are byte for byte the sprints they were), and rocks off the road (M3d's last piece,
`Rocks`: grey boxes 12-70 m out from the road's edge, never on the bed). M3e (the Esc menu: the seed, generate, a new
random seed, save this track, and since M4 **Load a track**, a list of the game's tracks and every saved one;
`--track=user://tracks/gen-<code>.json` drives a saved one on its own ground too)
is built too. **M3 passed on his word, 2026-11-24** ("It all looks and feels good to me so far Claude lets
continue"). **M4 (ghosts, replays and track codes) is planned** (`docs/M4-PLAN.md`), its seven decisions
taken as recommended, and under way: **M4a, codes and fingerprints, is built** (the generator is version 1;
codes like `5v1` in the menu and on `--seed=`; generated IDs `gen:5v1`; the track fingerprint; seed 5 ships as
`content/base/tracks/gen-5v1.json`, `--track=gen:5v1`, so a later generator can't take it from him), and
**M4b, the snapshot stream, is built** (every run recorded at 30 a second, about 0.4 KB a second; the
`ghost` oracle; `CarLooks` out of `Buggy`, nothing changed on screen), and **M4c, ghosts, is built** (his
best or the autodriver's, see-through beside him on the race clock, G to choose; each driver's best saved
under the track's fingerprint, so bests last between sessions), and **M4d, replays, is built** (the last run
or a best, from the Esc menu or Enter; cameras beside the track, chase or free; pause, skip, ¼ to 2x). M4 is
built and **passed on his word, 2026-11-24** ("Looks great to me Claude lets continue"). **Brake lights** too, at his word: red when the
brakes are on, white reversing, on the car, the ghost (not faded) and the replay. **M5, the editor, is planned**
(`docs/M5-PLAN.md`), its seven decisions taken as recommended, and under way: **M5a, the editor's core, is built**
(`TrackEditor` in Core: place, erase, open ends, next, undo; the editor from the Esc menu on the plain), and **M5b**:
track laid onto the road's end by Enter, the palette, the mouse, and **M5c**: Tab test-drives from the piece under the
cursor and Tab comes back (`EditorSession` keeps the work), and **M5d**: tracks on a seed's ground (the bed caught up in
squares, off the main thread), Ctrl+S to save with a name, circuits with laps. **M5 is built**; M5e, his play, passes it. His tuning comes after
M5, and **the car profile** to tune with is planned (`docs/PROFILE-PLAN.md`, his idea, its seven decisions taken, the
sweep of cautions from 50%) and under way: **P1, the meter and the sheet, is built** (Core's `Profiles/`; the
`track` oracle measures with it, its line unchanged), and **P2, the garage, is built** (the Esc menu's Change car,
F2's Save as a new car, the stock car never written by the game), and **P3, profiling in the game, is built**
(Profile this car and Profile every car in the Esc menu, `--profile=`, his finished runs as "you" rows; the stock car
is clean up to 80% on the shakedown, 56.73), and **a viewer for the sheets** (2026-11-25, at his ask, so he can "sort
easily" without folding `cars.csv` into every row: `tools/profile-viewer/`, `SandrailProfiles.exe`), and **cars set
inactive** (2026-11-25, his ask for a delete behind a lock, made "inactive or do not use for further testing" at his
word: in Change car a padlock beside each of his cars opens Set inactive or Set active for five seconds; an inactive
car's file is moved, unchanged, into `tunes/inactive/`, it stays in the list, dimmed, and can be driven, and Profile
every car leaves it out; nothing is ever deleted), and **P4, profile ghosts, is built** (2026-11-25, decision 7: each
car's fastest clean profile run on a track is kept as its ghost, `ghosts/<track>/profile-<car fingerprint>.ghost`, so G
can put car A's best beside car B; G steps through his best, the autodriver's, each profiled car's and none; the Esc
menu's Watch a car's profile best; a fresh profile of the same car at the same caution keeps within 17 mm of its ghost),
and **Profile every car on every track** (2026-11-25, his ask: Profile every car on each of Load a track's tracks in
turn, unattended, then back to the track and car it began on; an unfinished track left out, a fingerprint profiled once;
`--profile=everywhere`; his 12 sound tracks and 4 cars took 26 minutes, the same headless or in the window; on the way,
a sweep's first run no longer comes out a hundredth slow after a change of car); the three Profile buttons now sit under
one **Profile** button in the Esc menu, which opens them as Change car opens the garage (his word, "Yes, nest them").
**A run is the same however it's reached** (2026-11-25, his yes): a run could come out a hundredth apart after a reload,
because Jolt orders the chassis's contacts with the road (the loop, landings) by its bodies' IDs, which a reload changes;
the wheels' rays never tie. Now every load starts on a fresh physics world, and a row reached after any reload is the
launch's, cell for cell, the oracles unchanged.
**P5, his play with the profile, is next**, then his tuning.
Slow motion is in too: `,` and `.` (`GameClock`). **The stock car has his
steering** (`maxSteerDeg` 21.35, `minSteerDeg` 2.05; fingerprint `4f918acd`), lifted from his saved
buggy on the evening of 2026-11-23, and nothing else of his: players start with it and set it up by
trial and error. His saved buggy, a whole tune, is in his garage as `tunes/matthew-2026-11-24.json`
(`d0142095`), and since 2026-11-25, at his word, the repo's `buggy.json` is the stock car again: the game never
writes it (P2), so his tunes live only in the garage. Oracles checking Claude's changes still run on the committed
spec via `-Spec`.** The buggy drives a test ground on a sand plain, stations 1-6: a tabletop
jump, a slope and plateau with a crest, a 30° banked hairpin, a tall loop driven with the wheels
straight, a dirt bump patch, and a 526 m lap of six different corners (flat to 35° bank, 18 to 60 m
radius, one left-hander), which is also the shortlist for M2's corner tiles. **The autodriver** (his
idea) laps it on a minimum-curvature racing line drawn on the road (P to hand over, L for the line,
- and + for its aggression, 50-100%): at 80% the shipped car does 24.5 s clean, and his evening tune
22.1 s, clean up to 85% (21.8 s), but 2.5 m wide on the straight from corner 6 to corner 1. **That
isn't its braking, as first written here**: brake force, brake balance and the front bar all leave it
there, less power only trims it, and every car spins on that straight once pushed (the shipped car
from 90%), so it's most likely how the autodriver takes corner 6's exit; not yet pinned down.
**A lap timer** times the corner lap (a standing start from station 6 on the first throttle, then line
to line), with his best and the autodriver's on the board side by side. **No wheel-spin gauge, at
his word ("Not for now")**: the car has no wheel spin to measure, and he chose not to add it yet.
**The handbrake** (Space, pad A, his pad's button 2) locks the rear wheels by `handbrakeLock` (1:
fully), and a locked tyre slides against its own motion: at full lock from 60 km/h it turns the car
142° in 2 s against 62° without, and a half-second pull with the lock held swings it past sideways.
**Downforce comes from a wing, a part chosen by ID** (his ask: customizable, and to be earned and paid
for later): none, a lip spoiler, a strut wing or a twin-plane wing, picked in F2 above the angle, how
far back and how high. **The shipped buggy has none.** Bigger wings cost top speed (177 to 159 km/h)
and buy corner speed (lap 24.52 to 23.72 s); where it's mounted is the balance (at the rear axle it
only makes the car run wide; at the middle, 1.22 g at 136 km/h against 0.97 g bare); and in the air a
wing turns the car to follow its arc. **Air control**: with no wheel down, throttle lifts the nose,
brake drops it, and steering turns and rolls the car (F2's Air group; 0 is off). Doing nothing leaves
the jump's own rotation alone. A 0.3 s tap of brake is worth 11° at touchdown, and holding throttle
through the whole jump 36°. **The auto-respawn** puts the car back where R would after 3 s tipped over
and nearly stopped (on its roof or side), with a countdown, and at once if it falls off the world.
Twelve oracles (committed spec `964252ce`): the loop
goes through 0.1 m off centre, but **the suspension bottoms out on the jump's landing and through the
loop** (worklog, M1c). **Steering is his to open**: Shift doubles the lock (`wideLockFactor`), with
Ackermann steering (`ackermannShare`) to make 60° work. **He drives with his mum's clone pad**, with a
saved layout (throttle and brake on the right stick). **His own tunes are kept outside the repo**, in
`%APPDATA%\Godot\app_userdata\Sandrail\tunes\`, the garage (Esc, Change car; or `start.cmd --spec=<path>`):
`matthew-2026-11-23.json`, a heavier, much faster car (1004 kg, 243 kW, grip 1.4) whose springs are too
soft for its weight, so it bottoms out (worklog); `matthew-2026-11-23-late.json` (844 kg, 159.5 kW,
twin-plane wing); and `matthew-2026-11-24.json` (974 kg, grip 1.31, twin-plane wing), his latest. He's thinking of making **building
your car part of the game** (PLAN, "His ideas"). The bottoming is a lever to bring to his play rather
than to change unasked.

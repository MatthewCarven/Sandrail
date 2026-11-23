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
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | **M3, the generator**: eight facts (the noise's reach, how fast the road can climb, how steep the car can, why the route is walked in pieces, why a stunt can only replace its own footprint, the height field, painted ground, terrain in the track file), the model (settings, a seed per stage, terrain, route, stunts, the bed), six decisions (his, awaiting answers), slices M3a–M3f. |
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
must be none**. It reports time, length, pieces, attempts, backtracks, slopes, banks and how the road met
the ground; `--first=` starts elsewhere, `--turniness=` tries the dial, and five seeds or fewer print each.

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
is how to try numbers without touching what plays. There are sixteen: `straight`, `drop`, `brake` and
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
(the M3d entry). **`lap` is how a tune is compared**: run
it on two specs and read the flying lap, the corner speeds and where it left the road; `track` says
whether a car (or a track) can be finished at all.

Game flags, after `--` (or passed to `start.cmd`): `--seed=<n>` (a generated track on its own ground,
M3b: the timer, P and `--oracle=track` all work on it; the autodriver finishes most at `--caution=0.7`),
`--terrain=<seed>` (the generated ground alone, M3a), `--ground=test|showroom|bare` (the showroom is every
M2 piece on its own with a run-up, a station each; [ and ] step through them), `--track=base:shakedown`
(a track from `content/base/tracks/` by its ID, or `--track=<path>.json`; the car starts at its start
gate, the clock starts on the throttle, R goes back to the last checkpoint (before the first, it starts
again), and holding R for a second or pressing Backspace starts again, clock and all;
`--checkpoint=N` starts where R would put the car after checkpoint N, for a screenshot), `--station=N`,
`--surface=road|dirt|grass|sand` (sand by default on the test ground), `--spec=<path>`,
`--oracle=<name>`, `--panels=tuning,telemetry` (opens either at start),
`--record` (starts a run at start), `--autodrive` (the autodriver from the start; with `--station=6`),
`--caution=` (the autodriver's aggression, the share of grip it plans for, 0.5 to 1, 0.8 by default:
for the game and the `lap` oracle alike; in the game - and + change it),
and `--screenshot=<path.png>`, which saves a frame at rest and one driving, then quits; with it,
`--drive=throttle,steer` sets how it drives and `--camera=x,y,z:x,y,z` looks at the ground from a fixed
point instead of following the car. It opens a window for a few seconds, and it is how Claude sees
anything at all. `--padcheck` is a sensor run on a gamepad instead of a drive: it waits for a touch,
records 30 s, and writes `user://padcheck.txt` (worklog 2026-11-22 has his pad's). `--bindpad` asks
for each control on the pad and saves the pad's own layout to `user://pads/<guid>.json`, which then
replaces the default pad bindings for that pad. **His pad (a generic clone) has one**: throttle and
brake on the right stick, since its L2/R2 are only buttons.

**A tuning session** (M1b): Matthew drives with F3 (telemetry) up, presses F4 to record the bit that
feels wrong, and says what felt wrong. Runs land in `%APPDATA%\Godot\app_userdata\Sandrail\runs\` as
`<time>-<fingerprint>.csv` with the spec beside them as `.spec.json`. Read the CSV (49 columns, units
in the headers), change numbers in a `-Spec` variant, run the oracles, and only then touch
`buggy.json`. He can also change numbers live with F2, whose Save writes the loaded spec file.

## Layout

- `Sandrail.Core/` — no Godot. `Generation/` holds `Hash64`, `GradientNoise`, the `BumpField`, and
  M3's generator as it's built: `GenStage` (a seed per stage), `Terrain` with its `TerrainSettings`
  (heights a metre apart over 1024 m, relief regions, painted by height; seed 1 pinned to the last bit)
  `Bed` (the road's bed: the ground set under it, 25° dirt banks and cuttings either side, so a car that
  leaves the road can climb back),
  and `TrackGenerator` (`GenSettings`; the route walked in pieces with backtracking, levels chosen all at
  once to keep the road out of the ground, banks by a speed estimate; a `Track` with its `Ground`).
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
  its route, start to finish), the `RaceTimer` (gates passed through their arches, in order, with
  splits) and `TrackRacing` (a laid track's gates, and its route as one racing line, stunts down the
  middle at their speed hints). `Pads/` is `PadProfile`. The generator and its formats land beside them.
- `Sandrail.Core.Tests/` — xunit.
- `Sandrail.Bench/` — headless measurements, one arm per flag.
- `Sandrail.Game/` — the Godot project. `project.godot` sets Jolt, 120 physics ticks a second and
  physics interpolation. `Cars/` is the `Buggy`, its `ChaseCamera`, `CarReadout` and the `RunRecorder`;
  `Ui/` the `Hud`, `TelemetryOverlay` and `TuningPanel`; `World/` builds the test ground's meshes and
  colliders; `Oracles/` the headless drives; `content/base/cars/buggy.json` the shipped spec.
- `tools/godot-sln/` — the solution Godot's editor builds from (`project/solution_directory`).
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
next, then the rest of M3d (rocks). **The stock car has his
steering** (`maxSteerDeg` 21.35, `minSteerDeg` 2.05; fingerprint `4f918acd`), lifted from his saved
buggy on the evening of 2026-11-23, and nothing else of his: players start with it and set it up by
trial and error. His saved buggy, a whole tune, sits uncommitted in his working `buggy.json`; oracles
checking Claude's changes run on the committed spec via `-Spec`.** The buggy drives a test ground on a sand plain, stations 1-6: a tabletop
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
`%APPDATA%\Godot\app_userdata\Sandrail\tunes\`, and `start.cmd --spec=<path>` drives one:
`matthew-2026-11-23.json`, a heavier, much faster car (1004 kg, 243 kW, grip 1.4) whose springs are too
soft for its weight, so it bottoms out (worklog); and `matthew-2026-11-23-late.json` (844 kg, 159.5 kW,
twin-plane wing), which is also his working `buggy.json`, uncommitted. He's thinking of making **building
your car part of the game** (PLAN, "His ideas"). The bottoming is a lever to bring to his play rather
than to change unasked.

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
is how to try numbers without touching what plays. There are nine: `straight`, `drop`, `brake` and
`circle` on the bare plain with road grip (their M1a figures are in the 2026-10-31 worklog entry),
`handbrake` there too (a stop on it alone, and a full-lock turn with and without it; the M1d entry),
`jump`, `loop` and `bumps` on the test ground (figures in the M1c entry), and `lap`, the autodriver
lapping station 6 (figures in the 2026-11-23 autodriver entry). **`lap` is how a tune is compared**: run
it on two specs and read the flying lap, the corner speeds and where it left the road.

Game flags, after `--` (or passed to `start.cmd`): `--ground=test|bare`, `--station=1..6`,
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
`<time>-<fingerprint>.csv` with the spec beside them as `.spec.json`. Read the CSV (47 columns, units
in the headers), change numbers in a `-Spec` variant, run the oracles, and only then touch
`buggy.json`. He can also change numbers live with F2, whose Save writes the loaded spec file.

## Layout

- `Sandrail.Core/` — no Godot. `Generation/` holds `Hash64`, `GradientNoise` and the `BumpField`.
  `Cars/` holds `CarSpec`, `Surfaces` and `WheelModel`, the car's laws as plain functions. `Pieces/` is
  the road: `Frame`, the centrelines (line, pitch, banked turn, loop), `Chain`, `Profile`, the `Sweep`
  mesher and the `TestGroundLayout`. `Driving/` is the autodriver: `RacingLine` (samples, curvature,
  bank, speed plan, and `MinimumCurvature`, the racing line inside the wheels' room) and `Autodriver`
  (a front-axle Stanley tracker with yaw damping and slip guards); and `LapTimer`, which times laps
  line to line for the game and the lap oracle alike. `Pads/` is `PadProfile`. The track
  grid, generator and formats land beside them.
- `Sandrail.Core.Tests/` — xunit.
- `Sandrail.Bench/` — headless measurements, one arm per flag.
- `Sandrail.Game/` — the Godot project. `project.godot` sets Jolt, 120 physics ticks a second and
  physics interpolation. `Cars/` is the `Buggy`, its `ChaseCamera`, `CarReadout` and the `RunRecorder`;
  `Ui/` the `Hud`, `TelemetryOverlay` and `TuningPanel`; `World/` builds the test ground's meshes and
  colliders; `Oracles/` the headless drives; `content/base/cars/buggy.json` the shipped spec.
- `tools/godot-sln/` — the solution Godot's editor builds from (`project/solution_directory`).
- `Directory.Build.props` — Debug builds are JIT-optimised, Overburden's measured rule.

## Where things stand

**2026-11-23: M1a, M1b and M1c are built, plus wide lock, his pad's layout, a lap of corners and the
autodriver; M1d has begun with the handbrake.** The buggy drives a test ground on a sand plain, stations 1-6: a tabletop
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
Nine oracles (committed spec `da9506cc`): the loop
goes through 0.1 m off centre, but **the suspension bottoms out on the jump's landing and through the
loop** (worklog, M1c). **Steering is his to open**: Shift doubles the lock (`wideLockFactor`), with
Ackermann steering (`ackermannShare`) to make 60° work. **He drives with his mum's clone pad**, with a
saved layout (throttle and brake on the right stick). **`buggy.json` is the shipped car again**, restored
from GitHub at his word on the evening of 2026-11-23. **His own tune is kept outside the repo**, at
`%APPDATA%\Godot\app_userdata\Sandrail\tunes\matthew-2026-11-23.json`, and `start.cmd
--spec=<that path>` drives it. It's a heavier, much faster car (1004 kg, 243 kW, grip 1.4), but its
springs are too soft for its weight, so it bottoms out (worklog). He's thinking of making **building
your car part of the game** (PLAN, "His ideas"). **The rest of M1d is the other extras the pieces ask for** (downforce,
air control, auto-respawn), and the bottoming is a
lever to bring to his play rather than to change unasked.

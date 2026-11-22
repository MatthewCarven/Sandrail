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
is how to try numbers without touching what plays. There are seven: `straight`, `drop`, `brake` and
`circle` on the bare plain with road grip (their M1a figures are in the 2026-10-31 worklog entry), and
`jump`, `loop` and `bumps` on the test ground (figures in the M1c entry).

Game flags, after `--` (or passed to `start.cmd`): `--ground=test|bare`, `--station=1..5`,
`--surface=road|dirt|grass|sand` (sand by default on the test ground), `--spec=<path>`,
`--oracle=<name>`, `--panels=tuning,telemetry` (opens either at start), `--record` (starts a run at
start), and `--screenshot=<path.png>`, which saves a frame at rest and one driving, then quits; with it,
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
  mesher and the `TestGroundLayout`. The track grid, generator and formats land beside them.
- `Sandrail.Core.Tests/` — xunit.
- `Sandrail.Bench/` — headless measurements, one arm per flag.
- `Sandrail.Game/` — the Godot project. `project.godot` sets Jolt, 120 physics ticks a second and
  physics interpolation. `Cars/` is the `Buggy`, its `ChaseCamera`, `CarReadout` and the `RunRecorder`;
  `Ui/` the `Hud`, `TelemetryOverlay` and `TuningPanel`; `World/` builds the test ground's meshes and
  colliders; `Oracles/` the headless drives; `content/base/cars/buggy.json` the shipped spec.
- `tools/godot-sln/` — the solution Godot's editor builds from (`project/solution_directory`).
- `Directory.Build.props` — Debug builds are JIT-optimised, Overburden's measured rule.

## Where things stand

**2026-11-22: M1a, M1b and M1c are built, plus wide lock; M1d is next.** The buggy drives a test
ground on a sand plain: a tabletop jump, a slope and plateau with a crest, a 30° banked hairpin, a tall
loop driven with the wheels straight, and a dirt bump patch, on stations 1-5. Seven oracles
(fingerprint `a2e24a2b`): the loop goes through 0.1 m off centre, but **the suspension bottoms out on
the jump's landing and through the loop** (worklog, M1c). **Steering is his to open now**: after driving
the test ground he asked for Shift to double the lock, which is built (`wideLockFactor` 2, turn rates
scaled with it) and needed **Ackermann steering** (`ackermannShare` 1) to work at 60° (worklog,
2026-11-22). Otherwise unchanged since M1a: the friction circle is a dial, `forwardPriority`, because
the keyboard is what gets tuned (**there's no pad here**; the pad is an owed check); damping is
1,400/2,100. His first drive: "seems ok", the power oversteer *"exactly what i was hoping for"*. Nobody
has clicked the panel's Save or Reset yet. **M1d is the extras the pieces ask for** (handbrake,
downforce, air control, auto-respawn), and the bottoming is a lever to bring to his play rather than to
change unasked.

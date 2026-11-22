# Worklog

Newest first. What happened and why; findings worth keeping.

## 2026-11-22 — wide lock on Shift, and Ackermann steering

His ask, verbatim, after driving the test ground: *"Ok can we try shift doubles the lock to lock of the
angle?"* So the steering he'd said to leave alone is his to open, and he has.

**Built:** holding Shift multiplies the steering lock by `wideLockFactor` (2, a new spec number on the
F2 panel), at every speed, so 30° at a standstill becomes 60° and 8° at top speed becomes 16°. **The
turn rates scale with it**, so full lock takes the same time to reach either way; the bigger lock would
otherwise arrive twice as slowly, against his "a little slow". Telemetry says WIDE LOCK while it's held,
runs get a `key_wide_lock` column, and the pad's left shoulder is bound to it (untested, no pad).

**Found on the way, and fixed:** at 60° the car barely moved. At a quarter throttle on full lock it
managed 0.8 km/h after 3 s, against 13 km/h at the normal 28°. Turning the low-speed sideways cancel
off only got it to 3 km/h, so that wasn't the whole cause. The rest was geometry: both front wheels were
steered to the same angle, and on the 1.5 m radius that 60° gives, the inside wheel wants about 76° and
the outside 48°. They scrubbed against each other. **So the front wheels now have Ackermann steering**:
the inside one turns more, both rolling round one centre on the rear axle's line. `ackermannShare` (1,
true geometry; 0 is the old parallel steering) is on the panel. Tests pin it: the inside wheel turns
more, both axles meet at one point, a left turn mirrors a right one.

Full lock from rest, five seconds, bare plain:

```
                       before             with Ackermann
0.25 throttle, normal  13.2 km/h          14.9 km/h, front scrub 6.5° -> 3.6°
0.25 throttle, wide     0.8 km/h stuck     9.3 km/h, pivoting at 42°/s, front slip 5.5°
0.6 throttle, wide     19.6 km/h          21.4 km/h, the front ploughs (49°): too fast for 1.5 m
full throttle, wide    spinning           spinning, tail out 72°
```

**The oracles**, now spec `a2e24a2b` (the two new numbers moved the fingerprint): straight, drop,
brake, jump, loop and bumps read exactly as before, because their wheels are straight. The circle moved
from 1.07 g at 5.4° to 1.06 g at 5.5°: a little less front scrub with Ackermann.

**Worth him knowing:** wide lock at speed is a lot of lock. 16° at top speed is twice what the tyres
can use there, so Shift at speed means a spin or a plough. Whether that's a feature is his call, and
the panel's `wideLockFactor` and the speed fade (`minSteerDeg`, `steerFadeSpeedMps`) are the levers.

## 2026-11-17 — M1c: the sweep mesher and the test ground

Built on his "lets continue":

- **Core, `Pieces/`:** a `Frame` (position, forward, up, right, in Godot's axes); four centrelines
  (`Line`, `Pitch` for ramps and crests, `Turn` with an eased bank that pivots on its inside edge,
  `Loop`); `Chain` to lay them end to end; `Profile.Slab` (a flat top with rounded lips); `Sweep`, which
  places the profile along a centreline and returns a closed mesh as plain arrays; and
  `TestGroundLayout`, the whole test ground as data. `Generation/BumpField` is the bump patch, the
  first use of the ported noise. 74 tests: every piece is closed and wound outward, a right turn of 90°
  ends where geometry says, the bank keeps its inner edge on the ground, the loop comes out level and
  offset by exactly its offset, the stations are 30 m apart and fit the plain.
- **Game, `World/TestGround`:** each run swept into one mesh and one collider (the same triangles),
  with a road shader (edge lines, a dashed centre line, a band every 10 m). The bump patch is a
  129 × 129 height field. The plain is now sand when the test ground is up.
- **Stations 1-5** on the number keys: jump, slope, banked hairpin, loop, bumps. R respawns at the last
  station, and each shows its hint as it's reached.
- **Three oracles on the test ground:** `jump` (86 km/h at the lip), `loop` (97 km/h in), `bumps`
  (72 km/h across). The M1a four run on the bare plain on road grip, as before.
- **Flags:** `--ground=test|bare`, `--station=1..5`, and for Claude's screenshots `--drive=throttle,steer`
  and `--camera=x,y,z:x,y,z`, a fixed view of the ground.

**Two departures from the plan, both forced by arithmetic:**
1. **The loop is tall, not round.** At the least speed that gets a car over the top of a circle, its
   foot pulls 6 g whatever the radius, and this suspension holds about 7 g a corner before it runs out
   of travel. So the radius runs from 18 m at the foot to 7 m at the top, as real stunt loops do. It's
   24 m tall.
2. **Its offset grows with distance round, and the road arrives at an angle (11.6°).** A loop has to
   put its exit beside its entry, and an offset that grows with the *angle* would need steering
   mid-loop. The loop is an extrusion of its outline along one fixed right, so unrolled it's a flat
   strip, and an offset that grows evenly with *distance* is a straight line on it: straight wheels
   follow it. The run-up and run-out are built along the same skewed frame so they meet it square.

**The figures**, spec `f4559570`:

```
straight  unchanged from M1a (0-100 5.17 s, top 49.11 m/s); drop, brake, circle unchanged too
jump      off the lip at 82 km/h: 1.84 s in the air, landed 39.7 m out, on the down-ramp (20-46 m),
          pitch -8° | deepest compression 0.350 of 0.35 m (on the bump stop) | upright
loop      in at 96 km/h: THROUGH, +0.1 m off the exit road's centre | slowest over the top 79 km/h |
          deepest compression 0.350, 2.89 s on the bump stop | all four wheels off 0.00 s
bumps     through at 70 km/h: a wheel off the ground 46% of the time, all four 10% (longest 0.18 s) |
          deepest compression 0.291, bump stop 0% | worst roll 9°, pitch 13°
```

**What they say, for M1d and his play:**
- **The loop works with the wheels straight**: 0.1 m off the exit's centre after 79 m of loop. The
  design held.
- **The suspension bottoms out on the jump's landing and through the loop.** The jump lands at the
  ramp's own angle (-8°), which is the right landing, and still uses all 0.35 m. The loop spends
  2.9 s of about 3.5 on the bump stop, because the oracle holds 97 km/h with the throttle and the
  engine nearly cancels gravity on the way up: 79 km/h over a 7 m top is about 6 g. That's what a driver
  holding W gets. **No numbers were changed**: springs, travel and the bump stop are M1d's levers and
  his play's question.
- **The first bumps run read 32 km/h**, because the station put the car only 25 m before the patch, on
  sand. It now starts 60 m out.

**Seen:** overview shots of all five stations from `--camera`, plus a chase shot on the loop's run-up.
The loop is tall with its exit beside its entry; the kicker is a curved ramp onto a flat table; the
hairpin's outside edge lifts; the plateau reads as earthworks. The bump patch looks smoother on screen
than it drives, because the light flattens it; the oracle's 46% says it isn't.

**Unverified:** how any of it feels, which is his.

## 2026-11-17 — Sandrail

Asked for a name, Matthew picked **Sandrail** from Claude's list ("Sandrail please"). A sandrail is the
stripped-down, long-travel dune buggy that is this game's car. The repository is
https://github.com/MatthewCarven/Sandrail, and he said to push "as and when you please".

**The rename was the search-and-replace CLAUDE.md's third non-negotiable promised**: folders, project
files, the two solutions, namespaces, `project.godot`'s name and assembly, `start.cmd`, the oracle script
and the living docs. **No content ID changed**, because they're all `base:`, so the spec's fingerprint
is still `f4559570` and no file the game reads needed touching. The build caches (`bin/`, `obj/`,
`.godot/`) were deleted rather than moved, since they hold paths to the old names.

**The entries below this one still say Tumbleweed**, because that's what it was called when they
happened; read `Tumbleweed.*` there as `Sandrail.*`. One thing moves with the name: Godot's user
folder, so recorded runs now land in `%APPDATA%\Godot\app_userdata\Sandrail\runs\`. The old
`...\Tumbleweed\` folder holds only logs and caches from before, and nothing reads it.

## 2026-11-17 — M1b: the tuning tools

Built as M1-PLAN's M1b describes, on his "M1b as planned" (he was offered the bump patch first):

- **Core:** a `[Tune(group, min, max)]` on every number in `CarSpec`, `CarSpec.Tunables` (every number
  by reflection, in declaration order, with its group and range), and `WithValue(name, value)`, a copy
  with one number changed. `EveryNumberHasATuneRange` nags when a new number arrives without a range;
  it still gets a slider meanwhile. 52 tests.
- **F2, the tuning panel:** 39 sliders in six groups (body, wheels, suspension, tyres, engine and
  brakes, steering and pedals), each with a box to type into. A change applies the moment it's made;
  one the spec refuses (say, minimum steering above maximum) isn't applied and the panel says why in
  red. Numbers changed from the file are yellow and the title says "unsaved". **Save writes the file
  that was loaded** (the shipped `buggy.json`, or `--spec=`'s), and Reset reloads it. Sliders take no
  keyboard focus, so the arrows keep steering.
- **F3, telemetry:** speed, sideways and fore-aft g, body slip, yaw, roll, pitch; throttle, brake and
  steering against the keys behind them, with the steering lock allowed at that speed; and per wheel a
  compression bar, load, slip angle, surface, and FULL GRIP or AIR.
- **F4, recording:** every tick to `user://runs/<time>-<fingerprint>.csv`, with the spec saved beside
  it as `.spec.json`. That matters for numbers changed in the panel and never saved. 47 columns, units
  in the headers, invariant culture.
- **`Buggy.ApplySpec`**: the car takes a new spec live, and rebuilds its collider and grey boxes only
  when a dimension changed. `Buggy` also now reports its drive force, brake, steering lock and
  acceleration. `CarReadout` turns all that into the numbers the overlay and the recorder share.
- **Flags:** `--panels=tuning,telemetry` opens either at start; `--record` starts a run at start.

**Checks:** 52/52 tests; **all four oracles identical to M1a, fingerprint `f4559570`**, so the tools
changed nothing about how the car drives. A `--screenshot` run with both panels open and `--record`
on wrote 600 rows (5 s × 120) of 47 columns that read back cleanly, and the numbers agree with each
other: in the turn, the left wheels carry the load (1,741 and 1,928 N against 1,259 and 1,446), the
roll is 1° with the right side up, and the rears read FULL GRIP in an 18° power slide at 38 km/h,
which is the oversteer he liked. The two test runs were deleted afterwards so his runs folder starts
empty.

**Two faults the screenshot caught**, both fixed before this entry: the telemetry was double-spaced,
because `AppendLine` writes a carriage return on Windows and a Godot label counts it as a second line
break; and the recording message showed a long path with mixed slashes, so the HUD now shows the
file's name and the log keeps the whole path.

**Unverified:** nobody has clicked Save or Reset or dragged a slider. Their arithmetic is Core's
(tested), but the clicks are his to try. Also unverified: how any of it feels in use.

## 2026-11-17 — the first drive

Matthew drove M1a on the plain (spec `f4559570`, road, keyboard). His words, verbatim:

> Ok have driven seems ok, steering is a little slow but at the same time its difficult, the tail
> stepping out under acceleration was exactly what i was hoping for its a little difficult now but
> well i have not tried it with bumpy terrain like
>
> like leave the steering alone for noe

What it settles and what it leaves open:
- **Power oversteer is right.** M1a's dial, `forwardPriority` 0.3, is what he was hoping for, so it
  stays. It was the riskiest number in the spec, because the plan's own rule (1.0) would have spun the
  car on a keyboard.
- **Steering: "a little slow but at the same time its difficult", and left alone at his word.** Read
  two ways, it could be the 120°/s turn-in rate being slow to arrive while the result, once there, is
  hard to hold. When he picks it up again, the levers are `steerRateDegPerS` (120),
  `steerReturnRateDegPerS` (240), and the lock against speed (`maxSteerDeg` 30, `minSteerDeg` 8, fading
  over `steerFadeSpeedMps` 50). M1b's telemetry will show which one the difficulty lives in. Nothing
  changed.
- **"A little difficult now", with the bumps still to come.** No change until he's driven it on bumpy
  ground, which is M1c's bump patch.

## 2026-10-31 — M1a: the buggy on the plain

Built as [M1-PLAN](docs/M1-PLAN.md) describes:
- **Core:** `CarSpec` (every tunable number, strict JSON: an unknown field is refused, a missing one
  takes its default, NaN is caught), `Surfaces` (road, dirt, grass, sand), and `WheelModel`, the
  plan's model as plain functions. The laws being in Core, not just the numbers, wasn't in the plan;
  it means every rule (the circle, the slide, the bump stop, the pedal logic) is pinned by a test. 45
  tests.
- **Game:** `Buggy` (rays, forces and grey boxes; physics in `_IntegrateForces`, where ray queries
  through `state.GetSpaceState()` work under Jolt), `ChaseCamera`, `Controls` (pad bound but untested,
  no pad here), a speedometer, a 10 m and 100 m grid on the plain, physics interpolation on, and
  `content/base/cars/buggy.json`.
- **Oracles:** `--oracle=straight|drop|brake|circle`, all four via `pwsh tools/oracles.ps1`.

**The figures**, shipped spec `f4559570`, road:

```
straight  0-100 km/h 5.17 s | 5 s 27.0 m/s | 10 s 41.5 | 20 s 48.4 | top 49.11 m/s (177 km/h; estimate 49.65) | drift 0.00 m
drop      from 1 m: deepest 0.305 of 0.35 m | overshoots 1 | settled 0.70 s | rest 0.630 m (expected 0.630)
brake     from 100 km/h: 38.4 m, 2.81 s (grip limit 35.8 m) | mean 1.03 g | heading moved 0.7°
circle    max sideways 1.07 g at steer 0.25 (5.4°) | held to full lock, body slip -3.8°, no roll
```

All inside the plan's targets. Two runs of the same spec print the same figures, as `--fixed-fps 120`
intends.

**Two changes from the plan, both recorded there:**
1. **The friction circle is a dial, `forwardPriority`, 0.3 to start**, not pure forward-first. That
   follows from the keyboard answer: a key is full throttle, and at full forward-first a held
   throttle below about 24 m/s (where 4,500 N split across two rear tyres beats their roughly 1.9 kN
   of grip) leaves no sideways grip at the rear, so every turn would spin. At 0.3, pushing still takes
   grip from turning (`PushingTakesGripFromTurning`), gradually.
2. **Damping went from 1,000/1,500 to 1,400/2,100 N·s/m** (bump/rebound). The plan's numbers gave 2
   overshoots and touched the bump stop from a 1 m drop. Tried with `--spec=` on three variants:
   1,400/2,100 → 1 overshoot, 0.70 s; 1,600/2,400 → 0 overshoots, 0.48 s; 1,800/2,600 → 0 overshoots,
   0.52 s. The smallest change that meets the target was taken. A buggy that settles dead flat may
   feel wooden over bumps, and his play decides. The circle was the same on all three.

**Found on the way:** Godot's project default linear damping (0.1 a second) is a 3,250 N brake on
this car at 50 m/s, more than its modelled drag. `Buggy` replaces it with zero; drag is the spec's
alone. The plan's "velocity-at-a-point helper with a test of its own" wasn't needed:
`GetVelocityAtLocalPosition` is Godot's own, and the straight oracle's 0.00 m drift and the circle's
symmetric numbers are consistent with it.

**Seen, not just measured:** `--screenshot=` saved a frame at rest and one turning at 38 km/h. The car
sits on its wheels, axles across, and the camera follows. That is the extent of what's verified.
**How it feels is unverified until he drives it**, and the pad is unverified until there is one.

## 2026-10-31 — M0 committed, M1 planned

M0 went in as `ff26504`. Then [docs/M1-PLAN.md](docs/M1-PLAN.md), written in Overburden's layout
(facts, the model, decisions, slices, order, levers). Its first fact sets the shape of the milestone:
**Claude can't drive**, so feel arrives as Matthew's words, and M1 has to turn them into numbers.
That's why it has telemetry, recorded runs and headless oracles, not just a car.

His answers: keyboard and pad both, **but there's no pad here**, so the pad gets wired up and stays
untested, and the keyboard is what gets tuned; tuning starts grippy on the road and loose off it; the
camera rolls with the car through loops; R respawns, and the car also respawns itself after 3 s upside
down and nearly stopped. The spec as `base:buggy` JSON and km/h are taken as recommended and are his to
change.

## 2026-10-31 — the plan, and M0

**The plan.** Matthew: a game in the genre of 4D Sports Driving, "but better", with a different name,
not a direct copy; suspension and tyre grip physics "but cheaply"; long term, a level editor, with
levels randomly generated the way Overburden's world is; a dune buggy with a higher top speed. His
answers set point-to-point first with circuits later, drivable terrain, the C# Core/Game split, and
"better" as light physics, room for cheap multiplayer, ghosts and replays, seed sharing, and physics
feel tuned together in playtests. "It need not be exact and if you have something better please speak
up", so [docs/PLAN.md](docs/PLAN.md) carries nine suggestions marked as Claude's.

**M0, the skeleton.** He chose a codename over a name, so this is **Tumbleweed**. Built:

- `git init` on `main`. Nothing committed yet.
- Godot 4.7.1 mono copied in beside the code and gitignored (PLAN decision 3, taken as recommended:
  the two projects can then change Godot versions independently). `start.cmd` falls back to
  Overburden's copy.
- `Tumbleweed.Core`, `.Core.Tests`, `.Bench`, `.Game`, `Tumbleweed.slnx`, and `tools/godot-sln/` for
  the editor. `Directory.Build.props` and `.gitignore` are trimmed copies of Overburden's.
- `Hash64` and `GradientNoise` ported unchanged into `Tumbleweed.Core.Generation`, less `ForChunk`
  (chunks don't exist here), with their 14 tests.
- `project.godot` sets Jolt Physics and 120 ticks a second: a fast car through a loop wants the
  shorter step, and one rigid body with four raycasts can afford it.
- `Main.tscn`: sky, sun, a 640 m sandy plain (one 32 × 32 map of 20 m tiles) with a world-boundary
  collider, and a camera. `Main.cs` prints the version and `Hash64.Mix(0)`, the check that Game links
  the Core the tests ran against.
- **A choice made without asking, recorded here so it can be undone: the base game's content
  namespace is `base:`, not the codename** (CLAUDE.md non-negotiable 3). A rename then never touches a
  saved track.

Checks: `dotnet test` 14/14; `dotnet build Tumbleweed.Game` with 0 warnings; Godot `--import` and a
240-frame headless run, which printed `Core linked: Hash64.Mix(0) = 0xE220A8397B1DCDAF`. The version
reads `0.0.1` without a commit suffix because there's no commit yet.

**A finding for M3, from the bench's `--noise` arm** (Release, seed 1, 4 octaves, 1024 × 1024 at
frequency 0.01): **min −0.554, p1 −0.366, p50 −0.005, p99 0.378, max 0.547**, at 119 ns a sample.
So 4-octave fBm reaches about ±0.55, not ±1, and 98% of the map sits within ±0.37. A terrain amplitude
of *A* gives hills of about ±0.37 *A* nearly everywhere. Overburden's ±0.6 relief constants were set
for its single-octave relief field, which reaches further, so they need measuring again here rather
than copying. A whole 32 × 32 map's terrain at one sample per metre is about 410,000 samples, roughly
50 ms, so the generator can afford it on the main thread and needs no cleverness.

# The plan — Sandrail, a stunt-track buggy racer

2026-10-31. **A draft; M0 built the same day under the codename Tumbleweed, named Sandrail
2026-11-17.** Written with Matthew in the first session; his answers are the
decisions below, and the "speaking up" section is Claude's, each one his to veto.

## The game in one paragraph

A dune buggy with a high top speed, on tracks made of snap-together pieces (ramps, jumps, banked turns,
loops) laid over noise terrain you can drive on. Tracks are generated from a seed and shared as a short
code; later an editor opens any track to tweak it or build one from nothing. Race the clock and your own
ghost. In the genre of the late-80s tile-track stunt racers, but our own name, pieces, car and look: not a
remake of any one game. Cheap physics, not the Taj Mahal.

## His words, 2026-10-31, as decisions

1. **Point-to-point first.** Closed circuits wait until the editor is near, or after it. **Changed by
   him 2026-11-24**: the generator makes circuits too, about half its seeds, in M3 right after its
   checkpoints (M3-PLAN, decisions 7-10 and M3g); circuits in the editor stay with M6.
2. **Off the road is real ground.** The buggy can leave the track; terrain is drivable with less grip.
3. **C#, Core/Game split, Godot 4.7.1 mono**, the way Overburden is built.
4. **"Better" means:** lightweight physics that doesn't drag the frame rate; multiplayer possible later,
   cheaply; ghosts and replays; seed sharing; physics feel, **tuned together in playtesting**.
5. **It need not be exact**, and better ideas are wanted.
6. He offered to model 3D primitives (ramps and so on) if generating them is too hard. See suggestion 1.

## Where Claude is speaking up

1. **Track pieces are built in code, not modelled.** A piece is a centreline curve plus a cross-section
   (flat road, banked road, half-pipe); the mesher sweeps one along the other and the same sweep gives the
   collision mesh. A ramp is a straight line that rises; a loop is a circle with a sideways offset so it
   clears itself; a banked turn is an arc with a tilted section. That is easier than modelling them, and
   it lets the generator vary them (a longer ramp, a steeper bank). **The modelling offer is worth more
   on the buggy's body and the scenery** (rocks, grandstands, a start gate). Grey boxes stand in until
   then.
2. **A ghost car and a network car are the same thing:** a car driven by a stream of snapshots instead of
   physics. Build ghosts properly and multiplayer later is mostly "send that stream over the network".
3. **Replays record what happened, not the inputs.** Re-simulating inputs needs physics that comes out
   identical on every run and machine, which Godot's doesn't promise. Recording position and rotation is
   cheap (a 60-second run is a few hundred KB before compression) and can never drift.
4. **The generator only places stunts you can make.** A cheap speed estimate along the route (a dot
   accelerating to top speed, slowed for each corner's radius) says how fast you arrive at each piece, so a
   loop never comes straight after a hairpin. Later, a headless bot can drive every generated track as
   the real proof.
5. **The road sits in a bed carved into the terrain**, and the ground blends into its edges, so going
   off-road and coming back is one surface, not a step.
6. **One key from the editor into the car at the cursor, one key back.** The edit-and-test loop is the
   whole editor.
7. **One key respawns you at the last checkpoint.** Crashing is half the game, so the retry has to be
   instant.
8. **The generator places replay cameras** beside the stunts, since it already knows where the
   interesting bits are.
9. **Pick the name before M0.** It goes into every namespace and file format, and Overburden still
   carries `minercraft:` as an alias forever because of a rename. A neutral codename is fine if the real
   name isn't ready. Ideas, none checked for clashes: *Hang Time*, *Dustline*, *Gravel Rash*, *Airtime*.
   **Taken 2026-10-31: the codename Tumbleweed**, and content IDs use `base:` rather than any name, so a
   later rename touches code only and never a saved track (CLAUDE.md non-negotiable 3). **Named
   2026-11-17: Sandrail**, after a sandrail, the stripped-down long-travel dune buggy that is exactly
   this game's car. The rename was that search-and-replace.

## What carries over from Overburden's generator

| Overburden | Here |
|---|---|
| `Hash64` (splitmix64, written-down arithmetic, never `HashCode` or unseeded `Random`) | Ported as is, with its known-answer test. The seed decides everything, so a code reproduces a track on any machine. |
| `GradientNoise` 2D/3D fBm | Ported as is. The terrain heightfield. |
| One derived seed per feature (surface, caves, relief) | Terrain, route, stunts and scenery each get their own, so turning up stunt density doesn't reroute the track. |
| Relief regions: an amplitude floor and ceiling with one rarity dial | The same thing: flat plains and hilly patches in one map, not even lumps everywhere. The ±0.6 "reachable range" lesson comes with it. |
| A pure function of seed and settings, in stages | Terrain → route → pieces → stunts → bed and scenery. Each stage reads only the stages before it. |
| Core never references Godot; dials measured by a headless bench | The generator, track model and file formats live in Core. A bench generates thousands of seeds and reports how many are valid, their length, stunt count and time. |
| Namespaced string IDs, never enums | Piece IDs in track files (`base:ramp-short`), so adding or retiring a piece never breaks an old track. |

**What's different:** a track is bounded (32 × 32 tiles to start), not an endless streamed world, so
there are no chunks, no streaming and no stage barrier. A whole track generates in milliseconds.

## Architecture

- **`Sandrail.Core`**, no Godot: the tile grid, the piece catalogue, the generator, the track file, track
  codes, the replay format, the speed estimate.
- **`Sandrail.Core.Tests`** and **`Sandrail.Bench`**, as in Overburden.
- **`Sandrail.Game`**, Godot: the car, the piece mesher, the terrain mesh, cameras, the HUD, the editor,
  ghosts and replays.

**The car's physics lives in Game, on Godot's physics (Jolt), not in Core.** It's one rigid body and four
raycasts. Writing our own rigid-body solver just to keep it out of Godot would be the Taj Mahal. The
Core rule protects what multiplayer clients have to agree on, which is the track, and that is in Core.

## The car: cheap, and tuned by playing it

A `RigidBody3D` chassis and four raycast wheels; no `VehicleBody3D`, whose tyre model is hard to tune for
arcade feel. Per wheel, per physics tick:

- **Suspension:** a ray down from the wheel mount; compression gives a spring force plus a damper on the
  compression speed, pushed up along the chassis.
- **Grip:** take the ground speed at the contact point and split it into the wheel's forward and sideways
  directions (front wheels turned by steering). Sideways force opposes the sideways slip; forward force is
  engine, brake and rolling resistance. Their combined size is capped at **surface grip × wheel load**, a
  friction circle, so a loaded wheel grips harder and a wheel in the air grips nothing.
- **No simulated wheel spin.** Wheels turn visually from speed. Wheelspin and handbrake slides are faked
  by cutting rear grip, which is a fraction of the cost and plays the same.
- **Extras, each a few lines:** air control (a little pitch and roll torque with no wheels down), speed-
  squared downforce to hold it in loops, steering that tightens less at speed, an optional anti-roll bar.

Starting numbers, there to be tuned rather than defended: 650 kg; 2.6 m wheelbase; 1.7 m track; 0.38 m
wheels; 0.35 m of travel; springs about 16 kN/m a corner (about 0.1 m of sag) with damping near a third of
critical; rear-wheel drive; top speed around 50 m/s (180 km/h), set by drag balancing the engine; grip road
1.1, dirt 0.8, grass 0.7, sand 0.6; physics at 120 Hz. **An in-game tuning panel with live sliders** is
how the playtest sessions work: drive, nudge a slider, drive again, then write the values that felt right
into the car's data file.

Cost: four raycasts and some arithmetic per car per tick. Collision is a height-field shape for the
terrain and one mesh per piece. Neither touches the frame rate.

## Pieces

A piece is a **footprint** (1 × 1 tiles, or bigger for a wide corner or a loop), its **connectors** (which
edge, at what height level), a **centreline** and a **profile**. Height is in levels (2.5 m to start). The
catalogue is data, loaded the way Overburden loads mods.

A starter set: straight; a corner in two radii; slope up and down a level; a short and a long launch ramp
with a landing ramp; a banked corner; a loop; a gap jump (a launch, empty tiles, a landing); a chicane;
start and finish gates; a checkpoint arch. Pipes, corkscrews and bridges come after.

## The generator, point-to-point first

Each stage has its own derived seed and reads only the stages before it.

1. **Terrain.** fBm heightfield with relief regions, continuous, not stepped, because it's drivable.
2. **Route.** A self-avoiding walk across the grid from a start edge to a finish edge. It's pulled
   toward the finish, backtracks out of dead ends, and refuses steps where the terrain climbs faster than
   a slope piece can. Target 40–80 tiles.
3. **Pieces.** The route becomes pieces. Straight runs merge, height follows the terrain in levels with
   slopes where it changes, and the road goes up on pillars where the ground falls away.
4. **Stunts.** Swapped in on suitable runs (a ramp needs a straight run-up, a loop needs speed),
   controlled by a density dial with a weight per stunt type, the way `ReliefRarity` works. **The speed
   estimate approves each one** (suggestion 4).
5. **Bed and scenery.** Carve and fill the terrain to the road, paint ground types by height and slope,
   scatter rocks and props off the racing line, place the replay cameras.

**Track codes.** A short string holding the generator version, the seed and the dials. When generator
output changes, the version goes up; an older code then says which version it was made for rather than
quietly producing a different track. Edited tracks are shared as their file, which is a few KB.

**Closed circuits (decision 1, changed 2026-11-24):** first written here as a loop grown by pushing its
edges out at random, closed and self-avoiding by construction. **Now walked out and walked home
instead** (M3-PLAN, M3g): the grown loop works in single tiles, and turning it into 2 × 2 and 3 × 3
corners is the same fitting problem as closing a walk, without what the walker knows about the ground.
The walk turns for home halfway round, and a search over the last few pieces closes it exactly. Then
stages 3–5 run unchanged.

## Ghosts, replays, and later multiplayer

One **snapshot stream**: about 30 a second of position, rotation, steering angle, four suspension lengths
and throttle (for the engine sound). Playback interpolates between snapshots.

- **Ghost:** your best run on this track code, played back translucent beside you.
- **Replay:** the stream played back with the chase camera, the generator's trackside cameras and a free
  camera.
- **Multiplayer, later:** the same stream sent about 20 times a second and shown a tenth of a second
  behind. Clients agree on the track by exchanging its code, not its geometry. Cars pass through each other
  to start with, ghost-race style; whether they collide is a later decision.

## Milestones

Each one gets its own plan doc before it's built, the Overburden way, and ends in a play.

- **M0, the skeleton.** The name; `git init`; the Godot project, Core, Tests and Bench; `Hash64` and
  `GradientNoise` ported with their tests; `CLAUDE.md`; `start.cmd`. **Built 2026-10-31** (WORKLOG).
- **M1, the buggy.** Flat ground with a handful of test pieces (a ramp, a bump field, a banked curve, a
  loop) from the first version of the piece mesher; the raycast car; chase camera; respawn; the tuning
  panel. **It passes when he drives it and says it feels right.** **Built 2026-11-23** (M1a-M1d, with an
  autodriver, a lap timer, wings and air control besides); the pass waits, at his word, until after the
  track editor (M5), when there are real tracks to tune on. **The car profile** (his idea, 2026-11-24), to
  tune with: [PROFILE-PLAN.md](PROFILE-PLAN.md), its decisions his.
- **M2, pieces and the track file.** The grid and catalogue in Core, the full starter set, a
  hand-written point-to-point track, start gate, checkpoints, finish, timer. **Planned 2026-11-23**:
  [M2-PLAN.md](M2-PLAN.md), its decisions his.
- **M3, the generator.** Stages 1–5, with the seed on the command line and in a menu, the bench, and
  terrain you can drive on, and since 2026-11-24 circuits for about half the seeds (M3g). **Planned
  2026-11-23**: [M3-PLAN.md](M3-PLAN.md), its decisions his. **Built and passed 2026-11-24.**
- **M4, ghosts, replays and track codes.** **Planned 2026-11-24**: [M4-PLAN.md](M4-PLAN.md), its
  decisions his. **Built and passed 2026-11-24**, with brake lights besides (his idea).
- **M5, the editor.** Grid cursor, piece palette, rotate, raise and lower, place and erase; generate
  from a code and then edit; test-drive from the cursor; validate; save and load. **Planned 2026-11-24**:
  [M5-PLAN.md](M5-PLAN.md), its decisions his (decision 7 would fold M6's circuits into it).
- **M6, closed circuits.** Circuits in the editor. The loop generator and laps moved into M3 (M3g) at
  his word, 2026-11-24.
- **Later:** multiplayer on the snapshot stream, AI opponents following the centreline, more cars, a daily
  seed.

## His ideas, not yet scheduled

- **Tuning as part of the game** (2026-11-23, while tuning the car on the F2 panel himself): *"you have
  complete freedom over everything except the engine power and wattage settings like and increase those
  as you level"*. Most of it exists: the panel is the garage, `CarSpec` is in Core with a fingerprint,
  so a ghost or a leaderboard entry can name the exact car. Open questions for when it's planned:
  limits or trade-offs so freedom doesn't mean exploits (grip at maximum, mass at minimum); leaderboards
  by power class; what levelling is earned by. **The same evening, putting the shipped car back after
  tuning his own into a 1004 kg, 243 kW machine**: *"we might make a game out of building your car
  too"*. Building as well as tuning; what building means (parts to choose? a frame to put together?)
  is still open. **The first part exists**: the wing (M1d, same day), which he asked to be
  customizable *"like we will make you earn and pay for them later somehow"*. Four wings by ID in
  `Wings`, the car's file naming which it has, and the angle and mount its own. How they're earned
  and what they cost is still his to decide. **Later that evening, putting only his steering into the
  stock car**: players *"start with the car you provided initially we will make that part of the
  learning process setting up your car via trial and error"*. So the stock car is a starting point
  meant to be improved on, not a finished tune, and finding a better setup is part of what a player
  learns; the panel, the `lap` oracle's comparison and the `track` oracle's "can it finish" are the
  tools that exist for it now.
- **An autodriver** (same day: *"how do you feel about an autodriver?"*). **Built 2026-11-23** on the
  corner lap, on a minimum-curvature racing line that keeps the wheels on the road (his addition), with
  a lap oracle; worklog, that date. **Its aggression** (the share of grip it plans for, 50-100%) is on -
  and + since the same day, at his wish to tune it. Still to come: opponents, track checking, the demo. It follows a centreline,
  steering at a point ahead and planning speed from each corner's radius and bank, through the same
  `DriverInput` a player uses. Its first job would be **lap times for tuning** (an oracle that laps
  station 6, so a setup can be compared by numbers); then AI opponents, whose caution setting is their
  difficulty; then **proof that a generated track can be finished** (PLAN suggestion 4); and a title-screen
  demo.

## Decisions that are his

1. ~~**The name**, or a codename, before M0.~~ **Codename Tumbleweed, 2026-10-31; named Sandrail, 2026-11-17.**
2. ~~**Ghosts before the editor** (M4 before M5)?~~ **Yes, 2026-11-24** (M4-PLAN decision 1, taken as
   recommended): ghosts make generated tracks worth replaying before there's an editor.
3. **Where Godot lives:** share MinerCraft's `Godot_v4.7.1-stable_mono_win64\`, or copy it into this
   folder, gitignored. Recommended: copy, so the two projects can move to different Godot versions apart.
   **Taken as recommended at M0**; `start.cmd` falls back to MinerCraft's copy.
4. **Map and tile size:** 32 × 32 tiles of 20 m (a 640 m square) to start. Play will say.
5. **Later, not now:** whether multiplayer cars collide.

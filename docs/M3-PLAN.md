# M3 — The generator

> Planned 2026-11-23, before any code. Split M3a–M3f. Read [PLAN.md](PLAN.md) first, especially "The
> generator, point-to-point first", "What carries over from Overburden's generator" and suggestions 4
> and 5, and [M2-PLAN.md](M2-PLAN.md)'s model, which is what the generator writes. Every number here is
> a starting point labelled as one; the decisions at the end are his.

M2 made tracks out of pieces, one written by hand. M3 makes them **from a seed**: terrain you can drive
on, a route across it from one edge to another, the route made into pieces that follow the ground, stunts
swapped in where the car can make them, checkpoints, and the road set into a bed in the terrain. Its
output is an ordinary track (M2c's file, with its terrain), so everything M2 built (the validation, the
timer, the autodriver, the `track` oracle) works on a generated track unchanged.

Six things ship: **terrain** (M3a), **the route** (M3b), **stunts and checkpoints** (M3c), **the bed and
the ground** (M3d), **seeds in the game** (M3e), and **a play** (M3f), which is his: whether the tracks
it makes are ones he wants to drive.

Out of scope, on purpose: track codes, ghosts and replays, and with them the replay cameras (M4); the
editor (M5); closed circuits (M6); pillars and bridges; scenery beyond rocks; sound.

**M2e is still his.** The shakedown play (are the pieces the right size and shape?) can happen whenever
he likes. The generator reads the piece catalogue, so a piece he wants changed changes every generated
track with it.

---

## What M2 already gives

- **The grid** (32 × 32 tiles of 20 m, levels of 2.5 m) and **the catalogue**: fifteen pieces as data,
  each held to its way out to a millimetre, turned and mirrored.
- **`Track`**, its file and **`Lay`**, which refuses a broken track and says why, and finds the route
  start to finish. A generator that writes a track `Lay` refuses has a bug, and the bench will count them.
- **The speed hints** on the stunt pieces (short jump 85–95 km/h, long 110–118, loop 90–105), measured.
- **`RacingLine.SpeedProfile`**, which already slows the car for every corner and brakes it in time,
  and **the autodriver and the `track` oracle**, which drive a track start to finish headless and say
  exactly where they couldn't.
- **`Hash64` and `GradientNoise`**, ported from Overburden with their tests, and **the bench**, whose
  one arm so far measured the noise.

---

## Eight facts that shape it

1. **The noise reaches about ±0.55, not ±1.** The bench measured 4-octave fBm (2026-10-31): min −0.554,
   max 0.547, and 98% of samples within ±0.37. So an amplitude of *A* gives hills of about ±0.37 *A*
   nearly everywhere, and every terrain dial is calibrated against that, measured, not against the
   documented range (CLAUDE.md non-negotiable 5).
2. **The road climbs at most a level every two tiles**: 2.5 m in 40 m, 3.6° on average, and only on a
   slope piece. Where the ground along the route climbs faster than that, the road can't follow it
   and runs in a cutting or on an embankment. So the terrain under a route has to be gentle, and the
   route walker has to look at the ground, as PLAN's stage 2 says: it refuses steps where the terrain
   climbs faster than a slope piece can.
3. **The car climbs off the road only so steep.** By the friction circle alone, sand's 0.6 grip on the
   rear wheels carrying about half the car gives about 17°; dirt and grass a little more. Ground steeper
   than that is a wall on sand. Whether that's a hazard or a mistake is a decision (1); either way M3a
   measures it with an oracle rather than trusting the arithmetic.
4. **A route can't be walked tile by tile.** Corners are 2 × 2 and 3 × 3 tiles (M2 fact 1), so a walk
   that turns on any tile makes turns no piece can build. The walk places **pieces**, each checked
   against the tiles already taken, the way a person lays them in the editor.
5. **A stunt can only replace what has its footprint and its way out**, or swapping it in reshapes the
   route, and the route has its own seed (non-negotiable 4: turning the stunt dial mustn't reroute the
   track). The short jump is 5 tiles in a line, level: five straights. The long jump is 7 tiles in a line
   and comes out a level up: five straights and a slope up. **The loop is 2 × 5 and comes out a tile to
   the side, and nothing in the starter set does that**, so as things stand a generated track can't
   have a loop.
6. **Terrain you drive on is a height field.** Godot's `HeightMapShape3D` works on Jolt. The 640 m grid
   at a sample a metre is 410,000 heights, about 50 ms of noise (fact 1's bench figure), so it needs no
   cleverness. **The car can leave the grid**: the ground has to go on past it to somewhere sensible
   before Jolt's world boundary 1000 m out (decision 6).
7. **One body has one surface today.** The buggy reads grip from its collider's `surface` meta. Ground
   painted by height and slope (sand in the hollows, dirt on the slopes, say) means one terrain body
   with many surfaces, so the buggy needs to look the surface up by position in a Core map when its ray
   hits the terrain. A few lines in `SurfaceOf`, and the oracles on the flat plain mustn't notice.
8. **A track on terrain isn't the same track without its terrain.** A generated track saved as a file
   (to edit in M5, or to share before M4's codes) has to bring its ground with it. Its terrain is a pure
   function of a seed and a few dials, so the file needs only those: **format 2** adds a terrain block,
   and format 1 files (the shakedown) keep the flat plain.

---

## The model, written down

**Settings** (`GenSettings`): the seed and the dials, each with a default. Terrain: amplitude, feature
size, relief rarity. Route: length target, how often it turns, the start and finish edges. Stunts:
density and a weight per stunt. Checkpoints: how many. **Each stage takes its own seed**,
`Hash64.Mix(seed ^ stage)`, so turning one stage's dial can't reshape what an earlier stage made
(non-negotiable 4).

**Stage 1, terrain** (`Terrain`, M3a): heights at a metre over the ground, fBm with relief regions as in
Overburden: an amplitude floor and ceiling with one rarity dial, so a map has flat plains and hilly
patches, not even lumps everywhere. Continuous, not stepped, because it's driven on.

**PLAN's stages 2 and 3, route and pieces, become one here**, because the walk places pieces (fact 4);
stunts and the bed move up a number each.

**Stage 2, the route** (`RouteWalker`, M3b): from a start gate on one edge toward a finish on another,
a walk of straights and corners (30 and 50 m, left and right), each placed only on free tiles. It's
pulled toward the finish, prefers ground it can follow (fact 2), backs out of dead ends, and stops when
it reaches the finish edge within the length target. **Then levels**: each piece's level follows the
ground under it, a pair of straights becomes a slope where the ground rises or falls a level, and where
the ground moves faster than the road can, the road runs high or low and stage 5 makes good the
difference. **Banks**: a corner the speed estimate says is taken fast becomes its banked twin, same
footprint.

**Stage 3, stunts and checkpoints** (`StuntPlanner`, M3c): runs that fit a stunt (fact 5) are found and
swapped by density, with a weight per stunt. **The speed estimate approves each one** (PLAN suggestion
4): `SpeedProfile`'s corners and braking, plus a pass that accelerates the car from the start by its own
drive force and drag, say how fast it can arrive at the lip; a jump whose hint that speed can't reach,
or can't brake down to, isn't placed. Checkpoints go in as straights become checkpoint gates, spaced by
distance along the route.

**Stage 4, the bed and the ground** (M3d): the terrain carved down and built up to the road under each
piece, blended into the ground over a few metres either side (PLAN suggestion 5), so leaving the road and
coming back is one surface, not a step; raised road on an embankment that meets the ground. Ground
painted by height and slope. Rocks scattered off the road and well off the racing line.

**The output** is a `Track` (M2c, format 2) whose `Lay` comes back clean, and its terrain. **The
proof** is two-tier: the bench lays thousands of seeds and counts the ones `Lay` refuses (which must be
none) and the stunts the estimate turned down; the `track` oracle drives a sample start to finish in the
real physics, which is what PLAN suggestion 4 meant by "a headless bot can drive every generated track".

---

## Decisions

Numbered so they can be answered by number; the recommendation is first. **All six taken as
recommended, 2026-11-23**: *"Yes, as recommended"*.

1. **Terrain: gentle nearly everywhere, with a few hilly patches you can't climb on sand.** Hills of a
   few metres over a few hundred, so the road follows the ground most of the way and leaving it is a
   detour, not a dead end; relief regions (one rarity dial) put steeper ground in a few places, where it
   punishes a car that goes off. The alternative is gentle everywhere, which is safer and flatter; or
   dramatic everywhere, which means cuttings and embankments on most of the route.
2. **Add an S-bend piece, 2 × 5 tiles, that comes out a tile to the side**: the loop's footprint and way
   out as plain road (fact 5). The route walker uses it as road, and the stunt stage swaps a loop in for
   it, so generated tracks get loops without the stunt dial rerouting anything. It's also a corner type
   station 6 hasn't got, a long sweeping jink. The alternative is no loops on generated tracks until
   there's another way, or letting the route stage place loops itself, which ties the two seeds together.
3. **Raised road sits on an embankment, not pillars**, for M3: M2's earthworks, now meeting the
   terrain and blended into it. Pillars come with bridges. The alternative is pillars now, which need a
   model of their own and an under-road the car can drive through.
4. **Length: 1 to 2 km, a minute to a minute and a half**, about the shakedown's 1.28 km at its upper
   end. PLAN said 40–80 tiles; in pieces that's roughly the same thing. The alternative is shorter (a
   sprint, 30–45 s) or longer.
5. **Seeds in the game: `--seed=` on the command line, and a small menu on Esc** with the seed, "new
   random seed", and "generate", which builds the track where you are. The full title screen waits for
   when there's more than one thing to choose. The alternative is the command line only, for now.
6. **The ground goes on past the grid, flattening out**, a 1024 m square of terrain with the grid in
   its middle, so the car can go off the grid and come back. The alternative is a fence or a drop at the
   grid's edge.

---

## M3a — terrain

**Built 2026-11-23**, as below; 13 tests, worklog that date. The dials, from the bench: hills 50 m,
plains 3 m, a 200 m feature, 3 octaves at gain 0.45, 500 m regions, rarity 0.3. **The car climbs 32.3°
on road, 25.3° on dirt, 22.5° on grass and 19.7° on sand** (the `climb` oracle; fact 3 guessed 17° for
sand). **The ground is painted by height, not slope**: dirt on the hilltops, grass in the hollows, sand
between, steep faces included, because steep ground painted dirt climbed to 25° and the walls of decision
1 vanished. Half the seeds have some ground the car can't climb, at most about 1% of the grid.

- **Core:** `GenSettings` and `Terrain` (stage 1): heights at a metre over 1024 m, fBm and relief
  regions, a pure function of the seed and the terrain dials; slope and the surface at any point.
- **Bench `--terrain`:** over many seeds, the heights it reaches, how steep it gets and where, and how
  much of the grid a slope piece could follow (fact 2): the measurements the dials are set from.
- **Game:** `--terrain=<seed>` builds the ground: a mesh (a vertex every 2 m is plenty to look at) and a
  `HeightMapShape3D` at a metre, painted, replacing the plain; the buggy looks its grip up by position
  on it (fact 7).
- **A `climb` oracle:** the car driven straight at slopes of rising steepness on each surface, the
  steepest it makes the top of (fact 3).
- **Checks:** the same seed gives the same heights to the last bit across runs (the known-answer test,
  as `Hash64`'s); the flat-plain oracles unchanged; screenshots of three seeds.

## M3b — the route

**Built 2026-11-23**, as below, with `--seed=N` in the game already (the menu and saving are still
M3e's); 15 tests, worklog that date. Departures: **the road keeps out of the ground altogether** until M3d
carves it, since a buried road is sand under the tyres; **a track carries its ground as format 2** now,
since levels below the plain need it; and **the route line caps the speed at crests**, which the
autodriver needed on generated tracks' humps. A thousand seeds lay clean; the autodriver finishes seven of
eight at 70%, the rest running wide off raised road with sheer sides, M3d's to fix.

- **Core:** `RouteWalker` (stage 2): the walk in pieces, levels following the ground, slopes, banked
  corners where fast; the S-bend piece if decision 2 is yes. Writes a `Track` with a start, a finish
  and no stunts yet.
- **Bench `--generate`:** thousands of seeds; how many `Lay` refuses (must be none), how many dead-end
  walks it backed out of, the lengths, the time per track.
- **Checks:** every route in the bench's sweep lays clean; a test that the same seed gives the same
  track; a test that the stunt dial at zero and at full gives the same route; screenshots of routes on
  their terrain.

## M3c — stunts and checkpoints

- **Core:** the speed estimate (the acceleration pass beside `SpeedProfile`'s), `StuntPlanner` (stage
  3) with its density dial and weights, checkpoints by distance.
- **The bench** reports stunts per track by kind, and how many the estimate turned down and why.
- **Checks:** the route is the same with stunts on and off (fact 5); every stunt in the sweep is one
  the estimate approved; the `track` oracle drives ten seeds start to finish, and where one fails it says
  where, which is either a stunt the estimate shouldn't have approved or a lesson for the autodriver.

## M3d — the bed and the ground

**Begun 2026-11-23, ahead of M3c, at his word**: after driving generated tracks he wanted raised road's
sides to be banks a car can drive back up, and chose 25° (climbable from rest) over 45°. **Built so far:
the bed** (`Bed`): the ground set 0.2 m under the road, 25° banks beside raised road and 25° cuttings
beside sunk road, painted dirt; with cuttings, the level chooser lets the road into hills again (a metre
of cut 1.5 of fill), and the tallest embankments halved. The `bank` oracle climbs a track's three
tallest banks from rest. **Still to do here:** rocks off the racing line.

- **Core:** stage 4: the terrain carved and filled to the road under each piece, with a blend either
  side; the ground painted; rocks scattered off the racing line.
- **Game:** the bed in the terrain mesh and collider; rocks as grey boxes with colliders.
- **Checks:** along every route in a sweep, the ground meets the road's edge to within a few
  centimetres; off the edge it's drivable; screenshots from the car, leaving the road and coming back.

## M3e — seeds in the game

- **Game:** `--seed=` builds a generated track on its terrain; the Esc menu (decision 5); the track file
  saves a generated track with its terrain block (fact 8, format 2), and loads one back.
- **Checks:** a generated track saved and loaded is the same track; the shakedown, format 1, still
  loads on the plain; the `track` oracle runs with `--seed=`.

## M3f — the play

His, on generated tracks: **whether they're tracks he wants to drive.** Too twisty or too plain, too many
stunts or too few, the hills, the length. Each verdict goes into the worklog verbatim, and the dials move
by them. M3 passes on his word.

---

## Order and why

Terrain comes first because the route walker looks at the ground, and its dials have to be measured
before anything is built on them (fact 1). The route comes before stunts because stunts are swapped into
a route that already exists (fact 5). The bed comes after both, because it carves to the road, which
needs its levels. The game's menu comes last because it's the generator's front door, not its insides.
Each slice ends runnable and committed, and the bench grows an arm with each.

## Levers, named now so nobody has to rediscover them

Terrain amplitude, feature size and relief rarity; how steep counts as a wall; the walk's pull toward
the finish, its turn rate and length target; how fast a corner has to be taken before it's banked; the
stunt density and weights; how much margin the speed estimate keeps on a hint; checkpoint spacing; the
bed's blend width and the embankment's batter; rock density and how far from the line they stay; the
size of the ground beyond the grid.

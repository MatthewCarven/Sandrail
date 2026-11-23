# M2 — Pieces and the track file

> Planned 2026-11-23, before any code. Split M2a–M2e. Read [PLAN.md](PLAN.md) first, especially
> "Pieces" and suggestion 1 (pieces built in code, a centreline swept with a cross-section), and
> [M1-PLAN.md](M1-PLAN.md)'s M1c, which built the mesher this milestone stands on. Every number here is
> a starting point labelled as one; the decisions at the end are his.

M1 built a car and a test ground of hand-chained road. M2 makes road into **pieces on a grid**: a
catalogue of snap-together pieces in Core, a track file that places them, and a first hand-written
point-to-point track with a start gate, checkpoints, a finish and a timer. It's the model that M3's
generator writes and M5's editor edits, so it's built to be both written by a program and edited by a
person, even though in M2 Claude writes the one track by hand.

Five things ship: **the grid and the catalogue** (M2a), **the starter set of pieces** (M2b), **the track
file and a first track** (M2c), **racing it**: gates, timer, respawn at the last checkpoint, and the
autodriver finishing it (M2d), and **a play** (M2e), which is his: whether the pieces are the right size
and shape. Not car tuning: that waits for the editor, at his word.

Out of scope, on purpose: terrain (M3; the plain stays flat), the generator and track codes (M3, M4),
ghosts (M4), the editor's interface (M5; M2 builds the model it edits), closed circuits (M6),
diagonal pieces, pipes, corkscrews, bridges and pillars, scenery, sound.

---

## What M1 already gives

- **Centrelines** (`Line`, `Pitch`, `Turn` with bank, `Loop`), **profiles** (`Profile.Slab`) and **the
  sweep**, which gives the render mesh and the collision mesh from the same arrays. A piece's road is a
  sequence of these.
- **`Chain`**, which lays centrelines end to end, and the test that each end frame is where the next
  starts to within a millimetre. M1c wrote down that this was M2's connector rule arriving early.
- **`RacingLine`** (samples, curvature, bank, a speed plan, and the minimum-curvature line inside the
  wheels' room) and **the autodriver** that follows it. Built for the closed corner lap; a line can
  also be open.
- **`LapTimer`**, which times laps line to line and knows a cut from a clean lap by progress along the
  line, and **`StuckWatch`**, the auto-respawn's rule.
- **Station 6's corners**, flat to 35° bank and 18 to 60 m radius, which he asked for as the shortlist of
  what corner pieces should be.

---

## Eight facts that shape it

1. **A corner on one tile is a hairpin.** A tile is 20 m and the road 10 m wide. A 90° corner that enters
   one edge and leaves the next, both at their middles, has a radius of 10 m: 37 km/h at the car's
   grip, for a car that tops out at 177. On a square grid the corner radius is 20n − 10 for a corner
   n tiles square: **30 m on 2 × 2, 50 m on 3 × 3**. Flat, those take 65 and 84 km/h; banked 20°, 97
   and 125. Those sit inside station 6's range.
2. **On a four-way grid, pieces meet at the middles of tile edges, level and square.** Every connector
   faces north, east, south or west, at a whole height level, with the road flat across (no bank) and
   level along (no pitch). So any two pieces that share an edge meet exactly, and a bank or a slope
   eases in and out inside its own piece. The M1c test (every end frame is where the next start is,
   to a millimetre) becomes the catalogue's test, for every piece, rotated four ways and mirrored.
3. **A crest launches the car above √(gR).** A slope that climbs one level (2.5 m) in one tile has crest
   arcs of 41 m radius, so the car leaves the ground at 72 km/h. Over two tiles the arcs are 161 m and it
   flies only above 143 km/h. Whether a slope should throw the car is a decision (2), not an accident.
4. **The M1c loop doesn't fit the grid as it is.** It's driven with the wheels straight because its
   sideways offset grows evenly with distance round, and the price is that the road arrives at 11.6°
   off the loop's own heading and leaves 15 m to the side. On the grid it must arrive and leave along an
   axis, one tile (20 m) to the side. So the loop piece carries its own approach: a gentle S inside its
   footprint that puts the car on the skew and takes it off again, or a re-derived loop whose road
   twists to make the skew. M2b chooses by measurement: the loop oracle, wheels straight, from the
   piece's entry.
5. **The ground beside the road is drivable** (his decision 2), so a road can't stop a shortcut.
   **Checkpoints must be passed in order**, each a gate across the road, and a missed one means the run
   doesn't count until it's gone back for. `LapTimer`'s clean test, progress along the line, does the
   same job for a lap; checkpoints are the point-to-point form.
6. **A gap jump connects across empty tiles.** A launch ramp's lip doesn't meet a piece on the next tile;
   it meets a landing ramp some tiles on, in line, with nothing in between. So a connector has a kind:
   **road**, which must meet a matching road connector on the next tile, or **lip**, which must meet a
   **landing** connector within a few tiles straight ahead, the tiles between left empty. Validation
   knows both.
7. **A track names its pieces by ID, and the catalogue says what they are.** Like a car's wing, so
   changing a piece's shape changes every track that uses it. A piece is never renamed (an alias if it
   must be); a track file carries a format number; an unknown ID is refused by name, never dropped.
8. **Raised road needs something under it.** The plain is flat until M3, so a piece above level 0 sits on
   earthworks: its slab runs down to the ground, as the test ground's jump and plateau do. Pillars come
   with terrain.

---

## The model, written down

**The grid.** `TrackGrid`, 32 × 32 tiles of 20 m (PLAN decision 4), height in **levels of 2.5 m**,
level 0 lifted `Lift` (5 cm) above the plain as the test ground is. A tile is `(column, row)`; its
middle is at `(20·column + 10, level · 2.5, 20·row + 10)` in the track's frame.

**A piece** (`PieceDef`, in the catalogue):
- `id`, namespaced (`base:corner-30`), and a name for the palette.
- **Footprint:** which tiles it covers, in its own frame (`[[0,0],[0,1],[1,1]]`), and which are
  left empty under a jump.
- **Connectors:** each a tile edge (tile, side), a level and a kind (road, lip, landing). One is the
  piece's way in; a straight-through piece has one way out; a start gate has only a way out and a
  finish only a way in.
- **Path:** the road as segments, data rather than code, so the catalogue can be loaded like content
  and later added to: `{"line": 20}`, `{"pitch": {"angle": 8, "radius": 50}}`,
  `{"turn": {"angle": 90, "radius": 30, "bank": 20}}`, `{"loop": {"foot": 18, "top": 7}}`. The path
  starts at the way in and must end at the way out, which the catalogue's test checks to a millimetre.
- **Profile** (the 10 m slab unless it says otherwise), **surface** (road), and **what's on it**: a gate
  (start, checkpoint, finish) and its arch.
- **A speed hint:** the range the piece is meant to be taken at, as the stations have (*"80–95 km/h at
  the lip lands on the down-ramp"*). The HUD shows it, the autodriver plans from it where the line's
  own curvature can't tell it (a jump, a loop), and M3's speed estimate approves stunts with it (PLAN
  suggestion 4).

**A placement** (in a track): `id`, the tile of its footprint's origin, a level, a rotation (quarter
turns clockwise seen from above) and a mirror flag, so a left corner is a mirrored right one and the
catalogue doesn't need both.

**A track** (`Track`, the file's contents): a format number, a name, the grid's size, and the
placements. Validation, in words, as `CarSpec.Problems` does: a piece off the grid; two pieces on one
tile; an unknown ID; a road connector with nothing matching on the next tile; a lip with no landing in
line; not exactly one start and one finish; no route from the start's way out to the finish's way in;
a checkpoint off that route. **The route** is the chain of pieces from start to finish, and its
centrelines in order are what the racing line, the timer and the autodriver read.

**The file.** JSON, written to be read and hand-edited, as the car's is: `content/base/tracks/*.json`
for the game's own, `user://tracks/` for ones made later. Unknown fields refused.

---

## Decisions

Numbered so they can be answered by number; the recommendation is first.

1. **Corners: 30 m on 2 × 2 and 50 m on 3 × 3, each flat and banked 20°, and no one-tile hairpin in the
   starter set** (fact 1). The alternative is to add the 10 m hairpin for a slow, tight section; at 37
   km/h it's a different kind of corner from anything on station 6.
2. **Slopes climb one level over two tiles, so they only throw the car near top speed** (fact 3), and a
   one-tile **hump** that does throw it at 72 km/h comes later as a stunt piece. The alternative is
   one-tile slopes, which make every hill a jump.
3. **Checkpoints are pieces placed on the track** (a straight with an arch), passed in order. **R and the
   auto-respawn put the car back at the last checkpoint passed**, stopped and facing along the road,
   with the clock running (PLAN suggestion 7: the retry is instant and the cost is the time). The
   alternative is a checkpoint every so many pieces, automatically; placed ones are what the editor
   and generator will want anyway.
4. **The clock starts on the first throttle at the start gate and stops on crossing the finish**, as the
   lap timer's standing start does, with a split at each checkpoint against the best run so far. A
   run that missed a checkpoint isn't counted.
5. **The test ground stays as it is**, the oracles' home, and the first track is its own place:
   `--track=base:shakedown` (a shakedown is a first run to see what's loose). The test ground stays
   the default until there's more than one track.
6. **Grid and levels as PLAN has them**: 32 × 32 tiles of 20 m, levels of 2.5 m. M1c's kicker rose 3 m,
   about one level, which is a sign the level is about right.

---

## M2a — the grid and the catalogue

- **Core:** `TrackGrid`, `PieceDef` (footprint, connectors, path, profile, surface, gate, speed hint),
  `Placement` and its world frames (rotation, mirror), and the catalogue loaded from
  `content/base/pieces/*.json` with unknown fields refused. A path becomes centrelines through
  `Chain`, so the sweep and the racing line take pieces as they took the test ground.
- **One piece to start with**, a straight, so the plumbing is tested before the set is drawn.
- **Checks:** the catalogue loads; every piece's path ends at its declared way out to within a millimetre
  in all four rotations and mirrored; a placement's connectors land on the tile edges they claim; a
  piece's footprint and its connectors agree; a misspelt field or unknown kind is refused by name.

## M2b — the starter set

- **The pieces** (PLAN's starter set, sized by the decisions): straight; corners of 30 m and 50 m, flat
  and banked; a slope up a level (and down, as the same piece the other way); a short and a long
  launch ramp and a landing ramp, for a gap jump over empty tiles; the loop, re-derived for the grid
  (fact 4); a chicane that jinks inside its 2 × 1 footprint and comes back to the middle; start gate,
  finish gate and checkpoint, each a straight with an arch.
- **A showroom**, `--showroom`: every piece in a row on the plain, with a station at each, for
  screenshots and for driving one piece at a time.
- **Oracles for the stunt pieces**, as M1c's drove the test ground's: the loop goes through with the
  wheels straight at its hint speed and comes out on its exit tile; the jump at its hint speed lands on
  its landing ramp; the slope's crest throws the car only above the speed fact 3 says.
- **Checks:** every piece sweeps closed with outward normals (M1c's test, over the catalogue); the
  stunt oracles pass at the hints written in the pieces; screenshots of the showroom.

## M2c — the track file and the first track

- **Core:** `Track` with load, save and the validation above; the route, start to finish, as an ordered
  list of placements and their centrelines. Tests: a round trip changes nothing; each refusal on a
  small track written to break that rule; the route of a valid track runs start to finish through
  every piece once.
- **The shakedown**, `content/base/tracks/shakedown.json`, written by hand: every piece in the set at
  least once, both corner sizes both ways, a gap jump, the loop, a climb onto a raised section and back
  down, two checkpoints, on the 32 × 32 grid.
- **Game:** `--track=` builds a track from its file: each placement swept into a mesh and a collider (one
  static body per piece, tagged with its surface), raised pieces on earthworks (fact 8), the gates'
  arches as grey boxes.
- **Checks:** the shakedown validates; it loads, builds and is driven headless; how long it takes to
  build; screenshots from above and from the car.

## M2d — racing it

- **Core:** `RaceTimer`, the point-to-point form of `LapTimer`: start on the first throttle at the
  start gate, splits at checkpoints passed in order, a finish, and whether the run counts. Tests: a run
  through every gate counts; a skipped checkpoint doesn't, until it's gone back for; crossing a gate
  backwards doesn't count as passing it.
- **Respawn at the last checkpoint** (decision 3) for R and the auto-respawn, on a track; at the station,
  as now, on the test ground.
- **The HUD:** the run's time, checkpoint n of N with its split against the best, the finish and the
  best kept per track.
- **The autodriver on a track:** an open racing line over the route, with the pieces' speed hints for
  the jump and the loop. **The `track` oracle**: the autodriver drives the shakedown start to finish and
  prints its time, its splits, where it left the road and whether it finished. This is the first
  "proof that a track can be finished" (PLAN suggestion 4), which M3 will need for every generated one.
- **Checks:** the `track` oracle finishes the shakedown, or says exactly where it couldn't; the timer
  tests; respawn at a checkpoint seen in a screenshot.

## M2e — the play

His, on the shakedown: **whether the pieces are the right size and shape**. Corners too tight or too
open, the slope's crest, the jump's distance, the loop. Not the car: its tuning waits for the editor,
at his word (2026-11-23). Each verdict goes into the worklog verbatim. M2 passes on his word that the
pieces are right to build tracks from.

---

## Order and why

The grid and catalogue come first because every later slice places pieces through them, and one
straight is enough to prove the plumbing before twelve pieces are drawn. The pieces come before the
track because the first track is written to use all of them, and the stunt pieces have to be measured
(the loop above all, fact 4) before a track can depend on them. Racing comes last because the timer,
the checkpoints and the autodriver all read the route, which needs a valid track to exist. Each slice
ends runnable and committed.

## Levers, named now so nobody has to rediscover them

The corner radii (decision 1) and the bank's ease; the slope's length (decision 2) and its crest
radius; the level height; the loop's shape (foot radius, top radius, sharpness) and how it carries its
skew (fact 4); how far a lip may look for its landing; the road's width and lip radius; the earthworks'
depth; where each gate's plane sits and how wide it is beyond the road.

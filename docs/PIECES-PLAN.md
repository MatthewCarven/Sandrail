# More pieces: crossings first, then a shortlist

> Planned 2026-11-26, before any code, at his ask: *"more track pieces"*. Read [PLAN.md](PLAN.md) first
> ("Pieces": *"Pipes, corkscrews and bridges come after"*), and [M2-PLAN.md](M2-PLAN.md)'s model (grid,
> piece, placement, track, validation), which every piece here is written in. Every number is a starting
> point labelled as one; the decisions at the end are his.

The starter set is seventeen pieces, and M2, M3 and M5 each left bridges, pillars, crossings, pipes,
corkscrews and diagonals out on purpose. Now they're asked for. **Crossings come first**, because of what he
said about building his own tracks: he *"can never get the heights to work out correctly like i want to go
under sometimes"*. The answer turns out not to be heights at all (fact 1). **Then a shortlist** of other
pieces with what each costs, for him to pick from.

What ships, if he says yes to all of it: **crossings** (X1, the model; X2, in the game and the editor), **a
play** (X3, his: can he build a track that goes under itself?), **the pieces he picks from the shortlist**
(P1 onward, one slice each), and **a last play**.

Out of scope, on purpose: diagonal pieces, full pipes, crossroads on one level, road that changes width
(each costed below, none cheap); scenery; the generator using any of this, unless he says otherwise
(decision 4).

---

## What's there already

- **The grid and the catalogue** (M2): pieces as data, each held to its way out to a millimetre, turned
  and mirrored; a corner on n × n tiles has a radius of 20n − 10.
- **`Lay`**, which says every way a track is unsound, and the **editor** (M5), which lays track onto the
  road's open end, test-drives with Tab and checks the whole track after every change in under 1.5 ms.
- **A thin road**: the loop's slab is 0.5 m deep, where every other raised piece stands on earthworks down
  to the ground (`PlacedPiece.DepthM`, `DepthTo`).
- **The sweep takes any closed cross-section** (`Profile`), and the step types are line, pitch, turn (with
  bank) and loop.
- **The pieces oracle** in the showroom, and the `track` oracle, which drives any track start to finish.

---

## Ten facts that shape it

1. **A tile holds one piece, and that's why he can't go under.** `TrackLayer.Cover` refuses two pieces on a
   tile whatever their heights (*"both cover (14, 13)"*), and the editor's `Fits` says *"is in the way"*.
   No arrangement of slopes gets one road over another. Heights only matter once that rule allows it.
2. **A raised piece is a solid bank.** Its slab runs down to the ground, and on generated ground the bed
   fills 25° banks beside it (M3d). Nothing can pass under it as it's built. A road that goes over needs a
   thin deck on legs.
3. **Two levels clear, not one.** The car is about 2 m tall, wing and all, with 0.35 m of suspension travel,
   and a deck slab is 0.5 m. One level (2.5 m) leaves 2.0 m under the deck, and the car hits it on any bump.
   Two levels leave 4.5 m. So where roads cross they're two levels apart. With today's slopes (a level every
   two tiles) the upper road climbs for four tiles, crosses, and comes down for four: nine tiles. On
   generated ground the lower road can dip instead, since levels below the plain exist there. The flat plain
   can't be dug.
4. **Clearance is measured on the road, not from the levels.** A banked corner's outer edge stands up to
   3.4 m above its level (10 m of road at 20°), and a slope changes height inside the piece. So the gap is
   measured between the lower road's highest surface over the shared tile, edges included, and the upper
   road's underside, not between placement levels.
5. **Legs at the tile seams clear a straight road below, but not a corner.** A straight runs down the middle
   of its tiles, 10 m from either seam, so a leg at a seam stands 5 m off its edge. Corners and S-bends cut
   across tiles: the 30 m corner passes 1.7 m from the point where its four tiles meet. So legs are checked
   against the roads below and left out where they'd stand on one.
6. **Ways in are found by tile and side** (`Connect`'s `entries`). With two pieces on a tile, both could be
   entered from the same side at different levels, so the key needs the level too.
7. **The ground copes with crossings as it is.** Terrain is one height a metre, and under a crossing it's
   the lower road's bed. The upper road isn't ground, so a height field is enough. But the bed lets *"the
   highest bed win"* where roads overlap, so a deck must not lay bed under itself, or it buries the road
   below.
8. **A track's fingerprint includes each of its pieces, written out in full** (`TrackFingerprint`
   serialises every `PieceDef` it uses, leaving out only nulls). A new field on `PieceDef` that writes a
   default (a `false`, a `0`) would change every track's fingerprint, and his ghosts and profile sheets with
   it. So any new field on a piece is null unless that piece uses it.
9. **The showroom lays pieces in file-name order, and the pieces oracle drives it.** A new file called
   `bridge.json` would come before `checkpoint.json` and move every station after it. The editor's palette
   already keeps the starter order (`PaletteOrder`) and puts new pieces after it. The showroom must do the
   same.
10. **The generator names its pieces by ID**, so a new piece never reaches a seed's track unless the
    generator is changed. Changing it is a new generator version (`TheGeneratorVersionPinsItsTracks` pins
    seeds 1-10; seed 5 also ships as `gen-5v1.json`). After a version bump, `--seed=3` is another track
    and `3v1` only lives on as a saved file, unless version 1's code is kept generating beside version 2.

---

## The model, written down: crossings

**Stacking.** A tile may hold more than one piece, one above another, when for every pair that shares a
tile:
- the upper one's underside (its road surface less the 0.5 m slab) is at least **`ClearanceM`, 4 m**, above
  the lower one's highest road surface over that tile, edges and banks included (fact 4). On a tile a piece
  covers without road on it (the inside of a 50 m corner), its height there is taken from its levels;
- neither has a loop, which is taller than any clearance.

Nothing else changes: two pieces at the same height on a tile are refused as today, in today's words.

**Pillars come by themselves.** A piece with anything under it, on any of its tiles, stands on pillars: a
0.5 m slab, legs, and no bed or banks under it. Every other piece is built exactly as today. Nothing new
goes in the track file: whether a piece is on pillars follows from where the pieces are, so **a track with
no crossings is the same track, file, build and fingerprint**.

**Legs.** Posts 1 m square, under both edges of the deck, 1 m outside the road, every 20 m along it from its
way in, down to the ground (the plain, or the bedded terrain). A leg that would stand within 1 m of another
piece's road below is left out (fact 5), and the deck spans over. Legs have colliders: a car that leaves the
road can hit one.

**Connecting** finds a way in by tile, side and level (fact 6). For a track with no stacking it finds
exactly what it does today.

**The editor.** `TrackEditor.Fits` uses the same rule as `Lay`. A refused piece in hand says why in
numbers: *"needs 2 levels over the corner-30 at (4, 6) below, and it's 1"*, or *"0.7 m short of clearing
its banked edge"*. On a tile with two pieces, Delete and the right click take away the one nearest the
cursor's level, and the status line names both (*"the straight, level 2, over the corner-30, level 0"*).
`--drive-from=` takes an optional level, `col,row,level`.

**The bed** skips pieces on pillars (fact 7). Their legs stand on whatever the bed and the ground make
below.

**Racing** doesn't change. A route is a list of pieces, and the racing line's nearest-point searches are 3D
and start from where the car was, so a car on the lower road doesn't snap to the road above it.

---

## The shortlist: other pieces, and what each costs

**Cheap: a piece file, a measurement, a showroom station.** No Core code. Each is our own shape, sized by
the grid rather than borrowed from any one game.

| Piece | What it is | Starting numbers | Cost |
|---|---|---|---|
| **Steep slopes**, up and down | Two levels over three tiles | 9.5° at the steepest; crest arcs of 180 m, gentler than today's slope (161 m), so they throw the car later, not sooner | Two files. **A crossing in seven tiles, not nine** (3 + 1 + 3) |
| **70 m corners**, flat and banked | 90° on 4 × 4 | About 99 km/h flat, 148 banked 20°: a fast sweeper, which the set hasn't got | Two files |
| **U-turn hairpin**, flat and banked | 180° on 3 × 2, a 20 m radius, out beside in | About 53 km/h flat, 79 banked 20°: a slow corner, and a way to double back in little room | Two files. Its way out faces backward, which nothing else does yet: tested in `Lay` and the editor's next |
| **Hump** | Up and down in one or two tiles, level at both ends | Throws the car from about 65-75 km/h (M2 decision 2 named this *"a stunt piece, later"*) | A file and a hint, measured |
| **Tabletop** | Launch, flat top, down-ramp, no gap | A forgiving jump: short lands on the top, right lands on the down-ramp | A file and a hint, measured |
| **Longer gap jump** | A launch over three or four empty tiles onto the landing | 130+ km/h | A file and a hint, measured; may need its own landing |
| **Dirt and sand straights** | The road's shape, another surface | Dirt grip 0.8, sand 0.6 against road 1.1 | Two files, if the road's look reads the surface (to check) |
| **Steep-banked corner** | The 30 m corner banked 40° | With road grip of 1.1, a 40° bank holds the car past 300 km/h in theory, far beyond its top speed, so it's a flat-out corner. Its outer edge stands 6.4 m up | A file, measured |

**Moderate: Core code and tests.**

| Piece | What it is | Cost |
|---|---|---|
| **Climbing corner** | A 90° corner that climbs a level as it turns | A turn step with a rise, eased like the bank. Lets a road climb in a corner, so crossings fit in less room. About half a day with tests |
| **Corkscrew** | A barrel roll: the road turns once round an axis above it, the car upside down halfway | A new centreline, eased in and out like the loop. Enters and leaves level and square, in a line, so it fits the grid as a straight does. The autodriver holds it like the loop. About a day with measuring; the risk is how the suspension takes the roll coming in |
| **Half-pipe**, straight and corner | A U cross-section to ride up the walls | The sweep needs a cross-section that changes along the piece, from flat road to U and back, so it can meet ordinary road at both ends. That change is the cost. The profile meter must not count the walls as off the road, and the bed stamps wider. One to two days |
| **Jumping over a road** | A gap jump whose gap has a road crossing under it | Free-ish after crossings: let a piece into a gap if its road is low enough under the flight. Measured, since the arc must clear |

**Expensive: not now.**

- **Diagonals.** 45° roads need connectors at tile corners or a second grid, and with them every piece,
  the editor's cursor and the generator. A milestone of its own.
- **Full pipes.** The half-pipe's changing cross-section, plus the car holding to a roof and a camera
  inside a tube.
- **A crossroads on one level** (a figure-of-eight crossing itself flat). A piece driven twice, with two
  ways in and two ways out, where the route, the profile's per-piece rows and the editor's open ends all
  assume one of each. Bridges do the same job.
- **Road that changes width.** The sweep has no taper, so a narrow piece would meet the road with a step.

---

## Pinned: nothing he has changes under him

- **Every existing track file, its fingerprint, and so his ghosts and profile sheets, stay exactly as they
  are.** Crossings add no field to the track file or to any shipped piece (facts 8 and 9). A track with no
  stacking lays to the same route and builds the same bodies. New tests pin the shakedown's and
  `gen-5v1`'s fingerprints. **His saved tracks are fingerprinted from a scratch copy before the first change
  and after every slice**, and must match.
- **Seeds 1-10 stay as they are.** The generator isn't touched unless he says so (decision 4), and
  `TheGeneratorVersionPinsItsTracks` holds it. If the generator does take new pieces, it's version 2, with
  `gen-5v1` shipped as it is.
- **The oracles stay byte for byte**, `pieces` included. The showroom keeps the starter set where it is and
  lays new pieces after it, built after it. New pieces' trials go in an oracle of their own, **`pieces2`**,
  so `pieces` never grows. The check is `pwsh tools/oracles.ps1` from a copy of today's commit against each
  slice.
- **The palette's first nine keys stay where they are** (fact 9; `PaletteOrder` already does this).
- **The stock car isn't touched.**

---

## Decisions

Numbered so they can be answered by number; the recommendation is first. **All five taken 2026-11-26**:
decisions 1, 4 and 5 as recommended; for 2, **every cheap piece** (steep slopes, 70 m corners and the U-turn,
the hump and the tabletop, surfaces and the steep bank); for 3, **the climbing corner and the corkscrew**, the
half-pipe left for later.

1. **How a road gets over another: pillars by themselves.** Any piece goes up on pillars where another road
   runs under it, two levels clear (the model above). Nothing new to place, and any piece can be the bridge:
   a corner, a slope, a gate. The alternatives: **bridge pieces**, a deck straight on pillars (a corner
   later) that alone may cross a road, simpler to check but only straight over a road; or **a pillars
   switch** on each piece, chosen by him and kept in the track file (format 4), so a road could stand on
   pillars over a valley with nothing under it.
2. **Which cheap pieces, after crossings.** Recommended: **the steep slopes** with crossings (they make a
   crossing two tiles shorter each side), then **70 m corners and the U-turn hairpin**, then **the hump and
   the tabletop**. Surfaces and the steep-banked corner if he wants them.
3. **Which pieces with new code, after those.** Recommended: **the climbing corner** (it helps crossings
   fit), then **the corkscrew**. The half-pipe last or later: it costs the most for one piece.
4. **The generator stays version 1, and the new pieces are for the editor.** Seeds keep their tracks and
   nothing needs keeping alive beside them. The alternative: **generator version 2 in this milestone**, its
   routes allowed to cross themselves and to use the new pieces, with version 1's code kept generating so
   every v1 code still makes its track (fact 10). That's a milestone's worth of walker and level work on its
   own.
5. **A crossing track ships with the game**: `base:crossover`, a short circuit that goes under itself once,
   so the race, the autodriver, the `track` oracle and Profile every car on every track all drive a
   crossing. It shows up in Load a track and in his profile sheets as a new track. The alternative: the
   showroom's crossing station only.

---

## X1: crossings in Core

**Built 2026-11-26**, as below; 13 tests, worklog that date. Departures: **a tile shared at one height is still
refused in M2's words** (*"both cover"*), and one stacked but short says by how much and how many levels more; **an
open end** in the editor is one whose next tile is empty or has only road two levels away, so laying onto the road's
end goes over a road as it goes anywhere; **the bench grew `--fingerprints`**, which prints every track's fingerprint
as the game works it out: his 31 saved tracks (a scratch copy) and the game's own came out the same before and after,
line for line.

- **Core:** stacking in `TrackLayer.Cover` (`ClearanceM`, measured on the road over each shared tile);
  pillars worked out in `Lay` (`LaidTrack.OnPillars`); legs as Core data (where each stands and how tall),
  left out over roads below; `Connect` by tile, side and level; `Bed` skipping pieces on pillars;
  `TrackEditor.Fits`, `PieceAt` and `Erase` by level, with refusals in numbers.
- **Checks:** a crossing two levels clear lays clean and its route runs under and over; one level clear is
  refused saying by how much; a banked corner's edge under a deck counts; a leg over a corner's road is left
  out; a loop never stacks; ways in at two levels on one tile connect to the right ones; every existing
  track lays to the same route, and the shakedown's and `gen-5v1`'s fingerprints are pinned; seeds 1-10
  unchanged; his saved tracks' fingerprints from a scratch copy unchanged.

## X2: crossings in the game

**Built 2026-11-26**, as below; worklog that date. Departures: **the showroom lays anything new in the first free
spot**, a tile clear of the rest, because the crossing's block wrapped into a band off the grid; the starter set is
laid as it always was, pinned by a test. **The crossover's first layout taught a lesson**: a steep descent straight
into a 30 m corner put the autodriver off the road, since it plans braking as on the flat, so the crossover has two
flat tiles after each slope. Worth knowing when he builds with them. The steep slope throws the car from about 137 km/h
where the old slope does from 118.

- **Game:** pieces on pillars built as a thin slab with legs (grey boxes with colliders); the editor's hand
  green or red with the clearance reason, picking and erasing by level, the status line naming what's above
  and below; the steep slopes, if decision 2 says so; a crossing station in the showroom after the starter
  set; `base:crossover`, if decision 5 says so.
- **Checks:** the `track` oracle drives `base:crossover` start to finish at 80%, under and over; screenshots
  of a crossing from the car (under and over) and from the editor; a crossing laid in the editor by keys
  alone; `dotnet test`, the headless check, and `pwsh tools/oracles.ps1` byte for byte against today's
  commit.

## X3: the play

His: **can he build a track that goes under itself, and do the heights work out now?** Each verdict goes
into the worklog verbatim. Crossings pass on his word.

**Begun 2026-11-26** on the crossover, which he raced himself and set the profile on: *"its great to showcase the
feature like leave it in with the default tracks like its a prime example of a short course with some feature to it"*.
So `base:crossover` stays with the game's own tracks, as it shipped. Building one of his own is still to come.

## P1: 70 m corners and the U-turn

**Built 2026-11-27**: four piece files, no Core code; 6 tests, worklog that date. **Measured, against the table's
starting numbers**: `pieces2` holds each at a speed, steered down its middle, and steps up until the car leaves the
road. The 70 m corner holds at 79 km/h and not at 89; banked, at 99 and not at 108. The U-turn holds at 50 and not at
55; banked, at 59 and not at 64. **The banked figures are far under the table's 148 and 79**: a bank eases in from
flat over half the corner and out over the other half (M2's ease, 0.5), so at its entry a banked corner grips no
better than its flat twin, and the full bank's figure is true only at its middle. The ease is the lever if he wants
banks to count for more. Departures: **the showroom lays each slice's pieces after the last's**, in the order they were
added (`Showroom.Added`), so no station moves under `pieces2`; and **a block that finds no room facing north lies on
its side**, facing east, since the grid is filling up. The palette lists the new pieces after the starter set.

## P2: the hump and the tabletop

**Built 2026-11-27**: two piece files, no Core code; 5 tests, worklog that date. **The hump** is two tiles, four
50 m arcs up 2.02 m and down again, level at both ends (the table's "one or two tiles"): two because a one-tile hump
on arcs that throw the car near 72 km/h stands only 0.6 m tall. **The tabletop** is three tiles: a 15° ramp to a lip 3
m up (a 3 m arc, so it throws the car at any speed worth the name), a flat top 16 m long, a 10° ramp down and a
run-out. Both are stunts with a hint, so the autodriver takes them down the middle at it. **Measured** by `pieces2`,
held to the top (the crest, the lip) and coasted from there, hands off in the air: the hump keeps its wheels down at 67
km/h and throws the car from about 72 (M2's figure); at 76-86 it lands at its foot, nose down 16-19°, and past that
beyond the piece, upright at every speed to 133 (2 s and 52 m in the air). **Hint 75-90.** The tabletop lands on its
top below about 70 km/h (nose down 10-13°, the short one's thump), on its down-ramp from 72 to about 80 (nose down
15-18° on a 10° ramp), at its foot to 87 and on its run-out at 93, upright every time. **Hint 72-80.** The autodriver
drives a sprint over both clean at 70, 80 and 90%. The tabletop is the first block in the showroom that lies on its
side. Levers: the hump's height and its arcs (a nose-down landing comes from the crest's turn, v/R), the tabletop's lip
angle, top and down-ramp.

## P1 onward: the shortlist, a slice each

In the order decisions 2 and 3 give. Each slice: the piece files; Core support with tests where it needs
code; a showroom station after the others; the palette; trials in `pieces2`; screenshots; the tests, the
headless check and the oracles unchanged; a worklog entry; `CLAUDE.md` brought up to date; committed and
pushed. **A last play** when the list he picked is built.

---

## Order and why

Crossings first, because they're what he asked for in his own words and because the rule they change
(a tile, one piece) is under everything else. A jump over a road and the climbing corner both lean on it.
Core before the game, because the rule wants testing headless before anything draws it, and because the
pins (fingerprints, seeds, the route of every existing track) are Core's to prove. The cheap pieces next:
each is a file and a measurement, and they fill gaps in speed (a fast sweeper, a slow hairpin) that his
tracks will use at once. Code-heavy pieces last, most useful first.

## Levers, named now so nobody has to rediscover them

`ClearanceM` (4 m) and the deck's thickness (0.5 m); the legs' size, spacing, how far outside the road
they stand and how close to a road below they may come; whether a piece on pillars over nothing ever makes
sense (decision 1's switch); the steep slopes' length; each corner's bank and ease; the hump's height and
length; the tabletop's top; the gap jump's lip height and pitch; the corkscrew's radius and how it eases
in; the half-pipe's wall height and how long it takes to rise from flat.

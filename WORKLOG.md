# Worklog

Newest first. What happened and why; findings worth keeping.

## 2026-11-24 — M3f: he tries the menu

His first report of M3f's play, after trying the Esc menu: *"Load works, new random track works :-{D"*.
That's loading a saved track and New random seed, the two things the headless probe couldn't press. No
word on the rocks yet. M3f goes on as he drives.

## 2026-11-24 — M3e: the Esc menu, and saved tracks

Still *"Keep going"*. Built as M3-PLAN's M3e (decision 5). **Esc opens the menu** and stops the game, the
race clock with it, since the clock counts physics ticks. It's keyboard only: his pad's Start button is his
wide lock. It shows what's being driven ("Seed 5: a sprint of 2.1 km"), **the seed** in a box, and these
buttons:

- **Generate**, or Enter: the scene starts again on that seed. The flags that choose where to drive are
  replaced; his others (the car, the caution, the panels) are kept, through a static the new scene reads.
- **New random seed**: a number from 1 to 999999 from the clock. It only picks the seed; the track is still
  the seed's.
- **Save this track**: the generated track being driven, as its file, to
  `user://tracks/gen-<seed>.json` (format 2, or 3 for a circuit, with its ground block).
- **Resume** and **Quit**.

**`--track=` loads a saved one on its own ground**: the terrain made again from the ground block, the bed
and rocks laid again from it and the pieces, as the generator laid them. The file holds the pieces, so a
saved track keeps them even if the generator changes. **Seed 5 saved and loaded drives to the same 1:25.19
at 70%, the same splits, both jumps on the down-ramp.** The shakedown, format 1, still loads on the plain.
A test saves seed 5 and circuit 8 and checks the reload is the same file, the same bedded heights and the
same rocks. Tried with a temporary probe, since a headless run can't press Esc: the menu opened, saved
seed 5 and generated seed 7 in place. Tests 287.

**M3 is built** (M3a-M3g). M3f, his play on generated tracks, is what passes it.

## 2026-11-24 — Rocks: M3d finished

*"Keep going"*. The last of M3d: **rocks off the road** (`Generation/Rocks.cs`), grey boxes with colliders
in the game (`World/RockView.cs`, driven as road). About 120 per kilometre of road are offered, 12 to 70 m
out from its edge, thinning with distance; each is 0.8 to 3 m across and sunk 30% into the ground. **None
within 12 m of any road's edge**, so the racing line and a car that only runs wide never meet one. **None on
the bed or within 4 m of it** (the road's banks and cuttings, a jump's gap, a loop's pad), so a car that
leaves the road can always drive back up, his ask of 2026-11-23. They come from the ground's stage seed and
the laid track, like the bed, so they aren't in the track file and the pinned seeds stay byte for byte.
Every draw is made whether a rock comes of it or not, so one turned away moves no other.

A median of **116 rocks a track** (58 to 221 over 200 seeds), at no cost the bench can see. The autodriver's
ten seeds at 70% come out to the hundredth as they did before: its spins on seeds 1 and 9 stay clear of
them. Tests 286.

## 2026-11-24 — He drives the circuits

After M3g: *"Please and thanks Claude the up and down to the tracks is excellent you can really get a feel
for the compression"*. The ups and downs are the levels stage's slopes, two tiles each, rising or falling a
level (2.5 m) where the ground does, with their crests held under take-off speed. What he feels is the
suspension loading and unloading over them. That's a lever kept as it is: the slope's shape, the levels'
cut weight (1.5) and the crest share (0.8) all feed it, so any change to those should be checked against
this verdict.

## 2026-11-24 — M3g: circuits

*"Go on to M3g"*. Built as M3-PLAN's M3g. **First, seeds 1, 3, 5 and 7 were pinned as M3c made them**
(`TheSprintsHeDroveStayPut`, a hash of each track file with its jumps and checkpoints). They must stay
sprints, and they're unchanged to the last byte; seed 5 is the one he called the most fun he's had in a
driving game.

**The kind** comes from `GenStage.Kind`: a circuit when the draw is at least 1 less `CircuitShare` (0.5).
Seeds 2, 4, 6, 8 and 10 are circuits. A sprint is walked exactly as before: the kind's draw never touches
the route's seed.

**The walk** (`RouteWalker.WalkCircuit`): a start-finish line (`base:start-finish`, a straight with a white
arch, open both ends) at least 8 tiles in from every edge, and a straight after it. The tile behind the line
is held back for the way home. The walk goes out with a gentle push away for the first 45% of the shortest
lap, then home: gently while road is still owed, hard once it runs long. A step that leaves home out of reach
over free tiles is refused. From every place the walk reaches, once home is close enough and the lap long
enough, **a search tries every run of up to four pieces**, straights first, for one that ends behind the
line facing along it with the lap 0.8 to 1.5 km. None, and the walk carries on, backing out of dead ends as a
sprint's does. **Levels** come back round: the level search runs once from each starting level, held to
end there, and keeps the cheapest lap (its earth is worked out once; sprints get the same numbers as before).
**Banks** are judged on a flying lap: the estimate goes round once, then decides with the speed it came
round at. **Checkpoints** go a fifth of a lap apart at most (a quarter gave a median of two; it's still
two, since plain straights are scarce on a lap). Stunts are judged from a standing start on the first lap,
with a closed speed plan, so a loop near the line brakes for the next lap's first corner.

**The file**: `"laps": 3` makes a track a circuit, in **format 3**, so a build that has never heard of laps
refuses it. `Lay` finds the route from the line round and back into it, which must be every piece. A sprint
with a start-finish line, or a circuit with a start or finish gate, is refused, saying so.

**Racing it**: the timer's gates are the line (as its start), each lap's checkpoints and the line again
(as a checkpoint, the last time as the finish), so `RaceTimer` didn't change. The car starts 4 m past the
line: crossing it at the start read as the end of a lap passed out of order. The board says the lap ("LAP 2
of 3", the last lap's time) and keeps a best run and a best lap, his and the autodriver's apart. Crossing
the line flashes the lap's time against the best lap. The `track` oracle counts progress round the closed
line lap by lap and prints each lap's time.

**The bench, a thousand seeds**: 485 circuits and 515 sprints, none refused, none that failed to close.
Laps 801 to 1500 m, median 1037. 56 ms a track at p50. All circuits at once (200 seeds at `--circuits=1`):
every one closed, 48 ms each at p50, a median of 1 stunt (none on 95) and 2 checkpoints a lap.

**The autodriver on the five test circuits, stock car**:

| Seed | 70% | 60% / 80% |
|---|---|---|
| 4 | 2:06.85 (44.04, 41.41, 41.40) | |
| 8 | 2:04.08 (42.74, 40.67, 40.67), its jump every lap on the down-ramp | |
| 10 | 3:26.85 (1:10.19, 1:08.33, 1:08.33), a loop and a jump every lap | |
| 2 | spins on the straight before its jump and drops into the gap at 33 km/h | 60%: 2:06.19 clean; 80%: as at 70% |
| 6 | spins on the banked 50 m left at (13, 10) | 60%: 2:36.20, 80%: 3:01.13, spinning there every lap |

The misses are the autodriver, not the circuits. Seed 2's jump comes straight after a corner, so the racing
line has to swing to the road's middle for the launch in a few metres, and at 70% that spins the car. A
jump needing a few straights before it would cure that, but it would move seed 5's jumps, so it waits for
his word. Seed 6 is the banked-corner spin seeds 1 and 9 showed in M3c.

## 2026-11-24 — He drives seed 5 with its jumps

After M3c, on seed 5 with its two new short jumps and three checkpoints: *"Claude this is the most fun i've
had in a driving game for a long time like :-{D"*. The first verdict on a generated track with stunts, and
the bar for M3f's play: whatever M3g and the rest of M3 change, seed 5 as it is now should stay this good.

## 2026-11-24 — M3c: stunts and checkpoints

*"Go on to M3c"*, then *"please and thanks Claude"*. Built as M3-PLAN's M3c, with the departures written
there. **Before any of it, seeds 1-8 were pinned** (`TodaysRoutesStayPut`, a hash of each track file):
with the stunt dial and checkpoints at 0 they must come out as M3d made them, and they do.

**`StuntPlanner`** (`Generation/Stunts.cs`, stage 3, its own seed): walking the route, each place a stunt
fits is offered one at the density (0.8), the kind drawn by weight. The short jump is a launch ramp, a tile
of gap and a landing a level up: five tiles in a line that come out where they went in. The long jump is
seven that come out a level up. The loop takes an S-bend's place. **Two findings shaped where a jump goes.**
On the first five straights of a run, nearly every jump came straight off a corner too slowly to fly: so
it goes as late in its run as it fits, the rest being its run-up. And runs of five flat straights were
rare, the levels having made slopes of most of them, so a jump may go over slopes too, as long as its five
or seven tiles come out at the right level; that doubled the places one fits. Each stunt keeps 60 m of
ordinary road from the last.

**`SpeedEstimate`** (`Driving/`) approves each one: the route's middle (`CentreLine`, the route line
without the minimum curvature, which costs 100 ms a time) with the stunts' hints and the crests' caps,
its speed plan at 80% for the stock car, met by full throttle from the start (the engine's force held to
the grip on half the car's weight, less drag, rolling and slope; it makes 0-100 km/h in 5.2 s, as the
`straight` oracle does). A stunt the car can't arrive at the bottom of its hint for is *too slow*; one the
plan must brake through, *must brake*. The first refused comes out and the rest are judged again. **The
estimate's car is pinned in code**, so his tunes can't change a seed. **Checkpoints**: the first plain
straight once 400 m of road has gone by since the last gate, none in the last 200 m.

**The bed grew two stamps.** A jump's gap is ground at the launch's foot, as the plain is under the
shakedown's jumps (a hill there would be in the way). A loop stands on a flat pad at its foot's height, its
road stamped straight down; the first pad had holes where the loop climbs straight up and its stamps
bunched together, so those stamps reach a metre along.

**The bench, a thousand seeds at 0.8**: none refused; a median of **1 stunt a track**, p90 3, max 6, and
224 tracks with none; 795 short jumps, 235 long, 263 loops; turned down 937 too slow and 268 braking
through; checkpoints p50 2 (0 to 4); 68 ms a track at p50 (62 before). At density 1 the median is 2.

**The autodriver, seeds 1-10 on the stock car.** At 70% **all ten finish**, every checkpoint in order:
1:29.13, 1:03.11, 1:00.61, 1:15.43, 1:25.19, 1:06.24, 58.44, 49.25, 1:00.66, 1:28.59. All 14 stunts it met
came off: nine short jumps from the lip at 85 km/h and one long at 108, every one landing on its
down-ramp, and four loops through at 96 km/h. Seeds 1 and 9 each spin once on a banked 30 m corner. At 80%
seven finish: seed 4 sticks at the raised banked left at (28, 12) as it did before M3c, seed 8 spins past
checkpoint 1 outside its arch, and seed 9 spins off at (27, 3).

**The checkpoints caught a shortcut.** Seed 1 at 70% first came back *stuck at the finish, checkpoint 1
missed*. With the stunts and checkpoints off it "finished" in 55.44 s, as it did in M3d's eight-seed run.
It spins at (21, 0), and a lost autodriver looked for its line over the whole route: the nearest was the
road back past, 20 m away and 300 m on. So M3d's "all eight finish at 70%" was seven and a shortcut.
**Now a lost car is looked for first within 100 m back and 150 m on of where it was, and everywhere only
if it's more than 40 m from that** (a respawn). Seed 1 then finishes properly in 1:29.13.

**Seed 5 keeps its road and gains two short jumps**, at (0, 20) and (9, 31), and three checkpoints; at 70%
it's 1:25.19, both jumps on the down-ramp. `--seed=5 --stunts=0 --checkpoints=0` is seed 5 as he first
drove it. The sixteen oracles on the committed car (`4f918acd`) read as before. The `bank` oracle called
seed 1's loop a 23 m bank, its road being in the air, so it now skips stunts. Tests 268.

## 2026-11-24 — Circuits planned: M3g

*"Ok do any of the tracks make a complete circuit i want about 50% of them too if it isn't too difficult to
pull off what do you thing Claude plan with me please?"*. None did: every generated track runs from a
start gate on one edge to a finish on the opposite one, and PLAN had circuits in M6, after the editor
(his decision 1 of 2026-10-31). Claude's view: a medium job, about M3b's size, because most of it is
built (a closed racing line the autodriver laps at station 6, the lap timer with its quarters, a race
timer that's only gates in order, and terrain, levels, banks and the bed, which work on any route). The
hard part is closing the loop exactly on the grid with 2 × 2 and 3 × 3 corners. Four decisions, **taken
as recommended**: *"Yes, as recommended"*. Circuits come right after M3c (they need its checkpoints)
and before M3e; a run is 3 laps timed in total, with lap times and a best lap; a lap is 0.8 to 1.5 km;
the seed decides, half and half, by a dial.

Written up as **M3g** in M3-PLAN (facts, model, checks), with PLAN's decision 1, its generator section
and M6 changed to match. Two things the planning turned up:

- **PLAN's way of making a circuit is replaced.** Growing a loop by pushing its edges out closes by
  construction, but in single tiles, so fitting corners into it is the same problem as closing a walk,
  and it would lose what the walker does with the ground. Circuits are walked out and walked home: the
  walk turns for home halfway round, and a search over the last few pieces closes it exactly.
- **Seed 5 would have become a circuit.** The kind is drawn from a stage seed of its own
  (`GenStage.Kind`, `0x7E44A1_0005`), and with "a circuit when the draw is under the share" seeds 1, 3, 5
  and 7 come out circuits (515 of the first 1000 are). Either half is as good as the other, so the rule is
  "at least 1 − share", which keeps seed 5 the sprint he's learning; seeds 2, 4, 6 and 8 become circuits.
  Sprints will be walked exactly as now, pinned by a test on seed 5 before any of it is built.

## 2026-11-24 — Hold R to start again

To learn seed 5 in whole runs: *"fully reset the timer when you press the lap reset button"*. His pad has
no restart button (it's Backspace only since the fix below), and its respawn button puts the car back at
the last checkpoint with the clock running. Offered three ways, he chose **hold to restart**: a tap of R
still goes back to the last checkpoint at once, clock running, and **held for a second it starts the run
again**, as Backspace does: at the start gate, the clock at zero, waiting for the throttle. Holding on
doesn't restart it twice, and with no run on there's nothing to hold for (R already does that).

While there, the two ticks a restart waits before arming the new run now read no gates, so the jump back
to the start can't count as passing one or finish the old run on the way.

Tried headless on seed 5 with the autodriver and R pressed in code: a tap at 5 s put it back at the start
with the clock running on (5.00 s); a hold from 8 s restarted it at 1.00 s held, the clock back to 0.00,
and nothing more while it stayed held. **Seed 5 has no checkpoints yet** (they're M3c), so on it a tap of R
was always the start with the clock running on: a restart with the time so far added.

**So, his answer: a tap that lands on the start zeroes the clock too.** Before the first checkpoint, R
and the auto-respawn now start the run again, the clock at zero and waiting for the throttle, rather than
counting the start as a checkpoint (M2d). Past one, a tap still goes back to it with the clock running,
and only then is there anything to hold for. Tried the same way: on seed 5 a tap at 5 s zeroed the clock;
on the shakedown (the stock car, since his tune's autodriver leaves the road there at about 20 s), past
checkpoint 1 at 22.17, a tap at 30 s put it back at checkpoint 1 with the clock running on, and a hold
from 35 s restarted it with the clock at zero.

## 2026-11-23 — Seed 5, and his wide lock restarting the run

Driving the banks: *"Claude this is a really fun level seed=5 i need to redo my gamepad calibration uh when
i pressed my hard steer button it has become bound to reset"*. **Seed 5** is 2.11 km, 59 pieces, 20 slopes
and 9 banked corners, with the tallest bank the `bank` oracle climbed (5.5 m, outside a banked 30 m corner
at (2, 15)): the first generated track he's called fun.

**The reset was Claude's, not his calibration.** His saved pad layout (`--bindpad`, 2026-11-22) puts wide
lock on button 6. M2d bound restart (Backspace) to the pad's Start button by default, and Start is button 6.
A layout only replaces the defaults of the controls it knows, and restart came after it, so pressing wide
lock also started the run again. **Now a button a saved layout uses is the layout's**: every other action's
default on it comes off when the layout loads. On his pad, restart is Backspace only until a layout binds
it. Not tried on his pad from here, since headless runs see no pad.

## 2026-11-23 — M3d begun: the road's bed, with banks you can climb back up

His choice, between banks the car climbs from a standstill and steeper 45° ones that need a run at them:
*"Banks at 25°, climbable"*. That's M3d's bed, so it came before M3c.

**`Bed`** (`Generation/Bed.cs`), a pure function of the terrain and the track: the route's road walked
every 0.4 m, each cross-section stamping the ground. **Under the road** the ground is set 0.2 m below the
road's surface, tilt and all, hidden by the slab. **Beside it**, at d metres from the nearer edge, never
lower than the edge less d tan 25° (the bank) and never higher than the edge plus d tan 25° (the cutting).
Ground it moved more than 0.25 m, and the bed itself, is painted **dirt**, which climbs 25.3° from rest.
How far a bank reaches allows for ground that keeps falling away at up to 15°, so a bank doesn't end in a
step. Loops are skipped: their road stands on nothing. `Terrain` gained a bedded copy (its heights and
paint) that remembers the field it came from; the generator lays the bed after banking corners, so
`--seed=` tracks come bedded and `TrackView`'s slabs reach only just below them.

**With cuttings, the road may go into hills again**: the level chooser's price for a metre of road below
the ground came down from 1000 metres of fill (M3b's ban) to 1.5. Over 300 seeds the tallest embankment
halved, **p50 3.3 m and max 15.8 m** (from 6.5 and 30.6), and cuttings came back, p50 2.5 m and max 13.6
m. The bed adds about 35 ms to a track (p50 62 ms now).

**The `bank` oracle** (sixteen now): on a generated track, from rest 4 m beyond the foot of each of its
three tallest banks, full throttle at the road. **All nine climbed** on seeds 1, 2 and 5: 3.5 to 5.5 m
tall (the tallest stand on the outside of banked corners), onto the road in 3.2 to 5.0 s.

**The autodriver on seeds 1 to 8 now**, stock car:

```
70%   all eight finish: 55.44, 1:00.15, 57.35, 1:12.96, 1:20.50, 1:03.96, 56.03, 46.26
80%   six finish (1, 2, 5, 6, 7, 8): seeds 1 and 8 ran off for 16 and 18 s, climbed back and finished
      (1:28.01, 1:13.39); seed 3 came to the finish off the road, beside its arch, and stopped; seed 4
      stuck at the raised banked left at (28, 12)
```

**Seed 6 first read "fell off the world"** with its wheels never off the road: the oracle called a car
lost 5 m below the old plain, and seed 6's road runs through a hollow deeper than that. Both the oracle and
the auto-respawn (whose mark was 30 m below the plain, with ground on the bench reaching 27 m down) now
measure from the ground's own lowest point. Seed 6 then finished at both 70% and 80%.

**Checked:**
- 257 tests, 5 of them new: the ground just under the road everywhere along three tracks; beside it,
  meeting the road and falling away no steeper than the bank or than the ground already was; the banks
  painted dirt; the ground far from the road untouched; the same track getting the same bed.
- All sixteen oracles on the stock car (`4f918acd`): `bank` as above, and the other fifteen exactly as
  after M3b.
- Screenshots: seed 3's route from above with its dirt banks, and seed 1 from the car, the ground meeting
  the road's edge instead of a drop.

## 2026-11-23 — He drives generated tracks

After driving `--seed=` tracks: *"the raised road doesn't bother me much altho i wish its side bank was
more approaching a 45 degree or similar incline/decline where the surrounding road is flat. The raised
road is definitely more realistic but it would be nice if a player who has gone off the track can get back
on again"*.

So the raised road stays, and **its sheer sides become banks a car can drive back up**, which is M3d's
embankment (M3-PLAN decision 3) and the same fix the autodriver's misses need (the M3b entry, finding 4).
What angle is his to say. The `climb` oracle's figures set the trade-off: **at 45° nothing climbs from a
standstill** (the stock car's best is 32.3° on road, 25.3° on dirt), so a 45° bank needs a run at it,
about 25 km/h for a 2.5 m bank and 50 km/h for a 10 m one; **at about 25° in dirt** it climbs from rest
anywhere, and looks gentler.

## 2026-11-23 — M3b: the route

His go-ahead: *"Go on to M3b"*.

**The S-bend** (`base:s-bend`, decision 2): 2 × 5 tiles, coming out a tile to the side, the loop's
footprint and way out as plain road, so M3c's stunts can swap a loop in for it without moving the route.
Two opposite arcs of 22.62° at 130 m radius do it exactly (the chicane's 5-12-13 triangle again). It's
in the showroom too. The shakedown test now names M2's fifteen pieces, since the S-bend came after it.

**A track can carry its ground** (M3-PLAN fact 8): `Track.Ground`, a seed and the terrain's dials, written
as one line of format 2; with ground, pieces may go down to level -20. A track on the plain still saves
as format 1, so the shakedown's file is unchanged to the byte.

**`TrackGenerator`** (`Generation/`), stage 2:
- **The walk**, in pieces (fact 4): from a start gate on a random edge, straights in runs of one to
  three, 30 m and 50 m corners and S-bends both ways, and the odd chicane, weighted by chance from the
  route's seed. It's pulled away from the finish while the road still owes length and hard toward it
  once it's running long; it never takes a step after which no free path reaches the finish edge; a
  step with nowhere to go is undone and the next choice tried, and a walk past 4,000 placements is begun
  again from another start. It ends at the opposite edge heading out, between 1 and 2 km.
- **Levels**, all at once: the cheapest levels in earth moved, a pair of straights becoming a slope
  wherever the road should climb or fall.
- **Banks**: a corner the car arrives at over 1.3 times its flat speed (a steady 3.5 m/s² from each
  corner's exit, the autodriver's grip) becomes its banked twin.

**The bench's `--generate` arm**, a thousand seeds:

```
refused by Lay           0          no route found  0
time a track             p50 26 ms, p90 191 ms, max 606 ms (the slow ones backed out of a lot)
length                   p10 1,121 m, p50 1,585 m, p90 2,060 m, max 2,125 m
pieces                   p50 46;  slopes p50 11;  banked corners p50 7
attempts                 p50 1, p90 2, max 6
worst fill               p50 6.5 m, p90 14.9 m, max 30.6 m
worst cut                p50 0.0 m, max 0.3 m
```

Left and right turns come out even. **The tall fills are where a route crosses a hollow on corners**,
which can't change level; M3d's embankments are where that's dealt with.

**Four findings, each from driving it:**
1. **A corner over a hilltop looked flat to the walker**, which compared only the ground at a step's way in
   and way out. Seed 2's road went 9 m into a hill. It now looks at the ground across the step's whole
   footprint.
2. **A buried road is sand under the tyres.** The levels first charged six metres of fill for one of cut.
   The autodriver drove five generated tracks and slid off a corner on all five: where the road dips
   below the ground, the terrain's sand stands on it. **Until M3d carves the ground away, the road keeps
   out of it** (a metre of cut now costs a thousand of fill), and the worst cut over a thousand seeds is 0.3 m.
3. **The autodriver didn't know about crests.** Its speed plan reads only how the road bends sideways,
   so a slope up then a slope down, a hump, threw the car at 100 km/h and it landed sideways. **On ordinary
   road the route line now holds the car under 0.8 of a crest's take-off speed**, √(g R cos θ), the share
   where M2b measured the slope throwing it; stunts keep their hints. The shakedown's slope is held to 114
   km/h, which moves its time from 56.70 to 56.76 s and nothing else.
4. **What's left is the autodriver running wide out of a corner off raised road.** Seed 1, logged: out of
   a banked 30 m left at 77 km/h, sliding, the slip guard cutting the throttle as it should, it drifts past
   the road's edge and drops off its 1.5 m sheer side, then sits against it. On the test ground that's a
   trip onto the sand; here the road's side is a wall. **M3d's sloping embankments are the fix**, and M3c's
   checkpoints give R somewhere nearer to go.

**The `track` oracle on generated tracks** (stock car, `--seed=`):

```
80%   seeds 2, 3, 6 finish (58.03, 57.45, 1:01.34), clean; 1, 4, 5, 7, 8 run wide off raised road
70%   seeds 1, 2, 3, 4, 6, 7, 8 finish (1:09.41, 1:00.11, 58.33, 1:12.70, 1:03.55, 55.99, 46.42), all with
      the wheels never off the road; seed 5 slides off a raised banked left-hander
```

**Game:** `--seed=N` makes the ground and the route and builds both: the terrain, every piece with its
slab down to the ground (`PlacedPiece.DepthTo`), the timer from the start gate to the finish (no
checkpoints until M3c; the board says "next: the finish"), P for the autodriver. About 0.2 s to make and
0.3 s to build. `--oracle=track --seed=N` drives one.

**Checked:**
- 252 tests, 10 of them new: thirty seeds laying clean, the same seed giving the same track and another a
  different one, 1 to 2 km, edge to edge, carrying its ground, the road out of the ground, every piece
  kind used, refusals, and the crest caps on the shakedown's slope but not its stunts; plus 5 for ground.
- All fifteen oracles on the stock car: fourteen exactly as before, the shakedown as in finding 3.
- Screenshots: seed 3's route across its ground from above, seed 8 from above, and the autodriver on seed
  8 at 70%.

**His working `buggy.json` has moved on again** (`d10c8d40` when these ran, after `879d0d97`, the copy
in `tunes/matthew-2026-11-23-late.json`); it's still his and uncommitted.

## 2026-11-23 — He drives the terrain

After driving `--terrain=` in his saved buggy (`879d0d97`: 844 kg, the twin-plane wing): *"feels good to
me now Claude"*. So M3a's dials stand as the bench set them (hills 50 m, a 200 m feature, 500 m regions,
rarity 0.3, painted by height). It's a verdict on the ground as ground; how it reads under a route is
M3f's question.

## 2026-11-23 — M3a: terrain

His answer to M3-PLAN's six decisions: *"Yes, as recommended"*.

**Built, in Core (`Generation/Terrain.cs`):**
- **`GenStage`**: a seed per stage, `Hash64.Mix(seed ^ stage)`, for terrain, route, stunts and ground
  (non-negotiable 4).
- **`TerrainSettings`** with `Problems()`, and **`Terrain`**: heights a metre apart over 1024 m, the
  640 m grid in its middle, fBm times an amplitude that **relief regions** (Overburden's threshold and
  square ramp, unchanged) take from the plains' to the hills'. **The ground flattens past the grid**
  (decision 6) to the old plain's level by 32 m short of its edge. A million samples in about 15 ms,
  rows in parallel; each depends only on its own position, so sharing the rows out can't change it.
  `HeightAt` between samples, `SlopeDegAt`, `SurfaceAt`.
- **A known answer, as `Hash64`'s**: seed 1 at the shipped dials folds every sample's bits to
  `17732728842014489761`, the same in Debug and Release. If it moves, every generated track moves.

**The bench's `--terrain` arm** (dials from the command line, so they're tried without a rebuild). The
first guesses (hills 24 m, a 320 m feature, 900 m regions) made ground with nothing steeper than 6.6° and
no hilly patch in half the seeds. **The dials now: hills 50 m, plains 3 m, a 200 m feature, 3 octaves at
gain 0.45, 500 m regions, rarity 0.3.** Over 100 seeds:

```
height on the grid       p1 -8.8 m, p99 8.4 m, extremes -27.2 and 29.8
slope on the grid        p50 1.9°, p90 7.6°, p99 16.1°, p99.9 22.4°, max 34.0°
hilly share              p50 8% of the grid, p90 24%
too steep to climb       in 53% of seeds; p90 0.8% of the grid, max 1.3%
road can follow          tile to tile within 1.25 m: p50 85%, p10 74%, worst seed 60%
```

So: gentle nearly everywhere, a hilly patch in most seeds, and a wall the car can't get up in about
half (decision 1). The road follows the ground tile to tile 85% of the time on a typical seed, which
is what M3b's walker has to work with.

**The `climb` oracle** (fifteen now): from rest 3 m short of a 40 m slope, full throttle, the steepest
it makes the top of, by halving the gap six times a surface. **Stock car: road 32.3°, dirt 25.3°, grass
22.5°, sand 19.7°.** M3-PLAN fact 3 guessed 17° for sand by the friction circle alone; the weight moving
onto the rear wheels on a slope buys the rest. Its first run read 0° everywhere: the car starts below the
slope's foot, a negative distance up it, which the "slid back" rule read as sliding back from the first
tick.

**A design clash it caught.** Ground was first painted dirt wherever it was steeper than 12°, where the
sand would have slid off. But dirt climbs to 25° and sand to 20°, so the hilly patches decision 1 wanted
to be walls on sand were climbable dirt, and the walls nearly vanished. **So the ground is painted by
height now**: firm dirt on the hilltops (above 5 m), grass in the hollows (below -4 m), sand everywhere
else, steep faces included, as a dune's slip face is. **Steep ground is drawn darker** so it still reads
as steep, without changing its grip.

**Game:** `--terrain=<seed>` builds the ground in place of the plain: a `HeightMapShape3D` at a metre
(1025 × 1025, which Jolt takes as a height field) and a mesh at 2 m, coloured by surface. Made in 45 ms,
built in about 170 ms. **The buggy looks its grip up by position** on a body tagged `painted`
(`PaintedSurface`, M3-PLAN fact 7); every other body still has one surface, cached as before.

**Checked:**
- 237 tests, 13 of them new: the known answer; the same seed giving the same terrain and another a
  different one; the samples being the field; flat at the edge and whole on the grid; relief on the
  grid; flat dials giving flat ground; the painting; the relief ramp; a seed per stage; and refusals.
- The headless check on the test ground and on terrain.
- All fifteen oracles on the stock car (`4f918acd`): `climb` as above, and the other fourteen exactly
  as they read when his steering went in, so the buggy's new surface lookup changes nothing off the
  terrain.
- Screenshots: seeds 1 to 3 from the side, seed 2's steepest hill (21.7°, at its south edge) close up, and
  his saved buggy pulling away across seed 3.

## 2026-11-23 — M3 planned: the generator

His go-ahead: *"lets continue please Claude i cant wait to see the autodriver navigate the full map you
made"*. M2 is built but for its play, which is his and can come whenever, so the next thing is M3's plan,
before any code: [docs/M3-PLAN.md](docs/M3-PLAN.md), in M2's layout.

**Before it, the autodriver in his saved buggy** (`879d0d97`, the one the game loads), so what he
watches isn't a spin: at 80% it runs 0.5 s wide on the raised 30 m left, falls off its edge and tips
over; **at 70% it finishes in 56.11 s**, quicker than the stock car's 56.70 at 80%, clean, both jumps on
their down-ramps, the loop never more than 1.9 m off its line (his car is 30 cm wider). So he watches
it with `--caution=0.7`, or presses - twice.

**The facts that shape M3**, measured or worked out: the noise reaches ±0.55 (the 2026-10-31 bench
figure), so dials are set against that; the road climbs at most a level every two tiles (3.6°), so the
terrain under a route has to be gentle; the car climbs about 17° off-road on sand by the friction circle,
to be measured; the route has to be walked in pieces, not tiles, because corners are 2 × 2 and 3 × 3;
**a stunt can only replace something with its own footprint and way out**, or the stunt dial reroutes
the track, and **nothing in the starter set has the loop's** (a tile across in 2 × 5), so decision 2
proposes an S-bend piece with that footprint for the loop to swap in for; the terrain is a height field;
painted ground needs the buggy to look its grip up by position; and a generated track saved as a file
needs its terrain's seed and dials with it, format 2.

**Six decisions, his**: terrain mostly gentle with hilly patches; the S-bend piece; embankments, not
pillars; 1 to 2 km; `--seed=` and a small Esc menu; ground that goes on past the grid.

## 2026-11-23 — His steering goes into the stock car

His words, first *"yeah commit the steering change only like make them start with the car you provided
initially we will make that part of the learning process setting up your car via trial and error"*,
and then, to make it plain: *"in my buggy the one i saved lift the steering degrees and put that in the
original stock car that would be provided via github please"*. And on why: *"yeah my 30 degree lock to
lock was ambitious it seems like but it is nice to be able to double my default though when the need
arises"*.

**The stock car is the one shipped, with his steering and nothing else**: `maxSteerDeg` 30 → 21.349998
(21.35 on the panel) and `minSteerDeg` 8 → 2.05, taken from his saved buggy. Shift still doubles it, to
42.7° at walking pace. Its fingerprint is `4f918acd` (was `964252ce`). The rest of his saved buggy, a new
tune since the evening one (844 kg, 159.5 kW, grip 1.166, 85% rear drive, the twin-plane wing, track
2.0 m), stays in his working copy uncommitted, with a copy beside the evening tune at
`%APPDATA%\Godot\app_userdata\Sandrail\tunes\matthew-2026-11-23-late.json`. **Players start with this
car and find a better setup by trial and error** (PLAN, "His ideas"): the stock car is a starting point,
not a finished tune.

**The oracles on the new stock car**, against the old:

```
circle     1.06 g at steer 0.37 (5.2°), as before at 0.25 (5.5°): the same lock, now further along the input
handbrake  full lock from 61 km/h for 2 s: 65° on steering alone (was 62), 154° with the handbrake (was 142)
air        full right steer in flight: turned 38°, rolled 28° (was 37°, 27°)
lap        80%: standing 26.91 s, flying 24.51 s (was 26.93, 24.53), widest off the line 1.39 m (was 1.42)
track      80%: FINISHED in 56.70, exactly as before, clean
straight, drop, brake, sweeper, respawn, jump, loop, bumps, pieces: unchanged
```

**It doesn't hold 90% on the corner lap**: at 90% the flying lap is 26.64 with two spins, the first on
the straight into corner 1, as the old stock car did. His laps of 23.05 at 90% (the entry below) were on
15.25° of lock, not 21.35°. On the shakedown at 90% it finishes in 1:13.80 with slides out of the same
three corners as before. So at 80% the new stock car is the old one a little sharper, and the less lock
that held 90% is still there to be found on the panel.

## 2026-11-23 — His steering on the corner lap: the autodriver at 90%

He ran the autodriver round station 6 in the game on his 19:39 panel steering (`maxSteerDeg` 15.25,
`minSteerDeg` 2.05; spec `fce5b6e5`, still uncommitted) and pasted the last four laps. The rest are from
his game log, in the order he drove them:

```
80%    24.59, 24.47, 24.48, 24.48, 24.33       (after a first few not counted while it settled in)
100%   23.71
90%    one lap not counted (52.52), then 23.52, 23.04  <- its best
85%    25.53, 23.33, 23.33
90%    23.13, 23.07, 23.09, 23.05               <- the four he pasted, all within 0.09 s of the best
       then 37.03
```

**At 90% it laps within a tenth, at about 23.05, which the shipped car can't.** The shipped car does
24.53 at 80% (the `lap` oracle), and every car spins on the straight into corner 1 once pushed, the
shipped car from 90% (the autodriver entry). With his smaller lock it holds 90%, about a second and a half a
lap quicker than the shipped car at 80%.

**It still goes wrong about one lap in six at 90%**: the 52.52 that didn't count (it left the lap) and
the 37.03, both most likely a spin or a trip onto the sand.

The corner lap is kinder to his steering than the shakedown is. On the shakedown, the `track` oracle
spins his steering out of the 30 m right after the chicane at 80% and only finishes clean at 70% (58.46;
the M2d entry). These are the game's own laps, timed by the lap timer, not an oracle run; nothing was
changed for them.

## 2026-11-23 — M2d: racing the shakedown

His go-ahead, after saying he'd drive M2c first: *"Ok keep going please Claude"*. No verdicts on the
shakedown yet; those are M2e's.

**Built, in Core:**
- **`RaceTimer`** (`Tracks/`): the clock from the first throttle at the start gate, through each
  checkpoint in order, to the finish. **A gate is passed by driving through its arch forwards**, the
  crossing interpolated between ticks, not by getting level with it, since the ground beside the road is
  drivable (fact 5). A gate crossed early is said ("out of order") and not counted, so a skipped
  checkpoint holds the run up until it's gone back for. A gate crossed backwards, beside its posts or
  over its beam is nothing. A respawn tells it the car was moved, so the move isn't a drive through a gate.
- **`TrackRacing`**: a laid track's gates, and its route as one open racing line. The line turns least
  where the road leaves room and keeps to the middle of every stunt (the launch ramps, landings and the
  loop, each measured that way in M2b). On a stunt piece the speed plan holds the middle of its hint
  (90, 114, 97.5 km/h), not what its shape says, and the line holds the wheels straight in the loop
  itself. `RacingLine` gained those marks (`WithStunts`) and a room that can vary along the line; with
  no marks its plan is bit for bit what it was.
- **The autodriver**: hands off in the air (there the pedals and steering are air control, and the jump
  hints were measured with nothing touched), wheels straight in the loop, a stop at the end of an open
  line, and a search of the whole line when the car is suddenly 15 m from where it was (a respawn).

**Game:** on a track, the board top right shows the run's clock, the gates passed and the bests (his
and the autodriver's apart, for the session). A checkpoint flashes its split against the same driver's
best; a missed one says to go back for it. **R puts the car 4 m past the last checkpoint's arch with the
clock running** (decision 3), or at the start before the first. **Backspace starts the run again**,
which R no longer can. The auto-respawn does what R does. P hands the car to the autodriver from
anywhere on the route, and L shows its line, which has no strip across a jump's gap. The keys line
offers checkpoints and the restart on a track. `--checkpoint=N` starts the car where R puts it after
checkpoint N, for a screenshot.

**Two findings on the way:**
1. **The first `track` run "finished" with a lie in it.** The car slid off the loop's side at the
   bottom of the descent, onto the sand, and drove back onto the road; the oracle called the loop
   "through" and counted 5.5 s off the road in it. Logged through the loop, the car's offset from its
   line grew steadily all the way round, so it went in pointing about 4° across the road. Just before the
   loop it was 0.65 m left of its line, and the autodriver's pull back onto the line had it angled at the
   moment the wheels went straight. **In a loop a sideways offset stays as it is, but an angle grows into
   a drift.** So over the last 15 m before a loop the autodriver now lines the car up instead: in at
   0.6 m off and square, it comes round never further off than that. And the oracle now says how far off
   its line the car got in the loop, so a car that comes off the side can't be called through again.
   (A first guess, that the line read a false bend off the loop's own samples, wasn't it; the fix for it
   stays, since a heading read off road pointing at the sky is wrong anyway.)
2. **The 30 m right after the chicane is where cars spin.** Every run that left the road at all left it
   there, except his evening tune at 70%, which spun on the raised left a little before. The 90% and 100%
   runs also slid out of the first corner, the 50 m right after the short jump.

**The `track` oracle** (fourteen now):

```
shipped car (964252ce), 80%   FINISHED in 56.70 | splits 21.54, 40.64 | top 120 km/h
                              short jump: lip 82 km/h, landed 36.2 m past it, on its down-ramp
                              loop: in at 96 km/h, through, at most 0.6 m off its line, slowest 80
                              long jump: lip 108 km/h, landed 60.7 m past it, on its down-ramp, pitch -10°
                              wheels off the road 0.00 s, worst slide 22°
shipped car, 90%              finished in 1:14.10, with spins in four places, 8.3 s off the road
shipped car, 100%             didn't finish: spun before the long jump, went off its lip at 83 km/h,
                              fell short into the gap, stuck on the landing
his steering (fce5b6e5), 80%  didn't finish: spun out of the 30 m right after the chicane, stuck
his steering, 70%             FINISHED in 58.46, clean, worst slide 8°
his evening tune (5d844085)   didn't finish at 80% or 70%: spun on the raised left and after the chicane
```

The short jump lands near the start of its down-ramp (36 m; the ramp is 34-47 m): the autodriver holds
90 km/h less tightly up the ramp than M2b's oracle did, and leaves at 82. The racing line takes about
0.1 s to build.

**Checked:**
- 218 tests, 16 of them new: the race timer (a run through every gate counts, timed exactly; a skipped
  checkpoint holds the run up until it's gone back for; a gate crossed backwards, beside it or over it
  isn't passed; a respawn isn't a crossing), the shakedown's gates, the route line (stunts down the
  middle at their hints, wheels straight only in the loop itself), a line without marks planning exactly
  as before, and the autodriver in the air, at the end and in the loop.
- All fourteen oracles on the committed car: the thirteen exactly as they were, the lap still 24.53 s.
- The headless check on the test ground and on the shakedown with the autodriver.
- Screenshots: the car 4 m past checkpoint 1, and the autodriver on its way with the clock at 2.98.

## 2026-11-23 — M2c: the track file and the shakedown

His go-ahead: *"Go on to M2c"*.

**Built, in Core (`Tracks/Track.cs`):**
- **`Track`, the file**: a format number (1), an ID (`base:shakedown`), a name, the grid's size, and the
  placements, each a piece, a tile `[column, row]`, a level, the way it faces and whether it's mirrored.
  **The file says which way a piece faces (`"east"`), not how many quarter turns it's turned**: the same
  thing, but one a person can check against the grid. Unknown fields and values are refused by name, and
  **the format is read before anything else**, so a file from a newer Sandrail is refused as newer rather
  than for fields this build has never heard of. `Save` writes one placement a line, in the layout the
  shakedown is written in by hand, so a load and a save leave a hand-written file's text exactly as it
  was; a test holds the shakedown to that, since the editor will load and save files people wrote.
- **`Lay`, the validation**, against the catalogue. Every problem is said, each with the placement's
  index in the file, its piece and its tile: an unknown piece, by name; off the grid; two pieces on one
  tile; a level below the plain; road leading onto empty ground, off the grid, into a piece's side or
  onto another level; nothing leading into a piece; a lip with no landing in line, a landing facing
  elsewhere or at another level, a piece in a jump's gap, a landing no jump reaches; not exactly one start
  and one finish; no route between them; and anything off the route, **a checkpoint off it said as one**
  ("could never be passed in order"). A mismatch between two pieces is said once, from the way out.
- **The route**: the pieces from the start to the finish in the order they're driven, which must be
  every piece. It's what M2d's timer, racing line and autodriver read. `LengthM` is its road.

**The shakedown** (`content/base/tracks/shakedown.json`): 35 placements and 1.28 km of road, written by
hand on the 32 × 32 grid. In order:
1. The start gate and one straight, then **the short jump** over a tile onto its landing.
2. A 50 m right, then **a climb** onto a raised section, a 30 m left **on earthworks** a level up, and
   down again.
3. The first **checkpoint**, the **chicane**, a 30 m right, three straights, and **the loop**.
4. A **banked 50 m right**, the second checkpoint, and **the long jump** over two tiles, which lands a
   level up.
5. Down a slope, a 50 m left, a **banked 30 m left**, two straights and the finish.

Every piece is in it: both corner sizes both ways, and both banked corners.

**Only one straight between the start and the short jump, on purpose.** By the straight oracle's figures
(0-100 in 5.17 s, 27.0 m/s at 5 s), the car reaches the lip at about 88 km/h at full throttle from a
standing start. That's inside the jump's 85-95, so the first thing to do on the track is floor it. It's
an estimate, not a measurement; M2d's `track` oracle will say.

**Game:** `--track=base:shakedown` (or `--track=<path>.json`) builds a track. Each placement is swept
into a body of its own, tagged with its surface; raised pieces sit on earthworks; the gates have arches.
The car starts 4 m into the start gate, and R and the auto-respawn put it back there until M2d's
checkpoints. The plain is sand. **Headless, it loads in 64 ms and builds its 35 bodies in 37 ms.** The
pieces oracle's `Name` became `TrialName`, which had hidden `Node.Name` with a warning since M2b.

**Checked:**
- 202 tests, 40 of them new: the shakedown is sound; its route runs start to finish through every piece
  once, each road meeting the next to a millimetre, and a jump's landing starts the gap's width on; it has
  what M2c asks of it; the file round trip; and each refusal, on a small track written to break it.
- The headless check, on the test ground and on the shakedown.
- All thirteen oracles on the committed car (`964252ce`), unchanged from M2b.
- Screenshots: from above, from the side, of the long jump and of the raised section, and from the car on
  its way to the first jump.

**Left for M2d:** the HUD's keys line still offers stations on a track, where there are none.

## 2026-11-23 — M2b: the starter set

His go-ahead: *"Go on to M2b"*.

**Fifteen pieces** in `content/base/pieces/`, each held to its way out to a millimetre turned four ways
and mirrored: straight; corners of 30 m (2 × 2) and 50 m (3 × 3), flat and banked 20°; slope up and slope
down, a level over two tiles; a short launch ramp (one tile, lip 2.96 m up at 20°, M1c's kicker), a long
one (two tiles, lip 5.23 m at 20°) and a landing ramp (three tiles, M1c's table and 8° down-ramp, a level
down); the loop; a chicane (a 4 m jink inside 2 × 1, 26 m arcs); start, checkpoint and finish gates,
straights with arches (green, blue, red; no collider).

**What the model gained:** a **lip** connector is raised and pitched, at a height of its own, and says
how many empty tiles its jump clears and what level its landing is at; the **loop step** builds its own
S-bends; and a placed piece works out its **earthworks** from how high its road climbs (a fifth more
under a banked or pitched edge), and a loop gets a thin road. **The showroom** puts a launch ramp's
landing across its gap, starts a slope down a level up, gives pieces meant for over 100 km/h seven
straights of run-up instead of four, and [ and ] step through its fourteen stations (number keys reach
nine now). `Buggy.Respawn` can put the car down moving, for the oracle.

**The loop took two goes, which the plan expected** (M2-PLAN fact 4):
1. **An S-bend straight into the loop's foot dropped the car off the descending side.** The S of 123 m
   that puts the car on the loop's 11.6° skew ended exactly at the foot, so the car arrived still turning
   (12°/s of yaw out of the bend) and, with its wheels straight, kept turning and drifted off. A player
   would have had to unwind the steering at the exact moment the loop began.
2. **So the car gets 15 m of straight at the skew before the loop and after it** (`settle`). There's
   only room for that if the loop's own offset shrinks from M1c's 15 m to 12 m, with the road 8 m wide
   (M1c's loop width) to keep 4 m between the loop's way in and its way out where they pass at ground
   level. The bends come to 120 m (0.6 g at 97 km/h), and the piece is 2 × 5 tiles. The joint with
   10 m road either side is a 1 m step each side, which only shows.

**The `pieces` oracle** (thirteen now), each trial started at its run-up already moving:

```
loop at 90 / 97 / 105 km/h      THROUGH, 0.1-0.2 m off its exit road, slowest over the top 75 / 79 / 84
short jump at 85 / 90 / 95      at the lip 80 / 85 / 89: lands 34.8 / 40.1 / 45.0 m past it, all on the
                                landing's down-ramp (34-47 m), pitch -3° to -1°
long jump at 110 / 115 / 120    at the lip 104 / 109 / 114: 54.8 / 61.9 m on the down-ramp (54-67 m),
                                68.7 m on its foot; pitch -13°
slope at 100 / 110 km/h         wheels down
slope at 120 / 130 / 160        0.15 / 0.36 / 0.70 s in the air
```

So the hints are **short 85-95, long 110-118** (the long jump needs its longer run-up), **loop 90-105**.
**The slope throws the car from about 115 km/h, not 143**: the arithmetic took the crest's radius alone,
but the slope's two arcs meet with no straight between, and the suspension squashed on the foot springs
back over the crest. Decision 2 wanted slopes to throw the car only near top speed; 115 is two thirds of
177. Worth his eye in the play rather than a change now.

**His steering, again**: at 19:39, while this was built, he saved `maxSteerDeg` 15.25 and `minSteerDeg`
2.05 (from 30 and 8) from the panel. It stays in his working copy, uncommitted, as before; the figures
here are on the committed car (`964252ce`), where circle, handbrake, air and lap read exactly as they
did. On his numbers the car turns more at 60 km/h, not less (68° against 62° in two seconds at full
lock, 161° against 142° with the handbrake): at 30° of lock the fronts are past their peak slip.

**Checked:** 162 tests (the catalogue test holds all fifteen pieces every way); the headless check;
all thirteen oracles; screenshots of the showroom from above and of the loop and the short jump from
their stations.

## 2026-11-23 — M2a: the grid and the catalogue

His answer to M2-PLAN's six decisions: *"Yes, as recommended"*. And while M2a was being built, on the
test ground's pieces (M1c's jump, slope, banked hairpin, loop and the corner lap): *"I must compliment
you on your track pieces Claude they have been very very good like"*. That's the pieces M2 now turns
into grid pieces, so it's a good sign for their shapes.

**Built, in Core (`Tracks/`):**
- **`TrackGrid`**: 32 × 32 tiles of 20 m, levels of 2.5 m, sitting exactly on the 640 m plain
  (tile (16, 16)'s middle is 10 m south-east of the plain's), north -Z as Core's frames already have it.
  `Compass` and `Tile` with steps and quarter turns.
- **`PieceDef`**: tiles as [across, along], the way in (always the back of [0, 0]: road, landing or
  closed), the way out (tile, side, rise in levels, kind), the road as data steps (`line`, `pitch`,
  `turn` with bank and ease, `loop`), width, surface, a speed hint and a gate. **`Problems` builds the
  road and refuses a piece whose road doesn't end at its declared way out to a millimetre** (a heading or
  tilt off by a hundredth of a degree counts as a millimetre; angles by atan2 of cross and dot, since a
  near-1 dot's acos can't resolve that in floats). Jumps leave by lips and come down at landings.
- **`Placement`** and **`PlacedPiece`**: a piece turned in quarter turns and mirrored (a left corner is a
  right one mirrored: its turns and loop offset change sides), its tiles and connectors on the grid, and
  its road from its way in as centrelines through `Chain`.
- **`PieceCatalogue`**: one piece a file from `content/base/pieces/`, unknown fields and numbered enums
  refused, duplicate IDs refused, every file's problems reported together by name.
- **`Showroom`**: every piece behind four straights of run-up (80 m) with two of run-out along its way
  out, packed across the grid in bands, a station at each.
- **One piece, `base:straight`.**

**Game:** `--ground=showroom` builds the showroom (`TrackView`, with raised pieces on earthworks to the
plain, M2-PLAN fact 8) and its number keys go to its stations. `TestGround.Road` now sweeps both the test
ground's runs and the pieces.

**Checked:** 157 tests, 16 new. The straight, and a 2 × 2 corner of 30 m and a two-tile slope written in
the tests, end at their ways out to a millimetre turned all four ways and mirrored; the mirrored corner
leaves west from the right tile; the slope rises its level and ends level; two straights in a row meet;
the showroom lays every piece once, on the grid, without overlap. The headless check on the showroom,
screenshots of it from above and from the car, and jump, lap and respawn on the test ground read exactly
as before.

## 2026-11-23 — M2 planned

His go-ahead: *"Write M2's plan"*. [docs/M2-PLAN.md](docs/M2-PLAN.md), before any code, the way every
milestone starts.

What the arithmetic said while writing it, worth keeping apart from the plan:
- **A one-tile corner is a 10 m hairpin** (37 km/h); on a square grid the radius is 20n − 10, so 2 × 2
  and 3 × 3 give 30 m and 50 m (65 and 84 km/h flat, 97 and 125 banked 20°), inside station 6's range.
- **A slope's crest throws the car above √(gR)**: climbing a 2.5 m level in one tile puts 41 m arcs in
  it (airborne at 72 km/h), in two tiles 161 m arcs (143 km/h).
- **The M1c loop doesn't drop onto the grid**: it's arrived at 11.6° off its heading and leaves 15 m to
  the side, and a grid wants axis-aligned ends one tile (20 m) apart. The loop piece has to carry its
  own skew.
- **A gap jump is a connection across empty tiles**, so connectors need kinds (road, lip, landing).

Six decisions are his before M2a starts: corner sizes, slope length, checkpoints and where a respawn
goes, how the clock runs, whether the test ground stays, and the grid and level sizes.

## 2026-11-23 — M1d's last: the auto-respawn; M1 is built, its tuning after the editor

His word, after air control: *"Lets continue Claude i'll do better tuning after the track editor is
complete"*. So M1d finishes here, and **M1e's play, the pass on his word that it feels right, waits for
the track editor (M5)**, when there are real tracks to tune it on. M1 is built; the next thing is M2's
plan.

**The auto-respawn** (decision 4: R any time, and by itself after 3 s upside down and nearly stopped):
- **The rule is `StuckWatch`, in Core** (9 tests), fed numbers rather than a body, so a player, an
  oracle and later a bot share it. **Tipped over** is more than 70° from upright, which takes in on its
  side as well as on its roof, since a raycast car on its side can't reach the ground with a wheel
  either. **Nearly stopped** is under 2 m/s, which is what keeps the top of a loop (upside down at
  80 km/h) from counting. Any moment upright or moving starts the three seconds again.
- **Also below the world, at once**: 30 m under the ground. The plain is Jolt's world boundary, 2 km
  across by its default, so a car off its end fell for ever.
- **`AutoRespawn`**, a node in Game, puts the car where R would (the station it started from), and the
  HUD counts down in the middle of the screen after half a second stuck ("back on your wheels in 2,
  R now"). R resets the count. The other oracles run without it, so a crash in one is still reported as
  a crash.

**The `respawn` oracle** (twelve now), on the bare plain: dropped on its roof 20 m from the start, it's
back on its wheels at the start **3.00 s after it came to rest** (3.51 s after the drop); dropped just
past the plain's end, it's back **2.71 s** after, the moment it passes 30 m down. On its side isn't
tried: the chassis box, its centre of mass low, tips back onto its wheels. Putting it under the plain
doesn't work either, because the world boundary is solid all the way down and pushes it back up.

The other eleven read exactly as at `964252ce`; 141 tests. Not checked: the countdown on screen, since
no oracle runs windowed and a screenshot can't turn the car over.

## 2026-11-23 — M1d: air control

His go-ahead: *"Go on to air control"*. M1-PLAN's line: a little pitch from throttle and brake, roll and
yaw from steering, with no wheels down; landing a jump nose-first should be the player's doing.

**The law** (`WheelModel.AirControlStep`, 7 tests): with no wheel on the ground, **throttle lifts the
nose and brake drops it** (as a rider does with the rear wheel), and **steering turns the nose and rolls
the car the way it's steered**. Each is a spin full input adds a second, in F2's new Air group, 0 for
off: `airPitchDegPerS2` 25, `airYawDegPerS2` 30, `airRollDegPerS2` 20. **No input does nothing**, so a
jump's own rotation stays the physics'. **It won't spin the car past `airSpinCapDegPerS` (90), but it
always slows a spin**, at full strength, however fast, so a car flung off a lip can be caught. It
changes the spin directly rather than through a torque, so mass and shape don't change how it answers,
and it reads the eased pedals and the steering angle, so a keyboard tap is gentle in the air too. F3
shows IN THE AIR with the two inputs; the HUD's keys line says what W/S and A/D do up there.

**The `air` oracle**: station 1's jump five times, each with one input from the lip to the first touch
(at the first touch, not two wheels down: a car landing rolled has rocked back level by its second).

```
off the lip at 82 km/h, 1.84 s up        first numbers (40/40/25)    shipped (25/30/20)
coasting                                 pitch -5°                   -5°
throttle held                            +47°                        +31°
brake held                               -56°                        -38°
brake tapped for 0.3 s                   -                           -16°
full right steer held                    turned 47°, rolled 31°      turned 37°, rolled 27°, pitch -15°
```

**The first numbers were more than "a little"**, and they made a habit a trap: hold W over a jump, as
anyone will, and the car landed 47° tail down. At the shipped numbers holding throttle through the
whole flight costs 36°, and a tap of brake is worth 11°, which is a correction a player can aim.

**The other oracles**: `jump` lands at -7° rather than -8° (39.6 m, not 39.7), because the throttle the
approach used is still easing off for a few ticks after the lip; `bumps` moves by a hair (all four off
11%, not 10%; deepest 0.290 m); `lap` reads 24.53 s, not 24.52 (corner 6 at 80 km/h, not 81, widest
1.42 m, not 1.43), because all four wheels leave the road for a moment somewhere on the lap and the
autodriver's steering then turns the car a little; the rest read exactly as before. Committed spec
`964252ce`, 132 tests.
Not checked: driving it, and whether the pad's right stick, which is his throttle and brake, makes a
better pitch control than the keys do.

## 2026-11-23 — M1d: downforce, from a wing you choose

His go-ahead: *"please go on the downforce but if possible make the wing a little customizable like we
will make you earn and pay for them later somehow"*.

**The wing is a part, named by ID** (`Wing`, `Wings` in Core, like `Surfaces`), because parts are what
get earned and owned: `base:wing-none`, `base:wing-lip` (0.35 m², stalls at 10°), `base:wing-strut`
(0.8 m², 14°) and `base:wing-twin` (1.3 m², 18°). A car's spec names its wing (`wingId`) and sets it:
`wingAngleDeg`, `wingBackM` (behind the middle), `wingHeightM`. **The shipped buggy has none**, since
it's the car before anything's earned, so every figure it had stands. F2 has a wing list above the
three sliders; F3 shows the downforce and drag; runs gain `wing_down_n` and `wing_drag_n`, last, so
the 47 columns before them haven't moved.

**The law** (`Wing.LiftCoefficient`, `DragCoefficient`, `WheelModel.WingForces`): ½ρv²·A·C. The
downforce coefficient is 0.08 a degree up to the stall, then falls to half over 10° more; drag is
0.05 + 0.15·C_L² + 0.06 a degree past the stall. So each wing has a best angle and costs top speed in
proportion to what it gives. Three things the building found:

1. **The first stall penalty was too small.** At 0.03 a degree, the induced drag shed past the stall
   nearly cancelled it, so drag barely rose (`DragClimbsWithAngleAndSteeplyPastTheStall` caught it).
   It's 0.06.
2. **The first law flipped the car in the air**: it pressed as if the air always came from straight
   ahead, so in flight a rear wing's push rotated the nose up, and the twin-plane wing landed the jump
   at +48°. **A wing meets the air at its set angle less the angle the car is moving at**, measured
   with the wing's own velocity (so pitching swings it through the air and it resists), and the force
   goes across and along that airflow. In flight it's now a dart's flights: the twin-plane wing lands
   the jump at 1° to 11° depending on where it's mounted, against the bare car's -8°. On the road the
   angles agree within a degree or two.
3. **Where it's mounted is the balance.** On the new sweeper oracle, the twin-plane wing at 18° holds
   0.95 g 1.4 m behind the middle (all on the rear tyres: it runs wide, no gain at all), 1.07 g at
   0.7 m, and 1.22 g at the middle (until it runs out of power), against 0.97 g with no wing. **The
   default mount is 0.7 m, over the engine**: real grip, and at the limit it runs wide rather than
   spinning.

**The autodriver counts it.** `RacingLine.SpeedProfile` takes the downforce into the banked-curve
formula, `v² = g(tan θ + μ) / (κ(1 − μ tan θ) − μk/(m cos θ))`, and into braking, so `lap` compares
winged tunes fairly. Written so that with no wing it's the old formula to the last bit
(`AWinglessPlanIsTheOldPlanExactly`), and the lap figure didn't move.

**A new oracle, `sweeper`**: a 120 m circle on the bare plain, steered back onto it, the speed
creeping up a quarter of a metre a second each second until it runs 4 m wide, spins or runs out of
power. It reports the best half-second of sideways acceleration, measured from the path (the change in
velocity across the direction of travel: speed times yaw rate read 2.20 g as the car began to spin).
`circle` runs at 72 km/h, where a wing hardly presses.

**And an old flaw in `straight`**: thirty seconds flat out is over a kilometre and the bare plain ends a
kilometre from the start, so every car near 50 m/s **has always gone off its end at about 26 s**. Found
because the lip spoiler seemed to add 4 km/h of top speed: it was falling. Top speed now counts only
with four wheels down, and the line says when it went off. The shipped car reads 49.10 m/s, not 49.11.

**Each wing at its stall angle, mounted at 0.7 m** (the shipped car with each fitted, nothing else
changed):

```
                     none        lip 10°       strut 14°     twin 12°      twin 18°
top speed            177 km/h    174           168           166           159
0-100 km/h           5.17 s      5.12          5.04          5.00          4.97
100-0 km/h           38.4 m      38.0          37.2          36.6          35.9
sweeper              0.97 g      1.02 g        1.05 g        1.06 g        1.07 g
  at                 122, spun   125, spun     127, wide     128, wide     128, wide
  pressing           0 N         203           666           922           1387
jump landing pitch   -8°         -3°           +4°           0°            +11°
loop, over the top   79 km/h     79            79            79            79
lap at 80%, flying   24.52 s     24.36         24.08         23.93         23.72
  off the road       0.00 s      0.00          0.00          0.07          0.43 (into corner 1)
```

The rear downforce quickens 0-100 (more traction off the line) and shortens stops; the wing costs top
speed and buys corner speed, so the lap gets quicker with every size. **It does nothing for the loop**:
over the top at 79 km/h the twin-plane wing presses a few hundred newtons, and the loop already went
through. M1-PLAN's reason for downforce was the loop; at this loop's size it isn't needed.

Checked: 125 tests, the headless check, all ten oracles on the shipped car (`011f7969`; the nine old
ones exactly as before bar straight's 0.01), and screenshots of all three wings on the car, the plate's
trailing edge up. Not checked: driving it, which is his.

## 2026-11-23 — his evening tune, measured; and corner 1 isn't braking

He drove the handbrake, kept tuning, and asked: *"I've driven it and think i've completely ruined the
default setup but tell me what you think?"* The shipped car is safe in git; his numbers are only in his
working copy. No runs recorded, so this is the oracles on his file as saved at 17:22 (`8d33cc09`),
against the shipped car (`da9506cc`). Since the afternoon he'd taken the springs from 15900 to 10000,
power from 192.5 to 243 kW, grip from 1.214 to 1.4, brakes from 8980 to 11960 N, the lock from 30° to
16.4°, the wheelbase from 3.02 to 2.75 m and the track from 1.7 to 1.87 m, and put 10% of the drive
on the front.

```
                        shipped da9506cc        his tune 8d33cc09
0-100 km/h              5.17 s                  4.38 s
top speed               177 km/h                243 km/h
100-0 km/h              38.4 m (limit 35.8)     35.9 m (limit 28.2)
most sideways           1.06 g                  1.37 g
full lock 2 s, alone    62°                     89°
  with 0.5 s handbrake  142°                    154°
lap, flying, at 80%     24.52 s clean           22.09 s, 1.0 s off into corner 1, 2.50 m wide
  at 70 / 75 / 85%      25.61 / 25.00 / 24.31   23.05 / 22.53 / 21.75, no spins
  at 90%                spins twice             spins and sticks at 502 m (corner 6)
```

**Quicker everywhere.** **The fault is the springs: 10000 N/m under 1004 kg sits 0.25 m into 0.40 m of
travel parked (62%), where the shipped car sits at 28%.** Its weight went up 55% and its springs down
37%. With the dampers scaled to the shipped car's damping ratios (bump 0.43, rebound 0.65 of critical):

```
                        his 10000              16000, 1740/2610       22000, 2040/3060       shipped
sits into its travel    62%                    38%                    28%                    28%
1 m drop                bottoms, 2 overshoots  0.381 of 0.40, 1        0.325, 1               0.305 of 0.35, 1
jump landing pitch      -28° (nose down)       -18°                   +9°                    -8°
loop exit               10.0 m off centre      2.0 m                  0.5 m                  0.1 m
bumps: on the stop      27%                    5%                     0%                     0%
bumps: a wheel up       30%                    30%                    45%                    46%
lap at 80%              22.09 s clean          22.12 s clean          spins on lap 2         24.52 s
```

16000 (his afternoon value, near enough) with dampers raised to suit the weight keeps the soft car's
grip over bumps and its clean lap, and mostly ends the bottoming. 22000 ends it entirely but lifts
wheels as often as the shipped car, and the autodriver spins it at 80%.

**His brakes are weaker than his tyres**: 11960 N asks 1.21 g of a car whose grip gives 1.4. 17500 N
(the shipped car's ratio of brakes to grip) stops in 31.3 m instead of 35.9.

**Corner 1 isn't braking, as this worklog said twice today.** "Into corner 1" is the lap oracle's name
for the straight before it, which starts at corner 6's exit. On his tune at 80%, every variant runs
2.50-2.58 m wide there and drives exactly the same corner speeds:
brake force 11960 or 17500 N, brake balance 0.5 to 0.654, front anti-roll 0, 2000 or 4000. Power trims
it (2.37 m at 150 kW, 2.07 m at 110 kW) without curing it. The shipped car goes 1.43 m wide on the
same straight, and every car that spins in these sweeps (shipped at 90%; his with stiffer springs,
stronger brakes or the shipped front bar at 80-85%) spins there first, at 80-87 km/h with both pedals
off, which is the slip guards cutting them. So it's most likely how the autodriver takes corner 6's
exit (60°, 40 m, 15° bank easing out), worse with more speed. Not yet pinned down: the lap oracle
reports the piece, not the distance along it.

**Then the shipped car went back**, at his word: *"restore the default tune from the github repo
please we might make a game out of building your car too"*. `buggy.json` is GitHub's again (`da9506cc`;
straight 5.17 s, lap 24.52 s, as before). His tune is kept outside the repo, beside his runs, at
`%APPDATA%\Godot\app_userdata\Sandrail\tunes\matthew-2026-11-23.json`; `start.cmd --spec=` drives it.
The car-building idea is in PLAN, "His ideas".

## 2026-11-23 — M1d begins: the handbrake

His go-ahead: *"Yes, handbrake first"*.

**The law** (`WheelModel.LockedTyre`, 6 tests): **a locked tyre slides, so its force is against the
contact patch's motion, whichever way that is, at `slideGripFraction` of its grip.** M1-PLAN wrote it
as forward force at the circle's edge with sideways grip dropping to a fraction, and following the
motion says both at once. Going straight, the motion is all forward, so it's all braking. In a corner
the patch moves mostly forward and a little sideways, so only a little of the force holds the car in:
at 20 m/s with a 2.9° slip it's a twentieth of the grip sideways, where a rolling tyre gives over a
third. The rear axle lets go and the tail comes round, and once the car is sideways the force is
sideways too and catches it. It asks for whatever stops the patch in one tick (taking the wheel's share
of the car as load / g, like the rolling tyre's low-speed term), so within a few centimetres a second
of a standstill the wheel holds still instead of shivering: it's a parking brake too.

**The number is `handbrakeLock`, 0 to 1, default 1**: how far Space locks the rear wheels. Between,
each rear tyre is that far from rolling to locked; both lie inside the friction circle, so any blend
does too. 0 is off, as M1d asks of every extra. It acts at once, not eased like the pedals, because a
flick of the key has to lock: eased at the pedal rate, a tenth of a second's tap would reach only half.
The locked tyre's grip reuses `slideGripFraction` rather than a new number, since a locked tyre is a
tyre sliding.

**The input was already there**: Space and pad A since M1a, his pad's saved layout has it on button 2,
and the recorder's `key_handbrake` column has been logging it all along. New: telemetry shows
HANDBRAKE, the keys line says Space, the rear wheels stop turning while it's held, and `--drive=` takes
a fourth value for it.

**A ninth oracle, `handbrake`**, on the bare plain at road grip like the M1a four: three runs from
rest, each up to 60 km/h first. A stop on the handbrake alone; two seconds at full lock with the
throttle off; and the same with the handbrake for the first half second.

```
                    stop on it alone             full lock 2 s, steering alone    with 0.5 s of handbrake
committed da9506cc  34.0 m, 4.02 s, 0.43 g, 0.3° 62°, out at 43 km/h, slip 5°     142°, out at 18 km/h, slip 157°
his tune  28861ad7  34.7 m, 4.09 s, 0.42 g, 0.3° 70°, out at 38 km/h, slip 13°    134°, out at 22 km/h, slip 155°
```

The stop is what locked rears carrying about half the weight should give, and it stays straight. **The
turn more than doubles, and a half-second pull with the lock held is a spin**: the car swings past
sideways and loses two thirds of its speed. A hairpin wants a shorter pull and the lock taken off as the
tail comes round. Whether full lock is too savage is his to say from the driving seat; lowering
`handbrakeLock` (F2, Engine and brakes) gentles it.

**The other eight oracles read exactly as before**; the fingerprint moved from `a2e24a2b` to
`da9506cc` only because the file has a new field. The shipped file has to list every field
(`TheShippedBuggyListsEveryField`), so `"handbrakeLock": 1` went into his working copy beside his
values, and that one line is all of `buggy.json` that's committed. Not checked: driving it by hand,
which is his.

## 2026-11-23 — a lap timer for the corner lap; no wheel-spin gauge for now

His ask: *"a lap timer for the main track please Claude and a whellspin indicator i think?"*

**The lap timer** (`Driving/LapTimer.cs`, in Core, 7 tests): a lap runs from crossing station 6's
line forwards to crossing it again, with **the moment of crossing interpolated between ticks**.
Progress is measured along the road's middle, and **a lap counts only if it goes through each quarter
of the lap in order and never strays more than 15 m from the middle**, so half a lap in one tick (a
cut, or the car moved) or a trip across the sand makes it "not counted". Cutting a corner's inside isn't
caught. Reversing back over the line and forward again restarts the lap. **At station 6 it's a standing
start**: the clock waits for the first throttle (or reverse), so settling on the springs never starts
it. Arming waits two ticks after a respawn, until the car really is on the line: armed at once, a held
throttle would start the clock with the car still where it was.

**In the game**, a board at the top right: the lap running (or "ready", or "cross the line"), the last
lap, and **the best of his laps and of the autodriver's, kept apart**. A lap is the autodriver's if it
had the car at any point of it, so he can race it. Each finished lap flashes its time against the best.
Bests last the session; keeping them belongs with ghosts (M4).

**The lap oracle now times with the same timer.** Flying laps read exactly as before (24.52 s shipped,
24.03 s his tune). Standing laps read 0.03 s longer (26.93 and 26.70), because the old count ended at
the tick the nearest sample wrapped round, about half a metre short of the line. A minute of the
autodriver in the game, headless, timed 26.92 and 24.52, the same as the oracle.

**The wheel-spin gauge is on hold, at his word.** The car has no wheel spin to measure: its wheels have
no speed of their own, and the tyre goes straight from the engine's push to the grip's limit (2026-11-22
entry). A gauge of his ratio, wheel speed over ground speed, would read 1.0 all the time. Told so, he
said *"If there is no wheels spin i'll reconsider"*. Offered real wheel spin (each wheel with its own
speed; a real change in feel and a retune, his tune included) with the gauge on top, **he said "Not for
now"**. So the car keeps its tyre model with no wheel speed, PLAN's "no simulated wheel spin" stands,
and there's no gauge. If he opens it later, the gauge is the easy half; the wheel speeds are the work.

## 2026-11-23 — the autodriver's aggression, on - and +

His word, after driving with it: *"i like the autodriver i only wish i could tune the aggressiveness"*.

**Built:** its aggression is its caution, the share of the tyres' grip the speed plan uses for corners
and braking, **50% to 100% in steps of 5%**. `-` and `+` (the `=` key, the number pad's too, and the
D-pad, which no pad layout rebinds) change it, while it drives or before P. The banner shows it, and the
drawn racing line recolours: **its colours are now scaled to the default plan (80%)**, not to whichever
plan is drawn, so bolder turns the corners greener and calmer turns them redder. It holds between
handovers, and `--caution=` sets it at start for the game as for the lap oracle. Steering doesn't change
and the line doesn't move: bolder means more speed on the same line. `Autodriver.Caution` is now
settable and replans the whole lap at once. The line is also redrawn at each handover, for the car's
numbers then; before, it stayed as drawn for the spec loaded at start, so after a panel tune the line
shown and the line driven could differ.

**Swept with the lap oracle** (flying lap):

```
aggression  shipped a2e24a2b                           his tune 128eb3c6
60%         27.16 s  clean, worst slide 7°             26.33 s  1.48 s off the road
70%         25.61    clean                             24.98    1.41
75%         25.00    clean                             24.47    1.45
80%         24.52    clean, slide 16°                  24.03    1.53
85%         24.31    0.75 s off into corner 1, 22°     23.68    1.62, no spins   (his quickest)
90%         27.56    spins twice on lap 2              23.75    1.47, no spins
100%        48.30    9 spins, 11 s off the road        37.66    22 s off the road, 2 spins
```

**What it says:**
- **His tune runs wide into corners 1 and 3 at every aggression, even 60%**, by about the same 1.5 s.
  So it isn't speed that takes it off the road there, it's how his car brakes: still the first suspect
  is the front anti-roll bar at 0, and the brake balance is the second.
- **His tune takes more aggression than the shipped car**: quickest at 85%, 0.35 s faster than at 80%,
  and no spin even at 90%. The shipped car's edge is between 85% and 90%. (At 85% it spun five times a
  lap before the bank ease, the yaw damper and the brake guard; now it only runs wide.)
- **100% is a crash show for both**, kept as the top of the range so he can see where the edge is.
- **The default stays 80%**, the shipped car's quickest clean lap.

Checked: the oracles on the committed spec read exactly as before (lap 24.52 s at 80%), 100 tests
(`TurningUpItsAggressionReplansFasterCornersWithinItsRange`), and screenshots of corner 1 at 60% (red)
and 100% (yellow-orange), and of the banner while it drives. Not checked: the D-pad on his pad, where
Godot's names for the buttons don't match the clone's; the pad check saw all four D-pad directions.

## 2026-11-23 — the autodriver, on a minimum-curvature racing line

His ask was an autodriver (PLAN, "His ideas"), and mid-build he added: *"draw a line around the track
that minimizes the cornering and aim for this position taking into account the wheel positions"*.

**Built, in Core (`Driving/`):**
- `RacingLine`: a road sampled every metre, with curvature, bank and a speed plan (the banked-curve
  formula at a share of grip, the caution, then a backward braking pass).
- `RacingLine.MinimumCurvature`: each point moves only across the road, to where the sum of squared
  second differences is least, `(4(p₋₁+p₊₁) − (p₋₂+p₊₂)) / 6`, clamped to the room left once **the wheels
  are kept on**: half the flat road, less half the track and half a tyre, less a margin (0.5 m). It's
  coarse to fine: every 16th point, then 8, 4, 2 and 1. One level at one metre took 40,000 sweeps and
  still used only 3 of the 3.6 m of room; coarse to fine uses all of it in 29 ms. On the corner lap it
  cuts total squared curvature by 21%, and every corner's slowest point rises (55→61, 86→100, 48→55,
  60→69, 63→73, 71→87 km/h at caution 0.85).
- `Autodriver`: a Stanley tracker at the front axle with a curvature feed-forward, a yaw damper, a speed
  plan held with throttle and brake, and slip guards that ease throttle and brakes as the tail steps
  out. It drives through the same `DriverInput` a player does.

**What the building found, in order:**
1. **Pure pursuit cut inside every bend**, and on a line already at the inside edge that put the inside
   tyres on the sand: the kinematic lap test strayed 4.55 m from the middle, against 3.85 m allowed.
   Hence the front-axle tracker.
2. **Reading the line a little ahead of the axle left a steady offset**: on a 30 m circle the car sat a
   metre inside at 15 m/s. Reading it at the axle, turning the direction by the bend over the
   half-sample gap, and comparing the body with the line at its middle (where a body on a bend is square
   to it) brought it to 1-3 cm. `ItHoldsACircleToAFewCentimetres` pins it.
3. **The 35° bank launched the car.** It pivots on its inside edge, so its outside stands 5-6 m up, and it
   eased flat over the last quarter of the corner, 12 m: a wide exit dropped 4.8 m over a crest, and at
   68 km/h all four wheels left the road for 0.3 s and it landed in a spin. **The corner lap's banks now
   ease over half the corner each way** (`TestGroundLayout.BankEase`). A player taking a wide exit would
   have flown too.
4. **Caution matters most**: at 0.85 of grip the shipped car spun five times a lap. This car's real
   limit sits below the textbook's, because throttle takes rear grip. Swept: 0.80 is clean and fastest.
5. **A yaw damper** (steer against turning faster or slower than the line asks) calmed a weave at speed
   and a turn-in under braking, both seen in a recorded run of his tuned car.

**The lap oracle** (`--oracle=lap`, `--caution=`): a standing lap from station 6, then a flying one.

```
shipped a2e24a2b  caution 0.80  flying 24.52 s  standing 26.90  off-road 0.00 s  widest off line 1.43 m  slide 16°  spins 0
  corners driven/planned km/h: 58/59, 82/96, 52/52, 64/65, 68/67, 81/84
his tune 128eb3c6 caution 0.80  flying 24.03 s  standing 26.67  off-road 1.53 s (1.23 into corner 1, 0.31 into 3)
                                widest 2.11 m  slide 16°  spins 0
```

**So his tune is half a second a lap quicker, but runs about 2 m wide under braking into corner 1**,
where the shipped car doesn't, with the same driver. His tune has the front anti-roll bar at 0 and much
more power. That's the comparison the autodriver was built to make. Corner 2 is driven well under plan
(82 against 96) on both: the fast kink comes after a short straight, and the car can't reach its plan's
speed in time.

**In the game:** the racing line is drawn on the corner lap, coloured by planned speed (red slow,
green fast), and L hides it. P hands the car to the autodriver when it's within 15 m of the lap, and
gives it back; `--autodrive` starts with it. The oracles on the committed spec: the M1a four, jump, loop
and bumps read exactly as before. 99 tests.

## 2026-11-23 — station 6, a lap of corners; and the panel's mouse wheel

His ask: *"for me to really feel this out any further like i'll need corners and stuff of varying
degrees and angles of bank like but like dont go overboard its all going to mean more tiles"*. So one
small closed lap, not a circuit's worth: **station 6, six corners, each different**, which doubles as
the shortlist for M2's corner tiles. Whichever feel right are the ones worth making into pieces.

```
corner  angle   radius  bank   length  grip limit, point mass at grip 1.1
1       90 R    25 m    flat   39 m     59 km/h
2       45 R    60 m    10°    47 m    110 km/h   a fast kink
3       135 R   18 m    20°    42 m     75 km/h   tight
4       60 L    30 m    flat   31 m     65 km/h   the one left-hander
5       90 R    30 m    35°    47 m    173 km/h   steep
6       60 R    40 m    15°    42 m     99 km/h
```

The lap is 526 m. **It closes exactly because two straights are solved for, not guessed**: the end
of the chain is linear in their lengths, so three trial builds and a 2 × 2 solve give 85.2 m and
86.9 m. `TestGroundLayout.CornerLap` does it, and the tests hold it: it closes to a millimetre, both
solved straights are real (15-200 m), every corner differs, and it stays 30 m from every station. The
grip limits are the textbook banked-curve formula at the car's grip of 1.1 (bank and friction
together); they're a guide to where the edge is, not what the car does, which is his to find.

Built on keys 1-6 and `--station=6`. Seen from above: closed, the left-hander dents inward, and the
steep corner's outside edge stands up. **The oracles on the committed spec (`a2e24a2b`) read exactly as
before**, so the lap touched nothing else.

**And a bug of mine, found in his tuning.** His uncommitted `buggy.json` now has deliberate changes
(power 110 → 192.5 kW, drive force 4500 → 5580 N, grip 1.1 → 1.214, peak slip 8 → 15.8°, slide grip
0.8 → 0.709, mass 650 → 746 kg, wheelbase 2.6 → 3.018 m, forward priority 0.3 → 0.248, minimum lock
10°, pedal rate 6). It also has a dozen one-step nudges (spring 15900, rebound 2080, bump stop 9.9,
rear anti-roll 1950, brakes 8980 at 0.602, drag 0.855, wheel radius 0.3795, reverse top speed
10.049999). That's the signature of **the mouse wheel moving whichever slider was under the pointer
while he scrolled the panel**. Sliders now ignore the wheel (`Scrollable = false`), so it scrolls the
list and nothing else. **His file is untouched and uncommitted**, at his word; which nudges to undo is
his call, and the panel's yellow shows them.

## 2026-11-22 — the binding run, and his pad's layout

On his "Build it": `--bindpad` (`Diagnostics/PadBinder.cs`) asks for each game control on the pad in
turn, records what the pad sends, and saves it as that pad's layout, `user://pads/<guid>.json` (a
`PadProfile`, in Core: strict JSON, tested). `Controls` loads it at start-up and when a pad connects:
**for a pad with a saved layout, every default pad binding is taken off and the layout's put on**, so
a clone whose numbers don't match their labels can't fire the wrong control. Keys are untouched.
`--padcheck` now says whether it's checking a layout or the defaults. (It's his mum's clone pad, he
said, not his old Logitech; a Logitech would very likely just work with the defaults.)

**The first run bound nothing**: he was away from the pad ("i missed it, sorry claude was smoking"),
every prompt timed out, and it **saved an empty layout, which would have left the pad dead in the
game**, since loading a layout removes the defaults. Fixed three ways before the second run: it waits
for a button press before starting, as the pad check does; it never saves an empty layout; and
`PadProfile` refuses one (`AnEmptyLayoutIsRefused`), so even a stray file can't silence a pad. The
empty file was deleted.

**The second run bound all eight:**

```
steer left/right  left stick, axis 0 -/+
throttle          right stick up, axis 3 -   (analogue: this pad's L2/R2 are only buttons)
brake/reverse     right stick down, axis 3 +
wide lock         button 6   (Godot's name for 6 is "Start/Options", which shows how far off the labels are)
handbrake         button 1   (does nothing until M1d)
respawn           button 0
camera            button 3
```

The game loads it at start (`Pad layout for "Microntek USB Joystick" loaded: 8 controls`). **Not yet
seen:** him driving with it.

## 2026-11-22 — the pad check, on his pad

He fetched a PlayStation-style pad to catch oversteer with, and asked for *"a sensor run on my
gamepad/dual joystick"*. Built `--padcheck` (`Diagnostics/PadCheck.cs`): it lists every pad Godot sees,
waits for a first touch, records each axis's range and each button's presses for 30 s, takes the
sticks' drift over 3 s hands-off, checks the game's own controls, and writes `user://padcheck.txt`.

**What it found:**
- **The pad is a generic clone**: "Microntek USB Joystick", vendor 0x0079 (DragonRise), product 0x0006,
  GUID `0300be79790000000600000000000000`. Not a real DualShock.
- **The left stick works**: −1 to 1 both ways, 0.008 of drift at rest. The 0.15 dead zone is ten
  times what it needs.
- **The right stick and both triggers never moved as axes**, while the four face buttons took 10-12
  presses each against 1-2 for everything else. That's the signature of these pads in *digital*
  mode, where the right stick presses the face buttons. Rolling it round "all the way, slowly"
  would press them over and over. Their ANALOG button should switch it.
- **L2 and R2 look like plain buttons on this pad**, not pressure triggers, so analogue throttle has to
  come from the right stick's up and down, if anywhere.
- **The game's controls read "OK" for wide lock, handbrake, respawn and camera, but that only means
  *something* produced those button numbers.** Godot has no layout for this pad, so its button
  numbers are the pad's raw ones, and the PlayStation names in the report are guesses. Throttle and
  brake read NEVER.

**Second run, with ANALOG pressed (23:46): the digital-mode reading was right.** Both sticks now read
the full −1 to 1 both ways with no drift at all, the face buttons dropped from 10-12 presses to 0-3,
and all four D-pad directions register. **L2 and R2 still never move as axes**, so on this pad they
are plain buttons. Triangle and R1 read no presses although he went round every button, so the
button names the report prints are not the physical buttons: Godot has a mapping for this GUID, but
it doesn't match this clone's layout. The first report is kept as `padcheck-digital.txt`.

**So for this pad:** steering on the left stick works now, and analogue throttle and brake can only
come from the right stick (up and down). Which physical button lands on which game control can only be
found by asking for them one at a time.

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

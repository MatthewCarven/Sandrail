# Worklog

Newest first. What happened and why; findings worth keeping.

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

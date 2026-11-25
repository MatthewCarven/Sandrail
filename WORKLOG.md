# Worklog

Newest first. What happened and why; findings worth keeping.

## 2026-11-25 — The autodriver's aggression stays put between tracks

His ask: *"make the driver aggression stay the same between tracks so once you have changed it down it stays put until
the next restart"*. Until now every load (a new track, a seed, a change of car, the editor's test drive, a profile's
next car) began the scene again at `--caution=` or 80%, so a change with - or + lasted only as long as the track.

**What's built**: - and + now also keep what they set in a static, `Main._cautionChosen`, which outlives the scene the
way `NextArgs` does and goes when the game is closed. A scene takes it before anything else, and over `--caution=`,
since every load carries the flag along. Until - or + is pressed nothing changes: a launch, the oracles and the
profiles start where they did.
- **A profile is untouched**: it sets the caution run by run (50% up by 5%) without going through - and +, and when it
  ends it hands back the caution it began with, which is now his chosen one on every track of Profile every car on
  every track as well.

**Checks**: headless, with a temporary hook (since removed) pressing - twice: launched on the shakedown with
`--caution=0.9`, 90%; after two presses, 80%; Seed 5 loaded from there, 80%; a change of car there, 80%. The headless
check passes. Nothing the oracles drive reads it.

## 2026-11-25 — A fresh physics world for each load: a run is the same however it's reached

His yes to option 1 of "Why a run depends on what was loaded before it", two entries down: *"Yes, build it"*.

**What's built**: `Main._EnterTree` gives the window a new `World3D` on every load, at launch and after a reload
alike, before any of the scene's bodies exist. Each world has a Jolt of its own, so its body IDs start from nothing,
and a scene builds its bodies in the same order every time. The chassis's contacts with the road, which Jolt solves
in an order hashed from those IDs, now come out the same at launch, after a change of car and after any other track.
- **The engine error the trial printed is gone.** The trial dropped the old world as it handed the window the new
  one, and the old world's scenario was freed while the window still used it (`scenario is null`). Now the old world
  is held until the window has the new one, then let go.
- **Nothing else changes**: not the car, not the tracks, no fingerprint or ghost. Rows already in his sheets from a
  reload stay as they were; profiling those tracks again tidies them.
- **For whoever adds a body later**: build it inside the scene, in a fixed order, and never hold a world or its space
  across a reload (CLAUDE.md, Layout).

**Checks**, on scratch copies of his tracks and garage, with scratch `--ghosts=` and `--profiles=`:
- **Every car on every track, three ways**, on his 12 sound tracks with the stock car and his 3 active ones:
  - chained, as `--profile=everywhere` runs it, every track reached by a reload: 115 runs;
  - per track, `--profile=all --track=`, four at a time, each car after the first reached by a change of car;
  - each of his 3 cars on each track from a launch of its own, `--spec= --profile=this`: 36 runs.
  **All 115 rows are the same all three ways, cell for cell.** The same comparison before had 14 of 115 apart. His
  unfinished `1` was left out and `gen-5.json` skipped as Seed 5, as before.
- **The old world is freed on every load**, checked with a temporary probe (since removed) over a launch and two
  reloads.
- **His tracks and garage**, hashed before and after: the same 26 files.
- **The oracles**: all seventeen print the same lines as before, character for character. Tests (349) and the
  headless check pass.
- **In the window**: Every car on every track over the game's two tracks (Seed 5, then the shakedown after a reload)
  matched a launch of each, cell for cell, with no engine error, and a frame on Seed 5 is right (terrain, rocks,
  shadows, sky).
- **Comparing rows**: `when` and `version` left out. The version is the commit, so it differs between builds.

## 2026-11-25 — The Profile buttons under one Profile button

His word on the suggestion at the end of the Profile every car on every track entry: *"Yes, nest them"*.

**What changed** (`Ui/SeedMenu.cs` only; `Main.cs` passes the same three actions as before):
- **Profile this car, Profile every car and Profile every car on every track** now sit under one **Profile** button,
  where the three were. It opens them as a list under it, the way Change car opens the garage, in the list rows'
  smaller type.
- **The note under the menu** says what each does while the list is open: *"This car: yours, here, from 50% aggression
  until a run isn't clean; every car: the stock car and your active ones; every track: each of Load a track's in
  turn, then back here. Esc stops it; the rows written stay."*
- **One list at a time**, as before: opening Profile closes Change car's or Load a track's list, and the other way
  round. Closing the menu closes it too.
- **On the test ground** the list holds only Profile every car on every track, since there's no race to profile there.
- Open the profiles folder stays a button of its own.

**Checks:**
- **The menu in the default window** (1152 × 648), through a temporary probe (since removed) that pressed the real
  Escape key and clicked the real buttons:
  - On his `gen-746109v1`: the menu closed; Profile open with its three rows; Change car opened, which closed the
    Profile list; Profile opened and closed again.
  - On the test ground: Profile open with its one row.
- The headless check passes on the test ground and on the shakedown, tests (349) pass, and the profile code isn't
  touched.

## 2026-11-25 — Why a run depends on what was loaded before it: the chassis on the road, not the rays

Profile every car on every track left this found and not fixed: the same profile run can come out a little
differently at launch, after a scene reload, or after another track, and building the shakedown's pieces in reverse
moved the same cells. The working theory was a wheel's ray meeting two pieces at a seam at the same distance, with Jolt
reporting whichever it held first. **That theory is wrong**, and the real cause has a cheap fix that touches neither
the car nor the track. It's written here for him to choose; nothing is changed.

**No wheel ray ever ties.** A temporary probe logged every wheel ray of the stock car's sweep on the shakedown, tick by
tick, and cast each ray a second time with the collider it hit left out:
- **The shakedown**: 8 runs, 54,625 ticks, four rays a tick. No second collider within a millimetre of the first, in
  launch order or with the pieces built in reverse.
- **Seed 635508** (the 3-lap circuit where two clean times moved by 0.01 and 0.02 s), on the generator's own ground:
  8 runs, 152,616 ticks. No exact tie. The nearest was a wheel past the road's edge that met the slab's side 0.6 mm
  before the ground, and the nearer one is reported whatever the order.
- **The ground is built not to**: level road is lifted 5 cm above the plain (`TestGroundLayout.Lift`), and the bed is
  set 20 cm under the road (`Bed.UnderRoadM`). Where two pieces meet, a ray lands on one side of the seam or the other.

**It's the chassis touching the road.** Logged in both build orders, every run that reaches the loop is the same bit
for bit up to it. In every one of them the first difference comes on a tick where the chassis box is touching the
loop, two contacts becoming four or six. On that tick all four rays report the same piece, distance and normal both
ways, so the car's own forces are the same, and yet the velocity coming out of the tick differs in its last bits. The
difference is in how Jolt solved the box's contacts. Seed 635508's runs part first at its loop too, 7 of 8, on ticks
where all four rays agree both ways (the eighth goes wrong before the loop and is the same both ways). Six of its rows
moved by a hundredth or a tenth of a km/h, all clean both ways.
- **Where the chassis touches**: about 150 ticks (1.25 s) in the loop, and 1 to 8 ticks on each jump's take-off and
  landing. That's the bottoming M1c found (the `loop` oracle's 2.89 s on the bump stop). The wheel's compression stops
  at its travel, so past that the box carries the car.
- **Why order matters there**: Jolt solves a tick's contacts in an order sorted on a hash of the two bodies' IDs and
  the triangles touched. With several contacts at once (the loop's six), the IDs choose the order, and the order
  changes the last bits.
- **Why a reload changes the IDs**: a Jolt body ID is a slot and a count of how often the slot has been used. A freed
  slot is handed out again last freed first, with its count one higher. So after a reload every body's ID differs from
  the launch's, even in the same slots, and after another track the slots differ too. Reversing the build gave the
  loop another slot, and moved the same cells. (Jolt's source: `SubShapeIDPair::GetHash` over both `BodyID`s,
  `ContactConstraintManager::SortContacts`, `BodyManager`'s free list.)

**Seen on the way: the contact monitor changes the car.** Turning on Godot's contact reporting for the car (to log the
touches) also turns off Jolt's manifold reduction for it, and that alone moved three of the sweep's eight rows by a
hundredth or a tenth of a km/h. Nothing in the game turns it on. The figures above that compare rows came from the probe
with it off, which matched the plain game cell for cell.

**The options**:
1. **A fresh physics world for each scene load** (recommended). `Main` gives the window a new `World3D` as it enters
   the tree, before any of its bodies exist. Each world has a Jolt of its own, so its IDs start from nothing, and a
   scene builds its bodies in the same order every time, so a run reached by any road gets the same IDs as at launch.
   - **Tried, then taken out**. The shakedown reached after Seed 5 (`--profile=everywhere`) had 3 rows of 8 apart
     from a launch. With a fresh world it was the launch's, cell for cell, and so was Seed 5. A launch with it is the
     same as without, cell for cell. **All seventeen oracles printed the same lines with it, character for
     character.**
   - **Costs**: a few lines in `Main`, no change to the car or the track, no oracle moves, and no fingerprint or ghost
     changes. Clean and unclean runs alike, whatever touches what.
   - **One wrinkle to tidy**: the swap as tried prints one engine error at the start (`scenario is null`, from the
     renderer letting go of the old world). The frame is right (sky, sun, shadows, the car on its start) and the run
     goes on. The real version would find the clean way to swap, or show the error is only noise.
   - **What it doesn't do**: rows already in his sheets from a reload stay as they are; profiling them again tidies
     them. It makes a run the same however it was reached on this machine. Whether another machine gives the same
     figures is a separate question.
2. **Keep the chassis off the road in a clean run**: more travel, or a bump stop that holds the car off the road
   past the travel. A clean run then never touches, and the order can't matter. **But it's the car's physics**: it moves
   every oracle with a jump or the loop, it changes how landings feel, and the bottoming is his lever to bring to his
   play. Unclean runs (spins, rolls, rocks, banks) still touch, so they'd still differ. Worth his play for the feel,
   not for this.
3. **A launch for every track**: each launch is already the same every time. But it means restarting the game for
   every track and car, which a menu button can't do neatly. Option 1 gets the same result without the restart.
4. **Not these**, the ones the theory pointed at:
   - **A tie-break in the wheel raycast**: there are no ties to break.
   - **One collider for the whole road**: the hash holds the car's own ID too, and a merged road's ID changes on a
     reload like any body's.
   - **Pieces without shared seams**: seams don't tie.

**For him**: option 1? It's small, it moves no figure he has, and after it a profile row is the same whether it came
at launch, after a change of car, or after any other track.

**Checks** (scratch `--ghosts=`, `--profiles=`, `--tunes=` and `--saved=` throughout, his folders untouched):
- The stock car's sweep on the shakedown, plain, twice, with six games running at once: the same, cell for cell.
- The probe's extra rays, contact monitor off: the same as plain, cell for cell. With it on, 3 rows moved.
- `--profile=everywhere` over the game's two tracks against a plain launch of each: the shakedown, reached after Seed 5,
  3 rows apart. With a fresh world: none.
- A launch with a fresh world: the same rows. The oracles with and without: the same 23 lines.
- In the window: one screenshot run with a fresh world, a frame at rest and one driving, both right.
- The probe and the fresh-world switch are taken out again; the game is as it was.

## 2026-11-25 — Profile every car on every track

His ask: one more button, every car on every track. He knows the menu is getting long and is fine with one more.

**What's built** (`Main.ProfileEverywhere.cs`, beside the profiler):
- **Profile every car on every track**, in the Esc menu under Profile every car. It runs Profile every car on each track
  in turn, unattended, and comes back to the track and car it began on. It's offered on the test ground too, since it
  doesn't profile the place it starts from.
- **The tracks are Load a track's**: the game's own, then every saved one, newest first. A track that won't lay soundly
  is left out at the start (his `1` is one). A track whose fingerprint was already profiled in this run is skipped when
  its scene comes up: the fingerprint is the one the scene makes as it builds the track, so that costs a load and
  nothing more. His `gen-5.json`, saved before codes, is the game's Seed 5 under another name, and was skipped. No
  random seeds.
- **The cars are Profile every car's**: the stock car and his active ones. Each track starts with the first of them,
  so its sweep needs no change of car.
- **Underneath it's Profile every car, unchanged**: the same sweep, rows in each track's own sheet, the profile ghosts,
  and rows held while a spreadsheet has a sheet open. Tracks start without `--record`, `--autodrive` and `--ghost=`. A
  `--ghost=<car>` would refuse a track where that car hasn't been profiled, and quit. The way back puts them back.
- **The board** shows two lines: `PROFILING   track 2 of 13: Shakedown   Esc stops`, then `car 2 of 4:
  matthew-2026-11-23-original-3-2-2 at 50%`. The race board steps down under a two-line banner; under one line it stays
  where it was. Saved tracks go by their file names on the board, since a saved generated track and the game's share a
  name.
- **Esc** stops the lot and goes back. Going back, at the end or after Esc, the board shows three lines for 15 s: how
  long it took (and where it stopped), the tracks profiled, runs and new best ghosts, and the skipped tracks with why.
  The log gets the same on one line, each unfinished track's first problem, and how far in each track started.
- **`--profile=everywhere`** does it from the command line. It needs no `--track=` or `--seed=`, writes the rows and
  quits.

**Fixed on the way: a sweep's first run came out a hundredth slow after a change of car.** P4 saw it and left it "not
pinned down". It's pinned down now. A sweep's first run started from a deferred call:
- **At launch** that call lands inside the first physics tick, where every later run starts (from `ProfileTick`, in
  the tick).
- **After a scene reload** (a change of car, and now every track) it lands between frames. The car was put at the
  start one tick later than the race's two ticks of arming count from, so it had one tick less to settle before the
  clock started.

The stock car's 50% on the shakedown was 1:04.34 reached by a reload and 1:04.33 at launch. The first run now starts
on the next tick, in `ProfileTick`, like the rest: 1:04.33 both ways. Rows from a launch are the same as before, cell
for cell: the shakedown, Seed 5 and his `4`, profiled before and after. Also fixed: the line Profile every car leaves
when it comes back to a different car was covered at once by the track's opening line. It now shows after it.

**Found, not fixed: a run can depend a little on what was loaded before it.** With the first-run fix in, a launch and
a reload still differed in a few cells further up a sweep: a 0.1 km/h here, a loop 0.01 s there. Building the
shakedown's pieces in reverse order at launch moved those same cells, so it's the order the physics engine holds the
pieces in. Where two pieces meet, which one a wheel's ray reports depends on that order, and a reload, or the track
loaded before, changes it. Jolt hands out freed body IDs last freed first, which is most likely how. Measured over his
tracks (below):
- **Clean runs** move by a hundredth or two at most, and most don't move at all.
- **A run that has already gone wrong** (off the road, spinning) can go another way: one crash came out 7.34 s apart,
  and two stopped somewhere else.
- **Whether a run is clean never changed**, so neither did any car's limit or best clean time.

Each way on its own is the same every time. Fixing it would mean the car breaking a tie at a seam the same way
whatever the order, or one collider for the whole road: a change to the car's physics or the track's build, which
moves every oracle, so it's his to choose.

**Checks**, on scratch copies of his tracks and garage (`--saved=`, `--tunes=`), with scratch `--ghosts=` and
`--profiles=`:
- **A small folder first** (the game's two tracks, a copy of `gen-5v1`, his `4`, a half-built track): 3 tracks profiled,
  the half-built one left out, the copy skipped as Seed 5. Rows match plain `--profile=all` on each track, cell for
  cell, and match again on the final build.
- **`--profile=everywhere` on his tracks, headless**: 14 in the list, his `1` left out as unfinished, `gen-5.json`
  skipped as Seed 5. That leaves **12 tracks profiled, 115 runs, 11 new best ghosts, in 26 min 36 s**. Every track's
  sheet got its rows (4 to 12 each).
- **The same in the default window**, from the menu, started on his `gen-746109v1` with `-original-3-2`: **25 min 55
  s**, the same pace (×16.0 on the board). It came back to that track and car.
- **Against a plain `--profile=all --track=` on each of the 12 tracks** (run four at a time): the same 115 runs.
  - 101 rows the same, cell for cell.
  - Clean runs: finish times differ in two rows, both on Seed 635508 (3 laps of 1.45 km, about 3 minutes a run), by
    0.01 and 0.02 s.
  - Unclean runs: some differ as above.
  - **Each car's limit and best clean time are the same on all 12 tracks**, and the window run's are too: 48 of 48
    both ways.
- **Esc in the window**, through a temporary probe (since removed) that pressed the real Escape key and clicked the
  real menu button. It started on `gen-746109v1` with `-original-3-2` (`def35523`) and pressed Esc 150 m into car 2's
  run on track 2, the shakedown. The log: *"Every car on every track stopped in 1 min 17 s, stopped on Shakedown
  (track 2 of 13): 1 track profiled, 16 runs; 2 new best ghosts; 1 skipped: 1.json (unfinished)."* It came back on
  `gen:746109v1` with `def35523`, the menu closed. Frames of the banner and the summary, before and after the layout
  fix, are how the two layout problems were found.
- Tests (349) and the headless check pass. His real tracks and garage, hashed before and after: the same 26 files.
  The car isn't touched, so the oracles weren't run.

**For him:**
- **What he's starting when he presses it**: about 26 minutes on his tracks as they are now, about 2 minutes a track.
  It grows with every track he saves, and with every car that's clean far up the sweep.
- **His three active cars aren't clean even at 50% on any of the 12 tracks.** Each drives one run a track, and only the
  stock car sweeps (clean to 65-85%; on his `3` not at all).
- **The menu**: the three Profile buttons could go under one Profile button that opens them, the way Change car opens
  its list. Not done: his to say. (Done the same evening at his word: "The Profile buttons under one Profile button",
  above.)

## 2026-11-25 — Time up to ×16, and the board says how fast it really runs

His ask: *"can we adjust the time multiplier so it allows up to 16x but otherwise still does all the ticks required
to get there accurately i dont mind if the computer has to run slower just indicate the speed to me please"*; ×4, ×8
and ×16 as the new steps (*"yes thanks"*).

**The accuracy was already there.** A tick is always 1/120 s of game time (`GameClock`, 2026-11-24). A computer
that can't keep up never takes longer steps: Godot just runs the ticks later, so game time goes slower than asked.
What stopped ×16 was Godot's limit of 8 ticks a drawn frame, which is about ×4 at 60 frames a second. The profiler
already got round it at ×16 by allowing 64 ticks a frame.

**What changed:**
- **`,` and `.` (and the D-pad) step through ×0.1, ¼, ½, 1, 2, 4, 8 and 16**, from `GameClock.Steps`. `--time=` is held
  to ×0.01 to ×16.
- **The most ticks a frame rises with the speed**, in `GameClock.SetScale`: enough to keep up down to 30 frames a
  second, at least Godot's 8. That's 16 at ×4, 32 at ×8 and 64 at ×16. The profiler's own setting went, since this
  is the same rule; its rows are unchanged.
- **The board shows the speed** beside the km/h whenever it isn't ×1: `time ×16 · running ×15.7`. The second number
  is the ticks Godot really ran over the last two seconds, measured four times a second, and it turns amber when
  it's a tenth or more short. A frame half a second late or more (the Esc menu, a load) starts the measure again.
- **The messages at the bottom and the F3 readout count real seconds**, since Godot's frame delta is game time. At
  ×16 a two-second message had lasted an eighth of a second, and at ×0.1 twenty seconds.

**What it runs at on this computer**, the autodriver at 80% in the 1152 × 648 window, at about 130 frames a second:
- shakedown at ×16: ×15.8 over the drive, 56.73, splits 21.52 and 40.61, as at ×1;
- seed 5 at ×16: ×15.6 over the drive, and ×14.8 to ×16.0 two seconds at a time; 1:26.39, as at ×1.

**Why it isn't quite ×16, and why the readout averages two seconds.** Godot's frame timing loses a little even far
from the limit: allowing 96 ticks a frame made no difference. Half a second at a time the reading swung from ×13.6 to
×16.9 (a frame's hitch, then the catching up after it), and a second at a time from ×14.8 to ×16.3. At ×16 a tenth
short is ×14.4, so normal running stays white.

**Checks:**
- **Same results at ×16 as at ×1:** the autodriver's ghost from each drive, snapshots compared byte for byte,
  identical on the shakedown and on seed 5.
- **The profile is unchanged:** the stock car on the shakedown headless gave the same `Profile:` lines and rows,
  in 30.2 s against 30.0 s before.
- **Seen on screen** at ×16 and at ×0.25 (reading ×0.25), with a probe since removed.
- Tests (349) and the headless check pass, and the seventeen oracles match P4's line for line (`track` 56.76,
  `ghost` 1704 snapshots, on `4f918acd`).

## 2026-11-25 — The viewer sorts by three columns, joins a track's files, and has All tracks

His ask: *"the best way to implement multi sort columns like i want to sort by date then by track name and maybe a
3rd? 3 drop down boxes with the column above the sheet?"*. Two things stood in the way. First, each sheet is one
track, so sorting by track changed nothing. Second, the viewer could already sort by several columns (Shift-click a
header), but nothing showed the order. His choices, all as recommended:
- **Date means date and time.** The sort uses `when` itself; no `day` column.
- **All tracks with the shared columns only** (*"yes please"*).
- **A track's old and new files joined into one sheet** (*"yes please"*).

**What's built** (`tools/profile-viewer/`; the game is unchanged):
- **Three boxes above the table:** `Sort by [column] [↑] then [column] [↑] then [column] [↑] [Clear]`. Each box lists
  every column of the sheet, the car's included, or (none), and its arrow sets the way (up: smallest, earliest or A
  first). Clear goes back to the order driven.
  - A header click fills the boxes (Shift-click the next).
  - The sort stays from sheet to sheet, and between launches in the viewer's settings. A column a sheet hasn't is
    passed over, and the line at the bottom says so.
  - A new first column builds the table again, so its header shows the arrow. Dear ImGui's report from a table just
    built is told apart from a click, so it doesn't throw away the other two columns.
- **A track's files read as one sheet.** The list says `Seed 5  0070ecd8  (2 files)` for its `(2)` and `(written
  while open)` files. Headers are merged with each column where its own file put it (`track` after `when`), rows go in
  the order driven, and an older row's track comes from its file's name.
- **All tracks at the top of the list:** every track's runs, with the run's, the summary's and the car's columns, and
  `track` kept at the left with when, driver, car and caution. Splits, laps and pieces stay on the tracks' own sheets,
  since `03 straight` is a different piece on each track.
- **From a command line**, `--sort=` is given once per column, and `--sheet="All tracks"` picks the entry.

**Checks:**
- **Tests:** the viewer's 53 (14 new) pass, and they read his real profiles folder and leave it untouched.
- **His folder on screen** (`--no-settings`, which writes nothing): All tracks sorted by when (latest first), then
  track, then time.
- **A joined sheet on screen:** a scratch copy of the shakedown's old and new files, 19 runs, the old ones given
  `Shakedown`.
- **His folder now holds only today's track `4` sheet**, started at 17:16 with this morning's build: the older sheets
  are no longer there. So he'll see a joined sheet only once a header changes again.
- The arrows needed no font range: DearPyGui 2.3 loads a font's characters as they're drawn.
- `SandrailProfiles.exe` is rebuilt in the main checkout's `tools/profile-viewer/dist/`.

## 2026-11-25 — Every profile row says its track

He asked whether the map's name is kept with the car's data (*"can we just check that the map name is stored in the
car data please its just i couldn't see it"*). It wasn't in the rows:
- **Profile sheets:** the track's name was only in the sheet's file name (`Seed 5 0070ecd8.csv`, `4 847ed7b9.csv`).
  The viewer's list showed it, but no row did, in the viewer's table or in a spreadsheet.
- **`cars.csv`:** this is one row per car, the same on every track, so it has no track and still doesn't.
- **Ghosts:** a ghost's header already carries the track's name, ID and fingerprint.

At his word (*"Yes, add it"*), **every row now has a `track` column**, the track's name, right after `when`. It holds
`Shakedown`, `Seed 5`, or the name he gave a track of his own.

Since the header changed, an older sheet is left as it is and the next row starts a new file beside it, as the sheet
has always done: `Seed 5 0070ecd8 (2).csv`. His `Seed 5 0070ecd8.csv` and `4 847ed7b9.csv` keep their rows, and the
viewer lists the new file under them as `Seed 5  0070ecd8  (2)`. The viewer needed no change, since a column it
doesn't know goes with the run, shown at first. A test says so now.

**Checked:**
- Profiled the stock car on the shakedown into a scratch copy of P4's folder, whose sheet has the old header. The rows
  went into `Shakedown 8a6323aa (2).csv`, with `track` reading `Shakedown`.
- Its eight rows are the old file's eight stock rows, cell for cell, apart from `when`, the new cell and the version
  stamp (`c9bf1d4` against `c972d6c`). It was clean to 80% (56.73), as before.
- Tests (349) and the viewer's tests (39) pass.

## 2026-11-25 — The Esc menu scrolls when it's taller than the window

The menu ran off the default 1152 × 648 window. SANDRAIL was already cut off the top, and P4's Watch a car's profile best
also cut off half of Quit (the entry below). Now it scrolls, at his word (*"Yes, make it scroll"*).
- **When it fits, nothing changes.** In a 1152 × 1000 window with no list open, it shows whole and centred as before.
- **When it doesn't fit**, the menu fills the window's height with 16 px above and below, and its rows scroll, with a
  scrollbar beside them. Godot widens the menu by the scrollbar's 8 px, so the rows keep their width.
- **The note stays in sight.** It sits under the rows, outside the scroll, so what a button says, such as "Saved to
  ...", can't be scrolled out of view.
- **It opens at the top** every time, and the menu follows keyboard focus, so Tab never leaves the focused button out of
  sight.
- **It fits itself again** whenever the rows, the note or the window change size.
- **Lists inside it are unchanged.** Load a track, Change car and the profiled cars' list keep their own scrolls and the
  height the window leaves them.

How it was seen: a temporary probe, since removed, opened the menu on seed 5 against a scratch copy of his cars and the
P4 ghosts. It took six pictures:
- at 1152 × 648: the top; scrolled to the bottom (Resume, Quit and the note all in view); the profiled cars' list open
  and scrolled down; Change car's list open;
- at 1152 × 1000: no list open (no scroll); Change car's list open (scrolls).

At 648 the rows are 724 px tall, or 862 with the profiled cars' list open, inside a 616 px menu. Tests (349) and the
headless check pass. No oracle builds the menu, so none could change.

## 2026-11-25 — P4: profile ghosts

Decision 7, taken as recommended with the rest on 2026-11-24 (*"everything else as you put it please and thanks
Claude"*): **each car's best clean profile run is kept as a ghost to race.**

**What's built.**
- **A profiled car's ghost.** When a profile run is clean and is that car's fastest clean run on the track, its ghost is
  saved as `ghosts/<track fingerprint>/profile-<car fingerprint>.ghost`, beside `you.ghost` and `auto.ghost`. A faster
  clean run replaces it; a tie or an unclean run never does. A profile's runs still set no bests: the autodriver's best
  is still the autodriver driving in play.
- **Why filed by the car's fingerprint, not its name**: the fingerprint is the car, as `cars.csv` already has it.
  Numbers changed in F2 make another car, with a ghost of its own, so a new tune can race the old one's best. A car set
  inactive keeps its ghost, and so would a renamed file. The header now carries the car's name for the board, and the
  caution it was driven at. If two cars profiled on a track share a name (the same file before and after a Save), the
  board adds the fingerprint: `matthew-x (d0142095)`.
- **G** steps through his best, the autodriver's, each profiled car's on this track (fastest first), and none. The
  board says whose and what: `GHOST matthew-2026-11-23's profile best, 1:18.70 at 70%  (G)`, with "(this car)" when
  it's the car being driven. The choice is kept across tracks; on a track where that car has no ghost it says "none on
  this track", and G moves on. G works while a profile runs, too.
- **The Esc menu**: **Watch a car's profile best** opens a list of them under it, the way Change car does, since there
  are as many as there are cars. `--ghost=<car>` and `--replay=<car>` take a profiled car's name or fingerprint.
- **The ghost header's two new fields are left out when empty**, so every ghost saved before P4, and the `ghost`
  oracle's, is the same file byte for byte. An older build skips them when it reads a newer file. Format 1 still. His
  bests and the autodriver's carry the car's name too from now on, though nothing shows it yet.

**A timing problem, found and fixed.** A ghost takes its last snapshot at the first snapshot tick at or
past the line, up to three ticks after the finish. The profiler moved to the next run as soon as the race said
finished, and the next run's start stops the recorder. So **before P4 only the runs whose finish fell on a snapshot
tick made a ghost at all**: on the shakedown the stock car's 65% to 80%, and not 50% to 60%; on seed 5 its 65% only.
Now a finished run ends once its ghost has that snapshot. The meter stops at the line, so the row doesn't change. If an
auto-respawn comes in those three ticks, the run ends as finished.

**Checks**, on scratch copies (`--ghosts=`, `--profiles=`, `--tunes=` and `--saved=`, his garage copied):
- **Every profile row is unchanged.** Profile every car taken before the change and after:
  - the shakedown, 4 cars: 11 rows;
  - seed 5, 6 cars (his `matthew-2026-11-23` and `-24` set active in the scratch copy, the only two of his clean
    anywhere): 18 rows.

  The rows are identical apart from the time of day, and so are the `Profile:` lines and `cars.csv`.
- **The ghosts left behind**:
  - the shakedown: the stock car's, 56.73 at 80% (his three active cars aren't clean even at 50% there);
  - seed 5: `matthew-2026-11-23` 1:18.70 at 70%, `matthew-2026-11-24` 1:25.07 at 60%, stock 1:26.66 at 65%.

  Each sweep kept a new ghost at every step up, 1-2 s faster each time. The last word:
  *"Profile done: 18 runs written to … Seed 5 0070ecd8.csv; a new best ghost for 3 cars, G to race it."*
- **A profile ghost against a fresh profile of the same car, at the same caution**, racing it (`--profile=this
  --ghost=<car>`):
  - the stock car's 80% on the shakedown came within **0.017 m** of its ghost, worst 44.58 s in, the long jump's
    landing (the `ghost` oracle: straights 17.5 mm, worst of all 44.62 s in, on that landing);
  - `matthew-2026-11-23`'s 70% on seed 5 came within **0.014 m**, worst 57.29 s in (the oracle, seed 5: straights
    14.2 mm, worst on a landing).

  The runs tied their ghosts, so both files were left as they were.
- **G pressed six times on seed 5** (a temporary probe, removed): your best (none yet), the autodriver's (none yet),
  `matthew-2026-11-23` 1:18.70 at 70%, `matthew-2026-11-24` 1:25.07 at 60%, stock 1:26.66 at 65% (this car), none, your
  best. The ghost car played each one.
- **Frames from the game**:
  - the stock car on seed 5 with `--ghost=matthew-2026-11-23`, the board's GHOST line as above;
  - `--replay=matthew-2026-11-23` at 20 s, its banner *"REPLAY matthew-2026-11-23's profile best, 1:18.70"*;
  - the menu with the list open (a temporary probe, removed).
- Tests (349: one new, the header's two fields), the headless check, and the seventeen oracles, identical to `c972d6c`
  (the commit before), run side by side, all on the committed car (4f918acd):

  ```
  oracle straight: 0-100 km/h 5.17 s | at 5 s 27.0 m/s | 10 s 41.5 | 20 s 48.4 | top 49.10 m/s (177 km/h; the model's estimate 49.65) | drift 0.00 m | off the plain's end at 26.0 s
  oracle drop: from 1.0 m: deepest compression 0.305 of 0.35 m | overshoots 1 | settled 0.70 s after contact | rest height 0.630 m (expected 0.630)
  oracle brake: from 100 km/h: stopped in 38.4 m and 2.81 s (the grip's limit is 35.8 m) | mean 1.03 g | heading moved 0.7°
  oracle circle: max sideways 1.06 g at steer 0.37 (5.2°) | held to full lock (body slip -4.2°, speed 18.9 m/s)
  oracle handbrake: stop from 61 km/h on the handbrake alone: 34.0 m, 4.02 s, mean 0.43 g, heading moved 0.3° | full lock from 61 km/h, throttle off, 2 s: turned 65° on steering alone (out at 46 km/h, body slip peaked 5°); 154° with the handbrake for the first 0.5 s (out at 20 km/h, body slip peaked 166°)
  oracle sweeper: on a 120 m circle: max sideways 0.97 g at 122 km/h, wing pressing 0 N | spun at 117 km/h (body slip -20°)
  oracle respawn: on its roof: back at the start on its wheels 3.00 s after it came to rest (3.51 s after the drop); dropped past the plain's end: back 2.71 s after the drop, from -30 m
  oracle climb: from rest at the foot, the steepest 40 m slope it makes the top of: road 32.3°, dirt 25.3°, grass 22.5°, sand 19.7° (each within 0.7°)
  oracle jump: off the lip at 82 km/h: 1.84 s in the air, landed 39.6 m past the lip, on the down-ramp (the down-ramp is 20-46 m), pitch -7° | deepest compression 0.350 of 0.35 m (on the bump stop) | upright
  oracle air: off the lip at 82 km/h: coasting, 1.84 s up, touched down at pitch -5°; throttle held: pitch 31°; brake held: pitch -38°; brake tapped for 0.3 s: pitch -16°; full right steer held: turned 38°, rolled 28° right side down, pitch -16°
  oracle loop: in at 96 km/h: THROUGH, +0.1 m off the exit road's centre | slowest over the top 79 km/h | deepest compression 0.350 of 0.35 m, 2.89 s on the bump stop | all four wheels off the road 0.00 s
  oracle bumps: through the patch at 70 km/h on average: a wheel off the ground 46% of the time, all four 11% (longest 0.18 s) | deepest compression 0.290 of 0.35 m, on the bump stop 0% | worst roll 9°, pitch 13°
  oracle lap: caution 0.80: standing lap 26.91 s, flying lap 24.51 s | slowest in each corner, km/h driven/planned: 1: 58/59, 2: 82/96, 3: 52/52, 4: 64/65, 5: 68/67, 6: 81/84 | wheels off the road 0.00 s, widest off the line 1.39 m (into corner 1), worst slide 15°, spins 0
  oracle pieces: loop: in at 89 km/h, THROUGH, +0.1 m off its exit road, slowest over the top 75 km/h; loop: in at 96 km/h, THROUGH, +0.2 m off its exit road, slowest over the top 79 km/h; loop: in at 103 km/h, THROUGH, +0.2 m off its exit road, slowest over the top 84 km/h; short jump at 80 km/h: lands 34.8 m past the lip, on its down-ramp, pitch -3°; short jump at 85 km/h: lands 40.1 m past the lip, on its down-ramp, pitch -3°; short jump at 89 km/h: lands 45.0 m past the lip, on its down-ramp, pitch -1°; long jump at 104 km/h: lands 54.8 m past the lip, on its down-ramp, pitch -13°; long jump at 109 km/h: lands 61.9 m past the lip, on its down-ramp, pitch -12°; long jump at 114 km/h: lands 68.7 m past the lip, on its foot, pitch -12°; slope at 99 km/h: wheels down; slope at 108 km/h: wheels down; slope at 118 km/h: 0.15 s in the air; slope at 127 km/h: 0.36 s in the air; slope at 155 km/h: 0.70 s in the air
  oracle track: base:shakedown at caution 0.80: FINISHED in 56.76 | splits 21.54, 40.64 of 2 | top 114 km/h | launch ramp, short: lip 82 km/h, landed 36.2 m past it, on its down-ramp, pitch -0° | loop: in at 96 km/h, through, at most 0.6 m off its line, slowest over the top 80 km/h | launch ramp, long: lip 108 km/h, landed 60.7 m past it, on its down-ramp, pitch -10° | wheels off the road 0.00 s, worst slide 22°
  oracle bank: gen:1v1: from rest at the foot of its tallest banks (25°, dirt), full throttle at the road: 4.2 m bank beside corner-30-banked at (19, 30): ON THE ROAD in 4.4 s; 4.0 m bank beside corner-30-banked at (16, 6): ON THE ROAD in 3.8 s; 3.5 m bank beside corner-30-banked at (24, 1): ON THE ROAD in 4.2 s
  oracle ghost: base:shakedown at caution 0.80: FINISHED in 56.76 | ghost: 1704 snapshots over 56.77 s, 22.8 KB (0.40 KB a second), saved and loaded the same | played back against the car at 120 a second, worst miss: straights 17.5 mm (28.0 s), corners 4.1 mm (18.9 s), the loop 10.6 mm (6.0 s), in the air 1.8 mm (3.9 s); the worst of all 44.62 in, on landing at (24, 20); rotation 0.41°
  ```

**Seen in passing, not changed:**
- **The Esc menu is taller than the default window** (1152 × 648; noted 2026-11-25 with SANDRAIL cut off at the top). The
  new button, which shows only on a track with a profiled car's ghost, adds a row, so half of Quit goes off the bottom
  too, and with the list open Resume and Quit are off it. Esc still closes the menu. A menu that scrolls when it's
  taller than the window would fix all of it; not done unasked. (Done the same day at his yes: the entry above.)
- **The first run of a sweep can differ by a hundredth with how the scene began.** `matthew-2026-11-23` at 50% on seed 5
  is 1:25.16 launched with `--spec=` and `--profile=this`, and 1:25.17 when Profile every car started the scene again
  with it (splits 18.71, 52.59, 1:08.88 against 18.72, 52.61, 1:08.89); from 55% on they agree. It isn't P4's doing: the
  same with no ghost, and P4 changes nothing before a run's finish. The stock car, first either way, agrees. Not pinned
  down.
- **`matthew-2026-11-23` is clean to 70% on seed 5 now (1:18.70), not 80% (1:17.56) as in P3's table**: its file was
  saved again at 01:26 on the 25th, after those runs.

## 2026-11-25 — The Esc menu opens with its lists closed

A list left open when the menu closed (Change car's, or Load a track's) was still open the next time. That moved
everything below it down, and put a car where Change car had been, so a click aimed at Change car changed the car.
Claude's own check did it once. Closing the menu now closes its list and puts the note back to "Esc closes this.", at
his word (*"Yes, do it"*). Checked in the window: Change car open, Esc, Esc, and the menu came back as it first opens.

## 2026-11-25 — F2 is the right half of the window

His screenshot: F2 open on `matthew-2026-11-23-original`, with Save as new car cut to "Save as new ca", and Reset, the
number boxes' last digits and the scrollbar off the right edge (*"can we move the edit car dialog a little to the
left"*). The panel is pinned 470 px wide to the right edge, but its top bar (the car's name and fingerprint beside
Save, Save as a new car and Reset) needs about 545 px with a name that long. A Godot control that must be wider than
its anchors allow grows to the right by default, so the extra 75 px went off the window. It grew to the left after
that (`GrowHorizontal = Begin`), which brought everything on screen but left its left edge wandering with the car's
name (*"the left side has become a little unbound"*).

**So it's now the right half of the window**, from the centre to the right edge, at his ask, with the sliders taking
the width that the names (205 px) and number boxes (112 px) leave. In the default 1152 × 648 window the sliders go from
about 210 px to 240 px, and in 1600 × 900 to about 465 px (`--screenshot --panels=tuning --resolution`, both on that
car). It still grows to the left if half a narrow window can't hold the bar. In the default window it now covers the
right half of the car. **A slider's step is a 400th of its range**, made a round number. So past about 400 to 700 px,
which only a near-maximised window gives, the step rather than the pixels sets how fine a drag is. That's the next
lever if he wants finer still; typing in the box is always exact.

## 2026-11-25 — Cars set inactive, behind a lock

He found two identical cars in his garage (`matthew-2026-11-23` and `-2`: the same bytes), so every Profile every car
drove that car twice, and asked for *"a delete car button somewhere behind a lock icon"*. The plan put to him: in
Change car, a lock beside each of his cars; click it and a Delete appears; the file moved to a folder, not destroyed;
the car being driven not deletable; no lock on the stock car. *"Yes, as recommended"*, then, mid-build: *"do not
actually need delete just need inactive or do not use for further testing"*. So nothing is ever deleted.

**What's built.**
- **The lock.** In Change car, a padlock beside each of his cars, drawn in code (`Ui/LockButton.cs`, not an emoji a font
  may lack). A click opens it, amber with the shackle up, and shows **Set inactive** (or **Set active**) to its left.
  It shuts after five seconds, when the list or menu closes, or when another lock opens. Neither button takes keyboard
  focus, so it's mouse only: a stray Enter or Space can't do it.
- **Inactive.** The car's file is moved, unchanged, into `tunes/inactive/`. The car stays in the list, after the active
  ones, dimmed, with ", inactive", and **can still be driven**. **Profile every car leaves it out.** Set active moves it
  back. The garage lists only the files directly in `tunes/`, so the folder is invisible to everything that doesn't
  ask for it, and it can be done by hand in Explorer too.
- **The stock car has no lock**: it's what the others are measured against.
- **The car being driven can be set either way.** F2's Save and the next start of the scene (which carries the car as
  `--spec=`) follow its file to where it was moved. Had the file vanished, the next Load a track or Generate would
  have refused it and quit, which is why the delete in the plan kept the driven car out of it.
- **A name is unique across both folders**, so a car can always go back: Save as a new car checks `inactive/` too.

**Two wobbles, found on the first try and fixed.** Both moved a button under the pointer just after the lock opened:
- **The note under the menu changed on unlock**, which changed the menu's height, and the centred menu jumped 28 px.
  Opening and shutting a lock now leaves the note alone; the amber lock and the button showing say enough.
- **Set inactive made its row wider**, which widened the menu, slid the lock 21 px right, and put Set inactive where the
  lock had been. A double click on the lock would have pressed it. The car's name now gives way, cut short with "…"
  (the whole name on hover), so the row never changes width and the lock stays put.

**Checks**, on a scratch copy of his garage (`--tunes=`, with `--ghosts=`, `--profiles=` and `--saved=` in scratch
too):
- **The headless pass**, on the shakedown.
- **He did it himself**, in the window: four cars set inactive (both identical ones, `-late` and `-24`), then a change to
  `matthew-2026-11-23-original`. Their files were in `inactive/`, with their sizes and times unchanged.
- **`--profile=all`**: *"Profiling every car (2)"*, the stock car and `-original`, the four inactive cars left out. The
  stock car was clean to 80%, 56.73, as before.
- **Set active** on the car being driven. Its row read *"a054172a, driving now"*, without "inactive". That label only
  shows when the panel's file is the car's new place, so Save follows it.
- **His real garage**, hashed before and after: the same five files, the same bytes.

**Seen in passing, not changed:**
- **The five seconds have a cost.** A click that comes after the lock has shut lands on the car's name, which changes
  to that car. Claude's own slow click did it once, driving `-2` from `inactive/`, which at least proved that works.
- **The Esc menu reopens with the car list still open**, so the menu's buttons sit lower than when it first opens.
  (Changed the same night: the entry above.)
- **In the default window the menu is taller than the window**: SANDRAIL is cut off at the top.
- **F2's buttons and sliders took no clicks** in the small window under Claude's mouse (Save, Save as a new car, the
  Mass slider), while the Esc menu took them. He saved a car with Save as a new car tonight (`-2` is that name), so it
  works for him; not looked into further.

## 2026-11-25 — A viewer for the profile's sheets

The answer to "sort easily": a program of its own, `tools/profile-viewer/`, compiled to `SandrailProfiles.exe`. He
connected his Python spreadsheet code (Trellis, trellis-gui, PyBindery) and asked what could be reused. *"Yes, go"*
was to the plan below.

**What his code had, and didn't.**
- **None of the three sorts**, though he remembered trellis-gui doing it.
- trellis-gui's grid is an editor: one widget per cell, a floating editor, save. That's wrong for files the game is
  writing, and slow at a few hundred runs × 130 columns.
- **Trellis's CSV reader keeps `0.50`, `37.80` and `110.00` as text** (it keeps trailing zeros on purpose) and `0.55`
  as a number. The game writes fixed decimals, so a caution or time column would sort half as text.

So the viewer is on **the same stack, DearPyGui** (with PyInstaller, both already on his Python 3.14), but on
DearPyGui's own table. That table has click-to-sort, Shift-click for a second column, frozen header and columns, and
draws only the rows on screen. His `trellis-errors` puts errors into words. trellis-gui's lessons came along, among
them reopening the file dialog where it last was.

**Built:**
- **The window.** The folder (the game's unless changed, remembered with the sheet and groups in
  `%LOCALAPPDATA%\SandrailProfiles\viewer.json`), its sheets down the left, `(2)` and `(written while open)`
  included, and the chosen sheet as a table.
- **Sorting.** Numbers as numbers, text ignoring case, blanks last both ways, ties in the file's order.
- **The car's numbers.** Joined from `cars.csv` (and any `cars (2).csv`) on the fingerprint as the Car group, off
  at first.
- **Columns.** Groups ticked on and off: Run, Laps and splits, Summary, Piece times, Piece speeds, Car. When, driver,
  car and caution are always shown and frozen at the left.
- **Reading the rows.** Click a run to mark it. The differences from the stock car are cut to 300 px, with the whole
  text on hover.
- **Live.** The sheet and `cars.csv` are checked twice a second and new rows go into their sorted place; F5 re-reads.
  An empty folder opens its first sheet the moment the game writes one.
- **It only reads**, a file whole and let go at once.

**Read-only, and why the game is never refused.** `CarSheet.Append` calls `File.AppendAllText`, which shares
reading. Python's `open()` on Windows shares reading *and* writing, so even a handle held open lets .NET append; a
test holds a sheet open from Python while pwsh appends to it.

**Two DearPyGui 2.3 traps.**
- **A table column's `show=False` does nothing**; `enabled=False` hides it. Found on a screenshot: the Laps group
  was unticked and lap 1 still showed.
- **A sortable table reports a sort by its first column on its first frame**, which overrode a sort set before it.
  So the sort being kept is made the column's `default_sort`.

**Checks:**
- 38 tests, headless: the reader (on files written the way CarSheet writes them, and on his real folder, left
  untouched by size and time) and the window, built without a viewport the way trellis-gui tests its own.
- **The real game into a scratch folder**: the viewer opened on an empty folder, then `--profile=this` on the
  shakedown wrote 8 runs (the stock car clean to 80%, 56.73, stuck at 85%). The sheet appeared, all 8 rows came
  in as they were written, and nothing was kept aside.
- **The exe** (12.5 MB, one file) opened his real folder, sorted by piece 02's slowest speed, and left his files
  as they were.

**Seen in passing:** his own sheet has the stock car's 50% run twice, identical, 13 s apart (00:35:06 and 00:35:19).
`--profile=this` wrote no duplicate here, so it's most likely his Profile this car before Profile every car, the
first stopped after one run. Not looked into further.

## 2026-11-25 — The car's numbers on every row: tried, and taken back

He tried profiling first, a Profile this car and a Profile every car on his own track "4" (14 rows: the stock car clean
up to 85%, 1:45.55; his three tunes not clean past 55%). Then he asked for `cars.csv` to go into the track's sheet,
each row carrying its car's numbers, *"so you can sort easily"*. That went in as 02ad877, and his sheet was converted
to 130 columns, with the originals backed up. Having seen it in LibreOffice, he took it back: *"ok now i see why you
separated them the data for the cars is different per car and some fields seem to be left out uh :-{D Do you want to
revert that change ... now that i see its benefit i understand why it was setup like that"*.

**So the layout is as P1 built it again**: one sheet per track, and `cars.csv` with each car's numbers once, joined on
the fingerprint. 02ad877 is reverted, and his two original files are back in place of the converted one. **A viewer
that sorts and joins the two is the answer to "sort easily"**: a session of its own, built from Python spreadsheet and
database code of his that he'll connect to it, held until he says go.

**Seen in passing: he uses LibreOffice**, which marks an open file with `.~lock.<name>#` beside it rather than (as
Excel does) holding it shut. So the game's rows go in while the sheet is open there; LibreOffice shows them on
reloading, and saving from LibreOffice would write over any added since it was opened.

## 2026-11-25 — P3: profiling in the game

His word: *"Go on to P3"*.

**Built:**
- **The Esc menu's Profile this car and Profile every car.** The autodriver drives the track from its start gate at
  50%, 55% and on to 100%, until a run isn't clean. Each run is a row in the track's sheet.
- **A run ends** at the finish; stuck (a metre no further along in 8 s); tipped over or off the world (where the
  auto-respawn would act, so the row says where it went wrong); or after 10 minutes.
- **Every car** goes through the garage, starting the scene again for each car (P2), with what's left of the profile
  kept in statics. At the end it goes back to the car it began with.
- **It runs at ×16 with up to 64 physics ticks a frame**, a banner saying which car and aggression, and Esc stops it,
  keeping the rows written.
- **Every finished run of his** writes a "you" row; runs he handed to the autodriver with P don't.
- **Open the profiles folder** is in the menu; `--profile=this|all` runs it from the command line and quits;
  `--profiles=` points the sheets elsewhere, for checks.
- **A sheet held open by a spreadsheet** keeps its rows waiting and writes them, in order, once it's let go. Any still
  waiting at quit go into `<sheet> (written while open).csv` beside it.
- **It's the race's own code**: Backspace's restart, P's autodriver, the race clock and gates, and a meter in the race
  for every run. A profile's runs set no bests and save no ghosts.

**Found and fixed: a run depended on the one before it.** Handed the car at once, the autodriver had a tick of
throttle before the clock was armed whenever the last run had ended inside a physics tick: that's every run but a
sweep's first. So the stock car's 80% on the shakedown read **56.70 after seven runs and 56.73 as the first**. Now
the autodriver takes the car only once the race is armed, so the clock starts on its first throttle. **The 80% run
is 56.73 either way, the same as the game's own autodriver.** (The same kind of head start is open to a held throttle
after Backspace, a tick at most; left as it is.) Also fixed: Esc ending a profile started the scene again and then
posed the ghost in the scene that had gone (a NullReferenceException), the M5c Tab bug's twin.

**Checks:**
- The same profile twice gives identical rows, apart from the time of day.
- **The shakedown with the stock car**: 64.33, 62.52, 60.97, 59.63, 58.47, 57.43 and 56.73 from 50% to 80%, all
  clean; at 85% stuck 422 m along. **Clean up to 80%.**
- **Speed:** 34.8 s of real time headless and **31.5 s in a window**, about ×14 (seven and a half minutes of driving),
  with the same rows.
- **Profile every car on seed 5, four cars, 22 runs: 2 minutes 2 seconds.** Fastest clean time and the most
  aggression each car stays clean at:

  | car | fastest clean | clean up to |
  |---|---|---|
  | stock | 1:26.66 | 65% (at 70% stuck 709 m along) |
  | matthew-2026-11-24 | 1:25.07 | 60% (65%: 0.95 s off the road) |
  | matthew-2026-11-23-late | 1:23.35 | 65% (70%: 14.8 s off the road) |
  | matthew-2026-11-23 | **1:17.56** | **80%** (85%: stuck 1847 m along) |

  His heavy 243 kW tune of the 23rd, the one whose springs bottom out, is 9 s faster than the stock car on seed 5
  and stays clean 15 points further up. On the shakedown, none of his three is clean even at 50%: they're quick
  (1:01.12 and 1:02.53) but run off the road or spin.
- His run: a finished run that was his gives a "you" row (56.73, no aggression). Checked with a temporary probe
  that counted an autodriver run as his; since removed.
- Esc in Profile every car, during the second car: "Profile stopped: 10 runs written". It's back on the stock car,
  time at ×1, 8 ticks a frame, the autodriver off, and the menu shut.
- **A sheet held open the way Excel holds one** (other processes may read it but not write it), held for 15 s from
  PowerShell: three rows waited, then all 8 arrived in order. Held to the end: all 8 went into the
  "(written while open)" file. That's a stand-in for Excel, not Excel itself.
- Tests (348), the headless check, and the seventeen oracles.

## 2026-11-25 — The stock car back in `buggy.json`

Asked whether to put the repo's `buggy.json` back to the stock car now that his car is safe in the garage: *"Yes, put
it back"*. Checked first that `tunes/matthew-2026-11-24.json` was still his working file byte for byte (both 22:48:57,
1352 bytes, `cmp` the same), then restored the committed file: 650 kg, grip 1.1, no wing, his steering, fingerprint
`4f918acd`. The working tree is clean for the first time since 2026-11-23. **His tunes now live only in the garage**,
and since P2 the game never writes the stock car, so it can't fill up with a tune again: F2's Save on it asks for a
name.

## 2026-11-24 — P2: the garage

**His working car first**: asked what to call it, *"matthew-2026-11-24"*, so `buggy.json` as F2 last saved it (22:48,
fingerprint `d0142095`) is now `%APPDATA%\Godot\app_userdata\Sandrail\tunes\matthew-2026-11-24.json`, byte for byte.
**The repo's `buggy.json` is untouched and still his car**, so on his machine the "stock" car is his tune until he
says to put it back.

**Built:**
- **`Garage`** (Game) is the stock car (`res://content/base/cars/buggy.json`) and every car in `user://tunes/`
  (`--tunes=` for checks), named by file, newest first.
- **The Esc menu's Change car** lists them, each with its fingerprint, the one being driven marked. **Picking one
  starts the scene again with it, on the same track.** That isn't the in-place swap the plan said: a restart leaves
  nothing built from the old car behind (the racing line, the autodriver's plan, F2's file), and costs a track's
  reload. The menu also says which car you're driving.
- **F2:** Save writes the car's own file. **On the stock car it asks for a name instead, since the game never writes
  the stock car.** **Save as a new car** asks for a name on any car. The new car goes into the garage and becomes the
  one Save writes. A name already taken is refused, never written over. **The game pauses while the name is typed**,
  so the letters don't drive the car; Enter saves, Esc doesn't. The panel's title is the car's name, not `base:buggy`.
- **Every scene restart takes the car being driven along**: a new track, Load a track, the editor and back.

**Checked with a temporary probe (since removed), on scratch copies of his tunes:**
- Save on the stock car opened the name box and paused the game.
- "Probe Car" was saved as `probe-car.json`, became the car being driven, and the game unpaused.
- The same name again was refused, and Esc closed the box ("Not saved.") without opening the menu.
- **The stock file's MD5 was the same after as before.**
- Change car to `matthew-2026-11-24` restarted on the shakedown with fingerprint `d0142095`, as its file says, and a
  new track (seed 5) kept that car.
- Tests (348) and the headless check pass, and the seventeen oracles are identical to M5d's.

## 2026-11-24 — P1: the profile's meter and sheet

His decisions: *"2. yes but start at 50 some tracks the autodriver wont complete without this without crashing
like the will become obvious as track times rise for later attempts at increasing aggression i believe"* and
*"everything else as you put it please and thanks Claude"*. So the sweep runs from 50% to 100%.

**Built:** `Sandrail.Core/Profiles/`. **`RunMeter`** is fed a tick at a time while the race clock runs (the route
piece, laps done, speed, off the road or not, the slide where it's read, how far along). It counts the time and
slowest speed on every piece, lap by lap; the top speed; time off the road piece by piece; the worst slide; spins;
respawns. Its `RunSummary` gives the race's times, the laps and the best one, and the pieces on the best lap; it says
**clean** when the run finished with under 0.5 s off the road, no spin and no respawn. **`CarSheet`** turns a summary
into a CSV row under a header made from the track's route: who and what, the car's differences from stock in words,
the run, then the time on each piece and the slowest speed on each. One file per track (`Shakedown 8a6323aa.csv`);
`cars.csv` gets every number of each car once. It writes decimal points in any culture, UTF-8 with its mark so Excel
reads it, and Windows line ends. A file whose header has changed is left alone and a new one started beside it.
**`MeterRead`** (Game) is now the one rule for "off the road" and "a slide worth reading", and the `track` oracle's
own copy of those rules has moved into it. The panel's names for the car's numbers ("Bump damping (N·s/m)") moved
into Core as `Tunable.Words`, so the sheet uses the same words.

**A spin is a slide past 30°**, the `lap` oracle's rule, not the 90° first written in the plan, so "clean up to
85%" keeps the meaning it had on station 6.

**Checks:** eleven tests (348 in all, passing). **The `track` oracle on the meter prints the same line, character
for character**, and the seventeen oracles are identical to M5d's. A temporary probe (since removed) printed the
row the sheet would get:
- **The shakedown's pieces add up to 56.767 s against the clock's 56.756**. The 0.011 s is about a tick: whole
  ticks at the start and finish, against a clock that splits the tick it crosses a gate in. The start piece shows
  0.1 km/h (a standing start) and the worst slide 22°; the run is clean.
- **Seed 4 (a circuit) at 70%**: laps 44.04, 41.41, 41.40, and the best lap's pieces add up to exactly 41.400.
- **Seeds 2 and 6 at 70% don't finish** (stuck on a landing and on a banked corner, as before). Their rows read "no",
  with the laps and the pieces never reached left blank, not 0, so a spreadsheet's average skips them.

## 2026-11-24 — The car profile, planned

He's been saving tracks of his own (*"generated my own but can never get the heights to work out correctly like
i want to go under sometimes"*: going under can't be done yet, as a tile takes one piece, raised road sits on
earth, and a road in a jump's gap is a problem; bridges were left for later in M2 and M3), and is making a short
track to tune on. Then the idea: *"maybe there would be a way to profile your car over a track and keep the
result like a spreadsheet but no actual functions like just data and have more than one car … i mean we could
just implement car breeding like but bruteforcing it doesnt feel right here"*. Asked which to plan first, the
profile or crossings: *"Plan the car profile"*. **[docs/PROFILE-PLAN.md](docs/PROFILE-PLAN.md)**, its seven
decisions his.

Measured for it:

- **A run is the same at any speed**: the `track` oracle at ×1, ×8, ×32 and ×64 printed one line, character for
  character (56.76, splits 21.54, 40.64). So one run per car and caution is the answer.
- **About ×10 is the machine's ceiling headless**: 58.1 s of real time at ×1, 8.2 s at ×8, 6.9 s at ×32 and ×64,
  about 1.1 s of it loading. Godot's eight physics ticks a frame is the likely cap.
- **The autodriver plans from grip × caution, the top speed estimate and a wing's downforce**, braking at 0.9 of
  the grip (`RacingLine.SpeedProfile`), and nothing else: springs, bars, brakes, weight and steering show only in
  how well the car holds the plan. So the caution at which a car stops being clean is what tells cars of the same
  grip apart, and the plan sweeps cautions.
- **His working `buggy.json` is a car kept nowhere else**: saved by F2 at 22:48 today, 18 numbers from his late
  tune in `tunes/` (974 kg, grip 1.31, the twin-plane wing), uncommitted in the repo's file. The plan's garage
  saves it into `tunes/` first, under his name for it, before anything else touches that file.
- **The game and the `track` oracle differ by 0.03 s** on the shakedown with the stock car (56.73 against
  56.76), not yet pinned down; the profile is the game's code path only.

## 2026-11-24 — Time, slower and faster

His ask: *"Ok how about two keys to adjust how quickly time goes by? It will make tuning funer"*. **`,` and `.`**
(the keys under `<` and `>`), and the D-pad's left and right on a pad, step time through ×0.1, ×0.25, ×0.5, ×1 and
×2. The speedometer says so ("38 km/h   time ×0.25") whenever it isn't ×1, a flash says what the keys do, and
`--time=0.25` starts that way, for a check or a screenshot. The editor always opens at ×1: slow motion is for
driving. No pad layout rebinds the D-pad, so his clone pad's saved layout keeps it.

**The first try counted real time.** Godot's `Engine.TimeScale` alone keeps 120 physics ticks every real second
and makes each one shorter, so at ×0.5 the race clock, which counts ticks, timed the autodriver's shakedown at
1:53.46, twice its 56.73. A ghost's snapshots would have come out half as far apart and played back at half speed
against the car. **Now `GameClock.SetScale` turns the tick rate down with the time scale** (60 ticks a real second
at half speed, 12 at a tenth), so a tick stays 1/120 s of game time and the physics steps exactly as at full speed.
Everything that turned ticks into seconds by `Engine.PhysicsTicksPerSecond` (the race clock, ghosts, replays, the
run recorder, and the oracles) uses `GameClock.TicksPerSecond`, the project's 120, which never changes. The proof:
the autodriver's run on the shakedown in the game (`--autodrive`) at ×1, ×0.5 and ×2 alike is **56.73 (splits
21.52, 40.61)**, its ghost 1703 snapshots and 23.0 KB; and the `ghost` oracle at ×0.5 prints the line it prints
at ×1, character for character (56.76, 1704 snapshots, the worst miss on a straight 17.5 mm). The seventeen oracles
at ×1 on the committed spec are identical to M5d's.
At ×0.1 the screen gets only 12 physics ticks a second, and interpolation smooths the frames in between, so it
should still look smooth; at ×2 the machine runs 240 ticks a second, which it does easily.

**His verdict**, having tried it: *"i've tried it fast and slow and all seems in order"*.

## 2026-11-24 — M5e: the view holds still under the mouse

His next: *"Ok haveing the camera follow the mouse to move around make it too difficult to place things with it
can the view remain fixed unless you use the scroll button?"*. The view followed the cursor, and the cursor the
mouse, so the ground slid away under the pointer as he went to click.

**Now the view has a point of its own that it looks at.** It follows the cursor only when the keys move it (the
arrows, Enter, F, N, Backspace), so laying track by the keys never runs off the screen; the mouse moving the
cursor, or a click, leaves the view where it is. **Holding the scroll button (the middle button) and dragging
moves the view**, the ground under the pointer following it as a map dragged by hand does. The wheel still picks
the piece, Ctrl and the wheel zoom. The keys at the bottom are three lines now. A test drive keeps the view.
Checked with a temporary probe (since removed): moving the mouse about left the camera where it was (it finished
the last 1.5 m of its glide), a 200-pixel drag moved the view 35 m without moving the cursor, and Enter brought
it back to following.

## 2026-11-24 — M5e: corners, left and right, and clicks that snap

His first play in the editor came back as a picture: a track of 26 problems, nearly all "runs into … which isn't
entered from there", the pieces facing ways that don't meet. Then: *"ok with the circular pieces i had trouble
rotating to the other direction like there is no middle"*. Three faults, all the editor's:

- **Moving the mouse made the free cursor**, and a click put the piece where it pointed, turned however the
  cursor last faced. Corners came out a tile off or the wrong way, and everything after them with them.
- **At the road's end, Q and E did nothing**, the road setting the facing; turning a corner the other way was
  M, which nothing said.
- **With the free cursor, Q and E swung a piece round the tile it's entered from**, its corner, so a 3 × 3 corner
  jumped about instead of turning in place: no middle.

**Now:** snapped onto the road, **Q turns a corner left and E right** (the S-bend, chicane and loop to the left or
right too), and the status says which way it goes and which key flips it. **With the free cursor, Q, E and M
turn a piece about its middle.** **The free cursor on the tile just past an open end snaps the piece onto that
end**, so clicking along the road lays it as Enter does; and the mouse resting over the piece in hand at the
road's end keeps it there. Checked with a temporary probe (since removed): a corner right with E and one left
with Q, both meeting the road (two problems throughout: the end, no finish); a free 50 m corner turned four
times, its entry tile going round the corners of one 3 × 3 block, its middle staying at (20, 11); a click on the
tile past the road's end laying a straight onto it, connected.

## 2026-11-24 — M5d: ground, saving and circuits; M5 is built

*"Go on to M5d"*. Built as M5-PLAN has it (decisions 5, 6 and 7).

**Ground.** The editor opens a track on its seed's ground: a generated track (the menu's Edit this track hands it
over directly; it has no file), a saved one, or an empty grid on a seed's ground (`--edit=new:<seed>`, the menu's
New track on this ground). Pieces build against the bare terrain; the free cursor rides the ground, its level the
ground's under it, Page Up and Down lifting it. **The ground catches up in squares**: the editor's terrain is 64
squares of 128 m, and half a second after the last change the bed is laid under every placed piece (`Bed.Lay`
takes any pieces now, so an unfinished track has its bed) and only the squares whose ground moved are worked out,
all off the main thread; showing them takes about 2 ms. The first version rebuilt the whole 1 km mesh, 50 ms on
the main thread and a 66 ms frame just after he'd stop; with squares: *"42 ms in the background, 3 of 64 squares
shown again in 2.3 ms"*, the worst frame 10 ms, the same as on the plain. The editor needs no collider; a test
drive builds the real ground. A test drive of an unfinished track has its bed under every piece, and no rocks.

**Saving** (decision 6): **Ctrl+S** opens a name box offering the track's own name or "My track" (then "My track
2", and so on); Enter saves to `user://tracks/<name>.json` as `user:<name>`, Esc doesn't. Unfinished tracks save
too, and the Load list marks them ("unfinished: opens in the editor") and opens them in the editor, since the game
can't race them. A generated track becomes "Seed 5, edited" (`user:seed-5-edited`) at its first change, its code
no longer its own. `--saved=<folder>` moves the folder, which every check here used, so none of his tracks were
touched.

**Circuits** (decision 7, PLAN's M6 folded in): laying a start-finish line makes the track a circuit of 3 laps, L
changes the laps (1 to 9), and taking the line away makes it a sprint again; the status says which, and on whose
ground.

**Checks:**

- 3 tests (337 in all): a line makes a circuit, laps change and undo, taking it away makes a sprint; a circuit
  laid by `Next` from its line closes, sound, nothing open; a generated track edited, saved and read back is the
  same file, and its ground beds the same.
- A temporary probe (since removed) laid a circuit by the keys on an empty grid: the line (3 laps by itself),
  straights and four 50 m corners round to it: 12 pieces, no problems, nothing open. Ctrl+S and Enter saved it as
  "My track". **The `track` oracle raced it: FINISHED in 1:01.16, laps 21.95, 19.61, 19.61.**
- The probe on seed 5's ground: road laid, the ground caught up as above, saved as "My track 2"; reopened from
  its file with the same two things to do; test-driven from its start gate, untimed.
- All seventeen oracles as before. Not clicked: the menu's Edit this track on a generated track and New track on
  this ground, which go the ways tested.

**M5 is built** (M5a-M5d). M5e, his play, passes it; then his tuning, with the wheel-spin talk he asked to be
reminded of.

## 2026-11-24 — M5c: test driving

*"Go on to M5c"*. Built as M5-PLAN has it (decision 4).

**Tab in the editor starts the scene again as a test drive**, and Tab in the car starts it again as the editor.
It reuses the game's own setup rather than building a second one: the car, its camera, R, the autodriver, and
on a sound track from its start gate the race, ghosts and replays. The editor's work waits outside the scene in
`EditorSession` (the track, its undo history, the cursor, the piece in hand, the camera), as the Esc menu's next
flags wait in `NextArgs`, and comes back as it was. Tab drives from the piece under the cursor; at the road's
end, from the road's last piece; on empty ground, from the track's start. **From anywhere but a sound track's
start gate the drive is untimed**: the car a few metres into the piece, facing along it; the board says TEST
DRIVE, how many things the track has to do, and the keys; R and Backspace go back to where it began. A sound
track driven from mid-way still has its racing line, so P hands it to the autodriver. `--track=@editor` is the
editor's track; `--drive-from=<column>,<row>` test-drives any track file from a piece, reading it without
refusing it, which is how the checks drive.

**Checks:**

- **Ten pieces on the shakedown**, each driven from: both launch ramps, a landing (at its raised level), a slope,
  corners both ways, the banked corner, the chicane, the loop. The car was on its piece every time, heading the
  way the piece goes (north 0°, east 90°, south 180°; a few degrees into the turn on corners, the right way for
  each), and from the start gate it was the race.
- **The round trip**, by a temporary probe (since removed): it laid a sound track in the editor by the keys (ten
  pieces, start to finish, no problems), pressed Tab on empty ground (the race from its start gate), then Tab
  back: the cursor, level, facing, piece in hand, zoom and view as they were, the undo history kept.
- **The `track` oracle drove the editor-made track**: FINISHED in 14.89 at 80%, wheels on the road throughout.
- **An unfinished track** (four problems) test-driven from a straight: the board as above.

**Found and fixed on the way**: Tab left a `NullReferenceException` behind at each switch, the old scene's code
carrying on after the scene had left the tree, asking for its viewport (the same family as the menu's paused
new track); it now stops at once. And the probe's first run went astray when the game window opened under his
mouse: moving the mouse takes the editor's cursor off the road's end, as it should (*"Sorry claude i
interfered"*). The `track` oracle says "no checkpoints" on a track without, rather than "splits  of 0". Tests
334; all seventeen oracles as before.

## 2026-11-24 — M5b: laying track, the palette, the mouse

*"Go on to M5b"*. Built as M5-PLAN has it (`Editor/EditorView`).

**Laying track onto the road's end** (decision 2): whenever the road has an open end, the cursor waits there
and the piece in hand snaps onto it by `TrackEditor.Next`, facing along the road; Enter lays it and the cursor
moves on to the new end. A piece laid anywhere (the free cursor, or a click) takes the cursor to its end too,
so a start gate put down anywhere is followed onto at once. The hand changes by itself: the landing after a
launch ramp's lip, a straight after the landing and after the start gate; on an empty grid the start gate is in
hand. Backspace at the road's end takes back its last piece; N goes to another open end, F back to the newest.
The arrows or the mouse make a free cursor, where Q/E turn and Page Up/Down change the level as before.

**The palette**: two columns down the left on a dark backing, the piece in hand lit; the mouse wheel, [ ], 1-9
or a click pick from it. **The mouse** (decision 3): pointing moves the free cursor (onto the plane of its
level), a click lays, a right click takes away, Ctrl and the wheel zoom. Mirroring is only mentioned for the
pieces it changes ("turning left" for a corner).

Checked with a temporary probe (since removed) that laid a track from an empty grid by the keys: start,
straights, a 30 m corner, a short jump (its landing came into hand and went down across the gap), a 50 m corner
turning left, straights, then Backspace. Every piece met the last: one open end and the same two problems
throughout (the road's end on empty ground, no finish), 1 to 12 pieces and back to 11. A right click at the
screen's middle took away the piece under it, and the to-do list showed the two loose ends at once. Frames of a
track half laid. Tests 334; nothing of the car or the race changed.

## 2026-11-24 — M5a: the editor's core

M5's seven decisions taken as recommended: *"Yes, as recommended"*. Decision 7 folds PLAN's M6 (circuits in
the editor) into M5.

**`TrackEditor`** (Core, `Tracks/`): a track being edited and its history. **Place** and **erase**, refused in
words only when the change can't be made at all ("corner, 30 m at (20, 15) is in the way at (20, 15)", "would
run off the grid at (32, 5)", "level -1 is below the plain"); **undo** and **redo**, any number of steps;
**open ends**, every way out nothing meets, newest last; **next**, the placement that continues the road from
an open end with a piece (across the edge, or across the gap after a lip, facing the way the road goes, at the
end's level), refusing a piece that can't follow (a landing only after a jump, and only a landing after one).
The track is laid after every change, so its problems are always current. **`TrackEditor.Open`** reads a
track file keeping its problems, where `Track.Load` refuses one that can't be raced. 8 tests, the strong ones:
**next meets every way out**, every piece after every piece, every turn and mirror (over 500 pairs), with
nothing wrong between them as `Lay` judges it; **next alone lays whole tracks**, the shakedown and circuit 8
round to its line, piece for piece; the shakedown built placement by placement is the shakedown, byte for
byte. Tests 334.

**The editor** (`Editor/EditorView`, `Main.Editor`): the Esc menu's **New track** and **Edit this track** start
the scene again with `--edit=new` or `--edit=<track>`, with no car and no race. A camera above the grid at an
angle, gliding after the cursor; a tile-sized cursor; the piece in hand at the cursor, see-through, green where
it fits and red where it doesn't (and why, under the status); every piece built on its own as it's placed;
the problems listed top right in plain words ("The corner-30 at (14, 13) leads onto empty ground at (12, 14)"),
the pieces they name tinted red, or "SOUND: it can be raced." in green. Keys: arrows move (away from the camera
is up), [ ] pick the piece from seventeen (road, then stunts, then gates), Q/E turn, M mirror, Page Up/Down
level, Enter or Space place, Delete or Backspace take away, Ctrl+Z/Ctrl+Y undo and redo, C turns the view,
- and + zoom. Tracks on generated ground wait for M5d, so Edit this track shows only on the plain for now.

Checked: frames of the editor on an unfinished track (four problems, the two faulty pieces red) and on the
shakedown ("SOUND"); a temporary probe pressed the keys (Enter, →, ], Enter, Ctrl+Z, Ctrl+Y, Delete, Enter,
Enter on the same spot): 6, 6, 6, 7, 6, 7, 6, 7 pieces and a refusal, each as it should be. All seventeen
oracles as before.

## 2026-11-24 — M5 planned

*"Plan M5"*. [docs/M5-PLAN.md](docs/M5-PLAN.md): the editor, in five slices (its core, laying track,
test-driving, ground with saving and circuits, his play), with seven decisions for him, then his tuning.
Measured for it on the bench (seeds 1-6): `Lay` takes 0.1 to 1.3 ms, so the editor can check the whole track
after every change; the bed takes 36 to 84 ms to lay again and the rocks about 1 ms; the terrain's noise 15 to
60 ms, made once. Godot's terrain mesh and collider are the slow part, about 400 ms (a saved track builds in
559 ms, its pieces in 50), so on a seed's ground the editor lets the ground catch up a moment after an edit,
off the main thread, while each piece shows at once.

## 2026-11-24 — M4 passes

His word, after the brake lights: *"Looks great to me Claude lets continue"*. **M4 passes on it** (M4e: "M4
passes on his word"): codes, fingerprints, ghosts, replays, and the brake lights besides. He asked in the same
breath when the load button comes (*"like i know i can do it via the command line but am very very lazy"*).
The plan had "save and load" in M5, the editor, but there was no need to wait.

**Load a track**, in the Esc menu: a button that opens a list of every track there is, the game's own (seed
5, the shakedown) and then every one saved from the menu, newest first, each labelled as the board does
("Seed 7, code 7v1, saved"). One click loads it, unpaused first, as Generate is since the camera fix. The
list scrolls, as tall as the window leaves room for, so the menu never runs off a small window (it did, in
the first picture). Checked with a temporary probe (since removed) that pressed Esc, Load a track and his own
saved seed 7 by the menu's buttons: it loaded `user://tracks/gen-7.json`, fingerprint `08cb7713`, unpaused,
the camera 6.9 m behind the car at the middle of the screen.

## 2026-11-24 — M4e begins: "the replay works"; brake lights

His word on the replays, after the menu offered *"Watch the last run (1:20.67)"* on a random seed: *"the
replay works, do the brake lights now please Claude and thanks!"*. That's the menu's Watch button tried, one
of the things M4d hadn't. M4e, his play, goes on.

**Brake lights** (his idea, 2026-11-24: *"like anytime you request goes into the opposite direction to the
throttle on the brakes"*), on `CarLooks`, so the car, the ghost and the replay's car all have them:

- **Red at the back's corners when the brakes are on**, lit from a touch and brighter the harder. They
  follow the brakes as the pedals resolve them (`BrakeAmount`: the brake key rolling forward, or throttle
  rolling backward), not the key, so they don't light while the brake key is reversing the car.
- **White inboard of them when it's driving backwards** (`DriveForce` below 0).
- Unshaded, so they read at any distance and in any light. **A ghost's aren't faded**, so its braking points
  show far up the road while the rest of it is see-through.
- **Snapshots keep them**: `Brake` is now the brakes as applied, and `Reversing` rides in the flags byte's
  second bit, so ghosts saved before read as never reversing and nothing else changes.

Checked in frames: seed 5's replay from the chase camera at 45.8 s, the autodriver braking from 110 km/h,
both lights bright red; and the car reversing at 22 km/h (`--drive=` takes a fifth number now, the brake),
white lights on and the red ones dark. Not seen on a see-through ghost, though the code leaves its lights
out of the fade. Tests 326 (the ghost test carries reversing through the file); all seventeen oracles as
before.

## 2026-11-24 — M4d: replays

*"ok lets continue please Claude"*, then, offered brake lights first, *"carry on with the replays we can do
this after"*. Built as M4-PLAN has it.

**On the ghosts**, while trying them: the first ghost looked missing (*"not seeing a ghost"*), because the board
named the autodriver's best while the run that made it was still on, and a new best races from the next run.
The board now says so: *"…, from the next run (Backspace)"*. After Backspace: *"Backspace brought the ghost up
just faintly in front"*. Offered a pale blue tint, he's *"happy as is"*.

**The cameras** (`ReplayCameras`, Core): placed from the laid track when a replay first starts on it (8-10 ms),
not saved. One at the start; one by each stunt, 22 m out for a jump and 34 m for the loop; one on the
outside of a corner, at least 120 m from any other; and one partway along any straight that would leave
200 m without one. Each covers the road from halfway back to the camera before it to halfway on to the next,
so the car always has its nearest. Each stands 4 m up, past the road's bed, on whichever side sees its
stretch best, raised in 4 m steps (up to 24 m) until it sees 95% of the road over the ground. Seed 5 has 15,
circuit 8 6, the shakedown 10. 6 tests: above the ground, at least 90% seen, the whole route covered, a
camera by every stunt, the same each time, a ghost read along the route. 326 tests in all.

**Replay mode** (`Main.Replay`): the last run, or a saved best, from the Esc menu's new Watch buttons or Enter
for the last run (the finish flash says so, and the keys line has "Enter: replay"). The race stops (no game
tick runs, so the clock waits), the car is held still and hidden, and a solid copy of the run's car plays
it. **C** changes the camera: trackside (the one covering the car's stretch, looking at it, zoomed to keep
about 28 m across at the car), chase, or free (right mouse to look, W/A/S/D and Q/E to fly, Shift faster).
**Space** pauses (at the end, plays again), **← →** skip 5 s, **↑ ↓** change the speed (¼, ½, 1, 2), **Esc**
goes back to the car, moving as it was. The board shows the time, the speed and which camera, the speedometer
shows the replay's car, and the driving keys hide.

**Checks**: frames from the replay on seed 5 (a straight's camera at 116 km/h, then the jump's with the car
in mid-air over the gap), circuit 8 (a banked corner from beside it, and the chase camera), and the shakedown
(the loop's camera, the car on its way up inside the loop). A temporary probe pressed the keys in turn: C went
trackside, chase, free; Space paused; → skipped 21.12 to 26.12 s; ↑ made it x2; Esc ended it with the car's
own camera back, the car moving and shown, and the game not paused (the menu didn't open). All seventeen
oracles as before. **Not tried**: the menu's Watch buttons, Enter, and a replay of a run with an R in it (the
ghost holds across the cut, tested in M4b, and the chase camera starts behind the car again after any jump of
30 m).

**Also**: a lap within a hundredth of the best says "equal to the best lap", not "a new best lap by 0.00 s".

**Next, at his word**: brake lights (his idea, 2026-11-24): red when braking, white when reversing, and the
ghost's kept bright so its braking points show far up the road. The ghost already records the brake.

## 2026-11-24 — Fix: a new track from the menu started paused

His report, trying the ghosts: *"The camera doesnt seem to follow the car correctly anymore?"*, then *"like
when i go new track it doesnt follow after"*. **M4a's bug.** Godot takes the old scene out of the tree the
moment the menu starts it again, and M4a had moved the menu's unpause to after that, when the menu had no
tree left to unpause: a quiet `NullReferenceException`, and the new track ran paused. The physics went on
(Jolt doesn't stop for the pause), so the car rolled, but the camera, which moves in `_Process`, stayed put.
Esc and Resume would have freed it. Before M4a the menu unpaused first, which is why he'd found loading and
new random seeds fine at M3e.

**The fix**: `SeedMenu.Go` unpauses first, and pauses again only if the code can't be driven, saying why.
Found and checked with a temporary probe (since removed) that put the game on the test ground, paused it as
Esc does, and went through the menu's own code to seed 7: before the fix, the new scene never ran a frame of
`_Process`; after it, the car stays at the screen's middle, 7 m ahead of the camera, all the way down seed
7's first straight.

## 2026-11-24 — M4c: ghosts

*"Go on to M4c"*. Built as M4-PLAN has it.

**The ghost** (`GhostCar`): a `CarLooks` at 40% opacity with no shadow, no body and no collider, wearing
the run's own car, posed every frame from `Ghost.At` at the race clock's time, between the last two physics
ticks as the car is drawn. Before the clock starts it waits on the line; R back to a checkpoint leaves it
where its run was; a restart puts it back; a second after its own finish it has faded out. It fades as it
comes within 7 m of the camera and is gone at 3 m, so a ghost behind you never blocks the road.

**Bests are kept between sessions** (`Main.Ghosts`): on the finish, a best's ghost is saved to
`user://ghosts/<track fingerprint>/you.ghost` or `auto.ghost` (written beside it and moved, so a crash can't
lose the old one), and when a track is built both are read back. The board's bests come from them, his and
the autodriver's apart, a best set on another car marked "(another car)" (decision 6). A circuit's ghost is
the whole run (decision 5), and its best lap between sessions is the best run's best lap. The flash after a
best says "Saved as the ghost." The new best races from the next run; the one being raced plays on to its
finish.

**G** chooses the ghost (decision 4): his best, the autodriver's, none. It's on the board's new last line
(`GHOST your best, 1:25.17  (G)`), and kept when the Esc menu starts a new track. `--ghost=you|auto|none`
sets it from the command line and `--ghosts=<folder>` moves the folder, which is how every check here kept
clear of his. The keys at the bottom are three lines now: two ran off a 1152-wide window once G joined them.

**Checks**, on the committed car (4f918acd), into a scratch folder:

- The autodriver drove seed 5 at 70%: *"Saved as the autodriver's ghost"*, 37 KB.
- It drove again with that ghost showing: *"The car was at most 0.013 m from the ghost it raced"* over the
  whole 1:25.17, which is the oracle's miss. So saving, loading, the clock and playback line up. A tie
  isn't a best, and the file wasn't touched.
- A fresh launch read the best back: the board said `auto 1:25.17` before anything was driven.
- A frame from the game: the ghost pulling away up seed 5's first straight from a slower car.
- All seventeen oracles exactly as after M4b.

**Not driven**: R to a checkpoint, starting again, and finishing behind the ghost. They follow from its
clock, the race clock's ticks since the start, which R doesn't touch and a restart sets back. His play will
say.

## 2026-11-24 — M4b: the snapshot stream

*"Go on to M4b"*. Built as M4-PLAN has it.

**Core, `Ghosts/`**: `Snapshot` (position to the millimetre, rotation as four 16-bit components with W kept
positive, steering to a hundredth of a degree, each wheel's droop to the millimetre, throttle, brake and
handbrake to a 255th, and a cut flag), quantised as it's kept so a ghost saved and loaded is the same to the
bit. `Ghost`: the header (format, the track's fingerprint, ID and name, his or the autodriver's, the time,
splits, laps, when, the car's fingerprint) on one line of JSON with the car's whole spec in it, then the
snapshots column by column, positions and rotations as changes, deflated. `At(t)` lands on a snapshot
exactly, goes in a straight line between two (rotation slerped), holds before a cut, and holds at either end.
A newer format is refused by name. `GhostRecorder` takes every 4th tick from the clock's zero to the first
at or after the finish, and marks the snapshot after a teleport. 12 tests; 320 in all.

**Game**: `CarLooks`, the buggy's grey boxes and wing, moved out of `Buggy` into a node a ghost can wear
too, with a `Fade` for see-through. Nothing changed on screen: frames at rest and turning, before and after,
match, and his winged tune still shows its twin plane. `Buggy` gained `Droop(i)`, `Snapshot()` and a
`Teleports` count. **The race records every run** (`Main.Race`, `RecordGhost`), and keeps the last finished
one: the autodriver on the shakedown in the game filed *"The run as a ghost: 1703 snapshots over 56.73 s,
23.0 KB as a file"*.

**The `ghost` oracle** (seventeen now): the track oracle's drive, recorded the same way, saved and loaded,
and played back against the car at 120 a second:

```
shakedown 80%   FINISHED in 56.76 | 1704 snapshots, 22.8 KB (0.40 KB a second), saved and loaded the same
                worst miss: straights 17.5 mm, corners 4.1 mm, the loop 10.6 mm, in the air 1.8 mm; rotation 0.41°
                the worst of all 44.62 in, on the long jump's landing
seed 5, 70%     FINISHED in 1:25.19 | 2557 snapshots, 36.4 KB | straights 14.2, corners 4.5, air 1.8 mm; a landing
circuit 8, 70%  FINISHED in 2:04.08 | 3724 snapshots, 48.6 KB | straights 13.8, corners 4.4, air 1.9 mm; a landing
```

**Fact 2's arithmetic held in the corners and the air, and the loop came out at twice its guess (10.6 mm
against 5)**: the bump stop, not the loop's radius, sets the loop's worst. **The worst of all is a landing**,
one tick of 1.75 cm as the suspension hits its stops, which no straight line between snapshots can follow and
no eye could see. Files are half fact 3's guess. All the other oracles are as before on the committed car
(4f918acd): the shakedown 56.76, seed 5 1:25.19, circuit 8 2:04.08.

## 2026-11-24 — M4a: track codes and fingerprints

M4's seven decisions taken as recommended: *"Yes, as recommended"*. M4a built as M4-PLAN has it.

**The generator has a version**, `TrackGenerator.Version`, 1 today: the generator as M3 passed.
**`TheGeneratorVersionPinsItsTracks`** pins seeds 1 to 10 under it as fingerprints, and a change that moves
any of them fails until the version goes up and the new ones are pinned beside the old. A change to the
terrain, the bed or the rocks fails it too. That's on purpose: those reach tracks kept as files as well.

**A code is the seed and the version**, `5v1` (`TrackCode`). The menu's box, now "Code", and `--seed=` take
a code or a bare seed, which means this version. A generated track's ID is `gen:5v1` and its name is still
"Seed 5"; the menu, the opening flash and the log say "Seed 5, code 5v1". A file saved before codes (`gen:5`)
counts as version 1. A track made with other dials (`--stunts=` and the like) has no code: `gen:5v1-dials`,
"Seed 5 (own dials)". **A code from another version** drives its file if the game has one, among its own
tracks and then `user://tracks/`, or says why it can't: *"3v1 was made by generator version 1; this build
makes version 2, where seed 3 is a different track (3v2). Drive that, or ask for 3v1's file."* Tried with the
version set to 2 for a moment: `5v1` drove the shipped seed 5, `7v1` drove his own saved `gen-7.json` (the
same fingerprint as version 1's seed 7), `3v1` said the above.

**The track fingerprint** (`TrackFingerprint`): eight hex digits over the placements (not the ID or name),
the pieces' shapes, and on generated ground the bedded heights to the centimetre and the rocks to the
millimetre. 4.5 ms a track (the bench). Seed 5 is `0070ecd8`, seed 8 `605be908`, the shakedown `8a6323aa`.
The menu shows it under its top line; M4c files ghosts under it.

**Seed 5 ships as `content/base/tracks/gen-5v1.json`** (decision 3): `--track=gen:5v1` drives it, its
fingerprint is the generated one's, and a test holds it to version 1's pin, so it stays his whatever the
generator does next. That's what frees the cure for jumps straight after corners, whenever he wants it.

The two older pins (`TodaysRoutesStayPut`, `TheSprintsHeDroveStayPut`) stay as they were, comparing each
file under its old ID, which shows codes renamed the tracks and changed nothing else. Tests 308. All sixteen
oracles as before on the committed car (4f918acd), the shakedown 56.76 at 80%; seed 5 at 70% still finishes in
1:25.19, the same splits. The bench's `--generate` gained `--save=<folder>`, each track's file.

## 2026-11-24 — M3 passes; M4 planned

His word, after the rocks: *"It all looks and feels good to me so far Claude lets continue"*. **M3 passes on
it** (M3f: "M3 passes on his word"). What M3f left open stays open and can come back whenever he drives: any
seed he finds broken or no fun, and the cure for a jump straight after a corner (it would move seed 5's
jumps).

"Lets continue" was read as the plan's order, so **M4 is planned** ([docs/M4-PLAN.md](docs/M4-PLAN.md)):
ghosts, replays and track codes, in five slices (codes and fingerprints, the snapshot stream, ghosts,
replays, his play), with seven decisions for him. The first is PLAN's open decision 2, M4 before the
editor. Two findings from the planning, worth keeping:

- **30 snapshots a second is plenty**: a straight line between snapshots misses a curve by the turning
  acceleration ÷ 7200, under 2 mm in any corner, about 5 mm at the loop's foot, 1.4 mm through a jump.
- **Codes free the generator.** With the version in the code, and seed 5 as it is now shipped as its own
  file, the generator can change a seed's track when there's reason (the jumps-after-corners cure), without
  taking seed 5 away from him. That's decision 3.

## 2026-11-24 — M3f: the rocks pass

His word on the rocks: *"they look like the scenery now to me like, they are perfect as they are like
:-{D I stopped noticing them"*. Scenery you stop noticing is what they were for, so `RockSettings` stays as
it is (about 120 per km offered, 12-70 m out, never on the bed). Still to come in M3f: the seeds he likes
or finds broken, and whether a jump straight after a corner should get a straight before it (that would
move seed 5's jumps, so it waits for his word).

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

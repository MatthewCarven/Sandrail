# The car profile, for his tuning (M1e)

> Planned 2026-11-24, before any code, at his ask, while he builds a short track to tune on. Read
> [PLAN.md](PLAN.md)'s "His ideas" (tuning as part of the game, the stock car as a starting point) and
> [M1-PLAN.md](M1-PLAN.md)'s "The car's model" first. Every number here is a starting point labelled as one;
> the decisions at the end are his. Split P1–P5.

His words: *"maybe there would be a way to profile your car over a track and keep the result like a spreadsheet
but no actual functions like just data and have more than one car and start really testing "in game" to make
decisions about where you take the car you have, i mean we could just implement car breeding like but
bruteforcing it doesnt feel right here"*.

So: **drive a car over a track, write down what happened as plain numbers, one row a run, in a file a
spreadsheet opens**, for as many cars as he likes, so setting a car up becomes a matter of comparing rows
rather than remembering how a lap felt. **He makes the decisions; the game only measures.** No breeding, no
search over the numbers: that would tune cars to suit the autodriver rather than him, and it would skip the
trial and error that he wants to be part of the game (*"setting up your car via trial and error"*).

Five things ship: **the meter and the sheet** (P1), **the garage** (P2), **profiling in the game** (P3),
**a ghost for each car** (P4), and **his play** (P5), which is the short track he's making, two or three cars
and the sheet, and which runs straight into his tuning (M1e) and the wheel-spin talk he asked to have then.

Out of scope, on purpose: anything that changes a car's numbers by itself; charts in the game (the
spreadsheet draws charts if he wants them); comparing against other players; changing the car's physics. Real
wheel spin is M1e's to talk over, not this plan's to build.

---

## What's there already

- **The autodriver** drives any sound track on its route's racing line, at a caution from 50% to 100% (the
  share of the car's grip it plans for), through the same input a player uses.
- **The `track` and `lap` oracles** measure a car headless: time, splits, top speed, each jump's lip speed and
  landing, the loop, time off the road, the worst slide, corner by corner on station 6. **`lap` is already how a
  tune is compared** (CLAUDE.md), but from the command line, and only on station 6.
- **F4 records a run**, every tick in 49 columns: the detail behind a moment, about 2 MB a minute. Too fine to
  compare cars by, and the right thing to look at once the sheet says where.
- **Each car has a fingerprint**, eight hex digits over all its numbers; ghosts carry the car they were set on.
- **Time scale** (2026-11-24): `,` and `.`, and the physics the same at any speed (below).

---

## Eight facts that shape it

1. **A run comes out the same every time.** The same car on the same track gives the same result to the
   hundredth of a second and the millimetre, run after run (the seventeen oracles have printed identical lines
   between every commit that didn't touch the car), **and at any speed**: the `track` oracle at ×1, ×8, ×32 and
   ×64 printed the same line, character for character (measured 2026-11-24). **So one run per car and caution
   is the answer, and nothing needs averaging.** A change to the car's physics code changes every figure, so a
   row carries the game's version.
2. **The machine drives about ten times faster than real, headless.** The shakedown's 56.76 s took 58.1 s of
   real time at ×1, 8.2 s at ×8, and 6.9 s at both ×32 and ×64, about 1.1 s of each being loading (measured
   2026-11-24). The ceiling is near ×10, some 1,200 physics ticks a second; Godot's limit of eight ticks a frame
   is the likely cap, and a lever. **In a window it will be less**, as frames are drawn too: measured in P3. A
   30 s short track is about 3 s a run headless.
3. **The autodriver plans from three of the car's numbers**: its grip (times the caution), its top speed
   estimate (power against drag), and a wing's downforce; braking is planned at 0.9 of the grip. **Springs,
   bars, brake force and balance, weight and where it sits, and steering are not in its plan.** It drives the
   same plan whatever they are, and they show in how well it holds it: running wide, sliding, time off the road,
   a spin. So at one caution, two cars of the same grip get the same plan and nearly the same time. **What tells
   them apart is the caution at which each stops being clean**, which is how his evening tune was judged on
   station 6 ("clean up to 85%").
4. **His laps mix two things, him and the car.** The autodriver drives the same way every time, so a run of
   its measures only the car; a run of his measures both. Rows of each, side by side on the same track, say
   which is which: where he's slower than the autodriver on his own car, it's the driving; where every car is
   slow, it's the car or the track.
5. **Where the car is, piece by piece, is already known.** The racing line knows which route piece each of its
   samples belongs to, and the `track` oracle already counts time off the road piece by piece, so **time on
   each piece and the slowest speed on it cost nothing**. A corner's slowest speed is its apex; a jump's is near
   its lip; a loop's is over the top.
6. **The game and the oracle disagree by a whisker today**: on the shakedown, the game's autodriver does 56.73
   (splits 21.52, 40.61) and the `track` oracle 56.76 (21.54, 40.64), for reasons not pinned down (they start
   the car and the clock differently). **So the profile is one code path, the game's**, run in a window or
   headless, never a second copy of it that could hold another truth.
7. **Cars are files, and the game drives one a launch.** The stock car is `content/base/cars/buggy.json`, his
   tunes sit in `%APPDATA%\Godot\app_userdata\Sandrail\tunes\` (two: the 23rd's and the 23rd's late one), and
   `--spec=` picks one. **There's no way to change car in the game**, and **F2's Save writes back to whichever
   file was loaded**, which is how his tune came to sit in the repo's `buggy.json`. That file has moved on
   again since: as of 22:48 on the 24th it's a third car of his, 18 numbers away from his late tune (974 kg, grip
   1.31, the twin-plane wing), **kept nowhere else, uncommitted**, one `git checkout` from gone. And on his
   machine the "stock car" the game loads is his tune, so "against the stock car" would mean against his own.
   Every car's ID is `base:buggy`, so a car's name, in practice, is its file's. **(Since P2: his car is
   `tunes/matthew-2026-11-24.json`, byte for byte, and the repo's `buggy.json` went back to the stock car at his
   word on 2026-11-25.)**
8. **A spreadsheet holds a file it has open.** Excel locks a CSV while it's open, so writing a row then fails.
   (Windows' usual behaviour; P3 checks it and copes.)

---

## The model, written down

**A profile run** is the autodriver driving one car once, at one caution, over one track, from its start gate
to the finish (on a circuit, all its laps), as the race does it: **the same race code, the same clock**.

**A sweep** profiles one car at rising cautions, **50% to 100% in steps of 5%** (decision 2, his: from 50, not
70), until a run isn't clean, that run kept too and marked. **Clean** (starting numbers): finished, under 0.5 s with a wheel off
the road in all, no spin, no respawn. **A spin is a slide past 30°**, as the `lap` oracle has always counted one,
so "clean up to 85%" means what it meant on station 6 (first written here as 90°, changed in P1 to match). A sweep says two things: **the
car's best clean time, and its limit** (the highest clean caution).

**His own runs**: every run of his that finishes a sound track from its start gate gives a row too, the same
columns, driver "you", no caution. Untimed test drives don't.

**`RunMeter`** (Core, pure, tested headless): fed once a tick with the route piece the car's on, its speed,
whether a wheel is off the road, its body slip and whether it respawned; and the gates it passes from the race
timer. It gives a **run's summary**: finished or how far, the time, splits, laps, top speed, time off the road,
the worst slide, spins, and **for each route piece the time on it and the slowest speed on it** (on a circuit,
on the best lap).

**`CarSheet`** (Core, pure): a run's summary as a row of plain numbers and words, under a header made from the
track's route. Two files, both in `user://profiles/` (`%APPDATA%\Godot\app_userdata\Sandrail\profiles\`):

- **`<track name> <fingerprint>.csv`**, one per track, one row per run, newest at the bottom:
  - who and what: when, the track's name (since 2026-11-25, at his ask, so a row says where it was driven even
    copied out of its file), the game's version, driver (`auto` or `you`), the car's name and fingerprint, the
    caution, clean or not, and **the car's differences from the stock car in words** (`grip 1.31 (1.1); mass
    974 kg (650); wing twin-plane (none)`, his working car against the stock one);
  - the run: finished, time, splits, lap times, best lap, top speed, time off the road, worst slide, spins;
  - **time on each piece**, one column a piece in route order (`03 corner-30-banked s`), then **slowest speed on
    each**, the same order (`03 corner-30-banked km/h`).
- **`cars.csv`**, one row per car the first time it's profiled: name, fingerprint, and **every one of its
  numbers**, so a row in a track's file can always be traced to the exact car, even one changed in F2 and never
  saved.

Plain CSV, decimal points, no formulas: it opens in Excel or LibreOffice as it is. **A track whose route changes
has another fingerprint and so another file**, and a file whose header no longer matches (a newer game adding a
column) is left alone and a new one started beside it, so old rows never shift under new columns.

**The garage** (Game): the stock car and every car in `tunes/`, by name. The Esc menu's **Cars** list picks the
car to drive; F2 gains **Save as a new car**, which asks for a name; **the stock car is never written by the
game** (decision 5).

**The profiler** (Game): the Esc menu's **Profile this car** sweeps the car being driven over the track being
driven; **Profile every car** sweeps the whole garage. It drives each run from the start gate as fast as the
machine goes, the camera following, a line on the screen saying which car and caution and how far through; Esc
stops it, and rows already written stay. The same code runs from the command line: `--profile=this|all` with
`--track=`, in a window or headless, which is how Claude checks it.

**Profile ghosts** (P4): each car's fastest clean profile run kept as a ghost under the track, so G can put car
A's best beside him while he drives car B, or the replay can show two cars racing.

---

## Decisions

Numbered so they can be answered by number; the recommendation is first. **All seven taken, 2026-11-24**:
decision 2 with the sweep starting at 50% (*"2. yes but start at 50 some tracks the autodriver wont complete
without this without crashing like the will become obvious as track times rise for later attempts at increasing
aggression i believe"*), and the rest as recommended (*"everything else as you put it please and thanks
Claude"*).

1. **The autodriver profiles cars, and his own finished runs go in the same sheet**, marked "you" (facts 3, 4).
   The alternative is his runs only, which mixes driver and car in every row.
2. **A sweep of cautions, 70% to 100% by 5%, stopping after the first run that isn't clean**, so each car gets
   its best clean time and its limit (fact 3). **Taken from 50%, at his word**: some tracks the autodriver
   can't finish at 70% without crashing, and the limit shows as the times rise with the caution. The alternatives are one run at 80% (quick, but it measures the
   plan more than the car), or every step whatever happens (more rows, more time).
3. **One file per track, one row per run, time and slowest speed for each piece, the car named with its
   differences from stock in words, and every number in `cars.csv`** (the model). The alternatives are every
   one of the car's numbers on every row (some fifty more columns), or one row per piece per run (easier to
   filter, harder to read a run across). **The first alternative was tried on 2026-11-25 at his ask and taken back
   the same night**, once he'd seen it ("now that i see its benefit i understand why it was setup like that"); a
   viewer that sorts and joins the two files is being built instead.
4. **Every finished run of his is written down without asking.** The alternative is only when he presses a key,
   which forgets the lap he didn't expect to be good.
5. **The garage: cars live in `tunes/`, F2's Save on the stock car asks for a name and saves a new car there,
   and the game never writes the stock `buggy.json` again** (fact 7). **His working car is saved into `tunes/`
   first, under a name he chooses, and only then, at his word, does the repo's `buggy.json` go back to the
   stock car**, so nothing of his is lost and "differences from stock" mean what they say. The alternative is
   leaving cars as they are, one a launch by `--spec=`, F2 writing back to the loaded file.
6. **Profiles run as fast as the machine goes, the screen following the car, Esc to stop** (fact 2). The
   alternative is watching each run at a chosen speed, ×1 to ×2, which for seven cautions and three cars is a
   long watch.
7. **Each car's best clean profile run is kept as a ghost to race** (P4). The alternative is no profile ghosts:
   ghosts stay his best and the autodriver's on whatever car.

---

## Slices

### P1 — the meter and the sheet

`RunMeter` and `CarSheet` in Core, with tests: a made-up run fed tick by tick gives the right piece times and
slowest speeds; a circuit's piece columns are its best lap's; a sheet's header comes from the route; a row
appended under a matching header lands under it and a mismatched header starts a new file; differences from
stock name only the numbers that differ, in the panel's units; decimals are points in any culture. The `track`
oracle moves onto `RunMeter` with **its line unchanged**, which is the check that the meter measures what the
oracle did.

### P2 — the garage

**Built 2026-11-24**, with one change: picking a car **starts the scene again with it, on the same track**, rather
than swapping it in place, so nothing built from the old car is left over (the racing line, the autodriver's plan,
F2's file), at the cost of a track's reload.

**Cars set inactive, 2026-11-25**, at his ask (a delete behind a lock, which became *"just need inactive or do not use
for further testing"*): a padlock beside each of his cars in the Cars list opens Set inactive (or Set active). An
inactive car's file moves, unchanged, into `tunes/inactive/`; it stays in the list and can be driven, and **Profile
every car leaves it out**. Names stay unique across both folders. The worklog has the details.

The Cars list in the Esc menu, the car changed in place (F2 already swaps a car's numbers mid-drive); **Save as
a new car** in F2; the stock car never written. **His working car saved into `tunes/` under his name for it
before anything else**, and the repo's `buggy.json` put back only at his word. Checks: switching cars on a
track changes the fingerprint on the board and the ghost's "set on" line; a new car saved appears in the list
and survives a restart; the stock file's bytes don't change after a Save.

### P3 — profiling in the game

**Built 2026-11-25.** A sweep of the shakedown takes 31.5 s of real time in a window (about ×14), and Profile every car
did four cars on seed 5 in 2 minutes 2 seconds. A fix along the way: the autodriver takes the car only once the race
is armed, or a run had a tick's head start whenever the one before ended inside a physics tick (56.70 against 56.73
at 80%). A profile run ends at a respawn rather than going on, so the row says where it went wrong.

**Profile this car** and **Profile every car** in the Esc menu, and `--profile=` on the command line; his
finished runs written down; **Open the profiles folder** in the menu. A row that can't be written because the
spreadsheet has the file open is kept and written once it can, the screen saying so (fact 8). Checks: the
shakedown profiled from the command line gives the same rows twice; at the same caution, the game's profile
time is the game's autodriver time (56.73 for the stock car); how fast a profile runs in a window, measured and
written down; a sweep stops after the first unclean run; three cars on a short track, timed end to end.

### The viewer (2026-11-25, between P3 and P4)

**Built at his ask**, after trying `cars.csv` folded into every row and taking it back: a car's numbers belong once
per car. So instead there's `tools/profile-viewer/`, a program of its own, `SandrailProfiles.exe`. It lists the
profiles folder's sheets, sorts the runs by any column (numbers as numbers, blanks last either way), joins each run's
car from `cars.csv` on the fingerprint as the Car columns, and re-reads a sheet when the game adds a row. It only
reads, and never holds a file, so a row is never kept aside because of it. Python and DearPyGui, the stack of his
trellis-gui and PyBindery, on DearPyGui's own table. Checks: the reader and the window tested headless (38 tests);
the real game profiling the stock car into a scratch folder while the viewer had it open, with all 8 rows in the sheet
and on screen, none kept aside; his real sheets read and left untouched.

**Sorting by three columns, a track's files as one, and All tracks** (2026-11-25, at his ask: "sort by date then by
track name and maybe a 3rd"). Three boxes above the table, a column and a way each; a track's `(2)` and `(written
while open)` files read as one sheet, every row with a track; and All tracks, every track's runs with the columns
they share (the run, the summary, the car), so a sort by track means something. The worklog has the rest.

### P4 — profile ghosts

**Built 2026-11-25.** Each car's ghost is `ghosts/<track fingerprint>/profile-<car fingerprint>.ghost`, filed by the
car's fingerprint, not its name, because the fingerprint is the car (as in `cars.csv`): a tune changed in F2 is another
car, with a ghost of its own to race the old one's. The header carries the name and the caution for the board (`GHOST
matthew-2026-11-23's profile best, 1:18.70 at 70%  (G)`). The Esc menu's Watch a car's profile best lists them;
`--ghost=` and `--replay=` take a car's name. A fix on the way: a profile run ended on the tick it finished, before its
ghost had its last snapshot (up to three ticks later), so only runs that happened to finish on a snapshot tick ever made
one. Now a finished run waits for it, and every profile row is the same as before. The check: a fresh profile of the
same car at the same caution came within 17 mm of its ghost on the shakedown and 14 mm on seed 5, a landing each time,
as the `ghost` oracle has it. The worklog has the rest.

Each car's fastest clean profile run saved as a ghost under the track and car; G steps through his best, the
autodriver's, and each profiled car's; the replay list offers them. Check: a profile ghost plays against a
fresh profile run of the same car at the same caution with the `ghost` oracle's worst misses.

### P5 — his play

His short track, two or three cars (the stock car, his tunes), a sweep each and some laps of his own, and the
sheet opened. **Does it help him decide where to take his car?** Each verdict goes in the worklog verbatim, and
it passes on his word. **Then his tuning (M1e)**: real wheel spin, his V-line driving and `forwardPriority`,
raised as he asked, with F4 runs of his hot laps and now the sheet beside them.

---

## Order and why

The meter comes first because everything else writes what it measures, and moving the `track` oracle onto it
proves it measures the same things. The garage comes before profiling because "every car" needs cars to be
somewhere, and because his working car is at risk until it's saved somewhere that isn't the repo. Profile
ghosts come after the sheet because they're a pleasure, not the measurement.

## Levers, named now so nobody has to rediscover them

The sweep's range and step; what counts as clean (time off the road, the spin angle, respawns); whether the
sweep stops at the first unclean run; the columns and their order; the decimal places; the piece labels; the
file names; how fast a profile runs (and Godot's eight ticks a frame); whether his test drives count; which
ghosts G steps through.

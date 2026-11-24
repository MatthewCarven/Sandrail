# M4 — Ghosts, replays and track codes

> Planned 2026-11-24, before any code, the day M3 passed. Split M4a–M4e. Read [PLAN.md](PLAN.md) first,
> especially "Ghosts, replays, and later multiplayer", "Track codes" and suggestions 2, 3 and 8. Every
> number here is a starting point labelled as one; the decisions at the end are his.

M3 makes a track from a seed, and he's learning seed 5 in whole runs. M4 gives a run somewhere to go:
**your best run on a track, driven beside you as a ghost**; **replays** of a run from the chase camera,
cameras beside the track and a free one; and **track codes**, which say exactly which track a seed made, so a
shared code is the same track and a ghost is never laid on the wrong one. With them, **bests are kept
between sessions**, which the board has waited for since M2d.

Five things ship: **track codes and fingerprints** (M4a), **the snapshot stream** (M4b), **ghosts** (M4c),
**replays** (M4d), and **a play** (M4e), which is his: whether racing his ghost makes him want another run.

Out of scope, on purpose: multiplayer, though the stream is built so a network car is "the same stream,
sent" (PLAN suggestion 2); leaderboards and sharing ghosts between players; sound (the stream carries the
throttle for it); the editor (M5).

**M1e's tuning play and M2e's shakedown play are still his**, whenever he likes. A ghost names the car it was
driven on, so ghosts become a way to compare tunes by eye as well as by the `lap` oracle.

---

## What's there already

- **The race clock** (`RaceTimer`, M2d): it starts on the first throttle at the start gate and stops at the
  finish, with a split at every gate. R puts the car back at the last checkpoint with the clock running; a
  tap before the first checkpoint, holding R and Backspace all start again from zero.
- **Bests on the board**, his and the autodriver's apart, a run and (on a circuit) a lap, **for the session
  only** (`Main.Race`: "keeping them between sessions waits for M4").
- **`RunRecorder`** (F4, M1b): every tick of a drive to a CSV, 49 columns, for tuning. It's a diagnostic, about
  2 MB a minute, not a ghost; it stays as it is.
- **The car's fingerprint**: eight hex digits naming a spec's exact numbers, saved beside every recorded run.
- **A laid track knows its stunts** (`TrackRacing.IsStunt`) and its route as one line, the autodriver's.
- **The Esc menu** (M3e): the seed, Generate, New random seed, Save this track.

---

## Nine facts that shape it

1. **A ghost records what happened, not the inputs** (PLAN suggestion 3). Jolt doesn't promise the same
   result twice, and the car changes under the tuning panel mid-drive, so feeding a run's inputs back
   through the physics would drift off the road within seconds. Positions and rotations can't drift.
2. **30 snapshots a second is plenty.** Physics runs at 120 ticks a second, so that's every 4th tick
   exactly. Playback draws straight lines between snapshots, and the most a straight line misses a curve
   by is the turning acceleration ÷ 7200 (the sagitta of 1/30 s of arc): **under 2 mm in any corner** at
   1.2 g, **about 5 mm at the loop's foot** (26 m/s on an 18 m radius), **1.4 mm through a jump's arc**.
   Rotations are slerped; a spin at a full turn a second is 12° between snapshots. The `ghost` oracle
   (M4b) measures the real miss rather than trusting the arithmetic.
3. **A run is small.** About 30 bytes a snapshot (position, rotation, steering, four suspension lengths,
   throttle, brake, handbrake, a flag), so **about 0.9 KB a second**: seed 5's 1:25 run is about 75 KB and
   a 3½-minute circuit about 190 KB, before compression. PLAN's "a few hundred KB" was 120 floats a second.
4. **A ghost belongs to exactly one track.** Played on a track whose road or ground has moved, it drives
   through hills. A track's ID doesn't say that: `gen:5` names whatever the generator makes of seed 5
   today. So a ghost names its track by **a fingerprint of what's driven**: the placements, the pieces'
   shapes from the catalogue, the ground's heights (to the centimetre, so a last-bit difference between
   machines doesn't count) and the rocks. It's the same idea as the car's fingerprint.
5. **A seed is only a track for one version of the generator.** Any change that moves a road, such as
   the cure for jumps straight after corners (the M3c lesson), changes some seeds' tracks, and today
   nothing records which generator made a track. That's what a **code** is for (PLAN, "Track codes"): the
   version and the seed. The build has only its own generator, so it can't make an old version's track
   again. **It can still load one if it has the file**, and the file keeps the pieces whatever the
   generator does next. The file remakes its ground from code, though (terrain, bed, rocks), so a change to
   those still reaches it. Those are the changes to make with care, and the fingerprint says when one has
   happened.
6. **The ghost runs on the race clock.** It shows where its run was at the clock's time. R back to a
   checkpoint, clock running, leaves the ghost where it is, so you see what the crash cost. A restart puts
   it back on the start line. A run with an R in it has a jump in its stream, and playback must cut there,
   not draw the car flying from the crash to the checkpoint.
7. **The car's looks are built inside `Buggy`**, mixed in with its physics. The ghost and the replay car
   need the same looks with no physics, built from the ghost's own spec, since its car may have a wing
   yours hasn't. So the looks move into a node of their own, which the buggy then uses too.
8. **A saved ghost already holds a best**: its time, its splits and, on a circuit, its laps. Keeping bests
   between sessions is reading the ghost files back. No second store is needed.
9. **A laid track knows where its stunts are**, so the replay cameras (PLAN suggestion 8: "the generator
   places replay cameras") can be placed from the laid track instead, when the replay starts. That gives the
   shakedown and saved tracks cameras too, and keeps them out of the track file and the generator's seeds.

---

## The model, written down

**The generator version** (`TrackGenerator.Version`, 1 today): it goes up whenever a seed's track
changes. **A test holds it to that**: it pins the fingerprints of seeds 1 to 10 under each version, so a
change to the generator's output fails it until the version goes up and the new fingerprints are pinned
beside the old. `TodaysRoutesStayPut` and `TheSprintsHeDroveStayPut` become part of it.

**A track code** (`TrackCode`): the seed and the version, written **`5v1`**. The menu's box takes a code,
or a bare seed, which means this build's version. A generated track's ID becomes `gen:5v1` and its name
stays "Seed 5". A track saved before codes, ID `gen:5`, is read as version 1. **A code from another
version** is looked up among the game's own tracks and `user://tracks/` by its ID and loaded if found.
Otherwise the menu says so: "5v1 was made by generator version 1; this build makes version 2, where seed
5 is a different track. Drive that, or ask for 5v1's file." Nothing quietly drives a different track.

**A track's fingerprint** (`TrackFingerprint`, Core): eight hex digits over the placements as `Save`
writes them, the pieces they use as the catalogue holds them, and, on generated ground, the bedded
heights to the centimetre and the rocks. It's worked out once when a track is built, in a few
milliseconds; the bench measures it.

**A snapshot** (`Snapshot`, Core): at 30 a second from the race clock's zero, position (3 floats),
rotation (a quaternion, 4 × 16 bits), steering angle, the four wheels' droop (a byte each, of the travel),
throttle and brake (a byte each), and flags (handbrake, and **cut**: don't interpolate into this one, the
car was put back). The front wheels' angles come from `WheelModel.FrontWheelsDeg` and the spec; the wheels'
spin comes from the speed between snapshots.

**A ghost** (`Ghost`, Core): a header and the snapshots. The header holds the format, the track's
fingerprint, ID, name and code, the car's fingerprint and its whole spec (as runs save it beside them), whose
run it was (his or the autodriver's), the time, the splits, the laps and the date. `At(t)` gives the car at
any time, interpolated, holding at the cut flags. **The file**: one line of JSON for the header, readable
by eye, then the snapshots packed and deflated. **Where**: `user://ghosts/<track fingerprint>/you.ghost`
and `auto.ghost`, so a track's ghosts sit together and a changed track's are simply never found.

**The game:**

- **`CarLooks`**: the buggy's grey boxes, wheels and wing, built from a spec, posed by a snapshot or by the
  buggy.
- **The recorder**: every 4th tick from the clock's start to the finish, the car's pose into a stream; a
  respawn marks the next snapshot cut. On the finish, if it's a best, the stream becomes that driver's
  ghost file, as an autodriver run is its own (M2d's rule: a run it had the car for at any point).
- **`GhostCar`**: `CarLooks`, see-through, with no body and no collider, posed by `Ghost.At(clock)`. Before
  the clock starts it waits on the line, and after its run ends it fades out at the finish. It fades when
  it's close to the camera, so it never blocks the road ahead.
- **Replay**: the race stops and the buggy is hidden; a replay car (`CarLooks`, solid) is posed from the
  stream. The cameras are the chase camera, the trackside cameras and a free camera. Controls: Space pauses,
  ← and → skip 5 seconds, ↑ and ↓ change the speed (¼, ½, 1 and 2), C changes the camera, Esc leaves.
- **Replay cameras** (`ReplayCameras`, Core): from the laid track, one beside each stunt looking across
  its lip and landing, one at the start, and one on the outside of a corner every 150 m or so, each a few
  metres above the ground there. In a replay the camera is the one whose stretch of road the car is on,
  turning to follow it.

---

## Decisions

Numbered so they can be answered by number; the recommendation is first. **All seven taken as
recommended, 2026-11-24**: *"Yes, as recommended"*.

1. **M4 now, before the editor** (PLAN's open decision 2). Ghosts make generated tracks worth driving
   again, and they'll be the way to compare tunes when he tunes after the editor. His "lets continue" was
   read as the plan's order. The alternative is the editor first (M5), and M4 after it.
2. **A code is the seed and the generator version, nothing else**: `5v1`. A track made with other dials
   (`--stunts=`, `--checkpoints=`) is shared as its file. Those dials are developer flags today, and a code
   stays short enough to say out loud. The alternative is the dials in the code too (`5v1-s40-c300`).
3. **Seed 5 as it is now ships with the game**, as `gen:5v1`, so no generator change can take it away,
   and his best and ghost on it stay with it. That frees the generator to change a seed when there's a
   reason, with the version going up, for instance the cure for jumps after corners, which would move seed
   5's jumps. Any other seed he names can ship the same way. The alternative is never changing an
   existing seed's road, only adding new dials, which is how the pins have kept seed 5 by hand so far.
4. **The ghost is your best run by default, and G changes it** to the autodriver's best, then none. The
   autodriver's ghost shows a line through a track he's learning. The alternatives are his best only, as
   PLAN had it, or both at once.
5. **On a circuit, the ghost is the best whole run**, 3 laps on the run's clock, so it's racing the
   thing that's timed. The alternative is the best lap, starting again from the line each lap.
6. **Bests and ghosts are per track, whatever the car.** The ghost names the car it was driven on, and
   the board marks a best set on another car. The alternative is per track and car, which means every change
   in F2 starts a fresh set of bests, since the fingerprint changes with every number.
7. **Replays are the last run and the saved bests**, watched from the Esc menu or with Enter after the
   finish. The alternative is keeping every finished run as a file to watch again, a folder that grows,
   worth it if he wants to keep a good run that wasn't a best.

---

## M4a — track codes and fingerprints

**Built 2026-11-24**, as below; 21 tests, worklog that date. Departures: **the two older pins stay as they
were**, comparing each track's file under its old ID (`gen:5`), which shows codes changed what a track is
called and nothing else; **a track made with other dials has no code**: its ID is `gen:5v1-dials` and its
name "Seed 5 (own dials)"; **`--seed=` takes a code too**, and an old one drives its file as the menu does;
the menu's box is "Code", with the track's fingerprint under its top line; the bench's `--generate` times
the fingerprint (4.5 ms a track) and `--save=` writes each track's file. **Seed 5 ships as
`content/base/tracks/gen-5v1.json`**, fingerprint `0070ecd8`, the same as generated. Tried with the version
set to 2 for a moment: `5v1` drove the shipped file, `7v1` drove his own saved `gen-7.json` (version 1's seed
7, the same fingerprint), and `3v1` said why it couldn't.

- **Core:** `TrackGenerator.Version` (1), `TrackCode` (parse and write `5v1`; a bare seed), generated IDs
  `gen:5v1` (an old `gen:5` read as version 1), `TrackFingerprint`.
- **Game:** the menu's box takes a code; the menu's top line, the opening flash and the seed print show the
  code; an old code loads its file if the game has it, or says why it can't. If decision 3 is yes, seed 5
  ships as `content/base/tracks/gen-5v1.json`.
- **Checks:** the version test (seeds 1 to 10 pinned under version 1; seeds 1, 3, 5 and 7 still their
  roads, the IDs aside); codes round-trip, and nonsense is refused in words; a fingerprint is the same
  each time and different when a piece, a height or a rock moves; seed 5 shipped and seed 5 generated have
  the same fingerprint; the bench's time per track hardly moves.

## M4b — the snapshot stream

- **Core:** `Snapshot`, `Ghost` (the header, packing and deflating, `At(t)` with its cuts), their file.
- **Game:** `CarLooks` out of `Buggy`, which then uses it (nothing it shows may change); the recorder on the
  race clock.
- **A `ghost` oracle:** the autodriver drives seed 5 at 70%, the run is recorded, saved and loaded, and
  played back against the car's own position at every one of the 120 ticks a second: the worst miss, in a
  corner, in the loop and through the jumps (fact 2 says millimetres), and the file's size.
- **Checks:** a ghost saved and loaded is the same ghost; `At` lands on every snapshot exactly and between
  them on the straight line; a cut is never interpolated across; a ghost from a newer format is refused by
  name, as track files are; all sixteen oracles unchanged, since the looks moved and nothing else did.

## M4c — ghosts

- **Game:** `GhostCar` on the race clock (fact 6), beside you; the best saved on the finish; ghosts and bests
  loaded when a track is built, so the board's bests last between sessions; G to choose (decision 4); a
  best on another car marked (decision 6).
- **Checks:** a screenshot with the ghost beside the car; the autodriver's ghost on seed 5 followed by the
  autodriver itself, the two staying together to within the oracle's miss; restarting, R to a checkpoint and
  a finish slower than the ghost all do what fact 6 says.

## M4d — replays

- **Core:** `ReplayCameras` from a laid track.
- **Game:** replay mode (the last run and the saved bests, decision 7), its cameras and its controls.
- **Checks:** every camera's spot is above the ground and can see the road it covers; screenshots from the
  trackside cameras on seed 5 and a circuit; a replay of a run with a respawn cuts cleanly.

## M4e — the play

His: **whether racing his ghost makes him want another run**, and whether the replays show the bits worth
seeing. Each verdict goes into the worklog verbatim. M4 passes on his word.

---

## Order and why

Codes and fingerprints come first, because every ghost file names its track by them (fact 4), and a ghost
saved under a name that later changes is a ghost lost. The stream comes next, with the oracle that says 30
a second is enough, before anything is built that shows it. Ghosts come before replays because they're
the part he'll use every run, and replays reuse all of it. Each slice ends runnable and committed.

## Levers, named now so nobody has to rediscover them

The snapshot rate; how the stream is packed; how close the ghost comes before it fades, and how
see-through it is; which ghosts G cycles through; the replay cameras' spacing, height and how long one
holds before the next takes over; the replay speeds and the skip; the generator version and which seeds
its test pins; which seeds ship as files.

# M5 — The editor

> Planned 2026-11-24, before any code, the day M4 passed. Split M5a–M5e. Read [PLAN.md](PLAN.md) first,
> especially suggestion 6 ("one key from the editor into the car at the cursor, one key back") and the M5
> line, and [M2-PLAN.md](M2-PLAN.md)'s model, which is what the editor edits. Every number here is a
> starting point labelled as one; the decisions at the end are his.

M2 made tracks out of pieces, M3 generated them from a seed, and M4 made each one worth driving again. M5 lets
him **make and change tracks himself**: put pieces on the grid, turn them, raise them, take them away, try the
track at once from wherever the cursor is, and save it. PLAN's line for it: grid cursor, piece palette,
rotate, raise and lower, place and erase; generate from a code and then edit; test-drive from the cursor;
validate; save and load. **The edit-and-test loop is the whole editor** (suggestion 6).

Five things ship: **the editor's core** (M5a), **laying track** (M5b), **test-driving** (M5c), **ground,
saving and circuits** (M5d), and **a play** (M5e), which is his: whether making a track is quick and fun
enough that he wants to make another.

Out of scope, on purpose: sculpting the terrain; new pieces (pipes, corkscrews, bridges, PLAN's "after");
selecting, copying and pasting groups of pieces; scenery; sharing beyond the track's file.

**After M5 comes his tuning.** M1's play waits on it (*"i'll do better tuning after the track editor is
complete"*), and real wheel spin, his V-line driving and `forwardPriority` are to be raised then, at his ask.

---

## What's there already

- **The model** (M2): the grid (32 × 32 tiles of 20 m, levels of 2.5 m), seventeen pieces as data, a
  placement as piece, tile, level, facing and mirrored. **`Lay` says every way a track is unsound, in
  words**, names the pieces at fault, and finds the route.
- **Circuits** (M3g): format 3, `laps`, the start-finish piece; `Lay`, the timer and the autodriver all do
  them.
- **Each placed piece is a body of its own** (`TrackView.Build`), named for the piece and its tile.
- **The ground** (M3a, M3d): terrain from a seed and dials; the bed laid from the pieces; rocks from both.
- **The race, ghosts and replays** (M2d, M4): any sound track can be raced, its bests kept under its
  fingerprint, its runs replayed.
- **The Esc menu**: Generate, New random seed, Save this track, Load a track, Watch.

---

## Seven facts that shape it

1. **Validating is free.** `Lay` takes under 1.5 ms on a generated track (the bench, 2026-11-24), so the
   editor checks the whole track after every change and always knows what's wrong with it.
2. **A track being built is unsound for most of its life**, with half a route and loose ends, so `Lay`'s
   problems are the editor's to-do list, not errors. But **`Track.Load` refuses an unsound track**, and so
   does `--track=`, so the editor needs a way in that keeps a track's problems instead of refusing it. Only a
   sound track can be raced.
3. **A piece is quick to build**: the shakedown's 35 pieces build in about 50 ms, under 1.5 ms each, so a
   placed piece is built on its own, at once, and nothing else is rebuilt.
4. **The ground isn't quick.** On a seed's ground, the bed takes 36 to 84 ms to lay again and the rocks 1 ms
   (the bench), but **Godot's terrain mesh and collider take about 400 ms** (a saved track builds in 559 ms,
   of which the pieces are 50). That's too slow for every click. The bed is Core, pure, and can be laid off
   the main thread, so the ground can catch up a moment after an edit while the pieces show at once.
5. **Corners, the loop and the jumps are big**: corners 2 × 2 and 3 × 3 tiles, the loop and S-bend 2 × 5,
   jumps 5 and 7 tiles with a gap. Placing them by hand means turning and offsetting each to meet the last
   piece's way out, which is fiddly. Laying the next piece onto the open end of the road does that for him,
   the way model railway track goes down.
6. **An edited generated track isn't its code's track any more.** Its fingerprint changes with the first
   piece moved, so its ghosts and bests start afresh on their own (M4a's design), but its ID (`gen:5v1`)
   would claim a code that no longer makes it. It needs a name of its own.
7. **One key in, one key back** (suggestion 6) needs the car somewhere sensible: on the piece under the
   cursor, at its start, facing along it (every piece has a start frame), and the editor's cursor and camera
   just as they were when he comes back.

---

## The model, written down

**`TrackEditor`** (Core, pure, tested headless): a track being edited and its history.

- **Place** a piece at a tile, level, facing and mirror: refused, with the reason, if it runs off the grid
  or onto another piece's tiles; otherwise the track with it added. **Erase** the piece on a tile.
  **Undo** and **redo**, any number of steps, since a track is a few hundred bytes.
- **Open ends**: every way out (and way in) that nothing meets. **Next**: for a piece and an open end, the
  placement that continues the road from it, its tile, facing and level worked out (fact 5).
- After every change, **`Lay`**: the problems and the route, which the editor shows.

**The editor** (Game):

- **A camera above the grid**, looking down at an angle, following the cursor; zoom, and turn in quarter
  turns.
- **The cursor**: a tile and a level, moved by the mouse (pointing at the grid) or the arrow keys. **The
  piece in hand** shows at the cursor as see-through, green where it fits and red where it doesn't.
- **The palette**: the seventeen pieces in a bar along the bottom, picked by the mouse wheel, number keys or
  clicking.
- **The problems**, listed in a panel in `Lay`'s words, the pieces at fault tinted red.
- **Test drive**: one key into the car on the piece under the cursor, one key back (decision 4).
- **Save** with a name (decision 6); the Esc menu's Load list opens saved tracks in the game, and an
  **Edit this track** button opens the one being driven in the editor.

---

## Decisions

Numbered so they can be answered by number; the recommendation is first. **All seven taken as
recommended, 2026-11-24**: *"Yes, as recommended"*. Decision 7 folds PLAN's M6 into M5.

1. **The editor opens from the Esc menu**: **Edit this track** (whatever is being driven: a generated track,
   a saved one, or the game's own) and **New track** (an empty grid). The alternative is a separate editor
   started from the command line only.
2. **Track is laid onto the road's open end** (chain mode, fact 5): the cursor waits at the end of the road,
   facing along it; pick a piece and Enter lays it there, and the cursor moves on to its new end, like model
   railway track. A free cursor (the mouse or the arrows) does everything else: place anywhere, turn,
   mirror, raise and lower. The alternative is a free cursor only, every piece turned and lined up by hand.
3. **Mouse and keyboard, at the desk**: point and click to place, right-click to erase, the wheel to pick a
   piece; and everything on keys too (arrows to move, Q and E to turn, Page Up and Page Down for the level,
   M to mirror, Delete to erase, Ctrl+Z and Ctrl+Y to undo and redo). No pad in the editor. The alternatives
   are keyboard only, or the pad as well.
4. **Test drive is Tab, into the car on the piece under the cursor, and Tab back** to the editor as it was.
   From anywhere it's an untimed drive; from the start gate of a sound track the race works as always, ghosts
   and replays included. The alternative is test-driving from the start only.
5. **A track is edited on the ground it came with, and M5 doesn't sculpt it**: a generated track keeps its
   seed's terrain, the bed laid again a moment after he stops editing (and before every drive), off the main
   thread (fact 4). A new track starts on the flat plain, or on a seed's ground if he types one. Tools to raise
   and lower the ground can come later. The alternative is terrain tools now, a milestone's worth on its own.
6. **Saving asks for a name, and the track is his**: `user://tracks/<name>.json`, ID `user:<name>`. An
   edited generated track becomes a new track (fact 6). **Unfinished tracks save too**, and the editor reopens
   them as they were (fact 2); the Load list marks them and opens them in the editor, not the game. The
   alternative is saving only sound tracks.
7. **Circuits are in M5 from the start** (PLAN's M6 folded in): the start-finish piece and the laps in the
   editor, since the model, `Lay`, the timer and the autodriver already do circuits. The alternative is
   leaving circuits for M6.

---

## M5a — the editor's core

**Built 2026-11-24**, as below; 8 tests, worklog that date. Departures: **the editor reads tracks with
`TrackView.ReadTrack`**, which parses and doesn't refuse, and builds each piece with `TrackView.BuildOne`;
**a track on generated ground is refused for now** (M5d takes it on), and so the game's menu offers Edit this
track only on the plain; **the problems are in plain words**, "the corner-30 at (14, 13)" for the file's
"pieces[3] (base:corner-30 at (14, 13))"; the editor starts its cursor at the newest open end of the road. The
keys were pressed by a script (Enter, the arrows, ], Ctrl+Z, Ctrl+Y, Delete, a refused Enter) and did what
they say.

- **Core:** `TrackEditor`: place, erase, refusals in words, open ends, next, undo and redo; the way in that
  keeps a track's problems (fact 2).
- **Game:** the editor from the Esc menu (decision 1): the camera, the cursor on keys, the piece in hand
  green or red, place, erase, turn, mirror, raise and lower, undo; the problems panel. On the plain.
- **Checks:** tests that placing, erasing, undo and redo give the tracks they should; that a piece off the
  grid or onto another is refused, saying why; that **next** from every piece's way out, for every piece,
  meets it exactly (`Lay` finds no problem between them); that a track built in the editor from nothing is
  the shakedown, piece for piece. Screenshots of the editor.

## M5b — laying track

**Built 2026-11-24**, as below; worklog that date. Departures: **laying a piece anywhere goes to its end**,
so a start gate put down with the free cursor is followed onto at once; **the hand changes by itself** around
jumps (the landing after a lip, a straight after the landing) and after the start gate; **Backspace at the
road's end takes back its last piece**; N goes to another open end, F back to the newest; on an empty grid the
start gate is in hand. The palette is two columns down the left on a dark backing, with 1-9 for its first nine.
The test that **next** alone lays the shakedown came with M5a. A script laid a track from nothing by the keys,
and a right click by the mouse.

- **Game:** chain mode (decision 2), the palette, the mouse (pointing, clicking, the wheel; decision 3).
- **Checks:** a test that laying the shakedown's pieces in order by **next** alone builds the shakedown;
  screenshots of a track half laid.

## M5c — test driving

**Built 2026-11-24**, as below; worklog that date. Departures: **Tab starts the scene again as a test drive**,
reusing the game's own setup (the car, its camera, R, the autodriver, and from the start gate of a sound track
the race, ghosts and replays) rather than a second one; the editor's track, history and view wait outside the
scene in `EditorSession`, and Tab back puts them as they were. **Tab on empty ground drives from the track's
start.** `--track=@editor` is the editor's track, and **`--drive-from=<column>,<row>`** test-drives any track
from a piece, untimed, reading its file without refusing it. R and Backspace go back to where the drive began.

- **Game:** Tab into the car on the piece under the cursor and back (decision 4); the race from the start
  gate of a sound track; the autodriver (P) on an edited track.
- **Checks:** the car starts on the right piece facing the right way for a piece of each kind; the `track`
  oracle drives a track made in the editor; the cursor and camera are as they were after Tab back.

## M5d — ground, saving and circuits

**Built 2026-11-24**, as below; 3 tests, worklog that date. Departures: **the editor's ground is 64 squares of
128 m**, and a change rebuilds only the squares whose ground moved: half a second after the last change, the bed
is laid under every placed piece and the moved squares' arrays worked out off the main thread (about 40-80 ms),
and showing them takes about 2 ms, so the worst frame stays at 10 ms. The first version rebuilt the whole mesh,
50 ms on the main thread and a 66 ms frame. **`Bed.Lay` takes any pieces**, so an unfinished track has its bed
in the editor and on a test drive (and no rocks, which keep clear of a route). **The free cursor rides the
ground**: its level is the ground's under it, Page Up and Down lifting it. **Saving is Ctrl+S**, a name box (the
track's own name, or "My track", "My track 2"), Enter to save as `user:<name>`, Esc not to. A generated track
becomes "Seed 5, edited" at its first change. **Laying a start-finish line makes a circuit of 3 laps**, L
changes them, and taking the line away makes a sprint again. `--saved=<folder>` moves where tracks are saved,
which the checks use. The menu has **New track on this ground** on a generated track, and **Edit this track**
hands a generated one to the editor directly.

- **Game:** editing a generated track on its terrain, the bed laid again in the background (decision 5);
  a new track on a seed's ground; Save with a name (decision 6); unfinished tracks saved and reopened; Edit
  this track; the start-finish piece and the laps (decision 7).
- **Checks:** an edited generated track saved and loaded is the same track, on the same ground; an unfinished
  track saves and reopens with the same problems; a circuit built in the editor is raced three laps by the
  autodriver; the game stays smooth while the ground catches up.

## M5e — the play

His: **whether making a track is quick and fun enough that he wants to make another**, and whether driving
his own track is as good as driving a generated one. Each verdict goes into the worklog verbatim. M5 passes
on his word. **Then his tuning (M1e)**, with the wheel-spin talk he asked to be reminded of.

---

## Order and why

The core comes first because everything the editor does is a change to a track and a look at its problems,
and those want testing headless before anything draws them. Laying track comes next because it's what makes
the editor quick. Test-driving comes before the ground and saving because it closes the edit-and-test loop
on the plain, where rebuilding is cheap, before the slow ground is in the way. The ground, saving and
circuits come last, each one a matter of carrying what M3 and M4 already do into the editor.

## Levers, named now so nobody has to rediscover them

How long after the last edit the ground catches up; the camera's height, angle and zoom; how far the cursor
jumps; whether the cursor starts in chain mode; the piece in hand's colours; how the problems are worded and
which pieces they tint; the undo depth; where Tab puts the car on long pieces; the name rules for saved
tracks.

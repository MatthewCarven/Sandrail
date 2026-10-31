# Worklog

Newest first. What happened and why; findings worth keeping.

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

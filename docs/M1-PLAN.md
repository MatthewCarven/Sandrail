# M1 — The buggy

> Planned 2026-10-31, before any code. Split M1a–M1e. Read [PLAN.md](PLAN.md) first, especially "The
> car" and decision 4 of his ("physics feel, tuned together in playtesting"). Every number here is a
> starting point labelled as one; the first measurements are M1a's oracles.

M0 built a plain. M1 puts a car on it and makes the car **feel right**, which is the one thing every
later milestone is judged through: a generated track is only as good as driving it. Five things ship:
a **raycast buggy** on Godot's Jolt; the **chase camera**; a **test ground** of hand-placed pieces (a
jump, a slope, a banked curve, a loop, a field of bumps) from the first version of the piece mesher;
**the tools for tuning it together** (telemetry, live sliders, recorded runs, headless oracles); and
**the play** that passes it, which is his: *he drives it and says it feels right.*

Out of scope, on purpose: the track grid and catalogue (M2), generated terrain (M3), checkpoints and a
timer (M2), sound beyond nothing, the buggy's real body (grey boxes until he models one), a second car.

---

## Six facts that shape it

1. **Claude can't drive.** Feel is reported in words by the one person who can play, so M1 has to
   turn his words into numbers Claude can read. Hence M1b's telemetry and recorded runs, and M1a's
   oracles, which drive the car headless and print figures. Without them the tuning sessions are
   guesswork on both sides.
2. **Physics runs at 120 Hz and the screen at whatever the monitor does.** At 60 or 144 frames a second a
   car stepped at 120 visibly judders unless its drawn position is interpolated between ticks. Godot's
   3D physics interpolation (`physics/common/physics_interpolation`) does that, and the camera then
   reads `GetGlobalTransformInterpolated()`. On from M1a, not discovered in the play.
3. **At top speed the car covers 0.42 m a tick.** A ray from the chassis can't miss the ground at that,
   but a thin piece can be tunnelled through by the chassis on a hard landing. Road slabs are at least
   0.5 m thick, and continuous collision detection on the car is a lever if that isn't enough.
4. **A raycast wheel is a point.** It drops into a gap between two pieces and snags on a sharp edge such
   as a ramp's lip. M2's pieces meet exactly, so gaps are M2's problem; edges are softened by giving the
   mesher a lip radius. If wheels still catch, the lever is a sphere cast in place of the ray: same
   code, a different query.
5. **Without help, a light car with long suspension rolls over in every corner.** The arcade fixes are
   cheap and standard: a centre of mass lowered below the chassis's middle, an anti-roll bar, and grip
   forces applied a little above the contact point. All three are levers from M1a, because the first
   circle oracle needs them.
6. **What multiplayer and ghosts need from the car is its numbers and its pose, not its physics.** So
   the car's spec (every tunable number) is a record in Core with a JSON file, and the physics that
   reads it lives in Game. A ghost replays poses; a remote player will be sent the spec's ID and poses.

---

## The car's model, written down

Per physics tick, for each of the four wheels, a ray goes down from its mount along the chassis's own
down, as long as the suspension at full stretch plus the wheel's radius. **No hit: the wheel is in the
air and contributes nothing.** A hit gives:

- **Suspension.** Compression `x` is the rest length minus (hit distance − wheel radius), clamped to
  [0, travel]. Force is `k·x + c·ẋ`, where `ẋ` is the change in compression since last tick divided by
  the step, and `c` is the bump damping when compressing and the rebound damping when extending. Never
  negative: a spring can push the car up but never pull it down. Applied along the chassis's up at the
  mount. **A bump stop** adds a stiff spring over the last 10% of travel, so a big landing ends on the
  stop rather than on the chassis box.
- **Load.** The suspension force is the wheel's load `Fz`, which is what caps its grip.
- **The contact frame.** The hit normal `n`; the wheel's forward `f` (front wheels turned by the steering
  angle) flattened onto the ground plane; sideways `s = n × f`. The ground speed at the contact point is
  split into `v_f` and `v_s`.
- **Sideways grip.** Slip angle `α = atan2(v_s, max(|v_f|, 3 m/s))`. Force rises linearly to the full
  `μ·Fz` at the peak slip angle (8° to start), then eases to 80% of that by twice the peak, which is the
  slide. **Below about 2 m/s a direct term cancels sideways speed instead**, or the car creeps sideways on
  a slope at rest. `μ` is the car's grip times the surface's.
- **Forward force.** Engine on the driven wheels: throttle × min(the force limit, power ÷ speed), so it
  pulls hard off the line and fades with speed. Brakes oppose `v_f`, biased to the front. Rolling
  resistance is proportional to load and much higher on sand, which is what makes sand slow. Holding
  brake at a standstill reverses, weaker and capped at 10 m/s.
- **The friction circle, forward force first.** Forward force is clamped to `μ·Fz`; sideways force
  gets what's left, `√((μ·Fz)² − F_f²)`. That one rule is where power oversteer comes from: floor it in
  a corner and the rear wheels spend their grip pushing, so the tail steps out. No wheel spin is
  simulated; wheels are spun visually from `v_f`, faster when the forward force is clamped.
  **Changed at M1a (2026-10-31): forward-first is now one end of a dial**, `forwardPriority`, whose
  other end shares a shortfall between pushing and turning in proportion; 0.3 to start. The keyboard
  answer (decision 1) is why: a key is full throttle, and at full forward-first a held throttle below
  about 24 m/s spends a rear tyre's whole grip pushing, so the car would spin whenever it turned. The
  dial keeps power oversteer and makes it gradual.
- **Where it's applied:** at the contact point raised toward the centre of mass by a fraction (a roll
  lever, fact 5).

Then, once per tick for the whole car: drag `−c_d·|v|·v`; downforce `k_df·v_f²` along the chassis's
down (M1d); an anti-roll force on each axle proportional to the difference in compression between its
two wheels; and with no wheel on the ground, air control (M1d).

**Steering** turns toward the input at a limited rate, so a keyboard tap isn't a jerk, up to 30° at a
standstill and narrowing toward 8° at top speed.

**Starting numbers** (PLAN's, now with the engine): 650 kg; wheelbase 2.6 m; track 1.7 m; wheels 0.38 m;
travel 0.35 m; springs 16 kN/m a corner (about 0.1 m of sag); bump damping 1,000 and rebound 1,500 N·s/m (1,400 and 2,100 since M1a's drop oracle);
rear-wheel drive; force limit 4,500 N; power 110 kW; `c_d` 0.86, which with 110 kW tops out near 50 m/s;
brakes 9,000 N at 60% front; grip 1.1, peak slip 8°; centre of mass 0.3 m below the chassis's middle.
Surfaces multiply grip: road 1.0, dirt 0.75, grass 0.65, sand 0.6.

**Cost.** Four ray queries and a page of arithmetic per car per tick. Collision is a world boundary
for the plain, a height field for the bump patch, and one trimesh per piece.

---

## Decisions

Numbered so they can be answered by number. The recommendation is first. **1–4 answered 2026-10-31;
5 and 6 taken as recommended, his to change.**

1. **Controls: keyboard and gamepad from the first build.** Godot's input actions make the second one
   nearly free, and analogue steering and throttle are far better for judging feel. Keyboard: arrows or
   WASD, Space handbrake, R respawn, C camera. Pad: triggers for throttle and brake, left stick steers,
   A handbrake. The actions are registered in code, not hand-written into `project.godot`.
   **His answer: both, but there's no pad here.** So the pad is wired up and untested, an owed check for
   whenever one turns up, and **the keyboard is what gets tuned**. That puts weight on the steering's
   turn rate and on easing the throttle in and out, because a key is all or nothing.
2. **Where the tuning starts: grippy on the road, loose off it, and it slides when you ask** (the
   handbrake, or power out of a corner). The alternatives are a loose, rally-style car everywhere, or
   something closer to a simulation. This only picks the first numbers; the sessions move them.
   **Taken as recommended.**
3. **In a loop, the camera rolls with the car, smoothed.** A camera that keeps the horizon level spins
   wildly at the top of a loop. On ordinary ground the car's smoothed up is almost the world's up, so
   the difference only shows in stunts. The alternative is level always, with a loop shown from outside.
   **Taken as recommended.**
4. **Upside down: R respawns any time, and it happens by itself after 3 s upside down and nearly
   stopped.** The alternative is the key only. **Taken as recommended.**
5. **The car's spec is a JSON file loaded the way content is, and the base car's is
   `base:buggy`** (fact 6 and CLAUDE.md's `base:` rule). Saving from the tuning panel writes that file.
6. **Speed is shown in km/h**, with mph a toggle. *His word, if he'd rather mph.*

---

## M1a — the car on the plain

**Built 2026-10-31.** Every check met, after one change to the damping; the figures and the two
changes from this plan are in the worklog entry of that date. Also built and not planned: a
`--spec=` flag to drive a variant file, and `--screenshot=` so Claude can see a frame.

- **Core:** `CarSpec`, a record of every number above, with JSON load and save through
  `System.Text.Json`, and validation written as refusals so NaN is caught (Overburden's M3c lesson). Tests:
  a round trip; refusal of NaN, negative mass, and travel of zero; the shipped `base:buggy` file loads.
- **Game:** `Buggy`, a `RigidBody3D` with a box chassis and the model above, run from
  `_PhysicsProcess` with `ApplyForce` at each contact. The ray excludes the car's own body; the world is
  collision layer 1 and cars layer 2, and cars don't collide with each other (ghost-style, PLAN). Grey-box
  body and cylinder wheels. Input actions registered at start. Chase camera: behind and above, its
  position and up smoothed by critically damped springs, its field of view widening with speed. R
  respawns at the spawn point. Physics interpolation on (fact 2).
- **Oracles**, headless, one printed line each, so a tuning change shows up as a number:
  `--oracle=straight` (full throttle: time to 100 km/h, speed at 5, 10 and 20 s, top speed),
  `--oracle=drop` (from 1 m: deepest compression, overshoots, time to settle), `--oracle=brake` (from
  100 km/h: stopping distance), `--oracle=circle` (fixed steering and speed: steady sideways g, and whether
  it spins or rolls).
- **Checks, first numbers rather than truths:** top speed 48–52 m/s; 0–100 km/h in 5–7 s; from a 1 m drop
  it settles inside 1.2 s with at most one overshoot; 100–0 in about 40 m on road grip (35 m is the
  physics limit at 1.1); about 0.9 g steady in the circle without rolling. The velocity-at-a-point helper
  gets a test of its own: spin the body at a known rate and compare.

## M1b — telemetry, the tuning panel, recorded runs

**Built 2026-11-17**, as below. The oracles are unchanged (`f4559570`). Added beyond the plan: each
run saves its spec beside it, the panel refuses a change the spec would refuse and says why, and
`--panels=` and `--record` open the tools at start. The worklog entry has the checks.

- **F3, telemetry:** speed, engine force, and per wheel its load, compression, slip angle,
  surface and whether it's grounded, as bars and numbers.
- **F2, the tuning panel:** a slider for every number in `CarSpec`, grouped (suspension, tyres, engine,
  steering, aero, air), **built by reflection over the record** so a new field gets its slider for free.
  Changes apply live. Save writes the JSON; Reset reloads it.
- **F4, record a run:** every tick's telemetry to a CSV under `user://runs/`, named with the time and
  the spec's hash. **This is how a session works:** he drives, records the bit that felt wrong, says what
  felt wrong, and Claude reads the run. *"It floats over crests"* becomes a rebound figure.

## M1c — the sweep mesher and the test ground

**Built 2026-11-17**, with two departures the arithmetic forced (worklog): **the loop is tall rather
than round** (18 m radius at the foot, 7 m at the top), because a round loop pulls 6 g at its foot
even at the least speed that gets over the top; and **its offset grows with distance round, so it's
driven with the wheels straight**, arrived at along an 11.6° skewed run-up. Also built beyond the plan:
the layout as data in Core (`TestGroundLayout`), stations on keys 1-5, three oracles that drive the
pieces (`jump`, `loop`, `bumps`), and `--camera` and `--drive` for Claude's screenshots. The bump
patch is 128 m (a 129 × 129 height field), not 100.

- **Core:** `Sweep`. A centreline is something that gives a frame (position, forward, up) at any
  distance along it; a profile is a closed 2D cross-section. The sweep places the profile at frames along
  the centreline and joins them into triangles with normals and UVs, as plain arrays with no Godot types,
  the way Overburden's mesher hands buffers to its Game. Three centrelines: a **line** (with rise, so it's
  also a ramp), an **arc** (radius, angle, bank), and a **loop** (radius, and a sideways offset so the
  exit clears the entry). Two profiles: a road slab 10 m wide and 0.5 m thick with rounded lips (fact 4),
  and a narrower one for the loop. Tests: a closed profile gives a closed mesh; normals face outward; the
  end frame of every piece is where the next one starts to within a millimetre, which is M2's connector
  rule arriving early.
- **Game:** each swept mesh becomes an `ArrayMesh` and a `ConcavePolygonShape3D` on a static body
  tagged with its surface.
- **The test ground**, laid out on the plain in code: a straight run-up into a **kicker, a gap and a
  landing ramp**; a long **slope onto a plateau and down**; a **banked 180°**; a **loop** of 10 m
  radius; and a 100 × 100 m **bump patch**, a height field of `GradientNoise` and the first use of the
  port, standing in for M3's ground. The plain is sand, the bump patch dirt, the pieces road.

## M1d — the extras the pieces ask for

Each is a number in `CarSpec`, where 0 means off, so the panel can switch it on mid-drive:

- **Handbrake:** the rear wheels lock (forward force at the circle's edge against motion) and their
  sideways grip drops to a fraction.
- **Downforce**, proportional to speed squared, so a loop holds without a heroic entry speed.
- **Air control:** a little pitch from throttle and brake, and roll and yaw from steering, with no wheels
  down. Landing a jump nose-first should be the player's doing, not the physics'.
- **The auto-respawn** (decision 4), and continuous collision detection if fact 3 bites.

## M1e — the play

His, in as many sittings as it takes. Each verdict goes into the worklog verbatim with the spec it was
given on, and each round of numbers is committed, so any one of them can be got back. **M1 passes on his
word that it feels right**, on the plain, over the bumps, and through every piece.

**First drive, 2026-11-17**, M1a on the plain, spec `f4559570`, keyboard. "Seems ok." Power oversteer
is *"exactly what i was hoping for"*, so `forwardPriority` 0.3 stays. Steering is *"a little slow but at
the same time its difficult"*, and he said to leave it alone for now. It's a little difficult overall,
but the bumps haven't been tried. Verbatim in the worklog. **Not a pass yet**: the plain is one of the
three places the pass names.

**2026-11-22, after driving the test ground:** *"can we try shift doubles the lock to lock of the
angle?"*. Built as wide lock on Shift (`wideLockFactor` 2, with the turn rates scaled to match), and it
needed Ackermann steering (`ackermannShare` 1) to work at 60°. Worklog, that date.

---

## Order and why

The car on the flat comes first because everything else is judged through it, and the oracles come with
it so the first numbers are measured rather than guessed. Telemetry and the panel come before the pieces
because grip, springs and damping get settled on flat ground and bumps, where nothing else is going on.
The pieces come next, and the extras last, because a loop, a jump and a banked turn are what show
whether downforce, air control and the handbrake are needed and how much. Each slice ends runnable and
committed.

## Levers, named now so nobody has to rediscover them

Sphere casts in place of rays (fact 4); continuous collision detection (fact 3); grip force height and
anti-roll stiffness (fact 5); the slide's falloff after the peak slip angle; the low-speed cancelling
threshold; the camera's spring stiffness and roll smoothing; the physics rate itself (120 Hz, PLAN).

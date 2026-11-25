using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.Editor;
using Sandrail.Game.Oracles;
using Sandrail.Game.Ui;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// The entry point: the buggy on a 640 m plain (one 32 × 32 map of 20 m tiles) with M1c's test ground
/// on it, the chase camera, the HUD with its F2 tuning panel, F3 telemetry and F4 recorder, and the
/// oracles.
/// </summary>
/// <remarks>
/// <para>
/// Game flags, after the bare <c>--</c>: <c>--ground=test|showroom|bare</c> (the test ground, every M2 piece on its own, or the empty plain the
/// bare-plain oracles drive), <c>--track=base:shakedown</c> (a track by its ID, or a path ending
/// <c>.json</c>, started at its start gate), <c>--station=</c> to start at one of the stations,
/// <c>--surface=road|dirt|grass|sand</c> for the plain (sand with the test ground, the showroom or a
/// track, road on the bare plain, so the M1a oracles' targets still hold), <c>--oracle=</c> any of the thirteen, and
/// <c>--spec=&lt;path&gt;</c> to drive another spec file than the shipped buggy, so a variant can be tried
/// without touching what plays. The tuning panel saves to whichever file was loaded.
/// </para>
/// <para>
/// <c>--panels=tuning,telemetry</c> opens either at start, and <c>--record</c> starts a run at start.
/// <c>--screenshot=&lt;path.png&gt;</c> is Claude's eyes, since headless draws nothing: it saves one frame
/// at rest and one driving (<c>-rest</c>, <c>-turn</c>), then quits; <c>--drive=throttle,steer[,1]</c> (the 1 holds wide lock) sets
/// how it drives (0.7,0.6 by default), and <c>--camera=x,y,z:x,y,z</c> looks from one point at another
/// instead of following the car.
/// </para>
/// <para>
/// <c>--autodrive</c> hands the car to the autodriver at start (with <c>--station=6</c>), and
/// <c>--caution=</c> sets its aggression, the share of grip it plans for (0.5 to 1; 0.8 by default), for
/// the game and the lap oracle alike. In the game - and + change it while it drives.
/// </para>
/// <para>
/// The plain is drawn with a 10 m grid and a 100 m one, because a plain of one colour gives the eye
/// nothing to judge speed by, and speed is half of feel.
/// </para>
/// </remarks>
public partial class Main : Node3D
{
    private const string BuggyPath = Garage.StockPath;

    private const string GroundShader = """
        shader_type spatial;

        uniform vec3 base_colour : source_color = vec3(0.4, 0.4, 0.42);

        varying vec3 world_position;

        float grid_line(vec2 coordinate, float sharpness) {
            vec2 width = max(fwidth(coordinate), vec2(1e-4));
            vec2 distance = abs(fract(coordinate - 0.5) - 0.5) / width;
            return 1.0 - clamp(min(distance.x, distance.y) * sharpness, 0.0, 1.0);
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float minor = grid_line(world_position.xz / 10.0, 1.0);
            float major = grid_line(world_position.xz / 100.0, 0.5);
            ALBEDO = base_colour * (1.0 - (0.22 * minor) - (0.35 * major));
            ROUGHNESS = 0.95;
        }
        """;

    private Buggy? _car;
    private AutoRespawn? _autoRespawn;
    private ChaseCamera? _camera;
    private Hud? _hud;
    private TelemetryOverlay? _telemetry;
    private TuningPanel? _tuning;
    private RunRecorder? _recorder;
    private bool _oracle;
    private Transform3D _spawn;
    private string? _screenshot;
    private DriverInput _screenshotDrive = new() { Throttle = 0.7f, Steer = 0.6f };
    private int _ticks;
    private Shader? _groundShader;
    private TestGroundLayout? _layout;

    /// <summary>Where the number keys put the car: the test ground's stations, or the showroom's.</summary>
    private IReadOnlyList<Station>? _stations;

    private ShowroomLayout? _showroom;

    /// <summary>The track being driven, from <c>--track=</c>; null on the test ground, the showroom or the bare plain.</summary>
    private LaidTrack? _track;

    /// <summary>The generated ground, from <c>--terrain=</c>; null on the plain.</summary>
    private Terrain? _terrain;

    private RacingLine? _lapLine;
    private Autodriver? _autodriver;
    private float _caution = Autodriver.DefaultCaution;
    private MeshInstance3D? _lineView;

    // The corner lap's timer, and the laps it has timed: his and the autodriver's are kept apart.
    private LapTimer? _lapTimer;
    private int _spawnStation;
    private int _standingIn;
    private bool _autoThisLap;
    private double? _lastLap;
    private bool _lastLapAuto;
    private bool _lastLapCounted;
    private double? _bestYou;
    private double? _bestAuto;

    public override void _Ready()
    {
        string version = typeof(Main).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        GD.Print($"Sandrail {version}");

        // The published first output of splitmix64, which Hash64Tests pins. Printed from the Godot
        // side, it says this binary links the same Core the tests ran against.
        GD.Print($"Core linked: Hash64.Mix(0) = 0x{Hash64.Mix(0UL):X16} (expected 0xE220A8397B1DCDAF)");

        // The Esc menu's "generate" starts the scene again with the flags it chose (M3e), the rest kept.
        string[] args = NextArgs ?? OS.GetCmdlineUserArgs();
        _args = args;

        // Where tracks are saved and read back from (M3e, M5d): user://tracks unless told; checks point it elsewhere.
        TrackView.SavedPath = Flag(args, "--saved=") ?? "user://tracks";

        // Where his cars are kept (PROFILE-PLAN, P2): user://tunes unless told; checks point it elsewhere too.
        Garage.TunesPath = Flag(args, "--tunes=") ?? "user://tunes";

        // How fast time goes from the start (, and . change it in play): kept across the Esc menu's new tracks otherwise.
        if (FloatFlag(args, "--time=") is float time && time > 0.0f)
        {
            GameClock.SetScale(time);
        }

        // A sensor run on a gamepad instead of a drive (Diagnostics/PadCheck.cs), or a binding run that
        // saves the pad's own layout (Diagnostics/PadBinder.cs).
        if (args.Contains("--padcheck"))
        {
            Controls.Register();
            AddChild(new Diagnostics.PadCheck());
            return;
        }

        if (args.Contains("--bindpad"))
        {
            Controls.Register();
            AddChild(new Diagnostics.PadBinder());
            return;
        }

        string? oracle = Flag(args, "--oracle=");
        string specPath = Flag(args, "--spec=") ?? BuggyPath;
        string[] panels = (Flag(args, "--panels=") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        _screenshot = Flag(args, "--screenshot=");

        if (oracle is not null && !Oracle.IsKnown(oracle))
        {
            Refuse($"Unknown oracle '{oracle}'. Known: straight, drop, brake, circle, handbrake, sweeper, respawn, climb, jump, air, loop, bumps, lap, pieces, track, bank.");
            return;
        }

        // The M1a oracles, handbrake, sweeper and respawn drive the bare plain on road grip, as at M1a, so their figures
        // stay comparable; pieces drives the showroom; track drives a track, the shakedown unless told; everything
        // else gets the test ground on sand.
        bool trackOracle = oracle is not null && Oracle.NeedsTrack(oracle);
        bool showroomOracle = oracle is not null && Oracle.NeedsShowroom(oracle);
        bool bareOracle = oracle is not null && !Oracle.NeedsTestGround(oracle) && !showroomOracle && !trackOracle;
        // A generated track from a seed (M3b): its ground and its route.
        string? seedFlag = Flag(args, "--seed=") ?? (oracle is not null && Oracle.NeedsSeed(oracle) ? "1" : null);
        ulong genSeed = 0;
        string? codeFile = null;
        if (seedFlag is not null)
        {
            if (Flag(args, "--track=") is not null || Flag(args, "--terrain=") is not null || Flag(args, "--ground=") is not null)
            {
                Refuse("--seed= makes a track and its ground; it doesn't go with --track=, --terrain= or --ground=.");
                return;
            }

            // A seed or a code (M4a): 5 or 5v1. A code from another version drives its file, if the game has it.
            if (!TrackCode.TryParse(seedFlag, out TrackCode code, out string problem))
            {
                Refuse($"--seed={seedFlag}: {problem}");
                return;
            }

            genSeed = code.Seed;
            if (!code.Current)
            {
                codeFile = TrackView.FindCodeFile(code);
                if (codeFile is null)
                {
                    Refuse(TrackView.NoFileFor(code));
                    return;
                }

                seedFlag = null;
            }
        }

        string? trackName = codeFile ?? Flag(args, "--track=") ?? (trackOracle && seedFlag is null ? "base:shakedown" : null);

        // Generated ground from a seed on its own (M3a); --seed= lays a track on it.
        string? terrainFlag = Flag(args, "--terrain=");
        ulong terrainSeed = 0;
        if (terrainFlag is not null)
        {
            if (trackName is not null || Flag(args, "--ground=") is not null)
            {
                Refuse("--terrain= is ground of its own; --seed= lays a generated track on it.");
                return;
            }

            if (!ulong.TryParse(terrainFlag, out terrainSeed))
            {
                Refuse($"--terrain={terrainFlag}: a seed is a whole number, 0 or more.");
                return;
            }
        }

        string ground = seedFlag is not null ? "generated" : trackName is not null ? "track" : terrainFlag is not null ? "terrain"
            : Flag(args, "--ground=") ?? (showroomOracle ? "showroom" : bareOracle ? "bare" : "test");
        bool testGround = ground == "test";
        bool showroom = ground == "showroom";
        if (trackName is not null && Flag(args, "--ground=") is string other && other != "track")
        {
            Refuse($"--track={trackName} and --ground={other} are two places to drive; pick one.");
            return;
        }

        if (!testGround && !showroom && ground != "bare" && trackName is null && terrainFlag is null && seedFlag is null)
        {
            Refuse($"Unknown ground '{ground}'. Known: test, showroom, bare, a track with --track=, terrain with --terrain=, or a generated track with --seed=.");
            return;
        }

        string surfaceName = Flag(args, "--surface=") ?? (testGround || showroom || trackName is not null ? "sand" : "road");

        Surface? surface = Surfaces.Find(surfaceName);
        if (surface is null)
        {
            Refuse($"Unknown surface '{surfaceName}'. Known: {string.Join(", ", Surfaces.All.Select(s => s.Id))}.");
            return;
        }

        int station = oracle is not null && Oracle.NeedsTestGround(oracle)
            ? Oracle.StationFor(oracle)
            : int.TryParse(Flag(args, "--station="), out int n) ? n : 1;

        if (Flag(args, "--drive=") is string drive)
        {
            string[] parts = drive.Split(',');
            _screenshotDrive = new DriverInput
            {
                Throttle = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                Steer = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0.0f,
                WideLock = parts.Length > 2 && parts[2] == "1",
                Handbrake = parts.Length > 3 && parts[3] == "1",
                Brake = parts.Length > 4 ? float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) : 0.0f,
            };
        }

        if (float.TryParse(Flag(args, "--caution="), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float caution))
        {
            _caution = Math.Clamp(caution, Autodriver.MinCaution, Autodriver.MaxCaution);
        }

        CarSpec spec;
        try
        {
            spec = CarSpec.Parse(FileAccess.GetFileAsString(specPath));
        }
        catch (System.IO.InvalidDataException e)
        {
            Refuse($"{specPath}: {e.Message}");
            return;
        }

        Controls.Register();
        GetNode<DirectionalLight3D>("Sun").RotationDegrees = new Vector3(-50.0f, 30.0f, 0.0f);
        _groundShader = new Shader { Code = GroundShader };
        SetUpGround(surface);

        // The editor (M5): --edit=new for an empty grid, or --edit=<track id or path>. No car, no race.
        if (Flag(args, "--edit=") is string edit && oracle is null)
        {
            OpenEditor(args, edit);
            return;
        }

        if (testGround)
        {
            _layout = new TestGroundLayout();
            TestGround.Build(this, _layout, _groundShader);
            _stations = _layout.Stations;

            // The corner lap's racing line, for the autodriver (P), drawn on the road by planned speed (L).
            _lapLine = TestGround.CornerLapLine(_layout, spec);
            DrawLine(spec);
            _lapTimer = new LapTimer(new RacingLine(_layout.Runs.Single(r => r.Name == "corners").Pieces, closed: true));
        }
        else if (showroom)
        {
            // M2's pieces, each on its own with a run-up, and a station at the start of each (M2-PLAN, M2a).
            PieceCatalogue catalogue;
            try
            {
                catalogue = TrackView.LoadCatalogue();
            }
            catch (System.IO.InvalidDataException e)
            {
                Refuse(e.Message);
                return;
            }

            _showroom = Showroom.Lay(catalogue);
            TrackView.Build(this, _showroom.Pieces);
            _stations = _showroom.Stations;
        }
        else if (terrainFlag is not null)
        {
            // Generated ground in place of the plain (M3-PLAN, M3a): its mesh and height field, painted.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _terrain = new Terrain(terrainSeed, new TerrainSettings());
            double made = clock.Elapsed.TotalMilliseconds;
            var plain = GetNode<StaticBody3D>("Ground");
            plain.GetNode<MeshInstance3D>("Mesh").Visible = false;
            plain.GetNode<CollisionShape3D>("Shape").Disabled = true;
            AddChild(TerrainView.Build(_terrain, terrainSeed));
            GD.Print($"Terrain from seed {terrainSeed}: made in {made:F0} ms and built in {clock.Elapsed.TotalMilliseconds - made:F0} ms");
        }
        else if (seedFlag is not null)
        {
            // A generated track on its own ground (M3-PLAN, M3b): the route walked over the terrain, both from the seed.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            PieceCatalogue catalogue;
            Generated made;
            try
            {
                catalogue = TrackView.LoadCatalogue();

                // --stunts= and --checkpoints= turn the stunt stage's dials (M3c); --stunts=0 --checkpoints=0 is the
                // route as M3b and M3d left it, which is how seed 5 was before it had jumps.
                var stunts = new StuntSettings();
                stunts = stunts with
                {
                    Density = FloatFlag(args, "--stunts=") ?? stunts.Density,
                    CheckpointEveryM = FloatFlag(args, "--checkpoints=") ?? stunts.CheckpointEveryM,
                };
                made = TrackGenerator.Generate(genSeed, new GenSettings { Stunts = stunts }, catalogue);
                _track = made.Track.Lay(catalogue);
            }
            catch (Exception e) when (e is System.IO.InvalidDataException or InvalidOperationException or ArgumentException)
            {
                Refuse($"Seed {genSeed}: {e.Message}");
                return;
            }

            if (_track.Problems.Count > 0)
            {
                Refuse($"Seed {genSeed} made a track Lay refuses, which is a generator bug: {string.Join("; ", _track.Problems)}");
                return;
            }

            double generated = clock.Elapsed.TotalMilliseconds;
            _terrain = made.Terrain;
            _generated = made;
            BuildGround(_terrain, genSeed, made.Rocks);
            _trackPrint = TrackFingerprint.Of(made.Track, catalogue, made.Terrain, made.Rocks);
            GenStats s = made.Stats;
            StuntStats st = s.Stunts;
            GD.Print($"{TrackLabel(made.Track)}, fingerprint {_trackPrint}: {(s.Circuit ? $"a circuit, {made.Track.Laps} laps of " : "a sprint, ")}{s.LengthM / 1000.0f:F2} km, {_track.Placed.Count} pieces, {s.Slopes} slopes, {s.Banked} banked corners, "
                + $"worst fill {s.WorstFillM:F1} m and cut {s.WorstCutM:F1} m; {st.ShortJumps} short jumps, {st.LongJumps} long, {st.Loops} loops "
                + $"({st.TooSlow + st.MustBrake} turned down), {st.Checkpoints} checkpoints; generated in {generated:F0} ms, built in {clock.Elapsed.TotalMilliseconds - generated:F0} ms");
        }
        else if (trackName is not null)
        {
            // A track from its file (M2-PLAN, M2c): its pieces on the plain, the car at the start gate.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            PieceCatalogue catalogue;
            try
            {
                catalogue = TrackView.LoadCatalogue();

                // A test drive (M5c): the editor's own track (--track=@editor), or a file read without refusing
                // it, since a track being built can be driven before it can be raced.
                _testDrive = trackName == EditorSession.Id || Flag(args, "--drive-from=") is not null;
                _track = trackName == EditorSession.Id
                    ? EditorSession.Editor?.Laid ?? throw new System.IO.InvalidDataException("There's no track in the editor to drive.")
                    : _testDrive ? TrackView.ReadTrack(trackName).Lay(catalogue)
                    : TrackView.LoadTrack(trackName, catalogue);
                _editBack = trackName;
            }
            catch (System.IO.InvalidDataException e)
            {
                Refuse(e.Message);
                return;
            }

            double loaded = clock.Elapsed.TotalMilliseconds;
            if (_track.Track.Ground is TrackGround saved)
            {
                // A generated track saved with its ground (M3e): the terrain made again from its seed and dials,
                // and the bed and the rocks laid again from the track, as the generator laid them.
                // A test drive of an unfinished track (M5c, M5d) has no route yet: its bed is laid under every piece,
                // and it has no rocks, which keep clear of a route.
                bool whole = _track.Problems.Count == 0;
                _terrain = Bed.Lay(new Terrain(saved.Seed, saved.Terrain), whole ? _track.Route : _track.Placed);
                IReadOnlyList<Rock> rocks = whole ? Rocks.Scatter(saved.Seed, new RockSettings(), _track, _terrain) : [];
                BuildGround(_terrain, saved.Seed, rocks);
                _trackPrint = TrackFingerprint.Of(_track.Track, catalogue, _terrain, rocks);
            }
            else
            {
                TrackView.Build(this, _track.Placed);
                _trackPrint = TrackFingerprint.Of(_track.Track, catalogue);
            }

            float length = _track.LengthM;
            GD.Print($"Built {_track.Track.Id} ({TrackLabel(_track.Track)}), fingerprint {_trackPrint}: {_track.Placed.Count} pieces, {length / 1000.0f:F2} km of road, "
                + $"loaded in {loaded:F0} ms and built in {clock.Elapsed.TotalMilliseconds - loaded:F0} ms");
        }

        // A test drive from a piece that isn't a sound track's start gate is untimed (M5c, decision 4): the car on
        // that piece, a few metres in, facing along it. From the start gate of a sound track it's the race.
        PlacedPiece? driveFrom = _testDrive && _track is not null ? DriveFromPiece(_track, Flag(args, "--drive-from=")) : null;
        if (_testDrive && _track is not null && driveFrom is null)
        {
            Refuse("There are no pieces on the track to drive.");
            return;
        }

        if (_track is not null && driveFrom is not null && (_track.Problems.Count > 0 || driveFrom != _track.Route[0]))
        {
            if (_track.Problems.Count == 0)
            {
                // Sound but not from the start: the autodriver (P) can still drive its route.
                _routeLine = _track.RouteLine(spec);
                DrawLine(spec);
            }

            ICentreline first = driveFrom.Road().Pieces[0];
            _spawn = SpawnAt(first.FrameAt(MathF.Min(4.0f, first.Length / 2.0f)), spec);
            _testDriveFrom = driveFrom;
            Vector3 heading = -_spawn.Basis.Z;
            GD.Print($"Test drive from the {Short(driveFrom.Def)} at {driveFrom.Placement.At}, which goes {driveFrom.Forward.ToString().ToLowerInvariant()}, untimed: "
                + $"the car at ({_spawn.Origin.X:F1}, {_spawn.Origin.Y:F1}, {_spawn.Origin.Z:F1}), heading {Mathf.RadToDeg(MathF.Atan2(heading.X, -heading.Z)):F0}°, "
                + $"{(driveFrom.Tiles.Contains(TileUnder(_spawn.Origin)) ? "on the piece" : "OFF THE PIECE")}");
        }
        else if (_track is not null)
        {
            SetUpRace(_track, spec);
            _spawn = SpawnAt(TrackStart(_track), spec);

            // Where R puts the car after a checkpoint, for a screenshot of it: the run isn't timed from there.
            if (int.TryParse(Flag(args, "--checkpoint="), out int gate))
            {
                IReadOnlyList<RaceGate> gates = _race!.Gates;
                if (gate < 1 || gate > _race.Checkpoints)
                {
                    Refuse($"Unknown checkpoint {gate}. The track has 1 to {_race.Checkpoints}.");
                    return;
                }

                Frame at = gates[gate].Frame;
                _spawn = SpawnAt(at with { Position = at.Position + (at.Forward * PastTheArchM) }, spec);
                _raceArmIn = 0;
            }
        }
        else if (_stations is not null)
        {
            if (station < 1 || station > _stations.Count)
            {
                Refuse($"Unknown station {station}. They're 1 to {_stations.Count}.");
                return;
            }

            _spawn = SpawnAt(_stations[station - 1].Start, spec);
            PutDown(station);
        }
        else
        {
            float lift = oracle == Oracle.Drop ? Oracle.DropHeight : 0.02f;
            float under = _terrain?.HeightAt(0.0f, 0.0f) ?? 0.0f;
            _spawn = new Transform3D(Basis.Identity, new Vector3(0.0f, under + Buggy.RestHeight(spec) + lift, 0.0f));
        }

        _car = new Buggy { Transform = _spawn };
        _car.Configure(spec);
        if (_terrain is Terrain land)
        {
            _car.PaintedSurface = p => land.SurfaceAt(p.X, p.Z);
        }
        AddChild(_car);

        _camera = new ChaseCamera { Target = _car };
        AddChild(_camera);

        _recorder = new RunRecorder { Car = _car };
        AddChild(_recorder);

        _hud = new Hud { Car = _car, Recorder = _recorder, OnATrack = _track is not null };
        AddChild(_hud);

        // Ghosts and bests from their files (M4c), for play and screenshots: an oracle reads no one's ghosts, and
        // an untimed test drive has no race to ghost.
        if (_track is not null && _race is not null && oracle is null && !SetUpGhosts(args))
        {
            return;
        }

        // The Esc menu (M3e), for play: not for an oracle or a screenshot, which quit on their own.
        if (oracle is null && _screenshot is null)
        {
            TrackCode? code = _track is not null ? TrackCode.FromId(_track.Track.Id) : null;
            string driving = _generated is Generated g
                ? $"{TrackLabel(g.Track)}: {(g.Stats.Circuit ? $"a circuit, {g.Track.Laps} laps of {g.Stats.LengthM / 1000.0f:F1} km" : $"a sprint of {g.Stats.LengthM / 1000.0f:F1} km")}"
                : _track is not null ? (code is not null ? $"{TrackLabel(_track.Track)}, from its file" : $"{_track.Track.Name} ({_track.Track.Id})")
                : "The test ground";
            AddChild(new SeedMenu
            {
                Where = driving,
                Detail = string.Join("\n", new[] { _trackPrint is null ? string.Empty : $"Track fingerprint {_trackPrint}", CarWords() }.Where(s => s.Length > 0)),
                Seed = code?.ToString() ?? (_generated is not null ? genSeed.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty),
                Generate = c => GoToCode(args, c),
                Save = _generated is not null ? SaveGenerated : null,
                Replays = ReplaysOnOffer,
                ProfileReplays = ProfileReplaysOnOffer,
                EscTaken = () => EscTakenByReplay() || _tuning?.Typing == true || ProfileTakesEsc,
                Tracks = () => TracksToLoad(args, _menuCatalogue ??= TrackView.LoadCatalogue()),
                Cars = () => CarsToDrive(args),
                Profile = _race is not null && _testDriveFrom is null ? () => StartProfile(false) : null,
                ProfileAll = _race is not null && _testDriveFrom is null ? () => StartProfile(true) : null,
                ProfileEverywhere = _testDriveFrom is null ? StartProfileEverywhere : null,
                OpenProfiles = OpenProfilesFolder,
                Quitting = HoldPendingRows,
                NewTrack = () => StartAgainOn(args, "--edit=new"),
                EditThis = EditThisTrack(args, trackName),
                NewOnGround = _track?.Track.Ground is TrackGround here ? () => StartAgainOn(args, $"--edit=new:{here.Seed}") : null,
            });
        }
        _telemetry = new TelemetryOverlay { Car = _car, Visible = panels.Contains("telemetry") };
        _hud.AddChild(_telemetry);
        _tuning = new TuningPanel { Car = _car, SpecPath = specPath, Hud = _hud, Visible = panels.Contains("tuning") };
        _hud.AddChild(_tuning);

        // The auto-respawn (M1d, decision 4), for the game and the oracle that tests it; the other oracles
        // report a crash as a crash.
        if (oracle is null || oracle == Oracle.Respawn)
        {
            _autoRespawn = new AutoRespawn { Car = _car, Home = () => _track is not null ? TrackHome() : _spawn, LowestGroundM = MathF.Min(0.0f, _terrain?.Heights.Min() ?? 0.0f) };
            _autoRespawn.Respawned += AutoRespawned;
            AddChild(_autoRespawn);
        }

        if (oracle is not null)
        {
            _oracle = true;
            AddChild(new Oracle { Kind = oracle, Car = _car, Layout = _layout, Showroom = _showroom, Course = _track, Ground = _terrain, Caution = _caution, TrackPrint = _trackPrint });
        }

        if (args.Contains("--record"))
        {
            Recording(_recorder.Start());
        }

        // The autodriver from the start, for watching it or for a screenshot of it: station 6 makes sense.
        if (args.Contains("--autodrive"))
        {
            ToggleAutodriver();
        }

        // The profile sheets (P3): where they go, and a profile carried on from the scene before or asked for.
        if (oracle is null && _screenshot is null)
        {
            SetUpProfiles(args);
        }

        // A fixed camera for looking at the ground rather than the car: x,y,z:x,y,z, from and at.
        if (Flag(args, "--camera=") is string view)
        {
            float[] v = view.Replace(':', ',').Split(',')
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var overview = new Camera3D { Far = 2000.0f };
            AddChild(overview);
            overview.LookAtFromPosition(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]), Vector3.Up);
            overview.Current = true;
        }

        string where = _track is not null ? $", {_track.Track.Id}, from its start" : _stations is not null ? $", {ground}, from station {station}" : string.Empty;
        GD.Print($"Driving {spec.Id} {spec.Fingerprint()} on {surface.Id}{where}");
        if (_testDriveFrom is PlacedPiece from && oracle is null && _screenshot is null)
        {
            _hud.Flash($"Test drive from the {from.Def.Name.ToLowerInvariant()} at {from.Placement.At}, untimed"
                + (_track!.Problems.Count > 0 ? " (the track isn't finished)" : ": the race is from the start gate") + ". R puts you back here; Tab goes back to the editor.", 8.0);
        }
        else if (_track is not null && oracle is null && _screenshot is null)
        {
            float km = _track.LengthM / 1000.0f;
            string what = _track.Track.Circuit
                ? $"{_track.Track.Laps} laps of {km:F1} km, {_track.GatesPerLap() - 1} checkpoints a lap"
                : $"{km:F1} km and {_race!.Checkpoints} checkpoints";
            _hud.Flash($"{TrackLabel(_track.Track)}: {what}. The clock starts on the throttle; "
                + "R puts you back at the last checkpoint; hold R, or press Backspace, to start again.", 10.0);
        }
        else if (_stations is not null && oracle is null && _screenshot is null)
        {
            Station first = _stations[station - 1];
            _hud.Flash($"{first.Number}. {first.Name}: {first.Hint}", 8.0);
        }

        // A replay from the start (M4d), for a look at it or a screenshot of it: --replay=you|auto, or a car profiled on
        // the track by its name or fingerprint (P4), from --replay-at= seconds, on --replay-camera=trackside|chase|free.
        if (Flag(args, "--replay=") is string which && oracle is null)
        {
            (Sandrail.Core.Ghosts.Ghost? run, string title) = which switch
            {
                "you" => (_ghostYou, "your best"),
                "auto" => (_ghostAuto, "the autodriver's best"),
                _ => ProfileGhostNamed(which) is Sandrail.Core.Ghosts.Ghost car ? (car, $"{ProfileName(car)}'s profile best") : (null, string.Empty),
            };
            if (run is null)
            {
                Refuse($"--replay={which}: it's you, auto, or a car profiled on this track, with that ghost saved.");
                return;
            }

            StartReplay(run, title, FloatFlag(args, "--replay-at=") ?? 0.0f);
            if (Flag(args, "--replay-camera=") is string replayView && _replay is not null)
            {
                _replay.View = replayView switch
                {
                    "chase" => ReplayView.Chase,
                    "free" => ReplayView.Free,
                    _ => ReplayView.Trackside,
                };
                ShowReplayView();
            }
        }
    }

    /// <summary>
    /// The flags the scene starts with when the Esc menu starts it again (M3e): a seed to generate, the
    /// player's own flags kept. Static, so it outlives the scene it's set in.
    /// </summary>
    public static string[]? NextArgs { get; set; }

    /// <summary>The generated track being driven, with its stats, for the Esc menu to save; null on any other ground.</summary>
    private Generated? _generated;

    /// <summary>The flags that choose where to drive, which "generate" replaces with a seed; the rest (the car, the caution, the panels) stay.</summary>
    private static readonly string[] PlaceFlags =
        ["--seed=", "--track=", "--terrain=", "--ground=", "--station=", "--checkpoint=", "--screenshot=", "--oracle=", "--camera=", "--drive=", "--edit=", "--drive-from=", "--replay=", "--replay-at=", "--replay-camera="];

    /// <summary>
    /// The Esc menu's "generate" (M3e), with codes (M4a): this version's code builds its seed's track; another
    /// version's drives its file if the game has it. Returns why it can't, or null as the scene starts again.
    /// </summary>
    private string? GoToCode(string[] args, TrackCode code)
    {
        if (code.Current)
        {
            StartAgainOn(args, $"--seed={code.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            return null;
        }

        string? file = TrackView.FindCodeFile(code);
        if (file is null)
        {
            return TrackView.NoFileFor(code);
        }

        StartAgainOn(args, $"--track={file}");
        return null;
    }

    /// <summary>The scene started again somewhere else, the player's other flags kept.</summary>
    private void StartAgainOn(string[] args, params string[] places)
    {
        // The car goes along (P2): the one being driven, which F2's Save as a new car may have made since the scene
        // began. In the editor there's no car, and whatever --spec= the scene had is kept for the next drive.
        string? driving = _tuning?.SpecPath;
        NextArgs = args.Where(a => !PlaceFlags.Any(p => a.StartsWith(p, StringComparison.Ordinal)) && a != "--autodrive"
                && !(driving is not null && a.StartsWith("--spec=", StringComparison.Ordinal)))
            .Concat(places)
            .Concat(driving is not null && !Garage.IsStock(driving) ? [$"--spec={driving}"] : [])
            .ToArray();
        GD.Print($"Starting again: {string.Join(' ', NextArgs)}");
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// A track in words, for the menu, the opening flash and the log: "Seed 5, code 5v1" for a generated track
    /// with a code (M4a), its name otherwise.
    /// </summary>
    private static string TrackLabel(Track track) =>
        TrackCode.FromId(track.Id) is TrackCode code ? $"{track.Name}, code {code}" : track.Name;

    /// <summary>The track's fingerprint (M4a): what its ghosts are filed under. Null off a track.</summary>
    private string? _trackPrint;

    /// <summary>
    /// The Esc menu's "save": the generated track being driven written as its file (format 2, or 3 for a
    /// circuit), its ground in it, to <c>user://tracks/</c>, named for its ID (<c>gen-5v1.json</c>);
    /// <c>--track=</c> drives it again, and so does its code once the generator has moved on.
    /// </summary>
    private string SaveGenerated()
    {
        string Folder = TrackView.SavedPath;
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        string path = $"{Folder}/{_generated!.Track.Id.Replace(':', '-')}.json";
        using (FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                throw new System.IO.IOException($"{path}: {FileAccess.GetOpenError()}");
            }

            file.StoreString(_generated!.Track.Save());
        }

        GD.Print($"Saved {_generated.Track.Id} to {ProjectSettings.GlobalizePath(path)}");
        return $"{ProjectSettings.GlobalizePath(path)}. Drive it again with start.cmd --track={path}";
    }

    /// <summary>
    /// A generated track's world (M3b on, and a saved one loaded, M3e): the plain put away, the bedded terrain,
    /// the track's pieces on it, and its rocks.
    /// </summary>
    private void BuildGround(Terrain bedded, ulong seed, IReadOnlyList<Rock> rocks)
    {
        var plain = GetNode<StaticBody3D>("Ground");
        plain.GetNode<MeshInstance3D>("Mesh").Visible = false;
        plain.GetNode<CollisionShape3D>("Shape").Disabled = true;
        AddChild(TerrainView.Build(bedded, seed));
        TrackView.Build(this, _track!.Placed, bedded);
        AddChild(RockView.Build(rocks));
    }

    /// <summary>
    /// Where the car starts on a track: just inside its start gate, behind the arch; on a circuit just past
    /// its line (M3g), since crossing it is the end of a lap, and doing so at the start would read as a lap
    /// line passed out of order.
    /// </summary>
    private static Frame TrackStart(LaidTrack track) => track.Route[0].Road().Pieces[0].FrameAt(track.Track.Circuit ? 14.0f : 4.0f);

    /// <summary>Where the car goes to start from a frame on the road: resting on its wheels, facing along it.</summary>
    private static Transform3D SpawnAt(Frame start, CarSpec spec)
    {
        Vector3 up = start.Up.ToGodot().Normalized();
        Vector3 right = start.Forward.ToGodot().Cross(up).Normalized();
        Vector3 forward = up.Cross(right).Normalized();
        return new Transform3D(new Basis(right, up, -forward), start.Position.ToGodot() + (up * (Buggy.RestHeight(spec) + 0.02f)));
    }

    private void GoToStation(int number)
    {
        if (_stations is null || _car is null || number > _stations.Count)
        {
            return;
        }

        Station station = _stations[number - 1];
        _spawn = SpawnAt(station.Start, _car.Spec);
        _car.Respawn(_spawn);
        PutDown(number);
        _hud?.Flash($"{station.Number}. {station.Name}: {station.Hint}", 8.0);
    }

    /// <summary>The auto-respawn has put the car back where R would; this is what R does after.</summary>
    private void AutoRespawned(StuckReason reason)
    {
        // In a profile (P3) a respawn ends the run, so its row says where it went wrong. Over the line already, its ghost
        // waiting for its last snapshot (P4), the run is finished, and its row is as it was at the line.
        if (_sweeping)
        {
            if (_race is { Finished: true })
            {
                EndProfileRun(null);
                return;
            }

            _runMeter?.Respawned();
            EndProfileRun(reason == StuckReason.Lost ? $"off the world, {_meterAlong:F0} m along" : $"tipped over, {_meterAlong:F0} m along");
            return;
        }

        PutDown(_spawnStation);
        string where = _track is not null ? AfterTrackRespawn() : _stations is not null ? $"station {_spawnStation}" : "the start";
        _hud?.Flash(reason == StuckReason.Lost
            ? $"Off the edge of the world: back to {where}."
            : $"Tipped over for {StuckWatch.Seconds:F0} s: back to {where}.", 4.0);
    }

    /// <summary>
    /// The car has been put at a station. At station 6 it sits on the corner lap's line, so the timer arms
    /// a standing start; anywhere else no lap is running until the car next crosses the line.
    /// </summary>
    /// <remarks>
    /// The arming waits two ticks, until the respawn has really moved the car: armed at once, a driver
    /// already holding the throttle would start the clock with the car still where it was.
    /// </remarks>
    private void PutDown(int station)
    {
        _spawnStation = station;
        _lapTimer?.Stop();
        _standingIn = station == 6 ? 2 : 0;
    }

    /// <summary>
    /// Once a tick: the lap timer sees where the car is, starts a standing lap when the driver (or the
    /// autodriver) first asks for throttle or reverse, and files any lap finished.
    /// </summary>
    private void TimeLap()
    {
        if (_lapTimer is null || _car is null || _hud is null)
        {
            return;
        }

        double now = _ticks / (double)GameClock.TicksPerSecond;
        if (_standingIn > 0 && --_standingIn == 0)
        {
            _lapTimer.StandingStart();
        }

        LapEvent lap = _lapTimer.Update(_car.GlobalPosition.ToCore(), now);
        if (_lapTimer.Armed && (_car.Driver.Throttle > 0.0f || _car.Driver.Brake > 0.0f))
        {
            _lapTimer.Go(now);
            lap = new LapEvent(LapEventKind.Started);
        }

        if (lap.Kind == LapEventKind.Finished)
        {
            FileLap(lap);
        }

        if (lap.Kind != LapEventKind.None)
        {
            // The next lap begins now: it's the autodriver's if it has the car at any point of it.
            _autoThisLap = _autodriver is not null;
        }

        _autoThisLap |= _autodriver is not null;
    }

    private void FileLap(LapEvent lap)
    {
        _lastLap = lap.Seconds;
        _lastLapAuto = _autoThisLap;
        _lastLapCounted = lap.Counted;
        string who = _autoThisLap ? "Autodriver's lap" : "Lap";
        if (!lap.Counted)
        {
            _hud!.Flash($"{who} {LapTime(lap.Seconds)}, not counted: it cut across or left the lap.", 5.0);
            return;
        }

        double? best = _autoThisLap ? _bestAuto : _bestYou;
        string verdict = best is null ? string.Empty
            : lap.Seconds < best ? $", a new best by {best.Value - lap.Seconds:F2} s"
            : $", +{lap.Seconds - best.Value:F2} s on the best";
        if (best is null || lap.Seconds < best)
        {
            if (_autoThisLap)
            {
                _bestAuto = lap.Seconds;
            }
            else
            {
                _bestYou = lap.Seconds;
            }
        }

        _hud!.Flash($"{who} {LapTime(lap.Seconds)}{verdict}.", 5.0);
    }

    /// <summary>The lap board for the HUD, or null while there's nothing to show.</summary>
    private string? LapBoard()
    {
        if (_lapTimer is null || (!_lapTimer.Running && !_lapTimer.Armed && _standingIn == 0 && _lastLap is null))
        {
            return null;
        }

        double now = _ticks / (double)GameClock.TicksPerSecond;
        string current = _lapTimer.Running
            ? $"{LapTime(_lapTimer.Elapsed(now))}{(_lapTimer.Clean ? string.Empty : "  not counting")}"
            : _lapTimer.Armed || _standingIn > 0 ? $"{LapTime(0.0)}  ready" : "cross the line";
        string last = _lastLap is double l
            ? $"{LapTime(l)}{(_lastLapAuto ? "  auto" : string.Empty)}{(_lastLapCounted ? string.Empty : "  not counted")}"
            : "-";
        string best = $"you {(_bestYou is double y ? LapTime(y) : "-")}   auto {(_bestAuto is double a ? LapTime(a) : "-")}";
        return $"LAP   {current}\nLAST  {last}\nBEST  {best}";
    }

    /// <summary>Seconds to hundredths, with minutes once there are any: 24.52, 1:03.40.</summary>
    private static string LapTime(double seconds) =>
        seconds < 60.0
            ? seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : $"{(int)(seconds / 60.0)}:{(seconds % 60.0).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture)}";

    public override void _PhysicsProcess(double delta)
    {
        // The editor has no race: its ticks are counted apart, as a replay's are, for screenshots.
        if (_editorView is not null)
        {
            _replayTicks++;
            return;
        }

        if (_car is null || _oracle)
        {
            return;
        }

        // A replay (M4d) stops the race: no tick of it runs, so its clock waits. Counted apart, for screenshots.
        if (_replay is not null)
        {
            _replayTicks++;
            return;
        }

        _ticks++;
        if (_screenshot is not null)
        {
            bool driving = _ticks > 2 * GameClock.TicksPerSecond;
            _car.Driver = !driving ? default : _autodriver is not null ? AutodriverInput(_autodriver) : _screenshotDrive;
            TimeLap();
            TimeRace();
            return;
        }

        // A profile (P3) drives the race itself: none of his keys, no R, no Backspace; the car still until the
        // autodriver has it, then the clock and the run's end.
        if (_sweeping)
        {
            _car.Driver = _autodriver is not null ? AutodriverInput(_autodriver) : default;
            TimeRace();
            ProfileTick();
            return;
        }

        for (int number = 1; number <= Controls.NumberedStations; number++)
        {
            if (Input.IsActionJustPressed(Controls.Station(number)))
            {
                GoToStation(number);
            }
        }

        if (_stations is not null && Input.IsActionJustPressed(Controls.NextStation))
        {
            GoToStation((_spawnStation % _stations.Count) + 1);
        }

        if (_stations is not null && Input.IsActionJustPressed(Controls.PreviousStation))
        {
            GoToStation(((_spawnStation + _stations.Count - 2) % _stations.Count) + 1);
        }

        _car.Driver = new DriverInput
        {
            Throttle = Input.GetActionStrength(Controls.Throttle),
            Brake = Input.GetActionStrength(Controls.Brake),
            Steer = Input.GetActionStrength(Controls.SteerRight) - Input.GetActionStrength(Controls.SteerLeft),
            Handbrake = Input.IsActionPressed(Controls.Handbrake),
            WideLock = Input.IsActionPressed(Controls.WideLock),
        };

        if (_autodriver is not null)
        {
            _car.Driver = AutodriverInput(_autodriver);
        }

        if (Input.IsActionJustPressed(Controls.Respawn))
        {
            if (_track is not null)
            {
                RespawnOnTrack();
            }
            else
            {
                _car.Respawn(_spawn);
                _autoRespawn?.Reset();
                PutDown(_spawnStation);
            }
        }

        if (_track is not null)
        {
            WatchRespawnHeld();
        }

        if (Input.IsActionJustPressed(Controls.Restart))
        {
            RestartRun();
        }

        TimeLap();
        TimeRace();
    }

    public override void _Process(double delta)
    {
        if (_car is null)
        {
            // The editor (M5) has no car; only a screenshot of it to take.
            if (_editorView is not null && _screenshot is not null)
            {
                TakeScreenshots(_screenshot);
            }

            return;
        }

        if (_replay is not null)
        {
            UpdateReplay(delta);
            if (_screenshot is not null)
            {
                TakeScreenshots(_screenshot);
            }

            return;
        }

        // Tab: back to the editor from a test drive (M5c).
        if (WatchTabBack())
        {
            return;
        }

        RetryPendingRows();

        // While a profile drives (P3) the keys are its own: Esc stops it.
        if (_sweeping)
        {
            // Esc may have ended it and started the scene again with the car it began with: this one has left the tree.
            WatchProfileKeys();
            if (!IsInsideTree())
            {
                return;
            }

            PoseGhost(delta);
            if (_hud is not null && _race is not null)
            {
                _hud.Laps = RaceBoard();
            }

            return;
        }

        // Enter: watch the last run (M4d).
        if (Input.IsActionJustPressed(Controls.Watch) && _lastGhost is Sandrail.Core.Ghosts.Ghost last)
        {
            StartReplay(last, "the last run");
            return;
        }

        if (Input.IsActionJustPressed(Controls.Camera))
        {
            _camera?.ToggleView();
        }

        if (Input.IsActionJustPressed(Controls.Tuning))
        {
            _tuning?.Toggle();
        }

        if (Input.IsActionJustPressed(Controls.Telemetry) && _telemetry is not null)
        {
            _telemetry.Visible = !_telemetry.Visible;
        }

        if (Input.IsActionJustPressed(Controls.Record))
        {
            ToggleRecording();
        }

        if (Input.IsActionJustPressed(Controls.Autodrive))
        {
            ToggleAutodriver();
        }

        if (Input.IsActionJustPressed(Controls.Slower))
        {
            ChangeTime(-1);
        }

        if (Input.IsActionJustPressed(Controls.Faster))
        {
            ChangeTime(1);
        }

        if (Input.IsActionJustPressed(Controls.Bolder))
        {
            ChangeAggression(1);
        }

        if (Input.IsActionJustPressed(Controls.Calmer))
        {
            ChangeAggression(-1);
        }

        if (Input.IsActionJustPressed(Controls.ShowLine) && _lineView is not null)
        {
            _lineView.Visible = !_lineView.Visible;
        }

        if (Input.IsActionJustPressed(Controls.ChooseGhost))
        {
            ChooseGhost();
        }

        PoseGhost(delta);

        if (_hud is not null)
        {
            _hud.Laps = _race is not null ? RaceBoard() : _testDriveFrom is not null ? TestDriveBoard() : LapBoard();

            // The countdown once the car has been stuck for half a second, so a roll that rights itself
            // doesn't flash it.
            _hud.Alert = _autoRespawn?.SecondsLeft is float left && left < StuckWatch.Seconds - 0.5f
                ? $"Tipped over: back on your wheels in {MathF.Ceiling(left):F0}   (R now)"
                : null;
        }

        if (_screenshot is not null)
        {
            TakeScreenshots(_screenshot);
        }
    }

    /// <summary>
    /// P: hands the car to the autodriver on the corner lap, or takes it back. It drives the line built for
    /// the car's numbers as they are now, so a tune made on the panel is a tune it drives.
    /// </summary>
    private void ToggleAutodriver()
    {
        if (_car is null || _hud is null)
        {
            return;
        }

        if (_autodriver is not null)
        {
            _autodriver = null;
            _hud.Banner = null;
            _hud.Flash("You're driving.");
            return;
        }

        // On a track it drives the route, start to finish, from wherever on it the car is.
        if (_track is not null)
        {
            int near = _routeLine!.Nearest(_car.GlobalPosition.ToCore());
            if (_routeLine.PositionAt(near).ToGodot().DistanceTo(_car.GlobalPosition) > 15.0f)
            {
                _hud.Flash("The autodriver drives the track's road: get back on it first, or press R.");
                return;
            }

            _routeLine = _track.RouteLine(_car.Spec);
            _autodriver = new Autodriver(_routeLine, _car.Spec, _caution);
            DrawLine(_car.Spec);
            _hud.Banner = AutodriverBanner();
            _hud.Flash("The autodriver has the car, to the finish. - and + change its aggression.");
            return;
        }

        if (_layout is null || _lapLine is null)
        {
            _hud.Flash("The autodriver needs the test ground's corner lap or a track.");
            return;
        }

        int nearest = _lapLine.Nearest(_car.GlobalPosition.ToCore());
        if (_lapLine.PositionAt(nearest).ToGodot().DistanceTo(_car.GlobalPosition) > 15.0f)
        {
            _hud.Flash("The autodriver drives the corner lap: press 6 to go there first.");
            return;
        }

        _lapLine = TestGround.CornerLapLine(_layout, _car.Spec);
        _autodriver = new Autodriver(_lapLine, _car.Spec, _caution);
        DrawLine(_car.Spec);
        _hud.Banner = AutodriverBanner();
        _hud.Flash("The autodriver has the car, on the corner lap's racing line. - and + change its aggression.");
    }

    private string AutodriverBanner() => $"AUTODRIVER   aggression {_caution * 100.0f:F0}%   (-/+ to change, P to take over)";

    /// <summary>
    /// - and +: the autodriver's aggression, the share of the tyres' grip it plans corners and braking for,
    /// a step at a time. It holds between handovers, and the drawn line recolours to the new plan, so it can
    /// be set before pressing P as well as while it drives.
    /// </summary>
    private void ChangeAggression(int steps)
    {
        if (_car is null || _hud is null || DrivenLine is null)
        {
            return;
        }

        // Rounded to the step, so a dozen presses each way land back on 80% exactly.
        float wanted = MathF.Round((_caution + (steps * Autodriver.CautionStep)) / Autodriver.CautionStep) * Autodriver.CautionStep;
        _caution = Math.Clamp(wanted, Autodriver.MinCaution, Autodriver.MaxCaution);
        if (_autodriver is not null)
        {
            _autodriver.Caution = _caution;
            _hud.Banner = AutodriverBanner();
        }

        DrawLine(_car.Spec);
        string edge = _caution >= Autodriver.MaxCaution ? " (the most)" : _caution <= Autodriver.MinCaution ? " (the least)" : string.Empty;
        _hud.Flash($"Autodriver aggression {_caution * 100.0f:F0}%{edge}: it plans corners for that share of the tyres' grip.", 3.0);
    }

    /// <summary>
    /// Draws the lap's racing line with the plan at the current aggression, coloured on the scale of the
    /// default plan, replacing what was drawn and keeping whether it was shown.
    /// </summary>
    private void DrawLine(CarSpec spec)
    {
        if (DrivenLine is not RacingLine line)
        {
            return;
        }

        bool visible = _lineView?.Visible ?? true;
        _lineView?.QueueFree();
        _lineView = RacingLineView.Build(line, line.SpeedProfile(spec, _caution), line.SpeedProfile(spec, Autodriver.DefaultCaution));
        _lineView.Visible = visible;
        AddChild(_lineView);
    }

    /// <summary>What the autodriver asks for, from where the car is and how it's moving.</summary>
    private DriverInput AutodriverInput(Autodriver driver)
    {
        Basis basis = _car!.GlobalBasis;
        Vector3 up = basis.Y.Normalized();
        DriveCommand cmd = driver.Drive(
            _car.GlobalPosition.ToCore(),
            (-basis.Z).Normalized().ToCore(),
            up.ToCore(),
            _car.LinearVelocity.ToCore(),
            -_car.AngularVelocity.Dot(up),
            airborne: _car.GroundedCount == 0);
        return new DriverInput { Throttle = cmd.Throttle, Brake = cmd.Brake, Steer = cmd.Steer };
    }

    private void ToggleRecording()
    {
        if (_recorder is null || _hud is null)
        {
            return;
        }

        if (_recorder.Recording)
        {
            float seconds = _recorder.Seconds;
            string path = _recorder.Stop()!;
            GD.Print($"Run saved: {path}");
            _hud.Flash($"Saved a {seconds:F1} s run: runs/{System.IO.Path.GetFileName(path)}", 10.0);
        }
        else
        {
            Recording(_recorder.Start());
        }
    }

    /// <summary>The whole path goes to the log, where it can be copied; the HUD gets just the file's name.</summary>
    private void Recording(string path)
    {
        GD.Print($"Recording to {path}");
        _hud?.Flash($"Recording to runs/{System.IO.Path.GetFileName(path)}");
    }

    private void TakeScreenshots(string path)
    {
        int tps = GameClock.TicksPerSecond;
        string stem = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

        // A replay's ticks count too: the race's stop while one plays (M4d).
        int ticks = _ticks + _replayTicks;
        if (ticks >= (tps * 3) / 2 && !FileAccess.FileExists(stem + "-rest.png"))
        {
            GetViewport().GetTexture().GetImage().SavePng(stem + "-rest.png");
        }

        if (ticks >= tps * 5)
        {
            GetViewport().GetTexture().GetImage().SavePng(stem + "-turn.png");
            GD.Print($"Screenshots saved: {stem}-rest.png, {stem}-turn.png");
            string? run = _recorder?.Stop();
            if (run is not null)
            {
                GD.Print($"Run saved: {run}");
            }

            GetTree().Quit();
        }
    }

    private void SetUpGround(Surface surface)
    {
        var ground = GetNode<StaticBody3D>("Ground");
        ground.SetMeta("surface", surface.Id);
        ground.CollisionLayer = Buggy.WorldLayer;

        Color colour = surface.Id switch
        {
            "base:sand" => new Color(0.76f, 0.64f, 0.45f),
            "base:dirt" => new Color(0.5f, 0.38f, 0.26f),
            "base:grass" => new Color(0.33f, 0.48f, 0.24f),
            _ => new Color(0.36f, 0.36f, 0.38f),
        };

        var material = new ShaderMaterial { Shader = _groundShader };
        material.SetShaderParameter("base_colour", colour);
        ground.GetNode<MeshInstance3D>("Mesh").MaterialOverride = material;
    }

    private void Refuse(string message)
    {
        GD.PrintErr(message);
        GetTree().Quit(1);
    }

    private static string? Flag(string[] args, string prefix) =>
        args.LastOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    /// <summary>A flag's number, or null if it's missing or isn't one.</summary>
    private static float? FloatFlag(string[] args, string prefix) =>
        float.TryParse(Flag(args, prefix), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
            ? v : null;
}

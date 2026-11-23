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
    private const string BuggyPath = "res://content/base/cars/buggy.json";

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

        string[] args = OS.GetCmdlineUserArgs();

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
            Refuse($"Unknown oracle '{oracle}'. Known: straight, drop, brake, circle, handbrake, sweeper, respawn, jump, air, loop, bumps, lap, pieces, track.");
            return;
        }

        // The M1a oracles, handbrake, sweeper and respawn drive the bare plain on road grip, as at M1a, so their figures
        // stay comparable; pieces drives the showroom; track drives a track, the shakedown unless told; everything
        // else gets the test ground on sand.
        bool trackOracle = oracle is not null && Oracle.NeedsTrack(oracle);
        bool showroomOracle = oracle is not null && Oracle.NeedsShowroom(oracle);
        bool bareOracle = oracle is not null && !Oracle.NeedsTestGround(oracle) && !showroomOracle && !trackOracle;
        string? trackName = Flag(args, "--track=") ?? (trackOracle ? "base:shakedown" : null);
        string ground = trackName is not null ? "track" : Flag(args, "--ground=") ?? (showroomOracle ? "showroom" : bareOracle ? "bare" : "test");
        bool testGround = ground == "test";
        bool showroom = ground == "showroom";
        if (trackName is not null && Flag(args, "--ground=") is string other && other != "track")
        {
            Refuse($"--track={trackName} and --ground={other} are two places to drive; pick one.");
            return;
        }

        if (!testGround && !showroom && ground != "bare" && trackName is null)
        {
            Refuse($"Unknown ground '{ground}'. Known: test, showroom, bare, or a track with --track=.");
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
        else if (trackName is not null)
        {
            // A track from its file (M2-PLAN, M2c): its pieces on the plain, the car at the start gate.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _track = TrackView.LoadTrack(trackName, TrackView.LoadCatalogue());
            }
            catch (System.IO.InvalidDataException e)
            {
                Refuse(e.Message);
                return;
            }

            double loaded = clock.Elapsed.TotalMilliseconds;
            TrackView.Build(this, _track.Placed);
            float length = _track.LengthM;
            GD.Print($"Built {_track.Track.Id} ({_track.Track.Name}): {_track.Placed.Count} pieces, {length / 1000.0f:F2} km of road, "
                + $"loaded in {loaded:F0} ms and built in {clock.Elapsed.TotalMilliseconds - loaded:F0} ms");
        }

        if (_track is not null)
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
            _spawn = new Transform3D(Basis.Identity, new Vector3(0.0f, Buggy.RestHeight(spec) + lift, 0.0f));
        }

        _car = new Buggy { Transform = _spawn };
        _car.Configure(spec);
        AddChild(_car);

        _camera = new ChaseCamera { Target = _car };
        AddChild(_camera);

        _recorder = new RunRecorder { Car = _car };
        AddChild(_recorder);

        _hud = new Hud { Car = _car, Recorder = _recorder, OnATrack = _track is not null };
        AddChild(_hud);
        _telemetry = new TelemetryOverlay { Car = _car, Visible = panels.Contains("telemetry") };
        _hud.AddChild(_telemetry);
        _tuning = new TuningPanel { Car = _car, SpecPath = specPath, Hud = _hud, Visible = panels.Contains("tuning") };
        _hud.AddChild(_tuning);

        // The auto-respawn (M1d, decision 4), for the game and the oracle that tests it; the other oracles
        // report a crash as a crash.
        if (oracle is null || oracle == Oracle.Respawn)
        {
            _autoRespawn = new AutoRespawn { Car = _car, Home = () => _track is not null ? TrackHome() : _spawn };
            _autoRespawn.Respawned += AutoRespawned;
            AddChild(_autoRespawn);
        }

        if (oracle is not null)
        {
            _oracle = true;
            AddChild(new Oracle { Kind = oracle, Car = _car, Layout = _layout, Showroom = _showroom, Course = _track, Caution = _caution });
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
        if (_track is not null && oracle is null && _screenshot is null)
        {
            float km = _track.LengthM / 1000.0f;
            _hud.Flash($"{_track.Track.Name}: {km:F1} km and {_race!.Checkpoints} checkpoints. The clock starts on the throttle; "
                + "R puts you back at the last checkpoint, Backspace starts again.", 10.0);
        }
        else if (_stations is not null && oracle is null && _screenshot is null)
        {
            Station first = _stations[station - 1];
            _hud.Flash($"{first.Number}. {first.Name}: {first.Hint}", 8.0);
        }
    }

    /// <summary>Where a car starts on a track: on the start gate's road, a few metres short of its arch.</summary>
    private static Frame TrackStart(LaidTrack track) => track.Route[0].Road().Pieces[0].FrameAt(4.0f);

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

        double now = _ticks / (double)Engine.PhysicsTicksPerSecond;
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

        double now = _ticks / (double)Engine.PhysicsTicksPerSecond;
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
        if (_car is null || _oracle)
        {
            return;
        }

        _ticks++;
        if (_screenshot is not null)
        {
            bool driving = _ticks > 2 * Engine.PhysicsTicksPerSecond;
            _car.Driver = !driving ? default : _autodriver is not null ? AutodriverInput(_autodriver) : _screenshotDrive;
            TimeLap();
            TimeRace();
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

        if (_hud is not null)
        {
            _hud.Laps = _race is not null ? RaceBoard() : LapBoard();

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
        int tps = Engine.PhysicsTicksPerSecond;
        string stem = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

        if (_ticks >= (tps * 3) / 2 && !FileAccess.FileExists(stem + "-rest.png"))
        {
            GetViewport().GetTexture().GetImage().SavePng(stem + "-rest.png");
        }

        if (_ticks >= tps * 5)
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
}
